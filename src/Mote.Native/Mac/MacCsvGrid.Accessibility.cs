using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mote.Formats;
using Mote.Native.Mac.Canvas;

namespace Mote.Native.Mac;

/// <summary>Native-first bounded table selectors; no source actions or editable values.</summary>
/// <remarks>Opt-in until cross-process AX, both native ABIs and VoiceOver pass their gates.</remarks>
internal sealed unsafe partial class MacCsvGrid
{
    /// <summary>Experimental registration is separate from the established source provider.</summary>
    private static readonly bool AccessibilityEnabled =
        Environment.GetEnvironmentVariable("MOTE_NATIVE_GRID_ACCESSIBILITY") == "1";
    /// <summary>One statically registered Objective-C metadata class for all bounded windows.</summary>
    private const string AccessibilityNodeClass = "MoteCsvGridAccessibilityNode";
    /// <summary>Only current nodes have managed entries; retirement cannot retain a projection.</summary>
    private static readonly Dictionary<nint, AccessibilityNode> AccessibilityNodes = [];
    /// <summary>Each handle owns one native retain; retirement drops all managed lookups before releasing it.</summary>
    private readonly List<nint> _accessibilityOwned = [];
    /// <summary>Published only after all bounded metadata arrays are coherent.</summary>
    private GridAccessibilityFrame? _accessibilityFrame;
    /// <summary>Never-reused provider epoch, independent from native row slots.</summary>
    private long _accessibilitySerial;
    /// <summary>Semantic keyboard focus is independent from rectangle selection and native row highlight.</summary>
    private GridCoordinate? _accessibilityFocusCell;
    /// <summary>Distinguishes explicit table focus from the normal active-cell fallback.</summary>
    private bool _accessibilityTableFocus;
    /// <summary>Current local axis metadata, independent from complete-file dimensions.</summary>
    private nint[] _accessibilityRows = [], _accessibilityColumns = [];
    /// <summary>Ordinal labels, not guessed CSV schema names or additional data columns.</summary>
    private nint[] _accessibilityRowHeaders = [], _accessibilityColumnHeaders = [];
    /// <summary>Bounded lazy cache; zero handles mean not requested, never missing CSV values.</summary>
    private nint[,] _accessibilityCells = new nint[0, 0];
    /// <summary>Target-probe readback of the installed immutable frame; never source command authority.</summary>
    internal GridAccessibilityFrame? AccessibilityFrame => _accessibilityFrame;

    /// <summary>An exactly empty file has no CSV columns; unknown/pending extent never implies exact empty.</summary>
    private static GridRange AccessibilityColumns(NativeGridScrollFrame? navigation, GridRenderProjection? projection, GridRange requested) =>
        AccessibilityEnabled && (navigation is { Rows.Kind: NativeGridExtentKind.Exact, Rows.Count: 0 } ||
            navigation is null && projection?.Extent.ExactRowCount == 0) ? new(0, 0) : requested;

    /// <summary>Node coordinates are local and immutable for the entire native object's lifetime.</summary>
    private sealed record AccessibilityNode(MacCsvGrid Owner, GridAccessibilityId Id,
        string Kind, int Row, int Column);

    /// <summary>Architecture-specific aggregate returns must not use pointer-return objc_msgSend.</summary>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern ObjC.Rect AccessibilityRectDirect(nint receiver, nint selector, nint a, nint b);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend_stret")]
    private static extern void AccessibilityRectStret(out ObjC.Rect rect, nint receiver, nint selector, nint a, nint b);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern ObjC.Rect AccessibilityConvertDirect(nint receiver, nint selector, ObjC.Rect rect, nint view);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend_stret")]
    private static extern void AccessibilityConvertStret(out ObjC.Rect result, nint receiver, nint selector, ObjC.Rect rect, nint view);
    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static extern void NSAccessibilityPostNotification(nint element, nint notification);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSendSuper")]
    private static extern byte AccessibilitySuperResponder(ref MacOnScreenCanvasNative.Super receiver, nint selector);

    /// <summary>Publishes the Grid group without touching source hierarchy or input views.</summary>
    private void InitializeAccessibility()
    {
        if (!AccessibilityEnabled) return;
        ObjC.Send(View, ObjC.Sel("setAccessibilityElement:"), 1);
        ObjC.Send(View, ObjC.Sel("setAccessibilityRole:"), ObjC.String("AXGroup"));
        ObjC.Send(View, ObjC.Sel("setAccessibilityLabel:"), ObjC.String("Mote CSV grid navigation"));
        ObjC.Send(_table, ObjC.Sel("setAccessibilityLabel:"), ObjC.String("CSV grid window"));
        PublishAccessibility();
    }

    /// <summary>Retires lookup entries before releasing owned nodes; externally retained nodes read unavailable.</summary>
    private void RetireAccessibility()
    {
        if (!AccessibilityEnabled) return;
        _accessibilityFrame = null;
        _accessibilityFocusCell = null;
        _accessibilityTableFocus = false;
        foreach (var handle in _accessibilityOwned) AccessibilityNodes.Remove(handle);
        foreach (var handle in _accessibilityOwned) ObjC.Send(handle, ObjC.Sel("release"));
        _accessibilityOwned.Clear();
        _accessibilityRows = []; _accessibilityColumns = [];
        _accessibilityRowHeaders = []; _accessibilityColumnHeaders = [];
        _accessibilityCells = new nint[0, 0];
    }

    /// <summary>Copies only bounded ready descriptors and the adapter's retained logical selection.</summary>
    private void PublishAccessibility()
    {
        if (!AccessibilityEnabled || _installing || _table == 0) return;
        var document = _navigation?.Navigation.Document ?? _identity?.Document;
        if (document is null) { RetireAccessibility(); return; }
        var rows = new GridRange(_navigation?.RequestedRows.Start ?? _projection?.RequestedRows.Start ?? 0, _slots.Length);
        var selection = _selection is { } s && s.Document == document.Value
            ? new GridAccessibleSelection(new(s.AnchorRow ?? s.Row, s.AnchorRow is null ? s.Column : s.AnchorColumn),
                new(s.Row, s.Column), false) : (GridAccessibleSelection?)null;
        var window = ObjC.Send(_table, ObjC.Sel("window"));
        var hasFocus = window != 0 && ObjC.Send(window, ObjC.Sel("firstResponder")) == _table;
        var active = _accessibilityTableFocus ? null : _accessibilityFocusCell ?? selection?.Active;
        var focused = hasFocus && active is { } cell && cell.Row >= rows.Start && cell.Row < rows.End &&
            cell.Column >= _displayColumns.Start && cell.Column < _displayColumns.End ? active : null;
        var id = _accessibilityFrame?.Id ?? new GridAccessibilityId(document.Value, checked(++_accessibilitySerial));
        var frame = NativeGridAccessibility.Create(id, _navigation, _identity, _projection,
            rows, _displayColumns, selection, focused, hasFocus);
        var previous = _accessibilityFrame;
        var newTree = previous is null;
        if (newTree) BuildAccessibilityNodes(frame);
        _accessibilityFrame = frame;
        ObjC.Send(View, ObjC.Sel("setAccessibilityHelp:"), ObjC.String(frame.Status));
        if (_accessibilityFrame != frame) return;
        if (newTree) NSAccessibilityPostNotification(_table, ObjC.String("AXLayoutChanged"));
        else if (previous!.Selection != frame.Selection)
            NSAccessibilityPostNotification(_table, ObjC.String("AXSelectedCellsChanged"));
        if (_accessibilityFrame != frame) return;
        if (frame.HasTableFocus && (previous?.HasTableFocus != true || previous.FocusedCell != frame.FocusedCell))
            NSAccessibilityPostNotification(AccessibilityFocusedElement(), ObjC.String("AXFocusedUIElementChanged"));
    }

    /// <summary>Creates one bounded tree per installation; cells are never recycled into new ordinals.</summary>
    private void BuildAccessibilityNodes(GridAccessibilityFrame frame)
    {
        _accessibilityRows = new nint[frame.Rows.Count]; _accessibilityColumns = new nint[frame.Columns.Count];
        _accessibilityRowHeaders = new nint[frame.Rows.Count]; _accessibilityColumnHeaders = new nint[frame.Columns.Count];
        _accessibilityCells = new nint[frame.Rows.Count, frame.Columns.Count];
        for (var r = 0; r < frame.Rows.Count; r++)
        {
            _accessibilityRows[r] = NewAccessibilityNode(frame.Id, "row", r, -1);
            _accessibilityRowHeaders[r] = NewAccessibilityNode(frame.Id, "rowHeader", r, -1);
        }
        for (var c = 0; c < frame.Columns.Count; c++)
        {
            _accessibilityColumns[c] = NewAccessibilityNode(frame.Id, "column", -1, c);
            _accessibilityColumnHeaders[c] = NewAccessibilityNode(frame.Id, "columnHeader", -1, c);
        }
    }

    /// <summary>Owns one retain and one current lookup entry; no history is attached to a native wrapper.</summary>
    private nint NewAccessibilityNode(GridAccessibilityId id, string kind, int row, int column)
    {
        var node = ObjC.New(AccessibilityNodeClass);
        AccessibilityNodes.Add(node, new(this, id, kind, row, column));
        _accessibilityOwned.Add(node);
        return node;
    }

    /// <summary>Materializes only requested current-window cells; the cache never grows with file extent or history.</summary>
    private nint AccessibilityCell(int row, int column)
    {
        if (_accessibilityFrame is not { } frame || (uint)row >= (uint)frame.Rows.Count ||
            (uint)column >= (uint)frame.Columns.Count) return 0;
        var node = _accessibilityCells[row, column];
        if (node != 0) return node;
        node = NewAccessibilityNode(frame.Id, "cell", row, column);
        _accessibilityCells[row, column] = node;
        return node;
    }

    /// <summary>Registers native table selectors only under the explicit Grid gate.</summary>
    private static void RegisterTableAccessibility(nint cls)
    {
        if (!AccessibilityEnabled) return;
        foreach (var selector in new[] { "accessibilityChildren", "accessibilityRows", "accessibilityColumns",
            "accessibilityRowHeaderUIElements", "accessibilityColumnHeaderUIElements", "accessibilityVisibleRows",
            "accessibilityVisibleColumns", "accessibilityVisibleCells", "accessibilitySelectedCells",
            "accessibilitySelectedRows", "accessibilitySelectedColumns", "accessibilityHelp", "accessibilityRole",
            "accessibilityRowCount", "accessibilityColumnCount", "accessibilityFocusedUIElement" })
            Add(cls, selector, (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint>)&AccessibilityTableRead,
                selector.EndsWith("Count", StringComparison.Ordinal) ? "q@:" : "@@:");
        Add(cls, "accessibilityCellForColumn:row:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint>)&AccessibilityCellFor, "@@:qq");
        Add(cls, "setAccessibilitySelectedCells:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&AccessibilitySetCells, "v@:@");
        // Native row setters must not bypass rectangle admission.
        Add(cls, "setAccessibilitySelectedRows:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&AccessibilityRejectRows, "v@:@");
        Add(cls, "setAccessibilitySelectedColumns:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&AccessibilityRejectRows, "v@:@");
        Add(cls, "accessibilityHitTest:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, ObjC.Point, nint>)&AccessibilityHitTest, "@@:{CGPoint=dd}");
        foreach (var selector in new[] { "accessibilityOrderedByRow", "isAccessibilityFocused" })
            Add(cls, selector, (nint)(delegate* unmanaged[Cdecl]<nint, nint, byte>)&AccessibilityTableBool, "B@:");
        Add(cls, "setAccessibilityFocused:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, byte, void>)&AccessibilitySetFocused, "v@:B");
        foreach (var selector in new[] { "becomeFirstResponder", "resignFirstResponder" })
            Add(cls, selector, (nint)(delegate* unmanaged[Cdecl]<nint, nint, byte>)&AccessibilityResponderChanged, "B@:");
        var node = ObjC.AllocateClassPair(ObjC.Class("NSAccessibilityElement"), AccessibilityNodeClass, 0);
        if (node == 0) return;
        foreach (var selector in new[] { "accessibilityRole", "accessibilityLabel", "accessibilityHelp",
            "accessibilityValue", "accessibilityParent", "accessibilityChildren", "accessibilityIndex",
            "accessibilityRowHeaderUIElements", "accessibilityColumnHeaderUIElements", "accessibilityWindow",
            "accessibilityTopLevelUIElement", "accessibilityIdentifier" })
            Add(node, selector, (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint>)&AccessibilityNodeRead,
                selector == "accessibilityIndex" ? "q@:" : "@@:");
        foreach (var selector in new[] { "isAccessibilityElement", "isAccessibilitySelected", "isAccessibilityFocused" })
            Add(node, selector, (nint)(delegate* unmanaged[Cdecl]<nint, nint, byte>)&AccessibilityNodeBool, "B@:");
        foreach (var selector in new[] { "accessibilityRowIndexRange", "accessibilityColumnIndexRange" })
            Add(node, selector, (nint)(delegate* unmanaged[Cdecl]<nint, nint, ObjC.Range>)&AccessibilityNodeRange,
                "{_NSRange=QQ}@:");
        Add(node, "accessibilityFrame", (nint)(delegate* unmanaged[Cdecl]<nint, nint, ObjC.Rect>)&AccessibilityNodeFrame,
            "{CGRect={CGPoint=dd}{CGSize=dd}}@:");
        Add(node, "setAccessibilityFocused:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, byte, void>)&AccessibilitySetFocused, "v@:B");
        ObjC.RegisterClassPair(node);
    }

    /// <summary>Arrays are autoreleased and contain only current bounded native nodes.</summary>
    private static nint AccessibilityArray(IEnumerable<nint> handles)
    {
        var array = ObjC.Send(ObjC.Class("NSMutableArray"), ObjC.Sel("array"));
        foreach (var handle in handles) ObjC.Send(array, ObjC.Sel("addObject:"), handle);
        return array;
    }

    /// <summary>Compares interned selectors without querying source text or decoding caller data.</summary>
    private static bool AccessibilitySelector(nint selector, string name) => selector == ObjC.Sel(name);
    /// <summary>Refuse unexpected off-main AX dispatch instead of accessing AppKit or mutable lookup state.</summary>
    private static bool AccessibilityMainThread => ObjC.Send(ObjC.Class("NSThread"), ObjC.Sel("isMainThread")) != 0;
    /// <summary>Retired client handles have no provider entry and can never resolve to another window.</summary>
    private static AccessibilityNode? CurrentAccessibilityNode(nint self) =>
        AccessibilityNodes.TryGetValue(self, out var node) && node.Owner._accessibilityFrame?.Id == node.Id ? node : null;

    /// <summary>No local lookup may realize a file cell outside the installed table.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint AccessibilityCellFor(nint self, nint selector, nint column, nint row)
    {
        try
        {
            if (!AccessibilityMainThread) return 0;
            if (!Instances.TryGetValue(self, out var grid) || grid._accessibilityFrame is not { } f ||
                row < 0 || column < 0 || row >= f.Rows.Count || column >= f.Columns.Count) return 0;
            return grid.AccessibilityCell((int)row, (int)column);
        }
        catch { return 0; }
    }

    /// <summary>Serves only one published local table; native row selection is never reported as cell selection.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint AccessibilityTableRead(nint self, nint selector)
    {
        try
        {
            if (!AccessibilityMainThread) return 0;
            if (!Instances.TryGetValue(self, out var g) || g._accessibilityFrame is not { } f) return 0;
            if (AccessibilitySelector(selector, "accessibilityRole")) return ObjC.String("AXTable");
            if (AccessibilitySelector(selector, "accessibilityHelp")) return ObjC.String(f.Status);
            if (AccessibilitySelector(selector, "accessibilityRowCount")) return f.Rows.Count;
            if (AccessibilitySelector(selector, "accessibilityColumnCount")) return f.Columns.Count;
            if (AccessibilitySelector(selector, "accessibilityFocusedUIElement"))
                return g.AccessibilityFocusedElement();
            if (AccessibilitySelector(selector, "accessibilityRows")) return AccessibilityArray(g._accessibilityRows);
            if (AccessibilitySelector(selector, "accessibilityColumns")) return AccessibilityArray(g._accessibilityColumns);
            if (AccessibilitySelector(selector, "accessibilityChildren"))
                return AccessibilityArray(g._accessibilityColumnHeaders.Concat(g._accessibilityRows));
            if (AccessibilitySelector(selector, "accessibilityRowHeaderUIElements")) return AccessibilityArray(g._accessibilityRowHeaders);
            if (AccessibilitySelector(selector, "accessibilityColumnHeaderUIElements")) return AccessibilityArray(g._accessibilityColumnHeaders);
            if (AccessibilitySelector(selector, "accessibilitySelectedCells"))
                return AccessibilityArray(f.SelectedCells().Select(c => g.AccessibilityCell(c.Row - f.Rows.Start, c.Column - f.Columns.Start)));
            if (AccessibilitySelector(selector, "accessibilityVisibleRows")) return AccessibilityArray(g.VisibleAccessibilityNodes(g._accessibilityRows));
            if (AccessibilitySelector(selector, "accessibilityVisibleColumns")) return AccessibilityArray(g.VisibleAccessibilityNodes(g._accessibilityColumns));
            if (AccessibilitySelector(selector, "accessibilityVisibleCells"))
                return AccessibilityArray(g.VisibleAccessibilityCells(f));
            // Native selectedRow is the active rectangle endpoint, not semantic full-row selection.
            return AccessibilityArray([]);
        }
        catch { return 0; }
    }

    /// <summary>Native readback prevents a previously focused Grid from masking current source/scroller focus.</summary>
    private nint AccessibilityFocusedElement()
    {
        var window = ObjC.Send(_table, ObjC.Sel("window"));
        if (window == 0 || ObjC.Send(window, ObjC.Sel("firstResponder")) != _table) return 0;
        if (_accessibilityFrame is not { } f || f.FocusedCell is not { } active || !f.Contains(active)) return _table;
        return AccessibilityCell(active.Row - f.Rows.Start, active.Column - f.Columns.Start);
    }

    /// <summary>Native first responder remains the table, but only one semantic element reports keyboard focus.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte AccessibilityTableBool(nint self, nint selector)
    {
        try
        {
            if (!AccessibilityMainThread || !Instances.TryGetValue(self, out var g)) return 0;
            if (AccessibilitySelector(selector, "accessibilityOrderedByRow")) return 1;
            return g.AccessibilityFocusedElement() == self ? (byte)1 : (byte)0;
        }
        catch { return 0; }
    }

    /// <summary>Returns bounded state and relationships, never native editable field semantics.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint AccessibilityNodeRead(nint self, nint selector)
    {
        try
        {
            if (!AccessibilityMainThread) return 0;
            if (CurrentAccessibilityNode(self) is not { } n || n.Owner._accessibilityFrame is not { } f) return 0;
            var g = n.Owner;
            var coordinate = n.Kind == "cell" ? f.Coordinate(n.Row, n.Column) : default;
            if (AccessibilitySelector(selector, "accessibilityRole")) return ObjC.String(n.Kind switch
                { "cell" => "AXCell", "row" => "AXRow", "column" => "AXColumn", _ => "AXStaticText" });
            if (AccessibilitySelector(selector, "accessibilityLabel")) return ObjC.String(n.Kind == "cell" ? f.Cell(coordinate).Name :
                n.Row >= 0 ? $"Row {(long)f.Rows.Start + n.Row + 1}" : $"Column {(long)f.Columns.Start + n.Column + 1}");
            if (AccessibilitySelector(selector, "accessibilityIdentifier"))
                return ObjC.String($"mote.csv.window.{n.Id.WindowSerial}.{n.Kind}.{n.Row}.{n.Column}");
            if (AccessibilitySelector(selector, "accessibilityHelp")) return ObjC.String(n.Kind == "cell" ? f.Cell(coordinate).Help : "CSV ordinal header; indices are local to this window");
            if (AccessibilitySelector(selector, "accessibilityValue"))
                return n.Kind == "cell" && f.Cell(coordinate).PresentationValue is { } value ? ObjC.String(value) : 0;
            if (AccessibilitySelector(selector, "accessibilityIndex")) return n.Row >= 0 ? n.Row : n.Column;
            if (AccessibilitySelector(selector, "accessibilityWindow") || AccessibilitySelector(selector, "accessibilityTopLevelUIElement"))
                return ObjC.Send(g._table, ObjC.Sel("window"));
            if (AccessibilitySelector(selector, "accessibilityParent")) return n.Kind is "cell" or "rowHeader" ? g._accessibilityRows[n.Row] : g._table;
            if (AccessibilitySelector(selector, "accessibilityRowHeaderUIElements"))
                return AccessibilityArray(n.Row >= 0 ? [g._accessibilityRowHeaders[n.Row]] : []);
            if (AccessibilitySelector(selector, "accessibilityColumnHeaderUIElements"))
                return AccessibilityArray(n.Column >= 0 ? [g._accessibilityColumnHeaders[n.Column]] : []);
            if (AccessibilitySelector(selector, "accessibilityChildren") && n.Kind == "row")
                return AccessibilityArray(new[] { g._accessibilityRowHeaders[n.Row] }.Concat(
                    Enumerable.Range(0, f.Columns.Count).Select(c => g.AccessibilityCell(n.Row, c))));
            if (AccessibilitySelector(selector, "accessibilityChildren") && n.Kind == "column")
                return AccessibilityArray(Enumerable.Range(0, f.Rows.Count).Select(r => g.AccessibilityCell(r, n.Column)));
            return AccessibilityArray([]);
        }
        catch { return 0; }
    }

    /// <summary>Selection tests use the complete retained rectangle; semantic focus has one owner.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte AccessibilityNodeBool(nint self, nint selector)
    {
        try
        {
            if (!AccessibilityMainThread) return 0;
            if (CurrentAccessibilityNode(self) is not { } n || n.Owner._accessibilityFrame is not { } f) return 0;
            if (AccessibilitySelector(selector, "isAccessibilityElement")) return 1;
            if (AccessibilitySelector(selector, "isAccessibilityFocused")) return n.Owner.AccessibilityFocusedElement() == self ? (byte)1 : (byte)0;
            return n.Kind == "cell" && f.IsSelected(f.Coordinate(n.Row, n.Column)) ? (byte)1 : (byte)0;
        }
        catch { return 0; }
    }

    /// <summary>NSRange uses local cell positions; retired wrappers report an unavailable empty range.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static ObjC.Range AccessibilityNodeRange(nint self, nint selector)
    {
        try
        {
            if (!AccessibilityMainThread) return new(nuint.MaxValue, 0);
            if (CurrentAccessibilityNode(self) is not { Kind: "cell" } n) return new(nuint.MaxValue, 0);
            return new((nuint)(AccessibilitySelector(selector, "accessibilityRowIndexRange") ? n.Row : n.Column), 1);
        }
        catch { return new(nuint.MaxValue, 0); }
    }

    /// <summary>Returns actual clipped screen geometry; retired or off-main requests have no frame.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static ObjC.Rect AccessibilityNodeFrame(nint self, nint selector)
    {
        try { return AccessibilityMainThread && CurrentAccessibilityNode(self) is { } n ? n.Owner.AccessibilityScreenRect(n) : default; }
        catch { return default; }
    }

    /// <summary>Geometry uses actual AppKit layout and clips before screen conversion, including Retina.</summary>
    private ObjC.Rect AccessibilityLocalRect(AccessibilityNode n)
    {
        // macOS does not paint a separate row-number gutter. Its ordinal header is metadata, not a fabricated hit area.
        if (n.Kind == "rowHeader") return default;
        var receiver = n.Kind == "columnHeader" ? ObjC.Send(_table, ObjC.Sel("headerView")) : _table;
        if (receiver == 0) return default;
        if (n.Kind == "columnHeader")
        {
            ObjC.Rect header;
            if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                AccessibilityRectStret(out header, receiver, ObjC.Sel("headerRectOfColumn:"), n.Column, 0);
            else header = AccessibilityRectDirect(receiver, ObjC.Sel("headerRectOfColumn:"), n.Column, 0);
            return ClipAccessibilityRect(header, MacOnScreenCanvasNative.GetRect(receiver, ObjC.Sel("visibleRect")));
        }
        var selector = ObjC.Sel(n.Kind == "cell" ? "frameOfCellAtColumn:row:" : n.Row >= 0 ? "rectOfRow:" : "rectOfColumn:");
        var first = n.Kind == "cell" ? n.Column : n.Row >= 0 ? n.Row : n.Column;
        ObjC.Rect rect;
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
            AccessibilityRectStret(out rect, _table, selector, first, n.Row);
        else rect = AccessibilityRectDirect(_table, selector, first, n.Row);
        var visible = MacOnScreenCanvasNative.GetRect(_table, ObjC.Sel("visibleRect"));
        return ClipAccessibilityRect(rect, visible);
    }

    /// <summary>Empty clipped bounds never claim an offscreen cell exists at a screen point.</summary>
    internal static ObjC.Rect ClipAccessibilityRect(ObjC.Rect rect, ObjC.Rect clip)
    {
        if (!double.IsFinite(rect.Origin.X) || !double.IsFinite(rect.Origin.Y) ||
            !double.IsFinite(rect.Size.Width) || !double.IsFinite(rect.Size.Height) ||
            !double.IsFinite(clip.Origin.X) || !double.IsFinite(clip.Origin.Y) ||
            !double.IsFinite(clip.Size.Width) || !double.IsFinite(clip.Size.Height) ||
            rect.Size.Width <= 0 || rect.Size.Height <= 0 || clip.Size.Width <= 0 || clip.Size.Height <= 0)
            return default;
        var x = Math.Max(rect.Origin.X, clip.Origin.X); var y = Math.Max(rect.Origin.Y, clip.Origin.Y);
        var right = Math.Min(rect.Origin.X + rect.Size.Width, clip.Origin.X + clip.Size.Width);
        var top = Math.Min(rect.Origin.Y + rect.Size.Height, clip.Origin.Y + clip.Size.Height);
        return right <= x || top <= y ? default : new(x, y, right - x, top - y);
    }

    /// <summary>Converts through the actual native window, not source offsets or synthetic monitor scaling.</summary>
    private ObjC.Rect AccessibilityScreenRect(AccessibilityNode n)
    {
        var rect = AccessibilityLocalRect(n);
        if (rect.Size.Width <= 0 || rect.Size.Height <= 0) return default;
        var window = ObjC.Send(_table, ObjC.Sel("window"));
        if (window == 0) return default;
        var receiver = n.Kind == "columnHeader" ? ObjC.Send(_table, ObjC.Sel("headerView")) : _table;
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            AccessibilityConvertStret(out var inWindow, receiver, ObjC.Sel("convertRect:toView:"), rect, 0);
            AccessibilityConvertStret(out var screen, window, ObjC.Sel("convertRectToScreen:"), inWindow, 0);
            return screen;
        }
        var local = AccessibilityConvertDirect(receiver, ObjC.Sel("convertRect:toView:"), rect, 0);
        return AccessibilityConvertDirect(window, ObjC.Sel("convertRectToScreen:"), local, 0);
    }

    /// <summary>Visibility is measured from current clipped native layout, not ready-value existence.</summary>
    private IEnumerable<nint> VisibleAccessibilityNodes(IEnumerable<nint> handles) => handles.Where(h =>
        CurrentAccessibilityNode(h) is { } n && AccessibilityLocalRect(n) is { Size.Width: > 0, Size.Height: > 0 });

    /// <summary>Visibility enumeration allocates only intersecting cells and skips clipped rows before examining columns.</summary>
    private IEnumerable<nint> VisibleAccessibilityCells(GridAccessibilityFrame frame)
    {
        for (var row = 0; row < frame.Rows.Count; row++)
        {
            if (AccessibilityLocalRect(new(this, frame.Id, "row", row, -1)).Size.Height <= 0) continue;
            for (var column = 0; column < frame.Columns.Count; column++)
            {
                var rect = AccessibilityLocalRect(new(this, frame.Id, "cell", row, column));
                if (rect.Size.Width > 0 && rect.Size.Height > 0) yield return AccessibilityCell(row, column);
            }
        }
    }

    /// <summary>Native point lookup narrows to one data candidate; header fallback remains column-capped.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint AccessibilityHitTest(nint self, nint selector, ObjC.Point point)
    {
        try
        {
            if (!AccessibilityMainThread) return 0;
            if (!Instances.TryGetValue(self, out var g) || g._accessibilityFrame is not { } frame) return 0;
            var window = ObjC.Send(self, ObjC.Sel("window"));
            if (window == 0) return 0;
            var inWindow = MacOnScreenCanvasNative.SendPoint(window, ObjC.Sel("convertPointFromScreen:"), point, 0);
            var local = MacOnScreenCanvasNative.SendPoint(self, ObjC.Sel("convertPoint:fromView:"), inWindow, 0);
            var row = ObjC.Send(self, ObjC.Sel("rowAtPoint:"), local);
            var column = ObjC.Send(self, ObjC.Sel("columnAtPoint:"), local);
            if (row >= 0 && row < frame.Rows.Count && column >= 0 && column < frame.Columns.Count)
            {
                var handle = g.AccessibilityCell((int)row, (int)column);
                if (CurrentAccessibilityNode(handle) is { } cell && AccessibilityContainsPoint(g.AccessibilityScreenRect(cell), point))
                    return handle;
            }
            // Header lookup is bounded by the admitted column cap, never the file width.
            foreach (var handle in g._accessibilityColumnHeaders)
            {
                if (CurrentAccessibilityNode(handle) is not { } n) continue;
                if (AccessibilityContainsPoint(g.AccessibilityScreenRect(n), point)) return handle;
            }
            return 0;
        }
        catch { return 0; }
    }

    /// <summary>Uses half-open clipped geometry; an empty rectangle is never a hit.</summary>
    private static bool AccessibilityContainsPoint(ObjC.Rect rect, ObjC.Point point) =>
        rect.Size.Width > 0 && rect.Size.Height > 0 && point.X >= rect.Origin.X && point.Y >= rect.Origin.Y &&
        point.X < rect.Origin.X + rect.Size.Width && point.Y < rect.Origin.Y + rect.Size.Height;

    /// <summary>Validate the entire bounded array before changing the sole adapter selection.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void AccessibilitySetCells(nint self, nint selector, nint array)
    {
        try
        {
            if (!AccessibilityMainThread) return;
            if (!Instances.TryGetValue(self, out var g) || g._accessibilityFrame is not { } f) return;
            var count = array == 0 ? 0 : ObjC.Send(array, ObjC.Sel("count"));
            if (count < 0 || count > 8192) return;
            var cells = new List<GridCoordinate>((int)count);
            for (var i = 0; i < count; i++)
            {
                var handle = ObjC.Send(array, ObjC.Sel("objectAtIndex:"), i);
                if (CurrentAccessibilityNode(handle) is not { Kind: "cell" } n || n.Owner != g || n.Id != f.Id) return;
                cells.Add(f.Coordinate(n.Row, n.Column));
            }
            var result = NativeGridAccessibility.ValidateRectangle(f, cells, out var selection);
            if (result is not GridAccessibilityResult.Applied and not GridAccessibilityResult.NoChange) return;
            g.MutateSelection(f.Id, selection is { } s ? new GridSelectionMutation.ReplaceRectangle(s) : new GridSelectionMutation.Clear());
        }
        catch { /* Refuse atomically; never unwind into AppKit. */ }
    }

    /// <summary>Axis selection setters cannot bypass the rectangle-only cell selection owner.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void AccessibilityRejectRows(nint self, nint selector, nint rows) { }

    /// <summary>Selection is admitted against the latest full retained rectangle, not a provider copy.</summary>
    public GridAccessibilityResult MutateSelection(GridAccessibilityId id, GridSelectionMutation mutation)
    {
        if (!AccessibilityMainThread) return GridAccessibilityResult.Unavailable;
        if (_installing || _accessibilityFrame is not { } f || f.Id != id) return GridAccessibilityResult.Stale;
        var result = NativeGridAccessibility.Mutate(f.Selection, mutation, out var next);
        if (result != GridAccessibilityResult.Applied) return result;
        if (mutation is GridSelectionMutation.ReplaceRectangle && next is { } selection &&
            (!f.Contains(selection.Anchor) || !f.Contains(selection.Active)))
            return GridAccessibilityResult.InvalidCoordinate;
        if (mutation is GridSelectionMutation.AddCell add && !f.Contains(add.Cell) ||
            mutation is GridSelectionMutation.RemoveCell remove && !f.Contains(remove.Cell))
            return GridAccessibilityResult.InvalidCoordinate;
        var serial = _installSerial;
        _installing = true;
        try
        {
            if (next is { } selected)
            {
                _column = selected.Active.Column - f.Columns.Start;
                _anchorRow = selected.Anchor.Row - f.Rows.Start; _anchorColumn = selected.Anchor.Column - f.Columns.Start;
                _selection = (id.Document, selected.Active.Row, selected.Active.Column, selected.Anchor.Row, selected.Anchor.Column);
                if (f.Contains(selected.Active)) SelectRow(selected.Active.Row - f.Rows.Start, serial);
                else ObjC.Send(_table, ObjC.Sel("deselectAll:"), 0);
            }
            else
            {
                _selection = null; _anchorRow = -1;
                ObjC.Send(_table, ObjC.Sel("deselectAll:"), 0);
            }
            if (!Current(serial)) return GridAccessibilityResult.Stale;
            UpdateDetail(serial);
            if (!Current(serial)) return GridAccessibilityResult.Stale;
            // UpdateDetail reads native endpoints; keep the exact full rectangle for off-window anchors.
            _selection = next is { } retained ? (id.Document, retained.Active.Row, retained.Active.Column,
                retained.Anchor.Row, retained.Anchor.Column) : null;
            RefreshSelectionColors(serial);
            if (!Current(serial)) return GridAccessibilityResult.Stale;
        }
        finally { if (Current(serial)) _installing = false; }
        PublishAccessibility();
        if (_accessibilityFrame?.Id == id && _accessibilityFrame.Selection == next && f.Ready is { } ready)
            _command(new(ready, NativeGridIntentKind.Select, f.Rows.Start, f.Columns.Start));
        return _accessibilityFrame?.Id == id && _accessibilityFrame.Selection == next
            ? GridAccessibilityResult.Applied : GridAccessibilityResult.Stale;
    }

    /// <summary>Source composition is checked before transfer; success requires native focus readback after reentry.</summary>
    public GridAccessibilityResult Focus(GridAccessibilityId id, GridCoordinate? cell)
    {
        if (!AccessibilityMainThread) return GridAccessibilityResult.Unavailable;
        if (_installing || _accessibilityFrame is not { } f || f.Id != id) return GridAccessibilityResult.Stale;
        if (cell is { } coordinate && !f.Contains(coordinate)) return GridAccessibilityResult.InvalidCoordinate;
        if (cell is { } pending && f.Cell(pending).State == GridValueState.Pending) return GridAccessibilityResult.NotReady;
        if (_isCompositionActive?.Invoke() == true) return GridAccessibilityResult.CompositionBlocked;
        var window = ObjC.Send(_table, ObjC.Sel("window"));
        if (_accessibilityFrame?.Id != id) return GridAccessibilityResult.Stale;
        if (window == 0 || ObjC.Send(window, ObjC.Sel("makeFirstResponder:"), _table) == 0)
            return GridAccessibilityResult.Unavailable;
        if (_accessibilityFrame?.Id != id) return GridAccessibilityResult.Stale;
        if (ObjC.Send(window, ObjC.Sel("firstResponder")) != _table) return GridAccessibilityResult.Unavailable;
        if (_accessibilityFrame?.Id != id) return GridAccessibilityResult.Stale;
        _accessibilityFocusCell = cell;
        _accessibilityTableFocus = cell is null;
        PublishAccessibility();
        if (_accessibilityFrame?.Id != id) return GridAccessibilityResult.Stale;
        if (_accessibilityFrame.FocusedCell != cell || !_accessibilityFrame.HasTableFocus ||
            ObjC.Send(window, ObjC.Sel("firstResponder")) != _table) return GridAccessibilityResult.Stale;
        if (_accessibilityFrame?.Id != id || _accessibilityFrame.FocusedCell != cell) return GridAccessibilityResult.Stale;
        return GridAccessibilityResult.Applied;
    }

    /// <summary>AX focus transfer uses admitted native readback and never implicitly reveals source.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void AccessibilitySetFocused(nint self, nint selector, byte focused)
    {
        try
        {
            if (!AccessibilityMainThread) return;
            if (focused == 0) return;
            if (CurrentAccessibilityNode(self) is { Kind: "cell" } n && n.Owner._accessibilityFrame is { } f)
                n.Owner.Focus(n.Id, f.Coordinate(n.Row, n.Column));
            else if (Instances.TryGetValue(self, out var g) && g._accessibilityFrame is { } table)
                g.Focus(table.Id, null);
        }
        catch { /* Focus refusal must not unwind through AppKit. */ }
    }

    /// <summary>Native keyboard/pointer focus changes refresh the semantic frame without changing selection.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte AccessibilityResponderChanged(nint self, nint selector)
    {
        try
        {
            if (!AccessibilityMainThread) return 0;
            var superclass = new MacOnScreenCanvasNative.Super(self, ObjC.Class("NSTableView"));
            var result = AccessibilitySuperResponder(ref superclass, selector);
            if (result != 0 && Instances.TryGetValue(self, out var g)) g.PublishAccessibility();
            return result;
        }
        catch { return 0; }
    }

    /// <summary>Physical arrows start at the semantically focused cell; focus alone never changes the retained rectangle.</summary>
    private bool PrepareAccessibilityKeyboardNavigation()
    {
        if (!AccessibilityEnabled || _accessibilityFocusCell is not { } cell || _accessibilityFrame is not { } frame)
            return true;
        if (_installing || !frame.Contains(cell)) return false;
        var serial = _installSerial;
        var retained = _selection;
        _installing = true;
        try
        {
            _column = cell.Column - frame.Columns.Start;
            _anchorRow = -1;
            SelectRow(cell.Row - frame.Rows.Start, serial);
            if (!Current(serial)) return false;
            _selection = retained;
            _accessibilityFocusCell = null;
            _accessibilityTableFocus = false;
            return true;
        }
        finally { if (Current(serial)) _installing = false; }
    }
}
