using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>CSV cold-open staging and exact completion contracts.</summary>
public sealed class CsvColdAnalysisTests
{
    /// <summary>A first viewport is valid without waiting for the whole file index.</summary>
    [Fact]
    public void Cold_visible_result_is_bounded_then_full_request_completes()
    {
        using var document = new Document("a,b\r\n" + string.Concat(Enumerable.Repeat("1,2\r\n", 20_000)));
        using var session = ((IIncrementalDocumentPolicy)new CsvPolicy()).CreateSession();
        var snapshot = document.Snapshot;
        var range = new TextSpan(0, 10);

        var visible = session.Analyze(snapshot, [], new AnalysisRequest(range, AnalysisScope.Visible));
        Assert.Equal(AnalysisCompleteness.CoveredRegion, visible.Completeness);
        Assert.Null(visible.TotalDiagnosticCount);
        Assert.InRange(visible.Coverage.End, range.End, 64 * 1024 + 4096);
        Assert.Equal("a", visible.Root.Children[0].Children[0].Value);

        var full = session.Analyze(snapshot, [], new AnalysisRequest(range, AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, full.Completeness);
        Assert.Equal(new TextSpan(0, snapshot.Length), full.Coverage);
        Assert.Equal(0, full.TotalDiagnosticCount);
        Assert.True(full.Root.Children.Count < 20_001);

        var oversizedRange = session.Analyze(snapshot, [],
            new AnalysisRequest(new TextSpan(0, snapshot.Length), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, oversizedRange.Completeness);
        Assert.InRange(oversizedRange.Root.Children.Count, 1, 4_096);
        Assert.Equal(0, oversizedRange.TotalDiagnosticCount);
    }

    /// <summary>An unbounded first quoted record cannot masquerade as valid visible semantics.</summary>
    [Fact]
    public void Cold_visible_result_is_provisional_when_first_record_exceeds_scan_budget()
    {
        using var document = new Document("\"" + new string('x', 100_000) + "\n");
        using var session = ((IIncrementalDocumentPolicy)new CsvPolicy()).CreateSession();
        var snapshot = document.Snapshot;
        var request = new AnalysisRequest(new TextSpan(0, 20), AnalysisScope.Visible);

        var visible = session.Analyze(snapshot, [], request);
        Assert.Equal(AnalysisCompleteness.Provisional, visible.Completeness);
        Assert.Equal(0, visible.Coverage.Length);
        Assert.Null(visible.TotalDiagnosticCount);
        Assert.Empty(visible.Root.Children);

        var full = session.Analyze(snapshot, [], request with { Scope = AnalysisScope.Full });
        Assert.Equal(AnalysisCompleteness.Complete, full.Completeness);
        Assert.Equal(1, full.TotalDiagnosticCount);
        Assert.Contains(full.Diagnostics, diagnostic => diagnostic.Code == "CSV001");
    }

    /// <summary>Without a prior quote-state checkpoint, a distant line is not a certified record.</summary>
    [Fact]
    public void Cold_distant_viewport_does_not_claim_semantic_coverage()
    {
        using var document = new Document("a,b\n" + string.Concat(Enumerable.Repeat("1,2\n", 30_000)));
        using var session = ((IIncrementalDocumentPolicy)new CsvPolicy()).CreateSession();
        var snapshot = document.Snapshot;
        var request = new AnalysisRequest(new TextSpan(snapshot.Length - 100, 100), AnalysisScope.Visible);

        var result = session.Analyze(snapshot, [], request);
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.True(result.Coverage.End < request.VisibleRange.Start);
        Assert.Empty(result.Root.Children);
        Assert.Null(result.TotalDiagnosticCount);
    }

    /// <summary>Editing a partial index invalidates its old prefix and preserves exact future totals.</summary>
    [Fact]
    public void Edit_after_partial_result_rebuilds_prefix_and_full_count()
    {
        using var document = new Document("a,b\n" + string.Concat(Enumerable.Repeat("1,2\n", 20_000)));
        using var session = ((IIncrementalDocumentPolicy)new CsvPolicy()).CreateSession();
        var request = new AnalysisRequest(new TextSpan(0, 10), AnalysisScope.Visible);
        session.Analyze(document.Snapshot, [], request);

        var before = document.Snapshot;
        var change = new TextChange(0, 3, "a,b,c");
        var after = document.Apply(change);
        var edit = new VersionedEdit(before.Version, after.Version, change);
        var visible = session.Analyze(after, [edit], request);
        Assert.Equal(AnalysisCompleteness.CoveredRegion, visible.Completeness);
        Assert.Equal(3, visible.Root.Children[0].Children.Count);
        Assert.Null(visible.TotalDiagnosticCount);

        var full = session.Analyze(after, [], request with { Scope = AnalysisScope.Full });
        Assert.Equal(AnalysisCompleteness.Complete, full.Completeness);
        Assert.Equal(20_000, full.TotalDiagnosticCount);
    }

    /// <summary>Canceling a resumed full scan cannot publish an invalid completion.</summary>
    [Fact]
    public void Canceled_full_scan_can_retry_from_same_partial_version()
    {
        using var document = new Document("a,b\n" + string.Concat(Enumerable.Repeat("1,2\n", 20_000)));
        using var session = ((IIncrementalDocumentPolicy)new CsvPolicy()).CreateSession();
        var request = new AnalysisRequest(new TextSpan(0, 10), AnalysisScope.Visible);
        var partial = session.Analyze(document.Snapshot, [], request);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.Analyze(document.Snapshot, [],
            request with { Scope = AnalysisScope.Full }, cancellation.Token));
        var retried = session.Analyze(document.Snapshot, [], request with { Scope = AnalysisScope.Full });
        Assert.Equal(AnalysisCompleteness.CoveredRegion, partial.Completeness);
        Assert.Equal(AnalysisCompleteness.Complete, retried.Completeness);
        Assert.Equal(0, retried.TotalDiagnosticCount);
    }

    /// <summary>UTF-16 offsets and quoted CRLF semantics survive visible-to-full staging.</summary>
    [Fact]
    public void Staged_utf16_and_quoted_crlf_matches_legacy_full_parse()
    {
        var source = "name,value\r\n😀,1\r\n\"x\r\ny\",2\r\nragged\r\n" +
            string.Concat(Enumerable.Repeat("tail,3\r\n", 2_000));
        using var document = new Document(source);
        var policy = new CsvPolicy();
        using var session = policy.CreateSession();
        var visibleRequest = new AnalysisRequest(new TextSpan(0, 25), AnalysisScope.Visible);
        var first = session.Analyze(document.Snapshot, [], visibleRequest);
        Assert.Equal(AnalysisCompleteness.CoveredRegion, first.Completeness);
        Assert.Equal(new TextSpan(12, 2), first.Root.Children[1].Children[0].Span);

        var full = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, document.Snapshot.Length), AnalysisScope.Full));
        var legacy = policy.Analyze(source);
        Assert.Equal(AnalysisCompleteness.Complete, full.Completeness);
        Assert.Equal(legacy.Diagnostics, full.Diagnostics);
        Assert.Equal(legacy.Tokens, full.Tokens);
        Assert.Equal(legacy.Root.Children.Count, full.Root.Children.Count);
        for (var i = 0; i < legacy.Root.Children.Count; i++)
        {
            Assert.Equal(legacy.Root.Children[i].Span, full.Root.Children[i].Span);
            Assert.Equal(legacy.Root.Children[i].Children.Select(cell => (cell.Span, cell.Value)),
                full.Root.Children[i].Children.Select(cell => (cell.Span, cell.Value)));
        }
    }

    /// <summary>A single million-character row cannot allocate one node per offscreen cell.</summary>
    [Fact]
    public void Giant_single_row_projects_only_visible_cells()
    {
        using var document = new Document(new string(',', 1_000_000));
        using var session = ((IIncrementalDocumentPolicy)new CsvPolicy()).CreateSession();
        var request = new AnalysisRequest(new TextSpan(0, 16), AnalysisScope.Full);

        var result = session.Analyze(document.Snapshot, [], request);
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(0, result.TotalDiagnosticCount);
        var row = Assert.Single(result.Root.Children);
        Assert.Equal(document.Snapshot.Length, row.Span.Length);
        Assert.InRange(row.Children.Count, 1, 32);
        Assert.All(row.Children, cell => Assert.InRange(cell.Span.Start, 0, 16));
    }
}
