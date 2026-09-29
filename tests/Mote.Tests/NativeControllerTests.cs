using System.Collections.Concurrent;
using System.Diagnostics;
using Mote.Configuration;
using Mote.Formats;
using Mote.Native;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>UI-independent tests of source/native projection and editor orchestration.</summary>
public sealed class NativeControllerTests
{
    /// <summary>RichEdit CRLF coordinates preserve mixed original line endings outside an edit.</summary>
    [Fact]
    public void Projection_maps_mixed_newlines_without_rewriting_unedited_source()
    {
        const string source = "a\nb\rc\r\nd";
        var projection = new NativeTextProjection(source, NativeLineEndingMode.CrLf);
        Assert.Equal("a\r\nb\r\nc\r\nd", projection.Display);
        Assert.Null(projection.Difference(projection.Display));
        Assert.Equal(3, projection.ToDisplay(2));
        var change = projection.Difference("a\r\nB\r\nc\r\nd");
        Assert.NotNull(change);
        Assert.Equal(source.IndexOf('b'), change.Value.Start);
        Assert.Equal(1, change.Value.DeleteLength);
        Assert.Equal("B", change.Value.InsertText);
        Assert.Equal("a\nB\rc\r\nd", source.Remove(change.Value.Start, change.Value.DeleteLength)
            .Insert(change.Value.Start, change.Value.InsertText));
    }

    /// <summary>Insertion after a projected newline adopts nearby source spelling.</summary>
    [Theory]
    [InlineData("a\nb", "a\r\nb\r\nc", "a\nb\nc")]
    [InlineData("a\rb", "a\r\nb\r\nc", "a\rb\rc")]
    [InlineData("a\r\nb", "a\r\nb\r\nc", "a\r\nb\r\nc")]
    public void Projection_normalizes_only_newly_inserted_line_breaks(string source, string displayEdit, string expected)
    {
        var projection = new NativeTextProjection(source, NativeLineEndingMode.CrLf);
        var change = projection.Difference(displayEdit);
        Assert.NotNull(change);
        Assert.Equal(expected, source.Remove(change.Value.Start, change.Value.DeleteLength)
            .Insert(change.Value.Start, change.Value.InsertText));
    }

    /// <summary>Deleting text between distinct CR and LF breaks must not erase a displayed blank line.</summary>
    [Fact]
    public void Projection_preserves_two_breaks_when_edit_joins_cr_and_lf()
    {
        const string source = "\ra\n";
        var projection = new NativeTextProjection(source, NativeLineEndingMode.CrLf);
        Assert.Equal("\r\na\r\n", projection.Display);
        const string editedDisplay = "\r\n\r\n";
        var change = projection.Difference(editedDisplay);
        Assert.NotNull(change);
        var result = source.Remove(change.Value.Start, change.Value.DeleteLength)
            .Insert(change.Value.Start, change.Value.InsertText);
        Assert.Equal(editedDisplay, new NativeTextProjection(result, NativeLineEndingMode.CrLf).Display);
        using var document = new Mote.Engine.Document(result);
        Assert.Equal(3, document.Snapshot.LineCount);
    }

    /// <summary>Any canonical native edit round-trips through the engine's source representation.</summary>
    [Fact]
    public void Randomized_projection_edits_round_trip_canonical_display()
    {
        var random = new Random(0x4E415449);
        var atoms = new[] { "a", "b", "😀", "\r", "\n", "\r\n" };
        var inserts = new[] { "", "X", "😀", "\r\n", "Y\r\nZ" };
        for (var caseNumber = 0; caseNumber < 5_000; caseNumber++)
        {
            var source = string.Concat(Enumerable.Range(0, random.Next(1, 18))
                .Select(_ => atoms[random.Next(atoms.Length)]));
            var projection = new NativeTextProjection(source, NativeLineEndingMode.CrLf);
            var display = projection.Display;
            var boundaries = Enumerable.Range(0, display.Length + 1).Where(index =>
                !SplitsPair(display, index) && !SplitsCrLf(display, index)).ToArray();
            var left = boundaries[random.Next(boundaries.Length)];
            var right = boundaries[random.Next(boundaries.Length)];
            if (left > right) (left, right) = (right, left);
            var edited = display[..left] + inserts[random.Next(inserts.Length)] + display[right..];
            var change = projection.Difference(edited);
            if (edited == display) { Assert.Null(change); continue; }
            Assert.NotNull(change);
            var result = source.Remove(change.Value.Start, change.Value.DeleteLength)
                .Insert(change.Value.Start, change.Value.InsertText);
            var reprojection = new NativeTextProjection(result, NativeLineEndingMode.CrLf).Display;
            Assert.True(edited == reprojection,
                $"Case {caseNumber}: source={Escape(source)}, edited={Escape(edited)}, got={Escape(reprojection)}");
        }
    }

    /// <summary>Short combinations exhaust seam joins, empty edits, and surrogate-safe boundaries.</summary>
    [Fact]
    public void Exhaustive_short_projection_edits_round_trip()
    {
        var atoms = new[] { "", "a", "😀", "\r", "\n", "\r\n" };
        var inserts = new[] { "", "X", "\r\n", "😀" };
        foreach (var first in atoms)
        foreach (var middle in atoms)
        foreach (var last in atoms)
        {
            var source = first + middle + last;
            var projection = new NativeTextProjection(source, NativeLineEndingMode.CrLf);
            var display = projection.Display;
            var boundaries = Enumerable.Range(0, display.Length + 1).Where(index =>
                !SplitsPair(display, index) && !SplitsCrLf(display, index)).ToArray();
            foreach (var left in boundaries)
            foreach (var right in boundaries.Where(index => index >= left))
            foreach (var insert in inserts)
            {
                var edited = display[..left] + insert + display[right..];
                var change = projection.Difference(edited);
                if (edited == display) { Assert.Null(change); continue; }
                Assert.NotNull(change);
                var result = source.Remove(change.Value.Start, change.Value.DeleteLength)
                    .Insert(change.Value.Start, change.Value.InsertText);
                var reprojected = new NativeTextProjection(result, NativeLineEndingMode.CrLf).Display;
                Assert.True(edited == reprojected,
                    $"source={Escape(source)}, edit={Escape(edited)}, got={Escape(reprojected)}");
            }
        }
    }

    /// <summary>Fake native shell drives open/edit/undo/redo/save with exact source persistence.</summary>
    [Fact]
    public async Task Controller_open_edit_undo_redo_save_preserves_source_newlines()
    {
        using var temp = new RepoTemp();
        var path = temp.File("note.txt");
        await File.WriteAllTextAsync(path, "a\nb\r\n");
        var shell = new FakeShell(NativeLineEndingMode.CrLf);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("note.txt") == true);
        Assert.Equal("a\r\nb\r\n", shell.Document!.Text);
        Assert.Equal(ThemePolicies.DarkId, shell.Theme?.Id);

        shell.Edit("a\r\nB\r\n");
        Assert.True(shell.Document!.IsModified);
        shell.RequestUndo();
        Assert.Equal("a\r\nb\r\n", shell.Document!.Text);
        shell.RequestRedo();
        Assert.Equal("a\r\nB\r\n", shell.Document!.Text);
        shell.RequestSave();
        await shell.PumpUntilAsync(() => shell.Document?.IsModified == false &&
            File.ReadAllText(path) == "a\nB\r\n");
        Assert.Empty(shell.Errors);
    }

    /// <summary>Only the corrected document version may populate the analysis view.</summary>
    [Fact]
    public async Task Controller_discards_stale_analysis_after_rapid_edit()
    {
        using var temp = new RepoTemp();
        var path = temp.File("data.json");
        await File.WriteAllTextAsync(path, "{\"x\":");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("data.json") == true);
        shell.Edit("{\"x\":1}");
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("v1") == true &&
            shell.Analysis.DiagnosticsSummary == "No diagnostics.");
        await Task.Delay(150);
        shell.Pump();
        Assert.Equal("No diagnostics.", shell.Analysis!.DiagnosticsSummary);
        Assert.DoesNotContain(shell.Analyses, a => a.Status.Contains("v0") &&
            a.DiagnosticsSummary != "No diagnostics.");
    }

    /// <summary>An edit on the next bounded page changes only its source range.</summary>
    [Fact]
    public async Task Controller_edits_across_crlf_page_boundary_without_corrupting_prefix()
    {
        using var temp = new RepoTemp();
        var path = temp.File("large.txt");
        var prefix = new string('a', NativeEditorController.PageSize - 1) + "\r\n";
        await File.WriteAllTextAsync(path, prefix + "b");
        var shell = new FakeShell(NativeLineEndingMode.CrLf);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == prefix.Length + 1);
        Assert.Equal(prefix, shell.Document!.Text);
        shell.RequestNextPage();
        Assert.Equal(prefix.Length, shell.Document!.PageStart);
        Assert.Equal("b", shell.Document.Text);
        shell.Edit("B");
        shell.RequestSave();
        await shell.PumpUntilAsync(() => shell.Document?.IsModified == false &&
            File.ReadAllText(path) == prefix + "B");
        Assert.Empty(shell.Errors);
    }

    /// <summary>An already queued open cannot dispose the document while Save As is in flight.</summary>
    [Fact]
    public async Task Controller_rejects_inflight_open_completion_during_save()
    {
        using var temp = new RepoTemp();
        var current = temp.File("current.txt");
        var pendingOpen = temp.File("other.txt");
        var saveTarget = temp.File("target.txt");
        await File.WriteAllTextAsync(current, "old");
        await File.WriteAllTextAsync(pendingOpen, "other");
        await File.WriteAllTextAsync(saveTarget, "target before");
        var shell = new FakeShell(NativeLineEndingMode.Preserve)
        {
            OpenPath = pendingOpen,
            SavePath = saveTarget
        };
        using var controller = NewController(shell, temp.Path, current);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("current.txt") == true);
        shell.Edit("local edit");
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("v1") == true);

        shell.RequestOpen();
        await shell.WaitForPostedAsync(); // Open completion is queued but not yet dispatched.
        shell.RequestSaveAs();             // _saving becomes true before the queued open runs.
        shell.PumpOne();
        Assert.Contains(shell.Errors, error => error.Contains("save", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("current.txt", shell.Document!.Title);
        await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("target.txt") == true &&
            shell.Document.IsModified == false);
        Assert.Equal("local edit", await File.ReadAllTextAsync(saveTarget));
        Assert.Equal("old", await File.ReadAllTextAsync(current));
        Assert.Equal("other", await File.ReadAllTextAsync(pendingOpen));
    }

    /// <summary>Save on an untitled buffer can replace an existing picked target only after approval.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Controller_untitled_save_to_existing_target_honors_overwrite_decision(bool approve)
    {
        using var temp = new RepoTemp();
        var target = temp.File("existing.txt");
        await File.WriteAllTextAsync(target, "original");
        var shell = new FakeShell(NativeLineEndingMode.Preserve)
        {
            SavePath = target,
            OverwriteApproved = approve
        };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        shell.Edit("new text");
        shell.RequestSave();
        await shell.PumpUntilAsync(() => shell.OverwritePromptCount == 1);
        if (approve)
        {
            await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("existing.txt") == true &&
                shell.Document.IsModified == false);
            Assert.Equal("new text", await File.ReadAllTextAsync(target));
        }
        else
        {
            Assert.Equal("original", await File.ReadAllTextAsync(target));
            Assert.True(shell.Document!.IsModified);
            Assert.Contains("Untitled", shell.Document.Title);
            // A denied prompt is not a permanent busy state: a later approval can save.
            await Task.Delay(50);
            shell.Pump();
            shell.OverwriteApproved = true;
            shell.RequestSave();
            await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("existing.txt") == true &&
                shell.Document.IsModified == false);
            Assert.Equal("new text", await File.ReadAllTextAsync(target));
        }
    }

    /// <summary>Typing at a full page end must advance the visible caret, not reverse later inserts.</summary>
    [Fact]
    public async Task Controller_hundred_inserts_at_full_page_end_preserve_global_order()
    {
        using var temp = new RepoTemp();
        var path = temp.File("page-end.txt");
        var prefix = new string('a', NativeEditorController.PageSize);
        await File.WriteAllTextAsync(path, prefix);
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == prefix.Length);
        var inserted = new string(Enumerable.Range(0, 100).Select(i => (char)('A' + i % 26)).ToArray());
        var watch = Stopwatch.StartNew();
        foreach (var ch in inserted) shell.Edit(shell.Document!.Text + ch);
        watch.Stop();
        Assert.Equal(prefix.Length + inserted.Length, shell.Document!.TotalLength);
        Assert.EndsWith(inserted, shell.Document.Text, StringComparison.Ordinal);
        shell.RequestSave();
        await shell.PumpUntilAsync(() => shell.Document?.IsModified == false);
        Assert.Equal(prefix + inserted, await File.ReadAllTextAsync(path));
        Assert.True(watch.Elapsed < TimeSpan.FromMinutes(1), "Typing smoke exceeded a minute; no strict CI latency target is asserted.");
    }

    /// <summary>Crossing page slack rebases the viewport without losing a large paste or caret.</summary>
    [Fact]
    public async Task Controller_rebases_after_page_slack_exhaustion_and_keeps_edit_order()
    {
        using var temp = new RepoTemp();
        var path = temp.File("page-rebase.txt");
        var prefix = new string('a', NativeEditorController.PageSize);
        await File.WriteAllTextAsync(path, prefix);
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == prefix.Length);
        var paste = new string('Z', NativeEditorController.PageSlack + 1024);
        shell.Edit(shell.Document!.Text + paste);
        Assert.True(shell.Document!.PageStart > 0, "Exceeding page slack should rebase the viewport.");
        Assert.InRange(shell.Document.FocusDisplayOffset ?? -1, 0, shell.Document.Text.Length);
        var tail = new string(Enumerable.Range(0, 100).Select(i => (char)('A' + i % 26)).ToArray());
        foreach (var ch in tail) shell.Edit(shell.Document!.Text + ch);
        Assert.EndsWith(paste + tail, shell.Document!.Text, StringComparison.Ordinal);
        shell.RequestSave();
        await shell.PumpUntilAsync(() => shell.Document?.IsModified == false);
        Assert.Equal(prefix + paste + tail, await File.ReadAllTextAsync(path));
    }

    /// <summary>Builds a controller with project-local, side-effect-free configuration.</summary>
    private static NativeEditorController NewController(FakeShell shell, string userHome, string? path)
    {
        var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
        {
            UserHomeDirectory = userHome,
            UseEnvironmentOverride = false
        });
        return new NativeEditorController(shell, config, ThemePolicies.Get(config.ThemeId), path);
    }

    /// <summary>Rejects a boundary inside one surrogate pair.</summary>
    private static bool SplitsPair(string text, int index) => index > 0 && index < text.Length &&
        char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index]);

    /// <summary>Rejects a boundary inside one projected CRLF delimiter.</summary>
    private static bool SplitsCrLf(string text, int index) => index > 0 && index < text.Length &&
        text[index - 1] == '\r' && text[index] == '\n';

    /// <summary>Shows control characters in a failing randomized case.</summary>
    private static string Escape(string value) => value.Replace("\r", "\\r").Replace("\n", "\\n");

    /// <summary>A deterministic event queue standing in for the native UI dispatcher.</summary>
    private sealed class FakeShell(NativeLineEndingMode lineEndingMode) : INativeEditorShell
    {
        private readonly ConcurrentQueue<Action> _posted = new();

        /// <inheritdoc />
        public NativeLineEndingMode LineEndingMode { get; } = lineEndingMode;
        /// <inheritdoc />
        public bool PrefersDark => true;
        /// <inheritdoc />
        public event Action<string>? TextChanged;
        /// <inheritdoc />
        public event Action? NewRequested;
        /// <inheritdoc />
        public event Action? OpenRequested;
        /// <inheritdoc />
        public event Action? SaveRequested;
        /// <inheritdoc />
        public event Action? SaveAsRequested;
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
        public event EventHandler<NativeClosingEventArgs>? ClosingRequested;
        /// <inheritdoc />
        public event Action? Shown;

        /// <summary>The current fake text viewport.</summary>
        public NativeDocumentView? Document { get; private set; }
        /// <summary>The current fake semantic presentation.</summary>
        public NativeAnalysisView? Analysis { get; private set; }
        /// <summary>All presentations, for stale-result assertions.</summary>
        public List<NativeAnalysisView> Analyses { get; } = [];
        /// <summary>All recoverable UI errors.</summary>
        public List<string> Errors { get; } = [];
        /// <summary>The applied compile-time theme.</summary>
        public IThemePolicy? Theme { get; private set; }
        /// <summary>Next path returned by the Open dialog.</summary>
        public string? OpenPath { get; set; }
        /// <summary>Next path returned by the Save As dialog.</summary>
        public string? SavePath { get; set; }
        /// <summary>Decision returned by the next existing-target overwrite prompt.</summary>
        public bool OverwriteApproved { get; set; } = true;
        /// <summary>Number of explicit overwrite prompts shown.</summary>
        public int OverwritePromptCount { get; private set; }

        /// <inheritdoc />
        public void Run() => Shown?.Invoke();
        /// <inheritdoc />
        public void SetDocument(NativeDocumentView view) => Document = view;
        /// <inheritdoc />
        public void SetAnalysis(NativeAnalysisView view) { Analysis = view; Analyses.Add(view); }
        /// <inheritdoc />
        public void SetTheme(IThemePolicy theme) => Theme = theme;
        /// <inheritdoc />
        public string? PickOpenFile() => OpenPath;
        /// <inheritdoc />
        public string? PickSaveFile(string? currentPath) => SavePath ?? currentPath;
        /// <inheritdoc />
        public bool ConfirmOverwrite(string path)
        {
            OverwritePromptCount++;
            return OverwriteApproved;
        }
        /// <inheritdoc />
        public bool ConfirmDiscard() => true;
        /// <inheritdoc />
        public void ShowError(string message) => Errors.Add(message);
        /// <inheritdoc />
        public void Post(Action action) => _posted.Enqueue(action);
        /// <inheritdoc />
        public void Close() { }

        /// <summary>Raises one native text-edit notification.</summary>
        public void Edit(string text) => TextChanged?.Invoke(text);
        /// <summary>Raises the native Save command.</summary>
        public void RequestSave() => SaveRequested?.Invoke();
        /// <summary>Raises the native Save As command.</summary>
        public void RequestSaveAs() => SaveAsRequested?.Invoke();
        /// <summary>Raises the native Open command.</summary>
        public void RequestOpen() => OpenRequested?.Invoke();
        /// <summary>Raises the native Undo command.</summary>
        public void RequestUndo() => UndoRequested?.Invoke();
        /// <summary>Raises the native Redo command.</summary>
        public void RequestRedo() => RedoRequested?.Invoke();
        /// <summary>Raises native next-page navigation.</summary>
        public void RequestNextPage() => PageNextRequested?.Invoke();
        /// <summary>Raises the native New command.</summary>
        public void RequestNew() => NewRequested?.Invoke();
        /// <summary>Raises the native Format command.</summary>
        public void RequestFormat() => FormatRequested?.Invoke();
        /// <summary>Raises previous-page navigation.</summary>
        public void RequestPreviousPage() => PagePreviousRequested?.Invoke();
        /// <summary>Asks the controller whether closing is permitted.</summary>
        public bool RequestClose()
        {
            var args = new NativeClosingEventArgs();
            ClosingRequested?.Invoke(this, args);
            return !args.Cancel;
        }

        /// <summary>Executes posted callbacks until the queue is empty.</summary>
        public void Pump()
        {
            while (_posted.TryDequeue(out var action)) action();
        }

        /// <summary>Executes exactly one queued UI callback.</summary>
        public void PumpOne()
        {
            Assert.True(_posted.TryDequeue(out var action), "Expected one queued UI callback.");
            action();
        }

        /// <summary>Waits for a background completion without draining it.</summary>
        public async Task WaitForPostedAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            while (_posted.IsEmpty && stopwatch.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
            Assert.False(_posted.IsEmpty, "Background completion was not posted.");
        }

        /// <summary>Waits for background work by draining only the fake UI queue.</summary>
        public async Task PumpUntilAsync(Func<bool> condition)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
            {
                Pump();
                if (condition()) return;
                await Task.Delay(10);
            }
            Pump();
            Assert.True(condition(), "Timed out waiting for a native-controller state transition.");
        }
    }
}
