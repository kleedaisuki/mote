using System.Text;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;

namespace Mote.Engine;

/// <summary>
/// Owns one open document: immutable snapshots, bounded edit history, file identity,
/// and analysis caching. Format policies remain independent of this mechanism.
/// </summary>
/// <remarks>
/// Mutations are serialized internally. <see cref="Changed"/> is delivered in version order
/// outside the lock, so handlers may read or mutate the document. A handler may observe a
/// newer <see cref="Snapshot"/> than its event's After snapshot, and must marshal to its
/// own UI thread when necessary. A disposed document rejects mutation and I/O, but
/// previously obtained snapshots remain valid.
/// </remarks>
public sealed class Document : IDisposable
{
    private const string SavePhaseDataKey = "Mote.Engine.SavePhase";
    private enum SavePhase { TargetCheck, TempWriteAndHash, FinalTargetCheck, Move, Replace, Cleanup, SavedStamp }

    private const int DefaultHistoryBudget = 32 * 1024 * 1024;
    private const int DefaultHistoryCount = 512;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly List<HistoryEntry> _undo = [];
    private readonly List<HistoryEntry> _redo = [];
    private readonly Dictionary<object, object> _cache = new();
    private readonly Queue<PendingMutation> _pendingChanges = new();
    private TextSnapshot _snapshot;
    private long _nextVersion;
    private long _nextStateId;
    private long _stateId;
    private long _savedStateId;
    private long _historyCost;
    private bool _disposed;
    private bool _notifying;
    private string? _filePath;
    private Encoding _encoding;
    private bool _hasBom;
    private FileStamp? _fileStamp;
    private byte[]? _fileHash;

    /// <summary>Creates an unsaved UTF-8 document from text.</summary>
    /// <example><code>using var document = new Document("hello\n");</code></example>
    public Document(string text = "") : this(CreateValidRoot(text), null, new UTF8Encoding(false, true), false)
    {
    }

    private Document(RopeNode? root, string? filePath, Encoding encoding, bool hasBom)
    {
        _snapshot = new TextSnapshot(root, 0);
        _filePath = filePath;
        _encoding = encoding;
        _hasBom = hasBom;
        _stateId = 0;
        // A blank untitled buffer is not a user edit; supplied nonempty text still needs saving.
        _savedStateId = filePath is null && root is not null ? -1 : 0;
    }

    /// <summary>Raised after each edit, undo, or redo, with immutable before/after snapshots.</summary>
    public event EventHandler<DocumentChangedEventArgs>? Changed;

    /// <summary>
    /// Raised before <see cref="Changed"/> for each mutation, carrying only snapshot
    /// references and UTF-16 lengths so large Undo/Redo needs no contiguous text copy.
    /// </summary>
    public event EventHandler<DocumentChangedRangeEventArgs>? ChangedRange;

    /// <summary>Gets the current immutable snapshot.</summary>
    public TextSnapshot Snapshot { get { lock (_gate) { ThrowIfDisposed(); return _snapshot; } } }

    /// <summary>Gets the associated absolute path, or null for a new document.</summary>
    public string? FilePath { get { lock (_gate) { ThrowIfDisposed(); return _filePath; } } }

    /// <summary>Gets a defensive copy of the encoding detected at open or used for saving.</summary>
    public Encoding Encoding { get { lock (_gate) { ThrowIfDisposed(); return (Encoding)_encoding.Clone(); } } }

    /// <summary>Whether the file's encoding has a byte-order mark.</summary>
    public bool HasByteOrderMark { get { lock (_gate) { ThrowIfDisposed(); return _hasBom; } } }

    /// <summary>Whether current content differs from the last successful save state.</summary>
    public bool IsModified { get { lock (_gate) { ThrowIfDisposed(); return _stateId != _savedStateId; } } }

    /// <summary>Whether a prior edit can be restored.</summary>
    public bool CanUndo { get { lock (_gate) { ThrowIfDisposed(); return _undo.Count > 0; } } }

    /// <summary>Whether an undone edit can be reapplied.</summary>
    public bool CanRedo { get { lock (_gate) { ThrowIfDisposed(); return _redo.Count > 0; } } }

    /// <summary>
    /// Opens a file using BOM detection and strict UTF-8 otherwise. Input is read in bounded
    /// chunks; opening a large file does not require a whole-file string allocation.
    /// </summary>
    /// <exception cref="DecoderFallbackException">The input is invalid for its detected encoding.</exception>
    public static async Task<Document> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = Path.GetFullPath(path);
        var before = FileStamp.Read(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var marker = new byte[4];
        var markerLength = await stream.ReadAtLeastAsync(marker, 4, false, cancellationToken).ConfigureAwait(false);
        var (encoding, markerSize) = DetectEncoding(marker.AsSpan(0, markerLength));
        stream.Position = markerSize;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(marker, 0, markerSize);
        var decoder = encoding.GetDecoder();
        var bytes = new byte[64 * 1024];
        var chars = new char[64 * 1024];
        var chunks = new List<string>();
        long length = 0;
        int count;
        while ((count = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(bytes, 0, count);
            var consumed = 0;
            while (consumed < count)
            {
                decoder.Convert(bytes, consumed, count - consumed, chars, 0, chars.Length, false,
                    out var bytesUsed, out var charsUsed, out _);
                if (bytesUsed == 0 && charsUsed == 0)
                    throw new IOException("The decoder did not make progress.");
                consumed += bytesUsed;
                AddChunk(chunks, chars, charsUsed, ref length);
            }
        }
        decoder.Convert(Array.Empty<byte>(), 0, 0, chars, 0, chars.Length, true,
            out _, out var finalChars, out _);
        AddChunk(chunks, chars, finalChars, ref length);
        var after = FileStamp.Read(path);
        if (after != before) throw new IOException("The file changed while it was being opened.");
        var document = new Document(RopeNode.FromChunks(chunks), path, encoding, markerSize > 0);
        document._fileStamp = after;
        document._fileHash = hash.GetHashAndReset();
        return document;
    }

    /// <summary>Applies a UTF-16 replacement and returns the new immutable snapshot.</summary>
    /// <example><code>document.Apply(new TextChange(5, 0, " world"));</code></example>
    public TextSnapshot Apply(TextChange change)
    {
        ArgumentNullException.ThrowIfNull(change.InsertText);
        TextSnapshot after;
        lock (_gate)
        {
            ThrowIfDisposed();
            _snapshot.ValidateRange(change.Start, change.DeleteLength);
            ValidateBoundary(_snapshot, change.Start);
            ValidateBoundary(_snapshot, change.Start + change.DeleteLength);
            ValidateWellFormed(change.InsertText);
            if (change.DeleteLength == 0 && change.InsertText.Length == 0) return _snapshot;
            var beforeState = _stateId;
            var afterState = checked(++_nextStateId);
            var beforeRoot = _snapshot.Root;
            var afterRoot = RopeNode.Replace(beforeRoot, change.Start, change.DeleteLength, change.InsertText);
            var range = new TextChangeRange(change.Start, change.DeleteLength, change.InsertText.Length);
            after = Commit(range, afterRoot, Changed is null ? null : change);
            _stateId = afterState;
            foreach (var undone in _redo) _historyCost -= undone.Cost;
            _redo.Clear();
            var entry = new HistoryEntry(beforeRoot, afterRoot, change.Start,
                change.DeleteLength, change.InsertText.Length, beforeState, afterState);
            _undo.Add(entry);
            _historyCost += entry.Cost;
            TrimHistory();
        }
        DrainChanges();
        return after;
    }

    /// <summary>Restores the previous content state; returns false when history is empty.</summary>
    public bool Undo()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_undo.Count == 0) return false;
            var entry = _undo[^1];
            TextChange? legacy = null;
            if (Changed is not null)
            {
                var restored = new TextSnapshot(entry.BeforeRoot, checked(_nextVersion + 1));
                legacy = new TextChange(entry.Start, entry.InsertLength,
                    restored.GetText(entry.Start, entry.DeleteLength));
            }
            Pop(_undo);
            Commit(new TextChangeRange(entry.Start, entry.InsertLength, entry.DeleteLength),
                entry.BeforeRoot, legacy);
            _stateId = entry.BeforeStateId;
            _redo.Add(entry);
        }
        DrainChanges();
        return true;
    }

    /// <summary>Reapplies an undone content state; returns false when redo history is empty.</summary>
    public bool Redo()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_redo.Count == 0) return false;
            var entry = _redo[^1];
            TextChange? legacy = null;
            if (Changed is not null)
            {
                var restored = new TextSnapshot(entry.AfterRoot, checked(_nextVersion + 1));
                legacy = new TextChange(entry.Start, entry.DeleteLength,
                    restored.GetText(entry.Start, entry.InsertLength));
            }
            Pop(_redo);
            Commit(new TextChangeRange(entry.Start, entry.DeleteLength, entry.InsertLength),
                entry.AfterRoot, legacy);
            _stateId = entry.AfterStateId;
            _undo.Add(entry);
        }
        DrainChanges();
        return true;
    }

    /// <summary>
    /// Returns a cached analysis of the current version or computes one outside the lock.
    /// A concurrent edit may supersede the result; callers can compare snapshot versions.
    /// </summary>
    public T GetOrCompute<T>(object key, Func<TextSnapshot, T> compute) where T : class
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(compute);
        TextSnapshot snapshot;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_cache.TryGetValue(key, out var cached)) return (T)cached;
            snapshot = _snapshot;
        }
        var result = compute(snapshot) ?? throw new InvalidOperationException("The analysis returned null.");
        lock (_gate)
        {
            if (!_disposed && ReferenceEquals(snapshot, _snapshot)) _cache[key] = result;
        }
        return result;
    }

    /// <summary>
    /// Writes a captured snapshot to a temporary file in the target directory, flushes it,
    /// then replaces the document's current target. A different path must be new; use
    /// <see cref="SaveOverAsync"/> with an explicit target token to replace an existing file.
    /// An edit during save remains marked modified.
    /// </summary>
    /// <remarks>Do not assume this operation preserves target file metadata such as ACLs.</remarks>
    public async Task SaveAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveCoreAsync(path, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    /// <summary>
    /// Saves over a target that the caller explicitly approved and fingerprinted.
    /// The operation fails rather than overwrites if that target changed since capture.
    /// </summary>
    public async Task SaveOverAsync(FileOverwriteToken target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveCoreAsync(target.Path, target, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async Task SaveCoreAsync(string? path, FileOverwriteToken? overwrite,
        CancellationToken cancellationToken)
    {
        TextSnapshot snapshot;
        Encoding encoding;
        bool hasBom;
        long stateId;
        FileStamp? expectedStamp;
        byte[]? expectedHash;
        string? originalPath;
        lock (_gate)
        {
            ThrowIfDisposed();
            path ??= _filePath;
            if (path is null) throw new InvalidOperationException("An unsaved document needs a target path.");
            snapshot = _snapshot;
            encoding = _encoding;
            hasBom = _hasBom;
            stateId = _stateId;
            expectedStamp = _fileStamp;
            expectedHash = _fileHash;
            originalPath = _filePath;
        }
        path = Path.GetFullPath(path);
        var samePath = originalPath is not null && string.Equals(path, originalPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        if (overwrite is not null)
        {
            expectedStamp = overwrite.Stamp;
            expectedHash = overwrite.Hash;
        }
        else if (!samePath)
        {
            expectedStamp = null;
            expectedHash = null;
        }
        try
        {
            if (expectedStamp is not null)
            {
                if (FileStamp.ReadOrNull(path) != expectedStamp)
                    throw new IOException("The target changed outside mote; refusing to overwrite it.");
            }
            else if (File.Exists(path))
            {
                throw new IOException("The Save As target already exists; explicit overwrite approval is required.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AnnotateSaveFailure(exception, SavePhase.TargetCheck);
            throw;
        }
        var directory = Path.GetDirectoryName(path)!;
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        byte[] savedHash;
        try
        {
            try
            {
                savedHash = await WriteTempAsync(snapshot, tempPath, encoding, hasBom, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AnnotateSaveFailure(exception, SavePhase.TempWriteAndHash);
                throw;
            }
            await CommitTempAsync(tempPath, path, expectedStamp, expectedHash, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AnnotateSaveFailure(exception, SavePhase.Cleanup);
                throw;
            }
        }
        FileStamp savedStamp;
        try
        {
            savedStamp = FileStamp.Read(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AnnotateSaveFailure(exception, SavePhase.SavedStamp);
            throw;
        }
        lock (_gate)
        {
            if (_disposed) return;
            _filePath = path;
            _fileStamp = savedStamp;
            _fileHash = savedHash;
            _savedStateId = stateId;
        }
    }

    /// <summary>Encodes a snapshot before replacement, then fingerprints the exact bytes written.</summary>
    private static async Task<byte[]> WriteTempAsync(TextSnapshot snapshot, string tempPath, Encoding encoding,
        bool hasBom, CancellationToken cancellationToken)
    {
        await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            using (var writer = new StreamWriter(stream, ForWrite(encoding, hasBom), 64 * 1024, leaveOpen: true))
            {
                foreach (var chunk in snapshot.GetChunks())
                    await writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            stream.Flush(flushToDisk: true);
        }
        await using var saved = new FileStream(tempPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(saved, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Rechecks overwrite identity just before replacement; new-file moves never overwrite.</summary>
    private static async Task CommitTempAsync(string tempPath, string path, FileStamp? expectedStamp,
        byte[]? expectedHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedStamp is null)
        {
            try
            {
                File.Move(tempPath, path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AnnotateSaveFailure(exception, SavePhase.Move);
                throw;
            }
            return;
        }
        if (expectedHash is null) throw new InvalidOperationException("An opened document lacks a content fingerprint.");
        try
        {
            await VerifyOriginalContentAsync(path, expectedStamp.Value, expectedHash, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AnnotateSaveFailure(exception, SavePhase.FinalTargetCheck);
            throw;
        }
        try
        {
            File.Replace(tempPath, path, null, ignoreMetadataErrors: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AnnotateSaveFailure(exception, SavePhase.Replace);
            throw;
        }
    }

    /// <summary>Attaches a bounded, content-free stage to the original filesystem exception.</summary>
    private static void AnnotateSaveFailure(Exception exception, SavePhase phase) =>
        exception.Data[SavePhaseDataKey] = phase.ToString();

    /// <summary>Ends the document lifetime. Snapshots already handed out remain readable.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _cache.Clear();
            _undo.Clear();
            _redo.Clear();
            _pendingChanges.Clear();
            Changed = null;
            ChangedRange = null;
        }
    }

    private TextSnapshot Commit(TextChangeRange change, RopeNode? root, TextChange? legacy)
    {
        var before = _snapshot;
        _snapshot = new TextSnapshot(root, checked(++_nextVersion));
        _cache.Clear();
        _pendingChanges.Enqueue(new PendingMutation(before, _snapshot, change, legacy));
        return _snapshot;
    }

    private void DrainChanges()
    {
        lock (_gate)
        {
            if (_notifying || _disposed) return;
            _notifying = true;
        }
        Exception? failure = null;
        while (true)
        {
            PendingMutation pending;
            EventHandler<DocumentChangedRangeEventArgs>? rangeHandler;
            EventHandler<DocumentChangedEventArgs>? legacyHandler;
            lock (_gate)
            {
                if (_pendingChanges.Count == 0)
                {
                    _notifying = false;
                    break;
                }
                pending = _pendingChanges.Dequeue();
                rangeHandler = ChangedRange;
                legacyHandler = Changed;
            }
            try
            {
                rangeHandler?.Invoke(this, new DocumentChangedRangeEventArgs(
                    pending.Before, pending.After, pending.Change));
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
            try
            {
                if (legacyHandler is not null)
                {
                    var change = pending.Legacy ?? new TextChange(pending.Change.Start,
                        pending.Change.DeleteLength,
                        pending.After.GetText(pending.Change.Start, pending.Change.InsertLength));
                    legacyHandler.Invoke(this, new DocumentChangedEventArgs(
                        pending.Before, pending.After, change));
                }
            }
            catch (Exception ex)
            {
                // A faulty observer must not strand the other event or later versions.
                failure ??= ex;
            }
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void TrimHistory()
    {
        // One oversized action is kept as an undo barrier; ordinary history stays bounded.
        HistoryEntry? protectedEntry = null;
        for (var i = _undo.Count - 1; i >= 0; i--)
            if (_undo[i].Cost > DefaultHistoryBudget)
            {
                protectedEntry = _undo[i];
                break;
            }
        var ordinaryCost = _historyCost - (protectedEntry?.Cost ?? 0);
        while (_undo.Count > DefaultHistoryCount || ordinaryCost > DefaultHistoryBudget)
        {
            if (_undo.Count == 0) break;
            var oldest = _undo[0];
            _historyCost -= oldest.Cost;
            _undo.RemoveAt(0);
            if (ReferenceEquals(oldest, protectedEntry))
            {
                protectedEntry = null;
                ordinaryCost = _historyCost;
            }
            else
            {
                ordinaryCost -= oldest.Cost;
            }
        }
    }

    private static HistoryEntry Pop(List<HistoryEntry> list)
    {
        var index = list.Count - 1;
        var item = list[index];
        list.RemoveAt(index);
        return item;
    }

    private static (Encoding Encoding, int BomLength) DetectEncoding(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
            return (new UTF32Encoding(false, false, true), 4);
        if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            return (new UTF32Encoding(true, false, true), 4);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(false, true), 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (new UnicodeEncoding(false, false, true), 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (new UnicodeEncoding(true, false, true), 2);
        return (new UTF8Encoding(false, true), 0);
    }

    private static Encoding ForWrite(Encoding encoding, bool hasBom) => encoding.CodePage switch
    {
        65001 => new UTF8Encoding(hasBom, true),
        1200 => new UnicodeEncoding(false, hasBom, true),
        1201 => new UnicodeEncoding(true, hasBom, true),
        12000 => new UTF32Encoding(false, hasBom, true),
        12001 => new UTF32Encoding(true, hasBom, true),
        _ => encoding,
    };

    private static void AddChunk(List<string> chunks, char[] chars, int count, ref long totalLength)
    {
        if (count == 0) return;
        totalLength += count;
        if (totalLength > int.MaxValue) throw new IOException("The file exceeds the supported UTF-16 document length.");
        for (var start = 0; start < count; start += RopeNode.ChunkSize)
            chunks.Add(new string(chars, start, Math.Min(RopeNode.ChunkSize, count - start)));
    }

    private static async Task VerifyOriginalContentAsync(string path, FileStamp expectedStamp,
        byte[] expectedHash, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actualHash = await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false);
        if (!actualHash.AsSpan().SequenceEqual(expectedHash) || FileStamp.Read(path) != expectedStamp)
            throw new IOException("The file changed outside mote; refusing to overwrite it.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static RopeNode? CreateValidRoot(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateWellFormed(text);
        return RopeNode.FromString(text);
    }

    private static void ValidateWellFormed(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (++i >= text.Length || !char.IsLowSurrogate(text[i]))
                    throw new ArgumentException("Text contains an unpaired high surrogate.", nameof(text));
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                throw new ArgumentException("Text contains an unpaired low surrogate.", nameof(text));
            }
        }
    }

    private static void ValidateBoundary(TextSnapshot snapshot, int offset)
    {
        if (offset > 0 && offset < snapshot.Length &&
            char.IsHighSurrogate(RopeNode.CharAt(snapshot.Root!, offset - 1)) &&
            char.IsLowSurrogate(RopeNode.CharAt(snapshot.Root!, offset)))
            throw new ArgumentException("The edit bisects a UTF-16 surrogate pair.", nameof(offset));
    }

    /// <summary>
    /// History retains roots, not contiguous text copies. The newest giant edit can be
    /// undone without retaining its clipboard string or materializing a deleted region.
    /// </summary>
    private sealed record HistoryEntry(RopeNode? BeforeRoot, RopeNode? AfterRoot,
        int Start, int DeleteLength, int InsertLength, long BeforeStateId, long AfterStateId)
    {
        internal long Cost => checked(((long)InsertLength + DeleteLength) * sizeof(char));
    }

    /// <summary>One queued mutation shared by both notification forms in version order.</summary>
    private sealed record PendingMutation(TextSnapshot Before, TextSnapshot After,
        TextChangeRange Change, TextChange? Legacy);
}
