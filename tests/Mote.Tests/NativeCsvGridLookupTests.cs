using Mote.Formats;
using Mote.Native;

namespace Mote.Tests;

/// <summary>Ready-cache lookups preserve actual coordinates even when bounded delivery has gaps.</summary>
public sealed class NativeCsvGridLookupTests
{
    /// <summary>Sorted delivery does not imply contiguous logical records.</summary>
    [Fact]
    public void Row_lookup_preserves_omitted_ordinal_gap()
    {
        var first = new GridRow(0, new(0, 1), new(1, 1), 1,
            [new(0, new(0, 1), new(0, 1), GridValueState.Complete, false)]);
        var last = new GridRow(2, new(4, 1), new(5, 1), 1,
            [new(0, new(4, 1), new(1, 1), GridValueState.Complete, false)]);
        var grid = new GridRenderProjection(0, 6, new(3, 3, 1), AnalysisCompleteness.Complete,
            [new(0, 6)], 0, new(0, 3), new(0, 1), "ac", [first, last], [], true, false, false, false);
        Assert.Same(first, NativeCsvGrid.Row(grid, 0));
        Assert.Null(NativeCsvGrid.Row(grid, 1));
        Assert.Same(last, NativeCsvGrid.Row(grid, 2));
    }

    /// <summary>A sparse field list never renumbers an emitted tail value.</summary>
    [Fact]
    public void Cell_lookup_preserves_omitted_column_gap()
    {
        var row = new GridRow(0, new(0, 5), new(5, 0), 4,
            [new(0, new(0, 1), new(0, 1), GridValueState.Complete, false),
             new(3, new(4, 1), new(1, 1), GridValueState.Complete, false)]);
        Assert.Null(NativeCsvGrid.Cell(row, 1));
        Assert.Equal(3, NativeCsvGrid.Cell(row, 3)?.Column);
    }
}
