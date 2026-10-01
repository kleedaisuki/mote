using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Configuration;
using Mote.Engine;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Windows;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Actual hidden product HWND/controller checks for ordinary CSV delivery, without desktop input.</summary>
public sealed class NativeCsvReleaseReadinessTests
{
    /// <summary>Cancellation after the first committed Grid query retires its session before future edits are replayed.</summary>
    [Fact]
    public async Task Composite_cancellation_rebuilds_before_next_revision()
    {
        using var document = new Document("a,b\nc,d\ne,f");
        using var cancellation = new CancellationTokenSource();
        var policy = new CancelSecondQueryPolicy(cancellation);
        using var driver = new NativeFormatSessionDriver(policy);
        document.ChangedRange += (_, change) => driver.Record(change);
        CsvGridRequest Grid() => new([new(0, document.Snapshot.Length)],
            new CsvGridAnchor.Source(document.Snapshot.Length), 64, new(0, 16), AnalysisScope.Full);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.AnalyzePresentationAsync(document.Snapshot,
            new(new(0, document.Snapshot.Length), AnalysisScope.Full), cancellation.Token, Grid(), 24));
        Assert.Equal(1, policy.Created);
        Assert.Equal(1, policy.Disposed);
        document.Apply(new(0, 1, "changed"));
        var result = await driver.AnalyzePresentationAsync(document.Snapshot,
            new(new(0, document.Snapshot.Length), AnalysisScope.Full), default, Grid(), 24);
        Assert.Equal(2, policy.Created);
        Assert.Equal(document.Snapshot.Version, result.Grid!.Version);
        Assert.Equal(new[] { 0, 1, 2 }, result.Grid.Rows.Select(row => row.Ordinal));
        Assert.StartsWith("changed", result.Grid.DisplayText);
        Assert.All(policy.ChangesOnNewSession, changes => Assert.Empty(changes));
    }

    /// <summary>Wraps the actual CSV grammar and cancels precisely at normalized requery admission, not by timing.</summary>
    private sealed class CancelSecondQueryPolicy(CancellationTokenSource cancellation) : IIncrementalDocumentPolicy
    {
        private readonly IIncrementalDocumentPolicy _inner = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Csv);
        private readonly CancellationTokenSource _cancellation = cancellation;
        internal int Created, Disposed;
        internal readonly List<IReadOnlyList<VersionedEdit>> ChangesOnNewSession = [];
        public DocumentKind Kind => DocumentKind.Csv;
        public string DisplayName => "CSV cancellation probe";
        public IFormatSession CreateSession() => new Session(this, (ICsvGridFormatSession)_inner.CreateSession(), ++Created);
        public FormatAnalysis Analyze(string text, CancellationToken cancellationToken = default) => _inner.Analyze(text, cancellationToken);
        public string Format(string text) => _inner.Format(text);
        public string RenderHtml(FormatAnalysis analysis) => _inner.RenderHtml(analysis);
        private sealed class Session(CancelSecondQueryPolicy owner, ICsvGridFormatSession inner, int number) : ICsvGridFormatSession
        {
            public DocumentAnalysis Analyze(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changes,
                AnalysisRequest request, CancellationToken cancellationToken = default) => inner.Analyze(snapshot, changes, request, cancellationToken);
            public WindowedAnalysis AnalyzeWindows(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changes,
                IReadOnlyList<TextSpan> windows, AnalysisScope scope, CancellationToken cancellationToken = default) =>
                inner.AnalyzeWindows(snapshot, changes, windows, scope, cancellationToken);
            public CsvGridAnalysis AnalyzeGrid(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changes,
                CsvGridRequest request, CancellationToken cancellationToken = default)
            {
                if (number == 1 && request.Anchor is CsvGridAnchor.Row)
                { owner._cancellation.Cancel(); cancellationToken.ThrowIfCancellationRequested(); }
                if (number == 2) owner.ChangesOnNewSession.Add(changes.ToArray());
                return inner.AnalyzeGrid(snapshot, changes, request, cancellationToken);
            }
            public void Dispose() { owner.Disposed++; inner.Dispose(); }
        }
    }

    /// <summary>A terminal same-version table must never invent pending rows before its retained source-follow payload.</summary>
    [Fact]
    public void Source_follow_at_last_record_delivers_only_ready_native_slots()
    {
        if (!OperatingSystem.IsWindows()) return;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { if (OperatingSystem.IsWindows()) RunOwnedWindow(); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Owned product check did not terminate.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>Creates the actual shell controls but omits Run's ShowWindow and SetFocus; work is condition-pumped.</summary>
    [SupportedOSPlatform("windows")]
    private static void RunOwnedWindow()
    {
        using var temp = new RepoTemp();
        var path = temp.File("ready.csv");
        File.WriteAllText(path, "id,name,note\r\n1,mote,\"中国, quoted field\"\r\n2,reviewer,\"He said \"\"ready\"\"\"\r\n");
        var configuration = MoteConfigLoader.Load(new MoteConfigLoadOptions
        { UserHomeDirectory = temp.Path, UseEnvironmentOverride = false });
        var shell = new WindowsEditorShell(nativeSource: true);
        using var controller = new NativeEditorController(shell, configuration,
            ThemePolicies.Get(configuration.ThemeId), path, EditorPresentationProfile.NativeSource);
        var shellType = typeof(WindowsEditorShell);
        var creating = shellType.GetField("_creating", BindingFlags.Static | BindingFlags.NonPublic)!;
        var active = shellType.GetField("_active", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldCreating = creating.GetValue(null);
        var oldActive = active.GetValue(null);
        nint window = 0;
        var library = Win32.LoadLibraryW("Msftedit.dll");
        Assert.NotEqual(0, library);
        try
        {
            var procedure = (Win32.WindowProcedure)shellType.GetField("WindowProcedure",
                BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var windowClass = new Win32.WindowClass
            { WindowProc = procedure, Instance = Win32.GetModuleHandleW(null), ClassName = "MoteCsvReleaseOwnedTest" };
            Win32.RegisterClassW(ref windowClass);
            creating.SetValue(null, shell); active.SetValue(null, shell);
            window = Win32.CreateWindowExW(0, windowClass.ClassName, "CSV readiness test",
                Win32.WS_OVERLAPPEDWINDOW | Win32.WS_CLIPCHILDREN, 0, 0, 1100, 760,
                0, 0, windowClass.Instance, 0);
            Assert.NotEqual(0, window);
            creating.SetValue(null, oldCreating);
            ((Action)shellType.GetField("Shown", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!)();
            PumpUntil(() => Analysis(shell)?.Grid is { Extent.ExactRowCount: 3 }, "Initial exact CSV projection");
            var editor = (nint)Field(shell, "_editor")!;
            var position = File.ReadAllText(path).Length - 2;
            shell.SetSelection(position, position);
            shellType.GetMethod("PublishSourceView", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null);
            typeof(NativeEditorController).GetMethod("ScheduleAnalysis", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(controller, [default(Mote.Telemetry.TelemetryMark), true]);
            var previousIdentity = Analysis(shell)!.Identity;
            PumpUntil(() => Analysis(shell) is { Grid: { } grid } view && grid.Version == 0 &&
                view.Identity != previousIdentity && grid.Completeness == AnalysisCompleteness.Complete &&
                view.GridNavigation is { Pending: false, Ready: not null }, "Final-record complete source-follow projection: " + Analysis(shell)?.Status);
            var final = Analysis(shell)!;
            var nativeGrid = (WindowsCsvGrid)Field(shell, "_grid")!;
            var slots = (GridRow?[])Field(nativeGrid, "_slots")!;
            Assert.Equal(final.Identity, Field(nativeGrid, "_identity"));
            Assert.Equal(final.GridNavigation, Field(nativeGrid, "_navigation"));
            Assert.All(slots, slot => Assert.NotNull(slot));
            Assert.Equal(final.Grid!.RequestedRows.Start, final.GridNavigation!.RequestedRows.Start);
            Assert.Equal(final.Grid.Rows.Select(row => row.Ordinal), slots.Select(slot => slot!.Ordinal));
            Assert.Equal(slots.Length, (int)Win32.SendMessageW(nativeGrid.Handle, WindowsGridInterop.GetItemCount, 0, 0));
            Assert.Equal(new[] { 0, 1, 2 }, slots.Select(row => row!.Ordinal));
            for (var row = 0; row < 3; row++)
                for (var column = 0; column < 3; column++)
                    Assert.Equal(final.Grid.DisplayText.Substring(slots[row]!.Cells[column].DisplayRange.Start,
                        slots[row]!.Cells[column].DisplayRange.Length), NativeLabel(nativeGrid.Handle, row, column + 1));
            Assert.Null(Field(controller, "_gridAnchor"));
            var lateObserved = false;
            shell.Post(() => lateObserved = true);
            PumpUntil(() => lateObserved, "Late UI mailbox witness");
            Assert.Equal(final.Identity, Analysis(shell)!.Identity);
            shell.SetSelection(0, 0);
            shellType.GetMethod("PublishSourceView", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null);
            typeof(NativeEditorController).GetMethod("ScheduleAnalysis", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(controller, [default(Mote.Telemetry.TelemetryMark), true]);
            PumpUntil(() => Analysis(shell)?.Identity != final.Identity &&
                Analysis(shell)?.Grid?.RequestedAnchor is CsvGridAnchor.Source { Offset: 0 }, "Subsequent source-follow delivery");
            Assert.Null(Field(controller, "_gridAnchor"));
        }
        finally
        {
            controller.Dispose();
            if (window != 0) Win32.DestroyWindow(window);
            creating.SetValue(null, oldCreating); active.SetValue(null, oldActive);
            FreeLibrary(library);
        }
    }

    /// <summary>Observes actual message completions; the timeout is a hang guard, not an experience threshold.</summary>
    private static void PumpUntil(Func<bool> ready, string stage)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            while (PeekMessageW(out var message, 0, 0, 0, 1))
            {
                if (message.Id == 0x12) continue;
                Win32.TranslateMessage(ref message); Win32.DispatchMessageW(ref message);
            }
            if (ready()) return;
            Thread.Yield();
        }
        Assert.Fail(stage);
    }

    /// <summary>Reads the retained real product analysis, rather than deriving readiness from screenshot text.</summary>
    private static NativeAnalysisView? Analysis(WindowsEditorShell shell) => (NativeAnalysisView?)Field(shell, "_analysis");
    /// <summary>Retrieves actual owner-data callback text from the owned ListView, not a predicted render string.</summary>
    private static string NativeLabel(nint handle, int row, int column)
    {
        var buffer = Marshal.AllocHGlobal(512);
        var itemBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<WindowsGridInterop.Item>());
        try
        {
            var item = new WindowsGridInterop.Item { Mask = 1, Row = row, Column = column, Text = buffer, TextCapacity = 256 };
            Marshal.StructureToPtr(item, itemBuffer, false);
            Assert.NotEqual(0, Win32.SendMessageW(handle, WindowsGridInterop.GetItem, 0, itemBuffer));
            return Marshal.PtrToStringUni(buffer)!;
        }
        finally { Marshal.FreeHGlobal(itemBuffer); Marshal.FreeHGlobal(buffer); }
    }
    /// <summary>Inspects private retained authority without mutating product delivery state.</summary>
    private static object? Field(object owner, string name) => owner.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
    [DllImport("user32.dll")]
    private static extern bool PeekMessageW(out Win32.Message message, nint window, uint minimum, uint maximum, uint flags);
    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(nint module);
}
