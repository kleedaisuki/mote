using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace Mote.Telemetry;

/// <summary>Immutable data that can be written without reflection or user text.</summary>
internal readonly record struct TraceRecord(
    DateTimeOffset UtcTime,
    ActivityTraceId TraceId,
    ActivitySpanId SpanId,
    ActivitySpanId ParentSpanId,
    string Operation,
    long DurationUs,
    TelemetryStatus Status,
    TelemetryDimensions Dimensions,
    int? HResult = null,
    TelemetryReason Reason = TelemetryReason.None,
    NativeGridFocusAttributes? Focus = null);

/// <summary>
/// Single-consumer, bounded JSONL writer. Producers only call TryWrite; all I/O,
/// formatting, rotation, periodic OS flushing, and durable shutdown flushing run off the UI thread.
/// </summary>
internal sealed class JsonlTraceSink
{
    /// <summary>Caps dirty-buffer residence under healthy writer scheduling; this is not a durability deadline.</summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);
    /// <summary>Flushes sustained traffic without waiting for an idle timer turn.</summary>
    private const int FlushBytes = 64 * 1024;
    private static readonly byte[] Newline = [(byte)'\n'];
    private readonly Channel<TraceRecord> _channel;
    private readonly Task _writer;
    private readonly string _directory;
    private readonly TelemetryOptions _options;
    /// <summary>Optional test-only file factory; invoked exclusively by the writer at file creation.</summary>
    private readonly Func<string, FileStream>? _openFile;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly long _started = Stopwatch.GetTimestamp();
    private long _dropped;
    private long _pendingDropped;
    private int _faulted;
    /// <summary>The sign bit closes admission; remaining bits count producers owning an enqueue lease.</summary>
    private int _producers;
    /// <summary>Completes when closed admission has no producers; continuations never run on a producer.</summary>
    private readonly TaskCompletionSource _producersFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Remains set when closure abandoned admitted work, so the session cannot certify a complete drain.</summary>
    private int _producerDrainIncomplete;

    internal JsonlTraceSink(TelemetryOptions options, Func<string, FileStream>? openFile = null)
    {
        _options = options;
        _openFile = openFile;
        var root = options.OutputDirectory;
        if (root is null)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                throw new InvalidOperationException("User home directory is unavailable.");
            root = Path.Combine(home, ".mote", options.Subdirectory);
        }
        else if (!Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException("Telemetry output directory must be absolute.", nameof(options));
        }

        _directory = Path.GetFullPath(root);
        SessionTraceId = ActivityTraceId.CreateRandom();
        SessionSpanId = ActivitySpanId.CreateRandom();
        _channel = Channel.CreateBounded<TraceRecord>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _writer = Task.Run(WriteLoopAsync);
    }

    internal ActivityTraceId SessionTraceId { get; }
    internal ActivitySpanId SessionSpanId { get; }
    internal bool IsFaulted => Volatile.Read(ref _faulted) != 0;
    internal TelemetryHealth Health => new(true, IsFaulted, Interlocked.Read(ref _dropped));

    /// <summary>Atomically admits work before shutdown closes admission, without a writer lock.</summary>
    internal bool TryAcquireProducer()
    {
        var current = Volatile.Read(ref _producers);
        while (current >= 0)
        {
            var observed = Interlocked.CompareExchange(ref _producers, current + 1, current);
            if (observed == current) return true;
            current = observed;
        }
        return false;
    }

    /// <summary>Releases admission only after the producer's final enqueue attempt.</summary>
    internal void ReleaseProducer()
    {
        if (Interlocked.Decrement(ref _producers) == int.MinValue)
            _producersFinished.TrySetResult();
    }

    /// <summary>Accounts for an existing delayed interval that misses the shutdown admission boundary.</summary>
    internal void RecordRejected()
    {
        Interlocked.Increment(ref _dropped);
        Interlocked.Increment(ref _pendingDropped);
    }

    /// <summary>Enqueues a bounded record or accounts for its loss, never blocking.</summary>
    internal void TryRecord(TraceRecord record)
    {
        if (IsFaulted) return;
        if (!_channel.Writer.TryWrite(record))
        {
            RecordRejected();
            return;
        }

        // The aggregate is best effort here and guaranteed again at normal drain.
        var dropped = Interlocked.Exchange(ref _pendingDropped, 0);
        if (dropped == 0) return;
        if (!_channel.Writer.TryWrite(DropRecord(dropped)))
            Interlocked.Add(ref _pendingDropped, dropped);
    }

    /// <summary>Drains queued writes, with a caller-selected shutdown deadline.</summary>
    internal async Task ShutdownAsync(TimeSpan timeout)
    {
        var started = Stopwatch.GetTimestamp();
        var previous = Interlocked.Or(ref _producers, int.MinValue);
        if (previous == 0) _producersFinished.TrySetResult();
        try
        {
            if (timeout > TimeSpan.Zero)
                await _producersFinished.Task.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A stalled producer cannot extend the user's shutdown budget.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Invalid deadlines retain the existing nonthrowing shutdown contract.
            Volatile.Write(ref _faulted, 1);
        }
        if (!_producersFinished.Task.IsCompleted)
            Volatile.Write(ref _producerDrainIncomplete, 1);
        _channel.Writer.TryComplete();
        if (timeout <= TimeSpan.Zero) return;
        var remaining = timeout - Stopwatch.GetElapsedTime(started);
        if (remaining <= TimeSpan.Zero) return;
        try
        {
            await _writer.WaitAsync(remaining).ConfigureAwait(false);
        }
        catch (TimeoutException) { /* The worker may finish; never hold a user save. */ }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Diagnostics must never prevent an application shutdown.
            Volatile.Write(ref _faulted, 1);
        }
    }

    private TraceRecord DropRecord(long count) => new(
        DateTimeOffset.UtcNow, SessionTraceId, ActivitySpanId.CreateRandom(), SessionSpanId,
        "telemetry.dropped", 0, TelemetryStatus.Success,
        new TelemetryDimensions(Count: count));

    private async Task WriteLoopAsync()
    {
        FileStream? stream = null;
        var bytesInFile = 0L;
        var openedAt = 0L;
        var sequence = 0;
        var dirtyBytes = 0L;
        var flushedAt = Stopwatch.GetTimestamp();
        var buffer = new ArrayBufferWriter<byte>(512);

        // Only this consumer touches the stream. FlushAsync drains managed buffering to
        // the OS, not storage media; a stalled disk never makes producers wait.
        async Task FlushDirtyAsync()
        {
            if (stream is null || dirtyBytes == 0) return;
            await stream.FlushAsync().ConfigureAwait(false);
            dirtyBytes = 0;
            flushedAt = Stopwatch.GetTimestamp();
        }

        async Task AppendAsync(TraceRecord record)
        {
            buffer.Clear();
            WriteJson(buffer, record);
            var lineBytes = buffer.WrittenCount + 1;
            if (stream is null || bytesInFile + lineBytes > _options.MaxFileBytes ||
                Stopwatch.GetElapsedTime(openedAt) >= _options.MaxFileAge)
            {
                if (stream is not null)
                {
                    await FlushAndCloseAsync(stream).ConfigureAwait(false);
                    stream = null;
                }
                Directory.CreateDirectory(_directory);
                sequence++;
                var name = $"mote-trace-{_sessionId}-{sequence:D6}.jsonl";
                var path = Path.Combine(_directory, name);
                stream = _openFile?.Invoke(path) ?? new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                    FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                bytesInFile = 0;
                openedAt = Stopwatch.GetTimestamp();
                dirtyBytes = 0;
                flushedAt = openedAt;
                PruneSessionFiles(sequence);
            }

            await stream!.WriteAsync(buffer.WrittenMemory).ConfigureAwait(false);
            await stream.WriteAsync(Newline).ConfigureAwait(false);
            bytesInFile += lineBytes;
            dirtyBytes += lineBytes;
            if (dirtyBytes >= FlushBytes || Stopwatch.GetElapsedTime(flushedAt) >= FlushInterval)
                await FlushDirtyAsync().ConfigureAwait(false);
        }

        try
        {
            using var timer = new PeriodicTimer(FlushInterval);
            using var waits = new CancellationTokenSource();
            Task<bool>? readable = null;
            Task<bool>? tick = null;
            try
            {
                while (true)
                {
                    // Retain the losing wait rather than accumulating one pending
                    // channel wait per timer tick (or one timer wait per record).
                    readable ??= _channel.Reader.WaitToReadAsync(waits.Token).AsTask();
                    tick ??= timer.WaitForNextTickAsync(waits.Token).AsTask();
                    await Task.WhenAny(readable, tick).ConfigureAwait(false);
                    if (tick.IsCompleted)
                    {
                        await tick.ConfigureAwait(false);
                        tick = null;
                        await FlushDirtyAsync().ConfigureAwait(false);
                    }
                    if (!readable.IsCompleted) continue;
                    if (!await readable.ConfigureAwait(false)) break;
                    readable = null;
                    while (_channel.Reader.TryRead(out var record))
                        await AppendAsync(record).ConfigureAwait(false);
                }
            }
            finally
            {
                // Also runs on broken I/O. No background timer/read wait survives
                // this writer; observe cancellation without obscuring its failure.
                waits.Cancel();
                if (readable is not null) await ObserveWaitAsync(readable).ConfigureAwait(false);
                if (tick is not null) await ObserveWaitAsync(tick).ConfigureAwait(false);
            }

            var finalDropped = Interlocked.Exchange(ref _pendingDropped, 0);
            if (finalDropped > 0)
                await AppendAsync(DropRecord(finalDropped)).ConfigureAwait(false);

            await AppendAsync(new TraceRecord(
                DateTimeOffset.UtcNow, SessionTraceId, SessionSpanId, default,
                "mote.session", Stopwatch.GetElapsedTime(_started).Ticks / 10,
                Volatile.Read(ref _producerDrainIncomplete) == 0
                    ? TelemetryStatus.Success : TelemetryStatus.Cancelled, default)).ConfigureAwait(false);

            if (stream is not null)
            {
                await FlushAndCloseAsync(stream).ConfigureAwait(false);
                stream = null;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Do not reveal an exception message: filesystem errors can contain paths.
            Volatile.Write(ref _faulted, 1);
        }
        finally
        {
            if (stream is not null)
            {
                try { await stream.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Volatile.Write(ref _faulted, 1);
                }
            }
        }
    }

    /// <summary>Observes a retained readiness wait cancelled when the sole writer leaves.</summary>
    private static async Task ObserveWaitAsync(Task<bool> wait)
    {
        try { await wait.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private void PruneSessionFiles(int latestSequence)
    {
        var obsolete = latestSequence - _options.MaxFilesPerSession;
        if (obsolete < 1) return;
        var path = Path.Combine(_directory, $"mote-trace-{_sessionId}-{obsolete:D6}.jsonl");
        try { File.Delete(path); }
        catch (IOException) { /* Retention is best effort; never interrupt tracing. */ }
        catch (UnauthorizedAccessException) { /* The active writer remains usable. */ }
    }

    private static async Task FlushAndCloseAsync(FileStream stream)
    {
        await stream.FlushAsync().ConfigureAwait(false);
        // Data must reach the OS's durable flush path on orderly shutdown/rotation.
        stream.Flush(flushToDisk: true);
        await stream.DisposeAsync().ConfigureAwait(false);
    }

    private void WriteJson(IBufferWriter<byte> output, TraceRecord record)
    {
        using var writer = new Utf8JsonWriter(output);
        writer.WriteStartObject();
        writer.WriteNumber("schema_version", 1);
        writer.WriteString("utc_time", record.UtcTime);
        writer.WriteString("session_id", _sessionId);
        if (_options.RunId is Guid runId)
            writer.WriteString("run_id", runId);
        writer.WriteString("trace_id", record.TraceId.ToString());
        writer.WriteString("span_id", record.SpanId.ToString());
        if (record.ParentSpanId == default)
            writer.WriteNull("parent_span_id");
        else
            writer.WriteString("parent_span_id", record.ParentSpanId.ToString());
        writer.WriteString("operation", record.Operation);
        writer.WriteNumber("duration_us", record.DurationUs);
        writer.WriteString("status", StatusName(record.Status));
        writer.WriteStartObject("attributes");
        if (record.Focus is NativeGridFocusAttributes focus && record.Operation is
            ("native.grid.focus.adapter.received" or "native.grid.focus.adapter"))
            WriteFocusAttributes(writer, focus);
        else
        {
            if (record.Dimensions.Format != TelemetryFormat.Unknown)
                writer.WriteString("format", FormatName(record.Dimensions.Format));
            if (record.Dimensions.DocumentBytes is long bytes && bytes >= 0)
                writer.WriteString("size_bucket", SizeBucket(bytes));
            if (record.Dimensions.Version is long version && version >= 0)
                writer.WriteNumber("version", version);
            if (record.Dimensions.Count is long count && count >= 0)
                writer.WriteNumber("count", count);
            if (record.HResult is int hresult)
                writer.WriteNumber("hresult", hresult);
            if (MoteTelemetry.ReasonName(record.Reason) is string reason)
                writer.WriteString("reason", reason);
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();
    }

    /// <summary>Writes only closed focus evidence; legacy dimensions never enter these operations.</summary>
    private static void WriteFocusAttributes(Utf8JsonWriter writer, NativeGridFocusAttributes focus)
    {
        writer.WriteString("native_thread_relation", RelationName(focus.Before.NativeThreadRelation));
        writer.WriteString("managed_admission_relation", RelationName(focus.Before.ManagedAdmissionRelation));
        writer.WriteString("focus_before", PaneName(focus.Before.Pane));
        writer.WriteString("focus_target", focus.Target == TelemetryFocusTarget.Table ? "table" : "cell");
        if (focus.After is TelemetryFocusPane after) writer.WriteString("focus_after", PaneName(after));
        if (focus.Outcome is TelemetryFocusOutcome outcome) writer.WriteString("focus_result", OutcomeName(outcome));
    }

    /// <summary>Maps validated relationships without retaining thread identities.</summary>
    private static string RelationName(TelemetryFocusThreadRelation relation) => relation switch
    {
        TelemetryFocusThreadRelation.Owner => "owner",
        TelemetryFocusThreadRelation.NonOwner => "non_owner",
        _ => "unknown"
    };

    /// <summary>Maps the closed native physical focus categories.</summary>
    private static string PaneName(TelemetryFocusPane pane) => pane switch
    {
        TelemetryFocusPane.None => "none",
        TelemetryFocusPane.Source => "source",
        TelemetryFocusPane.Table => "table",
        TelemetryFocusPane.RowScroller => "row_scroller",
        TelemetryFocusPane.ColumnScroller => "column_scroller",
        TelemetryFocusPane.Coordinate => "coordinate",
        TelemetryFocusPane.OwnedOther => "owned_other",
        TelemetryFocusPane.Outside => "outside",
        _ => "unavailable"
    };

    /// <summary>Maps actual adapter outcomes separately from exceptional invocation failure.</summary>
    private static string OutcomeName(TelemetryFocusOutcome outcome) => outcome switch
    {
        TelemetryFocusOutcome.Applied => "applied",
        TelemetryFocusOutcome.NoChange => "no_change",
        TelemetryFocusOutcome.Unsupported => "unsupported",
        TelemetryFocusOutcome.Stale => "stale",
        TelemetryFocusOutcome.NotReady => "not_ready",
        TelemetryFocusOutcome.InvalidCoordinate => "invalid_coordinate",
        TelemetryFocusOutcome.Unavailable => "unavailable",
        TelemetryFocusOutcome.CompositionBlocked => "composition_blocked",
        _ => "fault"
    };

    private static string StatusName(TelemetryStatus status) => status switch
    {
        TelemetryStatus.Success => "success",
        TelemetryStatus.Failure => "failure",
        TelemetryStatus.Cancelled => "cancelled",
        TelemetryStatus.Skipped => "skipped",
        _ => "unknown"
    };

    private static string FormatName(TelemetryFormat format) => format switch
    {
        TelemetryFormat.PlainText => "plain_text",
        TelemetryFormat.Markdown => "markdown",
        TelemetryFormat.Toml => "toml",
        TelemetryFormat.Json => "json",
        TelemetryFormat.Yaml => "yaml",
        TelemetryFormat.Csv => "csv",
        _ => "unknown"
    };

    private static string SizeBucket(long bytes) => bytes switch
    {
        < 1024 => "<1KiB",
        < 4096 => "1-4KiB",
        < 16384 => "4-16KiB",
        < 65536 => "16-64KiB",
        < 262144 => "64-256KiB",
        < 1048576 => "256KiB-1MiB",
        < 4194304 => "1-4MiB",
        < 16777216 => "4-16MiB",
        < 67108864 => "16-64MiB",
        < 268435456 => "64-256MiB",
        < 1073741824 => "256MiB-1GiB",
        _ => ">=1GiB"
    };
}
