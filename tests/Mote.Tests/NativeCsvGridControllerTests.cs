using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Mote.Configuration;
using Mote.Engine;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Accessibility;
using Mote.Native.Viewport;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Independent source-backed CSV command workflows through the real controller lane.</summary>
public sealed class NativeCsvGridControllerTests
{
    /// <summary>CSV opens as real records, including quoted physical newlines, without inferred headers.</summary>
    [Fact]
    public async Task Startup_delivers_logical_grid_without_mutating_source()
    {
        using var fixture = await Fixture.Open("name,value\r\n\"two\r\nlines\",42\r\n");
        var grid = fixture.Shell.Analysis!.Grid!;
        Assert.True(fixture.Shell.Analysis.ShowPreview);
        Assert.Equal(2, grid.Rows.Count);
        Assert.Equal(0, grid.Rows[0].Ordinal);
        Assert.Equal(2, grid.Rows[1].Width);
        Assert.Equal(fixture.Original, fixture.Shell.Document!.Text);
        Assert.False(fixture.Shell.Document.IsModified);
    }

    /// <summary>A rebase changes presentation authority even when the source revision stays unchanged.</summary>
    [Fact]
    public async Task Old_same_version_identity_is_rejected_after_row_and_column_rebase()
    {
        using var fixture = await Fixture.Open(string.Join('\n', Enumerable.Range(0, 100)
            .Select(row => string.Join(',', Enumerable.Range(0, 20).Select(column => $"r{row}c{column}")))));
        var shell = fixture.Shell;
        var old = shell.Analysis!.Identity;
        shell.Window(new(old, 50, new GridRange(10, 4), 8));
        await shell.Until(() => shell.Analysis?.Grid?.Rows.FirstOrDefault()?.Ordinal == 50 &&
            shell.Analysis.Identity != old);
        Assert.Equal(old.Document, shell.Analysis!.Identity.Document);
        Assert.Equal(10, shell.Analysis.Grid!.Rows[0].Cells[0].Column);
        shell.Intent(new(old, NativeGridIntentKind.CopyValue, 0, 0));
        Assert.Equal(0, shell.ClipboardCalls);
        Assert.Equal("sentinel", shell.Clipboard);
        shell.Intent(new(shell.Analysis.Identity, NativeGridIntentKind.CopyValue, 50, 10));
        await shell.Until(() => shell.ClipboardCalls == 1);
        Assert.Equal("r50c10", shell.Clipboard);
        Assert.Equal(fixture.Original, shell.Document!.Text);
    }

    /// <summary>An installed table from an older source revision cannot authorize clipboard or replacement.</summary>
    [Fact]
    public async Task Source_edit_rejects_old_grid_stamp()
    {
        using var fixture = await Fixture.Open("a,b\r\nc,d");
        var shell = fixture.Shell;
        var old = shell.Analysis!.Identity;
        shell.Edit("x,b\r\nc,d");
        shell.Intent(new(old, NativeGridIntentKind.CopyValue, 0, 0));
        shell.Intent(new(old, NativeGridIntentKind.Replace, 0, 0));
        Assert.Equal(0, shell.ClipboardCalls);
        Assert.Equal(0, shell.PromptCalls);
        Assert.Equal("x,b\r\nc,d", shell.Document!.Text);
    }

    /// <summary>NUL is refused before touching the native clipboard, rather than silently truncating data.</summary>
    [Fact]
    public async Task Copy_value_containing_nul_preserves_clipboard_and_source()
    {
        using var fixture = await Fixture.Open("\"a\0b\",tail");
        var shell = fixture.Shell;
        shell.Intent(new(shell.Analysis!.Identity, NativeGridIntentKind.CopyValue, 0, 0));
        await shell.Until(() => shell.Errors.Count > 0);
        Assert.Equal(0, shell.ClipboardCalls);
        Assert.Equal("sentinel", shell.Clipboard);
        Assert.Equal(fixture.Original, shell.Document!.Text);
        Assert.False(shell.Document.IsModified);
    }

    /// <summary>CSV serialization retains an actual empty final record and does not invent a trailing one.</summary>
    [Theory]
    [InlineData("a\r\n\r\n", "a\r\n\"\"")]
    [InlineData("\r\n\r\n", "\"\"\r\n\"\"")]
    public async Task Copy_csv_preserves_empty_final_row(string source, string expected)
    {
        using var fixture = await Fixture.Open(source);
        var shell = fixture.Shell;
        Assert.Equal(2, shell.Analysis!.Grid!.Rows.Count);
        shell.Intent(new(shell.Analysis.Identity, NativeGridIntentKind.CopyCsv, 0, 0, 1, 0));
        await shell.Until(() => shell.ClipboardCalls == 1);
        Assert.Equal(expected, shell.Clipboard);
        using var copy = await Fixture.Open(shell.Clipboard);
        Assert.Equal(2, copy.Shell.Analysis!.Grid!.Rows.Count);
        Assert.Equal(source, shell.Document!.Text);
    }

    /// <summary>A quoted-cell replacement preserves surrounding bytes and belongs to exactly one undo transaction.</summary>
    [Fact]
    public async Task Replace_preserves_quotes_neighbors_and_crlf_with_one_undo()
    {
        using var fixture = await Fixture.Open("left,\"old\",right\r\nnext,row,tail\r\n");
        var shell = fixture.Shell;
        shell.Replacement = "new,\"value\"\r\nline";
        shell.Intent(new(shell.Analysis!.Identity, NativeGridIntentKind.Replace, 0, 1));
        await shell.Until(() => shell.Document!.IsModified);
        Assert.Equal("old", shell.PromptValue);
        const string expected = "left,\"new,\"\"value\"\"\r\nline\",right\r\nnext,row,tail\r\n";
        shell.Save();
        await shell.Until(() => !shell.Document!.IsModified && File.ReadAllText(fixture.Path) == expected);
        Assert.Equal(expected.Length, shell.Document!.TotalLength);
        Assert.Equal(expected, shell.Document.Text);
        shell.Undo();
        Assert.Equal(fixture.Original, shell.Document!.Text);
        Assert.True(shell.Document.IsModified); // The saved revision is the replacement, not the original.
    }

    /// <summary>A prepared clipboard completion is discarded when a source edit supersedes its snapshot.</summary>
    [Fact]
    public async Task Pending_copy_cannot_publish_after_source_edit()
    {
        using var fixture = await Fixture.Open("a,b\r\nc,d");
        var shell = fixture.Shell;
        shell.Intent(new(shell.Analysis!.Identity, NativeGridIntentKind.CopyValue, 0, 0));
        await shell.WaitPosted();
        shell.Edit("user,b\r\nc,d");
        await shell.Drain();
        Assert.Equal(0, shell.ClipboardCalls);
        Assert.Equal("sentinel", shell.Clipboard);
        Assert.Equal("user,b\r\nc,d", shell.Document!.Text);
    }

    /// <summary>Settling source composition precedes Replace admission and invalidates the precommit Grid identity.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Additional_replace_settles_or_vetoes_source_composition_without_stale_edit(bool veto)
    {
        using var fixture = await Fixture.Open("left,old,right\r\n");
        var shell = fixture.Shell;
        var identity = shell.Analysis!.Identity;
        var commits = shell.CommitCalls;
        shell.PendingSource = "left,中文,right\r\n";
        shell.VetoPendingCommit = veto;
        shell.Replacement = "must-not-replace";
        shell.Intent(new(identity, NativeGridIntentKind.Replace, 0, 1));
        Assert.Equal(commits + 1, shell.CommitCalls);
        Assert.Equal(0, shell.PromptCalls);
        Assert.Equal(veto ? fixture.Original : "left,中文,right\r\n", shell.Document!.Text);
        if (veto)
        {
            Assert.Equal("left,中文,right\r\n", shell.PendingSource);
            Assert.False(shell.Document.IsModified);
            return;
        }
        Assert.Null(shell.PendingSource);
        Assert.NotEqual(identity.Document, shell.Document.Stamp);
        shell.Undo();
        Assert.Equal(fixture.Original, shell.Document!.Text);
        Assert.False(shell.Document.IsModified);
    }

    /// <summary>A queued exact-value Copy cannot publish after same-version table geometry is replaced.</summary>
    [Fact]
    public async Task Additional_pending_copy_is_rejected_after_same_version_grid_reinstall()
    {
        using var fixture = await Fixture.Open("a,b\r\nc,d\r\ne,f");
        var shell = fixture.Shell;
        var old = shell.Analysis!.Identity;
        shell.Intent(new(old, NativeGridIntentKind.CopyValue, 0, 0));
        await shell.WaitPosted();
        shell.Window(new(old, 1, new GridRange(1, 1), 1));
        await shell.Until(() => shell.Analysis?.Grid?.Rows.FirstOrDefault()?.Ordinal == 1 &&
            shell.Analysis.Identity != old);
        Assert.Equal(old.Document, shell.Analysis!.Identity.Document);
        Assert.Equal(0, shell.ClipboardCalls);
        Assert.Equal("sentinel", shell.Clipboard);
        Assert.Equal(fixture.Original, shell.Document!.Text);
        Assert.False(shell.Document.IsModified);
        shell.Intent(new(shell.Analysis.Identity, NativeGridIntentKind.CopyValue, 1, 1));
        await shell.Until(() => shell.ClipboardCalls == 1);
        Assert.Equal("d", shell.Clipboard);
    }

    /// <summary>Continuous profile shares canonical Grid commands; mere Grid selection never modifies its source snapshot.</summary>
    [Fact]
    public async Task Additional_continuous_grid_selection_replace_save_and_single_undo_preserve_source()
    {
        using var fixture = await Fixture.Open("left,\"old\",right\r\nnext,row,tail\r\n", continuous: true);
        var shell = (CanvasGridShell)fixture.Shell;
        var before = shell.Binding!;
        shell.Intent(new(shell.Analysis!.Identity, NativeGridIntentKind.Select, 0, 1));
        Assert.Equal(before.BaseVersion, shell.Binding!.BaseVersion);
        Assert.Equal(fixture.Original, shell.Binding.Snapshot.GetText());
        Assert.False(shell.Modified);
        shell.Replacement = "new,value";
        shell.Intent(new(shell.Analysis.Identity, NativeGridIntentKind.Replace, 0, 1));
        const string expected = "left,\"new,value\",right\r\nnext,row,tail\r\n";
        Assert.Equal("old", shell.PromptValue);
        Assert.Equal(expected, shell.Binding!.Snapshot.GetText());
        Assert.True(shell.Modified);
        Assert.Null(shell.Document); // Continuous never installs a hidden page editor mirror.
        shell.Save();
        await shell.Until(() => !shell.Modified && File.ReadAllText(fixture.Path) == expected);
        shell.Undo();
        Assert.Equal(fixture.Original, shell.Binding!.Snapshot.GetText());
        Assert.True(shell.Modified);
    }

    /// <summary>FollowSource uses the source caret even when a detached blank table retains an irrelevant far or negative row.</summary>
    [Theory]
    [InlineData(1000)]
    [InlineData(-1)]
    public async Task FollowSource_recovers_blank_detached_grid_without_admitting_ignored_row(int ignoredRow)
    {
        using var fixture = await Fixture.Open("a\nb\nc\n");
        var shell = fixture.Shell;
        var actual = shell.Analysis!.Grid!;
        Assert.Equal(3, actual.Extent.ExactRowCount);
        Assert.Equal(0, shell.Document!.PageStart);
        var blank = new GridRenderProjection(actual.Version, actual.SourceLength, actual.Extent,
            actual.Completeness, actual.CertifiedCoverage, actual.TotalDiagnosticCount,
            new GridRange(1000, 1), actual.RequestedColumns, "", [], actual.Diagnostics,
            false, false, actual.DiagnosticsTruncated, false, new CsvGridAnchor.Row(1000));
        fixture.InjectPresentedGrid(blank);
        var identity = shell.Analysis!.Identity;
        Assert.Empty(shell.Analysis.Grid!.Rows);
        shell.Window(new(identity, ignoredRow, new GridRange(0, 1), 2, FollowSource: true));
        await shell.Until(() => shell.Analysis?.Identity != identity &&
            shell.Analysis?.Grid?.Rows.FirstOrDefault()?.Ordinal == 0);
        Assert.Equal(identity.Document, shell.Analysis!.Identity.Document);
        Assert.IsType<CsvGridAnchor.Source>(shell.Analysis.Grid!.RequestedAnchor);
        Assert.Equal(0, shell.Analysis.Grid.RequestedRows.Start);
        Assert.Equal("a", NativeCsvGrid.Display(shell.Analysis.Grid, shell.Analysis.Grid.Rows[0].Cells[0]));
        Assert.Equal(fixture.Original, shell.Document.Text);
        Assert.False(shell.Document.IsModified);
    }

    /// <summary>An ordinal request cannot install invented row zero in an exact-empty CSV.</summary>
    [Fact]
    public async Task FollowSource_companion_exact_empty_csv_refuses_ordinal_row_zero()
    {
        using var fixture = await Fixture.Open("");
        var shell = fixture.Shell;
        Assert.Equal(0, shell.Analysis!.Grid!.Extent.ExactRowCount);
        var identity = shell.Analysis.Identity;
        shell.Window(new(identity, 0, new GridRange(0, 1), 1));
        Assert.Equal(identity, shell.Analysis!.Identity);
        Assert.Empty(shell.Analysis.Grid!.Rows);
        Assert.Equal("", shell.Document!.Text);
        Assert.False(shell.Document.IsModified);
    }

    /// <summary>Clipboard values are exact decoded source, not the bounded native display's visible control substitutions.</summary>
    [Fact]
    public async Task Copy_value_and_rows_use_source_not_display()
    {
        using var fixture = await Fixture.Open("left,\"line\r\nwith\ttab\",tail\r\n");
        var shell = fixture.Shell;
        shell.Intent(new(shell.Analysis!.Identity, NativeGridIntentKind.CopyValue, 0, 1));
        await shell.Until(() => shell.ClipboardCalls == 1);
        Assert.Equal("line\r\nwith\ttab", shell.Clipboard);
        shell.Intent(new(shell.Analysis.Identity, NativeGridIntentKind.CopyRows, 0, 0));
        await shell.Until(() => shell.ClipboardCalls == 2);
        Assert.Equal(fixture.Original, shell.Clipboard);
        Assert.False(shell.Document!.IsModified);
    }

    /// <summary>Cancel and a source edit during the modal prompt never authorize a stale structured transaction.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_or_stale_prompt_creates_no_replace_transaction(bool editDuringPrompt)
    {
        using var fixture = await Fixture.Open("left,old,right\r\n");
        var shell = fixture.Shell;
        shell.Replacement = editDuringPrompt ? "replacement" : null;
        if (editDuringPrompt) shell.DuringPrompt = () => shell.Edit("left,user,right\r\n");
        shell.Intent(new(shell.Analysis!.Identity, NativeGridIntentKind.Replace, 0, 1));
        await shell.Drain();
        Assert.Equal(editDuringPrompt ? "left,user,right\r\n" : fixture.Original, shell.Document!.Text);
        shell.Undo();
        Assert.Equal(fixture.Original, shell.Document!.Text);
        Assert.False(shell.Document.IsModified);
    }

    /// <summary>Invalid syntax and absent coordinates must never enter replacement prompts or exact-value copying.</summary>
    [Theory]
    [InlineData("\"unclosed,tail", 0, 0)]
    [InlineData("a,b\r\nx", 1, 1)]
    public async Task Malformed_or_missing_cell_refuses_copy_and_replace(string source, int row, int column)
    {
        using var fixture = await Fixture.Open(source);
        var shell = fixture.Shell;
        shell.Intent(new(shell.Analysis!.Identity, NativeGridIntentKind.CopyValue, row, column));
        shell.Intent(new(shell.Analysis.Identity, NativeGridIntentKind.Replace, row, column));
        await shell.Until(() => shell.Errors.Count >= 2);
        Assert.Equal(0, shell.ClipboardCalls);
        Assert.Equal(0, shell.PromptCalls);
        Assert.Equal(source, shell.Document!.Text);
    }

    /// <summary>Field resource limits cannot be bypassed by a structured UI command.</summary>
    [Fact]
    public async Task Oversized_cell_refuses_copy_and_replace()
    {
        using var fixture = await Fixture.Open(new string('x', 9 * 1024 * 1024) + ",tail");
        var shell = fixture.Shell;
        await shell.Until(() => shell.Analysis?.Grid?.Rows.FirstOrDefault()?.Cells.FirstOrDefault().State == GridValueState.Oversized);
        Assert.Equal(GridValueState.Oversized, shell.Analysis!.Grid!.Rows[0].Cells[0].State);
        shell.Intent(new(shell.Analysis.Identity, NativeGridIntentKind.CopyValue, 0, 0));
        shell.Intent(new(shell.Analysis.Identity, NativeGridIntentKind.Replace, 0, 0));
        await shell.Until(() => shell.Errors.Count >= 2);
        Assert.Equal(0, shell.ClipboardCalls);
        Assert.Equal(0, shell.PromptCalls);
        Assert.False(shell.Document!.IsModified);
    }

    /// <summary>Hidden tables never retain command authority in explicit source-only layout.</summary>
    [Fact]
    public async Task Source_only_layout_refuses_grid_actions()
    {
        using var fixture = await Fixture.Open("a,b", sourceOnly: true);
        var shell = fixture.Shell;
        Assert.False(shell.Analysis!.ShowPreview);
        shell.Intent(new(shell.Analysis.Identity, NativeGridIntentKind.CopyValue, 0, 0));
        shell.Intent(new(shell.Analysis.Identity, NativeGridIntentKind.Replace, 0, 0));
        await shell.Drain();
        Assert.Equal(0, shell.ClipboardCalls);
        Assert.Equal(0, shell.PromptCalls);
        Assert.False(shell.Document!.IsModified);
    }

    /// <summary>Owns an isolated disk fixture and the actual controller/session driver.</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>All disk and settings state lives in a contained repository-local directory.</summary>
        private readonly RepoTemp _temp = new();
        /// <summary>The controller is retired before fixture files are deleted.</summary>
        private NativeEditorController? _controller;
        /// <summary>Original disk spelling, independent of the engine's native projection.</summary>
        internal string Original { get; private set; } = "";
        /// <summary>Disk authority used to verify that replacement preserved nonselected source.</summary>
        internal string Path { get; private set; } = "";
        /// <summary>Observed platform boundary; it never derives expected values from the controller.</summary>
        internal GridShell Shell { get; private set; } = new();

        /// <summary>Opens a CSV file through the normal startup workflow and awaits its installed analysis.</summary>
        internal static async Task<Fixture> Open(string text, bool sourceOnly = false, bool continuous = false)
        {
            var fixture = new Fixture { Original = text, Shell = continuous ? new CanvasGridShell() : new GridShell() };
            try
            {
                var path = fixture._temp.File("records.csv");
                fixture.Path = path;
                await File.WriteAllTextAsync(path, text);
                var configuration = MoteConfigLoader.Load(new MoteConfigLoadOptions
                { UserHomeDirectory = fixture._temp.Path, UseEnvironmentOverride = false });
                if (sourceOnly) configuration = configuration with { PreviewLayout = PreviewLayoutPreference.SourceOnly };
                fixture._controller = new(fixture.Shell, configuration, ThemePolicies.Get(configuration.ThemeId), path,
                    continuous ? EditorPresentationProfile.Continuous : null);
                fixture._controller.Run();
                await fixture.Shell.Until(() => (continuous
                    ? fixture.Shell is CanvasGridShell { Binding: { } binding } && binding.Title.Contains("records.csv") &&
                        fixture.Shell.Analysis?.Stamp == new NativeDocumentStamp(binding.DocumentGeneration, binding.BaseVersion)
                    : fixture.Shell.Document?.Title.Contains("records.csv") == true &&
                        fixture.Shell.Analysis?.Stamp == fixture.Shell.Document.Stamp) &&
                    (sourceOnly || fixture.Shell.Analysis.Grid is not null));
                return fixture;
            }
            catch { fixture.Dispose(); throw; }
        }

        /// <inheritdoc />
        public void Dispose() { _controller?.Dispose(); _temp.Dispose(); }

        /// <summary>Injects only valid bounded delivery state while retaining actual document identity and certified policy facts.</summary>
        internal void InjectPresentedGrid(GridRenderProjection grid)
        {
            var field = typeof(NativeEditorController).GetField("_presentedPreview", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var current = Assert.IsType<NativeAnalysisView>(field.GetValue(_controller));
            Assert.Equal(current.Stamp.Version, grid.Version);
            var blank = current with { Grid = grid };
            field.SetValue(_controller, blank);
            Shell.SetAnalysis(blank);
        }
    }

    /// <summary>Minimal UI dispatcher with a real, separately observed clipboard and modal reentrancy hook.</summary>
    private class GridShell : INativeEditorShell
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
        public void SetAnalysis(NativeAnalysisView view) => Analysis = view;
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
        public void SetSelection(int displayAnchor, int displayActive) { }
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
        /// <summary>Raises a user command through the actual subscribed controller boundary.</summary>
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

    /// <summary>Only the existing canvas adapter boundary is modeled; no geometry, painting, or native input behavior is invented.</summary>
    private sealed class CanvasGridShell : GridShell, INativeCanvasShell
    {
        /// <summary>Immutable engine snapshot last bound to the input host.</summary>
        internal NativeCanvasBinding? Binding { get; private set; }
        /// <summary>Actual controller chrome modified flag, including post-save and Undo updates.</summary>
        internal bool Modified { get; private set; }
        /// <inheritdoc />
        public bool CanvasEnabled => true;
        /// <inheritdoc />
        public bool IsCanvasComposing => false;
        /// <inheritdoc />
        public int MaxCanvasInputLength => CanvasInputWindowSelector.MaxLength;
#pragma warning disable CS0067 // These adapter events are outside the scoped Grid controller workflow.
        /// <inheritdoc />
        public event Action<CanvasCommittedEdit>? CanvasEditCommitted;
        /// <inheritdoc />
        public event Action<double>? CanvasScrollRequested;
        /// <inheritdoc />
        public event Action<CanvasHorizontalAnchorRequest>? CanvasHorizontalAnchorRequested;
        /// <inheritdoc />
        public event Action<double>? CanvasViewportResized;
        /// <inheritdoc />
        public event Action<int, int>? CanvasSelectionRequested;
        /// <inheritdoc />
        public event Action? CanvasAccessibilityFailed;
#pragma warning restore CS0067
        /// <inheritdoc />
        public void SetCanvasBinding(NativeCanvasBinding binding) => Binding = binding;
        /// <inheritdoc />
        public void SetCanvasFrame(CanvasFrame frame) { }
        /// <inheritdoc />
        public void SetCanvasInputUnavailable(TextSnapshot snapshot, CanvasFrame frame, string reason) =>
            throw new InvalidOperationException($"Unexpected unavailable input in the small CSV fixture: {reason}");
        /// <inheritdoc />
        public void SetCanvasChrome(string title, string status, bool isModified) => Modified = isModified;
        /// <inheritdoc />
        public void SetCanvasSemantics(NativeCanvasSemantics semantics) { }
        /// <inheritdoc />
        public CanvasCaretGeometry? GetCanvasCaretGeometry(CanvasFrame frame, int sourceOffset) => null;
        /// <inheritdoc />
        public void SetCanvasAccessibility(AccessibleDocument document, IAccessibleViewport viewport) { }
    }
}
