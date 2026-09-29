using Mote.Engine;
using Mote.Native.Viewport;

namespace Mote.Native.Accessibility;

/// <summary>A half-open, absolute UTF-16 interval in one immutable source version.</summary>
internal readonly record struct AccessibleRange(long Generation, long Version, int Start, int End)
{
    /// <summary>The number of source code units in the interval.</summary>
    internal int Length => End - Start;
}

/// <summary>
/// One atomic accessibility publication, independent of the caret-local input
/// host. Selection is carried by the source-versioned canvas frame.
/// </summary>
internal sealed record AccessibleCanvasState(
    long Generation, TextSnapshot Snapshot, CanvasFrame Frame)
{
    /// <summary>The immutable engine source version exposed by this state.</summary>
    internal long Version => Snapshot.Version;
}

/// <summary>
/// Engine-backed state for one logical accessible editor. Native input islands never
/// supply text here; this model reads only bounded intervals from the snapshot.
/// </summary>
/// <remarks>
/// Publish the state atomically on the UI thread after a committed edit. A range
/// from an older generation or version is rejected rather than describing a new file
/// with stale source coordinates. Platform providers must marshal requests to the UI
/// thread and must not report an oversized request as a successful truncated result.
/// </remarks>
internal sealed class AccessibleDocument
{
    /// <summary>Maximum source code units materialized by one text request.</summary>
    internal const int MaxTextRequest = 65_536;

    private readonly object _gate = new();
    private AccessibleCanvasState? _state;
    private int _closed;

    /// <summary>Creates an accessible source view from one immutable canvas state.</summary>
    internal AccessibleDocument(AccessibleCanvasState state)
    {
        _state = Validate(state);
    }

    /// <summary>The bound document generation, used to reject stale clients.</summary>
    internal long Generation => ReadState().Generation;

    /// <summary>The immutable source version currently presented to clients.</summary>
    internal long Version => ReadState().Version;

    /// <summary>The full document interval, including all offscreen text.</summary>
    internal AccessibleRange DocumentRange
    {
        get
        {
            var state = ReadState();
            return Range(state, 0, state.Snapshot.Length);
        }
    }

    /// <summary>The global selection, or a zero-width insertion-point range.</summary>
    internal AccessibleRange Selection
    {
        get
        {
            var state = ReadState();
            return Range(state, Math.Min(state.Frame.SelectionAnchor, state.Frame.SelectionActive),
                Math.Max(state.Frame.SelectionAnchor, state.Frame.SelectionActive));
        }
    }

    /// <summary>True when selection direction runs from higher to lower source offset.</summary>
    internal bool IsReverseSelection
    {
        get
        {
            var state = ReadState();
            return state.Frame.SelectionActive < state.Frame.SelectionAnchor;
        }
    }

    /// <summary>Whether the current frame contains any painted source slices.</summary>
    internal bool HasVisibleSlices => ReadState().Frame.Slices.Count != 0;

    /// <summary>
    /// Permanently invalidates ranges when the native editor closes. A retained
    /// COM/AX client cannot read the closed document through an old provider.
    /// </summary>
    internal void Invalidate()
    {
        lock (_gate)
        {
            Volatile.Write(ref _closed, 1);
            // Retained COM/AX range objects keep the model, not a closed 100 MiB rope.
            Volatile.Write(ref _state, null);
        }
    }

    /// <summary>
    /// Publishes source, selection, and viewport as one transition. The native
    /// input island may be absent or unable to bind; AX source truth is unaffected.
    /// </summary>
    internal void Publish(AccessibleCanvasState state)
    {
        state = Validate(state);
        lock (_gate)
        {
            EnsureOpen();
            var current = _state ?? throw new InvalidOperationException("Accessibility editor is closed.");
            if (state.Generation < current.Generation ||
                state.Generation == current.Generation && state.Version < current.Version)
                throw new InvalidOperationException("Accessibility state would regress document identity or version.");
            Volatile.Write(ref _state, state);
        }
    }

    /// <summary>Creates a checked source interval in the current document.</summary>
    internal AccessibleRange MakeRange(int start, int end) =>
        Range(ReadState(), start, end);

    /// <summary>
    /// Reads an offscreen or onscreen source interval without a full-document mirror.
    /// UIA maxLength = -1 requests the entire range; oversized requests fail rather
    /// than silently returning a prefix as if it were complete.
    /// </summary>
    /// <exception cref="AccessibleRequestTooLargeException">The requested output exceeds the per-call budget.</exception>
    internal string GetText(AccessibleRange range, int maxLength = -1)
    {
        var state = ReadState();
        ValidateRange(state, range);
        if (maxLength < -1) throw new ArgumentOutOfRangeException(nameof(maxLength));
        var length = maxLength == -1 ? range.Length : Math.Min(range.Length, maxLength);
        if (length > 0 && length < range.Length)
        {
            var boundary = range.Start + length;
            var seam = state.Snapshot.GetText(boundary - 1, 2);
            if (char.IsHighSurrogate(seam[0]) && char.IsLowSurrogate(seam[1]) ||
                seam[0] == '\r' && seam[1] == '\n') length--;
        }
        if (length > MaxTextRequest)
            throw new AccessibleRequestTooLargeException(length, MaxTextRequest);
        return state.Snapshot.GetText(range.Start, length);
    }

    /// <summary>
    /// Returns contiguous visible source spans. Adjacent full lines include their
    /// source delimiter; clipped long-line windows stay disjoint. Empty visibility
    /// is represented by one degenerate range, as UIA requires.
    /// </summary>
    internal IReadOnlyList<AccessibleRange> VisibleRanges()
    {
        var state = ReadState();
        var slices = state.Frame.Slices;
        if (slices.Count == 0)
        {
            var offset = Math.Clamp(state.Frame.TopAnchor.SourceOffset, 0, state.Snapshot.Length);
            return [Range(state, offset, offset)];
        }
        var ranges = new List<AccessibleRange>(slices.Count);
        ViewportSlice? previous = null;
        foreach (var slice in slices)
        {
            var next = Range(state, slice.SourceStart, checked(slice.SourceStart + slice.SourceLength));
            var contiguous = previous is { } before && ranges.Count > 0 &&
                (next.Start == ranges[^1].End ||
                 !before.HasHiddenSuffix && !slice.HasHiddenPrefix &&
                 slice.Line == before.Line + 1 &&
                 IsVisibleDelimiter(state.Snapshot, ranges[^1].End, next.Start));
            if (contiguous)
                ranges[^1] = ranges[^1] with { End = next.End };
            else ranges.Add(next);
            previous = slice;
        }
        return ranges;
    }

    /// <summary>Gets a whole logical line, including its original delimiter if present.</summary>
    internal AccessibleRange LineRange(int line)
    {
        var state = ReadState();
        var snapshot = state.Snapshot;
        var start = snapshot.GetLineStartOffset(line);
        var end = line + 1 < snapshot.LineCount
            ? snapshot.GetLineStartOffset(line + 1) : snapshot.Length;
        return Range(state, start, end);
    }

    /// <summary>Gets the logical line containing a source boundary.</summary>
    internal int LineFromOffset(int offset) =>
        ReadState().Snapshot.GetLineIndexFromOffset(offset);

    /// <summary>Gets the whole-line envelope of painted slices in one atomic source view.</summary>
    internal AccessibleRange VisibleLogicalLineRange()
    {
        var state = ReadState();
        var slices = state.Frame.Slices;
        if (slices.Count == 0)
        {
            var offset = Math.Clamp(state.Frame.TopAnchor.SourceOffset, 0, state.Snapshot.Length);
            return Range(state, offset, offset);
        }
        var snapshot = state.Snapshot;
        var first = snapshot.GetLineIndexFromOffset(slices[0].SourceStart);
        var tail = slices[^1];
        var last = snapshot.GetLineIndexFromOffset(Math.Max(tail.SourceStart,
            tail.SourceStart + tail.SourceLength - 1));
        var end = last + 1 < snapshot.LineCount
            ? snapshot.GetLineStartOffset(last + 1) : snapshot.Length;
        return Range(state, snapshot.GetLineStartOffset(first), end);
    }

    /// <summary>Checks that a client range still belongs to the currently bound source.</summary>
    internal void ValidateRange(AccessibleRange range) =>
        ValidateRange(ReadState(), range);

    private AccessibleCanvasState ReadState()
    {
        EnsureOpen();
        var state = Volatile.Read(ref _state)
            ?? throw new InvalidOperationException("Accessibility editor is closed.");
        EnsureOpen();
        return state;
    }

    private void EnsureOpen()
    {
        if (Volatile.Read(ref _closed) != 0)
            throw new InvalidOperationException("Accessibility editor is closed.");
    }

    private static void ValidateRange(AccessibleCanvasState state, AccessibleRange range)
    {
        if (range.Generation != state.Generation || range.Version != state.Version)
            throw new InvalidOperationException("Accessibility range belongs to a stale document version.");
        if (range.Start < 0 || range.End < range.Start || range.End > state.Snapshot.Length)
            throw new ArgumentOutOfRangeException(nameof(range));
    }

    private static AccessibleRange Range(AccessibleCanvasState state, int start, int end)
    {
        if (start < 0 || end < start || end > state.Snapshot.Length)
            throw new ArgumentOutOfRangeException(nameof(end));
        return new AccessibleRange(state.Generation, state.Version, start, end);
    }

    private static AccessibleCanvasState Validate(AccessibleCanvasState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.Snapshot);
        ArgumentNullException.ThrowIfNull(state.Frame);
        if (state.Generation < 0 || state.Frame.Version != state.Version)
            throw new ArgumentException("Generation and frame must match the source state.", nameof(state));
        ValidateFrame(state.Frame, state.Snapshot);
        return state with { Frame = state.Frame with
            { Slices = Array.AsReadOnly(state.Frame.Slices.ToArray()) } };
    }

    private static void ValidateFrame(CanvasFrame frame, TextSnapshot snapshot)
    {
        var sourceLength = snapshot.Length;
        if ((uint)frame.TopAnchor.SourceOffset > (uint)sourceLength)
            throw new ArgumentException("Viewport anchor exceeds source length.", nameof(frame));
        if ((uint)frame.SelectionAnchor > (uint)sourceLength ||
            (uint)frame.SelectionActive > (uint)sourceLength)
            throw new ArgumentException("Frame selection exceeds source length.", nameof(frame));
        ArgumentNullException.ThrowIfNull(frame.Slices);
        var priorLine = -1;
        var priorEnd = -1;
        foreach (var slice in frame.Slices)
        {
            if (slice.SourceStart < 0 || slice.SourceLength < 0 ||
                slice.SourceStart > sourceLength - slice.SourceLength)
                throw new ArgumentException("Visible slice exceeds source length.", nameof(frame));
            if (slice.Line < priorLine || slice.SourceStart < priorEnd ||
                slice.Line != snapshot.GetLineIndexFromOffset(slice.SourceStart))
                throw new ArgumentException("Visible slices must follow source line order.", nameof(frame));
            priorLine = slice.Line;
            priorEnd = slice.SourceStart + slice.SourceLength;
        }
    }

    private static bool IsVisibleDelimiter(TextSnapshot snapshot, int start, int end)
    {
        var length = end - start;
        if (length is not (1 or 2)) return false;
        var text = snapshot.GetText(start, length);
        return text is "\r" or "\n" or "\r\n";
    }
}

/// <summary>Signals an accessibility text request that cannot be served within one bounded call.</summary>
internal sealed class AccessibleRequestTooLargeException(int requested, int limit)
    : Exception($"Accessibility text request of {requested} UTF-16 units exceeds {limit} per call.")
{
    /// <summary>Requested UTF-16 code units.</summary>
    internal int Requested { get; } = requested;
    /// <summary>Maximum code units served by one call.</summary>
    internal int Limit { get; } = limit;
}
