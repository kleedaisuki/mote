using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Formats;
using Mote.Themes;

namespace Mote.Native.Windows;

/// <summary>
/// Read-only, owner-data native table over a ready immutable bounded Grid. Callbacks
/// never access the engine, parse CSV, decode source, or retain a whole-file mirror.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsCsvGrid : IDisposable
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
    private int _columns, _row, _column, _anchorRow, _anchorColumn;
    private GridRange _nativeColumns;
    private GridRange Columns => _navigation?.RequestedColumns ?? _grid?.RequestedColumns ?? new(0, 0);
    private bool _installing;
    private long _installation;
    private bool _wholeRows;
    private (int Row, int Column, bool Extend)? _pendingSelection;

    /// <summary>Creates a hidden report table; its parent forwards WM_NOTIFY to HandleNotify.</summary>
    internal WindowsCsvGrid(nint parent, int id, IThemePolicy theme)
    {
        _theme = theme;
        _parent = parent;
        var controls = new WindowsGridInterop.Controls
        { Size = (uint)Marshal.SizeOf<WindowsGridInterop.Controls>(), Classes = 1 };
        if (!WindowsGridInterop.InitCommonControlsEx(ref controls)) throw new Win32Exception();
        Handle = Win32.CreateWindowExW(Win32.WS_EX_CLIENTEDGE, "SysListView32", "CSV table",
            Win32.WS_CHILD | Win32.WS_TABSTOP | 0x0001 | 0x1000 | 0x0008,
            0, 0, 100, 100, parent, (nint)id, Win32.GetModuleHandleW(null), 0);
        if (Handle == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        _root = GCHandle.Alloc(this);
        if (!Win32.SetWindowSubclass(Handle, Procedure, 1, (nuint)GCHandle.ToIntPtr(_root)))
        { Dispose(); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        // Grid lines + double buffering. Sorting and header drag/drop are deliberately absent.
        Win32.SendMessageW(Handle, WindowsGridInterop.First + 54, 0, 0x00010001);
        RowScroller = CreateScroller(id + 1000, true);
        ColumnScroller = CreateScroller(id + 1001, false);
        SetTheme(theme);
    }

    /// <summary>The actual owner-data report HWND, not a text preview.</summary>
    internal nint Handle { get; private set; }
    /// <summary>Whether keyboard focus belongs to this detached native table.</summary>
    internal bool HasFocus => Handle != 0 && (WindowsGridInterop.GetFocus() == Handle ||
        WindowsGridInterop.GetFocus() == RowScroller || WindowsGridInterop.GetFocus() == ColumnScroller);
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
                _anchorRow = _row; _anchorColumn = _column; _wholeRows = false;
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
        finally { _installing = false; }
        if (installation != _installation) return;
        Win32.InvalidateRect(Handle, 0, false);
        HideLocalScrollbars();
        if (selectedPending && _identity == identity) Emit(NativeGridIntentKind.Select);
    }

    private nint CreateScroller(int id, bool vertical)
    {
        var handle = Win32.CreateWindowExW(0, "SCROLLBAR", vertical ? "File rows" : "File columns",
            Win32.WS_CHILD | Win32.WS_TABSTOP | (vertical ? 1u : 0u),
            0, 0, 10, 10, _parent, (nint)id, Win32.GetModuleHandleW(null), 0);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (!Win32.SetWindowSubclass(handle, Procedure, 1, (nuint)GCHandle.ToIntPtr(_root)))
        { Win32.DestroyWindow(handle); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        return handle;
    }

    /// <summary>Range authority is independent of ready source-backed command authority.</summary>
    internal void SetNavigation(NativeGridScrollFrame? frame)
    {
        var installation = ++_installation;
        if (_navigation?.Navigation != frame?.Navigation) { _gesture = null; _rowWheelRemainder = _columnWheelRemainder = 0; }
        if (_navigation?.Navigation.Document != frame?.Navigation.Document) _geometry = null;
        var placementChanged = _navigation?.RequestSerial != frame?.RequestSerial;
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
        Win32.InvalidateRect(Handle, 0, false);
    }

    private void RebuildSlots() => _slots = _navigation is { } frame
        ? NativeGridPlanner.Slots(_identity is null ? null : _grid, frame.RequestedRows, frame.Rows.Count)
        : _grid?.Rows.Cast<GridRow?>().ToArray() ?? [];

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
        var bodyHeight = Math.Max(0, height - horizontal);
        Win32.MoveWindow(Handle, x, y, bodyWidth, bodyHeight, true);
        Win32.MoveWindow(RowScroller, x + bodyWidth, y, vertical, bodyHeight, true);
        Win32.MoveWindow(ColumnScroller, x, y + bodyHeight, bodyWidth, horizontal, true);
        HideLocalScrollbars();
        PublishGeometry();
    }

    /// <summary>Shows or hides table and logical scrollers together.</summary>
    internal void Show(bool visible)
    {
        Win32.ShowWindow(Handle, visible ? 5 : 0);
        Win32.ShowWindow(RowScroller, visible && _navigation is not null ? 5 : 0);
        Win32.ShowWindow(ColumnScroller, visible && _navigation is not null ? 5 : 0);
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
    internal void Copy() => Emit(_wholeRows ? NativeGridIntentKind.CopyRows :
        _row == _anchorRow && _column == _anchorColumn ? NativeGridIntentKind.CopyValue : NativeGridIntentKind.CopyTsv);

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
        if (_navigation is { } frame)
        {
            var gesture = Begin();
            if (gesture is null) return;
            var input = Win32TextPrompt.Show(_parent, "Go to CSV cell", "One-based row:column:", $"{_row + 1L}:{_column + 1L}");
            var parts = input?.Split(':');
            if (parts is { Length: 2 } && int.TryParse(parts[0], out var row) && row > 0 &&
                int.TryParse(parts[1], out var column) && column > 0)
                GestureRequested?.Invoke(new(gesture.Id, NativeGridGesturePhase.Commit, row - 1, column - 1, NativeGridTargetKind.Cell));
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
        if (draw.Stage != 0x30001 || _grid is not { } grid || draw.Item >= (nuint)_slots.Length || _slots[(int)draw.Item] is null) return 0;
        var row = _slots[(int)draw.Item]!.Ordinal;
        var column = Columns.Start + draw.Column - 1;
        var selected = row >= Math.Min(_row, _anchorRow) && row <= Math.Max(_row, _anchorRow) &&
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
            if (self is not null && window != self.Handle)
            {
                if (message == 0x001F || message == 0x0215 ||
                    message == Win32.WM_KEYDOWN && wParam == 0x1B) self.CancelGesture();
                return Win32.DefSubclassProc(window, message, wParam, lParam);
            }
            if (self is not null && message == Win32.WM_NOTIFY &&
                self.HandleNotify(lParam, out var notificationResult)) return notificationResult;
            if (self is not null && self.Input(message, wParam, lParam)) return 0;
        }
        catch (Exception error) { self?.Report(error); }
        var result = Win32.DefSubclassProc(window, message, wParam, lParam);
        try { self?.AfterScroll(message, wParam); }
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
        var row = Math.Max(0L, (long)_row + rows);
        var column = Math.Max(0L, (long)_column + columns);
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
        var single = kind is NativeGridIntentKind.Reveal or NativeGridIntentKind.Replace or NativeGridIntentKind.CopyValue or NativeGridIntentKind.CopySource;
        IntentRequested?.Invoke(single ? new NativeGridIntent(identity, kind, _row, _column) :
            new NativeGridIntent(identity, kind, _anchorRow, _anchorColumn, _row, _column, _wholeRows));
    }

    /// <summary>Explicit Copy formats distinguish decoded values, quoted tabular text and exact source syntax.</summary>
    private void ContextMenu()
    {
        if (_identity is not { } identity || _grid is not { } grid) return;
        var navigationFrame = _navigation;
        var navigationPage = MeasurePage();
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
                var navigationGesture = navigationFrame is null ? null : GestureBeginning?.Invoke(
                    new(navigationFrame, navigationPage.Rows, navigationPage.Columns));
                if (navigationFrame is not null && navigationGesture is null) return;
                if (navigationGesture is null) CoordinatePrompt(identity, active.Row, active.Column, grid.Extent);
                else
                {
                    var input = Win32TextPrompt.Show(_parent, "Go to CSV cell", "One-based row:column:", $"{active.Row + 1L}:{active.Column + 1L}");
                    var parts = input?.Split(':');
                    if (parts is { Length: 2 } && int.TryParse(parts[0], out var row) && row > 0 &&
                        int.TryParse(parts[1], out var column) && column > 0)
                        GestureRequested?.Invoke(new(navigationGesture.Id, NativeGridGesturePhase.Commit,
                            row - 1, column - 1, NativeGridTargetKind.Cell));
                    else GestureRequested?.Invoke(new(navigationGesture.Id, NativeGridGesturePhase.Cancel,
                        navigationGesture.Frame.Rows.First, navigationGesture.Frame.Columns.First));
                }
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
                var single = value is NativeGridIntentKind.Reveal or NativeGridIntentKind.Replace or NativeGridIntentKind.CopyValue or NativeGridIntentKind.CopySource;
                IntentRequested?.Invoke((single ? active : frozen) with { Kind = value });
            }
        }
        finally { WindowsGridInterop.DestroyMenu(menu); }
    }

    private static uint Color(ThemeColor color) => (uint)(color.Red | color.Green << 8 | color.Blue << 16);
    private void Report(Exception error) { try { Faulted?.Invoke(error); } catch { /* No exception may cross the native callback. */ } }
    private unsafe nint Send<T>(int message, nuint parameter, ref T value) where T : unmanaged
    { fixed (T* pointer = &value) return Win32.SendMessageW(Handle, message, parameter, (nint)pointer); }

    /// <summary>Removes the native callback before releasing its root; no caller-owned buffer survives.</summary>
    public void Dispose()
    {
        _gesture = null; _navigation = null;
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
        if (_root.IsAllocated) _root.Free();
    }
}
