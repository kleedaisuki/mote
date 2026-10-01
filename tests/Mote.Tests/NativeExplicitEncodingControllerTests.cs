using System.Collections.Concurrent;
using System.Diagnostics;
using Mote.Configuration;
using Mote.Engine;
using Mote.Native;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Exercises actual controller encoding admission with portable modal and posted-completion witnesses.</summary>
public sealed class NativeExplicitEncodingControllerTests
{
    /// <summary>A strict default decode failure offers one explicit choice, then saves supported edits exactly.</summary>
    [Fact]
    public async Task Default_failure_offers_once_then_opens_and_saves_selected_codec()
    {
        using var temp = new RepoTemp();
        var path = temp.File("chinese.txt");
        await File.WriteAllBytesAsync(path, Convert.FromHexString("D6D0"));
        var shell = new EncodingShell { OpenPath = path, Choice = DocumentTextEncoding.Gbk };
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.Open();
        await shell.Until(() => shell.View?.Text == "中");
        Assert.Equal(1, shell.ChoiceCalls);
        Assert.Empty(shell.Errors);
        shell.Edit("文");
        shell.Save();
        await shell.Until(() => shell.View?.IsModified == false);
        Assert.Equal(Convert.FromHexString("CEC4"), await File.ReadAllBytesAsync(path));
        Assert.Contains("chinese.txt", shell.View!.Title);
    }

    /// <summary>Cancellation and a failed explicit decode preserve current identity, dirty text and undo history.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_or_invalid_choice_preserves_current_document(bool invalidChoice)
    {
        using var temp = new RepoTemp();
        var current = temp.File("current.txt");
        var target = temp.File("target.txt");
        await File.WriteAllTextAsync(current, "original");
        await File.WriteAllBytesAsync(target, Convert.FromHexString("D6D0"));
        var shell = new EncodingShell { OpenPath = target, Choice = invalidChoice ? DocumentTextEncoding.Utf8 : null };
        using var controller = Create(shell, temp.Path, current);
        controller.Run();
        await shell.Until(() => shell.View?.Text == "original");
        shell.Edit("dirty");
        var before = shell.View!;
        shell.Open();
        await shell.Until(() => invalidChoice ? shell.Errors.Count == 1 : shell.ChoiceCalls == 1);
        Assert.Equal(1, shell.ChoiceCalls);
        Assert.Equal(before.Stamp, shell.View!.Stamp);
        Assert.Equal(before.Title, shell.View.Title);
        Assert.Equal("dirty", shell.View.Text);
        Assert.True(shell.View.IsModified);
        shell.Undo();
        Assert.Equal("original", shell.View!.Text);
        Assert.False(shell.View.IsModified);
        Assert.Equal("original", await File.ReadAllTextAsync(current));
        Assert.Equal(Convert.FromHexString("D6D0"), await File.ReadAllBytesAsync(target));
    }

    /// <summary>BOM-less UTF-16 is explicitly selectable even when its bytes are also syntactically valid UTF-8.</summary>
    [Fact]
    public async Task Explicit_route_opens_valid_utf8_bytes_as_utf16_when_requested()
    {
        using var temp = new RepoTemp();
        var path = temp.File("utf16.txt");
        await File.WriteAllBytesAsync(path, Convert.FromHexString("41004200"));
        var shell = new EncodingShell { OpenPath = path, Choice = DocumentTextEncoding.Utf16LittleEndian };
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.OpenExplicit();
        await shell.Until(() => shell.View?.Text == "AB");
        Assert.Equal(1, shell.ChoiceCalls);
        Assert.Empty(shell.Errors);
    }

    /// <summary>A recoverable chooser exception is contained without editing or reopening the current document.</summary>
    [Fact]
    public void Chooser_exception_is_contained()
    {
        using var temp = new RepoTemp();
        var shell = new EncodingShell { OpenPath = temp.File("unused.txt"), DuringChoice = () => throw new InvalidOperationException("chooser unavailable") };
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.Edit("dirty");
        var before = shell.View;
        shell.OpenExplicit();
        Assert.Equal(before, shell.View);
        Assert.Single(shell.Errors);
        Assert.Contains("Cannot choose", shell.Errors[0]);
        Assert.Equal(1, shell.ChoiceCalls);
    }

    /// <summary>Modal reentrancy cannot publish a stale selection after New, a later Open, or disposal.</summary>
    [Theory]
    [InlineData("new")]
    [InlineData("open")]
    [InlineData("dispose")]
    public async Task Modal_lifetime_change_invalidates_old_open(string action)
    {
        using var temp = new RepoTemp();
        var path = temp.File("old-choice.txt");
        var later = temp.File("later.txt");
        await File.WriteAllBytesAsync(path, Convert.FromHexString("D6D0"));
        await File.WriteAllTextAsync(later, "later");
        var shell = new EncodingShell { OpenPath = path, Choice = DocumentTextEncoding.Gbk };
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.Edit("prior");
        shell.DuringChoice = () =>
        {
            if (action == "new") shell.New();
            else if (action == "dispose") controller.Dispose();
            else { shell.OpenPath = later; shell.Open(); }
        };
        shell.OpenExplicit();
        if (action == "open") await shell.Until(() => shell.View?.Text == "later");
        else Assert.Equal(action == "new" ? "" : "prior", shell.View!.Text);
        Assert.DoesNotContain("old-choice.txt", shell.View!.Title);
        Assert.Equal(1, shell.ChoiceCalls);
        if (action == "dispose")
        {
            Assert.Equal(0, shell.EncodingSubscribers);
            shell.OpenExplicit();
            Assert.Equal(1, shell.ChoiceCalls);
        }
    }

    /// <summary>Discard approval before a modal choice does not authorize edits occurring within that modal.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Edits_before_or_during_choice_require_fresh_discard_approval(bool duringChoice)
    {
        using var temp = new RepoTemp();
        var target = temp.File("target.txt");
        await File.WriteAllBytesAsync(target, Convert.FromHexString("D6D0"));
        var shell = new EncodingShell { OpenPath = target, Choice = DocumentTextEncoding.Gbk };
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.Edit("first");
        shell.Discard = () => shell.DiscardCalls == 1;
        if (duringChoice) shell.DuringChoice = () => shell.Edit("later edit");
        shell.Open();
        if (!duringChoice) shell.Edit("later edit");
        await shell.Until(() => shell.DiscardCalls == 2);
        Assert.Equal("later edit", shell.View!.Text);
        Assert.True(shell.View.IsModified);
        Assert.Equal(1, shell.ChoiceCalls);
        shell.Undo();
        Assert.Equal("first", shell.View!.Text);
    }

    /// <summary>In-flight Save and uncommittable IME input veto the chooser before picking or replacing a file.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_or_pending_input_vetoes_explicit_open(bool saving)
    {
        using var temp = new RepoTemp();
        var shell = new EncodingShell { OpenPath = temp.File("unused.txt"), SavePath = temp.File("saved.txt"), Choice = DocumentTextEncoding.Gbk };
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.Edit("keep");
        if (saving) shell.Save();
        else shell.CanCommit = false;
        shell.OpenExplicit();
        Assert.Equal(0, shell.ChoiceCalls);
        Assert.Equal(0, shell.PickerCalls);
        Assert.Equal("keep", shell.View!.Text);
        if (saving) await shell.Until(() => shell.View?.IsModified == false);
    }

    /// <summary>Legacy shell implementations retain the previous generic error path without the optional capability.</summary>
    [Fact]
    public async Task Shell_without_capability_keeps_generic_decode_error()
    {
        using var temp = new RepoTemp();
        var path = temp.File("legacy.txt");
        await File.WriteAllBytesAsync(path, Convert.FromHexString("D6D0"));
        var shell = new PlainShell { OpenPath = path };
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.Edit("keep");
        shell.Open();
        await shell.Until(() => shell.Errors.Count == 1);
        Assert.Equal("keep", shell.View!.Text);
        Assert.True(shell.View.IsModified);
    }

    /// <summary>Creates actual orchestration with project-local isolated configuration and no native controls.</summary>
    private static NativeEditorController Create(PlainShell shell, string home, string? startup = null)
    {
        var configuration = MoteConfigLoader.Load(new MoteConfigLoadOptions { UserHomeDirectory = home, UseEnvironmentOverride = false });
        return new NativeEditorController(shell, configuration, ThemePolicies.Get(configuration.ThemeId), startup);
    }

    /// <summary>Optional chooser fake models modal reentrancy and explicit event subscription lifetime.</summary>
    private sealed class EncodingShell : PlainShell, INativeOpenEncodingShell
    {
        /// <summary>Optional modal action executes before returning the deliberate choice.</summary>
        public Action? DuringChoice { get; set; }
        /// <summary>Null models cancellation, not a UTF-8 default.</summary>
        public DocumentTextEncoding? Choice { get; set; }
        /// <summary>Counts actual calls to the capability.</summary>
        public int ChoiceCalls { get; private set; }
        /// <summary>Counts live optional command subscribers.</summary>
        public int EncodingSubscribers => OpenWithEncodingRequested?.GetInvocationList().Length ?? 0;
        /// <inheritdoc />
        public event Action? OpenWithEncodingRequested;
        /// <inheritdoc />
        public DocumentTextEncoding? ChooseOpenEncoding() { ChoiceCalls++; DuringChoice?.Invoke(); return Choice; }
        /// <summary>Dispatches the explicit native command.</summary>
        public void OpenExplicit() => OpenWithEncodingRequested?.Invoke();
    }

    /// <summary>Minimal non-canvas shell confines background delivery to an explicit portable UI queue.</summary>
    private class PlainShell : INativeEditorShell
    {
        /// <summary>Only controller-posted actions enter this queue.</summary>
        private readonly ConcurrentQueue<Action> _posted = new();
        /// <summary>Last actual document projection installed by the controller.</summary>
        public NativeDocumentView? View { get; private set; }
        /// <summary>Collected recoverable shell errors.</summary>
        public List<string> Errors { get; } = [];
        /// <summary>Fixed picker results and modal controls.</summary>
        public string? OpenPath { get; set; }
        /// <summary>Fixed save destination.</summary>
        public string? SavePath { get; set; }
        /// <summary>Native IME commit admission witness.</summary>
        public bool CanCommit { get; set; } = true;
        /// <summary>Discard callback observes the incremented call count.</summary>
        public Func<bool>? Discard { get; set; }
        /// <summary>Discard prompt invocation count.</summary>
        public int DiscardCalls { get; private set; }
        /// <summary>File picker invocation count.</summary>
        public int PickerCalls { get; private set; }
        /// <inheritdoc />
        public NativeLineEndingMode LineEndingMode => NativeLineEndingMode.Preserve;
        /// <inheritdoc />
        public bool PrefersDark => true;
        /// <inheritdoc />
        public bool IsTextComposing => !CanCommit;
        /// <inheritdoc />
        public event Action<string>? TextChanged;
        /// <inheritdoc />
        public event Action? NewRequested;
        /// <inheritdoc />
        public event Action? OpenRequested;
        /// <inheritdoc />
        public event Action<NativeSaveRequest>? SaveRequested;
        /// <inheritdoc />
        public event Action? UndoRequested;
        /// <inheritdoc />
        public event Action? Shown;
        // Inert events do not pretend to deliver native UI activity.
        /// <inheritdoc />
        public event Action? AppearanceChanged { add { } remove { } }
        /// <inheritdoc />
        public event Action? CompositionSettled { add { } remove { } }
        /// <inheritdoc />
        public event Action<int, int>? SelectionChanged { add { } remove { } }
        /// <inheritdoc />
        public event Action<NativePreviewActivation>? PreviewActivated { add { } remove { } }
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
        public void Run() => Shown?.Invoke();
        /// <inheritdoc />
        public void SetDocument(NativeDocumentView view) => View = view;
        /// <inheritdoc />
        public void SetAnalysis(NativeAnalysisView view) { }
        /// <inheritdoc />
        public void SetTheme(IThemePolicy theme) { }
        /// <inheritdoc />
        public void SetStatusNotice(string? notice) { }
        /// <inheritdoc />
        public bool CommitPendingText() => CanCommit;
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
        public string? PickOpenFile() { PickerCalls++; return OpenPath; }
        /// <inheritdoc />
        public string? PickSaveFile(string? currentPath) => SavePath ?? currentPath;
        /// <inheritdoc />
        public bool ConfirmOverwrite(string path) => true;
        /// <inheritdoc />
        public bool ConfirmDiscard() { DiscardCalls++; return Discard?.Invoke() ?? true; }
        /// <inheritdoc />
        public void ShowError(string message) => Errors.Add(message);
        /// <inheritdoc />
        public void Post(Action action) => _posted.Enqueue(action);
        /// <inheritdoc />
        public void Close() { }
        /// <summary>Dispatches ordinary Open.</summary>
        public void Open() => OpenRequested?.Invoke();
        /// <summary>Dispatches New.</summary>
        public void New() => NewRequested?.Invoke();
        /// <summary>Dispatches one native edit.</summary>
        public void Edit(string text) => TextChanged?.Invoke(text);
        /// <summary>Dispatches Undo.</summary>
        public void Undo() => UndoRequested?.Invoke();
        /// <summary>Dispatches the actual Save receipt route.</summary>
        public void Save() => NativeSaveRequest.Receive(NativeSaveKind.Save).Dispatch(SaveRequested);
        /// <summary>Drains only fake queued completions until the independently defined postcondition holds.</summary>
        public async Task Until(Func<bool> condition)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                while (_posted.TryDequeue(out var callback)) callback();
                if (condition()) return;
                await Task.Delay(10);
            }
            Assert.True(condition(), $"Controller transition timed out; errors={Errors.Count}, text={View?.Text}.");
        }
    }
}
