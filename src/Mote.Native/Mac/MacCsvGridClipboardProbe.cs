using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mote.Configuration;
using Mote.Engine;
using Mote.Themes;

namespace Mote.Native
{
    /// <summary>Immutable canonical observations for the bounded clipboard acceptance probe.</summary>
    internal readonly record struct NativeGridClipboardAudit(TextSnapshot Snapshot, bool Modified,
        bool CanUndo, bool CanRedo, int Anchor, int Active);

    internal sealed partial class NativeEditorController
    {
        /// <summary>Observes controller-owned state without reflection, mutation or a public production API.</summary>
        /// <remarks>Called only on the owning UI thread by the explicit diagnostic route.</remarks>
        internal NativeGridClipboardAudit ProbeGridClipboardAudit() => new(_document.Snapshot,
            _document.IsModified, _document.CanUndo, _document.CanRedo, _navigation.Anchor, _navigation.Active);
    }
}

namespace Mote.Native.Mac
{
    /// <summary>Opt-in controller/table/production-publisher acceptance on a disposable hosted Mac.</summary>
    /// <remarks>Fake mode never accesses NSPasteboard. Actual mode destroys the runner's old clipboard.</remarks>
    [SupportedOSPlatform("macos")]
    internal static class MacCsvGridClipboardProbe
    {
        /// <summary>Permission must be materialized by the independently reviewed invocation, never this probe.</summary>
        private const string Approval = "disposable-github-hosted-macos-only";
        /// <summary>Exact identifiers admit a fresh six-case report, not an opt-out or stale success.</summary>
        private static readonly string[] Cases = ["quoted-crlf", "empty-final-row", "missing-refusal",
            "explicit-missing-padding", "nul-refusal", "over-cap-refusal"];
        /// <summary>Only synthetic content is published; no prior clipboard content is inspected or retained.</summary>
        private const string Sentinel = "mote disposable Mac clipboard sentinel 😀\r\nexact";

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern nint KeyEvent(nint receiver, nint selector, nuint type, ObjC.Point location,
            nuint flags, double timestamp, nint window, nint context, nint characters,
            nint ignoringModifiers, byte repeat, ushort keyCode);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern nint SendCell(nint receiver, nint selector, nint column, nint row, byte create);

        /// <summary>Runs fake publication by default; actual publication requires every hosted-runner gate.</summary>
        /// <remarks>External invocation must pin reviewed source bytes and build this exact checkout first.</remarks>
        internal static int Run(bool actual = false)
        {
            string? directory = null;
            string? runKey = null;
            string? sourceHash = null;
            try
            {
                Require(OperatingSystem.IsMacOS(), "macOS target required");
                var root = RepositoryRoot();
                if (actual)
                {
                    (directory, runKey, sourceHash) = AdmitActual(root);
                    Report(directory, "running", runKey, sourceHash, [], null);
                }
                using var watchdog = new Timer(static _ => Environment.Exit(124), null,
                    TimeSpan.FromSeconds(90), Timeout.InfiniteTimeSpan);
                Require(ObjC.ApplicationLoad(), "AppKit load");
                Require(ObjC.Send(ObjC.Class("NSThread"), ObjC.Sel("isMainThread")) != 0, "AppKit main thread");
                var pool = ObjC.New("NSAutoreleasePool");
                try
                {
                    Require(ObjC.Send(ObjC.Class("NSApplication"), ObjC.Sel("sharedApplication")) != 0,
                        "owned process AppKit application");
                    var fixtures = NewFixtureDirectory(root);
                    Check(fixtures, Cases[0], "\"a\r\nb\t\"\"c\",tail", NativeGridIntentKind.CopyValue, 0, 0, "a\r\nb\t\"c", actual);
                    Check(fixtures, Cases[1], "a\r\n\r\n", NativeGridIntentKind.CopyCsv, 1, 0, "a\r\n\"\"", actual);
                    Check(fixtures, Cases[2], "a,b\r\nx", NativeGridIntentKind.CopyCsv, 1, 1, null, actual);
                    Check(fixtures, Cases[3], "a,b\r\nx", NativeGridIntentKind.CopyCsvPadded, 1, 1, "a,b\r\nx,\"\"", actual);
                    Check(fixtures, Cases[4], "\"a\0b\",tail", NativeGridIntentKind.CopyValue, 0, 0, null, actual);
                    Check(fixtures, Cases[5], new string('x', NativeCsvGridCommands.MaxPayloadLength + 1),
                        NativeGridIntentKind.CopyValue, 0, 0, null, actual);
                }
                finally { ObjC.Send(pool, ObjC.Sel("drain")); }
                if (actual) Report(directory!, "passed", runKey!, sourceHash!, Cases, null);
                Console.WriteLine($"mote-native-mac-grid-clipboard-ready mode={(actual ? "actual" : "fake")}; cases=6; desktop-input=not-tested");
                return 0;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (directory is not null)
                    Report(directory, "failed", runKey!, sourceHash!, [], error.ToString());
                Console.Error.WriteLine($"Mac CSV Grid clipboard check failed: {error.Message}");
                return 1;
            }
        }

        /// <summary>Reads approval and current CI identity before AppKit or any clipboard call.</summary>
        private static (string Directory, string RunKey, string SourceHash) AdmitActual(string root)
        {
            Require(Environment.GetEnvironmentVariable("MOTE_DISPOSABLE_MAC_GRID_CLIPBOARD") == "1", "explicit actual opt-in");
            Require(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" &&
                Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "github-hosted", "disposable hosted GitHub runner only");
            Require(Path.GetFullPath(RequiredEnvironment("GITHUB_WORKSPACE")).TrimEnd(Path.DirectorySeparatorChar) == root,
                "exact checked-out workspace");
            var directory = Path.Combine(root, ".cache", "native-mac-grid-clipboard");
            CheckExistingPath(directory, isDirectory: true);
            var runKey = $"{RequiredEnvironment("GITHUB_RUN_ID")}:{RequiredEnvironment("GITHUB_RUN_ATTEMPT")}:{RequiredEnvironment("GITHUB_SHA")}";
            foreach (var name in new[] { "approval.txt", "approval-run.txt" })
                CheckExistingPath(Path.Combine(directory, name), isDirectory: false);
            Require(File.ReadAllText(Path.Combine(directory, "approval.txt")) == Approval, "exact independent approval marker");
            Require(File.ReadAllText(Path.Combine(directory, "approval-run.txt")) == runKey, "fresh approval identity");
            // Fresh invocation removes stale evidence before creating its approval markers.
            Require(!File.Exists(Path.Combine(directory, "report.json")) &&
                new FileInfo(Path.Combine(directory, "report.json")).LinkTarget is null, "absent prior report");
            var source = Path.Combine(root, "src", "Mote.Native", "Mac", "MacCsvGridClipboardProbe.cs");
            CheckExistingPath(source, isDirectory: false);
            return (directory, runKey, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))));
        }

        /// <summary>Requires genuine caller identity rather than inventing local CI defaults.</summary>
        private static string RequiredEnvironment(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value :
                throw new InvalidOperationException($"Required CI identity missing: {name}");

        /// <summary>Rejects symlink/reparse ancestry before repository scratch creation or marker admission.</summary>
        private static string RepositoryRoot()
        {
            var root = Path.GetFullPath(Environment.CurrentDirectory).TrimEnd(Path.DirectorySeparatorChar);
            Require(File.Exists(Path.Combine(root, "mote.sln")) &&
                File.Exists(Path.Combine(root, "src", "Mote.Native", "Mote.Native.csproj")) &&
                (Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, ".git"))), "repository working directory");
            CheckExistingPath(root, isDirectory: true);
            return root;
        }

        /// <summary>Ensures every existing path component has ordinary local ownership, not a redirected target.</summary>
        private static void CheckExistingPath(string path, bool isDirectory)
        {
            FileSystemInfo current = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
            Require(current.Exists && current.LinkTarget is null &&
                (current.Attributes & FileAttributes.ReparsePoint) == 0, "existing nonsymlink path");
            var parent = isDirectory ? ((DirectoryInfo)current).Parent : ((FileInfo)current).Directory;
            for (; parent is not null; parent = parent.Parent)
                Require(parent.Exists && parent.LinkTarget is null &&
                    (parent.Attributes & FileAttributes.ReparsePoint) == 0, "nonsymlink path ancestry");
        }

        /// <summary>Creates fresh bounded fixtures only beneath the checked repository .cache; no recursive cleanup.</summary>
        private static string NewFixtureDirectory(string root)
        {
            var directory = root;
            foreach (var segment in new[] { ".cache", "native-mac-grid-clipboard", "fixtures" })
            {
                directory = Path.Combine(directory, segment);
                Require(new DirectoryInfo(directory).LinkTarget is null, "unredirected scratch segment");
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                CheckExistingPath(directory, isDirectory: true);
            }
            directory = Path.Combine(directory, Guid.NewGuid().ToString("N"));
            Require(!Directory.Exists(directory) && new DirectoryInfo(directory).LinkTarget is null, "fresh fixture identity");
            Directory.CreateDirectory(directory);
            CheckExistingPath(directory, isDirectory: true);
            return directory;
        }

        /// <summary>Literal expectations distinguish exact decoded/source-backed values from sanitized table display.</summary>
        private static void Check(string fixtures, string name, string source, NativeGridIntentKind kind,
            int endRow, int endColumn, string? expected, bool actual)
        {
            var directory = Path.Combine(fixtures, name);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "records.csv");
            File.WriteAllText(path, source, new UTF8Encoding(false, true));
            using var shell = new GridShell(actual);
            var configuration = MoteConfigLoader.Load(new MoteConfigLoadOptions
            { UserHomeDirectory = directory, UseEnvironmentOverride = false });
            using var controller = new NativeEditorController(shell, configuration,
                ThemePolicies.Get(configuration.ThemeId), path);
            controller.Run();
            shell.Pump(() => shell.Document?.Title.Contains("records.csv", StringComparison.Ordinal) == true &&
                shell.Analysis?.Grid?.Extent.ExactRowCount is not null && shell.Analysis.Stamp == shell.Document.Stamp);
            shell.SelectSource(1, 2);
            var before = controller.ProbeGridClipboardAudit();
            Require(before.Anchor == 1 && before.Active == 2 && before.Snapshot.GetText() == source,
                "nonempty canonical source selection before Copy");
            var table = shell.Grid.Table;
            Require(ObjC.Send(table, ObjC.Sel("numberOfRows")) >= endRow + 1, "actual native table rows");
            Require(SendCell(table, ObjC.Sel("viewAtColumn:row:makeIfNecessary:"), 0, 0, 1) != 0,
                "actual AppKit data-source cell");
            if (endRow != 0 || endColumn != 0)
            {
                // Establish a rectangle through production Shift-arrow handling, then the native row API.
                // No physical event, first-responder focus, desktop menu or pointer hit test is claimed.
                Key(table, "\uf703", 1u << 17);
                if (endColumn == 0) Key(table, "\uf702", 1u << 17);
                var indexes = ObjC.Send(ObjC.Class("NSIndexSet"), ObjC.Sel("indexSetWithIndex:"), endRow);
                ObjC.Send(table, ObjC.Sel("selectRowIndexes:byExtendingSelection:"), indexes, (byte)0);
            }
            shell.SetClipboardText(Sentinel);
            var calls = shell.ClipboardCalls;
            var errors = shell.Errors.Count;
            var changeCount = actual ? PasteboardChangeCount() : 0;
            if (kind == NativeGridIntentKind.CopyValue) shell.Grid.CopySelection();
            else shell.Grid.Emit(kind);
            var intent = shell.LastIntent;
            Require(intent?.Kind == kind && intent?.Row == 0 && intent?.Column == 0 &&
                (endRow == 0 && endColumn == 0 || intent?.EndRow == endRow && intent?.EndColumn == endColumn),
                "native adapter captures exact selected rectangle");
            shell.Pump(() => expected is null ? shell.Errors.Count > errors :
                shell.ClipboardCalls > calls || shell.Errors.Count > errors);
            Require(shell.ClipboardCalls == (expected is null ? calls : calls + 1), "exact publication-call count");
            Require((actual ? ReadPasteboard() : shell.Clipboard) == (expected ?? Sentinel), "independent exact clipboard readback");
            if (expected is null && actual) Require(PasteboardChangeCount() == changeCount, "refusal preserves pasteboard ownership");
            if (expected is not null) Require(shell.Errors.Count == errors, "successful Copy has no rejection");
            Require(controller.ProbeGridClipboardAudit() == before && File.ReadAllText(path) == source,
                "source/version/selection/modified/Undo/Redo and disk unchanged");
            shell.RequestUndo();
            Require(controller.ProbeGridClipboardAudit() == before, "Copy added no canonical Undo transaction");
        }

        /// <summary>Requests a synthetic in-process native event without sending global/desktop input.</summary>
        private static void Key(nint table, string text, nuint flags)
        {
            var evt = KeyEvent(ObjC.Class("NSEvent"), ObjC.Sel("keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:characters:charactersIgnoringModifiers:isARepeat:keyCode:"),
                10, new(0, 0), flags, 0, 0, 0, ObjC.String(text), ObjC.String(text), 0, 0);
            Require(evt != 0, "synthetic native event");
            ObjC.Send(table, ObjC.Sel("keyDown:"), evt);
        }

        /// <summary>Observes only after the probe has deliberately replaced this disposable runner's clipboard.</summary>
        private static nint PasteboardChangeCount() => ObjC.Send(
            ObjC.Send(ObjC.Class("NSPasteboard"), ObjC.Sel("generalPasteboard")), ObjC.Sel("changeCount"));

        /// <summary>Uses NSString length/UTF-16 copying, independently of the production publisher and display projection.</summary>
        private static string ReadPasteboard()
        {
            var board = ObjC.Send(ObjC.Class("NSPasteboard"), ObjC.Sel("generalPasteboard"));
            var text = ObjC.Send(board, ObjC.Sel("stringForType:"), ObjC.String("public.utf8-plain-text"));
            Require(text != 0, "pasteboard plain-text representation");
            var length = ObjC.Send(text, ObjC.Sel("length"));
            Require(length >= 0 && length <= NativeCsvGridCommands.MaxPayloadLength, "bounded pasteboard readback");
            var buffer = Marshal.AllocHGlobal(checked(((int)length + 1) * sizeof(char)));
            try
            {
                ObjC.Send(text, ObjC.Sel("getCharacters:range:"), buffer, new ObjC.Range(0, (nuint)length));
                return Marshal.PtrToStringUni(buffer, (int)length) ?? throw new IOException("Missing readback string.");
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        /// <summary>Writes bounded evidence through an AOT-safe JSON writer, with no user file paths or clipboard contents.</summary>
        private static void Report(string directory, string status, string runKey, string hash,
            string[] cases, string? error)
        {
            using var stream = File.Create(Path.Combine(directory, "report.json"));
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteString("status", status);
            writer.WriteBoolean("nativeClipboard", true);
            writer.WriteString("runKey", runKey);
            writer.WriteString("sourceHash", hash);
            writer.WriteString("utc", DateTimeOffset.UtcNow);
            writer.WriteStartArray("cases");
            foreach (var name in cases) writer.WriteStringValue(name);
            writer.WriteEndArray();
            writer.WriteString("coverage", "Hidden NSTableView + production controller + production NSPasteboard publisher; no desktop input/IME/AX/paint/clipboard contention claim.");
            if (error is not null) writer.WriteString("error", error);
            writer.WriteEndObject();
        }

        /// <summary>Fails closed on any unobserved or divergent acceptance contract.</summary>
        private static void Require(bool condition, string contract)
        { if (!condition) throw new InvalidOperationException(contract); }

        /// <summary>Owns a hidden AppKit table; only explicitly admitted actual mode reaches the production publisher.</summary>
        private sealed class GridShell : INativeEditorShell, IDisposable
        {
            /// <summary>Immutable gate result, captured only after admission.</summary>
            private readonly bool _actual;
            /// <summary>Publisher construction does not load a user profile, start a desktop shell or access NSPasteboard.</summary>
            private readonly MacEditorShell _publisher = new();
            /// <summary>Background controller completions are drained on this table's creating/main thread.</summary>
            private readonly ConcurrentQueue<Action> _posted = new();
            /// <summary>Native table instance installed with production ready descriptors.</summary>
            internal MacCsvGrid Grid { get; }
            /// <summary>Last bounded source view; canonical state is observed separately through the controller.</summary>
            internal NativeDocumentView? Document { get; private set; }
            /// <summary>Last complete installed semantic presentation.</summary>
            internal NativeAnalysisView? Analysis { get; private set; }
            /// <summary>Intent emitted by actual native table selection, not directly fabricated controller input.</summary>
            internal NativeGridIntent? LastIntent { get; private set; }
            /// <summary>Fake-mode publication value, never obtained from the OS.</summary>
            internal string Clipboard { get; private set; } = "";
            /// <summary>Counts successful publisher calls, including fixture sentinel setup.</summary>
            internal int ClipboardCalls { get; private set; }
            /// <summary>Nonmodal rejection witness; no native alert or modal loop is entered.</summary>
            internal List<string> Errors { get; } = [];

            /// <summary>Creates owned, undisplayed native view state only.</summary>
            internal GridShell(bool actual)
            {
                _actual = actual;
                Grid = new MacCsvGrid(intent => { LastIntent = intent; GridIntentRequested?.Invoke(intent); },
                    request => GridWindowRequested?.Invoke(request));
            }

            /// <summary>Processes controller completions without a desktop event loop, bounded to 12 seconds per wait.</summary>
            internal void Pump(Func<bool> ready)
            {
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromSeconds(12))
                {
                    while (_posted.TryDequeue(out var action)) action();
                    if (ready()) return;
                    Thread.Sleep(5);
                }
                throw new TimeoutException($"Controller completion timed out; errors={string.Join(';', Errors)}.");
            }

            /// <summary>Raises normal bounded source-selection input through the subscribed production controller.</summary>
            internal void SelectSource(int anchor, int active) => SelectionChanged?.Invoke(anchor, active);
            /// <summary>Checks real canonical Undo routing after a non-mutating Copy.</summary>
            internal void RequestUndo() => UndoRequested?.Invoke();
            /// <summary>Releases only views/delegates created here; does not clear, restore or inspect old clipboard data.</summary>
            public void Dispose() => Grid.Dispose();
            /// <inheritdoc />
            public NativeLineEndingMode LineEndingMode => NativeLineEndingMode.Preserve;
            /// <inheritdoc />
            public bool PrefersDark => true;
            /// <inheritdoc />
            public bool IsTextComposing => false;

#pragma warning disable CS0067 // Interface events outside this bounded workflow are intentionally inert.
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
                Analysis = view;
                if (view.ShowPreview && view.Grid is { } grid) Grid.Install(grid, view.Identity);
                else Grid.Clear();
            }
            /// <inheritdoc />
            public void SetTheme(IThemePolicy theme) => Grid.SetTheme(theme);
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
            public void SetClipboardText(string text)
            {
                if (_actual) _publisher.SetClipboardText(text);
                Clipboard = text;
                ClipboardCalls++;
            }
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
        }
    }
}
