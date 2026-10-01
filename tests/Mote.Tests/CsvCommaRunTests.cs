using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Regression contracts for span-batched empty-field scanning.</summary>
public sealed class CsvCommaRunTests
{
    /// <summary>Delimiter runs crossing cursor windows retain legacy rows, values, and diagnostics.</summary>
    [Fact]
    public void Long_comma_run_matches_legacy_before_and_after_edit()
    {
        var source = new string('x', 5_000) + new string(',', 6_000) + "\r\n\"a,b\",c\r\n";
        using var document = new Document(source);
        var policy = new CsvPolicy();
        using var session = policy.CreateSession();

        AssertEquivalent(policy.Analyze(source), session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, document.Snapshot.Length), AnalysisScope.Full)));

        var before = document.Snapshot;
        var change = new TextChange(8_193, 1, "q");
        var after = document.Apply(change);
        var editedSource = source.Remove(change.Start, change.DeleteLength).Insert(change.Start, change.InsertText);
        AssertEquivalent(policy.Analyze(editedSource), session.Analyze(after,
            [new VersionedEdit(before.Version, after.Version, change)],
            new AnalysisRequest(new TextSpan(0, after.Length), AnalysisScope.Full)));
    }

    /// <summary>Empty leading and trailing cells preserve the first-row width contract.</summary>
    [Theory]
    [InlineData(",,,\n,a\n")]
    [InlineData("a,,,\r\n\"x,y\",z\r\n")]
    [InlineData(",\r\n\r\n")]
    public void Short_runs_match_legacy(string source)
    {
        using var document = new Document(source);
        var policy = new CsvPolicy();
        using var session = policy.CreateSession();
        AssertEquivalent(policy.Analyze(source), session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, source.Length), AnalysisScope.Full)));
    }

    /// <summary>Offscreen empty cells are skipped without losing distant viewport boundaries.</summary>
    [Fact]
    public void Distant_comma_viewport_projects_exact_empty_cell_offsets()
    {
        using var document = new Document(new string(',', 1_000_000));
        using var session = new CsvPolicy().CreateSession();
        var result = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(800_000, 16), AnalysisScope.Full));

        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(0, result.TotalDiagnosticCount);
        var row = Assert.Single(result.Root.Children);
        Assert.Equal(1_000_000, row.Span.Length);
        Assert.Equal(17, row.Children.Count);
        Assert.Equal(Enumerable.Range(800_000, 17).Select(position => new TextSpan(position, 0)),
            row.Children.Select(cell => cell.Span));
    }

    /// <summary>A prefix limit inside one record never certifies a partial logical row.</summary>
    [Fact]
    public void Visible_scan_stops_inside_comma_run_without_partial_record()
    {
        using var document = new Document(new string(',', 70_000) + "\nnext\n");
        using var session = new CsvPolicy().CreateSession();
        var result = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 16), AnalysisScope.Visible));

        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.Equal(new TextSpan(0, 0), result.Coverage);
        Assert.Null(result.TotalDiagnosticCount);
        Assert.Empty(result.Root.Children);
    }

    private static void AssertEquivalent(FormatAnalysis legacy, DocumentAnalysis incremental)
    {
        Assert.Equal(AnalysisCompleteness.Complete, incremental.Completeness);
        Assert.Equal(legacy.Diagnostics, incremental.Diagnostics);
        Assert.Equal(legacy.Tokens, incremental.Tokens);
        Assert.Equal(legacy.Root.Children.Count, incremental.Root.Children.Count);
        for (var row = 0; row < legacy.Root.Children.Count; row++)
        {
            var expected = legacy.Root.Children[row];
            var actual = incremental.Root.Children[row];
            Assert.Equal(expected.Span, actual.Span);
            Assert.Equal(expected.Children.Select(cell => (cell.Span, cell.Value)),
                actual.Children.Select(cell => (cell.Span, cell.Value)));
        }
    }
}
