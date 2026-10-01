using System.Collections.Immutable;
using Mote.Engine;

namespace Mote.Native.Viewport;

/// <summary>One immutable source version and its currently visible bounded rows.</summary>
internal sealed record CanvasFrame(
    long Version,
    ViewportAnchor TopAnchor,
    double ScrollY,
    IReadOnlyList<ViewportSlice> Slices,
    int SelectionAnchor,
    int SelectionActive)
{
    /// <summary>Source-backed horizontal position used to resolve this frame.</summary>
    internal HorizontalAnchor Horizontal { get; init; }

    /// <summary>
    /// Immutable row windows from the same resolution pass as <see cref="Slices"/>.
    /// Legacy manually constructed frames may leave this empty until adapted.
    /// </summary>
    internal ImmutableArray<HorizontalRowWindow> RowWindows { get; init; } =
        ImmutableArray<HorizontalRowWindow>.Empty;

    /// <summary>First selected UTF-16 source boundary.</summary>
    internal int SelectionStart => Math.Min(SelectionAnchor, SelectionActive);

    /// <summary>Number of selected UTF-16 source units, independent of the viewport.</summary>
    internal int SelectionLength => Math.Abs(SelectionActive - SelectionAnchor);
}

/// <summary>
/// Shared scroll and selection state for a source-backed native canvas. The
/// engine snapshot is canonical; OS views shape only <see cref="CanvasFrame.Slices"/>
/// and translate their own hit-test results back into source offsets. Text
/// input remains the controller's versioned engine transaction, not view state.
/// </summary>
/// <remarks>
/// This class does not perform glyph shaping or infer grapheme boundaries from
/// code units. A platform hit-tester must choose a valid cluster edge before
/// calling the pointer methods. Surrogate/CRLF seams are rejected defensively.
/// </remarks>
internal sealed class CanvasInteraction
{
    private const int MaxVisibleSlices = 512;
    private readonly NativeNavigationModel _selection;
    private readonly ContinuousViewport _viewport;
    private double _viewportHeight;
    private bool _dragging;

    /// <summary>
    /// Creates a source-backed canvas with no per-document line objects. A zero
    /// body height is valid and produces no visible rows until layout expands.
    /// </summary>
    internal CanvasInteraction(TextSnapshot snapshot, double lineHeight,
        double viewportHeight, int maxSliceLength = 4096,
        NativeNavigationModel? selection = null)
    {
        if (!double.IsFinite(viewportHeight) || viewportHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        _viewport = new ContinuousViewport(snapshot, lineHeight, maxSliceLength);
        _selection = selection ?? new NativeNavigationModel();
        _viewportHeight = viewportHeight;
    }

    /// <summary>The immutable source version bound to this canvas state.</summary>
    internal TextSnapshot Snapshot => _viewport.Snapshot;

    /// <summary>Global source-coordinate selection shared with the existing editor.</summary>
    internal NativeNavigationModel Selection => _selection;

    /// <summary>Current scroll anchor; wheel and drag never trigger page replacement.</summary>
    internal ViewportAnchor TopAnchor => _viewport.TopAnchor;

    /// <summary>Source-bound left edge that survives vertical scrolling and resizing.</summary>
    internal HorizontalAnchor HorizontalAnchor => _viewport.HorizontalAnchor;

    /// <summary>
    /// Accepts a platform-resolved horizontal caret edge and local pixel residual.
    /// The platform is responsible for grapheme-safe hit-testing and normalization.
    /// </summary>
    internal void SetHorizontalAnchor(int sourceBoundary,
        HorizontalCaretAffinity affinity = HorizontalCaretAffinity.Leading,
        double intraClusterPixels = 0, double? measuredXFromStart = null) =>
        _viewport.SetHorizontalAnchor(sourceBoundary, affinity, intraClusterPixels, measuredXFromStart);

    /// <summary>
    /// Reanchors after a current-version OS geometry probe reports an offscreen
    /// caret. Only the view edge is snapped around delimiters/surrogates; the
    /// requested source offset and global selection are never rewritten.
    /// </summary>
    internal void AnchorHorizontalAtSource(int sourceOffset) =>
        _viewport.SetHorizontalAnchor(_viewport.ClampToLineContentBoundary(sourceOffset));

    /// <summary>Visible area height in device-independent pixels.</summary>
    internal double ViewportHeight => _viewportHeight;

    /// <summary>
    /// Updates only visible geometry on a native view resize. Zero height means
    /// no row is painted or exposed as visible; source/selection state remains.
    /// </summary>
    internal void Resize(double viewportHeight)
    {
        if (!double.IsFinite(viewportHeight) || viewportHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        _viewportHeight = viewportHeight;
    }

    /// <summary>
    /// Moves through adjacent logical rows without changing source version or
    /// selection or the source-bound horizontal anchor. The optional flag remains
    /// for existing callers; horizontal focus is now always persistent.
    /// </summary>
    internal void ScrollBy(double pixels, bool preserveSourceFocus = false)
    {
        _viewport.ScrollBy(pixels);
    }

    /// <summary>
    /// Moves the vertical source anchor and conditionally shifts the horizontal
    /// source window. Membership in a bounded source slice does not prove that
    /// the caret is inside the physical pixel viewport, particularly for 16 Ki
    /// rows, variable-width glyphs, or bidirectional text. The shell must inspect
    /// current-version shaped caret geometry before claiming a completed reveal.
    /// </summary>
    internal HorizontalRevealStatus Reveal(int sourceOffset)
    {
        _viewport.ScrollToSource(sourceOffset);
        var line = Snapshot.GetLineIndexFromOffset(sourceOffset);
        foreach (var row in _viewport.GetHorizontalRows(_viewportHeight, MaxVisibleSlices))
        {
            var slice = row.Slice;
            var sliceEnd = slice.SourceStart + slice.SourceLength;
            if (slice.Line == line && sourceOffset >= slice.SourceStart &&
                (sourceOffset < sliceEnd || sourceOffset == sliceEnd && !slice.HasHiddenSuffix))
                return HorizontalRevealStatus.NeedsPixelVisibilityProof;
        }
        AnchorHorizontalAtSource(sourceOffset);
        return HorizontalRevealStatus.SourceWindowMoved;
    }

    /// <summary>
    /// Rebinds the viewport after a canonical engine edit. The controller owns
    /// transforming a shared selection exactly once via the document notification.
    /// The canvas consumes only replacement extents, never a copied insert string.
    /// </summary>
    internal void ApplyEdit(TextSnapshot after, TextChangeRange change)
    {
        _viewport.ApplyEdit(after, change);
    }

    /// <summary>Compatibility adapter for callers that still hold an inserted-text change.</summary>
    internal void ApplyEdit(TextSnapshot after, TextChange change)
    {
        if (change.InsertText is null) throw new ArgumentException("Insert text must not be null.", nameof(change));
        ApplyEdit(after, new TextChangeRange(change.Start, change.DeleteLength, change.InsertText.Length));
    }

    /// <summary>Starts an OS pointer selection at a platform-resolved source cluster edge.</summary>
    internal void BeginSelection(int sourceOffset)
    {
        ValidatePointerBoundary(sourceOffset);
        _selection.MoveCaret(Snapshot, sourceOffset);
        _dragging = true;
    }

    /// <summary>Extends the same global selection while a pointer drag is active.</summary>
    internal void ExtendSelection(int sourceOffset)
    {
        if (!_dragging) return;
        ValidatePointerBoundary(sourceOffset);
        _selection.SetSelection(Snapshot, _selection.Anchor, sourceOffset);
    }

    /// <summary>Ends pointer capture without collapsing the global selection.</summary>
    internal void EndSelection() => _dragging = false;

    /// <summary>
    /// Publishes one immutable row resolution. The compatibility Slices collection
    /// is projected from these exact rows, never independently re-queried.
    /// </summary>
    internal CanvasFrame Frame()
    {
        var rows = _viewport.GetHorizontalRows(_viewportHeight, MaxVisibleSlices);
        var slices = ImmutableArray.CreateBuilder<ViewportSlice>(rows.Length);
        foreach (var row in rows) slices.Add(row.Slice);
        return new CanvasFrame(Snapshot.Version, _viewport.TopAnchor, _viewport.ScrollY,
            slices.MoveToImmutable(), _selection.Anchor, _selection.Active)
        {
            Horizontal = _viewport.HorizontalAnchor,
            RowWindows = rows,
        };
    }

    /// <summary>Streams the selected original source without a second text model.</summary>
    internal void WriteSelection(TextWriter writer) => _selection.WriteSelection(Snapshot, writer);

    private void ValidatePointerBoundary(int offset)
    {
        if ((uint)offset > (uint)Snapshot.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (offset == 0 || offset == Snapshot.Length) return;
        var pair = Snapshot.GetText(offset - 1, 2);
        if (char.IsHighSurrogate(pair[0]) && char.IsLowSurrogate(pair[1]) ||
            pair[0] == '\r' && pair[1] == '\n')
            throw new ArgumentException("Pointer hit-test must return a visible cluster boundary.", nameof(offset));
    }
}
