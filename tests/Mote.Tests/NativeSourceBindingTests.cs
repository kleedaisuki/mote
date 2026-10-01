using Mote.Engine;
using Mote.Formats;
using Mote.Native;

namespace Mote.Tests;

/// <summary>Portable contracts for the production full-source binding, using actual engine history.</summary>
public sealed class NativeSourceBindingTests
{
    /// <summary>Preparation cannot commit; the controller's one Apply creates exactly one history entry.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Preparation_is_pure_and_one_controller_commit_advances(bool crLf)
    {
        const string source = "甲\r\n乙\n丙\r丁😀尾";
        using var document = new Document(source);
        var binding = Bind(document, crLf);
        var before = document.Snapshot;
        var notifications = 0;
        document.Changed += (_, _) => notifications++;
        var display = binding.Installation.Projection.Display.Replace("丁😀", "改😁", StringComparison.Ordinal);
        var candidate = Candidate(binding, display);
        var prepared = Assert.IsType<NativeSourcePreparedEdit>(binding.Prepare(before, candidate));
        Assert.Same(before, document.Snapshot);
        Assert.Equal(source, document.Snapshot.GetText());
        Assert.False(document.CanUndo);
        Assert.Equal(0, notifications);
        var committed = document.Apply(Assert.IsType<TextChange>(prepared.Change));
        var next = binding.Advance(committed, prepared);
        Assert.Equal(source.Replace("丁😀", "改😁", StringComparison.Ordinal), committed.GetText());
        Assert.Same(committed, next.Installation.Snapshot);
        Assert.Equal(display, next.Installation.Projection.Display);
        Assert.Equal(1, notifications);
        Assert.Equal(1, committed.Version);
        Assert.Equal(binding.Installation.Nonce, next.Installation.Nonce);
        Assert.Null(binding.Prepare(committed, candidate));
        Assert.Null(next.Prepare(committed, candidate));
        var echo = Assert.IsType<NativeSourcePreparedEdit>(next.Prepare(committed, Candidate(next, display)));
        Assert.Null(echo.Change);
        Assert.True(document.Undo());
        Assert.Equal(source, document.Snapshot.GetText());
        Assert.False(document.Undo());
        Assert.True(document.Redo());
        Assert.Equal(committed.GetText(), document.Snapshot.GetText());
        Assert.False(document.Redo());
    }

    /// <summary>Generation, text version, installation nonce and snapshot identity are separate fences.</summary>
    [Theory]
    [InlineData("generation")]
    [InlineData("version")]
    [InlineData("nonce")]
    [InlineData("snapshot")]
    [InlineData("mutated")]
    public void Stale_candidate_is_rejected_without_mutation(string fence)
    {
        using var document = new Document("abc");
        using var other = new Document("abc");
        var binding = Bind(document);
        var candidate = Candidate(binding, "axc");
        if (fence == "generation") candidate = candidate with { Stamp = candidate.Stamp with { Generation = 8 } };
        if (fence == "version") candidate = candidate with { Stamp = candidate.Stamp with { Version = 8 } };
        if (fence == "nonce") candidate = candidate with { Nonce = 32 };
        if (fence == "mutated") document.Apply(new(0, 0, "!"));
        var current = fence == "snapshot" ? other.Snapshot : document.Snapshot;
        Assert.Null(binding.Prepare(current, candidate));
        Assert.Same(current, fence == "snapshot" ? other.Snapshot : document.Snapshot);
        Assert.Equal(fence == "mutated" ? "!abc" : "abc", document.Snapshot.GetText());
        Assert.False(other.CanUndo);
    }

    /// <summary>Repeated pure preparation is harmless; an unchanged selection update creates no history.</summary>
    [Fact]
    public void No_change_and_repeated_preparation_do_not_mutate()
    {
        using var document = new Document("abcdef");
        var binding = Bind(document);
        var candidate = Candidate(binding, "abcdef") with { Selection = new(1, 4, 1) };
        var first = Assert.IsType<NativeSourcePreparedEdit>(binding.Prepare(document.Snapshot, candidate));
        var second = Assert.IsType<NativeSourcePreparedEdit>(binding.Prepare(document.Snapshot, candidate));
        Assert.Null(first.Change);
        Assert.Null(second.Change);
        Assert.Same(binding.Installation.Projection, first.Projection);
        var next = binding.Advance(document.Snapshot, first);
        Assert.Equal(4, next.Installation.Anchor);
        Assert.Equal(1, next.Installation.Active);
        Assert.True(next.DirectionKnown);
        Assert.False(document.CanUndo);
        Assert.Equal(0, document.Snapshot.Version);
    }

    /// <summary>Shared surrogate halves must be included in a real scalar-safe engine replacement.</summary>
    [Theory]
    [InlineData("甲😀尾", "甲😁尾", false)]
    [InlineData("甲😀尾", "甲\U0001FA00尾", false)]
    [InlineData("甲\n😀\r尾", "甲\r\n😁\r\n尾", true)]
    public void Emoji_preparation_applies_exactly(string source, string display, bool crLf)
    {
        using var document = new Document(source);
        var binding = Bind(document, crLf);
        var prepared = Assert.IsType<NativeSourcePreparedEdit>(binding.Prepare(document.Snapshot, Candidate(binding, display)));
        var change = Assert.IsType<TextChange>(prepared.Change);
        AssertBoundary(source, change.Start);
        AssertBoundary(source, change.Start + change.DeleteLength);
        var committed = document.Apply(change);
        Assert.Equal(display, binding.Advance(committed, prepared).Installation.Projection.Display);
        Assert.True(document.Undo());
        Assert.Equal(source, document.Snapshot.GetText());
    }

    /// <summary>Invalid UTF-16 and embedded NUL are refused before canonical mutation.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(0xD83D)]
    [InlineData(0xDE00)]
    public void Unsupported_native_text_is_rejected(int codeUnit)
    {
        using var document = new Document("abc");
        var binding = Bind(document);
        var invalid = "a" + (char)codeUnit + "c";
        Assert.Null(binding.Prepare(document.Snapshot, Candidate(binding, invalid)));
        Assert.Equal("abc", document.Snapshot.GetText());
        Assert.False(document.CanUndo);
    }

    /// <summary>Native selections cannot point inside a synthesized newline or a UTF-16 scalar.</summary>
    [Theory]
    [InlineData("a\nb", true, 2, 2, null)]
    [InlineData("a😀b", false, 2, 2, null)]
    [InlineData("abc", false, -1, 1, null)]
    [InlineData("abc", false, 2, 1, null)]
    [InlineData("abc", false, 0, 4, null)]
    [InlineData("abc", false, 0, 3, 1)]
    public void Invalid_selection_is_rejected(string source, bool crLf, int start, int end, int? active)
    {
        using var document = new Document(source);
        var binding = Bind(document, crLf);
        var candidate = Candidate(binding, binding.Installation.Projection.Display) with { Selection = new(start, end, active) };
        Assert.Null(binding.Prepare(document.Snapshot, candidate));
        Assert.False(document.CanUndo);
    }

    /// <summary>Ordered ranges without a native endpoint witness never certify direction.</summary>
    [Theory]
    [InlineData(null, 1, 4, false)]
    [InlineData(1, 4, 1, true)]
    [InlineData(4, 1, 4, true)]
    public void Selection_direction_requires_actual_witness(int? witness, int anchor, int active, bool known)
    {
        using var document = new Document("abcdef");
        var binding = Bind(document);
        var candidate = Candidate(binding, "abcdef") with { Selection = new(1, 4, witness) };
        var prepared = Assert.IsType<NativeSourcePreparedEdit>(binding.Prepare(document.Snapshot, candidate));
        Assert.Equal(anchor, prepared.Anchor);
        Assert.Equal(active, prepared.Active);
        Assert.Equal(known, prepared.DirectionKnown);
        Assert.Equal(known, binding.Advance(document.Snapshot, prepared).DirectionKnown);
    }

    /// <summary>Missing, truncated or boundary-invalid import observations do not certify a replica.</summary>
    [Fact]
    public void Import_observation_must_match_entire_display_and_selection()
    {
        using var document = new Document("a\nb😀");
        var binding = Bind(document, true);
        Assert.False(binding.Matches(null));
        Assert.False(binding.Matches(new("a", new(0, 0), new TextSpan(0, 1))));
        var display = binding.Installation.Projection.Display;
        Assert.False(binding.Matches(new(display, new(2, 2), new TextSpan(0, display.Length))));
        Assert.True(binding.Matches(new(display, new(0, display.Length), new TextSpan(0, display.Length))));
    }

    /// <summary>Source mismatch cannot be acknowledged as an admitted engine successor.</summary>
    [Fact]
    public void Advance_refuses_uncommitted_prepared_text()
    {
        using var document = new Document("abc");
        var binding = Bind(document);
        var prepared = Assert.IsType<NativeSourcePreparedEdit>(binding.Prepare(document.Snapshot, Candidate(binding, "axc")));
        Assert.Throws<InvalidOperationException>(() => binding.Advance(document.Snapshot, prepared));
        Assert.False(document.CanUndo);
    }

    /// <summary>Equal text does not substitute for unchanged snapshot identity or one admitted version.</summary>
    [Fact]
    public void Advance_rejects_unrelated_unchanged_snapshot_and_wrong_successor_version()
    {
        using var document = new Document("abc");
        using var unrelated = new Document("abc");
        using var wrongVersion = new Document("axc");
        var binding = Bind(document);
        var unchanged = Assert.IsType<NativeSourcePreparedEdit>(binding.Prepare(document.Snapshot, Candidate(binding, "abc")));
        Assert.Throws<InvalidOperationException>(() => binding.Advance(unrelated.Snapshot, unchanged));
        var changed = Assert.IsType<NativeSourcePreparedEdit>(binding.Prepare(document.Snapshot, Candidate(binding, "axc")));
        Assert.Throws<InvalidOperationException>(() => binding.Advance(wrongVersion.Snapshot, changed));
        wrongVersion.Apply(new(0, 0, "!"));
        wrongVersion.Apply(new(0, 1, ""));
        Assert.Equal("axc", wrongVersion.Snapshot.GetText());
        Assert.Throws<InvalidOperationException>(() => binding.Advance(wrongVersion.Snapshot, changed));
        Assert.False(document.CanUndo);
    }

    /// <summary>Replacement output is checked by an independent string splice, not by the same diff algorithm.</summary>
    [Theory]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\rb", "a\r\nb")]
    [InlineData("a\r\nb", "a\rX\nb")]
    [InlineData("a\rX\nb", "a\r\nb")]
    [InlineData("😀\r\n😁", "😁\n😀")]
    [InlineData("\r\n", "\r")]
    [InlineData("\r", "\r\n")]
    public void Replacement_is_exact_and_boundary_safe_at_newline_seams(string before, string after)
    {
        AssertReplacement(before, after, false);
        AssertReplacement(before, after, true);
    }

    /// <summary>Deterministic mixed-atom cases expose seam interactions beyond hand-picked examples.</summary>
    [Fact]
    public void Randomized_engine_replacements_are_exact_and_boundary_safe()
    {
        var random = new Random(0x534F5552);
        var atoms = new[] { "a", "中", "😀", "😁", "\U0001FA00", "\r", "\n", "\r\n", "e\u0301" };
        for (var i = 0; i < 1_000; i++)
        {
            var before = string.Concat(Enumerable.Range(0, random.Next(0, 12)).Select(_ => atoms[random.Next(atoms.Length)]));
            var after = string.Concat(Enumerable.Range(0, random.Next(0, 12)).Select(_ => atoms[random.Next(atoms.Length)]));
            AssertReplacement(before, after, i % 2 == 0);
        }
    }

    /// <summary>Installs a stable nonzero identity without any native window or diagnostic binding.</summary>
    private static NativeSourceBinding Bind(Document document, bool crLf = false) =>
        new(document.Snapshot, 7, 31, crLf ? NativeLineEndingMode.CrLf : NativeLineEndingMode.Preserve, 0, 0);

    /// <summary>Uses a collapsed actual endpoint at the final display boundary.</summary>
    private static NativeSourceCandidate Candidate(NativeSourceBinding binding, string display) =>
        new(binding.Installation.Stamp, binding.Installation.Nonce, display, new(display.Length, display.Length, display.Length));

    /// <summary>Exercises a real engine version and independently verifies the native replacement contract.</summary>
    private static void AssertReplacement(string before, string after, bool crLf)
    {
        using var document = new Document(before);
        var binding = Bind(document, crLf);
        var snapshot = document.Apply(new(0, before.Length, after));
        var replacement = binding.Replacement(snapshot, 0, after.Length);
        var oldDisplay = replacement.Before.Projection.Display;
        var newDisplay = replacement.After.Projection.Display;
        var oldEnd = replacement.DisplayStart + replacement.DisplayDeleteLength;
        var newEnd = replacement.DisplayStart + replacement.DisplayInsert.Length;
        Assert.Equal(newDisplay, oldDisplay[..replacement.DisplayStart] + replacement.DisplayInsert + oldDisplay[oldEnd..]);
        AssertBoundary(oldDisplay, replacement.DisplayStart);
        AssertBoundary(oldDisplay, oldEnd);
        AssertBoundary(newDisplay, replacement.DisplayStart);
        AssertBoundary(newDisplay, newEnd);
        Assert.Equal(after, replacement.After.Projection.Source);
        Assert.Same(snapshot, replacement.After.Snapshot);
    }

    /// <summary>Scalar and CRLF boundaries are independently evaluated directly from UTF-16 units.</summary>
    private static void AssertBoundary(string text, int offset)
    {
        Assert.InRange(offset, 0, text.Length);
        if (offset == 0 || offset == text.Length) return;
        Assert.False(char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]));
        Assert.False(text[offset - 1] == '\r' && text[offset] == '\n');
    }
}
