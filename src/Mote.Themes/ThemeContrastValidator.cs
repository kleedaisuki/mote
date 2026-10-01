namespace Mote.Themes;

/// <summary>A contrast deficiency in a named text or non-text UI role.</summary>
public sealed record ThemeContrastIssue(string Role, double Ratio, double Minimum);

/// <summary>Validates critical editor and chrome contrast pairs using WCAG 2.2 ratios.</summary>
/// <remarks>
/// Text pairs use 4.5:1 and essential non-text boundaries use 3:1. These are
/// palette checks, not proof that a UI adapter applies every foreground correctly.
/// Selection text must override syntax colors on the selection background.
/// </remarks>
public static class ThemeContrastValidator
{
    private static readonly string[] SemanticKinds =
    [
        "key", "heading", "string", "fenced-code", "code", "number", "boolean",
        "null", "keyword", "emphasis", "comment", "heading-marker", "list-marker",
        "sequence-marker", "fence", "link", "error", "invalid"
    ];

    /// <summary>Returns contrast failures; an empty result means all declared pairs pass.</summary>
    public static IReadOnlyList<ThemeContrastIssue> Validate(IThemePolicy theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var palette = theme.Palette;
        var issues = new List<ThemeContrastIssue>();

        Check("editor text", palette.EditorForeground, palette.EditorBackground, 4.5);
        Check("editor text on active line", palette.EditorForeground, palette.ActiveLineBackground, 4.5);
        Check("preview text", palette.PreviewForeground, palette.PreviewBackground, 4.5);
        Check("preview muted text", palette.MutedForeground, palette.PreviewBackground, 4.5);
        Check("preview accent text", palette.Accent, palette.PreviewBackground, 4.5);
        Check("preview information text", palette.Info, palette.PreviewBackground, 4.5);
        Check("preview error text", palette.Error, palette.PreviewBackground, 4.5);
        Check("secondary text", palette.MutedForeground, palette.PanelBackground, 4.5);
        Check("gutter text", palette.GutterForeground, palette.EditorBackground, 4.5);
        Check("selection text", palette.SelectionForeground, palette.SelectionBackground, 4.5);
        Check("button and active-tab text", palette.ControlForeground, palette.ControlBackground, 4.5);
        Check("accent text", palette.Accent, palette.PanelBackground, 4.5);
        Check("error text", palette.Error, palette.PanelBackground, 4.5);
        Check("warning text", palette.Warning, palette.PanelBackground, 4.5);
        Check("information text", palette.Info, palette.PanelBackground, 4.5);
        Check("success text", palette.Success, palette.PanelBackground, 4.5);
        Check("caret", palette.Cursor, palette.EditorBackground, 3);
        Check("panel divider", palette.Border, palette.PanelBackground, 3);
        foreach (var kind in SemanticKinds)
        {
            Check($"semantic {kind}", theme.SemanticColor(kind), palette.EditorBackground, 4.5);
            Check($"semantic {kind} on active line", theme.SemanticColor(kind),
                palette.ActiveLineBackground, 4.5);
            Check($"preview semantic {kind}", theme.SemanticColor(kind),
                palette.PreviewBackground, 4.5);
        }
        return issues;

        void Check(string role, ThemeColor foreground, ThemeColor background, double minimum)
        {
            var ratio = foreground.ContrastRatio(background);
            if (ratio < minimum) issues.Add(new ThemeContrastIssue(role, ratio, minimum));
        }
    }
}
