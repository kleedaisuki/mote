using System.Text.Json;
using Mote.Native;
using Mote.Telemetry;

namespace Mote.Tests;

/// <summary>Portable filtering and session-only contracts for opt-in menu checkpoints.</summary>
[Collection("Telemetry")]
public sealed class NativeMenuObservationTests
{
    /// <summary>Classification does not determine Save versus Save As, or reinterpret other bindings.</summary>
    [Theory]
    [InlineData(10, 1048576, 1, 's', true)]
    [InlineData(10, 1048576, 1, 'S', true)]
    [InlineData(10, 1179648, 1, 'S', true)]
    [InlineData(10, 1114112, 1, 'S', true)]
    [InlineData(10, 1310720, 1, 's', false)]
    [InlineData(10, 1572864, 1, 's', false)]
    [InlineData(11, 1048576, 1, 's', false)]
    [InlineData(1, 1048576, 1, 's', false)]
    [InlineData(10, 0, 1, 's', false)]
    [InlineData(10, 1048576, 0, 's', false)]
    [InlineData(10, 1048576, 2, 's', false)]
    [InlineData(10, 1048576, 1, 'x', false)]
    public void Candidate_filter_is_not_a_command_binding(int type, int modifiers, int length,
        char character, bool expected) => Assert.Equal(expected, NativeSaveFamilyCandidate.Matches(
            (nuint)type, (nuint)modifiers, (nuint)length, character));

    /// <summary>New independent positives remain session children even inside an ambient Save scope.</summary>
    [Fact]
    public async Task Checkpoints_do_not_inherit_ambient_request_or_user_attributes()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = temp.Path });
        try
        {
            using var ambient = MoteTelemetry.Start(TelemetryOperation.Save,
                new TelemetryDimensions(Version: 37));
            foreach (var kind in MenuEvents()) MoteTelemetry.RecordNativeMenuCheckpoint(kind);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Directory.GetFiles(temp.Path, "*.jsonl")
            .SelectMany(File.ReadAllLines).Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            var session = Assert.Single(records, row =>
                row.RootElement.GetProperty("operation").GetString() == "mote.session").RootElement;
            var menu = records.Where(row => row.RootElement.GetProperty("operation").GetString()!
                .StartsWith("native.menu.", StringComparison.Ordinal)).ToArray();
            Assert.Equal(5, menu.Length);
            Assert.Equal(5, menu.Select(row => row.RootElement.GetProperty("span_id").GetString()).Distinct().Count());
            foreach (var record in menu)
            {
                var row = record.RootElement;
                Assert.Equal(session.GetProperty("span_id").GetString(), row.GetProperty("parent_span_id").GetString());
                Assert.Equal(session.GetProperty("trace_id").GetString(), row.GetProperty("trace_id").GetString());
                Assert.Equal(0, row.GetProperty("duration_us").GetInt64());
                Assert.Equal("success", row.GetProperty("status").GetString());
                Assert.Empty(row.GetProperty("attributes").EnumerateObject());
            }
        }
        finally { foreach (var record in records) record.Dispose(); }
    }

    /// <summary>Default-off valid checkpoints allocate no warmed managed state or output.</summary>
    [Fact]
    public async Task Disabled_checkpoints_are_zero_allocation_and_closed_vocabulary()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = false, OutputDirectory = temp.Path });
        var events = MenuEvents();
        foreach (var kind in events) MoteTelemetry.RecordNativeMenuCheckpoint(kind);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            foreach (var kind in events) MoteTelemetry.RecordNativeMenuCheckpoint(kind);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MoteTelemetry.RecordNativeMenuCheckpoint(TelemetryEvent.SaveCompleted));
        await MoteTelemetry.ShutdownAsync();
        Assert.Empty(Directory.GetFileSystemEntries(temp.Path));
    }

    /// <summary>Fixed event set is intentionally not all TelemetryEvent values.</summary>
    private static TelemetryEvent[] MenuEvents() =>
    [
        TelemetryEvent.NativeMenuObservationReady, TelemetryEvent.NativeMenuObservationUnavailable,
        TelemetryEvent.NativeMenuSaveFamilyEntered, TelemetryEvent.NativeMenuSaveFamilyReturnedTrue,
        TelemetryEvent.NativeMenuSaveFamilyReturnedFalse
    ];
}
