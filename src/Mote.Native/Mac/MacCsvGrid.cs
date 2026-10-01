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
internal sealed unsafe partial class MacCsvGrid : IDisposable, IGridAccessibilityActions
{
    /// <summary>Instances are removed before their delegate is released on the UI thread.</summary>
    private static readonly Dictionary<nint, MacCsvGrid> Instances = [];
    private const string TableClass = "MoteCsvGridTable";
    private const string ContainerClass = "MoteCsvGridContainer";
    private const string ScrollerClass = "MoteCsvGridScroller";
    private const string DelegateClass = "MoteCsvGridDelegate";
    private readonly Action<NativeGridIntent> _command;
    private readonly Action<NativeGridWindowRequest> _windowRequest;
    /// <summary>Navigation callbacks are additive; legacy acceptance constructors remain valid.</summary>
    private readonly Func<NativeGridGestureBegin, NativeGridGesture?>? _beginGesture;
    private readonly Action<NativeGridGestureAction>? _gestureAction;
    private readonly Action<int, int>? _geometryChanged;
    /// <summary>Physical source composition blocks accessibility focus transfer without committing preedit.</summary>
    private readonly Func<bool>? _isCompositionActive;
    private (int Rows, int Columns)? _measuredPage;
    private NativeGridScrollFrame? _navigation;
    private NativeGridGesture? _tracking;
    private bool _trackingActive;
    private NativeGridGestureBegin? _menuNavigation;
    private int _trackingRow, _trackingColumn;
    private nint _rowScroller, _columnScroller;
    private double _wheelRows, _wheelColumns;
    private GridRow?[] _slots = [];
    private GridRange _displayColumns;
    private GridRange? _installedColumns;
    private int _anchorRow = -1;
    private int _anchorColumn;
    private bool _windowPending;
    private NativeGridIntent? _menuSelection;
    private NativeGridIntent? _menuCell;
    /// <summary>Diagnostic readback of the cell intent frozen at menu opening, never a source action result.</summary>
    internal NativeGridIntent? ProbeMenuCell => _menuCell;
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
    /// <summary>Every replacement owns a serial; reentrant newer installs permanently supersede outer work.</summary>
    private long _installSerial;
    private IThemePolicy? _theme;
    /// <summary>Snapshot-free logical coordinates survive same-document source page changes.</summary>
    private (NativeDocumentStamp Document, int Row, int Column, int? AnchorRow, int AnchorColumn)? _selection;

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SendCell(nint receiver, nint selector, nint column, nint row, byte create);

    /// <summary>CGFloat return and NSInteger arguments are distinct ABI register classes.</summary>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern double ScrollerWidth(nint receiver, nint selector, nint controlSize, nint style);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSendSuper")]
    private static extern void SendSuperEvent(ref MacOnScreenCanvasNative.Super receiver,
        nint selector, nint nativeEvent);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSendSuper")]
    private static extern void SendSuperSize(ref MacOnScreenCanvasNative.Super receiver,
        nint selector, ObjC.Size oldSize);

    /// <summary>Creates one owned container; native rows and columns never exceed IR budgets.</summary>
    internal MacCsvGrid(Action<NativeGridIntent> command, Action<NativeGridWindowRequest> windowRequest,
        Func<NativeGridGestureBegin, NativeGridGesture?>? beginGesture = null,
        Action<NativeGridGestureAction>? gestureAction = null,
        Action<int, int>? geometryChanged = null, Func<bool>? isCompositionActive = null)
    {
        _command = command; _windowRequest = windowRequest;
        _beginGesture = beginGesture; _gestureAction = gestureAction; _geometryChanged = geometryChanged;
        _isCompositionActive = isCompositionActive;
        RegisterClasses();
        View = ObjC.Send(ObjC.Send(ObjC.Class(ContainerClass), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(0, 0, 390, 730));
        Instances.Add(View, this);
        ObjC.Send(View, ObjC.Sel("setAutoresizingMask:"), (nint)18);
        _scroll = ObjC.Send(ObjC.Send(ObjC.Class("NSScrollView"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(0, 80, 374, 650));
        ObjC.Send(_scroll, ObjC.Sel("setAutoresizingMask:"), (nint)18);
        ObjC.Send(_scroll, ObjC.Sel("setHasVerticalScroller:"), 0);
        ObjC.Send(_scroll, ObjC.Sel("setHasHorizontalScroller:"), 0);
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
        ObjC.Send(_table, ObjC.Sel("setIntercellSpacing:"), new ObjC.Size(2, 2));
        ObjC.Send(_table, ObjC.Sel("setTarget:"), _delegate);
        ObjC.Send(_table, ObjC.Sel("setDoubleAction:"), ObjC.Sel("moteReveal:"));
        ObjC.Send(_table, ObjC.Sel("setAccessibilityLabel:"), ObjC.String("Mote CSV grid"));
        InstallMenu();
        ObjC.Send(_scroll, ObjC.Sel("setDocumentView:"), _table);
        ObjC.Send(View, ObjC.Sel("addSubview:"), _scroll);
        _rowScroller = CreateScroller(new ObjC.Rect(374, 80, 16, 650), 17, "CSV indexed rows");
        _columnScroller = CreateScroller(new ObjC.Rect(0, 64, 374, 16), 2, "CSV known columns");
        var style = ObjC.Send(ObjC.Class("NSScroller"), ObjC.Sel("preferredScrollerStyle"));
        var width = ScrollerWidth(ObjC.Class("NSScroller"), ObjC.Sel("scrollerWidthForControlSize:scrollerStyle:"), 0, style);
        if (!double.IsFinite(width) || width <= 0) throw new InvalidOperationException("AppKit returned an invalid scroller width.");
        ObjC.Send(_rowScroller, ObjC.Sel("setFrame:"), new ObjC.Rect(390 - width, 64 + width, width, 666 - width));
        ObjC.Send(_columnScroller, ObjC.Sel("setFrame:"), new ObjC.Rect(0, 64, 390 - width, width));
        ObjC.Send(_scroll, ObjC.Sel("setFrame:"), new ObjC.Rect(0, 64 + width, 390 - width, 666 - width));
        _detail = ObjC.Send(ObjC.Send(ObjC.Class("NSTextField"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(8, 4, 374, 56));
        ObjC.Send(_detail, ObjC.Sel("setAutoresizingMask:"), (nint)2);
        ObjC.Send(_detail, ObjC.Sel("setEditable:"), 0);
        ObjC.Send(_detail, ObjC.Sel("setSelectable:"), 0);
        ObjC.Send(_detail, ObjC.Sel("setBezeled:"), 0);
        ObjC.Send(_detail, ObjC.Sel("setDrawsBackground:"), 0);
        ObjC.Send(_detail, ObjC.Sel("setAccessibilityLabel:"), ObjC.String("CSV selected cell detail"));
        ObjC.Send(View, ObjC.Sel("addSubview:"), _detail);
        InitializeAccessibility();
        StartPairDiagnostic();
    }

    /// <summary>Retained container used in the established preview split.</summary>
    internal nint View { get; }
    /// <summary>Target-only native acceptance handle; does not mutate table contents.</summary>
    internal nint Table => _table;
    /// <summary>Exact installed identity, cleared before replacement or source invalidation.</summary>
    internal NativePresentationId? Identity => _identity;

    /// <summary>Installs navigation independently of ready source-command authority.</summary>
    internal void SetNavigation(NativeGridScrollFrame? frame)
    {
        var serial = BeginInstallation();
        try
        {
            if (!ApplyNavigation(frame, serial)) return;
            if (frame?.Pending == true)
            {
                _identity = null; _projection = null; _anchorRow = -1;
                _displayColumns = AccessibilityColumns(frame, null, frame.RequestedColumns);
                _slots = NativeGridPlanner.Slots(null, frame.RequestedRows, frame.Rows.Count);
                _ready = PendingStrings(_slots.Length, _displayColumns.Count);
                if (!InstallColumns(_displayColumns, serial)) return;
                ObjC.Send(_table, ObjC.Sel("reloadData"));
                if (!Current(serial)) return;
                ObjC.Send(_table, ObjC.Sel("deselectAll:"), 0);
                if (!Current(serial)) return;
                var clip = ObjC.Send(_scroll, ObjC.Sel("contentView"));
                if (!Current(serial)) return;
                ObjC.Send(clip, ObjC.Sel("scrollToPoint:"), new ObjC.Point(0, 0));
                if (!Current(serial)) return;
            }
            if (frame is not null)
                ObjC.Send(_detail, ObjC.Sel("setStringValue:"), ObjC.String(frame.Status));
        }
        finally { FinishInstallation(serial); }
    }

    /// <summary>Invalidates outer installs before entering any AppKit or controller callback.</summary>
    private long BeginInstallation()
    {
        RetireAccessibility();
        _installing = true;
        return checked(++_installSerial);
    }

    private bool Current(long serial) => serial == _installSerial;

    /// <summary>A superseded outer finally must not alter the winning installation's guard.</summary>
    private void FinishInstallation(long serial)
    {
        if (Current(serial)) { _installing = false; PublishAccessibility(); }
    }

    private bool ApplyNavigation(NativeGridScrollFrame? frame, long serial)
    {
        if (_navigation?.Navigation != frame?.Navigation)
        {
            _wheelRows = _wheelColumns = 0;
            if (frame is null) _measuredPage = null;
            var retired = _tracking;
            _tracking = null;
            if (retired is { } old)
                _gestureAction?.Invoke(new(old.Id, NativeGridGesturePhase.Cancel, 0, 0));
            if (!Current(serial)) return false;
        }
        _navigation = frame;
        return InstallAxis(_rowScroller, frame?.Rows, "rows", frame?.Status, serial) &&
            InstallAxis(_columnScroller, frame?.Columns, "columns", frame?.Status, serial);
    }

    /// <summary>Pending cells are display placeholders only, with no GridRow or source origin.</summary>
    private static string[,] PendingStrings(int rows, int columns)
    {
        var text = new string[rows, columns];
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++) text[row, column] = "[pending]";
        return text;
    }

    /// <summary>Checks installation ownership after every native mutation that can synchronously reenter.</summary>
    private bool InstallColumns(GridRange range, long serial)
    {
        if (_installedColumns == range) return Current(serial);
        _installedColumns = null;
        var columns = ObjC.Send(_table, ObjC.Sel("tableColumns"));
        if (!Current(serial)) return false;
        while (ObjC.Send(columns, ObjC.Sel("count")) > 0)
        {
            if (!Current(serial)) return false;
            var column = ObjC.Send(columns, ObjC.Sel("objectAtIndex:"), 0);
            if (!Current(serial)) return false;
            ObjC.Send(_table, ObjC.Sel("removeTableColumn:"), column);
            if (!Current(serial)) return false;
        }
        for (var column = 0; column < range.Count; column++)
        {
            var actual = range.Start + column;
            var native = ObjC.Send(ObjC.Send(ObjC.Class("NSTableColumn"), ObjC.Sel("alloc")),
                ObjC.Sel("initWithIdentifier:"), ObjC.String(actual.ToString(CultureInfo.InvariantCulture)));
            try
            {
                if (!Current(serial)) return false;
                ObjC.Send(native, ObjC.Sel("setTitle:"), ObjC.String($"Column {(long)actual + 1}"));
                if (!Current(serial)) return false;
                ObjC.Send(native, ObjC.Sel("setWidth:"), 160d);
                if (!Current(serial)) return false;
                ObjC.Send(native, ObjC.Sel("setMinWidth:"), 64d);
                if (!Current(serial)) return false;
                ObjC.Send(_table, ObjC.Sel("addTableColumn:"), native);
                if (!Current(serial)) return false;
            }
            finally { ObjC.Send(native, ObjC.Sel("release")); }
            if (!Current(serial)) return false;
        }
        if (!Current(serial)) return false;
        _installedColumns = range;
        return true;
    }

    /// <summary>Owned scrollers use AppKit's preferred style, not NSScrollView's cache range.</summary>
    private nint CreateScroller(ObjC.Rect frame, nint autoresize, string label)
    {
        var scroller = ObjC.Send(ObjC.Send(ObjC.Class(ScrollerClass), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), frame);
        Instances.Add(scroller, this);
        ObjC.Send(scroller, ObjC.Sel("setAutoresizingMask:"), autoresize);
        ObjC.Send(scroller, ObjC.Sel("setScrollerStyle:"), ObjC.Send(ObjC.Class("NSScroller"), ObjC.Sel("preferredScrollerStyle")));
        ObjC.Send(scroller, ObjC.Sel("setTarget:"), _delegate);
        ObjC.Send(scroller, ObjC.Sel("setAction:"), ObjC.Sel("moteGridScroll:"));
        ObjC.Send(scroller, ObjC.Sel("setEnabled:"), 0);
        ObjC.Send(scroller, ObjC.Sel("setAccessibilityLabel:"), ObjC.String(label));
        ObjC.Send(View, ObjC.Sel("addSubview:"), scroller);
        return scroller;
    }

    /// <summary>Publishes certified domain proportions without interpreting disabled axes as exact empty files.</summary>
    private bool InstallAxis(nint scroller, NativeGridScrollAxis? axis, string name, string? status, long serial)
    {
        if (scroller == 0) return Current(serial);
        ObjC.Send(scroller, ObjC.Sel("setEnabled:"), axis is { Count: > 0 } ? 1 : 0);
        if (!Current(serial)) return false;
        // CGFloat and double setters use floating-point registers on both supported RIDs.
        ObjC.Send(scroller, ObjC.Sel("setKnobProportion:"), axis?.Proportion ?? 1d);
        if (!Current(serial)) return false;
        ObjC.Send(scroller, ObjC.Sel("setDoubleValue:"), axis?.Position ?? 0d);
        if (!Current(serial)) return false;
        var domain = axis?.Kind == NativeGridExtentKind.Exact ? $"File {name}" : $"Indexed {name}; file total unknown";
        ObjC.Send(scroller, ObjC.Sel("setAccessibilityLabel:"), ObjC.String(domain));
        if (!Current(serial)) return false;
        ObjC.Send(scroller, ObjC.Sel("setToolTip:"), ObjC.String(status ?? "Grid navigation unavailable"));
        return Current(serial);
    }

    /// <summary>Measures fully visible rows and resized columns, then freezes controller admission.</summary>
    private NativeGridGesture? BeginGesture()
    {
        if (_installing || _navigation is not { } frame || _beginGesture is null) return null;
        var page = MeasurePage();
        return _beginGesture(new(frame, page.Rows, page.Columns));
    }

    /// <summary>Measures fully visible logical slots using bounded local column widths.</summary>
    private (int Rows, int Columns) MeasurePage()
    {
        var clip = ObjC.Send(_scroll, ObjC.Sel("contentView"));
        var bounds = MacOnScreenCanvasNative.GetRect(clip, ObjC.Sel("bounds"));
        var pitch = Math.Max(1, MacOnScreenCanvasNative.SendDouble(_table, ObjC.Sel("rowHeight")) + 2);
        var rows = Math.Max(1, (int)Math.Floor(bounds.Size.Height / pitch));
        var nativeColumns = ObjC.Send(_table, ObjC.Sel("tableColumns"));
        var count = (int)ObjC.Send(nativeColumns, ObjC.Sel("count"));
        var columns = 0;
        var remaining = bounds.Size.Width;
        for (var i = 0; i < count; i++)
        {
            var column = ObjC.Send(nativeColumns, ObjC.Sel("objectAtIndex:"), i);
            var width = MacOnScreenCanvasNative.SendDouble(column, ObjC.Sel("width")) + 2;
            if (remaining < width) break;
            remaining -= width; columns++;
        }
        if (columns == count) columns += (int)Math.Max(0, Math.Floor(remaining / 162));
        return NativeGridPlanner.Page(rows, Math.Max(1, columns));
    }

    /// <summary>Publishes a changed measured page only after a coherent install; dedupe precedes reentrancy.</summary>
    private void PublishGeometry()
    {
        if (_installing || _table == 0 || _navigation is null || _geometryChanged is null) return;
        var page = MeasurePage();
        if (_measuredPage == page) return;
        _measuredPage = page;
        _geometryChanged(page.Rows, page.Columns);
    }

    /// <summary>One-shot actions retain fresh controller admission through terminal dispatch.</summary>
    private void NavigateCell(int row, int column, NativeGridTargetKind kind)
    {
        if (BeginGesture() is { } gesture)
            _gestureAction?.Invoke(new(gesture.Id, NativeGridGesturePhase.Commit, row, column, kind));
    }

    private void NavigateSymbolic(NativeGridTargetKind kind) => NavigateCell(0, 0, kind);

    /// <summary>Freezes one denominator before AppKit enters its synchronous knob tracking loop.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ScrollerMouseDown(nint self, nint selector, nint nativeEvent)
    {
        try
        {
            if (!Instances.TryGetValue(self, out var grid) || grid._trackingActive || grid.BeginGesture() is not { } gesture) return;
            grid._trackingActive = true;
            grid._tracking = gesture;
            grid._trackingRow = gesture.Frame.Rows.First; grid._trackingColumn = gesture.Frame.Columns.First;
            try
            {
                var superclass = new MacOnScreenCanvasNative.Super(self, ObjC.Class("NSScroller"));
                SendSuperEvent(ref superclass, selector, nativeEvent);
            }
            finally
            {
                // Clear before dispatch: a terminal callback may synchronously install another frame.
                var current = grid._tracking;
                grid._tracking = null;
                grid._trackingActive = false;
                if (current is not null)
                    grid._gestureAction?.Invoke(new(current.Id, NativeGridGesturePhase.Commit, grid._trackingRow, grid._trackingColumn));
            }
        }
        catch { /* Never unwind into AppKit. */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ScrollerAction(nint self, nint selector, nint sender)
    {
        try
        {
            if (!Instances.TryGetValue(self, out var grid)) return;
            var captured = grid._tracking;
            if (grid._trackingActive && captured is null) return; // Invalidated tracking never borrows a replacement token.
            var gesture = captured ?? grid.BeginGesture();
            if (gesture is null) return;
            var rows = sender == grid._rowScroller;
            var axis = rows ? gesture.Frame.Rows : gesture.Frame.Columns;
            var previous = rows ? grid._trackingRow : grid._trackingColumn;
            if (captured is null) previous = axis.First;
            var part = (int)ObjC.Send(sender, ObjC.Sel("hitPart"));
            var target = MacGridScrollInterop.Target(part, axis, previous,
                MacOnScreenCanvasNative.SendDouble(sender, ObjC.Sel("doubleValue")));
            if (target is null) return;
            var row = rows ? target.Value : captured is null ? gesture.Frame.Rows.First : grid._trackingRow;
            var column = rows ? captured is null ? gesture.Frame.Columns.First : grid._trackingColumn : target.Value;
            grid._trackingRow = row; grid._trackingColumn = column;
            grid._gestureAction?.Invoke(new(gesture.Id, captured is null ? NativeGridGesturePhase.Commit : NativeGridGesturePhase.Track, row, column));
        }
        catch { /* Never unwind into AppKit. */ }
    }

    /// <summary>Installs bounded display strings before allowing AppKit to ask for rows.</summary>
    internal void Install(GridRenderProjection projection, NativePresentationId identity, NativeGridScrollFrame? navigation = null)
    {
        var retainedAccessibilitySelection = AccessibilityEnabled ? _selection : null;
        var serial = BeginInstallation();
        try
        {
            var sameDocument = _identity?.Document == identity.Document;
            var selected = sameDocument && _projection is not null ? (int)ObjC.Send(_table, ObjC.Sel("selectedRow")) : -1;
            if (!Current(serial)) return;
            int? selectedOrdinal = selected >= 0 && selected < _slots.Length ? _slots[selected]?.Ordinal : null;
            int? anchorOrdinal = sameDocument && _anchorRow >= 0 && _anchorRow < _slots.Length ? _slots[_anchorRow]?.Ordinal : null;
            var selectedColumn = sameDocument ? _displayColumns.Start + _column : projection.RequestedColumns.Start;
            var anchorColumn = sameDocument ? _displayColumns.Start + _anchorColumn : selectedColumn;
            if (!sameDocument && _selection is { } retained && retained.Document == identity.Document)
            {
                selectedOrdinal = retained.Row; selectedColumn = retained.Column;
                anchorOrdinal = retained.AnchorRow; anchorColumn = retained.AnchorColumn;
            }
            _identity = null; _projection = null; _anchorRow = -1;
            if (!ApplyNavigation(navigation, serial)) return;
            if (projection.Version != identity.Document.Version) return;
            _displayColumns = AccessibilityColumns(navigation, projection, navigation?.RequestedColumns ?? projection.RequestedColumns);
            var requested = navigation?.RequestedRows ?? projection.RequestedRows;
            var known = navigation?.Rows.Count ?? projection.Extent.ExactRowCount ?? projection.Extent.CertifiedPrefixRows;
            _slots = NativeGridPlanner.Slots(projection, requested, known);
            _ready = PendingStrings(_slots.Length, _displayColumns.Count);
            for (var row = 0; row < _slots.Length; row++)
            {
                foreach (var cell in _slots[row]?.Cells ?? [])
                {
                    if (cell.Column < _displayColumns.Start || cell.Column >= _displayColumns.End) continue;
                    var text = projection.DisplayText.Substring(cell.DisplayRange.Start, cell.DisplayRange.Length);
                    var suffix = cell.State switch
                    {
                        GridValueState.Clipped => " [clipped]", GridValueState.Oversized => "[oversized; reveal source]",
                        GridValueState.Pending => "[pending]", GridValueState.Missing => "[missing]", _ => string.Empty
                    };
                    _ready[row, cell.Column - _displayColumns.Start] = text + suffix +
                        (cell.HasSyntaxError ? " [syntax error]" : string.Empty);
                }
            }
            _column = Math.Clamp(selectedColumn - _displayColumns.Start, 0, Math.Max(0, _displayColumns.Count - 1));
            _projection = projection;
            if (!InstallColumns(_displayColumns, serial)) return;
            ObjC.Send(_table, ObjC.Sel("reloadData"));
            if (!Current(serial)) return;
            ObjC.Send(_table, ObjC.Sel("deselectAll:"), 0);
            if (!Current(serial)) return;
            var clip = ObjC.Send(_scroll, ObjC.Sel("contentView"));
            if (!Current(serial)) return;
            ObjC.Send(clip, ObjC.Sel("scrollToPoint:"), new ObjC.Point(0, 0));
            if (!Current(serial)) return;
            var restoredRow = selectedOrdinal is { } ordinal && selectedColumn >= _displayColumns.Start && selectedColumn < _displayColumns.End
                ? FindLocalRow(ordinal) : -1;
            if (restoredRow >= 0) SelectRow(restoredRow, serial);
            else if (navigation is null && selectedOrdinal is null && _slots.Length > 0 && _slots[0] is not null)
                SelectRow(0, serial); // Preserve standalone legacy initial-install behavior only.
            if (!Current(serial)) return;
            _identity = identity; _windowPending = false;
            if (restoredRow >= 0 && anchorOrdinal is { } anchor &&
                anchorColumn >= _displayColumns.Start && anchorColumn < _displayColumns.End)
            { _anchorRow = FindLocalRow(anchor); _anchorColumn = anchorColumn - _displayColumns.Start; }
            UpdateDetail(serial);
            if (Current(serial) && retainedAccessibilitySelection is { } retainedSelection && retainedSelection.Document == identity.Document)
            {
                _selection = retainedSelection;
                RefreshSelectionColors(serial);
            }
        }
        finally { FinishInstallation(serial); }
        if (Current(serial)) PublishGeometry();
    }

    /// <summary>Invalidates commands before native reload; newer nested installations always win.</summary>
    internal void Clear(bool preserveSelection = false)
    {
        var serial = BeginInstallation();
        try
        {
            if (!preserveSelection) _selection = null;
            _identity = null; _projection = null; _anchorRow = -1; _slots = []; _ready = new string[0, 0];
            if (_table != 0) ObjC.Send(_table, ObjC.Sel("reloadData"));
            if (!Current(serial)) return;
            if (_detail != 0) ObjC.Send(_detail, ObjC.Sel("setStringValue:"), ObjC.String(string.Empty));
        }
        finally { FinishInstallation(serial); }
    }

    /// <summary>Recolors read-only table and detail without source or Undo changes.</summary>
    internal void SetTheme(IThemePolicy theme)
    {
        RetireAccessibility();
        _theme = theme;
        ObjC.Send(_table, ObjC.Sel("setBackgroundColor:"), Color(theme.Palette.PreviewBackground));
        ObjC.Send(_detail, ObjC.Sel("setTextColor:"), Color(theme.Palette.PreviewForeground));
        ObjC.Send(_detail, ObjC.Sel("setFont:"), ObjC.Send(ObjC.Class("NSFont"),
            ObjC.Sel("systemFontOfSize:"), theme.Typography.UiFontSize));
        ObjC.Send(_table, ObjC.Sel("reloadData"));
        PublishAccessibility();
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
        if (AccessibilityEnabled && kind is NativeGridIntentKind.Reveal or NativeGridIntentKind.Replace &&
            _accessibilityFrame?.FocusedCell is { } focused)
            return CaptureFocusedIntent(projection, identity, kind, focused);
        var row = (int)ObjC.Send(_table, ObjC.Sel("selectedRow"));
        if (row < 0 || row >= _slots.Length || _column >= _displayColumns.Count) return null;
        var anchorRow = _anchorRow >= 0 && _anchorRow < _slots.Length ? _anchorRow : row;
        var rectangle = kind is not NativeGridIntentKind.Reveal and not NativeGridIntentKind.Replace and not NativeGridIntentKind.CopyValue and not NativeGridIntentKind.CopySource &&
            _anchorRow >= 0 && (anchorRow != row || _anchorColumn != _column);
        if (_slots[row] is not { } selected || _slots[anchorRow] is not { } anchor) return null;
        var firstRow = rectangle ? Math.Min(anchorRow, row) : row;
        var lastRow = rectangle ? Math.Max(anchorRow, row) : row;
        var firstColumn = _displayColumns.Start + (rectangle ? Math.Min(_anchorColumn, _column) : _column);
        var lastColumn = _displayColumns.Start + (rectangle ? Math.Max(_anchorColumn, _column) : _column);
        for (var slot = firstRow; slot <= lastRow; slot++)
        {
            if (_slots[slot] is not { } readyRow) return null;
            for (var column = firstColumn; column <= lastColumn; column++)
                if (NativeCsvGrid.Cell(readyRow, column) is not { State: not GridValueState.Pending }) return null;
        }
        return new NativeGridIntent(identity, kind, rectangle ? anchor.Ordinal : selected.Ordinal,
            _displayColumns.Start + (rectangle ? _anchorColumn : _column),
            rectangle ? selected.Ordinal : null,
            rectangle ? _displayColumns.Start + _column : null,
            kind == NativeGridIntentKind.CopyRows);
    }

    /// <summary>Captures a native keyboard/menu coordinate intent; the controller alone authorizes source actions.</summary>
    /// <remarks>Missing is an admitted descriptor, not Pending. Preserve legacy dispatch so source policy can refuse it.</remarks>
    internal static NativeGridIntent? CaptureFocusedIntent(GridRenderProjection projection,
        NativePresentationId identity, NativeGridIntentKind kind, GridCoordinate focused)
    {
        var cell = NativeCsvGrid.Row(projection, focused.Row) is { } row
            ? NativeCsvGrid.Cell(row, focused.Column) : null;
        return cell is { State: not GridValueState.Pending }
            ? new NativeGridIntent(identity, kind, focused.Row, focused.Column) : null;
    }

    private int FindLocalRow(int ordinal)
    {
        for (var row = 0; row < _slots.Length; row++) if (_slots[row]?.Ordinal == ordinal) return row;
        return -1;
    }

    /// <summary>Select All means only this certified bounded window, not unindexed rows.</summary>
    internal void SelectAllReady()
    {
        if (_projection is not { } grid || _slots.Length == 0 || _slots.Any(slot => slot is null) || _displayColumns.Count == 0) return;
        _anchorRow = 0; _anchorColumn = 0; _column = _displayColumns.Count - 1;
        SelectRow(_slots.Length - 1); UpdateDetail(); Emit(NativeGridIntentKind.Select);
    }

    private bool IsRectangle => _anchorRow >= 0 &&
        (_anchorRow != (int)ObjC.Send(_table, ObjC.Sel("selectedRow")) || _anchorColumn != _column);

    /// <summary>Requests adjacent bounded coordinates; repeated momentum events are coalesced.</summary>
    private void RequestWindow(int rowDirection, int columnDirection)
    {
        if (_navigation is not null && _beginGesture is not null)
        {
            var gesture = BeginGesture();
            if (gesture is null) return;
            var frame = gesture.Frame;
            var targetRow = (int)Math.Clamp((long)frame.Rows.First + rowDirection * (long)frame.Rows.Page, 0, int.MaxValue - 1L);
            var targetColumn = (int)Math.Clamp((long)frame.Columns.First + columnDirection * (long)frame.Columns.Page, 0, int.MaxValue - 1L);
            _gestureAction?.Invoke(new(gesture.Id, NativeGridGesturePhase.Commit, targetRow, targetColumn));
            return;
        }
        if (_windowPending || _identity is not { } identity || _projection is not { } projection) return;
        var first = projection.Rows.Count > 0 ? projection.Rows[0].Ordinal : projection.RequestedRows.Start;
        var row = rowDirection == 0 ? first : (int)Math.Clamp((long)first + rowDirection * Math.Max(1, projection.RequestedRows.Count), 0, int.MaxValue - 1L);
        if (projection.Extent.ExactRowCount is { } count && row >= count) return;
        var column = (int)Math.Clamp((long)_displayColumns.Start + columnDirection * _displayColumns.Count, 0, int.MaxValue - 1L);
        if (projection.Extent.ExactMaxWidth is { } width && column >= width) return;
        if (row == first && column == _displayColumns.Start) return;
        _windowPending = true;
        _windowRequest(new NativeGridWindowRequest(identity, row,
            new GridRange(column, Math.Min(_displayColumns.Count, int.MaxValue - column)),
            RowLimit: Math.Min(64, int.MaxValue - row)));
    }

    /// <summary>Rebases logical viewport instead of letting NSTableView auto-scroll a hidden cache range.</summary>
    private void MoveLogicalSelection(int delta)
    {
        if (_navigation is not { } frame) return;
        var serial = _installSerial;
        var local = (int)ObjC.Send(_table, ObjC.Sel("selectedRow"));
        if (!Current(serial)) return;
        var ordinal = local >= 0 ? (long)frame.RequestedRows.Start + local : frame.Rows.First;
        var target = (int)Math.Clamp(ordinal + delta, 0, int.MaxValue - 1L);
        if (frame.Rows.Kind == NativeGridExtentKind.Exact && target >= frame.Rows.Count) return;
        var slot = FindLocalRow(target);
        if (slot >= 0)
        {
            SelectRow(slot, serial);
            if (!Current(serial)) return;
            UpdateDetail(serial);
            if (!Current(serial)) return;
            Emit(NativeGridIntentKind.Select);
            if (!Current(serial)) return;
            if (target >= frame.Rows.First && (long)target < (long)frame.Rows.First + frame.Rows.Page) return;
        }
        else if (_identity is { } ready)
            _selection = (ready.Document, target, _displayColumns.Start + _column, null, 0);
        var first = target < frame.Rows.First ? target : Math.Max(0, target - frame.Rows.Page + 1);
        NavigateCell(first, frame.Columns.First, NativeGridTargetKind.Viewport);
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
                ("Go to row:column…", "moteGridGoToCell:"),
                ("Go to end of file", "moteGridEnd:"),
                ("Retry file indexing", "moteGridRetry:")
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

    private void SelectRow(int row, long? installation = null)
    {
        var indexes = ObjC.Send(ObjC.Class("NSIndexSet"), ObjC.Sel("indexSetWithIndex:"), row);
        if (installation is { } serial && !Current(serial)) return;
        ObjC.Send(_table, ObjC.Sel("selectRowIndexes:byExtendingSelection:"), indexes, (byte)0);
    }

    private void UpdateDetail(long? installation = null)
    {
        if (!_installing)
        {
            _accessibilityFocusCell = null;
            _accessibilityTableFocus = false;
        }
        if (_projection is not { } projection) { RefreshSelectionColors(installation); return; }
        var row = (int)ObjC.Send(_table, ObjC.Sel("selectedRow"));
        if (installation is { } serial && !Current(serial)) return;
        if (_identity is { } identity && row >= 0 && row < _slots.Length && _slots[row] is not null)
            _selection = (identity.Document, _slots[row]!.Ordinal, _displayColumns.Start + _column,
                _anchorRow >= 0 && _anchorRow < _slots.Length ? _slots[_anchorRow]?.Ordinal : null,
                _displayColumns.Start + _anchorColumn);
        var text = row >= 0 && row < _slots.Length && _column < _ready.GetLength(1)
            ? $"Row {(long)(_navigation?.RequestedRows.Start ?? projection.RequestedRows.Start) + row + 1}, Column {_displayColumns.Start + _column + 1}: {_ready[row, _column]}"
            : $"No certified rows ready. {projection.Completeness}";
        ObjC.Send(_detail, ObjC.Sel("setStringValue:"), ObjC.String(text +
            "\nShift: rectangle · Return: source · ⌘C: copy · ⌘Return: replace · Page ↑/↓: rows · ⌥←/→: columns"));
        if (installation is { } current && !Current(current)) return;
        RefreshSelectionColors(installation);
        if (!_installing) PublishAccessibility();
    }

    private nint CellView(nint column, int row)
    {
        if (row < 0 || row >= _slots.Length) return 0;
        var firstRow = _navigation?.RequestedRows.Start ?? _projection?.RequestedRows.Start ?? 0;
        var identifier = ObjC.Send(column, ObjC.Sel("identifier"));
        if (!int.TryParse(ObjC.ManagedString(identifier), NumberStyles.None, CultureInfo.InvariantCulture, out var actual)) return 0;
        var index = actual - _displayColumns.Start;
        if (index < 0 || index >= _displayColumns.Count) return 0;
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
        var descriptor = _slots[row] is { } readyRow ? NativeCsvGrid.Cell(readyRow, actual) : null;
        ObjC.Send(view, ObjC.Sel("setAccessibilityLabel:"), ObjC.String(
            $"Row {(long)firstRow + row + 1}, Column {actual + 1}, {descriptor?.State.ToString() ?? "Pending"}: {_ready[row, index]}"));
        ApplyCellColors(view, row, index);
        ObjC.Send(view, ObjC.Sel("setFont:"), ObjC.Send(ObjC.Class("NSFont"),
            ObjC.Sel("systemFontOfSize:"), _theme?.Typography.UiFontSize ?? 12d));
        ObjC.Send(ObjC.Send(view, ObjC.Sel("cell")), ObjC.Sel("setLineBreakMode:"), 4);
        return view;
    }

    /// <summary>Paints exactly the logical rectangle, not a misleading full-row selection.</summary>
    private void RefreshSelectionColors(long? installation = null)
    {
        if (_table == 0) return;
        for (var row = 0; row < _slots.Length; row++)
            for (var column = 0; column < _displayColumns.Count; column++)
            {
                if (installation is { } serial && !Current(serial)) return;
                var view = SendCell(_table, ObjC.Sel("viewAtColumn:row:makeIfNecessary:"), column, row, 0);
                if (installation is { } current && !Current(current)) return;
                if (view != 0) ApplyCellColors(view, row, column, installation);
            }
    }

    private void ApplyCellColors(nint view, int row, int column, long? installation = null)
    {
        var currentRow = (int)ObjC.Send(_table, ObjC.Sel("selectedRow"));
        if (installation is { } serial && !Current(serial)) return;
        var anchor = _anchorRow >= 0 ? _anchorRow : currentRow;
        var anchorColumn = _anchorRow >= 0 ? _anchorColumn : _column;
        var selected = currentRow >= 0 && row >= Math.Min(anchor, currentRow) && row <= Math.Max(anchor, currentRow) &&
            column >= Math.Min(anchorColumn, _column) && column <= Math.Max(anchorColumn, _column);
        if (AccessibilityEnabled && _selection is { } retained &&
            retained.Document == (_navigation?.Navigation.Document ?? _identity?.Document))
        {
            var absoluteRow = (_navigation?.RequestedRows.Start ?? _projection?.RequestedRows.Start ?? 0) + row;
            var absoluteColumn = _displayColumns.Start + column;
            selected = absoluteRow >= Math.Min(retained.AnchorRow ?? retained.Row, retained.Row) &&
                absoluteRow <= Math.Max(retained.AnchorRow ?? retained.Row, retained.Row) &&
                absoluteColumn >= Math.Min(retained.AnchorRow is null ? retained.Column : retained.AnchorColumn, retained.Column) &&
                absoluteColumn <= Math.Max(retained.AnchorRow is null ? retained.Column : retained.AnchorColumn, retained.Column);
        }
        ObjC.Send(view, ObjC.Sel("setDrawsBackground:"), selected ? 1 : 0);
        if (installation is { } current && !Current(current)) return;
        ObjC.Send(view, ObjC.Sel("setBackgroundColor:"), Color(_theme?.Palette.SelectionBackground ?? new ThemeColor(53,90,133)));
        if (installation is { } latest && !Current(latest)) return;
        ObjC.Send(view, ObjC.Sel("setTextColor:"), Color(selected
            ? _theme?.Palette.SelectionForeground ?? new ThemeColor(255,255,255)
            : _theme?.Palette.PreviewForeground ?? new ThemeColor(225,227,231)));
    }

    private static nint Color(ThemeColor color) => ObjC.Send(ObjC.Class("NSColor"),
        ObjC.Sel("colorWithSRGBRed:green:blue:alpha:"), color.Red / 255d, color.Green / 255d, color.Blue / 255d, 1d);

    private static void RegisterClasses()
    {
        var cls = ObjC.AllocateClassPair(ObjC.Class("NSView"), ContainerClass, 0);
        if (cls != 0)
        {
            Add(cls, "resizeSubviewsWithOldSize:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, ObjC.Size, void>)&ContainerResized, "v@:{CGSize=dd}");
            RegisterContainerAccessibility(cls);
            ObjC.RegisterClassPair(cls);
        }
        cls = ObjC.AllocateClassPair(ObjC.Class("NSScroller"), ScrollerClass, 0);
        if (cls != 0)
        {
            Add(cls, "mouseDown:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&ScrollerMouseDown, "v@:@");
            ObjC.RegisterClassPair(cls);
        }
        cls = ObjC.AllocateClassPair(ObjC.Class("NSTableView"), TableClass, 0);
        if (cls != 0)
        {
            Add(cls, "pasteAsPlainText:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&ReadOnlyCommand, "v@:@");
            Add(cls, "paste:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&ReadOnlyCommand, "v@:@");
            Add(cls, "cut:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&ReadOnlyCommand, "v@:@");
            Add(cls, "keyDown:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&KeyDown, "v@:@");
            Add(cls, "scrollWheel:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&ScrollWheel, "v@:@");
            Add(cls, "mouseDown:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MouseDown, "v@:@");
            RegisterTableAccessibility(cls);
            ObjC.RegisterClassPair(cls);
        }
        cls = ObjC.AllocateClassPair(ObjC.Class("NSObject"), DelegateClass, 0);
        if (cls == 0) return;
        Add(cls, "numberOfRowsInTableView:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint>)&RowCount, "q@:@");
        Add(cls, "tableView:viewForTableColumn:row:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, nint>)&ViewForCell, "@@:@@q");
        Add(cls, "tableViewColumnDidResize:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&ColumnsResized, "v@:@");
        Add(cls, "tableViewSelectionDidChange:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&SelectionChanged, "v@:@");
        Add(cls, "moteGridFollowSource:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MenuFollowSource, "v@:@");
        Add(cls, "moteGridEnd:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MenuEnd, "v@:@");
        Add(cls, "moteGridRetry:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MenuRetry, "v@:@");
        Add(cls, "moteGridGoToCell:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MenuGoToCell, "v@:@");
        Add(cls, "menuWillOpen:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MenuWillOpen, "v@:@");
        Add(cls, "menuDidClose:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MenuDidClose, "v@:@");
        if (AccessibilityEnabled)
            Add(cls, "moteGridShowMenu:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&AccessibilityPresentMenu, "v@:@");
        if (PairObservation is not null)
        {
            Add(cls, "moteGridPairTick:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&PairTick, "v@:@");
            Add(cls, "moteGridPairClose:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&PairClose, "v@:@");
        }
        Add(cls, "moteGridCommand:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MenuCommand, "v@:@");
        Add(cls, "moteGridScroll:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&ScrollerAction, "v@:@");
        Add(cls, "moteReveal:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Reveal, "v@:@");
        ObjC.RegisterClassPair(cls);
    }

    private static void Add(nint cls, string selector, nint implementation, string encoding)
    {
        if (!ObjC.AddMethod(cls, ObjC.Sel(selector), implementation, encoding))
            throw new InvalidOperationException($"Could not register CSV table selector {selector}.");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ContainerResized(nint self, nint selector, ObjC.Size oldSize)
    {
        try
        {
            var superclass = new MacOnScreenCanvasNative.Super(self, ObjC.Class("NSView"));
            SendSuperSize(ref superclass, selector, oldSize);
            if (Instances.TryGetValue(self, out var grid)) grid.PublishGeometry();
        }
        catch { /* Never unwind into AppKit. */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ColumnsResized(nint self, nint selector, nint notification)
    { try { if (Instances.TryGetValue(self, out var grid)) grid.PublishGeometry(); } catch { } }

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
            grid.AccessibilityMenuTransition(menu, true);
            grid.TraceMenu(MacCsvGridMenuPhase.WillOpen);
            var page = grid.MeasurePage();
            grid._menuNavigation = grid._navigation is { } frame ? new(frame, page.Rows, page.Columns) : null;
            grid._menuSelection = grid.CaptureIntent(NativeGridIntentKind.Select);
            grid._menuCell = grid.CaptureIntent(NativeGridIntentKind.Reveal);
            grid._menuWindow = grid._identity is { } identity && grid._projection is { } projection
                ? new NativeGridWindowRequest(identity, projection.RequestedRows.Start, grid._displayColumns) : null;
            grid._menuExtent = grid._projection?.Extent ?? default;
        } } catch { } }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MenuDidClose(nint self, nint selector, nint menu)
    {
        if (!AccessibilityEnabled) return;
        try
        {
            if (AccessibilityMainThread && Instances.TryGetValue(self, out var grid))
            {
                grid.AccessibilityMenuTransition(menu, false);
                grid.TraceMenu(MacCsvGridMenuPhase.DidClose);
            }
        }
        catch { /* Diagnostics cannot unwind into AppKit. */ }
    }

    /// <summary>Observes only existing native state; normal callbacks perform no diagnostic allocation or output.</summary>
    private void TraceMenu(MacCsvGridMenuPhase phase, bool? result = null)
    {
        if (MenuDiagnostic is null) return;
        try
        {
            if (!AccessibilityMainThread || !MenuDiagnostic.TryNext(phase, out var trace)) return;
            var menu = ObjC.Send(_table, ObjC.Sel("menu"));
            var count = ObjC.Send(menu, ObjC.Sel("numberOfItems"));
            var items = count is >= 0 and <= MacCsvGridMenuDiagnostic.Limit ? (int)count : -1;
            var coordinate = false;
            for (var i = 0; i < items; i++)
            {
                var item = ObjC.Send(menu, ObjC.Sel("itemAtIndex:"), i);
                if (ObjC.ManagedString(ObjC.Send(item, ObjC.Sel("title"))) == "Go to row:column…") coordinate = true;
            }
            var window = ObjC.Send(_table, ObjC.Sel("window"));
            var facts = new MacCsvGridMenuFacts(menu != 0, items, coordinate,
                ObjC.Send(_table, ObjC.Sel("accessibilityShownMenu")) != 0,
                AccessibilityNativeBool(window, ObjC.Sel("isKeyWindow")) != 0,
                _table != 0 && ObjC.Send(window, ObjC.Sel("firstResponder")) == _table,
                AccessibilityNativeBool(ObjC.Send(ObjC.Class("NSApplication"), ObjC.Sel("sharedApplication")), ObjC.Sel("isActive")) != 0,
                AccessibilitySelectorPermission(_accessibilityTable, ObjC.Sel("isAccessibilitySelectorAllowed:"),
                    ObjC.Sel("accessibilityPerformShowMenu")) != 0,
                AccessibilitySelectorPermission(_accessibilityTable, ObjC.Sel("isAccessibilitySelectorAllowed:"),
                    ObjC.Sel("accessibilityShownMenu")) != 0);
            Console.WriteLine(trace.Format(facts, result));
        }
        catch { /* Diagnostic faults must not alter native menu behavior or escape its delegate. */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MenuEnd(nint self, nint selector, nint item) => MenuNavigation(self, NativeGridTargetKind.End);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MenuRetry(nint self, nint selector, nint item) => MenuNavigation(self, NativeGridTargetKind.Retry);

    private static void MenuNavigation(nint self, NativeGridTargetKind kind)
    {
        try
        {
            if (Instances.TryGetValue(self, out var grid) && grid._menuNavigation is { } opening &&
                grid._beginGesture?.Invoke(opening) is { } gesture)
                grid._gestureAction?.Invoke(new(gesture.Id, NativeGridGesturePhase.Commit, 0, 0, kind));
        }
        catch { /* Never unwind into AppKit. */ }
    }

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
            if (!Instances.TryGetValue(self, out var grid)) return;
            if (grid._menuNavigation is { } openingNavigation)
            {
                var captured = grid._beginGesture?.Invoke(openingNavigation);
                if (captured is null) return;
                var enteredCoordinates = MacGridNavigation.Prompt("1:1");
                if (enteredCoordinates is null) { grid._gestureAction?.Invoke(new(captured.Id, NativeGridGesturePhase.Cancel, 0, 0)); return; }
                var parts = enteredCoordinates.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var row) &&
                    int.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var column) && row > 0 && column > 0)
                    grid._gestureAction?.Invoke(new(captured.Id, NativeGridGesturePhase.Commit, row - 1, column - 1, NativeGridTargetKind.Cell));
                else grid._gestureAction?.Invoke(new(captured.Id, NativeGridGesturePhase.Cancel, 0, 0));
                return;
            }
            if (grid._menuWindow is not { } opening) return;
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
            if (!Instances.TryGetValue(self, out var grid)) return;
            var gesture = grid.BeginGesture();
            if (gesture is null) return;
            var precise = ObjC.Send(nativeEvent, ObjC.Sel("hasPreciseScrollingDeltas")) != 0;
            var rowPitch = Math.Max(1, MacOnScreenCanvasNative.SendDouble(self, ObjC.Sel("rowHeight")) + 2);
            var dy = MacOnScreenCanvasNative.SendDouble(nativeEvent, ObjC.Sel("scrollingDeltaY"));
            var dx = MacOnScreenCanvasNative.SendDouble(nativeEvent, ObjC.Sel("scrollingDeltaX"));
            if (!double.IsFinite(dx) || !double.IsFinite(dy))
            { grid._gestureAction?.Invoke(new(gesture.Id, NativeGridGesturePhase.Cancel, 0, 0)); return; }
            grid._wheelRows -= dy / (precise ? rowPitch : 1);
            grid._wheelColumns -= dx / (precise ? 160 : 1);
            var rows = (long)Math.Clamp(Math.Truncate(grid._wheelRows), -int.MaxValue, int.MaxValue);
            var columns = (long)Math.Clamp(Math.Truncate(grid._wheelColumns), -int.MaxValue, int.MaxValue);
            grid._wheelRows -= rows; grid._wheelColumns -= columns;
            var frame = gesture.Frame;
            var row = (int)Math.Clamp((long)frame.Rows.First + rows, 0, int.MaxValue - 1L);
            var column = (int)Math.Clamp((long)frame.Columns.First + columns, 0, int.MaxValue - 1L);
            grid._gestureAction?.Invoke(new(gesture.Id, NativeGridGesturePhase.Commit, row, column));
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
                if (!grid.PrepareAccessibilityKeyboardNavigation()) return;
                if (!shift || grid._anchorRow < 0) { grid._anchorRow = (int)ObjC.Send(self, ObjC.Sel("selectedRow")); grid._anchorColumn = grid._column; }
                if (!shift) grid._anchorRow = -1;
            }
            if (command && key is "x" or "X" or "v" or "V") return;
            if (command && key is "a" or "A") { grid.SelectAllReady(); return; }
            if (key is "\r" or "\n") { grid.Emit(command ? NativeGridIntentKind.Replace : NativeGridIntentKind.Reveal); return; }
            if (command && key is "c" or "C") { grid.Emit(grid.IsRectangle ? NativeGridIntentKind.CopyTsv : NativeGridIntentKind.CopyValue); return; }
            if (key is "\uf700" or "\uf701" && grid._navigation is not null)
            { grid.MoveLogicalSelection(key == "\uf700" ? -1 : 1); return; }
            if (key == "\uf72b") { grid.NavigateSymbolic(NativeGridTargetKind.End); return; }
            if (key == "\uf729") { grid.NavigateCell(0, 0, NativeGridTargetKind.Viewport); return; }
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
        StopPairDiagnostic();
        CancelPairClose();
        CancelAccessibilityMenu();
        SetNavigation(null);
        RetireAccessibility();
        DetachAccessibilityTable();
        ExportPairDiagnostic();
        _identity = null;
        foreach (var scroller in new[] { _rowScroller, _columnScroller })
        { ObjC.Send(scroller, ObjC.Sel("setTarget:"), 0); Instances.Remove(scroller); ObjC.Send(scroller, ObjC.Sel("release")); }
        ObjC.Send(_table, ObjC.Sel("setDelegate:"), 0);
        ObjC.Send(_table, ObjC.Sel("setDataSource:"), 0);
        ObjC.Send(_table, ObjC.Sel("setTarget:"), 0);
        var menu = ObjC.Send(_table, ObjC.Sel("menu"));
        ObjC.Send(menu, ObjC.Sel("setDelegate:"), 0);
        DetachAccessibilityMenuCommands(menu);
        ObjC.Send(_table, ObjC.Sel("setMenu:"), 0);
        Instances.Remove(View); Instances.Remove(_table); Instances.Remove(_delegate);
        ObjC.Send(_delegate, ObjC.Sel("release"));
        ObjC.Send(_table, ObjC.Sel("release"));
        ObjC.Send(_scroll, ObjC.Sel("release"));
        ObjC.Send(_detail, ObjC.Sel("release"));
        ObjC.Send(View, ObjC.Sel("release"));
        _delegate = _table = _scroll = _detail = 0;
    }
}
