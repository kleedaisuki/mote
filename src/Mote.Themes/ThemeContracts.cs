namespace Mote.Themes;

/// <summary>Static, UI-neutral presentation policy selected by a stable configuration ID.</summary>
/// <remarks>
/// A desktop adapter converts these opaque RGB values into platform brushes. Format
/// policies emit semantic token kinds, never colors. Themes have no file or process state.
/// </remarks>
public interface IThemePolicy
{
    /// <summary>Stable ID used by configuration; not localized.</summary>
    string Id { get; }
    /// <summary>Human-readable theme name.</summary>
    string DisplayName { get; }
    /// <summary>Whether this palette is intended for a dark system appearance.</summary>
    bool IsDark { get; }
    /// <summary>Opaque colors for editor and application chrome.</summary>
    ThemePalette Palette { get; }
    /// <summary>Cross-platform font preferences and sizes.</summary>
    ThemeTypography Typography { get; }
    /// <summary>Density tokens in device-independent pixels.</summary>
    ThemeSpacing Spacing { get; }
    /// <summary>Returns the foreground for a format semantic kind or editor foreground if unknown.</summary>
    ThemeColor SemanticColor(string kind);
}

/// <summary>Colors consumed by UI adapters; selection text is explicit, not inherited from syntax.</summary>
public sealed record ThemePalette
{
    /// <summary>Top-level window surface.</summary>
    public required ThemeColor WindowBackground { get; init; }
    /// <summary>Inspector, toolbar and status surface.</summary>
    public required ThemeColor PanelBackground { get; init; }
    /// <summary>Editable text surface.</summary>
    public required ThemeColor EditorBackground { get; init; }
    /// <summary>Default editor text and fallback syntax foreground.</summary>
    public required ThemeColor EditorForeground { get; init; }
    /// <summary>Rendered preview surface.</summary>
    public required ThemeColor PreviewBackground { get; init; }
    /// <summary>Rendered preview text.</summary>
    public required ThemeColor PreviewForeground { get; init; }
    /// <summary>Secondary UI text.</summary>
    public required ThemeColor MutedForeground { get; init; }
    /// <summary>Line numbers and gutter text.</summary>
    public required ThemeColor GutterForeground { get; init; }
    /// <summary>Subtle current-line surface.</summary>
    public required ThemeColor ActiveLineBackground { get; init; }
    /// <summary>Opaque selection surface; foreground must be applied over all syntax colors.</summary>
    public required ThemeColor SelectionBackground { get; init; }
    /// <summary>Selected text color with tested contrast against selection surface.</summary>
    public required ThemeColor SelectionForeground { get; init; }
    /// <summary>Editor insertion caret color.</summary>
    public required ThemeColor Cursor { get; init; }
    /// <summary>Non-text divider and control border color.</summary>
    public required ThemeColor Border { get; init; }
    /// <summary>Primary interactive accent and focus indicator.</summary>
    public required ThemeColor Accent { get; init; }
    /// <summary>Error text, glyphs and diagnostics.</summary>
    public required ThemeColor Error { get; init; }
    /// <summary>Warning text and diagnostics.</summary>
    public required ThemeColor Warning { get; init; }
    /// <summary>Informational text and diagnostics.</summary>
    public required ThemeColor Info { get; init; }
    /// <summary>Success text and status.</summary>
    public required ThemeColor Success { get; init; }
    /// <summary>Toolbar buttons and selected tab surface.</summary>
    public required ThemeColor ControlBackground { get; init; }
    /// <summary>Button and selected tab label color, independent of inherited UI styles.</summary>
    public required ThemeColor ControlForeground { get; init; }
}

/// <summary>Font preferences are fallbacks, never a requirement to install or bundle fonts.</summary>
public sealed record ThemeTypography(
    string EditorFontFamilies,
    string UiFontFamilies,
    double EditorFontSize,
    double UiFontSize,
    double LineHeightMultiplier);

/// <summary>Small density vocabulary so controls and panels follow one coherent rhythm.</summary>
public sealed record ThemeSpacing(
    double Unit,
    double ControlHorizontalPadding,
    double ControlVerticalPadding,
    double PanelPadding,
    double SectionGap);
