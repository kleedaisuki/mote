using System.Text.Json;
using Mote.Telemetry;

namespace Mote.Tests;

/// <summary>Portable vocabulary, privacy, and session ownership checks; no AppKit or keyboard input.</summary>
[Collection("Telemetry")]
public sealed class NativeInputTelemetryTests
{
    /// <summary>Six appended values preserve every previously shipped event identifier.</summary>
    [Fact]
    public void Input_events_are_append_only_and_api_rejects_other_events()
    {
        var events = Events();
        for (var i = 0; i < events.Length; i++)
            Assert.Equal((int)TelemetryEvent.NativePostedCallbackReportFailed + i + 1, (int)events[i]);
        foreach (var kind in Enum.GetValues<TelemetryEvent>().Except(events))
            Assert.Throws<ArgumentOutOfRangeException>(() => MoteTelemetry.RecordNativeInputCheckpoint(kind));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MoteTelemetry.RecordNativeInputCheckpoint((TelemetryEvent)(-1)));
    }

    /// <summary>Each checkpoint has its own session child, never ambient Save identity or dimensions.</summary>
    [Fact]
    public async Task Input_checkpoints_preserve_fixed_names_outcomes_and_empty_attributes()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = temp.Path });
        try
        {
            using var ambient = MoteTelemetry.Start(TelemetryOperation.Save,
                new TelemetryDimensions(Version: 37, Count: 9));
            foreach (var kind in Events()) MoteTelemetry.RecordNativeInputCheckpoint(kind);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Directory.GetFiles(temp.Path, "*.jsonl").SelectMany(File.ReadAllLines)
            .Select(line =>
            {
                using var parsed = JsonDocument.Parse(line);
                return parsed.RootElement.Clone();
            }).ToArray();
        var session = Assert.Single(records, row => row.GetProperty("operation").GetString() == "mote.session");
        var input = records.Where(row => row.GetProperty("operation").GetString()!
            .StartsWith("native.input.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { "native.input.monitor.ready", "native.input.monitor.unavailable",
            "native.input.save_family_candidate", "native.input.monitor.callback_failed",
            "native.input.monitor.removed", "native.input.monitor.removal_failed" },
            input.Select(row => row.GetProperty("operation").GetString()));
        Assert.Equal(new[] { "success", "failure", "success", "failure", "success", "failure" },
            input.Select(row => row.GetProperty("status").GetString()));
        Assert.Equal(6, input.Select(row => row.GetProperty("span_id").GetString()).Distinct().Count());
        foreach (var row in input)
        {
            Assert.Equal(session.GetProperty("span_id").GetString(), row.GetProperty("parent_span_id").GetString());
            Assert.Equal(session.GetProperty("trace_id").GetString(), row.GetProperty("trace_id").GetString());
            Assert.Equal(0, row.GetProperty("duration_us").GetInt64());
            Assert.Empty(row.GetProperty("attributes").EnumerateObject());
        }
    }

    /// <summary>Default-off valid calls allocate no warmed managed state or output files.</summary>
    [Fact]
    public async Task Disabled_input_checkpoints_are_allocation_free()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = false, OutputDirectory = temp.Path });
        var events = Events();
        foreach (var kind in events) MoteTelemetry.RecordNativeInputCheckpoint(kind);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            foreach (var kind in events) MoteTelemetry.RecordNativeInputCheckpoint(kind);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        await MoteTelemetry.ShutdownAsync();
        Assert.Empty(Directory.GetFileSystemEntries(temp.Path));
    }

    /// <summary>Fixed order is both serialized vocabulary and append-only enum order.</summary>
    private static TelemetryEvent[] Events() =>
    [
        TelemetryEvent.NativeInputMonitorReady, TelemetryEvent.NativeInputMonitorUnavailable,
        TelemetryEvent.NativeInputSaveFamilyCandidate, TelemetryEvent.NativeInputMonitorCallbackFailed,
        TelemetryEvent.NativeInputMonitorRemoved, TelemetryEvent.NativeInputMonitorRemovalFailed
    ];
}
