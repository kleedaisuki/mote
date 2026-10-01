using System.Text;
using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Retained CSV indexes remain bounded without weakening snapshot-specific semantics.</summary>
public sealed class CsvIndexBudgetTests
{
    /// <summary>The dense representation allows at most 256 blocks of 1,024 records.</summary>
    private const int DenseRowLimit = 256 * 1024;
    /// <summary>The session-only retained graph must remain strictly below this target.</summary>
    private const long RetainedBudget = 32L * 1024 * 1024;

    /// <summary>Empty-file completion is distinct from an uninitialized or disposed cache.</summary>
    [Fact]
    public void Empty_full_and_disposal_have_explicit_cache_state()
    {
        using var document = new Document("");
        var session = Session();
        Assert.Equal("None", session.CacheStatistics.Mode);
        Assert.Null(session.CacheStatistics.Version);
        var result = session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(0, 0)], AnalysisScope.Full);
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(0, result.TotalDiagnosticCount);
        Assert.Equal("DenseFull", session.CacheStatistics.Mode);
        Assert.Empty(result.Root.Children);
        session.Dispose();
        Assert.Equal("None", session.CacheStatistics.Mode);
        Assert.Equal(0, session.CacheStatistics.SegmentCount);
        Assert.Equal(0, session.CacheStatistics.CheckpointCount);
        Assert.Null(session.CacheStatistics.Version);
    }

    /// <summary>A sub-mebiunit sparse source cannot bypass bounded legacy projection via empty segments.</summary>
    [Fact]
    public void Legacy_full_sparse_small_source_keeps_projection_row_cap()
    {
        var source = Repeat("a\n", 300_000);
        using var document = new Document(source);
        using var session = Session();
        var result = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, source.Length), AnalysisScope.Full));

        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(0, result.TotalDiagnosticCount);
        Assert.InRange(result.Root.Children.Count, 1, 4096);
        AssertSparseBudget(session, document.Snapshot.Version);
    }

    /// <summary>Legacy full semantics do not imply offwindow cell delivery in a sparse small file.</summary>
    [Fact]
    public void Legacy_full_sparse_small_source_narrow_range_excludes_offwindow_cells()
    {
        using var document = new Document(Repeat("a,\n", 300_000));
        using var session = Session();
        var result = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 1), AnalysisScope.Full));

        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(0, result.TotalDiagnosticCount);
        var row = Assert.Single(result.Root.Children);
        Assert.Equal(0, row.Span.Start);
        var cell = Assert.Single(row.Children);
        Assert.Equal(new TextSpan(0, 1), cell.Span);
        Assert.Equal("a", cell.Value);
        AssertSparseBudget(session, document.Snapshot.Version);
    }

    /// <summary>An empty committed dense cache must parse text newly inserted at offset zero.</summary>
    [Fact]
    public void Empty_dense_cache_insertion_retains_new_record_and_diagnostics()
    {
        using var document = new Document("");
        using var session = Session();
        var old = document.Snapshot;
        session.AnalyzeWindows(old, [], [new TextSpan(0, 0)], AnalysisScope.Full);
        Assert.Equal("DenseFull", session.CacheStatistics.Mode);
        var change = new TextChange(0, 0, "a,b\nwrong\n");
        var current = document.Apply(change);
        var actual = session.AnalyzeWindows(current, [new VersionedEdit(old.Version, current.Version, change)],
            [new TextSpan(0, current.Length)], AnalysisScope.Full);

        Assert.Equal(AnalysisCompleteness.Complete, actual.Completeness);
        Assert.Equal(1, actual.TotalDiagnosticCount);
        Assert.Equal([0, 4], actual.Root.Children.Select(row => row.Span.Start));
        Assert.Equal(["a", "b", "wrong"], actual.Root.Children.SelectMany(row => row.Children).Select(cell => cell.Value));
        Assert.Equal("CSV004", Assert.Single(actual.Diagnostics).Code);
        Assert.Equal(current.Version, session.CacheStatistics.Version);
        Assert.Equal("DenseFull", session.CacheStatistics.Mode);
    }

    /// <summary>Cold full validation streams offscreen errors after dense retention overflows.</summary>
    [Fact]
    public void Cold_full_uses_sparse_index_and_counts_offscreen_errors()
    {
        var source = "a,b\n" + Repeat("1,2\n", DenseRowLimit) + "bad\n\"unterminated";
        using var document = new Document(source);
        using var session = Session();
        var result = session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(0, 3)], AnalysisScope.Full);

        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(3, result.TotalDiagnosticCount);
        Assert.Empty(result.Diagnostics);
        Assert.Equal([new TextSpan(0, source.Length)], result.CertifiedCoverage);
        AssertSparseBudget(session, document.Snapshot.Version);
    }

    /// <summary>A bounded visible prefix promotes to an authoritative sparse full index.</summary>
    [Fact]
    public void Prefix_to_full_preserves_exact_width_count_and_tail_projection()
    {
        var source = "a,b\n" + Repeat("1,2\n", DenseRowLimit) + "tail\n";
        using var document = new Document(source);
        using var session = Session();
        var prefix = session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(0, 4)], AnalysisScope.Visible);
        Assert.Equal(AnalysisCompleteness.CoveredRegion, prefix.Completeness);
        Assert.Equal("Prefix", session.CacheStatistics.Mode);
        Assert.Null(prefix.TotalDiagnosticCount);
        var tail = source.Length - 5;
        var full = session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(tail, 4)], AnalysisScope.Full);

        Assert.Equal(1, full.TotalDiagnosticCount);
        Assert.Equal(tail, Assert.Single(full.Root.Children).Span.Start);
        Assert.Equal("CSV004", Assert.Single(full.Diagnostics).Code);
        Assert.True(Assert.Single(full.Windows).SourceIndexed);
        AssertSparseBudget(session, document.Snapshot.Version);
    }

    /// <summary>A sparse first-record edit cannot expose a prior-version exact count as fresh.</summary>
    [Fact]
    public void Sparse_edit_visible_is_provisional_then_full_revalidates_first_row_dependency()
    {
        using var document = new Document("a,b\n" + Repeat("1,2\n", DenseRowLimit));
        using var session = Session();
        var old = document.Snapshot;
        Assert.Equal(0, session.AnalyzeWindows(old, [], [new TextSpan(0, 3)], AnalysisScope.Full).TotalDiagnosticCount);
        var change = new TextChange(3, 0, ",c");
        var current = document.Apply(change);
        var edits = new[] { new VersionedEdit(old.Version, current.Version, change) };
        var windows = new[] { new TextSpan(0, 5), new TextSpan(current.Length - 4, 3) };
        var visible = session.AnalyzeWindows(current, edits, windows, AnalysisScope.Visible);

        Assert.Equal(current.Version, visible.Version);
        Assert.Equal(AnalysisCompleteness.Provisional, visible.Completeness);
        Assert.Null(visible.TotalDiagnosticCount);
        Assert.False(visible.Windows[^1].SourceIndexed);
        var full = session.AnalyzeWindows(current, [], windows, AnalysisScope.Full);
        Assert.Equal(DenseRowLimit, full.TotalDiagnosticCount);
        Assert.Equal(AnalysisCompleteness.Complete, full.Completeness);
        AssertSparseBudget(session, current.Version);
    }

    /// <summary>A canceled new-version scan leaves the entire previously committed cache intact.</summary>
    [Fact]
    public void Cancellation_preserves_sparse_committed_statistics_and_can_retry_missing_chain()
    {
        using var document = new Document(Repeat("a,b\n", DenseRowLimit + 1));
        using var session = Session();
        session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(0, 3)], AnalysisScope.Full);
        var before = session.CacheStatistics;
        var current = document.Apply(new TextChange(3, 0, ",c"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => session.AnalyzeWindows(current, [],
            [new TextSpan(0, 5)], AnalysisScope.Full, cancellation.Token));
        Assert.Equal(before, session.CacheStatistics);
        var retried = session.AnalyzeWindows(current, [], [new TextSpan(0, 5)], AnalysisScope.Full);
        Assert.Equal(DenseRowLimit, retried.TotalDiagnosticCount);
        AssertSparseBudget(session, current.Version);
    }

    /// <summary>Exact checkpoint and EOF interest retain the preceding boundary-owning record.</summary>
    [Fact]
    public void Zero_width_checkpoint_and_eof_windows_keep_dense_boundary_semantics()
    {
        using var document = new Document(Repeat("a\n", DenseRowLimit + 1));
        using var session = Session();
        const int checkpoint = 64 * 1024;
        var result = session.AnalyzeWindows(document.Snapshot, [],
            [new TextSpan(checkpoint, 0), new TextSpan(document.Snapshot.Length, 0)], AnalysisScope.Full);

        Assert.Equal([checkpoint - 2, checkpoint, document.Snapshot.Length - 2],
            result.Root.Children.Select(row => row.Span.Start));
        Assert.All(result.Windows, window => Assert.True(window.SourceIndexed));
        Assert.Equal(0, result.TotalDiagnosticCount);
        AssertSparseBudget(session, document.Snapshot.Version);
    }

    /// <summary>Certified seeks never begin inside a quoted CRLF or giant logical record.</summary>
    [Fact]
    public void Sparse_quoted_and_giant_rows_match_translated_dense_oracle()
    {
        var prefix = Repeat("a,b\n", DenseRowLimit + 1);
        var tail = "\"left\r\nright\",2\r\n\"" + new string('x', 80_000) + "\r\ny\",3\r\nend,4\r\n";
        using var document = new Document(prefix + tail);
        using var denseDocument = new Document(tail);
        using var sparse = Session();
        using var dense = Session();
        var local = new[] { new TextSpan(4, 8), new TextSpan(tail.Length - 18, 16) };
        var actual = sparse.AnalyzeWindows(document.Snapshot, [],
            local.Select(span => new TextSpan(span.Start + prefix.Length, span.Length)).ToArray(), AnalysisScope.Full);
        var expected = dense.AnalyzeWindows(denseDocument.Snapshot, [], local, AnalysisScope.Full);

        Assert.Equal("DenseFull", dense.CacheStatistics.Mode);
        Assert.Equal(expected.TotalDiagnosticCount, actual.TotalDiagnosticCount);
        Assert.Equal(expected.Root.Children.Select(row => Shift(row.Span, prefix.Length)), actual.Root.Children.Select(row => row.Span));
        Assert.Equal(expected.Root.Children.SelectMany(row => row.Children).Select(cell => Shift(cell.Span, prefix.Length)),
            actual.Root.Children.SelectMany(row => row.Children).Select(cell => cell.Span));
        Assert.Equal(expected.Root.Children.SelectMany(row => row.Children).Select(cell => cell.Value),
            actual.Root.Children.SelectMany(row => row.Children).Select(cell => cell.Value));
        Assert.Equal(expected.Diagnostics.Select(d => (d.Code, Shift(d.Span, prefix.Length))),
            actual.Diagnostics.Select(d => (d.Code, d.Span)));
        AssertSparseBudget(sparse, document.Snapshot.Version);
    }

    /// <summary>An incremental overflow drops the dense candidate rather than retaining excess slices.</summary>
    [Fact]
    public void Dense_limit_insertion_transitions_to_sparse_without_losing_semantics()
    {
        using var document = new Document(Repeat("a,b\n", DenseRowLimit));
        using var session = Session();
        var old = document.Snapshot;
        session.AnalyzeWindows(old, [], [new TextSpan(0, 3)], AnalysisScope.Full);
        Assert.Equal("DenseFull", session.CacheStatistics.Mode);
        Assert.Equal(256, session.CacheStatistics.SegmentCount);
        var edit = new TextChange(4, 0, "bad\n");
        var current = document.Apply(edit);
        var result = session.AnalyzeWindows(current, [new VersionedEdit(old.Version, current.Version, edit)],
            [new TextSpan(0, 8), new TextSpan(current.Length - 4, 3)], AnalysisScope.Full);

        Assert.Equal(1, result.TotalDiagnosticCount);
        Assert.Equal("CSV004", Assert.Single(result.Diagnostics).Code);
        AssertSparseBudget(session, current.Version);
    }

    /// <summary>Repeated record-level slices never grow the reachable dense graph without a cap.</summary>
    [Fact]
    public void Repeated_dense_slices_keep_retained_budget_and_exact_count()
    {
        using var document = new Document(Repeat("a,b\n", DenseRowLimit - 1024));
        using var session = Session();
        session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(0, 3)], AnalysisScope.Full);
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var old = document.Snapshot;
            var edit = new TextChange(4 * (1000 + iteration * 7000), 1, iteration % 2 == 0 ? "x" : "y");
            var current = document.Apply(edit);
            var result = session.AnalyzeWindows(current, [new VersionedEdit(old.Version, current.Version, edit)],
                [new TextSpan(edit.Start, 3)], AnalysisScope.Full);
            Assert.Equal(0, result.TotalDiagnosticCount);
            Assert.InRange(session.CacheStatistics.SegmentCount, 0, 256);
            Assert.InRange(session.CacheStatistics.EstimatedRetainedIndexBytes, 0, RetainedBudget - 1);
            Assert.Equal(current.Version, session.CacheStatistics.Version);
        }
    }

    /// <summary>Undo and redo without an edit chain rebuild exact sparse semantics at fresh versions.</summary>
    [Fact]
    public void Sparse_undo_redo_without_edit_chain_revalidates_width_dependency()
    {
        using var document = new Document("a,b\n" + Repeat("1,2\n", DenseRowLimit));
        using var session = Session();
        session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(0, 3)], AnalysisScope.Full);
        document.Apply(new TextChange(3, 0, ",c"));
        var changed = session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(0, 5)], AnalysisScope.Full);
        Assert.Equal(DenseRowLimit, changed.TotalDiagnosticCount);
        AssertSparseBudget(session, document.Snapshot.Version);

        Assert.True(document.Undo());
        var undone = session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(0, 3)], AnalysisScope.Full);
        Assert.Equal(0, undone.TotalDiagnosticCount);
        Assert.Equal(document.Snapshot.Version, undone.Version);
        Assert.True(undone.Version > changed.Version);
        AssertSparseBudget(session, document.Snapshot.Version);

        Assert.True(document.Redo());
        var redone = session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(0, 5)], AnalysisScope.Full);
        Assert.Equal(DenseRowLimit, redone.TotalDiagnosticCount);
        Assert.Equal(document.Snapshot.Version, redone.Version);
        Assert.True(redone.Version > undone.Version);
        AssertSparseBudget(session, document.Snapshot.Version);
    }

    /// <summary>Repeated one-record prefix extensions compact fragmented blocks before exceeding the cap.</summary>
    [Fact]
    public void Repeated_visible_prefix_extensions_keep_segment_cap_and_exact_coverage()
    {
        using var document = new Document(new string('\n', 70_000));
        using var session = Session();
        for (var extension = 0; extension < 300; extension++)
        {
            var end = 8192 + extension;
            var visible = session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(0, end)], AnalysisScope.Visible);
            Assert.Equal("Prefix", session.CacheStatistics.Mode);
            Assert.Equal([new TextSpan(0, end)], visible.CertifiedCoverage);
            Assert.True(Assert.Single(visible.Windows).SourceIndexed);
            Assert.Null(visible.TotalDiagnosticCount);
            Assert.InRange(session.CacheStatistics.SegmentCount, 1, 256);
            Assert.InRange(session.CacheStatistics.EstimatedRetainedIndexBytes, 1, RetainedBudget - 1);
        }
        var full = session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(69_999, 1)], AnalysisScope.Full);
        Assert.Equal(AnalysisCompleteness.Complete, full.Completeness);
        Assert.Equal(0, full.TotalDiagnosticCount);
        Assert.Equal([new TextSpan(0, document.Snapshot.Length)], full.CertifiedCoverage);
        Assert.Equal("DenseFull", session.CacheStatistics.Mode);
        Assert.InRange(session.CacheStatistics.SegmentCount, 1, 256);
    }

    /// <summary>A 100 Mi UTF-16-unit row fixture retains checkpoints, not millions of row summaries.</summary>
    [Fact]
    public void Hundred_mebiunit_full_index_stays_below_retained_budget()
    {
        const int targetLength = 100 * 1024 * 1024;
        var row = new string('x', 46) + ",1\n";
        var count = targetLength / row.Length;
        var source = Repeat(row, count) + "bad\n";
        using var document = new Document(source);
        using var session = Session();
        var result = session.AnalyzeWindows(document.Snapshot, [],
            [new TextSpan(0, 48), new TextSpan(source.Length - 4, 3)], AnalysisScope.Full);

        Assert.InRange(source.Length, targetLength - 48, targetLength + 4);
        Assert.Equal(1, result.TotalDiagnosticCount);
        Assert.Equal("CSV004", Assert.Single(result.Diagnostics).Code);
        AssertSparseBudget(session, document.Snapshot.Version);
        Assert.InRange(session.CacheStatistics.CheckpointCount, 1, source.Length / (64 * 1024) + 1);
        var warm = session.AnalyzeWindows(document.Snapshot, [],
            [new TextSpan(0, 48), new TextSpan(source.Length - 4, 3)], AnalysisScope.Visible);
        Assert.Equal(AnalysisCompleteness.Complete, warm.Completeness);
        Assert.Equal(1, warm.TotalDiagnosticCount);
        Assert.All(warm.Windows, window => Assert.True(window.SourceIndexed));
        Assert.InRange(session.CacheStatistics.ScannedSourceUnits, 1, 2 * 64 * 1024 + 512);
    }

    /// <summary>Uses friend access only for structural cache instrumentation, not parser internals.</summary>
    private static CsvIncrementalSession Session() => Assert.IsType<CsvIncrementalSession>(new CsvPolicy().CreateSession());

    /// <summary>Checks the committed sparse representation and its snapshot ownership.</summary>
    private static void AssertSparseBudget(CsvIncrementalSession session, long version)
    {
        var statistics = session.CacheStatistics;
        Assert.Equal("SparseFull", statistics.Mode);
        Assert.Equal(0, statistics.SegmentCount);
        Assert.True(statistics.CheckpointCount > 0);
        Assert.InRange(statistics.EstimatedRetainedIndexBytes, 1, RetainedBudget - 1);
        Assert.Equal(version, statistics.Version);
    }

    /// <summary>Translates a small-fixture oracle into the large source's UTF-16 coordinates.</summary>
    private static TextSpan Shift(TextSpan span, int offset) => new(span.Start + offset, span.Length);

    /// <summary>Builds deterministic fixtures without a temporary per-record string array.</summary>
    private static string Repeat(string record, int count)
    {
        var builder = new StringBuilder(checked(record.Length * count));
        for (var index = 0; index < count; index++) builder.Append(record);
        return builder.ToString();
    }
}
