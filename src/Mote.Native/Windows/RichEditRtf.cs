using System.Text;
using Mote.Formats;
using Mote.Themes;

namespace Mote.Native.Windows;

/// <summary>
/// Builds one RichEdit RTF replacement for a bounded page. A single import avoids the
/// quadratic layout cost of selecting and formatting thousands of semantic spans.
/// </summary>
internal static class RichEditRtf
{
    /// <summary>Escapes page text and applies non-overlapping semantic foreground runs.</summary>
    public static string Build(string text, IReadOnlyList<SemanticToken> tokens, IThemePolicy theme)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(theme);
        var ordered = tokens.Where(token => token.Span.Start >= 0 && token.Span.Length > 0 &&
            token.Span.End <= text.Length).OrderBy(token => token.Span.Start).ToArray();
        var colors = new List<ThemeColor> { theme.Palette.EditorForeground };
        var colorByValue = new Dictionary<ThemeColor, int>
        {
            [theme.Palette.EditorForeground] = 1
        };
        var tokenColors = new int[ordered.Length];
        for (var i = 0; i < ordered.Length; i++)
        {
            var color = theme.SemanticColor(ordered[i].Kind);
            if (!colorByValue.TryGetValue(color, out var colorIndex))
            {
                colors.Add(color);
                colorIndex = colors.Count;
                colorByValue.Add(color, colorIndex);
            }
            tokenColors[i] = colorIndex;
        }

        var output = new StringBuilder(text.Length + Math.Min(text.Length, ordered.Length * 16) + 256);
        output.Append(@"{\rtf1\ansi\deff0\uc1{");
        output.Append(@"fonttbl{\f0\fmodern ");
        AppendFont(output, FirstFont(theme.Typography.EditorFontFamilies));
        output.Append(@";}}{\colortbl;");
        foreach (var color in colors)
            output.Append("\\red").Append(color.Red).Append("\\green").Append(color.Green)
                .Append("\\blue").Append(color.Blue).Append(';');
        output.Append("}\\f0\\fs").Append((int)Math.Round(theme.Typography.EditorFontSize * 2))
            .Append("\\cf1 ");

        var tokenIndex = 0;
        var activeColor = 1;
        for (var offset = 0; offset < text.Length;)
        {
            while (tokenIndex < ordered.Length && ordered[tokenIndex].Span.End <= offset)
                tokenIndex++;
            var color = tokenIndex < ordered.Length && ordered[tokenIndex].Span.Start <= offset
                ? tokenColors[tokenIndex] : 1;
            if (color != activeColor)
            {
                output.Append("\\cf").Append(color).Append(' ');
                activeColor = color;
            }

            var c = text[offset++];
            if (c == '\r' && offset < text.Length && text[offset] == '\n') offset++;
            if (c is '\r' or '\n') output.Append("\\par\n");
            else if (c == '\t') output.Append("\\tab ");
            else if (c is '\\' or '{' or '}') output.Append('\\').Append(c);
            else if (c is >= ' ' and <= '~') output.Append(c);
            else output.Append("\\u").Append((short)c).Append('?');
        }
        return output.Append('}').ToString();
    }

    private static string FirstFont(string families)
    {
        var first = families.Split(',', 2)[0].Trim();
        return first.Length == 0 ? "Consolas" : first;
    }

    private static void AppendFont(StringBuilder output, string name)
    {
        foreach (var c in name)
        {
            if (c is '\\' or '{' or '}' or ';') continue;
            if (c is >= ' ' and <= '~') output.Append(c);
        }
    }
}
