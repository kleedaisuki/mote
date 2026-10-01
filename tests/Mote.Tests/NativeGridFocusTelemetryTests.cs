using System.Diagnostics;
using System.Text.Json;
using Mote.Telemetry;

namespace Mote.Tests;

/// <summary>Portable focus adapter provenance, privacy, and original-session ownership contracts.</summary>
[Collection("Telemetry")]
public sealed class NativeGridFocusTelemetryTests
{
    /// <summary>Disabled calls neither inspect invalid evidence nor allocate warmed state.</summary>
    [Fact]
    public async Task Disabled_receipt_is_zero_allocation_and_does_not_validate()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = false, OutputDirectory = temp.Path });
        var sample = new TelemetryFocusSample((TelemetryFocusThreadRelation)999,
            (TelemetryFocusThreadRelation)999, (TelemetryFocusPane)999);
        _ = MoteTelemetry.BeginNativeGridFocus((TelemetryFocusTarget)999, sample);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            _ = MoteTelemetry.BeginNativeGridFocus((TelemetryFocusTarget)999, sample);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        await MoteTelemetry.ShutdownAsync();
        Assert.Empty(Directory.GetFileSystemEntries(temp.Path));
    }

    /// <summary>Each actual result gets an independent session receipt and exactly one duration child.</summary>
    [Theory]
    [InlineData(TelemetryFocusOutcome.Applied, "applied", "success")]
    [InlineData(TelemetryFocusOutcome.NoChange, "no_change", "success")]
    [InlineData(TelemetryFocusOutcome.Unsupported, "unsupported", "failure")]
    [InlineData(TelemetryFocusOutcome.Stale, "stale", "failure")]
    [InlineData(TelemetryFocusOutcome.NotReady, "not_ready", "failure")]
    [InlineData(TelemetryFocusOutcome.InvalidCoordinate, "invalid_coordinate", "failure")]
    [InlineData(TelemetryFocusOutcome.Unavailable, "unavailable", "failure")]
    [InlineData(TelemetryFocusOutcome.CompositionBlocked, "composition_blocked", "failure")]
    [InlineData(TelemetryFocusOutcome.Fault, "fault", "failure")]
    public async Task Closed_outcomes_have_exact_fields_and_nonambient_parentage(
        TelemetryFocusOutcome outcome, string result, string status)
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = temp.Path });
        try
        {
            using var ambient = new Activity("SECRET-ambient").Start();
            var request = MoteTelemetry.BeginNativeGridFocus(TelemetryFocusTarget.Cell,
                new(TelemetryFocusThreadRelation.NonOwner, TelemetryFocusThreadRelation.Owner,
                    TelemetryFocusPane.RowScroller))!;
            Assert.Same(ambient, Activity.Current);
            Assert.True(request.EndOnce(outcome, TelemetryFocusPane.Table));
            Assert.False(request.EndOnce(TelemetryFocusOutcome.Fault, TelemetryFocusPane.Outside));
            Assert.Same(ambient, Activity.Current);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var rows = Read(temp.Path);
        var session = Assert.Single(rows, r => Op(r) == "mote.session");
        var receipt = Assert.Single(rows, r => Op(r) == "native.grid.focus.adapter.received");
        var terminal = Assert.Single(rows, r => Op(r) == "native.grid.focus.adapter");
        Assert.Equal(session.GetProperty("span_id").GetString(), receipt.GetProperty("parent_span_id").GetString());
        Assert.Equal(receipt.GetProperty("span_id").GetString(), terminal.GetProperty("parent_span_id").GetString());
        Assert.NotEqual(receipt.GetProperty("span_id").GetString(), terminal.GetProperty("span_id").GetString());
        Assert.Equal(session.GetProperty("trace_id").GetString(), terminal.GetProperty("trace_id").GetString());
        Assert.Equal(0, receipt.GetProperty("duration_us").GetInt64());
        Assert.True(terminal.GetProperty("duration_us").GetInt64() >= 0);
        Assert.Equal("success", receipt.GetProperty("status").GetString());
        Assert.Equal(status, terminal.GetProperty("status").GetString());
        var first = receipt.GetProperty("attributes");
        var last = terminal.GetProperty("attributes");
        Assert.Equal(new[] { "focus_before", "focus_target", "managed_admission_relation", "native_thread_relation" },
            first.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(new[] { "focus_after", "focus_before", "focus_result", "focus_target", "managed_admission_relation", "native_thread_relation" },
            last.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("non_owner", first.GetProperty("native_thread_relation").GetString());
        Assert.Equal("owner", first.GetProperty("managed_admission_relation").GetString());
        Assert.Equal("row_scroller", first.GetProperty("focus_before").GetString());
        Assert.Equal("cell", first.GetProperty("focus_target").GetString());
        Assert.Equal("table", last.GetProperty("focus_after").GetString());
        Assert.Equal(result, last.GetProperty("focus_result").GetString());
        Assert.DoesNotContain("SECRET", string.Join('\n', rows.Select(r => r.GetRawText())));
    }

    /// <summary>A delayed terminal cannot attach to a later configured session.</summary>
    [Fact]
    public async Task Original_sink_rejects_late_terminal_without_fallback()
    {
        using var old = new RepoTemp();
        using var next = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = old.Path });
        var request = MoteTelemetry.BeginNativeGridFocus(TelemetryFocusTarget.Table, default)!;
        await MoteTelemetry.ShutdownAsync();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = next.Path });
        try { Assert.True(request.EndOnce(TelemetryFocusOutcome.Applied, TelemetryFocusPane.Table)); }
        finally { await MoteTelemetry.ShutdownAsync(); }
        Assert.DoesNotContain(Read(old.Path), r => Op(r) == "native.grid.focus.adapter");
        Assert.DoesNotContain(Read(next.Path), r => Op(r).StartsWith("native.grid.focus"));
    }

    /// <summary>All enum positions reject values outside their closed domain before writing.</summary>
    [Fact]
    public async Task Enabled_evidence_is_closed_and_invalid_terminal_does_not_consume_owner()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = temp.Path });
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MoteTelemetry.BeginNativeGridFocus((TelemetryFocusTarget)99, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => MoteTelemetry.BeginNativeGridFocus(default, new((TelemetryFocusThreadRelation)99, default, default)));
            Assert.Throws<ArgumentOutOfRangeException>(() => MoteTelemetry.BeginNativeGridFocus(default, new(default, (TelemetryFocusThreadRelation)99, default)));
            Assert.Throws<ArgumentOutOfRangeException>(() => MoteTelemetry.BeginNativeGridFocus(default, new(default, default, (TelemetryFocusPane)99)));
            var request = MoteTelemetry.BeginNativeGridFocus(default, default)!;
            Assert.Throws<ArgumentOutOfRangeException>(() => request.EndOnce((TelemetryFocusOutcome)99, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => request.EndOnce(default, (TelemetryFocusPane)99));
            Assert.True(request.EndOnce(TelemetryFocusOutcome.Fault, TelemetryFocusPane.Unavailable));
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        Assert.Single(Read(temp.Path), r => Op(r) == "native.grid.focus.adapter.received");
        Assert.Single(Read(temp.Path), r => Op(r) == "native.grid.focus.adapter");
    }

    /// <summary>All categories serialize to the frozen vocabulary and competing ends select one child.</summary>
    [Fact]
    public async Task Category_mappings_and_concurrent_terminal_ownership_are_complete()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = temp.Path });
        var winners = 0;
        try
        {
            foreach (var pane in Enum.GetValues<TelemetryFocusPane>())
            {
                var request = MoteTelemetry.BeginNativeGridFocus(TelemetryFocusTarget.Table,
                    new(TelemetryFocusThreadRelation.Unknown, TelemetryFocusThreadRelation.NonOwner, pane))!;
                await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
                {
                    if (request.EndOnce(TelemetryFocusOutcome.NoChange, pane)) Interlocked.Increment(ref winners);
                })));
            }
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var rows = Read(temp.Path);
        var receipts = rows.Where(r => Op(r) == "native.grid.focus.adapter.received").ToArray();
        var terminals = rows.Where(r => Op(r) == "native.grid.focus.adapter").ToArray();
        Assert.Equal(9, winners);
        Assert.Equal(9, receipts.Length);
        Assert.Equal(9, terminals.Length);
        Assert.Equal(new[] { "unavailable", "none", "source", "table", "row_scroller", "column_scroller", "coordinate", "owned_other", "outside" },
            receipts.Select(r => r.GetProperty("attributes").GetProperty("focus_before").GetString()));
        foreach (var receipt in receipts)
        {
            var attributes = receipt.GetProperty("attributes");
            Assert.Equal("unknown", attributes.GetProperty("native_thread_relation").GetString());
            Assert.Equal("non_owner", attributes.GetProperty("managed_admission_relation").GetString());
            Assert.Equal("table", attributes.GetProperty("focus_target").GetString());
            var terminal = Assert.Single(terminals, r => r.GetProperty("parent_span_id").GetString() == receipt.GetProperty("span_id").GetString());
            Assert.Equal(attributes.GetProperty("focus_before").GetString(),
                terminal.GetProperty("attributes").GetProperty("focus_after").GetString());
        }
    }

    /// <summary>Clones JSON records before releasing parser storage.</summary>
    private static JsonElement[] Read(string directory) => Directory.GetFiles(directory, "*.jsonl")
        .SelectMany(File.ReadLines).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }).ToArray();

    /// <summary>Returns only the fixed operation identifier for assertions.</summary>
    private static string Op(JsonElement row) => row.GetProperty("operation").GetString()!;
}
