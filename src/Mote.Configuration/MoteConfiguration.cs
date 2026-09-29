namespace Mote.Configuration;

/// <summary>An issue in a local configuration file or its path resolution.</summary>
public sealed record ConfigDiagnostic(string Code, string Message);

/// <summary>
/// Immutable effective settings. All directory properties are absolute paths;
/// loading these settings does not create a file or directory.
/// </summary>
public sealed record MoteConfiguration
{
    /// <summary>The selected root for mote-owned files, normally ~/.mote.</summary>
    public required string HomeDirectory { get; init; }

    /// <summary>The TOML configuration file inside <see cref="HomeDirectory"/>.</summary>
    public required string ConfigPath { get; init; }

    /// <summary>Directory for replaceable parser and rendering caches.</summary>
    public required string CacheDirectory { get; init; }

    /// <summary>Directory for durable local application data, such as recovery state.</summary>
    public required string DataDirectory { get; init; }

    /// <summary>Directory for opt-in local trace files.</summary>
    public required string TraceDirectory { get; init; }

    /// <summary>Compile-time registered theme policy identifier.</summary>
    public required string ThemeId { get; init; }

    /// <summary>Whether local tracing is enabled by the configuration file.</summary>
    public required bool TraceEnabled { get; init; }

    /// <summary>Non-fatal configuration errors and unknown or invalid options.</summary>
    public required IReadOnlyList<ConfigDiagnostic> Diagnostics { get; init; }
}

/// <summary>Explicit load inputs, primarily for portable callers and deterministic tests.</summary>
public sealed record MoteConfigLoadOptions
{
    /// <summary>Absolute user profile directory; null uses the process user profile.</summary>
    public string? UserHomeDirectory { get; init; }

    /// <summary>Absolute mote root, or ~/path; null allows MOTE_HOME to select it.</summary>
    public string? MoteHomeOverride { get; init; }

    /// <summary>Whether MOTE_HOME may override the conventional root.</summary>
    public bool UseEnvironmentOverride { get; init; } = true;
}
