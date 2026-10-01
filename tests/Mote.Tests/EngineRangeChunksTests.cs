using System.Runtime.InteropServices;
using Mote.Engine;

namespace Mote.Tests;

/// <summary>Copy-free range traversal, code-unit boundaries, and retained snapshot contracts.</summary>
public sealed class EngineRangeChunksTests
{
    /// <summary>Nested and edited ropes produce exact ranges and reuse the original leaf strings.</summary>
    [Fact]
    public void Nested_rope_ranges_match_source_and_reuse_leaf_strings()
    {
        const int leafLength = 16 * 1024;
        var source = string.Concat(Enumerable.Range(0, 17).Select(i => new string((char)('a' + i), leafLength)));
        using var document = new Document(source);
        document.Apply(new TextChange(leafLength * 7 + 19, 3, "inserted"));
        var snapshot = document.Snapshot;
        source = source.Remove(leafLength * 7 + 19, 3).Insert(leafLength * 7 + 19, "inserted");
        var ranges = new (int Start, int Length)[]
        {
            (0, snapshot.Length), (0, 1), (leafLength, leafLength),
            (leafLength - 1, 2), (leafLength * 7 + 18, 11),
            (leafLength * 14 + 3, leafLength * 2 + 5), (snapshot.Length - 1, 1)
        };
        var leaves = snapshot.GetChunks().ToArray();
        foreach (var (start, length) in ranges)
        {
            var slices = snapshot.GetChunks(start, length).ToArray();
            Assert.Equal(source.Substring(start, length), Join(slices));
            Assert.Equal(length, slices.Sum(slice => slice.Length));
            Assert.All(slices, slice => AssertSharedLeaf(slice, leaves));
        }
        var random = new Random(7182);
        for (var i = 0; i < 150; i++)
        {
            var start = random.Next(snapshot.Length + 1);
            var length = random.Next(snapshot.Length - start + 1);
            Assert.Equal(source.Substring(start, length), Join(snapshot.GetChunks(start, length)));
        }
    }

    /// <summary>Reads preserve CRLF and surrogate code units even when split at leaf boundaries.</summary>
    [Theory]
    [InlineData("\r\n")]
    [InlineData("😀")]
    public void Range_boundaries_preserve_split_code_units(string pair)
    {
        using var document = new Document(new string('x', 16_383) + pair + "tail");
        var snapshot = document.Snapshot;
        Assert.Equal(2, snapshot.GetChunks(16_383, 2).Count());
        Assert.Equal(pair, Join(snapshot.GetChunks(16_383, 2)));
        Assert.Equal(pair[..1], Join(snapshot.GetChunks(16_383, 1)));
        Assert.Equal(pair[1..], Join(snapshot.GetChunks(16_384, 1)));
    }

    /// <summary>All valid empty ranges, including empty documents and end offsets, yield no slices.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void Zero_length_ranges_return_no_slices(string text)
    {
        using var document = new Document(text);
        for (var start = 0; start <= text.Length; start++)
            Assert.Empty(document.Snapshot.GetChunks(start, 0));
        Assert.Equal(Join(document.Snapshot.GetChunks()), Join(document.Snapshot.GetChunks(0, text.Length)));
    }

    /// <summary>Invalid extents fail at the call site, including overflow-shaped positive inputs.</summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(4, 0)]
    [InlineData(3, 1)]
    [InlineData(1, 3)]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(1, int.MaxValue)]
    public void Invalid_ranges_throw_before_enumeration(int start, int length)
    {
        using var document = new Document("abc");
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = document.Snapshot.GetChunks(start, length); });
    }

    /// <summary>Deferred sequences and yielded memories retain their version through edits and disposal.</summary>
    [Fact]
    public void Range_sequence_retains_original_snapshot_after_edits_and_disposal()
    {
        var document = new Document(new string('a', 20_000) + "original");
        var before = document.Snapshot;
        var deferred = before.GetChunks(16_380, before.Length - 16_380);
        var memories = deferred.ToArray();
        var expected = Join(memories);
        var after = document.Apply(new TextChange(16_380, before.Length - 16_380, "replacement"));
        Assert.True(document.Undo());
        Assert.True(document.Redo());
        Assert.True(after.Version > before.Version);
        Assert.True(document.Snapshot.Version > after.Version);
        document.Dispose();
        Assert.Equal(expected, Join(deferred));
        Assert.Equal(expected, Join(memories));
        Assert.Equal("replacement", Join(after.GetChunks(16_380, after.Length - 16_380)));
    }

    /// <summary>Asserts string identity rather than merely equivalent copied contents.</summary>
    private static void AssertSharedLeaf(ReadOnlyMemory<char> slice, ReadOnlyMemory<char>[] leaves)
    {
        Assert.False(slice.IsEmpty);
        Assert.True(MemoryMarshal.TryGetString(slice, out var text, out var start, out var length));
        Assert.Contains(leaves, leaf => MemoryMarshal.TryGetString(leaf, out var leafText, out var leafStart, out var leafLength)
            && ReferenceEquals(text, leafText) && start >= leafStart && start + length <= leafStart + leafLength);
    }

    /// <summary>Materializes test output only; the production traversal never concatenates slices.</summary>
    private static string Join(IEnumerable<ReadOnlyMemory<char>> chunks) => string.Concat(chunks.Select(chunk => chunk.ToString()));
}
