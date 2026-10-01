using Mote.Formats;

namespace Mote.Native.Mac;

/// <summary>Renderer-derived frame plus refusal facts; this is not a claim that experimental AX nodes were published.</summary>
internal sealed record MacReleaseGridObservation(GridAccessibilityFrame? Frame, int NativeSlots,
    int MissingNativeSlots, bool ColumnsInstalled, bool RenderShapeMatches, bool WindowPending,
    NativePresentationId? NativeIdentity, long? ProjectionVersion, bool NavigationPending);

/// <summary>Portable exact observations of AppKit's installed Grid state rather than optional provider state.</summary>
internal static class MacReleaseGridModel
{
    /// <summary>Refuses missing/misaligned actual slots or incomplete native installation even if projection metadata looks ready.</summary>
    internal static MacReleaseGridObservation Observe(NativePresentationId? identity, GridRenderProjection? projection,
        NativeGridScrollFrame? navigation, IReadOnlyList<GridRow?> slots, GridRange columns,
        GridRange? installedColumns, int readyRows, int readyColumns, bool pending, bool installing, long serial)
    {
        var start = navigation?.RequestedRows.Start ?? projection?.RequestedRows.Start ?? 0;
        var missing = slots.Where((slot, i) => slot is null || slot.Ordinal != start + i ||
            projection is null || !ReferenceEquals(slot, NativeCsvGrid.Row(projection, start + i))).Count();
        var columnsInstalled = installedColumns == columns;
        var shape = readyRows == slots.Count && readyColumns == columns.Count;
        GridAccessibilityFrame? frame = null;
        if (identity is { } ready && projection is not null && !pending && !installing &&
            missing == 0 && columnsInstalled && shape &&
            (navigation is null || navigation.Navigation.Document == ready.Document))
            frame = NativeGridAccessibility.Create(new(ready.Document, serial), navigation, identity, projection,
                new(start, slots.Count), columns, null, null, false);
        return new(frame, slots.Count, missing, columnsInstalled, shape, pending || installing,
            identity, projection?.Version, navigation?.Pending ?? false);
    }
}
