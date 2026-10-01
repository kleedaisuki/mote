using System.Security.Cryptography;

namespace Mote.Engine;

/// <summary>
/// Captures the exact target a caller has explicitly approved replacing during Save As.
/// A later save refuses replacement if the target changes after capture.
/// </summary>
/// <remarks>
/// Capture this only after a user has confirmed an overwrite prompt. Holding the token
/// does not lock the file; a final comparison protects against ordinary races, but a
/// portable atomic compare-and-replace primitive is not available.
/// </remarks>
public sealed class FileOverwriteToken
{
    private FileOverwriteToken(string path, FileStamp stamp, byte[] hash)
    {
        Path = path;
        Stamp = stamp;
        Hash = hash;
    }

    /// <summary>The absolute path approved for replacement.</summary>
    public string Path { get; }

    internal FileStamp Stamp { get; }

    internal byte[] Hash { get; }

    /// <summary>Records the current target fingerprint after an explicit overwrite decision.</summary>
    /// <example><code>
    /// var token = await FileOverwriteToken.CaptureAsync(chosenPath);
    /// await document.SaveOverAsync(token);
    /// </code></example>
    public static async Task<FileOverwriteToken> CaptureAsync(string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = System.IO.Path.GetFullPath(path);
        var before = FileStamp.Read(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        if (FileStamp.Read(path) != before)
            throw new IOException("The overwrite target changed while it was inspected.");
        return new FileOverwriteToken(path, before, hash);
    }
}

/// <summary>Cheap metadata prefilter paired with a strong content hash.</summary>
internal readonly record struct FileStamp(long Length, DateTime LastWriteUtc)
{
    internal static FileStamp Read(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("The document file no longer exists.", path);
        return new FileStamp(file.Length, file.LastWriteTimeUtc);
    }

    internal static FileStamp? ReadOrNull(string path) => File.Exists(path) ? Read(path) : null;
}
