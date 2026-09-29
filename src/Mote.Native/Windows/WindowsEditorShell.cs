using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Mote.Themes;
using Mote.Native.Viewport;
using Mote.Native.Windows.Canvas;
using Mote.Engine;
using Mote.Native.Accessibility;

namespace Mote.Native.Windows;

/// <summary>
/// Windows RichEdit adapter for one document. RichEdit provides system IME, text selection,
/// clipboard, and accessibility, while the engine remains authoritative for text and history.
/// </summary>
/// <remarks>
/// The editor contains only a bounded page supplied by the controller. RichEdit projects all
/// line endings to CRLF; callers must map display offsets back to source offsets before edits.
/// No native library ships with mote: msftedit.dll and comdlg32.dll are Windows components.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsEditorShell : INativeCanvasShell
{
    private const string WindowClassName = "MoteNativeEditorWindow";
    private const int EditorId = 101;
    private const int PreviewId = 102;
    private const int StatusId = 103;
    private const int NewId = 201;
    private const int OpenId = 202;
    private const int SaveId = 203;
    private const int SaveAsId = 204;
    private const int ExitId = 205;
    private const int UndoId = 206;
    private const int RedoId = 207;
    private const int FormatId = 208;
    private const int PreviousId = 209;
    private const int NextId = 210;
    private const int SelectAllId = 211;
    private const int CopyId = 212;
    private const int FindId = 213;
    private const int FindNextId = 214;
    private const int GoToLineId = 215;
    private const int CutId = 216;
    private const nuint StyleTimerId = 1;
    private const uint SelectionMessage = Win32.WM_APP + 1;
    private static readonly Win32.WindowProcedure WindowProcedure = Dispatch;
    private static readonly Win32.SubclassProcedure EditorSubclassProcedure = EditorSubclass;
    private static WindowsEditorShell? _creating;
    private static WindowsEditorShell? _active;
    private readonly ConcurrentQueue<Action> _posted = new();
    private nint _window;
    private nint _editor;
    private nint _preview;
    private nint _status;
    private nint _accelerators;
    private nint _editorFont;
    private nint _uiFont;
    private string _visibleText = "";
    private RichEditOffsetMap _editorOffsets = new("");
    private RichEditOffsetMap _previewOffsets = new("");
    private NativeTextProjection _previewProjection = new("", NativeLineEndingMode.CrLf);
    private NativeDocumentView? _document;
    private NativeAnalysisView? _analysis;
    private IThemePolicy _theme = ThemePolicies.Get(ThemePolicies.DefaultId);
    private bool _settingText;
    private bool _imeComposing;
    private bool _settingSelection;
    private bool _pendingSelection;
    private bool _selectionPostQueued;
    private int _lastSelectionStart;
    private int _lastSelectionEnd;
    private string _lastFind = "";
    private string? _styleText;
    private readonly bool _experimentalCanvas;
    private readonly bool _uiaFragmentExperimental;
    private WindowsRichEditIsland? _canvasIsland;
    private NativeCanvasBinding? _pendingCanvasBinding;
    private CanvasFrame? _pendingCanvasFrame;
    private NativeCanvasSemantics? _pendingCanvasSemantics;

    /// <summary>Creates the established editor or explicitly opts into the continuous canvas.</summary>
    internal WindowsEditorShell(bool experimentalCanvas = false, bool uiaFragmentExperimental = false)
    {
        if (uiaFragmentExperimental && !experimentalCanvas)
            throw new ArgumentException("UIA fragment diagnostics require the canvas shell.",
                nameof(uiaFragmentExperimental));
        _experimentalCanvas = experimentalCanvas;
        _uiaFragmentExperimental = uiaFragmentExperimental;
    }

    /// <inheritdoc />
    public bool CanvasEnabled => _experimentalCanvas;

    /// <inheritdoc />
    public bool IsCanvasComposing => _canvasIsland?.IsComposing ?? false;

    /// <inheritdoc />
    public event Action<CanvasCommittedEdit>? CanvasEditCommitted;
    /// <inheritdoc />
    public event Action<double>? CanvasScrollRequested;
    /// <inheritdoc />
    public event Action<double>? CanvasViewportResized;
    /// <inheritdoc />
    public event Action<int, int>? CanvasSelectionRequested;
    /// <inheritdoc />
    public event Action? CanvasAccessibilityFailed;

    /// <inheritdoc />
    public NativeLineEndingMode LineEndingMode => NativeLineEndingMode.CrLf;

    /// <inheritdoc />
    public bool PrefersDark
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                // Windows uses 0 for dark application surfaces and 1 for light.
                return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public event Action<string>? TextChanged;
    /// <inheritdoc />
    public event Action<int, int>? SelectionChanged;
    /// <inheritdoc />
    public event Action? NewRequested;
    /// <inheritdoc />
    public event Action? OpenRequested;
    /// <inheritdoc />
    public event Action? SaveRequested;
    /// <inheritdoc />
    public event Action? SaveAsRequested;
    /// <inheritdoc />
    public event Action? UndoRequested;
    /// <inheritdoc />
    public event Action? RedoRequested;
    /// <inheritdoc />
    public event Action? FormatRequested;
    /// <inheritdoc />
    public event Action? PagePreviousRequested;
    /// <inheritdoc />
    public event Action? PageNextRequested;
    /// <inheritdoc />
    public event Action? FindRequested;
    /// <inheritdoc />
    public event Action? FindNextRequested;
    /// <inheritdoc />
    public event Action? GoToLineRequested;
    /// <inheritdoc />
    public event Action? SelectAllRequested;
    /// <inheritdoc />
    public event Action? CopyRequested;
    /// <inheritdoc />
    public event Action? CutRequested;
    /// <inheritdoc />
    public event EventHandler<NativeClosingEventArgs>? ClosingRequested;
    /// <inheritdoc />
    public event Action? Shown;

    /// <inheritdoc />
    public void Run()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (_window != 0) throw new InvalidOperationException("The Windows shell can run only once.");
        if (Win32.LoadLibraryW("Msftedit.dll") == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows RichEdit is unavailable.");

        var instance = Win32.GetModuleHandleW(null);
        var windowClass = new Win32.WindowClass
        {
            WindowProc = WindowProcedure,
            Instance = instance,
            ClassName = WindowClassName
        };
        var atom = Win32.RegisterClassW(ref windowClass);
        if (atom == 0 && Marshal.GetLastPInvokeError() != 1410)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot register mote window.");

        _creating = this;
        _active = this;
        try
        {
            var window = Win32.CreateWindowExW(0, WindowClassName, "mote",
                Win32.WS_OVERLAPPEDWINDOW | Win32.WS_CLIPCHILDREN,
                Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT, 1100, 760,
                0, 0, instance, 0);
            if (window == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot create mote window.");
            _window = window;
        }
        finally
        {
            _creating = null;
        }

        InstallMenu();
        InstallAccelerators();
        Win32.ShowWindow(_window, 5);
        Win32.UpdateWindow(_window);
        Win32.SetFocus(_canvasIsland?.InputHandle ?? _editor);
        Shown?.Invoke();
        while (_posted.TryDequeue(out var queued)) queued();

        while (true)
        {
            var result = Win32.GetMessageW(out var message, 0, 0, 0);
            if (result == 0) break;
            if (result < 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows message loop failed.");
            if (_accelerators != 0 && Win32.TranslateAcceleratorW(_window, _accelerators, ref message) != 0)
                continue;
            Win32.TranslateMessage(ref message);
            Win32.DispatchMessageW(ref message);
        }
        if (_accelerators != 0) Win32.DestroyAcceleratorTable(_accelerators);
        _canvasIsland?.Dispose();
        if (_editorFont != 0) Win32.DeleteObject(_editorFont);
        if (_uiFont != 0) Win32.DeleteObject(_uiFont);
        _active = null;
    }

    /// <inheritdoc />
    public void Close()
    {
        if (_window != 0) Win32.PostMessageW(_window, Win32.WM_CLOSE, 0, 0);
    }

    /// <inheritdoc />
    public bool CommitPendingText() => !_imeComposing &&
        (_canvasIsland is null || _canvasIsland.FlushPendingText());

    /// <inheritdoc />
    public void SetCanvasBinding(NativeCanvasBinding binding)
    {
        if (!_experimentalCanvas) throw new InvalidOperationException("Canvas mode is not enabled.");
        ArgumentNullException.ThrowIfNull(binding);
        if (_canvasIsland?.IsComposing == true)
            throw new InvalidOperationException("An active IME composition cannot be rebound.");
        if (_window != 0)
        {
            Win32.SetWindowTextW(_window, binding.Title);
            UpdateStatus(binding.Status);
        }
        if (_canvasIsland is null) _pendingCanvasBinding = binding;
        else _canvasIsland.Bind(binding);
    }

    /// <inheritdoc />
    public void SetCanvasFrame(CanvasFrame frame)
    {
        if (!_experimentalCanvas) throw new InvalidOperationException("Canvas mode is not enabled.");
        ArgumentNullException.ThrowIfNull(frame);
        if (_canvasIsland is null) _pendingCanvasFrame = frame;
        else _canvasIsland.SetFrame(frame);
    }

    /// <inheritdoc />
    public void SetCanvasInputUnavailable(TextSnapshot snapshot, CanvasFrame frame, string reason)
    {
        if (!_experimentalCanvas) throw new InvalidOperationException("Canvas mode is not enabled.");
        _canvasIsland?.SetInputUnavailable(snapshot, frame, reason);
        UpdateStatus(reason);
    }

    /// <inheritdoc />
    public void SetCanvasSemantics(NativeCanvasSemantics semantics)
    {
        if (!_experimentalCanvas) throw new InvalidOperationException("Canvas mode is not enabled.");
        ArgumentNullException.ThrowIfNull(semantics);
        if (_canvasIsland is null) _pendingCanvasSemantics = semantics;
        else _canvasIsland.SetSemantics(semantics);
    }

    /// <inheritdoc />
    public void SetCanvasChrome(string title, string status, bool isModified)
    {
        if (!_experimentalCanvas) throw new InvalidOperationException("Canvas mode is not enabled.");
        if (_window != 0) Win32.SetWindowTextW(_window, title);
        UpdateStatus(status);
    }

    /// <inheritdoc />
    public void SetCanvasAccessibility(AccessibleDocument document, IAccessibleViewport viewport)
    {
        if (!_experimentalCanvas || _canvasIsland is null)
            throw new InvalidOperationException("Accessibility requires a visible experimental canvas.");
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(viewport);
        _canvasIsland.AttachAccessibility(document, viewport);
    }

    /// <inheritdoc />
    public void SetDocument(NativeDocumentView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_experimentalCanvas)
        {
            if (_window != 0)
            {
                Win32.SetWindowTextW(_window, view.Title);
                UpdateStatus(view.Status);
            }
            return;
        }
        _document = view;
        if (_window == 0) return;

        Win32.SetWindowTextW(_window, view.Title);
        UpdateStatus(view.Status);
        if (string.Equals(_visibleText, view.Text, StringComparison.Ordinal))
        {
            if (view.FocusDisplayOffset is int focus)
                SetSelection(Math.Clamp(focus, 0, _visibleText.Length),
                    Math.Clamp(focus, 0, _visibleText.Length));
            return;
        }
        CancelPendingStyle();
        var oldSelection = GetSelection();
        _settingText = true;
        try
        {
            // RichEdit's UTF-16 entry point retains one bounded page, not the entire document.
            var text = new Win32.SetTextEx { CodePage = Win32.CP_UNICODE };
            Win32.SendMessageW(_editor, Win32.EM_SETTEXTEX, ref text, view.Text);
            _visibleText = view.Text;
            _editorOffsets = new RichEditOffsetMap(view.Text);
            SetSelection(0, view.Text.Length);
            SetSelectionColor(_theme.Palette.EditorForeground);
            if (view.FocusDisplayOffset is int focus)
                SetSelection(Math.Clamp(focus, 0, view.Text.Length),
                    Math.Clamp(focus, 0, view.Text.Length));
            else
                SetSelection(Math.Min(oldSelection.Min, view.Text.Length),
                    Math.Min(oldSelection.Max, view.Text.Length));
        }
        finally
        {
            _settingText = false;
        }
    }

    /// <inheritdoc />
    public void SetAnalysis(NativeAnalysisView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        _analysis = view;
        if (_window == 0) return;
        if (!_experimentalCanvas) ScheduleStyle();
        _settingText = true;
        try
        {
            _previewProjection = new NativeTextProjection(view.PreviewText, NativeLineEndingMode.CrLf);
            _previewOffsets = new RichEditOffsetMap(_previewProjection.Display);
            var text = new Win32.SetTextEx { CodePage = Win32.CP_UNICODE };
            Win32.SendMessageW(_preview, Win32.EM_SETTEXTEX, ref text, _previewProjection.Display);
        }
        finally
        {
            _settingText = false;
        }
        ApplyPreviewColors(view);
        UpdateStatus(view.Status.Length == 0 ? view.DiagnosticsSummary :
            view.Status + "  " + view.DiagnosticsSummary);
    }

    /// <inheritdoc />
    public void SetTheme(IThemePolicy theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (_canvasIsland?.IsComposing == true)
            throw new InvalidOperationException("Theme changes cannot interrupt IME composition.");
        _theme = theme;
        if (_window == 0) return;
        Win32.SendMessageW(_editor, Win32.EM_SETBKGNDCOLOR, 0,
            (nint)ColorRef(theme.Palette.EditorBackground));
        Win32.SendMessageW(_preview, Win32.EM_SETBKGNDCOLOR, 0,
            (nint)ColorRef(theme.Palette.PreviewBackground));
        var oldEditorFont = _editorFont;
        var oldUiFont = _uiFont;
        var editorFace = FirstFont(theme.Typography.EditorFontFamilies, "Consolas");
        var uiFace = FirstFont(theme.Typography.UiFontFamilies, "Segoe UI");
        _editorFont = Win32.CreateFontW(-(int)Math.Round(theme.Typography.EditorFontSize * 96 / 72),
            0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, editorFace);
        _uiFont = Win32.CreateFontW(-(int)Math.Round(theme.Typography.UiFontSize * 96 / 72),
            0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, uiFace);
        if (_editorFont != 0) Win32.SendMessageW(_editor, Win32.WM_SETFONT, (nuint)_editorFont, (nint)1);
        if (_uiFont != 0)
        {
            Win32.SendMessageW(_preview, Win32.WM_SETFONT, (nuint)_uiFont, (nint)1);
            Win32.SendMessageW(_status, Win32.WM_SETFONT, (nuint)_uiFont, (nint)1);
        }
        if (oldEditorFont != 0) Win32.DeleteObject(oldEditorFont);
        if (oldUiFont != 0) Win32.DeleteObject(oldUiFont);
        if (_experimentalCanvas)
            _canvasIsland?.SetTheme(theme);
        else if (_analysis is not null) ScheduleStyle();
        else
        {
            var selected = GetSelection();
            SetSelection(0, _visibleText.Length);
            SetSelectionColor(theme.Palette.EditorForeground);
            SetSelection(selected.Min, selected.Max);
        }
        if (_analysis is not null) ApplyPreviewColors(_analysis);
    }

    /// <inheritdoc />
    public string? PickOpenFile() => PickFile(save: false, null);

    /// <inheritdoc />
    public string? PromptFind()
    {
        var value = Win32TextPrompt.Show(_window, "Find in document", "Find:", _lastFind);
        if (string.IsNullOrEmpty(value)) return null;
        _lastFind = value;
        return value;
    }

    /// <inheritdoc />
    public int? PromptGoToLine()
    {
        while (true)
        {
            var value = Win32TextPrompt.Show(_window, "Go to line", "Line number:", "1");
            if (value is null) return null;
            if (int.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var line) && line > 0)
                return line;
            ShowError("Enter a positive one-based line number.");
        }
    }

    /// <inheritdoc />
    public void SetClipboardText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var chars = (text + '\0').ToCharArray();
        var memory = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, (nuint)checked(chars.Length * sizeof(char)));
        if (memory == 0) throw ClipboardFailure("The selection cannot be allocated for the clipboard.");
        try
        {
            var pointer = Win32.GlobalLock(memory);
            if (pointer == 0) throw ClipboardFailure("The clipboard memory cannot be locked.");
            try { Marshal.Copy(chars, 0, pointer, chars.Length); }
            finally
            {
                if (!Win32.GlobalUnlock(memory) && Marshal.GetLastPInvokeError() != 0)
                    throw ClipboardFailure("The clipboard memory could not be unlocked.");
            }
            if (!Win32.OpenClipboard(_window)) throw ClipboardFailure("The system clipboard is busy.");
            try
            {
                if (!Win32.EmptyClipboard()) throw ClipboardFailure("The system clipboard could not be cleared.");
                if (Win32.SetClipboardData(Win32.CF_UNICODETEXT, memory) == 0)
                    throw ClipboardFailure("The system clipboard rejected Unicode text.");
                memory = 0; // Windows owns the movable block after SetClipboardData succeeds.
            }
            finally
            {
                if (!Win32.CloseClipboard()) throw ClipboardFailure("The system clipboard could not be closed.");
            }
        }
        finally
        {
            if (memory != 0) Win32.GlobalFree(memory);
        }
    }

    private static Win32Exception ClipboardFailure(string message) =>
        new(Marshal.GetLastPInvokeError(), message);

    /// <inheritdoc />
    public string? PickSaveFile(string? currentPath) => PickFile(save: true, currentPath);

    /// <inheritdoc />
    public bool ConfirmDiscard() => Win32.MessageBoxW(_window,
        "Discard unsaved changes?", "mote", Win32.MB_YESNOCANCEL | Win32.MB_ICONQUESTION) == Win32.IDYES;

    /// <inheritdoc />
    public bool ConfirmOverwrite(string path) => Win32.MessageBoxW(_window,
        $"Replace the existing file?\n\n{path}", "mote", Win32.MB_YESNOCANCEL | Win32.MB_ICONQUESTION)
        == Win32.IDYES;

    /// <inheritdoc />
    public void ShowError(string message) => Win32.MessageBoxW(_window, message, "mote",
        Win32.MB_OK | Win32.MB_ICONERROR);

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _posted.Enqueue(action);
        if (_window != 0) Win32.PostMessageW(_window, Win32.WM_APP, 0, 0);
    }

    private static nint Dispatch(nint window, uint message, nuint wParam, nint lParam)
    {
        var shell = _creating ?? _active;
        if (shell is null || (shell._window != 0 && shell._window != window))
            return Win32.DefWindowProcW(window, message, wParam, lParam);
        return shell.WindowMessage(window, message, wParam, lParam);
    }

    private nint WindowMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case Win32.WM_CREATE:
                _window = window;
                CreateControls();
                return 0;
            case Win32.WM_SIZE:
                ResizeControls();
                return 0;
            case Win32.WM_COMMAND:
                if ((int)(wParam & 0xFFFF) == EditorId && (int)((wParam >> 16) & 0xFFFF) == Win32.EN_CHANGE)
                {
                    if (!_settingText) OnTextChanged();
                    return 0;
                }
                HandleCommand((int)(wParam & 0xFFFF));
                return 0;
            case Win32.WM_NOTIFY:
                if (_editor != 0 && lParam != 0 && !_settingText && !_settingSelection)
                {
                    var header = Marshal.PtrToStructure<Win32.NotificationHeader>(lParam);
                    if (header.Window == _editor && header.Code == Win32.EN_SELCHANGE)
                    {
                        // RichEdit can collapse selection before EN_CHANGE for a typed edit.
                        // Dispatch after the input message so the controller sees text first.
                        _pendingSelection = true;
                        if (!_selectionPostQueued)
                        {
                            _selectionPostQueued = true;
                            Win32.PostMessageW(_window, SelectionMessage, 0, 0);
                        }
                    }
                }
                return 0;
            case SelectionMessage:
                _selectionPostQueued = false;
                FlushSelection();
                return 0;
            case Win32.WM_TIMER when wParam == StyleTimerId:
                Win32.KillTimer(_window, StyleTimerId);
                if (_imeComposing)
                {
                    Win32.SetTimer(_window, StyleTimerId, 300, 0);
                }
                else if (_analysis is not null && string.Equals(_styleText, _visibleText,
                    StringComparison.Ordinal))
                {
                    ApplySemanticColors(_analysis);
                }
                return 0;
            case Win32.WM_CLOSE:
                var closing = new NativeClosingEventArgs();
                ClosingRequested?.Invoke(this, closing);
                if (!closing.Cancel) Win32.DestroyWindow(window);
                return 0;
            case Win32.WM_DESTROY:
                Win32.KillTimer(window, StyleTimerId);
                _window = 0;
                Win32.PostQuitMessage(0);
                return 0;
            case Win32.WM_APP:
                while (_posted.TryDequeue(out var action)) action();
                return 0;
            default:
                return Win32.DefWindowProcW(window, message, wParam, lParam);
        }
    }

    private void CreateControls()
    {
        var instance = Win32.GetModuleHandleW(null);
        const uint editorStyle = Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_TABSTOP |
            Win32.WS_VSCROLL | Win32.WS_HSCROLL | Win32.ES_MULTILINE |
            Win32.ES_AUTOVSCROLL | Win32.ES_AUTOHSCROLL | Win32.ES_WANTRETURN |
            Win32.ES_NOHIDESEL;
        _editor = Win32.CreateWindowExW(Win32.WS_EX_CLIENTEDGE, "RICHEDIT50W", "", editorStyle,
            0, 0, 100, 100, _window, (nint)EditorId, instance, 0);
        _preview = Win32.CreateWindowExW(Win32.WS_EX_CLIENTEDGE, "RICHEDIT50W", "",
            editorStyle | Win32.ES_READONLY, 0, 0, 100, 100,
            _window, (nint)PreviewId, instance, 0);
        _status = Win32.CreateWindowExW(0, "STATIC", "Ready", Win32.WS_CHILD | Win32.WS_VISIBLE,
            0, 0, 100, 24, _window, (nint)StatusId, instance, 0);
        if (_editor == 0 || _preview == 0 || _status == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot create native editor controls.");
        if (_experimentalCanvas)
        {
            Win32.ShowWindow(_editor, 0);
            _canvasIsland = new WindowsRichEditIsland(_window, _theme,
                _uiaFragmentExperimental);
            _canvasIsland.EditCommitted += edit => CanvasEditCommitted?.Invoke(edit);
            _canvasIsland.ScrollRequested += delta => CanvasScrollRequested?.Invoke(delta);
            _canvasIsland.ViewportResized += height => CanvasViewportResized?.Invoke(height);
            _canvasIsland.SelectionRequested += (anchor, active) =>
                CanvasSelectionRequested?.Invoke(anchor, active);
            _canvasIsland.Faulted += message =>
            {
                UpdateStatus(message);
                ShowError(message);
            };
            _canvasIsland.AccessibilityFaulted += () => CanvasAccessibilityFailed?.Invoke();
            if (_pendingCanvasBinding is not null) _canvasIsland.Bind(_pendingCanvasBinding);
            if (_pendingCanvasFrame is not null) _canvasIsland.SetFrame(_pendingCanvasFrame);
            if (_pendingCanvasSemantics is not null) _canvasIsland.SetSemantics(_pendingCanvasSemantics);
        }
        // RichEdit's default user-edit limit is much smaller than a viewport page.
        Win32.SendMessageW(_editor, Win32.EM_EXLIMITTEXT, 0, (nint)int.MaxValue);
        Win32.SendMessageW(_editor, Win32.EM_SETEVENTMASK, 0,
            (nint)(Win32.ENM_CHANGE | Win32.ENM_SELCHANGE));
        if (!Win32.SetWindowSubclass(_editor, EditorSubclassProcedure, 1, 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot protect RichEdit IME composition.");
        SetTheme(_theme);
        if (_document is not null)
        {
            var view = _document;
            _document = null;
            SetDocument(view);
        }
        if (_analysis is not null) SetAnalysis(_analysis);
    }

    private void ResizeControls()
    {
        if (_editor == 0 || !Win32.GetClientRect(_window, out var rect)) return;
        var width = Math.Max(0, rect.Right - rect.Left);
        var height = Math.Max(0, rect.Bottom - rect.Top);
        const int statusHeight = 25;
        var bodyHeight = Math.Max(0, height - statusHeight);
        var editorWidth = Math.Max(0, width * 2 / 3);
        if (_experimentalCanvas) _canvasIsland?.Resize(editorWidth, bodyHeight);
        else Win32.MoveWindow(_editor, 0, 0, editorWidth, bodyHeight, true);
        Win32.MoveWindow(_preview, editorWidth, 0, width - editorWidth, bodyHeight, true);
        Win32.MoveWindow(_status, 4, bodyHeight, Math.Max(0, width - 8), statusHeight, true);
    }

    private void InstallMenu()
    {
        var menu = Win32.CreateMenu();
        var file = Win32.CreatePopupMenu();
        var edit = Win32.CreatePopupMenu();
        var view = Win32.CreatePopupMenu();
        Win32.AppendMenuW(file, Win32.MF_STRING, NewId, "&New\tCtrl+N");
        Win32.AppendMenuW(file, Win32.MF_STRING, OpenId, "&Open…\tCtrl+O");
        Win32.AppendMenuW(file, Win32.MF_STRING, SaveId, "&Save\tCtrl+S");
        Win32.AppendMenuW(file, Win32.MF_STRING, SaveAsId, "Save &As…\tCtrl+Shift+S");
        Win32.AppendMenuW(file, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(file, Win32.MF_STRING, ExitId, "E&xit");
        Win32.AppendMenuW(edit, Win32.MF_STRING, UndoId, "&Undo\tCtrl+Z");
        Win32.AppendMenuW(edit, Win32.MF_STRING, RedoId, "&Redo\tCtrl+Y");
        Win32.AppendMenuW(edit, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(edit, Win32.MF_STRING, SelectAllId, "Select &All\tCtrl+A");
        Win32.AppendMenuW(edit, Win32.MF_STRING, CopyId, "&Copy\tCtrl+C");
        Win32.AppendMenuW(edit, Win32.MF_STRING, CutId, "Cu&t\tCtrl+X");
        Win32.AppendMenuW(edit, Win32.MF_STRING, FindId, "&Find…\tCtrl+F");
        Win32.AppendMenuW(edit, Win32.MF_STRING, FindNextId, "Find &Next\tF3");
        Win32.AppendMenuW(edit, Win32.MF_STRING, GoToLineId, "Go to &Line…\tCtrl+G");
        Win32.AppendMenuW(edit, Win32.MF_STRING, FormatId, "&Format document\tCtrl+Shift+F");
        Win32.AppendMenuW(view, Win32.MF_STRING, PreviousId, "&Previous page\tF7");
        Win32.AppendMenuW(view, Win32.MF_STRING, NextId, "&Next page\tF8");
        Win32.AppendMenuW(menu, Win32.MF_POPUP, (nuint)file, "&File");
        Win32.AppendMenuW(menu, Win32.MF_POPUP, (nuint)edit, "&Edit");
        Win32.AppendMenuW(menu, Win32.MF_POPUP, (nuint)view, "&View");
        Win32.SetMenu(_window, menu);
    }

    private void InstallAccelerators()
    {
        static Win32.Accelerator Key(int key, int command, byte flags = Win32.FCONTROL | Win32.FVIRTKEY) =>
            new() { Flags = flags, Key = (ushort)key, Command = (ushort)command };
        var keys = new[]
        {
            Key('N', NewId), Key('O', OpenId), Key('S', SaveId),
            Key('S', SaveAsId, Win32.FCONTROL | Win32.FVIRTKEY | 0x04),
            Key('Z', UndoId), Key('Y', RedoId),
            Key('Z', RedoId, Win32.FCONTROL | Win32.FVIRTKEY | 0x04),
            Key('A', SelectAllId), Key('C', CopyId), Key('X', CutId), Key('F', FindId),
            Key('G', GoToLineId), Key(0x72, FindNextId, Win32.FVIRTKEY),
            Key('F', FormatId, Win32.FCONTROL | Win32.FVIRTKEY | 0x04),
            Key(0x76, PreviousId, Win32.FVIRTKEY), Key(0x77, NextId, Win32.FVIRTKEY)
        };
        _accelerators = Win32.CreateAcceleratorTableW(keys, keys.Length);
    }

    private void HandleCommand(int id)
    {
        if (id is CopyId or CutId or FindId or FindNextId or GoToLineId)
            FlushSelection();
        switch (id)
        {
            case NewId: NewRequested?.Invoke(); break;
            case OpenId: OpenRequested?.Invoke(); break;
            case SaveId: SaveRequested?.Invoke(); break;
            case SaveAsId: SaveAsRequested?.Invoke(); break;
            case ExitId: Win32.PostMessageW(_window, Win32.WM_CLOSE, 0, 0); break;
            case UndoId: UndoRequested?.Invoke(); break;
            case RedoId: RedoRequested?.Invoke(); break;
            case FormatId: FormatRequested?.Invoke(); break;
            case SelectAllId: SelectAllRequested?.Invoke(); break;
            case CopyId: CopyRequested?.Invoke(); break;
            case CutId: CutRequested?.Invoke(); break;
            case FindId: FindRequested?.Invoke(); break;
            case FindNextId: FindNextRequested?.Invoke(); break;
            case GoToLineId: GoToLineRequested?.Invoke(); break;
            case PreviousId: PagePreviousRequested?.Invoke(); break;
            case NextId: PageNextRequested?.Invoke(); break;
        }
    }

    private void OnTextChanged()
    {
        var text = ReadEditorText();
        if (string.Equals(text, _visibleText, StringComparison.Ordinal)) return;
        CancelPendingStyle();
        _visibleText = text;
        _editorOffsets = new RichEditOffsetMap(text);
        TextChanged?.Invoke(text);
    }

    private static nint EditorSubclass(nint window, uint message, nuint wParam,
        nint lParam, nuint subclassId, nuint reference)
    {
        var shell = _active ?? _creating;
        if (shell is null) return Win32.DefSubclassProc(window, message, wParam, lParam);
        if (message == Win32.WM_COPY)
        {
            shell.FlushSelection();
            shell.CopyRequested?.Invoke();
            return 0;
        }
        if (message == Win32.WM_CUT)
        {
            shell.FlushSelection();
            shell.CutRequested?.Invoke();
            return 0;
        }
        if (message is Win32.WM_CHAR or Win32.WM_PASTE or
            Win32.WM_CLEAR or Win32.WM_IME_STARTCOMPOSITION ||
            message == Win32.WM_KEYDOWN && (wParam == 0x08 || wParam == 0x2E))
            shell.FlushSelection();
        if (message == Win32.WM_IME_STARTCOMPOSITION) shell._imeComposing = true;
        var result = Win32.DefSubclassProc(window, message, wParam, lParam);
        if (message == Win32.WM_IME_ENDCOMPOSITION)
        {
            shell._imeComposing = false;
            if (shell._analysis is not null) shell.ScheduleStyle();
        }
        if (message == Win32.WM_NCDESTROY)
            Win32.RemoveWindowSubclass(window, EditorSubclassProcedure, subclassId);
        return result;
    }

    private void FlushSelection()
    {
        if (!_pendingSelection) return;
        _pendingSelection = false;
        var range = GetSelection();
        if (range.Min == _lastSelectionStart && range.Max == _lastSelectionEnd) return;
        _lastSelectionStart = range.Min;
        _lastSelectionEnd = range.Max;
        // RichEdit exposes ordered endpoints, not the active edge of a reverse selection.
        SelectionChanged?.Invoke(range.Min, range.Max);
    }

    private void ScheduleStyle()
    {
        if (_window == 0) return;
        _styleText = _visibleText;
        Win32.KillTimer(_window, StyleTimerId);
        Win32.SetTimer(_window, StyleTimerId, 300, 0);
    }

    private void CancelPendingStyle()
    {
        _styleText = null;
        if (_window != 0) Win32.KillTimer(_window, StyleTimerId);
    }

    private string ReadEditorText()
    {
        // WM_GETTEXTLENGTH may count internal paragraph delimiters as one code unit.
        // Doubled capacity safely accommodates their CRLF projection.
        var internalLength = (int)Win32.SendMessageW(_editor, Win32.WM_GETTEXTLENGTH, 0, 0);
        var capacity = checked((internalLength + 1) * 2 + 16);
        var buffer = Marshal.AllocHGlobal(checked(capacity * sizeof(char)));
        try
        {
            var request = new Win32.GetTextEx
            {
                ByteCapacity = (uint)(capacity * sizeof(char)),
                Flags = Win32.GT_USECRLF,
                CodePage = Win32.CP_UNICODE
            };
            var count = (int)Win32.SendMessageW(_editor, Win32.EM_GETTEXTEX, ref request, buffer);
            return Marshal.PtrToStringUni(buffer, count) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void ApplySemanticColors(NativeAnalysisView view)
    {
        if (_editor == 0) return;
        var saved = GetSelection();
        var firstLine = (int)Win32.SendMessageW(_editor, Win32.EM_GETFIRSTVISIBLELINE, 0, 0);
        var richText = RichEditRtf.Build(_visibleText, view.Tokens, _theme);
        _settingText = true;
        Win32.SendMessageW(_editor, Win32.WM_SETREDRAW, 0, 0);
        try
        {
            // RichEdit reflows on every selection formatting call even with redraw disabled.
            // Importing one escaped RTF page keeps semantic styling proportional to page size.
            var replacement = new Win32.SetTextEx { CodePage = Win32.CP_UNICODE };
            var imported = Win32.SendMessageW(_editor, Win32.EM_SETTEXTEX, ref replacement, richText);
            if (imported == 0 || !string.Equals(ReadEditorText(), _visibleText, StringComparison.Ordinal))
            {
                // A styling failure must never leak transformed RTF into the canonical editor.
                Win32.SendMessageW(_editor, Win32.EM_SETTEXTEX, ref replacement, _visibleText);
                SetSelection(0, _visibleText.Length);
                SetSelectionColor(_theme.Palette.EditorForeground);
            }
            SetSelection(Math.Min(saved.Min, _visibleText.Length),
                Math.Min(saved.Max, _visibleText.Length));
            var now = (int)Win32.SendMessageW(_editor, Win32.EM_GETFIRSTVISIBLELINE, 0, 0);
            if (now != firstLine)
                Win32.SendMessageW(_editor, Win32.EM_LINESCROLL, 0, (nint)(firstLine - now));
        }
        finally
        {
            _settingText = false;
            Win32.SendMessageW(_editor, Win32.WM_SETREDRAW, 1, 0);
            Win32.InvalidateRect(_editor, 0, false);
        }
    }

    private void ApplyPreviewColors(NativeAnalysisView view)
    {
        if (_preview == 0) return;
        var saved = GetSelection(_preview);
        var firstLine = (int)Win32.SendMessageW(_preview, Win32.EM_GETFIRSTVISIBLELINE, 0, 0);
        _settingText = true;
        Win32.SendMessageW(_preview, Win32.WM_SETREDRAW, 0, 0);
        try
        {
            var length = _previewProjection.Display.Length;
            SetSelection(_preview, 0, length);
            SetCharacterFormat(_preview, _theme.Palette.PreviewForeground, false, false, false);
            foreach (var span in view.PreviewSpans ?? [])
            {
                if (span.Start < 0 || span.Length <= 0 ||
                    span.Length > view.PreviewText.Length - span.Start) continue;
                var start = _previewProjection.ToDisplay(span.Start);
                var end = _previewProjection.ToDisplay(span.Start + span.Length);
                var color = span.Kind switch
                {
                    "paragraph" or "table-cell" => _theme.Palette.PreviewForeground,
                    "quote" => _theme.Palette.MutedForeground,
                    "table-header" => _theme.SemanticColor("key"),
                    _ => _theme.SemanticColor(span.Kind)
                };
                SetSelection(_preview, start, end);
                SetCharacterFormat(_preview, color, span.Emphasis || span.Kind == "heading",
                    span.Kind == "heading", span.Kind == "code");
            }
            SetSelection(_preview, Math.Min(saved.Min, length), Math.Min(saved.Max, length));
            var now = (int)Win32.SendMessageW(_preview, Win32.EM_GETFIRSTVISIBLELINE, 0, 0);
            if (now != firstLine)
                Win32.SendMessageW(_preview, Win32.EM_LINESCROLL, 0, (nint)(firstLine - now));
        }
        finally
        {
            _settingText = false;
            Win32.SendMessageW(_preview, Win32.WM_SETREDRAW, 1, 0);
            Win32.InvalidateRect(_preview, 0, false);
        }
    }

    private Win32.CharacterRange GetSelection() => GetSelection(_editor);

    private Win32.CharacterRange GetSelection(nint control)
    {
        var range = new Win32.CharacterRange();
        if (control != 0) Win32.SendMessageW(control, Win32.EM_EXGETSEL, 0, ref range);
        var offsets = control == _editor ? _editorOffsets : _previewOffsets;
        range.Min = offsets.ToDisplay(Math.Clamp(range.Min, 0,
            control == _editor ? _visibleText.Length - _editorOffsets.NewlineCount :
                _previewProjection.Display.Length - _previewOffsets.NewlineCount));
        range.Max = offsets.ToDisplay(Math.Clamp(range.Max, 0,
            control == _editor ? _visibleText.Length - _editorOffsets.NewlineCount :
                _previewProjection.Display.Length - _previewOffsets.NewlineCount));
        return range;
    }

    /// <inheritdoc />
    public void SetSelection(int displayAnchor, int displayActive)
    {
        if (_editor == 0) return;
        SetSelection(_editor, Math.Clamp(displayAnchor, 0, _visibleText.Length),
            Math.Clamp(displayActive, 0, _visibleText.Length));
    }

    private void SetSelection(nint control, int start, int end)
    {
        var offsets = control == _editor ? _editorOffsets : _previewOffsets;
        var range = new Win32.CharacterRange
        {
            Min = offsets.ToNative(start),
            Max = offsets.ToNative(end)
        };
        var wasSettingSelection = _settingSelection;
        _settingSelection = true;
        try { Win32.SendMessageW(control, Win32.EM_EXSETSEL, 0, ref range); }
        finally { _settingSelection = wasSettingSelection; }
        if (control == _editor)
        {
            _lastSelectionStart = Math.Min(start, end);
            _lastSelectionEnd = Math.Max(start, end);
        }
    }

    private void SetSelectionColor(ThemeColor color) =>
        SetCharacterFormat(_editor, color, false, false, false);

    private void SetCharacterFormat(nint control, ThemeColor color, bool bold,
        bool large, bool monospace)
    {
        var editorText = control == _editor;
        var format = new Win32.CharacterFormat
        {
            Size = (uint)Marshal.SizeOf<Win32.CharacterFormat>(),
            Mask = Win32.CFM_COLOR | Win32.CFM_BOLD | Win32.CFM_SIZE | Win32.CFM_FACE,
            Effects = bold ? Win32.CFE_BOLD : 0,
            Height = (int)Math.Round((editorText ? _theme.Typography.EditorFontSize :
                _theme.Typography.UiFontSize) * (large ? 1.25 : 1) * 15),
            TextColor = ColorRef(color),
            FaceName = monospace || editorText
                ? FirstFont(_theme.Typography.EditorFontFamilies, "Consolas")
                : FirstFont(_theme.Typography.UiFontFamilies, "Segoe UI")
        };
        Win32.SendMessageW(control, Win32.EM_SETCHARFORMAT, Win32.SCF_SELECTION, ref format);
    }

    private void UpdateStatus(string text)
    {
        if (_status != 0) Win32.SetWindowTextW(_status, text);
    }

    private string? PickFile(bool save, string? currentPath)
    {
        const int pathCapacity = 32768;
        var buffer = Marshal.AllocHGlobal(pathCapacity * sizeof(char));
        try
        {
            Marshal.WriteInt16(buffer, 0);
            if (save)
            {
                var suggestion = currentPath is null ? "Untitled.md" : Path.GetFileName(currentPath);
                Marshal.Copy((suggestion + '\0').ToCharArray(), 0, buffer, suggestion.Length + 1);
            }
            var name = new Win32.OpenFileName
            {
                Size = Marshal.SizeOf<Win32.OpenFileName>(),
                Owner = _window,
                Filter = "Text and structured files\0*.md;*.markdown;*.toml;*.json;*.yaml;*.yml;*.csv;*.txt\0All files\0*.*\0\0",
                File = buffer,
                MaxFile = pathCapacity,
                Title = save ? "Save one file" : "Open one file",
                // Explorer-style dialog; Save allows a new file, Open requires an existing one.
                Flags = 0x00080000 | 0x00000800 | (save ? 0x00000002u : 0x00001000u),
                DefaultExtension = "md"
            };
            var selected = save ? Win32.GetSaveFileNameW(ref name) : Win32.GetOpenFileNameW(ref name);
            var path = selected ? Marshal.PtrToStringUni(buffer) : null;
            return string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static uint ColorRef(ThemeColor color) =>
        (uint)(color.Red | (color.Green << 8) | (color.Blue << 16));

    private static string FirstFont(string families, string fallback)
    {
        var first = families.Split(',', 2)[0].Trim();
        return first.Length == 0 ? fallback : first;
    }
}
