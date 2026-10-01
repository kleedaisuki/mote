using System.Diagnostics;

namespace Mote.Telemetry;

public static partial class MoteTelemetry
{
    /// <summary>
    /// Starts an explicit native request and immediately records receipt. Returns
    /// null without allocating when tracing is disabled. No ambient Activity or
    /// producer lease survives this call; the caller owns the request lifetime.
    /// </summary>
    /// <example><code>
    /// var request = MoteTelemetry.BeginRequest(TelemetryOperation.CommandSave);
    /// request?.Checkpoint(TelemetryEvent.SaveAdmitted);
    /// request?.EndOnce(TelemetryStatus.Success, TelemetryReason.Completed);
    /// </code></example>
    public static TelemetryRequest? BeginRequest(
        TelemetryOperation operation, TelemetryDimensions dimensions = default)
    {
        var sink = Volatile.Read(ref _sink);
        if (sink is null || sink.IsFaulted || !sink.TryAcquireProducer()) return null;
        try
        {
            if (operation is not (TelemetryOperation.CommandSave or TelemetryOperation.CommandSaveAs))
                throw new ArgumentOutOfRangeException(nameof(operation));
            var mark = new TelemetryMark(sink, Stopwatch.GetTimestamp(), sink.SessionTraceId,
                ActivitySpanId.CreateRandom(), sink.SessionSpanId);
            var request = new TelemetryRequest(operation, mark, dimensions);
            WriteEntry(sink, EventName(TelemetryEvent.CommandReceived), mark, dimensions);
            return request;
        }
        finally { sink.ReleaseProducer(); }
    }

    /// <summary>
    /// Creates a current-time explicit child mark in its original session. A stale
    /// context is rejected and counted in the original sink, never redirected.
    /// No ambient Activity is consulted or installed and no enqueue lease is held.
    /// </summary>
    public static TelemetryMark MarkChild(TelemetryMark parent)
    {
        if (!TryAcquireOriginal(parent, out var sink)) return default;
        try { return NewChild(sink!, parent); }
        finally { sink!.ReleaseProducer(); }
    }

    /// <summary>
    /// Records a fresh instantaneous child under an explicit parent. Its zero
    /// duration and successful status mean that boundary executed, not that its
    /// enclosing request succeeded. Disabled/default parents allocate nothing.
    /// </summary>
    public static void RecordChild(TelemetryEvent kind, TelemetryMark parent,
        TelemetryDimensions dimensions = default, TelemetryStatus status = TelemetryStatus.Success)
    {
        if (!TryAcquireOriginal(parent, out var sink)) return;
        try { WriteCheckpoint(sink!, EventName(kind), parent, dimensions, status); }
        finally { sink!.ReleaseProducer(); }
    }

    /// <summary>
    /// Starts a nonambient duration child and enqueues its fixed .entered anchor
    /// checkpoint before returning. Only Save duration operations are supported;
    /// the caller must end each returned phase at most once. No long lease exists.
    /// </summary>
    public static TelemetryMark BeginPhase(TelemetryOperation operation, TelemetryMark parent,
        TelemetryDimensions dimensions = default)
    {
        if (!TryAcquireOriginal(parent, out var sink)) return default;
        try
        {
            var mark = NewChild(sink!, parent);
            WriteEntry(sink!, PhaseEntryName(operation), mark, dimensions);
            return mark;
        }
        finally { sink!.ReleaseProducer(); }
    }

    /// <summary>
    /// Ends an explicitly owned Save phase without ambient context or disk waits.
    /// The adapter supplies a numeric filesystem HResult only on failed phases;
    /// exception messages and arbitrary diagnostic data are not accepted.
    /// </summary>
    public static void EndPhase(TelemetryOperation operation, TelemetryMark mark,
        TelemetryDimensions dimensions = default, TelemetryStatus status = TelemetryStatus.Success,
        int? hresult = null)
    {
        if (!TryAcquireOriginal(mark, out var sink)) return;
        try
        {
            _ = PhaseEntryName(operation);
            sink!.TryRecord(new TraceRecord(DateTimeOffset.UtcNow, mark.TraceId,
                ActivitySpanId.CreateRandom(), mark.SpanId, OperationName(operation),
                Stopwatch.GetElapsedTime(mark.Timestamp).Ticks / 10, status, dimensions,
                status == TelemetryStatus.Failure ? hresult : null));
        }
        finally { sink!.ReleaseProducer(); }
    }

    /// <summary>Acquires only a short lease on the context's original active sink.</summary>
    private static bool TryAcquireOriginal(TelemetryMark mark, out JsonlTraceSink? sink)
    {
        sink = mark.Sink;
        if (sink is null) return false;
        if (!ReferenceEquals(Volatile.Read(ref _sink), sink) || sink.IsFaulted || !sink.TryAcquireProducer())
        {
            sink.RecordRejected();
            return false;
        }
        return true;
    }

    /// <summary>Creates a fresh identity without inheriting unrelated ambient work.</summary>
    private static TelemetryMark NewChild(JsonlTraceSink sink, TelemetryMark parent) =>
        new(sink, Stopwatch.GetTimestamp(), parent.TraceId, ActivitySpanId.CreateRandom(), parent.SpanId);

    /// <summary>Persists the causal anchor before work; terminal durations own distinct child IDs.</summary>
    private static void WriteEntry(JsonlTraceSink sink, string operation, TelemetryMark mark,
        TelemetryDimensions dimensions) =>
        sink.TryRecord(new TraceRecord(DateTimeOffset.UtcNow, mark.TraceId, mark.SpanId,
            mark.ParentSpanId, operation, 0, TelemetryStatus.Success, dimensions));

    /// <summary>Each checkpoint is a distinct event, never a partial duration record.</summary>
    private static void WriteCheckpoint(JsonlTraceSink sink, string operation, TelemetryMark parent,
        TelemetryDimensions dimensions, TelemetryStatus status) =>
        sink.TryRecord(new TraceRecord(DateTimeOffset.UtcNow, parent.TraceId,
            ActivitySpanId.CreateRandom(), parent.SpanId, operation, 0, status, dimensions));

    /// <summary>Records the single terminal duration as a distinct child of the persisted request anchor.</summary>
    internal static void EndRequest(TelemetryOperation operation, TelemetryMark mark,
        TelemetryDimensions dimensions, TelemetryStatus status, TelemetryReason reason)
    {
        if (!TryAcquireOriginal(mark, out var sink)) return;
        try
        {
            sink!.TryRecord(new TraceRecord(DateTimeOffset.UtcNow, mark.TraceId, ActivitySpanId.CreateRandom(),
                mark.SpanId, OperationName(operation), Stopwatch.GetElapsedTime(mark.Timestamp).Ticks / 10,
                status, dimensions, Reason: reason));
        }
        finally { sink!.ReleaseProducer(); }
    }

    /// <summary>Closed operation-specific entry names, not user-derived strings.</summary>
    private static string PhaseEntryName(TelemetryOperation operation) => operation switch
    {
        TelemetryOperation.Save => "document.save.entered",
        TelemetryOperation.SaveGateWait => "save.gate_wait.entered",
        TelemetryOperation.SaveSnapshotCapture => "save.snapshot_capture.entered",
        TelemetryOperation.SaveTargetCheck => "save.target_check.entered",
        TelemetryOperation.SaveTempEncodeWrite => "save.temp_encode_write.entered",
        TelemetryOperation.SaveTempFlush => "save.temp_flush.entered",
        TelemetryOperation.SaveTempHash => "save.temp_hash.entered",
        TelemetryOperation.SaveFinalTargetCheck => "save.final_target_check.entered",
        TelemetryOperation.SaveCommitMove => "save.commit_move.entered",
        TelemetryOperation.SaveCommitReplace => "save.commit_replace.entered",
        TelemetryOperation.SaveSavedStamp => "save.saved_stamp.entered",
        TelemetryOperation.SaveBookkeeping => "save.bookkeeping.entered",
        TelemetryOperation.SaveFailureCleanup => "save.failure_cleanup.entered",
        TelemetryOperation.SaveFailureInspection => "save.failure_inspection.entered",
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    /// <summary>Returns only allowlisted terminal reason values; None/unknown are omitted.</summary>
    internal static string? ReasonName(TelemetryReason reason) => reason switch
    {
        TelemetryReason.Completed => "completed",
        TelemetryReason.ViewDeferred => "view_deferred",
        TelemetryReason.CompositionBlocked => "composition_blocked",
        TelemetryReason.MissingHandler => "missing_handler",
        TelemetryReason.CallbackFailed => "callback_failed",
        TelemetryReason.AlreadySaving => "already_saving",
        TelemetryReason.PickerCancelled => "picker_cancelled",
        TelemetryReason.RecoveryRedirected => "recovery_redirected",
        TelemetryReason.OverwriteDeclined => "overwrite_declined",
        TelemetryReason.OperationCancelled => "operation_cancelled",
        TelemetryReason.StaleDocument => "stale_document",
        TelemetryReason.LifetimeEnded => "lifetime_ended",
        TelemetryReason.SaveFailed => "save_failed",
        TelemetryReason.UiPostFailed => "ui_post_failed",
        _ => null
    };
}

/// <summary>
/// Explicit enabled-only request owner containing identity and fixed dimensions,
/// never editor state. EndOnce atomically selects one terminal enqueue attempt;
/// queue loss or shutdown rejection is not retried into a different session.
/// </summary>
public sealed class TelemetryRequest
{
    private readonly TelemetryOperation _operation;
    private readonly TelemetryDimensions _dimensions;
    private int _ended;

    /// <summary>Captures only content-free request identity and initial dimensions.</summary>
    internal TelemetryRequest(TelemetryOperation operation, TelemetryMark mark, TelemetryDimensions dimensions)
    {
        _operation = operation;
        Mark = mark;
        _dimensions = dimensions;
    }

    /// <summary>Explicit parent identity for work scheduled in a later callback.</summary>
    public TelemetryMark Mark { get; }

    /// <summary>Whether a terminal disposition has been selected, even if transport rejected it.</summary>
    public bool IsEnded => Volatile.Read(ref _ended) != 0;

    /// <summary>Records a distinct target boundary under this request's original identity.</summary>
    public void Checkpoint(TelemetryEvent kind, TelemetryDimensions dimensions = default,
        TelemetryStatus status = TelemetryStatus.Success) => MoteTelemetry.RecordChild(kind, Mark, dimensions, status);

    /// <summary>Starts an explicit current-time duration child; the caller owns phase completion.</summary>
    public TelemetryMark BeginPhase(TelemetryOperation operation, TelemetryDimensions dimensions = default) =>
        MoteTelemetry.BeginPhase(operation, Mark, dimensions);

    /// <summary>Ends a phase owned by this request using the explicit phase mark.</summary>
    public void EndPhase(TelemetryOperation operation, TelemetryMark mark,
        TelemetryDimensions dimensions = default, TelemetryStatus status = TelemetryStatus.Success,
        int? hresult = null) => MoteTelemetry.EndPhase(operation, mark, dimensions, status, hresult);

    /// <summary>
    /// Selects the terminal record once across competing completions. Returns true
    /// only for the winning attempt, not proof of persistence. Default dimensions
    /// retain initial request dimensions; supply final dimensions when known.
    /// </summary>
    public bool EndOnce(TelemetryStatus status, TelemetryReason reason = TelemetryReason.None,
        TelemetryDimensions dimensions = default)
    {
        if (Interlocked.CompareExchange(ref _ended, 1, 0) != 0) return false;
        MoteTelemetry.EndRequest(_operation, Mark, dimensions == default ? _dimensions : dimensions, status, reason);
        return true;
    }
}
