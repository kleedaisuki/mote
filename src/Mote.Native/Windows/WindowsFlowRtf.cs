using System.Text;
using Mote.Formats;
using Mote.Themes;

namespace Mote.Native.Windows;

/// <summary>Serializes a bounded policy-owned Flow projection into one inert RichEdit transaction.</summary>
/// <remarks>No fields, destinations, objects, images, or native hyperlink effects are emitted.</remarks>
internal static class WindowsFlowRtf
{
    /// <summary>Rejects malformed display maps before any control text or identity can be installed.</summary>
    internal static bool IsValid(FlowRenderProjection flow)
    {
        if (flow.Text.Length > FlowRenderProjection.MaxTextLength || flow.Text.Contains('\0')) return false;
        var end = 0;
        foreach (var run in flow.Runs)
        {
            if (run.DisplayRange.Start < end || run.DisplayRange.Length <= 0 ||
                run.DisplayRange.Start < 0 || run.DisplayRange.End > flow.Text.Length ||
                run.SourceRange.Start < 0 || run.SourceRange.Length < 0) return false;
            end = run.DisplayRange.End;
        }
        end = 0;
        foreach (var paragraph in flow.Paragraphs)
        {
            if (paragraph.DisplayRange.Start < end || paragraph.DisplayRange.Length < 0 ||
                paragraph.DisplayRange.Start < 0 || paragraph.DisplayRange.End > flow.Text.Length ||
                paragraph.SourceRange.Start < 0 || paragraph.SourceRange.Length < 0) return false;
            if (paragraph.DisplayRange.Start > 0 && flow.Text[paragraph.DisplayRange.Start - 1] != '\n')
                return false;
            end = paragraph.DisplayRange.End;
        }
        return true;
    }

    /// <summary>Escapes every UTF-16 code unit and emits flat style runs, never native actions.</summary>
    internal static string Build(FlowRenderProjection flow, IThemePolicy theme)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(theme);
        if (!IsValid(flow)) throw new ArgumentException("Invalid bounded Flow projection.", nameof(flow));
        var colors = new List<ThemeColor> { theme.Palette.PreviewForeground };
        var indices = new Dictionary<ThemeColor, int> { [colors[0]] = 1 };
        var runColors = new int[flow.Runs.Count];
        for (var i = 0; i < flow.Runs.Count; i++)
        {
            var run = flow.Runs[i];
            var role = NativeFlowPresentation.Role(run,
                NativeFlowPresentation.ContainingParagraph(flow, run));
            var color = role switch
            {
                "heading" => theme.Palette.Accent,
                "paragraph" or "text" or "table-cell" => theme.Palette.PreviewForeground,
                "quote" => theme.Palette.MutedForeground,
                _ => theme.SemanticColor(role)
            };
            if (!indices.TryGetValue(color, out var index))
            {
                colors.Add(color);
                indices[color] = index = colors.Count;
            }
            runColors[i] = index;
        }
        var output = new StringBuilder(flow.Text.Length * 2 + 512);
        output.Append(@"{\rtf1\ansi\deff0\uc1{\fonttbl{\f0\fswiss ");
        AppendFont(output, theme.Typography.UiFontFamilies);
        output.Append(@";}{\f1\fmodern ");
        AppendFont(output, theme.Typography.EditorFontFamilies);
        output.Append(@";}}{\colortbl;");
        foreach (var color in colors)
            output.Append("\\red").Append(color.Red).Append("\\green").Append(color.Green)
                .Append("\\blue").Append(color.Blue).Append(';');
        output.Append(@"}\viewkind4 ");
        var runIndex = 0;
        var paragraphIndex = 0;
        var style = (FlowInlineStyle)(-1);
        var colorIndex = -1;
        var paragraphStyle = FlowInlineStyle.None;
        for (var offset = 0; offset < flow.Text.Length; offset++)
        {
            if (offset == 0 || flow.Text[offset - 1] == '\n')
            {
                while (paragraphIndex < flow.Paragraphs.Count &&
                    flow.Paragraphs[paragraphIndex].DisplayRange.End <= offset) paragraphIndex++;
                FlowParagraph? paragraph = paragraphIndex < flow.Paragraphs.Count &&
                    flow.Paragraphs[paragraphIndex].DisplayRange.Start <= offset
                    ? flow.Paragraphs[paragraphIndex] : null;
                AppendParagraph(output, paragraph, theme);
                paragraphStyle = paragraph?.Kind == "heading" ? FlowInlineStyle.Strong :
                    paragraph?.Kind is "code" or "code-block" ? FlowInlineStyle.Code : FlowInlineStyle.None;
                style = (FlowInlineStyle)(-1);
                colorIndex = -1;
            }
            while (runIndex < flow.Runs.Count && flow.Runs[runIndex].DisplayRange.End <= offset)
                runIndex++;
            var active = runIndex < flow.Runs.Count && flow.Runs[runIndex].DisplayRange.Start <= offset;
            var nextStyle = (active ? flow.Runs[runIndex].Style : FlowInlineStyle.None) | paragraphStyle;
            var nextColor = active ? runColors[runIndex] : 1;
            if (nextStyle != style)
            {
                output.Append((nextStyle & FlowInlineStyle.Strong) != 0 ? "\\b " : "\\b0 ")
                    .Append((nextStyle & FlowInlineStyle.Emphasis) != 0 ? "\\i " : "\\i0 ")
                    .Append((nextStyle & FlowInlineStyle.Code) != 0 ? "\\f1 " : "\\f0 ");
                style = nextStyle;
            }
            if (nextColor != colorIndex) { output.Append("\\cf").Append(nextColor).Append(' '); colorIndex = nextColor; }
            var c = flow.Text[offset];
            if (c == '\r' && offset + 1 < flow.Text.Length && flow.Text[offset + 1] == '\n') continue;
            AppendCharacter(output, c);
        }
        return output.Append('}').ToString();
    }

    /// <summary>Paragraph spacing and indentation are theme-derived; no synthetic text is added.</summary>
    private static void AppendParagraph(StringBuilder output, FlowParagraph? paragraph, IThemePolicy theme)
    {
        var heading = paragraph?.Kind == "heading";
        var code = paragraph?.Kind is "code" or "code-block";
        var quote = paragraph?.Kind == "quote";
        var indent = Math.Clamp(paragraph?.Depth ?? 0, 0, 16) * 240 + (quote ? 240 : 0);
        var size = theme.Typography.UiFontSize;
        if (heading) size *= 1.0 + (7 - Math.Clamp(paragraph.GetValueOrDefault().Level, 1, 6)) * 0.1;
        output.Append("\\pard\\li").Append(indent).Append("\\sa")
            .Append((int)Math.Round(theme.Spacing.SectionGap * 15))
            .Append("\\fs").Append((int)Math.Round(size * 2))
            .Append(code ? "\\f1 " : "\\f0 ");
    }

    /// <summary>Font family data cannot escape its font-table entry.</summary>
    private static void AppendFont(StringBuilder output, string families)
    {
        var font = families.Split(',', 2)[0].Trim();
        if (font.Length == 0) font = "Segoe UI";
        foreach (var c in font)
            if (c != ';' && !char.IsControl(c)) AppendCharacter(output, c);
    }

    /// <summary>Signed Unicode escapes preserve surrogate pairs and block RTF syntax injection.</summary>
    private static void AppendCharacter(StringBuilder output, char c)
    {
        if (c is '\r' or '\n') output.Append("\\par\n");
        else if (c == '\t') output.Append("\\tab ");
        else if (c is '\\' or '{' or '}') output.Append('\\').Append(c);
        else if (c is >= ' ' and <= '~') output.Append(c);
        else output.Append("\\u").Append((short)c).Append('?');
    }
}
