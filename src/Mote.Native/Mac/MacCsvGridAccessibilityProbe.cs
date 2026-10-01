using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using Mote.Formats;
using Mote.Native.Mac.Canvas;

namespace Mote.Native.Mac;

/// <summary>Target-only native selector regression checks, not cross-process or reader acceptance.</summary>
[SupportedOSPlatform("macos")]
internal static class MacCsvGridAccessibilityProbe
{
    /// <summary>Calls the object-return point ABI directly; this is not a CGRect aggregate return.</summary>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint HitTest(nint receiver, nint selector, ObjC.Point point);
    /// <summary>Direct BOOL return for the owned semantic menu action; no global event is posted.</summary>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern byte MenuAction(nint receiver, nint selector);
    /// <summary>Schedules cancellation in native menu-tracking mode as well as the ordinary main run loop.</summary>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ScheduleInModes(nint receiver, nint selector, nint action, nint value, double delay, nint modes);
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
        CheckDetachedRoot();
        Console.WriteLine("Mac CSV Grid AX selector probe passed; external AX/VoiceOver/geometry gates remain untested.");
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
        var table = grid.AccessibilityTable;
        var physicalTable = grid.Table;
        Require(table != 0 && table != physicalTable &&
            ObjC.Send(physicalTable, ObjC.Sel("isAccessibilityElement")) == 0 &&
            Count(ObjC.Send(physicalTable, ObjC.Sel("accessibilityChildren"))) == 0,
            "one distinct semantic Table and no promoted native implementation rows");
        var groupChildren = ObjC.Send(grid.View, ObjC.Sel("accessibilityChildren"));
        Require(Count(groupChildren) == 4 && ObjC.Send(groupChildren, ObjC.Sel("objectAtIndex:"), 0) == table,
            "group exposes semantic Table and real navigation/detail, not native scroll subtree");
        Require(ObjC.Send(table, ObjC.Sel("accessibilityParent")) == grid.View,
            "stable Table parents to the existing Grid group");
        CheckShownMenu(table, physicalTable);
        Require(ObjC.Send(table, ObjC.Sel("accessibilityRowCount")) == 3, "local row count, not file count");
        Require(ObjC.Send(table, ObjC.Sel("accessibilityColumnCount")) == 2, "local column count");
        var rows = ObjC.Send(table, ObjC.Sel("accessibilityRows"));
        var firstRow = ObjC.Send(rows, ObjC.Sel("objectAtIndex:"), 0);
        Require(Label(firstRow) == "Row 1001" &&
            ObjC.ManagedString(ObjC.Send(firstRow, ObjC.Sel("accessibilityIdentifier"))) ==
                $"mote.csv.window.{currentId.WindowSerial}.row.0.-1", "proxy row keeps exact absolute ordinal and window identity");
        Require(ObjC.Send(firstRow, ObjC.Sel("accessibilityParent")) == table, "row parents to stable semantic Table");
        Require(Task.Run(() => ObjC.Send(table, ObjC.Sel("accessibilityRows")))
            .GetAwaiter().GetResult() == 0, "semantic Table refuses off-main dispatch without reading owner state");
        var first = Cell(table, 0, 0);
        Require(first != 0 && Label(first).Contains("Row 1001, Column 17", StringComparison.Ordinal), "absolute coordinate label");
        Require(ObjC.Send(first, ObjC.Sel("accessibilityParent")) == firstRow, "cell parents to the exact semantic row");
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
            var cellBounds = MacOnScreenCanvasNative.GetRect(first, ObjC.Sel("accessibilityFrame"));
            Require(cellBounds.Size.Width > 0 && cellBounds.Size.Height > 0, "first admitted cell has actual clipped native bounds");
            var point = new ObjC.Point(cellBounds.Origin.X + cellBounds.Size.Width / 2,
                cellBounds.Origin.Y + cellBounds.Size.Height / 2);
            Require(HitTest(table, ObjC.Sel("accessibilityHitTest:"), point) == first &&
                HitTest(physicalTable, ObjC.Sel("accessibilityHitTest:"), point) == first &&
                HitTest(grid.View, ObjC.Sel("accessibilityHitTest:"), point) == first,
                "proxy, physical and group hit paths resolve the same semantic cell, not native rows");
            ObjC.Send(table, ObjC.Sel("setAccessibilitySelectedCells:"), Array(Cell(table, 0, 2)));
            Require(grid.Focus(currentId, new(1000, 16)) == GridAccessibilityResult.Applied, "admitted cell focus readback");
            Require(ObjC.Send(table, ObjC.Sel("accessibilityFocusedUIElement")) == first, "semantic focused cell");
            Require(ObjC.Send(shell.ProbeWindow, ObjC.Sel("firstResponder")) == physicalTable &&
                ObjC.Send(physicalTable, ObjC.Sel("accessibilityFocusedUIElement")) == first,
                "physical first responder projects current semantic cell without replacement input");
            Require(grid.AccessibilityFrame!.Selection?.Active == new GridCoordinate(1002, 16), "focus does not replace rectangle");
            MacCsvGridProbe.Key(physicalTable, "\r");
            MacCsvGridProbe.Key(physicalTable, "\r", 1u << 20);
            Require(commands.Count == 2 && commands[0].Kind == NativeGridIntentKind.Reveal &&
                commands[1].Kind == NativeGridIntentKind.Replace && commands.All(c => c.Row == 1000 && c.Column == 16),
                "Return and Command-Return act on focused cell, not unrelated rectangle endpoint");
            MacCsvGridProbe.Key(physicalTable, "\uf703");
            Require(grid.AccessibilityFrame!.Selection?.Active == new GridCoordinate(1000, 17), "arrow originates at focused cell");
            Require(grid.Focus(currentId, null) == GridAccessibilityResult.Applied &&
                ObjC.Send(table, ObjC.Sel("accessibilityFocusedUIElement")) == table, "explicit table focus is not retained selection");
            ObjC.Send(table, ObjC.Sel("setAccessibilitySelectedCells:"), Array(first));
            Require(grid.Focus(currentId, new(1000, 16)) == GridAccessibilityResult.Applied, "refocus first cell");
            ObjC.Send(physicalTable, ObjC.Sel("selectRowIndexes:byExtendingSelection:"),
                ObjC.Send(ObjC.Class("NSIndexSet"), ObjC.Sel("indexSetWithIndex:"), 2), (byte)0);
            Require(ObjC.Send(table, ObjC.Sel("accessibilityFocusedUIElement")) == Cell(table, 0, 2),
                "physical native selection clears prior AX focus override");
            ObjC.Send(table, ObjC.Sel("setAccessibilitySelectedCells:"), 0);
            CheckMenuPopup(shell, grid, table, physicalTable, first);
        }
        finally { ObjC.Send(grid.View, ObjC.Sel("removeFromSuperview")); }
        ObjC.Send(first, ObjC.Sel("retain"));
        try
        {
            grid.SetNavigation(navigation with { Pending = true, Ready = null, RequestSerial = 2 });
            Require(grid.AccessibilityTable == table, "stable Table survives child epoch replacement");
            Require(ObjC.Send(first, ObjC.Sel("accessibilityLabel")) == 0 &&
                ObjC.SendRange(first, ObjC.Sel("accessibilityRowIndexRange")).Length == 0, "retained node unavailable after retirement");
            var current = Cell(table, 0, 0);
            ObjC.Send(table, ObjC.Sel("setAccessibilitySelectedCells:"), Array(current, first));
            Require(Count(ObjC.Send(table, ObjC.Sel("accessibilitySelectedCells"))) == 0, "mixed retired setter cannot mutate");
            Require(Label(current).Contains("Pending", StringComparison.Ordinal), "pending replaces old ready values");
            Require(commands.Count == 2, "pending reads and selection never dispatch source commands");
        }
        finally { ObjC.Send(first, ObjC.Sel("release")); }
    }

    /// <summary>Exercises an actual owned popup and its delegate callbacks, then cancels without choosing a command.</summary>
    private static void CheckMenuPopup(MacEditorShell shell, MacCsvGrid grid, nint table, nint physicalTable, nint first)
    {
        ObjC.Send(table, ObjC.Sel("setAccessibilitySelectedCells:"), Array(first));
        var before = grid.ProbeMenuTransitions;
        var responder = ObjC.Send(shell.ProbeWindow, ObjC.Sel("firstResponder"));
        var menu = ObjC.Send(physicalTable, ObjC.Sel("menu"));
        var modes = ObjC.Send(ObjC.Class("NSMutableArray"), ObjC.Sel("array"));
        ObjC.Send(modes, ObjC.Sel("addObject:"), ObjC.String("NSDefaultRunLoopMode"));
        ObjC.Send(modes, ObjC.Sel("addObject:"), ObjC.String("NSEventTrackingRunLoopMode"));
        ScheduleInModes(menu, ObjC.Sel("performSelector:withObject:afterDelay:inModes:"),
            ObjC.Sel("cancelTracking"), 0, 0.25, modes);
        try
        {
            Require(MenuAction(table, ObjC.Sel("accessibilityPerformShowMenu")) != 0,
                "owned semantic menu request is admitted for next-turn native presentation");
            var until = ObjC.Send(ObjC.Class("NSDate"), ObjC.Sel("dateWithTimeIntervalSinceNow:"), 0.5);
            ObjC.Send(ObjC.Send(ObjC.Class("NSRunLoop"), ObjC.Sel("currentRunLoop")), ObjC.Sel("runUntilDate:"), until);
            Require(grid.ProbeMenuTransitions == (before.Opens + 1, before.Closes + 1),
                "actual owned popup produces one native will-open and did-close callback");
            Require(grid.ProbeMenuCell is { Row: 1000, Column: 16 } captured && captured.Identity == grid.Identity,
                "actual popup freezes the existing installed field identity without choosing a source command");
            Require(ObjC.Send(table, ObjC.Sel("accessibilityShownMenu")) == 0 &&
                ObjC.Send(shell.ProbeWindow, ObjC.Sel("firstResponder")) == responder,
                "cancelled owned popup clears its relation and preserves physical responder");
        }
        finally
        {
            ObjC.Send(ObjC.Class("NSObject"), ObjC.Sel("cancelPreviousPerformRequestsWithTarget:selector:object:"),
                menu, ObjC.Sel("cancelTracking"), 0);
            ObjC.Send(menu, ObjC.Sel("cancelTracking"));
            ObjC.Send(table, ObjC.Sel("setAccessibilitySelectedCells:"), 0);
        }
    }

    /// <summary>Retaining a native root after disposal must not preserve a managed owner, parent or bounded children.</summary>
    private static void CheckDetachedRoot()
    {
        var retiring = new MacCsvGrid(_ => { }, _ => { });
        var root = retiring.AccessibilityTable;
        var menu = ObjC.Send(retiring.Table, ObjC.Sel("menu"));
        ObjC.Send(root, ObjC.Sel("retain"));
        ObjC.Send(menu, ObjC.Sel("retain"));
        try
        {
            retiring.Dispose();
            Require(ObjC.Send(root, ObjC.Sel("isAccessibilityElement")) == 0 &&
                ObjC.Send(root, ObjC.Sel("accessibilityRole")) == 0 &&
                ObjC.Send(root, ObjC.Sel("accessibilityParent")) == 0 &&
                ObjC.Send(root, ObjC.Sel("accessibilityRows")) == 0 &&
                ObjC.Send(root, ObjC.Sel("accessibilityShownMenu")) == 0,
                "retained detached proxy has no owner, parent, role, rows or transient menu");
            var item = ObjC.Send(menu, ObjC.Sel("itemAtIndex:"), 9);
            Require(ObjC.Send(menu, ObjC.Sel("delegate")) == 0 &&
                ObjC.Send(item, ObjC.Sel("target")) == 0 && ObjC.Send(item, ObjC.Sel("action")) == 0,
                "retained detached menu has no callback or coordinate-command target");
        }
        finally { ObjC.Send(menu, ObjC.Sel("release")); ObjC.Send(root, ObjC.Sel("release")); }
    }

    /// <summary>Tests a native relation marker without displaying a menu or granting source actions.</summary>
    private static void CheckShownMenu(nint table, nint physicalTable)
    {
        var menu = ObjC.New("NSMenu");
        var installed = ObjC.Send(physicalTable, ObjC.Sel("menu"));
        var original = ObjC.Send(physicalTable, ObjC.Sel("accessibilityShownMenu"));
        ObjC.Send(original, ObjC.Sel("retain"));
        try
        {
            ObjC.Send(physicalTable, ObjC.Sel("setAccessibilityShownMenu:"), menu);
            Require(ObjC.Send(physicalTable, ObjC.Sel("accessibilityShownMenu")) == menu &&
                ObjC.Send(table, ObjC.Sel("accessibilityShownMenu")) == menu,
                "proxy preserves exact physical shown-menu relation without independent presentation");
            Require(Task.Run(() => ObjC.Send(table, ObjC.Sel("accessibilityShownMenu")))
                .GetAwaiter().GetResult() == 0, "shown-menu relation refuses off-main owner access");
            ObjC.Send(physicalTable, ObjC.Sel("setAccessibilityShownMenu:"), 0);
            Require(ObjC.Send(table, ObjC.Sel("accessibilityShownMenu")) == 0 &&
                ObjC.Send(physicalTable, ObjC.Sel("menu")) == installed,
                "proxy does not advertise the installed context menu when no menu is shown");
        }
        finally
        {
            ObjC.Send(physicalTable, ObjC.Sel("setAccessibilityShownMenu:"), original);
            ObjC.Send(original, ObjC.Sel("release"));
            ObjC.Send(menu, ObjC.Sel("release"));
        }
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
