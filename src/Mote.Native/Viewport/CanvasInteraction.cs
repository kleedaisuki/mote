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
    /// <summary>First selected UTF-16 source boundary.</summary>
    internal int SelectionStart => Math.Min(SelectionAnchor, SelectionActive);

    /// <summary>Number of selected UTF-16 source units, independent of the viewport.</summary>
    internal int SelectionLength => Math.Abs(SelectionActive - SelectionAnchor);
}

/// <summary>
/// Shared scroll and selection state for a read-only native canvas. The engine
/// snapshot is canonical; OS views shape only <see cref="CanvasFrame.Slices"/>
/// and translate their own hit-test results back into source offsets.
/// </summary>
/// <remarks>
/// This class does not perform glyph shaping or infer grapheme boundaries from
/// code units. A platform hit-tester must choose a valid cluster edge before
/// calling the pointer methods. Surrogate/CRLF seams are rejected defensively.
/// </remarks>
internal sealed class CanvasInteraction
{
    private const int MaxVisibleSlices = 512;
    private readonly NativeNavigationModel _selection = new();
    private readonly ContinuousViewport _viewport;
    private double _viewportHeight;
    private bool _dragging;

    /// <summary>Creates a source-backed canvas with no per-document line objects.</summary>
    internal CanvasInteraction(TextSnapshot snapshot, double lineHeight,
        double viewportHeight, int maxSliceLength = 4096)
    {
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        _viewport = new ContinuousViewport(snapshot, lineHeight, maxSliceLength);
        _viewportHeight = viewportHeight;
    }

    /// <summary>The immutable source version bound to this canvas state.</summary>
    internal TextSnapshot Snapshot => _viewport.Snapshot;

    /// <summary>Global source-coordinate selection shared with the existing editor.</summary>
    internal NativeNavigationModel Selection => _selection;

    /// <summary>Current scroll anchor; wheel and drag never trigger page replacement.</summary>
    internal ViewportAnchor TopAnchor => _viewport.TopAnchor;

    /// <summary>Visible area height in device-independent pixels.</summary>
    internal double ViewportHeight => _viewportHeight;

    /// <summary>Updates only visible geometry on a native view resize.</summary>
    internal void Resize(double viewportHeight)
    {
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        _viewportHeight = viewportHeight;
    }

    /// <summary>Moves through adjacent logical rows without changing source version or selection.</summary>
    internal void ScrollBy(double pixels) => _viewport.ScrollBy(pixels);

    /// <summary>Reveals a source boundary, including one far beyond the former native page.</summary>
    internal void Reveal(int sourceOffset) => _viewport.ScrollToSource(sourceOffset);

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

    /// <summary>Returns only bounded source intervals near the current scroll anchor.</summary>
    internal CanvasFrame Frame() => new(Snapshot.Version, _viewport.TopAnchor,
        _viewport.ScrollY,
        _viewport.GetVisibleSlices(_viewportHeight, MaxVisibleSlices,
            focusSourceOffset: _viewport.TopAnchor.SourceOffset),
        _selection.Anchor, _selection.Active);

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
