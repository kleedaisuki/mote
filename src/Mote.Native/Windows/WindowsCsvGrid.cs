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
    private GridRenderProjection? _grid;
    private NativePresentationId? _identity;
    private NativeGridWindowRequest? _requested;
    private IThemePolicy _theme;
    private readonly nint _parent;
    private int _columns, _row, _column, _anchorRow, _anchorColumn;
    private bool _installing;
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
        SetTheme(theme);
    }

    /// <summary>The actual owner-data report HWND, not a text preview.</summary>
    internal nint Handle { get; private set; }
    /// <summary>Whether keyboard focus belongs to this detached native table.</summary>
    internal bool HasFocus => Handle != 0 && WindowsGridInterop.GetFocus() == Handle;
    /// <summary>Ready-cache selection/action; the controller validates exact installation identity.</summary>
    internal event Action<NativeGridIntent>? IntentRequested;
    /// <summary>Coalesced bounded coordinate request, never synchronous data loading.</summary>
    internal event Action<NativeGridWindowRequest>? WindowRequested;
    /// <summary>Reports callback failures without throwing across an unmanaged stack.</summary>
    internal event Action<Exception>? Faulted;

    /// <summary>Installs a bounded immutable table; null immediately retires its command authority.</summary>
    internal void Install(GridRenderProjection? grid, NativePresentationId identity)
    {
        var sameDocument = _identity?.Document == identity.Document;
        var selectedPending = false;
        _identity = null;
        _installing = true;
        try
        {
            Win32.SendMessageW(Handle, WindowsGridInterop.SetItemCount, 0, 0);
            for (var index = _columns - 1; index >= 0; index--)
                Win32.SendMessageW(Handle, WindowsGridInterop.DeleteColumn, (nuint)index, 0);
            _columns = 0;
            _grid = grid;
            _requested = null;
            if (grid is null) { _pendingSelection = null; return; }
            if (grid.Version != identity.Document.Version) throw new ArgumentException("Grid identity version mismatch.");
            InsertColumn(0, -1);
            for (var index = 0; index < grid.RequestedColumns.Count; index++)
                InsertColumn(index + 1, grid.RequestedColumns.Start + index);
            _columns = grid.RequestedColumns.Count + 1;
            _identity = identity;
            if (!sameDocument)
            {
                _row = grid.Rows.Count == 0 ? grid.RequestedRows.Start : grid.Rows[0].Ordinal;
                _column = grid.RequestedColumns.Start;
                _anchorRow = _row; _anchorColumn = _column; _wholeRows = false;
            }
            if (_pendingSelection is { } pending && grid.Rows.Any(row => row.Ordinal == pending.Row) &&
                pending.Column >= grid.RequestedColumns.Start && pending.Column < grid.RequestedColumns.End)
            {
                _row = pending.Row; _column = pending.Column;
                if (!pending.Extend) { _anchorRow = _row; _anchorColumn = _column; }
                _pendingSelection = null;
                selectedPending = true;
            }
            Win32.SendMessageW(Handle, WindowsGridInterop.SetItemCount, (nuint)grid.Rows.Count, 0);
            SetNativeFocus();
        }
        finally { _installing = false; }
        Win32.InvalidateRect(Handle, 0, false);
        if (selectedPending && _identity == identity) Emit(NativeGridIntentKind.Select);
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
        _anchorRow = grid.Rows[0].Ordinal; _anchorColumn = grid.RequestedColumns.Start;
        Select(grid.Rows[^1].Ordinal, grid.RequestedColumns.End - 1, true);
    }

    /// <summary>Requests distant logical coordinates without synthesizing a whole-file native item count.</summary>
    internal void GoToCoordinate()
    {
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
        WindowRequested?.Invoke(new NativeGridWindowRequest(identity, row,
            new GridRange(column, Math.Min(64, int.MaxValue - column))));
        return true;
    }

    /// <summary>Handles only notifications from this table; buffers are caller-owned and never retained.</summary>
    internal bool HandleNotify(nint notification, out nint result)
    {
        result = 0;
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
        if (_identity is not null && _grid is { } grid && (uint)info.Item.Row < (uint)grid.Rows.Count &&
            (uint)info.Item.Column <= (uint)grid.RequestedColumns.Count)
        {
            var row = grid.Rows[info.Item.Row];
            label = info.Item.Column == 0 ? (row.Ordinal + 1L).ToString(System.Globalization.CultureInfo.InvariantCulture) :
                NativeCsvGrid.Display(grid, NativeCsvGrid.Cell(row, grid.RequestedColumns.Start + info.Item.Column - 1));
        }
        CopyLabel(label, info.Item.Text, info.Item.TextCapacity);
    }

    private nint Draw(nint notification)
    {
        var draw = Marshal.PtrToStructure<WindowsGridInterop.Draw>(notification);
        if (draw.Stage == 1) return 0x20; // CDDS_PREPAINT -> notify item draw
        if (draw.Stage == 0x10001) return 0x20; // item prepaint -> notify subitems
        if (draw.Stage != 0x30001 || _grid is not { } grid || draw.Item >= (nuint)grid.Rows.Count) return 0;
        var row = grid.Rows[(int)draw.Item].Ordinal;
        var column = grid.RequestedColumns.Start + draw.Column - 1;
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
            if (forward && atEnd) Request(grid.Rows[^1].Ordinal, grid.RequestedColumns);
            if (backward && info.Position == info.Minimum && grid.Rows[0].Ordinal > 0)
                Request(Math.Max(0, grid.Rows[0].Ordinal - Math.Max(1, grid.RequestedRows.Count - 1)), grid.RequestedColumns);
            return;
        }
        if (forward && atEnd && grid.RequestedColumns.End < int.MaxValue)
        {
            var start = grid.RequestedColumns.End - 1;
            Request(grid.Rows[0].Ordinal, new GridRange(start, Math.Min(64, int.MaxValue - start)));
        }
        if (backward && info.Position == info.Minimum && grid.RequestedColumns.Start > 0)
            Request(grid.Rows[0].Ordinal, new GridRange(Math.Max(0, grid.RequestedColumns.Start - 63), 64));
    }

    private bool Input(uint message, nuint key, nint coordinates)
    {
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
            if ((uint)hit.Row < (uint)grid.Rows.Count && (uint)hit.Column <= (uint)grid.RequestedColumns.Count)
            {
                Win32.SetFocus(Handle);
                _wholeRows = hit.Column == 0;
                Select(grid.Rows[hit.Row].Ordinal, grid.RequestedColumns.Start + Math.Max(0, hit.Column - 1), (key & 4) != 0);
                return true;
            }
        }
        if (message == 0x020A) // Wheel at bounded cache edge rebases to a new overlapping ready window.
        {
            var delta = unchecked((short)((key >> 16) & 0xffff));
            var top = (int)Win32.SendMessageW(Handle, WindowsGridInterop.First + 39, 0, 0);
            var visible = (int)Win32.SendMessageW(Handle, WindowsGridInterop.First + 40, 0, 0);
            if (delta < 0 && top + visible >= grid.Rows.Count - 1) Request(grid.Rows[^1].Ordinal, grid.RequestedColumns);
            if (delta > 0 && top == 0 && grid.Rows[0].Ordinal > 0)
                Request(Math.Max(0, grid.Rows[0].Ordinal - Math.Max(1, grid.RequestedRows.Count - 1)), grid.RequestedColumns);
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
        if (row < grid.Rows[0].Ordinal || row > grid.Rows[^1].Ordinal)
        { _pendingSelection = ((int)row, (int)column, extend); Request((int)row, grid.RequestedColumns); return; }
        if (column < grid.RequestedColumns.Start || column >= grid.RequestedColumns.End)
        {
            var start = columns < 0 ? Math.Max(0, grid.RequestedColumns.Start - grid.RequestedColumns.Count) : (int)column;
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
        SetNativeFocus();
        Win32.InvalidateRect(Handle, 0, false);
        Emit(NativeGridIntentKind.Select);
    }

    private void SetNativeFocus()
    {
        if (_grid is not { Rows.Count: > 0 } grid) return;
        var local = -1;
        for (var index = 0; index < grid.Rows.Count; index++)
            if (grid.Rows[index].Ordinal == _row) { local = index; break; }
        var item = new WindowsGridInterop.Item { State = 0, StateMask = 3 };
        Send(WindowsGridInterop.SetItemState, unchecked((nuint)(nint)(-1)), ref item);
        if ((uint)local >= (uint)grid.Rows.Count) return;
        item.State = 1; // Native focused row only; cell selection is drawn independently.
        Send(WindowsGridInterop.SetItemState, (nuint)local, ref item);
        Win32.SendMessageW(Handle, WindowsGridInterop.EnsureVisible, (nuint)local, 0);
    }

    private void Request(int row, GridRange columns)
    {
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
            if (command == 9) { CoordinatePrompt(identity, active.Row, active.Column, grid.Extent); return; }
            if (command == 10)
            {
                WindowRequested?.Invoke(new NativeGridWindowRequest(identity, active.Row,
                    grid.RequestedColumns, FollowSource: true));
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
        _identity = null; _grid = null;
        if (Handle != 0)
        {
            Win32.RemoveWindowSubclass(Handle, Procedure, 1);
            Win32.DestroyWindow(Handle);
            Handle = 0;
        }
        if (_root.IsAllocated) _root.Free();
    }
}
