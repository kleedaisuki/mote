namespace Mote.Telemetry;

/// <summary>Fixed, low-cardinality operation names accepted by local tracing.</summary>
public enum TelemetryOperation
{
    Startup,
    OpenToEditable,
    EditToAnalysis,
    EditToPresentation,
    AnalysisToPresentation,
    EditToPaint,
    Save,
    DocumentOpen,
    DocumentDecode,
    DocumentEdit,
    AnalysisParse,
    AnalysisSemantic,
    AnalysisPublish,
    ViewLayout,
    ViewPaint,
    /// <summary>Instrumented startup through the first installed editable source view, not process launch.</summary>
    StartupToEditable,
    /// <summary>Accepted source edit through return from the first matching native source draw callback.</summary>
    EditToDrawSubmission,
    /// <summary>Open request through return from the first matching native source draw callback.</summary>
    OpenToDrawSubmission
}

/// <summary>Fixed, low-cardinality instantaneous event names.</summary>
public enum TelemetryEvent
{
    EditCommitted,
    AnalysisPublished,
    AnalysisDiscarded,
    SaveCompleted,
    DroppedEvents
}

/// <summary>Normalized document format; filenames and extensions are never accepted.</summary>
public enum TelemetryFormat
{
    Unknown,
    PlainText,
    Markdown,
    Toml,
    Json,
    Yaml,
    Csv
}

/// <summary>Outcome of one operation or event.</summary>
public enum TelemetryStatus
{
    Success,
    Failure,
    Cancelled,
    Skipped
}

/// <summary>
/// Numeric, privacy-safe dimensions carried by a trace record.
/// Null means absent; no arbitrary text or path field exists.
/// </summary>
public readonly record struct TelemetryDimensions(
    TelemetryFormat Format = TelemetryFormat.Unknown,
    long? DocumentBytes = null,
    long? Version = null,
    long? Count = null);

/// <summary>Configuration for an explicit, local-only telemetry session.</summary>
public sealed class TelemetryOptions
{
    /// <summary>Enables persistence; false means instrumentation is a cheap no-op.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Optional single directory name under ~/.mote. Slashes, parent traversal,
    /// and absolute paths are rejected. The default resolves to ~/.mote/traces.
    /// </summary>
    public string Subdirectory { get; init; } = "traces";

    /// <summary>
    /// Explicit absolute destination override for a test or local benchmark.
    /// Environment configuration never reads this value.
    /// </summary>
    public string? OutputDirectory { get; init; }

    /// <summary>Optional opaque benchmark run identifier, never a free-form label.</summary>
    public Guid? RunId { get; init; }

    /// <summary>Maximum queued records; producers never wait when it fills.</summary>
    public int QueueCapacity { get; init; } = 4096;

    /// <summary>Approximate maximum bytes per JSONL file before rotation.</summary>
    public long MaxFileBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Maximum age of an open file before rotation.</summary>
    public TimeSpan MaxFileAge { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Maximum number of files retained per telemetry session.</summary>
    public int MaxFilesPerSession { get; init; } = 8;
}

/// <summary>Health snapshot without filesystem paths or exception messages.</summary>
public readonly record struct TelemetryHealth(bool Enabled, bool SinkFaulted, long DroppedRecords);
