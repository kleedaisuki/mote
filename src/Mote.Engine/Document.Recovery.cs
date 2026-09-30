using System.Security.Cryptography;
using System.Text;

namespace Mote.Engine;

public sealed partial class Document
{
    /// <summary>At most one owned staging slot, released only by explicit recovery actions.</summary>
    private SaveRecovery? _pendingSaveRecovery;

    /// <summary>Filesystem boundary; tests inject failures on one Document, never globally.</summary>
    internal DocumentSaveOperations SaveOperations { get; set; } = new();

    /// <summary>Gets the one owned retained stage. Dispose and ordinary discard never delete it.</summary>
    public SaveRecovery? PendingSaveRecovery { get { lock (_gate) { return _pendingSaveRecovery; } } }

    /// <summary>
    /// Computes the fixed per-target recovery slot. A hashed filename avoids exceeding
    /// filesystem component limits when the target filename is already near the limit.
    /// </summary>
    public static string GetSaveRecoveryPath(string targetPath)
    {
        targetPath = Path.GetFullPath(targetPath);
        var name = Path.GetFileName(targetPath);
        // APFS treats canonically equivalent Unicode spellings as the same filename.
        if (OperatingSystem.IsMacOS()) name = name.Normalize(NormalizationForm.FormC);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) name = name.ToUpperInvariant();
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
        return Path.Combine(Path.GetDirectoryName(targetPath)!, $".mote-save-{id}.recovery");
    }

    /// <summary>
    /// Exports the captured staged bytes to a new path distinct from the attempted target, verifies them,
    /// then releases the owned stage. Current buffer identity and dirty state are unchanged.
    /// Cancelled/failed exports retain the stage; an incomplete destination may remain.
    /// </summary>
    /// <example><code>await document.ExportSaveRecoveryAsync("recovered-copy.txt");</code></example>
    public async Task ExportSaveRecoveryAsync(string newPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPath);
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var recovery = GetOwnedRecovery();
            newPath = Path.GetFullPath(newPath);
            if (SameRecoveryTargetPath(newPath, recovery.AttemptedTargetPath))
                throw new IOException("Recovery export needs a different new path; it must not recreate the attempted Save target.");
            await VerifyRecoveryAsync(recovery).ConfigureAwait(false);
            await using (var source = new FileStream(recovery.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var target = new FileStream(newPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                target.Flush(flushToDisk: true);
            }
            var exported = await SaveRecovery.InspectAsync(newPath, null, recovery.Hash).ConfigureAwait(false);
            if (exported.Outcome != SaveFileOutcome.SavedSnapshot)
                throw new IOException("The exported recovery copy could not be verified; the stage was retained.");
            cancellationToken.ThrowIfCancellationRequested();
            await DeleteRecoveryAsync(recovery, cancellationToken).ConfigureAwait(false);
        }
        finally { _saveGate.Release(); }
    }

    /// <summary>Rejects common lexical target aliases conservatively, without claiming filesystem identity resolution.</summary>
    private static bool SameRecoveryTargetPath(string destination, string attemptedTarget)
    {
        if (OperatingSystem.IsMacOS())
        {
            destination = destination.Normalize(NormalizationForm.FormC);
            attemptedTarget = attemptedTarget.Normalize(NormalizationForm.FormC);
        }
        return string.Equals(destination, attemptedTarget,
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>Explicitly discards verified owned bytes or acknowledges their observed absence; never deletes the target.</summary>
    public async Task DiscardSaveRecoveryAsync(CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var recovery = GetOwnedRecovery();
            cancellationToken.ThrowIfCancellationRequested();
            var actual = await SaveRecovery.InspectAsync(recovery.Path, null, recovery.Hash).ConfigureAwait(false);
            if (actual.Outcome == SaveFileOutcome.Missing)
            {
                // Explicitly acknowledge externally removed bytes without deleting anything.
                lock (_gate) { _pendingSaveRecovery = null; }
                return;
            }
            await DeleteRecoveryAsync(recovery, cancellationToken).ConfigureAwait(false);
        }
        finally { _saveGate.Release(); }
    }

    /// <summary>Requires a live document and its own pending slot, not an existing orphan.</summary>
    private SaveRecovery GetOwnedRecovery()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return _pendingSaveRecovery ?? throw new InvalidOperationException("No owned Save recovery is pending.");
        }
    }

    /// <summary>Rejects changed/unknown bytes instead of deleting an externally replaced stage.</summary>
    private static async Task VerifyRecoveryAsync(SaveRecovery recovery)
    {
        if (recovery.Hash is null)
            throw new IOException("Incomplete recovery ownership cannot be verified; inspect the sidecar manually.");
        var actual = await SaveRecovery.InspectAsync(recovery.Path, null, recovery.Hash).ConfigureAwait(false);
        if (actual.Outcome != SaveFileOutcome.SavedSnapshot)
            throw new IOException("Recovery bytes changed or cannot be read; refusing to remove them.");
    }

    /// <summary>Deletes only after explicit action and re-verification; failures retain the pending slot.</summary>
    private async Task DeleteRecoveryAsync(SaveRecovery recovery, CancellationToken cancellationToken)
    {
        await VerifyRecoveryAsync(recovery).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        SaveOperations.Delete(recovery.Path);
        lock (_gate) { _pendingSaveRecovery = null; }
    }

    /// <summary>Failure-only inspection never replaces the primary exception or retries its commit.</summary>
    private async Task RecordSaveOutcomeAsync(Exception failure, string target, string stage, long version,
        byte[]? original, byte[]? saved, bool commitReturned, bool retainOwned, int? secondary)
    {
        // Establish ownership before any best-effort evidence work, including annotation.
        if (retainOwned)
            lock (_gate) { _pendingSaveRecovery = new SaveRecovery(stage, version, saved, target); }
        var targetResult = await InspectFailureOutcomeAsync(target, original, saved).ConfigureAwait(false);
        var stageResult = await InspectFailureOutcomeAsync(stage, null, saved).ConfigureAwait(false);
        if (retainOwned && stageResult.Outcome == SaveFileOutcome.Missing)
            lock (_gate) { _pendingSaveRecovery = null; }
        TrySetSaveFailureData(failure, SaveFailureInfo.DataKey,
            new SaveFailureInfo(target, stage, version, targetResult.Outcome, stageResult.Outcome,
                commitReturned, secondary ?? targetResult.Error ?? stageResult.Error));
    }

    /// <summary>Failure-only provider inspection always degrades to Unknown on nonfatal errors.</summary>
    private async Task<(SaveFileOutcome Outcome, int? Error)> InspectFailureOutcomeAsync(
        string path, byte[]? original, byte[]? saved)
    {
        try { return await SaveOperations.InspectAsync(path, original, saved).ConfigureAwait(false); }
        catch (Exception inspectionError) when (inspectionError is not OutOfMemoryException)
        {
            return (SaveFileOutcome.Unknown, inspectionError.HResult);
        }
    }
}
