using System.Runtime.InteropServices;

namespace Mote.Native.Windows;

/// <summary>Win32 and RichEdit declarations used by the Windows-only single-binary shell.</summary>
internal static class Win32
{
    internal const int CW_USEDEFAULT = unchecked((int)0x80000000);
    internal const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    internal const uint WS_CHILD = 0x40000000;
    internal const uint WS_VISIBLE = 0x10000000;
    internal const uint WS_VSCROLL = 0x00200000;
    internal const uint WS_HSCROLL = 0x00100000;
    internal const uint WS_TABSTOP = 0x00010000;
    internal const uint ES_MULTILINE = 0x0004;
    internal const uint ES_AUTOVSCROLL = 0x0040;
    internal const uint ES_AUTOHSCROLL = 0x0080;
    internal const uint ES_NOHIDESEL = 0x0100;
    internal const uint ES_READONLY = 0x0800;
    internal const uint ES_WANTRETURN = 0x1000;
    internal const uint WS_EX_CLIENTEDGE = 0x00000200;
    internal const uint WS_EX_DLGMODALFRAME = 0x00000001;
    internal const uint WS_CLIPCHILDREN = 0x02000000;
    internal const uint MF_STRING = 0;
    internal const uint MF_POPUP = 0x10;
    internal const uint MF_SEPARATOR = 0x800;
    internal const uint MB_OK = 0;
    internal const uint MB_OKCANCEL = 1;
    internal const uint MB_YESNOCANCEL = 3;
    internal const uint MB_ICONERROR = 0x10;
    internal const uint MB_ICONQUESTION = 0x20;
    internal const int IDYES = 6;
    internal const int IDCANCEL = 2;
    internal const int WM_CREATE = 0x0001;
    internal const int WM_DESTROY = 0x0002;
    internal const int WM_SIZE = 0x0005;
    internal const int WM_CLOSE = 0x0010;
    internal const int WM_COMMAND = 0x0111;
    internal const int WM_NOTIFY = 0x004E;
    internal const int WM_CTLCOLORSTATIC = 0x0138;
    internal const int SPI_GETHIGHCONTRAST = 0x0042;
    internal const int HCF_HIGHCONTRASTON = 0x00000001;
    internal const int COLOR_WINDOW = 5;
    internal const int COLOR_WINDOWTEXT = 8;
    internal const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    internal const int DWMWA_CAPTION_COLOR = 35;
    internal const int DWMWA_TEXT_COLOR = 36;
    internal const int DWMWA_COLOR_DEFAULT = -1;
    internal const int WM_KEYDOWN = 0x0100;
    internal const uint WM_CHAR = 0x0102;
    internal const uint WM_CUT = 0x0300;
    internal const uint WM_COPY = 0x0301;
    internal const uint WM_PASTE = 0x0302;
    internal const uint WM_CLEAR = 0x0303;
    internal const int WM_TIMER = 0x0113;
    internal const uint WM_NCDESTROY = 0x0082;
    internal const uint WM_IME_STARTCOMPOSITION = 0x010D;
    internal const uint WM_IME_ENDCOMPOSITION = 0x010E;
    internal const int WM_SETREDRAW = 0x000B;
    internal const int WM_SETTEXT = 0x000C;
    internal const int WM_GETTEXTLENGTH = 0x000E;
    internal const int WM_SETFONT = 0x0030;
    internal const int EM_GETFIRSTVISIBLELINE = 0x00CE;
    internal const int EM_LINESCROLL = 0x00B6;
    internal const int WM_USER = 0x0400;
    internal const int WM_APP = 0x8000;
    internal const int EM_SETBKGNDCOLOR = WM_USER + 67;
    internal const int EM_SETCHARFORMAT = WM_USER + 68;
    internal const int EM_GETCHARFORMAT = WM_USER + 58;
    internal const int EM_EXGETSEL = WM_USER + 52;
    internal const int EM_EXLIMITTEXT = WM_USER + 53;
    internal const int EM_EXSETSEL = WM_USER + 55;
    internal const int EM_GETTEXTEX = WM_USER + 94;
    internal const int EM_SETTEXTEX = WM_USER + 97;
    internal const int EM_SETEVENTMASK = WM_USER + 69;
    internal const int EM_GETEVENTMASK = WM_USER + 59;
    internal const int EN_CHANGE = 0x0300;
    internal const int EN_SELCHANGE = 0x0702;
    internal const int ENM_CHANGE = 1;
    internal const int ENM_SELCHANGE = 0x00080000;
    internal const uint GT_USECRLF = 1;
    internal const uint CP_UNICODE = 1200;
    internal const uint SCF_SELECTION = 1;
    internal const uint SCF_ALL = 4;
    internal const uint CFM_BOLD = 0x00000001;
    internal const uint CFE_BOLD = 0x00000001;
    internal const uint CFM_SIZE = 0x80000000;
    internal const uint CFM_FACE = 0x20000000;
    internal const uint CFM_COLOR = 0x40000000;
    internal const uint CF_UNICODETEXT = 13;
    internal const uint GMEM_MOVEABLE = 0x0002;
    internal const byte FVIRTKEY = 0x01;
    internal const byte FCONTROL = 0x08;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint SubclassProcedure(nint window, uint message, nuint wParam,
        nint lParam, nuint subclassId, nuint reference);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        internal uint Style;
        internal WindowProcedure WindowProc;
        internal int ClassExtra;
        internal int WindowExtra;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint Background;
        internal string? MenuName;
        internal string ClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        internal nint Window;
        internal uint Id;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal int X;
        internal int Y;
        internal uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    /// <summary>System high-contrast state used to leave native caption colors to Windows.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct HighContrast
    {
        internal uint Size;
        internal uint Flags;
        internal nint DefaultScheme;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CharacterRange
    {
        internal int Min;
        internal int Max;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GetTextEx
    {
        internal uint ByteCapacity;
        internal uint Flags;
        internal uint CodePage;
        internal nint DefaultChar;
        internal nint UsedDefaultChar;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SetTextEx
    {
        internal uint Flags;
        internal uint CodePage;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    internal struct CharacterFormat
    {
        internal uint Size;
        internal uint Mask;
        internal uint Effects;
        internal int Height;
        internal int Offset;
        internal uint TextColor;
        internal byte CharSet;
        internal byte PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string FaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct OpenFileName
    {
        internal int Size;
        internal nint Owner;
        internal nint Instance;
        internal string? Filter;
        internal nint CustomFilter;
        internal int MaxCustomFilter;
        internal int FilterIndex;
        internal nint File;
        internal int MaxFile;
        internal string? InitialDirectory;
        internal string? Title;
        internal uint Flags;
        internal short FileOffset;
        internal short FileExtension;
        internal string? DefaultExtension;
        internal nint Hook;
        internal string? TemplateName;
        internal nint Reserved;
        internal int ReservedValue;
        internal uint FlagsEx;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Accelerator
    {
        internal byte Flags;
        internal ushort Key;
        internal ushort Command;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NotificationHeader
    {
        internal nint Window;
        internal nuint Id;
        internal int Code;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SelectionChange
    {
        internal NotificationHeader Header;
        internal CharacterRange Range;
        internal uint Type;
    }


    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint LoadLibraryW(string fileName);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandleW(string? moduleName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassW(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")]
    internal static extern nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    internal static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")]
    internal static extern bool EnableWindow(nint window, bool enable);
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextW(nint window, [Out] char[] text, int capacity);
    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLengthW(nint window);
    [DllImport("user32.dll")]
    internal static extern bool IsDialogMessageW(nint dialog, ref Message message);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nuint SetTimer(nint window, nuint id, uint intervalMilliseconds, nint callback);
    [DllImport("user32.dll")]
    internal static extern bool KillTimer(nint window, nuint id);
    [DllImport("comctl32.dll", SetLastError = true)]
    internal static extern bool SetWindowSubclass(nint window, SubclassProcedure callback,
        nuint subclassId, nuint reference);
    [DllImport("comctl32.dll")]
    internal static extern bool RemoveWindowSubclass(nint window, SubclassProcedure callback,
        nuint subclassId);
    [DllImport("comctl32.dll")]
    internal static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int exitCode);
    [DllImport("user32.dll")]
    internal static extern int GetMessageW(out Message message, nint window, uint filterMin, uint filterMax);
    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")]
    internal static extern nint DispatchMessageW(ref Message message);
    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")]
    internal static extern bool UpdateWindow(nint window);
    [DllImport("user32.dll")]
    internal static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")]
    internal static extern bool MoveWindow(nint window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")]
    internal static extern nint SetFocus(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessageW(nint window, int message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessageW(nint window, int message, nuint wParam, string lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessageW(nint window, int message, nuint wParam, ref CharacterRange lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessageW(nint window, int message, nuint wParam, ref CharacterFormat lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessageW(nint window, int message, ref GetTextEx wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessageW(nint window, int message, ref SetTextEx wParam, string lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetWindowTextW(nint window, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int MessageBoxW(nint owner, string text, string caption, uint type);
    [DllImport("user32.dll")]
    internal static extern nint CreateMenu();
    [DllImport("user32.dll")]
    internal static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool AppendMenuW(nint menu, uint flags, nuint item, string? label);
    [DllImport("user32.dll")]
    internal static extern bool SetMenu(nint window, nint menu);
    [DllImport("user32.dll")]
    internal static extern nint CreateAcceleratorTableW(Accelerator[] accelerators, int count);
    [DllImport("user32.dll")]
    internal static extern int TranslateAcceleratorW(nint window, nint table, ref Message message);
    [DllImport("user32.dll")]
    internal static extern bool DestroyAcceleratorTable(nint table);
    [DllImport("user32.dll")]
    internal static extern bool InvalidateRect(nint window, nint rect, bool erase);
    /// <summary>Reads OS contrast state without changing user preferences.</summary>
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", ExactSpelling = true,
        SetLastError = true)]
    internal static extern bool SystemParametersInfoW(uint action, uint parameter,
        ref HighContrast value, uint update);
    /// <summary>Reads a contrast-aware system COLORREF when OS colors take precedence.</summary>
    [DllImport("user32.dll")]
    internal static extern uint GetSysColor(int index);
    /// <summary>Creates one cached status background brush per effective color.</summary>
    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateSolidBrush(uint color);
    /// <summary>Applies the policy text color to the static-control paint DC.</summary>
    [DllImport("gdi32.dll")]
    internal static extern uint SetTextColor(nint dc, uint color);
    /// <summary>Applies the matching opaque background to the static-control paint DC.</summary>
    [DllImport("gdi32.dll")]
    internal static extern uint SetBkColor(nint dc, uint color);
    /// <summary>Sets an officially documented Windows 11 non-client frame attribute.</summary>
    [DllImport("dwmapi.dll", ExactSpelling = true, EntryPoint = "DwmSetWindowAttribute")]
    internal static extern int DwmSetWindowAttribute(nint window, int attribute,
        ref int value, int size);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetClipboardData(uint format, nint data);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool CloseClipboard();
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")]
    internal static extern nint GlobalFree(nint memory);
    [DllImport("gdi32.dll")]
    internal static extern nint GetStockObject(int index);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint CreateFontW(int height, int width, int escapement, int orientation,
        int weight, uint italic, uint underline, uint strikeout, uint charSet, uint outputPrecision,
        uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(nint handle);
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool GetOpenFileNameW(ref OpenFileName name);
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool GetSaveFileNameW(ref OpenFileName name);
}
