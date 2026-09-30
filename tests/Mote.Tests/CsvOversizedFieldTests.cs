using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Certified oversized-field summaries bound projection without hiding semantic facts.</summary>
public sealed class CsvOversizedFieldTests
{
    /// <summary>Single and disjoint windows reuse the same quoted-field certificate at absolute offsets.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Quoted_giant_head_tail_and_two_windows_preserve_token_and_record(int variant)
    {
        const int contentLength = 100_000;
        var source = "\"" + new string('x', contentLength) + "\",2\n";
        using var document = new Document(source);
        using var session = Session();
        session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(0, 2)], AnalysisScope.Full);
        TextSpan[] windows = variant switch
        {
            0 => [new TextSpan(1, 2)],
            1 => [new TextSpan(contentLength - 2, 2)],
            _ => [new TextSpan(1, 2), new TextSpan(contentLength - 2, 2)]
        };
        var result = session.AnalyzeWindows(document.Snapshot, [], windows, AnalysisScope.Visible);

        Assert.Equal(document.Snapshot.Version, result.Version);
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(0, result.TotalDiagnosticCount);
        Assert.Equal(new TextSpan(0, source.Length - 1), Assert.Single(result.Root.Children).Span);
        Assert.Empty(result.Root.Children[0].Children);
        Assert.Equal(new TextSpan(0, contentLength + 2), Assert.Single(result.Tokens).Span);
        Assert.True(result.ProjectionTruncated);
        Assert.All(result.Windows, window => Assert.True(window.SourceIndexed));
        Assert.InRange(session.CacheStatistics.ScannedSourceUnits, 0, 1024);
        AssertBudget(session);
    }

    /// <summary>Skipping giant quoted contents preserves the absolute invalid-suffix diagnostic.</summary>
    [Fact]
    public void Closed_giant_invalid_suffix_keeps_csv002_summary()
    {
        const int length = 100_000;
        var source = "\"" + new string('x', length) + "\"oops,2\n";
        using var document = new Document(source);
        using var session = Session();
        var result = session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(length + 2, 4)], AnalysisScope.Full);

        Assert.Equal(1, result.TotalDiagnosticCount);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("CSV002", diagnostic.Code);
        Assert.Equal(new TextSpan(length + 2, 4), diagnostic.Span);
        Assert.Equal(new TextSpan(0, length + 6), Assert.Single(result.Tokens).Span);
        Assert.True(result.ProjectionTruncated);
        AssertBudget(session);
    }

    /// <summary>An unterminated giant certificate still reports its full CSV001 source extent.</summary>
    [Fact]
    public void Unterminated_giant_full_projection_preserves_csv001()
    {
        var source = "\"" + new string('x', 100_000);
        using var document = new Document(source);
        using var session = Session();
        var result = session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(99_998, 2)], AnalysisScope.Full);

        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(1, result.TotalDiagnosticCount);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("CSV001", diagnostic.Code);
        Assert.Equal(new TextSpan(0, source.Length), diagnostic.Span);
        Assert.Equal(new TextSpan(0, source.Length), Assert.Single(result.Tokens).Span);
        Assert.Equal(new TextSpan(0, source.Length), Assert.Single(result.Root.Children).Span);
        Assert.True(result.ProjectionTruncated);
    }

    /// <summary>Local unquoted quote errors match the legacy oracle while the global count remains exact.</summary>
    [Fact]
    public void Unquoted_giant_tail_quotes_keep_absolute_local_diagnostics()
    {
        var source = new string('x', 90_000) + "\"z\"w\",2\n";
        var window = new TextSpan(90_000, 5);
        var oracle = new CsvPolicy().Analyze(source);
        using var document = new Document(source);
        using var session = Session();
        session.AnalyzeWindows(document.Snapshot, [], [window], AnalysisScope.Full);
        var actual = session.AnalyzeWindows(document.Snapshot, [], [window], AnalysisScope.Visible);

        Assert.Equal(oracle.Diagnostics.Count, actual.TotalDiagnosticCount);
        Assert.Equal(oracle.Diagnostics.Where(d => d.Span.Start >= window.Start && d.Span.End <= window.End)
            .Select(d => (d.Code, d.Span)), actual.Diagnostics.Select(d => (d.Code, d.Span)));
        Assert.Equal(3, actual.TotalDiagnosticCount);
        Assert.True(actual.ProjectionTruncated);
        Assert.Empty(Assert.Single(actual.Root.Children).Children);
        Assert.InRange(session.CacheStatistics.ScannedSourceUnits, 0, 1024);
    }

    /// <summary>A sparse seeker skips a certified giant tail and retains EOF boundary ownership.</summary>
    [Fact]
    public void Sparse_giant_tail_and_eof_seek_stay_bounded()
    {
        var prefix = string.Concat(Enumerable.Repeat("a,b\n", 262_145));
        var source = prefix + "\"" + new string('x', 100_000) + "\",2\n";
        using var document = new Document(source);
        using var session = Session();
        session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(prefix.Length + 1, 2)], AnalysisScope.Full);
        Assert.Equal("SparseFull", session.CacheStatistics.Mode);
        var result = session.AnalyzeWindows(document.Snapshot, [],
            [new TextSpan(source.Length - 8, 2), new TextSpan(source.Length, 0)], AnalysisScope.Visible);

        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(0, result.TotalDiagnosticCount);
        Assert.Equal(prefix.Length, Assert.Single(result.Root.Children).Span.Start);
        Assert.Equal(new TextSpan(prefix.Length, 100_002), Assert.Single(result.Tokens).Span);
        Assert.All(result.Windows, window => Assert.True(window.SourceIndexed));
        Assert.InRange(session.CacheStatistics.ScannedSourceUnits, 0, 2 * 64 * 1024 + 256);
        AssertBudget(session);
    }

    /// <summary>Old giant certificates never publish an old exact total under a changed visible version.</summary>
    [Fact]
    public void Giant_edit_visible_is_bounded_provisional_without_global_semantic_leak()
    {
        using var document = new Document("\"" + new string('x', 100_000) + "\",2\n");
        using var session = Session();
        var old = document.Snapshot;
        session.AnalyzeWindows(old, [], [new TextSpan(0, 2)], AnalysisScope.Full);
        var change = new TextChange(10, 1, "y");
        var current = document.Apply(change);
        var edits = new[] { new VersionedEdit(old.Version, current.Version, change) };
        var visible = session.AnalyzeWindows(current, edits, [new TextSpan(99_990, 2)], AnalysisScope.Visible);

        Assert.Equal(current.Version, visible.Version);
        Assert.Equal(AnalysisCompleteness.Provisional, visible.Completeness);
        Assert.Null(visible.TotalDiagnosticCount);
        Assert.False(Assert.Single(visible.Windows).SourceIndexed);
        Assert.Empty(visible.Root.Children);
        Assert.InRange(session.CacheStatistics.ScannedSourceUnits, 0, 3 * 64 * 1024 + 4096);
        var legacy = session.Analyze(current, [], new AnalysisRequest(new TextSpan(99_990, 2), AnalysisScope.Visible));
        Assert.Equal(AnalysisCompleteness.Provisional, legacy.Completeness);
        Assert.Null(legacy.TotalDiagnosticCount);
        var full = session.AnalyzeWindows(current, [], [new TextSpan(99_990, 2)], AnalysisScope.Full);
        Assert.Equal(AnalysisCompleteness.Complete, full.Completeness);
        Assert.Equal(0, full.TotalDiagnosticCount);
        Assert.Equal(current.Version, full.Version);
    }

    /// <summary>A quote edit merging short records cannot trigger an unbounded visible full scan.</summary>
    [Fact]
    public void Dense_quote_merging_edit_visible_work_is_bounded()
    {
        using var document = new Document(string.Concat(Enumerable.Repeat("a,b\n", 100_000)));
        using var session = Session();
        var old = document.Snapshot;
        session.AnalyzeWindows(old, [], [new TextSpan(0, 3)], AnalysisScope.Full);
        Assert.Equal("DenseFull", session.CacheStatistics.Mode);
        var change = new TextChange(0, 1, "\"");
        var current = document.Apply(change);
        var visible = session.AnalyzeWindows(current, [new VersionedEdit(old.Version, current.Version, change)],
            [new TextSpan(0, 3)], AnalysisScope.Visible);

        Assert.Equal(AnalysisCompleteness.Provisional, visible.Completeness);
        Assert.Null(visible.TotalDiagnosticCount);
        Assert.Equal(current.Version, visible.Version);
        Assert.InRange(session.CacheStatistics.ScannedSourceUnits, 0, 3 * 64 * 1024 + 4096);
        AssertBudget(session);
    }

    /// <summary>Cancellation preserves a committed giant certificate for retry at its original version.</summary>
    [Fact]
    public void Canceled_giant_revalidation_preserves_committed_cache()
    {
        using var document = new Document("\"" + new string('x', 100_000));
        using var session = Session();
        var old = document.Snapshot;
        session.AnalyzeWindows(old, [], [new TextSpan(0, 1)], AnalysisScope.Full);
        var committed = session.CacheStatistics;
        var current = document.Apply(new TextChange(old.Length, 0, "\""));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.AnalyzeWindows(current, [],
            [new TextSpan(0, 1)], AnalysisScope.Full, cancellation.Token));
        Assert.Equal(committed, session.CacheStatistics);
        var result = session.AnalyzeWindows(old, [], [new TextSpan(99_990, 1)], AnalysisScope.Visible);
        Assert.Equal(1, result.TotalDiagnosticCount);
        Assert.Equal(old.Version, result.Version);
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("CSV001", diagnostic.Code);
        Assert.Equal(new TextSpan(0, old.Length), diagnostic.Span);
        Assert.Equal(new TextSpan(0, old.Length), Assert.Single(result.Tokens).Span);
        Assert.InRange(session.CacheStatistics.ScannedSourceUnits, 0, 1024);
    }

    /// <summary>Warm projection of 100 Mi UTF-16 units allocates only bounded output and skips the body.</summary>
    [Fact]
    public void Hundred_mebiunit_quoted_field_warm_projection_has_bounded_scan_and_allocation()
    {
        const int length = 100 * 1024 * 1024;
        using var document = new Document("\"" + new string('x', length) + "\",2\n");
        using var session = Session();
        var windows = new[] { new TextSpan(1, 2), new TextSpan(length - 2, 2) };
        session.AnalyzeWindows(document.Snapshot, [], windows, AnalysisScope.Full);
        session.AnalyzeWindows(document.Snapshot, [], windows, AnalysisScope.Visible);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = session.AnalyzeWindows(document.Snapshot, [], windows, AnalysisScope.Visible);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, result.TotalDiagnosticCount);
        Assert.Equal(new TextSpan(0, length + 2), Assert.Single(result.Tokens).Span);
        Assert.True(result.ProjectionTruncated);
        Assert.InRange(session.CacheStatistics.ScannedSourceUnits, 0, 1024);
        Assert.InRange(allocated, 0, 2 * 1024 * 1024 - 1);
        AssertBudget(session);
    }

    /// <summary>Disjoint delimiter windows seek certified field starts and preserve EOF's empty field.</summary>
    [Fact]
    public void Million_comma_head_tail_projection_is_bounded_and_exact()
    {
        const int length = 1_000_000;
        using var document = new Document(new string(',', length) + "\na\n");
        using var session = Session();
        var windows = new[] { new TextSpan(1, 3), new TextSpan(length - 3, 3) };
        session.AnalyzeWindows(document.Snapshot, [], windows, AnalysisScope.Full);
        var result = session.AnalyzeWindows(document.Snapshot, [], windows, AnalysisScope.Visible);
        Assert.Equal(1, result.TotalDiagnosticCount);
        Assert.All(result.Windows, window => Assert.True(window.SourceIndexed));
        var row = Assert.Single(result.Root.Children);
        Assert.Equal(new TextSpan(0, length), row.Span);
        Assert.Equal(new[] { 1, 2, 3, 4, length - 3, length - 2, length - 1, length },
            row.Children.Select(cell => cell.Span.Start));
        Assert.All(row.Children, cell => Assert.Equal("", cell.Value));
        Assert.InRange(session.CacheStatistics.ScannedSourceUnits, 0, 2 * 64 * 1024 + 256);
        AssertBudget(session);
    }

    /// <summary>Escapes, CRLF and malformed suffixes remain equivalent to fresh whole-source validation.</summary>
    [Theory]
    [InlineData("\"", "\"bad,z\r\nshort\r\n")]
    [InlineData("\"", "\",z\r\nshort\r\n")]
    [InlineData("", "\"bad\",z\r\nshort\r\n")]
    [InlineData("\"", "")]
    public void Giant_malformed_and_escaped_coordinates_match_legacy(string opening, string ending)
    {
        var body = opening.Length == 0 ? new string('x', 100_000) :
            string.Concat(Enumerable.Repeat("x\"\"y\r\n", 20_000));
        var source = "a,b\r\n" + opening + body + ending;
        using var document = new Document(source);
        using var session = Session();
        var window = new TextSpan(5 + opening.Length + body.Length - 8, Math.Min(16, ending.Length + 8));
        var oracle = new CsvPolicy().Analyze(source);
        session.AnalyzeWindows(document.Snapshot, [], [window], AnalysisScope.Full);
        var result = session.AnalyzeWindows(document.Snapshot, [], [window], AnalysisScope.Visible);
        Assert.Equal(oracle.Diagnostics.Count, result.TotalDiagnosticCount);
        Assert.Equal(oracle.Tokens.Where(token => Intersects(token.Span, window)), result.Tokens);
        Assert.Equal(oracle.Diagnostics.Where(diagnostic => Intersects(diagnostic.Span, window)), result.Diagnostics);
        Assert.InRange(session.CacheStatistics.ScannedSourceUnits, 0, 64 * 1024 + 256);
        AssertBudget(session);
    }

    /// <summary>A cancellation race either preserves the old commit or publishes complete new-version facts.</summary>
    /// <remarks>A timer does not establish when cancellation occurs; controlled mid-scan coverage is not claimed.</remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(Timeout.Infinite)]
    public void Cancellation_race_preserves_transactional_giant_certificate(int cancellationDelay)
    {
        using var document = new Document("\"" + new string('x', 10 * 1024 * 1024));
        using var session = Session();
        var old = document.Snapshot;
        session.AnalyzeWindows(old, [], [new TextSpan(0, 1)], AnalysisScope.Full);
        var committed = session.CacheStatistics;
        // Closing the field distinguishes the new semantic truth from the old CSV001 certificate.
        var current = document.Apply(new TextChange(old.Length, 0, "\""));
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(cancellationDelay);
        WindowedAnalysis? result = null;
        OperationCanceledException? canceled = null;
        try
        {
            result = session.AnalyzeWindows(current, [], [new TextSpan(0, 1)],
                AnalysisScope.Full, cancellation.Token);
        }
        catch (OperationCanceledException exception)
        {
            canceled = exception;
        }

        if (canceled is not null)
        {
            Assert.NotEqual(Timeout.Infinite, cancellationDelay);
            Assert.True(cancellation.IsCancellationRequested);
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
            Assert.Equal(committed, session.CacheStatistics);
            var retained = session.AnalyzeWindows(old, [], [new TextSpan(old.Length - 2, 1)],
                AnalysisScope.Visible);
            Assert.Equal(old.Version, retained.Version);
            Assert.Equal(AnalysisCompleteness.Complete, retained.Completeness);
            Assert.Equal(1, retained.TotalDiagnosticCount);
            var diagnostic = Assert.Single(retained.Diagnostics);
            Assert.Equal("CSV001", diagnostic.Code);
            Assert.Equal(new TextSpan(0, old.Length), diagnostic.Span);
            Assert.Equal(new TextSpan(0, old.Length), Assert.Single(retained.Tokens).Span);
            Assert.All(retained.Windows, window => Assert.True(window.SourceIndexed));
            AssertBudget(session);
            return;
        }

        Assert.NotNull(result);
        Assert.Equal(current.Version, result.Version);
        Assert.Equal(current.Version, session.CacheStatistics.Version);
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(0, result.TotalDiagnosticCount);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(new TextSpan(0, current.Length), Assert.Single(result.Tokens).Span);
        Assert.Equal(new TextSpan(0, current.Length), Assert.Single(result.Root.Children).Span);
        Assert.All(result.Windows, window => Assert.True(window.SourceIndexed));
        Assert.True(result.ProjectionTruncated);
        AssertBudget(session);
    }

    /// <summary>Mirrors the public inclusive boundary ownership rule for source-coordinate oracles.</summary>
    private static bool Intersects(TextSpan span, TextSpan range) =>
        span.Start < range.End && span.End > range.Start || range.Length == 0 && span.Start <= range.Start && span.End >= range.Start;

    /// <summary>The visible budget must not reject a real EOF reached before its scan allowance.</summary>
    [Fact]
    public void Ordinary_dense_eof_edit_keeps_complete_incremental_result()
    {
        using var document = new Document(string.Concat(Enumerable.Repeat("a,b\n", 100_000)) + "tail");
        using var session = Session();
        var old = document.Snapshot;
        session.AnalyzeWindows(old, [], [new TextSpan(old.Length - 4, 4)], AnalysisScope.Full);
        var change = new TextChange(old.Length - 1, 1, "x");
        var current = document.Apply(change);
        var result = session.AnalyzeWindows(current, [new VersionedEdit(old.Version, current.Version, change)],
            [new TextSpan(current.Length - 4, 4)], AnalysisScope.Visible);
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(1, result.TotalDiagnosticCount);
        Assert.Equal("DenseFull", session.CacheStatistics.Mode);
        Assert.Contains(result.Root.Children.SelectMany(row => row.Children), cell => cell.Value == "taix");
    }

    /// <summary>Inclusive zero-width diagnostic ownership neither loses nor duplicates neighboring quotes.</summary>
    [Fact]
    public void Giant_unquoted_zero_width_diagnostic_edges_match_legacy_once()
    {
        var source = new string('x', 100_000) + "\"z\"";
        var windows = new[] { new TextSpan(100_000, 0), new TextSpan(100_001, 0), new TextSpan(source.Length, 0) };
        using var document = new Document(source);
        using var session = Session();
        session.AnalyzeWindows(document.Snapshot, [], windows, AnalysisScope.Full);
        var result = session.AnalyzeWindows(document.Snapshot, [], windows, AnalysisScope.Visible);
        var oracle = new CsvPolicy().Analyze(source);
        Assert.Equal(2, result.TotalDiagnosticCount);
        Assert.Equal(oracle.Diagnostics.Where(diagnostic => windows.Any(window => Intersects(diagnostic.Span, window))),
            result.Diagnostics);
        Assert.InRange(session.CacheStatistics.ScannedSourceUnits, 0, 1024);
    }

    /// <summary>A replay budget reached exactly at EOF must preserve the final zero-width field.</summary>
    [Theory]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(131071)]
    [InlineData(131072)]
    [InlineData(131073)]
    public void Comma_tail_at_exact_replay_budget_keeps_final_empty_field(int length)
    {
        using var document = new Document(new string(',', length));
        using var session = Session();
        var windows = new[] { new TextSpan(0, 80), new TextSpan(length - 80, 80) };
        session.AnalyzeWindows(document.Snapshot, [], windows, AnalysisScope.Full);
        var result = session.AnalyzeWindows(document.Snapshot, [], windows, AnalysisScope.Visible);
        var row = Assert.Single(result.Root.Children);
        Assert.Equal(162, row.Children.Count);
        Assert.Contains(row.Children, cell => cell.Span == new TextSpan(length, 0));
        Assert.All(result.Windows, window => Assert.True(window.SourceIndexed));
        Assert.InRange(session.CacheStatistics.ScannedSourceUnits, 0, 64 * 1024 + 128);
    }

    /// <summary>A dense head exhausts only its own cell quota and cannot hide or starve a disjoint tail.</summary>
    [Fact]
    public void Giant_dense_head_quota_keeps_tail_cells_and_truthful_truncation()
    {
        const int length = 1_000_000;
        using var document = new Document(new string(',', length));
        using var session = Session();
        var windows = new[] { new TextSpan(0, 6000), new TextSpan(length - 80, 80) };
        session.AnalyzeWindows(document.Snapshot, [], windows, AnalysisScope.Full);
        var result = session.AnalyzeWindows(document.Snapshot, [], windows, AnalysisScope.Visible);
        var cells = Assert.Single(result.Root.Children).Children;
        Assert.Equal(4096, cells.Count(cell => cell.Span.Start < 6000));
        Assert.Equal(81, cells.Count(cell => cell.Span.Start >= length - 80));
        Assert.True(result.ProjectionTruncated);
        Assert.True(result.Windows[0].Truncated);
        Assert.All(result.Windows, window => Assert.True(window.SourceIndexed));
    }

    /// <summary>Lookbehind can span two ordinary fields before the next retained field checkpoint.</summary>
    [Fact]
    public void Giant_record_ordinary_field_lookbehind_keeps_requested_tail_value()
    {
        var source = new string('a', 40_000) + "," + new string('b', 40_000) + "," + new string('c', 40_000);
        using var document = new Document(source);
        using var session = Session();
        var window = new TextSpan(79_990, 5);
        session.AnalyzeWindows(document.Snapshot, [], [window], AnalysisScope.Full);
        var result = session.AnalyzeWindows(document.Snapshot, [], [window], AnalysisScope.Visible);
        var cell = Assert.Single(Assert.Single(result.Root.Children).Children);
        Assert.Equal(new TextSpan(40_001, 40_000), cell.Span);
        Assert.Equal(new string('b', 40_000), cell.Value);
        Assert.InRange(session.CacheStatistics.ScannedSourceUnits, 0, 2 * 64 * 1024 + window.Length);
    }

    /// <summary>Obtains structural instrumentation without exposing parser implementation state.</summary>
    private static CsvIncrementalSession Session() => Assert.IsType<CsvIncrementalSession>(new CsvPolicy().CreateSession());

    /// <summary>Both dense rows and giant-field summaries count toward the session retention target.</summary>
    private static void AssertBudget(CsvIncrementalSession session)
    {
        Assert.InRange(session.CacheStatistics.SegmentCount, 0, 256);
        Assert.InRange(session.CacheStatistics.EstimatedRetainedIndexBytes, 0, 32L * 1024 * 1024 - 1);
    }
}
