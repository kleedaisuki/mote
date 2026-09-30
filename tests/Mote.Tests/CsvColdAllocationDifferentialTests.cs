using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Independent compatibility and accounting checks for borrowed-chunk cold CSV validation.</summary>
public sealed class CsvColdAllocationDifferentialTests
{
    /// <summary>Places each lexer-sensitive pair across a rope leaf, including after fragmentation and prefix resume.</summary>
    [Theory]
    [InlineData("\r\n", false)]
    [InlineData("\"\"", false)]
    [InlineData("😀", false)]
    [InlineData("\r\n", true)]
    [InlineData("\"\"", true)]
    [InlineData("😀", true)]
    public void Quoted_pairs_cross_borrowed_chunk_boundary(string pair, bool fragmented)
    {
        // Opening quote plus padding puts the first unit at 16383, independently of parser window size.
        var source = "a,b\r\n\"" + new string('x', 16383 - 6) + pair + "z\",2\r\nq,3\r\n";
        using var document = new Document(source);
        if (fragmented)
        {
            var split = pair == "😀" ? 16385 : 16384;
            document.Apply(new TextChange(split, 1, source.Substring(split, 1)));
        }
        using var session = Session();
        var snapshot = document.Snapshot;
        var prefix = session.Analyze(snapshot, [], new AnalysisRequest(new TextSpan(0, 1), AnalysisScope.Visible));
        Assert.Null(prefix.TotalDiagnosticCount);
        Assert.Equal("Prefix", session.CacheStatistics.Mode);
        var actual = Full(session, snapshot);
        AssertOracle(source, actual);
        var row = actual.Root.Children[1];
        var expectedValue = new string('x', 16383 - 6) + (pair == "\"\"" ? "\"" : pair) + "z";
        Assert.Equal(expectedValue, row.Children[0].Value);
        Assert.Equal(new TextSpan(5, source.IndexOf(",2", StringComparison.Ordinal) - 5), row.Children[0].Span);
    }

    /// <summary>Malformed CSV positions are explicit UTF-16 source coordinates, not merely equal to another parser.</summary>
    [Fact]
    public void Diagnostic_spans_are_exact_after_fragmented_surrogate()
    {
        const string source = "a,b\r\n😀\"x,c\r\n\"ok\"bad,d\r\n\"open";
        using var document = new Document(source);
        document.Apply(new TextChange(5, 2, source.Substring(5, 2)));
        using var session = Session();
        var actual = Full(session, document.Snapshot);
        Assert.Equal(new[] { ("CSV003", new TextSpan(7, 1)), ("CSV002", new TextSpan(17, 3)),
            ("CSV001", new TextSpan(24, 5)), ("CSV004", new TextSpan(24, 5)) },
            actual.Diagnostics.Select(d => (d.Code, d.Span)));
        Assert.Equal(4, actual.TotalDiagnosticCount);
        AssertOracle(source, actual);
    }

    /// <summary>Giant nonmaterialized cells retain exact token/error extents without source-sized scan allocations.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Giant_full_has_bounded_allocations_and_exact_tail(bool quoted)
    {
        const int length = 8 * 1024 * 1024;
        var payload = new string('x', length);
        var source = quoted ? "\"" + payload + "\"bad,2\r\n" : payload + "\"bad,2\r\n";
        using var document = new Document(source);
        using var session = Session();
        var start = quoted ? length + 2 : length;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var result = session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(start, 4)], AnalysisScope.Full);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(1, result.TotalDiagnosticCount);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(quoted ? "CSV002" : "CSV003", diagnostic.Code);
        Assert.Equal(new TextSpan(start, quoted ? 3 : 1), diagnostic.Span);
        Assert.Equal(new TextSpan(0, source.Length - 2), Assert.Single(result.Root.Children).Span);
        Assert.True(result.ProjectionTruncated);
        Assert.InRange(allocated, 0, 2 * 1024 * 1024);
        if (quoted) Assert.Equal(new TextSpan(0, length + 5), Assert.Single(result.Tokens).Span);
        else Assert.Empty(result.Tokens);
    }

    /// <summary>More than 100 Mi UTF-16 units and 262144 rows force sparse counters with one known ragged record.</summary>
    [Fact]
    public void Hundred_mi_sparse_full_counts_without_source_sized_allocations()
    {
        const int rows = 262145;
        var record = new string('x', 396) + ",y\r\n";
        var source = string.Concat(Enumerable.Repeat(record, rows)) + "tail\r\n";
        Assert.True(source.Length >= 100 * 1024 * 1024);
        using var document = new Document(source);
        using var session = Session();
        var tail = rows * record.Length;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var actual = session.AnalyzeWindows(document.Snapshot, [], [new TextSpan(tail, 4)], AnalysisScope.Full);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.Equal(AnalysisCompleteness.Complete, actual.Completeness);
        Assert.Equal(1, actual.TotalDiagnosticCount);
        var diagnostic = Assert.Single(actual.Diagnostics);
        Assert.Equal(("CSV004", new TextSpan(tail, 4)),
            (diagnostic.Code, diagnostic.Span));
        Assert.Equal("SparseFull", session.CacheStatistics.Mode);
        Assert.InRange(session.CacheStatistics.SegmentCount, 0, 256);
        Assert.InRange(session.CacheStatistics.CheckpointCount, 1, source.Length / (64 * 1024) + 2);
        // This bounds cumulative allocation, not retained memory or OS working set.
        Assert.InRange(allocated, 0, 64L * 1024 * 1024);
    }

    /// <summary>Canceled changed Full preserves the published cache; retry after a provisional edit matches fresh semantics.</summary>
    [Fact]
    public void Edited_idle_full_retry_matches_fresh_and_preserves_old_on_cancel()
    {
        var source = "a,b\r\n\"" + new string('x', 40000) + "\"\"z\",2\r\nq,3\r\n";
        using var document = new Document(source);
        using var session = Session();
        var previous = document.Snapshot;
        Full(session, previous);
        var statistics = session.CacheStatistics;
        var change = new TextChange(source.IndexOf("\"\"", StringComparison.Ordinal), 1, "!");
        var current = document.Apply(change);
        var edits = new[] { new VersionedEdit(previous.Version, current.Version, change) };
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.Analyze(current, edits,
            new AnalysisRequest(new TextSpan(0, current.Length), AnalysisScope.Full), canceled.Token));
        Assert.Equal(statistics, session.CacheStatistics);
        session.Analyze(current, edits, new AnalysisRequest(new TextSpan(0, 1), AnalysisScope.Visible));
        var actual = Full(session, current);
        using var fresh = Session();
        var expected = Full(fresh, current);
        Assert.Equal(expected.Diagnostics, actual.Diagnostics);
        Assert.Equal(expected.Tokens, actual.Tokens);
        Assert.Equal(Flatten(expected.Root), Flatten(actual.Root));
        AssertOracle(source.Remove(change.Start, change.DeleteLength).Insert(change.Start, change.InsertText), actual);
    }

    /// <summary>Uses the established whole-string policy for compatibility and explicit structural comparisons.</summary>
    private static void AssertOracle(string source, DocumentAnalysis actual)
    {
        var oracle = new CsvPolicy().Analyze(source);
        Assert.Equal(AnalysisCompleteness.Complete, actual.Completeness);
        Assert.Equal(oracle.Diagnostics.Count, actual.TotalDiagnosticCount);
        Assert.Equal(oracle.Diagnostics, actual.Diagnostics);
        Assert.Equal(oracle.Tokens, actual.Tokens);
        Assert.Equal(Flatten(oracle.Root), Flatten(actual.Root));
    }

    /// <summary>Invokes small-document legacy Full projection without multi-window size truncation.</summary>
    private static DocumentAnalysis Full(CsvIncrementalSession session, TextSnapshot snapshot) =>
        session.Analyze(snapshot, [], new AnalysisRequest(new TextSpan(0, snapshot.Length), AnalysisScope.Full));

    /// <summary>Flattens semantic values and source provenance without relying on reference identity.</summary>
    private static IEnumerable<(string Kind, TextSpan Span, string? Value)> Flatten(SemanticNode node)
    {
        yield return (node.Kind, node.Span, node.Value);
        foreach (var child in node.Children)
            foreach (var item in Flatten(child)) yield return item;
    }

    /// <summary>Reads existing internal accounting without introducing a new public contract.</summary>
    private static CsvIncrementalSession Session() => Assert.IsType<CsvIncrementalSession>(new CsvPolicy().CreateSession());
}
