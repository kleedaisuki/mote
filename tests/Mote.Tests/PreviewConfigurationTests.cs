using Mote.Configuration;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Checks preview preferences and centralized policy presentation conventions.</summary>
public sealed class PreviewConfigurationTests
{
    /// <summary>Absent configuration delegates layout to policy without creating its home.</summary>
    [Fact]
    public void Missing_config_uses_auto_without_creating_home()
    {
        using var temp = new RepoTemp();
        var config = Load(temp);
        Assert.Equal(PreviewLayoutPreference.Auto, config.PreviewLayout);
        Assert.Empty(config.Diagnostics);
        Assert.False(Directory.Exists(config.HomeDirectory));
    }

    /// <summary>Each documented string is an explicit override with unrelated settings intact.</summary>
    [Theory]
    [InlineData("auto", PreviewLayoutPreference.Auto)]
    [InlineData("source", PreviewLayoutPreference.SourceOnly)]
    [InlineData("split", PreviewLayoutPreference.Split)]
    public void Valid_preference_overlays_only_layout(string value, PreviewLayoutPreference expected)
    {
        using var temp = new RepoTemp();
        WriteConfig(temp, $"[editor]\npreview = '{value}'\n" +
            "[paths]\ncache = 'custom-cache'\n[appearance]\ntheme = 'mote-light'\n" +
            "[telemetry]\nenabled = true\n");
        var config = Load(temp);
        Assert.Equal(expected, config.PreviewLayout);
        Assert.Empty(config.Diagnostics);
        Assert.Equal(Path.Combine(config.HomeDirectory, "custom-cache"), config.CacheDirectory);
        Assert.Equal("mote-light", config.ThemeId);
        Assert.True(config.TraceEnabled);
    }

    /// <summary>Wrong types, casing and unknown values report an issue and retain Auto.</summary>
    [Theory]
    [InlineData("'unknown'")]
    [InlineData("'Split'")]
    [InlineData("''")]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("['split']")]
    public void Invalid_preference_is_nonfatal(string value)
    {
        using var temp = new RepoTemp();
        WriteConfig(temp, $"[editor]\npreview = {value}\n[telemetry]\nenabled = true\n");
        var config = Load(temp);
        Assert.Equal(PreviewLayoutPreference.Auto, config.PreviewLayout);
        var diagnostic = Assert.Single(config.Diagnostics);
        Assert.Equal("CONFIG_VALUE", diagnostic.Code);
        Assert.Contains("editor.preview", diagnostic.Message);
        Assert.True(config.TraceEnabled);
    }

    /// <summary>Duplicate preferences invalidate the entire file rather than choosing an order.</summary>
    [Fact]
    public void Duplicate_preference_keeps_all_defaults()
    {
        using var temp = new RepoTemp();
        WriteConfig(temp, "[editor]\npreview = 'source'\npreview = 'split'\n" +
            "[telemetry]\nenabled = true\n");
        var config = Load(temp);
        Assert.Equal(PreviewLayoutPreference.Auto, config.PreviewLayout);
        Assert.False(config.TraceEnabled);
        Assert.Contains(config.Diagnostics, diagnostic => diagnostic.Code == "CONFIG_TOML");
    }

    /// <summary>Only plain text defaults to full-width source; no analysis is required.</summary>
    [Fact]
    public void Built_in_policy_conventions_are_complete()
    {
        IDocumentPolicy[] policies = [new PlainTextPolicy(), new MarkdownPolicy(),
            new TomlPolicy(), new JsonPolicy(), new YamlPolicy(), new CsvPolicy()];
        Assert.Equal(Enum.GetValues<DocumentKind>().Length, policies.Length);
        foreach (var policy in policies)
        {
            var expected = policy.Kind == DocumentKind.PlainText
                ? DocumentPresentationDefault.SourceOnly
                : DocumentPresentationDefault.SourceAndPreview;
            Assert.Equal(expected, DocumentPresentation.ForPolicy(policy));
        }
    }

    /// <summary>A missing policy is rejected instead of accidentally selecting a layout.</summary>
    [Fact]
    public void Missing_policy_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => DocumentPresentation.ForPolicy(null!));
    }

    /// <summary>Loads an isolated conventional home, ignoring the host's environment.</summary>
    private static MoteConfiguration Load(RepoTemp temp) => MoteConfigLoader.Load(new()
    {
        UserHomeDirectory = temp.Path,
        UseEnvironmentOverride = false
    });

    /// <summary>Creates only the fixture config; the loader remains side-effect free.</summary>
    private static void WriteConfig(RepoTemp temp, string content)
    {
        var home = Path.Combine(temp.Path, ".mote");
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), content);
    }
}
