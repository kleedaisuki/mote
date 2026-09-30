using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Security.Cryptography;
using Mote.Configuration;
using Mote.Engine;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Windows;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Opt-in disposable-runner publication through the real controller, table and Windows clipboard publisher.</summary>
public sealed class NativeCsvGridClipboardWorkflowTests
{
    /// <summary>Exact human-reviewed permission marker; it is never created by this test.</summary>
    private const string Approval = "disposable-github-hosted-windows-only";

    /// <summary>Exercises the identical controller and HWND routes locally, but never opens or reads the OS clipboard.</summary>
    [Fact]
    public void Fake_publication_workflow_never_touches_os_clipboard()
    {
        if (!OperatingSystem.IsWindows()) return;
        Exercise(realClipboard: false);
    }

    /// <summary>Mutates CF_UNICODETEXT only with all explicit CI, hosted-runner, workspace and approval-marker gates satisfied.</summary>
    [Fact]
    public void Approved_disposable_runner_publishes_exact_unicode_and_preserves_refused_sentinel()
    {
        if (Environment.GetEnvironmentVariable("MOTE_DISPOSABLE_GRID_CLIPBOARD") != "1") return;
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Clipboard opt-in requires Windows.");
        Assert.Equal("true", Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.Equal("github-hosted", Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT"));
        var root = RepositoryRoot();
        Assert.Equal(root.TrimEnd('\\'), Path.GetFullPath(Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? "").TrimEnd('\\'), ignoreCase: true);
        var directory = Path.Combine(root, ".cache", "native-grid-clipboard");
        Assert.Equal(Approval, File.ReadAllText(Path.Combine(directory, "approval.txt")).Trim());
        var run = RequiredEnvironment("GITHUB_RUN_ID");
        var attempt = RequiredEnvironment("GITHUB_RUN_ATTEMPT");
        var commit = RequiredEnvironment("GITHUB_SHA");
        var runKey = $"{run}:{attempt}:{commit}";
        Assert.Equal(runKey, File.ReadAllText(Path.Combine(directory, "approval-run.txt")).Trim());
        var sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,
            "tests", "Mote.Tests", "NativeCsvGridClipboardWorkflowTests.cs"))));
        var report = Path.Combine(directory, "report.json");
        File.WriteAllText(report, JsonSerializer.Serialize(new { status = "running", nativeClipboard = true, runKey, sourceHash, utc = DateTimeOffset.UtcNow }));
        try
        {
            Exercise(realClipboard: true);
            File.WriteAllText(report, JsonSerializer.Serialize(new { status = "passed", nativeClipboard = true, runKey, sourceHash, utc = DateTimeOffset.UtcNow,
                cases = new[] { "quoted-crlf", "empty-final-row", "missing-refusal", "explicit-missing-padding", "nul-refusal", "over-cap-refusal" },
                coverage = "Hidden HWND + production controller + production publisher; no desktop context-menu/input/Native AOT claim." }));
        }
        catch (Exception error)
        {
            File.WriteAllText(report, JsonSerializer.Serialize(new { status = "failed", runKey, sourceHash, utc = DateTimeOffset.UtcNow, error = error.ToString() }));
            throw;
        }
    }

    /// <summary>Refuses incomplete CI identity before any mutation; no synthetic local defaults exist.</summary>
    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value :
            throw new InvalidOperationException($"Required CI identity missing: {name}");

    /// <summary>Finds the checked-out repository; all fixtures and reports remain beneath it.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "mote.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root unavailable.");
    }

    /// <summary>Expected strings are literal contract examples, independent of command serialization code.</summary>
    [SupportedOSPlatform("windows")]
    private static void Exercise(bool realClipboard)
    {
        Check("\"a\r\nb\t\"\"c\",tail", NativeGridIntentKind.CopyValue, 0, 0, "a\r\nb\t\"c", realClipboard);
        Check("a\r\n\r\n", NativeGridIntentKind.CopyCsv, 1, 0, "a\r\n\"\"", realClipboard);
        Check("a,b\r\nx", NativeGridIntentKind.CopyCsv, 1, 1, null, realClipboard);
        Check("a,b\r\nx", NativeGridIntentKind.CopyCsvPadded, 1, 1, "a,b\r\nx,\"\"", realClipboard);
        Check("\"a\0b\",tail", NativeGridIntentKind.CopyValue, 0, 0, null, realClipboard);
        Check(new string('x', NativeCsvGridCommands.MaxPayloadLength + 1), NativeGridIntentKind.CopyValue, 0, 0, null, realClipboard);
    }

    /// <summary>Copies current real table coordinates without touching canonical text, source selection or history.</summary>
    [SupportedOSPlatform("windows")]
    private static void Check(string source, NativeGridIntentKind kind, int endRow, int endColumn, string? expected, bool realClipboard)
    {
        using var temp = new RepoTemp();
        var path = temp.File("records.csv");
        File.WriteAllText(path, source);
        using var shell = new GridShell(realClipboard);
        var configuration = MoteConfigLoader.Load(new MoteConfigLoadOptions { UserHomeDirectory = temp.Path, UseEnvironmentOverride = false });
        using var controller = new NativeEditorController(shell, configuration, ThemePolicies.Get(configuration.ThemeId), path);
        controller.Run();
        shell.Pump(() => shell.Document?.Title.Contains("records.csv") == true && shell.Analysis?.Grid?.Extent.ExactRowCount is not null && shell.Analysis.Stamp == shell.Document.Stamp);
        var document = (Document)typeof(NativeEditorController).GetField("_document", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        var navigation = (NativeNavigationModel)typeof(NativeEditorController).GetField("_navigation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        // Establish canonical fixture selection without scheduling a competing viewport analysis.
        navigation.SetSelection(document.Snapshot, 1, 2);
        var before = (document.Snapshot.Version, document.IsModified, document.CanUndo, document.CanRedo, navigation.Anchor, navigation.Active);
        // Private Select/Emit are the same native adapter paths as pointer and context-menu actions.
        // Avoid modal TrackPopupMenu and desktop input; this does not validate menu hit testing.
        typeof(WindowsCsvGrid).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell.Grid, [0, 0, false]);
        typeof(WindowsCsvGrid).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell.Grid, [endRow, endColumn, true]);
        const string sentinel = "mote disposable clipboard sentinel 😀\r\nexact";
        shell.SetClipboardText(sentinel);
        var calls = shell.ClipboardCalls;
        var errors = shell.Errors.Count;
        if (kind == NativeGridIntentKind.CopyValue) shell.Grid.Copy();
        else typeof(WindowsCsvGrid).GetMethod("Emit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell.Grid, [kind]);
        shell.Pump(() => expected is null ? shell.Errors.Count > errors : shell.ClipboardCalls > calls || shell.Errors.Count > errors);
        Assert.Equal(expected is null ? calls : calls + 1, shell.ClipboardCalls);
        Assert.Equal(expected ?? sentinel, realClipboard ? ReadNativeClipboard() : shell.Clipboard);
        if (expected is not null) Assert.Equal(errors, shell.Errors.Count);
        Assert.Equal(source, document.Snapshot.GetText());
        Assert.Equal(source, File.ReadAllText(path));
        Assert.Equal(before, (document.Snapshot.Version, document.IsModified, document.CanUndo, document.CanRedo, navigation.Anchor, navigation.Active));
        Assert.False(document.Undo());
        Assert.Equal(source, document.Snapshot.GetText());
    }

    /// <summary>Reads the real UTF-16 clipboard handle without normalization, using a bounded independent terminator check.</summary>
    private static string ReadNativeClipboard()
    {
        Assert.True(Win32.OpenClipboard(0));
        try
        {
            var memory = GetClipboardData(Win32.CF_UNICODETEXT);
            Assert.NotEqual(0, memory);
            var size = GlobalSize(memory);
            Assert.InRange((long)size, 2, (NativeCsvGridCommands.MaxPayloadLength + 1L) * 2);
            var pointer = Win32.GlobalLock(memory);
            Assert.NotEqual(0, pointer);
            try
            {
                var chars = new char[checked((int)(size / 2))];
                Marshal.Copy(pointer, chars, 0, chars.Length);
                var terminator = Array.IndexOf(chars, '\0');
                Assert.InRange(terminator, 0, chars.Length - 1);
                return new string(chars, 0, terminator);
            }
            finally { Win32.GlobalUnlock(memory); }
        }
        finally { Assert.True(Win32.CloseClipboard()); }
    }

    /// <summary>Returns the system-owned CF_UNICODETEXT handle; ownership is never transferred to this test.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint format);
    /// <summary>Bounds a native movable allocation before independent UTF-16 readback.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint GlobalSize(nint memory);
    [SupportedOSPlatform("windows")]
    private sealed class GridShell : INativeEditorShell, IDisposable
    {
        /// <summary>Only the explicitly approved disposable-runner mode may publish native clipboard data.</summary>
        private readonly bool _realClipboard;
        /// <summary>Actual production clipboard publisher, bound to the hidden native owner.</summary>
        private readonly WindowsEditorShell _publisher = new();
        /// <summary>Native table whose current selection creates production intents.</summary>
        internal readonly WindowsCsvGrid Grid;
        /// <summary>Hidden native clipboard owner; never shown or used for desktop input.</summary>
        private readonly nint _parent;
        /// <summary>Keeps the unmanaged callback alive for the complete native lifetime.</summary>
        private readonly Win32.SubclassProcedure _notify;

        /// <summary>Creates only local hidden HWND state; clipboard is untouched until explicit publication.</summary>
        internal GridShell(bool realClipboard)
        {
            _realClipboard = realClipboard;
            _parent = Win32.CreateWindowExW(0, "STATIC", "Disposable Grid clipboard probe", Win32.WS_OVERLAPPEDWINDOW,
                0, 0, 800, 500, 0, 0, Win32.GetModuleHandleW(null), 0);
            Assert.NotEqual(0, _parent);
            _notify = Notify;
            Assert.True(Win32.SetWindowSubclass(_parent, _notify, 89, 0));
            Grid = new WindowsCsvGrid(_parent, 104, ThemePolicies.Get(ThemePolicies.DefaultId));
            Grid.IntentRequested += Intent;
            Grid.WindowRequested += Window;
            Grid.Faulted += error => Errors.Add(error.ToString());
            typeof(WindowsEditorShell).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_publisher, _parent);
        }

        /// <summary>Forwards only table notification callbacks, without a global input hook or visible window.</summary>
        private nint Notify(nint window, uint message, nuint parameter, nint data, nuint id, nuint reference)
        {
            if (message == Win32.WM_NOTIFY && Grid is not null && Grid.HandleNotify(data, out var result)) return result;
            return Win32.DefSubclassProc(window, message, parameter, data);
        }

        /// <summary>Drains UI completions on the HWND-owning thread, with a bounded timeout.</summary>
        internal void Pump(Func<bool> ready)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(30))
            {
                while (_posted.TryDequeue(out var action)) action();
                if (ready()) return;
                Thread.Sleep(5);
            }
            Assert.Fail($"Timed out: {Analysis?.Status}; errors={string.Join(';', Errors)}");
        }

        /// <summary>Destroys only handles created by this fixture; it never restores or clears the developer clipboard.</summary>
        public void Dispose()
        {
            Grid.Dispose();
            Win32.RemoveWindowSubclass(_parent, _notify, 89);
            Win32.DestroyWindow(_parent);
        }

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
        /// <summary>Witnesses rejected commands so negative checks do not rely only on elapsed time.</summary>
        internal List<string> Errors { get; } = [];
        /// <inheritdoc />
        public NativeLineEndingMode LineEndingMode => NativeLineEndingMode.Preserve;
        /// <inheritdoc />
        public bool PrefersDark => true;
        /// <inheritdoc />
        public bool IsTextComposing => false;
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
        public void SetAnalysis(NativeAnalysisView view) { Analysis = view; Grid.Install(view.ShowPreview ? view.Grid : null, view.Identity); }
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
        public string? PromptGridReplacement(string currentValue) => null;
        /// <inheritdoc />
        public void SetClipboardText(string text) { if (_realClipboard) _publisher.SetClipboardText(text); ClipboardCalls++; Clipboard = text; }
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
    }
}
