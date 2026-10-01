using Mote.Themes;

namespace Mote.Themes.Tests;

/// <summary>Contracts that prevent palette regressions before a UI adapter ships.</summary>
public sealed class ThemePolicyTests
{
    /// <summary>Each statically linked palette has an independent stable ID.</summary>
    [Fact]
    public void RegistryHasThreeUniqueConcreteIds()
    {
        Assert.Equal([ThemePolicies.DarkId, ThemePolicies.LightId,
            ThemePolicies.HighContrastDarkId], ThemePolicies.All.Select(theme => theme.Id));
        Assert.Equal(ThemePolicies.All.Count,
            ThemePolicies.All.Select(theme => theme.Id).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>System preference is resolved outside the immutable palette policies.</summary>
    [Theory]
    [InlineData(null, true, ThemePolicies.DarkId)]
    [InlineData(null, false, ThemePolicies.LightId)]
    [InlineData("system", true, ThemePolicies.DarkId)]
    [InlineData("system", false, ThemePolicies.LightId)]
    [InlineData("unknown", false, ThemePolicies.LightId)]
    [InlineData("MOTE-DARK", false, ThemePolicies.DarkId)]
    [InlineData("mote-high-contrast-dark", false, ThemePolicies.HighContrastDarkId)]
    public void ResolveHonorsConcreteAndSystemPreferences(string? id, bool prefersDark, string expected) =>
        Assert.Equal(expected, ThemePolicies.Resolve(id, prefersDark).Id);

    /// <summary>Config diagnostics can distinguish typo fallback from an explicit system preference.</summary>
    [Fact]
    public void KnownIdRecognitionDistinguishesUnknownPreference()
    {
        Assert.All(ThemePolicies.All, theme => Assert.True(ThemePolicies.IsKnownId(theme.Id)));
        Assert.True(ThemePolicies.IsKnownId("SYSTEM"));
        Assert.False(ThemePolicies.IsKnownId("mote-drak"));
        Assert.False(ThemePolicies.IsKnownId(null));
    }

    /// <summary>Every declared text and essential non-text pair meets its contrast floor.</summary>
    [Fact]
    public void BundledPalettesMeetContrastContracts()
    {
        foreach (var theme in ThemePolicies.All)
        {
            var issues = ThemeContrastValidator.Validate(theme);
            Assert.True(issues.Count == 0, $"{theme.Id}: " + string.Join(", ",
                issues.Select(issue => $"{issue.Role}={issue.Ratio:F2} (needs {issue.Minimum:F1})")));
        }
    }

    /// <summary>Unknown future semantic roles remain legible without a special-case branch.</summary>
    [Fact]
    public void UnknownSemanticRoleUsesEditorForeground()
    {
        foreach (var theme in ThemePolicies.All)
            Assert.Equal(theme.Palette.EditorForeground, theme.SemanticColor("future-role"));
    }

    /// <summary>The validator must catch the exact dark-on-dark control failure seen in UI styling.</summary>
    [Fact]
    public void ValidatorRejectsUnreadableControlForeground()
    {
        var good = ThemePolicies.Get(ThemePolicies.DarkId);
        var bad = new BadControlTheme(good);
        Assert.Contains(ThemeContrastValidator.Validate(bad),
            issue => issue.Role == "button and active-tab text");
    }

    /// <summary>Native previews apply semantic colors on a distinct surface, not the editor surface.</summary>
    [Fact]
    public void ValidatorRejectsUnreadablePreviewSemanticColor()
    {
        var good = ThemePolicies.Get(ThemePolicies.DarkId);
        var bad = new BadPreviewTheme(good);
        Assert.Contains(ThemeContrastValidator.Validate(bad),
            issue => issue.Role == "preview semantic key");
    }

    /// <summary>Color serialization is exact and rejects alpha, malformed or ambiguous strings.</summary>
    [Fact]
    public void ColorHexRoundTripsAndRejectsMalformedValues()
    {
        Assert.Equal("#1F20A0", ThemeColor.FromHex("#1f20a0").ToHex());
        Assert.Equal(21d, ThemeColor.FromHex("#000000")
            .ContrastRatio(ThemeColor.FromHex("#FFFFFF")), precision: 8);
        Assert.Throws<FormatException>(() => ThemeColor.FromHex("#FFF"));
        Assert.Throws<FormatException>(() => ThemeColor.FromHex("#AABBCC80"));
        Assert.Throws<FormatException>(() => ThemeColor.FromHex("not-a-color"));
    }

    private sealed class BadControlTheme(IThemePolicy source) : IThemePolicy
    {
        public string Id => "bad-test-theme";
        public string DisplayName => "Bad test theme";
        public bool IsDark => source.IsDark;
        public ThemePalette Palette { get; } = source.Palette with
        {
            ControlForeground = source.Palette.ControlBackground
        };
        public ThemeTypography Typography => source.Typography;
        public ThemeSpacing Spacing => source.Spacing;
        public ThemeColor SemanticColor(string kind) => source.SemanticColor(kind);
    }

    private sealed class BadPreviewTheme(IThemePolicy source) : IThemePolicy
    {
        public string Id => "bad-preview-test-theme";
        public string DisplayName => "Bad preview test theme";
        public bool IsDark => source.IsDark;
        public ThemePalette Palette { get; } = source.Palette with
        {
            PreviewBackground = source.SemanticColor("key")
        };
        public ThemeTypography Typography => source.Typography;
        public ThemeSpacing Spacing => source.Spacing;
        public ThemeColor SemanticColor(string kind) => source.SemanticColor(kind);
    }
}
