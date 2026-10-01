using Mote.Formats;

namespace Mote.Native;

/// <summary>Identifies one bounded installed window, never a recycled native slot.</summary>
internal readonly record struct GridAccessibilityId(NativeDocumentStamp Document, long WindowSerial);
/// <summary>Absolute zero-based CSV coordinates, not native pattern indices.</summary>
internal readonly record struct GridCoordinate(int Row, int Column);
/// <summary>The adapter-owned complete rectangle, including retained off-window endpoints.</summary>
internal readonly record struct GridAccessibleSelection(GridCoordinate Anchor, GridCoordinate Active, bool WholeRows);
/// <summary>Closed synchronous selection/focus results; queued work is never Applied.</summary>
internal enum GridAccessibilityResult { Applied, NoChange, Unsupported, Stale, NotReady, InvalidCoordinate, Unavailable, CompositionBlocked }
/// <summary>Selection requests admitted by the adapter's sole selection owner.</summary>
internal abstract record GridSelectionMutation
{
    /// <summary>Replaces all retained selection, not only its visible intersection.</summary>
    internal sealed record ReplaceRectangle(GridAccessibleSelection Selection) : GridSelectionMutation;
    /// <summary>Clears the complete retained selection.</summary>
    internal sealed record Clear : GridSelectionMutation;
    /// <summary>Adds one cell only when the exact union remains a rectangle.</summary>
    internal sealed record AddCell(GridCoordinate Cell) : GridSelectionMutation;
    /// <summary>Removes one cell only when the exact difference remains a rectangle.</summary>
    internal sealed record RemoveCell(GridCoordinate Cell) : GridSelectionMutation;
}
/// <summary>UI-thread admission; no source decode, worker wait, or source command dispatch.</summary>
internal interface IGridAccessibilityActions
{
    /// <summary>Mutates and publishes coherent selection before reporting success.</summary>
    GridAccessibilityResult MutateSelection(GridAccessibilityId id, GridSelectionMutation mutation);
    /// <summary>Reads back native focus before reporting success.</summary>
    GridAccessibilityResult Focus(GridAccessibilityId id, GridCoordinate? cell);
}
/// <summary>Bounded presentation semantics; a null value is not an invented empty field.</summary>
internal sealed record GridAccessibleCell(string Name, string Help, string? PresentationValue,
    GridValueState State, bool HasSyntaxError, bool DisplaySanitized);

/// <summary>Snapshot-free immutable facts captured once by each provider request.</summary>
internal sealed record GridAccessibilityFrame(GridAccessibilityId Id, NativeGridScrollFrame? Navigation,
    NativePresentationId? Ready, GridRenderProjection? Projection, GridRange Rows, GridRange Columns,
    GridAccessibleSelection? Selection, GridCoordinate? FocusedCell, bool HasTableFocus)
{
    /// <summary>Tests the admitted bounded layout, including pending and Missing cells.</summary>
    internal bool Contains(GridCoordinate cell) => cell.Row >= Rows.Start && cell.Row < Rows.End &&
        cell.Column >= Columns.Start && cell.Column < Columns.End;

    /// <summary>Maps local native indices to checked absolute CSV coordinates.</summary>
    internal GridCoordinate Coordinate(int row, int column)
    {
        if ((uint)row >= (uint)Rows.Count || (uint)column >= (uint)Columns.Count)
            throw new ArgumentOutOfRangeException(nameof(row));
        return new(checked(Rows.Start + row), checked(Columns.Start + column));
    }

    /// <summary>Reports bounded presentation, never decoded/source text or clipboard authority.</summary>
    internal GridAccessibleCell Cell(GridCoordinate coordinate)
    {
        if (!Contains(coordinate)) throw new ArgumentOutOfRangeException(nameof(coordinate));
        var row = Projection is { } grid ? NativeCsvGrid.Row(grid, coordinate.Row) : null;
        var cell = row is null ? null : NativeCsvGrid.Cell(row, coordinate.Column);
        var state = cell?.State ?? GridValueState.Pending;
        var value = cell is { State: GridValueState.Complete or GridValueState.Clipped } descriptor ?
            Projection!.DisplayText.Substring(descriptor.DisplayRange.Start, descriptor.DisplayRange.Length) : null;
        var detail = state switch
        {
            GridValueState.Complete => value!.Length == 0 ? "empty value" : "presentation value: " + value,
            GridValueState.Clipped => "presentation value: " + value + "; display clipped; use Copy or reveal source for full value",
            GridValueState.Oversized => "Oversized value; value not materialized; reveal source",
            GridValueState.Missing => "Missing field in this record",
            _ => "Pending; requested row/field not ready"
        };
        if (cell?.HasSyntaxError == true) detail += "; CSV syntax error";
        if (cell?.DisplaySanitized == true) detail += "; display contains visible substitutions";
        var label = $"Row {(long)coordinate.Row + 1}, Column {(long)coordinate.Column + 1}";
        return new(label + "; " + detail,
            "Bounded CSV window; native indices are local. " + detail +
            ". Select/focus does not reveal source. Keyboard: Enter/Return reveal source; F2 or Command-Return replace; Ctrl+C or Command-C copy.",
            value, state, cell?.HasSyntaxError == true, cell?.DisplaySanitized == true);
    }

    /// <summary>Tests the full retained rectangle while enumeration remains window-bounded.</summary>
    internal bool IsSelected(GridCoordinate cell) => Selection is { } selected &&
        cell.Row >= Math.Min(selected.Anchor.Row, selected.Active.Row) && cell.Row <= Math.Max(selected.Anchor.Row, selected.Active.Row) &&
        (selected.WholeRows || cell.Column >= Math.Min(selected.Anchor.Column, selected.Active.Column) &&
            cell.Column <= Math.Max(selected.Anchor.Column, selected.Active.Column));

    /// <summary>Enumerates only the admitted window intersection, with at most 8192 entries.</summary>
    internal IEnumerable<GridCoordinate> SelectedCells()
    {
        for (var row = 0; row < Rows.Count; row++)
            for (var column = 0; column < Columns.Count; column++)
            {
                var cell = Coordinate(row, column);
                if (IsSelected(cell)) yield return cell;
            }
    }

    /// <summary>Separates proved file/prefix extent from the bounded Table dimensions.</summary>
    internal string Status
    {
        get
        {
            var extent = Navigation is { } nav ? nav.Rows.Kind switch
            {
                NativeGridExtentKind.Exact => $"File: {nav.Rows.Count} records; maximum {nav.Columns.Count} columns",
                NativeGridExtentKind.Prefix => $"Indexed prefix: {nav.Rows.Count} rows; file total unknown; known columns: at least {nav.Columns.Count}; file maximum unknown",
                _ => "Indexing; CSV row coordinates unavailable"
            } : Projection is { } grid ? grid.Extent.ExactRowCount is { } total ?
                $"File: {total} records; maximum {grid.Extent.ExactMaxWidth} columns" :
                $"Indexed prefix: {grid.Extent.CertifiedPrefixRows} rows; file total unknown; file maximum unknown" :
                "Indexing; CSV row coordinates unavailable";
            extent += $"; window: {Rows.Count} rows, {Columns.Count} columns";
            if (Navigation is { Pending: true } pending) extent += "; pending target; " + pending.Status;
            if (Selection is not { } selected) return extent + "; no Grid selection";
            extent += $"; selection anchor Row {(long)selected.Anchor.Row + 1}, Column {(long)selected.Anchor.Column + 1}; active Row {(long)selected.Active.Row + 1}, Column {(long)selected.Active.Column + 1}";
            if (!Contains(selected.Anchor) || !Contains(selected.Active))
                extent += "; selection extends outside this window";
            if (selected.WholeRows) extent += "; whole-row selection; reported cells are the window intersection only";
            return extent;
        }
    }
}

/// <summary>Pure bounded construction and exact rectangular-selection admission.</summary>
internal static class NativeGridAccessibility
{
    /// <summary>Rejects excessive layout and removes projection authority from pending placement.</summary>
    internal static GridAccessibilityFrame Create(GridAccessibilityId id, NativeGridScrollFrame? navigation,
        NativePresentationId? ready, GridRenderProjection? projection, GridRange rows, GridRange columns,
        GridAccessibleSelection? selection, GridCoordinate? focused, bool hasFocus)
    {
        if (rows.Count > GridRenderProjection.MaxRows || columns.Count > GridRenderProjection.MaxColumns ||
            (long)rows.Count * columns.Count > GridRenderProjection.MaxCells)
            throw new ArgumentOutOfRangeException(nameof(rows));
        if (navigation is { } nav && nav.Navigation.Document != id.Document)
            throw new ArgumentException("Navigation belongs to another document.", nameof(navigation));
        if (ready is not { } admitted || admitted.Document != id.Document || projection?.Version != id.Document.Version ||
            navigation is { } current && (current.Pending || current.Ready != ready))
        { ready = null; projection = null; }
        if (navigation is { Rows.Kind: NativeGridExtentKind.Exact, Rows.Count: 0 } || projection?.Extent.ExactRowCount == 0)
        {
            rows = new(0, 0); columns = new(0, 0);
            selection = null; focused = null;
        }
        var frame = new GridAccessibilityFrame(id, navigation, ready, projection, rows, columns, selection, focused, hasFocus);
        return focused is { } cell && (!frame.Contains(cell) || frame.Cell(cell).State == GridValueState.Pending)
            ? frame with { FocusedCell = null } : frame;
    }

    /// <summary>Validates an exact local cell array before replacing complete retained selection.</summary>
    internal static GridAccessibilityResult ValidateRectangle(GridAccessibilityFrame frame,
        IReadOnlyList<GridCoordinate> cells, out GridAccessibleSelection? selection)
    {
        selection = null;
        if (cells.Count > GridRenderProjection.MaxCells) return GridAccessibilityResult.Unsupported;
        if (cells.Count == 0) return GridAccessibilityResult.Applied;
        var unique = new HashSet<GridCoordinate>();
        var first = cells[0];
        var minRow = first.Row; var maxRow = first.Row; var minColumn = first.Column; var maxColumn = first.Column;
        foreach (var cell in cells)
        {
            if (!frame.Contains(cell)) return GridAccessibilityResult.InvalidCoordinate;
            unique.Add(cell);
            minRow = Math.Min(minRow, cell.Row); maxRow = Math.Max(maxRow, cell.Row);
            minColumn = Math.Min(minColumn, cell.Column); maxColumn = Math.Max(maxColumn, cell.Column);
        }
        if (((long)maxRow - minRow + 1) * ((long)maxColumn - minColumn + 1) != unique.Count)
            return GridAccessibilityResult.Unsupported;
        selection = new(new(minRow, minColumn), new(maxRow, maxColumn), false);
        return GridAccessibilityResult.Applied;
    }

    /// <summary>Computes exact union/difference without mutating the adapter or selecting a hull.</summary>
    internal static GridAccessibilityResult Mutate(GridAccessibleSelection? current,
        GridSelectionMutation mutation, out GridAccessibleSelection? next)
    {
        next = current;
        if (mutation is GridSelectionMutation.Clear)
        { next = null; return current is null ? GridAccessibilityResult.NoChange : GridAccessibilityResult.Applied; }
        if (mutation is GridSelectionMutation.ReplaceRectangle replace)
        {
            if (!Valid(replace.Selection.Anchor) || !Valid(replace.Selection.Active)) return GridAccessibilityResult.InvalidCoordinate;
            next = replace.Selection;
            return next == current ? GridAccessibilityResult.NoChange : GridAccessibilityResult.Applied;
        }
        var target = mutation switch
        {
            GridSelectionMutation.AddCell add => add.Cell,
            GridSelectionMutation.RemoveCell remove => remove.Cell,
            _ => new GridCoordinate(-1, -1)
        };
        if (!Valid(target)) return GridAccessibilityResult.InvalidCoordinate;
        if (current is null)
        {
            if (mutation is GridSelectionMutation.RemoveCell) return GridAccessibilityResult.NoChange;
            next = new(target, target, false); return GridAccessibilityResult.Applied;
        }
        var selected = current.Value;
        if (selected.WholeRows) return GridAccessibilityResult.Unsupported;
        var top = Math.Min(selected.Anchor.Row, selected.Active.Row); var bottom = Math.Max(selected.Anchor.Row, selected.Active.Row);
        var left = Math.Min(selected.Anchor.Column, selected.Active.Column); var right = Math.Max(selected.Anchor.Column, selected.Active.Column);
        var contains = target.Row >= top && target.Row <= bottom && target.Column >= left && target.Column <= right;
        if (mutation is GridSelectionMutation.AddCell)
        {
            if (contains) return GridAccessibilityResult.NoChange;
            var minRow = Math.Min(top, target.Row); var maxRow = Math.Max(bottom, target.Row);
            var minCol = Math.Min(left, target.Column); var maxCol = Math.Max(right, target.Column);
            var oldArea = ((long)bottom - top + 1) * ((long)right - left + 1);
            var area = ((long)maxRow - minRow + 1) * ((long)maxCol - minCol + 1);
            if (area != oldArea + 1) return GridAccessibilityResult.Unsupported;
            if (selected.Anchor == selected.Active) next = new(selected.Anchor, target, false);
            else
            {
                var anchor = new GridCoordinate(selected.Anchor.Row <= selected.Active.Row ? minRow : maxRow,
                    selected.Anchor.Column <= selected.Active.Column ? minCol : maxCol);
                var active = new GridCoordinate(selected.Anchor.Row <= selected.Active.Row ? maxRow : minRow,
                    selected.Anchor.Column <= selected.Active.Column ? maxCol : minCol);
                next = new(anchor, active, false);
            }
            return GridAccessibilityResult.Applied;
        }
        if (!contains) return GridAccessibilityResult.NoChange;
        if (top == bottom && left == right) { next = null; return GridAccessibilityResult.Applied; }
        if (top == bottom && (target.Column == left || target.Column == right))
        { if (target.Column == left) left++; else right--; }
        else if (left == right && (target.Row == top || target.Row == bottom))
        { if (target.Row == top) top++; else bottom--; }
        else return GridAccessibilityResult.Unsupported;
        var remainingAnchor = new GridCoordinate(Math.Clamp(selected.Anchor.Row, top, bottom), Math.Clamp(selected.Anchor.Column, left, right));
        var remainingActive = new GridCoordinate(Math.Clamp(selected.Active.Row, top, bottom), Math.Clamp(selected.Active.Column, left, right));
        next = new(remainingAnchor, remainingActive, false); return GridAccessibilityResult.Applied;
    }

    /// <summary>Coordinates are nonnegative even when full retained selection lies outside the window.</summary>
    private static bool Valid(GridCoordinate cell) => cell.Row >= 0 && cell.Column >= 0;
}
