using System.Text;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Mote.Configuration;

/// <summary>
/// Resolves conventional ~/.mote settings, then overlays a small, validated TOML file.
/// No directory is created on the read path; writers create only their own destination.
/// </summary>
public static class MoteConfigLoader
{
    private const int MaxConfigChars = 1_048_576;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Loads effective settings. For example, <c>var config = MoteConfigLoader.Load();</c>
    /// can be used before constructing a view or configuring an opt-in trace sink.
    /// An invalid file leaves conventional settings in force and reports diagnostics.
    /// </summary>
    public static MoteConfiguration Load(MoteConfigLoadOptions? options = null)
    {
        options ??= new MoteConfigLoadOptions();
        var diagnostics = new List<ConfigDiagnostic>();
        var userHome = ResolveUserHome(options.UserHomeDirectory);
        var overridePath = options.MoteHomeOverride;
        if (overridePath is null && options.UseEnvironmentOverride)
            overridePath = Environment.GetEnvironmentVariable("MOTE_HOME");

        var home = Path.Combine(userHome, ".mote");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (TryResolvePath(overridePath, userHome, userHome, requireAbsolute: true,
                out var resolvedHome))
                home = resolvedHome;
            else
                diagnostics.Add(new("MOTE_HOME_INVALID", "MOTE_HOME must be an absolute path or begin with '~/'."));
        }

        var configPath = Path.Combine(home, "config.toml");
        var cache = Path.Combine(home, "cache");
        var data = Path.Combine(home, "data");
        var traces = Path.Combine(home, "traces");
        var theme = "mote-dark";
        var traceEnabled = false;
        var previewLayout = PreviewLayoutPreference.Auto;

        string? content = ReadConfig(configPath, diagnostics);
        if (content is not null)
        {
            var syntax = SyntaxParser.Parse(content, validate: true);
            if (syntax.HasErrors)
            {
                foreach (var error in syntax.Diagnostics)
                    diagnostics.Add(new("CONFIG_TOML", error.Message));
            }
            else
            {
                foreach (var entry in EnumerateEntries(syntax))
                {
                    switch (entry.Section, entry.Key)
                    {
                        case ("editor", "preview"):
                            previewLayout = ResolvePreviewLayout(entry.Value, diagnostics);
                            break;
                        case ("paths", "cache"):
                            cache = ResolveConfiguredDirectory(entry.Value, home, userHome, cache,
                                "paths.cache", diagnostics);
                            break;
                        case ("paths", "data"):
                            data = ResolveConfiguredDirectory(entry.Value, home, userHome, data,
                                "paths.data", diagnostics);
                            break;
                        case ("paths", "traces"):
                            traces = ResolveConfiguredDirectory(entry.Value, home, userHome, traces,
                                "paths.traces", diagnostics);
                            break;
                        case ("appearance", "theme"):
                            if (entry.Value is StringValueSyntax { Value: { } themeValue } && IsThemeId(themeValue))
                                theme = themeValue;
                            else
                                diagnostics.Add(new("CONFIG_VALUE", "appearance.theme must be a nonempty kebab-case string."));
                            break;
                        case ("telemetry", "enabled"):
                            if (entry.Value is BooleanValueSyntax enabledValue)
                                traceEnabled = enabledValue.Value;
                            else
                                diagnostics.Add(new("CONFIG_VALUE", "telemetry.enabled must be a boolean."));
                            break;
                        default:
                            diagnostics.Add(new("CONFIG_UNKNOWN", $"Unknown configuration key '{entry.Section}.{entry.Key}'."));
                            break;
                    }
                }
            }
        }

        return new MoteConfiguration
        {
            HomeDirectory = home,
            ConfigPath = configPath,
            CacheDirectory = cache,
            DataDirectory = data,
            TraceDirectory = traces,
            ThemeId = theme,
            TraceEnabled = traceEnabled,
            PreviewLayout = previewLayout,
            Diagnostics = diagnostics.AsReadOnly()
        };
    }

    /// <summary>Accepts only documented layout strings; invalid values retain Auto.</summary>
    private static PreviewLayoutPreference ResolvePreviewLayout(ValueSyntax? value,
        List<ConfigDiagnostic> diagnostics)
    {
        if (value is StringValueSyntax text)
        {
            switch (text.Value)
            {
                case "auto": return PreviewLayoutPreference.Auto;
                case "source": return PreviewLayoutPreference.SourceOnly;
                case "split": return PreviewLayoutPreference.Split;
            }
        }
        diagnostics.Add(new("CONFIG_VALUE", "editor.preview must be 'auto', 'source', or 'split'."));
        return PreviewLayoutPreference.Auto;
    }

    private static string ResolveUserHome(string? explicitHome)
    {
        var home = explicitHome ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home) || !Path.IsPathFullyQualified(home))
            throw new InvalidOperationException("A fully qualified user profile path is required.");
        return Path.GetFullPath(home);
    }

    private static string? ReadConfig(string path, List<ConfigDiagnostic> diagnostics)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > MaxConfigChars)
            {
                diagnostics.Add(new("CONFIG_SIZE", "Configuration file exceeds the 1 MiB startup limit."));
                return null;
            }

            // File.ReadAllText auto-detects a UTF-16 BOM even when supplied a strict
            // UTF-8 decoder. TOML config is UTF-8 only, so disable BOM detection.
            using var reader = new StreamReader(path, StrictUtf8,
                detectEncodingFromByteOrderMarks: false);
            var content = reader.ReadToEnd();
            if (content.StartsWith('\uFEFF')) content = content[1..];
            if (content.Length <= MaxConfigChars) return content;
            diagnostics.Add(new("CONFIG_SIZE", "Configuration file exceeds the 1 MiB startup limit."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            diagnostics.Add(new("CONFIG_READ", $"Configuration could not be read: {ex.Message}"));
        }
        return null;
    }

    private static IEnumerable<(string Section, string Key, ValueSyntax? Value)> EnumerateEntries(
        DocumentSyntax syntax)
    {
        foreach (var pair in syntax.KeyValues)
            yield return (string.Empty, KeyText(pair.Key), pair.Value);
        foreach (var table in syntax.Tables)
        {
            var section = KeyText(table.Name);
            foreach (var pair in table.Items)
                yield return (section, KeyText(pair.Key), pair.Value);
        }
    }

    private static string KeyText(KeySyntax? key)
    {
        if (key is null) return string.Empty;
        var builder = new StringBuilder(PartText(key.Key));
        foreach (var item in key.DotKeys)
            builder.Append('.').Append(PartText(item.Key));
        return builder.ToString();
    }

    private static string PartText(BareKeyOrStringValueSyntax? key) => key switch
    {
        BareKeySyntax bare => bare.Key?.Text ?? string.Empty,
        StringValueSyntax quoted => quoted.Value ?? string.Empty,
        _ => string.Empty
    };

    private static string ResolveConfiguredDirectory(ValueSyntax? value, string home, string userHome,
        string fallback, string name, List<ConfigDiagnostic> diagnostics)
    {
        if (value is StringValueSyntax text &&
            TryResolvePath(text.Value, home, userHome, requireAbsolute: false, out var path))
            return path;
        diagnostics.Add(new("CONFIG_PATH", $"{name} must be a nonempty, valid path string."));
        return fallback;
    }

    private static bool TryResolvePath(string? value, string relativeBase, string userHome,
        bool requireAbsolute, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\0') >= 0) return false;
        try
        {
            string candidate;
            if (value == "~") candidate = userHome;
            else if (value.StartsWith("~/", StringComparison.Ordinal) ||
                value.StartsWith("~\\", StringComparison.Ordinal))
                candidate = Path.Combine(userHome, value[2..]);
            else if (Path.IsPathFullyQualified(value)) candidate = value;
            else if (Path.IsPathRooted(value)) return false;
            else if (!requireAbsolute) candidate = Path.Combine(relativeBase, value);
            else return false;
            path = Path.GetFullPath(candidate);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsThemeId(string value)
    {
        if (value.Length is < 1 or > 64 || value[0] == '-' || value[^1] == '-') return false;
        foreach (var ch in value)
            if (!char.IsAsciiLetterLower(ch) && !char.IsAsciiDigit(ch) && ch != '-') return false;
        return true;
    }
}
