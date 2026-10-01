using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Large Markdown fence certification and dependency-isolation contracts.</summary>
public sealed class MarkdownFenceAdmissionTests
{
    private const int LargeLength = 17 * 1024 * 1024;

    /// <summary>A cold visible request stays bounded; idle Full can certify the same unchanged version.</summary>
    [Fact]
    public void Cold_visible_is_provisional_then_full_certifies_fences()
    {
        var (source, _) = Corpus("\n");
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)new MarkdownPolicy()).CreateSession();
        var visible = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 100), AnalysisScope.Visible));
        Assert.Equal(AnalysisCompleteness.Provisional, visible.Completeness);
        Assert.True(visible.Coverage.Length < source.Length);
        Assert.Null(visible.TotalDiagnosticCount);

        var full = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 100), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, full.Completeness);
        Assert.Equal(new TextSpan(0, source.Length), full.Coverage);
        Assert.Equal(0, full.TotalDiagnosticCount);

        // Once certified, a visible request can reuse the complete index.
        var cached = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(source.Length - 100, 32), AnalysisScope.Visible));
        Assert.Equal(AnalysisCompleteness.Complete, cached.Completeness);
    }

    /// <summary>Closed, blank-separated fences preserve Markdig's source-spanned IR for both newline styles.</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Closed_fences_are_complete_and_match_legacy_visible_ir(string newline)
    {
        var (source, unit) = Corpus(newline);
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)new MarkdownPolicy()).CreateSession();
        var offset = source.Length - unit.Length + unit.IndexOf("```json", StringComparison.Ordinal) + 12;
        var request = new AnalysisRequest(new TextSpan(offset, 32), AnalysisScope.Full);
        var result = session.Analyze(document.Snapshot, [], request);

        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(new TextSpan(0, source.Length), result.Coverage);
        Assert.Equal(0, result.TotalDiagnosticCount);
        var oracle = new MarkdownPolicy().Analyze(source);
        var expected = oracle.Root.Children.Where(node =>
            node.Span.Start < request.VisibleRange.End && node.Span.End >= request.VisibleRange.Start);
        Assert.Equal(expected.Select(Signature), result.Root.Children.Select(Signature));
        Assert.Equal(oracle.Tokens.Where(token =>
            token.Span.Start < request.VisibleRange.End && token.Span.End >= request.VisibleRange.Start)
            .Select(token => (token.Kind, token.Span)),
            result.Tokens.Select(token => (token.Kind, token.Span)));
    }

    /// <summary>An early closer revokes completeness; removing it restores the same session's certificate.</summary>
    [Fact]
    public void Early_closer_revokes_and_repair_restores_complete_analysis()
    {
        var (source, _) = Corpus("\n");
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)new MarkdownPolicy()).CreateSession();
        var request = new AnalysisRequest(new TextSpan(0, 100), AnalysisScope.Full);
        Assert.Equal(AnalysisCompleteness.Complete, session.Analyze(document.Snapshot, [], request).Completeness);

        var insertion = "\n```\n";
        var before = document.Snapshot;
        var edit = new TextChange(source.IndexOf("```json", StringComparison.Ordinal) + 120, 0, insertion);
        var after = document.Apply(edit);
        var broken = session.Analyze(after, [new VersionedEdit(before.Version, after.Version, edit)], request);
        Assert.Equal(AnalysisCompleteness.Provisional, broken.Completeness);
        Assert.Null(broken.TotalDiagnosticCount);

        before = after;
        edit = new TextChange(edit.Start, insertion.Length, string.Empty);
        after = document.Apply(edit);
        var repaired = session.Analyze(after, [new VersionedEdit(before.Version, after.Version, edit)], request);
        Assert.Equal(AnalysisCompleteness.Complete, repaired.Completeness);
        Assert.Equal(0, repaired.TotalDiagnosticCount);
    }

    /// <summary>An offscreen unclosed fence cannot inherit the other blocks' complete claim.</summary>
    [Fact]
    public void Offscreen_unclosed_fence_is_provisional()
    {
        var (source, _) = Corpus("\r\n");
        var closer = source.LastIndexOf("```", StringComparison.Ordinal);
        source = source.Remove(closer, 3).Insert(closer, "~~~");
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)new MarkdownPolicy()).CreateSession();
        var result = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 100), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.Null(result.TotalDiagnosticCount);
    }

    /// <summary>Builds many short independent code blocks without truncating a fence at EOF.</summary>
    private static (string Source, string Unit) Corpus(string newline)
    {
        var unit = "# Heading" + newline + newline + "```json" + newline +
            "[id]: https://example.org" + newline + new string('a', 4096) +
            newline + "```" + newline + newline;
        var count = LargeLength / unit.Length + 1;
        return (string.Concat(Enumerable.Repeat(unit, count)), unit);
    }

    /// <summary>Includes values and nested nodes, not just broad syntax categories.</summary>
    private static string Signature(SemanticNode node) =>
        $"{node.Kind}:{node.Span.Start}:{node.Span.Length}:{node.Name}:{node.Value}:[{string.Join(',', node.Children.Select(Signature))}]";
}
