using System.Globalization;
using System.Runtime.Versioning;
using Mote.Formats;

namespace Mote.Native.Mac;

/// <summary>Explicit bounded table navigation; numeric input is not source text or a project setting.</summary>
[SupportedOSPlatform("macos")]
internal static class MacGridNavigation
{
    /// <summary>Parses positive one-based coordinates against frozen exact facts, preserving opening identity.</summary>
    internal static bool TryCreateRequest(string coordinates, NativeGridWindowRequest opening,
        GridExtent extent, out NativeGridWindowRequest request)
    {
        request = default;
        var input = coordinates.AsSpan();
        var separator = input.IndexOf(':');
        if (separator < 1 || separator == input.Length - 1 || input[(separator + 1)..].Contains(':') ||
            !int.TryParse(input[..separator].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var row) ||
            !int.TryParse(input[(separator + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var column) ||
            row < 1 || column < 1 || extent.ExactRowCount is { } rows && row > rows ||
            extent.ExactMaxWidth is { } columns && column > columns) return false;
        row--; column--;
        var columnCount = Math.Min(Math.Clamp(opening.Columns.Count, 1, 64), int.MaxValue - column);
        var rowLimit = Math.Min(Math.Min(Math.Clamp(opening.RowLimit, 1, 256), int.MaxValue - row),
            GridRenderProjection.MaxCells / columnCount);
        request = new NativeGridWindowRequest(opening.Identity, row, new GridRange(column, columnCount), rowLimit);
        return true;
    }

    /// <summary>Prompts only for numeric coordinates; cancelling causes no window request.</summary>
    internal static string? Prompt(string initial)
    {
        var alert = ObjC.New("NSAlert");
        var field = ObjC.Send(ObjC.Send(ObjC.Class("NSTextField"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(0,0,300,24));
        try
        {
            ObjC.Send(alert, ObjC.Sel("setMessageText:"), ObjC.String("Go to CSV row:column"));
            ObjC.Send(alert, ObjC.Sel("setInformativeText:"), ObjC.String(
                "One-based logical record and field, for example 12:3. Unindexed positions remain pending; no complete table is invented."));
            ObjC.Send(field, ObjC.Sel("setStringValue:"), ObjC.String(initial));
            ObjC.Send(alert, ObjC.Sel("setAccessoryView:"), field);
            ObjC.Send(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.String("Go"));
            ObjC.Send(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.String("Cancel"));
            ObjC.Send(ObjC.Send(alert, ObjC.Sel("window")), ObjC.Sel("makeFirstResponder:"), field);
            return ObjC.Send(alert, ObjC.Sel("runModal")) == 1000
                ? ObjC.ManagedString(ObjC.Send(field, ObjC.Sel("stringValue"))) : null;
        }
        finally { ObjC.Send(alert, ObjC.Sel("release")); ObjC.Send(field, ObjC.Sel("release")); }
    }
}
