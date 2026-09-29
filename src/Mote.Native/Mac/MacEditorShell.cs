using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Native.Accessibility;
using Mote.Native.Mac.Accessibility;
using Mote.Native.Mac.Canvas;
using Mote.Native.Viewport;
using Mote.Themes;

namespace Mote.Native.Mac;

/// <summary>
/// macOS AppKit presentation shell for a single bounded document page.
/// NSTextView supplies native text input, selection, clipboard, accessibility, and IME;
/// the controller remains the sole owner of text, undo history, and file operations.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacEditorShell : INativeCanvasShell
{
    private const nuint WindowStyle = 1 | 2 | 4 | 8;
    private const nuint ResizeWidthAndHeight = 2 | 16;
    private const string EditorAppearanceClass = "MoteDefaultEditorAppearanceView";
    private const string ObjcRuntime = "/usr/lib/libobjc.A.dylib";
    private static MacEditorShell? s_current;

    [DllImport(ObjcRuntime, EntryPoint = "objc_msgSendSuper")]
    private static extern void SendSuperNoArgument(ref MacOnScreenCanvasNative.Super receiver,
        nint selector);
    private readonly bool _experimentalCanvas;
    private readonly ConcurrentQueue<Action> _posted = new();
    private nint _application;
    private nint _delegate;
    private nint _window;
    private nint _editor;
    private nint _preview;
    private nint _status;
    private MacTextInputIsland? _canvas;
    private MacAccessibilityElementPrototype? _accessibility;
    private int _uiThreadId;
    private int _accessibilityFaultScheduled;
    private NativeCanvasBinding? _pendingCanvasBinding;
    private CanvasFrame? _pendingCanvasFrame;
    private NativeCanvasSemantics? _pendingCanvasSemantics;
    private (Mote.Engine.TextSnapshot Snapshot, CanvasFrame Frame, string Reason)?
        _pendingCanvasUnavailable;
    private IThemePolicy? _theme;
    private NativeDocumentView? _pendingDocument;
    private NativeAnalysisView? _pendingAnalysis;
    private Mote.Engine.TextSnapshot? _presentedCanvasSnapshot;
    private bool _deferredDocument;
    private string? _deferredAnalysisText;
    private bool _settingText;
    private bool _settingSelection;
    private bool _compositionDirty;
    private bool _compositionObserved;
    private bool _compositionCheckScheduled;
    private bool _compositionCommitRejected;
    private bool _closeApproved;
    private string _statusText = string.Empty;
    private string? _statusNotice;
    private string _canvasTitle = "mote";
    private bool _canvasIsModified;
    private string _visibleText = string.Empty;
    private string _findQuery = string.Empty;
    private int _selectionAnchor;
    private ObjC.Range? _pendingNativeSelection;
    private bool _selectionDeliveryScheduled;
    private (int Anchor, int Active)? _pendingSelection;
    private string? _probeOpenPath;
    private string? _probeSavePath;
    private bool _probeConfirmDiscardOnce;
    private bool _probeDidConfirmDiscard;
    private bool _probeCaptureCanvasErrors;
    private string? _probeCanvasError;
    private string? _previewText;
    private bool? _lastAppearanceDark;
    private bool _appearanceNotificationsReady;

    /// <inheritdoc />
    public bool IsTextComposing => _experimentalCanvas
        ? _canvas?.HasPendingComposition == true
        : _editor != 0 && (_compositionDirty ||
            ObjC.Send(_editor, ObjC.Sel("hasMarkedText")) != 0);

    /// <inheritdoc />
    public event Action? AppearanceChanged;

    /// <inheritdoc />
    public event Action? CompositionSettled;

    /// <summary>Constructs the established editor or an explicit, opt-in canvas editor.</summary>
    internal MacEditorShell(bool experimentalCanvas = false) =>
        _experimentalCanvas = experimentalCanvas;

    /// <inheritdoc />
    public bool CanvasEnabled => _experimentalCanvas;

    /// <inheritdoc />
    public int MaxCanvasInputLength => MacTextInputIsland.MaxBindingLength;

    /// <inheritdoc />
    public bool IsCanvasComposing => _experimentalCanvas && _canvas?.IsComposing == true;

    /// <inheritdoc />
    public event Action<CanvasCommittedEdit>? CanvasEditCommitted;
    /// <inheritdoc />
    public event Action<double>? CanvasScrollRequested;
    /// <inheritdoc />
    public event Action<CanvasHorizontalAnchorRequest>? CanvasHorizontalAnchorRequested;

    private void RequestHorizontalAnchor(CanvasHorizontalAnchorRequest request) =>
        CanvasHorizontalAnchorRequested?.Invoke(request);
    /// <inheritdoc />
    public event Action<double>? CanvasViewportResized;
    /// <inheritdoc />
    public event Action<int, int>? CanvasSelectionRequested;
    /// <inheritdoc />
    public event Action? CanvasAccessibilityFailed;

    /// <inheritdoc />
    public NativeLineEndingMode LineEndingMode => NativeLineEndingMode.Preserve;

    /// <inheritdoc />
    public bool PrefersDark
    {
        get
        {
            // A visible view may inherit a window-specific appearance rather than
            // NSApplication's default. Ask AppKit to classify custom/vibrant names.
            ObjC.ApplicationLoad();
            var pool = ObjC.New("NSAutoreleasePool");
            try
            {
                var application = ObjC.Send(ObjC.Class("NSApplication"),
                    ObjC.Sel("sharedApplication"));
                var view = _editor != 0 ? _editor : application;
                var appearance = ObjC.Send(view, ObjC.Sel("effectiveAppearance"));
                var names = ObjC.New("NSMutableArray");
                try
                {
                    ObjC.Send(names, ObjC.Sel("addObject:"), ObjC.String("NSAppearanceNameAqua"));
                    ObjC.Send(names, ObjC.Sel("addObject:"), ObjC.String("NSAppearanceNameDarkAqua"));
                    var matched = ObjC.ManagedString(ObjC.Send(appearance,
                        ObjC.Sel("bestMatchFromAppearancesWithNames:"), names));
                    var dark = matched == "NSAppearanceNameDarkAqua" ||
                        matched.Length == 0 && ObjC.ManagedString(ObjC.Send(appearance,
                            ObjC.Sel("name"))).Contains("Dark", StringComparison.OrdinalIgnoreCase);
                    if (!_appearanceNotificationsReady && _lastAppearanceDark is null)
                        _lastAppearanceDark = dark;
                    return dark;
                }
                finally { ObjC.Send(names, ObjC.Sel("release")); }
            }
            finally { ObjC.Send(pool, ObjC.Sel("release")); }
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
        if (s_current is not null) throw new InvalidOperationException("Only one macOS editor shell may run per process.");
        _uiThreadId = Environment.CurrentManagedThreadId;
        s_current = this;
        try
        {
            // A direct invocation of the system framework loads AppKit before objc_getClass.
            ObjC.ApplicationLoad();
            var pool = ObjC.New("NSAutoreleasePool");
            try
            {
                _delegate = ObjC.New(RegisterDelegateClass());
                _application = ObjC.Send(ObjC.Class("NSApplication"), ObjC.Sel("sharedApplication"));
                ObjC.Send(_application, ObjC.Sel("setActivationPolicy:"), 0);
                ObjC.Send(_application, ObjC.Sel("setDelegate:"), _delegate);
                if (_experimentalCanvas) MacTextInputIsland.TraceStage("S0-before-window");
                CreateWindow();
                if (_experimentalCanvas) MacTextInputIsland.TraceStage("S1-window-ready");
                CreateMenu();
                if (_theme is not null) ApplyTheme(_theme);
                if (_pendingDocument is not null) SetDocument(_pendingDocument);
                if (_pendingAnalysis is not null) SetAnalysis(_pendingAnalysis);
                if (_pendingCanvasBinding is { } binding) SetCanvasBinding(binding);
                if (_pendingCanvasUnavailable is { } unavailable)
                    SetCanvasInputUnavailable(unavailable.Snapshot, unavailable.Frame,
                        unavailable.Reason);
                if (_pendingCanvasFrame is { } frame) SetCanvasFrame(frame);
                if (_pendingCanvasSemantics is { } semantics) SetCanvasSemantics(semantics);
                if (_pendingSelection is { } selection) SetSelection(selection.Anchor, selection.Active);
                ObjC.Send(_window, ObjC.Sel("makeKeyAndOrderFront:"), 0);
                ObjC.Send(_application, ObjC.Sel("activateIgnoringOtherApps:"), 1);
                if (_experimentalCanvas) MacTextInputIsland.TraceStage("S2-before-loop");
                if (_experimentalCanvas) Post(() =>
                {
                    MacTextInputIsland.TraceStage("S3-before-body-publish");
                    _canvas?.PublishBodyHeight();
                    MacTextInputIsland.TraceStage("S4-body-published");
                });
                Post(() =>
                {
                    if (_experimentalCanvas) MacTextInputIsland.TraceStage("S5-before-shown");
                    Shown?.Invoke();
                    _appearanceNotificationsReady = true;
                    ObserveAppearanceChanged();
                    if (_experimentalCanvas) MacTextInputIsland.TraceStage("S6-shown-returned");
                });
                ObjC.Send(_application, ObjC.Sel("run"));
            }
            finally
            {
                // The AX element retains AppKit view references. Tear it down
                // on this UI thread before either native view or pool goes away.
                _accessibility?.Dispose();
                _accessibility = null;
                _canvas?.Dispose();
                ObjC.Send(pool, ObjC.Sel("release"));
            }
        }
        finally { s_current = null; }
    }

    /// <inheritdoc />
    public void SetDocument(NativeDocumentView view)
    {
        if (_pendingDocument is { } previous &&
            (previous.Stamp != view.Stamp || previous.PageStart != view.PageStart))
            ClearAnalysisPreview();
        _pendingDocument = view;
        _statusText = view.Status;
        if (_editor == 0) return;
        if (_experimentalCanvas)
        {
            SetCanvasChrome(view.Title, view.Status, view.IsModified);
            return;
        }
        _settingText = true;
        try
        {
            // A canonical echo after typing must not reset the native caret or IME state.
            if (!string.Equals(_visibleText, view.Text, StringComparison.Ordinal))
            {
                if (ObjC.Send(_editor, ObjC.Sel("hasMarkedText")) != 0)
                    _deferredDocument = true;
                else
                {
                    _pendingNativeSelection = null;
                    ObjC.Send(_editor, ObjC.Sel("setString:"), ObjC.String(view.Text));
                    _visibleText = view.Text;
                    _selectionAnchor = 0;
                    _deferredDocument = false;
                }
            }
            if (!_deferredDocument && view.FocusDisplayOffset is { } focus)
            {
                if (focus < 0 || focus > view.Text.Length)
                    throw new ArgumentOutOfRangeException(nameof(view), "Focus must belong to the displayed page.");
                var caret = new ObjC.Range((nuint)focus, 0);
                ObjC.Send(_editor, ObjC.Sel("setSelectedRange:"), caret);
                ObjC.Send(_editor, ObjC.Sel("scrollRangeToVisible:"), caret);
                _selectionAnchor = focus;
                _pendingNativeSelection = null;
            }
            ObjC.Send(_window, ObjC.Sel("setTitle:"), ObjC.String(view.Title));
            SetStatus(view.Status);
        }
        finally { _settingText = false; }
    }

    /// <inheritdoc />
    public void SetAnalysis(NativeAnalysisView view)
    {
        if (!AnalysisMatchesCurrentDocument(view)) return;
        _pendingAnalysis = view;
        if (!_experimentalCanvas && IsTextComposing)
        {
            _deferredAnalysisText = _visibleText;
            return;
        }
        ApplyAnalysis(view, updateFonts: true);
    }

    private bool AnalysisMatchesCurrentDocument(NativeAnalysisView view)
    {
        if (_experimentalCanvas)
            return _pendingCanvasBinding is { } binding && view.Stamp ==
                new NativeDocumentStamp(binding.DocumentGeneration, binding.BaseVersion);
        return _pendingDocument is { } document && view.Stamp == document.Stamp;
    }

    /// <summary>Clears stale token colors without changing text, selection, or preview content.</summary>
    private void RestyleBaseText()
    {
        if (_editor == 0 || _preview == 0 || _theme is null) return;
        if (!_experimentalCanvas)
        {
            var length = checked((int)ObjC.Send(ObjC.Send(_editor,
                ObjC.Sel("string")), ObjC.Sel("length")));
            ObjC.Send(_editor, ObjC.Sel("setTextColor:range:"),
                Color(_theme.Palette.EditorForeground), new ObjC.Range(0, (nuint)length));
        }
        var previewLength = checked((int)ObjC.Send(ObjC.Send(_preview,
            ObjC.Sel("string")), ObjC.Sel("length")));
        ObjC.Send(_preview, ObjC.Sel("setTextColor:range:"),
            Color(_theme.Palette.PreviewForeground),
            new ObjC.Range(0, (nuint)previewLength));
    }

    private void ApplyAnalysis(NativeAnalysisView view, bool updateFonts)
    {
        if (_editor == 0) return;
        if (_experimentalCanvas)
        {
            SetPreview(view, updateFonts);
            SetStatus(string.IsNullOrEmpty(view.DiagnosticsSummary)
                ? view.Status : $"{view.Status}  ·  {view.DiagnosticsSummary}");
            return;
        }
        if (ObjC.Send(_editor, ObjC.Sel("hasMarkedText")) != 0)
        {
            _deferredAnalysisText = _visibleText;
            return;
        }
        _deferredAnalysisText = null;
        var length = checked((int)ObjC.Send(ObjC.Send(_editor, ObjC.Sel("string")), ObjC.Sel("length")));
        var baseColor = Color(_theme?.Palette.EditorForeground ?? new ThemeColor(216, 218, 223));
        ObjC.Send(_editor, ObjC.Sel("setTextColor:range:"), baseColor,
            new ObjC.Range(0, (nuint)length));
        var tokenColors = new Dictionary<string, nint>(StringComparer.Ordinal);
        foreach (var token in view.Tokens)
        {
            var start = token.Span.Start;
            var spanLength = token.Span.Length;
            if (start < 0 || spanLength <= 0 || start > length || spanLength > length - start) continue;
            if (!tokenColors.TryGetValue(token.Kind, out var color))
            {
                color = Color(_theme?.SemanticColor(token.Kind) ?? new ThemeColor(216, 218, 223));
                tokenColors.Add(token.Kind, color);
            }
            ObjC.Send(_editor, ObjC.Sel("setTextColor:range:"), color,
                new ObjC.Range((nuint)start, (nuint)spanLength));
        }
        SetPreview(view, updateFonts);
        SetStatus(string.IsNullOrEmpty(view.DiagnosticsSummary)
            ? view.Status : $"{view.Status}  ·  {view.DiagnosticsSummary}");
    }

    /// <inheritdoc />
    public void SetTheme(IThemePolicy theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (IsTextComposing)
        {
            _compositionObserved = true;
            ScheduleCompositionCheck();
            throw new NativeThemeDeferredException();
        }
        var updateFonts = _theme is null || _theme.Typography != theme.Typography ||
            _theme.Spacing != theme.Spacing;
        if (_editor == 0) { _theme = theme; return; }
        // The island rechecks marked text before touching its host. It must
        // veto an unexpected preedit race before editor/preview colors change.
        try { _canvas?.SetTheme(theme); }
        catch (NativeThemeDeferredException)
        {
            _compositionObserved = true;
            ScheduleCompositionCheck();
            throw;
        }
        _theme = theme;
        ApplyTheme(theme, updateFonts);
        // Cached source-mapped analysis is recolored, never recomputed.
        if (_pendingAnalysis is { } analysis && AnalysisMatchesCurrentDocument(analysis))
            ApplyAnalysis(analysis, updateFonts);
        else
            RestyleBaseText();
    }

    /// <inheritdoc />
    public void SetStatusNotice(string? notice)
    {
        _statusNotice = string.IsNullOrWhiteSpace(notice) ? null : notice;
        RenderStatus();
    }

    /// <inheritdoc />
    public void SetCanvasBinding(NativeCanvasBinding binding)
    {
        if (!_experimentalCanvas) throw new InvalidOperationException("Canvas mode is disabled.");
        var changedSource = !ReferenceEquals(_presentedCanvasSnapshot, binding.Snapshot);
        _pendingCanvasBinding = binding;
        _pendingCanvasUnavailable = null;
        _pendingCanvasFrame = binding.Frame;
        _canvas?.Bind(binding);
        if (changedSource)
        {
            _presentedCanvasSnapshot = binding.Snapshot;
            ClearAnalysisPreview();
        }
        if (_canvas is null) return;
        SetCanvasChrome(binding.Title, binding.Status, binding.IsModified);
    }

    /// <inheritdoc />
    public void SetCanvasFrame(CanvasFrame frame)
    {
        if (!_experimentalCanvas) throw new InvalidOperationException("Canvas mode is disabled.");
        _pendingCanvasFrame = frame;
        _canvas?.SetFrame(frame);
    }

    /// <inheritdoc />
    public void SetCanvasInputUnavailable(Mote.Engine.TextSnapshot snapshot,
        CanvasFrame frame, string reason)
    {
        if (!_experimentalCanvas) throw new InvalidOperationException("Canvas mode is disabled.");
        var changedSource = !ReferenceEquals(_presentedCanvasSnapshot, snapshot);
        _pendingCanvasBinding = null;
        _pendingCanvasUnavailable = (snapshot, frame, reason);
        _pendingCanvasFrame = frame;
        _canvas?.SetUnavailable(snapshot, frame, reason);
        if (changedSource)
        {
            _presentedCanvasSnapshot = snapshot;
            ClearAnalysisPreview();
        }
        SetStatus(reason);
    }

    /// <inheritdoc />
    public void SetCanvasSemantics(NativeCanvasSemantics semantics)
    {
        if (!_experimentalCanvas) throw new InvalidOperationException("Canvas mode is disabled.");
        _pendingCanvasSemantics = semantics;
        _canvas?.SetSemantics(semantics);
    }

    /// <inheritdoc />
    public CanvasCaretGeometry? GetCanvasCaretGeometry(CanvasFrame frame, int sourceOffset) =>
        _experimentalCanvas ? _canvas?.GetCaretGeometry(frame, sourceOffset) : null;

    /// <inheritdoc />
    public void SetCanvasAccessibility(AccessibleDocument document,
        IAccessibleViewport viewport)
    {
        if (_experimentalCanvas) MacTextInputIsland.TraceStage("A0-shell-ax-enter");
        if (!_experimentalCanvas || _canvas is null ||
            _canvas.CanvasView == 0 || _canvas.Editor == 0)
            throw new InvalidOperationException("The opt-in canvas views are not ready for AX.");
        if (_accessibility is not null)
            throw new InvalidOperationException("The canvas AX element is already attached.");
        var provider = new MacAccessibilityElementPrototype(document, viewport);
        MacTextInputIsland.TraceStage("A1-shell-ax-provider-ready");
        provider.Faulted += QueueAccessibilityFault;
        try
        {
            MacTextInputIsland.TraceStage("A2-shell-ax-before-attach");
            provider.Attach(_canvas.CanvasView, _canvas.Editor, _canvas.BodyRect);
            MacTextInputIsland.TraceStage("A3-shell-ax-attached");
            _accessibility = provider;
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    private void AccessibilityFaulted()
    {
        Interlocked.Exchange(ref _accessibilityFaultScheduled, 0);
        if (Environment.CurrentManagedThreadId != _uiThreadId)
        {
            Post(AccessibilityFaulted);
            return;
        }
        if (_accessibility is null) return;
        try { _accessibility.Dispose(); }
        catch { /* Faulted AX must not disable the native text input host. */ }
        _accessibility = null;
        try { CanvasAccessibilityFailed?.Invoke(); }
        catch
        {
            try { ShowError("Accessibility became unavailable; editing remains available."); }
            catch { /* No exception may unwind through an AppKit IMP. */ }
        }
    }

    private void QueueAccessibilityFault(Exception error)
    {
        if (Interlocked.Exchange(ref _accessibilityFaultScheduled, 1) != 0 || _delegate == 0)
            return;
        // Never release an AX element while one of its Objective-C selector
        // callbacks is still on the stack, even when the callback is on UI.
        try
        {
            if (Environment.CurrentManagedThreadId == _uiThreadId)
                ObjC.Send(_delegate, ObjC.Sel("performSelector:withObject:afterDelay:"),
                    ObjC.Sel("moteDetachAccessibility:"), 0, 0d);
            else
                ObjC.Send(_delegate,
                    ObjC.Sel("performSelectorOnMainThread:withObject:waitUntilDone:"),
                    ObjC.Sel("moteDetachAccessibility:"), 0, 0);
        }
        catch
        {
            Interlocked.Exchange(ref _accessibilityFaultScheduled, 0);
            try { Post(AccessibilityFaulted); }
            catch { Console.Error.WriteLine("Canvas accessibility unavailable; editing remains available."); }
        }
    }

    /// <inheritdoc />
    public void SetCanvasChrome(string title, string status, bool isModified)
    {
        _canvasTitle = title;
        _canvasIsModified = isModified;
        _statusText = status;
        if (_window == 0) return;
        ObjC.Send(_window, ObjC.Sel("setTitle:"), ObjC.String(title));
        SetStatus(status);
    }

    /// <inheritdoc />
    public void SetSelection(int displayAnchor, int displayActive)
    {
        if (_experimentalCanvas) return;
        _pendingSelection = (displayAnchor, displayActive);
        if (_editor == 0) return;
        if (displayAnchor < 0 || displayActive < 0 ||
            displayAnchor > _visibleText.Length || displayActive > _visibleText.Length)
            throw new ArgumentOutOfRangeException(nameof(displayAnchor), "Selection must belong to the visible page.");
        var start = Math.Min(displayAnchor, displayActive);
        var range = new ObjC.Range((nuint)start, (nuint)Math.Abs(displayActive - displayAnchor));
        _settingSelection = true;
        try
        {
            _pendingNativeSelection = null;
            ObjC.Send(_editor, ObjC.Sel("setSelectedRange:"), range);
            ObjC.Send(_editor, ObjC.Sel("scrollRangeToVisible:"), range);
            _selectionAnchor = displayAnchor;
        }
        finally { _settingSelection = false; }
    }

    /// <inheritdoc />
    public string? PromptFind()
    {
        var value = PromptText("Find in document", "Search text", _findQuery);
        if (string.IsNullOrEmpty(value)) return null;
        _findQuery = value;
        return value;
    }

    /// <inheritdoc />
    public int? PromptGoToLine()
    {
        var value = PromptText("Go to line", "One-based line number", "1");
        if (value is null) return null;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var line) && line > 0)
            return line;
        ShowError("Enter a positive line number.");
        return null;
    }

    /// <inheritdoc />
    public void SetClipboardText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var board = ObjC.Send(ObjC.Class("NSPasteboard"), ObjC.Sel("generalPasteboard"));
        ObjC.Send(board, ObjC.Sel("clearContents"));
        if (ObjC.Send(board, ObjC.Sel("setString:forType:"),
            ObjC.String(text), ObjC.String("public.utf8-plain-text")) == 0)
            throw new IOException("The macOS pasteboard rejected the selected text.");
    }

    /// <inheritdoc />
    public bool CommitPendingText()
    {
        try
        {
            var settled = _experimentalCanvas ? _canvas?.CommitPendingText() ?? true :
                CommitMarkedTextBeforeCommand();
            if (settled) ObserveCompositionState();
            return settled;
        }
        catch (Exception error)
        {
            ShowError(error.Message);
            return false;
        }
    }

    /// <summary>
    /// Announces the end of a native marked-text episode only after its final
    /// source reconcile. Polling also covers cancellation without textDidChange.
    /// </summary>
    private void ObserveCompositionState()
    {
        if (IsTextComposing)
        {
            _compositionObserved = true;
            ScheduleCompositionCheck();
            return;
        }
        if (!_compositionObserved) return;
        _compositionObserved = false;
        CompositionSettled?.Invoke();
    }

    private static string? PromptText(string title, string explanation, string initialValue)
    {
        var alert = ObjC.New("NSAlert");
        ObjC.Send(alert, ObjC.Sel("setMessageText:"), ObjC.String(title));
        ObjC.Send(alert, ObjC.Sel("setInformativeText:"), ObjC.String(explanation));
        var field = ObjC.Send(ObjC.Send(ObjC.Class("NSTextField"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(0, 0, 300, 24));
        ObjC.Send(field, ObjC.Sel("setStringValue:"), ObjC.String(initialValue));
        ObjC.Send(alert, ObjC.Sel("setAccessoryView:"), field);
        ObjC.Send(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.String("Go"));
        ObjC.Send(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.String("Cancel"));
        ObjC.Send(ObjC.Send(alert, ObjC.Sel("window")), ObjC.Sel("makeFirstResponder:"), field);
        if (ObjC.Send(alert, ObjC.Sel("runModal")) != 1000) return null;
        return ObjC.ManagedString(ObjC.Send(field, ObjC.Sel("stringValue")));
    }


    /// <inheritdoc />
    public string? PickOpenFile()
    {
        if (_probeOpenPath is { } probePath)
        {
            _probeOpenPath = null;
            return probePath;
        }
        var panel = ObjC.Send(ObjC.Class("NSOpenPanel"), ObjC.Sel("openPanel"));
        ObjC.Send(panel, ObjC.Sel("setCanChooseDirectories:"), 0);
        ObjC.Send(panel, ObjC.Sel("setAllowsMultipleSelection:"), 0);
        return ObjC.Send(panel, ObjC.Sel("runModal")) == 1 ? PanelPath(panel) : null;
    }

    /// <inheritdoc />
    public string? PickSaveFile(string? currentPath)
    {
        if (_probeSavePath is { } probePath)
        {
            _probeSavePath = null;
            return probePath;
        }
        var panel = ObjC.Send(ObjC.Class("NSSavePanel"), ObjC.Sel("savePanel"));
        if (!string.IsNullOrEmpty(currentPath))
        {
            ObjC.Send(panel, ObjC.Sel("setNameFieldStringValue:"),
                ObjC.String(Path.GetFileName(currentPath)));
            var folder = ObjC.Send(ObjC.Class("NSURL"), ObjC.Sel("fileURLWithPath:"),
                ObjC.String(Path.GetDirectoryName(currentPath)!));
            ObjC.Send(panel, ObjC.Sel("setDirectoryURL:"), folder);
        }
        return ObjC.Send(panel, ObjC.Sel("runModal")) == 1 ? PanelPath(panel) : null;
    }

    /// <inheritdoc />
    public bool ConfirmDiscard()
    {
        if (_probeConfirmDiscardOnce)
        {
            _probeConfirmDiscardOnce = false;
            _probeDidConfirmDiscard = true;
            return true;
        }
        var alert = ObjC.New("NSAlert");
        ObjC.Send(alert, ObjC.Sel("setMessageText:"), ObjC.String("Discard unsaved changes?"));
        ObjC.Send(alert, ObjC.Sel("setInformativeText:"),
            ObjC.String("Changes to this document have not been saved."));
        ObjC.Send(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.String("Discard Changes"));
        ObjC.Send(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.String("Cancel"));
        return ObjC.Send(alert, ObjC.Sel("runModal")) == 1000;
    }

    /// <inheritdoc />
    public bool ConfirmOverwrite(string path)
    {
        var alert = ObjC.New("NSAlert");
        ObjC.Send(alert, ObjC.Sel("setAlertStyle:"), 2);
        ObjC.Send(alert, ObjC.Sel("setMessageText:"),
            ObjC.String($"Replace {Path.GetFileName(path)}?"));
        ObjC.Send(alert, ObjC.Sel("setInformativeText:"),
            ObjC.String("A file already exists at this location. Its current contents will be replaced."));
        ObjC.Send(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.String("Replace"));
        ObjC.Send(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.String("Cancel"));
        return ObjC.Send(alert, ObjC.Sel("runModal")) == 1000;
    }

    /// <inheritdoc />
    public void ShowError(string message)
    {
        if (_experimentalCanvas) MacTextInputIsland.TraceStage("E0-show-error-enter");
        if (_probeCaptureCanvasErrors)
        {
            _probeCanvasError = message;
            return;
        }
        var alert = ObjC.New("NSAlert");
        ObjC.Send(alert, ObjC.Sel("setAlertStyle:"), 2);
        ObjC.Send(alert, ObjC.Sel("setMessageText:"), ObjC.String("mote could not complete the operation"));
        ObjC.Send(alert, ObjC.Sel("setInformativeText:"), ObjC.String(message));
        if (_experimentalCanvas) MacTextInputIsland.TraceStage("E1-before-error-modal");
        ObjC.Send(alert, ObjC.Sel("runModal"));
        if (_experimentalCanvas) MacTextInputIsland.TraceStage("E2-error-modal-returned");
    }

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _posted.Enqueue(action);
        if (_delegate != 0)
            ObjC.Send(_delegate, ObjC.Sel("performSelectorOnMainThread:withObject:waitUntilDone:"),
                ObjC.Sel("moteDrainPosted:"), 0, 0);
    }

    /// <inheritdoc />
    public void Close()
    {
        if (_window != 0) ObjC.Send(_window, ObjC.Sel("performClose:"), 0);
    }

    private void StopAndWake()
    {
        ObjC.Send(_application, ObjC.Sel("stop:"), 0);
        // stop: invoked from a timer/selector callback may leave run waiting
        // for its next NSEvent. A benign application-defined event wakes it.
        var wake = ObjC.SendOtherEvent(ObjC.Class("NSEvent"),
            ObjC.Sel("otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:"),
            15, new ObjC.Point(0, 0), 0, 0, 0, 0, 0, 0, 0);
        if (wake != 0) ObjC.Send(_application, ObjC.Sel("postEvent:atStart:"), wake, (byte)1);
    }

    private static string? PanelPath(nint panel)
    {
        var url = ObjC.Send(panel, ObjC.Sel("URL"));
        return url == 0 ? null : ObjC.ManagedString(ObjC.Send(url, ObjC.Sel("path")));
    }

    /// <summary>Gets the actual native editor text for the in-process AppKit workflow probe.</summary>
    internal string ProbeNativeText => _editor == 0 ? string.Empty :
        ObjC.ManagedString(ObjC.Send(_editor, ObjC.Sel("string")));

    /// <summary>Gets the most recent controller title projected into the native window.</summary>
    internal string ProbeTitle => _experimentalCanvas ? _canvasTitle :
        _pendingDocument?.Title ?? string.Empty;

    /// <summary>Gets whether the controller still considers the current native page dirty.</summary>
    internal bool ProbeIsModified => _pendingDocument?.IsModified ?? false;

    /// <summary>Gets the canonical page last sent by the controller, excluding marked preedit.</summary>
    internal string ProbeProjectedText => _pendingDocument?.Text ?? string.Empty;

    /// <summary>Current controller-applied policy for the isolated AppKit theme probe.</summary>
    internal string? ProbeThemeId => _theme?.Id;

    /// <summary>Last presentation admitted by the document-generation guard.</summary>
    internal NativeAnalysisView? ProbeAnalysis => _pendingAnalysis;

    /// <summary>Stamp of the currently projected source document.</summary>
    internal NativeDocumentStamp? ProbeDocumentStamp => _pendingDocument?.Stamp;

    /// <summary>Native editable view handle; only the diagnostic reads it.</summary>
    internal nint ProbeEditorView => _editor;

    /// <summary>Native read-only preview view handle; only the diagnostic reads it.</summary>
    internal nint ProbePreviewView => _preview;

    /// <summary>Overrides only this process's window appearance; never changes system preferences.</summary>
    internal void ProbeSetWindowAppearance(bool dark)
    {
        if (_window == 0) throw new InvalidOperationException("Theme probe window is unavailable.");
        var name = dark ? "NSAppearanceNameDarkAqua" : "NSAppearanceNameAqua";
        var appearance = ObjC.Send(ObjC.Class("NSAppearance"),
            ObjC.Sel("appearanceNamed:"), ObjC.String(name));
        if (appearance == 0) throw new InvalidOperationException("Theme probe appearance is unavailable.");
        ObjC.Send(_window, ObjC.Sel("setAppearance:"), appearance);
    }

    /// <summary>Whether AppKit currently owns uncommitted marked text.</summary>
    internal bool ProbeHasMarkedText => _editor != 0 &&
        ObjC.Send(_editor, ObjC.Sel("hasMarkedText")) != 0;

    /// <summary>Inserts through NSTextView's native text-input method, not the engine API.</summary>
    internal void ProbeInsertAtEnd(string value)
    {
        var caret = new ObjC.Range((nuint)ProbeNativeText.Length, 0);
        ObjC.Send(_editor, ObjC.Sel("setSelectedRange:"), caret);
        ObjC.Send(_editor, ObjC.Sel("insertText:replacementRange:"),
            ObjC.String(value), new ObjC.Range(nuint.MaxValue, 0));
    }

    /// <summary>Exercises an ordinary native insertion at a long-line source start.</summary>
    internal void ProbeInsertAtStart(string value)
    {
        ObjC.Send(_editor, ObjC.Sel("setSelectedRange:"), new ObjC.Range(0, 0));
        ObjC.Send(_editor, ObjC.Sel("insertText:replacementRange:"),
            ObjC.String(value), new ObjC.Range(nuint.MaxValue, 0));
    }

    /// <summary>Stages a marked-text candidate through the real NSTextInputClient protocol.</summary>
    internal void ProbeSetMarkedAtEnd(string value)
    {
        var caret = new ObjC.Range((nuint)ProbeNativeText.Length, 0);
        ObjC.Send(_editor, ObjC.Sel("setSelectedRange:"), caret);
        ObjC.Send(_editor, ObjC.Sel("setMarkedText:selectedRange:replacementRange:"),
            ObjC.String(value), new ObjC.Range((nuint)value.Length, 0),
            new ObjC.Range(nuint.MaxValue, 0));
    }

    /// <summary>Requests a native menu action; only used by the dedicated published-binary workflow probe.</summary>
    internal void ProbeInvokeMenu(string selector) => ObjC.Send(_delegate, ObjC.Sel(selector), 0);

    /// <summary>Supplies a deterministic native picker result for one probe action.</summary>
    internal void ProbePickOpen(string path) => _probeOpenPath = path;

    /// <summary>Supplies a deterministic native picker result for one probe action.</summary>
    internal void ProbePickSave(string path) => _probeSavePath = path;

    /// <summary>Approves one probe-only discard after a marked-text commit.</summary>
    internal void ProbeApproveDiscardOnce() => _probeConfirmDiscardOnce = true;

    /// <summary>Whether the probe observed the dirty-document discard path.</summary>
    internal bool ProbeDidConfirmDiscard => _probeDidConfirmDiscard;

    /// <summary>Current canvas input binding version for published-binary probes.</summary>
    internal long ProbeCanvasVersion => _pendingCanvasBinding?.BaseVersion ?? -1;

    /// <summary>Current bounded host source start for diagnostic probe telemetry.</summary>
    internal int ProbeCanvasInputStart => _pendingCanvasBinding?.InputSourceStart ?? -1;
    /// <summary>Latest immutable source-backed frame delivered to the native canvas.</summary>
    internal CanvasFrame? ProbeCanvasFrame => _pendingCanvasFrame;
    /// <summary>Live source-backed canvas NSView for bounded raster probes.</summary>
    internal nint ProbeCanvasView => _canvas?.CanvasView ?? 0;
    /// <summary>Actual source paint rectangle, excluding the reserved input ribbon.</summary>
    internal ObjC.Rect ProbeCanvasBodyRect => _canvas?.BodyRect ?? new ObjC.Rect(0, 0, 0, 0);
    /// <summary>Synchronizes a probe-only native frame resize with the source viewport.</summary>
    internal void ProbeCanvasPublishBodyHeight() => _canvas?.PublishBodyHeight();
    /// <summary>Same horizontal source transition used by native scrollWheel callbacks.</summary>
    internal void ProbeCanvasPanHorizontal(double pixels) => _canvas?.ProbePanHorizontal(pixels);
    /// <summary>Same CoreText inverse source hit-test used by canvas pointer callbacks.</summary>
    internal int ProbeCanvasHitSource(double x, double topY) =>
        _canvas?.ProbeHitSource(x, topY) ?? -1;
    /// <summary>Native host local glyph and clip geometry, without document text.</summary>
    internal (bool Aligned, bool Hidden, double NativeX, double ClipX)?
        ProbeCanvasHostGeometry(int sourceOffset) => _canvas?.ProbeHostGeometry(sourceOffset);
    /// <summary>Probe-only tagged direct source jump, not a synthetic pixel width.</summary>
    internal void ProbeCanvasHorizontalAnchor(int sourceOffset)
    {
        if (_pendingCanvasBinding is not { } binding) return;
        RequestHorizontalAnchor(new CanvasHorizontalAnchorRequest(
            binding.DocumentGeneration, binding.BaseVersion, sourceOffset,
            HorizontalCaretAffinity.Leading, 0));
    }

    /// <summary>Current input binding nonce for diagnostic probe telemetry.</summary>
    internal long ProbeCanvasNonce => _pendingCanvasBinding?.BindingNonce ?? -1;

    /// <summary>Native selectedRange and event-origin counters for hosted debugging.</summary>
    internal string ProbeCanvasNativeSelectionTrace =>
        _canvas?.ProbeSelectionTrace ?? "canvas-unavailable";

    /// <summary>Top-level AppKit window for in-process accessibility tree traversal.</summary>
    internal nint ProbeWindow => _window;
    /// <summary>AppKit activation state, independent of external AX focus projection.</summary>
    internal (bool AppActive, nint ActivationPolicy, bool WindowKey,
        bool WindowVisible, bool EditorFirstResponder) ProbeAppKitFocus =>
        (_application != 0 && ObjC.Send(_application, ObjC.Sel("isActive")) != 0,
            _application == 0 ? (nint)(-1) : ObjC.Send(_application, ObjC.Sel("activationPolicy")),
            _window != 0 && ObjC.Send(_window, ObjC.Sel("isKeyWindow")) != 0,
            _window != 0 && ObjC.Send(_window, ObjC.Sel("isVisible")) != 0,
            _window != 0 && _editor != 0 &&
                ObjC.Send(_window, ObjC.Sel("firstResponder")) == _editor);

    /// <summary>Read-only AX focus routing diagnostics; reports roles, never document text.</summary>
    internal string ProbeCanvasAxFocusTrace
    {
        get
        {
            var proxy = _accessibility?.Element ?? 0;
            if (proxy == 0) return "proxy=unavailable";
            static nint Focused(nint receiver, string selector) =>
                receiver != 0 && ObjC.Send(receiver, ObjC.Sel("respondsToSelector:"),
                    ObjC.Sel(selector)) != 0
                    ? ObjC.Send(receiver, ObjC.Sel(selector)) : 0;
            static string Role(nint value) => value != 0 &&
                ObjC.Send(value, ObjC.Sel("respondsToSelector:"),
                    ObjC.Sel("accessibilityRole")) != 0
                ? ObjC.ManagedString(ObjC.Send(value, ObjC.Sel("accessibilityRole")))
                : "none";
            var canvas = Focused(_canvas?.CanvasView ?? 0, "accessibilityFocusedUIElement");
            var window = Focused(_window, "accessibilityFocusedUIElement");
            var app = Focused(_application, "accessibilityApplicationFocusedUIElement");
            return $"proxy-focused={ObjC.Send(proxy, ObjC.Sel("isAccessibilityFocused")) != 0};" +
                $"canvas={Role(canvas)}/{canvas == proxy};" +
                $"window={Role(window)}/{window == proxy};" +
                $"app={Role(app)}/{app == proxy}";
        }
    }

    /// <summary>Controller-visible canvas status for AX fault-recovery probes.</summary>
    internal string ProbeCanvasStatus => _statusText;

    /// <summary>Whether the optional AX provider is currently attached.</summary>
    internal bool ProbeCanvasAccessibilityAttached => _accessibility is not null;

    /// <summary>Whether the bounded text host remains editable after AX-only failure.</summary>
    internal bool ProbeCanvasInputEditable => _editor != 0 &&
        ObjC.Send(_editor, ObjC.Sel("isEditable")) != 0;

    /// <summary>Whether AppKit still focuses the real NSTextView input client.</summary>
    internal bool ProbeCanvasInputFocused => _window != 0 && _editor != 0 &&
        ObjC.Send(_window, ObjC.Sel("firstResponder")) == _editor;

    /// <summary>The bounded input host is hidden only from the opt-in AX tree.</summary>
    internal bool ProbeCanvasInputAccessible => _editor != 0 &&
        ObjC.Send(_editor, ObjC.Sel("isAccessibilityElement")) != 0;

    /// <summary>Injects a deferred AX-only fault through the production detach path.</summary>
    internal void ProbeCanvasAccessibilityFault() =>
        QueueAccessibilityFault(new InvalidOperationException("probe-injected AX fault"));

    /// <summary>Whether repeated native selection echoes visibly disabled editing.</summary>
    internal bool ProbeCanvasInputDisabled => _canvas?.ProbeInputDisabled ?? false;

    /// <summary>Injects an unarmed native selection echo only for fail-closed testing.</summary>
    internal void ProbeCanvasSelectionEcho(int start, int length) =>
        _canvas?.ProbeProgrammaticSelectionEcho(start, length);

    /// <summary>Controller's last projected dirty state in opt-in canvas mode.</summary>
    internal bool ProbeCanvasIsModified => _canvasIsModified;

    /// <summary>Immutable engine snapshot currently bound to the canvas probe.</summary>
    internal Mote.Engine.TextSnapshot? ProbeCanvasSnapshot => _pendingCanvasBinding?.Snapshot;

    /// <summary>Projects one global selection as if resolved by a canvas pointer.</summary>
    internal void ProbeCanvasSelectGlobal(int anchor, int active) =>
        CanvasSelectionRequested?.Invoke(anchor, active);

    /// <summary>Exercises NSTextView's real command-dispatch delegate for deletion.</summary>
    internal void ProbeCanvasDelete(bool forward) => ObjC.Send(_editor,
        ObjC.Sel("doCommandBySelector:"),
        ObjC.Sel(forward ? "deleteForward:" : "deleteBackward:"));

    /// <summary>Controller-projected global selection in the experimental canvas.</summary>
    internal (int Anchor, int Active) ProbeCanvasSelection =>
        (_pendingCanvasFrame?.SelectionAnchor ?? -1,
            _pendingCanvasFrame?.SelectionActive ?? -1);

    /// <summary>Selects through the real bounded NSTextView for regression probes.</summary>
    internal void ProbeCanvasSelect(int start, int length)
    {
        if (!_experimentalCanvas || _canvas is null || start < 0 || length < 0)
            throw new InvalidOperationException("Canvas probe selection is unavailable.");
        // The in-process probe has no physical NSEvent; explicitly arm the same
        // one-shot user-origin path that the NSTextView subclass arms on key/mouse.
        _canvas.ArmUserSelectionGesture();
        ObjC.Send(_editor, ObjC.Sel("setSelectedRange:"),
            new ObjC.Range((nuint)start, (nuint)length));
    }

    /// <summary>Latest bounded-paste rejection captured without opening a modal test dialog.</summary>
    internal string? ProbeCanvasError => _probeCanvasError;

    /// <summary>Routes probe-only paste errors to assertions instead of a blocking alert.</summary>
    internal void ProbeCaptureCanvasErrors() => _probeCaptureCanvasErrors = true;

    /// <summary>
    /// Writes a real OS pasteboard payload and invokes the custom NSTextView paste
    /// selector; the production paste preflight is not bypassed.
    /// </summary>
    internal void ProbeCanvasPaste(string plain, string? rtf)
    {
        if (!_experimentalCanvas || _canvas is null)
            throw new InvalidOperationException("The canvas input host is unavailable.");
        _probeCanvasError = null;
        var board = ObjC.Send(ObjC.Class("NSPasteboard"), ObjC.Sel("generalPasteboard"));
        ObjC.Send(board, ObjC.Sel("clearContents"));
        if (ObjC.Send(board, ObjC.Sel("setString:forType:"),
            ObjC.String(plain), ObjC.String("public.utf8-plain-text")) == 0)
            throw new IOException("The native clipboard refused plain text.");
        if (rtf is not null && ObjC.Send(board, ObjC.Sel("setString:forType:"),
            ObjC.String(rtf), ObjC.String("public.rtf")) == 0)
            throw new IOException("The native clipboard refused RTF test data.");
        ObjC.Send(_editor, ObjC.Sel("pasteAsPlainText:"), 0);
    }

    private void CreateWindow()
    {
        _window = ObjC.Send(ObjC.Send(ObjC.Class("NSWindow"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithContentRect:styleMask:backing:defer:"),
            new ObjC.Rect(120, 100, 1120, 760), WindowStyle, 2, 0);
        if (_experimentalCanvas)
            ObjC.Send(_window, ObjC.Sel("setContentMinSize:"), new ObjC.Size(420, 120));
        ObjC.Send(_window, ObjC.Sel("setReleasedWhenClosed:"), 0);
        ObjC.Send(_window, ObjC.Sel("setDelegate:"), _delegate);
        ObjC.Send(_window, ObjC.Sel("setTitle:"), ObjC.String("mote"));
        var root = ObjC.Send(_window, ObjC.Sel("contentView"));
        var split = ObjC.Send(ObjC.Send(ObjC.Class("NSSplitView"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(0, 30, 1120, 730));
        ObjC.Send(split, ObjC.Sel("setVertical:"), 1);
        ObjC.Send(split, ObjC.Sel("setAutoresizingMask:"), (nint)ResizeWidthAndHeight);
        ObjC.Send(root, ObjC.Sel("addSubview:"), split);

        nint editorScroll;
        if (_experimentalCanvas)
        {
            _canvas = new MacTextInputIsland(
                edit => CanvasEditCommitted?.Invoke(edit),
                delta => CanvasScrollRequested?.Invoke(delta),
                RequestHorizontalAnchor,
                height => CanvasViewportResized?.Invoke(height),
                (anchor, active) => CanvasSelectionRequested?.Invoke(anchor, active),
                message =>
                {
                    if (_probeCaptureCanvasErrors) _probeCanvasError = message;
                    else Post(() => ShowError(message));
                });
            _canvas.EffectiveAppearanceChanged += ObserveAppearanceChanged;
            _canvas.ViewGeometryChanged += () =>
            {
                try { _accessibility?.UpdateBodyRect(_canvas.BodyRect); }
                catch (Exception error) { QueueAccessibilityFault(error); }
            };
            editorScroll = _canvas.CreateView(new ObjC.Rect(0, 0, 730, 730));
            _editor = _canvas.Editor;
        }
        else editorScroll = CreateScrollView(new ObjC.Rect(0, 0, 730, 730), true, out _editor);
        var previewScroll = CreateScrollView(new ObjC.Rect(730, 0, 390, 730), false, out _preview);
        ObjC.Send(split, ObjC.Sel("addSubview:"), editorScroll);
        ObjC.Send(split, ObjC.Sel("addSubview:"), previewScroll);
        ObjC.Send(split, ObjC.Sel("adjustSubviews"));

        _status = ObjC.Send(ObjC.Send(ObjC.Class("NSTextField"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(12, 3, 1096, 23));
        ObjC.Send(_status, ObjC.Sel("setEditable:"), 0);
        ObjC.Send(_status, ObjC.Sel("setSelectable:"), 0);
        ObjC.Send(_status, ObjC.Sel("setBezeled:"), 0);
        ObjC.Send(_status, ObjC.Sel("setDrawsBackground:"), 0);
        ObjC.Send(_status, ObjC.Sel("setAutoresizingMask:"), (nint)2);
        ObjC.Send(root, ObjC.Sel("addSubview:"), _status);
        ObjC.Send(_editor, ObjC.Sel("setDelegate:"), _delegate);
        ObjC.Send(_window, ObjC.Sel("makeFirstResponder:"), _editor);
    }

    private static nint CreateScrollView(ObjC.Rect rect, bool editable, out nint textView)
    {
        var scroll = ObjC.Send(ObjC.Send(ObjC.Class("NSScrollView"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), rect);
        ObjC.Send(scroll, ObjC.Sel("setHasVerticalScroller:"), 1);
        ObjC.Send(scroll, ObjC.Sel("setAutohidesScrollers:"), 1);
        ObjC.Send(scroll, ObjC.Sel("setBorderType:"), 0);
        var textClass = editable ? RegisterEditorAppearanceClass() : "NSTextView";
        textView = ObjC.Send(ObjC.Send(ObjC.Class(textClass), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(0, 0, rect.Size.Width, rect.Size.Height));
        // NSText's range-specific color and font APIs require rich text. The
        // controller still receives and saves only NSString's plain characters.
        ObjC.Send(textView, ObjC.Sel("setRichText:"), 1);
        ObjC.Send(textView, ObjC.Sel("setImportsGraphics:"), 0);
        ObjC.Send(textView, ObjC.Sel("setEditable:"), editable ? 1 : 0);
        ObjC.Send(textView, ObjC.Sel("setSelectable:"), 1);
        ObjC.Send(textView, ObjC.Sel("setMinSize:"), new ObjC.Size(0, rect.Size.Height));
        ObjC.Send(textView, ObjC.Sel("setMaxSize:"), new ObjC.Size(1_000_000_000, 1_000_000_000));
        ObjC.Send(textView, ObjC.Sel("setVerticallyResizable:"), 1);
        ObjC.Send(textView, ObjC.Sel("setHorizontallyResizable:"), 0);
        ObjC.Send(textView, ObjC.Sel("setAutoresizingMask:"), (nint)2);
        var container = ObjC.Send(textView, ObjC.Sel("textContainer"));
        ObjC.Send(container, ObjC.Sel("setContainerSize:"),
            new ObjC.Size(rect.Size.Width, 1_000_000_000));
        ObjC.Send(container, ObjC.Sel("setWidthTracksTextView:"), 1);
        ObjC.Send(textView, ObjC.Sel("setTextContainerInset:"), new ObjC.Size(12, 10));
        ObjC.Send(textView, ObjC.Sel("setAllowsUndo:"), 0);
        ObjC.Send(textView, ObjC.Sel("setAutomaticQuoteSubstitutionEnabled:"), 0);
        ObjC.Send(textView, ObjC.Sel("setAutomaticDashSubstitutionEnabled:"), 0);
        ObjC.Send(textView, ObjC.Sel("setAutomaticTextReplacementEnabled:"), 0);
        ObjC.Send(textView, ObjC.Sel("setContinuousSpellCheckingEnabled:"), 0);
        ObjC.Send(scroll, ObjC.Sel("setDocumentView:"), textView);
        return scroll;
    }

    private void CreateMenu()
    {
        var main = ObjC.New("NSMenu");
        AddMenu(main, "mote", [
            ("About mote", "orderFrontStandardAboutPanel:", ""),
            ("Quit mote", "moteQuit:", "q")], true);
        AddMenu(main, "File", [
            ("New", "moteNew:", "n"), ("Open…", "moteOpen:", "o"),
            ("Save", "moteSave:", "s"), ("Save As…", "moteSaveAs:", "S"),
            ("Close", "performClose:", "w")], true);
        AddMenu(main, "Edit", [
            ("Undo", "moteUndo:", "z"), ("Redo", "moteRedo:", "Z"),
            ("Cut", "moteCut:", "x"), ("Copy", "moteCopy:", "c"),
            ("Paste", "pasteAsPlainText:", "v"), ("Select All", "moteSelectAll:", "a"),
            ("Format Document", "moteFormat:", "")], true);
        AddMenu(main, "Find", [
            ("Find…", "moteFind:", "f"),
            ("Find Next", "moteFindNext:", "g"),
            ("Go to Line…", "moteGoToLine:", "l")], true);
        AddMenu(main, "View", [
            ("Previous Page", "motePreviousPage:", ""),
            ("Next Page", "moteNextPage:", "")], true);
        ObjC.Send(_application, ObjC.Sel("setMainMenu:"), main);
    }

    private void AddMenu(nint main, string title, (string Label, string Action, string Key)[] items,
        bool delegateActions)
    {
        var holder = ObjC.Send(main, ObjC.Sel("addItemWithTitle:action:keyEquivalent:"),
            ObjC.String(title), 0, ObjC.String(""));
        var submenu = ObjC.New("NSMenu");
        ObjC.Send(submenu, ObjC.Sel("setTitle:"), ObjC.String(title));
        foreach (var item in items)
        {
            var menuItem = ObjC.Send(submenu, ObjC.Sel("addItemWithTitle:action:keyEquivalent:"),
                ObjC.String(item.Label), ObjC.Sel(item.Action), ObjC.String(item.Key));
            if (delegateActions && item.Action.StartsWith("mote", StringComparison.Ordinal))
                ObjC.Send(menuItem, ObjC.Sel("setTarget:"), _delegate);
            else if (item.Action is "performClose:")
                ObjC.Send(menuItem, ObjC.Sel("setTarget:"), _window);
            else if (item.Action is "terminate:" or "orderFrontStandardAboutPanel:")
                ObjC.Send(menuItem, ObjC.Sel("setTarget:"), _application);
        }
        ObjC.Send(main, ObjC.Sel("setSubmenu:forItem:"), submenu, holder);
    }

    private void ApplyTheme(IThemePolicy theme, bool updateFonts = true)
    {
        var palette = theme.Palette;
        var editorForeground = Color(palette.EditorForeground);
        ObjC.Send(_editor, ObjC.Sel("setBackgroundColor:"), Color(palette.EditorBackground));
        ObjC.Send(_editor, ObjC.Sel("setTextColor:"), editorForeground);
        ObjC.Send(_editor, ObjC.Sel("setInsertionPointColor:"), Color(palette.Cursor));
        ObjC.Send(_preview, ObjC.Sel("setBackgroundColor:"), Color(palette.PreviewBackground));
        ObjC.Send(_preview, ObjC.Sel("setTextColor:"), Color(palette.PreviewForeground));
        ObjC.Send(_status, ObjC.Sel("setTextColor:"), Color(palette.MutedForeground));
        if (updateFonts)
        {
            var editorFont = ObjC.Send(ObjC.Class("NSFont"),
                ObjC.Sel("monospacedSystemFontOfSize:weight:"),
                theme.Typography.EditorFontSize, 0d);
            ObjC.Send(_editor, ObjC.Sel("setFont:"), editorFont);
            ObjC.Send(_preview, ObjC.Sel("setFont:"), editorFont);
        }
        SetStatus(_statusText);
    }

    /// <summary>
    /// Removes previous-document or previous-page preview content immediately;
    /// analysis for the new presentation will repopulate it asynchronously.
    /// </summary>
    private void ClearAnalysisPreview()
    {
        _pendingAnalysis = null;
        _deferredAnalysisText = null;
        _previewText = string.Empty;
        if (_preview != 0)
            ObjC.Send(_preview, ObjC.Sel("setString:"), ObjC.String(string.Empty));
    }

    private void SetPreview(NativeAnalysisView view, bool updateFonts = true)
    {
        var textChanged = !string.Equals(_previewText, view.PreviewText,
            StringComparison.Ordinal);
        if (textChanged)
        {
            ObjC.Send(_preview, ObjC.Sel("setString:"), ObjC.String(view.PreviewText));
            _previewText = view.PreviewText;
        }
        updateFonts |= textChanged;
        var length = view.PreviewText.Length;
        if (length == 0) return;
        var palette = _theme?.Palette;
        var foreground = Color(palette?.PreviewForeground ?? new ThemeColor(225, 227, 231));
        var whole = new ObjC.Range(0, (nuint)length);
        ObjC.Send(_preview, ObjC.Sel("setTextColor:range:"), foreground, whole);
        if (updateFonts)
        {
            var baseFont = ObjC.Send(ObjC.Class("NSFont"), ObjC.Sel("systemFontOfSize:"),
                _theme?.Typography.UiFontSize ?? 12d);
            ObjC.Send(_preview, ObjC.Sel("setFont:range:"), baseFont, whole);
        }

        var colors = new Dictionary<string, nint>(StringComparer.Ordinal);
        nint headingFont = 0;
        nint boldFont = 0;
        nint codeFont = 0;
        foreach (var span in view.PreviewSpans ?? [])
        {
            if (span.Start < 0 || span.Length <= 0 || span.Start > length ||
                span.Length > length - span.Start) continue;
            if (!colors.TryGetValue(span.Kind, out var color))
            {
                color = Color(PreviewColor(span.Kind));
                colors.Add(span.Kind, color);
            }
            var range = new ObjC.Range((nuint)span.Start, (nuint)span.Length);
            ObjC.Send(_preview, ObjC.Sel("setTextColor:range:"), color, range);
            if (!updateFonts) continue;
            nint font = 0;
            if (span.Kind == "heading")
                font = headingFont != 0 ? headingFont :
                    (headingFont = ObjC.Send(ObjC.Class("NSFont"), ObjC.Sel("boldSystemFontOfSize:"),
                        (_theme?.Typography.UiFontSize ?? 12d) * 1.4));
            else if (span.Kind == "code")
                font = codeFont != 0 ? codeFont :
                    (codeFont = ObjC.Send(ObjC.Class("NSFont"), ObjC.Sel("monospacedSystemFontOfSize:weight:"),
                        _theme?.Typography.EditorFontSize ?? 13d, 0d));
            else if (span.Emphasis)
                font = boldFont != 0 ? boldFont :
                    (boldFont = ObjC.Send(ObjC.Class("NSFont"), ObjC.Sel("boldSystemFontOfSize:"),
                        _theme?.Typography.UiFontSize ?? 12d));
            if (font != 0) ObjC.Send(_preview, ObjC.Sel("setFont:range:"), font, range);
        }
    }

    private ThemeColor PreviewColor(string kind)
    {
        var palette = _theme?.Palette;
        return kind switch
        {
            "heading" or "table-header" or "list-marker" => palette?.Accent ?? new ThemeColor(141, 185, 237),
            "quote" or "comment" => palette?.MutedForeground ?? new ThemeColor(173, 179, 188),
            "error" => palette?.Error ?? new ThemeColor(242, 154, 154),
            "code" => palette?.Info ?? new ThemeColor(141, 185, 237),
            _ => _theme?.SemanticColor(kind) ?? new ThemeColor(225, 227, 231)
        };
    }

    private static nint Color(ThemeColor color) => ObjC.Send(ObjC.Class("NSColor"),
        ObjC.Sel("colorWithSRGBRed:green:blue:alpha:"),
        color.Red / 255d, color.Green / 255d, color.Blue / 255d, 1d);

    /// <summary>Publishes one UI-thread effective-appearance transition after startup.</summary>
    private void ObserveAppearanceChanged()
    {
        if (!_appearanceNotificationsReady || _editor == 0) return;
        var dark = PrefersDark;
        if (_lastAppearanceDark == dark) return;
        _lastAppearanceDark = dark;
        if (IsTextComposing)
        {
            _compositionObserved = true;
            ScheduleCompositionCheck();
        }
        AppearanceChanged?.Invoke();
    }

    private void SetStatus(string value)
    {
        _statusText = value;
        RenderStatus();
    }

    private void RenderStatus()
    {
        if (_status == 0) return;
        var text = _statusNotice is null ? _statusText :
            $"{_statusText}  ·  {_statusNotice}";
        ObjC.Send(_status, ObjC.Sel("setStringValue:"), ObjC.String(text));
    }

    private void ReplayDeferredAnalysis()
    {
        if (_deferredAnalysisText is null || _editor == 0 ||
            ObjC.Send(_editor, ObjC.Sel("hasMarkedText")) != 0) return;
        if (string.Equals(_deferredAnalysisText, _visibleText, StringComparison.Ordinal) &&
            _pendingAnalysis is { } analysis)
            SetAnalysis(analysis);
        else
            _deferredAnalysisText = null;
    }

    private void ReplayDeferredDocument()
    {
        if (!_deferredDocument || _editor == 0 ||
            ObjC.Send(_editor, ObjC.Sel("hasMarkedText")) != 0) return;
        _deferredDocument = false;
        if (_pendingDocument is { } view) SetDocument(view);
    }

    private void ScheduleCompositionCheck()
    {
        if (_compositionCheckScheduled) return;
        _compositionCheckScheduled = true;
        ObjC.Send(_delegate, ObjC.Sel("performSelector:withObject:afterDelay:"),
            ObjC.Sel("moteCommitComposition:"), 0, 0.1d);
    }

    private void CommitComposition()
    {
        if (!_compositionDirty || ObjC.Send(_editor, ObjC.Sel("hasMarkedText")) != 0) return;
        _compositionDirty = false;
        _pendingNativeSelection = null;
        var value = ObjC.ManagedString(ObjC.Send(_editor, ObjC.Sel("string")));
        _visibleText = value;
        TextChanged?.Invoke(value);
        _compositionCommitRejected = !string.Equals(_visibleText, value, StringComparison.Ordinal);
        ReplayDeferredDocument();
        ReplayDeferredAnalysis();
    }

    /// <summary>Commits visible marked text before any command that can save or discard the document.</summary>
    private bool CommitMarkedTextBeforeCommand()
    {
        if (_editor == 0) return true;
        if (ObjC.Send(_editor, ObjC.Sel("hasMarkedText")) == 0 && !_compositionDirty)
            return true;
        if (ObjC.Send(_editor, ObjC.Sel("hasMarkedText")) != 0)
        {
            _compositionCommitRejected = false;
            _compositionDirty = true;
            ObjC.Send(_editor, ObjC.Sel("unmarkText"));
            if (ObjC.Send(_editor, ObjC.Sel("hasMarkedText")) != 0) return false;
        }
        CommitComposition();
        return !_compositionDirty && !_compositionCommitRejected;
    }

    private void NotifyAfterComposition(Action? callback)
    {
        try
        {
            if (!CommitPendingText())
            {
                ShowError("Finish or cancel the current text composition before this command.");
                return;
            }
            callback?.Invoke();
        }
        catch (Exception error) { ShowError(error.Message); }
    }

    private static string RegisterEditorAppearanceClass()
    {
        var cls = ObjC.AllocateClassPair(ObjC.Class("NSTextView"), EditorAppearanceClass, 0);
        if (cls == 0) return EditorAppearanceClass;
        Add(cls, "viewDidChangeEffectiveAppearance",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&EditorAppearanceChanged,
            "v@:");
        ObjC.RegisterClassPair(cls);
        return EditorAppearanceClass;
    }

    private static string RegisterDelegateClass()
    {
        const string className = "MoteNativeEditorDelegate";
        var existing = ObjC.Class("NSObject");
        var cls = ObjC.AllocateClassPair(existing, className, 0);
        if (cls == 0) return className;
        Add(cls, "textDidChange:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&TextDidChange, "v@:@");
        Add(cls, "textViewDidChangeSelection:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&TextViewDidChangeSelection, "v@:@");
        Add(cls, "moteDeliverSelection:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&DeliverSelection, "v@:@");
        Add(cls, "moteCommitComposition:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&CommitCompositionCheck, "v@:@");
        Add(cls, "textView:doCommandBySelector:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, byte>)&TextViewDoCommand, "c@:@:");
        Add(cls, "textView:shouldChangeTextInRange:replacementString:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, ObjC.Range, nint, byte>)
                &TextViewShouldChange, "c@:@{_NSRange=QQ}@");
        Add(cls, "windowShouldClose:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, byte>)&WindowShouldClose, "c@:@");
        Add(cls, "windowWillClose:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&WindowWillClose, "v@:@");
        Add(cls, "applicationShouldTerminate:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint>)&ApplicationShouldTerminate, "q@:@");
        Add(cls, "moteDrainPosted:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&DrainPosted, "v@:@");
        Add(cls, "moteDetachAccessibility:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&DetachAccessibility,
            "v@:@");
        Add(cls, "moteNew:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&New, "v@:@");
        Add(cls, "moteOpen:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Open, "v@:@");
        Add(cls, "moteSave:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Save, "v@:@");
        Add(cls, "moteSaveAs:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&SaveAs, "v@:@");
        Add(cls, "moteUndo:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Undo, "v@:@");
        Add(cls, "moteRedo:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Redo, "v@:@");
        Add(cls, "moteFormat:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Format, "v@:@");
        Add(cls, "motePreviousPage:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&PreviousPage, "v@:@");
        Add(cls, "moteNextPage:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&NextPage, "v@:@");
        Add(cls, "moteFind:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Find, "v@:@");
        Add(cls, "moteFindNext:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&FindNext, "v@:@");
        Add(cls, "moteGoToLine:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&GoToLine, "v@:@");
        Add(cls, "moteSelectAll:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&SelectAll, "v@:@");
        Add(cls, "moteCopy:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Copy, "v@:@");
        Add(cls, "moteCut:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Cut, "v@:@");
        Add(cls, "moteQuit:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Quit, "v@:@");
        ObjC.RegisterClassPair(cls);
        return className;
    }

    private static void Add(nint cls, string selector, nint implementation, string encoding)
    {
        if (!ObjC.AddMethod(cls, ObjC.Sel(selector), implementation, encoding))
            throw new InvalidOperationException($"Could not register {selector}.");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void EditorAppearanceChanged(nint self, nint selector)
    {
        try
        {
            var superclass = new MacOnScreenCanvasNative.Super(self, ObjC.Class("NSTextView"));
            SendSuperNoArgument(ref superclass, selector);
            s_current?.ObserveAppearanceChanged();
        }
        catch { /* An AppKit IMP must never unwind a managed exception. */ }
    }

    private void Notify(Action? callback)
    {
        try { callback?.Invoke(); }
        catch (Exception error) { ShowError(error.Message); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void TextDidChange(nint self, nint selector, nint notification)
    {
        var shell = s_current;
        if (shell is null || shell._settingText) return;
        try
        {
            if (shell._experimentalCanvas)
            {
                MacTextInputIsland.TraceStage("D0-text-did-change-enter");
                shell._canvas?.OnTextChanged();
                MacTextInputIsland.TraceStage("D1-canvas-text-change-returned");
                shell.ObserveCompositionState();
                MacTextInputIsland.TraceStage("D2-text-did-change-returned");
                return;
            }
            // AppKit may announce caret collapse before textDidChange. Do not let
            // that transient page-local selection erase a global selection.
            shell._pendingNativeSelection = null;
            var value = ObjC.ManagedString(ObjC.Send(shell._editor, ObjC.Sel("string")));
            shell._visibleText = value;
            if (ObjC.Send(shell._editor, ObjC.Sel("hasMarkedText")) != 0)
            {
                shell._compositionDirty = true;
                shell.ObserveCompositionState();
                return;
            }
            var finishingComposition = shell._compositionDirty;
            shell._compositionDirty = false;
            shell.TextChanged?.Invoke(value);
            if (finishingComposition)
                shell._compositionCommitRejected = !string.Equals(shell._visibleText, value,
                    StringComparison.Ordinal);
            shell.ReplayDeferredDocument();
            shell.ReplayDeferredAnalysis();
            shell.ObserveCompositionState();
        }
        catch (Exception error)
        {
            if (shell._experimentalCanvas) shell._canvas?.DisableAfterFailure(error.Message);
            else shell.ShowError(error.Message);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void TextViewDidChangeSelection(nint self, nint selector, nint notification)
    {
        var shell = s_current;
        if (shell is null || shell._settingText || shell._settingSelection) return;
        try
        {
            if (shell._experimentalCanvas)
            {
                if (!shell._selectionDeliveryScheduled)
                {
                    shell._selectionDeliveryScheduled = true;
                    ObjC.Send(shell._delegate, ObjC.Sel("performSelector:withObject:afterDelay:"),
                        ObjC.Sel("moteDeliverSelection:"), 0, 0d);
                }
                return;
            }
            shell._pendingNativeSelection = ObjC.SendRange(shell._editor, ObjC.Sel("selectedRange"));
            shell.CommitComposition();
            shell.ObserveCompositionState();
            if (!shell._selectionDeliveryScheduled)
            {
                shell._selectionDeliveryScheduled = true;
                ObjC.Send(shell._delegate, ObjC.Sel("performSelector:withObject:afterDelay:"),
                    ObjC.Sel("moteDeliverSelection:"), 0, 0d);
            }
            shell.ReplayDeferredDocument();
            shell.ReplayDeferredAnalysis();
        }
        catch (Exception error) { shell.ShowError(error.Message); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DeliverSelection(nint self, nint selector, nint sender)
    {
        var shell = s_current;
        if (shell is null) return;
        try
        {
            shell._selectionDeliveryScheduled = false;
            if (shell._experimentalCanvas)
            {
                shell._canvas?.OnSelectionChanged();
                return;
            }
            if (shell._pendingNativeSelection is not { } range) return;
            shell._pendingNativeSelection = null;
            if (shell._compositionDirty ||
                ObjC.Send(shell._editor, ObjC.Sel("hasMarkedText")) != 0) return;
            if (range.Location > (nuint)shell._visibleText.Length ||
                range.Length > (nuint)shell._visibleText.Length - range.Location) return;
            var start = checked((int)range.Location);
            var end = checked((int)(range.Location + range.Length));
            // NSTextView exposes only an ordered range. Retain the prior anchor
            // when keyboard extension keeps it at one of the new boundaries.
            var anchor = shell._selectionAnchor == end && start != end ? end : start;
            var active = anchor == end ? start : end;
            shell._selectionAnchor = anchor;
            shell.SelectionChanged?.Invoke(anchor, active);
        }
        catch (Exception error) { shell.ShowError(error.Message); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CommitCompositionCheck(nint self, nint selector, nint sender)
    {
        var shell = s_current;
        if (shell is null) return;
        try
        {
            shell._compositionCheckScheduled = false;
            if (ObjC.Send(shell._editor, ObjC.Sel("hasMarkedText")) != 0)
                shell.ScheduleCompositionCheck();
            else if (shell._experimentalCanvas)
            {
                shell._canvas?.CommitPendingText();
                shell.ObserveCompositionState();
            }
            else
            {
                shell.CommitComposition();
                shell.ObserveCompositionState();
            }
        }
        catch (Exception error) { shell.ShowError(error.Message); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte TextViewDoCommand(nint self, nint selector, nint view, nint command)
    {
        var shell = s_current;
        if (shell is null) return 0;
        if (shell._experimentalCanvas && IsDeletionCommand(command))
        {
            try
            {
                shell._canvas?.CancelUserSelectionGesture();
                if (!shell.CommitPendingText()) return 1;
                if (shell._canvas?.TryDeleteGlobalSelection() == true) return 1;
            }
            catch (Exception error)
            {
                shell._canvas?.DisableAfterFailure(error.Message);
                return 1;
            }
        }
        if (command == ObjC.Sel("selectAll:"))
        {
            shell._canvas?.CancelUserSelectionGesture();
            shell.NotifyAfterComposition(shell.SelectAllRequested);
            return 1;
        }
        if (command == ObjC.Sel("copy:"))
        {
            shell._canvas?.CancelUserSelectionGesture();
            shell.NotifyAfterComposition(shell.CopyRequested);
            return 1;
        }
        if (command == ObjC.Sel("cut:"))
        {
            shell._canvas?.CancelUserSelectionGesture();
            shell.NotifyAfterComposition(shell.CutRequested);
            return 1;
        }
        return 0;
    }

    private static bool IsDeletionCommand(nint command) =>
        command == ObjC.Sel("deleteBackward:") ||
        command == ObjC.Sel("deleteForward:") ||
        command == ObjC.Sel("deleteWordBackward:") ||
        command == ObjC.Sel("deleteWordForward:") ||
        command == ObjC.Sel("deleteToBeginningOfLine:") ||
        command == ObjC.Sel("deleteToEndOfLine:") ||
        command == ObjC.Sel("deleteToBeginningOfParagraph:") ||
        command == ObjC.Sel("deleteToEndOfParagraph:") ||
        command == ObjC.Sel("deleteBackwardByDecomposingPreviousCharacter:");

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte TextViewShouldChange(nint self, nint selector,
        nint view, ObjC.Range range, nint replacement)
    {
        var shell = s_current;
        if (shell is null || !shell._experimentalCanvas || shell._canvas is null) return 1;
        MacTextInputIsland.TraceStage("T0-before-text-change-enter");
        try
        {
            var allowed = shell._canvas.BeforeTextChange(range, replacement);
            MacTextInputIsland.TraceStage(allowed
                ? "T1-before-text-change-allowed" : "T2-before-text-change-vetoed");
            return allowed ? (byte)1 : (byte)0;
        }
        catch (Exception error)
        {
            MacTextInputIsland.TraceStage("T3-before-text-change-fault");
            shell.ShowError(error.Message);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte WindowShouldClose(nint self, nint selector, nint sender)
    {
        var shell = s_current;
        if (shell is null) return 1;
        if (shell._closeApproved) return 1;
        try
        {
            if (!shell.CommitPendingText())
            {
                shell.ShowError("Finish or cancel the current text composition before closing.");
                return 0;
            }
            var args = new NativeClosingEventArgs();
            shell.ClosingRequested?.Invoke(shell, args);
            shell._closeApproved = !args.Cancel;
            return args.Cancel ? (byte)0 : (byte)1;
        }
        catch (Exception error) { shell.ShowError(error.Message); return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void WindowWillClose(nint self, nint selector, nint sender)
    {
        var shell = s_current;
        // terminate: exits inside AppKit and skips managed post-run verification.
        if (shell is not null) shell.StopAndWake();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint ApplicationShouldTerminate(nint self, nint selector, nint sender)
    {
        var shell = s_current;
        if (shell is null) return 1;
        if (shell._closeApproved) { shell.StopAndWake(); return 0; }
        try
        {
            if (!shell.CommitPendingText())
            {
                shell.ShowError("Finish or cancel the current text composition before quitting.");
                return 0;
            }
            var args = new NativeClosingEventArgs();
            shell.ClosingRequested?.Invoke(shell, args);
            if (args.Cancel) return 0;
            shell._closeApproved = true;
            ObjC.Send(shell._window, ObjC.Sel("close"));
            if (ObjC.Send(shell._window, ObjC.Sel("isVisible")) != 0)
                shell._closeApproved = false;
            // Cancels AppKit's direct exit; WindowWillClose has stopped the run loop.
            return 0;
        }
        catch (Exception error) { shell.ShowError(error.Message); return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DrainPosted(nint self, nint selector, nint sender)
    {
        var shell = s_current;
        if (shell is null) return;
        while (shell._posted.TryDequeue(out var action)) shell.Notify(action);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DetachAccessibility(nint self, nint selector, nint sender)
    {
        try { s_current?.AccessibilityFaulted(); }
        catch { /* Never unwind a managed error through an AppKit selector. */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void New(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.NewRequested); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Open(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.OpenRequested); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Save(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.SaveRequested); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SaveAs(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.SaveAsRequested); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Undo(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.UndoRequested); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Redo(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.RedoRequested); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Format(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.FormatRequested); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PreviousPage(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.PagePreviousRequested); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void NextPage(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.PageNextRequested); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Find(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.FindRequested); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FindNext(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.FindNextRequested); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void GoToLine(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.GoToLineRequested); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SelectAll(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.SelectAllRequested); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Copy(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.CopyRequested); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Cut(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.CutRequested); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Quit(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.Close(); }
}
