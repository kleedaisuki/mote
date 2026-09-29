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
/// Engine-backed state for one logical accessible editor. Native input islands never
/// supply text here; this model reads only bounded intervals from the snapshot.
/// </summary>
/// <remarks>
/// Replace the binding atomically on the UI thread after a committed edit. A range
/// from an older generation or version is rejected rather than describing a new file
/// with stale source coordinates. Platform providers must marshal requests to the UI
/// thread and must not report an oversized request as a successful truncated result.
/// </remarks>
internal sealed class AccessibleDocument
{
    /// <summary>Maximum source code units materialized by one text request.</summary>
    internal const int MaxTextRequest = 65_536;

    private readonly object _gate = new();
    private NativeCanvasBinding? _binding;
    private int _closed;

    /// <summary>Creates an accessible source view from a single canvas binding.</summary>
    internal AccessibleDocument(NativeCanvasBinding binding)
    {
        _binding = Validate(binding);
    }

    /// <summary>The bound document generation, used to reject stale clients.</summary>
    internal long Generation => ReadBinding().DocumentGeneration;

    /// <summary>The immutable source version currently presented to clients.</summary>
    internal long Version => ReadBinding().Snapshot.Version;

    /// <summary>The full document interval, including all offscreen text.</summary>
    internal AccessibleRange DocumentRange
    {
        get
        {
            var binding = ReadBinding();
            return Range(binding, 0, binding.Snapshot.Length);
        }
    }

    /// <summary>The global selection, or a zero-width insertion-point range.</summary>
    internal AccessibleRange Selection
    {
        get
        {
            var binding = ReadBinding();
            return Range(binding, Math.Min(binding.Anchor, binding.Active),
                Math.Max(binding.Anchor, binding.Active));
        }
    }

    /// <summary>True when selection direction runs from higher to lower source offset.</summary>
    internal bool IsReverseSelection
    {
        get
        {
            var binding = ReadBinding();
            return binding.Active < binding.Anchor;
        }
    }

    /// <summary>Whether the current frame contains any painted source slices.</summary>
    internal bool HasVisibleSlices => ReadBinding().Frame.Slices.Count != 0;

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
            Volatile.Write(ref _binding, null);
        }
    }

    /// <summary>
    /// Publishes the next immutable snapshot/selection/frame as one state transition.
    /// The frame must belong to the same version as the snapshot.
    /// </summary>
    internal void Rebind(NativeCanvasBinding binding)
    {
        binding = Validate(binding);
        lock (_gate)
        {
            EnsureOpen();
            Volatile.Write(ref _binding, binding);
        }
    }

    /// <summary>Updates visible geometry without rebinding input text or source bytes.</summary>
    internal void SetFrame(long generation, CanvasFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_gate)
        {
            EnsureOpen();
            var binding = _binding ?? throw new InvalidOperationException("Accessibility editor is closed.");
            if (generation != binding.DocumentGeneration || frame.Version != binding.Snapshot.Version)
                throw new InvalidOperationException("Accessibility frame belongs to a stale document generation or version.");
            ValidateFrame(frame, binding.Snapshot.Length);
            Volatile.Write(ref _binding, binding with { Frame = frame,
                Anchor = frame.SelectionAnchor, Active = frame.SelectionActive });
        }
    }

    /// <summary>Creates a checked source interval in the current document.</summary>
    internal AccessibleRange MakeRange(int start, int end) =>
        Range(ReadBinding(), start, end);

    /// <summary>
    /// Reads an offscreen or onscreen source interval without a full-document mirror.
    /// UIA maxLength = -1 requests the entire range; oversized requests fail rather
    /// than silently returning a prefix as if it were complete.
    /// </summary>
    /// <exception cref="AccessibleRequestTooLargeException">The requested output exceeds the per-call budget.</exception>
    internal string GetText(AccessibleRange range, int maxLength = -1)
    {
        var binding = ReadBinding();
        ValidateRange(binding, range);
        if (maxLength < -1) throw new ArgumentOutOfRangeException(nameof(maxLength));
        var length = maxLength == -1 ? range.Length : Math.Min(range.Length, maxLength);
        if (length > 0 && length < range.Length)
        {
            var boundary = range.Start + length;
            var seam = binding.Snapshot.GetText(boundary - 1, 2);
            if (char.IsHighSurrogate(seam[0]) && char.IsLowSurrogate(seam[1]) ||
                seam[0] == '\r' && seam[1] == '\n') length--;
        }
        if (length > MaxTextRequest)
            throw new AccessibleRequestTooLargeException(length, MaxTextRequest);
        return binding.Snapshot.GetText(range.Start, length);
    }

    /// <summary>
    /// Returns contiguous visible source spans. Adjacent full lines include their
    /// source delimiter; clipped long-line windows stay disjoint. Empty visibility
    /// is represented by one degenerate range, as UIA requires.
    /// </summary>
    internal IReadOnlyList<AccessibleRange> VisibleRanges()
    {
        var binding = ReadBinding();
        var slices = binding.Frame.Slices;
        if (slices.Count == 0)
        {
            var offset = Math.Clamp(binding.Frame.TopAnchor.SourceOffset, 0, binding.Snapshot.Length);
            return [Range(binding, offset, offset)];
        }
        var ranges = new List<AccessibleRange>(slices.Count);
        ViewportSlice? previous = null;
        foreach (var slice in slices)
        {
            var next = Range(binding, slice.SourceStart, checked(slice.SourceStart + slice.SourceLength));
            var contiguous = previous is { } before && ranges.Count > 0 &&
                (next.Start == ranges[^1].End ||
                 !before.HasHiddenSuffix && !slice.HasHiddenPrefix &&
                 slice.Line == before.Line + 1 &&
                 next.Start - ranges[^1].End is 1 or 2);
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
        var binding = ReadBinding();
        var snapshot = binding.Snapshot;
        var start = snapshot.GetLineStartOffset(line);
        var end = line + 1 < snapshot.LineCount
            ? snapshot.GetLineStartOffset(line + 1) : snapshot.Length;
        return Range(binding, start, end);
    }

    /// <summary>Gets the logical line containing a source boundary.</summary>
    internal int LineFromOffset(int offset) =>
        ReadBinding().Snapshot.GetLineIndexFromOffset(offset);

    /// <summary>Gets the whole-line envelope of painted slices in one atomic source view.</summary>
    internal AccessibleRange VisibleLogicalLineRange()
    {
        var binding = ReadBinding();
        var slices = binding.Frame.Slices;
        if (slices.Count == 0)
        {
            var offset = Math.Clamp(binding.Frame.TopAnchor.SourceOffset, 0, binding.Snapshot.Length);
            return Range(binding, offset, offset);
        }
        var snapshot = binding.Snapshot;
        var first = snapshot.GetLineIndexFromOffset(slices[0].SourceStart);
        var tail = slices[^1];
        var last = snapshot.GetLineIndexFromOffset(Math.Max(tail.SourceStart,
            tail.SourceStart + tail.SourceLength - 1));
        var end = last + 1 < snapshot.LineCount
            ? snapshot.GetLineStartOffset(last + 1) : snapshot.Length;
        return Range(binding, snapshot.GetLineStartOffset(first), end);
    }

    /// <summary>Checks that a client range still belongs to the currently bound source.</summary>
    internal void ValidateRange(AccessibleRange range) =>
        ValidateRange(ReadBinding(), range);

    private NativeCanvasBinding ReadBinding()
    {
        EnsureOpen();
        var binding = Volatile.Read(ref _binding)
            ?? throw new InvalidOperationException("Accessibility editor is closed.");
        EnsureOpen();
        return binding;
    }

    private void EnsureOpen()
    {
        if (Volatile.Read(ref _closed) != 0)
            throw new InvalidOperationException("Accessibility editor is closed.");
    }

    private static void ValidateRange(NativeCanvasBinding binding, AccessibleRange range)
    {
        if (range.Generation != binding.DocumentGeneration || range.Version != binding.Snapshot.Version)
            throw new InvalidOperationException("Accessibility range belongs to a stale document version.");
        if (range.Start < 0 || range.End < range.Start || range.End > binding.Snapshot.Length)
            throw new ArgumentOutOfRangeException(nameof(range));
    }

    private static AccessibleRange Range(NativeCanvasBinding binding, int start, int end)
    {
        if (start < 0 || end < start || end > binding.Snapshot.Length)
            throw new ArgumentOutOfRangeException(nameof(end));
        return new AccessibleRange(binding.DocumentGeneration, binding.Snapshot.Version, start, end);
    }

    private static NativeCanvasBinding Validate(NativeCanvasBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(binding.Snapshot);
        ArgumentNullException.ThrowIfNull(binding.Frame);
        if (binding.BaseVersion != binding.Snapshot.Version ||
            binding.Frame.Version != binding.Snapshot.Version)
            throw new ArgumentException("Source, input binding, and frame versions must match.", nameof(binding));
        if ((uint)binding.Anchor > (uint)binding.Snapshot.Length ||
            (uint)binding.Active > (uint)binding.Snapshot.Length)
            throw new ArgumentException("Selection exceeds source length.", nameof(binding));
        ValidateFrame(binding.Frame, binding.Snapshot.Length);
        if (binding.Frame.SelectionAnchor != binding.Anchor ||
            binding.Frame.SelectionActive != binding.Active)
            throw new ArgumentException("Frame and binding selections differ.", nameof(binding));
        return binding;
    }

    private static void ValidateFrame(CanvasFrame frame, int sourceLength)
    {
        if ((uint)frame.SelectionAnchor > (uint)sourceLength ||
            (uint)frame.SelectionActive > (uint)sourceLength)
            throw new ArgumentException("Frame selection exceeds source length.", nameof(frame));
        ArgumentNullException.ThrowIfNull(frame.Slices);
        foreach (var slice in frame.Slices)
        {
            if (slice.SourceStart < 0 || slice.SourceLength < 0 ||
                slice.SourceStart > sourceLength - slice.SourceLength)
                throw new ArgumentException("Visible slice exceeds source length.", nameof(frame));
        }
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
