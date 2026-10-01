using Mote.Formats;

namespace Mote.Native;

/// <summary>Explicit Grid operations; display strings are never clipboard authority.</summary>
internal enum NativeGridIntentKind { Select, Reveal, CopyValue, CopyCsv, CopyTsv, CopyRows, CopySource, CopyCsvPadded, Replace }

/// <summary>One logical selection and action bound to the installed native table identity.</summary>
internal readonly record struct NativeGridIntent(NativePresentationId Identity, NativeGridIntentKind Kind,
    int Row, int Column, int? EndRow = null, int? EndColumn = null, bool WholeRows = false);

/// <summary>A bounded table window request; FollowSource ignores Row and uses source caret.</summary>
internal readonly record struct NativeGridWindowRequest(NativePresentationId Identity, int Row,
    GridRange Columns, int RowLimit = 64, bool FollowSource = false);

/// <summary>Shared pure ready-cell lookup used by platform callbacks and command admission.</summary>
internal static class NativeCsvGrid
{
    /// <summary>Finds a proved logical row without scanning or retaining source.</summary>
    internal static GridRow? Row(GridRenderProjection grid, int ordinal)
    {
        var low = 0;
        var high = grid.Rows.Count - 1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            var row = grid.Rows[mid];
            if (row.Ordinal == ordinal) return row;
            if (row.Ordinal < ordinal) low = mid + 1;
            else high = mid - 1;
        }
        return null;
    }

    /// <summary>Finds a delivered field by its actual coordinate, not sparse list position.</summary>
    internal static GridCell? Cell(GridRow row, int column)
    {
        var start = row.Cells.Count == 0 ? 0 : row.Cells[0].Column;
        var local = column - start;
        if ((uint)local < (uint)row.Cells.Count && row.Cells[local].Column == column) return row.Cells[local];
        foreach (var cell in row.Cells) if (cell.Column == column) return cell;
        return null;
    }

    /// <summary>Returns visible explicit state; callers must not decode this display as CSV data.</summary>
    internal static string Display(GridRenderProjection grid, GridCell? cell) => cell switch
    {
        null => "[pending]",
        { State: GridValueState.Missing } => "[missing]",
        { State: GridValueState.Pending } => "[pending]",
        { State: GridValueState.Oversized } => "[oversized — reveal source]",
        { } ready => grid.DisplayText.Substring(ready.DisplayRange.Start, ready.DisplayRange.Length) +
            (ready.State == GridValueState.Clipped ? " [clipped]" : "")
    };
}
