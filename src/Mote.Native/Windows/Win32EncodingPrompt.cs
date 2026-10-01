using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Engine;

namespace Mote.Native.Windows;

/// <summary>Owned modal codec selection; no guessed codec or file access occurs here.</summary>
[SupportedOSPlatform("windows")]
internal sealed class Win32EncodingPrompt
{
    /// <summary>Private class and control IDs keep the chooser separate from text prompts.</summary>
    private const string ClassName = "MoteNativeEncodingPrompt";
    /// <summary>Controls never overlap the text prompt IDs.</summary>
    private const int ChoiceId = 401, OpenId = 402, CancelId = 403;
    /// <summary>Standard closed-list combo messages, with no extra native library.</summary>
    private const int AddString = 0x0143, GetSelection = 0x0147;
    /// <summary>Rooted callback and modal instance live until the owned window is destroyed.</summary>
    private static readonly Win32.WindowProcedure Procedure = Dispatch;
    /// <summary>Single owner-thread modal lifetime; nested choosers are rejected.</summary>
    private static Win32EncodingPrompt? _active;
    /// <summary>The existing editor is the only disabled and restored owner.</summary>
    private readonly nint _owner;
    /// <summary>Owned window and child handles are used only during the modal loop.</summary>
    private nint _window, _choice, _open, _cancel;
    /// <summary>Nonfatal callback failures are rethrown only after returning to managed code.</summary>
    private ExceptionDispatchInfo? _error;
    /// <summary>Only a deliberate Open command with a valid selected codec assigns a result.</summary>
    private DocumentTextEncoding? _result;

    /// <summary>Captures ownership without creating windows or inspecting documents.</summary>
    private Win32EncodingPrompt(nint owner) => _owner = owner;

    /// <summary>Returns an explicitly chosen codec, or null for Escape, close, or Cancel.</summary>
    internal static DocumentTextEncoding? Show(nint owner)
    {
        if (_active is not null) throw new InvalidOperationException("An encoding chooser is already open.");
        var prompt = new Win32EncodingPrompt(owner);
        prompt.Run();
        return prompt._result;
    }

    /// <summary>Runs an owned modal loop, always restoring the owner and releasing callback state.</summary>
    private void Run()
    {
        var instance = Win32.GetModuleHandleW(null);
        var windowClass = new Win32.WindowClass { WindowProc = Procedure, Instance = instance, ClassName = ClassName, Background = 16 };
        if (Win32.RegisterClassW(ref windowClass) == 0 && Marshal.GetLastPInvokeError() != 1410)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot register encoding chooser.");
        _active = this;
        Win32.EnableWindow(_owner, false);
        try
        {
            var window = Win32.CreateWindowExW(Win32.WS_EX_DLGMODALFRAME, ClassName,
                "Open with Encoding", 0x80C80000, Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT,
                470, 175, _owner, 0, instance, 0);
            _error?.Throw();
            if (window == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot open encoding chooser.");
            Win32.ShowWindow(window, 5);
            Win32.SetForegroundWindow(window);
            Win32.SetFocus(_cancel); // Enter initially cancels; no codec is preselected.
            while (_window != 0)
            {
                var status = Win32.GetMessageW(out var message, 0, 0, 0);
                if (status == 0) { Win32.PostQuitMessage(0); break; }
                if (status < 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                if (message.Id == Win32.WM_KEYDOWN && message.WParam == 0x1B)
                { Close(); continue; }
                if (!Win32.IsDialogMessageW(_window, ref message))
                {
                    Win32.TranslateMessage(ref message);
                    Win32.DispatchMessageW(ref message);
                }
                _error?.Throw();
            }
            _error?.Throw();
        }
        finally
        {
            if (_window != 0) Win32.DestroyWindow(_window);
            _active = null;
            Win32.EnableWindow(_owner, true);
            Win32.SetForegroundWindow(_owner);
        }
    }

    /// <summary>Contains nonfatal managed failures at the unmanaged callback boundary.</summary>
    private static nint Dispatch(nint window, uint message, nuint parameter, nint data)
    {
        var prompt = _active;
        if (prompt is null || (prompt._window != 0 && prompt._window != window))
            return Win32.DefWindowProcW(window, message, parameter, data);
        try { return prompt.Handle(window, message, parameter, data); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            prompt._error ??= ExceptionDispatchInfo.Capture(error);
            prompt._result = null;
            // WM_CREATE failure tells user32 to destroy the incomplete window itself.
            if (message == Win32.WM_CREATE) return -1;
            prompt.Close();
            return 0;
        }
    }

    /// <summary>Selection notifications only enable Open; acceptance remains a separate action.</summary>
    private nint Handle(nint window, uint message, nuint parameter, nint data)
    {
        if (message == Win32.WM_CREATE) { _window = window; CreateControls(); return 0; }
        // IsDialogMessage also works with an ordinary owned window. Its default stays Cancel.
        if (message == 0x0400) return (nint)((0x534B << 16) | CancelId); // DM_GETDEFID / DC_HASDEFID.
        if (message == 0x0401) return 1; // DM_SETDEFID: never promote Open implicitly.
        if (message == Win32.WM_DESTROY) { _window = 0; return 0; }
        if (message == Win32.WM_CLOSE) { Close(); return 0; }
        if (message != Win32.WM_COMMAND) return Win32.DefWindowProcW(window, message, parameter, data);
        var id = (int)(parameter & 0xFFFF);
        var notification = (int)((parameter >> 16) & 0xFFFF);
        if (id == ChoiceId && notification == 1) // CBN_SELCHANGE.
            Win32.EnableWindow(_open, SelectedIndex() >= 0);
        if (id == CancelId && notification == 0) Close();
        if (id == OpenId && notification == 0)
        {
            var selected = SelectedIndex();
            if (selected < 0) return 0;
            _result = NativeOpenEncodingChoices.All[selected].Encoding;
            Close();
        }
        return 0;
    }

    /// <summary>Creates a closed dropdown; any failed control or list insertion aborts the chooser.</summary>
    private void CreateControls()
    {
        const uint child = Win32.WS_CHILD | Win32.WS_VISIBLE;
        var instance = Win32.GetModuleHandleW(null);
        var label = Win32.CreateWindowExW(0, "STATIC", "Choose the file's text encoding:", child,
            18, 15, 420, 22, _window, 0, instance, 0);
        _choice = Win32.CreateWindowExW(0, "COMBOBOX", "", child | Win32.WS_TABSTOP | 0x00200003,
            18, 40, 420, 250, _window, ChoiceId, instance, 0); // CBS_DROPDOWNLIST | WS_VSCROLL.
        _open = Win32.CreateWindowExW(0, "BUTTON", "Open", child | Win32.WS_TABSTOP,
            270, 90, 80, 28, _window, OpenId, instance, 0);
        _cancel = Win32.CreateWindowExW(0, "BUTTON", "Cancel", child | Win32.WS_TABSTOP | 1,
            358, 90, 80, 28, _window, CancelId, instance, 0); // BS_DEFPUSHBUTTON.
        if (label == 0 || _choice == 0 || _open == 0 || _cancel == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot create encoding controls.");
        if (Win32.SendMessageW(_choice, AddString, 0, "Select an encoding…") != 0)
            throw new InvalidOperationException("Cannot populate encoding placeholder.");
        var expectedIndex = 1;
        foreach (var choice in NativeOpenEncodingChoices.All)
        {
            if (Win32.SendMessageW(_choice, AddString, 0, choice.Label) != expectedIndex++)
                throw new InvalidOperationException("Cannot populate encoding choices.");
        }
        // Index zero is a prompt, not a codec; selecting it never enables Open.
        if (Win32.SendMessageW(_choice, 0x014E, 0, 0) != 0) // CB_SETCURSEL.
            throw new InvalidOperationException("Cannot select encoding placeholder.");
        Win32.EnableWindow(_open, false);
        var font = Win32.GetStockObject(17);
        foreach (var control in new[] { label, _choice, _open, _cancel })
            Win32.SendMessageW(control, Win32.WM_SETFONT, (nuint)font, 1);
    }

    /// <summary>Rejects absent or out-of-range selections rather than guessing a codec.</summary>
    private int SelectedIndex()
    {
        var value = Win32.SendMessageW(_choice, GetSelection, 0, 0);
        return value > 0 && value <= NativeOpenEncodingChoices.All.Count ? (int)value - 1 : -1;
    }

    /// <summary>Destroys only this owned modal; cancellation leaves the result unset.</summary>
    private void Close()
    {
        if (_window != 0) Win32.DestroyWindow(_window);
    }
}
