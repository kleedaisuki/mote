using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Formats;
using Mote.Themes;

namespace Mote.Native.Mac;

/// <summary>Disposable in-process AppKit CSV table acceptance, without external input or clipboard writes.</summary>
/// <remarks>No files, input sources, TCC grants, network, pasteboard or hidden source edits are used.</remarks>
[SupportedOSPlatform("macos")]
internal static class MacCsvGridProbe
{
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SendCell(nint receiver, nint selector, nint column, nint row, byte create);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint KeyEvent(nint receiver, nint selector, nuint type, ObjC.Point location,
        nuint flags, double timestamp, nint window, nint context, nint characters,
        nint ignoringModifiers, byte repeat, ushort keyCode);

    /// <summary>Runs a fresh native shell with ready in-memory certified coordinates.</summary>
    internal static int Run()
    {
        var shell = new MacEditorShell();
        var passed = false;
        shell.Shown += () => shell.Post(() =>
        {
            try { Check(shell); passed = true; }
            catch (Exception error) when (error is not OutOfMemoryException)
            { Console.Error.WriteLine($"Mac CSV Grid check failed: {error.Message}"); }
            finally { shell.Close(); }
        });
        shell.Run();
        return passed ? 0 : 1;
    }

    private static void Check(MacEditorShell shell)
    {
        const string source = "a,b\nc,d\n";
        var stamp = new NativeDocumentStamp(5, 1);
        var rows = new[]
        {
            new GridRow(0, new(0,3),new(3,1),2,
                [new(0,new(0,1),new(0,1),GridValueState.Complete,false),
                 new(1,new(2,1),new(1,1),GridValueState.Complete,false),
                 new(2,null,new(2,0),GridValueState.Missing,false)]),
            new GridRow(1, new(4,3),new(7,1),2,
                [new(0,new(4,1),new(2,1),GridValueState.Complete,false),
                 new(1,new(6,1),new(3,1),GridValueState.Complete,false),
                 new(2,null,new(4,0),GridValueState.Missing,false)])
        };
        var grid = new GridRenderProjection(1, source.Length, new(2,2,2), AnalysisCompleteness.Complete,
            [new(0,source.Length)],0,new(0,64),new(0,3),"abcd",rows,[],false,false,false,false);
        var view = new NativeAnalysisView([],"","","",stamp,PresentationSequence:1,Grid:grid);
        shell.SetTheme(ThemePolicies.Resolve(ThemePolicies.DarkId,true));
        shell.SetDocument(new("Grid probe",source,0,source.Length,false,"",stamp));
        shell.SetAnalysis(view);
        var table = shell.ProbeGrid.Table;
        Require(table != 0 && shell.ProbeGrid.Identity == view.Identity,"real table and exact identity");
        Require(ObjC.Send(table,ObjC.Sel("numberOfRows")) == 2,"bounded native row count");
        Require(ObjC.Send(table,ObjC.Sel("numberOfColumns")) == 3,"actual columns including Missing delivery");
        Require(ObjC.ManagedString(ObjC.Send(table,ObjC.Sel("accessibilityLabel"))) ==
            (Environment.GetEnvironmentVariable("MOTE_NATIVE_GRID_ACCESSIBILITY") == "1" ? "CSV grid window" : "Mote CSV grid"),"native table AX label");
        var cell = SendCell(table,ObjC.Sel("viewAtColumn:row:makeIfNecessary:"),1,0,1);
        Require(cell != 0 && ObjC.ManagedString(ObjC.Send(cell,ObjC.Sel("stringValue"))) == "b","ready cell readback");
        Require(ObjC.Send(cell,ObjC.Sel("isEditable")) == 0,"read-only native field");
        Require(ObjC.ManagedString(ObjC.Send(cell,ObjC.Sel("accessibilityLabel"))).Contains("Row 1, Column 2, Complete",StringComparison.Ordinal),"actual logical cell AX coordinates");
        var missing = SendCell(table,ObjC.Sel("viewAtColumn:row:makeIfNecessary:"),2,1,1);
        Require(ObjC.ManagedString(ObjC.Send(missing,ObjC.Sel("stringValue"))) == "[missing]","missing distinct from empty");
        var intents = new List<NativeGridIntent>();
        var windows = new List<NativeGridWindowRequest>();
        var sourceCuts = 0; var sourceSelectAll = 0;
        shell.GridIntentRequested += intents.Add;
        shell.GridWindowRequested += windows.Add;
        shell.CutRequested += () => sourceCuts++;
        shell.SelectAllRequested += () => sourceSelectAll++;
        ObjC.Send(shell.ProbeWindow,ObjC.Sel("makeFirstResponder:"),table);
        Key(table,"\uf703");
        Key(table,"\r");
        Require(intents.Last().Kind == NativeGridIntentKind.Reveal && intents.Last().Column == 1 && intents.Last().Row == 0,"Return actual selected column");
        Key(table,"c",1u<<20);
        Require(intents.Last().Kind == NativeGridIntentKind.CopyValue && intents.Last().Identity == view.Identity,"Copy carries identity, not display value");
        Key(table,"\r",1u<<20);
        Require(intents.Last().Kind == NativeGridIntentKind.Replace,"explicit Replace intent only");
        Key(table,"\uf703",1u<<17);
        Key(table,"c",1u<<20);
        Require(intents.Last().Kind == NativeGridIntentKind.CopyTsv && intents.Last().EndColumn == 2,"Shift rectangle quoted TSV intent");
        shell.SetAnalysis(view with { PresentationSequence = 2 });
        Key(table,"c",1u<<20);
        Require(intents.Last().Identity.Sequence == 2 && intents.Last().Column == 1 && intents.Last().EndColumn == 2,"same-document selection survives new identity");
        // Run before the nested AX probe: its responder changes cannot explain this native admission regression.
        if (Environment.GetEnvironmentVariable("MOTE_NATIVE_GRID_ACCESSIBILITY") == "1")
            Require(shell.ProbeGridAccessibilityFrame is { FocusedCell: { Row: 0, Column: 2 } } activeFrame &&
                activeFrame.Cell(new(0, 2)).State == GridValueState.Missing,
                "native Missing active field and semantic focus agree before menu opening");
        var missingBefore = intents.Count;
        Key(table,"\r");
        Key(table,"\r",1u<<20);
        Require(intents.Count == missingBefore + 2 && intents[^2].Kind == NativeGridIntentKind.Reveal &&
            intents[^1].Kind == NativeGridIntentKind.Replace &&
            intents.TakeLast(2).All(i => i.Identity.Sequence == 2 && i.Row == 0 && i.Column == 2 && i.EndColumn is null),
            "Missing native keyboard coordinate intents retain legacy controller admission");
        // Freeze menu opening, then install new same-version map before its action.
        var menu = ObjC.Send(table,ObjC.Sel("menu"));
        var menuDelegate = ObjC.Send(menu,ObjC.Sel("delegate"));
        ObjC.Send(menuDelegate,ObjC.Sel("menuWillOpen:"),menu);
        Require(shell.ProbeGridMenuCell is { } frozenCell && frozenCell.Identity.Sequence == 2 &&
            frozenCell.Kind == NativeGridIntentKind.Reveal && frozenCell.Row == 0 && frozenCell.Column == 2 &&
            frozenCell.EndColumn is null,"Missing menu cell freezes sequence 2 active field before replacement");
        shell.SetAnalysis(view with { PresentationSequence = 3 });
        var beforeMenu = intents.Count;
        var revealItem = ObjC.Send(menu,ObjC.Sel("itemAtIndex:"),0);
        ObjC.Send(ObjC.Send(revealItem,ObjC.Sel("target")),ObjC.Send(revealItem,ObjC.Sel("action")),revealItem);
        Require(intents.Count == beforeMenu + 1 && intents.Last().Identity.Sequence == 2 && intents.Last().Kind == NativeGridIntentKind.Reveal &&
            intents.Last().Column == 2 && intents.Last().EndColumn is null,"menu freezes old identity and active field");
        var csvItem = ObjC.Send(menu,ObjC.Sel("itemAtIndex:"),2);
        ObjC.Send(ObjC.Send(csvItem,ObjC.Sel("target")),ObjC.Send(csvItem,ObjC.Sel("action")),csvItem);
        Require(intents.Last().Identity.Sequence == 2 && intents.Last().Column == 1 && intents.Last().EndColumn == 2,
            "menu rectangle does not rebound to current identity");
        var followItem = ObjC.Send(menu,ObjC.Sel("itemAtIndex:"),8);
        ObjC.Send(ObjC.Send(followItem,ObjC.Sel("target")),ObjC.Send(followItem,ObjC.Sel("action")),followItem);
        Require(windows.Count == 1 && windows[0].FollowSource && windows[0].Identity.Sequence == 2,
            "Follow source retains frozen menu identity");
        MacCsvGridAccessibilityProbe.Check(shell);
        ObjC.Send(shell.ProbeWindow,ObjC.Sel("makeFirstResponder:"),table);
        windows.Clear();
        var opening = new NativeGridWindowRequest(view.Identity,0,new(0,3));
        Require(MacGridNavigation.TryCreateRequest("2:2",opening,grid.Extent,out var go) &&
            go.Identity == view.Identity && go.Row == 1 && go.Columns.Start == 1 && go.RowLimit == 64,
            "numeric navigation is bounded and identity-preserving");
        foreach (var invalid in new[] { "0:1", "1:0", "3:1", "1:3", "1:1:1", "2147483648:1", "-1:1" })
            Require(!MacGridNavigation.TryCreateRequest(invalid,opening,grid.Extent,out _),"invalid navigation coordinates refused");
        Require(MacGridNavigation.TryCreateRequest("2147483647:2147483647",opening,new(0,null,null),out var far) &&
            far.RowLimit == 1 && far.Columns.Count == 1,"unknown distant navigation has checked coordinate bounds");
        Key(table,"x",1u<<20); Key(table,"v",1u<<20);
        ObjC.Send(table,ObjC.Sel("pasteAsPlainText:"),0); ObjC.Send(table,ObjC.Sel("cut:"),0);
        var applicationDelegate = ObjC.Send(shell.ProbeWindow,ObjC.Sel("delegate"));
        ObjC.Send(applicationDelegate,ObjC.Sel("moteCut:"),0);
        ObjC.Send(applicationDelegate,ObjC.Sel("moteSelectAll:"),0);
        ObjC.Send(applicationDelegate,ObjC.Sel("moteCopy:"),0);
        Require(intents.Last().Kind == NativeGridIntentKind.CopyTsv && intents.Last().Column == 0 &&
            intents.Last().EndColumn == 2,"Edit Select All and Copy route bounded Grid selection");
        Require(shell.ProbeNativeText == source && sourceCuts == 0 && sourceSelectAll == 0,
            "read-only Cut/Paste and Edit commands leave hidden source unchanged");
        Key(table,"\uf702",1u<<19);
        Require(windows.Count == 0,"left edge has no negative coordinate request");
        // With only 2 proved rows, PageDown cannot request an invented row beyond EOF.
        Key(table,"\uf72d");
        Require(windows.Count == 0,"exact EOF prevents phantom rows");
        var editor = MacGridReplacement.CreateEditor("CR\rLF\nCRLF\r\n😀");
        try { Require(ObjC.ManagedString(ObjC.Send(editor,ObjC.Sel("string"))) == "CR\rLF\nCRLF\r\n😀","temporary decoded editor exact line-ending roundtrip"); }
        finally { ObjC.Send(editor,ObjC.Sel("release")); }
        shell.SetDocument(new("Next source",source,0,source.Length,false,"",new(5,2)));
        var before = intents.Count;
        Key(table,"\r");
        Require(shell.ProbeGrid.Identity is null && intents.Count == before,"source edit invalidates table commands immediately");
        shell.SetAnalysis(new([],"","Flow","",new(5,2),PresentationSequence:3));
        Require(ObjC.ManagedString(ObjC.Send(shell.ProbePreviewView,ObjC.Sel("string"))) == "Flow","Flow control survives Grid switch");
        Require(shell.ProbeNativeText == source,"table never edits source");
    }

    /// <summary>Dispatches a synthetic native key to the probe table without posting global input.</summary>
    internal static void Key(nint table, string characters, nuint flags = 0)
    {
        var evt = KeyEvent(ObjC.Class("NSEvent"),ObjC.Sel("keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:characters:charactersIgnoringModifiers:isARepeat:keyCode:"),
            10,new(0,0),flags,0,0,0,ObjC.String(characters),ObjC.String(characters),0,0);
        if (evt == 0) throw new InvalidOperationException("Could not create native key event.");
        ObjC.Send(table,ObjC.Sel("keyDown:"),evt);
    }

    private static void Require(bool condition, string contract)
    { if (!condition) throw new InvalidOperationException(contract); }
}
