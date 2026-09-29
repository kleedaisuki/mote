namespace Mote.Themes;

/// <summary>Compile-time registry for the three bundled, original color policies.</summary>
/// <remarks>
/// <c>system</c> is a configuration preference resolved by the composition root,
/// not a palette with mutable OS state. This keeps rendering deterministic and AOT-safe.
/// </remarks>
public static class ThemePolicies
{
    /// <summary>Configuration ID for the default dark palette.</summary>
    public const string DarkId = "mote-dark";
    /// <summary>Configuration ID for the light palette.</summary>
    public const string LightId = "mote-light";
    /// <summary>Configuration ID for the dark high-contrast palette.</summary>
    public const string HighContrastDarkId = "mote-high-contrast-dark";
    /// <summary>Config preference that follows OS light/dark appearance.</summary>
    public const string SystemId = "system";
    /// <summary>Fallback palette ID when OS appearance is unknown.</summary>
    public const string DefaultId = DarkId;

    private static readonly ThemeTypography Typography = new(
        "Cascadia Code,JetBrains Mono,Consolas,Menlo,monospace",
        "Segoe UI,Inter,San Francisco,Arial,sans-serif", 13, 12, 1.45);
    private static readonly ThemeSpacing Spacing = new(4, 12, 6, 16, 8);

    private static readonly IThemePolicy Dark = new ThemePolicy(
        DarkId, "mote Dark", true,
        new ThemePalette
        {
            WindowBackground = C("#202124"), PanelBackground = C("#27292D"),
            EditorBackground = C("#1F2023"), EditorForeground = C("#D8DADF"),
            PreviewBackground = C("#27292D"), PreviewForeground = C("#E1E3E7"),
            MutedForeground = C("#ADB3BC"), GutterForeground = C("#A2A8B1"),
            ActiveLineBackground = C("#303238"), SelectionBackground = C("#355A85"),
            SelectionForeground = C("#FFFFFF"), Cursor = C("#F0F2F5"),
            Border = C("#737983"), Accent = C("#8DB9ED"),
            Error = C("#F29A9A"), Warning = C("#E6C37A"),
            Info = C("#8DB9ED"), Success = C("#A4CD9B"),
            ControlBackground = C("#383B42"), ControlForeground = C("#F0F1F3")
        },
        new ThemeSemanticColors(
            Key: C("#9CC6E8"), String: C("#D9A58A"), Number: C("#B6D29E"),
            Keyword: C("#C8A8E7"), Comment: C("#A3BA98"), Marker: C("#B9BEC7"),
            Link: C("#81C8E6"), Error: C("#F29A9A")));

    private static readonly IThemePolicy Light = new ThemePolicy(
        LightId, "mote Light", false,
        new ThemePalette
        {
            WindowBackground = C("#F1F2F4"), PanelBackground = C("#F6F7F9"),
            EditorBackground = C("#FFFFFF"), EditorForeground = C("#26282E"),
            PreviewBackground = C("#F6F7F9"), PreviewForeground = C("#26282E"),
            MutedForeground = C("#515863"), GutterForeground = C("#5D6570"),
            ActiveLineBackground = C("#EEF3FA"), SelectionBackground = C("#BFD8F6"),
            SelectionForeground = C("#172339"), Cursor = C("#1C2737"),
            Border = C("#8A9099"), Accent = C("#215FAD"),
            Error = C("#B32633"), Warning = C("#785000"),
            Info = C("#255F9E"), Success = C("#296B39"),
            ControlBackground = C("#E1E5EB"), ControlForeground = C("#202630")
        },
        new ThemeSemanticColors(
            Key: C("#245E9B"), String: C("#9D5137"), Number: C("#386A3B"),
            Keyword: C("#66469B"), Comment: C("#536A4D"), Marker: C("#5D6370"),
            Link: C("#0B698F"), Error: C("#B32633")));

    private static readonly IThemePolicy HighContrastDark = new ThemePolicy(
        HighContrastDarkId, "mote High Contrast Dark", true,
        new ThemePalette
        {
            WindowBackground = C("#000000"), PanelBackground = C("#080808"),
            EditorBackground = C("#000000"), EditorForeground = C("#FFFFFF"),
            PreviewBackground = C("#080808"), PreviewForeground = C("#FFFFFF"),
            MutedForeground = C("#D0D0D0"), GutterForeground = C("#C8C8C8"),
            ActiveLineBackground = C("#1B1B1B"), SelectionBackground = C("#174F88"),
            SelectionForeground = C("#FFFFFF"), Cursor = C("#FFFFFF"),
            Border = C("#BBBBBB"), Accent = C("#82CAFF"),
            Error = C("#FF9494"), Warning = C("#FFD67A"),
            Info = C("#82CAFF"), Success = C("#B6EAA7"),
            ControlBackground = C("#202020"), ControlForeground = C("#FFFFFF")
        },
        new ThemeSemanticColors(
            Key: C("#A9D4FF"), String: C("#FFBE9C"), Number: C("#C6F0A7"),
            Keyword: C("#DEC1FF"), Comment: C("#B5D5AD"), Marker: C("#D5D5D5"),
            Link: C("#9DE4FF"), Error: C("#FF9494")));

    private static readonly IReadOnlyList<IThemePolicy> Policies =
        Array.AsReadOnly([Dark, Light, HighContrastDark]);

    /// <summary>The fixed bundled policies, with no runtime discovery or loading.</summary>
    public static IReadOnlyList<IThemePolicy> All => Policies;

    /// <summary>Whether a configuration value names a supported policy or system preference.</summary>
    public static bool IsKnownId(string? id) =>
        string.Equals(id, DarkId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(id, LightId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(id, HighContrastDarkId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(id, SystemId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a user preference. <c>system</c>, null, empty and unknown values follow
    /// <paramref name="prefersDark"/>; a caller can separately warn about unknown IDs.
    /// </summary>
    /// <example><c>var theme = ThemePolicies.Resolve(config.ThemeId, osPrefersDark);</c></example>
    public static IThemePolicy Resolve(string? configuredId, bool prefersDark)
    {
        if (string.Equals(configuredId, DarkId, StringComparison.OrdinalIgnoreCase)) return Dark;
        if (string.Equals(configuredId, LightId, StringComparison.OrdinalIgnoreCase)) return Light;
        if (string.Equals(configuredId, HighContrastDarkId, StringComparison.OrdinalIgnoreCase)) return HighContrastDark;
        return prefersDark ? Dark : Light;
    }

    /// <summary>Gets a concrete ID; unknown IDs use the default dark palette.</summary>
    public static IThemePolicy Get(string? id) => Resolve(id, prefersDark: true);

    private static ThemeColor C(string value) => ThemeColor.FromHex(value);

    private sealed class ThemePolicy(
        string id, string displayName, bool isDark, ThemePalette palette,
        ThemeSemanticColors semanticColors) : IThemePolicy
    {
        public string Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public bool IsDark { get; } = isDark;
        public ThemePalette Palette { get; } = palette;
        public ThemeTypography Typography { get; } = ThemePolicies.Typography;
        public ThemeSpacing Spacing { get; } = ThemePolicies.Spacing;

        public ThemeColor SemanticColor(string kind)
        {
            ArgumentNullException.ThrowIfNull(kind);
            return kind switch
            {
                "key" or "heading" => semanticColors.Key,
                "string" or "fenced-code" or "code" => semanticColors.String,
                "number" => semanticColors.Number,
                "boolean" or "null" or "keyword" or "emphasis" => semanticColors.Keyword,
                "comment" => semanticColors.Comment,
                "heading-marker" or "list-marker" or "sequence-marker" or "fence" => semanticColors.Marker,
                "link" => semanticColors.Link,
                "error" or "invalid" => semanticColors.Error,
                _ => Palette.EditorForeground
            };
        }
    }

    private sealed record ThemeSemanticColors(
        ThemeColor Key, ThemeColor String, ThemeColor Number, ThemeColor Keyword,
        ThemeColor Comment, ThemeColor Marker, ThemeColor Link, ThemeColor Error);
}
