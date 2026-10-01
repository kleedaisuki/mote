using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Mote.Configuration;
using Mote.Engine;
using Mote.Formats;
using Mote.Native;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Portable whole-source controller workflows against an independently maintained native replica.</summary>
public sealed class NativeSourceControllerTests
{
    /// <summary>A queued presentation failure keeps its attempted version after a newer canonical edit.</summary>
    [Fact]
    public async Task Presentation_failure_does_not_relabel_stale_attempt_as_current_document()
    {
        using var temp = new RepoTemp();
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.EditSource("one");
        shell.EditSource("two");
        shell.EditSource("three");
        var stamp = shell.Installation!.Stamp;
        var serial = controller.CurrentAnalysisSerial;
        typeof(NativeEditorController).GetMethod("PostAnalysis", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(controller, [stamp, serial, (Action)(() => throw new InvalidOperationException("private detail"))]);
        shell.EditSource("four");
        await Assert.ThrowsAsync<InvalidOperationException>(() => shell.Until(() => true));
        var failure = Assert.IsType<NativeAnalysisFailure>(controller.LastAnalysisFailure);
        Assert.Equal(stamp, failure.Stamp);
        Assert.Equal(3, failure.Stamp.Version);
        Assert.Equal(4, Canonical(controller).Snapshot.Version);
        Assert.False(failure.Matches(shell.Installation!.Stamp, controller.CurrentAnalysisSerial));
        Assert.Equal("four", Canonical(controller).Snapshot.GetText());
    }

    /// <summary>OS-delivered paths use ordinary dirty/marked-input admission and never a file picker.</summary>
    [Fact]
    public async Task External_open_preserves_rejected_input_and_uses_canonical_open()
    {
        using var temp = new RepoTemp();
        var path = temp.File("external.txt");
        await File.WriteAllTextAsync(path, "opened");
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.EditSource("unsaved");
        shell.CanCommit = false;
        Assert.False(shell.ExternalOpen(path));
        Assert.Equal(0, shell.DiscardCalls);
        shell.CanCommit = true;
        shell.Discard = () => false;
        Assert.False(shell.ExternalOpen(path));
        Assert.Equal("unsaved", Canonical(controller).Snapshot.GetText());
        shell.Discard = () => true;
        Assert.True(shell.ExternalOpen(path));
        await shell.Until(() => shell.Buffer == "opened");
        Assert.Equal(0, shell.PickerCalls);
        Assert.False(Canonical(controller).IsModified);
    }

    /// <summary>A nested edit during discard confirmation cannot authorize losing the newer text.</summary>
    [Fact]
    public void External_open_rejects_stale_discard_consent()
    {
        using var temp = new RepoTemp();
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.EditSource("first");
        shell.Discard = () => { shell.EditSource("newer"); return true; };
        Assert.False(shell.ExternalOpen(temp.File("unused.txt")));
        Assert.Equal("newer", Canonical(controller).Snapshot.GetText());
    }

    /// <summary>Native source respects the policy layout convention before explicit configuration.</summary>
    [Theory]
    [InlineData("txt", false)]
    [InlineData("md", true)]
    [InlineData("toml", true)]
    [InlineData("json", true)]
    [InlineData("yaml", true)]
    [InlineData("csv", true)]
    public async Task Native_source_auto_preview_uses_format_convention(string extension, bool preview)
    {
        using var temp = new RepoTemp();
        var path = temp.File("source." + extension);
        await File.WriteAllTextAsync(path, "source");
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path, path);
        controller.Run();
        await shell.Until(() => shell.Buffer == "source");
        Assert.Equal(preview, (bool)typeof(NativeEditorController).GetMethod("PreviewVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, null)!);
    }

    /// <summary>A queued Find cannot replace a newer native selection; viewport-only updates retain it.</summary>
    [Fact]
    public async Task Native_selection_cancels_queued_find_but_viewport_does_not()
    {
        using var temp = new RepoTemp();
        var path = temp.File("find.txt");
        await File.WriteAllTextAsync(path, "start\nTARGET\nend");
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path, path);
        controller.Run();
        await shell.Until(() => shell.Buffer == "start\nTARGET\nend");
        shell.Find("TARGET");
        await shell.WaitForPosted();
        var serial = FindSerial(controller);
        shell.Observe(new(0, 0, 0), new(0, 2), 1);
        Assert.Equal(serial, FindSerial(controller));
        shell.Observe(new(2, 2, 2), new(0, 2), 2);
        Assert.True(FindSerial(controller) > serial);
        await shell.Until(() => true);
        Assert.DoesNotContain(shell.Selections, x => x.Anchor == 6 && x.Active == 12);
        var binding = (NativeSourceBinding)typeof(NativeEditorController).GetField("_sourceBinding", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        Assert.Equal(2, binding.Installation.Active);
        shell.Find("TARGET");
        await shell.Until(() => shell.Selections.Any(x => x.Anchor == 6 && x.Active == 12));
    }

    /// <summary>Reads the cancellation generation without exposing a production-only test hook.</summary>
    private static long FindSerial(NativeEditorController controller) =>
        (long)typeof(NativeEditorController).GetField("_findSerial", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;

    /// <summary>Whole-source import and offscreen emoji editing create exactly one canonical history entry.</summary>
    [Fact]
    public async Task Offscreen_edit_is_one_commit_and_history_uses_ranges_not_imports()
    {
        using var temp = new RepoTemp();
        var original = new string('a', 90_000) + "\r\n😀tail";
        var path = temp.File("long.txt");
        await File.WriteAllTextAsync(path, original);
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path, path);
        controller.Run();
        await shell.Until(() => shell.Buffer == original);
        var imports = shell.Imports;
        var version = Canonical(controller).Snapshot.Version;
        var edited = original.Replace("😀", "😁", StringComparison.Ordinal);
        var old = shell.EditSource(edited);
        Assert.Equal(edited, Canonical(controller).Snapshot.GetText());
        Assert.Equal(version + 1, Canonical(controller).Snapshot.Version);
        Assert.Equal(imports, shell.Imports);
        shell.Echo(old);
        Assert.Equal(version + 1, Canonical(controller).Snapshot.Version);
        shell.Undo();
        Assert.Equal(original, shell.Buffer);
        Assert.Equal(original, Canonical(controller).Snapshot.GetText());
        Assert.Single(shell.Replacements);
        shell.Undo();
        Assert.Single(shell.Replacements);
        shell.Redo();
        Assert.Equal(edited, shell.Buffer);
        Assert.Equal(2, shell.Replacements.Count);
        Assert.Equal(imports, shell.Imports);
    }

    /// <summary>No-change callbacks carry selection but create no text version or undo entry.</summary>
    [Fact]
    public void Nochange_candidate_does_not_add_history()
    {
        using var temp = new RepoTemp();
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.EditSource("text");
        var version = Canonical(controller).Snapshot.Version;
        shell.EditSource("text", new(1, 3, 1));
        Assert.Equal(version, Canonical(controller).Snapshot.Version);
        Assert.Equal(3, shell.Installation!.Anchor);
        Assert.Equal(1, shell.Installation.Active);
        shell.Undo();
        Assert.Equal("", Canonical(controller).Snapshot.GetText());
        Assert.Equal("", shell.Buffer);
    }

    /// <summary>Ordered selection without an endpoint witness must remain explicitly direction-unknown.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Selection_direction_requires_actual_endpoint(bool known)
    {
        using var temp = new RepoTemp();
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.EditSource("abcdef", new(1, 4, known ? 1 : null));
        var binding = (NativeSourceBinding)typeof(NativeEditorController).GetField("_sourceBinding", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        Assert.Equal(known, binding.DirectionKnown);
        Assert.Equal(known ? 4 : 1, binding.Installation.Anchor);
        Assert.Equal(known ? 1 : 4, binding.Installation.Active);
    }

    /// <summary>Full-source viewport and copy never interpret offscreen positions as page-local offsets.</summary>
    [Fact]
    public async Task Offscreen_selection_copy_selectall_and_navigation_are_global()
    {
        using var temp = new RepoTemp();
        var original = new string('a', 90_000) + "\nTARGET\nend";
        var path = temp.File("long.txt");
        await File.WriteAllTextAsync(path, original);
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path, path);
        controller.Run();
        await shell.Until(() => shell.Buffer == original);
        shell.Observe(new(90_001, 90_007, 90_001), new(90_001, 10), 2);
        Assert.Equal(90_001, (int)typeof(NativeEditorController).GetField("_pageStart", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!);
        shell.Observe(new(0, 1, 1), new(0, 10), 1); // Older viewport sequence cannot move global interest.
        Assert.Equal(90_001, (int)typeof(NativeEditorController).GetField("_pageStart", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!);
        shell.Copy();
        await shell.Until(() => shell.Clipboard == "TARGET");
        shell.SelectAll();
        Assert.Equal((0, original.Length, false), shell.Selections.Last());
        shell.Copy();
        await shell.Until(() => shell.Clipboard == original);
        shell.GoToLine = 3;
        shell.Navigate();
        Assert.Contains(shell.Selections, x => x == (90_008, 90_008, true));
        Assert.Equal(original, shell.Buffer);
        Assert.Empty(shell.Replacements);
    }

    /// <summary>A retired document's callback cannot mutate a newly installed empty document.</summary>
    [Fact]
    public void New_retires_nonce_even_when_text_versions_repeat()
    {
        using var temp = new RepoTemp();
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path);
        controller.Run();
        var old = shell.EditSource("old");
        var oldNonce = shell.Installation!.Nonce;
        shell.New();
        Assert.NotEqual(oldNonce, shell.Installation!.Nonce);
        shell.Echo(old);
        Assert.Equal("", Canonical(controller).Snapshot.GetText());
        Assert.Equal("", shell.Buffer);
    }

    /// <summary>Embedded NUL import is disabled while exact canonical bytes remain saveable.</summary>
    [Fact]
    public async Task Nul_import_keeps_canonical_document_without_editable_replica()
    {
        using var temp = new RepoTemp();
        var path = temp.File("nul.txt");
        await File.WriteAllTextAsync(path, "a\0b");
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path, path);
        controller.Run();
        await shell.Until(() => shell.Unavailable);
        Assert.Equal("a\0b", Canonical(controller).Snapshot.GetText());
        Assert.Equal(1, shell.Imports); // Only the initial empty document was imported.
        Assert.Equal("a\0b", await File.ReadAllTextAsync(path));
    }

    /// <summary>Failed native acknowledgement never rolls back an already committed canonical edit.</summary>
    [Fact]
    public void Failed_ack_retains_commit_and_disables_native_replica()
    {
        using var temp = new RepoTemp();
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.AckSucceeds = false;
        shell.EditSource("committed");
        Assert.True(shell.Unavailable);
        Assert.Equal(NativeSourceFailure.CanonicalRetained, shell.Failure);
        Assert.Equal("committed", Canonical(controller).Snapshot.GetText());
        Assert.True(Canonical(controller).IsModified);
        shell.Undo();
        Assert.Equal("", Canonical(controller).Snapshot.GetText());
        Assert.Equal(1, shell.Imports);
    }

    /// <summary>Rejected range publication keeps the engine undo result, not a misleading restored replica.</summary>
    [Fact]
    public void Failed_engine_range_keeps_canonical_undo_result()
    {
        using var temp = new RepoTemp();
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.EditSource("native");
        shell.RangeSucceeds = false;
        shell.Undo();
        Assert.True(shell.Unavailable);
        Assert.Equal("", Canonical(controller).Snapshot.GetText());
        Assert.Equal("native", shell.Buffer);
        Assert.Single(shell.Replacements);
        Assert.Equal(1, shell.Imports);
    }

    /// <summary>Save settles pending native text once and a fresh document decodes exact resulting bytes.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Save_settles_or_vetoes_without_losing_canonical_text(bool allow)
    {
        using var temp = new RepoTemp();
        var path = temp.File("saved.txt");
        var shell = new SourceShell { SavePath = path };
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.EditSource("before");
        shell.DuringCommit = () => { if (allow) shell.EditSource("中\r\n😁"); };
        shell.CanCommit = allow;
        shell.Save();
        if (!allow)
        {
            Assert.False(File.Exists(path));
            Assert.Equal("before", Canonical(controller).Snapshot.GetText());
            return;
        }
        await shell.Until(() => File.Exists(path) && !Canonical(controller).IsModified);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("中\r\n😁"), await File.ReadAllBytesAsync(path));
        using var reopened = await Document.OpenAsync(path);
        Assert.Equal("中\r\n😁", reopened.Snapshot.GetText());
        Assert.False(reopened.IsModified);
    }

    /// <summary>Dirty Open cancellation cannot retire the source nonce or history.</summary>
    [Fact]
    public void Dirty_open_cancel_preserves_native_identity_and_undo()
    {
        using var temp = new RepoTemp();
        var shell = new SourceShell { OpenPath = temp.File("other.txt"), Discard = () => false };
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.EditSource("dirty");
        var nonce = shell.Installation!.Nonce;
        shell.Open();
        Assert.Equal(nonce, shell.Installation.Nonce);
        Assert.Equal("dirty", shell.Buffer);
        Assert.Equal(0, shell.PickerCalls);
        shell.Undo();
        Assert.Equal("", shell.Buffer);
    }

    /// <summary>Invalid scalar readback is not an engine edit and disables the uncertified replica.</summary>
    [Fact]
    public void Malformed_native_candidate_never_commits()
    {
        using var temp = new RepoTemp();
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.EditSource("valid");
        var version = Canonical(controller).Snapshot.Version;
        shell.EditSource(new string((char)0xD800, 1));
        Assert.True(shell.Unavailable);
        Assert.Equal(NativeSourceFailure.UnadmittedNativeText, shell.Failure);
        Assert.Equal("valid", Canonical(controller).Snapshot.GetText());
        Assert.Equal(version, Canonical(controller).Snapshot.Version);
    }

    /// <summary>Composition observations are provisional and cannot mutate canonical text until settled.</summary>
    [Fact]
    public void Composing_candidate_is_not_committed_until_settlement()
    {
        using var temp = new RepoTemp();
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.CanCommit = false;
        var pending = shell.EditSource("候補");
        Assert.Equal("", Canonical(controller).Snapshot.GetText());
        Assert.False(shell.Unavailable);
        shell.CanCommit = true;
        shell.Echo(pending);
        Assert.Equal("候補", Canonical(controller).Snapshot.GetText());
        Assert.Equal(1, shell.Imports);
    }

    /// <summary>Final readback inside a command settlement barrier is admitted despite generic composing state.</summary>
    [Fact]
    public void Settled_candidate_inside_commit_barrier_is_one_commit()
    {
        using var temp = new RepoTemp();
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.Settling = true;
        Assert.True(shell.IsTextComposing);
        Assert.False(shell.HasSourceMarkedText);
        var candidate = shell.EditSource("settled");
        Assert.Equal("settled", Canonical(controller).Snapshot.GetText());
        Assert.Equal(1, Canonical(controller).Snapshot.Version);
        shell.Echo(candidate);
        Assert.Equal(1, Canonical(controller).Snapshot.Version);
        Assert.Equal(1, shell.Imports);
    }

    /// <summary>Committed adapter failure leaves canonical Save available; unadmitted native text vetoes Save.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failure_recovery_save_distinguishes_committed_and_unadmitted_text(bool committed)
    {
        using var temp = new RepoTemp();
        var path = temp.File("recovery.txt");
        var shell = new SourceShell { SavePath = path };
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.EditSource("canonical");
        if (committed)
        {
            shell.AckSucceeds = false;
            shell.EditSource("committed");
        }
        else shell.EditSource(new string((char)0xD800, 1));
        shell.Save();
        if (!committed)
        {
            Assert.False(File.Exists(path));
            Assert.Equal("canonical", Canonical(controller).Snapshot.GetText());
            return;
        }
        await shell.Until(() => File.Exists(path) && !Canonical(controller).IsModified);
        using var reopened = await Document.OpenAsync(path);
        Assert.Equal("committed", reopened.Snapshot.GetText());
    }

    /// <summary>Cancelled native-only recovery retains both the visible unadmitted candidate and canonical history.</summary>
    [Fact]
    public void Recovery_cancel_does_not_discard_pending_native_text()
    {
        using var temp = new RepoTemp();
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.EditSource("canonical");
        var pending = new string((char)0xD800, 1);
        shell.EditSource(pending);
        var imports = shell.Imports;
        shell.SelectAll();
        Assert.Equal(imports, shell.Imports);
        Assert.Equal(pending, shell.Buffer);
        Assert.Equal("canonical", Canonical(controller).Snapshot.GetText());
        Assert.True(Canonical(controller).CanUndo);
        Assert.True(shell.Unavailable);
    }

    /// <summary>Confirmed recovery reinstalls exact canonical source under a fresh nonce without changing dirty history.</summary>
    [Fact]
    public void Recovery_confirmation_reinstalls_canonical_without_history_loss()
    {
        using var temp = new RepoTemp();
        var shell = new SourceShell();
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.EditSource("canonical");
        var nonce = shell.Installation!.Nonce;
        var version = Canonical(controller).Snapshot.Version;
        shell.EditSource(new string((char)0xD800, 1));
        shell.RecoverConsent = true;
        shell.SelectAll();
        Assert.False(shell.Unavailable);
        Assert.Null(shell.Failure);
        Assert.Equal("canonical", shell.Buffer);
        Assert.NotEqual(nonce, shell.Installation!.Nonce);
        Assert.Equal(version, Canonical(controller).Snapshot.Version);
        Assert.True(Canonical(controller).IsModified);
        Assert.Equal((0, 9, false), shell.Selections.Last());
        shell.Undo();
        Assert.Equal("", shell.Buffer);
        Assert.False(Canonical(controller).IsModified);
    }

    /// <summary>Consent alone cannot authorize commands when recovery import remains uncertified.</summary>
    [Fact]
    public void Recovery_failed_import_vetoes_command_and_retains_canonical()
    {
        using var temp = new RepoTemp();
        var path = temp.File("must-not-save.txt");
        var shell = new SourceShell { SavePath = path };
        using var controller = Create(shell, temp.Path);
        controller.Run();
        shell.EditSource("canonical");
        var pending = new string((char)0xD800, 1);
        shell.EditSource(pending);
        shell.ImportSucceeds = false;
        shell.RecoverConsent = true;
        shell.Save();
        Assert.False(File.Exists(path));
        Assert.True(shell.Unavailable);
        Assert.Equal(pending, shell.Buffer);
        Assert.Equal("canonical", Canonical(controller).Snapshot.GetText());
        Assert.True(Canonical(controller).CanUndo);
        Assert.Equal(NativeSourceFailure.UnadmittedNativeText, shell.Failure);
        shell.RecoverConsent = false;
        shell.Save();
        Assert.False(File.Exists(path)); // A failed recovery cannot unlock a later silent discard.
    }

    /// <summary>Reads actual canonical state solely to check mutation and failure retention contracts.</summary>
    private static Document Canonical(NativeEditorController controller) =>
        (Document)typeof(NativeEditorController).GetField("_document", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;

    /// <summary>Creates actual orchestration with isolated configuration and an explicit source profile.</summary>
    private static NativeEditorController Create(SourceShell shell, string home, string? startup = null)
    {
        var configuration = MoteConfigLoader.Load(new MoteConfigLoadOptions { UserHomeDirectory = home, UseEnvironmentOverride = false });
        return new NativeEditorController(shell, configuration, ThemePolicies.Get(configuration.ThemeId), startup, EditorPresentationProfile.NativeSource);
    }
    private sealed class SourceShell : INativeEditorShell, INativeSourceShell, INativeExternalOpenShell
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
        public bool IsTextComposing => !CanCommit || Settling;
        /// <inheritdoc />
        public event Action<string>? TextChanged;
        /// <inheritdoc />
        public event Action? NewRequested;
        /// <inheritdoc />
        public event Action? OpenRequested;
        /// <inheritdoc />
        public event Func<string, bool>? ExternalOpenRequested;
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
        public event Action? RedoRequested;
        /// <inheritdoc />
        public event Action? FormatRequested { add { } remove { } }
        /// <inheritdoc />
        public event Action? PagePreviousRequested { add { } remove { } }
        /// <inheritdoc />
        public event Action? PageNextRequested { add { } remove { } }
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
        public bool CommitPendingText()
        {
            DuringCommit?.Invoke(); DuringCommit = null;
            if (!CanCommit) return false;
            if (Failure != NativeSourceFailure.UnadmittedNativeText) return true;
            if (!RecoverConsent) return false;
            return SourceRecoveryRequested?.Invoke() == true && !Unavailable;
        }
        /// <inheritdoc />
        public void SetSelection(int displayAnchor, int displayActive) { }
        /// <inheritdoc />
        public void FocusSource() { }
        /// <inheritdoc />
        public string? PromptFind() => FindQuery;
        /// <inheritdoc />
        public int? PromptGoToLine() => GoToLine;
        /// <inheritdoc />
        public void SetClipboardText(string text) => Clipboard = text;
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
        /// <summary>Delivers an OS open request with synchronous admission acknowledgement.</summary>
        public bool ExternalOpen(string path) => ExternalOpenRequested?.Invoke(path) == true;
        /// <summary>Dispatches New.</summary>
        public void New() => NewRequested?.Invoke();
        /// <summary>Dispatches one native edit.</summary>
        public void Edit(string text) => TextChanged?.Invoke(text);
        /// <summary>Dispatches Undo.</summary>
        public void Undo() => UndoRequested?.Invoke();
        /// <summary>Dispatches the actual Save receipt route.</summary>
        public void Save() => NativeSaveRequest.Receive(NativeSaveKind.Save).Dispatch(SaveRequested);
        /// <summary>Dispatches an actual Find prompt result through the subscribed controller event.</summary>
        public void Find(string query) { FindQuery = query; FindRequested?.Invoke(); }
        /// <summary>Dispatches the retained-query Find Next command.</summary>
        public void FindNext() => FindNextRequested?.Invoke();
        /// <summary>Prompt response is explicit rather than an inert event stub.</summary>
        public string? FindQuery { get; private set; }
        /// <summary>Queued callbacks permit deterministic withholding of asynchronous completions.</summary>
        public int PendingCallbacks => _posted.Count;
        /// <summary>Waits for completion delivery without executing the queued UI callback.</summary>
        public async Task WaitForPosted()
        {
            var clock = Stopwatch.StartNew();
            while (PendingCallbacks == 0 && clock.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(10);
            Assert.True(PendingCallbacks > 0, "No asynchronous callback was posted.");
        }
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
        /// <summary>Complete native replica retained independently from controller chrome.</summary>
        public NativeSourceInstallation? Installation { get; private set; }
        /// <summary>Actual fake native buffer, not the engine snapshot.</summary>
        public string Buffer { get; private set; } = "";
        /// <summary>Import count excludes range mutations and acknowledgements.</summary>
        public int Imports { get; private set; }
        /// <summary>Engine-originated guarded replacements.</summary>
        public List<NativeSourceReplacement> Replacements { get; } = [];
        /// <summary>Failure injection after an actual engine commit.</summary>
        public bool AckSucceeds { get; set; } = true;
        /// <summary>Failure injection before native range mutation.</summary>
        public bool RangeSucceeds { get; set; } = true;
        /// <summary>Replica disabled state.</summary>
        public bool Unavailable { get; private set; }
        /// <summary>Canonical modified chrome witness.</summary>
        public bool Modified { get; private set; }
        /// <summary>Explicit settlement action executes once at command entry.</summary>
        public Action? DuringCommit { get; set; }
        /// <summary>Captured fake clipboard, never the operating system clipboard.</summary>
        public string? Clipboard { get; private set; }
        /// <summary>Fixed navigation prompt.</summary>
        public int? GoToLine { get; set; }
        /// <summary>Selection publications and reveal witnesses.</summary>
        public List<(int Anchor, int Active, bool Reveal)> Selections { get; } = [];
        /// <summary>Whole-source capability is explicit and never size-dependent.</summary>
        public bool NativeSourceEnabled => true;
        /// <summary>Provisional marked text excludes the synchronous settlement barrier.</summary>
        public bool HasSourceMarkedText => !CanCommit;
        /// <summary>Native command barrier can be active while final readback is settled.</summary>
        public bool Settling { get; set; }
        /// <inheritdoc />
        public event Action<NativeSourceCandidate>? SourceCandidate;
        /// <inheritdoc />
        public event Func<bool>? SourceRecoveryRequested;
        /// <summary>Explicit consent is false unless a test deliberately authorizes discarding native-only text.</summary>
        public bool RecoverConsent { get; set; }
        /// <summary>Failure injection returns no complete import certification.</summary>
        public bool ImportSucceeds { get; set; } = true;
        /// <inheritdoc />
        public event Action<NativeSourceViewObservation>? SourceViewChanged;
        /// <inheritdoc />
        public NativeSourceObservation? InstallSource(NativeSourceInstallation installation)
        {
            Imports++;
            if (!ImportSucceeds) return null;
            Failure = null;
            Installation = installation;
            Buffer = installation.Projection.Display;
            Unavailable = false;
            return Observation();
        }
        /// <inheritdoc />
        public bool AcknowledgeSource(NativeSourceInstallation installation)
        {
            if (!AckSucceeds) return false;
            Assert.Equal(installation.Projection.Display, Buffer);
            Installation = installation;
            return true;
        }
        /// <inheritdoc />
        public NativeSourceObservation? ApplySourceChange(NativeSourceReplacement replacement)
        {
            Replacements.Add(replacement);
            Assert.Equal(Installation!.Stamp, replacement.Before.Stamp);
            Assert.Equal(Installation.Nonce, replacement.Before.Nonce);
            Assert.Equal(replacement.Before.Projection.Display, Buffer);
            if (!RangeSucceeds) return null;
            Buffer = Buffer.Remove(replacement.DisplayStart, replacement.DisplayDeleteLength)
                .Insert(replacement.DisplayStart, replacement.DisplayInsert);
            Assert.Equal(replacement.After.Projection.Display, Buffer);
            Installation = replacement.After;
            return Observation();
        }
        /// <inheritdoc />
        public void SetSourceSelection(NativeSourceInstallation installation, bool reveal)
        {
            Installation = installation;
            Selections.Add((installation.Anchor, installation.Active, reveal));
        }
        /// <inheritdoc />
        public void SetSourceSemantics(NativeSourceSemantics semantics) { }
        /// <inheritdoc />
        public void SetSourceChrome(string title, string status, bool modified, bool canUndo = false, bool canRedo = false)
        { Modified = modified; CanUndo = canUndo; CanRedo = canRedo; }
        /// <summary>Actual engine-owned command availability in source chrome.</summary>
        public bool CanUndo { get; private set; }
        /// <summary>Actual engine-owned redo availability in source chrome.</summary>
        public bool CanRedo { get; private set; }
        /// <inheritdoc />
        public void SetSourceUnavailable(string reason, NativeSourceFailure failure = NativeSourceFailure.CanonicalRetained)
        { Unavailable = true; Failure = failure; }
        /// <summary>Distinguishes committed canonical recovery from unresolved native edits.</summary>
        public NativeSourceFailure? Failure { get; private set; }
        /// <summary>Returns exact buffer readback with a legal collapsed source selection.</summary>
        private NativeSourceObservation Observation() => new(Buffer, new(0, 0, 0), new(0, Math.Min(100, Buffer.Length)));
        /// <summary>Models one settled native edit before emitting its old identity.</summary>
        public NativeSourceCandidate EditSource(string display, NativeSourceSelection? selection = null)
        {
            var candidate = new NativeSourceCandidate(Installation!.Stamp, Installation.Nonce,
                display, selection ?? new(display.Length, display.Length, display.Length));
            Buffer = display;
            SourceCandidate?.Invoke(candidate);
            return candidate;
        }
        /// <summary>Delivers an old callback without changing the current native buffer.</summary>
        public void Echo(NativeSourceCandidate candidate) => SourceCandidate?.Invoke(candidate);
        /// <summary>Delivers a genuine viewport observation without mutation.</summary>
        public void Observe(NativeSourceSelection selection, TextSpan visible, long sequence) =>
            SourceViewChanged?.Invoke(new(Installation!.Stamp, Installation.Nonce, selection, visible, sequence));
        /// <summary>Dispatches engine redo.</summary>
        public void Redo() => RedoRequested?.Invoke();
        /// <summary>Dispatches global selection.</summary>
        public void SelectAll() => SelectAllRequested?.Invoke();
        /// <summary>Dispatches copy through the controller queue.</summary>
        public void Copy() => CopyRequested?.Invoke();
        /// <summary>Dispatches global line navigation.</summary>
        public void Navigate() => GoToLineRequested?.Invoke();

    }
}
