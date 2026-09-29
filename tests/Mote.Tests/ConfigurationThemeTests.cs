using Mote.Configuration;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Checks local configuration and compile-time theme policies without desktop UI.</summary>
public sealed class ConfigurationThemeTests
{
    /// <summary>Reading absent settings is side-effect free and uses conventional paths.</summary>
    [Fact]
    public void Missing_config_uses_defaults_without_creating_directories()
    {
        using var temp = new RepoTemp();
        var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
        {
            UserHomeDirectory = temp.Path,
            UseEnvironmentOverride = false
        });
        var home = Path.Combine(temp.Path, ".mote");
        Assert.Equal(home, config.HomeDirectory);
        Assert.Equal(Path.Combine(home, "config.toml"), config.ConfigPath);
        Assert.Equal(Path.Combine(home, "cache"), config.CacheDirectory);
        Assert.Equal(Path.Combine(home, "data"), config.DataDirectory);
        Assert.Equal(Path.Combine(home, "traces"), config.TraceDirectory);
        Assert.Equal(ThemePolicies.DarkId, config.ThemeId);
        Assert.False(config.TraceEnabled);
        Assert.Empty(config.Diagnostics);
        Assert.False(Directory.Exists(home));
    }

    /// <summary>Configured relative paths resolve inside the selected mote root.</summary>
    [Fact]
    public async Task Valid_toml_overlays_theme_paths_and_opt_in_trace()
    {
        using var temp = new RepoTemp();
        var home = temp.File("config-home");
        Directory.CreateDirectory(home);
        await File.WriteAllTextAsync(Path.Combine(home, "config.toml"),
            "[paths]\ncache = 'cache-custom'\ndata = 'data-custom'\ntraces = 'logs'\n" +
            "[appearance]\ntheme = 'mote-light'\n[telemetry]\nenabled = true\n");
        var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
        {
            UserHomeDirectory = temp.Path,
            MoteHomeOverride = home,
            UseEnvironmentOverride = false
        });
        Assert.Empty(config.Diagnostics);
        Assert.Equal(Path.Combine(home, "cache-custom"), config.CacheDirectory);
        Assert.Equal(Path.Combine(home, "data-custom"), config.DataDirectory);
        Assert.Equal(Path.Combine(home, "logs"), config.TraceDirectory);
        Assert.Equal(ThemePolicies.LightId, config.ThemeId);
        Assert.True(config.TraceEnabled);
        Assert.False(Directory.Exists(config.CacheDirectory));
        Assert.False(Directory.Exists(config.TraceDirectory));
    }

    /// <summary>Malformed or unknown settings preserve safe defaults and report why.</summary>
    [Fact]
    public async Task Invalid_config_does_not_replace_defaults_silently()
    {
        using var temp = new RepoTemp();
        var home = temp.File("config-home");
        Directory.CreateDirectory(home);
        await File.WriteAllTextAsync(Path.Combine(home, "config.toml"),
            "[appearance]\ntheme = 42\n[telemetry]\nenabled = 'yes'\n[unknown]\nthing = 1\n");
        var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
        {
            UserHomeDirectory = temp.Path,
            MoteHomeOverride = home,
            UseEnvironmentOverride = false
        });
        Assert.Equal(ThemePolicies.DarkId, config.ThemeId);
        Assert.False(config.TraceEnabled);
        Assert.Contains(config.Diagnostics, d => d.Code == "CONFIG_VALUE");
        Assert.Contains(config.Diagnostics, d => d.Code == "CONFIG_UNKNOWN");
    }

    /// <summary>Non-UTF8 configuration is rejected rather than interpreted under a locale code page.</summary>
    [Fact]
    public async Task Invalid_utf8_config_reports_read_error()
    {
        using var temp = new RepoTemp();
        var home = temp.File("config-home");
        Directory.CreateDirectory(home);
        await File.WriteAllBytesAsync(Path.Combine(home, "config.toml"), [0xFF, 0xFE, 0xFF]);
        var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
        {
            UserHomeDirectory = temp.Path,
            MoteHomeOverride = home,
            UseEnvironmentOverride = false
        });
        Assert.Contains(config.Diagnostics, d => d.Code == "CONFIG_READ");
        Assert.Equal(ThemePolicies.DarkId, config.ThemeId);
    }

    /// <summary>Theme IDs remain fixed and OS preference resolves without dynamic discovery.</summary>
    [Fact]
    public void Theme_registry_resolves_dark_light_and_high_contrast()
    {
        Assert.Equal(3, ThemePolicies.All.Count);
        Assert.Equal(ThemePolicies.DarkId, ThemePolicies.Resolve("system", prefersDark: true).Id);
        Assert.Equal(ThemePolicies.LightId, ThemePolicies.Resolve("system", prefersDark: false).Id);
        Assert.Equal(ThemePolicies.HighContrastDarkId,
            ThemePolicies.Get(ThemePolicies.HighContrastDarkId).Id);
        Assert.Equal(ThemePolicies.LightId, ThemePolicies.Resolve("unknown", prefersDark: false).Id);
    }

    /// <summary>Built-in palette pairs meet their declared text and chrome contrast minima.</summary>
    [Fact]
    public void All_bundled_themes_meet_declared_contrast_thresholds()
    {
        foreach (var theme in ThemePolicies.All)
        {
            var issues = ThemeContrastValidator.Validate(theme);
            Assert.True(issues.Count == 0,
                $"{theme.Id}: {string.Join(", ", issues.Select(i => $"{i.Role} {i.Ratio:F2} < {i.Minimum:F1}"))}");
        }
    }

    /// <summary>Unknown semantic token kinds fall back to readable editor text.</summary>
    [Fact]
    public void Theme_unknown_semantic_kind_uses_editor_foreground()
    {
        foreach (var theme in ThemePolicies.All)
            Assert.Equal(theme.Palette.EditorForeground, theme.SemanticColor("unrecognized-kind"));
    }

    /// <summary>Opaque RGB parsing and contrast follow stable sRGB endpoints.</summary>
    [Fact]
    public void Theme_color_round_trips_and_black_white_contrast_is_21()
    {
        var black = ThemeColor.FromHex("#000000");
        var white = ThemeColor.FromHex("#FFFFFF");
        Assert.Equal("#000000", black.ToHex());
        Assert.Equal(21.0, black.ContrastRatio(white), precision: 10);
        Assert.Throws<FormatException>(() => ThemeColor.FromHex("#FF000080"));
    }
}
