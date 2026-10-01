using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mote.Native.Windows;

/// <summary>A small OS-native modal text prompt without resource files or a companion DLL.</summary>
[SupportedOSPlatform("windows")]
internal sealed class Win32TextPrompt
{
    private const string ClassName = "MoteNativeTextPrompt";
    private const int EditId = 301;
    private const int AcceptId = 302;
    private const int CancelId = 303;
    private const uint PromptStyle = 0x80C80000; // WS_POPUP | WS_CAPTION | WS_SYSMENU.
    private static readonly Win32.WindowProcedure WindowProcedure = Dispatch;
    private static readonly Win32.SubclassProcedure InputProcedure = InputDispatch;
    private static Win32TextPrompt? _creating;
    private static Win32TextPrompt? _active;
    private readonly nint _owner;
    private readonly string _title;
    private readonly string _label;
    private readonly string _initial;
    private readonly bool _multiline;
    private nint _window;
    private nint _edit;
    private string? _result;
    private bool _composing;

    private Win32TextPrompt(nint owner, string title, string label, string initial, bool multiline = false)
    {
        _owner = owner;
        _title = title;
        _label = label;
        _initial = initial;
        _multiline = multiline;
    }

    /// <summary>Returns entered text, including empty text, or null when canceled.</summary>
    public static string? Show(nint owner, string title, string label, string initial, bool multiline = false)
    {
        if (multiline && (initial.Length > NativeCsvGridCommands.MaxPayloadLength || initial.Contains('\0')))
            throw new ArgumentException("The native cell prompt requires bounded text without embedded NUL.", nameof(initial));
        var prompt = new Win32TextPrompt(owner, title, label, initial, multiline);
        prompt.Run();
        return prompt._result;
    }

    private void Run()
    {
        var instance = Win32.GetModuleHandleW(null);
        var windowClass = new Win32.WindowClass
        {
            WindowProc = WindowProcedure,
            Instance = instance,
            ClassName = ClassName
        };
        var atom = Win32.RegisterClassW(ref windowClass);
        if (atom == 0 && Marshal.GetLastPInvokeError() != 1410)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot register native prompt.");

        Win32.EnableWindow(_owner, false);
        _creating = this;
        _active = this;
        try
        {
            var window = Win32.CreateWindowExW(Win32.WS_EX_DLGMODALFRAME, ClassName,
                _title, PromptStyle, Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT,
                450, _multiline ? 320 : 155, _owner, 0, instance, 0);
            if (window == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot open native prompt.");
            _window = window;
            Win32.ShowWindow(window, 5);
            Win32.SetForegroundWindow(window);
            Win32.SetFocus(_edit);
            Win32.SendMessageW(_edit, 0x00B1, 0, (nint)(-1)); // EM_SETSEL, select initial text.

            while (_window != 0)
            {
                var result = Win32.GetMessageW(out var message, 0, 0, 0);
                if (result == 0) { Win32.PostQuitMessage(0); break; }
                if (result < 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                if (message.Window == _edit && message.Id == Win32.WM_KEYDOWN)
                {
                    if (message.WParam == 0x0D && !_composing && (!_multiline || Win32.GetKeyState(0x11) < 0))
                    { Accept(); continue; }
                    if (message.WParam == 0x1B) { Cancel(); continue; }
                }
                if (!Win32.IsDialogMessageW(_window, ref message))
                {
                    Win32.TranslateMessage(ref message);
                    Win32.DispatchMessageW(ref message);
                }
            }
        }
        finally
        {
            if (_window != 0) Win32.DestroyWindow(_window);
            Win32.EnableWindow(_owner, true);
            Win32.SetForegroundWindow(_owner);
            _creating = null;
            _active = null;
        }
    }

    private static nint Dispatch(nint window, uint message, nuint wParam, nint lParam)
    {
        var prompt = _creating ?? _active;
        if (prompt is null || (prompt._window != 0 && prompt._window != window))
            return Win32.DefWindowProcW(window, message, wParam, lParam);
        return prompt.Handle(window, message, wParam, lParam);
    }

    private nint Handle(nint window, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case Win32.WM_CREATE:
                _window = window;
                CreateControls();
                return 0;
            case Win32.WM_COMMAND:
                switch ((int)(wParam & 0xFFFF))
                {
                    case AcceptId: Accept(); return 0;
                    case CancelId: Cancel(); return 0;
                }
                break;
            case Win32.WM_CLOSE:
                Cancel();
                return 0;
            case Win32.WM_DESTROY:
                _window = 0;
                return 0;
        }
        return Win32.DefWindowProcW(window, message, wParam, lParam);
    }

    private void CreateControls()
    {
        var instance = Win32.GetModuleHandleW(null);
        const uint child = Win32.WS_CHILD | Win32.WS_VISIBLE;
        var label = Win32.CreateWindowExW(0, "STATIC", _label, child,
            18, 15, 405, 20, _window, 0, instance, 0);
        _edit = CreateInput(_window, _initial, _multiline);
        if (_edit != 0 && !Win32.SetWindowSubclass(_edit, InputProcedure, 71, 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot protect prompt input composition.");
        var buttonY = _multiline ? 240 : 76;
        var accept = Win32.CreateWindowExW(0, "BUTTON", _multiline ? "OK (Ctrl+Enter)" : "OK",
            child | Win32.WS_TABSTOP | 0x00000001, _multiline ? 205 : 265, buttonY, _multiline ? 136 : 76, 28,
            _window, (nint)AcceptId, instance, 0);
        var cancel = Win32.CreateWindowExW(0, "BUTTON", "Cancel",
            child | Win32.WS_TABSTOP, 347, buttonY, 76, 28,
            _window, (nint)CancelId, instance, 0);
        if (label == 0 || _edit == 0 || accept == 0 || cancel == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot create native prompt controls.");
        var font = Win32.GetStockObject(17); // DEFAULT_GUI_FONT.
        foreach (var control in new[] { label, _edit, accept, cancel })
            Win32.SendMessageW(control, Win32.WM_SETFONT, (nuint)font, (nint)1);
    }

    /// <summary>Creates the production prompt input; multiline retains literal initial line endings.</summary>
    internal static nint CreateInput(nint parent, string initial, bool multiline)
    {
        const uint child = Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_TABSTOP;
        var style = multiline ? Win32.ES_MULTILINE | Win32.ES_AUTOVSCROLL | Win32.ES_WANTRETURN | Win32.WS_VSCROLL : Win32.ES_AUTOHSCROLL;
        var input = Win32.CreateWindowExW(Win32.WS_EX_CLIENTEDGE, "EDIT", initial,
            child | style, 18, 39, 405, multiline ? 185 : 26, parent, (nint)EditId,
            Win32.GetModuleHandleW(null), 0);
        // Multiline paste must not be silently clipped to a valid-looking replacement.
        // Accept validates the complete value with a visible refusal instead.
        if (input != 0) Win32.SendMessageW(input, 0x00C5, multiline ? (nuint)int.MaxValue : 4096u, 0);
        return input;
    }

    private static nint InputDispatch(nint window, uint message, nuint parameter, nint data, nuint id, nuint reference)
    {
        var prompt = _active;
        if (prompt?._edit != window) return Win32.DefSubclassProc(window, message, parameter, data);
        if (message == Win32.WM_IME_STARTCOMPOSITION) prompt._composing = true;
        var result = Win32.DefSubclassProc(window, message, parameter, data);
        if (message == Win32.WM_IME_ENDCOMPOSITION) prompt._composing = false;
        if (message == Win32.WM_NCDESTROY) Win32.RemoveWindowSubclass(window, InputProcedure, id);
        return result;
    }

    private void Accept()
    {
        if (_composing)
        {
            Win32.MessageBoxW(_window, "Finish or cancel input composition before accepting.", "Replace CSV cell", Win32.MB_OK);
            return;
        }
        var length = Win32.GetWindowTextLengthW(_edit);
        if (length > (_multiline ? NativeCsvGridCommands.MaxPayloadLength : 4096))
        {
            Win32.MessageBoxW(_window, "The value exceeds the accepted text limit. Edit it or cancel; no replacement was applied.", _title, Win32.MB_OK);
            return;
        }
        var chars = new char[length + 1];
        var copied = Win32.GetWindowTextW(_edit, chars, chars.Length);
        _result = new string(chars, 0, copied);
        Win32.DestroyWindow(_window);
    }

    private void Cancel()
    {
        _result = null;
        Win32.DestroyWindow(_window);
    }
}
