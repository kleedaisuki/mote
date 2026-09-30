using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Forward Full scans borrow immutable chunks without changing CSV semantics or random projection costs.</summary>
public sealed class CsvColdAllocationTests
{
    /// <summary>Full validation avoids payload-sized reader copies at both tested lengths, excluding document creation.</summary>
    [Theory]
    [InlineData(1 * 1024 * 1024)]
    [InlineData(8 * 1024 * 1024)]
    public void Giant_quoted_full_borrows_source_instead_of_copying_windows(int length)
    {
        using var document = new Document("\"" + new string('x', length - 2) + "\"");
        using var session = Session();
        var snapshot = document.Snapshot;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var result = session.AnalyzeWindows(snapshot, [], [new TextSpan(0, 2)], AnalysisScope.Full);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(0, result.TotalDiagnosticCount);
        Assert.Equal(new TextSpan(0, length), Assert.Single(result.Tokens).Span);
        Assert.Equal(length, session.CacheStatistics.ScannedSourceUnits);
        Assert.InRange(allocated, 0, 1024 * 1024);
        var warm = session.AnalyzeWindows(snapshot, [], [new TextSpan(0, 2), new TextSpan(length - 2, 2)], AnalysisScope.Visible);
        Assert.Equal(0, warm.TotalDiagnosticCount);
        Assert.Equal(0, session.CacheStatistics.ScannedSourceUnits);
    }

    /// <summary>Resuming inside a chunk preserves complete diagnostics, exact decoded cells and UTF-16 anchors.</summary>
    [Theory]
    [InlineData(8191)]
    [InlineData(16382)]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(32767)]
    public void Resume_and_fragmented_chunk_boundaries_match_full_oracle(int boundary)
    {
        var source = "a,b\r\n" + new string('x', boundary - 6) + ",\"z\"\"q\r\n😀\"bad\r\n" +
            "un\"quoted,2,3\r\n\"unfinished";
        using var document = new Document(source);
        // A no-op content replacement creates tiny rope leaves around lexer-sensitive units.
        var quote = source.IndexOf("\"\"", StringComparison.Ordinal);
        document.Apply(new TextChange(quote, 1, "\""));
        using var session = Session();
        var snapshot = document.Snapshot;
        session.Analyze(snapshot, [], new AnalysisRequest(new TextSpan(0, 1), AnalysisScope.Visible));
        var result = session.Analyze(snapshot, [], new AnalysisRequest(new TextSpan(0, snapshot.Length), AnalysisScope.Full));
        var oracle = new CsvPolicy().Analyze(source);

        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(oracle.Diagnostics.Count, result.TotalDiagnosticCount);
        Assert.Equal(oracle.Diagnostics, result.Diagnostics);
        Assert.Equal(oracle.Tokens, result.Tokens);
        Assert.Equal(Flatten(oracle.Root), Flatten(result.Root));
        Assert.Contains(result.Diagnostics, item => item.Code == "CSV001");
        Assert.Contains(result.Diagnostics, item => item.Code == "CSV002");
        Assert.Contains(result.Diagnostics, item => item.Code == "CSV003");
        Assert.Contains(result.Diagnostics, item => item.Code == "CSV004");
    }

    /// <summary>A canceled fresh Full cannot replace the committed old certificate or diagnostic count.</summary>
    [Fact]
    public void Precanceled_changed_full_keeps_old_borrowed_source_semantics()
    {
        using var document = new Document("\"" + new string('x', 100_000));
        using var session = Session();
        var before = document.Snapshot;
        session.AnalyzeWindows(before, [], [new TextSpan(0, 1)], AnalysisScope.Full);
        var stats = session.CacheStatistics;
        var change = new TextChange(before.Length, 0, "\"");
        var after = document.Apply(change);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.AnalyzeWindows(after,
            [new VersionedEdit(before.Version, after.Version, change)], [new TextSpan(0, 1)], AnalysisScope.Full,
            cancellation.Token));
        Assert.Equal(stats, session.CacheStatistics);
        var old = session.AnalyzeWindows(before, [], [new TextSpan(0, 1)], AnalysisScope.Visible);
        Assert.Equal(1, old.TotalDiagnosticCount);
        Assert.Equal(new TextSpan(0, before.Length), Assert.Single(old.Diagnostics).Span);
        var updated = session.AnalyzeWindows(after, [], [new TextSpan(0, 1)], AnalysisScope.Full);
        Assert.Equal(0, updated.TotalDiagnosticCount);
        Assert.Equal(new TextSpan(0, after.Length), Assert.Single(updated.Tokens).Span);
    }

    /// <summary>Compares structural meaning instead of object identity.</summary>
    private static IEnumerable<(string Kind, TextSpan Span, string? Value)> Flatten(SemanticNode node)
    {
        yield return (node.Kind, node.Span, node.Value);
        foreach (var child in node.Children)
            foreach (var item in Flatten(child)) yield return item;
    }

    /// <summary>Obtains existing internal accounting without extending the public policy contract.</summary>
    private static CsvIncrementalSession Session() => Assert.IsType<CsvIncrementalSession>(new CsvPolicy().CreateSession());
}
