using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Independent contract checks for large Markdown admission and flat certification.</summary>
public sealed class MarkdownAdmissionValidationTests
{
    /// <summary>Whole-file parser admission limit expressed in UTF-16 code units.</summary>
    private const int SixteenMiB = 16 * 1024 * 1024;

    /// <summary>The 4 MiB admission edge is inclusive, while an over-limit long line is never globally certified.</summary>
    [Fact]
    public void Four_mib_exact_edge_and_first_rejected_code_unit()
    {
        const int fourMiB = 4 * 1024 * 1024;
        using var document = new Document(new string('a', fourMiB));
        using var session = Session();
        AssertComplete(document.Snapshot, Analyze(session, document.Snapshot, []));
        var before = document.Snapshot;
        var edit = new TextChange(before.Length, 0, "a");
        var after = document.Apply(edit);
        AssertProvisional(after, Analyze(session, after,
            [new VersionedEdit(before.Version, after.Version, edit)]));
    }

    /// <summary>A caller-provided whole-document viewport cannot force unbounded flat IR materialization.</summary>
    [Fact]
    public void Whole_document_visible_range_keeps_flat_projection_bounded()
    {
        var block = "Alpha " + new string('a', 4_200) + "\n\n";
        var source = string.Concat(Enumerable.Repeat(block, 4_100));
        Assert.True(source.Length > SixteenMiB);
        using var document = new Document(source);
        using var session = Session();
        var request = new AnalysisRequest(new TextSpan(0, source.Length), AnalysisScope.Full);
        var result = session.Analyze(document.Snapshot, [], request);
        AssertComplete(document.Snapshot, result);
        Assert.InRange(result.Root.Children.Count, 1, 2_048);
        // Whole blocks may cross the projection boundary, but starts and maximum block size are bounded.
        Assert.All(result.Root.Children, node =>
        {
            Assert.True(node.Span.Start <= 256 * 1024);
            Assert.True(node.Span.End <= 320 * 1024);
        });
        Assert.True(result.Root.Children.Count < 4_100);
    }

    /// <summary>Repairing a missing blank separator must invalidate the relational known-bad cache.</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Separator_repair_recertifies_after_cached_relational_obstruction(string newline)
    {
        var source = FlatSource(newline);
        var seam = source.IndexOf(newline + newline + "Alpha", source.Length / 2, StringComparison.Ordinal);
        Assert.True(seam >= 0);
        var removedAt = seam + newline.Length;
        using var document = new Document(source.Remove(removedAt, newline.Length));
        using var session = Session();
        AssertProvisional(document.Snapshot, Analyze(session, document.Snapshot, []));
        AssertProvisional(document.Snapshot, Analyze(session, document.Snapshot, []));

        var before = document.Snapshot;
        var edit = new TextChange(removedAt, 0, newline);
        var after = document.Apply(edit);
        AssertComplete(after, Analyze(session, after,
            [new VersionedEdit(before.Version, after.Version, edit)]));
    }

    /// <summary>Even offscreen Markdown syntax invalidates a whole-file flat certificate.</summary>
    [Theory]
    [InlineData("\n", "[ref]: https://example.org")]
    [InlineData("\r\n", "1. item")]
    [InlineData("\n", "```json")]
    [InlineData("\r\n", "Text [link](https://example.org)")]
    public void Offscreen_active_construct_cannot_be_complete(string newline, string construct)
    {
        var source = FlatSource(newline);
        var lastStart = source.LastIndexOf("Alpha ", StringComparison.Ordinal);
        source = source[..lastStart] + construct + source[(lastStart + construct.Length)..];
        using var document = new Document(source);
        using var session = Session();
        var result = Analyze(session, document.Snapshot, []);
        AssertProvisional(document.Snapshot, result);

        // A repeated request may use a known-bad-line cache, but must not promote its claim.
        AssertProvisional(document.Snapshot, Analyze(session, document.Snapshot, []));
    }

    /// <summary>Both newline conventions yield source-spanned visible nodes equal to a full Markdig parse.</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Certified_flat_projection_matches_full_parse_at_distant_viewports(string newline)
    {
        var source = FlatSource(newline);
        using var document = new Document(source);
        using var session = Session();
        var oracle = new MarkdownPolicy().Analyze(source);
        foreach (var offset in new[] { 4, source.Length / 2, source.Length - 100 })
        {
            var request = new AnalysisRequest(new TextSpan(offset, 8), AnalysisScope.Full);
            var actual = session.Analyze(document.Snapshot, [], request);
            AssertComplete(document.Snapshot, actual);
            var expected = oracle.Root.Children.Where(node =>
                node.Span.Start <= offset + 8 && node.Span.End >= offset).ToArray();
            Assert.Equal(expected.Select(NodeSignature), actual.Root.Children.Select(NodeSignature));
            Assert.Equal(oracle.Tokens.Where(token =>
                token.Span.Start <= offset + 8 && token.Span.End >= offset)
                .Select(token => (token.Kind, token.Span)),
                actual.Tokens.Select(token => (token.Kind, token.Span)));
        }
    }

    /// <summary>Versioned edits shift certified suffixes and never preserve stale completeness after syntax insertion.</summary>
    [Fact]
    public void Versioned_edit_shifts_distant_projection_and_unsafe_edit_revokes_certificate()
    {
        using var document = new Document(FlatSource("\r\n"));
        using var session = Session();
        AssertComplete(document.Snapshot, Analyze(session, document.Snapshot, []));

        var before = document.Snapshot;
        var edit = new TextChange(20, 0, "Z");
        var after = document.Apply(edit);
        var offset = after.Length - 100;
        var actual = Analyze(session, after, [new VersionedEdit(before.Version, after.Version, edit)], offset);
        AssertComplete(after, actual);
        var oracle = new MarkdownPolicy().Analyze(after.GetText());
        var expected = oracle.Root.Children.Where(node => node.Span.Start <= offset + 8 && node.Span.End >= offset);
        Assert.Equal(expected.Select(NodeSignature), actual.Root.Children.Select(NodeSignature));

        before = after;
        edit = new TextChange(21, 0, "[x]");
        after = document.Apply(edit);
        AssertProvisional(after, Analyze(session, after,
            [new VersionedEdit(before.Version, after.Version, edit)]));

        // An unrelated edit shifts the known bad line but cannot erase it.
        before = after;
        edit = new TextChange(after.Length - 100, 0, "Q");
        after = document.Apply(edit);
        AssertProvisional(after, Analyze(session, after,
            [new VersionedEdit(before.Version, after.Version, edit)]));
    }

    /// <summary>Cancellation cannot commit a new version or poison a valid edit-chain retry.</summary>
    [Fact]
    public void Canceled_flat_edit_can_be_retried()
    {
        using var document = new Document(FlatSource("\n"));
        using var session = Session();
        AssertComplete(document.Snapshot, Analyze(session, document.Snapshot, []));
        var before = document.Snapshot;
        var edit = new TextChange(8, 1, "Z");
        var after = document.Apply(edit);
        var chain = new[] { new VersionedEdit(before.Version, after.Version, edit) };
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => Analyze(session, after, chain, token: canceled.Token));
        AssertComplete(after, Analyze(session, after, chain));
    }

    /// <summary>A giant paste cannot reuse a formerly simple block to bypass parse admission.</summary>
    [Fact]
    public void Giant_insertion_into_cached_simple_block_is_provisional()
    {
        using var document = new Document("Alpha text\n");
        using var session = Session();
        AssertComplete(document.Snapshot, Analyze(session, document.Snapshot, []));
        var before = document.Snapshot;
        var edit = new TextChange(5, 0, new string('a', SixteenMiB + 1));
        var after = document.Apply(edit);
        AssertProvisional(after, Analyze(session, after,
            [new VersionedEdit(before.Version, after.Version, edit)]));
    }

    /// <summary>Creates a >16 MiB flat file with bounded single-line paragraphs and blank separators.</summary>
    private static string FlatSource(string newline)
    {
        var paragraph = "Alpha " + new string('a', 32_700) + newline + newline;
        var source = "# Heading" + newline + newline + string.Concat(Enumerable.Repeat(paragraph, 520));
        Assert.True(source.Length > SixteenMiB);
        return source;
    }

    /// <summary>Creates the public per-document policy session.</summary>
    private static IFormatSession Session() => new MarkdownPolicy().CreateSession();

    /// <summary>Requests Full semantics around one bounded viewport.</summary>
    private static DocumentAnalysis Analyze(IFormatSession session, TextSnapshot snapshot,
        IReadOnlyList<VersionedEdit> edits, int offset = 4, CancellationToken token = default) =>
        session.Analyze(snapshot, edits,
            new AnalysisRequest(new TextSpan(offset, Math.Min(8, snapshot.Length - offset)), AnalysisScope.Full), token);

    /// <summary>Checks the externally meaningful whole-file guarantee, not projection size.</summary>
    private static void AssertComplete(TextSnapshot snapshot, DocumentAnalysis result)
    {
        Assert.Equal(snapshot.Version, result.Version);
        Assert.Equal(new TextSpan(0, snapshot.Length), result.Coverage);
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(0, result.TotalDiagnosticCount);
    }

    /// <summary>Checks honest bounded semantics when global Markdown meaning is unknown.</summary>
    private static void AssertProvisional(TextSnapshot snapshot, DocumentAnalysis result)
    {
        Assert.Equal(snapshot.Version, result.Version);
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.Null(result.TotalDiagnosticCount);
        Assert.True(result.Coverage.Length <= 512 * 1024);
    }

    /// <summary>Compares immutable source-spanned public node fields with independent full parsing.</summary>
    private static (string Kind, TextSpan Span, string? Name, string? Value) NodeSignature(SemanticNode node) =>
        (node.Kind, node.Span, node.Name, node.Value);
}
