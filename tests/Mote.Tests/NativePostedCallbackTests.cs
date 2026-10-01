using System.Collections;
using System.Reflection;
using System.Text.Json;
using Mote.Native;
using Mote.Telemetry;

namespace Mote.Tests;

/// <summary>Portable fault boundaries for posted UI work; no real native windows are created.</summary>
[Collection("Telemetry")]
public sealed class NativePostedCallbackTests
{
    /// <summary>The actual Windows drain installs a generic notice or preserves an actionable one without native UI.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Windows_uncreated_shell_drain_preserves_notice_policy(bool existingNotice)
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = temp.Path });
        var type = typeof(NativePostedCallback).Assembly.GetType("Mote.Native.Windows.WindowsEditorShell")!;
        var shell = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(bool), typeof(bool)], null)!.Invoke([false, false]);
        var field = type.GetField("_statusNotice", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (existingNotice) field.SetValue(shell, "Preserve actionable notice");
        var calls = 0;
        try
        {
            var post = type.GetMethod("Post")!;
            post.Invoke(shell, [(Action)(() => { calls++; throw new HostileException(); })]);
            post.Invoke(shell, [(Action)(() => calls++)]);
            type.GetMethod("DrainPosted", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null);
            Assert.Equal(2, calls);
            Assert.Equal(existingNotice ? "Preserve actionable notice" :
                "Editor update unavailable; editing remains available.", field.GetValue(shell));
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        Assert.Single(records, row => row.GetProperty("operation").GetString() == "native.posted.callback.failed");
        Assert.DoesNotContain(records, row => row.GetProperty("operation").GetString() ==
            "native.posted.callback.report_failed");
    }

    /// <summary>A primary fault and a reporting fault leave later queue work available without retry.</summary>
    [Fact]
    public void Ordered_callbacks_continue_and_preserve_original_exception()
    {
        var sequence = new List<int>();
        var primary = new InvalidOperationException("primary");
        var queue = new Queue<Action>();
        queue.Enqueue(() => sequence.Add(1));
        queue.Enqueue(() => { sequence.Add(2); throw primary; });
        queue.Enqueue(() => sequence.Add(3));
        Exception? observed = null;
        Exception? reported = null;
        var reports = 0;
        while (queue.TryDequeue(out var action))
        {
            var error = NativePostedCallback.Invoke(action);
            if (error is null) continue;
            observed = error;
            NativePostedCallback.Report(error, original =>
            {
                reported = original;
                reports++;
                throw new InvalidOperationException("secondary");
            });
        }
        Assert.Equal(new[] { 1, 2, 3 }, sequence);
        Assert.Same(primary, observed);
        Assert.Same(primary, reported);
        Assert.Equal(1, reports);
    }

    /// <summary>UI failure after a completed write cannot undo persistence or trigger another callback.</summary>
    [Fact]
    public void Post_commit_fault_preserves_bytes_and_reports_once()
    {
        using var temp = new RepoTemp();
        var path = temp.File("saved.txt");
        var runs = 0;
        var reports = 0;
        var failure = NativePostedCallback.Invoke(() =>
        {
            runs++;
            File.WriteAllText(path, "exact saved bytes");
            throw new InvalidOperationException("view unavailable");
        });
        Assert.NotNull(failure);
        NativePostedCallback.Report(failure, _ => reports++);
        Assert.Equal("exact saved bytes", File.ReadAllText(path));
        Assert.Equal(1, runs);
        Assert.Equal(1, reports);
    }

    /// <summary>Successful callbacks allocate nothing and emit nothing, even when tracing is enabled.</summary>
    [Fact]
    public async Task Success_is_zero_allocation_and_has_no_records()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = temp.Path });
        try
        {
            Action callback = static () => { };
            for (var i = 0; i < 1000; i++) NativePostedCallback.Invoke(callback);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++) NativePostedCallback.Invoke(callback);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Null(NativePostedCallback.Invoke(callback));
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        Assert.Single(records);
        Assert.Equal("mote.session", records[0].GetProperty("operation").GetString());
    }

    /// <summary>Fault events are independent session children, with no exception data or ambient request ancestry.</summary>
    [Fact]
    public async Task Fault_telemetry_is_content_free_and_session_only()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = temp.Path });
        var primary = new HostileException();
        try
        {
            using var ambient = MoteTelemetry.Start(TelemetryOperation.Save,
                new TelemetryDimensions(Version: 27));
            var result = NativePostedCallback.Invoke(() => throw primary);
            Assert.Same(primary, result);
            NativePostedCallback.Report(result!, _ => throw new HostileException());
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        Assert.Equal(0, primary.MetadataReads);
        var records = Read(temp.Path);
        var session = Assert.Single(records, row => row.GetProperty("operation").GetString() == "mote.session");
        var faults = records.Where(row => row.GetProperty("operation").GetString()!
            .StartsWith("native.posted.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { "native.posted.callback.failed", "native.posted.callback.report_failed" },
            faults.Select(row => row.GetProperty("operation").GetString()));
        foreach (var row in faults)
        {
            Assert.Equal(session.GetProperty("span_id").GetString(), row.GetProperty("parent_span_id").GetString());
            Assert.Equal(session.GetProperty("trace_id").GetString(), row.GetProperty("trace_id").GetString());
            Assert.Equal("failure", row.GetProperty("status").GetString());
            Assert.Equal(0, row.GetProperty("duration_us").GetInt64());
            Assert.Empty(row.GetProperty("attributes").EnumerateObject());
        }
    }

    /// <summary>Out-of-memory exceptions remain fatal for primary work and optional reporting.</summary>
    [Fact]
    public void Fatal_errors_are_not_converted_to_nonfatal_results()
    {
        var fatal = new OutOfMemoryException();
        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(() =>
            NativePostedCallback.Invoke(() => throw fatal)));
        Assert.Same(fatal, Assert.Throws<OutOfMemoryException>(() =>
            NativePostedCallback.Report(new InvalidOperationException(), _ => throw fatal)));
    }

    /// <summary>Appended event IDs preserve the existing schema-one numeric enum values.</summary>
    [Fact]
    public void Event_ids_are_appended_and_vocabulary_is_closed()
    {
        Assert.Equal((int)TelemetryEvent.NativeMenuSaveFamilyReturnedFalse + 1,
            (int)TelemetryEvent.NativePostedCallbackFailed);
        Assert.Equal((int)TelemetryEvent.NativePostedCallbackFailed + 1,
            (int)TelemetryEvent.NativePostedCallbackReportFailed);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MoteTelemetry.RecordNativePostedFailure(TelemetryEvent.SaveCompleted));
    }

    /// <summary>Reads and clones complete normal-exit JSONL records without retaining parser ownership.</summary>
    private static JsonElement[] Read(string directory) => Directory.GetFiles(directory, "*.jsonl")
        .SelectMany(File.ReadAllLines).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }).ToArray();

    /// <summary>Detects forbidden exception metadata access by observability code.</summary>
    private sealed class HostileException : Exception
    {
        /// <summary>Counts attempts to inspect exception-provided user content.</summary>
        internal int MetadataReads;
        /// <inheritdoc />
        public override string Message { get { MetadataReads++; throw new InvalidOperationException(); } }
        /// <inheritdoc />
        public override IDictionary Data { get { MetadataReads++; throw new InvalidOperationException(); } }
    }
}
