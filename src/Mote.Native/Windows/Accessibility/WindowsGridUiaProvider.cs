using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace Mote.Native.Windows.Accessibility;

/// <summary>One bounded UIA registration, independently retired from the source provider.</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsGridUiaBridge
{
    private readonly object _gate = new();
    private GridAccessibilityFrame? _frame;
    private IGridAccessibilityActions? _actions;
    private Func<int, int, int, UiaRect>? _bounds;
    private WindowsGridUiaNode? _root;
    private readonly Dictionary<(int Kind, int Row, int Column), WindowsGridUiaNode> _nodes = [];
    private nint _hwnd, _header;
    private readonly WindowsGridUiaGroup? _status;
    private UiaRect[] _geometry = [];
    /// <summary>Creates a native-backed geometry and adapter-owned action bridge.</summary>
    internal WindowsGridUiaBridge(nint hwnd, IGridAccessibilityActions actions, Func<int, int, int, UiaRect> bounds, WindowsGridUiaGroup? status = null)
    { _hwnd = hwnd; _actions = actions; _bounds = bounds; _status = status; }
    /// <summary>Identifies the native header host whose duplicate semantic subtree is overridden.</summary>
    internal void BindHeader(nint header) => _header = header;
    /// <summary>Only the native header HWND is an owned descendant override.</summary>
    internal bool IsHeader(nint hwnd) => hwnd != 0 && hwnd == _header;
    /// <summary>Links Table orientation to the actual group status HWND provider.</summary>
    internal int Description(out UiaVariant value)
    {
        value = default;
        if (_status is null) return 0;
        var array = SafeArrayCreateVector(13, 0, 1);
        if (array == 0) return unchecked((int)0x8007000E);
        var pointer = UiaComInterface.Pointer(_status, typeof(IRawElementProviderSimpleAbi).GUID);
        int result; var index = 0;
        try { result = SafeArrayPutElement(array, ref index, pointer); }
        finally { Marshal.Release(pointer); }
        if (result < 0) { SafeArrayDestroy(array); return result; }
        value.Type = 0x200D; value.Pointer = array; return 0;
    }
    /// <summary>Publishes one coherent installation, discarding wrappers on identity replacement.</summary>
    internal void Publish(GridAccessibilityFrame frame, bool geometryChanged = false, Func<bool>? isCurrent = null, bool raiseEvents = true)
    {
        var previous = _frame;
        var replaced = previous?.Id != frame.Id;
        var geometry = _geometry;
        var captureBounds = _bounds;
        if (captureBounds is null) return;
        if (replaced || geometryChanged)
        {
            var rectangles = new UiaRect[1 + frame.Rows.Count + frame.Columns.Count + frame.Rows.Count * frame.Columns.Count];
            rectangles[0] = captureBounds(0, 0, 0);
            for (var row = 0; row < frame.Rows.Count; row++) rectangles[1 + row] = captureBounds(2, row, 0);
            for (var column = 0; column < frame.Columns.Count; column++) rectangles[1 + frame.Rows.Count + column] = captureBounds(3, 0, column);
            for (var row = 0; row < frame.Rows.Count; row++)
                for (var column = 0; column < frame.Columns.Count; column++)
                    rectangles[1 + frame.Rows.Count + frame.Columns.Count + row * frame.Columns.Count + column] = captureBounds(1, row, column);
            geometry = rectangles;
        }
        if (isCurrent is not null && !isCurrent()) return;
        lock (_gate)
        {
            if (isCurrent is not null && !isCurrent()) return;
            _geometry = geometry;
            Volatile.Write(ref _frame, frame);
            if (replaced) _nodes.Clear();
            _root ??= new(this, frame.Id, 0, 0, 0);
        }
        if (!raiseEvents || _root is null) return;
        var pointer = UiaComInterface.Pointer(_root, typeof(IRawElementProviderSimpleAbi).GUID);
        try
        {
            if (replaced) UiaRaiseStructureChangedEvent(pointer, 2, 0, 0); // ChildrenInvalidated.
            else if (previous?.Selection != frame.Selection) UiaRaiseAutomationEvent(pointer, 20013);
            if (!ReferenceEquals(Volatile.Read(ref _frame), frame)) return;
            if (frame.HasTableFocus && (previous?.FocusedCell != frame.FocusedCell || previous?.HasTableFocus != true))
            {
                var focused = frame.FocusedCell is { } cell && frame.Contains(cell)
                    ? Node(frame.Id, 1, cell.Row - frame.Rows.Start, cell.Column - frame.Columns.Start) : _root;
                var focusPointer = UiaComInterface.Pointer(focused, typeof(IRawElementProviderSimpleAbi).GUID);
                try { UiaRaiseAutomationEvent(focusPointer, 20005); }
                finally { Marshal.Release(focusPointer); }
            }
        }
        finally { Marshal.Release(pointer); }
    }
    /// <summary>Raises selection invalidation only after the callback-free transaction has completed.</summary>
    internal void NotifySelection(GridAccessibilityId id)
    {
        var root = Node(id, 0, 0, 0);
        if (Frame(id) is null) return;
        var pointer = UiaComInterface.Pointer(root, typeof(IRawElementProviderSimpleAbi).GUID);
        try { UiaRaiseAutomationEvent(pointer, 20013); }
        finally { Marshal.Release(pointer); }
    }
    /// <summary>The HWND Table survives installations; only its bounded children are epoch-bound.</summary>
    internal GridAccessibilityFrame? CurrentFrame => Volatile.Read(ref _frame);
    /// <summary>Gets current immutable facts only if a retained node is still admitted.</summary>
    internal GridAccessibilityFrame? Frame(GridAccessibilityId id)
    {
        var frame = Volatile.Read(ref _frame);
        return frame?.Id == id ? frame : null;
    }
    /// <summary>Gets one lazy bounded wrapper; no visited-window cache survives publication.</summary>
    internal WindowsGridUiaNode Node(GridAccessibilityId id, int kind, int row, int column)
    {
        lock (_gate)
        {
            // A superseded read can yield only an immediately unavailable old-key wrapper,
            // never a wrapper authorized by the new installation.
            if (kind == 0 && _root is not null) return _root;
            if (_frame?.Id != id) return new(this, id, kind, row, column);
            var key = (kind, row, column);
            if (!_nodes.TryGetValue(key, out var node)) _nodes[key] = node = new(this, id, kind, row, column);
            return node;
        }
    }
    /// <summary>Reads actual clipped native screen geometry on the UIA-dispatched window thread.</summary>
    internal UiaRect Bounds(GridAccessibilityId id, int kind, int row, int column)
    {
        lock (_gate)
        {
        var frame = _frame;
        if (frame?.Id != id) return default;
        var index = kind switch { 0 => 0, 2 => 1 + row, 3 => 1 + frame.Rows.Count + column,
            _ => 1 + frame.Rows.Count + frame.Columns.Count + row * frame.Columns.Count + column };
        return (uint)index < (uint)_geometry.Length ? _geometry[index] : default;
        }
    }
    /// <summary>Invokes the sole adapter selection owner and translates its actual result.</summary>
    internal int Mutate(GridAccessibilityId id, GridSelectionMutation mutation) => Result(_actions?.MutateSelection(id, mutation) ?? GridAccessibilityResult.Unavailable);
    /// <summary>Focus is admitted by the adapter, not inferred from native message dispatch.</summary>
    internal int Focus(GridAccessibilityId id, GridCoordinate? cell) => Result(WindowsGridFocusOperation.Invoke(_actions, id, cell));
    /// <summary>Maps closed admission outcomes without false success.</summary>
    internal static int Result(GridAccessibilityResult result) => result switch
    {
        GridAccessibilityResult.Applied or GridAccessibilityResult.NoChange => 0,
        GridAccessibilityResult.Stale or GridAccessibilityResult.Unavailable => unchecked((int)0x80040201),
        GridAccessibilityResult.InvalidCoordinate => unchecked((int)0x80070057),
        _ => unchecked((int)0x80131509)
    };
    /// <summary>Only the table root obtains the default HWND host, without default cell proxies.</summary>
    internal int Host(out nint provider) => UiaHostProviderFromHwnd(_hwnd, out provider);
    /// <summary>Handles only UIA WM_GETOBJECT requests; no MSAA/source registration changes.</summary>
    internal nint GetObject(nuint wParam, nint lParam)
    {
        if (_root is null) return 0;
        var pointer = UiaComInterface.Pointer(_root, typeof(IRawElementProviderSimpleAbi).GUID);
        try { return UiaReturnRawElementProvider(_hwnd, wParam, lParam, pointer); }
        finally { Marshal.Release(pointer); }
    }
    /// <summary>Clears an installation while preserving the reusable HWND registration owner.</summary>
    internal void Clear() { lock (_gate) { _frame = null; _geometry = []; _nodes.Clear(); } }
    /// <summary>Retires all retained nodes and releases the sole current projection reference.</summary>
    internal void Detach()
    {
        Clear(); _root = null; _actions = null; _bounds = null;
        if (_hwnd != 0) UiaReturnRawElementProvider(_hwnd, 0, 0, 0);
        _hwnd = 0;
    }
    /// <summary>Builds an owned native SAFEARRAY of interface pointers with failure cleanup.</summary>
    internal static int Array(IEnumerable<WindowsGridUiaNode> nodes, out nint array)
    {
        var values = nodes.ToArray();
        array = SafeArrayCreateVector(13, 0, (uint)values.Length);
        if (array == 0) return unchecked((int)0x8007000E);
        for (var index = 0; index < values.Length; index++)
        {
            var pointer = UiaComInterface.Pointer(values[index], typeof(IRawElementProviderSimpleAbi).GUID);
            int result;
            try { result = SafeArrayPutElement(array, ref index, pointer); }
            finally { Marshal.Release(pointer); }
            if (result >= 0) continue;
            SafeArrayDestroy(array); array = 0; return result;
        }
        return 0;
    }
    /// <summary>Builds a runtime ID with the full installation serial and local node key.</summary>
    internal static unsafe int RuntimeId(GridAccessibilityId id, int kind, int row, int column, out nint array)
    {
        int[] values = [3, (int)id.WindowSerial, (int)(id.WindowSerial >> 32), kind, row, column];
        array = SafeArrayCreateVector(3, 0, (uint)values.Length);
        if (array == 0) return unchecked((int)0x8007000E);
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index];
            var result = SafeArrayPutElement(array, ref index, (nint)(&value));
            if (result >= 0) continue;
            SafeArrayDestroy(array); array = 0; return result;
        }
        return 0;
    }
    [LibraryImport("UIAutomationCore.dll")] private static partial int UiaHostProviderFromHwnd(nint hwnd, out nint provider);
    [LibraryImport("UIAutomationCore.dll")] private static partial nint UiaReturnRawElementProvider(nint hwnd, nuint wParam, nint lParam, nint provider);
    [LibraryImport("UIAutomationCore.dll")] private static partial int UiaRaiseStructureChangedEvent(nint provider, int change, nint runtimeId, int count);
    [LibraryImport("UIAutomationCore.dll")] private static partial int UiaRaiseAutomationEvent(nint provider, int eventId);
    [LibraryImport("oleaut32.dll")] private static partial nint SafeArrayCreateVector(ushort type, int lowerBound, uint count);
    [LibraryImport("oleaut32.dll")] private static partial int SafeArrayPutElement(nint array, ref int index, nint value);
    [LibraryImport("oleaut32.dll")] private static partial int SafeArrayDestroy(nint array);
}

/// <summary>A retained node stores only a local key and epoch, never an old projection.</summary>
[GeneratedComClass, SupportedOSPlatform("windows")]
internal sealed partial class WindowsGridUiaNode : IRawElementProviderSimpleAbi,
    IRawElementProviderFragmentAbi, IRawElementProviderFragmentRootAbi, IRawElementProviderHwndOverrideAbi,
    IGridProviderAbi, ITableProviderAbi, ISelectionProviderAbi, IGridItemProviderAbi,
    ITableItemProviderAbi, ISelectionItemProviderAbi, IGridValueProviderAbi
{
    private const int Unavailable = unchecked((int)0x80040201), Invalid = unchecked((int)0x80070057), Unsupported = unchecked((int)0x80131509);
    private readonly WindowsGridUiaBridge _owner;
    private readonly GridAccessibilityId _id;
    private readonly int _kind, _row, _column;
    private GridAccessibilityFrame? Frame => _kind == 0 ? _owner.CurrentFrame : _owner.Frame(_id);
    private GridCoordinate Coordinate(GridAccessibilityFrame frame) => frame.Coordinate(_row, _column);
    /// <summary>Captures a bounded local node key in one installed epoch.</summary>
    internal WindowsGridUiaNode(WindowsGridUiaBridge owner, GridAccessibilityId id, int kind, int row, int column)
    { _owner = owner; _id = id; _kind = kind; _row = row; _column = column; }
    private nint Pointer(WindowsGridUiaNode node, Type? abi = null) => UiaComInterface.Pointer(node, (abi ?? typeof(IRawElementProviderSimpleAbi)).GUID);
    /// <inheritdoc />
    public int GetProviderOptions(out int options) { options = 2 | 16 | 32 | 64; return Frame is null ? Unavailable : 0; }
    /// <inheritdoc />
    public int GetPatternProvider(int patternId, out nint provider)
    {
        provider = 0;
        var frame = Frame;
        if (frame is null) return Unavailable;
        var abi = (_kind, patternId) switch
        {
            (0, 10006) => typeof(IGridProviderAbi), (0, 10012) => typeof(ITableProviderAbi), (0, 10001) => typeof(ISelectionProviderAbi),
            (1, 10007) => typeof(IGridItemProviderAbi), (1, 10013) => typeof(ITableItemProviderAbi), (1, 10010) => typeof(ISelectionItemProviderAbi),
            (1, 10002) when frame.Cell(Coordinate(frame)).PresentationValue is not null => typeof(IGridValueProviderAbi),
            _ => null
        };
        if (abi is not null) provider = Pointer(this, abi);
        return 0;
    }
    /// <inheritdoc />
    public int GetPropertyValue(int propertyId, out UiaVariant value)
    {
        value = default;
        var frame = Frame;
        if (frame is null) return Unavailable;
        var cell = _kind == 1 ? frame.Cell(Coordinate(frame)) : null;
        if (propertyId == 30105 && _kind == 0) return _owner.Description(out value);
        if (propertyId == 30003) { value.Type = 3; value.Integer = _kind == 0 ? 50036 : _kind == 1 ? 50029 : _kind == 4 ? 50033 : 50035; }
        else if (propertyId is 30005 or 30011 or 30013)
        {
            var text = propertyId == 30011 ? (_kind == 0 ? "Mote.CsvGrid.Window" : $"Mote.CsvGrid.{_kind}.{_row}.{_column}") :
                propertyId == 30013 ? (cell?.Help ?? frame.Status) : _kind switch
                { 0 => "CSV grid window", 1 => cell!.Name, 4 => "CSV native header host", 2 => $"Row {frame.Rows.Start + _row + 1L}", _ => $"Column {frame.Columns.Start + _column + 1L}" };
            value.Type = 8; value.Pointer = Marshal.StringToBSTR(text);
        }
        else if (propertyId is 30008 or 30009 or 30010 or 30016 or 30017 or 30022)
        {
            var yes = propertyId switch
            {
                30008 => frame.HasTableFocus && (_kind == 0 ? frame.FocusedCell is null : _kind == 1 && frame.FocusedCell == Coordinate(frame)),
                30009 => _kind == 0 || _kind == 1 && cell!.State != Mote.Formats.GridValueState.Pending, 30022 => _owner.Bounds(frame.Id, _kind, _row, _column).Width <= 0 || _owner.Bounds(frame.Id, _kind, _row, _column).Height <= 0,
                _ => _kind != 4
            };
            value.Type = 11; value.Integer = yes ? -1 : 0;
        }
        return 0;
    }
    /// <inheritdoc />
    public int GetHostRawElementProvider(out nint provider)
    { provider = 0; return Frame is null ? Unavailable : _kind == 0 ? _owner.Host(out provider) : 0; }
    private int ChildIndex(GridAccessibilityFrame frame) => _kind switch
    { 2 => _row, 3 => frame.Rows.Count + _column, _ => frame.Rows.Count + frame.Columns.Count + _row * frame.Columns.Count + _column };
    private WindowsGridUiaNode Child(GridAccessibilityFrame frame, int index)
    {
        if (index < frame.Rows.Count) return _owner.Node(frame.Id, 2, index, 0);
        index -= frame.Rows.Count;
        if (index < frame.Columns.Count) return _owner.Node(frame.Id, 3, 0, index);
        index -= frame.Columns.Count;
        return _owner.Node(frame.Id, 1, index / frame.Columns.Count, index % frame.Columns.Count);
    }
    /// <inheritdoc />
    public int Navigate(int direction, out nint provider)
    {
        provider = 0; var frame = Frame; if (frame is null) return Unavailable;
        if (_kind == 4) { if (direction == 0) provider = Pointer(_owner.Node(_id, 0, 0, 0), typeof(IRawElementProviderFragmentAbi)); return 0; }
        var count = frame.Rows.Count + frame.Columns.Count + frame.Rows.Count * frame.Columns.Count;
        if (_kind != 0 && direction == 0) provider = Pointer(_owner.Node(_id, 0, 0, 0), typeof(IRawElementProviderFragmentAbi));
        else if (_kind == 0 && direction is 3 or 4 && count > 0) provider = Pointer(Child(frame, direction == 3 ? 0 : count - 1), typeof(IRawElementProviderFragmentAbi));
        else if (_kind != 0 && direction is 1 or 2)
        {
            var index = ChildIndex(frame) + (direction == 1 ? 1 : -1);
            if ((uint)index < (uint)count) provider = Pointer(Child(frame, index), typeof(IRawElementProviderFragmentAbi));
        }
        return 0;
    }
    /// <inheritdoc />
    public int GetRuntimeId(out nint id) { id = 0; return Frame is null ? Unavailable : _kind == 0 ? 0 : WindowsGridUiaBridge.RuntimeId(_id, _kind, _row, _column, out id); }
    /// <inheritdoc />
    public int GetBoundingRectangle(out UiaRect rectangle) { rectangle = default; var frame = Frame; if (frame is null) return Unavailable; rectangle = _kind == 4 ? default : _owner.Bounds(frame.Id, _kind, _row, _column); return 0; }
    /// <inheritdoc />
    public int GetEmbeddedFragmentRoots(out nint roots) { roots = 0; return Frame is null ? Unavailable : 0; }
    /// <inheritdoc />
    public int SetFocus() { var frame = Frame; return frame is null ? Unavailable : _kind is 0 or 1 ? _owner.Focus(frame.Id, _kind == 1 ? Coordinate(frame) : null) : Unsupported; }
    /// <inheritdoc />
    public int GetFragmentRoot(out nint root) { root = 0; if (Frame is null) return Unavailable; root = Pointer(_owner.Node(_id, 0, 0, 0), typeof(IRawElementProviderFragmentRootAbi)); return 0; }
    /// <inheritdoc />
    public int GetOverrideProviderForHwnd(nint hwnd, out nint provider)
    {
        provider = 0;
        var frame = Frame; if (frame is null) return Unavailable;
        if (_kind == 0 && _owner.IsHeader(hwnd)) provider = Pointer(_owner.Node(frame.Id, 4, 0, 0));
        return 0;
    }
    /// <inheritdoc />
    public int ElementProviderFromPoint(double x, double y, out nint provider)
    {
        provider = 0; var frame = Frame; if (frame is null) return Unavailable;
        if (_kind != 0) return Unsupported;
        var count = frame.Rows.Count + frame.Columns.Count + frame.Rows.Count * frame.Columns.Count;
        for (var index = 0; index < count; index++)
        {
            var node = Child(frame, index); var bounds = _owner.Bounds(frame.Id, node._kind, node._row, node._column);
            if (x >= bounds.Left && y >= bounds.Top && x < bounds.Left + bounds.Width && y < bounds.Top + bounds.Height)
            { provider = Pointer(node, typeof(IRawElementProviderFragmentAbi)); return 0; }
        }
        var table = _owner.Bounds(frame.Id, 0, 0, 0);
        if (x >= table.Left && y >= table.Top && x < table.Left + table.Width && y < table.Top + table.Height) provider = Pointer(_owner.Node(_id, 0, 0, 0), typeof(IRawElementProviderFragmentAbi));
        return 0;
    }
    /// <inheritdoc />
    public int GetFocus(out nint provider)
    {
        provider = 0; var frame = Frame; if (frame is null) return Unavailable;
        if (_kind != 0) return Unsupported;
        if (frame.HasTableFocus) provider = Pointer(frame.FocusedCell is { } cell && frame.Contains(cell) ? _owner.Node(frame.Id, 1, cell.Row - frame.Rows.Start, cell.Column - frame.Columns.Start) : _owner.Node(_id, 0, 0, 0), typeof(IRawElementProviderFragmentAbi));
        return 0;
    }
    /// <inheritdoc />
    public int GetItem(int row, int column, out nint provider)
    {
        provider = 0; var frame = Frame; if (frame is null) return Unavailable;
        if (_kind != 0) return Unsupported;
        if ((uint)row >= (uint)frame.Rows.Count || (uint)column >= (uint)frame.Columns.Count) return Invalid;
        provider = Pointer(_owner.Node(frame.Id, 1, row, column)); return 0;
    }
    /// <inheritdoc />
    public int GetRowCount(out int count) { var frame = Frame; count = _kind == 0 ? frame?.Rows.Count ?? 0 : 0; return frame is null ? Unavailable : _kind == 0 ? 0 : Unsupported; }
    /// <inheritdoc />
    public int GetColumnCount(out int count) { var frame = Frame; count = _kind == 0 ? frame?.Columns.Count ?? 0 : 0; return frame is null ? Unavailable : _kind == 0 ? 0 : Unsupported; }
    /// <inheritdoc />
    public int GetRowHeaders(out nint headers) { headers = 0; var frame = Frame; return frame is null ? Unavailable : _kind != 0 ? Unsupported : WindowsGridUiaBridge.Array(Enumerable.Range(0, frame.Rows.Count).Select(row => _owner.Node(frame.Id, 2, row, 0)), out headers); }
    /// <inheritdoc />
    public int GetColumnHeaders(out nint headers) { headers = 0; var frame = Frame; return frame is null ? Unavailable : _kind != 0 ? Unsupported : WindowsGridUiaBridge.Array(Enumerable.Range(0, frame.Columns.Count).Select(column => _owner.Node(frame.Id, 3, 0, column)), out headers); }
    /// <inheritdoc />
    public int GetRowOrColumnMajor(out int major) { major = 0; return Frame is null ? Unavailable : _kind == 0 ? 0 : Unsupported; }
    /// <inheritdoc />
    public int GetSelection(out nint selection) { selection = 0; var frame = Frame; return frame is null ? Unavailable : _kind != 0 ? Unsupported : WindowsGridUiaBridge.Array(frame.SelectedCells().Select(cell => _owner.Node(frame.Id, 1, cell.Row - frame.Rows.Start, cell.Column - frame.Columns.Start)), out selection); }
    /// <inheritdoc />
    public int GetCanSelectMultiple(out int multiple) { multiple = 1; return Frame is null ? Unavailable : _kind == 0 ? 0 : Unsupported; }
    /// <inheritdoc />
    public int GetIsSelectionRequired(out int required) { required = 0; return Frame is null ? Unavailable : _kind == 0 ? 0 : Unsupported; }
    /// <inheritdoc />
    public int GetRow(out int row) { row = _row; return Frame is null ? Unavailable : _kind == 1 ? 0 : Unsupported; }
    /// <inheritdoc />
    public int GetColumn(out int column) { column = _column; return Frame is null ? Unavailable : _kind == 1 ? 0 : Unsupported; }
    /// <inheritdoc />
    public int GetRowSpan(out int span) { span = 1; return Frame is null ? Unavailable : _kind == 1 ? 0 : Unsupported; }
    /// <inheritdoc />
    public int GetColumnSpan(out int span) { span = 1; return Frame is null ? Unavailable : _kind == 1 ? 0 : Unsupported; }
    /// <inheritdoc />
    public int GetContainingGrid(out nint grid) { grid = 0; if (Frame is null) return Unavailable; if (_kind != 1) return Unsupported; grid = Pointer(_owner.Node(_id, 0, 0, 0)); return 0; }
    /// <inheritdoc />
    public int GetRowHeaderItems(out nint headers) { headers = 0; return Frame is null ? Unavailable : _kind != 1 ? Unsupported : WindowsGridUiaBridge.Array([_owner.Node(_id, 2, _row, 0)], out headers); }
    /// <inheritdoc />
    public int GetColumnHeaderItems(out nint headers) { headers = 0; return Frame is null ? Unavailable : _kind != 1 ? Unsupported : WindowsGridUiaBridge.Array([_owner.Node(_id, 3, 0, _column)], out headers); }
    /// <inheritdoc />
    public int Select() { var frame = Frame; return frame is null ? Unavailable : _kind != 1 ? Unsupported : _owner.Mutate(_id, new GridSelectionMutation.ReplaceRectangle(new(Coordinate(frame), Coordinate(frame), false))); }
    /// <inheritdoc />
    public int AddToSelection() { var frame = Frame; return frame is null ? Unavailable : _kind != 1 ? Unsupported : _owner.Mutate(_id, new GridSelectionMutation.AddCell(Coordinate(frame))); }
    /// <inheritdoc />
    public int RemoveFromSelection() { var frame = Frame; return frame is null ? Unavailable : _kind != 1 ? Unsupported : _owner.Mutate(_id, new GridSelectionMutation.RemoveCell(Coordinate(frame))); }
    /// <inheritdoc />
    public int GetIsSelected(out int selected) { selected = 0; var frame = Frame; if (frame is null) return Unavailable; if (_kind != 1) return Unsupported; selected = frame.IsSelected(Coordinate(frame)) ? 1 : 0; return 0; }
    /// <inheritdoc />
    public int GetSelectionContainer(out nint container) => GetContainingGrid(out container);
    /// <inheritdoc />
    public int SetValue(nint value) => Frame is null ? Unavailable : Unsupported;
    /// <inheritdoc />
    public int GetValue(out nint value) { value = 0; var frame = Frame; if (frame is null) return Unavailable; if (_kind != 1) return Unsupported; var text = frame.Cell(Coordinate(frame)).PresentationValue; if (text is null) return Unsupported; value = Marshal.StringToBSTR(text); return 0; }
    /// <inheritdoc />
    public int GetIsReadOnly(out int readOnly) { readOnly = 1; return Frame is null ? Unavailable : _kind == 1 ? 0 : Unsupported; }
}
