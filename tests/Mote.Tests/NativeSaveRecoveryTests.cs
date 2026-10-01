using System.Reflection;
using System.Security;
using System.Collections;
using Mote.Engine;
using Mote.Native;

namespace Mote.Tests;

public sealed partial class NativeControllerTests
{
    /// <summary>Real controller/fake shell Save failures display observed facts, never blanket preservation.</summary>
    [Theory]
    [InlineData(1175, "matched the original snapshot")]
    [InlineData(1176, "target was missing")]
    [InlineData(1177, "contained different bytes")]
    [InlineData(32, "could not be verified")]
    public async Task Native_save_failure_message_classifies_target_without_blanket_preservation(int code, string expected)
    {
        using var temp = new RepoTemp();
        var path = temp.File("target.txt");
        await File.WriteAllTextAsync(path, "original");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("target.txt") == true);
        var document = GetSaveTestDocument(controller);
        FileStream? held = null;
        document.SaveOperations = new NativeFaultOperations
        {
            Commit = (_, target) =>
            {
                if (code == 1176) File.Delete(target);
                if (code == 1177) File.WriteAllText(target, "external bytes");
                if (code == 32)
                {
                    if (OperatingSystem.IsWindows())
                        held = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None);
                    else File.Delete(target); // Unknown read control below is Windows-specific.
                }
                throw new IOException("injected commit failure", unchecked((int)(0x80070000u | (uint)code)));
            }
        };
        try
        {
            shell.Edit("attempted bytes");
            shell.RequestSave();
            await shell.PumpUntilAsync(() => shell.Errors.Any(e => e.StartsWith("Save failed.", StringComparison.Ordinal)));
            var message = shell.Errors.Single(e => e.StartsWith("Save failed.", StringComparison.Ordinal));
            Assert.DoesNotContain("original file was retained", message);
            if (code != 32 || OperatingSystem.IsWindows()) Assert.Contains(expected, message);
            Assert.Contains(document.PendingSaveRecovery!.Path, message);
            Assert.Equal("attempted bytes", await File.ReadAllTextAsync(document.PendingSaveRecovery.Path));
            Assert.True(document.IsModified);
        }
        finally { held?.Dispose(); }
    }

    /// <summary>Recovery export uses the real command/picker/task/post path and never silently saves later edits.</summary>
    [Fact]
    public async Task Native_recovery_export_then_explicit_save_keeps_versions_and_identity_distinct()
    {
        using var temp = new RepoTemp();
        var path = temp.File("target.txt");
        await File.WriteAllTextAsync(path, "original");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("target.txt") == true);
        var document = GetSaveTestDocument(controller);
        document.SaveOperations = new NativeFaultOperations { Commit = (_, _) => throw new IOException("injected failure") };
        shell.Edit("attempted bytes");
        shell.RequestSave();
        await shell.PumpUntilAsync(() => shell.Errors.Any(e => e.StartsWith("Save failed.", StringComparison.Ordinal)));
        var recovery = document.PendingSaveRecovery!;
        shell.Edit("current later bytes");
        var exported = temp.File("exported.txt");
        shell.SavePath = exported;
        shell.RequestSave();
        await shell.PumpUntilAsync(() => document.PendingSaveRecovery is null);
        Assert.Equal("attempted bytes", await File.ReadAllTextAsync(exported));
        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Assert.Equal("current later bytes", document.Snapshot.GetText());
        Assert.True(document.IsModified);
        Assert.Equal(path, document.FilePath);
        Assert.False(File.Exists(recovery.Path));
        Assert.Contains(shell.Errors, e => e.Contains("later edits are not included"));
        // Export clears its own busy state via the posted UI callback before a new Save.
        await shell.PumpUntilAsync(() => shell.CanvasStatus?.Contains("Recovery exported") == true);
        document.SaveOperations = new DocumentSaveOperations();
        shell.RequestSave();
        await shell.PumpUntilAsync(() => !document.IsModified && shell.Document!.IsModified == false);
        Assert.Equal("current later bytes", await File.ReadAllTextAsync(path));
    }

    /// <summary>
    /// Delivers an already queued current-version analysis after export completion.
    /// Semantic publication must not hide the warning that export did not save the buffer.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_recovery_export_notice_survives_queued_analysis_and_explicit_save_is_available(
        bool editAfterSaveSnapshot)
    {
        using var temp = new RepoTemp();
        var path = temp.File("target.txt");
        await File.WriteAllTextAsync(path, "original");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("target.txt") == true);
        var document = GetSaveTestDocument(controller);
        document.SaveOperations = new NativeFaultOperations { Commit = (_, _) => throw new IOException("injected failure") };
        shell.Edit("attempted bytes");
        shell.RequestSave();
        await shell.PumpUntilAsync(() => shell.Errors.Any(e => e.StartsWith("Save failed.", StringComparison.Ordinal)) &&
            shell.Analysis?.Status.Contains("Complete · v1", StringComparison.Ordinal) == true);

        shell.Edit("current later bytes");
        await shell.WaitForPostedAsync(); // Hold v2 analysis instead of draining it.
        shell.SavePath = temp.File("exported.txt");
        shell.RequestSave();
        await shell.WaitForPostedCountAsync(2); // Both analysis and export completion are now queued.
        shell.PumpReverse(); // Export first, then the still-valid v2 semantic presentation.

        Assert.Null(document.PendingSaveRecovery);
        Assert.Contains("Complete · v2", shell.Analysis!.Status);
        Assert.Contains("Recovery exported", shell.StatusNotice);
        var effectiveStatusAfterAnalysis = shell.CanvasStatus;
        var cachedAnalysis = shell.Analysis;
        Assert.Equal("attempted bytes", await File.ReadAllTextAsync(shell.SavePath));
        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Assert.True(document.IsModified);
        Assert.Equal(path, document.FilePath);

        // Another ordinary operation must not steal the still-relevant recovery instruction.
        shell.ChangeSelection(0, 7);
        shell.RequestCopy();
        await shell.PumpUntilAsync(() => shell.ClipboardText == "current");
        Assert.Contains("Recovery exported", shell.CanvasStatus);

        // Prove the busy flag was released independently of the warning presentation.
        document.SaveOperations = new DocumentSaveOperations();
        shell.RequestSave();
        if (editAfterSaveSnapshot)
        {
            await shell.WaitForPostedAsync(); // Save captured v2 and posted completion; do not deliver it yet.
            shell.Edit("newest unsaved bytes");
            await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("Complete · v3") == true);
            Assert.True(document.IsModified);
            Assert.Equal("newest unsaved bytes", document.Snapshot.GetText());
            Assert.Contains("Recovery exported", shell.CanvasStatus);
        }
        else
        {
            await shell.PumpUntilAsync(() => !document.IsModified && shell.Document?.IsModified == false);
            Assert.DoesNotContain("Recovery exported", shell.Document!.Status);
            Assert.DoesNotContain("Recovery exported", shell.CanvasStatus);
            // Native hosts may replay their cached semantic view without visiting the controller.
            shell.SetAnalysis(cachedAnalysis!);
            Assert.DoesNotContain("Recovery exported", shell.CanvasStatus);
        }
        Assert.Equal("current later bytes", await File.ReadAllTextAsync(path));
        Assert.Contains("Recovery exported", effectiveStatusAfterAnalysis);
        Assert.Contains("Current buffer is not saved", effectiveStatusAfterAnalysis);
    }

    /// <summary>Generic Close/New warns but never implicitly deletes recovery or blocks permitted discard.</summary>
    [Fact]
    public async Task Native_close_and_new_warn_and_retain_stage()
    {
        using var temp = new RepoTemp();
        var path = temp.File("target.txt");
        await File.WriteAllTextAsync(path, "original");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("target.txt") == true);
        var document = GetSaveTestDocument(controller);
        document.SaveOperations = new NativeFaultOperations { Commit = (_, _) => throw new IOException("injected failure") };
        shell.Edit("attempted bytes");
        shell.RequestSave();
        await shell.PumpUntilAsync(() => shell.Errors.Any(e => e.StartsWith("Save failed.", StringComparison.Ordinal)));
        var stage = document.PendingSaveRecovery!.Path;
        Assert.True(shell.RequestClose());
        Assert.Contains(shell.Errors, e => e.Contains("will remain after closing") && e.Contains(stage));
        shell.RequestNew();
        Assert.Contains("Untitled", shell.Document!.Title);
        Assert.Equal("attempted bytes", await File.ReadAllTextAsync(stage));
        Assert.Equal("original", await File.ReadAllTextAsync(path));
    }

    /// <summary>Incomplete cleanup artifacts are not presented as completed snapshots or an automatic export choice.</summary>
    [Fact]
    public void Native_incomplete_recovery_explains_manual_resolution_without_claiming_complete_bytes()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { SavePath = temp.File("must-not-export.txt") };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        var document = GetSaveTestDocument(controller);
        var stage = Document.GetSaveRecoveryPath(temp.File("partial.txt"));
        File.WriteAllText(stage, "partial encoded bytes");
        typeof(Document).GetField("_pendingSaveRecovery", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(document, new SaveRecovery(stage, document.Snapshot.Version, null, temp.File("partial.txt")));
        shell.RequestSave();
        Assert.Contains(shell.Errors, e => e.Contains("Incomplete Save recovery") && e.Contains(stage));
        Assert.Contains(shell.Errors, e => e.Contains("not a verified complete snapshot") && e.Contains("manually"));
        Assert.False(File.Exists(shell.SavePath));
        Assert.True(shell.RequestClose());
        Assert.Contains(shell.Errors, e => e.Contains("incomplete or unverified staged data"));
        Assert.Equal("partial encoded bytes", File.ReadAllText(stage));
    }

    /// <summary>Restart after a target-absent commit failure discovers, but never trusts or deletes, the same-path sidecar.</summary>
    [Fact]
    public async Task Native_restart_open_failure_hints_only_same_path_unowned_sidecar()
    {
        using var temp = new RepoTemp();
        var path = temp.File("lost-target.txt");
        var stage = Document.GetSaveRecoveryPath(path);
        File.WriteAllText(stage, "unverified recovered bytes");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Errors.Count > 0);
        var failure = shell.Errors.Single();
        Assert.StartsWith("Cannot open file:", failure);
        Assert.Contains("unowned, unverified", failure);
        Assert.Contains(stage, failure);
        Assert.DoesNotContain("unverified recovered bytes", failure);
        Assert.Null(GetSaveTestDocument(controller).PendingSaveRecovery);
        Assert.Contains("Untitled", shell.Document!.Title);
        Assert.Equal("unverified recovered bytes", File.ReadAllText(stage));
        shell.OpenPath = temp.File("different-missing-target.txt");
        shell.RequestOpen();
        await shell.PumpUntilAsync(() => shell.Errors.Count > 1);
        Assert.DoesNotContain(stage, shell.Errors.Last());
        Assert.DoesNotContain("recovery sidecar", shell.Errors.Last());
        // A successful Open is unchanged even when a recovery sidecar remains nearby.
        File.WriteAllText(path, "original restored externally");
        shell.OpenPath = path;
        shell.RequestOpen();
        await shell.PumpUntilAsync(() => shell.Document!.Title.Contains("lost-target.txt"));
        Assert.Equal("original restored externally", GetSaveTestDocument(controller).Snapshot.GetText());
        Assert.Null(GetSaveTestDocument(controller).PendingSaveRecovery);
        Assert.Equal("unverified recovered bytes", File.ReadAllText(stage));
        Assert.Equal(2, shell.Errors.Count);
    }

    /// <summary>Unavailable exception Data still posts Save failure, clears busy state and permits explicit recovery export.</summary>
    [Fact]
    public async Task Native_rejecting_failure_data_reports_error_and_resets_saving()
    {
        using var temp = new RepoTemp();
        var path = temp.File("target.txt");
        await File.WriteAllTextAsync(path, "original");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("target.txt") == true);
        var document = GetSaveTestDocument(controller);
        document.SaveOperations = new NativeFaultOperations { Commit = (_, _) => throw new NativeRejectingDataException() };
        shell.Edit("attempted bytes");
        shell.RequestSave();
        await shell.PumpUntilAsync(() => shell.Errors.Any(e => e.StartsWith("Save failed.", StringComparison.Ordinal)));
        Assert.Contains(shell.Errors, e => e.Contains("Disk outcome was not verified"));
        Assert.NotNull(document.PendingSaveRecovery);
        shell.SavePath = temp.File("recovered-copy.txt");
        shell.RequestSave(); // Would be ignored forever if the failure never reset _saving.
        await shell.PumpUntilAsync(() => document.PendingSaveRecovery is null);
        Assert.Equal("attempted bytes", await File.ReadAllTextAsync(shell.SavePath));
        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Assert.True(document.IsModified);
    }

    /// <summary>Reproduces a provider that cannot supply optional exception evidence to UI/telemetry.</summary>
    private sealed class NativeRejectingDataException() : IOException("injected no-data failure", unchecked((int)0x80070498))
    {
        public override IDictionary Data => throw new SecurityException("optional evidence unavailable");
    }

    /// <summary>Accesses the engine boundary only for deterministic failure injection; no production test API.</summary>
    private static Document GetSaveTestDocument(NativeEditorController controller) =>
        (Document)typeof(NativeEditorController).GetField("_document", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(controller)!;

    /// <summary>Injects only commit errors while keeping all actual controller workflow code.</summary>
    private sealed class NativeFaultOperations : DocumentSaveOperations
    {
        internal required Action<string, string> Commit { get; init; }
        internal override void Replace(string stage, string target) => Commit(stage, target);
        internal override void Move(string stage, string target) => Commit(stage, target);
    }
}
