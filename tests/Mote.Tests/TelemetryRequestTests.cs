using System.Diagnostics;
using System.Text.Json;
using Mote.Telemetry;

namespace Mote.Tests;

/// <summary>Explicit request correlation, shutdown isolation, privacy and allocation contracts.</summary>
[Collection("Telemetry")]
public sealed class TelemetryRequestTests
{
    /// <summary>Disabled explicit instrumentation allocates no request, mark payload or checkpoint.</summary>
    [Fact]
    public async Task Disabled_request_and_checkpoints_allocate_nothing()
    {
        await MoteTelemetry.ShutdownAsync();
        for (var i = 0; i < 100; i++) ExerciseDisabled();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++) ExerciseDisabled();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    /// <summary>Exercises all explicit fast paths without allocating a closure.</summary>
    private static void ExerciseDisabled()
    {
        var request = MoteTelemetry.BeginRequest(TelemetryOperation.CommandSave);
        request?.Checkpoint(TelemetryEvent.SaveAdmitted);
        Assert.Null(request);
        _ = MoteTelemetry.MarkChild(default);
        MoteTelemetry.RecordChild(TelemetryEvent.SaveWorkerStarted, default);
        _ = MoteTelemetry.BeginPhase(TelemetryOperation.Save, default);
        MoteTelemetry.EndPhase(TelemetryOperation.Save, default);
    }

    /// <summary>Request lifetime does not retain producer admission or delay bounded normal shutdown.</summary>
    [Fact]
    public async Task Unfinished_request_does_not_hold_shutdown_lease()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = temp.Path });
        var request = Assert.IsType<TelemetryRequest>(MoteTelemetry.BeginRequest(TelemetryOperation.CommandSave));
        await MoteTelemetry.ShutdownAsync();
        var records = Read(temp.Path);
        Assert.Single(records, r => Op(r) == "command.received");
        Assert.DoesNotContain(records, r => Op(r) == "command.save");
        Assert.Equal("success", records.Single(r => Op(r) == "mote.session").GetProperty("status").GetString());
        Assert.True(request.EndOnce(TelemetryStatus.Cancelled, TelemetryReason.LifetimeEnded));
        Assert.Equal(1, request.Mark.Sink!.Health.DroppedRecords);
    }

    /// <summary>Fresh checkpoints and nested phases preserve explicit ancestry without touching Activity.Current.</summary>
    [Fact]
    public async Task Request_and_phase_children_have_unique_ids_and_explicit_ancestry()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = temp.Path });
        try
        {
            using var unrelated = new Activity("unrelated").Start();
            var ambient = Activity.Current;
            var request = Assert.IsType<TelemetryRequest>(MoteTelemetry.BeginRequest(TelemetryOperation.CommandSaveAs));
            Assert.Same(ambient, Activity.Current);
            request.Checkpoint(TelemetryEvent.SaveAdmitted);
            var coarse = request.BeginPhase(TelemetryOperation.Save);
            var phase = MoteTelemetry.BeginPhase(TelemetryOperation.SaveSnapshotCapture, coarse);
            MoteTelemetry.RecordChild(TelemetryEvent.SaveSnapshotCaptured, phase, new TelemetryDimensions(Version: 7));
            MoteTelemetry.EndPhase(TelemetryOperation.SaveSnapshotCapture, phase, new TelemetryDimensions(Version: 7));
            MoteTelemetry.EndPhase(TelemetryOperation.Save, coarse);
            Assert.True(request.EndOnce(TelemetryStatus.Success, TelemetryReason.Completed));
            Assert.Same(ambient, Activity.Current);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var rows = Read(temp.Path);
        Assert.Equal(rows.Length, rows.Select(Span).Distinct().Count());
        var requestRow = rows.Single(r => Op(r) == "command.save_as");
        var coarseRow = rows.Single(r => Op(r) == "document.save");
        var phaseRow = rows.Single(r => Op(r) == "save.snapshot_capture");
        var received = rows.Single(r => Op(r) == "command.received");
        var coarseEntry = rows.Single(r => Op(r) == "document.save.entered");
        var phaseEntry = rows.Single(r => Op(r) == "save.snapshot_capture.entered");
        Assert.Equal(Span(received), Parent(requestRow));
        Assert.Equal(Span(received), Parent(coarseEntry));
        Assert.Equal(Span(coarseEntry), Parent(coarseRow));
        Assert.Equal(Span(coarseEntry), Parent(phaseEntry));
        Assert.Equal(Span(phaseEntry), Parent(phaseRow));
        var snapshot = rows.Single(r => Op(r) == "save.snapshot_captured");
        Assert.Equal(Span(phaseEntry), Parent(snapshot));
        Assert.Equal(7, snapshot.GetProperty("attributes").GetProperty("version").GetInt64());
        Assert.Equal("completed", requestRow.GetProperty("attributes").GetProperty("reason").GetString());
        foreach (var row in rows.Where(r => Op(r).EndsWith(".entered", StringComparison.Ordinal)))
            Assert.Equal(0, row.GetProperty("duration_us").GetInt64());
    }

    /// <summary>Concurrent terminal callbacks select one record, never duplicate successful completion.</summary>
    [Fact]
    public async Task EndOnce_selects_exactly_one_concurrent_terminal()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = temp.Path });
        try
        {
            var request = Assert.IsType<TelemetryRequest>(MoteTelemetry.BeginRequest(TelemetryOperation.CommandSave));
            var winners = 0;
            Parallel.For(0, 100, _ =>
            {
                if (request.EndOnce(TelemetryStatus.Skipped, TelemetryReason.AlreadySaving))
                    Interlocked.Increment(ref winners);
            });
            Assert.Equal(1, winners);
            Assert.True(request.IsEnded);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var terminal = Assert.Single(Read(temp.Path), r => Op(r) == "command.save");
        Assert.Equal("skipped", terminal.GetProperty("status").GetString());
        Assert.Equal("already_saving", terminal.GetProperty("attributes").GetProperty("reason").GetString());
    }

    /// <summary>Every explicit stale-context operation counts against the old sink and cannot contaminate a new session.</summary>
    [Fact]
    public async Task Reconfigured_sink_rejects_old_request_and_children()
    {
        using var old = new RepoTemp();
        using var fresh = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = old.Path });
        var request = Assert.IsType<TelemetryRequest>(MoteTelemetry.BeginRequest(TelemetryOperation.CommandSave));
        var phase = request.BeginPhase(TelemetryOperation.Save);
        var original = request.Mark.Sink!;
        await MoteTelemetry.ShutdownAsync();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = fresh.Path });
        try
        {
            request.Checkpoint(TelemetryEvent.SaveWorkerStarted);
            Assert.False(MoteTelemetry.MarkChild(request.Mark).IsActive);
            Assert.False(MoteTelemetry.BeginPhase(TelemetryOperation.SaveTargetCheck, phase).IsActive);
            MoteTelemetry.EndPhase(TelemetryOperation.Save, phase);
            Assert.True(request.EndOnce(TelemetryStatus.Cancelled, TelemetryReason.LifetimeEnded));
            Assert.False(request.EndOnce(TelemetryStatus.Success, TelemetryReason.Completed));
            Assert.Equal(5, original.Health.DroppedRecords);
            Assert.Equal(0, MoteTelemetry.Health.DroppedRecords);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        Assert.All(Read(fresh.Path), r => Assert.Equal("mote.session", Op(r)));
        Assert.DoesNotContain(Read(old.Path), r => Op(r) == "command.save");
    }

    /// <summary>Schema-v1 remains fixed; terminal reasons and numeric error codes use the expanded closed allowlist.</summary>
    [Fact]
    public async Task Terminal_reason_and_phase_error_have_closed_private_schema()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = temp.Path });
        try
        {
            var request = Assert.IsType<TelemetryRequest>(MoteTelemetry.BeginRequest(TelemetryOperation.CommandSave));
            var phase = request.BeginPhase(TelemetryOperation.SaveCommitReplace);
            MoteTelemetry.EndPhase(TelemetryOperation.SaveCommitReplace, phase, status: TelemetryStatus.Failure,
                hresult: unchecked((int)0x80070020));
            request.EndOnce(TelemetryStatus.Failure, TelemetryReason.SaveFailed);
            var noReason = Assert.IsType<TelemetryRequest>(MoteTelemetry.BeginRequest(TelemetryOperation.CommandSaveAs));
            noReason.EndOnce(TelemetryStatus.Success);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var rows = Read(temp.Path);
        foreach (var row in rows)
        {
            Assert.Equal(1, row.GetProperty("schema_version").GetInt32());
            Assert.Equal(new[] { "attributes", "duration_us", "operation", "parent_span_id", "schema_version",
                "session_id", "span_id", "status", "trace_id", "utc_time" },
                row.EnumerateObject().Select(p => p.Name).Order());
            Assert.All(row.GetProperty("attributes").EnumerateObject(), a =>
                Assert.Contains(a.Name, new[] { "format", "size_bucket", "version", "count", "hresult", "reason" }));
        }
        Assert.Equal(unchecked((int)0x80070020), rows.Single(r => Op(r) == "save.commit_replace")
            .GetProperty("attributes").GetProperty("hresult").GetInt32());
        Assert.Equal("save_failed", rows.Single(r => Op(r) == "command.save")
            .GetProperty("attributes").GetProperty("reason").GetString());
        Assert.False(rows.Single(r => Op(r) == "command.save_as").GetProperty("attributes").TryGetProperty("reason", out _));
    }

    /// <summary>A held request and phase leave a flushed prefix with reconstructible ancestry before any terminal.</summary>
    [Fact]
    public async Task Held_phase_prefix_has_persisted_request_and_phase_anchors()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = temp.Path });
        try
        {
            var request = Assert.IsType<TelemetryRequest>(MoteTelemetry.BeginRequest(TelemetryOperation.CommandSave));
            var coarse = request.BeginPhase(TelemetryOperation.Save);
            _ = MoteTelemetry.BeginPhase(TelemetryOperation.SaveCommitReplace, coarse);
            JsonElement[] rows = [];
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                rows = ReadPrefix(temp.Path);
                if (rows.Any(r => Op(r) == "save.commit_replace.entered")) break;
                await Task.Delay(25);
            }
            var received = Assert.Single(rows, r => Op(r) == "command.received");
            var coarseEntry = Assert.Single(rows, r => Op(r) == "document.save.entered");
            var phaseEntry = Assert.Single(rows, r => Op(r) == "save.commit_replace.entered");
            Assert.Equal(Span(received), Parent(coarseEntry));
            Assert.Equal(Span(coarseEntry), Parent(phaseEntry));
            Assert.DoesNotContain(rows, r => Op(r) is "command.save" or "document.save" or "save.commit_replace");
            Assert.False(request.IsEnded);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
    }

    /// <summary>Reads only complete newline-terminated JSONL records while the writer may still be active.</summary>
    private static JsonElement[] ReadPrefix(string directory)
    {
        var rows = new List<JsonElement>();
        foreach (var file in Directory.GetFiles(directory, "*.jsonl"))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var prefix = reader.ReadToEnd();
            var last = prefix.LastIndexOf('\n');
            if (last < 0) continue;
            foreach (var line in prefix[..last].Split('\n'))
            {
                using var document = JsonDocument.Parse(line);
                rows.Add(document.RootElement.Clone());
            }
        }
        return rows.ToArray();
    }

    /// <summary>Reads independent immutable JSON roots after orderly writer completion.</summary>
    private static JsonElement[] Read(string directory) => Directory.GetFiles(directory, "*.jsonl")
        .SelectMany(File.ReadLines).Select(line =>
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.Clone();
        }).ToArray();

    /// <summary>Extracts only the fixed operation field.</summary>
    private static string Op(JsonElement row) => row.GetProperty("operation").GetString()!;
    /// <summary>Extracts an opaque span identity for causal assertions.</summary>
    private static string Span(JsonElement row) => row.GetProperty("span_id").GetString()!;
    /// <summary>Extracts an opaque parent identity for causal assertions.</summary>
    private static string? Parent(JsonElement row) => row.GetProperty("parent_span_id").GetString();
}
