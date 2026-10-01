using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Formats;
using Mote.Themes;
using Mote.Native.Windows.Accessibility;

namespace Mote.Native.Windows;

/// <summary>
/// Read-only, owner-data native table over a ready immutable bounded Grid. Callbacks
/// never access the engine, parse CSV, decode source, or retain a whole-file mirror.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsCsvGrid : IDisposable, IGridAccessibilityActions, IWindowsGridFocusEvidence
{
    private static readonly Win32.SubclassProcedure Procedure = Dispatch;
    private GCHandle _root;
    private NativeGridScrollFrame? _navigation;
    private NativeGridGesture? _gesture;
    private NativeGridAxis _gestureAxis;
    private int _rowWheelRemainder, _columnWheelRemainder;
    private GridRow?[] _slots = [];
    private (int Rows, int Columns)? _geometry;
    private (int X, int Y, int Width, int Height)? _bounds;
    /// <summary>Publishes changed clipped page geometry after installation is coherent.</summary>
    internal event Action<int, int>? GeometryChanged;
    /// <summary>Owned logical row scrollbar, independent of cache-local pixel extent.</summary>
    internal nint RowScroller { get; private set; }
    /// <summary>Allows shell status publication only for the surviving installation after reentrancy.</summary>
    internal bool HasNavigation(NativeGridScrollFrame? frame) => ReferenceEquals(_navigation, frame);
    /// <summary>The actual coordinate button for the shell-owned pane focus cycle.</summary>
    internal nint CoordinateControl => _goToHandle;
    /// <summary>Owned logical column scrollbar.</summary>
    internal nint ColumnScroller { get; private set; }
    /// <summary>The controller supplies the exact authority retained by subsequent phases.</summary>
    internal event Func<NativeGridGestureBegin, NativeGridGesture?>? GestureBeginning;
    /// <summary>Frozen-token phases never manufacture source origins.</summary>
    internal event Action<NativeGridGestureAction>? GestureRequested;
    private GridRenderProjection? _grid;
    private NativePresentationId? _identity;
    private NativePresentationId? _installedIdentity;
    private NativeDocumentStamp? _selectionDocument;
    private NativeGridWindowRequest? _requested;
    private IThemePolicy _theme;
    private readonly nint _parent;
    /// <summary>Actual shell input capability; cleared before disposal and never serialized.</summary>
    private nint _sourceHandle;
    /// <summary>Expected shell input control identity; zero means no source capability was supplied.</summary>
    private readonly int _sourceControlId;
    /// <summary>Native control identity used to validate this Grid's roles without guessing from focus.</summary>
    private readonly int _controlId;
    /// <summary>Observation-only native lifetime; retirement never changes the original adapter admission.</summary>
    private bool _focusEvidenceAlive;
    private int _columns, _row, _column, _anchorRow, _anchorColumn;
    private GridRange _nativeColumns;
    private GridRange Columns => _navigation?.RequestedColumns ?? _grid?.RequestedColumns ?? new(0, 0);
    private bool _installing;
    private long _installation;
    private bool _wholeRows;
    private bool _selectionCleared;
    private readonly Func<bool> _isCompositionActive;
    private readonly bool _accessibilityEnabled;
    private bool _ownsComApartment;
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    private readonly WindowsGridUiaBridge _uia;
    private readonly WindowsGridUiaGroup _accessibleGroup;
    private readonly WindowsGridUiaGroup _accessibleStatus;
    private readonly WindowsGridUiaScroller _accessibleRowScroller, _accessibleColumnScroller;
    private nint _groupHandle, _goToHandle, _statusHandle;
    private GridAccessibilityFrame? _accessibleFrame;
    private long _accessibleSerial;
    private GridCoordinate? _accessibleFocusedCell;
    private bool _tableOnlyFocus;
    private const uint AccessibilityRequestMessage = 0x804B;
    private readonly ConcurrentDictionary<long, AccessibilityRequest> _accessibleRequests = new();
    private long _accessibleRequestSerial;
    private int _accessibleRequestActive;
    private AccessibilityRequest? _admittingAccessibilityRequest;
    /// <summary>A bounded synchronous HWND admission token, never an unmanaged retained pointer.</summary>
    private sealed record AccessibilityRequest(GridAccessibilityId Id, GridSelectionMutation Mutation, long Deadline)
    {
        /// <summary>Timeout cancellation is checked again immediately before UI admission.</summary>
        internal int Cancelled;
        /// <summary>Serializes only callback-free selection commit against timeout cancellation.</summary>
        internal readonly object Gate = new();
        /// <summary>Actual completed synchronous result, not merely a queued admission.</summary>
        internal GridAccessibilityResult? Completed;
        /// <summary>Whether admission remains within the caller's bounded synchronous wait.</summary>
        internal bool Expired => Volatile.Read(ref Cancelled) != 0 || Stopwatch.GetTimestamp() >= Deadline;
    }
    private (int Row, int Column, bool Extend)? _pendingSelection;
    /// <summary>A coordinate dialog target bound to the actual admitted navigation serial, never a prediction.</summary>
    private (NativeDocumentStamp Document, long BeginSerial, long? RequestSerial, int Row, int Column)? _coordinateSelection;

    /// <summary>Creates a hidden report table; its parent forwards WM_NOTIFY to HandleNotify.</summary>
    internal WindowsCsvGrid(nint parent, int id, IThemePolicy theme, Func<bool>? isCompositionActive = null,
        bool? accessibilityEnabled = null, nint sourceHandle = 0, int sourceControlId = 0)
    {
        _theme = theme;
        _parent = parent;
        _sourceHandle = sourceHandle; _sourceControlId = sourceControlId; _controlId = id;
        _isCompositionActive = isCompositionActive ?? (() => false);
        _accessibilityEnabled = accessibilityEnabled ?? Environment.GetEnvironmentVariable("MOTE_NATIVE_GRID_ACCESSIBILITY") == "1";
        try
        {
        _ownsComApartment = WindowsGridInterop.CoInitializeEx(0, 2) >= 0;
        var controls = new WindowsGridInterop.Controls
        { Size = (uint)Marshal.SizeOf<WindowsGridInterop.Controls>(), Classes = 1 };
        if (!WindowsGridInterop.InitCommonControlsEx(ref controls)) throw new Win32Exception();
        _groupHandle = Win32.CreateWindowExW(0, "STATIC", "Mote CSV grid navigation",
            Win32.WS_CHILD | 0x02000000, 0, 0, 100, 100, parent, (nint)(id + 1100), Win32.GetModuleHandleW(null), 0);
        if (_groupHandle == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        _accessibleGroup = new(_groupHandle);
        Handle = Win32.CreateWindowExW(Win32.WS_EX_CLIENTEDGE, "SysListView32", "CSV table",
            Win32.WS_CHILD | Win32.WS_TABSTOP | 0x0001 | 0x1000 | 0x0008,
            0, 0, 100, 100, _groupHandle, (nint)id, Win32.GetModuleHandleW(null), 0);
        if (Handle == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        _root = GCHandle.Alloc(this);
        if (!Win32.SetWindowSubclass(Handle, Procedure, 1, (nuint)GCHandle.ToIntPtr(_root)))
        { Dispose(); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        if (!Win32.SetWindowSubclass(_groupHandle, Procedure, 1, (nuint)GCHandle.ToIntPtr(_root)))
        { Dispose(); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        _goToHandle = Win32.CreateWindowExW(0, "BUTTON", "Go to CSV cell…", Win32.WS_CHILD | Win32.WS_TABSTOP,
            0, 0, 130, 24, _groupHandle, (nint)(id + 1101), Win32.GetModuleHandleW(null), 0);
        _statusHandle = Win32.CreateWindowExW(0, "STATIC", "CSV grid status", Win32.WS_CHILD,
            0, 0, 100, 24, _groupHandle, (nint)(id + 1102), Win32.GetModuleHandleW(null), 0);
        if (_goToHandle == 0 || _statusHandle == 0) { Dispose(); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        // Grid lines + double buffering. Sorting and header drag/drop are deliberately absent.
        Win32.SendMessageW(Handle, WindowsGridInterop.First + 54, 0, 0x00010001);
        RowScroller = CreateScroller(id + 1000, true);
        ColumnScroller = CreateScroller(id + 1001, false);
        _accessibleRowScroller = new(RowScroller, true);
        _accessibleColumnScroller = new(ColumnScroller, false);
        _accessibleStatus = new(_statusHandle, true);
        if (!Win32.SetWindowSubclass(_statusHandle, Procedure, 1, (nuint)GCHandle.ToIntPtr(_root)))
        { Dispose(); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        _uia = new(Handle, this, AccessibleBounds, _accessibleStatus);
        _uia.BindHeader(Win32.SendMessageW(Handle, WindowsGridInterop.First + 31, 0, 0));
        SetTheme(theme);
        Volatile.Write(ref _focusEvidenceAlive, true);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>The actual owner-data report HWND, not a text preview.</summary>
    internal nint Handle { get; private set; }
    /// <summary>Whether keyboard focus belongs to this detached native table.</summary>
    internal bool HasFocus => Handle != 0 && (WindowsGridInterop.GetFocus() == Handle ||
        WindowsGridInterop.GetFocus() == RowScroller || WindowsGridInterop.GetFocus() == ColumnScroller || WindowsGridInterop.GetFocus() == _goToHandle);
    /// <summary>Ready-cache selection/action; the controller validates exact installation identity.</summary>
    internal event Action<NativeGridIntent>? IntentRequested;
    /// <summary>Coalesced bounded coordinate request, never synchronous data loading.</summary>
    internal event Action<NativeGridWindowRequest>? WindowRequested;
    /// <summary>Reports callback failures without throwing across an unmanaged stack.</summary>
    internal event Action<Exception>? Faulted;

    /// <summary>Installs a bounded immutable table; null immediately retires its command authority.</summary>
    internal void Install(GridRenderProjection? grid, NativePresentationId identity)
    {
        var installation = ++_installation;
        _accessibleFrame = null; _uia.Clear();
        _accessibleFocusedCell = null; _tableOnlyFocus = false;
        var sameDocument = _selectionDocument == identity.Document;
        _installedIdentity = grid is null ? null : identity;
        var selectedPending = false;
        _identity = null;
        _installing = true;
        try
        {
            Win32.SendMessageW(Handle, WindowsGridInterop.SetItemCount, 0, 0);
            if (installation != _installation) return;
            for (var index = _columns - 1; index >= 0; index--)
            {
                Win32.SendMessageW(Handle, WindowsGridInterop.DeleteColumn, (nuint)index, 0);
                if (installation != _installation) return;
            }
            _columns = 0; _nativeColumns = default;
            _grid = grid;
            RebuildSlots();
            _requested = null;
            if (grid is null) { _pendingSelection = null; return; }
            if (grid.Version != identity.Document.Version) throw new ArgumentException("Grid identity version mismatch.");
            InsertColumn(0, -1);
            if (installation != _installation) return;
            for (var index = 0; index < Columns.Count; index++)
            {
                InsertColumn(index + 1, Columns.Start + index);
                if (installation != _installation) return;
            }
            _columns = Columns.Count + 1; _nativeColumns = Columns;
            _identity = _navigation is null || _navigation.Ready == identity ? identity : null;
            _selectionDocument = identity.Document;
            if (!sameDocument)
            {
                _row = grid.Rows.Count == 0 ? grid.RequestedRows.Start : grid.Rows[0].Ordinal;
                _column = Columns.Start;
                _anchorRow = _row; _anchorColumn = _column; _wholeRows = false; _selectionCleared = false;
            }
            if (_pendingSelection is { } pending && grid.Rows.Any(row => row.Ordinal == pending.Row) &&
                pending.Column >= Columns.Start && pending.Column < Columns.End)
            {
                _row = pending.Row; _column = pending.Column;
                if (!pending.Extend) { _anchorRow = _row; _anchorColumn = _column; }
                _pendingSelection = null;
                selectedPending = true;
            }
            Win32.SendMessageW(Handle, WindowsGridInterop.SetItemCount, (nuint)_slots.Length, 0);
            if (installation != _installation) return;
            SetNativeFocus(false);
        }
        finally { if (installation == _installation) { _installing = false; PublishAccessibility(); } }
        if (installation != _installation) return;
        Win32.InvalidateRect(Handle, 0, false);
        HideLocalScrollbars();
        if (selectedPending && _identity == identity) Emit(NativeGridIntentKind.Select);
    }

    private nint CreateScroller(int id, bool vertical)
    {
        var handle = Win32.CreateWindowExW(0, "SCROLLBAR", vertical ? "File rows" : "File columns",
            Win32.WS_CHILD | Win32.WS_TABSTOP | (vertical ? 1u : 0u),
            0, 0, 10, 10, _groupHandle, (nint)id, Win32.GetModuleHandleW(null), 0);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (!Win32.SetWindowSubclass(handle, Procedure, 1, (nuint)GCHandle.ToIntPtr(_root)))
        { Win32.DestroyWindow(handle); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        return handle;
    }

    /// <summary>Range authority is independent of ready source-backed command authority.</summary>
    internal void SetNavigation(NativeGridScrollFrame? frame)
    {
        var installation = ++_installation;
        _accessibleFrame = null; _uia.Clear();
        _accessibleFocusedCell = null; _tableOnlyFocus = false;
        _installing = true;
        try
        {
        if (_navigation?.Navigation != frame?.Navigation) { _gesture = null; _rowWheelRemainder = _columnWheelRemainder = 0; }
        if (_navigation?.Navigation.Document != frame?.Navigation.Document) _geometry = null;
        var placementChanged = _navigation?.RequestSerial != frame?.RequestSerial;
        if (_coordinateSelection is { } coordinate)
        {
            if (frame is null || frame.Navigation.Document != coordinate.Document) _coordinateSelection = null;
            else if (coordinate.RequestSerial is { } frozenSerial)
            {
                if (frame.RequestSerial != frozenSerial) _coordinateSelection = null;
            }
            else if (frame.RequestSerial > coordinate.BeginSerial)
            {
                if (frame.Pending && coordinate.Row >= frame.RequestedRows.Start && coordinate.Row < frame.RequestedRows.End &&
                    coordinate.Column >= frame.RequestedColumns.Start && coordinate.Column < frame.RequestedColumns.End)
                    _coordinateSelection = coordinate with { RequestSerial = frame.RequestSerial };
                else _coordinateSelection = null;
            }
        }
        _navigation = frame;
        _identity = frame is null ? _installedIdentity : frame.Ready == _installedIdentity ? frame.Ready : null;
        if (frame is not null && (_nativeColumns != frame.RequestedColumns || _columns != frame.RequestedColumns.Count + 1))
        {
            for (var index = _columns - 1; index >= 0; index--)
            {
                Win32.SendMessageW(Handle, WindowsGridInterop.DeleteColumn, (nuint)index, 0);
                if (installation != _installation) return;
            }
            InsertColumn(0, -1);
            if (installation != _installation) return;
            for (var index = 0; index < frame.RequestedColumns.Count; index++)
            {
                InsertColumn(index + 1, frame.RequestedColumns.Start + index);
                if (installation != _installation) return;
            }
            _columns = frame.RequestedColumns.Count + 1; _nativeColumns = frame.RequestedColumns;
        }
        RebuildSlots();
        Win32.SendMessageW(Handle, WindowsGridInterop.SetItemCount, (nuint)_slots.Length, 0);
        if (installation != _installation) return;
        InstallAxis(RowScroller, frame?.Rows);
        if (installation != _installation) return;
        InstallAxis(ColumnScroller, frame?.Columns);
        if (installation != _installation) return;
        // A rebase places the first requested ordinal at the local table top; retained selection cannot move it back.
        if (placementChanged && _slots.Length > 0)
            Win32.SendMessageW(Handle, WindowsGridInterop.EnsureVisible, 0, 0);
        if (installation != _installation) return;
        if (_bounds is { } bounds) Resize(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        if (installation != _installation) return;
        HideLocalScrollbars();
        if (_coordinateSelection is { RequestSerial: { } serial } target && frame is { Pending: false } && frame.RequestSerial == serial &&
            _identity is not null && _grid is { } readyGrid && NativeCsvGrid.Row(readyGrid, target.Row) is { } targetRow &&
            NativeCsvGrid.Cell(targetRow, target.Column) is { State: not GridValueState.Pending })
        {
            _coordinateSelection = null;
            _row = _anchorRow = target.Row; _column = _anchorColumn = target.Column;
            _selectionCleared = false; _wholeRows = false; _selectionDocument = target.Document;
            SetNativeFocus(false);
        }
        Win32.InvalidateRect(Handle, 0, false);
        PublishAccessibility();
        }
        finally { if (installation == _installation) { _installing = false; PublishAccessibility(true); } }
    }

    private void RebuildSlots() => _slots = _navigation is { } frame
        ? NativeGridPlanner.Slots(_identity is null ? null : _grid, frame.RequestedRows, frame.Rows.Count)
        : _grid is { } grid ? NativeGridPlanner.Slots(grid, grid.RequestedRows, grid.Extent.ExactRowCount ?? grid.RequestedRows.End) : [];

    private static void InstallAxis(nint handle, NativeGridScrollAxis? axis)
    {
        var value = axis ?? NativeGridScrollAxis.Create(NativeGridExtentKind.Unavailable, 0, 1, 0);
        var info = new WindowsGridInterop.Scroll
        {
            Size = (uint)Marshal.SizeOf<WindowsGridInterop.Scroll>(), Mask = 1 | 2 | 4,
            Minimum = 0, Maximum = Math.Max(0, value.Count - 1),
            Page = (uint)Math.Min(value.Count, value.Page), Position = value.First
        };
        WindowsGridInterop.SetScrollInfo(handle, 2, ref info, true);
        if (!WindowsGridInterop.GetScrollInfo(handle, 2, ref info))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (value.Count > 0 && (info.Maximum != value.Count - 1 ||
            info.Page != (uint)Math.Min(value.Count, value.Page) || info.Position != value.First))
            throw new InvalidOperationException("Native Grid scrollbar range readback mismatch.");
        WindowsGridInterop.EnableWindow(handle, value.Kind != NativeGridExtentKind.Unavailable && value.Count > 0);
    }

    /// <summary>Reserves native scrollbar edges without expanding retained table storage.</summary>
    internal void Resize(int x, int y, int width, int height)
    {
        _bounds = (x, y, width, height);
        var vertical = _navigation is null ? 0 : Math.Max(1, WindowsGridInterop.GetSystemMetrics(2));
        var horizontal = _navigation is null ? 0 : Math.Max(1, WindowsGridInterop.GetSystemMetrics(3));
        var bodyWidth = Math.Max(0, width - vertical);
        var footer = Math.Min(28, Math.Max(0, height));
        var bodyHeight = Math.Max(0, height - horizontal - footer);
        Win32.MoveWindow(_groupHandle, x, y, width, height, true);
        Win32.MoveWindow(Handle, 0, 0, bodyWidth, bodyHeight, true);
        Win32.MoveWindow(RowScroller, bodyWidth, 0, vertical, bodyHeight, true);
        Win32.MoveWindow(ColumnScroller, 0, bodyHeight, bodyWidth, horizontal, true);
        Win32.MoveWindow(_goToHandle, 0, height - footer, Math.Min(130, width), footer, true);
        Win32.MoveWindow(_statusHandle, Math.Min(136, width), height - footer, Math.Max(0, width - 136), footer, true);
        HideLocalScrollbars();
        PublishGeometry();
        PublishAccessibility(true);
    }

    /// <summary>Shows passive table/navigation surfaces without taking activation; explicit input owns focus.</summary>
    internal void Show(bool visible)
    {
        // Ready/pending analysis may install after the user starts editing another pane.
        // Visibility publication is not an input action and must not activate these children.
        var show = visible ? WindowsGridInterop.ShowWithoutActivation : 0;
        Win32.ShowWindow(_groupHandle, show);
        Win32.ShowWindow(_goToHandle, show);
        Win32.ShowWindow(_statusHandle, show);
        Win32.ShowWindow(Handle, show);
        var navigation = visible && _navigation is not null ? WindowsGridInterop.ShowWithoutActivation : 0;
        Win32.ShowWindow(RowScroller, navigation);
        Win32.ShowWindow(ColumnScroller, navigation);
        PublishAccessibility(true);
    }

    private void HideLocalScrollbars()
    {
        if (_navigation is not null) WindowsGridInterop.ShowScrollBar(Handle, 3, false);
    }

    private (int Rows, int Columns) MeasurePage()
    {
        var rows = Math.Max(1, (int)Win32.SendMessageW(Handle, WindowsGridInterop.First + 40, 0, 0));
        var columns = 1;
        if (Win32.GetClientRect(Handle, out var bounds))
        {
            var remaining = Math.Max(0, bounds.Right - bounds.Left - 72);
            columns = 0;
            for (var index = 1; index < _columns; index++)
            {
                var width = (int)Win32.SendMessageW(Handle, WindowsGridInterop.First + 29, (nuint)index, 0);
                if (width <= 0 || remaining < width) break;
                remaining -= width; columns++;
            }
        }
        return NativeGridPlanner.Page(rows, columns);
    }

    private void PublishGeometry()
    {
        if (_navigation is null || _installing) return;
        var page = MeasurePage();
        if (_geometry == page) return;
        _geometry = page;
        GeometryChanged?.Invoke(page.Rows, page.Columns);
    }

    private NativeGridGesture? Begin()
    {
        if (_navigation is not { } frame) return null;
        var page = MeasurePage();
        return GestureBeginning?.Invoke(new(frame, page.Rows, page.Columns));
    }

    private void Submit(NativeGridTargetKind kind, int row, int column)
    {
        var gesture = Begin();
        if (gesture is not null)
            GestureRequested?.Invoke(new(gesture.Id, NativeGridGesturePhase.Commit, row, column, kind));
    }

    private void Navigate(NativeGridAxis axis, long delta)
    {
        var gesture = Begin();
        if (gesture is null) return;
        var frame = gesture.Frame;
        GestureRequested?.Invoke(new(gesture.Id, NativeGridGesturePhase.Commit,
            axis == NativeGridAxis.Rows ? frame.Rows.Step(delta) : frame.Rows.First,
            axis == NativeGridAxis.Columns ? frame.Columns.Step(delta) : frame.Columns.First));
    }

    /// <summary>Owned HWND routing uses full 32-bit thumb positions, never the message high word.</summary>
    internal bool HandleScroll(uint message, nuint command, nint control)
    {
        if (message is not (0x0114 or 0x0115) || control != RowScroller && control != ColumnScroller) return false;
        var operation = (int)(command & 0xffff);
        if (_navigation is null) return true;
        var axis = control == RowScroller ? NativeGridAxis.Rows : NativeGridAxis.Columns;
        if (_gesture is not null && _gestureAxis != axis) CancelGesture();
        // SB_ENDSCROLL follows an already committed line/position action.
        if (operation == 8 && _gesture is null) return true;
        var gesture = _gesture ?? Begin();
        if (gesture is null) return true;
        _gesture = gesture; _gestureAxis = axis;
        var range = axis == NativeGridAxis.Rows ? gesture.Frame.Rows : gesture.Frame.Columns;
        var position = range.First;
        if (operation is 4 or 5 or 8)
        {
            var info = new WindowsGridInterop.Scroll
            { Size = (uint)Marshal.SizeOf<WindowsGridInterop.Scroll>(), Mask = 0x10 };
            if (!WindowsGridInterop.GetScrollInfo(control, 2, ref info))
            { CancelGesture(); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
            position = Math.Clamp(info.TrackPosition, 0, range.Last);
        }
        else position = operation switch
        {
            0 => range.Step(-1), 1 => range.Step(1), 2 => range.Step(-range.Page),
            3 => range.Step(range.Page), 6 => 0, 7 => range.Last, _ => range.First
        };
        var phase = operation == 5 ? NativeGridGesturePhase.Track : NativeGridGesturePhase.Commit;
        if (phase != NativeGridGesturePhase.Track) _gesture = null;
        GestureRequested?.Invoke(new(gesture.Id, phase,
            axis == NativeGridAxis.Rows ? position : gesture.Frame.Rows.First,
            axis == NativeGridAxis.Columns ? position : gesture.Frame.Columns.First,
            operation == 7 && axis == NativeGridAxis.Rows ? NativeGridTargetKind.End : NativeGridTargetKind.Viewport));
        return true;
    }

    private void CancelGesture()
    {
        var gesture = _gesture; _gesture = null;
        if (gesture is not null) GestureRequested?.Invoke(new(gesture.Id, NativeGridGesturePhase.Cancel,
            gesture.Frame.Rows.First, gesture.Frame.Columns.First));
    }

    /// <summary>Updates native cell surfaces without changing source, selection, or identity.</summary>
    internal void SetTheme(IThemePolicy theme)
    {
        _theme = theme;
        Win32.SendMessageW(Handle, WindowsGridInterop.First + 1, 0, (nint)Color(theme.Palette.PreviewBackground));
        Win32.SendMessageW(Handle, WindowsGridInterop.First + 36, 0, (nint)Color(theme.Palette.PreviewForeground));
        Win32.SendMessageW(Handle, WindowsGridInterop.First + 38, 0, (nint)Color(theme.Palette.PreviewBackground));
        Win32.InvalidateRect(Handle, 0, false);
    }

    /// <summary>Routes explicit shell Copy to exact cell/rectangle source semantics, not clipped labels.</summary>
    internal void Copy() { if (_selectionCleared) return; Emit(_wholeRows ? NativeGridIntentKind.CopyRows :
        _row == _anchorRow && _column == _anchorColumn ? NativeGridIntentKind.CopyValue : NativeGridIntentKind.CopyTsv); }

    /// <summary>Selects only delivered coordinates; never pretends to select a whole-file row mirror.</summary>
    internal void SelectAll()
    {
        if (_grid is not { Rows.Count: > 0 } grid) return;
        _wholeRows = false;
        _anchorRow = grid.Rows[0].Ordinal; _anchorColumn = Columns.Start;
        Select(grid.Rows[^1].Ordinal, Columns.End - 1, true);
    }

    /// <summary>Requests distant logical coordinates without synthesizing a whole-file native item count.</summary>
    internal void GoToCoordinate()
    {
        if (_isCompositionActive()) return;
        if (_navigation is { } frame)
        {
            var gesture = Begin();
            if (gesture is null) return;
            var input = Win32TextPrompt.Show(_parent, "Go to CSV cell", "One-based row:column:", $"{_row + 1L}:{_column + 1L}");
            if (!ReferenceEquals(_navigation, gesture.Frame)) return;
            var parts = input?.Split(':');
            if (parts is { Length: 2 } && int.TryParse(parts[0], out var row) && row > 0 &&
                int.TryParse(parts[1], out var column) && column > 0)
            {
                _coordinateSelection = (gesture.Frame.Navigation.Document, gesture.Frame.RequestSerial, null, row - 1, column - 1);
                GestureRequested?.Invoke(new(gesture.Id, NativeGridGesturePhase.Commit, row - 1, column - 1, NativeGridTargetKind.Cell));
                if (_coordinateSelection is { RequestSerial: null }) _coordinateSelection = null;
            }
            else GestureRequested?.Invoke(new(gesture.Id, NativeGridGesturePhase.Cancel, frame.Rows.First, frame.Columns.First));
            return;
        }
        if (_identity is not { } identity) return;
        CoordinatePrompt(identity, _row, _column, _grid?.Extent);
    }

    private void CoordinatePrompt(NativePresentationId identity, int row, int column, GridExtent? extent)
    {
        var input = Win32TextPrompt.Show(_parent, "Go to CSV cell", "One-based row:column:", $"{row + 1L}:{column + 1L}");
        if (input is not null && !RequestCoordinate(input, identity, extent))
            Win32.MessageBoxW(_parent, "Enter positive row:column coordinates within the known document extent and supported range.", "Go to CSV cell", Win32.MB_OK);
    }

    /// <summary>A modal coordinate command retains its original identity across nested-loop data installation.</summary>
    internal bool RequestCoordinate(string input, NativePresentationId identity, GridExtent? frozenExtent = null)
    {
        if (_navigation is not null && _navigation.Ready != identity) return false;
        var parts = input.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var row) || row <= 0 ||
            !int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var column) || column <= 0) return false;
        row--; column--;
        var extent = frozenExtent ?? (_identity == identity ? _grid?.Extent : null);
        if (extent is { } known && (known.ExactRowCount is { } rows && row >= rows ||
            known.ExactMaxWidth is { } columns && column >= columns)) return false;
        if (_identity == identity) _pendingSelection = (row, column, false);
        if (_navigation is not null) Submit(NativeGridTargetKind.Cell, row, column);
        else WindowRequested?.Invoke(new NativeGridWindowRequest(identity, row,
            new GridRange(column, Math.Min(64, int.MaxValue - column))));
        return true;
    }

    /// <summary>Handles only notifications from this table; buffers are caller-owned and never retained.</summary>
    internal bool HandleNotify(nint notification, out nint result)
    {
        result = 0;
        if (notification != 0)
        {
            var header = Marshal.PtrToStructure<WindowsGridInterop.Header>(notification);
            if (header.Window == Win32.SendMessageW(Handle, WindowsGridInterop.First + 31, 0, 0) &&
                header.Code is -321 or -301) { PublishGeometry(); return true; }
        }
        if (notification == 0 || Marshal.PtrToStructure<WindowsGridInterop.Header>(notification).Window != Handle)
            return false;
        try
        {
            var code = Marshal.PtrToStructure<WindowsGridInterop.Header>(notification).Code;
            if (code == WindowsGridInterop.GetDispInfo) FillDisplay(notification);
            else if (code == -179) result = -1; // LVN_ODFINDITEMW: no synchronous source/type-ahead search.
            else if (code == WindowsGridInterop.CustomDraw) result = Draw(notification);
            else if (code == WindowsGridInterop.DoubleClick && !_installing) Emit(NativeGridIntentKind.Reveal);
        }
        catch (Exception error) { Report(error); }
        return true;
    }

    /// <summary>Copies at most the ready value and never splits a UTF-16 surrogate pair.</summary>
    internal static unsafe void CopyLabel(string value, nint destination, int capacity)
    {
        if (destination == 0 || capacity <= 0) return;
        var count = Math.Min(value.Length, capacity - 1);
        if (count > 0 && count < value.Length && char.IsHighSurrogate(value[count - 1]) && char.IsLowSurrogate(value[count]))
            count--;
        value.AsSpan(0, count).CopyTo(new Span<char>((void*)destination, count));
        ((char*)destination)[count] = '\0';
    }

    private void InsertColumn(int local, int ordinal)
    {
        var label = Marshal.StringToHGlobalUni(ordinal < 0 ? "Row" : $"Column {ordinal + 1}");
        try
        {
            var column = new WindowsGridInterop.Column { Mask = 1 | 2 | 4 | 8, Width = ordinal < 0 ? 72 : 160, Text = label, SubItem = local };
            if (Send(WindowsGridInterop.InsertColumn, (nuint)local, ref column) == -1)
                throw new Win32Exception("Cannot insert Grid column.");
        }
        finally { Marshal.FreeHGlobal(label); }
    }

    private void FillDisplay(nint notification)
    {
        var info = Marshal.PtrToStructure<WindowsGridInterop.DisplayInfo>(notification);
        if ((info.Item.Mask & 1) == 0) return;
        var label = "[pending]";
        if (_identity is not null && _grid is { } grid && (uint)info.Item.Row < (uint)_slots.Length && _slots[info.Item.Row] is not null &&
            (uint)info.Item.Column <= (uint)Columns.Count)
        {
            var row = _slots[info.Item.Row]!;
            label = info.Item.Column == 0 ? (row.Ordinal + 1L).ToString(System.Globalization.CultureInfo.InvariantCulture) :
                NativeCsvGrid.Display(grid, NativeCsvGrid.Cell(row, Columns.Start + info.Item.Column - 1));
        }
        CopyLabel(label, info.Item.Text, info.Item.TextCapacity);
    }

    private nint Draw(nint notification)
    {
        var draw = Marshal.PtrToStructure<WindowsGridInterop.Draw>(notification);
        if (draw.Stage == 1) return 0x20; // CDDS_PREPAINT -> notify item draw
        if (draw.Stage == 0x10001) return 0x20; // item prepaint -> notify subitems
        if (draw.Stage != 0x30001 || draw.Item >= (nuint)_slots.Length) return 0;
        var row = (_navigation?.RequestedRows.Start ?? _grid?.RequestedRows.Start ?? 0) + (int)draw.Item;
        var column = Columns.Start + draw.Column - 1;
        var selected = !_selectionCleared && row >= Math.Min(_row, _anchorRow) && row <= Math.Max(_row, _anchorRow) &&
            (_wholeRows || column >= Math.Min(_column, _anchorColumn) && column <= Math.Max(_column, _anchorColumn));
        draw.Foreground = Color(selected ? _theme.Palette.SelectionForeground : _theme.Palette.PreviewForeground);
        draw.Background = Color(selected ? _theme.Palette.SelectionBackground : _theme.Palette.PreviewBackground);
        // Suppress the ListView's row-wide selected surface; the controller selection is cell-based.
        draw.State &= ~1u;
        Marshal.StructureToPtr(draw, notification, false);
        return 0;
    }

    private static nint Dispatch(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        var self = GCHandle.FromIntPtr((nint)data).Target as WindowsCsvGrid;
        try
        {
            if (self is not null && self._accessibilityEnabled && message == 0x003D && lParam == -25 &&
                (window == self.RowScroller || window == self.ColumnScroller))
                return (window == self.RowScroller ? self._accessibleRowScroller : self._accessibleColumnScroller).GetObject(wParam, lParam);
            if (self is not null && self._accessibilityEnabled && window == self._statusHandle && message == 0x003D && lParam == -25)
                return self._accessibleStatus.GetObject(wParam, lParam);
            if (self is not null && window == self._groupHandle)
            {
                if (self._accessibilityEnabled && message == 0x003D && lParam == -25) return self._accessibleGroup.GetObject(wParam, lParam);
                if (message == 0x0111 && lParam == self._goToHandle) { self.GoToCell(); return 0; }
                if (message is 0x0114 or 0x0115) return Win32.SendMessageW(self._parent, (int)message, wParam, lParam);
                if (message == Win32.WM_NOTIFY) return Win32.SendMessageW(self._parent, (int)message, wParam, lParam);
                return Win32.DefSubclassProc(window, message, wParam, lParam);
            }
            if (self is not null && window != self.Handle)
            {
                if (message == 0x001F || message == 0x0215 ||
                    message == Win32.WM_KEYDOWN && wParam == 0x1B) self.CancelGesture();
                var nativeResult = Win32.DefSubclassProc(window, message, wParam, lParam);
                if (message is 0x0007 or 0x0008) self.PublishAccessibility();
                return nativeResult;
            }
            if (self is not null && message == AccessibilityRequestMessage)
            {
                try { return (nint)(int)self.AdmitAccessibilityRequest((long)wParam); }
                catch (Exception error) { self.Report(error); return (nint)(int)GridAccessibilityResult.Unavailable; }
            }
            if (self is not null && self._accessibilityEnabled && message == 0x003D && lParam == -25)
                return self._uia.GetObject(wParam, lParam);
            if (self is not null && message == Win32.WM_NOTIFY &&
                self.HandleNotify(lParam, out var notificationResult)) { self.PublishAccessibility(); return notificationResult; }
            if (self is not null && self.Input(message, wParam, lParam)) { self.PublishAccessibility(); return 0; }
        }
        catch (Exception error) { self?.Report(error); }
        var result = Win32.DefSubclassProc(window, message, wParam, lParam);
        try { self?.AfterScroll(message, wParam); if (message is 0x0007 or 0x0008 or 0x0005 or 0x0003) self?.PublishAccessibility(message is 0x0005 or 0x0003); }
        catch (Exception error) { self?.Report(error); }
        return result;
    }

    /// <summary>Scrollbar boundaries rebase only after native scrolling settles; callbacks still never load source.</summary>
    private void AfterScroll(uint message, nuint command)
    {
        if (_navigation is not null) return;
        if (message is not (0x0114 or 0x0115) || _grid is not { Rows.Count: > 0 } grid || _identity is null) return;
        var operation = (int)(command & 0xffff);
        var info = new WindowsGridInterop.Scroll
        { Size = (uint)Marshal.SizeOf<WindowsGridInterop.Scroll>(), Mask = 1 | 2 | 4 };
        if (!WindowsGridInterop.GetScrollInfo(Handle, message == 0x0114 ? 0 : 1, ref info)) return;
        var forward = operation is 1 or 3 or 7 || operation is 4 or 5 && info.Position > info.Minimum;
        var backward = operation is 0 or 2 or 6 || operation is 4 or 5 && info.Position == info.Minimum;
        var atEnd = (long)info.Position + Math.Max(1u, info.Page) - 1 >= info.Maximum;
        if (message == 0x0115)
        {
            if (forward && atEnd) Request(grid.Rows[^1].Ordinal, Columns);
            if (backward && info.Position == info.Minimum && grid.Rows[0].Ordinal > 0)
                Request(Math.Max(0, grid.Rows[0].Ordinal - Math.Max(1, grid.RequestedRows.Count - 1)), Columns);
            return;
        }
        if (forward && atEnd && Columns.End < int.MaxValue)
        {
            var start = Columns.End - 1;
            Request(grid.Rows[0].Ordinal, new GridRange(start, Math.Min(64, int.MaxValue - start)));
        }
        if (backward && info.Position == info.Minimum && Columns.Start > 0)
            Request(grid.Rows[0].Ordinal, new GridRange(Math.Max(0, Columns.Start - 63), 64));
    }

    private bool Input(uint message, nuint key, nint coordinates)
    {
        if (_navigation is not null && message is 0x020A or 0x020E)
        {
            ref var remainder = ref (message == 0x020A ? ref _rowWheelRemainder : ref _columnWheelRemainder);
            remainder += unchecked((short)((key >> 16) & 0xffff));
            var ticks = remainder / 120;
            remainder %= 120;
            if (ticks != 0) Navigate(message == 0x020A ? NativeGridAxis.Rows : NativeGridAxis.Columns,
                message == 0x020A ? -3L * ticks : 3L * ticks);
            return true;
        }
        if (_navigation is { } navigation && message == Win32.WM_KEYDOWN)
        {
            if (key == 0x23) { Submit(NativeGridTargetKind.End, navigation.Rows.First, navigation.Columns.First); return true; }
            if (key == 0x74) { Submit(NativeGridTargetKind.Retry, navigation.Rows.First, navigation.Columns.First); return true; }
            if (key is 0x21 or 0x22)
            {
                Navigate(NativeGridAxis.Rows, key == 0x21 ? -navigation.Rows.Page : navigation.Rows.Page);
                return true;
            }
        }
        if (_identity is null || _grid is not { Rows.Count: > 0 } grid) return false;
        if (message == Win32.WM_COPY) { Copy(); return true; }
        if (message is Win32.WM_CUT or Win32.WM_PASTE or Win32.WM_CLEAR) return true;
        if (message == 0x007B) { ContextMenu(); return true; }
        if (message == Win32.WM_CHAR && key is 13 or 32) return true;
        if (message == Win32.WM_LBUTTONDOWN)
        {
            var hit = new WindowsGridInterop.Hit
            { Point = new Win32.Point { X = unchecked((short)((long)coordinates & 0xffff)), Y = unchecked((short)(((long)coordinates >> 16) & 0xffff)) } };
            Send(WindowsGridInterop.HitTest, 0, ref hit);
            if ((uint)hit.Row < (uint)_slots.Length && _slots[hit.Row] is not null && (uint)hit.Column <= (uint)Columns.Count)
            {
                Win32.SetFocus(Handle);
                _wholeRows = hit.Column == 0;
                Select(_slots[hit.Row]!.Ordinal, Columns.Start + Math.Max(0, hit.Column - 1), (key & 4) != 0);
                return true;
            }
        }
        if (message == 0x020A) // Wheel at bounded cache edge rebases to a new overlapping ready window.
        {
            var delta = unchecked((short)((key >> 16) & 0xffff));
            var top = (int)Win32.SendMessageW(Handle, WindowsGridInterop.First + 39, 0, 0);
            var visible = (int)Win32.SendMessageW(Handle, WindowsGridInterop.First + 40, 0, 0);
            if (delta < 0 && top + visible >= grid.Rows.Count - 1) Request(grid.Rows[^1].Ordinal, Columns);
            if (delta > 0 && top == 0 && grid.Rows[0].Ordinal > 0)
                Request(Math.Max(0, grid.Rows[0].Ordinal - Math.Max(1, grid.RequestedRows.Count - 1)), Columns);
        }
        if (message != Win32.WM_KEYDOWN) return false;
        var shift = Win32.GetKeyState(0x10) < 0;
        switch (key)
        {
            case 13: case 32: Emit(NativeGridIntentKind.Reveal); return true;
            case 0x71: Emit(NativeGridIntentKind.Replace); return true; // F2
            case 0x43 when Win32.GetKeyState(0x11) < 0: Copy(); return true;
            case 0x41 when Win32.GetKeyState(0x11) < 0: SelectAll(); return true;
            case 0x25: Move(0, -1, shift); return true;
            case 0x27: Move(0, 1, shift); return true;
            case 0x26: Move(-1, 0, shift); return true;
            case 0x28: Move(1, 0, shift); return true;
            case 0x21: Move(-Math.Max(1, grid.Rows.Count - 1), 0, shift); return true;
            case 0x22: Move(Math.Max(1, grid.Rows.Count - 1), 0, shift); return true;
        }
        return false;
    }

    private void Move(int rows, int columns, bool extend)
    {
        if (_grid is not { Rows.Count: > 0 } grid) return;
        var origin = _accessibleFocusedCell ?? new GridCoordinate(_row, _column);
        var row = Math.Max(0L, (long)origin.Row + rows);
        var column = Math.Max(0L, (long)origin.Column + columns);
        if (row > int.MaxValue || column > int.MaxValue) return;
        if (_navigation is { } navigation &&
            (row < navigation.Rows.First || row >= (long)navigation.Rows.First + navigation.Rows.Page ||
             column < navigation.Columns.First || column >= (long)navigation.Columns.First + navigation.Columns.Page))
        {
            _pendingSelection = ((int)row, (int)column, extend);
            Submit(NativeGridTargetKind.Cell, (int)row, (int)column);
            return;
        }
        if (row < grid.Rows[0].Ordinal || row > grid.Rows[^1].Ordinal)
        { _pendingSelection = ((int)row, (int)column, extend); Request((int)row, Columns); return; }
        if (column < Columns.Start || column >= Columns.End)
        {
            var start = columns < 0 ? Math.Max(0, Columns.Start - Columns.Count) : (int)column;
            _pendingSelection = ((int)row, (int)column, extend);
            Request((int)row, new GridRange(start, Math.Min(64, int.MaxValue - start))); return;
        }
        if (columns != 0) _wholeRows = false;
        Select((int)row, (int)column, extend);
    }

    private void Select(int row, int column, bool extend)
    {
        _accessibleFocusedCell = null; _tableOnlyFocus = false;
        _row = row; _column = column;
        if (!extend) { _anchorRow = row; _anchorColumn = column; }
        SetNativeFocus(_navigation is null);
        Win32.InvalidateRect(Handle, 0, false);
        Emit(NativeGridIntentKind.Select);
    }

    private void SetNativeFocus(bool reveal = true)
    {
        var installation = _installation;
        if (_grid is not { Rows.Count: > 0 } grid) return;
        var local = -1;
        for (var index = 0; index < _slots.Length; index++)
            if (_slots[index]?.Ordinal == _row) { local = index; break; }
        var item = new WindowsGridInterop.Item { State = 0, StateMask = 3 };
        Send(WindowsGridInterop.SetItemState, unchecked((nuint)(nint)(-1)), ref item);
        if (installation != _installation) return;
        if ((uint)local >= (uint)_slots.Length) return;
        item.State = 1; // Native focused row only; cell selection is drawn independently.
        Send(WindowsGridInterop.SetItemState, (nuint)local, ref item);
        if (installation != _installation) return;
        if (reveal) Win32.SendMessageW(Handle, WindowsGridInterop.EnsureVisible, (nuint)local, 0);
    }

    private void Request(int row, GridRange columns)
    {
        if (_navigation is not null) { Submit(NativeGridTargetKind.Cell, row, columns.Start); return; }
        if (_identity is not { } identity || _grid is not { } grid) return;
        if (grid.Extent.ExactRowCount is { } count && row >= count ||
            grid.Extent.ExactMaxWidth is { } width && columns.Start >= width) return;
        var request = new NativeGridWindowRequest(identity, row, columns,
            Math.Min(256, Math.Max(1, Math.Min(128, 8192 / Math.Max(1, columns.Count)))));
        if (_requested == request) return;
        _requested = request;
        WindowRequested?.Invoke(request);
    }

    private void Emit(NativeGridIntentKind kind)
    {
        if (_identity is not { } identity || _grid is not { Rows.Count: > 0 }) return;
        if (kind == NativeGridIntentKind.Select) _selectionCleared = false;
        PublishAccessibility();
        var single = kind is NativeGridIntentKind.Reveal or NativeGridIntentKind.Replace or NativeGridIntentKind.CopyValue or NativeGridIntentKind.CopySource;
        var commandCell = kind is NativeGridIntentKind.Reveal or NativeGridIntentKind.Replace ?
            _accessibleFocusedCell ?? new GridCoordinate(_row, _column) : new GridCoordinate(_row, _column);
        IntentRequested?.Invoke(single ? new NativeGridIntent(identity, kind, commandCell.Row, commandCell.Column) :
            new NativeGridIntent(identity, kind, _anchorRow, _anchorColumn, _row, _column, _wholeRows));
    }

    /// <summary>Explicit Copy formats distinguish decoded values, quoted tabular text and exact source syntax.</summary>
    private void ContextMenu()
    {
        if (_identity is not { } identity || _grid is not { } grid) return;
        var navigationFrame = _navigation;
        var frozen = new NativeGridIntent(identity, NativeGridIntentKind.Select,
            _anchorRow, _anchorColumn, _row, _column, _wholeRows);
        var active = new NativeGridIntent(identity, NativeGridIntentKind.Select, _row, _column);
        if (!WindowsGridInterop.GetCursorPos(out var point)) return;
        var menu = Win32.CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            Win32.AppendMenuW(menu, 0, 1, "Reveal cell in source");
            Win32.AppendMenuW(menu, 0, 2, "Copy decoded value");
            Win32.AppendMenuW(menu, 0, 3, "Copy cells as quoted TSV");
            Win32.AppendMenuW(menu, 0, 4, "Copy cells as CSV");
            Win32.AppendMenuW(menu, 0, 5, "Copy cells as padded CSV");
            Win32.AppendMenuW(menu, 0, 6, "Copy exact cell source");
            Win32.AppendMenuW(menu, 0, 7, "Copy exact selected rows");
            Win32.AppendMenuW(menu, 0, 8, "Replace cell…");
            Win32.AppendMenuW(menu, 0, 9, "Go to row:column…");
            Win32.AppendMenuW(menu, 0, 10, "Follow source selection");
            var command = WindowsGridInterop.TrackPopupMenu(menu, 0x0100 | 0x0002,
                point.X, point.Y, 0, Handle, 0);
            if (command == 9)
            {
                // The frozen menu navigation frame must still be current before opening its dialog.
                if (ReferenceEquals(_navigation, navigationFrame)) GoToCoordinate();
                return;
            }
            if (command == 10)
            {
                WindowRequested?.Invoke(new NativeGridWindowRequest(identity, active.Row,
                    Columns, FollowSource: true));
                return;
            }
            var kind = command switch
            {
                1 => NativeGridIntentKind.Reveal, 2 => NativeGridIntentKind.CopyValue,
                3 => NativeGridIntentKind.CopyTsv, 4 => NativeGridIntentKind.CopyCsv,
                5 => NativeGridIntentKind.CopyCsvPadded, 6 => NativeGridIntentKind.CopySource,
                7 => NativeGridIntentKind.CopyRows, 8 => NativeGridIntentKind.Replace,
                _ => (NativeGridIntentKind?)null
            };
            if (kind is { } value)
            {
                if (_selectionCleared && value is not (NativeGridIntentKind.Reveal or NativeGridIntentKind.Replace)) return;
                var single = value is NativeGridIntentKind.Reveal or NativeGridIntentKind.Replace or NativeGridIntentKind.CopyValue or NativeGridIntentKind.CopySource;
                IntentRequested?.Invoke((single ? active : frozen) with { Kind = value });
            }
        }
        finally { WindowsGridInterop.DestroyMenu(menu); }
    }

    /// <summary>Publishes adapter-owned facts after native installation/selection is coherent.</summary>
    private void PublishAccessibility(bool geometryChanged = false)
    {
        if (_installing || Handle == 0 || _uia is null) return;
        var document = _navigation?.Navigation.Document ?? _installedIdentity?.Document;
        if (document is null)
        {
            _accessibleFrame = null; _uia.Clear();
            _accessibleRowScroller.Publish(null, false); _accessibleColumnScroller.Publish(null, false);
            _accessibleFocusedCell = null; _tableOnlyFocus = false;
            return;
        }
        var rows = _navigation is { } nav ? new GridRange(nav.RequestedRows.Start, _slots.Length) :
            new GridRange(_grid?.RequestedRows.Start ?? 0, _slots.Length);
        var previous = _accessibleFrame;
        var retired = previous is null || previous.Id.Document != document || previous.Rows != rows ||
            previous.Columns != Columns || previous.Ready != _identity || !ReferenceEquals(previous.Projection, _identity is null ? null : _grid);
        var id = retired ? new GridAccessibilityId(document.Value, ++_accessibleSerial) : previous!.Id;
        GridAccessibleSelection? selection = _selectionCleared || _selectionDocument != document ? null :
            new(new(_anchorRow, _anchorColumn), new(_row, _column), _wholeRows);
        GridCoordinate? focused = _tableOnlyFocus ? null : _accessibleFocusedCell ?? new GridCoordinate(_row, _column);
        var hasFocus = WindowsGridInterop.GetFocus() == Handle;
        var installation = _installation;
        var frame = NativeGridAccessibility.Create(id, _navigation, _identity, _grid, rows, Columns, selection, focused, hasFocus);
        if (frame.Rows.Count == 0) _selectionCleared = true;
        _accessibleFrame = frame;
        _accessibleGroup.Publish(frame.Status + "; F6/Shift+F6 cycles source, table, row navigation, column navigation, Go to CSV cell; Tab remains source editing");
        _accessibleStatus.Publish(frame.Status);
        WindowsGridInterop.SetWindowTextW(_statusHandle, "CSV grid status: " + frame.Status);
        _accessibleRowScroller.Publish(_navigation, WindowsGridInterop.GetFocus() == RowScroller);
        _accessibleColumnScroller.Publish(_navigation, WindowsGridInterop.GetFocus() == ColumnScroller);
        WindowsGridInterop.SetWindowTextW(RowScroller, _navigation?.Rows.Kind == NativeGridExtentKind.Exact ? "File rows" : "Indexed prefix rows");
        WindowsGridInterop.SetWindowTextW(ColumnScroller, _navigation?.Columns.Kind == NativeGridExtentKind.Exact ? "File columns" : "Known columns");
        if (_accessibilityEnabled) _uia.Publish(frame, geometryChanged, () => installation == _installation && ReferenceEquals(_accessibleFrame, frame));
    }

    /// <summary>Exact full-retained selection admission, without source intent dispatch or focus theft.</summary>
    public GridAccessibilityResult MutateSelection(GridAccessibilityId id, GridSelectionMutation mutation)
    {
        if (Environment.CurrentManagedThreadId != _uiThread) return SendAccessibilityRequest(id, mutation);
        if (_installing) return GridAccessibilityResult.Stale;
        if (_isCompositionActive()) return GridAccessibilityResult.CompositionBlocked;
        if (_admittingAccessibilityRequest?.Expired == true) return GridAccessibilityResult.Unavailable;
        var frame = _accessibleFrame;
        if (frame is null || Handle == 0) return GridAccessibilityResult.Unavailable;
        if (frame.Id != id) return GridAccessibilityResult.Stale;
        var valid = mutation switch
        {
            GridSelectionMutation.ReplaceRectangle replace => frame.Contains(replace.Selection.Anchor) && frame.Contains(replace.Selection.Active),
            GridSelectionMutation.AddCell add => frame.Contains(add.Cell),
            GridSelectionMutation.RemoveCell remove => frame.Contains(remove.Cell),
            GridSelectionMutation.Clear => true,
            _ => false
        };
        if (!valid) return GridAccessibilityResult.InvalidCoordinate;
        var result = NativeGridAccessibility.Mutate(frame.Selection, mutation, out var next);
        if (result != GridAccessibilityResult.Applied) return result;
        var request = _admittingAccessibilityRequest;
        if (request is null) CommitAccessibleSelection(frame, next);
        else
        {
            lock (request.Gate)
            {
                if (request.Expired) return GridAccessibilityResult.Unavailable;
                CommitAccessibleSelection(frame, next);
                request.Completed = GridAccessibilityResult.Applied;
            }
        }
        // Cancel old Copy before native labels/events can pump a reentrant completion.
        if (_accessibleFrame?.Id == id && _accessibleFrame.Selection == next && frame.Ready is { } ready)
        {
            var active = next?.Active ?? (frame.Rows.Count > 0 && frame.Columns.Count > 0 ? frame.Coordinate(0, 0) : (GridCoordinate?)null);
            if (active is { } coordinate) IntentRequested?.Invoke(new NativeGridIntent(ready, NativeGridIntentKind.Select,
                next?.Anchor.Row ?? coordinate.Row, next?.Anchor.Column ?? coordinate.Column,
                coordinate.Row, coordinate.Column, next?.WholeRows ?? false));
        }
        if (_accessibleFrame?.Id != id || _accessibleFrame.Selection != next) return GridAccessibilityResult.Stale;
        PublishAccessibility();
        if (_accessibilityEnabled) _uia.NotifySelection(id);
        if (_accessibleFrame?.Id != id || _accessibleFrame.Selection != next) return GridAccessibilityResult.Stale;
        Win32.InvalidateRect(Handle, 0, false);
        return GridAccessibilityResult.Applied;
    }

    /// <summary>Callback-free selection publication; timeout observes either cancellation or these complete facts.</summary>
    private void CommitAccessibleSelection(GridAccessibilityFrame frame, GridAccessibleSelection? next)
    {
        if (next is { } selected)
        {
            _anchorRow = selected.Anchor.Row; _anchorColumn = selected.Anchor.Column;
            _row = selected.Active.Row; _column = selected.Active.Column; _wholeRows = selected.WholeRows;
        }
        _selectionCleared = next is null; _selectionDocument = frame.Id.Document;
        var focused = _tableOnlyFocus ? (GridCoordinate?)null : _accessibleFocusedCell ?? new GridCoordinate(_row, _column);
        var publication = NativeGridAccessibility.Create(frame.Id, frame.Navigation, frame.Ready, frame.Projection,
            frame.Rows, frame.Columns, next, focused, frame.HasTableFocus);
        _accessibleFrame = publication;
        _accessibleGroup.Publish(publication.Status + "; F6/Shift+F6 cycles CSV navigation panes; Tab remains source editing");
        _accessibleStatus.Publish(publication.Status);
        if (_accessibilityEnabled) _uia.Publish(publication, raiseEvents: false);
    }

    /// <summary>Focuses only this table, guarding composition and reentrant installation replacement.</summary>
    public GridAccessibilityResult Focus(GridAccessibilityId id, GridCoordinate? cell)
    {
        if (Environment.CurrentManagedThreadId != _uiThread) return GridAccessibilityResult.Unsupported;
        if (_installing) return GridAccessibilityResult.Stale;
        if (_isCompositionActive()) return GridAccessibilityResult.CompositionBlocked;
        if (_admittingAccessibilityRequest?.Expired == true) return GridAccessibilityResult.Unavailable;
        var frame = _accessibleFrame;
        if (frame is null || Handle == 0) return GridAccessibilityResult.Unavailable;
        if (frame.Id != id) return GridAccessibilityResult.Stale;
        if (cell is { } coordinate && !frame.Contains(coordinate)) return GridAccessibilityResult.InvalidCoordinate;
        if (cell is { } pending && frame.Cell(pending).State == GridValueState.Pending) return GridAccessibilityResult.NotReady;
        if (_admittingAccessibilityRequest?.Expired == true) return GridAccessibilityResult.Unavailable;
        var unchanged = WindowsGridInterop.GetFocus() == Handle && frame.FocusedCell == cell;
        Win32.SetFocus(Handle);
        if (_accessibleFrame?.Id != id) return GridAccessibilityResult.Stale;
        if (_admittingAccessibilityRequest?.Expired == true) return GridAccessibilityResult.Unavailable;
        if (WindowsGridInterop.GetFocus() != Handle) return GridAccessibilityResult.Unavailable;
        _accessibleFocusedCell = cell; _tableOnlyFocus = cell is null;
        PublishAccessibility();
        if (_accessibleFrame?.Id != id || _accessibleFrame.FocusedCell != cell || WindowsGridInterop.GetFocus() != Handle) return GridAccessibilityResult.Stale;
        return unchanged ? GridAccessibilityResult.NoChange : GridAccessibilityResult.Applied;
    }

    /// <summary>Synchronously admits COM-thread actions on the HWND owner, bounded to 500 ms.</summary>
    private GridAccessibilityResult SendAccessibilityRequest(GridAccessibilityId id, GridSelectionMutation mutation)
    {
        if (Interlocked.CompareExchange(ref _accessibleRequestActive, 1, 0) != 0) return GridAccessibilityResult.Unsupported;
        var handle = Handle;
        if (handle == 0) { Volatile.Write(ref _accessibleRequestActive, 0); return GridAccessibilityResult.Unavailable; }
        var serial = Interlocked.Increment(ref _accessibleRequestSerial);
        var request = new AccessibilityRequest(id, mutation, Stopwatch.GetTimestamp() + Stopwatch.Frequency / 2);
        _accessibleRequests[serial] = request;
        try
        {
            var completed = WindowsGridInterop.SendMessageTimeoutW(handle, AccessibilityRequestMessage, (nuint)serial, 0,
                0x0002 | 0x0020, 500, out var result); // ABORTIFHUNG | ERRORONEXIT; never block indefinitely.
            if (completed != 0) return (GridAccessibilityResult)(int)result;
            lock (request.Gate)
            {
                Volatile.Write(ref request.Cancelled, 1);
                return request.Completed ?? GridAccessibilityResult.Unavailable;
            }
        }
        finally
        {
            Volatile.Write(ref request.Cancelled, 1);
            _accessibleRequests.TryRemove(serial, out _);
            Volatile.Write(ref _accessibleRequestActive, 0);
        }
    }

    /// <summary>Late native dispatch cannot recover a timed-out token or mutate a stale installation.</summary>
    private GridAccessibilityResult AdmitAccessibilityRequest(long serial)
    {
        if (!_accessibleRequests.TryGetValue(serial, out var request) || request.Expired) return GridAccessibilityResult.Unavailable;
        var previous = _admittingAccessibilityRequest;
        _admittingAccessibilityRequest = request;
        try { return MutateSelection(request.Id, request.Mutation); }
        finally { _admittingAccessibilityRequest = previous; }
    }

    /// <summary>Uses native subitem/header rectangles and clips them to the actual visible table client.</summary>
    private UiaRect AccessibleBounds(int kind, int row, int column)
    {
        if (Handle == 0 || !WindowsGridInterop.IsWindowVisible(Handle) || !Win32.GetClientRect(Handle, out var client)) return default;
        var table = ScreenRectangle(Handle, client);
        if (kind == 0) return table;
        Win32.Rect rectangle;
        nint window = Handle;
        if (kind == 3)
        {
            window = Win32.SendMessageW(Handle, WindowsGridInterop.First + 31, 0, 0);
            rectangle = default;
            unsafe
            {
                if (Win32.SendMessageW(window, 0x1207, (nuint)(column + 1), (nint)(&rectangle)) == 0) return default;
            }
        }
        else
        {
            rectangle = new Win32.Rect { Top = kind == 2 ? 0 : column + 1, Left = 0 };
            unsafe
            {
                if (Win32.SendMessageW(Handle, WindowsGridInterop.First + 56, (nuint)row, (nint)(&rectangle)) == 0) return default;
            }
            if (kind == 2) rectangle.Right = rectangle.Left + (int)Win32.SendMessageW(Handle, WindowsGridInterop.First + 29, 0, 0);
        }
        var bounds = ScreenRectangle(window, rectangle);
        var left = Math.Max(table.Left, bounds.Left); var top = Math.Max(table.Top, bounds.Top);
        var right = Math.Min(table.Left + table.Width, bounds.Left + bounds.Width);
        var bottom = Math.Min(table.Top + table.Height, bounds.Top + bounds.Height);
        return right <= left || bottom <= top ? default : new(left, top, right - left, bottom - top);
    }

    /// <summary>Converts HWND client coordinates without interpolating source offsets.</summary>
    private static UiaRect ScreenRectangle(nint window, Win32.Rect rectangle)
    {
        var point = new Win32.Point { X = rectangle.Left, Y = rectangle.Top };
        if (!WindowsGridInterop.ClientToScreen(window, ref point)) return default;
        return new(point.X, point.Y, Math.Max(0, rectangle.Right - rectangle.Left), Math.Max(0, rectangle.Bottom - rectangle.Top));
    }

    /// <summary>Opens the actual coordinate command; navigation is requested, never reported as decoded readiness.</summary>
    private void GoToCell() => GoToCoordinate();

    private static uint Color(ThemeColor color) => (uint)(color.Red | color.Green << 8 | color.Blue << 16);
    private void Report(Exception error) { try { Faulted?.Invoke(error); } catch { /* No exception may cross the native callback. */ } }
    private unsafe nint Send<T>(int message, nuint parameter, ref T value) where T : unmanaged
    { fixed (T* pointer = &value) return Win32.SendMessageW(Handle, message, parameter, (nint)pointer); }

    /// <summary>Removes the native callback before releasing its root; no caller-owned buffer survives.</summary>
    public void Dispose()
    {
        Volatile.Write(ref _focusEvidenceAlive, false);
        ++_installation;
        _sourceHandle = 0;
        foreach (var request in _accessibleRequests.Values) Volatile.Write(ref request.Cancelled, 1);
        _accessibleRequests.Clear();
        _uia?.Detach(); _accessibleGroup?.Detach(); _accessibleStatus?.Detach(); _accessibleRowScroller?.Detach(); _accessibleColumnScroller?.Detach(); _accessibleFrame = null;
        _gesture = null; _navigation = null; _coordinateSelection = null;
        _identity = null; _installedIdentity = null; _selectionDocument = null; _grid = null;
        if (RowScroller != 0) Win32.DestroyWindow(RowScroller);
        if (ColumnScroller != 0) Win32.DestroyWindow(ColumnScroller);
        RowScroller = ColumnScroller = 0;
        if (Handle != 0)
        {
            Win32.RemoveWindowSubclass(Handle, Procedure, 1);
            Win32.DestroyWindow(Handle);
            Handle = 0;
        }
        if (_groupHandle != 0) Win32.DestroyWindow(_groupHandle);
        _groupHandle = _goToHandle = _statusHandle = 0;
        if (_root.IsAllocated) _root.Free();
        if (_ownsComApartment) { WindowsGridInterop.CoUninitialize(); _ownsComApartment = false; }
    }
}
