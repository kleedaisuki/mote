using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Mote.Configuration;
using Mote.Engine;
using Mote.Native;
using Mote.Telemetry;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Verifies that asynchronous Save completion never unwinds nonfatal failures into the native dispatcher.</summary>
[Collection("Telemetry")]
public sealed class NativeSaveCompletionContainmentTests
{
    /// <summary>Committed bytes and failed disk operations retain their own outcomes even when completion UI throws.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task Throwing_completion_returns_without_success_certificate(bool diskFails, bool tracing)
    {
        using var temp = new RepoTemp();
        var target = temp.File("SECRET-target.txt");
        var shell = new CompletionShell(target) { FailPresentation = !diskFails, FailErrorUi = diskFails };
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = tracing, OutputDirectory = temp.Path });
        try
        {
            var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
            { UserHomeDirectory = temp.Path, UseEnvironmentOverride = false });
            using var controller = new NativeEditorController(shell, config, ThemePolicies.Get(config.ThemeId), null);
            var document = (Document)Field(controller, "_document")!;
            document.Apply(new TextChange(0, 0, "SECRET-saved-source"));
            var version = document.Snapshot.Version;
            if (diskFails) document.SaveOperations = new FailingMove();
            shell.RequestSaveAs();
            Assert.True((bool)Field(controller, "_saving")!);
            await shell.Posted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // This is the actual queued Action, not a reflection call that wraps an escaped exception.
            var completion = shell.TakePosted();
            Assert.Null(Record.Exception(completion));
            Assert.False((bool)Field(controller, "_saving")!);
            Assert.Null(Field(controller, "_activeSaveTrace"));
            Assert.Equal(version, document.Snapshot.Version);
            if (diskFails)
            {
                Assert.False(File.Exists(target));
                Assert.True(document.IsModified);
                Assert.Equal(1, shell.ErrorCalls);
                Assert.Equal(0, shell.PresentationCalls);
            }
            else
            {
                Assert.Equal("SECRET-saved-source", await File.ReadAllTextAsync(target));
                Assert.False(document.IsModified);
                Assert.Equal(1, shell.PresentationCalls);
                Assert.Equal(0, shell.ErrorCalls);
            }
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        if (!tracing) { Assert.Empty(records); return; }
        var terminal = Assert.Single(records, r => Operation(r) == "command.save_as");
        Assert.Equal("failure", terminal.GetProperty("status").GetString());
        Assert.Equal("callback_failed", terminal.GetProperty("attributes").GetProperty("reason").GetString());
        Assert.Equal(1, terminal.GetProperty("attributes").GetProperty("version").GetInt64());
        var receipt = Assert.Single(records, r => Operation(r) == "command.save_as.received");
        Assert.Equal(receipt.GetProperty("span_id").GetString(), terminal.GetProperty("parent_span_id").GetString());
        var engine = Assert.Single(records, r => Operation(r) == "document.save");
        Assert.Equal(diskFails ? "failure" : "success", engine.GetProperty("status").GetString());
        var commit = Assert.Single(records, r => Operation(r) == "save.commit_move");
        Assert.Equal(diskFails ? "failure" : "success", commit.GetProperty("status").GetString());
        Assert.Single(records, r => Operation(r) == "save.ui_started");
        Assert.DoesNotContain(records, r => Operation(r) == "save.completed");
        Assert.DoesNotContain("SECRET", string.Join('\n', records.Select(r => r.GetRawText())));
    }

    /// <summary>Reads orchestration ownership without introducing a production test-only API.</summary>
    private static object? Field(NativeEditorController controller, string name) =>
        typeof(NativeEditorController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller);

    /// <summary>Reads the closed journal only after writer shutdown.</summary>
    private static JsonElement[] Read(string path) => Directory.GetFiles(path, "*.jsonl").SelectMany(File.ReadLines)
        .Select(line => { using var json = JsonDocument.Parse(line); return json.RootElement.Clone(); }).ToArray();

    /// <summary>Returns the fixed schema operation name.</summary>
    private static string Operation(JsonElement record) => record.GetProperty("operation").GetString()!;

    /// <summary>Fails before target creation so the test independently observes a noncommitted disk result.</summary>
    private sealed class FailingMove : DocumentSaveOperations
    {
        /// <inheritdoc />
        internal override void Move(string stage, string target) => throw new IOException("SECRET-commit-failure");
    }

    /// <summary>Queues real controller callbacks and injects failures at native presentation/error boundaries only.</summary>
    private sealed class CompletionShell(string target) : INativeEditorShell
    {
        /// <summary>Retains each callback until the test explicitly dispatches it.</summary>
        private readonly ConcurrentQueue<Action> _posted = new();
        /// <summary>Signals the first worker handoff without timing-dependent sleeps.</summary>
        internal TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Injects a nonfatal view installation failure.</summary>
        internal bool FailPresentation { get; init; }
        /// <summary>Injects a nonfatal failure in the existing Save error-reporting path.</summary>
        internal bool FailErrorUi { get; init; }
        /// <summary>Counts presentation attempts, including failed attempts.</summary>
        internal int PresentationCalls { get; private set; }
        /// <summary>Counts error UI attempts to detect recursive reporting.</summary>
        internal int ErrorCalls { get; private set; }
        /// <summary>Raises the shipped typed command rather than invoking completion privately.</summary>
        internal void RequestSaveAs() => NativeSaveRequest.Receive(NativeSaveKind.SaveAs).Dispatch(SaveRequested);
        /// <summary>Takes the worker's completion Action for direct dispatcher-equivalent execution.</summary>
        internal Action TakePosted() { Assert.True(_posted.TryDequeue(out var action)); return action!; }
        /// <inheritdoc />
        public NativeLineEndingMode LineEndingMode => NativeLineEndingMode.Preserve;
        /// <inheritdoc />
        public bool PrefersDark => true;
        /// <inheritdoc />
        public bool IsTextComposing => false;
        /// <inheritdoc />
        public event Action<NativeSaveRequest>? SaveRequested;
        /// <inheritdoc />
        public event Action? AppearanceChanged { add { } remove { } }
        /// <inheritdoc />
        public event Action? CompositionSettled { add { } remove { } }
        /// <inheritdoc />
        public event Action<string>? TextChanged { add { } remove { } }
        /// <inheritdoc />
        public event Action<int, int>? SelectionChanged { add { } remove { } }
        /// <inheritdoc />
        public event Action<NativePreviewActivation>? PreviewActivated { add { } remove { } }
        /// <inheritdoc />
        public event Action? NewRequested { add { } remove { } }
        /// <inheritdoc />
        public event Action? OpenRequested { add { } remove { } }
        /// <inheritdoc />
        public event Action? UndoRequested { add { } remove { } }
        /// <inheritdoc />
        public event Action? RedoRequested { add { } remove { } }
        /// <inheritdoc />
        public event Action? FormatRequested { add { } remove { } }
        /// <inheritdoc />
        public event Action? PagePreviousRequested { add { } remove { } }
        /// <inheritdoc />
        public event Action? PageNextRequested { add { } remove { } }
        /// <inheritdoc />
        public event Action? FindRequested { add { } remove { } }
        /// <inheritdoc />
        public event Action? FindNextRequested { add { } remove { } }
        /// <inheritdoc />
        public event Action? GoToLineRequested { add { } remove { } }
        /// <inheritdoc />
        public event Action? SelectAllRequested { add { } remove { } }
        /// <inheritdoc />
        public event Action? CopyRequested { add { } remove { } }
        /// <inheritdoc />
        public event Action? CutRequested { add { } remove { } }
        /// <inheritdoc />
        public event EventHandler<NativeClosingEventArgs>? ClosingRequested { add { } remove { } }
        /// <inheritdoc />
        public event Action? Shown { add { } remove { } }
        /// <inheritdoc />
        public void Run() { }
        /// <inheritdoc />
        public void SetDocument(NativeDocumentView view)
        {
            PresentationCalls++;
            if (FailPresentation) throw new InvalidOperationException("SECRET-presentation-failure");
        }
        /// <inheritdoc />
        public void SetAnalysis(NativeAnalysisView view) { }
        /// <inheritdoc />
        public void SetTheme(IThemePolicy theme) { }
        /// <inheritdoc />
        public void SetStatusNotice(string? notice) { }
        /// <inheritdoc />
        public bool CommitPendingText() => true;
        /// <inheritdoc />
        public void SetSelection(int displayAnchor, int displayActive) { }
        /// <inheritdoc />
        public void FocusSource() { }
        /// <inheritdoc />
        public string? PromptFind() => null;
        /// <inheritdoc />
        public int? PromptGoToLine() => null;
        /// <inheritdoc />
        public void SetClipboardText(string text) { }
        /// <inheritdoc />
        public string? PickOpenFile() => null;
        /// <inheritdoc />
        public string? PickSaveFile(string? currentPath) => target;
        /// <inheritdoc />
        public bool ConfirmOverwrite(string path) => true;
        /// <inheritdoc />
        public bool ConfirmDiscard() => true;
        /// <inheritdoc />
        public void ShowError(string message)
        {
            ErrorCalls++;
            if (FailErrorUi) throw new InvalidOperationException("SECRET-error-ui-failure");
        }
        /// <inheritdoc />
        public void Post(Action action) { _posted.Enqueue(action); Posted.TrySetResult(); }
        /// <inheritdoc />
        public void Close() { }
    }
}
