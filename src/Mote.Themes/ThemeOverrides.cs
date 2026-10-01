namespace Mote.Themes;

/// <summary>The closed, case-sensitive vocabulary of configurable opaque colors.</summary>
public enum ThemeColorRole
{
    /// <summary>The <c>window.background</c> color.</summary>
    WindowBackground,
    /// <summary>The <c>panel.background</c> color.</summary>
    PanelBackground,
    /// <summary>The <c>editor.background</c> color.</summary>
    EditorBackground,
    /// <summary>The <c>editor.foreground</c> color.</summary>
    EditorForeground,
    /// <summary>The <c>preview.background</c> color.</summary>
    PreviewBackground,
    /// <summary>The <c>preview.foreground</c> color.</summary>
    PreviewForeground,
    /// <summary>The <c>text.muted</c> color.</summary>
    MutedForeground,
    /// <summary>The <c>gutter.foreground</c> color.</summary>
    GutterForeground,
    /// <summary>The <c>editor.activeLineBackground</c> color.</summary>
    ActiveLineBackground,
    /// <summary>The <c>selection.background</c> color.</summary>
    SelectionBackground,
    /// <summary>The <c>selection.foreground</c> color.</summary>
    SelectionForeground,
    /// <summary>The <c>editor.cursor</c> color.</summary>
    Cursor,
    /// <summary>The <c>border</c> color.</summary>
    Border,
    /// <summary>The <c>accent</c> color.</summary>
    Accent,
    /// <summary>The <c>diagnostic.error</c> color.</summary>
    Error,
    /// <summary>The <c>diagnostic.warning</c> color.</summary>
    Warning,
    /// <summary>The <c>diagnostic.info</c> color.</summary>
    Info,
    /// <summary>The <c>diagnostic.success</c> color.</summary>
    Success,
    /// <summary>The <c>control.background</c> color.</summary>
    ControlBackground,
    /// <summary>The <c>control.foreground</c> color.</summary>
    ControlForeground,
    /// <summary>The <c>semantic.key</c> color.</summary>
    SemanticKey,
    /// <summary>The <c>semantic.string</c> color.</summary>
    SemanticString,
    /// <summary>The <c>semantic.number</c> color.</summary>
    SemanticNumber,
    /// <summary>The <c>semantic.keyword</c> color.</summary>
    SemanticKeyword,
    /// <summary>The <c>semantic.comment</c> color.</summary>
    SemanticComment,
    /// <summary>The <c>semantic.marker</c> color.</summary>
    SemanticMarker,
    /// <summary>The <c>semantic.link</c> color.</summary>
    SemanticLink,
    /// <summary>The <c>semantic.error</c> color.</summary>
    SemanticError,
}

/// <summary>A rejected override entry or candidate; contrast failures include measured ratios.</summary>
public sealed record ThemeOverrideIssue(string Code, string Role, string Message,
    double? Ratio = null, double? Minimum = null);

/// <summary>A defensive immutable sparse map; no caller-owned collection is retained.</summary>
/// <remarks>Role keys are case-sensitive. All entries form one failure unit.</remarks>
public sealed class ThemeOverrideData
{
    /// <summary>Maximum entries in the closed schema.</summary>
    public const int MaximumEntries = 28;
    /// <summary>Maximum ASCII characters in a role key.</summary>
    public const int MaximumKeyLength = 64;
    private readonly ThemeColor?[] _colors;

    private ThemeOverrideData(ThemeColor?[] colors) => _colors = colors;

    /// <summary>The empty map inherits all base-policy values.</summary>
    public static ThemeOverrideData Empty { get; } = new(new ThemeColor?[MaximumEntries]);

    /// <summary>Looks up a typed override without string dispatch in rendering loops.</summary>
    public bool TryGetColor(ThemeColorRole role, out ThemeColor color)
    {
        var index = (int)role;
        if ((uint)index >= MaximumEntries) throw new ArgumentOutOfRangeException(nameof(role));
        var value = _colors[index];
        color = value.GetValueOrDefault();
        return value.HasValue;
    }

    /// <summary>Validates a complete map; a rejected map yields Empty, never partial values.</summary>
    /// <remarks>Issues follow input order and are bounded by the entry cap. Enumeration stops at entry 29.</remarks>
    /// <example><code>
    /// ThemeOverrideData.TryCreate([new("editor.foreground", "#FFFFFF")], out var data, out var issues);
    /// var result = ThemeComposer.Compose(ThemePolicies.Get("mote-dark"), data);
    /// </code></example>
    public static bool TryCreate(IEnumerable<KeyValuePair<string, string>> entries,
        out ThemeOverrideData data, out IReadOnlyList<ThemeOverrideIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var colors = new ThemeColor?[MaximumEntries];
        var seen = new bool[MaximumEntries];
        var failures = new List<ThemeOverrideIssue>();
        var count = 0;
        foreach (var entry in entries)
        {
            if (++count > MaximumEntries)
            {
                failures.Add(new("CONFIG_THEME_LIMIT", "", "The theme map exceeds 28 entries."));
                break;
            }
            if (!TryParseRole(entry.Key, out var role))
            {
                // Invalid names can originate outside the bounded configuration parser.
                var name = entry.Key is null ? "" : entry.Key[..Math.Min(entry.Key.Length, MaximumKeyLength)];
                failures.Add(new("CONFIG_THEME_ROLE", name, "Unknown or invalid case-sensitive theme role."));
                continue;
            }
            if (seen[(int)role])
            {
                failures.Add(new("CONFIG_THEME_ROLE", entry.Key!, "Duplicate logical theme role."));
                continue;
            }
            seen[(int)role] = true;
            if (!ThemeColor.TryParse(entry.Value, out var color))
            {
                failures.Add(new("CONFIG_THEME_COLOR", entry.Key!, "Theme colors require opaque #RRGGBB syntax."));
                continue;
            }
            colors[(int)role] = color;
        }
        issues = failures.AsReadOnly();
        data = failures.Count == 0 ? new(colors) : Empty;
        return failures.Count == 0;
    }

    /// <summary>Parses only the documented exact role keys, without normalization or reflection.</summary>
    public static bool TryParseRole(string? key, out ThemeColorRole role)
    {
        var index = key switch
        {
            "window.background" => (int)ThemeColorRole.WindowBackground,
            "panel.background" => (int)ThemeColorRole.PanelBackground,
            "editor.background" => (int)ThemeColorRole.EditorBackground,
            "editor.foreground" => (int)ThemeColorRole.EditorForeground,
            "preview.background" => (int)ThemeColorRole.PreviewBackground,
            "preview.foreground" => (int)ThemeColorRole.PreviewForeground,
            "text.muted" => (int)ThemeColorRole.MutedForeground,
            "gutter.foreground" => (int)ThemeColorRole.GutterForeground,
            "editor.activeLineBackground" => (int)ThemeColorRole.ActiveLineBackground,
            "selection.background" => (int)ThemeColorRole.SelectionBackground,
            "selection.foreground" => (int)ThemeColorRole.SelectionForeground,
            "editor.cursor" => (int)ThemeColorRole.Cursor,
            "border" => (int)ThemeColorRole.Border,
            "accent" => (int)ThemeColorRole.Accent,
            "diagnostic.error" => (int)ThemeColorRole.Error,
            "diagnostic.warning" => (int)ThemeColorRole.Warning,
            "diagnostic.info" => (int)ThemeColorRole.Info,
            "diagnostic.success" => (int)ThemeColorRole.Success,
            "control.background" => (int)ThemeColorRole.ControlBackground,
            "control.foreground" => (int)ThemeColorRole.ControlForeground,
            "semantic.key" => (int)ThemeColorRole.SemanticKey,
            "semantic.string" => (int)ThemeColorRole.SemanticString,
            "semantic.number" => (int)ThemeColorRole.SemanticNumber,
            "semantic.keyword" => (int)ThemeColorRole.SemanticKeyword,
            "semantic.comment" => (int)ThemeColorRole.SemanticComment,
            "semantic.marker" => (int)ThemeColorRole.SemanticMarker,
            "semantic.link" => (int)ThemeColorRole.SemanticLink,
            "semantic.error" => (int)ThemeColorRole.SemanticError,
            _ => -1
        };
        role = (ThemeColorRole)index;
        return index >= 0;
    }
}

/// <summary>Composition outcome; rejected candidates retain the unchanged base policy.</summary>
public sealed record ThemeCompositionResult(IThemePolicy Theme, IReadOnlyList<ThemeOverrideIssue> Issues)
{
    /// <summary>Whether the whole candidate passed every declared contrast edge.</summary>
    public bool IsValid => Issues.Count == 0;
}

/// <summary>Pure AOT-safe composition of compiled policies and data-only color overrides.</summary>
public static class ThemeComposer
{
    /// <summary>Inherits unspecified roles and validates the complete resulting palette.</summary>
    public static ThemeCompositionResult Compose(IThemePolicy basePolicy, ThemeOverrideData data)
    {
        ArgumentNullException.ThrowIfNull(basePolicy);
        ArgumentNullException.ThrowIfNull(data);
        var candidate = new ComposedThemePolicy(basePolicy, data);
        var failures = ThemeContrastValidator.Validate(candidate);
        if (failures.Count == 0) return new(candidate, Array.Empty<ThemeOverrideIssue>());
        var issues = failures.Take(32).Select(issue => new ThemeOverrideIssue(
            "CONFIG_THEME_CONTRAST", issue.Role, "Composed theme contrast is below the required minimum.",
            issue.Ratio, issue.Minimum)).ToList();
        if (failures.Count > 32) issues.Add(new("CONFIG_THEME_CONTRAST", "",
            $"{failures.Count - 32} additional contrast issues omitted."));
        return new(basePolicy, issues.AsReadOnly());
    }

    private sealed class ComposedThemePolicy : IThemePolicy
    {
        private readonly IThemePolicy _base;
        private readonly ThemeColor[] _semantic;

        internal ComposedThemePolicy(IThemePolicy basePolicy, ThemeOverrideData data)
        {
            _base = basePolicy;
            Palette = basePolicy.Palette with
            {
                WindowBackground = Color(ThemeColorRole.WindowBackground, basePolicy.Palette.WindowBackground),
                PanelBackground = Color(ThemeColorRole.PanelBackground, basePolicy.Palette.PanelBackground),
                EditorBackground = Color(ThemeColorRole.EditorBackground, basePolicy.Palette.EditorBackground),
                EditorForeground = Color(ThemeColorRole.EditorForeground, basePolicy.Palette.EditorForeground),
                PreviewBackground = Color(ThemeColorRole.PreviewBackground, basePolicy.Palette.PreviewBackground),
                PreviewForeground = Color(ThemeColorRole.PreviewForeground, basePolicy.Palette.PreviewForeground),
                MutedForeground = Color(ThemeColorRole.MutedForeground, basePolicy.Palette.MutedForeground),
                GutterForeground = Color(ThemeColorRole.GutterForeground, basePolicy.Palette.GutterForeground),
                ActiveLineBackground = Color(ThemeColorRole.ActiveLineBackground, basePolicy.Palette.ActiveLineBackground),
                SelectionBackground = Color(ThemeColorRole.SelectionBackground, basePolicy.Palette.SelectionBackground),
                SelectionForeground = Color(ThemeColorRole.SelectionForeground, basePolicy.Palette.SelectionForeground),
                Cursor = Color(ThemeColorRole.Cursor, basePolicy.Palette.Cursor),
                Border = Color(ThemeColorRole.Border, basePolicy.Palette.Border),
                Accent = Color(ThemeColorRole.Accent, basePolicy.Palette.Accent),
                Error = Color(ThemeColorRole.Error, basePolicy.Palette.Error),
                Warning = Color(ThemeColorRole.Warning, basePolicy.Palette.Warning),
                Info = Color(ThemeColorRole.Info, basePolicy.Palette.Info),
                Success = Color(ThemeColorRole.Success, basePolicy.Palette.Success),
                ControlBackground = Color(ThemeColorRole.ControlBackground, basePolicy.Palette.ControlBackground),
                ControlForeground = Color(ThemeColorRole.ControlForeground, basePolicy.Palette.ControlForeground),
            };
            _semantic = [
                Color(ThemeColorRole.SemanticKey, basePolicy.SemanticColor("key")),
                Color(ThemeColorRole.SemanticString, basePolicy.SemanticColor("string")),
                Color(ThemeColorRole.SemanticNumber, basePolicy.SemanticColor("number")),
                Color(ThemeColorRole.SemanticKeyword, basePolicy.SemanticColor("keyword")),
                Color(ThemeColorRole.SemanticComment, basePolicy.SemanticColor("comment")),
                Color(ThemeColorRole.SemanticMarker, basePolicy.SemanticColor("heading-marker")),
                Color(ThemeColorRole.SemanticLink, basePolicy.SemanticColor("link")),
                Color(ThemeColorRole.SemanticError, basePolicy.SemanticColor("error")),
            ];

            ThemeColor Color(ThemeColorRole role, ThemeColor inherited) =>
                data.TryGetColor(role, out var value) ? value : inherited;
        }

        public string Id => _base.Id;
        public string DisplayName => _base.DisplayName;
        public bool IsDark => _base.IsDark;
        public ThemePalette Palette { get; }
        public ThemeTypography Typography => _base.Typography;
        public ThemeSpacing Spacing => _base.Spacing;

        public ThemeColor SemanticColor(string kind)
        {
            ArgumentNullException.ThrowIfNull(kind);
            return kind switch
            {
                "key" or "heading" => _semantic[0],
                "string" or "fenced-code" or "code" => _semantic[1],
                "number" => _semantic[2],
                "boolean" or "null" or "keyword" or "emphasis" => _semantic[3],
                "comment" => _semantic[4],
                "heading-marker" or "list-marker" or "sequence-marker" or "fence" => _semantic[5],
                "link" => _semantic[6],
                "error" or "invalid" => _semantic[7],
                _ => Palette.EditorForeground
            };
        }
    }
}

/// <summary>Immutable value identity for native installation, independent of base ID or map order.</summary>
/// <remarks>Callers own monotonic revision counters; equality includes every effective paint/metric value.</remarks>
public sealed record ThemeEffectiveValues(
    bool IsDark, ThemePalette Palette, ThemeTypography Typography, ThemeSpacing Spacing,
    ThemeColor Key, ThemeColor String, ThemeColor Number, ThemeColor Keyword,
    ThemeColor Comment, ThemeColor Marker, ThemeColor Link, ThemeColor Error)
{
    /// <summary>Captures the finite policy values without process-random hashes or mutable global state.</summary>
    public static ThemeEffectiveValues Capture(IThemePolicy theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return new(theme.IsDark, theme.Palette, theme.Typography, theme.Spacing,
            theme.SemanticColor("key"), theme.SemanticColor("string"), theme.SemanticColor("number"),
            theme.SemanticColor("keyword"), theme.SemanticColor("comment"), theme.SemanticColor("heading-marker"),
            theme.SemanticColor("link"), theme.SemanticColor("error"));
    }
}
