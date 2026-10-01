using System.Security.Cryptography;

namespace Mote.Engine;

/// <summary>An observed byte outcome, not a guarantee against later external changes.</summary>
public enum SaveFileOutcome
{
    /// <summary>Access failed or the observation was unstable; absence must not be inferred.</summary>
    Unknown,
    /// <summary>The path was observed missing through an actual read/open error.</summary>
    Missing,
    /// <summary>The primary content stream matched the previously opened/approved bytes.</summary>
    OriginalSnapshot,
    /// <summary>The primary content stream matched the staged attempted snapshot.</summary>
    SavedSnapshot,
    /// <summary>Readable stable bytes matched neither available fingerprint.</summary>
    OtherContent
}

/// <summary>Local-only failure evidence. Paths and fingerprints must never enter telemetry.</summary>
public sealed record SaveFailureInfo(
    string TargetPath, string RecoveryPath, long SnapshotVersion,
    SaveFileOutcome TargetOutcome, SaveFileOutcome RecoveryOutcome,
    bool CommitReturned, int? SecondaryHResult = null)
{
    /// <summary>Typed local evidence key; never enumerate this into a trace event.</summary>
    internal const string DataKey = "Mote.Engine.SaveFailureInfo";

    /// <summary>Gets evidence attached to the original exception without wrapping it.</summary>
    public static SaveFailureInfo? FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try { return exception.Data[DataKey] as SaveFailureInfo; }
        catch (Exception evidenceError) when (evidenceError is not OutOfMemoryException)
        {
            return null;
        }
    }
}

/// <summary>One owned staged snapshot; later edits do not change these bytes or version.</summary>
public sealed class SaveRecovery
{
    /// <summary>Records only the owning attempt's completed hash; null means partial or unverified staging.</summary>
    internal SaveRecovery(string path, long version, byte[]? hash, string attemptedTargetPath)
    {
        Path = path;
        SnapshotVersion = version;
        Hash = hash;
        AttemptedTargetPath = attemptedTargetPath;
    }

    /// <summary>Absolute same-directory sidecar path; disposal does not remove it.</summary>
    public string Path { get; }

    /// <summary>The attempted absolute Save/Save As target; recovery export must not recreate this path.</summary>
    public string AttemptedTargetPath { get; }

    /// <summary>The attempted snapshot version, not necessarily the current buffer.</summary>
    public long SnapshotVersion { get; }

    /// <summary>Whether the completed snapshot was fingerprinted before commit.</summary>
    public bool IsCompleteSnapshot => Hash is not null;

    /// <summary>Immutable internal fingerprint used to refuse deletion/export of externally changed bytes.</summary>
    internal byte[]? Hash { get; }

    /// <summary>Reads actual bytes with stable stamps; access failures are Unknown, not Missing.</summary>
    internal static async Task<(SaveFileOutcome Outcome, int? Error)> InspectAsync(
        string path, byte[]? original, byte[]? saved)
    {
        try
        {
            var before = ReadObservationStamp(path);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
            if (before != ReadObservationStamp(path)) return (SaveFileOutcome.Unknown, null);
            if (saved is not null && hash.AsSpan().SequenceEqual(saved))
                return (SaveFileOutcome.SavedSnapshot, null);
            if (original is not null && hash.AsSpan().SequenceEqual(original))
                return (SaveFileOutcome.OriginalSnapshot, null);
            return (SaveFileOutcome.OtherContent, null);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return (SaveFileOutcome.Missing, null);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Outcome inspection is best effort; even unexpected security/provider errors
            // must not replace the Save exception whose evidence is being collected.
            return (SaveFileOutcome.Unknown, error.HResult);
        }
    }

    /// <summary>Unlike FileInfo.Exists, Length does not suppress access failures into false absence.</summary>
    private static FileStamp ReadObservationStamp(string path)
    {
        var file = new FileInfo(path);
        return new FileStamp(file.Length, file.LastWriteTimeUtc);
    }
}
