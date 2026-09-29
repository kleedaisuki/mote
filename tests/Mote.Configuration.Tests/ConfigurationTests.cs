using System.Text;
using Mote.Configuration;
using Xunit;

namespace Mote.Configuration.Tests;

/// <summary>Exercises path conventions, explicit overrides, and startup-safe failure behavior.</summary>
public sealed class ConfigurationTests
{
    /// <summary>Default resolution is pure and places all generated data below ~/.mote.</summary>
    [Fact]
    public void DefaultsDoNotCreateDirectories()
    {
        using var fixture = new Fixture();
        var config = MoteConfigLoader.Load(fixture.Options);

        Assert.Equal(Path.Combine(fixture.Home, ".mote"), config.HomeDirectory);
        Assert.Equal(Path.Combine(config.HomeDirectory, "config.toml"), config.ConfigPath);
        Assert.Equal(Path.Combine(config.HomeDirectory, "cache"), config.CacheDirectory);
        Assert.Equal(Path.Combine(config.HomeDirectory, "data"), config.DataDirectory);
        Assert.Equal(Path.Combine(config.HomeDirectory, "traces"), config.TraceDirectory);
        Assert.Equal("mote-dark", config.ThemeId);
        Assert.False(config.TraceEnabled);
        Assert.Empty(config.Diagnostics);
        Assert.False(Directory.Exists(config.HomeDirectory));
    }

    /// <summary>Valid TOML selectively overrides conventions and resolves relative paths at MOTE_HOME.</summary>
    [Fact]
    public void TomlOverridesDirectoriesThemeAndTelemetry()
    {
        using var fixture = new Fixture();
        fixture.Write("""
            [paths]
            cache = "scratch/cache"
            data = "~/mote-records"
            traces = "../trace-out"

            [appearance]
            theme = "mote-light"

            [telemetry]
            enabled = true
            """);

        var config = MoteConfigLoader.Load(fixture.Options);

        Assert.Empty(config.Diagnostics);
        Assert.Equal(Path.Combine(config.HomeDirectory, "scratch", "cache"), config.CacheDirectory);
        Assert.Equal(Path.Combine(fixture.Home, "mote-records"), config.DataDirectory);
        Assert.Equal(Path.Combine(fixture.Home, "trace-out"), config.TraceDirectory);
        Assert.Equal("mote-light", config.ThemeId);
        Assert.True(config.TraceEnabled);
    }

    /// <summary>An explicit root relocates config and every default without touching user files.</summary>
    [Fact]
    public void RootOverrideRelocatesConventions()
    {
        using var fixture = new Fixture();
        var root = Path.Combine(fixture.Home, "elsewhere");
        var config = MoteConfigLoader.Load(fixture.Options with { MoteHomeOverride = root });

        Assert.Equal(root, config.HomeDirectory);
        Assert.Equal(Path.Combine(root, "traces"), config.TraceDirectory);
        Assert.False(Directory.Exists(root));
    }

    /// <summary>A process-relative root cannot make startup depend on the launch directory.</summary>
    [Fact]
    public void RelativeRootOverrideFallsBackWithDiagnostic()
    {
        using var fixture = new Fixture();

        var config = MoteConfigLoader.Load(fixture.Options with { MoteHomeOverride = "relative/mote" });

        Assert.Equal(Path.Combine(fixture.Home, ".mote"), config.HomeDirectory);
        Assert.Contains(config.Diagnostics, issue => issue.Code == "MOTE_HOME_INVALID");
    }

    /// <summary>Ambiguous TOML never partially overrides a path or telemetry choice.</summary>
    [Fact]
    public void MalformedTomlUsesWholeDefaultConfiguration()
    {
        using var fixture = new Fixture();
        fixture.Write("[paths]\ncache = \"good\"\ncache = \"bad\"\n");

        var config = MoteConfigLoader.Load(fixture.Options);

        Assert.Equal(Path.Combine(config.HomeDirectory, "cache"), config.CacheDirectory);
        Assert.Contains(config.Diagnostics, issue => issue.Code == "CONFIG_TOML");
    }

    /// <summary>Invalid values are ignored independently, and unknown keys are surfaced.</summary>
    [Fact]
    public void InvalidValuesFallBackWithoutHidingOtherValidValues()
    {
        using var fixture = new Fixture();
        fixture.Write("[paths]\ncache = 3\ndata = \"valid\"\n[appearance]\ntheme = \"Bad Theme\"\nunknown = 1\n");

        var config = MoteConfigLoader.Load(fixture.Options);

        Assert.Equal(Path.Combine(config.HomeDirectory, "cache"), config.CacheDirectory);
        Assert.Equal(Path.Combine(config.HomeDirectory, "valid"), config.DataDirectory);
        Assert.Equal("mote-dark", config.ThemeId);
        Assert.Equal(3, config.Diagnostics.Count);
    }

    /// <summary>Invalid UTF-8 is never silently replaced before TOML parsing.</summary>
    [Fact]
    public void InvalidUtf8UsesDefaultsAndReportsReadError()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Home, ".mote"));
        File.WriteAllBytes(Path.Combine(fixture.Home, ".mote", "config.toml"), [0xFF]);

        var config = MoteConfigLoader.Load(fixture.Options);

        Assert.Equal("mote-dark", config.ThemeId);
        Assert.Contains(config.Diagnostics, issue => issue.Code == "CONFIG_READ");
    }

    /// <summary>A UTF-16 BOM must not silently switch a TOML file away from UTF-8.</summary>
    [Fact]
    public void Utf16BomIsRejectedAsInvalidUtf8()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Home, ".mote"));
        File.WriteAllBytes(Path.Combine(fixture.Home, ".mote", "config.toml"), [0xFF, 0xFE, 0xFF]);

        var config = MoteConfigLoader.Load(fixture.Options);

        Assert.Equal("mote-dark", config.ThemeId);
        Assert.Contains(config.Diagnostics, issue => issue.Code == "CONFIG_READ");
    }

    /// <summary>The optional UTF-8 BOM remains readable without enabling UTF-16 detection.</summary>
    [Fact]
    public void Utf8BomIsAccepted()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Home, ".mote"));
        File.WriteAllBytes(Path.Combine(fixture.Home, ".mote", "config.toml"),
            [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("[appearance]\ntheme = \"mote-light\"\n")]);

        var config = MoteConfigLoader.Load(fixture.Options);

        Assert.Equal("mote-light", config.ThemeId);
        Assert.Empty(config.Diagnostics);
    }

    /// <summary>Owns only test artifacts beneath the repository's .temp directory.</summary>
    private sealed class Fixture : IDisposable
    {
        internal Fixture()
        {
            var repo = FindRepositoryRoot();
            Home = Path.Combine(repo, ".temp", "configuration-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Home);
            Options = new MoteConfigLoadOptions { UserHomeDirectory = Home, UseEnvironmentOverride = false };
        }

        internal string Home { get; }
        internal MoteConfigLoadOptions Options { get; }

        internal void Write(string content)
        {
            var root = Path.Combine(Home, ".mote");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "config.toml"), content, new UTF8Encoding(false));
        }

        public void Dispose() => Directory.Delete(Home, recursive: true);

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "mote.sln")))
                current = current.Parent;
            return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }
}
