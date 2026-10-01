using Mote.Engine;
using Mote.Native;

namespace Mote.Tests;

/// <summary>Native page differences must be real scalar-safe engine edits, not just string splices.</summary>
public sealed class NativeUnicodeEditDifferenceTests
{
    /// <summary>A shared high surrogate cannot remain outside the replacement.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shared_high_surrogate_replacement_applies(bool crLf)
    {
        AssertEdit("left😀right", "left😁right", "left😁right",
            crLf ? NativeLineEndingMode.CrLf : NativeLineEndingMode.Preserve);
    }

    /// <summary>A shared low surrogate cannot become an unchanged suffix inside a scalar.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shared_low_surrogate_replacement_applies(bool crLf)
    {
        AssertEdit("x\U0001F600z", "x\U0001FA00z", "x\U0001FA00z",
            crLf ? NativeLineEndingMode.CrLf : NativeLineEndingMode.Preserve);
    }

    /// <summary>Insertions, deletions and mixed delimiters retain exact untouched source units.</summary>
    [Theory]
    [InlineData("ab", "a😀b", "a😀b")]
    [InlineData("a😀b", "ab", "ab")]
    [InlineData("😀😁", "😁😀", "😁😀")]
    [InlineData("中😀\n文\r尾\r\n", "中😁\r\n文\r\n尾\r\n", "中😁\n文\r尾\r\n")]
    [InlineData("😀\ra\n😀", "😀\r\n\r\n😀", "😀\r\r\n😀")]
    [InlineData("a\0😀b", "a\0😁b", "a\0😁b")]
    public void Canonical_native_edits_preserve_source(string source, string display, string expected)
    {
        AssertEdit(source, display, expected, NativeLineEndingMode.CrLf);
    }

    /// <summary>Rope leaves and page offsets do not relax the engine scalar boundary contract.</summary>
    [Fact]
    public void Replacement_across_rope_leaf_boundary_applies()
    {
        var prefix = new string('x', 16_383);
        AssertEdit(prefix + "😀tail", prefix + "😁tail", prefix + "😁tail",
            NativeLineEndingMode.Preserve);
    }

    /// <summary>Malformed native UTF-16 must fail rather than silently lose or replace characters.</summary>
    [Theory]
    [InlineData(0xD83D, 1)]
    [InlineData(0xDE00, 1)]
    [InlineData(0xD83D, 2)]
    public void Malformed_native_text_remains_rejected(int codeUnit, int count)
    {
        // Construct at runtime: test-runner serialization can repair malformed UTF-16.
        var edited = "x" + new string((char)codeUnit, count) + "y";
        foreach (var mode in new[] { NativeLineEndingMode.Preserve, NativeLineEndingMode.CrLf })
        {
            const string source = "x😀y";
            var change = new NativeTextProjection(source, mode).Difference(edited);
            Assert.NotNull(change);
            using var document = new Document(source);
            Assert.Throws<ArgumentException>(() => document.Apply(change.Value));
            Assert.Equal(source, document.Snapshot.GetText());
        }
    }

    /// <summary>Independent scalar-boundary selections, including reverse selection order, apply exactly.</summary>
    [Fact]
    public void Randomized_scalar_selections_apply_to_document()
    {
        var random = new Random(0x5343414C);
        var atoms = new[] { "a", "中", "😀", "😁", "\U0001FA00", "\r", "\n", "\r\n", "\0" };
        var inserts = new[] { "", "中", "😁", "\U0001FA00", "\r\n", "😀\r\n😁", "\0" };
        for (var caseNumber = 0; caseNumber < 5_000; caseNumber++)
        {
            var source = string.Concat(Enumerable.Range(0, random.Next(1, 18))
                .Select(_ => atoms[random.Next(atoms.Length)]));
            var mode = caseNumber % 2 == 0 ? NativeLineEndingMode.CrLf : NativeLineEndingMode.Preserve;
            var projection = new NativeTextProjection(source, mode);
            var before = projection.Display;
            var boundaries = Enumerable.Range(0, before.Length + 1).Where(index =>
                index == 0 || index == before.Length ||
                !(char.IsHighSurrogate(before[index - 1]) && char.IsLowSurrogate(before[index])) &&
                !(before[index - 1] == '\r' && before[index] == '\n')).ToArray();
            var anchor = boundaries[random.Next(boundaries.Length)];
            var caret = boundaries[random.Next(boundaries.Length)];
            var left = Math.Min(anchor, caret);
            var right = Math.Max(anchor, caret);
            var edited = before[..left] + inserts[random.Next(inserts.Length)] + before[right..];
            var change = projection.Difference(edited);
            if (edited == before) { Assert.Null(change); continue; }
            Assert.NotNull(change);
            using var document = new Document(source);
            document.Apply(change.Value);
            Assert.Equal(edited, new NativeTextProjection(document.Snapshot.GetText(), mode).Display);
        }
    }

    /// <summary>Checks exact engine text, native reprojection, and history after an actual edit.</summary>
    private static void AssertEdit(string source, string display, string expected,
        NativeLineEndingMode mode)
    {
        var projection = new NativeTextProjection(source, mode);
        var change = projection.Difference(display);
        Assert.NotNull(change);
        using var document = new Document(source);
        document.Apply(change.Value);
        Assert.Equal(expected, document.Snapshot.GetText());
        Assert.Equal(display, new NativeTextProjection(document.Snapshot.GetText(), mode).Display);
        Assert.True(document.Undo());
        Assert.Equal(source, document.Snapshot.GetText());
        Assert.True(document.Redo());
        Assert.Equal(expected, document.Snapshot.GetText());
    }
}

