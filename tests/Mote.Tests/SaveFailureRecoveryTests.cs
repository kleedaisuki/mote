using System.Text;
using System.Security;
using System.Collections;
using Mote.Engine;

namespace Mote.Tests;

/// <summary>Deterministic application-policy tests, not reproductions of OS filter errors.</summary>
public sealed class SaveFailureRecoveryTests
{
    /// <summary>Primary errors survive every documented/uncertain commit result; unique staged bytes survive.</summary>
    [Theory]
    [InlineData(1175, SaveFileOutcome.OriginalSnapshot)]
    [InlineData(1176, SaveFileOutcome.Missing)]
    [InlineData(1177, SaveFileOutcome.OtherContent)]
    public async Task Failed_replace_preserves_primary_and_retains_one_owned_snapshot(int code, SaveFileOutcome outcome)
    {
        using var temp = new RepoTemp();
        var path = temp.File("target.txt");
        await File.WriteAllTextAsync(path, "original", new UTF8Encoding(false));
        using var document = await Document.OpenAsync(path);
        document.Apply(new TextChange(8, 0, " changed"));
        var primary = Failure(code);
        var operations = new FaultOperations
        {
            Commit = (_, target) =>
            {
                if (code == 1176) File.Delete(target);
                if (code == 1177) File.WriteAllText(target, "external bytes");
                throw primary;
            }
        };
        document.SaveOperations = operations;
        Assert.Same(primary, await Assert.ThrowsAsync<IOException>(() => document.SaveAsync()));
        Assert.Equal(unchecked((int)(0x80070000u | (uint)code)), primary.HResult);
        Assert.Equal("Replace", primary.Data["Mote.Engine.SavePhase"]);
        var evidence = Assert.IsType<SaveFailureInfo>(SaveFailureInfo.FromException(primary));
        Assert.Equal(outcome, evidence.TargetOutcome);
        Assert.Equal(SaveFileOutcome.SavedSnapshot, evidence.RecoveryOutcome);
        Assert.False(evidence.CommitReturned);
        var recovery = Assert.IsType<SaveRecovery>(document.PendingSaveRecovery);
        Assert.Equal(document.Snapshot.Version, recovery.SnapshotVersion);
        Assert.Equal("original changed", await File.ReadAllTextAsync(recovery.Path));
        Assert.True(document.IsModified);
        Assert.Equal(path, document.FilePath);
        await Assert.ThrowsAsync<IOException>(() => document.SaveAsync());
        Assert.Equal(1, operations.CommitCount);
        Assert.Single(Directory.GetFiles(temp.Path, "*.recovery"));
    }

    /// <summary>A commit can throw after target installation; it must not be described as original retained.</summary>
    [Fact]
    public async Task Installed_target_then_error_is_classified_as_saved_bytes_without_recommit()
    {
        using var temp = new RepoTemp();
        using var document = new Document("new bytes");
        var path = temp.File("new.txt");
        var primary = Failure(1177);
        document.SaveOperations = new FaultOperations
        {
            Commit = (stage, target) => { File.Move(stage, target); throw primary; }
        };
        Assert.Same(primary, await Assert.ThrowsAsync<IOException>(() => document.SaveAsync(path)));
        var info = SaveFailureInfo.FromException(primary)!;
        Assert.Equal(SaveFileOutcome.SavedSnapshot, info.TargetOutcome);
        Assert.Equal(SaveFileOutcome.Missing, info.RecoveryOutcome);
        Assert.Null(document.PendingSaveRecovery);
        Assert.Null(document.FilePath);
        Assert.True(document.IsModified);
        Assert.Equal("Move", primary.Data["Mote.Engine.SavePhase"]);
    }

    /// <summary>Successful replacement followed by failed bookkeeping stays a distinct outcome.</summary>
    [Fact]
    public async Task Saved_stamp_failure_preserves_primary_and_reports_commit_returned()
    {
        using var temp = new RepoTemp();
        using var document = new Document("installed bytes");
        var primary = Failure(5);
        document.SaveOperations = new FaultOperations { SavedStampFailure = primary };
        var path = temp.File("new.txt");
        Assert.Same(primary, await Assert.ThrowsAsync<IOException>(() => document.SaveAsync(path)));
        var info = SaveFailureInfo.FromException(primary)!;
        Assert.True(info.CommitReturned);
        Assert.Equal(SaveFileOutcome.SavedSnapshot, info.TargetOutcome);
        Assert.Equal("SavedStamp", primary.Data["Mote.Engine.SavePhase"]);
        Assert.Null(document.PendingSaveRecovery);
        Assert.True(document.IsModified);
        Assert.Null(document.FilePath);
    }

    /// <summary>Cleanup failure is secondary; the fingerprint conflict remains the primary failure.</summary>
    [Fact]
    public async Task Cleanup_does_not_mask_final_content_conflict()
    {
        using var temp = new RepoTemp();
        var path = temp.File("conflict.txt");
        await File.WriteAllTextAsync(path, "AAAA", new UTF8Encoding(false));
        using var document = await Document.OpenAsync(path);
        var stamp = File.GetLastWriteTimeUtc(path);
        document.Apply(new TextChange(0, 4, "CCCC"));
        await File.WriteAllTextAsync(path, "BBBB", new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(path, stamp);
        var cleanup = Failure(5);
        document.SaveOperations = new FaultOperations { CleanupFailure = cleanup };
        var primary = await Assert.ThrowsAsync<IOException>(() => document.SaveAsync());
        Assert.NotSame(cleanup, primary);
        Assert.Equal("FinalTargetCheck", primary.Data["Mote.Engine.SavePhase"]);
        Assert.Equal(cleanup.HResult, primary.Data["Mote.Engine.SaveCleanupHResult"]);
        Assert.Equal(cleanup.HResult, SaveFailureInfo.FromException(primary)!.SecondaryHResult);
        Assert.Equal("BBBB", await File.ReadAllTextAsync(path));
        Assert.Equal("CCCC", await File.ReadAllTextAsync(document.PendingSaveRecovery!.Path));
    }

    /// <summary>Existing slots, including orphans from another process, are never adopted or cleaned.</summary>
    [Fact]
    public async Task Preexisting_slot_blocks_save_without_adoption_or_deletion()
    {
        using var temp = new RepoTemp();
        var path = temp.File("target.txt");
        var stage = Document.GetSaveRecoveryPath(path);
        await File.WriteAllTextAsync(stage, "other owner's recovery");
        using var document = new Document("new document");
        var primary = await Assert.ThrowsAsync<IOException>(() => document.SaveAsync(path));
        Assert.Equal("TempWriteAndHash", primary.Data["Mote.Engine.SavePhase"]);
        Assert.Equal(stage, SaveFailureInfo.FromException(primary)!.RecoveryPath);
        Assert.Null(document.PendingSaveRecovery);
        Assert.Equal("other owner's recovery", await File.ReadAllTextAsync(stage));
        Assert.False(File.Exists(path));
    }

    /// <summary>Export recovers the attempted snapshot, not later buffer edits or a new saved-state identity.</summary>
    [Fact]
    public async Task Export_preserves_old_snapshot_and_leaves_later_edits_dirty()
    {
        using var temp = new RepoTemp();
        using var document = await FailedNewDocumentAsync(temp);
        var recovery = document.PendingSaveRecovery!;
        document.Apply(new TextChange(document.Snapshot.Length, 0, " later"));
        var exported = temp.File("exported.txt");
        await document.ExportSaveRecoveryAsync(exported);
        Assert.Equal("staged bytes", await File.ReadAllTextAsync(exported));
        Assert.Equal("staged bytes later", document.Snapshot.GetText());
        Assert.True(document.IsModified);
        Assert.Null(document.FilePath);
        Assert.Null(document.PendingSaveRecovery);
        Assert.False(File.Exists(recovery.Path));
        document.SaveOperations = new DocumentSaveOperations();
        await document.SaveAsync(temp.File("current.txt"));
        Assert.False(document.IsModified);
        Assert.Equal("staged bytes later", await File.ReadAllTextAsync(document.FilePath!));
    }

    /// <summary>Failed/cancelled export never replaces existing content or releases its source stage.</summary>
    [Fact]
    public async Task Export_refuses_overwrite_and_cancellation_retains_source()
    {
        using var temp = new RepoTemp();
        using var document = await FailedNewDocumentAsync(temp);
        var recovery = document.PendingSaveRecovery!;
        var existing = temp.File("existing.txt");
        await File.WriteAllTextAsync(existing, "keep me");
        await Assert.ThrowsAsync<IOException>(() => document.ExportSaveRecoveryAsync(existing));
        Assert.Equal("keep me", await File.ReadAllTextAsync(existing));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            document.ExportSaveRecoveryAsync(temp.File("cancelled.txt"), cancelled.Token));
        Assert.Same(recovery, document.PendingSaveRecovery);
        Assert.Equal("staged bytes", await File.ReadAllTextAsync(recovery.Path));
    }

    /// <summary>Explicit cleanup must not erase an externally changed sidecar.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Changed_stage_is_not_removed_by_export_or_discard(bool export)
    {
        using var temp = new RepoTemp();
        using var document = await FailedNewDocumentAsync(temp);
        var recovery = document.PendingSaveRecovery!;
        await File.WriteAllTextAsync(recovery.Path, "external recovery");
        await Assert.ThrowsAsync<IOException>(() => export
            ? document.ExportSaveRecoveryAsync(temp.File("copy.txt"))
            : document.DiscardSaveRecoveryAsync());
        Assert.Same(recovery, document.PendingSaveRecovery);
        Assert.Equal("external recovery", await File.ReadAllTextAsync(recovery.Path));
    }

    /// <summary>Explicit discard removes only the verified old snapshot, leaving the current buffer dirty.</summary>
    [Fact]
    public async Task Explicit_discard_releases_slot_but_not_buffer()
    {
        using var temp = new RepoTemp();
        using var document = await FailedNewDocumentAsync(temp);
        var recovery = document.PendingSaveRecovery!;
        await document.DiscardSaveRecoveryAsync();
        Assert.False(File.Exists(recovery.Path));
        Assert.Null(document.PendingSaveRecovery);
        Assert.True(document.IsModified);
        Assert.Equal("staged bytes", document.Snapshot.GetText());
    }

    /// <summary>Dispose leaves discoverable bytes; a new process/document refuses to overwrite that slot.</summary>
    [Fact]
    public async Task Disposal_preserves_recovery_and_restart_is_bounded()
    {
        using var temp = new RepoTemp();
        var document = await FailedNewDocumentAsync(temp);
        var stage = document.PendingSaveRecovery!.Path;
        document.Dispose();
        Assert.Equal("staged bytes", await File.ReadAllTextAsync(stage));
        using var restarted = new Document("later bytes");
        await Assert.ThrowsAsync<IOException>(() => restarted.SaveAsync(temp.File("target.txt")));
        Assert.Equal("staged bytes", await File.ReadAllTextAsync(stage));
        Assert.Null(restarted.PendingSaveRecovery);
        Assert.Single(Directory.GetFiles(temp.Path, "*.recovery"));
    }

    /// <summary>Fixed hashed components do not exceed filename limits for already-long user filenames.</summary>
    [Fact]
    public void Recovery_path_has_bounded_component_and_same_directory()
    {
        using var temp = new RepoTemp();
        var path = temp.File(new string('a', 240) + ".txt");
        var stage = Document.GetSaveRecoveryPath(path);
        Assert.Equal(Path.GetDirectoryName(path), Path.GetDirectoryName(stage));
        Assert.True(Path.GetFileName(stage).Length < 100);
        Assert.Equal(stage, Document.GetSaveRecoveryPath(path));
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            Assert.Equal(Document.GetSaveRecoveryPath(temp.File("TARGET.txt")),
                Document.GetSaveRecoveryPath(temp.File("target.txt")));
        if (OperatingSystem.IsMacOS())
            Assert.Equal(Document.GetSaveRecoveryPath(temp.File("é.txt")),
                Document.GetSaveRecoveryPath(temp.File("e\u0301.txt")));
    }

    /// <summary>Explicitly acknowledging an already missing owned slot deletes nothing and releases the Save gate.</summary>
    [Fact]
    public async Task Discard_acknowledges_missing_stage_without_deleting_target_or_buffer()
    {
        using var temp = new RepoTemp();
        using var document = await FailedNewDocumentAsync(temp);
        File.Delete(document.PendingSaveRecovery!.Path);
        await document.DiscardSaveRecoveryAsync();
        Assert.Null(document.PendingSaveRecovery);
        Assert.True(document.IsModified);
        Assert.Equal("staged bytes", document.Snapshot.GetText());
        document.SaveOperations = new DocumentSaveOperations();
        await document.SaveAsync(temp.File("target.txt"));
        Assert.False(document.IsModified);
    }

    /// <summary>If stage inspection cannot read bytes, ownership remains pending; exporting is possible after the lock is released.</summary>
    [Fact]
    public async Task Unreadable_stage_after_commit_error_is_not_cleaned_or_misclassified_as_missing()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var temp = new RepoTemp();
        using var document = new Document("staged bytes");
        FileStream? held = null;
        var primary = Failure(1176);
        document.SaveOperations = new FaultOperations
        {
            Commit = (stage, _) =>
            {
                held = new FileStream(stage, FileMode.Open, FileAccess.Read, FileShare.None);
                throw primary;
            }
        };
        try
        {
            Assert.Same(primary, await Assert.ThrowsAsync<IOException>(() => document.SaveAsync(temp.File("target.txt"))));
            Assert.Equal(SaveFileOutcome.Unknown, SaveFailureInfo.FromException(primary)!.RecoveryOutcome);
            Assert.NotNull(document.PendingSaveRecovery);
        }
        finally { held?.Dispose(); }
        await document.ExportSaveRecoveryAsync(temp.File("copy.txt"));
        Assert.Equal("staged bytes", await File.ReadAllTextAsync(temp.File("copy.txt")));
        Assert.Null(document.PendingSaveRecovery);
    }

    /// <summary>Actual Windows denied reads are Unknown, never falsely Missing.</summary>
    [Fact]
    public async Task Unreadable_target_after_failure_reports_unknown_and_retains_stage()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var temp = new RepoTemp();
        var path = temp.File("held.txt");
        await File.WriteAllTextAsync(path, "original");
        using var document = await Document.OpenAsync(path);
        document.Apply(new TextChange(8, 0, " changed"));
        FileStream? held = null;
        var primary = Failure(1176);
        document.SaveOperations = new FaultOperations
        {
            Commit = (_, target) =>
            {
                held = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None);
                throw primary;
            }
        };
        try
        {
            Assert.Same(primary, await Assert.ThrowsAsync<IOException>(() => document.SaveAsync()));
            Assert.Equal(SaveFileOutcome.Unknown, SaveFailureInfo.FromException(primary)!.TargetOutcome);
            Assert.NotNull(SaveFailureInfo.FromException(primary)!.SecondaryHResult);
            Assert.NotNull(document.PendingSaveRecovery);
        }
        finally { held?.Dispose(); }
    }

    /// <summary>Actual Windows read-only replacement failure retains exact staged bytes and allows explicit recovery.</summary>
    [Fact]
    public async Task Read_only_target_failure_retains_original_and_stage()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var temp = new RepoTemp();
        var path = temp.File("readonly.txt");
        await File.WriteAllTextAsync(path, "original");
        using var document = await Document.OpenAsync(path);
        document.Apply(new TextChange(8, 0, " changed"));
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var primary = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => document.SaveAsync());
            Assert.Equal(unchecked((int)0x80070005), primary.HResult);
            Assert.Equal("Replace", primary.Data["Mote.Engine.SavePhase"]);
            Assert.Equal(SaveFileOutcome.OriginalSnapshot, SaveFailureInfo.FromException(primary)!.TargetOutcome);
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Equal("original changed", await File.ReadAllTextAsync(document.PendingSaveRecovery!.Path));
            Assert.True(document.IsModified);
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        await document.ExportSaveRecoveryAsync(temp.File("recovered.txt"));
        await document.SaveAsync();
        Assert.Equal("original changed", await File.ReadAllTextAsync(path));
        Assert.False(document.IsModified);
    }

    /// <summary>1176 recovery export cannot implicitly recreate the attempted target or change saved identity.</summary>
    [Fact]
    public async Task Export_refuses_missing_attempted_target_including_save_as()
    {
        using var temp = new RepoTemp();
        var original = temp.File("original.txt");
        var attempted = temp.File("attempted-save-as.txt");
        await File.WriteAllTextAsync(original, "original");
        await File.WriteAllTextAsync(attempted, "approved target");
        using var document = await Document.OpenAsync(original);
        document.Apply(new TextChange(8, 0, " changed"));
        var approved = await FileOverwriteToken.CaptureAsync(attempted);
        document.SaveOperations = new FaultOperations
        {
            Commit = (_, target) => { File.Delete(target); throw Failure(1176); }
        };
        await Assert.ThrowsAsync<IOException>(() => document.SaveOverAsync(approved));
        var recovery = document.PendingSaveRecovery!;
        Assert.Equal(attempted, recovery.AttemptedTargetPath);
        var lexicalAlias = Path.Combine(temp.Path, ".", Path.GetFileName(attempted));
        await Assert.ThrowsAsync<IOException>(() => document.ExportSaveRecoveryAsync(lexicalAlias));
        Assert.False(File.Exists(attempted));
        Assert.Same(recovery, document.PendingSaveRecovery);
        Assert.Equal("original changed", await File.ReadAllTextAsync(recovery.Path));
        Assert.Equal(original, document.FilePath);
        Assert.True(document.IsModified);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            await Assert.ThrowsAsync<IOException>(() => document.ExportSaveRecoveryAsync(attempted.ToUpperInvariant()));
        await document.ExportSaveRecoveryAsync(temp.File("separate-recovered-copy.txt"));
        Assert.False(File.Exists(attempted));
        Assert.Equal(original, document.FilePath);
        Assert.True(document.IsModified);
    }

    /// <summary>Security/provider inspection failures are bounded Unknown evidence, never the new primary exception.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unexpected_failure_inspection_preserves_primary_and_ownership(bool security)
    {
        using var temp = new RepoTemp();
        using var document = new Document("unique staged bytes");
        var primary = Failure(1176);
        Exception inspection = security ? new SecurityException("unavailable metadata")
            : new InvalidOperationException("provider unavailable");
        document.SaveOperations = new FaultOperations
        {
            Commit = (_, _) => throw primary,
            InspectionFailure = inspection
        };
        Assert.Same(primary, await Assert.ThrowsAsync<IOException>(() => document.SaveAsync(temp.File("target.txt"))));
        var evidence = SaveFailureInfo.FromException(primary)!;
        Assert.Equal(SaveFileOutcome.Unknown, evidence.TargetOutcome);
        Assert.Equal(SaveFileOutcome.Unknown, evidence.RecoveryOutcome);
        Assert.Equal(inspection.HResult, evidence.SecondaryHResult);
        Assert.Equal("unique staged bytes", await File.ReadAllTextAsync(document.PendingSaveRecovery!.Path));
        Assert.True(document.IsModified);
    }

    /// <summary>A rejecting exception Data collection cannot hide the original commit failure or lose stage ownership.</summary>
    [Fact]
    public async Task Failed_exception_annotation_preserves_primary_and_recovery()
    {
        using var temp = new RepoTemp();
        using var document = new Document("unique staged bytes");
        var primary = new RejectingDataException();
        document.SaveOperations = new FaultOperations { Commit = (_, _) => throw primary };
        Assert.Same(primary, await Assert.ThrowsAsync<RejectingDataException>(() => document.SaveAsync(temp.File("target.txt"))));
        Assert.Null(SaveFailureInfo.FromException(primary));
        Assert.Equal("unique staged bytes", await File.ReadAllTextAsync(document.PendingSaveRecovery!.Path));
        Assert.True(document.IsModified);
    }

    /// <summary>Creates a complete owned failed stage through the new-file commit boundary.</summary>
    private static async Task<Document> FailedNewDocumentAsync(RepoTemp temp)
    {
        var document = new Document("staged bytes")
        {
            SaveOperations = new FaultOperations { Commit = (_, _) => throw Failure(1176) }
        };
        await Assert.ThrowsAsync<IOException>(() => document.SaveAsync(temp.File("target.txt")));
        return document;
    }

    /// <summary>Builds a Win32-facility HRESULT without claiming OS reproduction.</summary>
    private static IOException Failure(int code) => new("injected save failure", unchecked((int)(0x80070000u | (uint)code)));

    /// <summary>Simulates a provider exception whose diagnostic dictionary is unavailable.</summary>
    private sealed class RejectingDataException : IOException
    {
        public override IDictionary Data => throw new SecurityException("exception data unavailable");
    }

    /// <summary>Per-document fake operations preserve normal behavior outside the injected boundary.</summary>
    private sealed class FaultOperations : DocumentSaveOperations
    {
        internal Action<string, string>? Commit { get; init; }
        internal IOException? CleanupFailure { get; init; }
        internal IOException? SavedStampFailure { get; init; }
        internal Exception? InspectionFailure { get; init; }
        internal int CommitCount { get; private set; }

        internal override void Replace(string stage, string target)
        {
            CommitCount++;
            if (Commit is not null) Commit(stage, target);
            else base.Replace(stage, target);
        }

        internal override void Move(string stage, string target)
        {
            CommitCount++;
            if (Commit is not null) Commit(stage, target);
            else base.Move(stage, target);
        }

        internal override void Delete(string stage)
        {
            if (CleanupFailure is not null) throw CleanupFailure;
            base.Delete(stage);
        }

        internal override FileStamp ReadSavedStamp(string target) =>
            SavedStampFailure is null ? base.ReadSavedStamp(target) : throw SavedStampFailure;

        internal override Task<(SaveFileOutcome Outcome, int? Error)> InspectAsync(
            string path, byte[]? original, byte[]? saved) =>
            InspectionFailure is null ? base.InspectAsync(path, original, saved) : throw InspectionFailure;
    }
}
