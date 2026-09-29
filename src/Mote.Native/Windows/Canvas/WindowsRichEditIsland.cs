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
    /// <summary>Maximum safe single-line RichEdit projection before native layout limits.</summary>
    internal const int MaxInputLength = 2048;
    private const int InputHardLimit = 32 * 1024;
    private const uint CommitMessage = Win32.WM_APP + 41;
    private const uint SelectionMessage = Win32.WM_APP + 42;
    private const uint ClearSuppressedBackspaceMessage = Win32.WM_APP + 43;
    private const uint WmImeComposition = 0x010F;
    private const uint WmGetObject = 0x003D;
    private const uint WmMouseHWheel = 0x020E;
    private const uint WmHScroll = 0x0114;
    private const int EmPosFromChar = 0x00D6;
    private const int EmGetScrollPos = 0x04DD;
    private const int EmSetScrollPos = 0x04DE;
    private const int MkShift = 0x0004;
    private const uint GcsResultString = 0x0800;
    private const int ScrollRange = 1_000_000;
    private const float TextLeft = 24;
    private const int InputLabelWidth = 180;
    private static readonly Win32.WindowProcedure WindowProcedure = Dispatch;
    private static readonly Win32.SubclassProcedure InputProcedure = InputSubclass;
    private static WindowsRichEditIsland? _creating;
    private static WindowsRichEditIsland? _active;
    private readonly nint _parent;
    private readonly bool _fragmentExperiment;
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
    private bool _compositionObserved;
    private bool _compositionSettledPending;
    private bool _compositionAttempted;
    private bool _commitQueued;
    private bool _selectionQueued;
    private bool _dragging;
    private bool _suppressBackspaceChar;
    private bool _bodyResizeDeferred;
    private int _dragAnchor;
    private int _publishedBodyHeight;
    private PendingEditSelection? _pendingEditSelection;
    private bool _faulted;
    private bool _disposed;

    private readonly record struct PendingEditSelection(long Nonce, int DisplayStart,
        int DisplayEnd, int LocalStart, int LocalEnd, int GlobalStart, int GlobalLength,
        bool ConfirmedReplacement = false);

    /// <summary>Creates a child canvas inside the existing native editor window.</summary>
    internal WindowsRichEditIsland(nint parent, IThemePolicy theme,
        bool fragmentExperiment = false)
    {
        if (parent == 0) throw new ArgumentOutOfRangeException(nameof(parent));
        ArgumentNullException.ThrowIfNull(theme);
        _parent = parent;
        _fragmentExperiment = fragmentExperiment;
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
                    Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPCHILDREN |
                    Win32.WS_VSCROLL | Win32.WS_HSCROLL,
                    0, 0, 1, 1, parent, 0, instance, 0);
            }
            finally { _creating = null; }
            if (_window == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot create interactive canvas.");
            _input = Win32.CreateWindowExW(0, "RICHEDIT50W", "",
                Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_TABSTOP |
                Win32.ES_MULTILINE | Win32.ES_WANTRETURN | Win32.ES_NOHIDESEL |
                Win32.ES_AUTOHSCROLL,
                24, 0, 300, 24, _window, (nint)InputId, instance, 0);
            if (_input == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot create RichEdit input island.");
            Win32.SendMessageW(_input, Win32.EM_EXLIMITTEXT, 0, (nint)InputHardLimit);
            Win32.SendMessageW(_input, Win32.EM_SETEVENTMASK, 0,
                (nint)(Win32.ENM_CHANGE | Win32.ENM_SELCHANGE | 0x10000000 | 0x20000000));
            if (!Win32.SetWindowSubclass(_input, InputProcedure, 2, 0))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot subclass input island.");
            SetInputAppearance(theme, updateFont: true);
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

    /// <summary>Whether a final IME edit callback still owns the input transaction.</summary>
    internal bool IsCompositionPending => _composition || _compositionSettledPending;

    /// <summary>Document pixels; the remaining bottom strip is a separate OS input ribbon.</summary>
    private int BodyHeight => Math.Max(0, _height - RibbonHeight);

    /// <summary>A visible input row plus separator, independent of source-row painting.</summary>
    private int RibbonHeight => Math.Max(24, (int)Math.Ceiling(LineHeight) + 8);

    /// <summary>
    /// Measures a caret against the same bounded row transform used for paint
    /// and pointer hit-testing; source-window membership alone is insufficient.
    /// </summary>
    internal CanvasCaretGeometry? GetCaretGeometry(CanvasFrame frame, int sourceOffset)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var snapshot = _snapshot;
        if (_height <= RibbonHeight || snapshot is null || !ReferenceEquals(frame, _frame) ||
            frame.Version != snapshot.Version || frame.RowWindows.IsDefaultOrEmpty ||
            frame.RowWindows.Length != frame.Slices.Count ||
            sourceOffset < 0 || sourceOffset > snapshot.Length) return null;
        for (var index = 0; index < frame.Slices.Count; index++)
        {
            var row = frame.Slices[index];
            if (sourceOffset < row.SourceStart ||
                sourceOffset > row.SourceStart + row.SourceLength) continue;
            if (!TryRowOrigin(snapshot, frame, index, out var origin)) return null;
            var hit = _geometry.HitTest(snapshot, row, sourceOffset);
            var x = origin + hit.X;
            var visible = x >= TextLeft && x < _width - 8 &&
                hit.Y >= 0 && hit.Y + hit.Height <= BodyHeight;
            if (visible && sourceOffset == frame.SelectionActive &&
                _binding is { } binding &&
                sourceOffset >= binding.InputSourceStart &&
                sourceOffset <= binding.InputSourceStart + binding.InputSourceText.Length)
            {
                // The input ribbon intentionally has a different coordinate
                // space. Prove its physical OS caret is visible, but never
                // pretend its X/Y equals the source canvas glyph position.
                if (!TryInputCaretPoint(sourceOffset, out var native) ||
                    native.X < 0 || native.X >= Math.Max(1, _width - _inputX - 4) ||
                    native.Y < 0 || native.Y >= RibbonHeight)
                    return null;
            }
            return new CanvasCaretGeometry(x, hit.Y, hit.Height, visible);
        }
        return null;
    }

    /// <summary>Settles a queued ordinary edit before save/new/close, vetoing IME preedit.</summary>
    internal bool FlushPendingText()
    {
        if (_composition) return false;
        if (_commitQueued)
        {
            _commitQueued = false;
            CommitFinalText();
        }
        FinishCompositionNotification();
        return true;
    }

    /// <summary>One final edit tagged with the exact controller binding.</summary>
    internal event Action<CanvasCommittedEdit>? EditCommitted;
    /// <summary>A wheel/scrollbar delta in logical pixels.</summary>
    internal event Action<double>? ScrollRequested;
    /// <summary>A DirectWrite-resolved horizontal source edge for the controller.</summary>
    internal event Action<CanvasHorizontalAnchorRequest>? HorizontalAnchorRequested;
    /// <summary>The current view height in logical pixels.</summary>
    internal event Action<double>? ViewportResized;
    /// <summary>Global UTF-16 source pointer selection.</summary>
    internal event Action<int, int>? SelectionRequested;
    /// <summary>First native callback failure; input is disabled before notification.</summary>
    internal event Action<string>? Faulted;
    /// <summary>An AX-only failure detached the provider without disabling text input.</summary>
    internal event Action? AccessibilityFaulted;
    /// <summary>Raised after final commit/cancel reconciliation leaves the native input island.</summary>
    internal event Action? CompositionFinished;

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
        _uiaBridge = _fragmentExperiment
            ? new WindowsUiaBridgePrototype(document, viewport, _input,
                fragmentExperiment: true)
            : new WindowsUiaBridgePrototype(document, viewport);
        _uiaBridge.SetSourceBodyClientHeight(BodyHeight);
        _publishedBodyHeight = BodyHeight;
        _uiaResponder = responderOverride ?? _uiaBridge.HandleGetObject;
    }

    /// <summary>Replaces only the bounded input interval after composition has ended.</summary>
    internal void Bind(NativeCanvasBinding binding)
    {
        if (_faulted) return;
        ArgumentNullException.ThrowIfNull(binding);
        if (_composition) throw new InvalidOperationException("IME composition owns the input island.");
        if (binding.InputSourceText.Length > MaxInputLength ||
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
            if (_binding is not null) SetNativeSelection(frame.SelectionAnchor, frame.SelectionActive);
            PlaceInput();
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
        if (IsCompositionPending) throw new NativeThemeDeferredException();
        var updateMetrics = _theme.Typography != theme.Typography ||
            _theme.Spacing != theme.Spacing;
        var geometry = updateMetrics ? NewGeometry(theme) : null;
        WindowsCanvasPainter painter;
        try { painter = new WindowsCanvasPainter(theme); }
        catch
        {
            geometry?.Dispose();
            throw;
        }
        var oldTheme = _theme;
        var oldGeometry = _geometry;
        var oldPainter = _painter;
        _theme = theme;
        _painter = painter;
        if (geometry is not null) _geometry = geometry;
        try
        {
            SetInputAppearance(theme, updateMetrics);
            if (updateMetrics)
            {
                PlaceInput();
                PublishBodyHeight();
            }
            Invalidate();
        }
        catch
        {
            _theme = oldTheme;
            _geometry = oldGeometry;
            _painter = oldPainter;
            try { SetInputAppearance(oldTheme, updateMetrics); }
            catch { /* Preserve the original failure for the controller's rollback. */ }
            geometry?.Dispose();
            painter.Dispose();
            throw;
        }
        oldPainter.Dispose();
        if (geometry is not null) oldGeometry.Dispose();
    }

    /// <summary>
    /// Publishes one body boundary with monotonic UIA/frame ordering: expand
    /// provider bounds before the larger frame, shrink after the smaller frame.
    /// </summary>
    private void PublishBodyHeight()
    {
        var height = BodyHeight;
        if (height > _publishedBodyHeight)
            _uiaBridge?.SetSourceBodyClientHeight(height);
        ViewportResized?.Invoke(height);
        if (height <= _publishedBodyHeight)
            _uiaBridge?.SetSourceBodyClientHeight(height);
        _publishedBodyHeight = height;
    }

    /// <summary>Settles a deferred resize only after OS IME releases its candidate.</summary>
    private void FinishDeferredBodyResize()
    {
        if (!_bodyResizeDeferred) return;
        _bodyResizeDeferred = false;
        PlaceInput();
        PublishBodyHeight();
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
                if (_height <= RibbonHeight) SuspendPointerDrag();
                if (!_composition) PlaceInput();
                UpdateScrollbar();
                if (_composition) _bodyResizeDeferred = true;
                else PublishBodyHeight();
                Invalidate();
                return 0;
            case CanvasWin32.WmPaint:
                Paint();
                return 0;
            case WmGetObject when _uiaBridge is not null:
                return HandleAccessibilityGetObject(window, wParam, lParam);
            case CanvasWin32.WmMouseWheel:
                if (!_composition && BodyHeight > 0)
                {
                    var delta = (short)(((ulong)wParam >> 16) & 0xFFFF);
                    if (((ulong)wParam & MkShift) != 0)
                        PanHorizontal(-delta / 120d * 3 * LineHeight);
                    else ScrollRequested?.Invoke(-delta / 120d * 3 * LineHeight);
                }
                return 0;
            case WmMouseHWheel:
                if (!_composition && BodyHeight > 0)
                    PanHorizontal((short)(((ulong)wParam >> 16) & 0xFFFF) /
                        120d * 3 * LineHeight);
                return 0;
            case CanvasWin32.WmVScroll:
                if (!_composition && BodyHeight > 0)
                    ScrollRequested?.Invoke(ScrollDelta((int)(wParam & 0xFFFF)));
                return 0;
            case WmHScroll:
                if (!_composition && BodyHeight > 0) HorizontalScrollbar((int)(wParam & 0xFFFF));
                return 0;
            case CanvasWin32.WmLButtonDown:
                if (_composition) return 0;
                if (_height <= RibbonHeight) return 0;
                if ((short)(((long)lParam >> 16) & 0xFFFF) >= BodyHeight) return 0;
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
                        BeginComposition();
                    else if (header.Window == _input && header.Code == 0x0714)
                        EndComposition();
                }
                return 0;
            case CommitMessage:
                if (!_composition)
                {
                    if (_commitQueued)
                    {
                        _commitQueued = false;
                        CommitFinalText();
                    }
                    FinishCompositionNotification();
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
            island.BeginComposition();
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
            // The ribbon is input focus, not a second source selection surface.
            // A user changes the canonical caret on the canvas above it.
            if (message == CanvasWin32.WmLButtonDown) Win32.SetFocus(window);
            return 0;
        }
        if (message == CanvasWin32.WmMouseWheel)
        {
            if (!island._composition && island.BodyHeight > 0)
            {
                var delta = (short)(((ulong)wParam >> 16) & 0xFFFF);
                if (((ulong)wParam & MkShift) != 0)
                    island.PanHorizontal(-delta / 120d * 3 * island.LineHeight);
                else island.ScrollRequested?.Invoke(-delta / 120d * 3 * island.LineHeight);
            }
            return 0;
        }
        if (message == WmMouseHWheel)
        {
            if (!island._composition && island.BodyHeight > 0)
                island.PanHorizontal((short)(((ulong)wParam >> 16) & 0xFFFF) /
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
            island.EndComposition();
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

    /// <summary>Starts one OS-owned preedit without duplicating the captured edit base.</summary>
    private void BeginComposition()
    {
        if (_composition) return;
        CaptureBeforeEdit();
        _composition = true;
        _compositionObserved = true;
        _compositionAttempted = true;
        SuspendPointerDrag();
    }

    /// <summary>Queues final reconcile before announcing a committed or cancelled IME episode.</summary>
    private void EndComposition()
    {
        if (!_composition && !_compositionObserved) return;
        _composition = false;
        _compositionObserved = false;
        _compositionSettledPending = true;
        QueueCommit();
        // A prior CommitMessage can have been consumed during preedit while
        // _commitQueued stayed true; ensure one post-end callback still runs.
        if (_window != 0) Win32.PostMessageW(_window, CommitMessage, 0, 0);
        FinishDeferredBodyResize();
    }

    /// <summary>Announces one settled episode after its final source transaction.</summary>
    private void FinishCompositionNotification()
    {
        if (_composition || !_compositionSettledPending) return;
        _compositionSettledPending = false;
        CompositionFinished?.Invoke();
    }

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
        // The focused OS editor occupies an explicit ribbon, never the source
        // canvas. No independently shaped host glyphs can cover a source row.
        var inputHeight = Math.Max(18, RibbonHeight - 4);
        var active = _frame?.SelectionActive ?? _binding.Active;
        _inputX = Math.Min(InputLabelWidth, Math.Max(0, _width / 3));
        _inputY = Math.Min(_height - 1, BodyHeight + 2);
        var inputWidth = Math.Max(1, _width - _inputX - 4);
        Win32.MoveWindow(_input, _inputX, _inputY,
            inputWidth, inputHeight, true);
        if (!TryInputCaretPoint(active, out var native)) return;
        if (native.X < 8 || native.X >= inputWidth - 16)
        {
            var scroll = new NativePoint();
            SendMessagePoint(_input, EmGetScrollPos, 0, ref scroll);
            scroll.X = Math.Max(0, scroll.X + native.X - Math.Min(16, inputWidth / 4));
            SendMessagePoint(_input, EmSetScrollPos, 0, ref scroll);
        }
    }

    /// <summary>Reads RichEdit's real caret position in the bounded host client area.</summary>
    private bool TryInputCaretPoint(int sourceOffset, out NativePoint point)
    {
        point = default;
        if (_input == 0 || _binding is not { } binding ||
            sourceOffset < binding.InputSourceStart ||
            sourceOffset > binding.InputSourceStart + binding.InputSourceText.Length)
            return false;
        var display = _projection.ToDisplay(sourceOffset - binding.InputSourceStart);
        var native = _offsets.ToNative(display);
        SendMessageCaretPoint(_input, EmPosFromChar, ref point, native);
        return point.X != -1 && point.Y != -1;
    }

    /// <summary>POINTL used by RichEdit caret and pixel-scroll messages.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { internal int X; internal int Y; }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessageCaretPoint(nint window, int message,
        ref NativePoint point, int nativeIndex);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessagePoint(nint window, int message,
        nuint unused, ref NativePoint point);

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

    private void SetInputAppearance(IThemePolicy theme, bool updateFont)
    {
        if (_input == 0) return;
        Win32.SendMessageW(_input, Win32.EM_SETBKGNDCOLOR, 0,
            (nint)ColorRef(theme.Palette.PanelBackground));
        if (updateFont)
        {
            var family = theme.Typography.EditorFontFamilies.Split(',', 2)[0].Trim();
            var font = Win32.CreateFontW(
                -(int)Math.Round(theme.Typography.EditorFontSize * 96 / 72),
                0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0,
                family.Length == 0 ? "Consolas" : family);
            if (font == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Input font creation failed.");
            var old = _inputFont;
            _inputFont = font;
            Win32.SendMessageW(_input, Win32.WM_SETFONT, (nuint)font, (nint)1);
            if (old != 0) Win32.DeleteObject(old);
        }
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
        if (BodyHeight == 0 || frame is null || snapshot is null ||
            frame.Slices.Count == 0) return _frame?.SelectionActive ?? 0;
        var screenX = (short)((long)lParam & 0xFFFF);
        var y = Math.Clamp((short)(((long)lParam >> 16) & 0xFFFF), 0, BodyHeight - 1);
        var rowIndex = frame.Slices.Count - 1;
        for (var index = 0; index < frame.Slices.Count; index++)
        {
            var slice = frame.Slices[index];
            if (y < slice.TopY || y >= slice.TopY + slice.Height) continue;
            rowIndex = index;
            break;
        }
        var row = frame.Slices[rowIndex];
        if (row.SourceLength == 0) return row.SourceStart;
        if (!TryRowOrigin(snapshot, frame, rowIndex, out var origin))
            return row.SourceStart;
        var hit = _geometry.HitTestPoint(snapshot, row, screenX - origin, y);
        var offset = hit.SourceStart + (hit.IsTrailing ? hit.SourceLength : 0);
        return Math.Clamp(offset, row.SourceStart, row.SourceStart + row.SourceLength);
    }

    /// <summary>Derives the one screen transform shared by paint, hit-test and caret proof.</summary>
    private bool TryRowOrigin(TextSnapshot snapshot, CanvasFrame frame,
        int index, out float origin)
    {
        origin = TextLeft;
        if (frame.RowWindows.IsDefaultOrEmpty) return true; // Legacy probe frame.
        if (frame.RowWindows.Length != frame.Slices.Count) return false;
        var window = frame.RowWindows[index];
        var row = frame.Slices[index];
        if (window.Slice != row ||
            window.LeftEdgeSourceBoundary < row.SourceStart ||
            window.LeftEdgeSourceBoundary > row.SourceStart + row.SourceLength)
            return false;
        if (row.SourceLength == 0) return true;
        var anchor = _geometry.HitTest(snapshot, row, window.LeftEdgeSourceBoundary);
        origin = TextLeft - anchor.X - (float)window.IntraClusterPixels;
        return float.IsFinite(origin);
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
            if (BodyHeight > 0 && frame is not null && snapshot is not null &&
                frame.Version == snapshot.Version)
            {
                for (var index = 0; index < frame.Slices.Count; index++)
                {
                    var row = frame.Slices[index];
                    if (row.SourceLength > 16 * 1024)
                        throw new InvalidOperationException("Canvas attempted to shape an unbounded slice.");
                    if (!TryRowOrigin(snapshot, frame, index, out var origin)) continue;
                    var selected = DrawSelection(snapshot, row, frame, origin);
                    _painter.Text(snapshot.GetText(row.SourceStart, row.SourceLength),
                        origin, (float)row.TopY, selected, ColorsFor(row));
                    DrawDiagnostics(snapshot, row, origin);
                    DrawSourceCaret(snapshot, row, frame, origin);
                }
            }
            PaintRibbon();
            _painter.End();
            if (!CanvasWin32.BitBlt(dc, 0, 0, _width, _height,
                _memoryDc, 0, 0, CanvasWin32.Srccopy))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "BitBlt failed.");
        }
        finally { CanvasWin32.EndPaint(_window, ref paint); }
    }

    /// <summary>Overpaints the reserved ribbon with a single Direct2D glyph renderer.</summary>
    private void PaintRibbon()
    {
        var body = BodyHeight;
        _painter.Fill(0, body, _width, _height, _theme.Palette.PanelBackground);
        _painter.Fill(0, body, _width, Math.Min(_height, body + 1),
            _theme.Palette.Border);
        if (body == 0) return; // Tiny window: input-only until enlarged.
        var active = _frame?.SelectionActive ?? _binding?.Active ?? 0;
        var label = _inputX < 150 ? "Input" : $"Input @ {active:N0}";
        _painter.Text(label, 8, body + 4, null,
            [new WindowsCanvasColorSpan(0, label.Length, _theme.Palette.MutedForeground)]);
    }

    private static uint ColorRef(ThemeColor color) =>
        (uint)(color.Red | color.Green << 8 | color.Blue << 16);

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

    /// <summary>Shows the canonical source caret even though OS text input lives in the ribbon.</summary>
    private void DrawSourceCaret(TextSnapshot snapshot, ViewportSlice row,
        CanvasFrame frame, float origin)
    {
        if (frame.SelectionLength != 0) return;
        var active = frame.SelectionActive;
        var end = row.SourceStart + row.SourceLength;
        if (active < row.SourceStart || active > end ||
            active == end && row.HasHiddenSuffix) return;
        var local = row.SourceLength == 0 ? 0 : _geometry.HitTest(snapshot, row, active).X;
        var x = origin + local;
        if (x < TextLeft || x >= _width - 8) return;
        _painter.Fill(x, (float)row.TopY + 2, x + 2,
            (float)(row.TopY + row.Height - 2), _theme.Palette.Cursor);
    }

    private void DrawDiagnostics(TextSnapshot snapshot, ViewportSlice row, float origin)
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
            _painter.DiagnosticUnderline(origin + Math.Min(left, right),
                (float)(row.TopY + row.Height - 3),
                origin + Math.Max(left, right), color);
        }
    }

    private (float Left, float Top, float Right, float Bottom)? DrawSelection(
        TextSnapshot snapshot, ViewportSlice row, CanvasFrame frame, float origin)
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
            bounds = (origin + Math.Min(left.X, right.X), (float)row.TopY,
                origin + Math.Max(left.X, right.X), (float)(row.TopY + row.Height));
            _painter.Selection(bounds.Value.Left, bounds.Value.Top,
                bounds.Value.Right, bounds.Value.Bottom);
        }
        var next = row.Line + 1 < snapshot.LineCount
            ? snapshot.GetLineStartOffset(row.Line + 1) : endContent;
        if (!row.HasHiddenSuffix && next > endContent &&
            frame.SelectionStart < next && endSelection > endContent)
        {
            var right = row.SourceLength == 0 ? origin :
                origin + _geometry.HitTest(snapshot, row, endContent).X;
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
        var total = Math.Max(BodyHeight, snapshot.LineCount * LineHeight);
        return command switch
        {
            0 => -LineHeight, 1 => LineHeight,
            2 => -BodyHeight, 3 => BodyHeight,
            4 or 5 => ThumbDelta(total, frame.ScrollY),
            6 => -frame.ScrollY,
            7 => total - BodyHeight - frame.ScrollY,
            _ => 0
        };
    }

    /// <summary>Converts a local wheel/page displacement to a bounded source edge.</summary>
    private void PanHorizontal(double pixels)
    {
        if (!double.IsFinite(pixels) || pixels == 0 || _composition ||
            _frame is not { } frame || _snapshot is not { } snapshot ||
            _binding is null || frame.RowWindows.IsDefaultOrEmpty) return;
        var index = 0;
        for (var i = 0; i < frame.RowWindows.Length; i++)
        {
            if (frame.RowWindows[i].Slice.SourceLength == 0) continue;
            index = i;
            if (snapshot.GetLineStartOffset(frame.Slices[i].Line) ==
                frame.Horizontal.ReferenceLineStart) break;
        }
        var row = frame.Slices[index];
        if (row.SourceLength == 0 || !TryRowOrigin(snapshot, frame, index, out var origin)) return;
        var localX = (float)(TextLeft + pixels - origin);
        var lineStart = snapshot.GetLineStartOffset(row.Line);
        var contentEnd = LineContentEnd(snapshot, row.Line);
        var first = _geometry.HitTest(snapshot, row, row.SourceStart);
        var last = _geometry.HitTest(snapshot, row, row.SourceStart + row.SourceLength);
        if (localX < first.X && row.HasHiddenPrefix)
        {
            var previous = Math.Max(lineStart, row.SourceStart - 64);
            RequestHorizontal(snapshot, frame, previous, 0);
            return;
        }
        if (localX > last.X && row.HasHiddenSuffix)
        {
            RequestHorizontal(snapshot, frame,
                Math.Min(contentEnd, row.SourceStart + row.SourceLength), 0);
            return;
        }
        var hit = _geometry.HitTestPoint(snapshot, row, localX,
            (float)(row.TopY + row.Height * 0.5));
        if (hit.BidiLevel % 2 != 0) return; // Visual RTL edge needs explicit affinity proof.
        var source = Math.Clamp(hit.SourceStart, row.SourceStart,
            row.SourceStart + row.SourceLength);
        var residual = Math.Max(0, localX - hit.X);
        if (hit.SourceLength > 0 && residual >= hit.Width)
        {
            source = Math.Min(contentEnd, hit.SourceStart + hit.SourceLength);
            residual = 0;
        }
        RequestHorizontal(snapshot, frame, source, residual);
    }

    /// <summary>Maps OS scrollbar commands to source-bound horizontal requests.</summary>
    private void HorizontalScrollbar(int command)
    {
        var frame = _frame;
        var snapshot = _snapshot;
        if (frame is null || snapshot is null || _binding is null) return;
        var line = snapshot.GetLineIndexFromOffset(frame.Horizontal.SourceBoundary);
        var start = snapshot.GetLineStartOffset(line);
        var end = LineContentEnd(snapshot, line);
        if (end == start) return;
        switch (command)
        {
            case 0: PanHorizontal(-32); return;
            case 1: PanHorizontal(32); return;
            case 2: PanHorizontal(-Math.Max(32, _width - TextLeft - 32)); return;
            case 3: PanHorizontal(Math.Max(32, _width - TextLeft - 32)); return;
            case 6: RequestHorizontal(snapshot, frame, start, 0); return;
            case 7: RequestHorizontal(snapshot, frame, end, 0); return;
            case 4 or 5:
                var info = new CanvasWin32.ScrollInfo
                {
                    Size = (uint)Marshal.SizeOf<CanvasWin32.ScrollInfo>(), Mask = 0x10
                };
                if (!CanvasWin32.GetScrollInfo(_window, 0, ref info)) return;
                var offset = start + (int)Math.Round((end - start) *
                    Math.Clamp(info.TrackPosition / (double)ScrollRange, 0, 1));
                RequestHorizontal(snapshot, frame, offset, 0);
                return;
        }
    }

    /// <summary>Publishes only a versioned, grapheme-certified source boundary.</summary>
    private void RequestHorizontal(TextSnapshot snapshot, CanvasFrame frame,
        int sourceBoundary, double residual)
    {
        if (_composition || _binding is not { } binding ||
            frame.Version != binding.BaseVersion ||
            sourceBoundary == frame.Horizontal.SourceBoundary &&
            Math.Abs(residual - frame.Horizontal.IntraClusterPixels) < 0.01) return;
        try
        {
            // The pure viewport protects scalar/CRLF seams. The native input
            // selector additionally certifies a bounded grapheme boundary.
            _ = CanvasInputWindowSelector.Select(snapshot, sourceBoundary);
        }
        catch (CanvasInputWindowBoundaryException) { return; }
        HorizontalAnchorRequested?.Invoke(new CanvasHorizontalAnchorRequest(
            binding.DocumentGeneration, frame.Version, sourceBoundary,
            HorizontalCaretAffinity.Leading, residual));
    }

    /// <summary>Returns the logical line end without its preserved CR/LF delimiter.</summary>
    private static int LineContentEnd(TextSnapshot snapshot, int line)
    {
        if (line == snapshot.LineCount - 1) return snapshot.Length;
        var start = snapshot.GetLineStartOffset(line);
        var end = snapshot.GetLineStartOffset(line + 1);
        if (end > start && snapshot.GetText(end - 1, 1)[0] == '\n') end--;
        if (end > start && snapshot.GetText(end - 1, 1)[0] == '\r') end--;
        return end;
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
        var total = Math.Max(BodyHeight, _snapshot.LineCount * LineHeight);
        var info = new CanvasWin32.ScrollInfo
        {
            Size = (uint)Marshal.SizeOf<CanvasWin32.ScrollInfo>(), Mask = 0x17,
            Minimum = 0, Maximum = ScrollRange,
            Page = (uint)Math.Clamp(BodyHeight / total * ScrollRange, 1, ScrollRange),
            Position = (int)Math.Clamp(_frame.ScrollY / total * ScrollRange, 0, ScrollRange)
        };
        CanvasWin32.SetScrollInfo(_window, 1, ref info, true);

        // A source-proportional thumb is deliberately not a pixel-width claim:
        // measuring an entire 50 MiB logical line would defeat bounded shaping.
        var line = _snapshot.GetLineIndexFromOffset(_frame.Horizontal.SourceBoundary);
        var start = _snapshot.GetLineStartOffset(line);
        var end = LineContentEnd(_snapshot, line);
        var length = Math.Max(1, end - start);
        var visibleSource = _frame.RowWindows.IsDefaultOrEmpty ? 1 :
            Math.Max(1, _frame.RowWindows[0].Slice.SourceLength);
        var horizontal = new CanvasWin32.ScrollInfo
        {
            Size = (uint)Marshal.SizeOf<CanvasWin32.ScrollInfo>(), Mask = 0x17,
            Minimum = 0, Maximum = ScrollRange,
            Page = (uint)Math.Clamp(visibleSource / (double)length * ScrollRange,
                1, ScrollRange),
            Position = (int)Math.Clamp(
                (_frame.Horizontal.SourceBoundary - start) / (double)length * ScrollRange,
                0, ScrollRange)
        };
        CanvasWin32.SetScrollInfo(_window, 0, ref horizontal, true);
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
