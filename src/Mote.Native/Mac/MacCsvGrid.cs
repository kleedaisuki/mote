using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Formats;
using Mote.Native.Mac.Canvas;
using Mote.Themes;

namespace Mote.Native.Mac;

/// <summary>A read-only AppKit table over ready bounded CSV descriptors, never engine text.</summary>
/// <remarks>Native callbacks only read the installed projection. Commands carry its identity.</remarks>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacCsvGrid : IDisposable
{
    /// <summary>Instances are removed before their delegate is released on the UI thread.</summary>
    private static readonly Dictionary<nint, MacCsvGrid> Instances = [];
    private const string TableClass = "MoteCsvGridTable";
    private const string DelegateClass = "MoteCsvGridDelegate";
    private readonly Action<NativeGridIntent> _command;
    private readonly Action<NativeGridWindowRequest> _windowRequest;
    private int _anchorRow = -1;
    private int _anchorColumn;
    private bool _windowPending;
    private NativeGridIntent? _menuSelection;
    private NativeGridIntent? _menuCell;
    private NativeGridWindowRequest? _menuWindow;
    private GridExtent _menuExtent;
    private nint _delegate;
    private nint _table;
    private nint _scroll;
    private nint _detail;
    private GridRenderProjection? _projection;
    private NativePresentationId? _identity;
    private string[,] _ready = new string[0, 0];
    private int _column;
    private bool _installing;
    private IThemePolicy? _theme;
    /// <summary>Snapshot-free logical coordinates survive same-document source page changes.</summary>
    private (NativeDocumentStamp Document, int Row, int Column, int? AnchorRow, int AnchorColumn)? _selection;

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SendCell(nint receiver, nint selector, nint column, nint row, byte create);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSendSuper")]
    private static extern void SendSuperEvent(ref MacOnScreenCanvasNative.Super receiver,
        nint selector, nint nativeEvent);

    /// <summary>Creates one owned container; native rows and columns never exceed IR budgets.</summary>
    internal MacCsvGrid(Action<NativeGridIntent> command, Action<NativeGridWindowRequest> windowRequest)
    {
        _command = command; _windowRequest = windowRequest;
        RegisterClasses();
        View = ObjC.Send(ObjC.Send(ObjC.Class("NSView"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(0, 0, 390, 730));
        ObjC.Send(View, ObjC.Sel("setAutoresizingMask:"), (nint)18);
        _scroll = ObjC.Send(ObjC.Send(ObjC.Class("NSScrollView"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(0, 64, 390, 666));
        ObjC.Send(_scroll, ObjC.Sel("setAutoresizingMask:"), (nint)18);
        ObjC.Send(_scroll, ObjC.Sel("setHasVerticalScroller:"), 1);
        ObjC.Send(_scroll, ObjC.Sel("setHasHorizontalScroller:"), 1);
        _table = ObjC.Send(ObjC.Send(ObjC.Class(TableClass), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(0, 0, 390, 666));
        _delegate = ObjC.New(DelegateClass);
        Instances.Add(_table, this); Instances.Add(_delegate, this);
        ObjC.Send(_table, ObjC.Sel("setDelegate:"), _delegate);
        ObjC.Send(_table, ObjC.Sel("setDataSource:"), _delegate);
        ObjC.Send(_table, ObjC.Sel("setSelectionHighlightStyle:"), -1);
        ObjC.Send(_table, ObjC.Sel("setAllowsMultipleSelection:"), 0);
        ObjC.Send(_table, ObjC.Sel("setAllowsEmptySelection:"), 1);
        ObjC.Send(_table, ObjC.Sel("setAllowsColumnReordering:"), 0);
        ObjC.Send(_table, ObjC.Sel("setColumnAutoresizingStyle:"), 0);
        ObjC.Send(_table, ObjC.Sel("setRowHeight:"), 24d);
        ObjC.Send(_table, ObjC.Sel("setTarget:"), _delegate);
        ObjC.Send(_table, ObjC.Sel("setDoubleAction:"), ObjC.Sel("moteReveal:"));
        ObjC.Send(_table, ObjC.Sel("setAccessibilityLabel:"), ObjC.String("Mote CSV grid"));
        InstallMenu();
        ObjC.Send(_scroll, ObjC.Sel("setDocumentView:"), _table);
        ObjC.Send(View, ObjC.Sel("addSubview:"), _scroll);
        _detail = ObjC.Send(ObjC.Send(ObjC.Class("NSTextField"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(8, 4, 374, 56));
        ObjC.Send(_detail, ObjC.Sel("setAutoresizingMask:"), (nint)2);
        ObjC.Send(_detail, ObjC.Sel("setEditable:"), 0);
        ObjC.Send(_detail, ObjC.Sel("setSelectable:"), 0);
        ObjC.Send(_detail, ObjC.Sel("setBezeled:"), 0);
        ObjC.Send(_detail, ObjC.Sel("setDrawsBackground:"), 0);
        ObjC.Send(_detail, ObjC.Sel("setAccessibilityLabel:"), ObjC.String("CSV selected cell detail"));
        ObjC.Send(View, ObjC.Sel("addSubview:"), _detail);
    }

    /// <summary>Retained container used in the established preview split.</summary>
    internal nint View { get; }
    /// <summary>Target-only native acceptance handle; does not mutate table contents.</summary>
    internal nint Table => _table;
    /// <summary>Exact installed identity, cleared before replacement or source invalidation.</summary>
    internal NativePresentationId? Identity => _identity;

    /// <summary>Installs bounded display strings before allowing AppKit to ask for rows.</summary>
    internal void Install(GridRenderProjection projection, NativePresentationId identity)
    {
        var old = _projection;
        var sameDocument = _identity?.Document == identity.Document;
        var selected = sameDocument && old is not null ? (int)ObjC.Send(_table, ObjC.Sel("selectedRow")) : -1;
        int? selectedOrdinal = selected >= 0 && selected < (old?.Rows.Count ?? 0) ? old!.Rows[selected].Ordinal : null;
        int? anchorOrdinal = sameDocument && old is not null && _anchorRow >= 0 && _anchorRow < old.Rows.Count ? old.Rows[_anchorRow].Ordinal : null;
        var selectedColumn = sameDocument && old is not null ? old.RequestedColumns.Start + _column : projection.RequestedColumns.Start;
        var anchorColumn = sameDocument && old is not null ? old.RequestedColumns.Start + _anchorColumn : selectedColumn;
        if (!sameDocument && _selection is { } retained && retained.Document == identity.Document)
        {
            selectedOrdinal = retained.Row; selectedColumn = retained.Column;
            anchorOrdinal = retained.AnchorRow; anchorColumn = retained.AnchorColumn;
        }
        Clear(preserveSelection: true);
        if (projection.Version != identity.Document.Version) return;
        _installing = true;
        try
        {
            var columns = ObjC.Send(_table, ObjC.Sel("tableColumns"));
            while (ObjC.Send(columns, ObjC.Sel("count")) > 0)
                ObjC.Send(_table, ObjC.Sel("removeTableColumn:"),
                    ObjC.Send(columns, ObjC.Sel("objectAtIndex:"), 0));
            _ready = new string[projection.Rows.Count, projection.RequestedColumns.Count];
            for (var column = 0; column < projection.RequestedColumns.Count; column++)
            {
                var actual = projection.RequestedColumns.Start + column;
                var native = ObjC.Send(ObjC.Send(ObjC.Class("NSTableColumn"), ObjC.Sel("alloc")),
                    ObjC.Sel("initWithIdentifier:"), ObjC.String(actual.ToString(CultureInfo.InvariantCulture)));
                ObjC.Send(native, ObjC.Sel("setTitle:"), ObjC.String($"Column {actual + 1}"));
                ObjC.Send(native, ObjC.Sel("setWidth:"), 160d);
                ObjC.Send(native, ObjC.Sel("setMinWidth:"), 64d);
                ObjC.Send(_table, ObjC.Sel("addTableColumn:"), native);
                ObjC.Send(native, ObjC.Sel("release"));
            }
            for (var row = 0; row < projection.Rows.Count; row++)
            {
                for (var column = 0; column < projection.RequestedColumns.Count; column++)
                    _ready[row, column] = "[pending]";
                foreach (var cell in projection.Rows[row].Cells)
                {
                    var text = projection.DisplayText.Substring(cell.DisplayRange.Start, cell.DisplayRange.Length);
                    var suffix = cell.State switch
                    {
                        GridValueState.Clipped => " [clipped]", GridValueState.Oversized => "[oversized; reveal source]",
                        GridValueState.Pending => "[pending]", GridValueState.Missing => "[missing]", _ => string.Empty
                    };
                    _ready[row, cell.Column - projection.RequestedColumns.Start] = text + suffix +
                        (cell.HasSyntaxError ? " [syntax error]" : string.Empty);
                }
            }
            _column = Math.Clamp(selectedColumn - projection.RequestedColumns.Start, 0, Math.Max(0, projection.RequestedColumns.Count - 1));
            _projection = projection;
            ObjC.Send(_table, ObjC.Sel("reloadData"));
            _identity = identity; _windowPending = false; _anchorRow = -1;
            var restoredRow = selectedOrdinal is { } ordinal ? FindLocalRow(projection, ordinal) : -1;
            if (projection.Rows.Count > 0) SelectRow(restoredRow >= 0 ? restoredRow : 0);
            if (restoredRow >= 0 && anchorOrdinal is { } anchor &&
                anchorColumn >= projection.RequestedColumns.Start && anchorColumn < projection.RequestedColumns.End)
            { _anchorRow = FindLocalRow(projection, anchor); _anchorColumn = anchorColumn - projection.RequestedColumns.Start; }
            UpdateDetail();
        }
        finally { _installing = false; }
    }

    /// <summary>Invalidates commands before native reload can reenter a delegate callback.</summary>
    internal void Clear(bool preserveSelection = false)
    {
        if (!preserveSelection) _selection = null;
        _identity = null; _projection = null; _anchorRow = -1; _ready = new string[0, 0];
        if (_table != 0) ObjC.Send(_table, ObjC.Sel("reloadData"));
        if (_detail != 0) ObjC.Send(_detail, ObjC.Sel("setStringValue:"), ObjC.String(string.Empty));
    }

    /// <summary>Recolors read-only table and detail without source or Undo changes.</summary>
    internal void SetTheme(IThemePolicy theme)
    {
        _theme = theme;
        ObjC.Send(_table, ObjC.Sel("setBackgroundColor:"), Color(theme.Palette.PreviewBackground));
        ObjC.Send(_detail, ObjC.Sel("setTextColor:"), Color(theme.Palette.PreviewForeground));
        ObjC.Send(_detail, ObjC.Sel("setFont:"), ObjC.Send(ObjC.Class("NSFont"),
            ObjC.Sel("systemFontOfSize:"), theme.Typography.UiFontSize));
        ObjC.Send(_table, ObjC.Sel("reloadData"));
    }

    /// <summary>Commands never copy sanitized display strings or modify AppKit field text.</summary>
    internal void CopySelection() => Emit(IsRectangle ? NativeGridIntentKind.CopyTsv : NativeGridIntentKind.CopyValue);

    internal void Emit(NativeGridIntentKind kind)
    {
        var intent = CaptureIntent(kind);
        if (intent is { } ready) _command(ready);
    }

    private NativeGridIntent? CaptureIntent(NativeGridIntentKind kind)
    {
        if (_installing || _identity is not { } identity || _projection is not { } projection) return null;
        var row = (int)ObjC.Send(_table, ObjC.Sel("selectedRow"));
        if (row < 0 || row >= projection.Rows.Count || _column >= projection.RequestedColumns.Count) return null;
        var anchorRow = _anchorRow >= 0 && _anchorRow < projection.Rows.Count ? _anchorRow : row;
        var rectangle = kind is not NativeGridIntentKind.Reveal and not NativeGridIntentKind.Replace and not NativeGridIntentKind.CopyValue and not NativeGridIntentKind.CopySource &&
            _anchorRow >= 0 && (anchorRow != row || _anchorColumn != _column);
        return new NativeGridIntent(identity, kind, projection.Rows[rectangle ? anchorRow : row].Ordinal,
            projection.RequestedColumns.Start + (rectangle ? _anchorColumn : _column),
            rectangle ? projection.Rows[row].Ordinal : null,
            rectangle ? projection.RequestedColumns.Start + _column : null,
            kind == NativeGridIntentKind.CopyRows);
    }

    private static int FindLocalRow(GridRenderProjection grid, int ordinal)
    {
        for (var row = 0; row < grid.Rows.Count; row++) if (grid.Rows[row].Ordinal == ordinal) return row;
        return -1;
    }

    /// <summary>Select All means only this certified bounded window, not unindexed rows.</summary>
    internal void SelectAllReady()
    {
        if (_projection is not { Rows.Count: > 0 } grid || grid.RequestedColumns.Count == 0) return;
        _anchorRow = 0; _anchorColumn = 0; _column = grid.RequestedColumns.Count - 1;
        SelectRow(grid.Rows.Count - 1); UpdateDetail(); Emit(NativeGridIntentKind.Select);
    }

    private bool IsRectangle => _anchorRow >= 0 &&
        (_anchorRow != (int)ObjC.Send(_table, ObjC.Sel("selectedRow")) || _anchorColumn != _column);

    /// <summary>Requests adjacent bounded coordinates; repeated momentum events are coalesced.</summary>
    private void RequestWindow(int rowDirection, int columnDirection)
    {
        if (_windowPending || _identity is not { } identity || _projection is not { } projection) return;
        var first = projection.Rows.Count > 0 ? projection.Rows[0].Ordinal : projection.RequestedRows.Start;
        var row = rowDirection == 0 ? first : (int)Math.Clamp((long)first + rowDirection * Math.Max(1, projection.RequestedRows.Count), 0, int.MaxValue - 1L);
        if (projection.Extent.ExactRowCount is { } count && row >= count) return;
        var column = (int)Math.Clamp((long)projection.RequestedColumns.Start + columnDirection * projection.RequestedColumns.Count, 0, int.MaxValue - 1L);
        if (projection.Extent.ExactMaxWidth is { } width && column >= width) return;
        if (row == first && column == projection.RequestedColumns.Start) return;
        _windowPending = true;
        _windowRequest(new NativeGridWindowRequest(identity, row,
            new GridRange(column, Math.Min(projection.RequestedColumns.Count, int.MaxValue - column)),
            RowLimit: Math.Min(64, int.MaxValue - row)));
    }

    /// <summary>Explicit modes are discoverable without overloading sanitized display Copy.</summary>
    private void InstallMenu()
    {
        var menu = ObjC.New("NSMenu");
        ObjC.Send(menu, ObjC.Sel("setDelegate:"), _delegate);
        try
        {
            foreach (var (title, kind) in new (string, NativeGridIntentKind)[]
            {
                ("Reveal cell in source", NativeGridIntentKind.Reveal),
                ("Copy decoded value", NativeGridIntentKind.CopyValue),
                ("Copy selected cells as CSV", NativeGridIntentKind.CopyCsv),
                ("Copy selected cells as quoted TSV", NativeGridIntentKind.CopyTsv),
                ("Copy exact source records", NativeGridIntentKind.CopyRows),
                ("Copy exact field source", NativeGridIntentKind.CopySource),
                ("Copy CSV with missing fields padded", NativeGridIntentKind.CopyCsvPadded),
                ("Replace selected cell…", NativeGridIntentKind.Replace)
            })
            {
                var item = ObjC.Send(ObjC.Send(ObjC.Class("NSMenuItem"), ObjC.Sel("alloc")),
                    ObjC.Sel("initWithTitle:action:keyEquivalent:"), ObjC.String(title),
                    ObjC.Sel("moteGridCommand:"), ObjC.String(string.Empty));
                ObjC.Send(item, ObjC.Sel("setTarget:"), _delegate);
                ObjC.Send(item, ObjC.Sel("setTag:"), (nint)kind);
                ObjC.Send(menu, ObjC.Sel("addItem:"), item);
                ObjC.Send(item, ObjC.Sel("release"));
            }
            foreach (var (title, selector) in new[]
            {
                ("Follow source caret", "moteGridFollowSource:"),
                ("Go to row:column…", "moteGridGoToCell:")
            })
            {
                var item = ObjC.Send(ObjC.Send(ObjC.Class("NSMenuItem"), ObjC.Sel("alloc")),
                    ObjC.Sel("initWithTitle:action:keyEquivalent:"), ObjC.String(title),
                    ObjC.Sel(selector), ObjC.String(string.Empty));
                ObjC.Send(item, ObjC.Sel("setTarget:"), _delegate);
                ObjC.Send(menu, ObjC.Sel("addItem:"), item);
                ObjC.Send(item, ObjC.Sel("release"));
            }
            ObjC.Send(_table, ObjC.Sel("setMenu:"), menu);
        }
        finally { ObjC.Send(menu, ObjC.Sel("release")); }
    }

    private void SelectRow(int row)
    {
        var indexes = ObjC.Send(ObjC.Class("NSIndexSet"), ObjC.Sel("indexSetWithIndex:"), row);
        ObjC.Send(_table, ObjC.Sel("selectRowIndexes:byExtendingSelection:"), indexes, (byte)0);
    }

    private void UpdateDetail()
    {
        if (_projection is not { } projection) return;
        var row = (int)ObjC.Send(_table, ObjC.Sel("selectedRow"));
        if (_identity is { } identity && row >= 0 && row < projection.Rows.Count)
            _selection = (identity.Document, projection.Rows[row].Ordinal, projection.RequestedColumns.Start + _column,
                _anchorRow >= 0 && _anchorRow < projection.Rows.Count ? projection.Rows[_anchorRow].Ordinal : null,
                projection.RequestedColumns.Start + _anchorColumn);
        var text = row >= 0 && row < projection.Rows.Count
            ? $"Row {projection.Rows[row].Ordinal + 1}, Column {projection.RequestedColumns.Start + _column + 1}: {_ready[row, _column]}"
            : $"No certified rows ready. {projection.Completeness}";
        ObjC.Send(_detail, ObjC.Sel("setStringValue:"), ObjC.String(text +
            "\nShift: rectangle · Return: source · ⌘C: copy · ⌘Return: replace · Page ↑/↓: rows · ⌥←/→: columns"));
        RefreshSelectionColors();
    }

    private nint CellView(nint column, int row)
    {
        if (_projection is not { } projection || row < 0 || row >= projection.Rows.Count) return 0;
        var identifier = ObjC.Send(column, ObjC.Sel("identifier"));
        if (!int.TryParse(ObjC.ManagedString(identifier), NumberStyles.None, CultureInfo.InvariantCulture, out var actual)) return 0;
        var index = actual - projection.RequestedColumns.Start;
        if (index < 0 || index >= projection.RequestedColumns.Count) return 0;
        var view = ObjC.Send(_table, ObjC.Sel("makeViewWithIdentifier:owner:"), identifier, _delegate);
        if (view == 0)
        {
            view = ObjC.Send(ObjC.Send(ObjC.Class("NSTextField"), ObjC.Sel("alloc")),
                ObjC.Sel("initWithFrame:"), new ObjC.Rect(0, 0, 160, 24));
            ObjC.Send(view, ObjC.Sel("setIdentifier:"), identifier);
            ObjC.Send(view, ObjC.Sel("setEditable:"), 0);
            ObjC.Send(view, ObjC.Sel("setSelectable:"), 0);
            ObjC.Send(view, ObjC.Sel("setBezeled:"), 0);
            ObjC.Send(view, ObjC.Sel("setDrawsBackground:"), 0);
            ObjC.Send(view, ObjC.Sel("autorelease"));
        }
        ObjC.Send(view, ObjC.Sel("setStringValue:"), ObjC.String(_ready[row, index]));
        ObjC.Send(view, ObjC.Sel("setToolTip:"), ObjC.String(_ready[row, index]));
        var descriptor = NativeCsvGrid.Cell(projection.Rows[row], actual);
        ObjC.Send(view, ObjC.Sel("setAccessibilityLabel:"), ObjC.String(
            $"Row {projection.Rows[row].Ordinal + 1}, Column {actual + 1}, {descriptor?.State.ToString() ?? "Pending"}: {_ready[row, index]}"));
        ApplyCellColors(view, row, index);
        ObjC.Send(view, ObjC.Sel("setFont:"), ObjC.Send(ObjC.Class("NSFont"),
            ObjC.Sel("systemFontOfSize:"), _theme?.Typography.UiFontSize ?? 12d));
        ObjC.Send(ObjC.Send(view, ObjC.Sel("cell")), ObjC.Sel("setLineBreakMode:"), 4);
        return view;
    }

    /// <summary>Paints exactly the logical rectangle, not a misleading full-row selection.</summary>
    private void RefreshSelectionColors()
    {
        if (_table == 0 || _projection is not { } grid) return;
        for (var row = 0; row < grid.Rows.Count; row++)
            for (var column = 0; column < grid.RequestedColumns.Count; column++)
            {
                var view = SendCell(_table, ObjC.Sel("viewAtColumn:row:makeIfNecessary:"), column, row, 0);
                if (view != 0) ApplyCellColors(view, row, column);
            }
    }

    private void ApplyCellColors(nint view, int row, int column)
    {
        var currentRow = (int)ObjC.Send(_table, ObjC.Sel("selectedRow"));
        var anchor = _anchorRow >= 0 ? _anchorRow : currentRow;
        var anchorColumn = _anchorRow >= 0 ? _anchorColumn : _column;
        var selected = currentRow >= 0 && row >= Math.Min(anchor, currentRow) && row <= Math.Max(anchor, currentRow) &&
            column >= Math.Min(anchorColumn, _column) && column <= Math.Max(anchorColumn, _column);
        ObjC.Send(view, ObjC.Sel("setDrawsBackground:"), selected ? 1 : 0);
        ObjC.Send(view, ObjC.Sel("setBackgroundColor:"), Color(_theme?.Palette.SelectionBackground ?? new ThemeColor(53,90,133)));
        ObjC.Send(view, ObjC.Sel("setTextColor:"), Color(selected
            ? _theme?.Palette.SelectionForeground ?? new ThemeColor(255,255,255)
            : _theme?.Palette.PreviewForeground ?? new ThemeColor(225,227,231)));
    }

    private static nint Color(ThemeColor color) => ObjC.Send(ObjC.Class("NSColor"),
        ObjC.Sel("colorWithSRGBRed:green:blue:alpha:"), color.Red / 255d, color.Green / 255d, color.Blue / 255d, 1d);

    private static void RegisterClasses()
    {
        var cls = ObjC.AllocateClassPair(ObjC.Class("NSTableView"), TableClass, 0);
        if (cls != 0)
        {
            Add(cls, "pasteAsPlainText:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&ReadOnlyCommand, "v@:@");
            Add(cls, "paste:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&ReadOnlyCommand, "v@:@");
            Add(cls, "cut:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&ReadOnlyCommand, "v@:@");
            Add(cls, "keyDown:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&KeyDown, "v@:@");
            Add(cls, "scrollWheel:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&ScrollWheel, "v@:@");
            Add(cls, "mouseDown:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MouseDown, "v@:@");
            ObjC.RegisterClassPair(cls);
        }
        cls = ObjC.AllocateClassPair(ObjC.Class("NSObject"), DelegateClass, 0);
        if (cls == 0) return;
        Add(cls, "numberOfRowsInTableView:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint>)&RowCount, "q@:@");
        Add(cls, "tableView:viewForTableColumn:row:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, nint>)&ViewForCell, "@@:@@q");
        Add(cls, "tableViewSelectionDidChange:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&SelectionChanged, "v@:@");
        Add(cls, "moteGridFollowSource:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MenuFollowSource, "v@:@");
        Add(cls, "moteGridGoToCell:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MenuGoToCell, "v@:@");
        Add(cls, "menuWillOpen:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MenuWillOpen, "v@:@");
        Add(cls, "menuDidClose:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MenuDidClose, "v@:@");
        Add(cls, "moteGridCommand:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MenuCommand, "v@:@");
        Add(cls, "moteReveal:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Reveal, "v@:@");
        ObjC.RegisterClassPair(cls);
    }

    private static void Add(nint cls, string selector, nint implementation, string encoding)
    {
        if (!ObjC.AddMethod(cls, ObjC.Sel(selector), implementation, encoding))
            throw new InvalidOperationException($"Could not register CSV table selector {selector}.");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ReadOnlyCommand(nint self, nint selector, nint sender) { }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint RowCount(nint self, nint selector, nint table)
    { try { return Instances.TryGetValue(self, out var grid) ? grid._ready.GetLength(0) : 0; } catch { return 0; } }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint ViewForCell(nint self, nint selector, nint table, nint column, nint row)
    { try { return Instances.TryGetValue(self, out var grid) ? grid.CellView(column, checked((int)row)) : 0; } catch { return 0; } }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SelectionChanged(nint self, nint selector, nint notification)
    { try { if (Instances.TryGetValue(self, out var grid)) { grid.UpdateDetail(); grid.Emit(NativeGridIntentKind.Select); } } catch { } }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Reveal(nint self, nint selector, nint table)
    {
        try
        {
            if (!Instances.TryGetValue(self, out var grid)) return;
            var column = (int)ObjC.Send(grid._table, ObjC.Sel("clickedColumn"));
            if (column >= 0 && column < grid._ready.GetLength(1)) grid._column = column;
            grid.Emit(NativeGridIntentKind.Reveal);
        }
        catch { }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MenuWillOpen(nint self, nint selector, nint menu)
    { try { if (Instances.TryGetValue(self, out var grid)) {
            grid._menuSelection = grid.CaptureIntent(NativeGridIntentKind.Select);
            grid._menuCell = grid.CaptureIntent(NativeGridIntentKind.Reveal);
            grid._menuWindow = grid._identity is { } identity && grid._projection is { } projection
                ? new NativeGridWindowRequest(identity, projection.RequestedRows.Start, projection.RequestedColumns) : null;
            grid._menuExtent = grid._projection?.Extent ?? default;
        } } catch { } }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MenuDidClose(nint self, nint selector, nint menu) { }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MenuFollowSource(nint self, nint selector, nint item)
    {
        try
        {
            if (Instances.TryGetValue(self, out var grid) && grid._menuWindow is { } opening)
                grid._windowRequest(opening with { FollowSource = true });
        }
        catch { /* Never unwind into AppKit. */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MenuGoToCell(nint self, nint selector, nint item)
    {
        try
        {
            if (!Instances.TryGetValue(self, out var grid) || grid._menuWindow is not { } opening) return;
            // Freeze before modal reentrancy; a new projection cannot rebound this command.
            var extent = grid._menuExtent;
            var active = grid._menuCell;
            var initial = active is { } cell ? $"{(long)cell.Row + 1}:{(long)cell.Column + 1}" : "1:1";
            var coordinates = MacGridNavigation.Prompt(initial);
            if (coordinates is null) return;
            if (MacGridNavigation.TryCreateRequest(coordinates, opening, extent, out var request))
                grid._windowRequest(request);
            else ObjC.Send(grid._detail, ObjC.Sel("setStringValue:"), ObjC.String(
                "Enter positive row:column coordinates within the known exact extent. No table window was changed."));
        }
        catch { /* Never unwind into AppKit. */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MenuCommand(nint self, nint selector, nint item)
    {
        try
        {
            if (!Instances.TryGetValue(self, out var grid)) return;
            var kind = (NativeGridIntentKind)(int)ObjC.Send(item, ObjC.Sel("tag"));
            var frozen = kind is NativeGridIntentKind.Reveal or NativeGridIntentKind.Replace or NativeGridIntentKind.CopyValue or NativeGridIntentKind.CopySource
                ? grid._menuCell : grid._menuSelection;
            if (Enum.IsDefined(kind) && frozen is { } origin)
                grid._command(origin with { Kind = kind, WholeRows = kind == NativeGridIntentKind.CopyRows });
        }
        catch { }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MouseDown(nint self, nint selector, nint nativeEvent)
    {
        try
        {
            if (!Instances.TryGetValue(self, out var grid)) return;
            var shift = ((nuint)ObjC.Send(nativeEvent, ObjC.Sel("modifierFlags")) & (1u << 17)) != 0;
            if (shift && grid._anchorRow < 0) { grid._anchorRow = (int)ObjC.Send(self, ObjC.Sel("selectedRow")); grid._anchorColumn = grid._column; }
            var superclass = new MacOnScreenCanvasNative.Super(self, ObjC.Class("NSTableView"));
            SendSuperEvent(ref superclass, selector, nativeEvent);
            var column = (int)ObjC.Send(self, ObjC.Sel("clickedColumn"));
            if (column >= 0 && column < grid._ready.GetLength(1)) grid._column = column;
            if (!shift) { grid._anchorRow = (int)ObjC.Send(self, ObjC.Sel("selectedRow")); grid._anchorColumn = grid._column; }
            grid.UpdateDetail(); grid.Emit(NativeGridIntentKind.Select);
        }
        catch { /* Never unwind into AppKit. */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ScrollWheel(nint self, nint selector, nint nativeEvent)
    {
        try
        {
            var superclass = new MacOnScreenCanvasNative.Super(self, ObjC.Class("NSTableView"));
            SendSuperEvent(ref superclass, selector, nativeEvent);
            if (!Instances.TryGetValue(self, out var grid)) return;
            var delta = MacOnScreenCanvasNative.SendDouble(nativeEvent, ObjC.Sel("scrollingDeltaY"));
            var clip = ObjC.Send(grid._scroll, ObjC.Sel("contentView"));
            var visible = MacOnScreenCanvasNative.GetRect(clip, ObjC.Sel("bounds"));
            var frame = MacOnScreenCanvasNative.GetRect(self, ObjC.Sel("frame"));
            var horizontal = MacOnScreenCanvasNative.SendDouble(nativeEvent, ObjC.Sel("scrollingDeltaX"));
            if (Math.Abs(horizontal) > Math.Abs(delta))
            {
                if (horizontal < 0 && visible.Origin.X + visible.Size.Width >= frame.Size.Width - 1) grid.RequestWindow(0, 1);
                if (horizontal > 0 && visible.Origin.X <= 1) grid.RequestWindow(0, -1);
            }
            else
            {
                if (delta < 0 && visible.Origin.Y + visible.Size.Height >= frame.Size.Height - 1) grid.RequestWindow(1, 0);
                if (delta > 0 && visible.Origin.Y <= 1) grid.RequestWindow(-1, 0);
            }
        }
        catch { /* Never unwind into AppKit. */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void KeyDown(nint self, nint selector, nint nativeEvent)
    {
        try
        {
            if (!Instances.TryGetValue(self, out var grid)) return;
            var key = ObjC.ManagedString(ObjC.Send(nativeEvent, ObjC.Sel("charactersIgnoringModifiers")));
            var flags = (nuint)ObjC.Send(nativeEvent, ObjC.Sel("modifierFlags"));
            var command = (flags & (1u << 20)) != 0;
            var option = (flags & (1u << 19)) != 0;
            var shift = (flags & (1u << 17)) != 0;
            if (key is "\uf700" or "\uf701" or "\uf702" or "\uf703")
            {
                if (!shift || grid._anchorRow < 0) { grid._anchorRow = (int)ObjC.Send(self, ObjC.Sel("selectedRow")); grid._anchorColumn = grid._column; }
                if (!shift) grid._anchorRow = -1;
            }
            if (command && key is "x" or "X" or "v" or "V") return;
            if (command && key is "a" or "A") { grid.SelectAllReady(); return; }
            if (key is "\r" or "\n") { grid.Emit(command ? NativeGridIntentKind.Replace : NativeGridIntentKind.Reveal); return; }
            if (command && key is "c" or "C") { grid.Emit(grid.IsRectangle ? NativeGridIntentKind.CopyTsv : NativeGridIntentKind.CopyValue); return; }
            if (key is "\uf72c" or "\uf72d")
            { grid.RequestWindow(key == "\uf72c" ? -1 : 1, 0); return; }
            if (key is "\uf702" or "\uf703")
            {
                if (option) grid.RequestWindow(0, key == "\uf702" ? -1 : 1);
                else
                {
                    grid._column = Math.Clamp(grid._column + (key == "\uf702" ? -1 : 1), 0, Math.Max(0, grid._ready.GetLength(1) - 1));
                    grid.UpdateDetail(); grid.Emit(NativeGridIntentKind.Select);
                }
                return;
            }
            var superclass = new MacOnScreenCanvasNative.Super(self, ObjC.Class("NSTableView"));
            SendSuperEvent(ref superclass, selector, nativeEvent);
        }
        catch { /* Never unwind into AppKit. */ }
    }

    /// <summary>Detaches callback targets before removing the managed lookup and owned handles.</summary>
    public void Dispose()
    {
        _identity = null;
        ObjC.Send(_table, ObjC.Sel("setDelegate:"), 0);
        ObjC.Send(_table, ObjC.Sel("setDataSource:"), 0);
        ObjC.Send(_table, ObjC.Sel("setTarget:"), 0);
        ObjC.Send(ObjC.Send(_table, ObjC.Sel("menu")), ObjC.Sel("setDelegate:"), 0);
        ObjC.Send(_table, ObjC.Sel("setMenu:"), 0);
        Instances.Remove(_table); Instances.Remove(_delegate);
        ObjC.Send(_delegate, ObjC.Sel("release"));
        ObjC.Send(_table, ObjC.Sel("release"));
        ObjC.Send(_scroll, ObjC.Sel("release"));
        ObjC.Send(_detail, ObjC.Sel("release"));
        ObjC.Send(View, ObjC.Sel("release"));
        _delegate = _table = _scroll = _detail = 0;
    }
}
