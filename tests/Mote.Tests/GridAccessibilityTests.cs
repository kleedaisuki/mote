using Mote.Formats;
using Mote.Native;

namespace Mote.Tests;

/// <summary>Truthful bounded-window semantics independent of native clients and source mutation.</summary>
public sealed class GridAccessibilityTests
{
    /// <summary>Native pattern indices stay local while CSV labels preserve actual ordinals and gaps.</summary>
    [Fact]
    public void Local_indices_absolute_labels_and_sparse_pending_are_distinct()
    {
        var frame = Frame();
        Assert.Equal(new(1000, 16), frame.Coordinate(0, 0));
        Assert.StartsWith("Row 1001, Column 17;", frame.Cell(frame.Coordinate(0, 0)).Name);
        Assert.Equal(GridValueState.Pending, frame.Cell(frame.Coordinate(1, 0)).State);
        Assert.Null(frame.Cell(frame.Coordinate(1, 0)).PresentationValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.Coordinate(3, 0));
        Assert.Equal(3, frame.Rows.Count);
        Assert.Contains("file total unknown", frame.Status);
    }

    /// <summary>Selection output is bounded to the installed window, not a whole-file array.</summary>
    [Fact]
    public void Selection_retains_full_rectangle_but_reports_only_intersection()
    {
        var frame = Frame() with { Selection = new(new(999, 15), new(1001, 17), false) };
        Assert.Equal(4, frame.SelectedCells().Count());
        Assert.Contains("selection extends outside this window", frame.Status);
        Assert.Equal(new(999, 15), frame.Selection!.Value.Anchor);
    }

    /// <summary>A pending navigation cannot reuse old ready values merely because the version matches.</summary>
    [Fact]
    public void Pending_and_stale_projection_have_no_read_authority()
    {
        var frame = Frame();
        var nav = frame.Navigation! with { Pending = true, Ready = null };
        var pending = NativeGridAccessibility.Create(frame.Id, nav, frame.Ready, frame.Projection,
            frame.Rows, frame.Columns, frame.Selection, frame.FocusedCell, false);
        Assert.Null(pending.Ready);
        Assert.Null(pending.Projection);
        Assert.Equal(GridValueState.Pending, pending.Cell(pending.Coordinate(0, 0)).State);
        var stale = NativeGridAccessibility.Create(frame.Id, null, new(frame.Id.Document with { Version = 9 }, 1),
            frame.Projection, frame.Rows, frame.Columns, null, null, false);
        Assert.Null(stale.Projection);
    }

    /// <summary>Missing, oversized, sanitized clipped and complete empty values are never conflated.</summary>
    [Fact]
    public void State_descriptions_do_not_invent_empty_values()
    {
        var cells = new GridCell[]
        {
            new(16, new(0, 0), new(0, 0), GridValueState.Complete, false),
            new(17, new(0, 1), new(0, 1), GridValueState.Clipped, true, true),
            new(18, new(1, 1), new(1, 0), GridValueState.Oversized, false),
            new(19, null, new(1, 0), GridValueState.Missing, false)
        };
        var row = new GridRow(1000, new(0, 2), new(2, 0), 19, cells);
        var grid = new GridRenderProjection(0, 2, new(1001, null, null), AnalysisCompleteness.CoveredRegion,
            [new(0, 2)], null, new(1000, 1), new(16, 4), "x", [row], [], false, false, false, true);
        var frame = NativeGridAccessibility.Create(new(new(1, 0), 1), null, new(new(1, 0), 1), grid,
            new(1000, 1), new(16, 4), null, null, false);
        Assert.Equal("", frame.Cell(new(1000, 16)).PresentationValue);
        Assert.Contains("empty value", frame.Cell(new(1000, 16)).Name);
        Assert.Contains("display clipped", frame.Cell(new(1000, 17)).Name);
        Assert.Contains("CSV syntax error", frame.Cell(new(1000, 17)).Name);
        Assert.Contains("visible substitutions", frame.Cell(new(1000, 17)).Help);
        Assert.Null(frame.Cell(new(1000, 18)).PresentationValue);
        Assert.Null(frame.Cell(new(1000, 19)).PresentationValue);
        Assert.Equal(GridValueState.Missing, frame.Cell(new(1000, 19)).State);
    }

    /// <summary>Exact add/remove rejects holes and hulls without changing retained endpoints.</summary>
    [Theory]
    [InlineData(true, 0, 2, (int)GridAccessibilityResult.Unsupported)]
    [InlineData(false, 0, 0, (int)GridAccessibilityResult.Unsupported)]
    [InlineData(true, 0, 0, (int)GridAccessibilityResult.NoChange)]
    [InlineData(false, 5, 5, (int)GridAccessibilityResult.NoChange)]
    public void Rectangle_mutations_reject_nonrepresentable_sets(bool add, int row, int col, int expected)
    {
        GridAccessibleSelection? selection = new(new(0, 0), new(1, 1), false);
        GridSelectionMutation mutation = add ? new GridSelectionMutation.AddCell(new(row, col)) :
            new GridSelectionMutation.RemoveCell(new(row, col));
        Assert.Equal((GridAccessibilityResult)expected, NativeGridAccessibility.Mutate(selection, mutation, out var result));
        Assert.Equal(selection, result);
    }

    /// <summary>Only exact single-axis edge changes are admitted; removal of the last cell clears.</summary>
    [Fact]
    public void Single_axis_exact_mutations_and_clear()
    {
        GridAccessibleSelection? line = new(new(8, 3), new(8, 4), false);
        Assert.Equal(GridAccessibilityResult.Applied,
            NativeGridAccessibility.Mutate(line, new GridSelectionMutation.AddCell(new(8, 5)), out var enlarged));
        Assert.Equal(new(8, 5), enlarged!.Value.Active);
        Assert.Equal(GridAccessibilityResult.Applied,
            NativeGridAccessibility.Mutate(enlarged, new GridSelectionMutation.RemoveCell(new(8, 3)), out var reduced));
        Assert.Equal(new(8, 4), reduced!.Value.Anchor);
        Assert.Equal(GridAccessibilityResult.Unsupported,
            NativeGridAccessibility.Mutate(line, new GridSelectionMutation.AddCell(new(9, 4)), out _));
        Assert.Equal(GridAccessibilityResult.Applied,
            NativeGridAccessibility.Mutate(new(new(8, 3), new(8, 3), false), new GridSelectionMutation.RemoveCell(new(8, 3)), out var empty));
        Assert.Null(empty);
        Assert.Equal(GridAccessibilityResult.Applied,
            NativeGridAccessibility.Mutate(line, new GridSelectionMutation.Clear(), out empty));
        Assert.Null(empty);
    }

    /// <summary>Unbounded whole-row semantics are not guessed from visible column count.</summary>
    [Fact]
    public void Whole_row_add_remove_is_unsupported()
    {
        GridAccessibleSelection? selection = new(new(8, 3), new(8, 4), true);
        Assert.Equal(GridAccessibilityResult.Unsupported,
            NativeGridAccessibility.Mutate(selection, new GridSelectionMutation.RemoveCell(new(8, 3)), out var result));
        Assert.Equal(selection, result);
    }

    /// <summary>AX array validation deduplicates and rejects sparse/mixed-window arrays atomically.</summary>
    [Fact]
    public void Array_selection_requires_exact_local_rectangle()
    {
        var frame = Frame();
        Assert.Equal(GridAccessibilityResult.Applied, NativeGridAccessibility.ValidateRectangle(frame,
            [new(1000, 16), new(1000, 17), new(1000, 16)], out var selected));
        Assert.Equal(new(1000, 17), selected!.Value.Active);
        Assert.False(selected.Value.WholeRows);
        Assert.Equal(GridAccessibilityResult.Unsupported, NativeGridAccessibility.ValidateRectangle(frame,
            [new(1000, 16), new(1001, 17)], out selected));
        Assert.Null(selected);
        Assert.Equal(GridAccessibilityResult.InvalidCoordinate, NativeGridAccessibility.ValidateRectangle(frame,
            [new(999, 16)], out _));
        Assert.Equal(GridAccessibilityResult.Applied, NativeGridAccessibility.ValidateRectangle(frame, [], out selected));
        Assert.Null(selected);
    }

    /// <summary>Pending layout itself obeys Formats' 8192-cell cap and exact-empty has no fake cell.</summary>
    [Fact]
    public void Bounds_cap_applies_without_projection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeGridAccessibility.Create(new(new(1, 0), 1),
            null, null, null, new(0, 256), new(0, 64), null, null, false));
        var empty = NativeGridAccessibility.Create(new(new(1, 0), 1), null, null, null,
            new(0, 0), new(0, 0), null, new(0, 0), false);
        Assert.Null(empty.FocusedCell);
        Assert.Empty(empty.SelectedCells());
    }

    /// <summary>Pending requested targets remain selection/status, not fabricated focused ready cells.</summary>
    [Fact]
    public void Pending_slot_focus_is_the_table_not_a_cell()
    {
        var frame = Frame();
        var pending = NativeGridAccessibility.Create(frame.Id, frame.Navigation, frame.Ready, frame.Projection,
            frame.Rows, frame.Columns, new(new(1001, 16), new(1001, 16), false), new(1001, 16), true);
        Assert.True(pending.HasTableFocus);
        Assert.Null(pending.FocusedCell);
        Assert.Equal(new(1001, 16), pending.Selection!.Value.Active);
    }

    /// <summary>Proved exact-empty files have no invented first cell or retained selection.</summary>
    [Fact]
    public void Exact_empty_extent_clears_all_table_coordinates()
    {
        var frame = Frame();
        var nav = frame.Navigation! with
        {
            Ready = null,
            Rows = NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 0, 1, 0),
            Columns = NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 0, 1, 0),
            RequestedRows = new(0, 0), RequestedColumns = new(0, 0)
        };
        var empty = NativeGridAccessibility.Create(frame.Id, nav, null, null, new(0, 0), new(0, 1),
            new(new(0, 0), new(0, 0), false), new(0, 0), true);
        Assert.Equal(0, empty.Rows.Count);
        Assert.Equal(0, empty.Columns.Count);
        Assert.Null(empty.Selection);
        Assert.Null(empty.FocusedCell);
        Assert.Contains("no Grid selection", empty.Status);
    }

    /// <summary>Edge removal does not silently reverse the adapter's retained anchor and active endpoint.</summary>
    [Fact]
    public void Reversed_endpoint_direction_survives_exact_edge_removal()
    {
        var reversed = new GridAccessibleSelection(new(4, 6), new(4, 3), false);
        Assert.Equal(GridAccessibilityResult.Applied,
            NativeGridAccessibility.Mutate(reversed, new GridSelectionMutation.RemoveCell(new(4, 6)), out var next));
        Assert.Equal(new(4, 5), next!.Value.Anchor);
        Assert.Equal(new(4, 3), next.Value.Active);
        Assert.Equal(GridAccessibilityResult.Applied,
            NativeGridAccessibility.Mutate(reversed, new GridSelectionMutation.AddCell(new(4, 7)), out next));
        Assert.Equal(new(4, 7), next!.Value.Anchor);
        Assert.Equal(new(4, 3), next.Value.Active);
    }

    /// <summary>Exhaustive small finite sets independently check exact union/difference, not implementation formulas.</summary>
    [Fact]
    public void Exact_mutation_matches_finite_set_oracle()
    {
        for (var top = 0; top < 4; top++)
        for (var bottom = top; bottom < 4; bottom++)
        for (var left = 0; left < 4; left++)
        for (var right = left; right < 4; right++)
        for (var row = 0; row < 5; row++)
        for (var column = 0; column < 5; column++)
        {
            var rectangle = new GridAccessibleSelection(new(top, left), new(bottom, right), false);
            var original = Expand(rectangle);
            foreach (var add in new[] { true, false })
            {
                var requested = new HashSet<GridCoordinate>(original);
                var changed = add ? requested.Add(new(row, column)) : requested.Remove(new(row, column));
                var representable = requested.Count == 0 ||
                    Enumerable.Range(requested.Min(c => c.Row), requested.Max(c => c.Row) - requested.Min(c => c.Row) + 1)
                        .SelectMany(r => Enumerable.Range(requested.Min(c => c.Column), requested.Max(c => c.Column) - requested.Min(c => c.Column) + 1)
                            .Select(c => new GridCoordinate(r, c))).ToHashSet().SetEquals(requested);
                GridSelectionMutation mutation = add ? new GridSelectionMutation.AddCell(new(row, column)) :
                    new GridSelectionMutation.RemoveCell(new(row, column));
                var result = NativeGridAccessibility.Mutate(rectangle, mutation, out var next);
                Assert.Equal(!changed ? GridAccessibilityResult.NoChange : representable ? GridAccessibilityResult.Applied :
                    GridAccessibilityResult.Unsupported, result);
                Assert.True(Expand(next).SetEquals(representable ? requested : original));
            }
        }
    }

    /// <summary>Materializes only tiny test sets, independently of the production rectangle reducer.</summary>
    private static HashSet<GridCoordinate> Expand(GridAccessibleSelection? rectangle) => rectangle is { } selected ?
        Enumerable.Range(Math.Min(selected.Anchor.Row, selected.Active.Row), Math.Abs(selected.Anchor.Row - selected.Active.Row) + 1)
            .SelectMany(row => Enumerable.Range(Math.Min(selected.Anchor.Column, selected.Active.Column),
                Math.Abs(selected.Anchor.Column - selected.Active.Column) + 1).Select(column => new GridCoordinate(row, column))).ToHashSet() : [];

    /// <summary>Creates sparse bounded delivery at a large absolute offset without large source data.</summary>
    private static GridAccessibilityFrame Frame()
    {
        var row = new GridRow(1000, new(0, 1), new(1, 0), 18,
            [new(16, new(0, 1), new(0, 1), GridValueState.Complete, false)]);
        var grid = new GridRenderProjection(0, 1, new(1003, null, null), AnalysisCompleteness.CoveredRegion,
            [new(0, 1)], null, new(1000, 3), new(16, 2), "a", [row], [], true, false, false, false);
        var ready = new NativePresentationId(new(1, 0), 1);
        var nav = new NativeGridScrollFrame(new(new(1, 0), 1), ready,
            NativeGridScrollAxis.Create(NativeGridExtentKind.Prefix, 1003, 3, 1000),
            NativeGridScrollAxis.Create(NativeGridExtentKind.Prefix, 18, 2, 16), new(1000, 3), new(16, 2), 1, false, "Ready");
        return NativeGridAccessibility.Create(new(new(1, 0), 1), nav, ready, grid,
            new(1000, 3), new(16, 2), null, null, false);
    }
}
