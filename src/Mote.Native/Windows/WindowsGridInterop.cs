using System.Runtime.InteropServices;

namespace Mote.Native.Windows;

/// <summary>Owner-data ListView ABI; all item and column counts remain bounded by the installed Grid.</summary>
internal static class WindowsGridInterop
{
    internal const int First = 0x1000;
    internal const int GetItemCount = First + 4, SetItemCount = First + 47;
    internal const int InsertColumn = First + 97, DeleteColumn = First + 28;
    internal const int GetItem = First + 75, SetItemState = First + 43;
    internal const int HitTest = First + 57, EnsureVisible = First + 19;
    internal const int GetDispInfo = -177, CustomDraw = -12, Click = -2, DoubleClick = -3;

    /// <summary>Initializes only the platform list-view class, without loading a shipped library.</summary>
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitCommonControlsEx(ref Controls controls);
    /// <summary>Current focus is used to route shell accelerators to the detached Grid.</summary>
    [DllImport("user32.dll")]
    internal static extern nint GetFocus();
    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out Win32.Point point);
    [DllImport("user32.dll")]
    internal static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint owner, nint bounds);
    [DllImport("user32.dll")]
    internal static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")]
    internal static extern bool GetScrollInfo(nint window, int bar, ref Scroll info);
    /// <summary>Returns the resulting position; zero is not a failure indication.</summary>
    [DllImport("user32.dll")]
    internal static extern int SetScrollInfo(nint window, int bar, ref Scroll info, bool redraw);
    /// <summary>Suppresses cache-local scrollbars.</summary>
    [DllImport("user32.dll")]
    internal static extern bool ShowScrollBar(nint window, int bar, bool show);
    /// <summary>Disables unavailable coordinate domains.</summary>
    [DllImport("user32.dll")]
    internal static extern bool EnableWindow(nint window, bool enabled);
    /// <summary>Retrieves platform scrollbar geometry.</summary>
    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    /// <summary>Acquires the HWND owner's STA for platform COM callback marshalling.</summary>
    [DllImport("ole32.dll")]
    internal static extern int CoInitializeEx(nint reserved, uint apartment);
    /// <summary>Balances only successful S_OK/S_FALSE apartment acquisitions.</summary>
    [DllImport("ole32.dll")]
    internal static extern void CoUninitialize();
    /// <summary>Bounded synchronous UI-thread admission; timeout never reports queued success.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessageTimeoutW(nint window, uint message, nuint parameter, nint data, uint flags, uint timeout, out nuint result);
    /// <summary>Publishes native control labels from bounded immutable accessibility facts.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool SetWindowTextW(nint window, string text);
    /// <summary>Converts platform cell rectangles to screen coordinates.</summary>
    [DllImport("user32.dll")]
    internal static extern bool ClientToScreen(nint window, ref Win32.Point point);
    /// <summary>Hidden table slots have no visible geometry.</summary>
    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint window);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Controls { internal uint Size, Classes; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Header { internal nint Window; internal nuint Id; internal int Code; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Item
    {
        internal uint Mask; internal int Row, Column; internal uint State, StateMask;
        internal nint Text; internal int TextCapacity, Image; internal nint Parameter;
        internal int Indent, Group, ColumnCount; internal nint Columns, ColumnFormats; internal int GroupIndex;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayInfo { internal Header Header; internal Item Item; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Column
    {
        internal uint Mask; internal int Format, Width; internal nint Text;
        internal int TextCapacity, SubItem, Image, Order, MinimumWidth, DefaultWidth, IdealWidth;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Hit
    {
        internal Win32.Point Point; internal uint Flags; internal int Row, Column, Group;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Draw
    {
        internal Header Header; internal uint Stage; internal nint Dc; internal Win32.Rect Bounds;
        internal nuint Item; internal uint State; internal nint Parameter;
        internal uint Foreground, Background; internal int Column;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Scroll
    {
        internal uint Size, Mask; internal int Minimum, Maximum; internal uint Page;
        internal int Position, TrackPosition;
    }
}
