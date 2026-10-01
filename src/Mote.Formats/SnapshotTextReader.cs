using Mote.Engine;

namespace Mote.Formats;

/// <summary>
/// A bounded, cancellation-aware <see cref="TextReader"/> over immutable engine chunks.
/// It never creates a whole-document string and preserves UTF-16 source coordinates.
/// </summary>
internal sealed class SnapshotTextReader : TextReader
{
    private readonly IEnumerator<ReadOnlyMemory<char>> _chunks;
    private readonly CancellationToken _cancellationToken;
    private ReadOnlyMemory<char> _current;
    private int _chunkOffset;
    private int _absolute;
    private readonly int _end;
    private bool _disposed;

    /// <summary>Opens a range; full-document reads pass start 0 and snapshot.Length.</summary>
    internal SnapshotTextReader(TextSnapshot snapshot, int start, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (start < 0 || length < 0 || start > snapshot.Length || length > snapshot.Length - start)
            throw new ArgumentOutOfRangeException(nameof(length));
        _chunks = snapshot.GetChunks().GetEnumerator();
        _cancellationToken = cancellationToken;
        _end = start + length;
        Skip(start);
    }

    /// <inheritdoc />
    public override int Peek()
    {
        ThrowIfDisposed();
        _cancellationToken.ThrowIfCancellationRequested();
        return _absolute >= _end || !EnsureChunk() ? -1 : _current.Span[_chunkOffset];
    }

    /// <inheritdoc />
    public override int Read()
    {
        var value = Peek();
        if (value >= 0) { _chunkOffset++; _absolute++; }
        return value;
    }

    /// <inheritdoc />
    public override int Read(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (index < 0 || count < 0 || index > buffer.Length || count > buffer.Length - index)
            throw new ArgumentOutOfRangeException(nameof(count));
        return Read(buffer.AsSpan(index, count));
    }

    /// <inheritdoc />
    public override int Read(Span<char> buffer)
    {
        ThrowIfDisposed();
        var written = 0;
        while (!buffer.IsEmpty && _absolute < _end && EnsureChunk())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, Math.Min(_current.Length - _chunkOffset, _end - _absolute));
            _current.Span.Slice(_chunkOffset, count).CopyTo(buffer);
            _chunkOffset += count;
            _absolute += count;
            written += count;
            buffer = buffer[count..];
        }
        return written;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _chunks.Dispose();
            _disposed = true;
        }
        base.Dispose(disposing);
    }

    /// <summary>Skips an absolute source prefix without allocating it.</summary>
    private void Skip(int count)
    {
        while (count > 0 && EnsureChunk())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var take = Math.Min(count, _current.Length - _chunkOffset);
            _chunkOffset += take;
            _absolute += take;
            count -= take;
        }
        if (count != 0) throw new InvalidOperationException("Snapshot chunks ended before their declared length.");
    }

    /// <summary>Advances over exhausted rope chunks.</summary>
    private bool EnsureChunk()
    {
        while (_chunkOffset >= _current.Length)
        {
            if (!_chunks.MoveNext()) return false;
            _current = _chunks.Current;
            _chunkOffset = 0;
        }
        return true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
