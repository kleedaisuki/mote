using Mote.Formats;

namespace Mote.Native;

/// <summary>Names the proved coordinate domain; Prefix never denotes the whole file.</summary>
internal enum NativeGridExtentKind { Unavailable, Prefix, Exact }
/// <summary>Navigation authority, independent of source-backed command authority.</summary>
internal readonly record struct NativeGridNavigationId(NativeDocumentStamp Document, long Epoch);
/// <summary>One admitted gesture; sequences are never reused.</summary>
internal readonly record struct NativeGridGestureId(NativeGridNavigationId Navigation, long Sequence);
/// <summary>Logical axis units rather than retained table rows or pixel extents.</summary>
internal enum NativeGridAxis { Rows, Columns }
/// <summary>Terminal phases consume their token before any reentrant installation.</summary>
internal enum NativeGridGesturePhase { Track, Commit, Cancel }
/// <summary>End is symbolic while certification is unavailable; Cell is never silently clamped.</summary>
internal enum NativeGridTargetKind { Viewport, End, Cell, Retry }
/// <summary>Certified range with checked, bounded page and origin.</summary>
internal readonly record struct NativeGridScrollAxis(NativeGridExtentKind Kind, int Count, int Page, int First)
{
    /// <summary>Last legal visible origin, calculated without integer overflow.</summary>
    internal int Last => Math.Max(0, Count - Math.Min(Count, Page));
    /// <summary>Normalized knob location; exact endpoints remain exact.</summary>
    internal double Position => Last == 0 ? 0 : (double)First / Last;
    /// <summary>Proportion occupied by fully visible slots.</summary>
    internal double Proportion => Count == 0 ? 1 : (double)Math.Min(Count, Page) / Count;
    /// <summary>Creates a validated domain and clamps only viewport placement.</summary>
    internal static NativeGridScrollAxis Create(NativeGridExtentKind kind, int count, int page, int first)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegative(first);
        if (page < 1 || !Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(page));
        return new(kind, count, page, Math.Min(first, Math.Max(0, count - Math.Min(count, page))));
    }
    /// <summary>Rejects nonfinite AppKit values before converting to an ordinal.</summary>
    internal int? FromNormalized(double value) => !double.IsFinite(value) ? null :
        (int)Math.Clamp(Math.Round(Math.Clamp(value, 0, 1) * Last, MidpointRounding.AwayFromZero), 0, Last);
    /// <summary>Uses wide arithmetic for line and page steps.</summary>
    internal int Step(long delta) => delta > Last - (long)First ? Last :
        delta < -(long)First ? 0 : (int)(First + delta);
}
/// <summary>One immutable navigation installation; Ready authorizes commands only when data is delivered.</summary>
internal sealed record NativeGridScrollFrame(NativeGridNavigationId Navigation, NativePresentationId? Ready,
    NativeGridScrollAxis Rows, NativeGridScrollAxis Columns, GridRange RequestedRows,
    GridRange RequestedColumns, long RequestSerial, bool Pending, string Status);
/// <summary>Adapter-provided fully visible geometry is admitted against fixed delivery caps.</summary>
internal readonly record struct NativeGridGestureBegin(NativeGridScrollFrame Frame, int VisibleRows, int VisibleColumns);
/// <summary>The adapter retains this exact token and frozen denominator for every queued phase.</summary>
internal sealed record NativeGridGesture(NativeGridGestureId Id, NativeGridScrollFrame Frame);
/// <summary>One captured phase; Row/Column name absolute ordinals, never source offsets.</summary>
internal readonly record struct NativeGridGestureAction(NativeGridGestureId Id, NativeGridGesturePhase Phase,
    int Row, int Column, NativeGridTargetKind Kind = NativeGridTargetKind.Viewport);

/// <summary>Pure bounded planner shared by native controls and controller tests.</summary>
internal static class NativeGridPlanner
{
    /// <summary>Caps geometry and total cells once; columns never retain file-wide width state.</summary>
    internal static (int Rows, int Columns) Page(int rows, int columns)
    {
        columns = Math.Clamp(columns, 1, GridRenderProjection.MaxColumns);
        rows = Math.Clamp(rows, 1, Math.Min(GridRenderProjection.MaxRows, GridRenderProjection.MaxCells / columns));
        return (rows, columns);
    }
    /// <summary>Preserves ordinal gaps; null slots have no invented GridRow or source origin.</summary>
    internal static GridRow?[] Slots(GridRenderProjection? grid, GridRange requested, int knownRows)
    {
        var count = Math.Min(requested.Count, Math.Max(0, knownRows - requested.Start));
        if (count > GridRenderProjection.MaxRows) throw new ArgumentOutOfRangeException(nameof(requested));
        var slots = new GridRow?[count];
        if (grid is null) return slots;
        foreach (var row in grid.Rows)
        {
            var index = (long)row.Ordinal - requested.Start;
            if (index >= 0 && index < slots.Length) slots[(int)index] = row;
        }
        return slots;
    }
}
