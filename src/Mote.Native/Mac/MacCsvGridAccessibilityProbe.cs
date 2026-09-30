using System.Runtime.Versioning;
using Mote.Formats;

namespace Mote.Native.Mac;

/// <summary>Target-only native selector regression checks, not cross-process or reader acceptance.</summary>
[SupportedOSPlatform("macos")]
internal static class MacCsvGridAccessibilityProbe
{
    /// <summary>Runs under the existing Mac CSV probe only when experimental Grid AX registration is requested.</summary>
    internal static void Check(MacEditorShell shell)
    {
        if (Environment.GetEnvironmentVariable("MOTE_NATIVE_GRID_ACCESSIBILITY") != "1") return;
        var commands = new List<NativeGridIntent>();
        var composing = true;
        using var grid = new MacCsvGrid(intent => { if (intent.Kind != NativeGridIntentKind.Select) commands.Add(intent); },
            _ => { }, isCompositionActive: () => composing);
        var stamp = new NativeDocumentStamp(51, 1);
        var identity = new NativePresentationId(stamp, 2);
        var projection = CreateProjection();
        var navigation = new NativeGridScrollFrame(new(stamp, 1), identity,
            NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 1003, 3, 1000),
            NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 18, 2, 16),
            new(1000, 3), new(16, 2), 1, false, "Probe exact window");
        grid.Install(projection, identity, navigation);
        CheckInstalled(shell, grid, navigation, () => composing = false, commands);
    }

    /// <summary>Pure constructor-valid synthetic descriptor fixture; does not parse or claim a real source file.</summary>
    internal static GridRenderProjection CreateProjection()
    {
        var rows = new[]
        {
            new GridRow(1000, new(0, 3), new(3, 0), 17,
                [new(16, new(0, 1), new(0, 1), GridValueState.Complete, false),
                 new(17, null, new(1, 0), GridValueState.Missing, false)]),
            new GridRow(1002, new(3, 3), new(6, 0), 18,
                [new(16, new(3, 0), new(1, 0), GridValueState.Complete, false),
                 new(17, new(3, 3), new(1, 0), GridValueState.Oversized, false)])
        };
        return new GridRenderProjection(1, 6, new(1003, 1003, 18), AnalysisCompleteness.Complete,
            [new(0, 6)], 0, new(1000, 3), new(16, 2), "a", rows, [], true, false, false, true);
    }

    private static void CheckInstalled(MacEditorShell shell, MacCsvGrid grid, NativeGridScrollFrame navigation,
        Action endComposition, List<NativeGridIntent> commands)
    {
        var currentId = grid.AccessibilityFrame!.Id;
        Require(grid.Focus(currentId, new(1000, 16)) == GridAccessibilityResult.CompositionBlocked,
            "active physical source composition blocks focus transfer");
        var table = grid.Table;
        Require(ObjC.Send(table, ObjC.Sel("accessibilityRowCount")) == 3, "local row count, not file count");
        Require(ObjC.Send(table, ObjC.Sel("accessibilityColumnCount")) == 2, "local column count");
        var first = Cell(table, 0, 0);
        Require(first != 0 && Label(first).Contains("Row 1001, Column 17", StringComparison.Ordinal), "absolute coordinate label");
        Require(ObjC.SendRange(first, ObjC.Sel("accessibilityRowIndexRange")) == new ObjC.Range(0, 1), "local NSRange row ABI");
        Require(ObjC.SendRange(first, ObjC.Sel("accessibilityColumnIndexRange")) == new ObjC.Range(0, 1), "local NSRange column ABI");
        Require(Cell(table, 2, 0) == 0 && Cell(table, 0, 3) == 0 && Cell(table, -1, 0) == 0, "out-of-window lookup refused");
        Require(Label(Cell(table, 0, 1)).Contains("Row 1002, Column 17", StringComparison.Ordinal) &&
            Label(Cell(table, 0, 1)).Contains("Pending", StringComparison.Ordinal), "sparse row slot not compacted");
        Require(Label(Cell(table, 1, 0)).Contains("Missing", StringComparison.Ordinal), "Missing state");
        Require(Label(Cell(table, 0, 2)).Contains("empty value", StringComparison.Ordinal), "complete empty state");
        Require(ObjC.Send(Cell(table, 1, 2), ObjC.Sel("accessibilityValue")) == 0, "Oversized has no fabricated value");
        var rectangular = Array(first, Cell(table, 1, 0));
        ObjC.Send(table, ObjC.Sel("setAccessibilitySelectedCells:"), rectangular);
        Require(Count(ObjC.Send(table, ObjC.Sel("accessibilitySelectedCells"))) == 2, "rectangle setter publishes selection");
        ObjC.Send(table, ObjC.Sel("setAccessibilitySelectedCells:"), Array(first, Cell(table, 1, 2)));
        Require(Count(ObjC.Send(table, ObjC.Sel("accessibilitySelectedCells"))) == 2, "sparse setter rejects atomically");
        ObjC.Send(table, ObjC.Sel("setAccessibilitySelectedRows:"), Array(ObjC.Send(ObjC.Send(table,
            ObjC.Sel("accessibilityRows")), ObjC.Sel("objectAtIndex:"), 2)));
        Require(Count(ObjC.Send(table, ObjC.Sel("accessibilitySelectedCells"))) == 2, "native row setter cannot bypass selection");
        Require(Count(ObjC.Send(table, ObjC.Sel("accessibilitySelectedRows"))) == 0, "endpoint is not whole-row selection");
        var oversized = ObjC.Send(ObjC.Class("NSMutableArray"), ObjC.Sel("array"));
        for (var i = 0; i <= 8192; i++) ObjC.Send(oversized, ObjC.Sel("addObject:"), first);
        ObjC.Send(table, ObjC.Sel("setAccessibilitySelectedCells:"), oversized);
        Require(Count(ObjC.Send(table, ObjC.Sel("accessibilitySelectedCells"))) == 2, "oversized setter rejected before partial mutation");
        ObjC.Send(table, ObjC.Sel("setAccessibilitySelectedCells:"), 0);
        Require(Count(ObjC.Send(table, ObjC.Sel("accessibilitySelectedCells"))) == 0, "nil setter clears entire selection");
        Require(commands.Count == 0, "AX reads and selection never dispatch source commands");
        var content = ObjC.Send(shell.ProbeWindow, ObjC.Sel("contentView"));
        ObjC.Send(content, ObjC.Sel("addSubview:"), grid.View);
        try
        {
            endComposition();
            ObjC.Send(table, ObjC.Sel("setAccessibilitySelectedCells:"), Array(Cell(table, 0, 2)));
            Require(grid.Focus(currentId, new(1000, 16)) == GridAccessibilityResult.Applied, "admitted cell focus readback");
            Require(ObjC.Send(table, ObjC.Sel("accessibilityFocusedUIElement")) == first, "semantic focused cell");
            Require(grid.AccessibilityFrame!.Selection?.Active == new GridCoordinate(1002, 16), "focus does not replace rectangle");
            MacCsvGridProbe.Key(table, "\r");
            MacCsvGridProbe.Key(table, "\r", 1u << 20);
            Require(commands.Count == 2 && commands[0].Kind == NativeGridIntentKind.Reveal &&
                commands[1].Kind == NativeGridIntentKind.Replace && commands.All(c => c.Row == 1000 && c.Column == 16),
                "Return and Command-Return act on focused cell, not unrelated rectangle endpoint");
            MacCsvGridProbe.Key(table, "\uf703");
            Require(grid.AccessibilityFrame!.Selection?.Active == new GridCoordinate(1000, 17), "arrow originates at focused cell");
            Require(grid.Focus(currentId, null) == GridAccessibilityResult.Applied &&
                ObjC.Send(table, ObjC.Sel("accessibilityFocusedUIElement")) == table, "explicit table focus is not retained selection");
            ObjC.Send(table, ObjC.Sel("setAccessibilitySelectedCells:"), Array(first));
            Require(grid.Focus(currentId, new(1000, 16)) == GridAccessibilityResult.Applied, "refocus first cell");
            ObjC.Send(table, ObjC.Sel("selectRowIndexes:byExtendingSelection:"),
                ObjC.Send(ObjC.Class("NSIndexSet"), ObjC.Sel("indexSetWithIndex:"), 2), (byte)0);
            Require(ObjC.Send(table, ObjC.Sel("accessibilityFocusedUIElement")) == Cell(table, 0, 2),
                "physical native selection clears prior AX focus override");
            ObjC.Send(table, ObjC.Sel("setAccessibilitySelectedCells:"), 0);
        }
        finally { ObjC.Send(grid.View, ObjC.Sel("removeFromSuperview")); }
        ObjC.Send(first, ObjC.Sel("retain"));
        try
        {
            grid.SetNavigation(navigation with { Pending = true, Ready = null, RequestSerial = 2 });
            Require(ObjC.Send(first, ObjC.Sel("accessibilityLabel")) == 0 &&
                ObjC.SendRange(first, ObjC.Sel("accessibilityRowIndexRange")).Length == 0, "retained node unavailable after retirement");
            var current = Cell(table, 0, 0);
            ObjC.Send(table, ObjC.Sel("setAccessibilitySelectedCells:"), Array(current, first));
            Require(Count(ObjC.Send(table, ObjC.Sel("accessibilitySelectedCells"))) == 0, "mixed retired setter cannot mutate");
            Require(Label(current).Contains("Pending", StringComparison.Ordinal), "pending replaces old ready values");
            Require(commands.Count == 2, "pending reads and selection never dispatch source commands");
        }
        finally { ObjC.Send(first, ObjC.Sel("release")); }
        Console.WriteLine("Mac CSV Grid AX selector probe passed; external AX/VoiceOver/geometry gates remain untested.");
    }

    private static nint Cell(nint table, int column, int row) => ObjC.Send(table, ObjC.Sel("accessibilityCellForColumn:row:"), column, row);
    private static string Label(nint element) => ObjC.ManagedString(ObjC.Send(element, ObjC.Sel("accessibilityLabel")));
    private static nint Count(nint array) => array == 0 ? 0 : ObjC.Send(array, ObjC.Sel("count"));
    private static nint Array(params nint[] cells)
    {
        var array = ObjC.Send(ObjC.Class("NSMutableArray"), ObjC.Sel("array"));
        foreach (var cell in cells) ObjC.Send(array, ObjC.Sel("addObject:"), cell);
        return array;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Grid AX probe: " + message);
    }
}
