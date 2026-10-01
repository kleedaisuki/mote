namespace Mote.Engine;

/// <summary>
/// An immutable view of a document. Its chunked rope shares unchanged text with later
/// snapshots, so retaining a snapshot does not force a full copy on every keystroke.
/// </summary>
/// <remarks>
/// Offsets and lengths are UTF-16 code units. CR, LF, and CRLF each delimit one line;
/// all line-ending bytes are retained in the underlying text. A trailing delimiter creates
/// an empty final line, even if a CRLF straddles two rope chunks.
/// </remarks>
public sealed class TextSnapshot
{
    internal TextSnapshot(RopeNode? root, long version)
    {
        Root = root;
        Version = version;
    }

    internal RopeNode? Root { get; }

    /// <summary>Monotonically increasing mutation version, including undo and redo.</summary>
    public long Version { get; }

    /// <summary>Document length in UTF-16 code units.</summary>
    public int Length => Root?.Length ?? 0;

    /// <summary>Number of logical lines, including an empty final line after a trailing delimiter.</summary>
    public int LineCount => checked((Root?.Breaks ?? 0) + 1);

    /// <summary>Materializes all text. Prefer range or line reads for large files and viewports.</summary>
    public string GetText() => GetText(0, Length);

    /// <summary>Reads only the requested UTF-16 range without materializing the whole file.</summary>
    /// <example><code>string visible = document.Snapshot.GetText(start, length);</code></example>
    public string GetText(int start, int length)
    {
        ValidateRange(start, length);
        return RopeNode.Slice(Root, start, length);
    }

    /// <summary>Gets one line without its CR, LF, or CRLF delimiter.</summary>
    public string GetLine(int line)
    {
        var start = GetLineStartOffset(line);
        var end = line == LineCount - 1 ? Length : RopeNode.NthBreakEnd(Root!, line);
        if (end > start && RopeNode.CharAt(Root!, end - 1) == '\n') end--;
        if (end > start && RopeNode.CharAt(Root!, end - 1) == '\r') end--;
        return GetText(start, end - start);
    }

    /// <summary>Gets the UTF-16 offset of the first code unit in a line.</summary>
    public int GetLineStartOffset(int line)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        if (line >= LineCount) throw new ArgumentOutOfRangeException(nameof(line));
        return line == 0 ? 0 : RopeNode.NthBreakEnd(Root!, line - 1);
    }

    /// <summary>Finds the zero-based line containing an offset; the end offset belongs to the final line.</summary>
    public int GetLineIndexFromOffset(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (offset > Length) throw new ArgumentOutOfRangeException(nameof(offset));
        var breaks = RopeNode.BreaksBefore(Root, offset);
        if (offset > 0 && offset < Length && RopeNode.CharAt(Root!, offset - 1) == '\r' &&
            RopeNode.CharAt(Root!, offset) == '\n') breaks--;
        return breaks;
    }

    /// <summary>Enumerates underlying text chunks in order without copying them.</summary>
    /// <remarks>Chunks are immutable strings; consumers should not rely on their sizes or boundaries.</remarks>
    public IEnumerable<ReadOnlyMemory<char>> GetChunks()
    {
        foreach (var chunk in RopeNode.Chunks(Root)) yield return chunk.AsMemory();
    }

    /// <summary>Enumerates a UTF-16 range as slices of immutable leaf strings without copying text.</summary>
    /// <param name="start">Zero-based UTF-16 offset; the end offset is valid for an empty range.</param>
    /// <param name="length">Number of UTF-16 code units to enumerate.</param>
    /// <returns>Nonempty memory slices in source order, or an empty sequence for a zero-length range.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is negative or exceeds this snapshot.</exception>
    /// <remarks>
    /// Validation occurs when this method is called, not when enumeration begins. Traversal costs
    /// O(log n + k) for n rope leaves and k returned slices, with O(log n) traversal storage.
    /// Boundaries are code-unit boundaries and may split CRLF or surrogate pairs. Consumers must
    /// preserve decoder state across slices and must not rely on leaf sizes or boundaries.
    /// The sequence and returned memories remain valid after later edits or document disposal.
    /// </remarks>
    /// <example><code>foreach (var chunk in document.Snapshot.GetChunks(start, length)) Process(chunk.Span);</code></example>
    public IEnumerable<ReadOnlyMemory<char>> GetChunks(int start, int length)
    {
        ValidateRange(start, length);
        return RopeNode.Chunks(Root, start, length);
    }

    internal void ValidateRange(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (start > Length || length > Length - start)
            throw new ArgumentOutOfRangeException(nameof(length), "The range exceeds the snapshot.");
    }
}
