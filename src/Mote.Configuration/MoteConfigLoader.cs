using System.Text;
using Mote.Themes;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Mote.Configuration;

/// <summary>
/// Resolves conventional ~/.mote settings, then overlays a small, validated TOML file.
/// No directory is created on the read path; writers create only their own destination.
/// </summary>
public static class MoteConfigLoader
{
    /// <summary>The file budget; the reader probes exactly one byte beyond it.</summary>
    private const int MaxConfigBytes = 1_048_576;
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
        var diagnostics = new DiagnosticCollector();
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
        var themeOverrides = ThemeOverrideData.Empty;
        var themeOverridesAccepted = true;

        string? content = ReadConfig(configPath, diagnostics, out var readDisposition);
        if (content is not null)
        {
            var syntax = SyntaxParser.Parse(content, validate: true);
            if (syntax.HasErrors)
            {
                readDisposition = ConfigReadDisposition.Rejected;
                foreach (var error in syntax.Diagnostics)
                    diagnostics.Add(new("CONFIG_TOML", error.Message));
            }
            else
            {
                themeOverrides = ReadThemeOverrides(syntax, diagnostics, out themeOverridesAccepted);
                foreach (var entry in EnumerateEntries(syntax))
                {
                    if (entry.IsTheme) continue;
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
            ReadDisposition = readDisposition,
            HomeDirectory = home,
            ConfigPath = configPath,
            CacheDirectory = cache,
            DataDirectory = data,
            TraceDirectory = traces,
            ThemeId = theme,
            ThemeOverrides = themeOverrides,
            ThemeOverridesAccepted = themeOverridesAccepted,
            TraceEnabled = traceEnabled,
            PreviewLayout = previewLayout,
            Diagnostics = diagnostics.AsReadOnly()
        };
    }

    /// <summary>Accepts only documented layout strings; invalid values retain Auto.</summary>
    private static PreviewLayoutPreference ResolvePreviewLayout(ValueSyntax? value,
        DiagnosticCollector diagnostics)
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

    private static string? ReadConfig(string path, DiagnosticCollector diagnostics,
        out ConfigReadDisposition disposition)
    {
        disposition = ConfigReadDisposition.Rejected;
        try
        {
            // Read the open stream itself with a cap, rather than trusting metadata
            // that can become stale when another process grows the file.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            // Length is only an allocation hint; subsequent growth is still read
            // through a hard cap plus one-byte overflow probe.
            var bytes = new byte[(int)Math.Min(stream.Length, MaxConfigBytes) + 1];
            var count = 0;
            while (count <= MaxConfigBytes)
            {
                if (count == bytes.Length)
                    Array.Resize(ref bytes, Math.Min(MaxConfigBytes + 1, bytes.Length * 2));
                var read = stream.Read(bytes, count, bytes.Length - count);
                if (read == 0) break;
                count += read;
            }
            if (count > MaxConfigBytes)
            {
                diagnostics.Add(new("CONFIG_SIZE", "Configuration file exceeds the 1 MiB startup limit."));
                return null;
            }
            // Only UTF-8 is permitted. Strip one optional UTF-8 BOM, never
            // autodetect UTF-16/32 or strip a subsequent literal U+FEFF.
            var offset = count >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            var content = StrictUtf8.GetString(bytes, offset, count - offset);
            if (content.Length <= MaxConfigChars)
            {
                disposition = ConfigReadDisposition.Loaded;
                return content;
            }
            diagnostics.Add(new("CONFIG_SIZE", "Decoded configuration exceeds the character limit."));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            disposition = ConfigReadDisposition.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            diagnostics.Add(new("CONFIG_READ", $"Configuration could not be read: {ex.Message}"));
        }
        return null;
    }

    /// <summary>Flattens structural TOML paths while retaining quoted dotted keys as single segments.</summary>
    private static IEnumerable<ConfigEntry> EnumerateEntries(DocumentSyntax syntax)
    {
        foreach (var pair in syntax.KeyValues)
            foreach (var entry in Flatten(KeyParts(pair.Key), pair.Value)) yield return entry;
        var ancestry = new TableAncestry();
        foreach (var table in syntax.Tables)
        {
            var prefix = KeyParts(table.Name);
            var arrayAncestor = ancestry.Observe(prefix, table is TableArraySyntax);
            if (IsThemePath(prefix) && arrayAncestor)
            {
                yield return Entry(prefix, null, arrayAncestor);
                continue;
            }
            if (!table.Items.Any() && IsThemePath(prefix) && !IsThemeContainer(prefix))
                yield return Entry(prefix, null, arrayAncestor);
            foreach (var pair in table.Items)
                foreach (var entry in Flatten([.. prefix, .. KeyParts(pair.Key)], pair.Value, arrayAncestor)) yield return entry;
        }
    }

    /// <summary>Uses an explicit work stack so deeply nested inline tables do not recurse on the call stack.</summary>
    private static IEnumerable<ConfigEntry> Flatten(string[] path, ValueSyntax? value, bool arrayAncestor = false)
    {
        var pending = new Stack<(string[] Path, ValueSyntax? Value)>();
        pending.Push((path, value));
        while (pending.TryPop(out var item))
        {
            if (item.Value is not InlineTableSyntax table)
            {
                yield return Entry(item.Path, item.Value, arrayAncestor);
                continue;
            }
            if (!table.Items.Any() && (!IsThemePath(item.Path) || !IsThemeContainer(item.Path) || arrayAncestor))
                yield return Entry(item.Path, item.Value, arrayAncestor);
            foreach (var child in table.Items.Reverse())
            {
                var pair = child.KeyValue;
                if (pair is null) continue;
                pending.Push(([.. item.Path, .. KeyParts(pair.Key)], pair.Value));
            }
        }
    }

    /// <summary>Normalizes only leaves inside the structural theme namespace into logical role keys.</summary>
    private static ConfigEntry Entry(string[] path, ValueSyntax? value, bool arrayAncestor = false) => IsThemePath(path)
        ? new("appearance.colors", string.Join('.', path.Skip(2)), value, true, arrayAncestor)
        : new(path.Length > 1 ? string.Join('.', path[..^1]) : string.Empty,
            path.Length > 0 ? path[^1] : string.Empty, value, false, arrayAncestor);

    /// <summary>Recognizes actual table segments, not a quoted key containing structural dots.</summary>
    private static bool IsThemePath(string[] path) => path.Length >= 2 &&
        path[0] == "appearance" && path[1] == "colors";

    /// <summary>Only known role families may be empty structural containers, not individual color values.</summary>
    private static bool IsThemeContainer(string[] path) => path.Length == 2 ||
        (path.Length == 3 && path[2] is "window" or "panel" or "editor" or "preview" or
            "text" or "gutter" or "selection" or "diagnostic" or "control" or "semantic");

    /// <summary>Retains each parsed key segment including literal dots inside quoted keys.</summary>
    private static string[] KeyParts(KeySyntax? key)
    {
        if (key is null) return [];
        return [PartText(key.Key), .. key.DotKeys.Select(item => PartText(item.Key))];
    }

    private static string PartText(BareKeyOrStringValueSyntax? key) => key switch
    {
        BareKeySyntax bare => bare.Key?.Text ?? string.Empty,
        StringValueSyntax quoted => quoted.Value ?? string.Empty,
        _ => string.Empty
    };

    /// <summary>All theme leaves form one failure unit; other configuration groups remain independent.</summary>
    private static ThemeOverrideData ReadThemeOverrides(DocumentSyntax syntax, DiagnosticCollector diagnostics,
        out bool accepted)
    {
        var entries = new List<KeyValuePair<string, string>>();
        var invalid = false;
        var count = 0;
        foreach (var entry in EnumerateEntries(syntax))
        {
            if (!entry.IsTheme) continue;
            if (++count > ThemeOverrideData.MaximumEntries)
            {
                if (count == ThemeOverrideData.MaximumEntries + 1)
                    diagnostics.Add(new("CONFIG_THEME_LIMIT", "Theme overrides exceed 28 entries; the entire map is ignored."));
                invalid = true;
                continue;
            }
            if (entry.HasArrayAncestor)
            {
                diagnostics.Add(new("CONFIG_THEME_COLOR", "Theme overrides cannot descend from an array of tables; the entire map is ignored."));
                invalid = true;
                continue;
            }
            if (entry.Key.Length != 0 && !ThemeOverrideData.TryParseRole(entry.Key, out _))
            {
                diagnostics.Add(new("CONFIG_THEME_ROLE", $"Unknown theme role '{BoundedRole(entry.Key)}'; the entire map is ignored."));
                invalid = true;
                continue;
            }
            if (entry.Value is not StringValueSyntax { Value: { } color })
            {
                diagnostics.Add(new("CONFIG_THEME_COLOR", $"Theme role '{BoundedRole(entry.Key)}' requires a #RRGGBB string; the entire map is ignored."));
                invalid = true;
                continue;
            }
            entries.Add(new(entry.Key, color));
        }
        if (!ThemeOverrideData.TryCreate(entries, out var data, out var issues)) invalid = true;
        foreach (var issue in issues)
            diagnostics.Add(new(issue.Code, $"Theme role '{issue.Role}': {issue.Message} The entire map is ignored."));
        accepted = !invalid;
        return invalid ? ThemeOverrideData.Empty : data;
    }

    /// <summary>Caps hostile role text retained in nonmodal settings diagnostics.</summary>
    private static string BoundedRole(string role) => role[..Math.Min(role.Length, ThemeOverrideData.MaximumKeyLength)];

    /// <summary>A flattened leaf with explicit namespace classification and its original scalar syntax.</summary>
    private readonly record struct ConfigEntry(string Section, string Key, ValueSyntax? Value, bool IsTheme, bool HasArrayAncestor);

    /// <summary>Tracks structural table ancestry without joining quoted segments or rescanning all previous arrays.</summary>
    private sealed class TableAncestry
    {
        /// <summary>Structural roots use exact case-sensitive TOML segment identity.</summary>
        private readonly Node _root = new();

        /// <summary>Registers a table shape and returns whether any segment on this path is an array.</summary>
        public bool Observe(string[] path, bool isArray)
        {
            var node = _root;
            var arrayAncestor = false;
            foreach (var segment in path)
            {
                if (!node.Children.TryGetValue(segment, out var child))
                {
                    child = new();
                    node.Children.Add(segment, child);
                }
                node = child;
                arrayAncestor |= node.IsArray;
            }
            node.IsArray |= isArray;
            return arrayAncestor || node.IsArray;
        }

        /// <summary>A table segment whose array shape remains fixed by valid TOML definitions.</summary>
        private sealed class Node
        {
            /// <summary>Child table segments, independent of punctuation inside a quoted segment.</summary>
            public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
            /// <summary>Whether this segment was introduced as an array of tables.</summary>
            public bool IsArray { get; set; }
        }
    }

    /// <summary>Retains at most 32 details and a deterministic omitted-count summary for hostile input.</summary>
    private sealed class DiagnosticCollector
    {
        /// <summary>The first 32 details in deterministic traversal order.</summary>
        private readonly List<ConfigDiagnostic> _details = [];
        /// <summary>The count of later details discarded to bound retained storage.</summary>
        private int _omitted;

        /// <summary>Retains a detail or increments the omitted count without growing the collection.</summary>
        public void Add(ConfigDiagnostic diagnostic)
        {
            if (_details.Count < 32) _details.Add(diagnostic);
            else _omitted++;
        }

        /// <summary>Seals the collection once after loading, with a final summary when needed.</summary>
        public IReadOnlyList<ConfigDiagnostic> AsReadOnly()
        {
            if (_omitted != 0) _details.Add(new("CONFIG_DIAGNOSTICS", $"{_omitted} additional configuration diagnostics omitted."));
            return _details.AsReadOnly();
        }
    }

    private static string ResolveConfiguredDirectory(ValueSyntax? value, string home, string userHome,
        string fallback, string name, DiagnosticCollector diagnostics)
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
