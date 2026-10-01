using Mote.Formats;
using Mote.Themes;

namespace Mote.Native.Mac;

/// <summary>Portable contracts for AppKit source ranges and bounded visible foreground overlays.</summary>
internal static class MacProductSourceModel
{
    /// <summary>Refuses paragraph-sized viewport certificates rather than silently styling an entire long line.</summary>
    internal const int MaximumVisibleCharacters = 65536;
    /// <summary>A coalesced display-coordinate native foreground run.</summary>
    internal readonly record struct ForegroundRun(int Start, int Length, ThemeColor Color);

    /// <summary>Only a focused, unavailable product replica with unadmitted text may bypass canonical Copy.</summary>
    internal static bool CanCopyUnadmittedSource(bool enabled, bool unavailable, bool unadmitted, bool sourceFocused) =>
        enabled && unavailable && unadmitted && sourceFocused;

    /// <summary>Rejects invalid snapshots, incomplete projections, NUL and scalar-splitting selections before native mutation.</summary>
    internal static void ValidateInstallation(NativeSourceInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (installation.Nonce <= 0 || installation.Stamp.Generation <= 0 ||
            installation.Stamp.Version != installation.Snapshot.Version ||
            installation.Projection.Source != installation.Snapshot.GetText())
            throw new ArgumentException("Source installation must identify the complete immutable snapshot.");
        var display = installation.Projection.Display;
        if (display.Contains('\0')) throw new NotSupportedException("Embedded NUL cannot be imported by this source profile.");
        if ((uint)installation.Anchor > installation.Snapshot.Length || (uint)installation.Active > installation.Snapshot.Length ||
            !Boundary(installation.Projection.Source, installation.Anchor) || !Boundary(installation.Projection.Source, installation.Active))
            throw new ArgumentException("Source selection must contain valid scalar boundaries.");
    }

    /// <summary>Certifies Before/After identity and exact range equation without creating or applying an engine change.</summary>
    internal static void ValidateReplacement(NativeSourceReplacement replacement)
    {
        ValidateInstallation(replacement.Before);
        ValidateInstallation(replacement.After);
        var before = replacement.Before.Projection.Display;
        var after = replacement.After.Projection.Display;
        var start = replacement.DisplayStart;
        var delete = replacement.DisplayDeleteLength;
        if (replacement.Before.Nonce != replacement.After.Nonce ||
            replacement.Before.Stamp.Generation != replacement.After.Stamp.Generation ||
            replacement.After.Stamp.Version <= replacement.Before.Stamp.Version ||
            start < 0 || delete < 0 || start > before.Length || delete > before.Length - start ||
            !Boundary(before, start) || !Boundary(before, start + delete) ||
            !string.Equals(string.Concat(before.AsSpan(0, start), replacement.DisplayInsert,
                before.AsSpan(start + delete)), after, StringComparison.Ordinal))
            throw new ArgumentException("Guarded source replacement must exactly produce its declared successor.");
    }

    /// <summary>Sorted range is not direction; only a matching explicit witness or collapsed range supplies Active.</summary>
    internal static NativeSourceSelection Selection(nuint location, nuint length, int textLength,
        NativeSourceSelection? witness = null)
    {
        if (location > (nuint)textLength || length > (nuint)textLength - location)
            throw new ArgumentOutOfRangeException(nameof(location));
        var start = checked((int)location);
        var end = checked((int)(location + length));
        int? active = start == end ? start : witness is { } actual && actual.Start == start && actual.End == end
            ? actual.Active : null;
        return new(start, end, active);
    }

    /// <summary>Checks a native viewport witness without clamping a bogus range into a seemingly valid result.</summary>
    internal static TextSpan Visible(nuint location, nuint length, int textLength)
    {
        if (location > (nuint)textLength || length > (nuint)textLength - location || length > MaximumVisibleCharacters)
            throw new ArgumentOutOfRangeException(nameof(location), "Native viewport does not certify bounded visible characters.");
        return new(checked((int)location), checked((int)length));
    }

    /// <summary>
    /// Builds the original ordered token overlay only inside actual visible display characters.
    /// Neutral color fills uncovered/pending locations. Coalescing avoids one native call per character.
    /// </summary>
    internal static IReadOnlyList<ForegroundRun> Foreground(NativeTextProjection projection,
        TextSpan visible, NativeSourceSemantics? semantics, IThemePolicy theme)
    {
        if (visible.Start < 0 || visible.Length < 0 || visible.Start > projection.Display.Length ||
            visible.Length > projection.Display.Length - visible.Start ||
            visible.Length > MaximumVisibleCharacters) throw new ArgumentOutOfRangeException(nameof(visible));
        if (visible.Length == 0) return Array.Empty<ForegroundRun>();
        var colors = new ThemeColor[visible.Length];
        Array.Fill(colors, theme.Palette.EditorForeground);
        if (semantics is not null)
        {
            foreach (var token in semantics.Tokens)
            {
                if (token.Span.Start < 0 || token.Span.Length <= 0 || token.Span.Start > projection.Source.Length ||
                    token.Span.Length > projection.Source.Length - token.Span.Start) continue;
                var start = Math.Max(visible.Start, projection.ToDisplay(token.Span.Start));
                var end = Math.Min(visible.End, projection.ToDisplay(token.Span.End));
                if (end <= start) continue;
                Array.Fill(colors, theme.SemanticColor(token.Kind), start - visible.Start, end - start);
            }
        }
        var runs = new List<ForegroundRun>();
        var runStart = 0;
        for (var i = 1; i <= colors.Length; i++)
        {
            if (i < colors.Length && colors[i] == colors[runStart]) continue;
            runs.Add(new(visible.Start + runStart, i - runStart, colors[runStart]));
            runStart = i;
        }
        return runs;
    }

    /// <summary>Surrogate pairs are indivisible canonical UTF-16 scalar boundaries.</summary>
    private static bool Boundary(string text, int offset) => offset == 0 || offset == text.Length ||
        !char.IsHighSurrogate(text[offset - 1]) || !char.IsLowSurrogate(text[offset]);
}
