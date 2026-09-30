using Mote.Themes;

namespace Mote.Tests;

/// <summary>Exercises pure override parsing, complete-palette validation and effective identity.</summary>
public sealed class ThemeOverrideCompositionTests
{
    [Fact]
    public void Empty_map_preserves_all_bundled_effective_values()
    {
        foreach (var theme in ThemePolicies.All)
        {
            var result = ThemeComposer.Compose(theme, ThemeOverrideData.Empty);
            Assert.True(result.IsValid);
            Assert.Equal(ThemeEffectiveValues.Capture(theme), ThemeEffectiveValues.Capture(result.Theme));
            Assert.Equal(theme.Id, result.Theme.Id);
        }
    }

    [Theory]
    [InlineData("EDITOR.foreground", "#FFFFFF", "CONFIG_THEME_ROLE")]
    [InlineData("semantic.unknown", "#FFFFFF", "CONFIG_THEME_ROLE")]
    [InlineData("editor.foreground", "#fff", "CONFIG_THEME_COLOR")]
    [InlineData("editor.foreground", "#FFFFFFFF", "CONFIG_THEME_COLOR")]
    [InlineData("editor.foreground", "# FF000", "CONFIG_THEME_COLOR")]
    [InlineData("editor.foreground", "#FF00 0", "CONFIG_THEME_COLOR")]
    [InlineData("editor.foreground", "white", "CONFIG_THEME_COLOR")]
    [InlineData("editor.foreground", "#ＦＦ0000", "CONFIG_THEME_COLOR")]
    public void Invalid_entry_rejects_whole_map(string role, string value, string code)
    {
        Assert.False(ThemeOverrideData.TryCreate(
            [new("window.background", "#111111"), new(role, value)], out var data, out var issues));
        Assert.Same(ThemeOverrideData.Empty, data);
        Assert.Contains(issues, issue => issue.Code == code);
        Assert.False(data.TryGetColor(ThemeColorRole.WindowBackground, out _));
    }

    [Fact]
    public void Duplicates_reject_even_after_an_invalid_first_color()
    {
        Assert.False(ThemeOverrideData.TryCreate(
            [new("semantic.key", "bad"), new("semantic.key", "#FFFFFF")], out var data, out var issues));
        Assert.Same(ThemeOverrideData.Empty, data);
        Assert.Equal(["CONFIG_THEME_COLOR", "CONFIG_THEME_ROLE"], issues.Select(issue => issue.Code));
        Assert.Contains("Duplicate", issues[1].Message);
    }

    [Fact]
    public void Validation_stops_at_entry_limit_and_bounds_diagnostics()
    {
        var entries = Enumerable.Range(0, 100).Select(index => new KeyValuePair<string, string>($"bad{index}", "bad"));
        Assert.False(ThemeOverrideData.TryCreate(entries, out _, out var issues));
        Assert.Equal(29, issues.Count);
        Assert.Equal("CONFIG_THEME_LIMIT", issues[^1].Code);
    }

    [Fact]
    public void Map_is_defensively_copied_and_semantic_aliases_inherit()
    {
        var entries = new List<KeyValuePair<string, string>> { new("semantic.key", "#ffffff") };
        Assert.True(ThemeOverrideData.TryCreate(entries, out var data, out var issues));
        Assert.Empty(issues);
        entries[0] = new("semantic.key", "#000000");
        var original = ThemePolicies.Get(ThemePolicies.DarkId);
        var result = ThemeComposer.Compose(original, data);
        Assert.True(result.IsValid);
        Assert.Equal(ThemeColor.FromHex("#FFFFFF"), result.Theme.SemanticColor("heading"));
        Assert.Equal(original.SemanticColor("string"), result.Theme.SemanticColor("code"));
        Assert.Equal(original.Palette, result.Theme.Palette);
        Assert.Equal(original.Typography, result.Theme.Typography);
        Assert.Equal(original.Spacing, result.Theme.Spacing);
        Assert.Equal(original.Palette.EditorForeground, result.Theme.SemanticColor("unknown"));
    }

    [Fact]
    public void Contrast_failure_rejects_candidate_and_reports_ratio()
    {
        var original = ThemePolicies.Get(ThemePolicies.DarkId);
        Assert.True(ThemeOverrideData.TryCreate([new("editor.foreground", original.Palette.EditorBackground.ToHex())],
            out var data, out _));
        var result = ThemeComposer.Compose(original, data);
        Assert.False(result.IsValid);
        Assert.Same(original, result.Theme);
        Assert.Contains(result.Issues, issue => issue.Code == "CONFIG_THEME_CONTRAST" && issue.Ratio == 1 && issue.Minimum == 4.5);
    }

    [Fact]
    public void Semantic_override_is_checked_on_preview_and_active_line_surfaces()
    {
        var original = ThemePolicies.Get(ThemePolicies.DarkId);
        Assert.True(ThemeOverrideData.TryCreate([new("semantic.link", original.Palette.PreviewBackground.ToHex())],
            out var data, out _));
        var result = ThemeComposer.Compose(original, data);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Role == "preview semantic link" && issue.Ratio == 1);
    }

    [Fact]
    public void Value_identity_ignores_map_order_and_hex_case_but_not_same_id_color_changes()
    {
        var original = ThemePolicies.Get(ThemePolicies.DarkId);
        Assert.True(ThemeOverrideData.TryCreate([new("semantic.key", "#FFFFFF"), new("window.background", "#111111")], out var a, out _));
        Assert.True(ThemeOverrideData.TryCreate([new("window.background", "#111111"), new("semantic.key", "#ffffff")], out var b, out _));
        var first = ThemeComposer.Compose(original, a);
        var second = ThemeComposer.Compose(original, b);
        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.Equal(first.Theme.Id, original.Id);
        Assert.Equal(ThemeEffectiveValues.Capture(first.Theme), ThemeEffectiveValues.Capture(second.Theme));
        Assert.NotEqual(ThemeEffectiveValues.Capture(original), ThemeEffectiveValues.Capture(first.Theme));
    }

    [Fact]
    public void Closed_schema_contains_exactly_28_unique_roles()
    {
        string[] keys = [
            "window.background", "panel.background", "editor.background", "editor.foreground",
            "preview.background", "preview.foreground", "text.muted", "gutter.foreground",
            "editor.activeLineBackground", "selection.background", "selection.foreground", "editor.cursor",
            "border", "accent", "diagnostic.error", "diagnostic.warning", "diagnostic.info", "diagnostic.success",
            "control.background", "control.foreground", "semantic.key", "semantic.string", "semantic.number",
            "semantic.keyword", "semantic.comment", "semantic.marker", "semantic.link", "semantic.error"];
        Assert.Equal(ThemeOverrideData.MaximumEntries, keys.Length);
        var roles = keys.Select(key =>
        {
            Assert.True(ThemeOverrideData.TryParseRole(key, out var role));
            return role;
        }).ToArray();
        Assert.Equal(28, roles.Distinct().Count());
        Assert.Equal(Enum.GetValues<ThemeColorRole>(), roles);
        Assert.True(ThemeOverrideData.TryCreate(keys.Select(key => new KeyValuePair<string, string>(key, "#Aa11FF")),
            out var data, out _));
        foreach (var role in roles)
        {
            Assert.True(data.TryGetColor(role, out var value));
            Assert.Equal(ThemeColor.FromHex("#AA11FF"), value);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#00000G")]
    [InlineData(" #000000")]
    [InlineData("#000000 ")]
    public void Strict_nonthrowing_color_parser_rejects_invalid_input(string? value)
    {
        Assert.False(ThemeColor.TryParse(value, out var color));
        Assert.Equal(default, color);
    }

    [Fact]
    public void Invalid_typed_role_does_not_index_internal_array()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ThemeOverrideData.Empty.TryGetColor((ThemeColorRole)99, out _));
    }
}
