using System.Text;
using Mote.Engine;

namespace Mote.Tests;

/// <summary>Independent logical-line contracts for scalar and LF-fast-path rope construction and queries.</summary>
public sealed class EngineLineBreakFastPathTests
{
    /// <summary>Every small CR/LF combination agrees with an independent whole-string line oracle.</summary>
    [Fact]
    public void Exhaustive_small_strings_preserve_line_and_offset_contracts()
    {
        for (var length = 0; length <= 8; length++)
        {
            var count = (int)Math.Pow(3, length);
            for (var seed = 0; seed < count; seed++)
            {
                var chars = new char[length];
                var value = seed;
                for (var i = 0; i < chars.Length; i++)
                {
                    chars[i] = "x\r\n"[value % 3];
                    value /= 3;
                }
                using var document = new Document(new string(chars));
                AssertLines(document.Snapshot, new string(chars), exhaustiveOffsets: true);
            }
        }
    }

    /// <summary>UTF-16 offsets and CRLF transitions agree even when either pair straddles a leaf.</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("")]
    public void Fragmented_unicode_sources_preserve_every_line_and_boundary(string newline)
    {
        var text = new string('x', 16_383) + "😀" + newline + "é中" + newline +
            new string('y', 16_383) + "\r\n" + "尾\r\n";
        using var document = new Document(text);
        AssertLines(document.Snapshot, text, exhaustiveOffsets: true);
        Assert.Equal("😀", document.Snapshot.GetText(16_383, 2));
        Assert.Throws<ArgumentException>(() => document.Apply(new TextChange(16_384, 1, "x")));
    }

    /// <summary>Edits merge and split newline pairs while immutable snapshots survive Undo and Redo.</summary>
    [Fact]
    public void Boundary_edits_and_history_preserve_line_facts()
    {
        var text = new string('x', 16_383) + "\r\n" + "😀é中\n" + new string('y', 16_383) + "\rZ";
        using var document = new Document(text);
        var original = document.Snapshot;
        foreach (var change in new[] {
            new TextChange(16_384, 1, ""), new TextChange(16_384, 0, "\n"),
            new TextChange(16_383, 1, ""), new TextChange(16_383, 0, "\r"),
            new TextChange(0, 0, "😀\n"), new TextChange(0, 3, "中\r\n") })
        {
            var before = text;
            text = text[..change.Start] + change.InsertText + text[(change.Start + change.DeleteLength)..];
            var prior = document.Snapshot;
            document.Apply(change);
            AssertLines(document.Snapshot, text, exhaustiveOffsets: false);
            AssertLines(prior, before, exhaustiveOffsets: false);
            Assert.True(document.Undo());
            AssertLines(document.Snapshot, before, exhaustiveOffsets: false);
            Assert.True(document.Redo());
            AssertLines(document.Snapshot, text, exhaustiveOffsets: false);
        }
        AssertLines(original, new string('x', 16_383) + "\r\n" + "😀é中\n" + new string('y', 16_383) + "\rZ", false);
    }

    /// <summary>Bounded file decoding retains Unicode and line semantics for BOM and no-BOM encodings.</summary>
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    public async Task File_open_preserves_unicode_and_cross_chunk_lines(string encodingName)
    {
        var directory = Path.Combine(FindRoot(), ".temp", "tests", "engine-line-break-fastpath", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "synthetic.txt");
        var text = new string('x', 65_535) + "\r\n😀é中\n" + new string('y', 16_383) + "\r\n";
        Encoding encoding = encodingName switch
        {
            "utf16le" => new UnicodeEncoding(false, true, true),
            "utf16be" => new UnicodeEncoding(true, true, true),
            _ => new UTF8Encoding(false, true)
        };
        await File.WriteAllTextAsync(path, text, encoding);
        using var document = await Document.OpenAsync(path);
        AssertLines(document.Snapshot, text, exhaustiveOffsets: false);
    }

    /// <summary>Builds independent CR/LF/CRLF line starts without invoking rope metadata or normalized text.</summary>
    private static void AssertLines(TextSnapshot snapshot, string text, bool exhaustiveOffsets)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            starts.Add(i + 1);
        }
        Assert.Equal(text.Length, snapshot.Length);
        Assert.Equal(starts.Count, snapshot.LineCount);
        Assert.Equal(text, snapshot.GetText());
        for (var line = 0; line < starts.Count; line++)
        {
            var end = line + 1 == starts.Count ? text.Length : starts[line + 1];
            if (end > starts[line] && text[end - 1] == '\n') end--;
            if (end > starts[line] && text[end - 1] == '\r') end--;
            Assert.Equal(starts[line], snapshot.GetLineStartOffset(line));
            Assert.Equal(text[starts[line]..end], snapshot.GetLine(line));
        }
        var stride = exhaustiveOffsets ? 1 : Math.Max(1, text.Length / 1000);
        for (var offset = 0; offset <= text.Length; offset += stride)
            Assert.Equal(starts.FindLastIndex(start => start <= offset), snapshot.GetLineIndexFromOffset(offset));
        Assert.Equal(starts.Count - 1, snapshot.GetLineIndexFromOffset(text.Length));
    }

    /// <summary>Finds repository-local test scratch independently of the test host's working directory.</summary>
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "mote.sln"))) return directory.FullName;
        throw new InvalidOperationException("Repository root unavailable for synthetic test artifacts.");
    }
}
