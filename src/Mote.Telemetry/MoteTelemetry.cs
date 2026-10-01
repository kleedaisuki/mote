using System.Diagnostics;
using System.Threading;

namespace Mote.Telemetry;

/// <summary>
/// Local, opt-in instrumentation. The disabled fast path reads one nullable field;
/// no trace object, string, queue item, or file is created.
/// </summary>
public static partial class MoteTelemetry
{
    private static readonly object Gate = new();
    private static readonly ActivitySource Source = new("Mote", "1.0.0");
    private static JsonlTraceSink? _sink;
    private static ActivityListener? _listener;
    private static int _setupFaulted;

    /// <summary>Returns a path-free health snapshot suitable for a diagnostics UI.</summary>
    public static TelemetryHealth Health
    {
        get
        {
            var sink = Volatile.Read(ref _sink);
            return sink is null ? new(false, Volatile.Read(ref _setupFaulted) != 0, 0) : sink.Health;
        }
    }

    /// <summary>
    /// Enables local JSONL tracing only when MOTE_TRACE is exactly 1. An optional
    /// MOTE_TRACE_SUBDIR is a single safe directory name beneath ~/.mote.
    /// </summary>
    public static void ConfigureFromEnvironment(string? outputDirectory = null)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("MOTE_TRACE") != "1") return;
            Configure(new TelemetryOptions
            {
                Enabled = true,
                Subdirectory = outputDirectory is null
                    ? Environment.GetEnvironmentVariable("MOTE_TRACE_SUBDIR") ?? "traces"
                    : "traces",
                OutputDirectory = outputDirectory
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An invalid opt-in setting must not prevent the user's editor from launching.
            Volatile.Write(ref _setupFaulted, 1);
        }
    }

    /// <summary>
    /// Configures one process session. Call once before editor startup work, then
    /// call ShutdownAsync during normal application exit. Reconfiguration requires
    /// a completed shutdown so records cannot switch destinations mid-operation.
    /// </summary>
    public static void Configure(TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled) return;
        Validate(options);
        lock (Gate)
        {
            if (_sink is not null) throw new InvalidOperationException("Telemetry is already configured.");
            var sink = new JsonlTraceSink(options);
            var listener = new ActivityListener
            {
                ShouldListenTo = static source => source.Name == "Mote",
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded
            };
            ActivitySource.AddActivityListener(listener);
            _listener = listener;
            Volatile.Write(ref _setupFaulted, 0);
            Volatile.Write(ref _sink, sink);
        }
    }

    /// <summary>
    /// Starts an Activity-backed operation. Use a using declaration and call
    /// SetStatus before disposal for failures or cancellations. Returns null
    /// when disabled or when shutdown has closed producer admission.
    /// </summary>
    public static TelemetryScope? Start(TelemetryOperation operation, TelemetryDimensions dimensions = default)
    {
        var sink = Volatile.Read(ref _sink);
        if (sink is null || sink.IsFaulted || !sink.TryAcquireProducer()) return null;
        try
        {
            var parent = Activity.Current;
            var activity = parent is null
                ? Source.StartActivity(OperationName(operation), ActivityKind.Internal,
                    new ActivityContext(sink.SessionTraceId, sink.SessionSpanId, ActivityTraceFlags.Recorded))
                : Source.StartActivity(OperationName(operation), ActivityKind.Internal);
            return new TelemetryScope(sink, activity, operation, dimensions);
        }
        catch { sink.ReleaseProducer(); throw; }
    }

    /// <summary>
    /// Starts a child of a previously captured cross-callback mark. This keeps
    /// parse and presentation spans causally attached to the edit that scheduled
    /// them even when work runs on another thread or after an async delay.
    /// Returns null when disabled or shutdown has closed producer admission.
    /// </summary>
    public static TelemetryScope? StartChild(
        TelemetryOperation operation,
        TelemetryMark parent,
        TelemetryDimensions dimensions = default)
    {
        var sink = Volatile.Read(ref _sink);
        if (sink is null || sink.IsFaulted) return null;
        if (!ReferenceEquals(sink, parent.Sink)) return Start(operation, dimensions);
        if (!sink.TryAcquireProducer()) return null;
        try
        {
            var context = new ActivityContext(parent.TraceId, parent.SpanId, ActivityTraceFlags.Recorded);
            var activity = Source.StartActivity(OperationName(operation), ActivityKind.Internal, context);
            return new TelemetryScope(sink, activity, operation, dimensions);
        }
        catch { sink.ReleaseProducer(); throw; }
    }

    /// <summary>
    /// Captures a monotonic timestamp and parent trace context for a duration that
    /// ends in a later callback, such as edit-to-paint. It is inert when disabled.
    /// </summary>
    public static TelemetryMark Mark()
    {
        var sink = Volatile.Read(ref _sink);
        if (sink is null || sink.IsFaulted) return default;
        var parent = Activity.Current;
        return new TelemetryMark(sink, Stopwatch.GetTimestamp(),
            parent?.TraceId ?? sink.SessionTraceId,
            ActivitySpanId.CreateRandom(), parent?.SpanId ?? sink.SessionSpanId);
    }

    /// <summary>
    /// Creates a distinct child interval with the parent's original monotonic
    /// start. Use when one edit has both semantic and native-draw endpoints;
    /// each interval then owns a unique span ID instead of completing a mark twice.
    /// </summary>
    public static TelemetryMark Fork(TelemetryMark parent)
    {
        var sink = Volatile.Read(ref _sink);
        if (sink is null || sink.IsFaulted || !ReferenceEquals(sink, parent.Sink)) return default;
        return new TelemetryMark(sink, parent.Timestamp, parent.TraceId,
            ActivitySpanId.CreateRandom(), parent.SpanId);
    }

    /// <summary>Ends a cross-callback interval without waiting for disk.</summary>
    public static void RecordElapsed(
        TelemetryOperation operation,
        TelemetryMark mark,
        TelemetryDimensions dimensions = default,
        TelemetryStatus status = TelemetryStatus.Success)
    {
        if (!mark.IsActive) return;
        var sink = mark.Sink!;
        if (!sink.TryAcquireProducer()) { sink.RecordRejected(); return; }
        try
        {
            var elapsedUs = Stopwatch.GetElapsedTime(mark.Timestamp).Ticks / 10;
            sink.TryRecord(new TraceRecord(
                DateTimeOffset.UtcNow, mark.TraceId, mark.SpanId, mark.ParentSpanId,
                OperationName(operation), elapsedUs, status, dimensions));
        }
        finally { sink.ReleaseProducer(); }
    }

    /// <summary>Records one numeric, privacy-safe instantaneous event.</summary>
    public static void Record(
        TelemetryEvent kind,
        long value = 0,
        TelemetryDimensions dimensions = default,
        TelemetryStatus status = TelemetryStatus.Success)
    {
        var sink = Volatile.Read(ref _sink);
        if (sink is null || sink.IsFaulted || !sink.TryAcquireProducer()) return;
        try
        {
            var parent = Activity.Current;
            sink.TryRecord(new TraceRecord(
                DateTimeOffset.UtcNow, parent?.TraceId ?? sink.SessionTraceId,
                ActivitySpanId.CreateRandom(), parent?.SpanId ?? sink.SessionSpanId,
                EventName(kind), 0, status, dimensions with { Count = value }));
        }
        finally { sink.ReleaseProducer(); }
    }

    /// <summary>
    /// Records a failed filesystem Save with its original numeric HResult and a
    /// fixed engine phase. Exception text, paths, and arbitrary Data values are
    /// never persisted; non-filesystem failures have no diagnostic code here.
    /// </summary>
    public static void RecordSaveFailure(Exception error)
    {
        var sink = Volatile.Read(ref _sink);
        if (sink is null || sink.IsFaulted || error is not (IOException or UnauthorizedAccessException))
            return;
        if (!sink.TryAcquireProducer()) return;
        try
        {
            string? phase = null;
            try { phase = error.Data["Mote.Engine.SavePhase"] as string; }
            catch (Exception evidenceError) when (evidenceError is not OutOfMemoryException)
            {
                // Optional provider evidence is unavailable; record the primary code as unknown phase.
            }
            var parent = Activity.Current;
            sink.TryRecord(new TraceRecord(
                DateTimeOffset.UtcNow, parent?.TraceId ?? sink.SessionTraceId,
                ActivitySpanId.CreateRandom(), parent?.SpanId ?? sink.SessionSpanId,
                SaveFailureName(phase), 0, TelemetryStatus.Failure, default, error.HResult));
        }
        finally { sink.ReleaseProducer(); }
    }

    /// <summary>
    /// Closes admission, awaits already admitted scopes, then drains and flushes.
    /// Producer completion and writer drain share the caller's single deadline.
    /// Dispose caller-owned scopes before awaiting shutdown. The caller may
    /// supply a short timeout to bound UI shutdown; timed-out writes may be
    /// lost, but a user document save is never blocked by tracing.
    /// </summary>
    public static async Task ShutdownAsync(TimeSpan? timeout = null)
    {
        JsonlTraceSink? sink;
        ActivityListener? listener;
        lock (Gate)
        {
            sink = _sink;
            listener = _listener;
            Volatile.Write(ref _sink, null);
            _listener = null;
        }
        listener?.Dispose();
        if (sink is not null) await sink.ShutdownAsync(timeout ?? TimeSpan.FromSeconds(2)).ConfigureAwait(false);
    }

    private static void Validate(TelemetryOptions options)
    {
        if (options.Subdirectory.Length is < 1 or > 64 ||
            options.Subdirectory is "." or ".." ||
            options.Subdirectory.IndexOfAny(['/', '\\', ':']) >= 0 ||
            options.Subdirectory.Any(static c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("Telemetry subdirectory must be a simple ASCII name.", nameof(options));
        if (options.QueueCapacity is < 8 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(options), "Queue capacity must be between 8 and 1,000,000.");
        if (options.MaxFileBytes is < 4096 or > 1024L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(options), "File size must be between 4 KiB and 1 GiB.");
        if (options.MaxFileAge < TimeSpan.FromMinutes(1) || options.MaxFileAge > TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(options), "File age must be between one minute and 30 days.");
        if (options.MaxFilesPerSession is < 1 or > 128)
            throw new ArgumentOutOfRangeException(nameof(options), "Max files per session must be between 1 and 128.");
    }

    internal static string OperationName(TelemetryOperation operation) => operation switch
    {
        TelemetryOperation.Startup => "mote.startup",
        TelemetryOperation.OpenToEditable => "document.open_to_editable",
        TelemetryOperation.EditToAnalysis => "document.edit_to_analysis",
        TelemetryOperation.EditToPresentation => "document.edit_to_presentation",
        TelemetryOperation.AnalysisToPresentation => "analysis.to_presentation",
        TelemetryOperation.EditToPaint => "document.edit_to_paint",
        TelemetryOperation.Save => "document.save",
        TelemetryOperation.DocumentOpen => "document.open",
        TelemetryOperation.DocumentDecode => "document.decode",
        TelemetryOperation.DocumentEdit => "document.edit",
        TelemetryOperation.AnalysisParse => "analysis.parse",
        TelemetryOperation.AnalysisSemantic => "analysis.semantic",
        TelemetryOperation.AnalysisPublish => "analysis.publish",
        TelemetryOperation.ViewLayout => "view.layout",
        TelemetryOperation.ViewPaint => "view.paint",
        TelemetryOperation.StartupToEditable => "mote.startup_to_editable",
        TelemetryOperation.EditToDrawSubmission => "document.edit_to_draw_submission",
        TelemetryOperation.OpenToDrawSubmission => "document.open_to_draw_submission",
        TelemetryOperation.CommandSave => "command.save",
        TelemetryOperation.CommandSaveAs => "command.save_as",
        TelemetryOperation.SaveGateWait => "save.gate_wait",
        TelemetryOperation.SaveSnapshotCapture => "save.snapshot_capture",
        TelemetryOperation.SaveTargetCheck => "save.target_check",
        TelemetryOperation.SaveTempEncodeWrite => "save.temp_encode_write",
        TelemetryOperation.SaveTempFlush => "save.temp_flush",
        TelemetryOperation.SaveTempHash => "save.temp_hash",
        TelemetryOperation.SaveFinalTargetCheck => "save.final_target_check",
        TelemetryOperation.SaveCommitMove => "save.commit_move",
        TelemetryOperation.SaveCommitReplace => "save.commit_replace",
        TelemetryOperation.SaveSavedStamp => "save.saved_stamp",
        TelemetryOperation.SaveBookkeeping => "save.bookkeeping",
        TelemetryOperation.SaveFailureCleanup => "save.failure_cleanup",
        TelemetryOperation.SaveFailureInspection => "save.failure_inspection",
        TelemetryOperation.NativeSourceInstall => "native.source.install",
        TelemetryOperation.NativeSourceReadback => "native.source.readback",
        TelemetryOperation.NativeSourceReconcile => "native.source.reconcile",
        TelemetryOperation.NativeSourceRangePublish => "native.source.range_publish",
        TelemetryOperation.NativeSourceStylePublish => "native.source.style_publish",

        _ => "unknown"
    };

    private static string EventName(TelemetryEvent kind) => kind switch
    {
        TelemetryEvent.EditCommitted => "edit.committed",
        TelemetryEvent.AnalysisPublished => "analysis.published",
        TelemetryEvent.AnalysisDiscarded => "analysis.discarded",
        TelemetryEvent.SaveCompleted => "save.completed",
        TelemetryEvent.DroppedEvents => "telemetry.dropped",
        TelemetryEvent.CommandReceived => "command.received",
        TelemetryEvent.SaveCompositionSettled => "save.composition_settled",
        TelemetryEvent.SaveCompositionBlocked => "save.composition_blocked",
        TelemetryEvent.SaveControllerEntered => "save.controller_entered",
        TelemetryEvent.SaveAdmitted => "save.admitted",
        TelemetryEvent.SaveWorkerStarted => "save.worker_started",
        TelemetryEvent.SaveOverwriteRequested => "save.overwrite_requested",
        TelemetryEvent.SaveOverwriteApproved => "save.overwrite_approved",
        TelemetryEvent.SaveOverwriteDeclined => "save.overwrite_declined",
        TelemetryEvent.SaveSnapshotCaptured => "save.snapshot_captured",
        TelemetryEvent.SaveUiLocalQueued => "save.ui_local_queued",
        TelemetryEvent.SaveUiWakeRequested => "save.ui_wake_requested",
        TelemetryEvent.SaveUiPostReturned => "save.ui_post_returned",
        TelemetryEvent.SaveUiStarted => "save.ui_started",
        TelemetryEvent.SaveUiDeferred => "save.ui_deferred",
        TelemetryEvent.NativeMenuObservationReady => "native.menu.observation.ready",
        TelemetryEvent.NativeMenuObservationUnavailable => "native.menu.observation.unavailable",
        TelemetryEvent.NativeMenuSaveFamilyEntered => "native.menu.save_family.entered",
        TelemetryEvent.NativeMenuSaveFamilyReturnedTrue => "native.menu.save_family.returned_true",
        TelemetryEvent.NativeMenuSaveFamilyReturnedFalse => "native.menu.save_family.returned_false",
        TelemetryEvent.NativePostedCallbackFailed => "native.posted.callback.failed",
        TelemetryEvent.NativePostedCallbackReportFailed => "native.posted.callback.report_failed",
        TelemetryEvent.NativeInputMonitorReady => "native.input.monitor.ready",
        TelemetryEvent.NativeInputMonitorUnavailable => "native.input.monitor.unavailable",
        TelemetryEvent.NativeInputSaveFamilyCandidate => "native.input.save_family_candidate",
        TelemetryEvent.NativeInputMonitorCallbackFailed => "native.input.monitor.callback_failed",
        TelemetryEvent.NativeInputMonitorRemoved => "native.input.monitor.removed",
        TelemetryEvent.NativeInputMonitorRemovalFailed => "native.input.monitor.removal_failed",

        _ => "unknown"
    };

    /// <summary>Never derives a trace operation from exception-provided text.</summary>
    private static string SaveFailureName(string? phase) => phase switch
    {
        "TargetCheck" => "save.failure.target_check",
        "TempWriteAndHash" => "save.failure.temp_write_and_hash",
        "FinalTargetCheck" => "save.failure.final_target_check",
        "Move" => "save.failure.move",
        "Replace" => "save.failure.replace",
        "Cleanup" => "save.failure.cleanup",
        "SavedStamp" => "save.failure.saved_stamp",
        _ => "save.failure.unknown"
    };
}

/// <summary>A delayed duration marker, safe to carry across UI callbacks.</summary>
public readonly struct TelemetryMark
{
    internal TelemetryMark(JsonlTraceSink sink, long timestamp, ActivityTraceId traceId,
        ActivitySpanId spanId, ActivitySpanId parentSpanId)
    {
        Sink = sink;
        Timestamp = timestamp;
        TraceId = traceId;
        SpanId = spanId;
        ParentSpanId = parentSpanId;
    }

    internal JsonlTraceSink? Sink { get; }
    internal long Timestamp { get; }
    internal ActivityTraceId TraceId { get; }
    internal ActivitySpanId SpanId { get; }
    internal ActivitySpanId ParentSpanId { get; }
    internal bool IsActive => Sink is not null;
}

/// <summary>
/// One admitted Activity-backed interval. Disposal records once and releases
/// admission; an undisposed scope can consume the bounded shutdown budget.
/// </summary>
public sealed class TelemetryScope : IDisposable
{
    private readonly JsonlTraceSink _sink;
    private readonly Activity? _activity;
    private readonly TelemetryOperation _operation;
    private readonly TelemetryDimensions _dimensions;
    private readonly long _started;
    private int _disposed;
    private TelemetryStatus _status;

    internal TelemetryScope(JsonlTraceSink sink, Activity? activity, TelemetryOperation operation, TelemetryDimensions dimensions)
    {
        _sink = sink;
        _activity = activity;
        _operation = operation;
        _dimensions = dimensions;
        _started = Stopwatch.GetTimestamp();
    }

    /// <summary>Sets the final status before disposal.</summary>
    public void SetStatus(TelemetryStatus status)
    {
        _status = status;
        if (status == TelemetryStatus.Failure)
            _activity?.SetStatus(ActivityStatusCode.Error);
        else if (status == TelemetryStatus.Success)
            _activity?.SetStatus(ActivityStatusCode.Ok);
        else
            _activity?.SetTag("mote.status", status == TelemetryStatus.Cancelled ? "cancelled" : "skipped");
    }

    /// <summary>Stops the Activity and enqueues its monotonic duration once.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var durationUs = Stopwatch.GetElapsedTime(_started).Ticks / 10;
        var traceId = _activity?.TraceId ?? _sink.SessionTraceId;
        var spanId = _activity?.SpanId ?? ActivitySpanId.CreateRandom();
        var parentSpanId = _activity?.ParentSpanId ?? _sink.SessionSpanId;
        try
        {
            _activity?.Dispose();
            _sink.TryRecord(new TraceRecord(DateTimeOffset.UtcNow, traceId, spanId,
                parentSpanId, MoteTelemetry.OperationName(_operation), durationUs, _status, _dimensions));
        }
        finally { _sink.ReleaseProducer(); }
    }
}
