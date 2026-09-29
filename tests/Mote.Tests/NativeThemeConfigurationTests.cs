using Mote.Configuration;
using Mote.Native;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Checks composition-root validation of user-selected theme policy IDs.</summary>
public sealed class NativeThemeConfigurationTests
{
    /// <summary>Every shipped policy and the system preference remain warning-free.</summary>
    [Theory]
    [InlineData(ThemePolicies.DarkId)]
    [InlineData(ThemePolicies.LightId)]
    [InlineData(ThemePolicies.HighContrastDarkId)]
    [InlineData(ThemePolicies.SystemId)]
    [InlineData("MOTE-LIGHT")]
    public void Known_theme_ids_preserve_configuration_without_a_warning(string id)
    {
        var config = Configuration(id);

        var checkedConfig = Program.ValidateThemeId(config, out var warning);

        Assert.Same(config, checkedConfig);
        Assert.Null(warning);
        Assert.Empty(checkedConfig.Diagnostics);
    }

    /// <summary>A syntactically valid typo is visible and retains a safe OS-theme fallback.</summary>
    [Fact]
    public void Unknown_theme_typo_adds_actionable_nonfatal_diagnostic()
    {
        using var temp = new RepoTemp();
        var home = temp.File("config-home");
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"),
            "[appearance]\ntheme = 'mote-drak'\n[paths]\ncache = 'my-cache'\n");
        var loaded = MoteConfigLoader.Load(new MoteConfigLoadOptions
        {
            UserHomeDirectory = temp.Path,
            MoteHomeOverride = home,
            UseEnvironmentOverride = false
        });
        Assert.Empty(loaded.Diagnostics);

        var checkedConfig = Program.ValidateThemeId(loaded, out var warning);

        Assert.NotNull(warning);
        Assert.Equal("CONFIG_THEME", warning.Code);
        Assert.Contains("mote-drak", warning.Message, StringComparison.Ordinal);
        Assert.Contains("mote-dark", warning.Message, StringComparison.Ordinal);
        Assert.Contains("mote-light", warning.Message, StringComparison.Ordinal);
        Assert.Contains("system", warning.Message, StringComparison.Ordinal);
        Assert.Equal("mote-drak", checkedConfig.ThemeId);
        Assert.Equal(Path.Combine(home, "my-cache"), checkedConfig.CacheDirectory);
        Assert.Single(checkedConfig.Diagnostics);
        Assert.Same(warning, checkedConfig.Diagnostics[0]);
        Assert.Equal(ThemePolicies.DarkId, ThemePolicies.Resolve(checkedConfig.ThemeId, prefersDark: true).Id);
        Assert.Equal(ThemePolicies.LightId, ThemePolicies.Resolve(checkedConfig.ThemeId, prefersDark: false).Id);
    }

    /// <summary>Theme validation appends rather than erasing unrelated config warnings.</summary>
    [Fact]
    public void Unknown_theme_preserves_other_configuration_diagnostics()
    {
        var previous = new ConfigDiagnostic("CONFIG_UNKNOWN", "Another setting is unknown.");
        var config = Configuration("mote-drak") with { Diagnostics = [previous] };

        var checkedConfig = Program.ValidateThemeId(config, out var warning);

        Assert.NotNull(warning);
        Assert.Equal(2, checkedConfig.Diagnostics.Count);
        Assert.Same(previous, checkedConfig.Diagnostics[0]);
        Assert.Same(warning, checkedConfig.Diagnostics[1]);
    }

    /// <summary>Builds one pure config object without consulting the process environment.</summary>
    private static MoteConfiguration Configuration(string themeId) => new()
    {
        HomeDirectory = "/profile/.mote",
        ConfigPath = "/profile/.mote/config.toml",
        CacheDirectory = "/profile/.mote/cache",
        DataDirectory = "/profile/.mote/data",
        TraceDirectory = "/profile/.mote/traces",
        ThemeId = themeId,
        TraceEnabled = false,
        Diagnostics = []
    };
}
