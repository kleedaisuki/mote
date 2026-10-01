using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Mote.Configuration;
using Mote.Engine;
using Mote.Formats;
using Mote.Native;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Independent logical coordinate arithmetic and real-controller gesture authority checks.</summary>
public sealed class NativeGridNavigationTests
{
    /// <summary>Normalized endpoints remain exact even near the maximum ordinal.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(100, 24)]
    [InlineData(int.MaxValue, 256)]
    public void Axis_endpoints_are_exact(int count, int page)
    {
        var axis = NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, count, page, 0);
        var last = (int)Math.Max(0L, (long)count - page);
        Assert.Equal(last, axis.Last);
        Assert.Equal(0, axis.FromNormalized(-10));
        Assert.Equal(last, axis.FromNormalized(10));
        Assert.Equal(0, axis.FromNormalized(0));
        Assert.Equal(last, axis.FromNormalized(1));
        Assert.InRange(axis.Proportion, 0, 1);
    }

    /// <summary>Nonfinite native values cannot become integer coordinates.</summary>
    [Fact]
    public void Nonfinite_normalized_positions_are_rejected()
    {
        var axis = NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 100, 4, 20);
        Assert.Null(axis.FromNormalized(double.NaN));
        Assert.Null(axis.FromNormalized(double.PositiveInfinity));
        Assert.Null(axis.FromNormalized(double.NegativeInfinity));
    }

    /// <summary>Step saturation is defined mathematically, including signed-wide overflow boundaries.</summary>
    [Fact]
    public void Step_saturates_without_overflow()
    {
        var axis = NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, int.MaxValue, 24, 100);
        Assert.Equal(axis.Last, axis.Step(long.MaxValue));
        Assert.Equal(0, axis.Step(long.MinValue));
        Assert.Equal(101, axis.Step(1));
        Assert.Equal(99, axis.Step(-1));
    }

    /// <summary>Every geometry pair is bounded by row, column, and total-cell caps.</summary>
    [Fact]
    public void Page_caps_hold_for_extreme_and_regular_geometry()
    {
        foreach (var rows in new[] { int.MinValue, -1, 0, 1, 24, 256, int.MaxValue })
        foreach (var columns in new[] { int.MinValue, -1, 0, 1, 4, 64, int.MaxValue })
        {
            var page = NativeGridPlanner.Page(rows, columns);
            Assert.InRange(page.Rows, 1, GridRenderProjection.MaxRows);
            Assert.InRange(page.Columns, 1, GridRenderProjection.MaxColumns);
            Assert.InRange((long)page.Rows * page.Columns, 1, GridRenderProjection.MaxCells);
        }
    }

    /// <summary>Normalized conversion is monotonic, bounded, and roundtrips all small-domain origins.</summary>
    [Fact]
    public void Normalized_positions_obey_order_and_roundtrip_properties()
    {
        for (var count = 0; count <= 100; count++)
        for (var page = 1; page <= 10; page++)
        {
            var axis = NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, count, page, 0);
            var previous = 0;
            for (var i = 0; i <= 100; i++)
            {
                var ordinal = axis.FromNormalized(i / 100.0)!.Value;
                Assert.InRange(ordinal, previous, axis.Last);
                previous = ordinal;
            }
            for (var first = 0; first <= axis.Last; first++)
            {
                var positioned = NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, count, page, first);
                Assert.Equal(first, axis.FromNormalized(positioned.Position));
            }
        }
    }

    /// <summary>Invalid domains fail explicitly rather than manufacturing valid coordinates.</summary>
    [Fact]
    public void Axis_rejects_invalid_domains_and_slots_reject_overdelivery()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, -1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 1, 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeGridScrollAxis.Create((NativeGridExtentKind)99, 1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeGridPlanner.Slots(null, new(0, 257), 1000));
        Assert.Empty(NativeGridPlanner.Slots(null, new(int.MaxValue, 0), int.MaxValue));
    }

    /// <summary>Sparse ready records retain their absolute positions rather than collapsing gaps.</summary>
    [Fact]
    public void Sparse_slots_do_not_invent_rows_or_collapse_gaps()
    {
        var rows = new[] { new GridRow(10, new(0, 0), new(0, 0), 0, []),
            new GridRow(12, new(0, 0), new(0, 0), 0, []) };
        var grid = new GridRenderProjection(0, 0, new(13, 13, 1), AnalysisCompleteness.Complete,
            [new(0, 0)], 0, new(10, 4), new(0, 1), "", rows, [], true, false, false, false);
        var slots = NativeGridPlanner.Slots(grid, new(10, 4), 13);
        Assert.Equal(3, slots.Length);
        Assert.Same(rows[0], slots[0]);
        Assert.Null(slots[1]);
        Assert.Same(rows[1], slots[2]);
        Assert.All(NativeGridPlanner.Slots(null, new(10, 4), 13), Assert.Null);
    }

    /// <summary>A new gesture invalidates all phases of its predecessor on the same source revision.</summary>
    [Fact]
    public async Task Stale_A_cannot_move_B_and_terminal_duplicate_is_inert()
    {
        using var fixture = await Fixture.Open();
        var shell = fixture.Shell;
        var a = shell.Begin();
        shell.Send(a, NativeGridGesturePhase.Track, 30);
        var b = shell.Begin();
        var serial = shell.Navigation!.RequestSerial;
        shell.Send(a, NativeGridGesturePhase.Commit, 90);
        Assert.Equal(serial, shell.Navigation.RequestSerial);
        shell.Send(b, NativeGridGesturePhase.Commit, 50);
        var terminal = shell.Navigation.RequestSerial;
        shell.Send(b, NativeGridGesturePhase.Commit, 70);
        Assert.Equal(terminal, shell.Navigation.RequestSerial);
        await shell.Until(() => shell.Navigation is { Pending: false, Rows.First: 50 });
    }

    /// <summary>Geometry changes create a distinct coordinate epoch and cap the admitted page.</summary>
    [Fact]
    public async Task Geometry_change_advances_epoch()
    {
        using var fixture = await Fixture.Open();
        var before = fixture.Shell.Navigation!;
        var gesture = fixture.Shell.Begin(1000, 1000);
        Assert.NotEqual(before.Navigation.Epoch, gesture.Id.Navigation.Epoch);
        Assert.Equal(128, gesture.Frame.Rows.Page);
        Assert.Equal(64, gesture.Frame.Columns.Page);
        Assert.Equal(before.Navigation.Document, gesture.Id.Navigation.Document);
    }

    /// <summary>A same-version asynchronous ready refresh does not retire a live Track token.</summary>
    [Fact]
    public async Task Same_version_refresh_preserves_token()
    {
        using var fixture = await Fixture.Open();
        var shell = fixture.Shell;
        var token = shell.Begin();
        shell.Send(token, NativeGridGesturePhase.Track, 30);
        await shell.Until(() => shell.Navigation is { Pending: false, Rows.First: 30 });
        shell.Send(token, NativeGridGesturePhase.Commit, 60);
        await shell.Until(() => shell.Navigation is { Pending: false, Rows.First: 60 });
        Assert.Equal(token.Id.Navigation.Document, shell.Navigation!.Navigation.Document);
    }

    /// <summary>A terminal callback consumes A before installing pending state that can synchronously admit B.</summary>
    [Fact]
    public async Task Reentrant_B_survives_A_terminal_tail()
    {
        using var fixture = await Fixture.Open();
        var shell = fixture.Shell;
        var a = shell.Begin();
        NativeGridGesture? b = null;
        shell.DuringNavigation = () => { shell.DuringNavigation = null; b = shell.Begin(); };
        shell.Send(a, NativeGridGesturePhase.Commit, 30);
        Assert.NotNull(b);
        shell.Send(b!, NativeGridGesturePhase.Commit, 70);
        await shell.Until(() => shell.Navigation is { Pending: false, Rows.First: 70 });
    }

    /// <summary>Pending navigation has no source-backed Copy authority, and End does not edit source or selection.</summary>
    [Fact]
    public async Task Pending_copy_is_refused_and_End_uses_exact_final_viewport()
    {
        using var fixture = await Fixture.Open();
        var shell = fixture.Shell;
        var original = shell.Document!;
        var selectionCalls = shell.SelectionCalls;
        var ready = shell.Analysis!.Identity;
        var token = shell.Begin();
        shell.Send(token, NativeGridGesturePhase.Commit, 0, NativeGridTargetKind.End);
        Assert.True(shell.Navigation!.Pending);
        Assert.Null(shell.Navigation.Ready);
        shell.Intent(new(ready, NativeGridIntentKind.CopyValue, 0, 0));
        Assert.Equal(0, shell.ClipboardCalls);
        await shell.Until(() => shell.Navigation is { Pending: false, Rows.First: 96 });
        Assert.Equal(120, shell.Navigation!.Rows.Count);
        Assert.Equal(original.Text, shell.Document!.Text);
        Assert.Equal(original.Stamp, shell.Document.Stamp);
        Assert.False(shell.Document.IsModified);
        Assert.Equal(selectionCalls, shell.SelectionCalls);
        Assert.Equal(fixture.Source, await File.ReadAllTextAsync(fixture.Path));
    }

    /// <summary>A changed source revision rejects a previously admitted token immediately.</summary>
    [Fact]
    public async Task Source_edit_retires_old_token()
    {
        using var fixture = await Fixture.Open();
        var shell = fixture.Shell;
        var token = shell.Begin();
        shell.Edit("changed,value");
        var before = shell.Navigation;
        shell.Send(token, NativeGridGesturePhase.Commit, 80);
        Assert.Same(before, shell.Navigation);
        Assert.Equal("changed,value", shell.Document!.Text);
        await shell.Until(() => shell.Navigation is { Pending: false, Rows.Count: 1 });
    }

    /// <summary>Malformed phases do not consume authority; a subsequent valid terminal still works.</summary>
    [Fact]
    public async Task Invalid_phase_and_kind_leave_live_token_usable()
    {
        using var fixture = await Fixture.Open();
        var shell = fixture.Shell;
        var token = shell.Begin();
        var before = shell.Navigation;
        shell.Send(token, (NativeGridGesturePhase)99, 30);
        shell.Send(token, NativeGridGesturePhase.Commit, 30, (NativeGridTargetKind)99);
        shell.Send(token, NativeGridGesturePhase.Commit, -1);
        Assert.Same(before, shell.Navigation);
        shell.Send(token, NativeGridGesturePhase.Commit, 50);
        await shell.Until(() => shell.Navigation is { Pending: false, Rows.First: 50 });
    }

    /// <summary>Composition invalidates coordinate authority rather than permitting a late scroll request.</summary>
    [Fact]
    public async Task Composition_retires_live_gesture_before_navigation()
    {
        using var fixture = await Fixture.Open();
        var shell = fixture.Shell;
        var original = shell.Document!;
        var token = shell.Begin();
        shell.PendingSource = "preedit";
        shell.Send(token, NativeGridGesturePhase.Track, 30);
        Assert.Null(shell.Navigation);
        shell.PendingSource = null;
        shell.Send(token, NativeGridGesturePhase.Commit, 50);
        Assert.Null(shell.Navigation);
        Assert.Same(original, shell.Document);
    }

    /// <summary>Cancel retires its token and restores last accepted delivery, not an uninstalled Track target.</summary>
    [Fact]
    public async Task Cancel_restores_ready_authority_and_rejects_late_commit()
    {
        using var fixture = await Fixture.Open();
        var shell = fixture.Shell;
        var token = shell.Begin();
        shell.Send(token, NativeGridGesturePhase.Track, 30);
        shell.Send(token, NativeGridGesturePhase.Cancel, 30);
        await shell.Until(() => shell.Navigation is { Pending: false, Ready: not null, Rows.First: 0 });
        var ready = shell.Navigation;
        shell.Send(token, NativeGridGesturePhase.Commit, 60);
        Assert.Same(ready, shell.Navigation);
    }

    /// <summary>An unused gesture does not invalidate a frozen source command from its unchanged ready table.</summary>
    [Fact]
    public async Task Unused_cancel_preserves_ready_identity_and_source_command_authority()
    {
        using var fixture = await Fixture.Open();
        var shell = fixture.Shell;
        var identity = shell.Analysis!.Identity;
        var before = shell.Document!;
        var serial = fixture.AnalysisSerial;
        var token = shell.Begin();
        shell.Send(token, NativeGridGesturePhase.Cancel, 0);
        Assert.Equal(identity, shell.Analysis.Identity);
        Assert.Equal(identity, shell.Navigation!.Ready);
        Assert.False(shell.Navigation.Pending);
        Assert.Equal(serial, fixture.AnalysisSerial);
        shell.Intent(new(identity, NativeGridIntentKind.CopyValue, 0, 0));
        await shell.Until(() => shell.ClipboardCalls == 1);
        Assert.Equal("r0", shell.Clipboard);
        Assert.Same(before, shell.Document);
        Assert.Equal(fixture.Source, await File.ReadAllTextAsync(fixture.Path));
    }

    /// <summary>Restoring a retained older prefix payload cannot downgrade already learned exact same-version facts.</summary>
    [Fact]
    public async Task Cancel_restoring_older_prefix_payload_preserves_exact_navigation_facts()
    {
        using var fixture = await Fixture.Open();
        var shell = fixture.Shell;
        var before = shell.Navigation!;
        var identity = shell.Analysis!.Identity;
        fixture.RetainOlderPrefixPayload();
        var token = shell.Begin();
        shell.Send(token, NativeGridGesturePhase.Track, 30);
        shell.Send(token, NativeGridGesturePhase.Cancel, 30);
        Assert.False(shell.Navigation!.Pending);
        Assert.Equal(NativeGridExtentKind.Exact, shell.Navigation.Rows.Kind);
        Assert.Equal(before.Rows.Count, shell.Navigation.Rows.Count);
        Assert.Equal(before.Columns.Count, shell.Navigation.Columns.Count);
        Assert.Equal(0, shell.Navigation.Rows.First);
        Assert.NotEqual(identity, shell.Navigation.Ready);
        Assert.Null(shell.Analysis!.Grid!.Extent.ExactRowCount);
        Assert.Equal(fixture.Source, shell.Document!.Text);
    }

    /// <summary>A native geometry event immediately retires a live token even before a replacement begins.</summary>
    [Fact]
    public async Task Geometry_event_retires_live_token_and_recertifies_page()
    {
        using var fixture = await Fixture.Open();
        var shell = fixture.Shell;
        var token = shell.Begin();
        shell.Geometry(12, 2);
        var changed = shell.Navigation!;
        Assert.True(changed.Pending);
        Assert.Null(changed.Ready);
        Assert.NotEqual(token.Id.Navigation.Epoch, changed.Navigation.Epoch);
        shell.Send(token, NativeGridGesturePhase.Commit, 70);
        Assert.Same(changed, shell.Navigation);
        await shell.Until(() => shell.Navigation is { Pending: false, Rows.Page: 12, Columns.Page: 2 });
        Assert.Equal(0, shell.Navigation!.Rows.First);
    }

    /// <summary>A burst replaces the latest logical interest without starting one parser request per sample.</summary>
    [Fact]
    public async Task Track_burst_coalesces_to_one_analysis_and_latest_interest()
    {
        using var fixture = await Fixture.Open();
        var shell = fixture.Shell;
        var token = shell.Begin();
        var serial = fixture.AnalysisSerial;
        var installations = shell.ReadyOrigins.Count;
        for (var row = 1; row <= 50; row++) shell.Send(token, NativeGridGesturePhase.Track, row);
        Assert.Equal(serial, fixture.AnalysisSerial);
        Assert.Equal(installations, shell.ReadyOrigins.Count);
        Assert.True(shell.Navigation!.Pending);
        Assert.Null(shell.Navigation.Ready);
        Assert.Equal(50, shell.Navigation.Rows.First);
        await shell.Until(() => shell.Navigation is { Pending: false, Rows.First: 50 });
        Assert.Equal(serial + 1, fixture.AnalysisSerial);
        Assert.Equal(new[] { 50 }, shell.ReadyOrigins.Skip(installations));
    }

    /// <summary>A queued older result cannot restore ready authority after a newer pending sample.</summary>
    [Fact]
    public async Task Queued_stale_delivery_does_not_publish_during_new_pending_interest()
    {
        using var fixture = await Fixture.Open();
        var shell = fixture.Shell;
        var token = shell.Begin();
        var installations = shell.ReadyOrigins.Count;
        shell.Send(token, NativeGridGesturePhase.Track, 30);
        await shell.WaitPosted();
        shell.PumpOne();
        await shell.WaitPosted();
        shell.Send(token, NativeGridGesturePhase.Track, 70);
        shell.PumpOne();
        Assert.True(shell.Navigation!.Pending);
        Assert.Null(shell.Navigation.Ready);
        Assert.Equal(70, shell.Navigation.Rows.First);
        Assert.DoesNotContain(30, shell.ReadyOrigins.Skip(installations));
        await shell.Until(() => shell.Navigation is { Pending: false, Rows.First: 70 });
        Assert.DoesNotContain(30, shell.ReadyOrigins.Skip(installations));
    }

    /// <summary>A real native installation exception restores last delivered coordinates without Copy authority.</summary>
    [Fact]
    public async Task SetAnalysis_throw_retains_last_delivered_placement_without_command_identity()
    {
        using var fixture = await Fixture.Open();
        var shell = fixture.Shell;
        shell.Send(shell.Begin(), NativeGridGesturePhase.Commit, 20);
        await shell.Until(() => shell.Navigation is { Pending: false, Rows.First: 20 });
        var delivered = shell.Analysis!;
        shell.ThrowNextGridAnalysis = true;
        shell.Send(shell.Begin(), NativeGridGesturePhase.Commit, 70);
        await shell.Until(() => shell.Navigation?.Status.Contains("delivery failed") == true);
        Assert.Equal(1, shell.AnalysisFailures);
        Assert.Same(delivered, shell.Analysis);
        Assert.Equal(20, shell.Navigation!.Rows.First);
        Assert.True(shell.Navigation.Pending);
        Assert.Null(shell.Navigation.Ready);
        shell.Intent(new(delivered.Identity, NativeGridIntentKind.CopyValue, 20, 0));
        Assert.Equal(0, shell.ClipboardCalls);
        shell.Send(shell.Begin(), NativeGridGesturePhase.Commit, 60);
        await shell.Until(() => shell.Navigation is { Pending: false, Ready: not null, Rows.First: 60 });
        Assert.Equal(fixture.Source, shell.Document!.Text);
        Assert.False(shell.Document.IsModified);
    }

    /// <summary>Owns repository-local disk/config state and the production controller.</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>Contains every fixture file below the repository temporary root.</summary>
        private readonly RepoTemp _temp = new();
        /// <summary>Production state machine, disposed before disk cleanup.</summary>
        private NativeEditorController? _controller;
        /// <summary>Deterministic UI dispatcher without native clipboard access.</summary>
        internal NavigationShell Shell { get; } = new();
        /// <summary>Independent exact disk source retained for nonmutation assertions.</summary>
        internal string Source { get; private set; } = "";
        /// <summary>Fixture disk path, never a user file.</summary>
        internal string Path { get; private set; } = "";
        /// <summary>Observes parser scheduling separately from the request-placement serial.</summary>
        internal long AnalysisSerial => (long)typeof(NativeEditorController)
            .GetField("_analysisSerial", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_controller)!;
        /// <summary>Models a retained valid same-version delivery that predates exact certification; never alters current extent facts.</summary>
        internal void RetainOlderPrefixPayload()
        {
            var view = Shell.Analysis!;
            var grid = view.Grid!;
            var last = grid.Rows[^1];
            var prefix = new GridRenderProjection(grid.Version, grid.SourceLength,
                new(last.Ordinal + 1, null, null), AnalysisCompleteness.CoveredRegion,
                [new(0, last.RecordDelimiter.End)], null, grid.RequestedRows, grid.RequestedColumns,
                grid.DisplayText, grid.Rows, grid.Diagnostics, grid.RowsTruncated, grid.ColumnsTruncated,
                grid.DiagnosticsTruncated, grid.ValuesTruncated, grid.RequestedAnchor);
            typeof(NativeEditorController).GetField("_gridDeliveredView", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(_controller, view with { Grid = prefix });
        }
        /// <summary>Opens exactly 120 logical records, independently counting rows without a trailing delimiter.</summary>
        internal static async Task<Fixture> Open()
        {
            var fixture = new Fixture();
            try
            {
                fixture.Source = string.Join('\n', Enumerable.Range(0, 120).Select(i => $"r{i},v{i}"));
                fixture.Path = fixture._temp.File("navigation.csv");
                await File.WriteAllTextAsync(fixture.Path, fixture.Source);
                var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
                { UserHomeDirectory = fixture._temp.Path, UseEnvironmentOverride = false });
                fixture._controller = new(fixture.Shell, config, ThemePolicies.Get(config.ThemeId), fixture.Path);
                fixture._controller.Run();
                await fixture.Shell.Until(() => fixture.Shell.Navigation is { Pending: false, Rows.Kind: NativeGridExtentKind.Exact });
                return fixture;
            }
            catch { fixture.Dispose(); throw; }
        }
        /// <inheritdoc />
        public void Dispose() { _controller?.Dispose(); _temp.Dispose(); }
    }
    /// <summary>Minimal UI dispatcher with a real, separately observed clipboard and modal reentrancy hook.</summary>
    private sealed class NavigationShell : INativeEditorShell
    {
        /// <summary>Background completions are delivered only when tests explicitly pump the UI boundary.</summary>
        private readonly ConcurrentQueue<Action> _posted = new();
        /// <summary>Last installed bounded source page, not a duplicate canonical document.</summary>
        internal NativeDocumentView? Document { get; private set; }
        /// <summary>Last installed semantic map and its exact presentation identity.</summary>
        internal NativeAnalysisView? Analysis { get; private set; }
        /// <summary>A nonempty sentinel makes premature or truncating publication observable.</summary>
        internal string Clipboard { get; private set; } = "sentinel";
        /// <summary>Counts actual calls to the platform publication boundary.</summary>
        internal int ClipboardCalls { get; private set; }
        /// <summary>Counts entries into the decoded-value editor, including cancellations.</summary>
        internal int PromptCalls { get; private set; }
        /// <summary>Exact decoded source value passed to the temporary editor.</summary>
        internal string? PromptValue { get; private set; }
        /// <summary>The user's result; null represents cancellation rather than an empty replacement.</summary>
        internal string? Replacement { get; set; }
        /// <summary>Injects a synchronous source revision change inside the modal editor.</summary>
        internal Action? DuringPrompt { get; set; }
        /// <summary>Exact final CSV source emitted when simulated native preedit commits.</summary>
        internal string? PendingSource { get; set; }
        /// <summary>Models a native input host that cannot safely settle its current composition.</summary>
        internal bool VetoPendingCommit { get; set; }
        /// <summary>Counts command preflight calls separately from value-editor entry.</summary>
        internal int CommitCalls { get; private set; }
        /// <summary>Witnesses rejected commands so negative checks do not rely only on elapsed time.</summary>
        internal List<string> Errors { get; } = [];
        /// <inheritdoc />
        public NativeLineEndingMode LineEndingMode => NativeLineEndingMode.Preserve;
        /// <inheritdoc />
        public bool PrefersDark => true;
        /// <inheritdoc />
        public bool IsTextComposing => PendingSource is not null;
#pragma warning disable CS0067 // Required interface events not exercised by these scoped workflows.
        /// <inheritdoc />
        public event Action? AppearanceChanged;
        /// <inheritdoc />
        public event Action? CompositionSettled;
        /// <inheritdoc />
        public event Action<string>? TextChanged;
        /// <inheritdoc />
        public event Action<int, int>? SelectionChanged;
        /// <inheritdoc />
        public event Action<NativePreviewActivation>? PreviewActivated;
        /// <inheritdoc />
        public event Action<NativeGridIntent>? GridIntentRequested;
        /// <inheritdoc />
        public event Action<NativeGridWindowRequest>? GridWindowRequested;
        /// <inheritdoc />
        public event Action? NewRequested;
        /// <inheritdoc />
        public event Action? OpenRequested;
        /// <inheritdoc />
        public event Action<NativeSaveRequest>? SaveRequested;
        /// <inheritdoc />
        public event Action? UndoRequested;
        /// <inheritdoc />
        public event Action? RedoRequested;
        /// <inheritdoc />
        public event Action? FormatRequested;
        /// <inheritdoc />
        public event Action? PagePreviousRequested;
        /// <inheritdoc />
        public event Action? PageNextRequested;
        /// <inheritdoc />
        public event Action? FindRequested;
        /// <inheritdoc />
        public event Action? FindNextRequested;
        /// <inheritdoc />
        public event Action? GoToLineRequested;
        /// <inheritdoc />
        public event Action? SelectAllRequested;
        /// <inheritdoc />
        public event Action? CopyRequested;
        /// <inheritdoc />
        public event Action? CutRequested;
        /// <inheritdoc />
        public event EventHandler<NativeClosingEventArgs>? ClosingRequested;
        /// <inheritdoc />
        public event Action? Shown;
#pragma warning restore CS0067
        /// <inheritdoc />
        public void Run() => Shown?.Invoke();
        /// <inheritdoc />
        public void SetDocument(NativeDocumentView view) => Document = view;
        /// <inheritdoc />
        public void SetAnalysis(NativeAnalysisView view)
        {
            if (ThrowNextGridAnalysis && view.Grid is not null)
            {
                ThrowNextGridAnalysis = false;
                AnalysisFailures++;
                throw new InvalidOperationException("Independent native grid installation failure.");
            }
            Analysis = view; Navigation = view.GridNavigation;
            if (Navigation is { Pending: false, Ready: not null }) ReadyOrigins.Add(Navigation.Rows.First);
        }
        /// <inheritdoc />
        public void SetTheme(IThemePolicy theme) { }
        /// <inheritdoc />
        public void SetStatusNotice(string? notice) { }
        /// <inheritdoc />
        public bool CommitPendingText()
        {
            CommitCalls++;
            if (VetoPendingCommit) return false;
            if (PendingSource is not { } source) return true;
            PendingSource = null;
            Edit(source);
            return true;
        }
        /// <inheritdoc />
        public void SetSelection(int displayAnchor, int displayActive) { SelectionCalls++; }
        /// <inheritdoc />
        public void FocusSource() { }
        /// <inheritdoc />
        public string? PromptFind() => null;
        /// <inheritdoc />
        public int? PromptGoToLine() => null;
        /// <inheritdoc />
        public string? PromptGridReplacement(string currentValue)
        { PromptCalls++; PromptValue = currentValue; DuringPrompt?.Invoke(); return Replacement; }
        /// <inheritdoc />
        public void SetClipboardText(string text) { ClipboardCalls++; Clipboard = text; }
        /// <inheritdoc />
        public string? PickOpenFile() => null;
        /// <inheritdoc />
        public string? PickSaveFile(string? currentPath) => null;
        /// <inheritdoc />
        public bool ConfirmOverwrite(string path) => false;
        /// <inheritdoc />
        public bool ConfirmDiscard() => true;
        /// <inheritdoc />
        public void ShowError(string message) => Errors.Add(message);
        /// <inheritdoc />
        public void Post(Action action) => _posted.Enqueue(action);
        /// <inheritdoc />
        public void Close() { }
        /// <summary>Last installed logical frame, including pending requests.</summary>
        internal NativeGridScrollFrame? Navigation { get; private set; }
        /// <summary>Observes source-selection side effects independently.</summary>
        internal int SelectionCalls { get; private set; }
        /// <summary>One-shot callback models native installation reentrancy.</summary>
        internal Action? DuringNavigation { get; set; }
        /// <summary>Fail exactly one native grid installation before accepting any of its visible state.</summary>
        internal bool ThrowNextGridAnalysis { get; set; }
        /// <summary>Counts genuine SetAnalysis exceptions observed by the production recovery lane.</summary>
        internal int AnalysisFailures { get; private set; }
        /// <summary>Independent history of successfully installed ready viewport origins.</summary>
        internal List<int> ReadyOrigins { get; } = [];
        /// <inheritdoc />
        public event Action<int, int>? GridGeometryChanged;
        /// <inheritdoc />
        public event Func<NativeGridGestureBegin, NativeGridGesture?>? GridGestureBeginning;
        /// <inheritdoc />
        public event Action<NativeGridGestureAction>? GridGestureRequested;
        /// <inheritdoc />
        public void SetGridNavigation(NativeGridScrollFrame? frame)
        { Navigation = frame; DuringNavigation?.Invoke(); }
        /// <summary>Admits a token through the actual controller event boundary.</summary>
        internal NativeGridGesture Begin(int rows = 24, int columns = 4)
        { return Assert.IsType<NativeGridGesture>(GridGestureBeginning?.Invoke(new(Navigation!, rows, columns))); }
        /// <summary>Dispatches one captured phase without native clipboard or input mutations.</summary>
        internal void Send(NativeGridGesture token, NativeGridGesturePhase phase, int row,
            NativeGridTargetKind kind = NativeGridTargetKind.Viewport) =>
            GridGestureRequested?.Invoke(new(token.Id, phase, row, 0, kind));
        /// <summary>Emits an actual platform geometry event without starting a new gesture.</summary>
        internal void Geometry(int rows, int columns) => GridGeometryChanged?.Invoke(rows, columns);
        /// <summary>Delivers exactly one queued callback to distinguish dispatch admission from parser completion.</summary>
        internal void PumpOne()
        { Assert.True(_posted.TryDequeue(out var action)); action(); }
        /// <summary>Raises a source-backed command through the controller event boundary.</summary>
        internal void Intent(NativeGridIntent intent) => GridIntentRequested?.Invoke(intent);
        /// <summary>Raises a user command through the actual subscribed controller boundary.</summary>
        internal void Window(NativeGridWindowRequest request) => GridWindowRequested?.Invoke(request);
        /// <summary>Raises a user command through the actual subscribed controller boundary.</summary>
        internal void Edit(string text) => TextChanged?.Invoke(text);
        /// <summary>Raises a user command through the actual subscribed controller boundary.</summary>
        internal void Undo() => UndoRequested?.Invoke();
        /// <summary>Raises a user command through the actual subscribed controller boundary.</summary>
        internal void Save() => NativeSaveRequest.Receive(NativeSaveKind.Save).Dispatch(SaveRequested);

        /// <summary>Waits for an actual queued completion without executing it, enabling deterministic stale-publication tests.</summary>
        internal async Task WaitPosted()
        {
            var watch = Stopwatch.StartNew();
            while (_posted.IsEmpty && watch.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(10);
            Assert.False(_posted.IsEmpty, "Expected a background completion before source mutation.");
        }

        /// <summary>Drains queued work and bounded background completions without advancing product state itself.</summary>
        internal async Task Drain()
        { for (var i = 0; i < 20; i++) { while (_posted.TryDequeue(out var action)) action(); await Task.Delay(10); } }

        /// <summary>Pumps actual controller completions until an independently observed outcome appears.</summary>
        internal async Task Until(Func<bool> ready)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(15))
            {
                while (_posted.TryDequeue(out var action)) action();
                if (ready()) return;
                await Task.Delay(10);
            }
            Assert.Fail($"Controller outcome unavailable; analysis={Analysis?.Status}; errors={string.Join("; ", Errors)}");
        }
    }

}
