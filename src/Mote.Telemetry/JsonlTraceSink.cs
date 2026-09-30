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
    int? HResult = null);

/// <summary>
/// Single-consumer, bounded JSONL writer. Producers only call TryWrite; all I/O,
/// formatting, rotation, and durable shutdown flushing run off the UI thread.
/// </summary>
internal sealed class JsonlTraceSink
{
    private static readonly byte[] Newline = [(byte)'\n'];
    private readonly Channel<TraceRecord> _channel;
    private readonly Task _writer;
    private readonly string _directory;
    private readonly TelemetryOptions _options;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly long _started = Stopwatch.GetTimestamp();
    private long _dropped;
    private long _pendingDropped;
    private int _faulted;

    internal JsonlTraceSink(TelemetryOptions options)
    {
        _options = options;
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

    /// <summary>Enqueues a bounded record or accounts for its loss, never blocking.</summary>
    internal void TryRecord(TraceRecord record)
    {
        if (IsFaulted) return;
        if (!_channel.Writer.TryWrite(record))
        {
            Interlocked.Increment(ref _dropped);
            Interlocked.Increment(ref _pendingDropped);
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
        _channel.Writer.TryComplete();
        if (timeout <= TimeSpan.Zero) return;
        try
        {
            await _writer.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The worker may finish in the background; shutdown must not hold a save.
        }
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
        var buffer = new ArrayBufferWriter<byte>(512);

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
                stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                    FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                bytesInFile = 0;
                openedAt = Stopwatch.GetTimestamp();
                PruneSessionFiles(sequence);
            }

            await stream!.WriteAsync(buffer.WrittenMemory).ConfigureAwait(false);
            await stream.WriteAsync(Newline).ConfigureAwait(false);
            bytesInFile += lineBytes;
        }

        try
        {
            await foreach (var record in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
                await AppendAsync(record).ConfigureAwait(false);

            var finalDropped = Interlocked.Exchange(ref _pendingDropped, 0);
            if (finalDropped > 0)
                await AppendAsync(DropRecord(finalDropped)).ConfigureAwait(false);

            await AppendAsync(new TraceRecord(
                DateTimeOffset.UtcNow, SessionTraceId, SessionSpanId, default,
                "mote.session", Stopwatch.GetElapsedTime(_started).Ticks / 10,
                TelemetryStatus.Success, default)).ConfigureAwait(false);

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
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();
    }

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
