using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Mote.Engine;
using Mote.Formats;
using Mote.Native.Accessibility;
using Mote.Native.Windows.Accessibility;
using Mote.Native.Viewport;
using Mote.Themes;

namespace Mote.Native.Windows.Canvas;

/// <summary>
/// Source-backed Win32 canvas with a visible, bounded RichEdit input island.
/// The native control never mirrors more than one controller-supplied logical-line interval.
/// </summary>
/// <remarks>
/// The owner keeps snapshot, selection and history authoritative. This class owns only OS
/// geometry/paint and a temporary input projection; EN_CHANGE is coalesced after native
/// dispatch so IME preedit cannot leak into the document and controller rebinding cannot
/// recursively replace text inside RichEdit's notification stack.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsRichEditIsland : IDisposable
{
    private const string ClassName = "MoteInteractiveCanvas";
    private const int InputId = 301;
    private const int InputHardLimit = 32 * 1024;
    private const uint CommitMessage = Win32.WM_APP + 41;
    private const uint SelectionMessage = Win32.WM_APP + 42;
    private const uint ClearSuppressedBackspaceMessage = Win32.WM_APP + 43;
    private const uint WmImeComposition = 0x010F;
    private const uint WmGetObject = 0x003D;
    private const uint GcsResultString = 0x0800;
    private const int ScrollRange = 1_000_000;
    private const float TextLeft = 24;
    private static readonly Win32.WindowProcedure WindowProcedure = Dispatch;
    private static readonly Win32.SubclassProcedure InputProcedure = InputSubclass;
    private static WindowsRichEditIsland? _creating;
    private static WindowsRichEditIsland? _active;
    private readonly nint _parent;
    private WindowsDirectWriteCanvas _geometry;
    private WindowsCanvasPainter _painter;
    private IThemePolicy _theme;
    private nint _window;
    private nint _input;
    private nint _inputFont;
    private nint _memoryDc;
    private nint _bitmap;
    private nint _priorBitmap;
    private int _width = 1;
    private int _height = 1;
    private int _inputX;
    private int _inputY;
    private NativeCanvasBinding? _binding;
    private TextSnapshot? _snapshot;
    private CanvasFrame? _frame;
    private NativeCanvasSemantics? _semantics;
    private WindowsUiaBridgePrototype? _uiaBridge;
    private Func<nint, nuint, nint, nint>? _uiaResponder;
    private NativeTextProjection _projection = new("", NativeLineEndingMode.CrLf);
    private RichEditOffsetMap _offsets = new("");
    private bool _settingText;
    private bool _settingSelection;
    private bool _composition;
    private bool _compositionAttempted;
    private bool _commitQueued;
    private bool _selectionQueued;
    private bool _dragging;
    private bool _suppressBackspaceChar;
    private int _dragAnchor;
    private PendingEditSelection? _pendingEditSelection;
    private bool _faulted;
    private bool _disposed;

    private readonly record struct PendingEditSelection(long Nonce, int DisplayStart,
        int DisplayEnd, int LocalStart, int LocalEnd, int GlobalStart, int GlobalLength,
        bool ConfirmedReplacement = false);

    /// <summary>Creates a child canvas inside the existing native editor window.</summary>
    internal WindowsRichEditIsland(nint parent, IThemePolicy theme)
    {
        if (parent == 0) throw new ArgumentOutOfRangeException(nameof(parent));
        ArgumentNullException.ThrowIfNull(theme);
        _parent = parent;
        _theme = theme;
        _geometry = NewGeometry(theme);
        _painter = new WindowsCanvasPainter(theme);
        try
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
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot register interactive canvas.");
            _creating = this;
            _active = this;
            try
            {
                _window = Win32.CreateWindowExW(0, ClassName, "",
                    Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPCHILDREN | Win32.WS_VSCROLL,
                    0, 0, 1, 1, parent, 0, instance, 0);
            }
            finally { _creating = null; }
            if (_window == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot create interactive canvas.");
            _input = Win32.CreateWindowExW(Win32.WS_EX_CLIENTEDGE, "RICHEDIT50W", "",
                Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_TABSTOP |
                Win32.ES_MULTILINE | Win32.ES_WANTRETURN | Win32.ES_NOHIDESEL,
                24, 0, 300, 54, _window, (nint)InputId, instance, 0);
            if (_input == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot create RichEdit input island.");
            Win32.SendMessageW(_input, Win32.EM_EXLIMITTEXT, 0, (nint)InputHardLimit);
            Win32.SendMessageW(_input, Win32.EM_SETEVENTMASK, 0,
                (nint)(Win32.ENM_CHANGE | Win32.ENM_SELCHANGE | 0x10000000 | 0x20000000));
            if (!Win32.SetWindowSubclass(_input, InputProcedure, 2, 0))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot subclass input island.");
            SetInputAppearance(theme);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>The visible OS text-input handle used for focus and IME candidate placement.</summary>
    internal nint InputHandle => _input;

    /// <summary>Whether native IME preedit owns the input island.</summary>
    internal bool IsComposing => _composition;

    /// <summary>Settles a queued ordinary edit before save/new/close, vetoing IME preedit.</summary>
    internal bool FlushPendingText()
    {
        if (_composition) return false;
        if (_commitQueued)
        {
            _commitQueued = false;
            CommitFinalText();
        }
        return true;
    }

    /// <summary>One final edit tagged with the exact controller binding.</summary>
    internal event Action<CanvasCommittedEdit>? EditCommitted;
    /// <summary>A wheel/scrollbar delta in logical pixels.</summary>
    internal event Action<double>? ScrollRequested;
    /// <summary>The current view height in logical pixels.</summary>
    internal event Action<double>? ViewportResized;
    /// <summary>Global UTF-16 source pointer selection.</summary>
    internal event Action<int, int>? SelectionRequested;
    /// <summary>First native callback failure; input is disabled before notification.</summary>
    internal event Action<string>? Faulted;
    /// <summary>An AX-only failure detached the provider without disabling text input.</summary>
    internal event Action? AccessibilityFaulted;

    /// <summary>
    /// Registers the one source-backed UIA element on this canvas HWND. The bounded
    /// RichEdit remains a native input host, never the provider's document text.
    /// </summary>
    internal void AttachAccessibility(AccessibleDocument document, IAccessibleViewport viewport,
        Func<nint, nuint, nint, nint>? responderOverride = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(viewport);
        if (_window == 0) throw new InvalidOperationException("Canvas HWND is not ready.");
        if (_uiaBridge is not null) throw new InvalidOperationException("UIA is already attached.");
        _uiaBridge = new WindowsUiaBridgePrototype(document, viewport);
        _uiaResponder = responderOverride ?? _uiaBridge.HandleGetObject;
    }

    /// <summary>Replaces only the bounded input interval after composition has ended.</summary>
    internal void Bind(NativeCanvasBinding binding)
    {
        if (_faulted) return;
        ArgumentNullException.ThrowIfNull(binding);
        if (_composition) throw new InvalidOperationException("IME composition owns the input island.");
        if (binding.InputSourceText.Length > 16 * 1024 ||
            binding.InputSourceText.Contains('\r') || binding.InputSourceText.Contains('\n') ||
            binding.InputSourceStart < 0 ||
            binding.InputSourceStart > binding.Snapshot.Length - binding.InputSourceText.Length ||
            binding.BaseVersion != binding.Snapshot.Version ||
            !string.Equals(binding.Snapshot.GetText(binding.InputSourceStart,
                binding.InputSourceText.Length), binding.InputSourceText, StringComparison.Ordinal))
            throw new ArgumentException("Canvas input must be one exact bounded snapshot interval.", nameof(binding));
        _binding = binding;
        _snapshot = binding.Snapshot;
        _semantics = null;
        _commitQueued = false;
        _selectionQueued = false;
        _pendingEditSelection = null;
        _compositionAttempted = false;
        _frame = binding.Frame;
        _projection = new NativeTextProjection(binding.InputSourceText, NativeLineEndingMode.CrLf);
        _offsets = new RichEditOffsetMap(_projection.Display);
        if (_input != 0) Win32.ShowWindow(_input, 5);
        if (_input != 0)
        {
            _settingText = true;
            try
            {
                var text = new Win32.SetTextEx { CodePage = Win32.CP_UNICODE };
                Win32.SendMessageW(_input, Win32.EM_SETTEXTEX, ref text, _projection.Display);
                ApplyInputTextColor();
                SetNativeSelection(binding.Anchor, binding.Active);
            }
            finally { _settingText = false; }
        }
        PlaceInput();
        UpdateScrollbar();
        Invalidate();
    }

    /// <summary>Updates paint/selection without replacing native input text or its nonce.</summary>
    internal void SetFrame(CanvasFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_composition && _frame is not null && frame.TopAnchor != _frame.TopAnchor)
            throw new InvalidOperationException("Canvas scrolling cannot displace an IME candidate.");
        if (_snapshot is not null && frame.Version != _snapshot.Version)
            return; // An in-flight old analysis/frame cannot paint over a new document.
        _frame = frame;
        _selectionQueued = false;
        if (!_composition)
        {
            PlaceInput();
            if (_binding is not null) SetNativeSelection(frame.SelectionAnchor, frame.SelectionActive);
        }
        UpdateScrollbar();
        Invalidate();
    }

    /// <summary>Prevents native edits when a grapheme-safe bounded binding is unavailable.</summary>
    internal void SetInputUnavailable(TextSnapshot snapshot, CanvasFrame frame, string reason)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (_composition) throw new InvalidOperationException("IME composition owns the input island.");
        if (snapshot.Version != frame.Version)
            throw new ArgumentException("Unavailable input frame must match the source snapshot.", nameof(frame));
        _binding = null;
        _snapshot = snapshot;
        _frame = frame;
        _semantics = null;
        _commitQueued = false;
        _selectionQueued = false;
        _pendingEditSelection = null;
        if (_input != 0) Win32.ShowWindow(_input, 0);
        Invalidate();
    }

    /// <summary>Stores only tokens for the bound immutable source version.</summary>
    internal void SetSemantics(NativeCanvasSemantics semantics)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        if (_snapshot is null || semantics.Version != _snapshot.Version) return;
        _semantics = semantics;
        if (!_composition) Invalidate();
    }

    /// <summary>Recreates OS drawing resources when a policy changes outside composition.</summary>
    internal void SetTheme(IThemePolicy theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (_composition) throw new InvalidOperationException("Theme change would displace IME composition.");
        var geometry = NewGeometry(theme);
        var painter = new WindowsCanvasPainter(theme);
        _geometry.Dispose();
        _painter.Dispose();
        _geometry = geometry;
        _painter = painter;
        _theme = theme;
        SetInputAppearance(theme);
        PlaceInput();
        Invalidate();
    }

    /// <summary>Resizes the child surface without rebinding native input text.</summary>
    internal void Resize(int width, int height)
    {
        if (_window == 0) return;
        Win32.MoveWindow(_window, 0, 0, Math.Max(1, width), Math.Max(1, height), true);
    }

    /// <summary>Releases HWND and COM/GDI objects without posting WM_QUIT to the owner.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_window != 0) Win32.DestroyWindow(_window);
        if (_inputFont != 0) Win32.DeleteObject(_inputFont);
        ReleaseBitmap();
        _geometry.Dispose();
        _painter.Dispose();
        if (ReferenceEquals(_active, this)) _active = null;
    }

    private static WindowsDirectWriteCanvas NewGeometry(IThemePolicy theme)
    {
        var family = theme.Typography.EditorFontFamilies.Split(',', 2)[0].Trim();
        return new WindowsDirectWriteCanvas(family.Length == 0 ? "Consolas" : family,
            (float)theme.Typography.EditorFontSize);
    }

    private static nint Dispatch(nint window, uint message, nuint wParam, nint lParam)
    {
        var island = _creating ?? _active;
        if (island is null || island._window != 0 && island._window != window)
            return Win32.DefWindowProcW(window, message, wParam, lParam);
        try { return island.HandleMessage(window, message, wParam, lParam); }
        catch (Exception ex)
        {
            island.FailClosed(ex);
            return 0;
        }
    }

    private nint HandleMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case Win32.WM_CREATE:
                _window = window;
                return 0;
            case Win32.WM_SIZE:
                _width = Math.Max(1, (int)(ushort)((long)lParam & 0xFFFF));
                _height = Math.Max(1, (int)(ushort)(((long)lParam >> 16) & 0xFFFF));
                ReleaseBitmap();
                PlaceInput();
                UpdateScrollbar();
                ViewportResized?.Invoke(_height);
                Invalidate();
                return 0;
            case CanvasWin32.WmPaint:
                Paint();
                return 0;
            case WmGetObject when _uiaBridge is not null:
                return HandleAccessibilityGetObject(window, wParam, lParam);
            case CanvasWin32.WmMouseWheel:
                if (!_composition)
                    ScrollRequested?.Invoke(-((short)(((ulong)wParam >> 16) & 0xFFFF)) /
                        120d * 3 * LineHeight);
                return 0;
            case CanvasWin32.WmVScroll:
                if (!_composition) ScrollRequested?.Invoke(ScrollDelta((int)(wParam & 0xFFFF)));
                return 0;
            case CanvasWin32.WmLButtonDown:
                if (_composition) return 0;
                _dragging = true;
                _dragAnchor = HitSource(lParam);
                CanvasWin32.SetCapture(window);
                SelectionRequested?.Invoke(_dragAnchor, _dragAnchor);
                return 0;
            case CanvasWin32.WmMouseMove when _dragging:
                if (_composition) return 0;
                SelectionRequested?.Invoke(_dragAnchor, HitSource(lParam));
                return 0;
            case CanvasWin32.WmLButtonUp when _dragging:
                if (_composition) return 0;
                SelectionRequested?.Invoke(_dragAnchor, HitSource(lParam));
                _dragging = false;
                CanvasWin32.ReleaseCapture();
                if (_input != 0) Win32.SetFocus(_input);
                return 0;
            case Win32.WM_COMMAND:
                if ((int)(wParam & 0xFFFF) == InputId &&
                    (int)((wParam >> 16) & 0xFFFF) == Win32.EN_CHANGE &&
                    !_settingText && !_composition)
                    QueueCommit();
                return 0;
            case Win32.WM_NOTIFY:
                if (lParam != 0 && !_settingText && !_settingSelection)
                {
                    var header = Marshal.PtrToStructure<Win32.NotificationHeader>(lParam);
                    if (header.Window == _input && header.Code == Win32.EN_SELCHANGE)
                    {
                        if (!_selectionQueued)
                        {
                            _selectionQueued = true;
                            Win32.PostMessageW(_window, SelectionMessage, 0, 0);
                        }
                    }
                    else if (header.Window == _input && header.Code == 0x0713)
                    {
                        CaptureBeforeEdit();
                        _composition = true;
                        _compositionAttempted = true;
                        SuspendPointerDrag();
                    }
                    else if (header.Window == _input && header.Code == 0x0714)
                    {
                        _composition = false;
                        QueueCommit();
                    }
                }
                return 0;
            case CommitMessage:
                if (_commitQueued && !_composition)
                {
                    _commitQueued = false;
                    CommitFinalText();
                }
                return 0;
            case SelectionMessage:
                if (!_selectionQueued) return 0;
                _selectionQueued = false;
                if (!_composition && !_commitQueued) PublishInputSelection();
                return 0;
            case ClearSuppressedBackspaceMessage:
                _suppressBackspaceChar = false;
                return 0;
            case CanvasWin32.WmDestroy:
                try { _uiaBridge?.Close(window); }
                catch (Exception) { NotifyAccessibilityFault(); }
                finally
                {
                    _uiaBridge = null;
                    _uiaResponder = null;
                    _window = 0;
                    _input = 0;
                }
                return 0;
            default:
                return Win32.DefWindowProcW(window, message, wParam, lParam);
        }
    }

    private nint HandleAccessibilityGetObject(nint window, nuint wParam, nint lParam)
    {
        try { return _uiaResponder!(window, wParam, lParam); }
        catch (Exception)
        {
            // AX is an optional adapter. A COM/UIA fault must never pass through
            // the generic native-input FailClosed path or consume the document.
            // Detach first: UIA may have retained a partial HWND registration.
            try { _uiaBridge?.DetachRegistration(window); }
            catch
            {
                // The provider-local lifetime token is invalidated before OS
                // unregistration; even a failing cleanup cannot unwind to user32.
            }
            _uiaResponder = null;
            _uiaBridge = null;
            NotifyAccessibilityFault();
            return 0;
        }
    }

    private void NotifyAccessibilityFault()
    {
        try { AccessibilityFaulted?.Invoke(); }
        catch
        {
            // A UIA callback must not unwind through user32 into editor input.
        }
    }

    private static nint InputSubclass(nint window, uint message, nuint wParam,
        nint lParam, nuint subclassId, nuint reference)
    {
        var island = _active;
        if (island is null) return Win32.DefSubclassProc(window, message, wParam, lParam);
        try { return InputSubclassCore(island, window, message, wParam, lParam, subclassId); }
        catch (Exception ex)
        {
            island.FailClosed(ex);
            return 0;
        }
    }

    private static nint InputSubclassCore(WindowsRichEditIsland island, nint window,
        uint message, nuint wParam, nint lParam, nuint subclassId)
    {
        if (island._faulted && message != Win32.WM_NCDESTROY) return 0;
        if (message == Win32.WM_IME_STARTCOMPOSITION)
        {
            island.CaptureBeforeEdit();
            island._composition = true;
            island._compositionAttempted = true;
            island.SuspendPointerDrag();
        }
        var confirmedImeResult = message == WmImeComposition &&
            ((ulong)lParam & GcsResultString) != 0 && HasImeResultString(window);
        if (message == Win32.WM_COPY || message == Win32.WM_CUT)
        {
            // The parent accelerator/controller owns global selection and clipboard.
            Win32.SendMessageW(island._parent, Win32.WM_COMMAND,
                (nuint)(message == Win32.WM_COPY ? 212 : 216), 0);
            return 0;
        }
        if (message == Win32.WM_PASTE)
        {
            island.PastePlainText();
            return 0; // Never let RichEdit choose CF_RTF or hit EM_EXLIMITTEXT.
        }
        if (message == Win32.WM_CHAR && wParam == 0x08 &&
            island._suppressBackspaceChar)
        {
            island._suppressBackspaceChar = false;
            return 0; // WM_KEYDOWN already submitted the global deletion.
        }
        if (!island._composition &&
            (message == Win32.WM_KEYDOWN && (wParam == 0x08 || wParam == 0x2E) ||
             message == Win32.WM_CHAR && wParam == 0x08 ||
             message == Win32.WM_CLEAR) && island.EmitGlobalSelectionDelete())
        {
            if (message == Win32.WM_KEYDOWN && wParam == 0x08)
            {
                island._suppressBackspaceChar = true;
                Win32.PostMessageW(island._window, ClearSuppressedBackspaceMessage, 0, 0);
            }
            return 0;
        }
        if (!island._composition &&
            (message is Win32.WM_CHAR or Win32.WM_CLEAR ||
             message == Win32.WM_KEYDOWN && (wParam == 0x08 || wParam == 0x2E)))
            island.CaptureBeforeEdit();
        if ((message == CanvasWin32.WmLButtonDown || message == CanvasWin32.WmLButtonUp ||
             message == CanvasWin32.WmMouseMove && island._dragging))
        {
            // The visible input overlay must not turn a global canvas drag into a
            // page-local RichEdit selection. Translate its client point to canvas.
            var x = (short)((long)lParam & 0xFFFF) + island._inputX;
            var y = (short)(((long)lParam >> 16) & 0xFFFF) + island._inputY;
            var point = (nint)((ushort)x | (uint)(ushort)y << 16);
            island.HandleMessage(island._window, message, wParam, point);
            return 0;
        }
        if (message == CanvasWin32.WmMouseWheel)
        {
            if (!island._composition)
                island.ScrollRequested?.Invoke(-((short)(((ulong)wParam >> 16) & 0xFFFF)) /
                    120d * 3 * island.LineHeight);
            return 0;
        }
        var result = Win32.DefSubclassProc(window, message, wParam, lParam);
        if (confirmedImeResult && island._pendingEditSelection is { } imePending)
            island._pendingEditSelection = imePending with { ConfirmedReplacement = true };
        if (message == Win32.WM_CHAR && wParam >= 0x20 &&
            island._pendingEditSelection is { } pending)
        {
            // A same-text host replacement can still replace off-host source.
            island._pendingEditSelection = pending with { ConfirmedReplacement = true };
            if (pending.GlobalLength > 0) island.QueueCommit();
        }
        if (message == Win32.WM_IME_ENDCOMPOSITION)
        {
            island._composition = false;
            island.QueueCommit();
        }
        if (message == Win32.WM_NCDESTROY)
            Win32.RemoveWindowSubclass(window, InputProcedure, subclassId);
        return result;
    }

    private void FailClosed(Exception error)
    {
        if (_faulted) return;
        _faulted = true;
        _binding = null;
        _commitQueued = false;
        _selectionQueued = false;
        _pendingEditSelection = null;
        try
        {
            if (_input != 0) Win32.ShowWindow(_input, 0);
            Faulted?.Invoke($"Interactive canvas input was disabled after a native callback error: {error.Message}");
        }
        catch
        {
            // A reverse P/Invoke callback must never unwind into user32/comctl32.
        }
    }

    private void SuspendPointerDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        CanvasWin32.ReleaseCapture();
    }

    private double LineHeight => Math.Max(16,
        _theme.Typography.EditorFontSize * _theme.Typography.LineHeightMultiplier);

    private void QueueCommit()
    {
        if (_commitQueued || _window == 0) return;
        _commitQueued = true;
        Win32.PostMessageW(_window, CommitMessage, 0, 0);
    }

    private void CommitFinalText()
    {
        var binding = _binding;
        if (binding is null || _input == 0) return;
        var display = ReadInputText();
        var difference = _projection.Difference(display);
        var pending = _pendingEditSelection;
        _pendingEditSelection = null;
        if (_compositionAttempted && pending is not { ConfirmedReplacement: true })
        {
            // RichEdit can remove a selection during a cancelled preedit. That
            // native-only change is not proof of a document edit; restore source.
            _compositionAttempted = false;
            if (difference is not null) Bind(binding);
            return;
        }
        _compositionAttempted = false;
        if (difference is null &&
            pending is not { GlobalLength: > 0, ConfirmedReplacement: true })
        {
            return;
        }
        var local = difference ?? new TextChange(0, 0, "");
        var start = binding.InputSourceStart + local.Start;
        var deleteLength = local.DeleteLength;
        var insert = local.InsertText;
        if (pending is { } selected && selected.Nonce == binding.BindingNonce &&
            (selected.GlobalLength > 0 || selected.LocalEnd > selected.LocalStart))
        {
            var before = _projection.Display;
            var prefix = before.AsSpan(0, selected.DisplayStart);
            var suffix = before.AsSpan(selected.DisplayEnd);
            if (display.Length < prefix.Length + suffix.Length ||
                !display.AsSpan(0, prefix.Length).SequenceEqual(prefix) ||
                !display.AsSpan(display.Length - suffix.Length).SequenceEqual(suffix))
            {
                Win32.MessageBoxW(_parent,
                    "Native text changed outside the selected input range; the edit was not applied.",
                    "mote", Win32.MB_OK | Win32.MB_ICONERROR);
                Bind(binding);
                return;
            }
            start = selected.GlobalLength > 0 ? selected.GlobalStart : selected.LocalStart;
            deleteLength = selected.GlobalLength > 0 ? selected.GlobalLength :
                selected.LocalEnd - selected.LocalStart;
            insert = NormalizeNewlines(display.Substring(selected.DisplayStart,
                display.Length - prefix.Length - suffix.Length),
                PreferredNewline(binding.Snapshot, start));
            if (selected.GlobalLength == 0)
                SelectionRequested?.Invoke(selected.LocalStart, selected.LocalEnd);
        }
        var change = new TextChange(start, deleteLength, insert);
        var active = checked(start + insert.Length);
        // A synchronous controller callback must replace the nonce before another input edit.
        EditCommitted?.Invoke(new CanvasCommittedEdit(binding.DocumentGeneration,
            binding.BaseVersion, binding.BindingNonce, change, active));
    }

    private bool EmitGlobalSelectionDelete()
    {
        if (_binding is null || _frame is null || _frame.SelectionLength == 0) return false;
        if (!FlushPendingText()) return false;
        var binding = _binding;
        var frame = _frame;
        if (binding is null || frame is null || frame.SelectionLength == 0) return false;
        _pendingEditSelection = null;
        EditCommitted?.Invoke(new CanvasCommittedEdit(binding.DocumentGeneration,
            binding.BaseVersion, binding.BindingNonce,
            new TextChange(frame.SelectionStart, frame.SelectionLength, ""),
            frame.SelectionStart));
        return true;
    }

    private void CaptureBeforeEdit()
    {
        if (_composition || _binding is null || _input == 0)
            return;
        // A second edit cannot be allowed to overtake a queued first transaction.
        if (_commitQueued) FlushPendingText();
        _pendingEditSelection = null;
        var binding = _binding;
        if (binding is null) return;
        var range = new Win32.CharacterRange();
        Win32.SendMessageW(_input, Win32.EM_EXGETSEL, 0, ref range);
        var max = _projection.Display.Length - _offsets.NewlineCount;
        var displayStart = _offsets.ToDisplay(Math.Clamp(range.Min, 0, max));
        var displayEnd = _offsets.ToDisplay(Math.Clamp(range.Max, 0, max));
        var localStart = binding.InputSourceStart + _projection.ToSourceBoundary(displayStart);
        var localEnd = binding.InputSourceStart +
            _projection.ToSourceBoundary(displayEnd, towardEnd: true);
        var frame = _frame;
        _pendingEditSelection = new PendingEditSelection(binding.BindingNonce,
            displayStart, displayEnd, localStart, localEnd,
            frame?.SelectionStart ?? localStart, frame?.SelectionLength ?? 0);
    }

    private void PublishInputSelection()
    {
        var binding = _binding;
        if (binding is null || _input == 0) return;
        // If native text differs, selection collapse belongs to the edit and is not allowed
        // to erase a document-global selection before the controller receives the edit.
        if (_projection.Difference(ReadInputText()) is not null)
        {
            QueueCommit();
            return;
        }
        var range = new Win32.CharacterRange();
        Win32.SendMessageW(_input, Win32.EM_EXGETSEL, 0, ref range);
        var max = _projection.Display.Length - _offsets.NewlineCount;
        var a = binding.InputSourceStart + _projection.ToSourceBoundary(
            _offsets.ToDisplay(Math.Clamp(range.Min, 0, max)));
        var b = binding.InputSourceStart + _projection.ToSourceBoundary(
            _offsets.ToDisplay(Math.Clamp(range.Max, 0, max)), towardEnd: true);
        SelectionRequested?.Invoke(a, b);
    }

    private string ReadInputText()
    {
        var length = (int)Win32.SendMessageW(_input, Win32.WM_GETTEXTLENGTH, 0, 0);
        var capacity = checked((length + 1) * 2 + 16);
        var buffer = Marshal.AllocHGlobal(checked(capacity * sizeof(char)));
        try
        {
            var request = new Win32.GetTextEx
            {
                ByteCapacity = (uint)(capacity * sizeof(char)),
                Flags = Win32.GT_USECRLF,
                CodePage = Win32.CP_UNICODE
            };
            var count = (int)Win32.SendMessageW(_input, Win32.EM_GETTEXTEX, ref request, buffer);
            return Marshal.PtrToStringUni(buffer, count) ?? "";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private void SetNativeSelection(int anchor, int active)
    {
        if (_input == 0 || _binding is null) return;
        var start = _binding.InputSourceStart;
        var end = start + _binding.InputSourceText.Length;
        var a = _offsets.ToNative(_projection.ToDisplay(Math.Clamp(anchor, start, end) - start));
        var b = _offsets.ToNative(_projection.ToDisplay(Math.Clamp(active, start, end) - start));
        var range = new Win32.CharacterRange { Min = a, Max = b };
        _settingSelection = true;
        try { Win32.SendMessageW(_input, Win32.EM_EXSETSEL, 0, ref range); }
        finally { _settingSelection = false; }
    }

    private void PlaceInput()
    {
        if (_input == 0 || _binding is null) return;
        var active = _frame?.SelectionActive ?? _binding.Active;
        ViewportSlice? target = null;
        if (_frame is not null)
        {
            foreach (var slice in _frame.Slices)
            {
                if (active < slice.SourceStart ||
                    active > slice.SourceStart + slice.SourceLength) continue;
                target = slice;
                break;
            }
        }
        var y = target is { } row ? (int)Math.Round(row.TopY) : _height - (int)(LineHeight * 3 + 8);
        _inputX = 24;
        _inputY = Math.Clamp(y, 0, Math.Max(0, _height - (int)(LineHeight * 3 + 8)));
        Win32.MoveWindow(_input, _inputX, _inputY,
            Math.Max(120, _width - _inputX - 18), (int)(LineHeight * 3 + 8), true);
    }

    /// <summary>
    /// Routes the exact checked plain-text clipboard payload straight to the controller.
    /// RichEdit's 32 Ki limit and alternate CF_RTF representation cannot truncate it.
    /// </summary>
    private void PastePlainText()
    {
        if (_composition || _binding is null || !TryReadClipboardText(out var text))
        {
            Win32.MessageBoxW(_parent, "Plain Unicode clipboard text could not be pasted safely.",
                "mote", Win32.MB_OK | Win32.MB_ICONERROR);
            return;
        }
        // Settle a prior WM_CHAR before applying a second independent transaction.
        if (!FlushPendingText()) return;
        var binding = _binding;
        if (binding is null) return;
        var (localStart, localEnd) = InputSourceSelection(binding);
        var frame = _frame;
        var globalSelection = frame is not null && frame.SelectionLength > 0;
        var start = globalSelection ? frame!.SelectionStart : localStart;
        var length = globalSelection ? frame!.SelectionLength : localEnd - localStart;
        var insert = NormalizeNewlines(text, PreferredNewline(binding.Snapshot, start));
        if (!globalSelection)
            SelectionRequested?.Invoke(localStart, localEnd);
        EditCommitted?.Invoke(new CanvasCommittedEdit(binding.DocumentGeneration,
            binding.BaseVersion, binding.BindingNonce,
            new TextChange(start, length, insert), checked(start + insert.Length)));
    }

    private bool TryReadClipboardText(out string text)
    {
        text = "";
        if (!Win32.OpenClipboard(_input)) return false;
        try
        {
            var data = GetClipboardData(Win32.CF_UNICODETEXT);
            if (data == 0) return false;
            var size = GlobalSize(data);
            if (size < 2 || size > int.MaxValue) return false;
            var pointer = Win32.GlobalLock(data);
            if (pointer == 0) return false;
            try
            {
                var count = checked((int)size / sizeof(char));
                for (var i = 0; i < count; i++)
                    if (Marshal.ReadInt16(pointer, i * sizeof(char)) == 0)
                    {
                        text = Marshal.PtrToStringUni(pointer, i) ?? "";
                        return true;
                    }
                return false;
            }
            finally { Win32.GlobalUnlock(data); }
        }
        finally { Win32.CloseClipboard(); }
    }

    private (int Start, int End) InputSourceSelection(NativeCanvasBinding binding)
    {
        var range = new Win32.CharacterRange();
        Win32.SendMessageW(_input, Win32.EM_EXGETSEL, 0, ref range);
        var max = _projection.Display.Length - _offsets.NewlineCount;
        var start = binding.InputSourceStart + _projection.ToSourceBoundary(
            _offsets.ToDisplay(Math.Clamp(range.Min, 0, max)));
        var end = binding.InputSourceStart + _projection.ToSourceBoundary(
            _offsets.ToDisplay(Math.Clamp(range.Max, 0, max)), towardEnd: true);
        return (start, end);
    }

    private static string PreferredNewline(TextSnapshot snapshot, int sourceOffset)
    {
        var line = snapshot.GetLineIndexFromOffset(sourceOffset);
        if (line > 0)
        {
            var prior = snapshot.GetLineStartOffset(line);
            var last = snapshot.GetText(prior - 1, 1)[0];
            if (last == '\n') return prior > 1 && snapshot.GetText(prior - 2, 1)[0] == '\r'
                ? "\r\n" : "\n";
            if (last == '\r') return "\r";
        }
        if (line + 1 < snapshot.LineCount)
        {
            var next = snapshot.GetLineStartOffset(line + 1);
            var last = snapshot.GetText(next - 1, 1)[0];
            if (last == '\n') return next > 1 && snapshot.GetText(next - 2, 1)[0] == '\r'
                ? "\r\n" : "\n";
            if (last == '\r') return "\r";
        }
        return Environment.NewLine;
    }

    private static string NormalizeNewlines(string source, string newline)
    {
        var result = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\r' && i + 1 < source.Length && source[i + 1] == '\n')
            {
                result.Append(newline);
                i++;
            }
            else if (source[i] is '\r' or '\n') result.Append(newline);
            else result.Append(source[i]);
        }
        return result.ToString();
    }

    private static bool HasImeResultString(nint input)
    {
        var context = ImmGetContext(input);
        if (context == 0) return false;
        try { return ImmGetCompositionStringW(context, GcsResultString, 0, 0) > 0; }
        finally { ImmReleaseContext(input, context); }
    }

    private void SetInputAppearance(IThemePolicy theme)
    {
        if (_input == 0) return;
        Win32.SendMessageW(_input, Win32.EM_SETBKGNDCOLOR, 0,
            (nint)(uint)(theme.Palette.EditorBackground.Red |
                theme.Palette.EditorBackground.Green << 8 |
                theme.Palette.EditorBackground.Blue << 16));
        var old = _inputFont;
        var family = theme.Typography.EditorFontFamilies.Split(',', 2)[0].Trim();
        _inputFont = Win32.CreateFontW(-(int)Math.Round(theme.Typography.EditorFontSize * 96 / 72),
            0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, family.Length == 0 ? "Consolas" : family);
        if (_inputFont != 0)
            Win32.SendMessageW(_input, Win32.WM_SETFONT, (nuint)_inputFont, (nint)1);
        if (old != 0) Win32.DeleteObject(old);
        if (_binding is not null)
        {
            _settingText = true;
            try
            {
                ApplyInputTextColor();
                SetNativeSelection(_frame?.SelectionAnchor ?? _binding.Anchor,
                    _frame?.SelectionActive ?? _binding.Active);
            }
            finally { _settingText = false; }
        }
    }

    private void ApplyInputTextColor()
    {
        if (_input == 0) return;
        var range = new Win32.CharacterRange { Min = 0, Max = -1 };
        Win32.SendMessageW(_input, Win32.EM_EXSETSEL, 0, ref range);
        var color = _theme.Palette.EditorForeground;
        var format = new Win32.CharacterFormat
        {
            Size = (uint)Marshal.SizeOf<Win32.CharacterFormat>(),
            Mask = Win32.CFM_COLOR,
            TextColor = (uint)(color.Red | color.Green << 8 | color.Blue << 16),
            FaceName = ""
        };
        Win32.SendMessageW(_input, Win32.EM_SETCHARFORMAT, Win32.SCF_SELECTION, ref format);
    }

    private int HitSource(nint lParam)
    {
        var frame = _frame;
        var snapshot = _snapshot;
        if (frame is null || snapshot is null || frame.Slices.Count == 0) return 0;
        var x = Math.Max(0, (short)((long)lParam & 0xFFFF) - TextLeft);
        var y = Math.Clamp((short)(((long)lParam >> 16) & 0xFFFF), 0, _height - 1);
        ViewportSlice? match = null;
        foreach (var slice in frame.Slices)
        {
            if (y < slice.TopY || y >= slice.TopY + slice.Height) continue;
            match = slice;
            break;
        }
        var row = match ?? frame.Slices[^1];
        if (row.SourceLength == 0) return row.SourceStart;
        var hit = _geometry.HitTestPoint(snapshot, row, x, y);
        var offset = hit.SourceStart + (hit.IsTrailing ? hit.SourceLength : 0);
        return Math.Clamp(offset, row.SourceStart, row.SourceStart + row.SourceLength);
    }

    private void Paint()
    {
        var dc = CanvasWin32.BeginPaint(_window, out var paint);
        if (dc == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "BeginPaint failed.");
        try
        {
            EnsureBitmap();
            _painter.Bind(_memoryDc, _width, _height);
            _painter.Begin();
            var frame = _frame;
            var snapshot = _snapshot;
            if (frame is not null && snapshot is not null && frame.Version == snapshot.Version)
            {
                foreach (var row in frame.Slices)
                {
                    if (row.SourceLength > 16 * 1024)
                        throw new InvalidOperationException("Canvas attempted to shape an unbounded slice.");
                    var selected = DrawSelection(snapshot, row, frame);
                    _painter.Text(snapshot.GetText(row.SourceStart, row.SourceLength),
                        TextLeft, (float)row.TopY, selected, ColorsFor(row));
                    DrawDiagnostics(snapshot, row);
                }
            }
            _painter.End();
            if (!CanvasWin32.BitBlt(dc, 0, 0, _width, _height,
                _memoryDc, 0, 0, CanvasWin32.Srccopy))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "BitBlt failed.");
        }
        finally { CanvasWin32.EndPaint(_window, ref paint); }
    }

    private IReadOnlyList<WindowsCanvasColorSpan>? ColorsFor(ViewportSlice row)
    {
        var semantics = _semantics;
        if (semantics is null || _snapshot is null ||
            semantics.Version != _snapshot.Version || semantics.Tokens.Count == 0)
            return null;
        var start = row.SourceStart;
        var end = start + row.SourceLength;
        var spans = new List<WindowsCanvasColorSpan>();
        foreach (var token in semantics.Tokens)
        {
            if (token.Span.Start >= end) break;
            if (token.Span.Start + token.Span.Length <= start) continue;
            var overlapStart = Math.Max(start, token.Span.Start);
            var overlapEnd = Math.Min(end, token.Span.Start + token.Span.Length);
            if (overlapEnd <= overlapStart) continue;
            spans.Add(new WindowsCanvasColorSpan(overlapStart - start,
                overlapEnd - overlapStart, _theme.SemanticColor(token.Kind)));
        }
        return spans;
    }

    private void DrawDiagnostics(TextSnapshot snapshot, ViewportSlice row)
    {
        var semantics = _semantics;
        if (semantics is null || semantics.Version != snapshot.Version ||
            semantics.Diagnostics.Count == 0) return;
        var rowEnd = row.SourceStart + row.SourceLength;
        foreach (var diagnostic in semantics.Diagnostics)
        {
            if (diagnostic.Span.Start > rowEnd) break;
            var start = Math.Max(row.SourceStart, diagnostic.Span.Start);
            var end = Math.Min(rowEnd,
                diagnostic.Span.Start + Math.Max(diagnostic.Span.Length, 1));
            if (end < start) continue;
            var left = row.SourceLength == 0 ? 0 : _geometry.HitTest(snapshot, row, start).X;
            var right = row.SourceLength == 0 ? 0 : _geometry.HitTest(snapshot, row, end).X;
            var color = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => _theme.Palette.Error,
                DiagnosticSeverity.Warning => _theme.Palette.Warning,
                _ => _theme.Palette.Info
            };
            _painter.DiagnosticUnderline(TextLeft + Math.Min(left, right),
                (float)(row.TopY + row.Height - 3),
                TextLeft + Math.Max(left, right), color);
        }
    }

    private (float Left, float Top, float Right, float Bottom)? DrawSelection(
        TextSnapshot snapshot, ViewportSlice row, CanvasFrame frame)
    {
        var endSelection = frame.SelectionStart + frame.SelectionLength;
        var endContent = row.SourceStart + row.SourceLength;
        var start = Math.Max(row.SourceStart, frame.SelectionStart);
        var end = Math.Min(endContent, endSelection);
        (float Left, float Top, float Right, float Bottom)? bounds = null;
        if (end > start)
        {
            var left = _geometry.HitTest(snapshot, row, start);
            var right = _geometry.HitTest(snapshot, row, end);
            bounds = (TextLeft + Math.Min(left.X, right.X), (float)row.TopY,
                TextLeft + Math.Max(left.X, right.X), (float)(row.TopY + row.Height));
            _painter.Selection(bounds.Value.Left, bounds.Value.Top,
                bounds.Value.Right, bounds.Value.Bottom);
        }
        var next = row.Line + 1 < snapshot.LineCount
            ? snapshot.GetLineStartOffset(row.Line + 1) : endContent;
        if (!row.HasHiddenSuffix && next > endContent &&
            frame.SelectionStart < next && endSelection > endContent)
        {
            var right = row.SourceLength == 0 ? TextLeft :
                TextLeft + _geometry.HitTest(snapshot, row, endContent).X;
            _painter.Selection(right, (float)row.TopY,
                Math.Max(right + 8, _width - 8), (float)(row.TopY + row.Height));
        }
        return bounds;
    }

    private double ScrollDelta(int command)
    {
        var frame = _frame;
        var snapshot = _snapshot;
        if (frame is null || snapshot is null) return 0;
        var total = Math.Max(_height, snapshot.LineCount * LineHeight);
        return command switch
        {
            0 => -LineHeight, 1 => LineHeight,
            2 => -_height, 3 => _height,
            4 or 5 => ThumbDelta(total, frame.ScrollY),
            6 => -frame.ScrollY,
            7 => total - _height - frame.ScrollY,
            _ => 0
        };
    }

    private double ThumbDelta(double total, double scrollY)
    {
        var info = new CanvasWin32.ScrollInfo
        {
            Size = (uint)Marshal.SizeOf<CanvasWin32.ScrollInfo>(), Mask = 0x10
        };
        return CanvasWin32.GetScrollInfo(_window, 1, ref info)
            ? info.TrackPosition / (double)ScrollRange * total - scrollY : 0;
    }

    private void UpdateScrollbar()
    {
        if (_window == 0 || _snapshot is null || _frame is null) return;
        var total = Math.Max(_height, _snapshot.LineCount * LineHeight);
        var info = new CanvasWin32.ScrollInfo
        {
            Size = (uint)Marshal.SizeOf<CanvasWin32.ScrollInfo>(), Mask = 0x17,
            Minimum = 0, Maximum = ScrollRange,
            Page = (uint)Math.Clamp(_height / total * ScrollRange, 1, ScrollRange),
            Position = (int)Math.Clamp(_frame.ScrollY / total * ScrollRange, 0, ScrollRange)
        };
        CanvasWin32.SetScrollInfo(_window, 1, ref info, true);
    }

    private void EnsureBitmap()
    {
        if (_memoryDc != 0 && _bitmap != 0) return;
        _memoryDc = CanvasWin32.CreateCompatibleDC(0);
        if (_memoryDc == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateCompatibleDC failed.");
        var info = new CanvasWin32.BitmapInfo
        {
            Header = new CanvasWin32.BitmapInfoHeader
            {
                Size = 40, Width = _width, Height = -_height,
                Planes = 1, BitCount = 32
            }
        };
        _bitmap = CanvasWin32.CreateDIBSection(0, ref info, 0, out _, 0, 0);
        if (_bitmap == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateDIBSection failed.");
        _priorBitmap = CanvasWin32.SelectObject(_memoryDc, _bitmap);
    }

    private void ReleaseBitmap()
    {
        if (_memoryDc != 0 && _priorBitmap != 0)
            CanvasWin32.SelectObject(_memoryDc, _priorBitmap);
        _priorBitmap = 0;
        if (_bitmap != 0) CanvasWin32.DeleteObject(_bitmap);
        _bitmap = 0;
        if (_memoryDc != 0) CanvasWin32.DeleteDC(_memoryDc);
        _memoryDc = 0;
    }

    private void Invalidate()
    {
        if (_window != 0) Win32.InvalidateRect(_window, 0, false);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint GlobalSize(nint memory);

    [DllImport("imm32.dll")]
    private static extern nint ImmGetContext(nint window);

    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(nint window, nint context);

    [DllImport("imm32.dll")]
    private static extern int ImmGetCompositionStringW(nint context, uint index,
        nint buffer, uint bufferBytes);
}
