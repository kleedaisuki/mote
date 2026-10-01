using System;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>Small external Win32 observer for the opt-in native canvas benchmark.</summary>
/// <remarks>It does not load mote assemblies or inspect its private controller state.</remarks>
public static class MoteCanvasGuiProbe
{
    /// <summary>Callback used to enumerate top-level windows for one process.</summary>
    public delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

    /// <summary>Win32 scrollbar state; Position is normalized by mote's canvas.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ScrollInfo
    {
        /// <summary>ABI size required by GetScrollInfo.</summary>
        public uint Size;
        /// <summary>Fields requested from GetScrollInfo.</summary>
        public uint Mask;
        /// <summary>Minimum scroll coordinate.</summary>
        public int Minimum;
        /// <summary>Maximum scroll coordinate.</summary>
        public int Maximum;
        /// <summary>Page size in normalized units.</summary>
        public uint Page;
        /// <summary>Current normalized scroll coordinate.</summary>
        public int Position;
        /// <summary>Thumb-tracking coordinate.</summary>
        public int TrackPosition;
    }

    /// <summary>Win32 GUI-thread focus state, used to require the real bounded input host.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GuiThreadInfo
    {
        /// <summary>ABI size required by GetGUIThreadInfo.</summary>
        public uint Size;
        /// <summary>GUI-thread flags.</summary>
        public uint Flags;
        /// <summary>Active top-level window.</summary>
        public IntPtr Active;
        /// <summary>Focused child window.</summary>
        public IntPtr Focus;
        /// <summary>Pointer-capturing child window.</summary>
        public IntPtr Capture;
        /// <summary>Active menu owner.</summary>
        public IntPtr MenuOwner;
        /// <summary>Window currently being moved or resized.</summary>
        public IntPtr MoveSize;
        /// <summary>Native caret owner.</summary>
        public IntPtr Caret;
        /// <summary>Native caret rectangle.</summary>
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr window, StringBuilder buffer, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr window, StringBuilder buffer, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string className, string title);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDlgItem(IntPtr parent, int controlId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessageW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool PostMessageW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern bool GetScrollInfo(IntPtr window, int bar, ref ScrollInfo info);

    /// <summary>Queries a cross-process child control via WM_GETTEXTLENGTH.</summary>
    /// <remarks>GetWindowTextLengthW cannot reliably read another process's child text.</remarks>
    public static int InputLength(IntPtr window) =>
        checked((int)SendMessageW(window, 0x000E, UIntPtr.Zero, IntPtr.Zero));

    /// <summary>Finds a top-level native editor window belonging to an exact child PID.</summary>
    public static IntPtr EditorWindow(uint processId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != processId) return true;
            var name = new StringBuilder(128);
            GetClassNameW(window, name, name.Capacity);
            if (name.ToString() != "MoteNativeEditorWindow") return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Reads only native window chrome, never the user's document body.</summary>
    public static string WindowTitle(IntPtr window)
    {
        var title = new StringBuilder(512);
        GetWindowTextW(window, title, title.Capacity);
        return title.ToString();
    }

    /// <summary>Gets the focused native child of the editor's GUI thread.</summary>
    public static IntPtr FocusedChild(IntPtr main)
    {
        var thread = GetWindowThreadProcessId(main, out _);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(thread, ref info) ? info.Focus : IntPtr.Zero;
    }

    /// <summary>Reads the vertical canvas scrollbar without commanding an internal model seek.</summary>
    public static int VerticalPosition(IntPtr canvas)
    {
        var info = new ScrollInfo { Size = (uint)Marshal.SizeOf<ScrollInfo>(), Mask = 0x17 };
        if (!GetScrollInfo(canvas, 1, ref info))
            throw new InvalidOperationException("Canvas vertical scrollbar is unavailable.");
        return info.Position;
    }
}
