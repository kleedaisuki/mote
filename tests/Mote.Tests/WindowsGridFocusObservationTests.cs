using System.Text.Json;
using Mote.Native;
using Mote.Native.Windows;
using Mote.Native.Windows.Accessibility;
using Mote.Telemetry;

namespace Mote.Tests;

/// <summary>Portable adapter transparency checks; no HWND is created and no native entry point is invoked.</summary>
[Collection("Telemetry")]
public sealed class WindowsGridFocusObservationTests
{
    /// <summary>Every original result survives observation and its existing HRESULT reduction unchanged.</summary>
    [Theory]
    [InlineData(0, "applied", 0)]
    [InlineData(1, "no_change", 0)]
    [InlineData(2, "unsupported", unchecked((int)0x80131509))]
    [InlineData(3, "stale", unchecked((int)0x80040201))]
    [InlineData(4, "not_ready", unchecked((int)0x80131509))]
    [InlineData(5, "invalid_coordinate", unchecked((int)0x80070057))]
    [InlineData(6, "unavailable", unchecked((int)0x80040201))]
    [InlineData(7, "composition_blocked", unchecked((int)0x80131509))]
    public async Task All_results_execute_once_before_unchanged_hresult_mapping(int value, string name, int hresult)
    {
        using var temp = new RepoTemp();
        var actions = new Actions { Result = (GridAccessibilityResult)value };
        var cell = new GridCoordinate(17, 29);
        MoteTelemetry.Configure(new() { Enabled = true, OutputDirectory = temp.Path });
        try
        {
            var result = WindowsGridFocusOperation.Invoke(actions, default, cell);
            Assert.Equal((GridAccessibilityResult)value, result);
            // This method is a pure enum switch despite its enclosing native bridge annotation.
#pragma warning disable CA1416
            Assert.Equal(hresult, WindowsGridUiaBridge.Result(result));
#pragma warning restore CA1416
            Assert.Equal(cell, actions.Cell);
            Assert.Equal(1, actions.Calls);
            Assert.Equal(2, actions.Captures);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var receipt = Assert.Single(Rows(temp.Path), r => Op(r).EndsWith(".received"));
        var terminal = Assert.Single(Rows(temp.Path), r => Op(r) == "native.grid.focus.adapter");
        Assert.Equal(receipt.GetProperty("span_id").GetString(), terminal.GetProperty("parent_span_id").GetString());
        Assert.Equal(name, Attr(terminal, "focus_result"));
        Assert.Equal("cell", Attr(terminal, "focus_target"));
        Assert.Equal("source", Attr(terminal, "focus_before"));
        Assert.Equal("table", Attr(terminal, "focus_after"));
    }

    /// <summary>A warmed disabled adapter performs no evidence query, allocation, or file output.</summary>
    [Fact]
    public async Task Disabled_path_is_zero_allocation_and_never_queries()
    {
        using var temp = new RepoTemp();
        var actions = new Actions();
        MoteTelemetry.Configure(new() { Enabled = false, OutputDirectory = temp.Path });
        try
        {
            _ = WindowsGridFocusOperation.Invoke(actions, default, null);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) WindowsGridFocusOperation.Invoke(actions, default, null);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.Equal(1001, actions.Calls);
            Assert.Equal(0, actions.Captures);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        Assert.Empty(Directory.GetFileSystemEntries(temp.Path));
    }

    /// <summary>Nonfatal action exceptions remain the same object; only enabled observation emits Fault.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Original_exception_is_preserved_even_when_evidence_throws(bool enabled, bool captureThrows)
    {
        using var temp = new RepoTemp();
        var error = new InvalidOperationException("PRIVATE-primary");
        var actions = new Actions { Error = error, ThrowCapture = captureThrows ? 3 : 0 };
        MoteTelemetry.Configure(new() { Enabled = enabled, OutputDirectory = temp.Path });
        try
        {
            Assert.Same(error, Assert.Throws<InvalidOperationException>(() => WindowsGridFocusOperation.Invoke(actions, default, null)));
            Assert.Equal(1, actions.Calls);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var rows = Rows(temp.Path);
        if (enabled) Assert.Equal("fault", Attr(Assert.Single(rows, r => Op(r) == "native.grid.focus.adapter"), "focus_result"));
        else Assert.Empty(rows);
        Assert.DoesNotContain("PRIVATE", string.Join('\n', rows.Select(r => r.GetRawText())));
    }

    /// <summary>Query failures reduce to unavailable evidence, never a fabricated known pane.</summary>
    [Theory]
    [InlineData(1, "unavailable", "table")]
    [InlineData(2, "source", "unavailable")]
    [InlineData(3, "unavailable", "unavailable")]
    public async Task Capture_failures_do_not_change_primary_result(int throwCapture, string before, string after)
    {
        using var temp = new RepoTemp();
        var actions = new Actions { ThrowCapture = throwCapture, Result = GridAccessibilityResult.NoChange };
        MoteTelemetry.Configure(new() { Enabled = true, OutputDirectory = temp.Path });
        try { Assert.Equal(GridAccessibilityResult.NoChange, WindowsGridFocusOperation.Invoke(actions, default, null)); }
        finally { await MoteTelemetry.ShutdownAsync(); }
        Assert.Equal(1, actions.Calls);
        var row = Assert.Single(Rows(temp.Path), r => Op(r) == "native.grid.focus.adapter");
        Assert.Equal("no_change", Attr(row, "focus_result"));
        Assert.Equal(before, Attr(row, "focus_before"));
        Assert.Equal(after, Attr(row, "focus_after"));
    }

    /// <summary>Invalid producer evidence cannot skip the action or manufacture a terminal certificate.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Invalid_evidence_preserves_action_without_false_completion(int invalidCapture)
    {
        using var temp = new RepoTemp();
        var actions = new Actions { InvalidCapture = invalidCapture };
        MoteTelemetry.Configure(new() { Enabled = true, OutputDirectory = temp.Path });
        try { Assert.Equal(GridAccessibilityResult.Applied, WindowsGridFocusOperation.Invoke(actions, default, null)); }
        finally { await MoteTelemetry.ShutdownAsync(); }
        Assert.Equal(1, actions.Calls);
        Assert.DoesNotContain(Rows(temp.Path), r => Op(r) == "native.grid.focus.adapter");
        Assert.Equal(invalidCapture == 1 ? 0 : 1, Rows(temp.Path).Count(r => Op(r).EndsWith(".received")));
    }

    /// <summary>An unknown adapter result is returned unchanged, not converted into synthetic Fault.</summary>
    [Fact]
    public async Task Unknown_result_retains_receipt_but_has_no_invented_terminal()
    {
        using var temp = new RepoTemp();
        var actions = new Actions { Result = (GridAccessibilityResult)99 };
        MoteTelemetry.Configure(new() { Enabled = true, OutputDirectory = temp.Path });
        try { Assert.Equal((GridAccessibilityResult)99, WindowsGridFocusOperation.Invoke(actions, default, null)); }
        finally { await MoteTelemetry.ShutdownAsync(); }
        Assert.Equal(1, actions.Calls);
        Assert.Single(Rows(temp.Path), r => Op(r).EndsWith(".received"));
        Assert.DoesNotContain(Rows(temp.Path), r => Op(r) == "native.grid.focus.adapter");
    }

    /// <summary>Missing actions preserve Unavailable while all evidence remains explicitly unknown.</summary>
    [Fact]
    public async Task Null_actions_remain_unavailable()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new() { Enabled = true, OutputDirectory = temp.Path });
        try { Assert.Equal(GridAccessibilityResult.Unavailable, WindowsGridFocusOperation.Invoke(null, default, null)); }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var row = Assert.Single(Rows(temp.Path), r => Op(r) == "native.grid.focus.adapter");
        Assert.Equal("unavailable", Attr(row, "focus_result"));
        Assert.Equal("unknown", Attr(row, "native_thread_relation"));
        Assert.Equal("unavailable", Attr(row, "focus_after"));
    }

    /// <summary>A real writer directory failure selects the transparent fast path without evidence reads.</summary>
    [Fact]
    public async Task Faulted_sink_never_queries_or_changes_action()
    {
        using var temp = new RepoTemp();
        var blocked = temp.File("not-a-directory");
        File.WriteAllText(blocked, "fixture");
        var actions = new Actions { Result = GridAccessibilityResult.NoChange };
        MoteTelemetry.Configure(new() { Enabled = true, OutputDirectory = blocked });
        try
        {
            // Configuration is lazy; enqueue one record to exercise actual file creation.
            _ = MoteTelemetry.BeginNativeGridFocus(TelemetryFocusTarget.Table, default);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!MoteTelemetry.Health.SinkFaulted && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.True(MoteTelemetry.Health.SinkFaulted);
            Assert.Equal(GridAccessibilityResult.NoChange, WindowsGridFocusOperation.Invoke(actions, default, null));
            Assert.Equal(1, actions.Calls);
            Assert.Equal(0, actions.Captures);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        Assert.Equal("fixture", File.ReadAllText(blocked));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.jsonl"));
    }

    /// <summary>The pure reducer recognizes checked roles, rejects foreign matches, and never invents a missing source.</summary>
    [Fact]
    public void Classifier_handles_all_roles_without_source_fallback()
    {
        var roles = new WindowsGridFocusRoles(10, 20, 30, 40, 50);
        Assert.Equal(TelemetryFocusPane.None, WindowsGridFocusClassifier.Classify(0, roles, false));
        var expected = new[] { TelemetryFocusPane.Source, TelemetryFocusPane.Table, TelemetryFocusPane.RowScroller,
            TelemetryFocusPane.ColumnScroller, TelemetryFocusPane.Coordinate };
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], WindowsGridFocusClassifier.Classify((i + 1) * 10, roles, true));
            Assert.Equal(TelemetryFocusPane.Outside, WindowsGridFocusClassifier.Classify((i + 1) * 10, roles, false));
        }
        Assert.Equal(TelemetryFocusPane.OwnedOther, WindowsGridFocusClassifier.Classify(99, roles, true));
        Assert.Equal(TelemetryFocusPane.OwnedOther, WindowsGridFocusClassifier.Classify(10, roles with { Source = 0 }, true));
        Assert.Equal(TelemetryFocusPane.None, WindowsGridFocusClassifier.Classify(0, default, true));
    }

    /// <summary>Programmable evidence and action boundaries, deliberately independent of native implementations.</summary>
    private sealed class Actions : IGridAccessibilityActions, IWindowsGridFocusEvidence
    {
        /// <summary>Original configured result, including unknown values for compatibility checks.</summary>
        internal GridAccessibilityResult Result = GridAccessibilityResult.Applied;
        /// <summary>Original exception instance, never replaced by observation.</summary>
        internal Exception? Error;
        /// <summary>Bitmask selecting failing first and second evidence reads.</summary>
        internal int ThrowCapture;
        /// <summary>Ordinal selecting an invalid enum read.</summary>
        internal int InvalidCapture;
        /// <summary>Actual action invocation count.</summary>
        internal int Calls;
        /// <summary>Actual evidence invocation count.</summary>
        internal int Captures;
        /// <summary>Original nullable coordinate forwarded without transformation.</summary>
        internal GridCoordinate? Cell;
        /// <summary>Selection is outside this focus boundary and must never execute.</summary>
        public GridAccessibilityResult MutateSelection(GridAccessibilityId id, GridSelectionMutation mutation) => throw new NotSupportedException();
        /// <summary>Returns or throws exactly the configured primary outcome.</summary>
        public GridAccessibilityResult Focus(GridAccessibilityId id, GridCoordinate? cell)
        {
            Calls++;
            Cell = cell;
            if (Error is not null) throw Error;
            return Result;
        }
        /// <summary>Produces different before/after categories or deliberate independent evidence faults.</summary>
        public TelemetryFocusSample CaptureFocusEvidence()
        {
            Captures++;
            if ((ThrowCapture & (1 << (Captures - 1))) != 0) throw new InvalidOperationException("PRIVATE-query");
            if (InvalidCapture == Captures) return new(default, default, (TelemetryFocusPane)99);
            return new(TelemetryFocusThreadRelation.NonOwner, TelemetryFocusThreadRelation.Owner,
                Captures == 1 ? TelemetryFocusPane.Source : TelemetryFocusPane.Table);
        }
    }

    /// <summary>Reads owned trace rows without retaining JSON parser storage.</summary>
    private static JsonElement[] Rows(string directory) => Directory.GetFiles(directory, "*.jsonl")
        .SelectMany(File.ReadLines).Select(line => { using var json = JsonDocument.Parse(line); return json.RootElement.Clone(); }).ToArray();
    /// <summary>Retrieves a fixed operation name.</summary>
    private static string Op(JsonElement row) => row.GetProperty("operation").GetString()!;
    /// <summary>Retrieves one closed attribute for a contract assertion.</summary>
    private static string Attr(JsonElement row, string key) => row.GetProperty("attributes").GetProperty(key).GetString()!;
}
