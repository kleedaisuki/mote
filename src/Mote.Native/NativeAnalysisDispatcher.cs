using Mote.Engine;
using Mote.Formats;

namespace Mote.Native;

/// <summary>A reserved Full turn could not pass the dispatch-time resource check.</summary>
internal sealed class NativeFullAnalysisDeferredException : Exception
{
    /// <summary>Creates a resource refusal without claiming parser failure.</summary>
    public NativeFullAnalysisDeferredException() : base("Full analysis deferred by memory admission.") { }
}

/// <summary>
/// Serializes one document's policy turns with bounded latest-interest slots.
/// Content has priority over reserved Full, which has priority over viewport work.
/// </summary>
/// <remarks>
/// Content registers immediately with its edit-derived delay; viewport input never resets that deadline.
/// Only one pump and one driver call run; replaced interests complete as canceled.
/// The driver remains externally owned and must outlive this dispatcher.
/// </remarks>
internal sealed class NativeAnalysisDispatcher : IDisposable
{
    /// <summary>Protects slot replacement and pump ownership; no policy call executes under this lock.</summary>
    private readonly object _gate = new();
    /// <summary>Externally owned single parser session used only by the pump.</summary>
    private readonly NativeFormatSessionDriver _driver;
    /// <summary>Advisory resource admission repeated at the Full dispatch boundary.</summary>
    private readonly Func<TextSnapshot, bool> _fullAdmission;
    /// <summary>Latest mandatory edit interest; navigation cannot replace it.</summary>
    private Work? _content;
    /// <summary>Latest coordinate delivery interest, superseded independently of certification.</summary>
    private Work? _viewport;
    /// <summary>Reserved certification turn ahead of all remaining viewport interests.</summary>
    private Work? _full;
    /// <summary>The only dispatched work, retired before another driver call begins.</summary>
    private Work? _running;
    /// <summary>Monotonic accepted snapshot version; older submissions cannot resurrect work.</summary>
    private long _version = -1;
    /// <summary>Exactly one pump owns nonempty slots, including content debounce waits.</summary>
    private bool _pumping;
    /// <summary>Prevents new admissions after document lifetime retirement.</summary>
    private bool _disposed;

    /// <summary>Creates a document-local dispatcher with a dispatch-time Full resource check.</summary>
    public NativeAnalysisDispatcher(NativeFormatSessionDriver driver,
        Func<TextSnapshot, bool>? fullAdmission = null)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _fullAdmission = fullAdmission ?? (snapshot =>
        {
            var memory = GC.GetGCMemoryInfo();
            return NativeIdleFullAnalysis.HasMemoryBudget(driver.Kind, snapshot.Length,
                memory.TotalAvailableMemoryBytes, memory.HighMemoryLoadThresholdBytes,
                memory.MemoryLoadBytes, snapshot.LineCount);
        });
    }

    /// <summary>Enqueues the latest content or viewport interest, replacing only its own slot.</summary>
    public Task<NativeFormatPresentation> AnalyzeAsync(TextSnapshot snapshot, AnalysisRequest request,
        CancellationToken cancellation, CsvGridRequest? gridRequest = null, bool content = false, int delayMilliseconds = 0, int? gridVisibleRows = null) =>
        Enqueue(snapshot, request, cancellation, gridRequest, content, full: false, delayMilliseconds, gridVisibleRows);

    /// <summary>Reserves Full ahead of all same-version viewport interests without a quiet delay.</summary>
    public Task<NativeFormatPresentation> ReserveFullAsync(TextSnapshot snapshot,
        TextSpan visibleRange, CancellationToken cancellation) =>
        Enqueue(snapshot, new AnalysisRequest(visibleRange, AnalysisScope.Full), cancellation,
            null, content: false, full: true, delayMilliseconds: 0);

    /// <summary>Atomically replaces one bounded slot and retires obsolete version interests.</summary>
    private Task<NativeFormatPresentation> Enqueue(TextSnapshot snapshot, AnalysisRequest request,
        CancellationToken cancellation, CsvGridRequest? grid, bool content, bool full, int delayMilliseconds, int? gridVisibleRows = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(delayMilliseconds);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (snapshot.Version < _version || cancellation.IsCancellationRequested)
                return Task.FromCanceled<NativeFormatPresentation>(new CancellationToken(true));
            if (snapshot.Version > _version)
            {
                _version = snapshot.Version;
                Retire(ref _content);
                Retire(ref _viewport);
                Retire(ref _full);
                _running?.Cancel();
            }
            if (full && _running is { Full: true } active && active.Snapshot.Version == snapshot.Version)
                return active.Completion.Task;
            if (full && _full is { } reserved) return reserved.Completion.Task;
            var work = new Work(snapshot, request, cancellation, grid, full, delayMilliseconds, gridVisibleRows);
            if (content) { Retire(ref _content); _content = work; }
            else if (full) _full = work;
            else { Retire(ref _viewport); _viewport = work; }
            if (!_pumping)
            {
                _pumping = true;
                _ = Task.Run(PumpAsync);
            }
            return work.Completion.Task;
        }
    }

    /// <summary>Services mandatory content, reserved certification, then the latest viewport.</summary>
    private async Task PumpAsync()
    {
        while (true)
        {
            Work? work;
            lock (_gate)
            {
                work = Take(ref _content) ?? Take(ref _full) ?? Take(ref _viewport);
                _running = work;
                if (work is null) { _pumping = false; return; }
            }
            try
            {
                work.Cancellation.Token.ThrowIfCancellationRequested();
                var remaining = work.Due - Environment.TickCount64;
                if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining),
                    work.Cancellation.Token).ConfigureAwait(false);
                if (work.Full && !_fullAdmission(work.Snapshot))
                    throw new NativeFullAnalysisDeferredException();
                var result = await _driver.AnalyzePresentationAsync(work.Snapshot, work.Request,
                    work.Cancellation.Token, work.Grid, work.GridVisibleRows).ConfigureAwait(false);
                work.Cancellation.Token.ThrowIfCancellationRequested();
                work.Completion.TrySetResult(result);
            }
            catch (OperationCanceledException) { work.Completion.TrySetCanceled(); }
            catch (Exception error) { work.Completion.TrySetException(error); }
            finally
            {
                lock (_gate) { _running = null; work.Dispose(); }
            }
        }
    }

    /// <summary>Transfers a queued slot to the single pump under the dispatcher gate.</summary>
    private static Work? Take(ref Work? slot)
    {
        var work = slot;
        slot = null;
        return work;
    }

    /// <summary>Completes undispatched work as canceled without invoking the driver.</summary>
    private static void Retire(ref Work? slot)
    {
        var work = Take(ref slot);
        if (work is null) return;
        work.Completion.TrySetCanceled();
        work.Dispose();
    }

    /// <summary>Retires queued interests and cancels the running turn without blocking the native thread.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Retire(ref _content);
            Retire(ref _viewport);
            Retire(ref _full);
            _running?.Cancel();
        }
    }

    /// <summary>One retained interest; no driver Task is created until dispatch.</summary>
    private sealed class Work(TextSnapshot snapshot, AnalysisRequest request,
        CancellationToken token, CsvGridRequest? grid, bool full, int delayMilliseconds, int? gridVisibleRows = null) : IDisposable
    {
        /// <summary>Immutable version owner retained only while queued or running.</summary>
        public TextSnapshot Snapshot { get; } = snapshot;
        /// <summary>Policy analysis scope and certified source interest.</summary>
        public AnalysisRequest Request { get; } = request;
        /// <summary>Optional bounded projection request, sharing this one policy turn.</summary>
        public CsvGridRequest? Grid { get; } = grid;
        /// <summary>Native page geometry captured on the UI thread, never read from background delivery.</summary>
        public int? GridVisibleRows { get; } = gridVisibleRows;
        /// <summary>Requires resource recheck immediately before dispatch.</summary>
        public bool Full { get; } = full;
        /// <summary>Monotonic edit-derived deadline captured once, unaffected by viewport submissions.</summary>
        public long Due { get; } = Environment.TickCount64 + delayMilliseconds;
        /// <summary>Combines caller cancellation with dispatcher version/lifetime retirement.</summary>
        public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(token);
        /// <summary>Asynchronous consumer notification; never executes user continuation under the gate.</summary>
        public TaskCompletionSource<NativeFormatPresentation> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Tracks nonblocking cancellation callbacks before disposing their CTS.</summary>
        private Task? _cancellation;

        /// <summary>Cancels asynchronously so parser callbacks cannot block native dispatch.</summary>
        public void Cancel() => _cancellation ??= Cancellation.CancelAsync();

        /// <summary>Retires cancellation ownership only after asynchronous callbacks finish.</summary>
        public void Dispose()
        {
            if (_cancellation is null || _cancellation.IsCompleted) Cancellation.Dispose();
            else _ = DisposeAfterCancellationAsync();
        }

        /// <summary>Prevents cancellation callbacks from racing cancellation-source disposal.</summary>
        private async Task DisposeAfterCancellationAsync()
        {
            try { await _cancellation!.ConfigureAwait(false); }
            catch (AggregateException) { }
            finally { Cancellation.Dispose(); }
        }
    }
}
