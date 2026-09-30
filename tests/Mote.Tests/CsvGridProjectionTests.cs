using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>CSV Grid publishes explicit bounded delivery without losing giant-column identity.</summary>
public sealed class CsvGridProjectionTests
{
    /// <summary>A giant first field remains column zero; its small successor never becomes zero.</summary>
    [Fact]
    public void Giant_field_and_tail_have_actual_coordinates_without_decoded_copy()
    {
        var source = "\"" + new string('x', 100_000) + "\",tail\r\nnext,\n";
        using var document = new Document(source);
        using var session = Session();
        var result = session.AnalyzeGrid(document.Snapshot, [], Request(new CsvGridAnchor.Row(0), 2, 3, AnalysisScope.Full));
        Assert.Equal(2, result.Grid.Extent.ExactRowCount);
        Assert.Equal(2, result.Grid.Extent.ExactMaxWidth);
        Assert.Equal(AnalysisCompleteness.Complete, result.Grid.Completeness);
        var row = result.Grid.Rows[0];
        Assert.Equal(GridValueState.Oversized, row.Cells[0].State);
        Assert.Equal(new TextSpan(0, 100_002), row.Cells[0].SourceRange);
        Assert.Equal(1, row.Cells[1].Column);
        Assert.Equal("tail", Text(result.Grid, row.Cells[1]));
        Assert.Equal(GridValueState.Missing, row.Cells[2].State);
        var empty = result.Grid.Rows[1].Cells[1];
        Assert.Equal(GridValueState.Complete, empty.State);
        Assert.Equal(new TextSpan(source.Length - 1, 0), empty.SourceRange);
        Assert.Equal(0, empty.DisplayRange.Length);
        Assert.True(result.Grid.ValuesTruncated);
    }

    /// <summary>Warm two-window queries of a 100 Mi giant value allocate no decoded giant and scan no giant units.</summary>
    [Fact]
    public void Hundred_mi_giant_warm_grid_projection_is_bounded()
    {
        const int size = 100 * 1024 * 1024;
        using var document = new Document("\"" + new string('x', size) + "\",tail\n");
        using var session = Session();
        var windows = new[] { new TextSpan(1, 2), new TextSpan(size + 3, 4) };
        var request = new CsvGridRequest(windows, new CsvGridAnchor.Row(0), 1, new GridRange(0, 2), AnalysisScope.Full);
        session.AnalyzeGrid(document.Snapshot, [], request);
        request = new CsvGridRequest(windows, new CsvGridAnchor.Row(0), 1, new GridRange(0, 2), AnalysisScope.Visible);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = session.AnalyzeGrid(document.Snapshot, [], request);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(GridValueState.Oversized, result.Grid.Rows[0].Cells[0].State);
        Assert.Equal("tail", Text(result.Grid, result.Grid.Rows[0].Cells[1]));
        Assert.InRange(allocated, 0, 256 * 1024);
        Assert.InRange(session.CacheStatistics.ScannedSourceUnits, 0, 64);
        Assert.InRange(session.CacheStatistics.EstimatedRetainedIndexBytes, 0, 32L * 1024 * 1024 - 1);
    }

    /// <summary>Millions of empty source columns do not materialize a row-sized cell array.</summary>
    [Fact]
    public void Wide_empty_record_seeks_actual_column_checkpoint()
    {
        const int columns = 500_000;
        using var document = new Document(new string(',', columns) + "tail");
        using var session = Session();
        session.AnalyzeGrid(document.Snapshot, [], Request(new CsvGridAnchor.Row(0), 1, 1, AnalysisScope.Full));
        var request = new CsvGridRequest([new TextSpan(0, 1)], new CsvGridAnchor.Row(0), 1,
            new GridRange(columns - 1, 3), AnalysisScope.Visible);
        var result = session.AnalyzeGrid(document.Snapshot, [], request);
        Assert.Equal(columns + 1, result.Grid.Rows[0].Width);
        Assert.Equal(columns - 1, result.Grid.Rows[0].Cells[0].Column);
        Assert.Equal(GridValueState.Complete, result.Grid.Rows[0].Cells[0].State);
        Assert.Equal("tail", Text(result.Grid, result.Grid.Rows[0].Cells[1]));
        Assert.Equal(GridValueState.Missing, result.Grid.Rows[0].Cells[2].State);
        Assert.InRange(session.CacheStatistics.ScannedSourceUnits, 0, 3 * 64 * 1024);
    }

    /// <summary>A clipped display keeps its full syntax origin and never splits a supplementary scalar.</summary>
    [Fact]
    public void Display_arena_and_cell_limits_are_scalar_safe_and_explicit()
    {
        using var document = new Document(string.Join(',', Enumerable.Repeat(new string('x', 1023) + "😀", 64)));
        using var session = Session();
        var result = session.AnalyzeGrid(document.Snapshot, [], Request(new CsvGridAnchor.Row(0), 1, 64, AnalysisScope.Full));
        Assert.All(result.Grid.Rows[0].Cells, cell => Assert.Equal(GridValueState.Clipped, cell.State));
        Assert.All(result.Grid.Rows[0].Cells, cell => Assert.Equal(1023, cell.DisplayRange.Length));
        Assert.Equal(64 * 1023, result.Grid.DisplayText.Length);
        Assert.True(result.Grid.ValuesTruncated);
    }

    /// <summary>Syntax controls are display substitutions, never authoritative copied data.</summary>
    [Fact]
    public void Display_controls_are_marked_sanitized()
    {
        using var document = new Document("\"a\t\r\nb\0\"");
        using var session = Session();
        var result = session.AnalyzeGrid(document.Snapshot, [], Request(new CsvGridAnchor.Row(0), 1, 1, AnalysisScope.Full));
        var cell = result.Grid.Rows[0].Cells[0];
        Assert.Equal(GridValueState.Complete, cell.State);
        Assert.True(cell.DisplaySanitized);
        Assert.Equal("a⇥␍↵b�", Text(result.Grid, cell));
    }

    /// <summary>A giant local syntax summary exposes exact origin and honest omitted diagnostics.</summary>
    [Fact]
    public void Giant_error_summary_is_not_hidden_by_width_warning()
    {
        using var document = new Document("ok,ok\n" + new string('x', 100_000) + "\"\"\"\n");
        using var session = Session();
        var result = session.AnalyzeGrid(document.Snapshot, [], Request(new CsvGridAnchor.Row(1), 1, 1, AnalysisScope.Full));
        Assert.True(result.Grid.Rows[0].Cells[0].HasSyntaxError);
        Assert.Equal(4, result.Grid.TotalDiagnosticCount);
        Assert.True(result.Grid.DiagnosticsTruncated);
        Assert.Single(result.Grid.Diagnostics, diagnostic => diagnostic.Code == "CSV004");
    }

    /// <summary>Wide ordinary fields obey replay admission and mark undelivered actual cells Pending, not Missing.</summary>
    [Fact]
    public void Bounded_row_replay_exposes_pending_cells_with_exact_width()
    {
        using var document = new Document(string.Join(',', Enumerable.Repeat(new string('x', 60_000), 64)));
        using var session = Session();
        var full = Request(new CsvGridAnchor.Row(0), 1, 1, AnalysisScope.Full);
        session.AnalyzeGrid(document.Snapshot, [], full);
        var result = session.AnalyzeGrid(document.Snapshot, [], Request(new CsvGridAnchor.Row(0), 1, 64, AnalysisScope.Visible));
        Assert.Equal(64, result.Grid.Rows[0].Width);
        Assert.Equal(64, result.Grid.Rows[0].Cells.Count);
        Assert.Contains(result.Grid.Rows[0].Cells, cell => cell.State == GridValueState.Pending);
        Assert.DoesNotContain(result.Grid.Rows[0].Cells, cell => cell.State == GridValueState.Missing);
        Assert.True(result.Grid.ColumnsTruncated);
        Assert.True(result.Grid.ValuesTruncated);
        Assert.Equal(AnalysisCompleteness.Complete, result.Grid.Completeness);
        Assert.InRange(session.CacheStatistics.ScannedSourceUnits, 0, 5 * 64 * 1024);
    }

    /// <summary>Timed cancellation has two legal outcomes; neither publishes a partially updated coordinate cache.</summary>
    [Fact]
    public void Timed_full_index_cancellation_preserves_old_state_or_commits_exact_new_state()
    {
        using var document = new Document("a,b\n");
        using var session = Session();
        var old = document.Snapshot;
        session.AnalyzeGrid(old, [], Request(new CsvGridAnchor.Row(0), 1, 2, AnalysisScope.Full));
        var stats = session.CacheStatistics;
        var change = new TextChange(0, old.Length, "\"" + new string('x', 10 * 1024 * 1024) + "\",tail\n");
        var current = document.Apply(change);
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(1);
        try
        {
            var result = session.AnalyzeGrid(current, [new VersionedEdit(old.Version, current.Version, change)],
                Request(new CsvGridAnchor.Row(0), 1, 2, AnalysisScope.Full), cancellation.Token);
            Assert.Equal(current.Version, session.CacheStatistics.Version);
            Assert.Equal(1, result.Grid.Extent.ExactRowCount);
            Assert.Equal(GridValueState.Oversized, result.Grid.Rows[0].Cells[0].State);
            Assert.Equal("tail", Text(result.Grid, result.Grid.Rows[0].Cells[1]));
        }
        catch (OperationCanceledException)
        {
            Assert.True(cancellation.IsCancellationRequested);
            Assert.Equal(stats, session.CacheStatistics);
            var result = session.AnalyzeGrid(old, [], Request(new CsvGridAnchor.Row(0), 1, 2, AnalysisScope.Visible));
            Assert.Equal("a", Text(result.Grid, result.Grid.Rows[0].Cells[0]));
        }
    }

    /// <summary>The maximum Cartesian admission preserves real empty versus arena-exhausted clipped values.</summary>
    [Fact]
    public void Eight_thousand_cells_share_a_sixty_four_ki_display_arena()
    {
        var record = string.Join(',', Enumerable.Repeat("123456789", 32));
        using var document = new Document(string.Join('\n', Enumerable.Repeat(record, 256)));
        using var session = Session();
        var result = session.AnalyzeGrid(document.Snapshot, [], Request(new CsvGridAnchor.Row(0), 256, 32, AnalysisScope.Full));
        Assert.Equal(256, result.Grid.Rows.Count);
        Assert.Equal(8192, result.Grid.Rows.Sum(row => row.Cells.Count));
        Assert.Equal(64 * 1024, result.Grid.DisplayText.Length);
        var last = result.Grid.Rows[^1].Cells[^1];
        Assert.Equal(GridValueState.Clipped, last.State);
        Assert.Equal(0, last.DisplayRange.Length);
        Assert.Equal(9, last.SourceRange!.Value.Length);
        Assert.True(result.Grid.ValuesTruncated);
        Assert.False(result.Grid.RowsTruncated);
        Assert.False(result.Grid.ColumnsTruncated);
    }

    private static CsvIncrementalSession Session() => Assert.IsType<CsvIncrementalSession>(new CsvPolicy().CreateSession());
    private static CsvGridRequest Request(CsvGridAnchor anchor, int rows, int columns, AnalysisScope scope) =>
        new([new TextSpan(0, 1)], anchor, rows, new GridRange(0, columns), scope);
    private static string Text(GridRenderProjection grid, GridCell cell) =>
        grid.DisplayText.Substring(cell.DisplayRange.Start, cell.DisplayRange.Length);
}
