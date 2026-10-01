using System.Text;
using Mote.Configuration;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Checks atomic data-only color overrides and bounded, strict local configuration reads.</summary>
public sealed class ThemeOverrideConfigurationTests
{
    /// <summary>The public configuration byte-budget contract.</summary>
    private const int ByteLimit = 1_048_576;

    /// <summary>All supported TOML representations resolve to the same typed sparse role.</summary>
    [Theory]
    [InlineData("[appearance.colors]\n\"preview.background\" = '#202124'")]
    [InlineData("[appearance.colors]\npreview.background = '#202124'")]
    [InlineData("[appearance.colors.preview]\nbackground = '#202124'")]
    [InlineData("[appearance]\ncolors = { \"preview.background\" = '#202124' }")]
    [InlineData("[appearance]\ncolors = { preview = { background = '#202124' } }")]
    [InlineData("appearance.colors.\"preview.background\" = '#202124'")]
    [InlineData("appearance = { colors = { preview.background = '#202124' } }")]
    [InlineData("[appearance.colors]\n'preview.background' = '#202124'")]
    public void Equivalent_color_forms_have_identical_meaning(string text)
    {
        using var temp = new RepoTemp();
        var config = WriteAndLoad(temp, text);
        Assert.Empty(config.Diagnostics);
        Assert.True(config.ThemeOverrides.TryGetColor(ThemeColorRole.PreviewBackground, out var color));
        Assert.Equal("#202124", color.ToHex());
        Assert.False(config.ThemeOverrides.TryGetColor(ThemeColorRole.EditorForeground, out _));
    }

    /// <summary>An absent or deliberately empty map retains the additive inherited default.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("[appearance.colors]")]
    [InlineData("[appearance]\ncolors = {}")]
    [InlineData("appearance = { colors = {} }")]
    [InlineData("[appearance.colors]\npreview = {}")]
    [InlineData("[appearance.colors.preview]")]
    public void Empty_map_has_no_overrides(string text)
    {
        using var temp = new RepoTemp();
        var config = WriteAndLoad(temp, text);
        Assert.Empty(config.Diagnostics);
        AssertEmpty(config.ThemeOverrides);
    }

    /// <summary>A malformed map is never partly published, but independent valid groups still apply.</summary>
    [Theory]
    [InlineData("'unknown.role' = '#FFFFFF'", "CONFIG_THEME_ROLE")]
    [InlineData("'Preview.background' = '#FFFFFF'", "CONFIG_THEME_ROLE")]
    [InlineData("'semantic.string' = '#fff'", "CONFIG_THEME_COLOR")]
    [InlineData("'semantic.string' = '#FFFFFFFF'", "CONFIG_THEME_COLOR")]
    [InlineData("'semantic.string' = 42", "CONFIG_THEME_COLOR")]
    [InlineData("'semantic.string' = {}", "CONFIG_THEME_COLOR")]
    [InlineData("'unknown.role' = {}", "CONFIG_THEME_ROLE")]
    [InlineData("'semantic.string' = true", "CONFIG_THEME_COLOR")]
    [InlineData("'semantic.string' = ['#FFFFFF']", "CONFIG_THEME_COLOR")]
    [InlineData("'semantic.string' = 1979-05-27", "CONFIG_THEME_COLOR")]
    [InlineData("'semantic.string' = 'red'", "CONFIG_THEME_COLOR")]
    public void Bad_color_rejects_whole_map_not_other_groups(string invalid, string code)
    {
        using var temp = new RepoTemp();
        var config = WriteAndLoad(temp, "[appearance]\ntheme = 'mote-light'\n" +
            "[editor]\npreview = 'source'\n[telemetry]\nenabled = true\n" +
            "[paths]\ncache = 'custom'\n[appearance.colors]\n'preview.background' = '#202124'\n" + invalid);
        AssertEmpty(config.ThemeOverrides);
        Assert.False(config.ThemeOverridesAccepted);
        Assert.Equal(ConfigReadDisposition.Loaded, config.ReadDisposition);
        Assert.Contains(config.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Equal("mote-light", config.ThemeId);
        Assert.Equal(PreviewLayoutPreference.SourceOnly, config.PreviewLayout);
        Assert.True(config.TraceEnabled);
        Assert.Equal(Path.Combine(config.HomeDirectory, "custom"), config.CacheDirectory);
        Assert.False(Directory.Exists(config.CacheDirectory));
    }

    /// <summary>Quoted dotted and nested leaves can be distinct TOML keys but one logical role.</summary>
    [Theory]
    [InlineData("[appearance.colors]\n'preview.background' = '#202124'\npreview.background = '#111111'")]
    [InlineData("[appearance]\ncolors = { 'preview.background' = '#202124', preview = { background = '#111111' } }")]
    public void Logical_duplicate_rejects_map(string text)
    {
        using var temp = new RepoTemp();
        var config = WriteAndLoad(temp, text);
        AssertEmpty(config.ThemeOverrides);
        Assert.Contains(config.Diagnostics, diagnostic => diagnostic.Code == "CONFIG_THEME_ROLE" &&
            diagnostic.Message.Contains("Duplicate", StringComparison.Ordinal));
    }

    /// <summary>An empty unknown nested table is still an unsupported role, not an ignored extension.</summary>
    [Fact]
    public void Empty_unknown_color_table_rejects_map()
    {
        using var temp = new RepoTemp();
        var config = WriteAndLoad(temp, "[appearance.colors]\n'accent' = '#ABCDEF'\n[appearance.colors.unknown]");
        AssertEmpty(config.ThemeOverrides);
        Assert.Contains(config.Diagnostics, diagnostic => diagnostic.Code == "CONFIG_THEME_ROLE");
    }

    /// <summary>Every array-of-tables ancestor invalidates the whole requested map, including empty descendants.</summary>
    [Theory]
    [InlineData("[[appearance]]\ncolors = { accent = '#ABCDEF' }")]
    [InlineData("[[appearance]]\n[appearance.colors]\naccent = '#ABCDEF'")]
    [InlineData("[[appearance]]\n[appearance.colors.preview]\nbackground = '#202124'")]
    [InlineData("[[appearance]]\ncolors = {}")]
    [InlineData("[[appearance]]\n[appearance.colors]")]
    [InlineData("[[appearance.colors]]\naccent = '#ABCDEF'")]
    [InlineData("[[appearance.colors.preview]]\nbackground = '#202124'")]
    [InlineData("[appearance.colors]\naccent = '#ABCDEF'\n[[appearance.colors.preview]]\nbackground = '#202124'")]
    public void Array_ancestry_rejects_map_but_keeps_independent_settings(string text)
    {
        using var temp = new RepoTemp();
        var config = WriteAndLoad(temp, "[editor]\npreview = 'source'\n[telemetry]\nenabled = true\n" + text);
        Assert.Equal(ConfigReadDisposition.Loaded, config.ReadDisposition);
        Assert.False(config.ThemeOverridesAccepted);
        AssertEmpty(config.ThemeOverrides);
        Assert.Contains(config.Diagnostics, diagnostic => diagnostic.Code == "CONFIG_THEME_COLOR");
        Assert.Equal(PreviewLayoutPreference.SourceOnly, config.PreviewLayout);
        Assert.True(config.TraceEnabled);
    }

    /// <summary>An unrelated array path cannot poison a sibling ordinary theme map.</summary>
    [Theory]
    [InlineData("[[other]]\nvalue = 1\n[appearance.colors]\naccent = '#ABCDEF'")]
    [InlineData("[[appearance.other]]\nvalue = 1\n[appearance.colors]\naccent = '#ABCDEF'")]
    [InlineData("[['appearance.colors']]\nvalue = 1\n[appearance.colors]\naccent = '#ABCDEF'")]
    public void Sibling_arrays_do_not_reject_valid_map(string text)
    {
        using var temp = new RepoTemp();
        var config = WriteAndLoad(temp, text);
        Assert.Equal(ConfigReadDisposition.Loaded, config.ReadDisposition);
        Assert.True(config.ThemeOverridesAccepted);
        Assert.True(config.ThemeOverrides.TryGetColor(ThemeColorRole.Accent, out _));
        Assert.DoesNotContain(config.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CONFIG_THEME", StringComparison.Ordinal));
    }

    /// <summary>Reload classification uses the actual open failure, including a directory at the file path.</summary>
    [Fact]
    public void Unreadable_config_is_rejected_not_missing()
    {
        using var temp = new RepoTemp();
        var configPath = Path.Combine(temp.Path, ".mote", "config.toml");
        Directory.CreateDirectory(configPath);
        var config = Load(temp);
        Assert.Equal(ConfigReadDisposition.Rejected, config.ReadDisposition);
        Assert.Contains(config.Diagnostics, diagnostic => diagnostic.Code == "CONFIG_READ");
        Assert.False(config.TraceEnabled);
    }

    /// <summary>Deleting the config deliberately returns Missing, unlike a rejected existing candidate.</summary>
    [Fact]
    public void Deleted_config_returns_missing_defaults()
    {
        using var temp = new RepoTemp();
        var config = WriteAndLoad(temp, "[telemetry]\nenabled = true");
        Assert.Equal(ConfigReadDisposition.Loaded, config.ReadDisposition);
        File.Delete(config.ConfigPath);
        var missing = Load(temp);
        Assert.Equal(ConfigReadDisposition.Missing, missing.ReadDisposition);
        Assert.True(missing.ThemeOverridesAccepted);
        Assert.False(missing.TraceEnabled);
        Assert.Empty(missing.Diagnostics);
    }

    /// <summary>TOML's own duplicate-key failure still rejects the whole file, including other groups.</summary>
    [Fact]
    public void Syntax_duplicate_preserves_whole_file_defaults()
    {
        using var temp = new RepoTemp();
        var config = WriteAndLoad(temp, "[telemetry]\nenabled = true\n[appearance.colors]\n" +
            "'preview.background' = '#202124'\n'preview.background' = '#111111'");
        Assert.False(config.TraceEnabled);
        AssertEmpty(config.ThemeOverrides);
        Assert.Equal(ConfigReadDisposition.Rejected, config.ReadDisposition);
        Assert.Contains(config.Diagnostics, diagnostic => diagnostic.Code == "CONFIG_TOML");
    }

    /// <summary>A literal dotted top-level key is not a structural appearance/colors path.</summary>
    [Fact]
    public void Quoted_structural_path_does_not_enter_theme_namespace()
    {
        using var temp = new RepoTemp();
        var config = WriteAndLoad(temp, "'appearance.colors' = { 'preview.background' = '#202124' }");
        AssertEmpty(config.ThemeOverrides);
        Assert.Contains(config.Diagnostics, diagnostic => diagnostic.Code == "CONFIG_UNKNOWN");
    }

    /// <summary>Nonmap colors and arrays of tables cannot masquerade as an override map.</summary>
    [Theory]
    [InlineData("[appearance]\ncolors = 42")]
    [InlineData("[[appearance.colors]]\n'preview.background' = '#202124'")]
    public void Invalid_map_container_is_rejected(string text)
    {
        using var temp = new RepoTemp();
        var config = WriteAndLoad(temp, text);
        AssertEmpty(config.ThemeOverrides);
        Assert.Contains(config.Diagnostics, diagnostic => diagnostic.Code == "CONFIG_THEME_COLOR");
    }

    /// <summary>The finite schema and warning details remain bounded even for many invalid entries.</summary>
    [Fact]
    public void Oversized_map_and_unknown_keys_have_bounded_deterministic_diagnostics()
    {
        using var temp = new RepoTemp();
        var text = "[appearance.colors]\n" + string.Join('\n', Enumerable.Range(0, 60)
            .Select(index => $"'invalid{index}' = '#FFFFFF'")) + "\n[unknown]\n" +
            string.Join('\n', Enumerable.Range(0, 60).Select(index => $"key{index} = true"));
        var first = WriteAndLoad(temp, text);
        var second = Load(temp);
        AssertEmpty(first.ThemeOverrides);
        Assert.Equal(33, first.Diagnostics.Count);
        Assert.Equal(first.Diagnostics, second.Diagnostics);
        Assert.Contains(first.Diagnostics, diagnostic => diagnostic.Code == "CONFIG_THEME_LIMIT");
        Assert.Equal("CONFIG_DIAGNOSTICS", first.Diagnostics[^1].Code);
    }

    /// <summary>Exactly all 28 declared roles are accepted; the 29th leaf rejects the whole set.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Closed_schema_entry_limit_has_exact_boundary(bool extra)
    {
        string[] roles = ["window.background", "panel.background", "editor.background", "editor.foreground",
            "preview.background", "preview.foreground", "text.muted", "gutter.foreground",
            "editor.activeLineBackground", "selection.background", "selection.foreground", "editor.cursor",
            "border", "accent", "diagnostic.error", "diagnostic.warning", "diagnostic.info", "diagnostic.success",
            "control.background", "control.foreground", "semantic.key", "semantic.string", "semantic.number",
            "semantic.keyword", "semantic.comment", "semantic.marker", "semantic.link", "semantic.error"];
        using var temp = new RepoTemp();
        var text = "[appearance.colors]\n" + string.Join('\n', roles.Select(role => $"'{role}' = '#ABCDEF'"));
        if (extra) text += "\n'extra' = '#ABCDEF'";
        var config = WriteAndLoad(temp, text);
        if (extra)
        {
            AssertEmpty(config.ThemeOverrides);
            Assert.Contains(config.Diagnostics, diagnostic => diagnostic.Code == "CONFIG_THEME_LIMIT");
            return;
        }
        Assert.Empty(config.Diagnostics);
        foreach (var role in Enum.GetValues<ThemeColorRole>())
            Assert.True(config.ThemeOverrides.TryGetColor(role, out _));
    }

    /// <summary>Role-name diagnostics never echo an unbounded hostile key.</summary>
    [Fact]
    public void Oversized_role_name_is_rejected_with_bounded_detail()
    {
        using var temp = new RepoTemp();
        var config = WriteAndLoad(temp, "[appearance.colors]\n'" + new string('x', 4000) + "' = '#FFFFFF'");
        AssertEmpty(config.ThemeOverrides);
        var diagnostic = Assert.Single(config.Diagnostics);
        Assert.Equal("CONFIG_THEME_ROLE", diagnostic.Code);
        Assert.True(diagnostic.Message.Length < 200);
    }

    /// <summary>UTF-8 BOM counts against the byte budget and only one initial marker is stripped.</summary>
    [Fact]
    public void Optional_utf8_bom_is_accepted()
    {
        using var temp = new RepoTemp();
        var config = WriteBytesAndLoad(temp, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(
            "[appearance.colors]\n'accent' = '#aBcDeF'")]);
        Assert.Empty(config.Diagnostics);
        Assert.True(config.ThemeOverrides.TryGetColor(ThemeColorRole.Accent, out var color));
        Assert.Equal("#ABCDEF", color.ToHex());
    }

    /// <summary>Invalid UTF-8 and UTF-16/32 signatures cannot select a different decoder.</summary>
    [Theory]
    [InlineData(new byte[] { 0xC0, 0xAF })]
    [InlineData(new byte[] { 0xE2, 0x82 })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41, 0 })]
    [InlineData(new byte[] { 0, 0, 0xFE, 0xFF })]
    public void Non_utf8_is_rejected(byte[] bytes)
    {
        using var temp = new RepoTemp();
        var config = WriteBytesAndLoad(temp, bytes);
        AssertEmpty(config.ThemeOverrides);
        Assert.Equal(ConfigReadDisposition.Rejected, config.ReadDisposition);
        Assert.Contains(config.Diagnostics, diagnostic => diagnostic.Code == "CONFIG_READ");
    }

    /// <summary>Reads accept exactly the byte cap but reject cap plus one before parsing.</summary>
    [Theory]
    [InlineData(ByteLimit, false)]
    [InlineData(ByteLimit + 1, true)]
    public void Config_byte_budget_has_exact_boundary(int count, bool rejected)
    {
        using var temp = new RepoTemp();
        var bytes = Enumerable.Repeat((byte)' ', count).ToArray();
        var config = WriteBytesAndLoad(temp, bytes);
        Assert.Equal(rejected ? ConfigReadDisposition.Rejected : ConfigReadDisposition.Loaded, config.ReadDisposition);
        Assert.Equal(rejected, config.Diagnostics.Any(diagnostic => diagnostic.Code == "CONFIG_SIZE"));
        if (!rejected) Assert.Empty(config.Diagnostics);
    }

    /// <summary>Multibyte UTF-8 is limited in bytes, not merely in decoded UTF-16 code units.</summary>
    [Fact]
    public void Multibyte_content_uses_byte_budget()
    {
        using var temp = new RepoTemp();
        var bytes = Encoding.UTF8.GetBytes("#" + new string('中', ByteLimit / 3 + 1));
        Assert.True(bytes.Length > ByteLimit);
        var config = WriteBytesAndLoad(temp, bytes);
        Assert.Contains(config.Diagnostics, diagnostic => diagnostic.Code == "CONFIG_SIZE");
    }

    /// <summary>Explicit root and independent paths keep their prior semantics and loading writes nothing.</summary>
    [Fact]
    public void Colors_do_not_relocate_root_or_create_user_destinations()
    {
        using var temp = new RepoTemp();
        var home = temp.File("portable");
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "[appearance.colors]\n'accent' = '#ABCDEF'\n" +
            "[paths]\ncache = '../cache-elsewhere'\ndata = '~/durable'\ntraces = 'trace-elsewhere'");
        var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
        {
            UserHomeDirectory = temp.Path, MoteHomeOverride = home, UseEnvironmentOverride = false
        });
        Assert.Empty(config.Diagnostics);
        Assert.Equal(home, config.HomeDirectory);
        Assert.Equal(Path.Combine(temp.Path, "cache-elsewhere"), config.CacheDirectory);
        Assert.Equal(Path.Combine(temp.Path, "durable"), config.DataDirectory);
        Assert.Equal(Path.Combine(home, "trace-elsewhere"), config.TraceDirectory);
        Assert.False(Directory.Exists(config.CacheDirectory));
        Assert.False(Directory.Exists(config.DataDirectory));
        Assert.False(Directory.Exists(config.TraceDirectory));
    }

    /// <summary>A missing conventional root is not created and existing object initializers remain valid.</summary>
    [Fact]
    public void Missing_config_and_existing_initializers_use_empty_default()
    {
        using var temp = new RepoTemp();
        var config = Load(temp);
        Assert.False(Directory.Exists(config.HomeDirectory));
        Assert.Equal(ConfigReadDisposition.Missing, config.ReadDisposition);
        Assert.True(config.ThemeOverridesAccepted);
        Assert.Empty(config.Diagnostics);
        AssertEmpty(config.ThemeOverrides);
        var legacy = new MoteConfiguration
        {
            HomeDirectory = config.HomeDirectory, ConfigPath = config.ConfigPath,
            CacheDirectory = config.CacheDirectory, DataDirectory = config.DataDirectory,
            TraceDirectory = config.TraceDirectory, ThemeId = config.ThemeId,
            TraceEnabled = config.TraceEnabled, Diagnostics = []
        };
        Assert.Same(ThemeOverrideData.Empty, legacy.ThemeOverrides);
    }

    /// <summary>Configuration stores requested colors without preempting platform-dependent composition.</summary>
    [Fact]
    public void Low_contrast_request_is_preserved_for_theme_composer()
    {
        using var temp = new RepoTemp();
        var config = WriteAndLoad(temp, "[appearance.colors]\n'editor.foreground' = '#202020'");
        Assert.Empty(config.Diagnostics);
        Assert.True(config.ThemeOverrides.TryGetColor(ThemeColorRole.EditorForeground, out _));
        Assert.False(ThemeComposer.Compose(ThemePolicies.Get(config.ThemeId), config.ThemeOverrides).IsValid);
    }

    /// <summary>Checks absence across the entire closed role vocabulary.</summary>
    private static void AssertEmpty(ThemeOverrideData data)
    {
        foreach (var role in Enum.GetValues<ThemeColorRole>()) Assert.False(data.TryGetColor(role, out _));
    }

    /// <summary>Writes a UTF-8 test config inside the repository temporary root.</summary>
    private static MoteConfiguration WriteAndLoad(RepoTemp temp, string text) =>
        WriteBytesAndLoad(temp, Encoding.UTF8.GetBytes(text));

    /// <summary>Loads exact fixture bytes without an implicit text encoder or BOM conversion.</summary>
    private static MoteConfiguration WriteBytesAndLoad(RepoTemp temp, byte[] bytes)
    {
        var home = Path.Combine(temp.Path, ".mote");
        Directory.CreateDirectory(home);
        File.WriteAllBytes(Path.Combine(home, "config.toml"), bytes);
        return Load(temp);
    }

    /// <summary>Disables ambient root overrides to keep tests deterministic.</summary>
    private static MoteConfiguration Load(RepoTemp temp) => MoteConfigLoader.Load(new MoteConfigLoadOptions
    {
        UserHomeDirectory = temp.Path, UseEnvironmentOverride = false
    });
}
