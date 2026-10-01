using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>CSV sparse projections share one record index without certifying gaps.</summary>
public sealed class CsvWindowedAnalysisTests
{
    /// <summary>Two distant windows retain absolute UTF-16 offsets and the exact offscreen total.</summary>
    [Fact]
    public void Full_projects_disjoint_rows_once_without_losing_offscreen_diagnostics()
    {
        var source = "a,b\r\n😀,1\r\n" + string.Concat(Enumerable.Repeat("middle,2\r\n", 300_000)) + "bad\r\nlast,3\r\n";
        using var document = new Document(source);
        using var session = Windowed();
        var tail = source.LastIndexOf("last,3", StringComparison.Ordinal);
        var windows = new[] { new TextSpan(tail, 6), new TextSpan(5, 4) };
        var result = session.AnalyzeWindows(document.Snapshot, [], windows, AnalysisScope.Full);

        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal([new TextSpan(0, source.Length)], result.CertifiedCoverage);
        Assert.Equal(1, result.TotalDiagnosticCount);
        Assert.Equal([5, tail], result.Root.Children.Select(row => row.Span.Start));
        Assert.Equal(new TextSpan(5, 2), result.Root.Children[0].Children[0].Span);
        Assert.Equal("😀", result.Root.Children[0].Children[0].Value);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CSV004");
        Assert.Equal(source, document.Snapshot.GetText());
    }

    /// <summary>A cold distant request cannot turn the unscanned middle into a certified hull.</summary>
    [Fact]
    public void Cold_visible_distant_window_is_provisional_with_prefix_only_coverage()
    {
        var source = "a,b\n" + string.Concat(Enumerable.Repeat("1,2\n", 600_000));
        using var document = new Document(source);
        using var session = Windowed();
        var tail = source.Length - 8;
        var result = session.AnalyzeWindows(document.Snapshot, [],
            [new TextSpan(0, 4), new TextSpan(tail, 4)], AnalysisScope.Visible);

        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.Null(result.TotalDiagnosticCount);
        Assert.Single(result.CertifiedCoverage);
        Assert.True(result.CertifiedCoverage[0].End < tail);
        Assert.True(result.Windows[1].Truncated);
        Assert.False(result.Windows[1].SourceIndexed);
        Assert.DoesNotContain(result.Root.Children, row => row.Span.Start >= tail);
    }

    /// <summary>Zero-length interest does not certify an unclosed giant first record.</summary>
    [Fact]
    public void Cold_zero_length_window_does_not_claim_unindexed_source()
    {
        using var document = new Document("\"" + new string('x', 100_000));
        using var session = Windowed();
        var result = session.AnalyzeWindows(document.Snapshot, [],
            [new TextSpan(0, 0)], AnalysisScope.Visible);
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.False(Assert.Single(result.Windows).SourceIndexed);
        Assert.True(Assert.Single(result.Windows).Truncated);
        Assert.Empty(result.Root.Children);
    }

    /// <summary>One logical quoted record crossing both windows emits one owner and no gap cells.</summary>
    [Fact]
    public void Overlap_and_multiline_record_deduplicate_rows_and_cells()
    {
        var source = "a,b\r\n\"left\r\nright\",middle,tail\r\nend,1\r\n";
        using var document = new Document(source);
        using var session = Windowed();
        var left = source.IndexOf("left", StringComparison.Ordinal);
        var tail = source.IndexOf("tail", StringComparison.Ordinal);
        var result = session.AnalyzeWindows(document.Snapshot, [],
            [new TextSpan(tail, 4), new TextSpan(left, 2), new TextSpan(left + 1, 4)], AnalysisScope.Full);

        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        var row = Assert.Single(result.Root.Children);
        Assert.Equal(5, row.Span.Start);
        Assert.Equal(["left\r\nright", "tail"], row.Children.Select(cell => cell.Value));
        Assert.Equal([new TextSpan(5, 13), new TextSpan(tail, 4)],
            row.Children.Select(cell => cell.Span));
        Assert.Single(result.Tokens);
        Assert.Equal(1, result.TotalDiagnosticCount);
    }

    /// <summary>A first-row width edit updates offscreen CSV004 facts without reparsing their payloads.</summary>
    [Fact]
    public void First_row_width_change_invalidates_distant_width_dependency()
    {
        using var document = new Document("a,b\r\n1,2\r\n3,4\r\n");
        using var session = Windowed();
        var old = document.Snapshot;
        Assert.Equal(0, session.AnalyzeWindows(old, [],
            [new TextSpan(0, 1), new TextSpan(10, 3)], AnalysisScope.Full).TotalDiagnosticCount);
        var edit = new TextChange(3, 0, ",c");
        var current = document.Apply(edit);
        var result = session.AnalyzeWindows(current,
            [new VersionedEdit(old.Version, current.Version, edit)],
            [new TextSpan(0, 1), new TextSpan(12, 3)], AnalysisScope.Full);

        Assert.Equal(2, result.TotalDiagnosticCount);
        Assert.Equal("CSV004", Assert.Single(result.Diagnostics).Code);
        Assert.Equal(new TextSpan(12, 3), result.Diagnostics[0].Span);
        Assert.Equal(["a", "3", "4"], result.Root.Children.SelectMany(row => row.Children).Select(cell => cell.Value));
    }

    /// <summary>Cancellation and a missing edit never reuse another snapshot's certificate.</summary>
    [Fact]
    public void Canceled_and_gapped_versions_can_retry_from_authoritative_snapshot()
    {
        using var document = new Document("a,b\n" + string.Concat(Enumerable.Repeat("1,2\n", 20_000)));
        using var session = Windowed();
        var windows = new[] { new TextSpan(0, 2), new TextSpan(document.Snapshot.Length - 4, 3) };
        var first = session.AnalyzeWindows(document.Snapshot, [], windows, AnalysisScope.Full);
        var edit1 = new TextChange(3, 0, ",c");
        var intermediate = document.Apply(edit1);
        var edit2 = new TextChange(0, 1, "z");
        var latest = document.Apply(edit2);
        var staleChain = new[] { new VersionedEdit(first.Version, intermediate.Version, edit1) };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.AnalyzeWindows(latest, staleChain, windows,
            AnalysisScope.Full, cancellation.Token));
        var retried = session.AnalyzeWindows(latest, staleChain, windows, AnalysisScope.Full);
        Assert.Equal(latest.Version, retried.Version);
        Assert.Equal(20_000, retried.TotalDiagnosticCount);
        Assert.Equal(0, first.Version);
        Assert.Equal("z,b,c", latest.GetText(0, 5));
    }

    /// <summary>The additive capability leaves the historical one-window API identical.</summary>
    [Fact]
    public void One_window_full_matches_legacy_projection_and_rejects_excess_batch_width()
    {
        const string source = "a,b\r\n😀,1\r\n\"x\r\ny\",2\r\nbad\r\n";
        using var document = new Document(source);
        using var oldSession = new CsvPolicy().CreateSession();
        using var newSession = Windowed();
        var range = new TextSpan(0, source.Length);
        var legacy = oldSession.Analyze(document.Snapshot, [], new AnalysisRequest(range, AnalysisScope.Full));
        var batch = newSession.AnalyzeWindows(document.Snapshot, [], [range], AnalysisScope.Full);
        Assert.Equal(legacy.Completeness, batch.Completeness);
        Assert.Equal(legacy.TotalDiagnosticCount, batch.TotalDiagnosticCount);
        Assert.Equal(legacy.Root.Children.Select(row => row.Span), batch.Root.Children.Select(row => row.Span));
        Assert.Equal(legacy.Diagnostics, batch.Diagnostics);
        Assert.Equal(legacy.Tokens, batch.Tokens);
        var narrow = new TextSpan(source.IndexOf("x", StringComparison.Ordinal), 2);
        var oldVisible = oldSession.Analyze(document.Snapshot, [],
            new AnalysisRequest(narrow, AnalysisScope.Visible));
        var newVisible = newSession.AnalyzeWindows(document.Snapshot, [], [narrow], AnalysisScope.Visible);
        Assert.Equal(oldVisible.Root.Children.Select(row => row.Span),
            newVisible.Root.Children.Select(row => row.Span));
        Assert.Equal(oldVisible.Diagnostics, newVisible.Diagnostics);
        Assert.Equal(oldVisible.Tokens, newVisible.Tokens);
        Assert.Throws<ArgumentOutOfRangeException>(() => newSession.AnalyzeWindows(document.Snapshot, [],
            [new TextSpan(0, 0), new TextSpan(0, 0), new TextSpan(0, 0), new TextSpan(0, 0),
                new TextSpan(0, 0), new TextSpan(0, 0), new TextSpan(0, 0), new TextSpan(0, 0),
                new TextSpan(0, 0)], AnalysisScope.Visible));
    }

    /// <summary>Two tiny windows in one giant comma record avoid a gap-sized cell projection.</summary>
    [Fact]
    public void Giant_record_across_windows_keeps_one_row_and_bounded_cells()
    {
        using var document = new Document(new string(',', 1_000_000));
        using var session = Windowed();
        var result = session.AnalyzeWindows(document.Snapshot, [],
            [new TextSpan(3, 4), new TextSpan(999_990, 4)], AnalysisScope.Full);
        var row = Assert.Single(result.Root.Children);
        Assert.InRange(row.Children.Count, 2, 12);
        Assert.DoesNotContain(row.Children, cell => cell.Span.Start is > 7 and < 999_990);
        Assert.Equal(0, result.TotalDiagnosticCount);
    }

    /// <summary>A dense head cannot consume the entire row budget before a disjoint tail.</summary>
    [Fact]
    public void Dense_head_is_truncated_but_tail_receives_a_row_and_explicit_delivery_status()
    {
        var head = "a,b,c,d\n" + string.Concat(Enumerable.Repeat("1,2,3,4\n", 6_000));
        var tailStart = head.Length + 8;
        var source = head + "x,y,z,w\n" + "tail,3\n";
        using var document = new Document(source);
        using var session = Windowed();
        var result = session.AnalyzeWindows(document.Snapshot, [],
            [new TextSpan(0, head.Length), new TextSpan(tailStart, 6)], AnalysisScope.Full);

        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(1, result.TotalDiagnosticCount);
        Assert.Equal([new TextSpan(0, source.Length)], result.CertifiedCoverage);
        Assert.True(result.ProjectionTruncated);
        Assert.Equal(2_048, result.Windows[0].ProjectedRowCount);
        Assert.True(result.Windows[0].Truncated);
        Assert.True(result.Windows[0].SourceIndexed);
        Assert.Equal(1, result.Windows[1].ProjectedRowCount);
        Assert.False(result.Windows[1].Truncated);
        Assert.True(result.Windows[1].SourceIndexed);
        Assert.Equal(tailStart, result.Root.Children[^1].Span.Start);
        Assert.Equal(["tail", "3"], result.Root.Children[^1].Children.Select(cell => cell.Value));

        using var legacy = new CsvPolicy().CreateSession();
        var old = legacy.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, head.Length), AnalysisScope.Full));
        Assert.Equal(4_096, old.Root.Children.Count);
    }

    private static IWindowedFormatSession Windowed() =>
        Assert.IsAssignableFrom<IWindowedFormatSession>(new CsvPolicy().CreateSession());
}
