using Mote.Engine;
using Mote.Formats;

namespace Mote.Native.Windows;

/// <summary>Pure last-writer foreground planner over a bounded viewport interest; no native state.</summary>
internal static class WindowsSourceForegroundPlan
{
    /// <summary>One absolute display interval with one effective explicit COLORREF.</summary>
    internal readonly record struct Run(int Start, int End, uint Color);

    /// <summary>Trims an interest inward to complete scalar/paragraph units without expanding a logical line.</summary>
    internal static TextSpan Trim(string display, TextSpan interest)
    {
        var start = interest.Start;
        var end = interest.End;
        if (Trailing(display, start)) start++;
        if (Trailing(display, end)) end--;
        return new TextSpan(start, Math.Max(0, end - start));
    }

    /// <summary>Coalesces adjacent equal colors in source publication order, keeping scalar/CRLF units whole.</summary>
    internal static Run[] Build(string display, TextSpan interest, uint foreground,
        IReadOnlyList<Run> styles)
    {
        var visible = Trim(display, interest);
        var colors = new uint[visible.Length];
        Array.Fill(colors, foreground);
        foreach (var style in styles)
        {
            var start = Math.Max(visible.Start, style.Start);
            var end = Math.Min(visible.End, style.End);
            if (end > start) Array.Fill(colors, style.Color, start - visible.Start, end - start);
        }
        // A source token must not create a trailing-half-only native attribute.
        for (var i = 1; i < colors.Length; i++)
            if (Trailing(display, visible.Start + i)) colors[i] = colors[i - 1];
        var result = new List<Run>();
        for (var i = 0; i < colors.Length;)
        {
            var end = i + 1;
            while (end < colors.Length && colors[end] == colors[i]) end++;
            result.Add(new Run(visible.Start + i, visible.Start + end, colors[i]));
            i = end;
        }
        return result.ToArray();
    }

    /// <summary>True only for a boundary inside one UTF-16 scalar or projected CRLF paragraph.</summary>
    private static bool Trailing(string display, int position) => position > 0 && position < display.Length &&
        (display[position] == '\n' && display[position - 1] == '\r' ||
         char.IsLowSurrogate(display[position]) && char.IsHighSurrogate(display[position - 1]));
}
