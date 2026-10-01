using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Formats;
using Mote.Native.Mac.Canvas;

namespace Mote.Native.Mac;

/// <summary>Opt-in hidden AppKit logical-scroller acceptance using process-owned controls only.</summary>
/// <remarks>No window, desktop events, files, settings, clipboard, network or input-source changes.</remarks>
[SupportedOSPlatform("macos")]
internal static unsafe class MacGridScrollerProbe
{
    /// <summary>Injected native part belongs only to this probe's two temporary scroller instances.</summary>
    private static nint _part;

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "object_setClass")]
    private static extern nint SetClass(nint instance, nint cls);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint Cell(nint table, nint selector, nint column, nint row, byte create);

    /// <summary>Runs synchronously on the entry-point thread; no tracking loop can block termination.</summary>
    internal static int Run()
    {
        ObjC.ApplicationLoad();
        var pool = ObjC.New("NSAutoreleasePool");
        try
        {
            ObjC.Send(ObjC.Class("NSApplication"), ObjC.Sel("sharedApplication"));
            Check();
            Console.WriteLine("mote-native-mac-grid-scroller-ready; native-action=synthetic; physical-input=not-tested; controller-stale-token=not-tested");
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine("mote-native-mac-grid-scroller-failed; contract=acceptance");
            return 1;
        }
        finally { ObjC.Send(pool, ObjC.Sel("drain")); }
    }

    /// <summary>Reads native state and drives production target/action without entering mouseDown tracking.</summary>
    private static void Check()
    {
        var commands = new List<NativeGridIntent>();
        var actions = new List<NativeGridGestureAction>();
        var begins = new List<NativeGridGestureBegin>();
        long sequence = 0;
        using var grid = new MacCsvGrid(commands.Add, _ => throw new InvalidOperationException("Unexpected legacy navigation."),
            begin => { begins.Add(begin); return new(new(begin.Frame.Navigation, ++sequence), begin.Frame); }, actions.Add);
        var controls = ObjC.Send(grid.View, ObjC.Sel("subviews"));
        var scrollers = new List<nint>();
        nint scroll = 0;
        for (nint i = 0; i < ObjC.Send(controls, ObjC.Sel("count")); i++)
        {
            var control = ObjC.Send(controls, ObjC.Sel("objectAtIndex:"), i);
            if (ObjC.Send(control, ObjC.Sel("isKindOfClass:"), ObjC.Class("NSScroller")) != 0) scrollers.Add(control);
            if (ObjC.Send(control, ObjC.Sel("isKindOfClass:"), ObjC.Class("NSScrollView")) != 0) scroll = control;
        }
        Require(scrollers.Count == 2 && scroll != 0, "two owned scrollers beside real scroll view");
        Require(ObjC.Send(scroll, ObjC.Sel("hasVerticalScroller")) == 0 && ObjC.Send(scroll, ObjC.Sel("hasHorizontalScroller")) == 0,
            "competing cache scrollers disabled");
        var vertical = scrollers.Single(s => MacOnScreenCanvasNative.GetRect(s, ObjC.Sel("frame")).Size.Height >
            MacOnScreenCanvasNative.GetRect(s, ObjC.Sel("frame")).Size.Width);
        var horizontal = scrollers.Single(s => s != vertical);
        foreach (var control in scrollers)
        {
            var rect = MacOnScreenCanvasNative.GetRect(control, ObjC.Sel("frame"));
            Require(double.IsFinite(rect.Size.Width) && double.IsFinite(rect.Size.Height) && rect.Size.Width > 0 && rect.Size.Height > 0,
                "finite nonempty native hit-area geometry");
        }
        var document = new NativeDocumentStamp(11, 1);
        var frame = new NativeGridScrollFrame(new(document, 1), null,
            NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 200_000, 16, 70_000),
            NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 90_000, 2, 80_000),
            new(70_000, 3), new(80_000, 2), 1, true, "Probe pending");
        grid.SetNavigation(frame);
        CheckAxis(vertical, frame.Rows, "File rows");
        CheckAxis(horizontal, frame.Columns, "File columns");
        Require(ObjC.Send(grid.Table, ObjC.Sel("numberOfRows")) == 3 && ObjC.Send(grid.Table, ObjC.Sel("numberOfColumns")) == 2,
            "distant domain retains only bounded pending slots");
        Require(Text(grid.Table, 0, 1) == "[pending]", "pending native cell readback");
        grid.Emit(NativeGridIntentKind.CopyValue); grid.Emit(NativeGridIntentKind.Reveal); grid.Emit(NativeGridIntentKind.Replace);
        Require(commands.Count == 0 && grid.Identity is null, "pending cannot authorize source commands");
        var cls = ObjC.AllocateClassPair(ObjC.Class("MoteCsvGridScroller"), "MoteGridProbePartScroller", 0);
        Require(cls != 0 && ObjC.AddMethod(cls, ObjC.Sel("hitPart"), (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint>)&HitPart, "q@:"),
            "probe-only native part subclass");
        ObjC.RegisterClassPair(cls);
        var oldClasses = new nint[scrollers.Count];
        try
        {
            for (var i = 0; i < scrollers.Count; i++) oldClasses[i] = SetClass(scrollers[i], cls);
            foreach (var part in new[] { 1, 3, 4, 5, 2, 6 })
            {
                _part = part;
                ObjC.Send(vertical, ObjC.Sel("setDoubleValue:"), 1d);
                var before = actions.Count;
                Fire(vertical);
                var expected = MacGridScrollInterop.Target(part, frame.Rows, frame.Rows.First, 1d);
                Require(actions.Count == before + 1 && actions[^1].Row == expected && actions[^1].Column == frame.Columns.First &&
                    actions[^1].Phase == NativeGridGesturePhase.Commit && actions[^1].Id.Navigation == frame.Navigation,
                    "real row target/action retains admitted navigation and distant ordinals");
            }
            _part = 2; ObjC.Send(horizontal, ObjC.Sel("setDoubleValue:"), 0d); Fire(horizontal);
            Require(actions[^1].Column == 0 && actions[^1].Row == frame.Rows.First, "horizontal exact endpoint mapping");
            Require(begins.All(begin => begin.VisibleRows is >= 1 and <= 256 && begin.VisibleColumns is >= 1 and <= 64),
                "native measured geometry bounded by delivery caps");
            Require(actions.Select(action => action.Id.Sequence).Distinct().Count() == actions.Count,
                "independent one-shot actions use captured unique admission tokens");
            var prefix = frame with { Rows = NativeGridScrollAxis.Create(NativeGridExtentKind.Prefix, 80_000, 16, 70_000), RequestSerial = 2 };
            grid.SetNavigation(prefix); CheckAxis(vertical, prefix.Rows, "Indexed rows; file total unknown");
            var beforeRetire = actions.Count;
            grid.SetNavigation(null); Fire(vertical);
            Require(actions.Count == beforeRetire, "delayed action after navigation retirement cannot acquire authority");
        }
        finally
        {
            for (var i = 0; i < scrollers.Count; i++)
                if (oldClasses[i] != 0) SetClass(scrollers[i], oldClasses[i]);
        }
        CheckSparse(grid, document, commands);
    }

    /// <summary>Exercises actual table callbacks over a gap, not a compacted fabricated row.</summary>
    private static void CheckSparse(MacCsvGrid grid, NativeDocumentStamp document, List<NativeGridIntent> commands)
    {
        var row = new GridRow(2, new(2, 1), new(3, 0), 1,
            [new(0, new(2, 1), new(0, 1), GridValueState.Complete, false)]);
        var projection = new GridRenderProjection(1, 3, new(3, 3, 1), AnalysisCompleteness.Complete,
            [new(0, 3)], 0, new(0, 3), new(0, 1), "c", [row], [], false, false, false, false);
        grid.Install(projection, new(document, 1));
        Require(ObjC.Send(grid.Table, ObjC.Sel("numberOfRows")) == 3 && Text(grid.Table, 0, 0) == "[pending]" &&
            Text(grid.Table, 0, 1) == "[pending]" && Text(grid.Table, 0, 2) == "c", "sparse descriptor preserves exact ordinal slot");
        var indexes = ObjC.Send(ObjC.Class("NSIndexSet"), ObjC.Sel("indexSetWithIndex:"), 1);
        ObjC.Send(grid.Table, ObjC.Sel("selectRowIndexes:byExtendingSelection:"), indexes, (byte)0);
        var before = commands.Count;
        grid.Emit(NativeGridIntentKind.CopyValue); grid.Emit(NativeGridIntentKind.Reveal); grid.Emit(NativeGridIntentKind.Replace);
        Require(commands.Count == before, "selected ordinal gap has no source command");
    }

    /// <summary>Checks CGFloat/double native getter ABI and installed domain labels.</summary>
    private static void CheckAxis(nint control, NativeGridScrollAxis axis, string label)
    {
        Require(Math.Abs(MacOnScreenCanvasNative.SendDouble(control, ObjC.Sel("doubleValue")) - axis.Position) < 1e-9,
            "native normalized position readback");
        Require(Math.Abs(MacOnScreenCanvasNative.SendDouble(control, ObjC.Sel("knobProportion")) - axis.Proportion) < 1e-9,
            "native logical knob proportion readback");
        Require(ObjC.ManagedString(ObjC.Send(control, ObjC.Sel("accessibilityLabel"))) == label,
            "exact/prefix native label distinguishes knowledge domain");
    }

    private static string Text(nint table, int column, int row) => ObjC.ManagedString(ObjC.Send(
        Cell(table, ObjC.Sel("viewAtColumn:row:makeIfNecessary:"), column, row, 1), ObjC.Sel("stringValue")));

    /// <summary>Invokes the actual installed delegate selector, without NSApplication event injection.</summary>
    private static void Fire(nint control) => ObjC.Send(ObjC.Send(control, ObjC.Sel("target")),
        ObjC.Send(control, ObjC.Sel("action")), control);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint HitPart(nint self, nint selector) => _part;

    private static void Require(bool condition, string contract)
    { if (!condition) throw new InvalidOperationException(contract); }
}
