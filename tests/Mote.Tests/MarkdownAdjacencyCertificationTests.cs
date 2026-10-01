using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Whole-file certification of adjacent, independently parsed Markdown blocks.</summary>
public sealed class MarkdownAdjacencyCertificationTests
{
    private const int LargeLength = 16 * 1024 * 1024 + 4096;

    /// <summary>Adjacent headings and paragraphs retain exact Markdig spans and semantic values.</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Large_adjacent_blocks_match_whole_document_oracle(string newline)
    {
        var source = Corpus(newline);
        using var document = new Document(source);
        using var session = Session();
        var offset = source.Length - 150;
        var request = new AnalysisRequest(new TextSpan(offset, 100), AnalysisScope.Full);
        var actual = session.Analyze(document.Snapshot, [], request);

        Assert.Equal(AnalysisCompleteness.Complete, actual.Completeness);
        Assert.Equal(new TextSpan(0, source.Length), actual.Coverage);
        Assert.Equal(0, actual.TotalDiagnosticCount);
        var oracle = new MarkdownPolicy().Analyze(source);
        var expected = oracle.Root.Children.Where(node =>
            node.Span.Start < request.VisibleRange.End && node.Span.End >= request.VisibleRange.Start);
        Assert.Equal(expected.Select(Signature), actual.Root.Children.Select(Signature));
        Assert.Equal(oracle.Tokens.Where(token =>
            token.Span.Start < request.VisibleRange.End && token.Span.End >= request.VisibleRange.Start)
            .Select(token => (token.Kind, token.Span)),
            actual.Tokens.Select(token => (token.Kind, token.Span)));
    }

    /// <summary>Local edits reuse a certificate, but a heading-to-paragraph merge revokes it.</summary>
    [Fact]
    public void Versioned_edit_preserves_or_revokes_adjacency_as_appropriate()
    {
        using var document = new Document(Corpus("\n"));
        using var session = Session();
        var request = new AnalysisRequest(new TextSpan(0, 64), AnalysisScope.Full);
        Assert.Equal(AnalysisCompleteness.Complete,
            session.Analyze(document.Snapshot, [], request).Completeness);

        var before = document.Snapshot;
        var change = new TextChange(3, 1, "X");
        var after = document.Apply(change);
        var edits = new[] { new VersionedEdit(before.Version, after.Version, change) };
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                session.Analyze(after, edits, request, canceled.Token));
        }
        var safe = session.Analyze(after, edits, request);
        Assert.Equal(AnalysisCompleteness.Complete, safe.Completeness);
        Assert.Equal(Signature(new MarkdownPolicy().Analyze(after.GetText()).Root.Children[0]),
            Signature(safe.Root.Children[0]));

        before = after;
        var secondHeading = after.GetText().IndexOf("# Child", StringComparison.Ordinal);
        change = new TextChange(secondHeading, 1, "A");
        after = document.Apply(change);
        var merged = session.Analyze(after,
            [new VersionedEdit(before.Version, after.Version, change)], request);
        Assert.Equal(AnalysisCompleteness.Provisional, merged.Completeness);
        Assert.Null(merged.TotalDiagnosticCount);

        before = after;
        change = new TextChange(secondHeading, 1, "#");
        after = document.Apply(change);
        var repaired = session.Analyze(after,
            [new VersionedEdit(before.Version, after.Version, change)], request);
        Assert.Equal(AnalysisCompleteness.Complete, repaired.Completeness);
    }

    /// <summary>Only the accepted adjacency relation is complete; two paragraph lines merge.</summary>
    [Fact]
    public void Two_adjacent_paragraph_lines_remain_provisional()
    {
        var source = Corpus("\n").Replace("# Child", "A Child", StringComparison.Ordinal);
        using var document = new Document(source);
        using var session = Session();
        var result = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 64), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.Null(result.TotalDiagnosticCount);
    }

    /// <summary>A closed fence and consecutive headings have the same global spans as Markdig.</summary>
    [Fact]
    public void Fence_to_heading_and_heading_to_heading_are_exact()
    {
        var source = "```\nopaque\n```\n# Top\n# Subtitle\n" + Corpus("\n");
        using var document = new Document(source);
        using var session = Session();
        var request = new AnalysisRequest(new TextSpan(0, 64), AnalysisScope.Full);
        var actual = session.Analyze(document.Snapshot, [], request);
        var oracle = new MarkdownPolicy().Analyze(source);
        Assert.Equal(AnalysisCompleteness.Complete, actual.Completeness);
        Assert.Equal(oracle.Root.Children.Where(node => node.Span.Start < 64)
            .Select(Signature), actual.Root.Children.Select(Signature));
    }

    /// <summary>Reference dependencies remain outside the certificate, even after an adjacent-heading prefix.</summary>
    [Fact]
    public void Offscreen_reference_use_and_definition_remain_provisional()
    {
        var source = Corpus("\n") + "\n[use][id]\n\n[id]: https://example.org\n";
        using var document = new Document(source);
        using var session = Session();
        var result = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 64), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.Null(result.TotalDiagnosticCount);
    }

    /// <summary>Builds a large valid document without blank separators around ATX headings.</summary>
    private static string Corpus(string newline)
    {
        var unit = "# Heading" + newline + "Alpha " + new string('a', 4096) + newline +
            "# Child" + newline + "Beta " + new string('b', 4096) + newline;
        return string.Concat(Enumerable.Repeat(unit, LargeLength / unit.Length + 1));
    }

    /// <summary>Creates an isolated session whose state cannot leak across cases.</summary>
    private static IFormatSession Session() =>
        ((IIncrementalDocumentPolicy)new MarkdownPolicy()).CreateSession();

    /// <summary>Compares recursive semantic structure, positions, names and values.</summary>
    private static string Signature(SemanticNode node) =>
        $"{node.Kind}:{node.Span.Start}:{node.Span.Length}:{node.Name}:{node.Value}:[{string.Join(',', node.Children.Select(Signature))}]";
}
