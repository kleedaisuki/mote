using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Mote.Telemetry;

namespace Mote.Tests;

/// <summary>Verifies OS-readable diagnostic prefixes independently of orderly session shutdown.</summary>
[Collection("Telemetry")]
public sealed class TelemetryRecoverablePrefixTests
{
    private const string ChildDirectoryVariable = "MOTE_TEST_ABNORMAL_TRACE_DIRECTORY";

    /// <summary>An idle small session exposes completed records while an admitted operation remains unfinished.</summary>
    [Fact]
    public async Task Idle_dirty_trace_is_readable_before_shutdown()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = temp.Path });
        var unfinished = MoteTelemetry.Start(TelemetryOperation.Save);
        try
        {
            MoteTelemetry.Record(TelemetryEvent.EditCommitted, 7);
            var lines = await WaitForPrefixAsync(temp.Path);
            Assert.Single(lines);
            Assert.Contains("\"operation\":\"edit.committed\"", lines[0], StringComparison.Ordinal);
            Assert.DoesNotContain(lines, line => line.Contains("\"operation\":\"mote.session\"", StringComparison.Ordinal));
        }
        finally { unfinished?.Dispose(); await MoteTelemetry.ShutdownAsync(); }
    }

    /// <summary>Only the owned child is killed, after a live prefix is observed; an unfinished Save has no invented terminal record.</summary>
    [Fact]
    public async Task Forced_kill_retains_completed_prefix_not_unfinished_operation()
    {
        var childDirectory = Environment.GetEnvironmentVariable(ChildDirectoryVariable);
        if (childDirectory is not null)
        {
            MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = childDirectory });
            using var unfinished = MoteTelemetry.Start(TelemetryOperation.Save);
            MoteTelemetry.Record(TelemetryEvent.EditCommitted, 19);
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return;
        }

        using var temp = new RepoTemp();
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = temp.Path
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(TelemetryRecoverablePrefixTests).Assembly.Location);
        start.ArgumentList.Add("--TestCaseFilter:FullyQualifiedName=Mote.Tests.TelemetryRecoverablePrefixTests.Forced_kill_retains_completed_prefix_not_unfinished_operation");
        start.Environment[ChildDirectoryVariable] = temp.Path;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Owned trace child did not start.");
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        try
        {
            var before = await WaitForPrefixAsync(temp.Path, TimeSpan.FromSeconds(30));
            Assert.Single(before);
            using var record = JsonDocument.Parse(before[0]);
            Assert.Equal("edit.committed", record.RootElement.GetProperty("operation").GetString());
            Assert.Equal(19, record.RootElement.GetProperty("attributes").GetProperty("count").GetInt64());
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var after = ReadValidPrefix(temp.Path);
            Assert.Equal(before, after);
            Assert.DoesNotContain(after, line => line.Contains("\"operation\":\"document.save\"", StringComparison.Ordinal));
            Assert.DoesNotContain(after, line => line.Contains("\"operation\":\"mote.session\"", StringComparison.Ordinal));
        }
        catch (Exception error)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            throw new InvalidOperationException($"Owned trace child failed. {await stdout} {await stderr}", error);
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(stdout, stderr);
        }
    }

    /// <summary>A destination failure stays on the consumer and does not turn producer calls into filesystem I/O.</summary>
    [Fact]
    public async Task Broken_destination_faults_writer_without_blocking_producers()
    {
        using var temp = new RepoTemp();
        var destination = temp.File("not-a-directory");
        await File.WriteAllTextAsync(destination, "occupied");
        var sink = new JsonlTraceSink(new TelemetryOptions { Enabled = true, OutputDirectory = destination, QueueCapacity = 8 });
        var record = new TraceRecord(DateTimeOffset.UtcNow, sink.SessionTraceId, ActivitySpanId.CreateRandom(),
            sink.SessionSpanId, "edit.committed", 0, TelemetryStatus.Success, default);
        try
        {
            await Task.Run(() => { for (var i = 0; i < 100_000; i++) sink.TryRecord(record); })
                .WaitAsync(TimeSpan.FromSeconds(10));
            var deadline = Stopwatch.StartNew();
            while (!sink.IsFaulted && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
            Assert.True(sink.IsFaulted);
            Assert.Empty(Directory.GetFiles(temp.Path, "*.jsonl"));
        }
        finally { await sink.ShutdownAsync(TimeSpan.FromSeconds(10)); }
    }

    /// <summary>A stalled OS flush never blocks producers or extends their caller-selected shutdown budget.</summary>
    [Fact]
    public async Task Stalled_flush_preserves_nonwaiting_producers_and_bounded_shutdown()
    {
        using var temp = new RepoTemp();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new JsonlTraceSink(new TelemetryOptions
        {
            Enabled = true, OutputDirectory = temp.Path, QueueCapacity = 8
        }, path => new GatedFlushFile(path, entered, release));
        var record = new TraceRecord(DateTimeOffset.UtcNow, sink.SessionTraceId, ActivitySpanId.CreateRandom(),
            sink.SessionSpanId, "edit.committed", 0, TelemetryStatus.Success, default);
        try
        {
            sink.TryRecord(record);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Run(() => { for (var i = 0; i < 100_000; i++) sink.TryRecord(record); })
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(sink.Health.DroppedRecords > 0);
            await sink.ShutdownAsync(TimeSpan.FromMilliseconds(10)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(release.Task.IsCompleted);
            Assert.False(sink.IsFaulted);
        }
        finally
        {
            release.TrySetResult();
            await sink.ShutdownAsync(TimeSpan.FromSeconds(10));
        }
        var lines = ReadValidPrefix(temp.Path);
        Assert.Contains(lines, line => line.Contains("\"operation\":\"telemetry.dropped\"", StringComparison.Ordinal));
        Assert.Contains("\"operation\":\"mote.session\"", lines[^1], StringComparison.Ordinal);
    }

    /// <summary>Real FileStream with a controlled asynchronous flush stall; normal durable flushing remains inherited.</summary>
    private sealed class GatedFlushFile(string path, TaskCompletionSource entered, TaskCompletionSource release)
        : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan)
    {
        /// <summary>Signals the writer boundary and waits only on the consumer, never a producer.</summary>
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            await base.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Only an incomplete final physical line is ignorable; malformed earlier evidence is a hard failure.</summary>
    [Fact]
    public void Prefix_reader_ignores_only_unterminated_trailing_line()
    {
        using var temp = new RepoTemp();
        var path = temp.File("trace.jsonl");
        File.WriteAllText(path, "{\"operation\":\"checkpoint\"}\n{\"partial\":", new UTF8Encoding(false));
        Assert.Single(ReadValidPrefix(temp.Path));
        File.WriteAllText(path, "{broken}\n{\"valid\":true}\n", new UTF8Encoding(false));
        Assert.ThrowsAny<JsonException>(() => ReadValidPrefix(temp.Path));
        File.WriteAllText(path, "{\"valid\":true}\n{broken}\n", new UTF8Encoding(false));
        Assert.ThrowsAny<JsonException>(() => ReadValidPrefix(temp.Path));
    }

    /// <summary>Polls an externally opened shared handle, not an internal flush signal or orderly close.</summary>
    private static async Task<string[]> WaitForPrefixAsync(string directory, TimeSpan? timeout = null)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < (timeout ?? TimeSpan.FromSeconds(10)))
        {
            var lines = ReadValidPrefix(directory);
            if (lines.Length != 0) return lines;
            await Task.Delay(25);
        }
        throw new TimeoutException("No externally readable trace prefix before the test deadline.");
    }

    /// <summary>Validates newline-complete rows; an unfinished last row is censored, never success.</summary>
    private static string[] ReadValidPrefix(string directory)
    {
        var lines = new List<string>();
        foreach (var path in Directory.GetFiles(directory, "*.jsonl").Order(StringComparer.Ordinal))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
            var text = reader.ReadToEnd();
            var lastNewline = text.LastIndexOf('\n');
            if (lastNewline < 0) continue;
            foreach (var line in text[..lastNewline].Split('\n'))
            {
                using var parsed = JsonDocument.Parse(line);
                lines.Add(line);
            }
        }
        return lines.ToArray();
    }
}
