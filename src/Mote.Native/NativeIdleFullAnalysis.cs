using Mote.Engine;
using Mote.Formats;

namespace Mote.Native;

/// <summary>Why a visible result did or did not enqueue an idle Full pass.</summary>
internal enum IdleFullOffer
{
    Scheduled,
    AlreadyPendingOrAttempted,
    Complete,
    PolicyLimited,
    MemoryLimited
}

/// <summary>
/// Gives a stable, partially analyzed document one opportunistic whole-document
/// semantic pass without delaying the visible-page analysis lane.
/// </summary>
/// <remarks>
/// This scheduler belongs to one document and its format driver. The driver
/// serializes policy calls; the controller must cancel this lane before an edit
/// or viewport interaction and must validate document identity and version on
/// the UI thread before publishing the callback result. A Full request is only
/// a request: its returned completeness remains the policy's claim.
/// </remarks>
internal sealed class NativeIdleFullAnalysis : IDisposable
{
    private const int SmallLimit = 4 * 1024 * 1024;
    private const int MediumLimit = 32 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly NativeFormatSessionDriver _driver;
    private readonly DocumentKind _kind;
    private readonly Action<TextSnapshot, DocumentAnalysis, TextSpan> _publish;
    private readonly Action<TextSnapshot, NativeFormatPresentation, TextSpan>? _publishPresentation;
    private readonly Action<TextSnapshot, Exception>? _onError;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private Pending? _pending;
    private long? _attemptedVersion;
    private bool _disposed;

    /// <summary>Creates an idle lane for one driver and one document lifetime.</summary>
    /// <param name="driver">The driver's serialized format session.</param>
    /// <param name="kind">The active compile-time document policy kind.</param>
    /// <param name="publish">Called on a worker thread with the offered viewport for a successful Full result.</param>
    /// <param name="onError">Optional worker-thread observer for noncancellation failures.</param>
    /// <param name="delay">Optional test clock; production uses cancellable <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    public NativeIdleFullAnalysis(NativeFormatSessionDriver driver, DocumentKind kind,
        Action<TextSnapshot, DocumentAnalysis, TextSpan> publish,
        Action<TextSnapshot, Exception>? onError = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Action<TextSnapshot, NativeFormatPresentation, TextSpan>? publishPresentation = null)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        if (driver.Kind != kind)
            throw new ArgumentException("The idle lane and format driver must use the same policy.", nameof(kind));
        _kind = kind;
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _publishPresentation = publishPresentation;
        _onError = onError;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>
    /// Offers a validated visible result. Repeated offers for its version do not
    /// restart the idle timer or repeat a completed Full attempt.
    /// </summary>
    public IdleFullOffer Offer(TextSnapshot snapshot, DocumentAnalysis visibleResult,
        TextSpan visibleRange)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(visibleResult);
        if (visibleResult.Version != snapshot.Version)
            throw new ArgumentException("The visible result must describe the offered snapshot.",
                nameof(visibleResult));
        if (visibleRange.Start < 0 || visibleRange.Start > snapshot.Length ||
            visibleRange.Length < 0 || visibleRange.Length > snapshot.Length - visibleRange.Start)
            throw new ArgumentOutOfRangeException(nameof(visibleRange));

        lock (_gate)
        {
            if (_disposed) return IdleFullOffer.AlreadyPendingOrAttempted;
            if (visibleResult.Completeness == AnalysisCompleteness.Complete)
            {
                CancelPending();
                _attemptedVersion = snapshot.Version;
                return IdleFullOffer.Complete;
            }
            if (!CanRun()) return IdleFullOffer.PolicyLimited;
            if (_attemptedVersion == snapshot.Version)
                return IdleFullOffer.AlreadyPendingOrAttempted;
            if (_pending is { } current)
            {
                if (current.Snapshot.Version == snapshot.Version)
                    return IdleFullOffer.AlreadyPendingOrAttempted;
                CancelPending();
            }
            var memory = GC.GetGCMemoryInfo();
            if (!HasMemoryBudget(_kind, snapshot.Length,
                memory.TotalAvailableMemoryBytes, memory.HighMemoryLoadThresholdBytes,
                memory.MemoryLoadBytes, snapshot.LineCount)) return IdleFullOffer.MemoryLimited;
            var pending = new Pending(snapshot);
            _pending = pending;
            _ = RunAsync(pending, visibleRange);
            return IdleFullOffer.Scheduled;
        }
    }

    /// <summary>
    /// Cancels a queued or running pass without waiting for a format policy on the
    /// native event thread. A canceled attempt may be offered again after idle.
    /// </summary>
    public void Cancel()
    {
        lock (_gate) CancelPending();
    }

    /// <summary>Ends the lane on close or document/policy replacement without blocking.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CancelPending();
        }
    }

    /// <summary>
    /// Offers Full only to policies with a cancellable, resource-admitted path.
    /// CSV's cold Visible pass indexes only a bounded prefix. Its cancellable
    /// Full pass is needed to discover off-page ragged rows and exact counts.
    /// Markdown's large-file policy certifies only a restricted flat subset and
    /// returns Provisional for structures it cannot prove within its budget.
    /// </summary>
    private bool CanRun() => _kind switch
    {
        DocumentKind.Json or DocumentKind.Yaml or DocumentKind.Toml or DocumentKind.Csv or
            DocumentKind.Markdown => true,
        _ => false
    };

    /// <summary>
    /// Applies an advisory work-memory budget using the GC's last physical-load
    /// observation. Zero/unknown measurements cannot safely veto a pass. This
    /// avoids a known high-allocation TOML certification on a pressured host;
    /// it is not an OOM guarantee because other processes may allocate later.
    /// </summary>
    internal static bool HasMemoryBudget(DocumentKind kind, int length,
        long totalAvailable, long highLoadThreshold, long memoryLoad,
        int physicalLineCount = 0)
    {
        const long fixedReserve = 128L * 1024 * 1024;
        var factor = kind switch
        {
            DocumentKind.Toml => 6L,
            DocumentKind.Markdown => 8L,
            _ => 4L
        };
        // CSV retains one 24-byte Row per logical record. Logical records
        // cannot outnumber physical lines, even when quoted cells span lines.
        // The 32-byte envelope includes block-array/list overhead and avoids
        // admitting a newline-dense 100 MiB file on a memory-poor host while
        // keeping ordinary wide-row files eligible for global diagnostics.
        var rowEnvelope = kind == DocumentKind.Csv
            ? checked(32L * Math.Max(0, physicalLineCount)) : 0;
        var estimatedWork = checked(fixedReserve +
            Math.Max(checked(factor * length), rowEnvelope));
        if (totalAvailable > 0 && estimatedWork > totalAvailable / 2) return false;
        if (highLoadThreshold > 0 && memoryLoad > 0)
        {
            var headroom = Math.Max(0, highLoadThreshold - memoryLoad);
            if (estimatedWork > headroom - headroom / 5) return false;
        }
        return true;
    }

    /// <summary>
    /// Gives larger files a longer quiet interval. In particular a 100 MiB file
    /// cannot start another complete scan merely because a short idle tick fired.
    /// </summary>
    private static TimeSpan DelayFor(int length) => length switch
    {
        <= SmallLimit => TimeSpan.FromSeconds(1),
        <= MediumLimit => TimeSpan.FromSeconds(3),
        _ => TimeSpan.FromSeconds(15)
    };

    /// <summary>Runs timer and Full pass outside the UI thread and owns CTS retirement.</summary>
    private async Task RunAsync(Pending pending, TextSpan visibleRange)
    {
        try
        {
            await _delay(DelayFor(pending.Snapshot.Length), pending.Cancellation.Token)
                .ConfigureAwait(false);
            lock (_gate)
            {
                pending.Cancellation.Token.ThrowIfCancellationRequested();
                if (_disposed || !ReferenceEquals(_pending, pending)) return;
                pending.Started = true;
                _attemptedVersion = pending.Snapshot.Version;
            }
            var request = new AnalysisRequest(visibleRange, AnalysisScope.Full);
            var presentation = _publishPresentation is null ? null :
                await _driver.AnalyzePresentationAsync(pending.Snapshot, request,
                    pending.Cancellation.Token).ConfigureAwait(false);
            var result = presentation?.Analysis ??
                await _driver.AnalyzeAsync(pending.Snapshot, request,
                    pending.Cancellation.Token).ConfigureAwait(false);
            pending.Cancellation.Token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(_pending, pending)) return;
            }
            if (presentation is not null)
                _publishPresentation!(pending.Snapshot, presentation, visibleRange);
            else _publish(pending.Snapshot, result, visibleRange);
        }
        catch (OperationCanceledException) when (pending.Cancellation.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (pending.Cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _onError?.Invoke(pending.Snapshot, exception);
        }
        finally
        {
            Task? cancellation;
            lock (_gate)
            {
                if (ReferenceEquals(_pending, pending)) _pending = null;
                cancellation = pending.CancellationTask;
            }
            if (cancellation is not null)
            {
                try { await cancellation.ConfigureAwait(false); }
                catch (AggregateException) { }
            }
            pending.Cancellation.Dispose();
        }
    }

    /// <summary>Must be called under the scheduler lock.</summary>
    private void CancelPending()
    {
        var pending = _pending;
        if (pending is null) return;
        _pending = null;
        if (pending.Started && _attemptedVersion == pending.Snapshot.Version)
            _attemptedVersion = null;
        pending.CancellationTask = pending.Cancellation.CancelAsync();
    }

    /// <summary>One timer or parser request and its cancellation ownership.</summary>
    private sealed class Pending(TextSnapshot snapshot)
    {
        /// <summary>Immutable snapshot retained until the request retires.</summary>
        public TextSnapshot Snapshot { get; } = snapshot;
        /// <summary>Cancellation resource disposed by the running task.</summary>
        public CancellationTokenSource Cancellation { get; } = new();
        /// <summary>Whether the driver's Full call has been attempted.</summary>
        public bool Started { get; set; }
        /// <summary>Completion of asynchronous cancellation callbacks, when requested.</summary>
        public Task? CancellationTask { get; set; }
    }
}
