using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Mote.Configuration;
using Mote.Engine;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Viewport;
using Mote.Native.Windows;
using Mote.Native.Windows.Canvas;
using Mote.Telemetry;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Deterministic causal, cancellation, privacy and disabled-overhead contracts, not physical paint tests.</summary>
[Collection("Telemetry")]
public sealed class NativePaintTraceTests
{
    /// <summary>Wrong revision, wrong lifetime and duplicate draw completions cannot steal a source endpoint.</summary>
    [Fact]
    public async Task Draw_matches_generation_version_and_completes_once()
    {
        using var temp = new RepoTemp();
        Configure(temp.Path);
        try
        {
            var trace = new NativeDrawTrace();
            var edit = MoteTelemetry.Mark();
            trace.Arm(TelemetryOperation.EditToDrawSubmission, MoteTelemetry.Fork(edit), 4, 2);
            Assert.Equal(0, trace.BeginDraw(3, 2));
            Assert.Equal(0, trace.BeginDraw(4, 1));
            var ticket = trace.BeginDraw(4, 2);
            Assert.NotEqual(0, ticket);
            trace.CompleteDraw(ticket, 4, 3);
            Assert.True(trace.IsPending);
            trace.CompleteDraw(ticket, 4, 2);
            trace.CompleteDraw(ticket, 4, 2);
            Assert.False(trace.IsPending);
            MoteTelemetry.RecordElapsed(TelemetryOperation.EditToPresentation, edit);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        var draw = Assert.Single(records, r => Operation(r) == "document.edit_to_draw_submission");
        var parent = Assert.Single(records, r => Operation(r) == "document.edit_to_presentation");
        Assert.Equal("success", draw.GetProperty("status").GetString());
        Assert.Equal(2, draw.GetProperty("attributes").GetProperty("version").GetInt64());
        Assert.Equal(parent.GetProperty("span_id").GetString(), draw.GetProperty("parent_span_id").GetString());
        Assert.NotEqual(parent.GetProperty("span_id").GetString(), draw.GetProperty("span_id").GetString());
        Assert.DoesNotContain("generation", string.Join('\n', records.Select(r => r.GetRawText())));
    }

    /// <summary>A nested replacement or same-version second interval invalidates an older draw ticket.</summary>
    [Fact]
    public async Task Reentrant_same_revision_replacement_cancels_old_ticket()
    {
        using var temp = new RepoTemp();
        Configure(temp.Path);
        try
        {
            var trace = new NativeDrawTrace();
            trace.Arm(TelemetryOperation.EditToDrawSubmission, MoteTelemetry.Mark(), 7, 9);
            var stale = trace.BeginDraw(7, 9);
            trace.Arm(TelemetryOperation.EditToDrawSubmission, MoteTelemetry.Mark(), 7, 9);
            trace.CompleteDraw(stale, 7, 9);
            Assert.True(trace.IsPending);
            trace.CompleteDraw(trace.BeginDraw(7, 9), 7, 9);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path).Where(r => Operation(r) == "document.edit_to_draw_submission").ToArray();
        Assert.Equal(2, records.Length);
        Assert.Equal("cancelled", records[0].GetProperty("status").GetString());
        Assert.Equal("success", records[1].GetProperty("status").GetString());
    }

    /// <summary>Document replacement and close emit bounded terminal cancellation, never a borrowed draw.</summary>
    [Fact]
    public async Task Replacement_and_close_cancel_once()
    {
        using var temp = new RepoTemp();
        Configure(temp.Path);
        try
        {
            var trace = new NativeDrawTrace();
            trace.Arm(TelemetryOperation.OpenToDrawSubmission, MoteTelemetry.Mark(), 1, 0);
            trace.ObserveDocument(1, 0);
            Assert.True(trace.IsPending);
            trace.ObserveDocument(2, 0);
            Assert.False(trace.IsPending);
            trace.Cancel();
            trace.Arm(TelemetryOperation.EditToDrawSubmission, MoteTelemetry.Mark(), 2, 1);
            trace.Cancel();
            trace.Cancel();
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var intervals = Read(temp.Path).Where(r => Operation(r).Contains("draw_submission", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, intervals.Length);
        Assert.All(intervals, r => Assert.Equal("cancelled", r.GetProperty("status").GetString()));
    }

    /// <summary>Disabled input and draw bookkeeping allocate no per-edit objects after warmup.</summary>
    [Fact]
    public void Disabled_trace_hot_path_has_zero_managed_allocations()
    {
        Assert.False(MoteTelemetry.Health.Enabled);
        var trace = new NativeDrawTrace();
        Exercise(trace, 100);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Exercise(trace, 10_000);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.False(trace.IsPending);
    }

    /// <summary>Closing before an asynchronous open callback is pumped cancels its request-owned interval.</summary>
    [Fact]
    public async Task Controller_dispose_closes_pending_open_before_writer_shutdown()
    {
        using var temp = new RepoTemp();
        Configure(temp.Path);
        try
        {
            using var controller = Controller(temp.Path);
            PrivateMethod("StartOpen").Invoke(controller, [temp.File("missing-private-SECRET.md")]);
            controller.Dispose();
            controller.Dispose();
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        var open = Assert.Single(records, r => Operation(r) == "document.open_to_editable");
        Assert.Equal("cancelled", open.GetProperty("status").GetString());
        Assert.DoesNotContain("SECRET", string.Join('\n', records.Select(r => r.GetRawText())));
    }

    /// <summary>An edit replaced before debounce gets cancelled once rather than silently disappearing.</summary>
    [Fact]
    public async Task Controller_cancels_replaced_semantic_parent()
    {
        using var temp = new RepoTemp();
        Configure(temp.Path);
        try
        {
            using var controller = Controller(temp.Path);
            PrivateMethod("ScheduleAnalysis").Invoke(controller, [MoteTelemetry.Mark(), false]);
            PrivateMethod("ScheduleAnalysis").Invoke(controller, [MoteTelemetry.Mark(), false]);
            controller.Dispose();
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path).Where(r => Operation(r) == "document.edit_to_presentation").ToArray();
        Assert.Equal(2, records.Length);
        Assert.All(records, r => Assert.Equal("cancelled", r.GetProperty("status").GetString()));
    }

    /// <summary>The parser's scope records failure or cancellation before disposal, never implicit success.</summary>
    [Theory]
    [InlineData(false, "failure")]
    [InlineData(true, "cancelled")]
    public async Task Parser_exception_status_is_truthful(bool cancel, string status)
    {
        using var temp = new RepoTemp();
        Configure(temp.Path);
        try
        {
            using var document = new Document("synthetic");
            var method = typeof(NativeEditorController).GetMethod("AnalyzeTraced",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null,
                [new ThrowingPolicy(cancel), "synthetic", CancellationToken.None,
                 MoteTelemetry.Mark(), document.Snapshot]));
            Assert.IsAssignableFrom<Exception>(error.InnerException);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var parse = Assert.Single(Read(temp.Path), r => Operation(r) == "analysis.parse");
        Assert.Equal(status, parse.GetProperty("status").GetString());
    }

    /// <summary>Analysis failure ends its edit parent on the UI callback, before a later disposal can relabel it.</summary>
    [Fact]
    public async Task Controller_failed_analysis_finishes_parent_as_failure()
    {
        using var temp = new RepoTemp();
        Configure(temp.Path);
        try
        {
            using var controller = Controller(temp.Path);
            var driverField = typeof(NativeEditorController).GetField("_sessionDriver", BindingFlags.NonPublic | BindingFlags.Instance)!;
            ((IDisposable?)driverField.GetValue(controller))?.Dispose();
            driverField.SetValue(controller, null);
            typeof(NativeEditorController).GetField("_policy", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(controller, new ThrowingPolicy(false));
            var shell = typeof(NativeEditorController).GetField("_shell", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(controller)!;
            PrivateMethod("ScheduleAnalysis").Invoke(controller, [MoteTelemetry.Mark(), false]);
            var pump = shell.GetType().GetMethod("Pump")!;
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(5))
            {
                pump.Invoke(shell, null);
                var analysis = (NativeAnalysisView?)shell.GetType().GetProperty("Analysis")!.GetValue(shell);
                if (analysis?.DiagnosticsSummary == "Analysis failed.") break;
                await Task.Delay(10);
            }
            Assert.Equal("Analysis failed.", ((NativeAnalysisView)shell.GetType().GetProperty("Analysis")!.GetValue(shell)!).DiagnosticsSummary);
            controller.Dispose();
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var parent = Assert.Single(Read(temp.Path), r => Operation(r) == "document.edit_to_presentation");
        Assert.Equal("failure", parent.GetProperty("status").GetString());
    }

    /// <summary>
    /// Exercises actual Win32 source callbacks in an owned synthetic window,
    /// with no document file, save, clipboard, IME, capture, or compositor claim.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Windows_native_source_draw_matches_installed_revision(bool canvas)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var temp = new RepoTemp();
        Configure(temp.Path);
        try
        {
            var shell = new WindowsEditorShell(experimentalCanvas: canvas);
            using var document = new Document("synthetic source");
            Exception? callbackFailure = null;
            shell.Shown += () =>
            {
                if (!OperatingSystem.IsWindows()) return;
                try
                {
                    var mark = MoteTelemetry.Mark();
                    document.Apply(new TextChange(0, 0, "accepted "));
                    var snapshot = document.Snapshot;
                    var stamp = new NativeDocumentStamp(17, snapshot.Version);
                    shell.TraceSourceDraw(stamp, mark, new(Version: snapshot.Version));
                    nint surface;
                    if (canvas)
                    {
                        var frame = new CanvasInteraction(snapshot, 20, 600).Frame();
                        shell.SetCanvasBinding(new NativeCanvasBinding(stamp.Generation, stamp.Version, 1,
                            snapshot, frame, 0, snapshot.GetText(), 0, 0, "mote trace test", "", true));
                        var island = (WindowsRichEditIsland)typeof(WindowsEditorShell)
                            .GetField("_canvasIsland", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
                        surface = (nint)typeof(WindowsRichEditIsland)
                            .GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(island)!;
                    }
                    else
                    {
                        shell.SetDocument(new NativeDocumentView("mote trace test", snapshot.GetText(),
                            0, snapshot.Length, true, "", stamp));
                        surface = (nint)typeof(WindowsEditorShell)
                            .GetField("_editor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
                    }
                    Assert.NotEqual(0, surface);
                    Win32.InvalidateRect(surface, 0, false);
                    Win32.UpdateWindow(surface);
                }
                catch (Exception error) { callbackFailure = error; }
                finally { shell.Close(); }
            };
            shell.Run();
            shell.CancelSourceDrawTrace();
            Assert.Null(callbackFailure);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var draw = Assert.Single(Read(temp.Path), r => Operation(r) == "document.edit_to_draw_submission");
        Assert.Equal("success", draw.GetProperty("status").GetString());
        Assert.Equal(1, draw.GetProperty("attributes").GetProperty("version").GetInt64());
    }

    /// <summary>A discriminating synthetic failure; no exception text is eligible for trace dimensions.</summary>
    private sealed class ThrowingPolicy(bool cancel) : IDocumentPolicy
    {
        /// <inheritdoc />
        public DocumentKind Kind => DocumentKind.PlainText;
        /// <inheritdoc />
        public string DisplayName => "synthetic";
        /// <inheritdoc />
        public FormatAnalysis Analyze(string text, CancellationToken cancellationToken = default) =>
            throw (cancel ? new OperationCanceledException() : new InvalidOperationException("SECRET-parser-detail"));
        /// <inheritdoc />
        public string Format(string text) => text;
        /// <inheritdoc />
        public string RenderHtml(FormatAnalysis analysis) => string.Empty;
    }

    /// <summary>Exercises the exact disabled edit-to-draw bookkeeping without timing-dependent assertions.</summary>
    private static void Exercise(NativeDrawTrace trace, int count)
    {
        for (var i = 0; i < count; i++)
        {
            trace.Arm(TelemetryOperation.EditToDrawSubmission, MoteTelemetry.Fork(MoteTelemetry.Mark()), 1, i);
            trace.ObserveDocument(1, i);
            trace.CompleteDraw(trace.BeginDraw(1, i), 1, i);
        }
    }

    /// <summary>Reuses the established UI-free fake rather than inventing a second controller test shell.</summary>
    private static NativeEditorController Controller(string home)
    {
        var shellType = typeof(NativeControllerTests).GetNestedType("FakeShell", BindingFlags.NonPublic)!;
        var shell = (INativeEditorShell)Activator.CreateInstance(shellType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [NativeLineEndingMode.Preserve], null)!;
        var configuration = MoteConfigLoader.Load(new MoteConfigLoadOptions
        { UserHomeDirectory = home, UseEnvironmentOverride = false });
        return new NativeEditorController(shell, configuration, ThemePolicies.Get(configuration.ThemeId), null);
    }

    /// <summary>Invokes orchestration without pumping a platform loop; reflection is test-only, not shipped.</summary>
    private static MethodInfo PrivateMethod(string name) =>
        typeof(NativeEditorController).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>Enables isolated traces below the repository test scratch directory.</summary>
    private static void Configure(string directory) => MoteTelemetry.Configure(new TelemetryOptions
    { Enabled = true, OutputDirectory = directory });

    /// <summary>Returns independent JSON values after the writer has drained.</summary>
    private static JsonElement[] Read(string directory) => Directory.GetFiles(directory, "*.jsonl")
        .SelectMany(File.ReadLines).Select(line =>
        { using var document = JsonDocument.Parse(line); return document.RootElement.Clone(); }).ToArray();

    /// <summary>Reads only the fixed operation name.</summary>
    private static string Operation(JsonElement record) => record.GetProperty("operation").GetString()!;
}
