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
    OpenToDrawSubmission,
    /// <summary>Explicit, content-free Save request or engine phase interval.</summary>
    CommandSave,
    /// <summary>Explicit, content-free Save request or engine phase interval.</summary>
    CommandSaveAs,
    /// <summary>Explicit, content-free Save request or engine phase interval.</summary>
    SaveGateWait,
    /// <summary>Explicit, content-free Save request or engine phase interval.</summary>
    SaveSnapshotCapture,
    /// <summary>Explicit, content-free Save request or engine phase interval.</summary>
    SaveTargetCheck,
    /// <summary>Explicit, content-free Save request or engine phase interval.</summary>
    SaveTempEncodeWrite,
    /// <summary>Explicit, content-free Save request or engine phase interval.</summary>
    SaveTempFlush,
    /// <summary>Explicit, content-free Save request or engine phase interval.</summary>
    SaveTempHash,
    /// <summary>Explicit, content-free Save request or engine phase interval.</summary>
    SaveFinalTargetCheck,
    /// <summary>Explicit, content-free Save request or engine phase interval.</summary>
    SaveCommitMove,
    /// <summary>Explicit, content-free Save request or engine phase interval.</summary>
    SaveCommitReplace,
    /// <summary>Explicit, content-free Save request or engine phase interval.</summary>
    SaveSavedStamp,
    /// <summary>Explicit, content-free Save request or engine phase interval.</summary>
    SaveBookkeeping,
    /// <summary>Explicit, content-free Save request or engine phase interval.</summary>
    SaveFailureCleanup,
    /// <summary>Explicit, content-free Save request or engine phase interval.</summary>
    SaveFailureInspection,
}

/// <summary>Fixed, low-cardinality instantaneous event names.</summary>
public enum TelemetryEvent
{
    EditCommitted,
    AnalysisPublished,
    AnalysisDiscarded,
    SaveCompleted,
    DroppedEvents,
    /// <summary>Target-owned Save boundary; success certifies entry only.</summary>
    CommandReceived,
    /// <summary>Target-owned Save boundary; success certifies entry only.</summary>
    SaveCompositionSettled,
    /// <summary>Target-owned Save boundary; success certifies entry only.</summary>
    SaveCompositionBlocked,
    /// <summary>Target-owned Save boundary; success certifies entry only.</summary>
    SaveControllerEntered,
    /// <summary>Target-owned Save boundary; success certifies entry only.</summary>
    SaveAdmitted,
    /// <summary>Target-owned Save boundary; success certifies entry only.</summary>
    SaveWorkerStarted,
    /// <summary>Target-owned Save boundary; success certifies entry only.</summary>
    SaveOverwriteRequested,
    /// <summary>Target-owned Save boundary; success certifies entry only.</summary>
    SaveOverwriteApproved,
    /// <summary>Target-owned Save boundary; success certifies entry only.</summary>
    SaveOverwriteDeclined,
    /// <summary>Target-owned Save boundary; success certifies entry only.</summary>
    SaveSnapshotCaptured,
    /// <summary>Target-owned Save boundary; success certifies entry only.</summary>
    SaveUiLocalQueued,
    /// <summary>Target-owned Save boundary; success certifies entry only.</summary>
    SaveUiWakeRequested,
    /// <summary>Target-owned Save boundary; success certifies entry only.</summary>
    SaveUiPostReturned,
    /// <summary>Target-owned Save boundary; success certifies entry only.</summary>
    SaveUiStarted,
    /// <summary>Target-owned Save boundary; success certifies entry only.</summary>
    SaveUiDeferred,
    /// <summary>Owned observed main menu installed; not universal input coverage.</summary>
    NativeMenuObservationReady,
    /// <summary>Optional menu instrumentation setup failed; not command failure.</summary>
    NativeMenuObservationUnavailable,
    /// <summary>Owned menu received a Save-family candidate, not a Save request.</summary>
    NativeMenuSaveFamilyEntered,
    /// <summary>Superclass handled this menu call; not successful Save.</summary>
    NativeMenuSaveFamilyReturnedTrue,
    /// <summary>Superclass did not handle this menu call; not failed Save.</summary>
    NativeMenuSaveFamilyReturnedFalse,
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

/// <summary>Fixed, content-free terminal reason; None leaves the attribute absent.</summary>
public enum TelemetryReason
{
    /// <summary>Fixed request disposition; never derived from user or exception text.</summary>
    None,
    /// <summary>Fixed request disposition; never derived from user or exception text.</summary>
    Completed,
    /// <summary>Fixed request disposition; never derived from user or exception text.</summary>
    ViewDeferred,
    /// <summary>Fixed request disposition; never derived from user or exception text.</summary>
    CompositionBlocked,
    /// <summary>Fixed request disposition; never derived from user or exception text.</summary>
    MissingHandler,
    /// <summary>Fixed request disposition; never derived from user or exception text.</summary>
    CallbackFailed,
    /// <summary>Fixed request disposition; never derived from user or exception text.</summary>
    AlreadySaving,
    /// <summary>Fixed request disposition; never derived from user or exception text.</summary>
    PickerCancelled,
    /// <summary>Fixed request disposition; never derived from user or exception text.</summary>
    RecoveryRedirected,
    /// <summary>Fixed request disposition; never derived from user or exception text.</summary>
    OverwriteDeclined,
    /// <summary>Fixed request disposition; never derived from user or exception text.</summary>
    OperationCancelled,
    /// <summary>Fixed request disposition; never derived from user or exception text.</summary>
    StaleDocument,
    /// <summary>Fixed request disposition; never derived from user or exception text.</summary>
    LifetimeEnded,
    /// <summary>Fixed request disposition; never derived from user or exception text.</summary>
    SaveFailed,
    /// <summary>Fixed request disposition; never derived from user or exception text.</summary>
    UiPostFailed,
}
