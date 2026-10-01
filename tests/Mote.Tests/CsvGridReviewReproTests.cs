using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Independent regression probes for bounded Grid diagnostic delivery.</summary>
public sealed class CsvGridReviewReproTests
{
    /// <summary>A omitted width warning must remain observable when syntax fills the arena.</summary>
    [Fact]
    public void Diagnostic_cap_reports_omitted_width_warning()
    {
        var ordinary = string.Join(',', Enumerable.Repeat("a\"", 32));
        var source = string.Join('\n', Enumerable.Repeat(ordinary, 255)) + "\n" + ordinary + ",clean";
        using var document = new Document(source);
        using var session = Assert.IsType<CsvIncrementalSession>(new CsvPolicy().CreateSession());
        var request = new CsvGridRequest([new TextSpan(0, 1)], new CsvGridAnchor.Row(0),
            256, new GridRange(0, 32), AnalysisScope.Full);
        var grid = session.AnalyzeGrid(document.Snapshot, [], request).Grid;
        Assert.Equal(8193, grid.TotalDiagnosticCount);
        Assert.Equal(8192, grid.Diagnostics.Count);
        Assert.True(grid.DiagnosticsTruncated);
        Assert.True(grid.Rows[^1].DiagnosticsTruncated);
    }
}
