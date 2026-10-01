using System.Runtime.Versioning;
using Mote.Formats;
using Mote.Themes;

namespace Mote.Native.Mac;

/// <summary>Applies bounded, format-owned Flow styles directly to native text storage.</summary>
/// <remarks>UI-thread only. Link appearance never installs NSLink or performs external I/O.</remarks>
[SupportedOSPlatform("macos")]
internal static class MacFlowAttributes
{
    /// <summary>Restyles existing characters without replacing text or native selection.</summary>
    internal static void Apply(nint storage, FlowRenderProjection flow, IThemePolicy? theme)
    {
        if (flow.Text.Length == 0) return;
        var whole = new ObjC.Range(0, (nuint)flow.Text.Length);
        var defaults = ObjC.New("NSMutableDictionary");
        var fonts = new Dictionary<(int, FlowInlineStyle), nint>();
        var colors = new Dictionary<ThemeColor, nint>();
        ObjC.Send(storage, ObjC.Sel("beginEditing"));
        try
        {
            ObjC.Send(storage, ObjC.Sel("setAttributes:range:"), defaults, whole);
            Attribute(storage, "NSFont", Font(theme, 0, FlowInlineStyle.None), whole);
            Attribute(storage, "NSColor", Color(theme?.Palette.PreviewForeground ??
                new ThemeColor(225, 227, 231)), whole);
            foreach (var paragraph in flow.Paragraphs) Paragraph(storage, paragraph, theme);
            foreach (var run in flow.Runs) Run(storage, flow, run, theme, fonts, colors);
        }
        finally
        {
            ObjC.Send(storage, ObjC.Sel("endEditing"));
            ObjC.Send(defaults, ObjC.Sel("release"));
        }
    }

    private static void Paragraph(nint storage, FlowParagraph paragraph, IThemePolicy? theme)
    {
        if (paragraph.DisplayRange.Length == 0) return;
        var style = ObjC.New("NSMutableParagraphStyle");
        try
        {
            var unit = theme?.Spacing.Unit ?? 4d;
            var indent = Math.Min(paragraph.Depth, 16) * unit * 4;
            var list = paragraph.Marker is not null || paragraph.Kind == "list-item";
            if (paragraph.Kind == "quote") indent += unit * 4;
            ObjC.Send(style, ObjC.Sel("setFirstLineHeadIndent:"), indent);
            ObjC.Send(style, ObjC.Sel("setHeadIndent:"), indent + (list ? unit * 4 : 0));
            ObjC.Send(style, ObjC.Sel("setParagraphSpacing:"), list ? unit : unit * 2);
            ObjC.Send(style, ObjC.Sel("setParagraphSpacingBefore:"),
                paragraph.Kind == "heading" ? unit * 2 : 0d);
            ObjC.Send(style, ObjC.Sel("setLineHeightMultiple:"),
                theme?.Typography.LineHeightMultiplier ?? 1.35d);
            var range = Range(paragraph.DisplayRange);
            Attribute(storage, "NSParagraphStyle", style, range);
            if (paragraph.Kind == "heading")
                Attribute(storage, "NSFont", Font(theme, paragraph.Level, FlowInlineStyle.Strong), range);
        }
        finally { ObjC.Send(style, ObjC.Sel("release")); }
    }

    private static void Run(nint storage, FlowRenderProjection flow, FlowRun run, IThemePolicy? theme,
        Dictionary<(int, FlowInlineStyle), nint> fonts, Dictionary<ThemeColor, nint> colors)
    {
        if (run.DisplayRange.Length == 0) return;
        var level = 0;
        foreach (var paragraph in flow.Paragraphs)
        {
            if (paragraph.DisplayRange.Start > run.DisplayRange.Start) break;
            if (paragraph.Kind == "heading" && run.DisplayRange.Start < paragraph.DisplayRange.End)
                level = paragraph.Level;
        }
        var range = Range(run.DisplayRange);
        var style = level > 0 ? run.Style | FlowInlineStyle.Strong : run.Style;
        var fontStyle = style & ~FlowInlineStyle.Link;
        if (!fonts.TryGetValue((level, fontStyle), out var font))
            fonts.Add((level, fontStyle), font = Font(theme, level, fontStyle));
        Attribute(storage, "NSFont", font, range);
        var role = NativeFlowPresentation.Role(run,
            NativeFlowPresentation.ContainingParagraph(flow, run));
        var color = role switch
        {
            "heading" or "list-marker" => theme?.Palette.Accent,
            "quote" or "comment" or "notice" => theme?.Palette.MutedForeground,
            "code" => theme?.Palette.Info,
            _ => theme?.SemanticColor(role)
        } ?? new ThemeColor(225, 227, 231);
        if (style.HasFlag(FlowInlineStyle.Link)) color = theme?.Palette.Accent ?? color;
        if (!colors.TryGetValue(color, out var nativeColor))
            colors.Add(color, nativeColor = Color(color));
        Attribute(storage, "NSColor", nativeColor, range);
        if (style.HasFlag(FlowInlineStyle.Link))
            Attribute(storage, "NSUnderline", ObjC.Send(ObjC.Class("NSNumber"),
                ObjC.Sel("numberWithInteger:"), (nint)1), range);
    }

    private static nint Font(IThemePolicy? theme, int level, FlowInlineStyle style)
    {
        var size = theme?.Typography.UiFontSize ?? 12d;
        if (level > 0) size *= level switch { 1 => 1.8, 2 => 1.55, 3 => 1.35, _ => 1.15 };
        var font = style.HasFlag(FlowInlineStyle.Code)
            ? ObjC.Send(ObjC.Class("NSFont"), ObjC.Sel("monospacedSystemFontOfSize:weight:"),
                theme?.Typography.EditorFontSize ?? 13d, 0d)
            : ObjC.Send(ObjC.Class("NSFont"), ObjC.Sel("systemFontOfSize:"), size);
        var traits = (style.HasFlag(FlowInlineStyle.Strong) ? 2 : 0) |
            (style.HasFlag(FlowInlineStyle.Emphasis) ? 1 : 0);
        if (traits == 0) return font;
        var manager = ObjC.Send(ObjC.Class("NSFontManager"), ObjC.Sel("sharedFontManager"));
        return ObjC.Send(manager, ObjC.Sel("convertFont:toHaveTrait:"), font, (nint)traits);
    }

    private static ObjC.Range Range(TextSpan span) => new((nuint)span.Start, (nuint)span.Length);

    private static void Attribute(nint storage, string key, nint value, ObjC.Range range) =>
        ObjC.Send(storage, ObjC.Sel("addAttribute:value:range:"), ObjC.String(key), value, range);

    private static nint Color(ThemeColor color) => ObjC.Send(ObjC.Class("NSColor"),
        ObjC.Sel("colorWithSRGBRed:green:blue:alpha:"),
        color.Red / 255d, color.Green / 255d, color.Blue / 255d, 1d);
}
