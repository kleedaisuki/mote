using System.Text;
using Mote.Engine;

namespace Mote.Tests;

/// <summary>Observable document, snapshot, and persistence contract tests.</summary>
public sealed class EngineTests
{
    /// <summary>An untouched blank buffer needs no close warning; an edit or prefilling does.</summary>
    [Fact]
    public void New_blank_document_starts_clean_but_prefilled_document_is_dirty()
    {
        using var blank = new Document();
        Assert.False(blank.IsModified);
        blank.Apply(new TextChange(0, 0, "x"));
        Assert.True(blank.IsModified);
        using var prefilled = new Document("restored content");
        Assert.True(prefilled.IsModified);
    }

    /// <summary>UTF-16 indexing, CRLF line semantics, and terminal empty lines agree.</summary>
    [Fact]
    public void Snapshot_uses_utf16_offsets_and_logical_lf_lines()
    {
        using var document = new Document("A😀\r\nB\n");
        var snapshot = document.Snapshot;
        Assert.Equal(7, snapshot.Length);
        Assert.Equal(3, snapshot.LineCount);
        Assert.Equal("A😀", snapshot.GetLine(0));
        Assert.Equal("B", snapshot.GetLine(1));
        Assert.Equal("", snapshot.GetLine(2));
        Assert.Equal(5, snapshot.GetLineStartOffset(1));
        Assert.Equal(7, snapshot.GetLineStartOffset(2));
        Assert.Equal(0, snapshot.GetLineIndexFromOffset(4));
        Assert.Equal(1, snapshot.GetLineIndexFromOffset(5));
        Assert.Equal(2, snapshot.GetLineIndexFromOffset(snapshot.Length));
        Assert.Equal("😀", snapshot.GetText(1, 2));
    }

    /// <summary>A CRLF split across rope chunks is still one logical line break.</summary>
    [Fact]
    public void CrLf_crossing_chunk_boundary_indexes_as_one_break()
    {
        var firstLine = new string('a', 16_383);
        using var document = new Document(firstLine + "\r\nb");
        var snapshot = document.Snapshot;
        Assert.Equal(2, snapshot.LineCount);
        Assert.Equal(firstLine, snapshot.GetLine(0));
        Assert.Equal("b", snapshot.GetLine(1));
        Assert.Equal(16_385, snapshot.GetLineStartOffset(1));
        Assert.Equal(0, snapshot.GetLineIndexFromOffset(16_384));
        Assert.Equal(1, snapshot.GetLineIndexFromOffset(16_385));
    }

    /// <summary>Standalone CR forms a line break, but source text is not normalized.</summary>
    [Fact]
    public void Standalone_cr_is_a_line_break()
    {
        using var document = new Document("a\rb");
        Assert.Equal(2, document.Snapshot.LineCount);
        Assert.Equal("a", document.Snapshot.GetLine(0));
        Assert.Equal("b", document.Snapshot.GetLine(1));
        Assert.Equal("a\rb", document.Snapshot.GetText());
    }

    /// <summary>Offsets are UTF-16, but an edit may not split a surrogate pair.</summary>
    [Fact]
    public void Edit_rejects_half_a_surrogate_pair()
    {
        using var document = new Document("😀");
        var before = document.Snapshot;
        Assert.Throws<ArgumentException>(() => document.Apply(new TextChange(1, 1, "")));
        Assert.Same(before, document.Snapshot);
        Assert.Equal("😀", document.Snapshot.GetText());
    }

    /// <summary>An old snapshot remains valid through changes and document disposal.</summary>
    [Fact]
    public void Snapshots_are_immutable_and_versions_increase_through_history()
    {
        var document = new Document("abc");
        var original = document.Snapshot;
        var edited = document.Apply(new TextChange(1, 1, "😀"));
        Assert.Equal("a😀c", edited.GetText());
        Assert.Equal("abc", original.GetText());
        Assert.True(document.IsModified);
        Assert.True(document.Undo());
        var undone = document.Snapshot;
        Assert.Equal("abc", undone.GetText());
        Assert.True(document.Redo());
        Assert.Equal("a😀c", document.Snapshot.GetText());
        Assert.True(original.Version < edited.Version && edited.Version < undone.Version && undone.Version < document.Snapshot.Version);
        document.Dispose();
        Assert.Equal("abc", original.GetText());
        Assert.Equal("a😀c", edited.GetText());
        Assert.Throws<ObjectDisposedException>(() => document.Apply(new TextChange(0, 0, "x")));
    }

    /// <summary>Undo restores saved cleanliness, redo restores dirty state, and new edits truncate redo.</summary>
    [Fact]
    public async Task Undo_redo_track_saved_content_state()
    {
        using var temp = new RepoTemp();
        using var document = new Document("one");
        await document.SaveAsync(temp.File("note.txt"));
        Assert.False(document.IsModified);
        document.Apply(new TextChange(3, 0, " two"));
        Assert.True(document.IsModified);
        Assert.True(document.Undo());
        Assert.False(document.IsModified);
        Assert.True(document.Redo());
        Assert.True(document.IsModified);
        Assert.True(document.Undo());
        document.Apply(new TextChange(3, 0, " three"));
        Assert.False(document.CanRedo);
        Assert.Equal("one three", document.Snapshot.GetText());
    }

    /// <summary>Invalid source ranges are rejected without mutating the document.</summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(4, 0)]
    [InlineData(2, 2)]
    public void Invalid_edits_are_atomic(int start, int deleteLength)
    {
        using var document = new Document("abc");
        var before = document.Snapshot;
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Apply(new TextChange(start, deleteLength, "x")));
        Assert.Same(before, document.Snapshot);
        Assert.Equal("abc", document.Snapshot.GetText());
        Assert.False(document.CanUndo);
    }

    /// <summary>Rope edits agree with an independent .NET string model across varied boundaries.</summary>
    [Fact]
    public void Randomized_edits_match_string_model()
    {
        const string seed = "α😀\r\nβ\n";
        using var document = new Document(seed);
        var model = seed;
        var random = new Random(0x4D4F5445);
        var inserts = new[] { "", "x", "😀", "\n", "\r\n", "漢字" };
        for (var i = 0; i < 600; i++)
        {
            var start = random.Next(model.Length + 1);
            var deleteLength = random.Next(Math.Min(8, model.Length - start) + 1);
            var insertion = inserts[random.Next(inserts.Length)];
            if (SplitsPair(model, start) || SplitsPair(model, start + deleteLength))
            {
                var before = document.Snapshot;
                Assert.Throws<ArgumentException>(() => document.Apply(new TextChange(start, deleteLength, insertion)));
                Assert.Same(before, document.Snapshot);
                continue;
            }
            model = model.Remove(start, deleteLength).Insert(start, insertion);
            document.Apply(new TextChange(start, deleteLength, insertion));
            Assert.Equal(model, document.Snapshot.GetText());
            AssertLinesMatch(model, document.Snapshot, random);
        }
    }

    /// <summary>Large-file tail edits preserve distant text; correctness is independent of timing.</summary>
    [Fact]
    public void Large_file_tail_edit_preserves_prefix_and_line_index()
    {
        var original = string.Concat(Enumerable.Repeat("0123456789abcdef\n", 100_000));
        using var document = new Document(original);
        var old = document.Snapshot;
        var offset = original.Length - 3;
        document.Apply(new TextChange(offset, 2, "😀\n"));
        Assert.Equal(original[..offset] + "😀\n" + original[(offset + 2)..], document.Snapshot.GetText());
        Assert.Equal(original[..128], document.Snapshot.GetText(0, 128));
        Assert.Equal(original, old.GetText());
        Assert.Equal(CountLines(document.Snapshot.GetText()), document.Snapshot.LineCount);
    }

    /// <summary>Repeated head/tail edits preserve rope balance-related functional behavior.</summary>
    [Fact]
    public void Ten_thousand_alternating_endpoint_edits_preserve_text()
    {
        using var document = new Document();
        var expected = new StringBuilder();
        for (var i = 0; i < 10_000; i++)
        {
            if ((i & 1) == 0)
            {
                document.Apply(new TextChange(0, 0, "h"));
                expected.Insert(0, 'h');
            }
            else
            {
                document.Apply(new TextChange(document.Snapshot.Length, 0, "t"));
                expected.Append('t');
            }
        }
        Assert.Equal(expected.ToString(), document.Snapshot.GetText());
        Assert.Equal(10_000, document.Snapshot.Length);
    }

    /// <summary>Open/save without edits is byte-for-byte faithful for supported BOMs and CRLF.</summary>
    [Theory]
    [InlineData("utf8", false)]
    [InlineData("utf8", true)]
    [InlineData("utf16le", true)]
    [InlineData("utf16be", true)]
    [InlineData("utf32le", true)]
    [InlineData("utf32be", true)]
    public async Task Open_save_preserves_encoding_bom_and_newlines(string encodingName, bool bom)
    {
        using var temp = new RepoTemp();
        var path = temp.File("encoding.txt");
        const string content = "first😀\r\nsecond\r\n";
        Encoding encoding = encodingName switch
        {
            "utf16le" => new UnicodeEncoding(false, bom, true),
            "utf16be" => new UnicodeEncoding(true, bom, true),
            "utf32le" => new UTF32Encoding(false, bom, true),
            "utf32be" => new UTF32Encoding(true, bom, true),
            _ => new UTF8Encoding(bom, true)
        };
        var originalBytes = encoding.GetPreamble().Concat(encoding.GetBytes(content)).ToArray();
        await System.IO.File.WriteAllBytesAsync(path, originalBytes);
        using var document = await Document.OpenAsync(path);
        Assert.Equal(content, document.Snapshot.GetText());
        Assert.Equal(bom, document.HasByteOrderMark);
        Assert.False(document.IsModified);
        await document.SaveAsync();
        Assert.Equal(originalBytes, await System.IO.File.ReadAllBytesAsync(path));
    }

    /// <summary>Encoding preserves a surrogate pair even when rope chunks split its two code units.</summary>
    [Fact]
    public async Task Save_open_handles_surrogate_pair_crossing_rope_chunk()
    {
        using var temp = new RepoTemp();
        var path = temp.File("chunk.txt");
        var source = new string('a', 16_383) + "😀" + " tail";
        using (var document = new Document(source)) await document.SaveAsync(path);
        Assert.Equal(source, await System.IO.File.ReadAllTextAsync(path, new UTF8Encoding(false, true)));
        using var reopened = await Document.OpenAsync(path);
        Assert.Equal(source, reopened.Snapshot.GetText());
    }

    /// <summary>A pre-canceled save cannot replace existing user data.</summary>
    [Fact]
    public async Task Canceled_save_keeps_existing_target()
    {
        using var temp = new RepoTemp();
        var path = temp.File("existing.txt");
        await System.IO.File.WriteAllTextAsync(path, "original");
        using var document = new Document("replacement");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.SaveAsync(path, cancellation.Token));
        Assert.Equal("original", await System.IO.File.ReadAllTextAsync(path));
    }

    /// <summary>Save refuses to overwrite an externally changed file after open.</summary>
    [Fact]
    public async Task External_file_change_causes_save_conflict()
    {
        using var temp = new RepoTemp();
        var path = temp.File("conflict.txt");
        await System.IO.File.WriteAllTextAsync(path, "first");
        using var document = await Document.OpenAsync(path);
        document.Apply(new TextChange(5, 0, " local"));
        await System.IO.File.WriteAllTextAsync(path, "changed externally");
        var exception = await Assert.ThrowsAsync<IOException>(() => document.SaveAsync());
        Assert.Equal("TargetCheck", exception.Data["Mote.Engine.SavePhase"]);
        Assert.Equal("changed externally", await System.IO.File.ReadAllTextAsync(path));
        Assert.True(document.IsModified);
    }

    /// <summary>A held Windows target distinguishes replacement failure from verification without changing the original bytes.</summary>
    [Fact]
    public async Task Held_target_reports_replace_phase_without_losing_original()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new RepoTemp();
        var path = temp.File("held-target.txt");
        const string original = "original content";
        await File.WriteAllTextAsync(path, original);
        using var document = await Document.OpenAsync(path);
        document.Apply(new TextChange(original.Length, 0, " changed"));

        // Reads remain allowed for the final fingerprint, but replacement needs delete sharing.
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var exception = await Assert.ThrowsAsync<IOException>(() => document.SaveAsync());
            Assert.Equal(unchecked((int)0x80070020), exception.HResult);
            Assert.Equal("Replace", exception.Data["Mote.Engine.SavePhase"]);
            Assert.Equal(original, await File.ReadAllTextAsync(path));
            Assert.True(document.IsModified);
        }

        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, ".held-target.txt.*.tmp"));
    }

    /// <summary>Save As cannot silently overwrite a different existing file.</summary>
    [Fact]
    public async Task Save_as_existing_target_requires_explicit_overwrite_token()
    {
        using var temp = new RepoTemp();
        var target = temp.File("target.txt");
        await File.WriteAllTextAsync(target, "existing user data");
        using var document = new Document("new document");
        var exception = await Assert.ThrowsAsync<IOException>(() => document.SaveAsync(target));
        Assert.Equal("TargetCheck", exception.Data["Mote.Engine.SavePhase"]);
        Assert.Equal("existing user data", await File.ReadAllTextAsync(target));
        Assert.Null(document.FilePath);
        Assert.True(document.IsModified);
    }

    /// <summary>An approved overwrite changes file identity and persists the captured text.</summary>
    [Fact]
    public async Task Approved_save_over_updates_document_identity()
    {
        using var temp = new RepoTemp();
        var original = temp.File("original.txt");
        var target = temp.File("target.txt");
        await File.WriteAllTextAsync(target, "old target");
        using var document = new Document("new content");
        await document.SaveAsync(original);
        var token = await FileOverwriteToken.CaptureAsync(target);
        await document.SaveOverAsync(token);
        Assert.Equal("new content", await File.ReadAllTextAsync(target));
        Assert.Equal(Path.GetFullPath(target), document.FilePath);
        Assert.False(document.IsModified);
        Assert.Equal("new content", await File.ReadAllTextAsync(original));
    }

    /// <summary>A token cannot overwrite bytes changed after approval even if metadata is restored.</summary>
    [Fact]
    public async Task Overwrite_token_rejects_same_size_same_timestamp_change()
    {
        using var temp = new RepoTemp();
        var target = temp.File("target.txt");
        await File.WriteAllTextAsync(target, "AAAA");
        var token = await FileOverwriteToken.CaptureAsync(target);
        var timestamp = File.GetLastWriteTimeUtc(target);
        await File.WriteAllTextAsync(target, "BBBB");
        File.SetLastWriteTimeUtc(target, timestamp);
        using var document = new Document("CCCC");
        await Assert.ThrowsAsync<IOException>(() => document.SaveOverAsync(token));
        Assert.Equal("BBBB", await File.ReadAllTextAsync(target));
        Assert.Null(document.FilePath);
    }

    /// <summary>Cache entries are reused only for an unchanged snapshot.</summary>
    [Fact]
    public void Analysis_cache_invalidates_after_mutations()
    {
        using var document = new Document("a");
        var key = new object();
        var calls = 0;
        string Compute(TextSnapshot snapshot) { calls++; return snapshot.GetText(); }
        Assert.Equal("a", document.GetOrCompute(key, Compute));
        Assert.Equal("a", document.GetOrCompute(key, Compute));
        Assert.Equal(1, calls);
        document.Apply(new TextChange(1, 0, "b"));
        Assert.Equal("ab", document.GetOrCompute(key, Compute));
        Assert.Equal(2, calls);
        document.Undo();
        Assert.Equal("a", document.GetOrCompute(key, Compute));
        Assert.Equal(3, calls);
    }

    /// <summary>An analysis completed after a newer edit must not poison the current cache.</summary>
    [Fact]
    public async Task Stale_analysis_is_returned_only_to_original_caller()
    {
        using var document = new Document("old");
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var key = new object();
        var pending = Task.Run(() => document.GetOrCompute(key, snapshot =>
        {
            started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test analysis was not released.");
            return snapshot.GetText();
        }));
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)), "Background analysis did not start.");
        document.Apply(new TextChange(0, 3, "new"));
        release.Set();
        Assert.Equal("old", await pending);
        Assert.Equal("new", document.GetOrCompute(key, snapshot => snapshot.GetText()));
    }

    /// <summary>Concurrent edits publish a strictly ordered version chain to observers.</summary>
    [Fact]
    public void Concurrent_changed_events_follow_commit_order()
    {
        using var document = new Document();
        var observed = new List<DocumentChangedEventArgs>();
        var gate = new object();
        document.Changed += (_, change) =>
        {
            lock (gate) observed.Add(change);
        };
        Parallel.For(0, 1_000, _ => document.Apply(new TextChange(0, 0, "x")));
        Assert.Equal(1_000, observed.Count);
        for (var i = 0; i < observed.Count; i++)
        {
            Assert.Equal(i, observed[i].Before.Version);
            Assert.Equal(i + 1, observed[i].After.Version);
        }
        Assert.Equal(1_000, document.Snapshot.Length);
    }

    /// <summary>A handler may reenter Apply, but v2 notification must follow v1 completion.</summary>
    [Fact]
    public void Reentrant_changed_handler_preserves_notification_order()
    {
        using var document = new Document();
        var steps = new List<string>();
        document.Changed += (_, change) =>
        {
            steps.Add($"begin-{change.After.Version}");
            if (change.After.Version == 1) document.Apply(new TextChange(1, 0, "b"));
            steps.Add($"end-{change.After.Version}");
        };
        document.Apply(new TextChange(0, 0, "a"));
        Assert.Equal(new[] { "begin-1", "end-1", "begin-2", "end-2" }, steps);
        Assert.Equal("ab", document.Snapshot.GetText());
    }

    /// <summary>Counts CR, LF, and CRLF delimiters from a plain string reference model.</summary>
    private static int CountLines(string text)
    {
        var count = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                count++;
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else if (text[i] == '\n') count++;
        }
        return count;
    }

    /// <summary>Checks line text and coordinates against an independent sequential scanner.</summary>
    private static void AssertLinesMatch(string text, TextSnapshot snapshot, Random random)
    {
        var starts = new List<int> { 0 };
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            lines.Add(text[start..i]);
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
            starts.Add(start);
        }
        lines.Add(text[start..]);
        Assert.Equal(lines.Count, snapshot.LineCount);
        for (var line = 0; line < lines.Count; line++)
        {
            Assert.Equal(lines[line], snapshot.GetLine(line));
            Assert.Equal(starts[line], snapshot.GetLineStartOffset(line));
        }
        foreach (var offset in new[] { 0, text.Length, random.Next(text.Length + 1) })
        {
            var expected = starts.FindLastIndex(lineStart => lineStart <= offset);
            Assert.Equal(expected, snapshot.GetLineIndexFromOffset(offset));
        }
    }

    /// <summary>Detects a UTF-16 boundary between one high/low surrogate pair.</summary>
    private static bool SplitsPair(string text, int offset) =>
        offset > 0 && offset < text.Length && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]);
}
