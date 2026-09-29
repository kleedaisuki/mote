using Mote.Engine;

namespace Mote.Native.Viewport;

/// <summary>A stable source-backed vertical anchor in UTF-16 and device-independent pixels.</summary>
/// <param name="SourceOffset">A source boundary in the current snapshot; interior line offsets are retained.</param>
/// <param name="IntraRowY">Pixels below the top of the containing visual row.</param>
internal readonly record struct ViewportAnchor(int SourceOffset, double IntraRowY);

/// <summary>A bounded source interval to shape for one visible logical row.</summary>
/// <remarks>
/// The interval excludes CR/LF delimiters. A long line is intentionally represented
/// by one focused window, not a promise that the whole row has been shaped. Callers
/// must obtain further windows before claiming exact remote horizontal geometry.
/// </remarks>
internal readonly record struct ViewportSlice(
    int Line, int SourceStart, int SourceLength, double TopY, double Height,
    bool HasHiddenPrefix, bool HasHiddenSuffix);

/// <summary>
/// Source-backed continuous viewport model. It owns no text and never allocates a
/// per-document line table; geometry is implicit except for measured exceptions.
/// Platform shapers own glyph width, wrapping, hit-testing, and grapheme semantics.
/// </summary>
internal sealed class ContinuousViewport
{
    private readonly SparseHeightIndex _heights = new();
    private readonly int _maxSliceLength;
    private TextSnapshot _snapshot;
    private double _baseHeight;
    private ViewportAnchor _anchor;

    /// <summary>Creates a no-wrap viewport with a bounded UTF-16 shaping request size.</summary>
    /// <example><code>var view = new ContinuousViewport(document.Snapshot, 18, 4096);
    /// foreach (var slice in view.GetVisibleSlices(800))
    ///     Shape(view.Snapshot.GetText(slice.SourceStart, slice.SourceLength));</code></example>
    internal ContinuousViewport(TextSnapshot snapshot, double lineHeight, int maxSliceLength = 4096)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        ValidateHeight(lineHeight, nameof(lineHeight));
        if (maxSliceLength < 2) throw new ArgumentOutOfRangeException(nameof(maxSliceLength));
        _baseHeight = lineHeight;
        _maxSliceLength = maxSliceLength;
        _anchor = new ViewportAnchor(0, 0);
    }

    /// <summary>Immutable snapshot against which all coordinates are interpreted.</summary>
    internal TextSnapshot Snapshot => _snapshot;

    /// <summary>Current source anchor and fractional row offset.</summary>
    internal ViewportAnchor TopAnchor => _anchor;

    /// <summary>Current horizontal pixel offset, interpreted by a platform shaper.</summary>
    internal double HorizontalOffset { get; private set; }

    /// <summary>Baseline height for an unmeasured logical row.</summary>
    internal double LineHeight => _baseHeight;

    /// <summary>Number of non-baseline line measurements currently retained.</summary>
    internal int SparseHeightCount => _heights.Count;

    /// <summary>Exact vertical extent under the measurements currently known.</summary>
    internal double DocumentHeight => _snapshot.LineCount * _baseHeight + _heights.TotalDelta;

    /// <summary>Absolute Y coordinate of the viewport top under current measurements.</summary>
    internal double ScrollY => GetDocumentY(_anchor.SourceOffset) + _anchor.IntraRowY;

    /// <summary>Changes only the horizontal viewport state; source content is not copied.</summary>
    internal void SetHorizontalOffset(double pixels)
    {
        if (!double.IsFinite(pixels) || pixels < 0) throw new ArgumentOutOfRangeException(nameof(pixels));
        HorizontalOffset = pixels;
    }

    /// <summary>Anchors a source boundary, preserving interior offsets including CRLF seams.</summary>
    internal void ScrollToSource(int sourceOffset, double intraRowY = 0)
    {
        if ((uint)sourceOffset > (uint)_snapshot.Length)
            throw new ArgumentOutOfRangeException(nameof(sourceOffset));
        if (!double.IsFinite(intraRowY) || intraRowY < 0)
            throw new ArgumentOutOfRangeException(nameof(intraRowY));
        var line = _snapshot.GetLineIndexFromOffset(sourceOffset);
        _anchor = new ViewportAnchor(sourceOffset, Math.Min(intraRowY, Math.BitDecrement(GetRowHeight(line))));
    }

    /// <summary>Scrolls in pixels through all logical lines without a page boundary.</summary>
    internal void ScrollBy(double pixels)
    {
        if (!double.IsFinite(pixels)) throw new ArgumentOutOfRangeException(nameof(pixels));
        if (pixels == 0) return;
        var lastY = Math.BitDecrement(DocumentHeight);
        var nextY = Math.Clamp(ScrollY + pixels, 0, lastY);
        var next = GetSourceAtDocumentY(nextY);
        if (_snapshot.GetLineIndexFromOffset(next.SourceOffset) ==
            _snapshot.GetLineIndexFromOffset(_anchor.SourceOffset))
            next = next with { SourceOffset = _anchor.SourceOffset };
        _anchor = next;
    }

    /// <summary>
    /// Updates the implicit row height after font/DPI changes. All measurements are
    /// invalidated, while the source anchor and fractional pixel are retained.
    /// </summary>
    internal void Reflow(double lineHeight)
    {
        ValidateHeight(lineHeight, nameof(lineHeight));
        _baseHeight = lineHeight;
        _heights.Clear();
        ScrollToSource(_anchor.SourceOffset, _anchor.IntraRowY);
    }

    /// <summary>
    /// Records a refined row height. The source anchor prevents an above-viewport
    /// measurement from moving the visible glyph even though global ScrollY changes.
    /// </summary>
    internal void SetMeasuredHeight(int line, double height)
    {
        if ((uint)line >= (uint)_snapshot.LineCount) throw new ArgumentOutOfRangeException(nameof(line));
        ValidateHeight(height, nameof(height));
        _heights.SetDelta(line, height - _baseHeight);
        ScrollToSource(_anchor.SourceOffset, _anchor.IntraRowY);
    }

    /// <summary>
    /// Rebinds after a committed source edit, transforming the anchor with right
    /// affinity. Measurements are invalidated because line identities may have moved.
    /// Only replacement extents are needed; the viewport never reads inserted text.
    /// </summary>
    internal void ApplyEdit(TextSnapshot after, TextChangeRange change)
    {
        ArgumentNullException.ThrowIfNull(after);
        if (change.Start < 0 || change.DeleteLength < 0 || change.InsertLength < 0 ||
            change.Start > _snapshot.Length - change.DeleteLength)
            throw new ArgumentOutOfRangeException(nameof(change));
        var expectedLength = (long)_snapshot.Length - change.DeleteLength + change.InsertLength;
        if (after.Length != expectedLength)
            throw new ArgumentException("Snapshot length does not match the committed change.", nameof(after));

        var oldOffset = _anchor.SourceOffset;
        var end = change.Start + change.DeleteLength;
        var newOffset = oldOffset < change.Start ? oldOffset :
            oldOffset <= end ? change.Start + change.InsertLength :
            oldOffset + change.InsertLength - change.DeleteLength;
        var intraRowY = _anchor.IntraRowY;
        _snapshot = after;
        _heights.Clear();
        ScrollToSource(Math.Clamp(newOffset, 0, after.Length), intraRowY);
    }

    /// <summary>Compatibility adapter for callers that still hold an inserted-text change.</summary>
    internal void ApplyEdit(TextSnapshot after, TextChange change)
    {
        if (change.InsertText is null) throw new ArgumentException("Insert text must not be null.", nameof(change));
        ApplyEdit(after, new TextChangeRange(change.Start, change.DeleteLength, change.InsertText.Length));
    }

    /// <summary>Returns the document Y coordinate at the top of the containing row.</summary>
    internal double GetDocumentY(int sourceOffset)
    {
        if ((uint)sourceOffset > (uint)_snapshot.Length)
            throw new ArgumentOutOfRangeException(nameof(sourceOffset));
        var line = _snapshot.GetLineIndexFromOffset(sourceOffset);
        return line * _baseHeight + _heights.PrefixDelta(line);
    }

    /// <summary>
    /// Converts document Y to the source start of its row and an intra-row pixel.
    /// Horizontal/source-column resolution belongs to the platform shaper.
    /// </summary>
    internal ViewportAnchor GetSourceAtDocumentY(double y)
    {
        if (!double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(y));
        var clamped = Math.Clamp(y, 0, Math.BitDecrement(DocumentHeight));
        var (line, intra) = _heights.Locate(clamped, _snapshot.LineCount, _baseHeight);
        return new ViewportAnchor(_snapshot.GetLineStartOffset(line), intra);
    }

    /// <summary>
    /// Produces at most <paramref name="maxSlices"/> visible, bounded UTF-16 intervals.
    /// A focus offset chooses the window within an exceptionally long visible line.
    /// No call reads a complete logical line or materializes text.
    /// </summary>
    internal IReadOnlyList<ViewportSlice> GetVisibleSlices(
        double viewportHeight, int maxSlices = 512, int? focusSourceOffset = null)
    {
        if (!double.IsFinite(viewportHeight) || viewportHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        if (maxSlices < 0) throw new ArgumentOutOfRangeException(nameof(maxSlices));
        if (focusSourceOffset is { } focus && (uint)focus > (uint)_snapshot.Length)
            throw new ArgumentOutOfRangeException(nameof(focusSourceOffset));
        var result = new List<ViewportSlice>(Math.Min(maxSlices, 64));
        var line = _snapshot.GetLineIndexFromOffset(_anchor.SourceOffset);
        var firstLine = line;
        var y = -_anchor.IntraRowY;
        while (line < _snapshot.LineCount && y < viewportHeight && result.Count < maxSlices)
        {
            var start = _snapshot.GetLineStartOffset(line);
            var end = GetLineContentEnd(line, start);
            var sourceFocus = focusSourceOffset is { } offset && offset >= start && offset <= end
                ? offset : line == firstLine
                    ? _anchor.SourceOffset : start;
            var sliceStart = end - start <= _maxSliceLength ? start :
                Math.Clamp((long)sourceFocus - _maxSliceLength / 2, start, (long)end - _maxSliceLength);
            var boundedStart = (int)sliceStart;
            boundedStart = SnapStart(boundedStart, start);
            var sliceEnd = Math.Min(end, boundedStart + _maxSliceLength);
            sliceEnd = SnapEnd(sliceEnd, boundedStart, end);
            var height = GetRowHeight(line);
            result.Add(new ViewportSlice(line, boundedStart, sliceEnd - boundedStart, y, height,
                boundedStart > start, sliceEnd < end));
            y += height;
            line++;
        }

        return result;
    }

    private int GetLineContentEnd(int line, int start)
    {
        if (line == _snapshot.LineCount - 1) return _snapshot.Length;
        var end = _snapshot.GetLineStartOffset(line + 1);
        var delimiter = _snapshot.GetText(Math.Max(start, end - 2), Math.Min(2, end - start));
        if (delimiter.EndsWith("\r\n", StringComparison.Ordinal)) return end - 2;
        return end - 1;
    }

    private int SnapStart(int offset, int lineStart)
    {
        if (offset <= lineStart || offset >= _snapshot.Length) return offset;
        var pair = _snapshot.GetText(offset - 1, 2);
        return char.IsHighSurrogate(pair[0]) && char.IsLowSurrogate(pair[1]) ? offset - 1 : offset;
    }

    private int SnapEnd(int offset, int start, int lineEnd)
    {
        if (offset >= lineEnd || offset <= start) return offset;
        var pair = _snapshot.GetText(offset - 1, 2);
        if (!char.IsHighSurrogate(pair[0]) || !char.IsLowSurrogate(pair[1])) return offset;
        return offset - 1 > start ? offset - 1 : Math.Min(lineEnd, offset + 1);
    }

    private double GetRowHeight(int line) => _baseHeight + _heights.GetDelta(line);

    private static void ValidateHeight(double height, string name)
    {
        if (!double.IsFinite(height) || height <= 0) throw new ArgumentOutOfRangeException(name);
    }
}
