using System.Globalization;
using System.Text;
using Mote.Engine;

namespace Mote.Native.Viewport;

/// <summary>One original-source, single-line window for an OS text-input host.</summary>
internal readonly record struct CanvasInputWindow(int SourceStart, string SourceText)
{
    /// <summary>Absolute source boundary after the copied window.</summary>
    internal int SourceEnd => SourceStart + SourceText.Length;
}

/// <summary>
/// A caret or required window boundary cannot be certified as an extended grapheme
/// boundary within the bounded input-host context. The caller must invalidate any
/// previous host binding, keep the new document canvas readable, and offer a
/// recoverable edit error rather than bind partial text.
/// </summary>
internal sealed class CanvasInputWindowBoundaryException(int caret, string message)
    : InvalidOperationException(message)
{
    /// <summary>Global source caret for which the input host cannot be safely bound.</summary>
    internal int Caret { get; } = caret;
}

/// <summary>
/// Chooses a caret-local input window without reading a complete logical line.
/// Source line delimiters remain in the engine but never enter the island;
/// inserting one creates a normal source edit followed by a new binding.
/// Window edges are extended grapheme boundaries according to .NET StringInfo.
/// A local break is accepted as a segmentation reset only when the preceding
/// scalar cannot carry RI parity, ZWJ, or extender/linker context; otherwise the
/// selector scans backward at most the requested window length and segments
/// forward. A cluster or dependency exceeding that bound produces a typed
/// failure. This protects source integrity, but does not certify native font
/// shaping or bidi geometry.
/// </summary>
internal static class CanvasInputWindowSelector
{
    internal const int MaxLength = 16 * 1024;

    /// <summary>
    /// Returns no more than 16 Ki UTF-16 units around a grapheme-boundary caret.
    /// A local Unicode segment is certified from a bounded preceding boundary;
    /// an unbounded cluster or context dependency fails explicitly.
    /// </summary>
    /// <exception cref="CanvasInputWindowBoundaryException">
    /// The caret is inside a grapheme, or a safe window cannot be proven within
    /// <see cref="MaxLength"/> UTF-16 units.
    /// </exception>
    internal static CanvasInputWindow Select(TextSnapshot snapshot, int caret) =>
        Select(snapshot, caret, MaxLength);

    /// <summary>
    /// Returns a single-line, grapheme-safe input window no longer than
    /// <paramref name="maxLength"/> UTF-16 units. Platforms may request less
    /// than 16 Ki to stay inside native text-layout width limits. Valid bounds
    /// are 2 through <see cref="MaxLength"/> inclusive.
    /// </summary>
    /// <exception cref="CanvasInputWindowBoundaryException">
    /// The caret is inside a grapheme, or its cluster/context cannot be certified
    /// within the requested window length.
    /// </exception>
    internal static CanvasInputWindow Select(TextSnapshot snapshot, int caret, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (maxLength is < 2 or > MaxLength)
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        if ((uint)caret > (uint)snapshot.Length)
            throw new ArgumentOutOfRangeException(nameof(caret));
        var line = snapshot.GetLineIndexFromOffset(caret);
        var start = snapshot.GetLineStartOffset(line);
        var end = ContentEnd(snapshot, line, start);
        var clampedCaret = Math.Clamp(caret, start, end);
        var caretBoundary = PreviousCertifiedBoundary(snapshot, clampedCaret, start, end, caret, maxLength);
        if (caretBoundary != clampedCaret)
            throw new CanvasInputWindowBoundaryException(caret,
                "The input caret is inside an extended grapheme cluster.");

        var idealStart = Math.Max(start, clampedCaret - maxLength / 2);
        var windowStart = PreviousCertifiedBoundary(snapshot, idealStart, start, end, caret, maxLength);
        if (clampedCaret - windowStart > maxLength)
            windowStart = clampedCaret;
        var windowEnd = LastBoundaryWithinLimit(snapshot, windowStart, end, clampedCaret, caret, maxLength);
        if (windowEnd == clampedCaret && clampedCaret < end && windowStart < clampedCaret)
        {
            // The first cluster after the caret may fit only after discarding left
            // context; prefer a complete forward cluster to a misleading empty tail.
            windowStart = clampedCaret;
            windowEnd = LastBoundaryWithinLimit(snapshot, windowStart, end, clampedCaret, caret, maxLength);
        }
        return new CanvasInputWindow(windowStart, snapshot.GetText(windowStart, windowEnd - windowStart));
    }

    private static int ContentEnd(TextSnapshot snapshot, int line, int start)
    {
        if (line == snapshot.LineCount - 1) return snapshot.Length;
        var end = snapshot.GetLineStartOffset(line + 1);
        if (end > start && snapshot.GetText(end - 1, 1)[0] == '\n') end--;
        if (end > start && snapshot.GetText(end - 1, 1)[0] == '\r') end--;
        return end;
    }

    private static int PreviousCertifiedBoundary(TextSnapshot snapshot, int offset,
        int lineStart, int lineEnd, int caret, int maxLength)
    {
        var probe = SnapSurrogateBackward(snapshot, offset, lineStart);
        if (probe == lineEnd || probe == lineStart) return probe;
        while (probe > lineStart && probe < lineEnd)
        {
            var previous = PreviousScalarStart(snapshot, probe, lineStart);
            if (IsCertifiedBreak(snapshot, previous, probe, lineEnd)) break;
            probe = previous;
            if (offset - probe > maxLength)
                throw new CanvasInputWindowBoundaryException(caret,
                    "The grapheme context exceeds the bounded input window.");
        }

        // The safe anchor resets RI parity and ZWJ / linker context. Segment forward
        // to recover boundaries inside that run rather than treating the whole run
        // as one cluster (notably every second regional-indicator pair).
        var contextEnd = Math.Min((long)lineEnd, (long)offset + 4);
        var context = snapshot.GetText(probe, (int)(contextEnd - probe));
        var boundaries = StringInfo.ParseCombiningCharacters(context);
        var last = probe;
        foreach (var relative in boundaries)
        {
            var boundary = probe + relative;
            if (boundary > offset) break;
            last = boundary;
        }

        return last;
    }

    private static int LastBoundaryWithinLimit(TextSnapshot snapshot, int start,
        int lineEnd, int caretBoundary, int caret, int maxLength)
    {
        if (start == lineEnd) return start;
        // Four lookahead units include a complete scalar beyond the maximum window,
        // so a cluster ending at the cap is not mistaken for a truncated cluster.
        var contextLength = Math.Min((long)lineEnd - start, maxLength + 4L);
        var context = snapshot.GetText(start, (int)contextLength);
        var starts = StringInfo.ParseCombiningCharacters(context);
        var limit = (int)Math.Min(lineEnd, (long)start + maxLength);
        var last = start;
        foreach (var relative in starts)
        {
            var boundary = start + relative;
            if (boundary > limit) break;
            last = boundary;
        }

        if (start + context.Length == lineEnd && lineEnd <= limit) last = lineEnd;
        if (last < caretBoundary || last == start && start < lineEnd)
            throw new CanvasInputWindowBoundaryException(caret,
                "The caret's grapheme cannot fit in the bounded input window.");
        return last;
    }

    private static bool IsCertifiedBreak(TextSnapshot snapshot, int previous,
        int current, int lineEnd)
    {
        if (current == lineEnd) return true;
        var prevText = snapshot.GetText(previous, current - previous);
        var previousRune = Rune.GetRuneAt(prevText, 0);
        if (DependsOnEarlierContext(previousRune)) return false;
        var currentEnd = Math.Min(lineEnd, current + 2);
        var pair = snapshot.GetText(previous, currentEnd - previous);
        var boundaries = StringInfo.ParseCombiningCharacters(pair);
        return boundaries.Length >= 2 && boundaries[1] == current - previous;
    }

    private static bool DependsOnEarlierContext(Rune rune)
    {
        if (rune.Value is >= 0x1F1E6 and <= 0x1F1FF) return true; // RI parity.
        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.Format; // Extend/linker/ZWJ context.
    }

    private static int PreviousScalarStart(TextSnapshot snapshot, int offset, int lineStart)
    {
        var previous = offset - 1;
        return SnapSurrogateBackward(snapshot, previous, lineStart);
    }

    private static int SnapSurrogateBackward(TextSnapshot snapshot, int offset, int lineStart)
    {
        if (offset <= lineStart || offset >= snapshot.Length) return offset;
        var pair = snapshot.GetText(offset - 1, 2);
        return char.IsHighSurrogate(pair[0]) && char.IsLowSurrogate(pair[1])
            ? offset - 1 : offset;
    }
}
