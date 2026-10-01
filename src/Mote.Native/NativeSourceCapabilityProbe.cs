using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Mote.Configuration;
using Mote.Engine;
using Mote.Formats;
using Mote.Telemetry;
using Mote.Themes;

namespace Mote.Native;

/// <summary>Hosted-only full-resident native experiment; never selects a product editing profile.</summary>
internal static class NativeSourceCapabilityProbe
{
    /// <summary>Runs on the entry-point STA/main thread; rejects admission before any writer or native object exists.</summary>
    internal static int Run(string output)
    {
        if (!Hosted()) return 3;
        string directory;
        try { directory = AdmitDirectory(output); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return 2; }
        PhaseReport report;
        try { Directory.CreateDirectory(directory); report = new PhaseReport(Path.Combine(directory, "report.jsonl")); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return 2; }
        using var ownedReport = report;
        try
        {
            Require(Environment.ProcessPath is not null, "executable-identity-unavailable");
            report.Note("executable", "identity", 0, 0, Hash(File.ReadAllBytes(Environment.ProcessPath!)));
            report.Note("runtime-architecture", RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), 0, 0);
            var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
            {
                MoteHomeOverride = Path.Combine(directory, "mote-home"), UseEnvironmentOverride = false
            });
            MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = config.TraceDirectory });
            var theme = ThemePolicies.Resolve(config.ThemeId, prefersDark: true);
            for (var index = 0; index < 3; index++) RunFixture(index, directory, report, theme);
            var health = MoteTelemetry.Health;
            Require(health.Enabled && !health.SinkFaulted && health.DroppedRecords == 0, "telemetry-health-failed");
            report.Note("telemetry-health", "healthy", 0, 0);
            report.Note("probe", "complete", 0, 0);
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            report.Failure("probe", ex);
            return 1;
        }
        finally { MoteTelemetry.ShutdownAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
    }

    /// <summary>No environment variable enables this diagnostic on ordinary user machines.</summary>
    private static bool Hosted() => Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" &&
        (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("RUNNER_OS") == "Windows" ||
         OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("RUNNER_OS") == "macOS");

    /// <summary>Requires a new repository-contained destination and refuses linked existing ancestors.</summary>
    private static string AdmitDirectory(string output)
    {
        var root = Path.GetFullPath(Environment.CurrentDirectory);
        Require(Directory.Exists(Path.Combine(root, ".git")), "repository-root-required");
        var path = Path.GetFullPath(output);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Require(new[] { ".cache", ".temp" }.Any(area => path.StartsWith(
            Path.Combine(root, area) + Path.DirectorySeparatorChar, comparison)), "artifact-boundary-invalid");
        Require(!File.Exists(path) && !Directory.Exists(path), "artifact-already-exists");
        for (var ancestor = new DirectoryInfo(path); ancestor is not null; ancestor = ancestor.Parent)
        {
            if (!ancestor.Exists) { Require(!File.Exists(ancestor.FullName), "artifact-ancestor-file"); continue; }
            Require((ancestor.Attributes & FileAttributes.ReparsePoint) == 0 && ancestor.LinkTarget is null, "artifact-ancestor-linked");
        }
        return path;
    }

    /// <summary>Owns one fixture and native view at a time; no resident queue of generated documents exists.</summary>
    private static void RunFixture(int index, string directory, PhaseReport report, IThemePolicy theme)
    {
        report.Fixture = index switch { 0 => "mixed-text", 1 => "novel-text", _ => "dense-json" };
        var fixture = report.Measure("fixture-generation", TelemetryOperation.ViewLayout,
            () => NativeSourceDiagnosticFixtures.Create(index));
        var inputBytes = Encoding.UTF8.GetBytes(fixture.Text);
        var input = Path.Combine(directory, fixture.Id + ".input");
        WriteNew(input, inputBytes);
        report.Note("fixture", "ready", inputBytes.Length, fixture.Text.Length, Hash(inputBytes));
        using var document = report.Measure("engine-open", TelemetryOperation.DocumentOpen,
            () => Document.OpenAsync(input).GetAwaiter().GetResult());
        Require(document.Snapshot.GetText() == fixture.Text && !document.CanUndo, "engine-open-mismatch");
        var mode = OperatingSystem.IsWindows() ? NativeLineEndingMode.CrLf : NativeLineEndingMode.Preserve;
        var binding = report.Measure("projection", TelemetryOperation.ViewLayout,
            () => new NativeSourceDiagnosticBinding(document.Snapshot, 1, 1, mode, fixture.EditOffset, fixture.EditOffset));
        using var host = report.Measure("native-host-create", TelemetryOperation.ViewLayout,
            () => CreateHost(theme));
        var backend = host.LayoutBackend;
        Require(OperatingSystem.IsWindows() ? backend == "RichEdit50W" : backend is "textkit1" or "textkit2", "backend-unknown");
        report.Note("backend", backend, 0, 0);
        try
        {
            Install(host, binding, report);
            VerifyScroll(host, binding, report);
            Publish(host, binding, fixture.Policy, theme, document, report);
            binding = Edit(host, binding, document, fixture, report);
            Publish(host, binding, fixture.Policy, theme, document, report);
            var finalText = fixture.Text.Insert(fixture.EditOffset, NativeSourceDiagnosticFixtures.Insertion);
            Require(document.Snapshot.GetText() == finalText && document.CanUndo && !document.CanRedo, "edited-source-mismatch");
            binding = Restore(host, binding, document, true, mode, fixture.EditOffset, report);
            Require(document.Snapshot.GetText() == fixture.Text, "undo-source-mismatch");
            Publish(host, binding, fixture.Policy, theme, document, report);
            binding = Restore(host, binding, document, false, mode,
                fixture.EditOffset + NativeSourceDiagnosticFixtures.Insertion.Length, report);
            Require(document.Snapshot.GetText() == finalText, "redo-source-mismatch");
            Publish(host, binding, fixture.Policy, theme, document, report);
            Save(document, finalText, directory, report);
        }
        finally { binding.Dispose(); }
    }

    /// <summary>Explicit OS checks preserve the platform API contract without suppressing analyzer evidence.</summary>
    private static INativeSourceDiagnosticHost CreateHost(IThemePolicy theme)
    {
        if (OperatingSystem.IsWindows()) return Windows.WindowsNativeSourceCapabilityProbe.Create(theme);
        if (OperatingSystem.IsMacOS()) return Mac.MacNativeSourceCapabilityProbe.Create(theme);
        throw new PlatformNotSupportedException();
    }

    /// <summary>Import and complete readback are separate intervals; selection uses global display boundaries.</summary>
    private static void Install(INativeSourceDiagnosticHost host, NativeSourceDiagnosticBinding binding, PhaseReport report)
    {
        report.Measure("native-import", TelemetryOperation.ViewLayout, () => host.Install(binding.Projection.Display));
        var readback = report.Measure("native-readback", TelemetryOperation.ViewLayout, host.ReadText);
        report.Note("import-readback-identity", "observed", Encoding.UTF8.GetByteCount(readback), readback.Length,
            Hash(Encoding.UTF8.GetBytes(readback)));
        Require(binding.CertifyInstalled(readback), "import-mismatch");
        report.Measure("import-draw-return", TelemetryOperation.ViewPaint, host.FlushDraw);
        var anchor = binding.Projection.ToDisplay(binding.Anchor);
        var active = binding.Projection.ToDisplay(binding.Active);
        host.SetSelection(binding.Projection.Display, anchor, active);
        Require(host.ReadSelection(binding.Projection.Display) == new NativeSourceDiagnosticRange(
            Math.Min(anchor, active), Math.Abs(active - anchor)), "selection-mismatch");
    }

    /// <summary>Records a scroll tuple, not distant pixels, while certifying unchanged text and selection.</summary>
    private static void VerifyScroll(INativeSourceDiagnosticHost host, NativeSourceDiagnosticBinding binding, PhaseReport report)
    {
        var display = binding.Projection.Display;
        var selection = host.ReadSelection(display);
        var before = host.CaptureViewport();
        report.Measure("scroll-draw-return", TelemetryOperation.ViewPaint, () =>
        {
            host.ScrollToEnd(display);
            host.FlushDraw();
            host.RestoreViewport(before);
            host.FlushDraw();
        });
        var after = host.CaptureViewport();
        report.Viewport(before, after);
        Require(before == after && host.ReadText() == display && host.ReadSelection(display) == selection, "scroll-state-mismatch");
    }

    /// <summary>Full policy analysis and all-token attribute publication are independently timed, including verification.</summary>
    private static void Publish(INativeSourceDiagnosticHost host, NativeSourceDiagnosticBinding binding,
        IDocumentPolicy policy, IThemePolicy theme, Document document, PhaseReport report)
    {
        var snapshot = document.Snapshot;
        var analysis = report.Measure("full-policy-analysis", TelemetryOperation.AnalysisParse,
            () => policy.Analyze(binding.Projection.Source));
        report.Note("semantic-counts", "observed", analysis.Tokens.Count, analysis.Diagnostics.Count);
        Require(analysis.SourceText == binding.Projection.Source && analysis.Diagnostics.Count == 0, "analysis-mismatch");
        var styles = report.Measure("semantic-token-projection", TelemetryOperation.AnalysisSemantic,
            () => analysis.Tokens.Select(token => new NativeSourceDiagnosticStyle(
                binding.Projection.ToDisplay(token.Span.Start),
                binding.Projection.ToDisplay(token.Span.End) - binding.Projection.ToDisplay(token.Span.Start),
                theme.SemanticColor(token.Kind))).ToArray());
        var selection = host.ReadSelection(binding.Projection.Display);
        var viewport = host.CaptureViewport();
        var undo = document.CanUndo;
        var redo = document.CanRedo;
        report.Measure("semantic-publication-verified", TelemetryOperation.AnalysisPublish, () =>
        {
            host.PublishStyles(binding.Projection.Display, styles);
            Require(host.ReadText() == binding.Projection.Display && host.ReadSelection(binding.Projection.Display) == selection, "style-text-selection-mismatch");
            Require(host.CaptureViewport() == viewport && ReferenceEquals(snapshot, document.Snapshot), "style-scroll-version-mismatch");
            Require(document.CanUndo == undo && document.CanRedo == redo, "style-history-mismatch");
        });
        report.Measure("semantic-draw-return", TelemetryOperation.ViewPaint, host.FlushDraw);
        report.Note("semantic-counts", "complete", analysis.Tokens.Count, analysis.Diagnostics.Count);
    }

    /// <summary>Native insertion includes adapter readback cost; reconciliation admits exactly one canonical change.</summary>
    private static NativeSourceDiagnosticBinding Edit(INativeSourceDiagnosticHost host, NativeSourceDiagnosticBinding binding,
        Document document, NativeSourceDiagnosticFixture fixture, PhaseReport report)
    {
        var old = binding;
        var version = document.Snapshot.Version;
        var mutations = 0;
        document.ChangedRange += Count;
        try
        {
            report.Measure("native-controlled-insert", TelemetryOperation.DocumentEdit,
                () => host.Insert(NativeSourceDiagnosticFixtures.Insertion));
            Require(document.Snapshot.Version == version && mutations == 0, "native-mutated-engine");
            report.Measure("edit-draw-return", TelemetryOperation.ViewPaint, host.FlushDraw);
            var display = report.Measure("post-edit-readback", TelemetryOperation.ViewLayout, host.ReadText);
            var range = host.ReadSelection(display);
            report.Note("post-edit-native-range", "observed", range.Start, range.Length);
            var edit = report.Measure("reconcile-map-diff", TelemetryOperation.DocumentEdit,
                () => old.Reconcile(document, old.Stamp, old.InstallationNonce, display, range));
            Require(edit.Outcome == NativeSourceDiagnosticEditOutcome.Applied && edit.Binding is not null, "edit-admission-mismatch");
            Require(edit.Change == new TextChange(fixture.EditOffset, 0, NativeSourceDiagnosticFixtures.Insertion), "exact-edit-mismatch");
            binding = edit.Binding!;
            report.Note("commit-counts", "observed", mutations, checked((int)(document.Snapshot.Version - version)));
            report.Note("post-edit-source-range", "observed", binding.Anchor, binding.Active);
            Require(mutations == 1 && document.Snapshot.Version == version + 1 && binding.Projection.Display == display, "commit-count-mismatch");
            var expectedActive = fixture.EditOffset + NativeSourceDiagnosticFixtures.Insertion.Length;
            Require(binding.Anchor == expectedActive && binding.Active == expectedActive, "source-selection-mismatch");
            Require(host.ReadSelection(display) == new NativeSourceDiagnosticRange(
                binding.Projection.ToDisplay(expectedActive), 0), "display-selection-mismatch");
            Require(old.Reconcile(document, old.Stamp, old.InstallationNonce, display, range).Outcome == NativeSourceDiagnosticEditOutcome.Stale, "retired-admission-mismatch");
            Require(binding.Reconcile(document, old.Stamp, old.InstallationNonce, display, range).Outcome == NativeSourceDiagnosticEditOutcome.Stale, "stamp-admission-mismatch");
            Require(binding.Reconcile(document, binding.Stamp, binding.InstallationNonce + 1, display, range).Outcome == NativeSourceDiagnosticEditOutcome.Stale, "nonce-admission-mismatch");
            var echo = binding.Reconcile(document, binding.Stamp, binding.InstallationNonce, display, range);
            Require(echo.Outcome == NativeSourceDiagnosticEditOutcome.NoChange && echo.Binding is not null && mutations == 1, "echo-admission-mismatch");
            binding.Dispose();
            return echo.Binding!;
        }
        finally { old.Dispose(); document.ChangedRange -= Count; }
        void Count(object? sender, DocumentChangedRangeEventArgs args) => mutations++;
    }

    /// <summary>Engine history drives explicit reinstallation with a new nonce; native undo is not substituted.</summary>
    private static NativeSourceDiagnosticBinding Restore(INativeSourceDiagnosticHost host, NativeSourceDiagnosticBinding old,
        Document document, bool undo, NativeLineEndingMode mode, int selection, PhaseReport report)
    {
        report.Measure(undo ? "engine-undo" : "engine-redo", TelemetryOperation.DocumentEdit,
            () => Require(undo ? document.Undo() : document.Redo(), "engine-history-unavailable"));
        var binding = report.Measure("history-projection", TelemetryOperation.ViewLayout,
            () => new NativeSourceDiagnosticBinding(document.Snapshot, 1, old.InstallationNonce + 1, mode, selection, selection));
        old.Dispose();
        Install(host, binding, report);
        return binding;
    }

    /// <summary>Exact saved bytes and a fresh engine document are proved; no independent process is claimed.</summary>
    private static void Save(Document document, string expected, string directory, PhaseReport report)
    {
        var path = Path.Combine(directory, report.Fixture + ".saved");
        report.Measure("save-new-path", TelemetryOperation.Save,
            () => document.SaveAsync(path).GetAwaiter().GetResult());
        var bytes = File.ReadAllBytes(path);
        report.Note("saved-bytes", "observed", bytes.Length, expected.Length, Hash(bytes));
        Require(bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(expected)) && !document.IsModified, "save-bytes-mismatch");
        report.Note("saved-bytes", "exact", bytes.Length, expected.Length, Hash(bytes));
        using var reopened = report.Measure("fresh-document-reopen", TelemetryOperation.DocumentOpen,
            () => Document.OpenAsync(path).GetAwaiter().GetResult());
        Require(reopened.Snapshot.GetText() == expected && !reopened.CanUndo && !reopened.IsModified, "reopen-source-mismatch");
    }

    /// <summary>Creates only new artifacts; an existing target is never overwritten.</summary>
    private static void WriteNew(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
        stream.Flush(true);
    }

    /// <summary>Content identity without exposing generated text.</summary>
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    /// <summary>Unknown or mismatched evidence fails rather than selecting a substitute implementation.</summary>
    private static void Require(bool condition, string code) { if (!condition) throw new ProbeFailure(code); }

    /// <summary>Only source-defined invariant identifiers may enter diagnostic evidence.</summary>
    private sealed class ProbeFailure(string code) : Exception
    {
        /// <summary>Closed source-defined failure, not an OS or user supplied message.</summary>
        internal string Code { get; } = code;
    }

    /// <summary>Explicit AOT-safe JSONL phases; durable entry records survive a later native hang.</summary>
    private sealed class PhaseReport : IDisposable
    {
        /// <summary>Exclusively created durable append-only phase artifact.</summary>
        private readonly FileStream _stream;
        /// <summary>Closed generated fixture identity, never a user path.</summary>
        internal string Fixture { get; set; } = "probe";
        /// <summary>Creates a new artifact without overwriting earlier evidence.</summary>
        internal PhaseReport(string path) => _stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        /// <summary>Measures synchronous work without a synthetic result.</summary>
        internal void Measure(string phase, TelemetryOperation operation, Action action) =>
            Measure(phase, operation, () => { action(); return true; });
        /// <summary>Times actual work; evidence serialization occurs outside its interval.</summary>
        internal T Measure<T>(string phase, TelemetryOperation operation, Func<T> action)
        {
            using var scope = MoteTelemetry.Start(operation);
            var trace = Activity.Current;
            Write(phase, "entered", null, null, trace);
            var start = Stopwatch.GetTimestamp();
            var allocated = GC.GetTotalAllocatedBytes(false);
            try
            {
                var value = action();
                Write(phase, "completed", Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                    GC.GetTotalAllocatedBytes(false) - allocated, trace);
                return value;
            }
            catch (Exception ex)
            {
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var allocation = GC.GetTotalAllocatedBytes(false) - allocated;
                scope?.SetStatus(TelemetryStatus.Failure);
                Failure(phase, ex);
                Write(phase, "failed", elapsed, allocation, trace);
                throw;
            }
        }
        /// <summary>Serializes closed failures and numeric codes; exception messages and paths are never inspected.</summary>
        internal void Failure(string phase, Exception error) => Emit(writer =>
        {
            Header(writer, phase, "failure-evidence");
            writer.WriteString("failure_code", error is ProbeFailure failure ? failure.Code : error switch
            {
                IOException => "io-failure", UnauthorizedAccessException => "access-denied",
                NotSupportedException => "unsupported", ArgumentException => "invalid-argument",
                OperationCanceledException => "cancelled", OutOfMemoryException => "out-of-memory",
                _ => "runtime-failure"
            });
            writer.WriteNumber("hresult", error.HResult);
        });

        /// <summary>Records content-free counts and optional byte identity.</summary>
        internal void Note(string phase, string state, int bytes, int characters, string? hash = null) =>
            Emit(writer => { Header(writer, phase, state); writer.WriteNumber("count_a", bytes);
                writer.WriteNumber("count_b", characters); if (hash is not null) writer.WriteString("sha256", hash); });
        /// <summary>Records owned-control scroll observations without pixel claims.</summary>
        internal void Viewport(NativeSourceDiagnosticViewport before, NativeSourceDiagnosticViewport after) => Emit(writer =>
        {
            Header(writer, "scroll-observation", "observed");
            writer.WriteNumber("before_x", before.Horizontal); writer.WriteNumber("before_y", before.Vertical);
            writer.WriteNumber("before_line", before.FirstVisibleLine);
            writer.WriteNumber("after_x", after.Horizontal); writer.WriteNumber("after_y", after.Vertical);
            writer.WriteNumber("after_line", after.FirstVisibleLine);
        });
        /// <summary>Records measured terminal facts and exact optional Activity context.</summary>
        private void Write(string phase, string state, double? milliseconds, long? allocation, Activity? activity) => Emit(writer =>
        {
            Header(writer, phase, state);
            if (milliseconds is { } duration) writer.WriteNumber("duration_ms", duration); else writer.WriteNull("duration_ms");
            if (allocation is { } bytes) writer.WriteNumber("allocated_bytes", bytes); else writer.WriteNull("allocated_bytes");
            var workingSet = Environment.WorkingSet;
            if (workingSet > 0) writer.WriteNumber("working_set_bytes", workingSet); else writer.WriteNull("working_set_bytes");
            if (activity is not null) { writer.WriteString("trace_id", activity.TraceId.ToHexString());
                writer.WriteString("span_id", activity.SpanId.ToHexString()); }
        });
        /// <summary>Writes fixed context shared by every diagnostic record.</summary>
        private void Header(Utf8JsonWriter writer, string phase, string state)
        { writer.WriteString("fixture", Fixture); writer.WriteString("phase", phase); writer.WriteString("state", state); }
        /// <summary>Flushes each JSON record durably before proceeding to native work.</summary>
        private void Emit(Action<Utf8JsonWriter> fields)
        {
            using (var writer = new Utf8JsonWriter(_stream)) { writer.WriteStartObject(); fields(writer); writer.WriteEndObject(); }
            _stream.WriteByte((byte)'\n'); _stream.Flush(true);
        }
        /// <summary>Closes only the owned phase stream, retaining all evidence.</summary>
        public void Dispose() => _stream.Dispose();
    }
}
