using Mote.Formats;

namespace Mote.Tests;

/// <summary>Validates immutable bounded Grid contracts without invoking the CSV parser.</summary>
public sealed class CsvGridContractTests
{
    /// <summary>Checks the construction invariant named by this test.</summary>
    [Fact]
    public void Coordinate_ranges_and_closed_anchors_reject_negative_or_overflowing_values()
    {
        Assert.Equal(8, new GridRange(3, 5).End);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridRange(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridRange(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridRange(int.MaxValue, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CsvGridAnchor.Source(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CsvGridAnchor.Row(-1));
    }

    /// <summary>Checks the construction invariant named by this test.</summary>
    [Fact]
    public void Request_copies_interests_and_enforces_independent_budgets()
    {
        var interests = new[] { new TextSpan(0, 10) };
        var request = new CsvGridRequest(interests, new CsvGridAnchor.Source(0), 128, new GridRange(0, 64), AnalysisScope.Visible);
        interests[0] = new TextSpan(99, 1);
        Assert.Equal(new TextSpan(0, 10), request.SourceInterests[0]);
        Assert.Throws<ArgumentException>(() => Request(257, 1));
        Assert.Throws<ArgumentException>(() => Request(256, 64));
        Assert.Throws<ArgumentException>(() => Request(1, 65));
        Assert.Throws<ArgumentException>(() => new CsvGridRequest([], new CsvGridAnchor.Row(0), 1, new GridRange(0, 1), AnalysisScope.Visible));
        Assert.Throws<ArgumentException>(() => new CsvGridRequest([new TextSpan(0, 512 * 1024 + 1)], new CsvGridAnchor.Row(0), 1, new GridRange(0, 1), AnalysisScope.Full));
        Assert.Throws<ArgumentException>(() => new CsvGridRequest([new TextSpan(int.MaxValue, 1)], new CsvGridAnchor.Row(0), 1, new GridRange(0, 1), AnalysisScope.Full));
    }

    /// <summary>Checks the construction invariant named by this test.</summary>
    [Fact]
    public void Extent_distinguishes_prefix_from_paired_exact_coordinate_facts()
    {
        Assert.Null(new GridExtent(2, null, null).ExactRowCount);
        Assert.Equal(2, new GridExtent(2, 2, 3).ExactRowCount);
        Assert.Throws<ArgumentException>(() => new GridExtent(3, 2, 1));
        Assert.Throws<ArgumentException>(() => new GridExtent(0, 2, null));
        Assert.Throws<ArgumentException>(() => new GridExtent(0, 0, 1));
    }

    /// <summary>Checks the construction invariant named by this test.</summary>
    [Fact]
    public void Row_keeps_sparse_actual_columns_and_defensively_copies_fields()
    {
        var cells = new[] { Cell(1, new TextSpan(3, 1), new TextSpan(0, 1)) };
        var row = new GridRow(0, new TextSpan(0, 4), new TextSpan(4, 1), 2, cells);
        cells[0] = Cell(0, new TextSpan(0, 1), new TextSpan(0, 1));
        Assert.Equal(1, row.Cells[0].Column);
        Assert.Throws<ArgumentException>(() => new GridRow(0, new TextSpan(0, 4), new TextSpan(3, 1), 2, []));
        Assert.Throws<ArgumentException>(() => new GridRow(0, new TextSpan(0, 4), new TextSpan(4, 0), 2,
            [Cell(1, new TextSpan(0, 1), new TextSpan(0, 1)), Cell(0, new TextSpan(2, 1), new TextSpan(1, 1))]));
    }

    /// <summary>Checks the construction invariant named by this test.</summary>
    [Theory]
    [InlineData(GridValueState.Complete)]
    [InlineData(GridValueState.Clipped)]
    [InlineData(GridValueState.Oversized)]
    public void Delivered_field_requires_whole_origin_inside_actual_row_width(GridValueState state)
    {
        Assert.Throws<ArgumentException>(() => Row(new GridCell(0, null, new TextSpan(0, 0), state, false)));
        Assert.Throws<ArgumentException>(() => Row(new GridCell(0, new TextSpan(0, 3), new TextSpan(0, 0), state, false)));
        Assert.Throws<ArgumentException>(() => Row(new GridCell(1, new TextSpan(0, 1), new TextSpan(0, 0), state, false)));
        Assert.Equal(state, Row(new GridCell(0, new TextSpan(0, 0), new TextSpan(0, 0), state, false)).Cells[0].State);
    }

    /// <summary>Checks the construction invariant named by this test.</summary>
    [Fact]
    public void Missing_and_pending_never_invent_origins_or_syntax_errors()
    {
        Assert.Equal(GridValueState.Missing, Row(new GridCell(1, null, new TextSpan(0, 0), GridValueState.Missing, false)).Cells[0].State);
        Assert.Equal(GridValueState.Pending, Row(new GridCell(0, null, new TextSpan(0, 0), GridValueState.Pending, false)).Cells[0].State);
        Assert.Throws<ArgumentException>(() => Row(new GridCell(0, null, new TextSpan(0, 0), GridValueState.Missing, false)));
        Assert.Throws<ArgumentException>(() => Row(new GridCell(0, new TextSpan(0, 0), new TextSpan(0, 0), GridValueState.Pending, false)));
        Assert.Throws<ArgumentException>(() => Row(new GridCell(1, null, new TextSpan(0, 0), GridValueState.Missing, true)));
    }

    /// <summary>Checks the construction invariant named by this test.</summary>
    [Fact]
    public void Complete_requires_exact_extent_total_and_whole_coverage_but_not_full_delivery()
    {
        var projection = Projection(completeness: AnalysisCompleteness.Complete, extent: new GridExtent(1, 1, 1), total: 0);
        Assert.True(projection.RowsTruncated);
        Assert.Equal(AnalysisCompleteness.Complete, projection.Completeness);
        Assert.Throws<ArgumentException>(() => Projection(completeness: AnalysisCompleteness.Complete, total: 0));
        Assert.Throws<ArgumentException>(() => Projection(completeness: AnalysisCompleteness.Complete, extent: new GridExtent(1, 1, 1)));
        Assert.Throws<ArgumentException>(() => Projection(total: 0));
        Assert.Throws<ArgumentException>(() => Projection(completeness: AnalysisCompleteness.Complete, extent: new GridExtent(1, 1, 1), total: 0, coverage: [new TextSpan(0, 1)]));
        Assert.Equal(1, Projection(extent: new GridExtent(1, 1, 1)).Extent.ExactRowCount);
    }

    /// <summary>Checks the construction invariant named by this test.</summary>
    [Fact]
    public void Projection_copies_collections_and_keeps_disjoint_coverage()
    {
        var coverage = new[] { new TextSpan(0, 0), new TextSpan(2, 0) };
        var rows = new[] { Row(Cell(0, new TextSpan(0, 2), new TextSpan(0, 1))) };
        var diagnostics = new[] { new Diagnostic(DiagnosticSeverity.Error, "CSV001", "bad", new TextSpan(0, 1)) };
        var projection = Projection(coverage: coverage, rows: rows, diagnostics: diagnostics);
        coverage[0] = new TextSpan(99, 0); rows[0] = null!; diagnostics[0] = null!;
        Assert.Equal(new TextSpan(0, 0), projection.CertifiedCoverage[0]);
        Assert.Equal(0, projection.Rows[0].Ordinal);
        Assert.Equal("CSV001", projection.Diagnostics[0].Code);
        Assert.Throws<ArgumentException>(() => Projection(coverage: [new TextSpan(0, 2), new TextSpan(1, 1)]));
        Assert.Throws<ArgumentException>(() => Projection(diagnostics: [new Diagnostic(DiagnosticSeverity.Error, "CSV001", "bad", new TextSpan(2, 1))]));
    }

    /// <summary>Checks the construction invariant named by this test.</summary>
    [Theory]
    [InlineData(0xd800)]
    [InlineData(0xdc00)]
    public void Projection_rejects_unpaired_display_surrogates(int codeUnit)
    {
        Assert.Throws<ArgumentException>(() => Projection(text: new string((char)codeUnit, 1)));
    }

    /// <summary>Checks the construction invariant named by this test.</summary>
    [Fact]
    public void Display_ranges_cannot_split_scalars_overlap_or_escape_arena()
    {
        Assert.Throws<ArgumentException>(() => Projection(text: "😀", rows: [Row(Cell(0, new TextSpan(0, 2), new TextSpan(0, 1)))]));
        Assert.Throws<ArgumentException>(() => Projection(rows: [Row(Cell(0, new TextSpan(0, 2), new TextSpan(0, 2)))]));
        Assert.Equal("😀", Projection(text: "😀", rows: [Row(Cell(0, new TextSpan(0, 2), new TextSpan(0, 2)))]).DisplayText);
        Assert.Throws<ArgumentException>(() => Projection(text: new string('x', 65537)));
        Assert.Throws<ArgumentException>(() => Projection(text: new string('x', 1025), rows: [Row(Cell(0, new TextSpan(0, 2), new TextSpan(0, 1025)))]));
    }

    /// <summary>Checks the construction invariant named by this test.</summary>
    [Fact]
    public void Projection_enforces_requested_and_exact_coordinates()
    {
        Assert.Throws<ArgumentException>(() => Projection(rows: [new GridRow(1, new TextSpan(0, 2), new TextSpan(2, 0), 1, [])]));
        Assert.Throws<ArgumentException>(() => Projection(rows: [new GridRow(0, new TextSpan(0, 2), new TextSpan(2, 0), 2,
            [Cell(1, new TextSpan(0, 1), new TextSpan(0, 1))])]));
        Assert.Throws<ArgumentException>(() => Projection(extent: new GridExtent(0, 0, 0), rows: [Row(Cell(0, new TextSpan(0, 2), new TextSpan(0, 1)))]));
    }

    /// <summary>Unresolved source-follow coordinates retain the original query without guessing a row.</summary>
    [Fact]
    public void Unresolved_source_anchor_roundtrips_with_empty_ordinal_range()
    {
        var anchor = new CsvGridAnchor.Source(100);
        var projection = new GridRenderProjection(0, 200, new GridExtent(2, null, null),
            AnalysisCompleteness.Provisional, [new TextSpan(0, 20)], null, new GridRange(0, 0),
            new GridRange(0, 1), string.Empty, [], [], true, false, false, false, anchor);
        Assert.Equal(anchor, projection.RequestedAnchor);
        Assert.Equal(new GridRange(0, 0), projection.RequestedRows);
        Assert.Empty(projection.Rows);
        Assert.Equal(new CsvGridAnchor.Row(0), Projection().RequestedAnchor);
    }
    /// <summary>Constructs a minimal bounded fixture for invariant checks.</summary>
    private static CsvGridRequest Request(int rows, int columns) => new([new TextSpan(0, 0)], new CsvGridAnchor.Row(0), rows, new GridRange(0, columns), AnalysisScope.Visible);
    /// <summary>Constructs a minimal bounded fixture for invariant checks.</summary>
    private static GridCell Cell(int column, TextSpan origin, TextSpan display) => new(column, origin, display, GridValueState.Complete, false);
    /// <summary>Constructs a minimal bounded fixture for invariant checks.</summary>
    private static GridRow Row(GridCell cell) => new(0, new TextSpan(0, 2), new TextSpan(2, 0), 1, [cell]);
    /// <summary>Constructs a minimal bounded fixture for invariant checks.</summary>
    private static GridRenderProjection Projection(AnalysisCompleteness completeness = AnalysisCompleteness.CoveredRegion,
        GridExtent? extent = null, int? total = null, IReadOnlyList<TextSpan>? coverage = null,
        string text = "x", IReadOnlyList<GridRow>? rows = null, IReadOnlyList<Diagnostic>? diagnostics = null) =>
        new(0, 2, extent ?? new GridExtent(0, null, null), completeness, coverage ?? [new TextSpan(0, 2)], total,
            new GridRange(0, 1), new GridRange(0, 1), text, rows ?? [], diagnostics ?? [], true, false, false, false);
}
