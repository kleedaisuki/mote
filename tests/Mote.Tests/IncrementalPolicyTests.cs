using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Versioned session contracts independent of the legacy whole-string API.</summary>
public sealed class IncrementalPolicyTests
{
    /// <summary>Plain text can assert complete semantics without copying any source string.</summary>
    [Fact]
    public void Plain_session_reports_complete_absolute_coverage_for_visible_request()
    {
        using var document = new Document("a😀\r\nb");
        var policy = Assert.IsAssignableFrom<IIncrementalDocumentPolicy>(
            DocumentPolicies.ForKind(DocumentKind.PlainText));
        using var session = policy.CreateSession();
        var snapshot = document.Snapshot;
        var result = session.Analyze(snapshot, [],
            new AnalysisRequest(new TextSpan(2, 1), AnalysisScope.Visible));
        Assert.Equal(snapshot.Version, result.Version);
        Assert.Equal(new TextSpan(0, snapshot.Length), result.Coverage);
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(0, result.TotalDiagnosticCount);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Tokens);
        Assert.Equal(snapshot.Length, result.Root.Span.Length);
    }

    /// <summary>Cancellation leaves the session usable; a history gap rebuilds from authority.</summary>
    [Fact]
    public void Plain_session_cancellation_and_edit_gap_do_not_publish_stale_version()
    {
        using var document = new Document("abc");
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.PlainText)).CreateSession();
        var original = document.Snapshot;
        var request = new AnalysisRequest(new TextSpan(0, 1), AnalysisScope.Visible);
        var first = session.Analyze(original, [], request);
        document.Apply(new TextChange(3, 0, "d"));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.Analyze(document.Snapshot, [], request, canceled.Token));
        document.Apply(new TextChange(4, 0, "e"));
        var rebuilt = session.Analyze(document.Snapshot, [], request);
        Assert.Equal(document.Snapshot.Version, rebuilt.Version);
        Assert.True(rebuilt.Version > first.Version);
        Assert.Equal(document.Snapshot.Length, rebuilt.Coverage.Length);
        Assert.Equal(AnalysisCompleteness.Complete, rebuilt.Completeness);
    }

    /// <summary>CSV record indexes agree with a fresh whole-document parser after structural edits.</summary>
    [Fact]
    public void Csv_session_matches_oracle_after_width_and_multiline_edits()
    {
        using var document = new Document("a,b\r\n\"x\r\ny\",2\r\nc,3\r\n");
        var policy = Assert.IsAssignableFrom<IIncrementalDocumentPolicy>(
            DocumentPolicies.ForKind(DocumentKind.Csv));
        using var session = policy.CreateSession();
        AssertEquivalent(policy.Analyze(document.Snapshot.GetText()), Full(session, document.Snapshot, []));

        var before = document.Snapshot;
        var width = new TextChange(0, 3, "a,b,c");
        var after = document.Apply(width);
        var widened = Full(session, after, [new VersionedEdit(before.Version, after.Version, width)]);
        AssertEquivalent(policy.Analyze(after.GetText()), widened);
        Assert.Equal(2, widened.TotalDiagnosticCount);

        before = after;
        var y = before.GetText().IndexOf('y');
        var content = new TextChange(y, 1, "Y");
        after = document.Apply(content);
        AssertEquivalent(policy.Analyze(after.GetText()),
            Full(session, after, [new VersionedEdit(before.Version, after.Version, content)]));

        // An omitted edit history is a gap, not permission to reuse stale row offsets.
        document.Apply(new TextChange(document.Snapshot.Length, 0, "d,4\n"));
        AssertEquivalent(policy.Analyze(document.Snapshot.GetText()), Full(session, document.Snapshot, []));
    }

    /// <summary>CSV completeness counts offscreen ragged rows even when projection is bounded.</summary>
    [Fact]
    public void Csv_large_visible_projection_keeps_exact_global_diagnostic_count()
    {
        var source = "a,b\n" + string.Concat(Enumerable.Repeat("1,2\n", 5_000));
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Csv)).CreateSession();
        var visible = new AnalysisRequest(new TextSpan(0, 4), AnalysisScope.Full);
        var initial = session.Analyze(document.Snapshot, [], visible);
        Assert.Equal(AnalysisCompleteness.Complete, initial.Completeness);
        Assert.Equal(0, initial.TotalDiagnosticCount);
        Assert.True(initial.Root.Children.Count < 5_001);

        var before = document.Snapshot;
        var change = new TextChange(0, 3, "a,b,c");
        var after = document.Apply(change);
        var changed = session.Analyze(after,
            [new VersionedEdit(before.Version, after.Version, change)], visible);
        Assert.Equal(AnalysisCompleteness.Complete, changed.Completeness);
        Assert.Equal(5_000, changed.TotalDiagnosticCount);
        Assert.Equal(new TextSpan(0, after.Length), changed.Coverage);
    }

    /// <summary>CSV cancellation does not advance the committed index version.</summary>
    [Fact]
    public void Csv_session_cancellation_allows_same_edit_chain_retry()
    {
        using var document = new Document("a,b\n1,2\n");
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Csv);
        using var session = policy.CreateSession();
        Full(session, document.Snapshot, []);
        var before = document.Snapshot;
        var change = new TextChange(4, 1, "3");
        var after = document.Apply(change);
        var edit = new VersionedEdit(before.Version, after.Version, change);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            Full(session, after, [edit], canceled.Token));
        AssertEquivalent(policy.Analyze(after.GetText()), Full(session, after, [edit]));
    }

    /// <summary>Seeded malformed and structural CSV edits stay equivalent to a fresh parse.</summary>
    [Fact]
    public void Csv_session_random_edits_match_whole_document_oracle()
    {
        using var document = new Document("a,b\r\n\"x\r\ny\",2\r\n3,4\n");
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Csv);
        using var session = policy.CreateSession();
        Full(session, document.Snapshot, []);
        var random = new Random(0x435356);
        var insertions = new[] { "", "x", ",", "\"", "\r", "\n", "\r\n" };
        for (var i = 0; i < 250; i++)
        {
            var before = document.Snapshot;
            var start = random.Next(before.Length + 1);
            var remove = random.Next(Math.Min(3, before.Length - start) + 1);
            var change = new TextChange(start, remove, insertions[random.Next(insertions.Length)]);
            var after = document.Apply(change);
            var actual = Full(session, after, [new VersionedEdit(before.Version, after.Version, change)]);
            AssertEquivalent(policy.Analyze(after.GetText()), actual);
        }
    }

    /// <summary>Local Markdown inline edits preserve whole-document semantic equivalence.</summary>
    [Fact]
    public void Markdown_session_matches_oracle_after_local_heading_and_inline_edits()
    {
        using var document = new Document("Intro *one* and [link](https://example.com)\n\n# Heading\n\nTail\n");
        var policy = Assert.IsAssignableFrom<IIncrementalDocumentPolicy>(
            DocumentPolicies.ForKind(DocumentKind.Markdown));
        using var session = policy.CreateSession();
        AssertEquivalent(policy.Analyze(document.Snapshot.GetText()), Full(session, document.Snapshot, []));
        var before = document.Snapshot;
        var change = new TextChange(before.GetText().IndexOf("one", StringComparison.Ordinal), 3, "two");
        var after = document.Apply(change);
        AssertEquivalent(policy.Analyze(after.GetText()),
            Full(session, after, [new VersionedEdit(before.Version, after.Version, change)]));
        before = after;
        change = new TextChange(before.GetText().IndexOf("Heading", StringComparison.Ordinal), 7, "Title");
        after = document.Apply(change);
        AssertEquivalent(policy.Analyze(after.GetText()),
            Full(session, after, [new VersionedEdit(before.Version, after.Version, change)]));
    }

    /// <summary>Markdown global references and fences force safe results, not stale local facts.</summary>
    [Theory]
    [InlineData("[foo][ref]\n\n[ref]: https://old.example\n", "old.example", "new.example")]
    [InlineData("```json\n{\"x\":1}\n```\n", "1", "2")]
    [InlineData("Text [link](https://example.com)\n", "]", "")]
    public void Markdown_session_matches_oracle_after_nonlocal_or_malformed_edit(
        string source, string oldText, string newText)
    {
        using var document = new Document(source);
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Markdown);
        using var session = policy.CreateSession();
        Full(session, document.Snapshot, []);
        var before = document.Snapshot;
        var change = new TextChange(source.IndexOf(oldText, StringComparison.Ordinal), oldText.Length, newText);
        var after = document.Apply(change);
        AssertEquivalent(policy.Analyze(after.GetText()),
            Full(session, after, [new VersionedEdit(before.Version, after.Version, change)]));
    }

    /// <summary>Large viewport Markdown results cannot claim complete semantics without a full parse.</summary>
    [Fact]
    public void Markdown_large_visible_analysis_is_explicitly_provisional()
    {
        using var document = new Document(new string('x', 5 * 1024 * 1024));
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Markdown)).CreateSession();
        var result = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(2 * 1024 * 1024, 100), AnalysisScope.Visible));
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.Null(result.TotalDiagnosticCount);
        Assert.True(result.Coverage.Length <= 512 * 1024);
        Assert.Equal(document.Snapshot.Version, result.Version);
    }

    /// <summary>Markdown canceled analysis cannot consume an edit chain or publish its version.</summary>
    [Fact]
    public void Markdown_session_cancellation_allows_retry()
    {
        using var document = new Document("# Heading\n\nParagraph\n");
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Markdown);
        using var session = policy.CreateSession();
        Full(session, document.Snapshot, []);
        var before = document.Snapshot;
        var change = new TextChange(2, 7, "Title");
        var after = document.Apply(change);
        var edit = new VersionedEdit(before.Version, after.Version, change);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => Full(session, after, [edit], canceled.Token));
        AssertEquivalent(policy.Analyze(after.GetText()), Full(session, after, [edit]));
    }

    /// <summary>Requests all semantics and projection for a small authoritative snapshot.</summary>
    private static DocumentAnalysis Full(IFormatSession session, TextSnapshot snapshot,
        IReadOnlyList<VersionedEdit> changes, CancellationToken cancellationToken = default) =>
        session.Analyze(snapshot, changes,
            new AnalysisRequest(new TextSpan(0, snapshot.Length), AnalysisScope.Full), cancellationToken);

    /// <summary>Compares source-anchored IR and diagnostics with the independent legacy parser.</summary>
    private static void AssertEquivalent(FormatAnalysis expected, DocumentAnalysis actual)
    {
        Assert.Equal(AnalysisCompleteness.Complete, actual.Completeness);
        Assert.Equal(expected.SourceText.Length, actual.Coverage.Length);
        Assert.Equal(expected.Diagnostics.Count, actual.TotalDiagnosticCount);
        Assert.Equal(Shape(expected.Root), Shape(actual.Root));
        Assert.Equal(expected.Diagnostics, actual.Diagnostics);
        Assert.Equal(expected.Tokens, actual.Tokens);
    }

    /// <summary>Serializes a semantic tree including absolute source spans.</summary>
    private static string[] Shape(SemanticNode node) =>
        new[] { $"{node.Kind}|{node.Name}|{node.Value}|{node.Span.Start}|{node.Span.Length}" }
            .Concat(node.Children.SelectMany(Shape)).ToArray();
}
