using System.Reflection;
using System.Text.Json;
using Mote.Configuration;
using Mote.Engine;
using Mote.Native;
using Mote.Telemetry;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Checks native Save causality against actual disk effects and deterministic worker boundaries.</summary>
[Collection("Telemetry")]
public sealed class NativeSaveRequestTests
{
    /// <summary>The terminal version is the saved snapshot, not a newer edit received before UI completion.</summary>
    [Fact]
    public async Task Save_as_links_request_engine_phases_and_exact_captured_version()
    {
        using var temp = new RepoTemp();
        var target = temp.File("SECRET-target.txt");
        Configure(temp.Path);
        try
        {
            var shell = Shell();
            Set(shell, "SavePath", target);
            using var controller = Controller(shell, temp.Path);
            var document = Document(controller);
            document.Apply(new TextChange(0, 0, "SECRET-first"));
            var captured = document.Snapshot.Version;
            using var gate = new CommitGate();
            document.SaveOperations = gate;
            var request = NativeSaveRequest.Receive(NativeSaveKind.SaveAs);
            request.Dispatch(r => Invoke(controller, "StartSave", r));
            try
            {
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                document.Apply(new TextChange(document.Snapshot.Length, 0, "SECRET-later"));
            }
            finally { gate.Release.Set(); }
            await WaitPosted(shell);
            Pump(shell);
            Assert.False((bool)Field(controller, "_saving")!);
            Assert.Equal("SECRET-first", await File.ReadAllTextAsync(target));
            Assert.True(document.IsModified);
            Assert.True(document.Snapshot.Version > captured);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        var receipt = Receipt(records);
        var terminal = Terminal(records, "command.save_as", "success", "completed");
        AssertParent(receipt, terminal);
        AssertVersion(terminal, 1);
        var save = One(records, "document.save");
        var saveEntry = One(records, "document.save.entered");
        AssertParent(receipt, saveEntry);
        AssertParent(saveEntry, save);
        AssertVersion(save, 1);
        foreach (var phase in new[] { "gate_wait", "snapshot_capture", "target_check", "temp_encode_write",
            "temp_flush", "temp_hash", "commit_move", "saved_stamp", "bookkeeping" })
        {
            var record = One(records, "save." + phase);
            Assert.Equal("success", record.GetProperty("status").GetString());
            var entry = One(records, "save." + phase + ".entered");
            AssertParent(saveEntry, entry);
            AssertParent(entry, record);
        }
        foreach (var name in new[] { "save.controller_entered", "save.composition_settled", "save.admitted",
            "save.worker_started", "save.snapshot_captured", "save.ui_post_returned", "save.ui_started", "save.completed" })
            AssertParent(receipt, One(records, name));
        AssertVersion(One(records, "save.completed"), 1);
        Privacy(records);
    }

    /// <summary>Commands rejected before admission never pretend that an engine Save ran.</summary>
    [Theory]
    [InlineData("composition", "skipped", "composition_blocked")]
    [InlineData("picker", "cancelled", "picker_cancelled")]
    [InlineData("missing-handler", "skipped", "missing_handler")]
    public async Task Rejected_receipts_have_one_terminal_and_no_worker(string mode, string status, string reason)
    {
        using var temp = new RepoTemp();
        Configure(temp.Path);
        try
        {
            var shell = Shell();
            using var controller = Controller(shell, temp.Path);
            if (mode == "composition") Set(shell, "VetoPendingCommit", true);
            var request = NativeSaveRequest.Receive(NativeSaveKind.Save);
            request.Dispatch(mode == "missing-handler" ? null : r => Invoke(controller, "StartSave", r));
            Assert.False((bool)Field(controller, "_saving")!);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        AssertParent(Receipt(records), Terminal(records, "command.save", status, reason));
        Assert.DoesNotContain(records, r => Op(r) is "save.admitted" or "save.worker_started" or "document.save" or "save.completed");
        Privacy(records);
    }

    /// <summary>A second command cannot release the admitted command's busy state or borrow its identity.</summary>
    [Fact]
    public async Task Already_saving_is_a_distinct_request_without_second_worker()
    {
        using var temp = new RepoTemp();
        Configure(temp.Path);
        try
        {
            var shell = Shell();
            Set(shell, "SavePath", temp.File("SECRET-target.txt"));
            using var controller = Controller(shell, temp.Path);
            Document(controller).Apply(new TextChange(0, 0, "SECRET-source"));
            using var gate = new CommitGate();
            Document(controller).SaveOperations = gate;
            Invoke(controller, "StartSave", NativeSaveRequest.Receive(NativeSaveKind.SaveAs));
            try
            {
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Invoke(controller, "StartSave", NativeSaveRequest.Receive(NativeSaveKind.Save));
                Assert.True((bool)Field(controller, "_saving")!);
            }
            finally { gate.Release.Set(); }
            await WaitPosted(shell);
            Pump(shell);
            Assert.False((bool)Field(controller, "_saving")!);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        var receipts = records.Where(r => Op(r) is "command.save.received" or "command.save_as.received").ToArray();
        Assert.Equal(2, receipts.Length);
        Assert.NotEqual(Id(receipts[0]), Id(receipts[1]));
        AssertParent(receipts[0], Terminal(records, "command.save_as", "success", "completed"));
        AssertParent(receipts[1], Terminal(records, "command.save", "skipped", "already_saving"));
        One(records, "document.save");
        One(records, "save.admitted");
        Privacy(records);
    }

    /// <summary>Cancellation, filesystem failure, and deferred rendering are observably different outcomes.</summary>
    [Theory]
    [InlineData("overwrite", "cancelled", "overwrite_declined")]
    [InlineData("failure", "failure", "save_failed")]
    [InlineData("deferred", "success", "view_deferred")]
    public async Task Save_outcome_matches_disk_and_ui_effects(string mode, string status, string reason)
    {
        using var temp = new RepoTemp();
        var target = temp.File("SECRET-target.txt");
        if (mode == "overwrite") await File.WriteAllTextAsync(target, "SECRET-original");
        Configure(temp.Path);
        try
        {
            var shell = Shell();
            Set(shell, "SavePath", target);
            Set(shell, "OverwriteApproved", false);
            using var controller = Controller(shell, temp.Path);
            Document(controller).Apply(new TextChange(0, 0, "SECRET-new"));
            if (mode == "failure") Document(controller).SaveOperations = new FailingMove();
            Invoke(controller, "StartSave", NativeSaveRequest.Receive(NativeSaveKind.SaveAs));
            await WaitPosted(shell);
            if (mode == "deferred") Set(shell, "VetoPendingCommit", true);
            Pump(shell);
            if (mode == "overwrite") { await WaitPosted(shell); Pump(shell); }
            Assert.False((bool)Field(controller, "_saving")!);
            if (mode == "failure")
            {
                Assert.False(File.Exists(target));
                Assert.NotEmpty((IEnumerable<string>)Get(shell, "Errors")!);
                Assert.True(Document(controller).IsModified);
            }
            else Assert.Equal(mode == "overwrite" ? "SECRET-original" : "SECRET-new", await File.ReadAllTextAsync(target));
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        AssertParent(Receipt(records), Terminal(records, "command.save_as", status, reason));
        Assert.Equal(mode == "deferred" ? 1 : 0, records.Count(r => Op(r) == "save.completed"));
        if (mode == "deferred") One(records, "save.ui_deferred");
        if (mode == "failure")
        {
            Assert.Equal("failure", One(records, "save.commit_move").GetProperty("status").GetString());
            Assert.DoesNotContain(records, r => Op(r) == "save.failure_cleanup");
            One(records, "save.failure_inspection");
        }
        Privacy(records);
    }

    /// <summary>Late completion cannot revive an ended intent or publish into a replacement document.</summary>
    [Theory]
    [InlineData(false, "lifetime_ended")]
    [InlineData(true, "stale_document")]
    public async Task Retired_intent_stays_terminal_when_queued_completion_arrives(bool replace, string reason)
    {
        using var temp = new RepoTemp();
        Configure(temp.Path);
        try
        {
            var shell = Shell();
            Set(shell, "SavePath", temp.File("SECRET-target.txt"));
            using var controller = Controller(shell, temp.Path);
            Document(controller).Apply(new TextChange(0, 0, "SECRET-old"));
            Invoke(controller, "StartSave", NativeSaveRequest.Receive(NativeSaveKind.SaveAs));
            await WaitPosted(shell);
            if (replace)
            {
                var replacement = new Document();
                replacement.Apply(new TextChange(0, 0, "SECRET-replacement"));
                Invoke(controller, "ReplaceDocument", replacement, default(TelemetryMark), 0);
            }
            else controller.Dispose();
            var presentation = Get(shell, "Document");
            Pump(shell);
            Assert.Same(presentation, Get(shell, "Document"));
            if (replace) Assert.Equal("SECRET-replacement", Document(controller).Snapshot.GetText());
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        Terminal(records, "command.save_as", "cancelled", reason);
        Assert.Equal("success", One(records, "document.save").GetProperty("status").GetString());
        One(records, "save.ui_started");
        Assert.DoesNotContain(records, r => Op(r) == "save.completed");
        Privacy(records);
    }

    /// <summary>Native callbacks may replace the document; each reentrant boundary must revoke old intent.</summary>
    [Theory]
    [InlineData("settlement")]
    [InlineData("presentation")]
    public async Task Reentrant_replacement_never_certifies_old_request_success(string boundary)
    {
        using var temp = new RepoTemp();
        Configure(temp.Path);
        try
        {
            var shell = Shell();
            Set(shell, "SavePath", temp.File("SECRET-reentrant.txt"));
            using var controller = Controller(shell, temp.Path);
            Document(controller).Apply(new TextChange(0, 0, "SECRET-old"));
            Invoke(controller, "StartSave", NativeSaveRequest.Receive(NativeSaveKind.SaveAs));
            await WaitPosted(shell);
            var replacement = new Document("SECRET-replacement");
            var calls = 0;
            /// <summary>Replaces once without recursively retaining the test callback.</summary>
            void Replace()
            {
                calls++;
                Set(shell, "DuringPendingCommit", null);
                Set(shell, "DuringDocumentSet", null);
                Invoke(controller, "ReplaceDocument", replacement, default(TelemetryMark), 0);
            }
            if (boundary == "settlement") Set(shell, "DuringPendingCommit", (Action)Replace);
            else Set(shell, "DuringDocumentSet", (Action<NativeDocumentView>)(_ => Replace()));
            Pump(shell);
            Assert.Equal(1, calls);
            Assert.Same(replacement, Document(controller));
            Assert.Equal("SECRET-replacement", Document(controller).Snapshot.GetText());
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        Terminal(records, "command.save_as", "cancelled", "stale_document");
        Assert.DoesNotContain(records, r => Op(r) == "save.completed");
        Privacy(records);
    }

    /// <summary>Posting failure is distinct from filesystem success and never certifies a UI completion.</summary>
    [Fact]
    public async Task Failed_ui_post_keeps_committed_file_but_not_successful_request()
    {
        using var temp = new RepoTemp();
        var target = temp.File("SECRET-post-target.txt");
        Configure(temp.Path);
        try
        {
            var shell = Shell();
            Set(shell, "SavePath", target);
            using var controller = Controller(shell, temp.Path);
            Document(controller).Apply(new TextChange(0, 0, "SECRET-saved"));
            Set(shell, "RejectPost", true);
            var request = NativeSaveRequest.Receive(NativeSaveKind.SaveAs);
            // Await the shipped worker directly: a rejected Post intentionally supplies no queued signal.
            var worker = (Task)typeof(NativeEditorController)
                .GetMethod("RunSaveAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(controller, [Document(controller), Field(controller, "_canvasGeneration"), 0L,
                    target, true, false, request.Trace])!;
            await worker.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("SECRET-saved", await File.ReadAllTextAsync(target));
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        Terminal(records, "command.save_as", "failure", "ui_post_failed");
        Assert.Equal("success", One(records, "document.save").GetProperty("status").GetString());
        Assert.DoesNotContain(records, r => Op(r) is "save.ui_started" or "save.completed");
        Privacy(records);
    }

    /// <summary>The generation guard rejects a queued completion even when the same document identity remains.</summary>
    [Fact]
    public async Task Changed_generation_rejects_queued_completion()
    {
        using var temp = new RepoTemp();
        Configure(temp.Path);
        try
        {
            var shell = Shell();
            Set(shell, "SavePath", temp.File("SECRET-generation.txt"));
            using var controller = Controller(shell, temp.Path);
            Document(controller).Apply(new TextChange(0, 0, "SECRET-source"));
            Invoke(controller, "StartSave", NativeSaveRequest.Receive(NativeSaveKind.SaveAs));
            await WaitPosted(shell);
            typeof(NativeEditorController).GetField("_canvasGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(controller, (long)Field(controller, "_canvasGeneration")! + 1);
            Pump(shell);
            Assert.False((bool)Field(controller, "_saving")!);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        Terminal(records, "command.save_as", "cancelled", "stale_document");
        Assert.DoesNotContain(records, r => Op(r) == "save.completed");
        Privacy(records);
    }

    /// <summary>Admission belongs to the document captured before a synchronous native callback or modal picker.</summary>
    [Theory]
    [InlineData("commit", false)]
    [InlineData("commit", true)]
    [InlineData("picker", false)]
    [InlineData("picker", true)]
    public async Task Admission_reentry_replacement_or_disposal_never_starts_save(string boundary, bool dispose)
    {
        using var temp = new RepoTemp();
        var target = temp.File("SECRET-admission.txt");
        Configure(temp.Path);
        try
        {
            var shell = Shell();
            Set(shell, "SavePath", target);
            using var controller = Controller(shell, temp.Path);
            Document(controller).Apply(new TextChange(0, 0, "SECRET-old"));
            var hook = boundary == "commit" ? "DuringPendingCommit" : "DuringSavePicker";
            var calls = 0;
            Set(shell, hook, (Action)(() =>
            {
                calls++;
                Set(shell, hook, null);
                if (dispose) controller.Dispose();
                else Invoke(controller, "ReplaceDocument", new Document("SECRET-replacement"), default(TelemetryMark), 0);
            }));
            var request = NativeSaveRequest.Receive(NativeSaveKind.SaveAs);
            request.Dispatch(r => Invoke(controller, "StartSave", r));
            Assert.Equal(1, calls);
            Assert.False((bool)Field(controller, "_saving")!);
            Assert.False(File.Exists(target));
            if (!dispose) Assert.Equal("SECRET-replacement", Document(controller).Snapshot.GetText());
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        AssertParent(Receipt(records), Terminal(records, "command.save_as", "cancelled",
            dispose ? "lifetime_ended" : "stale_document"));
        Assert.DoesNotContain(records, r => Op(r) is "save.admitted" or "save.worker_started" or "document.save" or "save.completed");
        Privacy(records);
    }

    /// <summary>A nested command admitted by a modal picker retains the busy token; the outer command is rejected.</summary>
    [Fact]
    public async Task Modal_picker_nested_save_does_not_admit_outer_request()
    {
        using var temp = new RepoTemp();
        var target = temp.File("SECRET-nested.txt");
        Configure(temp.Path);
        try
        {
            var shell = Shell();
            Set(shell, "SavePath", target);
            using var controller = Controller(shell, temp.Path);
            Document(controller).Apply(new TextChange(0, 0, "SECRET-source"));
            using var gate = new CommitGate();
            Document(controller).SaveOperations = gate;
            Set(shell, "DuringSavePicker", (Action)(() =>
            {
                Set(shell, "DuringSavePicker", null);
                var inner = NativeSaveRequest.Receive(NativeSaveKind.Save);
                inner.Dispatch(r => Invoke(controller, "StartSave", r));
            }));
            var outer = NativeSaveRequest.Receive(NativeSaveKind.SaveAs);
            try
            {
                outer.Dispatch(r => Invoke(controller, "StartSave", r));
                Assert.True((bool)Field(controller, "_saving")!);
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { gate.Release.Set(); }
            await PumpUntilSaved(shell, controller);
            Assert.Equal("SECRET-source", await File.ReadAllTextAsync(target));
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        AssertParent(One(records, "command.save_as.received"),
            Terminal(records, "command.save_as", "skipped", "already_saving"));
        AssertParent(One(records, "command.save.received"), Terminal(records, "command.save", "success", "completed"));
        One(records, "document.save");
        AssertParent(One(records, "command.save.received"), One(records, "save.admitted"));
        Privacy(records);
    }

    /// <summary>Modal overwrite approval cannot outlive its document, and a throwing dialog must not strand the worker.</summary>
    [Theory]
    [InlineData("replace", "cancelled", "stale_document")]
    [InlineData("dispose", "cancelled", "lifetime_ended")]
    [InlineData("throw", "failure", "save_failed")]
    public async Task Modal_overwrite_reentry_or_throw_never_mutates_target(string mode, string status, string reason)
    {
        using var temp = new RepoTemp();
        var target = temp.File("SECRET-existing.txt");
        await File.WriteAllTextAsync(target, "SECRET-original");
        Configure(temp.Path);
        try
        {
            var shell = Shell();
            Set(shell, "SavePath", target);
            Set(shell, "OverwriteApproved", true);
            using var controller = Controller(shell, temp.Path);
            Document(controller).Apply(new TextChange(0, 0, "SECRET-new"));
            var calls = 0;
            Set(shell, "DuringOverwriteConfirm", (Action)(() =>
            {
                calls++;
                Set(shell, "DuringOverwriteConfirm", null);
                if (mode == "dispose") controller.Dispose();
                else if (mode == "replace")
                    Invoke(controller, "ReplaceDocument", new Document("SECRET-replacement"), default(TelemetryMark), 0);
                else throw new InvalidOperationException("SECRET-confirmation-error");
            }));
            var request = NativeSaveRequest.Receive(NativeSaveKind.SaveAs);
            request.Dispatch(r => Invoke(controller, "StartSave", r));
            await PumpUntilSaved(shell, controller);
            Assert.Equal(1, calls);
            Assert.Equal("SECRET-original", await File.ReadAllTextAsync(target));
            if (mode == "replace") Assert.Equal("SECRET-replacement", Document(controller).Snapshot.GetText());
            if (mode == "throw") Assert.NotEmpty((IEnumerable<string>)Get(shell, "Errors")!);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        AssertParent(Receipt(records), Terminal(records, "command.save_as", status, reason));
        Assert.DoesNotContain(records, r => Op(r) is "save.overwrite_approved" or "save.snapshot_captured" or
            "save.commit_replace" or "save.completed");
        Assert.Equal(mode == "throw" ? "failure" : "cancelled", One(records, "document.save").GetProperty("status").GetString());
        Privacy(records);
    }

    /// <summary>The posted confirmation transports the original exception to its awaiting worker instead of throwing on UI dispatch.</summary>
    [Fact]
    public async Task Throwing_confirmation_preserves_original_exception_identity()
    {
        using var temp = new RepoTemp();
        var shell = Shell();
        using var controller = Controller(shell, temp.Path);
        var expected = new InvalidOperationException("SECRET-original-confirmation-exception");
        Set(shell, "DuringOverwriteConfirm", (Action)(() => throw expected));
        var task = (Task<bool>)typeof(NativeEditorController)
            .GetMethod("ConfirmSaveOverwriteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(controller, [temp.File("SECRET-target.txt"), Document(controller), Field(controller, "_canvasGeneration"), null])!;
        Pump(shell);
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Same(expected, observed);
    }

    /// <summary>Pumps until the admitted worker releases busy; polling is only a bounded deadlock guard, not an ordering assertion.</summary>
    private static async Task PumpUntilSaved(object shell, NativeEditorController controller)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while ((bool)Field(controller, "_saving")! && timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            Pump(shell);
            await Task.Delay(10);
        }
        Assert.False((bool)Field(controller, "_saving")!, "Save worker stranded after a modal native callback.");
    }

    /// <summary>Native ABI dispatch contains callback errors while retaining a truthful typed failure terminal.</summary>
    [Theory]
    [InlineData(false, "command.save")]
    [InlineData(true, "command.save_as")]
    public async Task Contained_dispatch_failure_never_escapes_native_callback(bool saveAs, string operation)
    {
        using var temp = new RepoTemp();
        Configure(temp.Path);
        try
        {
            var calls = 0;
            var succeeded = NativeSaveRequest.DispatchContained(saveAs ? NativeSaveKind.SaveAs : NativeSaveKind.Save, _ =>
            {
                calls++;
                throw new InvalidOperationException("SECRET-native-callback-error");
            });
            Assert.False(succeeded);
            Assert.Equal(1, calls);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        AssertParent(One(records, operation + ".received"), Terminal(records, operation, "failure", "callback_failed"));
        Assert.DoesNotContain(records, r => Op(r) is "save.admitted" or "save.worker_started" or "document.save" or "save.completed");
        Privacy(records);
    }

    /// <summary>Blocks commit after snapshot capture; release is always guaranteed even after an assertion fails.</summary>
    private sealed class CommitGate : DocumentSaveOperations, IDisposable
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new();
        internal override void Move(string stage, string target)
        {
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test commit release was not signalled.");
            base.Move(stage, target);
        }
        public void Dispose() { Release.Set(); Release.Dispose(); }
    }

    /// <summary>Injects failure before commit, retaining an error message that must never enter telemetry.</summary>
    private sealed class FailingMove : DocumentSaveOperations
    {
        internal override void Move(string stage, string target) => throw new IOException("SECRET-filesystem-error");
    }

    /// <summary>Reuses the native-controller event queue without native OS windows.</summary>
    private static INativeEditorShell Shell() => (INativeEditorShell)Activator.CreateInstance(
        typeof(NativeControllerTests).GetNestedType("FakeShell", BindingFlags.NonPublic)!,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        [NativeLineEndingMode.Preserve], null)!;

    /// <summary>Loads repository-local defaults without user environment overrides.</summary>
    private static NativeEditorController Controller(INativeEditorShell shell, string home)
    {
        var config = MoteConfigLoader.Load(new MoteConfigLoadOptions { UserHomeDirectory = home, UseEnvironmentOverride = false });
        return new NativeEditorController(shell, config, ThemePolicies.Get(config.ThemeId), null);
    }

    /// <summary>Uses the controller's actual canonical document.</summary>
    private static Document Document(NativeEditorController controller) => (Document)Field(controller, "_document")!;
    /// <summary>Reads private orchestration state without a production test-only API.</summary>
    private static object? Field(NativeEditorController controller, string name) =>
        typeof(NativeEditorController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller);
    /// <summary>Invokes shipped orchestration, preserving reflection exception semantics.</summary>
    private static void Invoke(NativeEditorController controller, string name, params object?[] args) =>
        typeof(NativeEditorController).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, args);
    /// <summary>Injects an established fake-shell behavior.</summary>
    private static void Set(object shell, string name, object? value) => shell.GetType().GetProperty(name)!.SetValue(shell, value);
    /// <summary>Reads observable shell effects independently from telemetry.</summary>
    private static object? Get(object shell, string name) => shell.GetType().GetProperty(name)!.GetValue(shell);
    /// <summary>Waits for a known queued callback; no timing sleep chooses worker ordering.</summary>
    private static Task WaitPosted(object shell) => (Task)shell.GetType().GetMethod("WaitForPostedAsync")!.Invoke(shell, null)!;
    /// <summary>Drains callbacks explicitly after arranging the intended test state.</summary>
    private static void Pump(object shell) => shell.GetType().GetMethod("Pump")!.Invoke(shell, null);
    /// <summary>Enables local, content-free tracing inside repository scratch space.</summary>
    private static void Configure(string path) => MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = path });
    /// <summary>Reads durable records only after producer and writer shutdown.</summary>
    private static JsonElement[] Read(string path) => Directory.GetFiles(path, "*.jsonl").SelectMany(File.ReadLines)
        .Select(line => { using var json = JsonDocument.Parse(line); return json.RootElement.Clone(); }).ToArray();
    /// <summary>Returns the fixed operation field.</summary>
    private static string Op(JsonElement record) => record.GetProperty("operation").GetString()!;
    /// <summary>Finds the unique typed request anchor independently of the terminal operation.</summary>
    private static JsonElement Receipt(JsonElement[] records) => Assert.Single(records, r =>
        Op(r) is "command.save.received" or "command.save_as.received");
    /// <summary>Returns the persisted span identity.</summary>
    private static string Id(JsonElement record) => record.GetProperty("span_id").GetString()!;
    /// <summary>Requires exactly one record for the specified operation.</summary>
    private static JsonElement One(JsonElement[] records, string op) => Assert.Single(records, r => Op(r) == op);
    /// <summary>Requires one truthful terminal and independently checks its status and allowlisted reason.</summary>
    private static JsonElement Terminal(JsonElement[] records, string op, string status, string reason)
    {
        var result = One(records, op);
        Assert.Equal(status, result.GetProperty("status").GetString());
        Assert.Equal(reason, result.GetProperty("attributes").GetProperty("reason").GetString());
        return result;
    }
    /// <summary>Checks the explicit causal edge, not merely a shared session trace.</summary>
    private static void AssertParent(JsonElement parent, JsonElement child)
    {
        Assert.Equal(Id(parent), child.GetProperty("parent_span_id").GetString());
        Assert.Equal(parent.GetProperty("trace_id").GetString(), child.GetProperty("trace_id").GetString());
    }
    /// <summary>Checks the exact saved immutable source version.</summary>
    private static void AssertVersion(JsonElement record, long version) =>
        Assert.Equal(version, record.GetProperty("attributes").GetProperty("version").GetInt64());
    /// <summary>Secret paths, source, and injected exception messages must all be absent from the journal.</summary>
    private static void Privacy(JsonElement[] records) => Assert.DoesNotContain("SECRET", string.Join('\n', records.Select(r => r.GetRawText())));
}
