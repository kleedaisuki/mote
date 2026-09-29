using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Themes;

namespace Mote.Native.Mac;

/// <summary>
/// macOS AppKit presentation shell for a single bounded document page.
/// NSTextView supplies native text input, selection, clipboard, accessibility, and IME;
/// the controller remains the sole owner of text, undo history, and file operations.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacEditorShell : INativeEditorShell
{
    private const nuint WindowStyle = 1 | 2 | 4 | 8;
    private const nuint ResizeWidthAndHeight = 2 | 16;
    private static MacEditorShell? s_current;
    private readonly ConcurrentQueue<Action> _posted = new();
    private nint _application;
    private nint _delegate;
    private nint _window;
    private nint _editor;
    private nint _preview;
    private nint _status;
    private IThemePolicy? _theme;
    private NativeDocumentView? _pendingDocument;
    private NativeAnalysisView? _pendingAnalysis;
    private bool _deferredDocument;
    private string? _deferredAnalysisText;
    private bool _settingText;
    private bool _settingSelection;
    private bool _compositionDirty;
    private bool _compositionCheckScheduled;
    private bool _compositionCommitRejected;
    private bool _closeApproved;
    private string _statusText = string.Empty;
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

    /// <inheritdoc />
    public NativeLineEndingMode LineEndingMode => NativeLineEndingMode.Preserve;

    /// <inheritdoc />
    public bool PrefersDark
    {
        get
        {
            // The app-level effective appearance includes macOS's system choice and
            // is the same appearance AppKit uses when drawing this application.
            ObjC.ApplicationLoad();
            var application = ObjC.Send(ObjC.Class("NSApplication"), ObjC.Sel("sharedApplication"));
            var appearance = ObjC.Send(application, ObjC.Sel("effectiveAppearance"));
            var name = ObjC.ManagedString(ObjC.Send(appearance, ObjC.Sel("name")));
            return name.Contains("Dark", StringComparison.OrdinalIgnoreCase);
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
                CreateWindow();
                CreateMenu();
                if (_theme is not null) ApplyTheme(_theme);
                if (_pendingDocument is not null) SetDocument(_pendingDocument);
                if (_pendingAnalysis is not null) SetAnalysis(_pendingAnalysis);
                if (_pendingSelection is { } selection) SetSelection(selection.Anchor, selection.Active);
                ObjC.Send(_window, ObjC.Sel("makeKeyAndOrderFront:"), 0);
                ObjC.Send(_application, ObjC.Sel("activateIgnoringOtherApps:"), 1);
                Post(() => Shown?.Invoke());
                ObjC.Send(_application, ObjC.Sel("run"));
            }
            finally { ObjC.Send(pool, ObjC.Sel("release")); }
        }
        finally { s_current = null; }
    }

    /// <inheritdoc />
    public void SetDocument(NativeDocumentView view)
    {
        _pendingDocument = view;
        _statusText = view.Status;
        if (_editor == 0) return;
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
        _pendingAnalysis = view;
        if (_editor == 0) return;
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
        SetPreview(view);
        SetStatus(string.IsNullOrEmpty(view.DiagnosticsSummary)
            ? view.Status : $"{view.Status}  ·  {view.DiagnosticsSummary}");
    }

    /// <inheritdoc />
    public void SetTheme(IThemePolicy theme)
    {
        _theme = theme;
        if (_editor != 0) ApplyTheme(theme);
    }

    /// <inheritdoc />
    public void SetSelection(int displayAnchor, int displayActive)
    {
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
        try { return CommitMarkedTextBeforeCommand(); }
        catch (Exception error)
        {
            ShowError(error.Message);
            return false;
        }
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
        var alert = ObjC.New("NSAlert");
        ObjC.Send(alert, ObjC.Sel("setAlertStyle:"), 2);
        ObjC.Send(alert, ObjC.Sel("setMessageText:"), ObjC.String("mote could not complete the operation"));
        ObjC.Send(alert, ObjC.Sel("setInformativeText:"), ObjC.String(message));
        ObjC.Send(alert, ObjC.Sel("runModal"));
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

    private static string? PanelPath(nint panel)
    {
        var url = ObjC.Send(panel, ObjC.Sel("URL"));
        return url == 0 ? null : ObjC.ManagedString(ObjC.Send(url, ObjC.Sel("path")));
    }

    /// <summary>Gets the actual native editor text for the in-process AppKit workflow probe.</summary>
    internal string ProbeNativeText => _editor == 0 ? string.Empty :
        ObjC.ManagedString(ObjC.Send(_editor, ObjC.Sel("string")));

    /// <summary>Gets the most recent controller title projected into the native window.</summary>
    internal string ProbeTitle => _pendingDocument?.Title ?? string.Empty;

    /// <summary>Gets whether the controller still considers the current native page dirty.</summary>
    internal bool ProbeIsModified => _pendingDocument?.IsModified ?? false;

    /// <summary>Gets the canonical page last sent by the controller, excluding marked preedit.</summary>
    internal string ProbeProjectedText => _pendingDocument?.Text ?? string.Empty;

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

    private void CreateWindow()
    {
        _window = ObjC.Send(ObjC.Send(ObjC.Class("NSWindow"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithContentRect:styleMask:backing:defer:"),
            new ObjC.Rect(120, 100, 1120, 760), WindowStyle, 2, 0);
        ObjC.Send(_window, ObjC.Sel("setReleasedWhenClosed:"), 0);
        ObjC.Send(_window, ObjC.Sel("setDelegate:"), _delegate);
        ObjC.Send(_window, ObjC.Sel("setTitle:"), ObjC.String("mote"));
        var root = ObjC.Send(_window, ObjC.Sel("contentView"));
        var split = ObjC.Send(ObjC.Send(ObjC.Class("NSSplitView"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(0, 30, 1120, 730));
        ObjC.Send(split, ObjC.Sel("setVertical:"), 1);
        ObjC.Send(split, ObjC.Sel("setAutoresizingMask:"), (nint)ResizeWidthAndHeight);
        ObjC.Send(root, ObjC.Sel("addSubview:"), split);

        var editorScroll = CreateScrollView(new ObjC.Rect(0, 0, 730, 730), true, out _editor);
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
        textView = ObjC.Send(ObjC.Send(ObjC.Class("NSTextView"), ObjC.Sel("alloc")),
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
            ("Quit mote", "terminate:", "q")], false);
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

    private void ApplyTheme(IThemePolicy theme)
    {
        var palette = theme.Palette;
        var editorForeground = Color(palette.EditorForeground);
        ObjC.Send(_editor, ObjC.Sel("setBackgroundColor:"), Color(palette.EditorBackground));
        ObjC.Send(_editor, ObjC.Sel("setTextColor:"), editorForeground);
        ObjC.Send(_editor, ObjC.Sel("setInsertionPointColor:"), Color(palette.Cursor));
        ObjC.Send(_preview, ObjC.Sel("setBackgroundColor:"), Color(palette.PreviewBackground));
        ObjC.Send(_preview, ObjC.Sel("setTextColor:"), Color(palette.PreviewForeground));
        ObjC.Send(_status, ObjC.Sel("setTextColor:"), Color(palette.MutedForeground));
        var editorFont = ObjC.Send(ObjC.Class("NSFont"), ObjC.Sel("monospacedSystemFontOfSize:weight:"),
            theme.Typography.EditorFontSize, 0d);
        ObjC.Send(_editor, ObjC.Sel("setFont:"), editorFont);
        ObjC.Send(_preview, ObjC.Sel("setFont:"), editorFont);
        SetStatus(_statusText);
    }

    private void SetPreview(NativeAnalysisView view)
    {
        ObjC.Send(_preview, ObjC.Sel("setString:"), ObjC.String(view.PreviewText));
        var length = view.PreviewText.Length;
        if (length == 0) return;
        var palette = _theme?.Palette;
        var foreground = Color(palette?.PreviewForeground ?? new ThemeColor(225, 227, 231));
        var whole = new ObjC.Range(0, (nuint)length);
        ObjC.Send(_preview, ObjC.Sel("setTextColor:range:"), foreground, whole);
        var baseFont = ObjC.Send(ObjC.Class("NSFont"), ObjC.Sel("systemFontOfSize:"),
            _theme?.Typography.UiFontSize ?? 12d);
        ObjC.Send(_preview, ObjC.Sel("setFont:range:"), baseFont, whole);

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

    private void SetStatus(string value) => ObjC.Send(_status, ObjC.Sel("setStringValue:"), ObjC.String(value));

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
            if (!CommitMarkedTextBeforeCommand())
            {
                ShowError("Finish or cancel the current text composition before this command.");
                return;
            }
            callback?.Invoke();
        }
        catch (Exception error) { ShowError(error.Message); }
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
        Add(cls, "windowShouldClose:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, byte>)&WindowShouldClose, "c@:@");
        Add(cls, "windowWillClose:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&WindowWillClose, "v@:@");
        Add(cls, "applicationShouldTerminate:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint>)&ApplicationShouldTerminate, "q@:@");
        Add(cls, "moteDrainPosted:", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&DrainPosted, "v@:@");
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
        ObjC.RegisterClassPair(cls);
        return className;
    }

    private static void Add(nint cls, string selector, nint implementation, string encoding)
    {
        if (!ObjC.AddMethod(cls, ObjC.Sel(selector), implementation, encoding))
            throw new InvalidOperationException($"Could not register {selector}.");
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
            // AppKit may announce caret collapse before textDidChange. Do not let
            // that transient page-local selection erase a global selection.
            shell._pendingNativeSelection = null;
            var value = ObjC.ManagedString(ObjC.Send(shell._editor, ObjC.Sel("string")));
            shell._visibleText = value;
            if (ObjC.Send(shell._editor, ObjC.Sel("hasMarkedText")) != 0)
            {
                shell._compositionDirty = true;
                shell.ScheduleCompositionCheck();
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
        }
        catch (Exception error) { shell.ShowError(error.Message); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void TextViewDidChangeSelection(nint self, nint selector, nint notification)
    {
        var shell = s_current;
        if (shell is null || shell._settingText || shell._settingSelection) return;
        try
        {
            shell._pendingNativeSelection = ObjC.SendRange(shell._editor, ObjC.Sel("selectedRange"));
            shell.CommitComposition();
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
            else
                shell.CommitComposition();
        }
        catch (Exception error) { shell.ShowError(error.Message); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte TextViewDoCommand(nint self, nint selector, nint view, nint command)
    {
        var shell = s_current;
        if (shell is null) return 0;
        if (command == ObjC.Sel("selectAll:"))
        {
            shell.NotifyAfterComposition(shell.SelectAllRequested);
            return 1;
        }
        if (command == ObjC.Sel("copy:"))
        {
            shell.NotifyAfterComposition(shell.CopyRequested);
            return 1;
        }
        if (command == ObjC.Sel("cut:"))
        {
            shell.NotifyAfterComposition(shell.CutRequested);
            return 1;
        }
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte WindowShouldClose(nint self, nint selector, nint sender)
    {
        var shell = s_current;
        if (shell is null) return 1;
        try
        {
            if (!shell.CommitMarkedTextBeforeCommand())
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
        if (shell is not null) ObjC.Send(shell._application, ObjC.Sel("terminate:"), 0);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint ApplicationShouldTerminate(nint self, nint selector, nint sender)
    {
        var shell = s_current;
        if (shell is null || shell._closeApproved) return 1;
        try
        {
            if (!shell.CommitMarkedTextBeforeCommand())
            {
                shell.ShowError("Finish or cancel the current text composition before quitting.");
                return 0;
            }
            var args = new NativeClosingEventArgs();
            shell.ClosingRequested?.Invoke(shell, args);
            return args.Cancel ? 0 : 1;
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
}
