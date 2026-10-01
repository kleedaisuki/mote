using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Engine;
using Mote.Formats;
using Mote.Native.Viewport;
using Mote.Themes;
using Mote.Telemetry;

namespace Mote.Native.Mac.Canvas;

/// <summary>
/// A source-backed AppKit canvas with one visible, bounded NSTextView input host.
/// The engine snapshot owns all off-host text; marked text remains in AppKit until commit.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacTextInputIsland
{
    /// <summary>Optional source endpoint shared with the containing shell.</summary>
    private readonly NativeDrawTrace? _drawTrace;
    private const string ViewClass = "MoteInteractiveCanvasView";
    private const string InputClass = "MoteInteractiveCanvasInputView";
    private const string HostScrollClass = "MoteInteractiveCanvasHostScrollView";
    private const int MaxInputLength = 16 * 1024;
    /// <summary>
    /// Controller-requested source window. The remaining native capacity lets
    /// NSTextView accept ordinary insertion or IME commits before the next
    /// synchronous source rebind; the 16 Ki pre-change limit remains a hard cap.
    /// </summary>
    internal const int MaxBindingLength = MaxInputLength / 2;
    private const double LeftInset = 12;
    private const double RibbonHeight = 36;
    private const double RibbonLabelWidth = 140;
    private const string ObjcRuntime = "/usr/lib/libobjc.A.dylib";
    private static MacTextInputIsland? s_current;
    private static readonly bool s_traceStages =
        Environment.GetEnvironmentVariable("MOTE_NATIVE_MAC_STAGE_TRACE") == "1";

    /// <summary>Privacy-safe target-host ABI breadcrumbs, disabled in normal editing.</summary>
    internal static void TraceStage(string code)
    {
        if (s_traceStages) Console.Error.WriteLine($"mote-mac-stage:{code}");
    }

    private readonly Action<CanvasCommittedEdit> _edit;
    private readonly Action<double> _scroll;
    private readonly Action<CanvasHorizontalAnchorRequest> _horizontal;
    private readonly Action<double> _resize;
    private readonly Action<int, int> _selection;
    private readonly Action<string> _error;
    private NativeCanvasBinding? _binding;
    private NativeCanvasBinding? _deferredBinding;
    private CanvasFrame? _frame;
    private NativeCanvasSemantics? _semantics;
    private TextSnapshot? _snapshot;
    private NativeTextProjection? _projection;
    private IThemePolicy? _theme;
    private nint _view;
    private nint _hostScroll;
    private nint _ribbonLabel;
    private nint _editor;
    private nint _font;
    private nint _fontAttribute;
    private nint _contextColorAttribute;
    private nint _trueValue;
    private bool _setting;
    private bool _inChange;
    private bool _compositionDirty;
    private bool _nativeChangeObserved;
    private ObjC.Range? _replacementRangeBeforeEdit;
    private double _width;
    private double _height;
    private double _reportedBodyHeight = -1;
    private bool _pendingResize;
    private int _dragAnchor;
    private bool _reportedFailure;
    private bool _hostAligned;
    private bool _userSelectionPending;
    private int _selectionRepairCount;
    private int _selectionNotifications;
    private int _selectionEchoes;
    private int _selectionUserEvents;
    private string _lastSelectionOutcome = "none";

    [DllImport(ObjcRuntime, EntryPoint = "objc_msgSend")]
    private static extern ObjC.Rect SendGlyphRectDirect(nint receiver, nint selector,
        ObjC.Range glyphRange, nint textContainer);

    [DllImport(ObjcRuntime, EntryPoint = "objc_msgSend_stret")]
    private static extern void SendGlyphRectStret(out ObjC.Rect result,
        nint receiver, nint selector, ObjC.Range glyphRange, nint textContainer);

    [DllImport(ObjcRuntime, EntryPoint = "objc_msgSendSuper")]
    private static extern void SendSuperNoArgument(ref MacOnScreenCanvasNative.Super receiver,
        nint selector);

    /// <summary>Creates callbacks that keep document mutations in the controller.</summary>
    internal MacTextInputIsland(Action<CanvasCommittedEdit> edit, Action<double> scroll,
        Action<CanvasHorizontalAnchorRequest> horizontal, Action<double> resize,
        Action<int, int> selection, Action<string> error, NativeDrawTrace? drawTrace = null)
    {
        _drawTrace = drawTrace;
        _edit = edit;
        _scroll = scroll;
        _horizontal = horizontal;
        _resize = resize;
        _selection = selection;
        _error = error;
    }

    /// <summary>The real visible NSTextView receiving AppKit text input.</summary>
    internal nint Editor => _editor;

    /// <summary>The source-backed NSView that may own the opt-in AX element.</summary>
    internal nint CanvasView => _view;

    /// <summary>The actually painted source region in the canvas view's coordinates.</summary>
    internal ObjC.Rect BodyRect => new(0, Math.Min(RibbonHeight, _height),
        _width, Math.Max(0, _height - RibbonHeight));

    /// <summary>Publishes the actual source-body height before AX attaches to a frame.</summary>
    internal void PublishBodyHeight()
    {
        if (_view == 0) return;
        var rect = MacOnScreenCanvasNative.GetRect(_view, ObjC.Sel("bounds"));
        var changedBounds = false;
        if (rect.Size.Width >= 0 && rect.Size.Height >= 0 &&
            (rect.Size.Width != _width || rect.Size.Height != _height))
        {
            _width = rect.Size.Width;
            _height = rect.Size.Height;
            changedBounds = true;
        }
        var composing = IsComposing;
        if (composing) _pendingResize = true;
        var body = BodyHeight;
        var changedBody = Math.Abs(_reportedBodyHeight - body) > 0.01;
        // Shrink the source frame before its AX rectangle; on growth, expand
        // the AX rectangle first. Either intermediate state is conservative.
        if (changedBody && _reportedBodyHeight >= 0 && body < _reportedBodyHeight)
        {
            _reportedBodyHeight = body;
            _resize(body);
            if (changedBounds) ViewGeometryChanged?.Invoke();
        }
        else
        {
            if (changedBounds) ViewGeometryChanged?.Invoke();
            if (changedBody)
            {
                _reportedBodyHeight = body;
                _resize(body);
            }
        }
        if (!composing) PlaceHost();
    }

    /// <summary>
    /// Measures a current-version source caret with the same bounded CoreText
    /// row transform used for paint and hit testing. Ambiguous visual edges do
    /// not become false visibility proofs.
    /// </summary>
    internal CanvasCaretGeometry? GetCaretGeometry(CanvasFrame frame, int sourceOffset)
    {
        if (!ReferenceEquals(frame, _frame) || _snapshot is null || _font == 0 ||
            frame.Version != _snapshot.Version || sourceOffset < 0 ||
            sourceOffset > _snapshot.Length) return null;
        foreach (var row in frame.RowWindows)
        {
            var slice = row.Slice;
            if (sourceOffset < slice.SourceStart ||
                sourceOffset > slice.SourceStart + slice.SourceLength) continue;
            if (slice.HasHiddenSuffix &&
                sourceOffset == slice.SourceStart + slice.SourceLength) return null;
            if (slice.HasHiddenPrefix && sourceOffset == slice.SourceStart)
            {
                var lineStart = _snapshot.GetLineStartOffset(slice.Line);
                var lineEnd = slice.Line + 1 < _snapshot.LineCount
                    ? _snapshot.GetLineStartOffset(slice.Line + 1) : _snapshot.Length;
                if (SnapGraphemeAt(_snapshot, lineStart, lineEnd, sourceOffset) !=
                    sourceOffset) return null;
            }
            var text = _snapshot.GetText(slice.SourceStart, slice.SourceLength);
            if (!IsGraphemeBoundary(text, sourceOffset - slice.SourceStart)) return null;
            CanvasCaretGeometry? result = null;
            WithLine(text, line =>
            {
                if (!TryRowOrigin(line, row, text, out var origin) ||
                    !TryCaretOffset(line, sourceOffset - slice.SourceStart, out var x)) return;
                var screenX = origin + x;
                var inViewport = screenX >= LeftInset && screenX <= _width &&
                    slice.TopY >= 0 && slice.TopY < Math.Max(0, _height - RibbonHeight);
                result = new CanvasCaretGeometry(screenX, slice.TopY, slice.Height,
                    inViewport);
            });
            return result;
        }
        return null;
    }

    /// <summary>Raised after AppKit changes the canvas view bounds.</summary>
    internal event Action? ViewGeometryChanged;

    /// <summary>Raised on AppKit's UI thread when the canvas inherits a new appearance.</summary>
    internal event Action? EffectiveAppearanceChanged;

    /// <summary>Whether the OS currently owns provisional candidate text.</summary>
    internal bool IsComposing => _editor != 0 && ObjC.Send(_editor, ObjC.Sel("hasMarkedText")) != 0;

    /// <summary>Whether a final native commit/cancel still needs source reconciliation.</summary>
    internal bool HasPendingComposition => _compositionDirty || IsComposing;

    private double BodyHeight => Math.Max(0, _height - RibbonHeight);

    /// <summary>Probe-only native selected range and event-origin summary.</summary>
    internal string ProbeSelectionTrace
    {
        get
        {
            var range = _editor == 0 ? new ObjC.Range(0, 0) :
                ObjC.SendRange(_editor, ObjC.Sel("selectedRange"));
            return $"native={range.Location},{range.Length}; notifications={_selectionNotifications}; " +
                $"echoes={_selectionEchoes}; user={_selectionUserEvents}; " +
                $"pending={_userSelectionPending}; last={_lastSelectionOutcome}";
        }
    }

    /// <summary>Whether a native callback fault has disabled the bounded host.</summary>
    internal bool ProbeInputDisabled => _reportedFailure && _binding is null;

    /// <summary>Bounded host alignment and local TextKit clip telemetry for target-host QA.</summary>
    internal (bool Aligned, bool Hidden, double NativeX, double ClipX)?
        ProbeHostGeometry(int sourceOffset)
    {
        if (_binding is null || _hostScroll == 0) return null;
        var local = sourceOffset - _binding.InputSourceStart;
        if (!TryNativeCaretX(local, out var nativeX)) return null;
        var clip = ObjC.Send(_hostScroll, ObjC.Sel("contentView"));
        var clipX = MacOnScreenCanvasNative.GetRect(clip, ObjC.Sel("bounds")).Origin.X;
        return (_hostAligned, ObjC.Send(_hostScroll, ObjC.Sel("isHidden")) != 0,
            nativeX, clipX);
    }

    /// <summary>Drives the same bounded pan and inverse hit test as native AppKit callbacks.</summary>
    internal void ProbePanHorizontal(double pixels) => PanHorizontal(pixels);

    /// <summary>Returns the global UTF-16 source edge selected by canvas hit testing.</summary>
    internal int ProbeHitSource(double x, double topY) => HitSource(x, topY);

    /// <summary>Injects one unarmed AppKit selection echo for a hosted fail-closed probe.</summary>
    internal void ProbeProgrammaticSelectionEcho(int start, int length)
    {
        if (_editor == 0 || _reportedFailure) return;
        ObjC.Send(_editor, ObjC.Sel("setSelectedRange:"),
            new ObjC.Range((nuint)start, (nuint)length));
        OnSelectionChanged();
    }

    /// <summary>Marks a real native keyboard or pointer event as the source of selection.</summary>
    internal void ArmUserSelectionGesture() => _userSelectionPending = true;

    /// <summary>Invalidates a pending local gesture before a global menu command projects state.</summary>
    internal void CancelUserSelectionGesture() => _userSelectionPending = false;

    /// <summary>Releases only the font owned by this island and unhooks native callbacks.</summary>
    internal void Dispose()
    {
        if (s_current == this) s_current = null;
        ViewGeometryChanged = null;
        EffectiveAppearanceChanged = null;
        if (_font != 0) CoreTextNative.Release(_font);
        _font = 0;
        _font = 0;
    }

    /// <summary>Creates the canvas and bounded input host as one left-pane view.</summary>
    internal nint CreateView(ObjC.Rect frame)
    {
        TraceStage("C0-create-view");
        if (s_current is not null) throw new InvalidOperationException("Only one interactive canvas may exist.");
        s_current = this;
        _width = frame.Size.Width;
        _height = frame.Size.Height;
        _view = ObjC.Send(ObjC.Send(ObjC.Class(RegisterClass()), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), frame);
        if (_view == 0) throw new InvalidOperationException("AppKit could not create the canvas.");
        ObjC.Send(_view, ObjC.Sel("setAutoresizingMask:"), (nint)18);
        _hostScroll = ObjC.Send(ObjC.Send(ObjC.Class(RegisterHostScrollClass()), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(RibbonLabelWidth, 4,
                Math.Max(1, _width - RibbonLabelWidth - 8), RibbonHeight - 8));
        ObjC.Send(_hostScroll, ObjC.Sel("setBorderType:"), 0);
        // The clip view still scrolls the caret into view programmatically;
        // a scrollbar would consume almost the entire one-row input island.
        ObjC.Send(_hostScroll, ObjC.Sel("setHasHorizontalScroller:"), 0);
        ObjC.Send(_hostScroll, ObjC.Sel("setAutohidesScrollers:"), 1);
        // Explicitly resize after composition: AppKit's autoresizing would
        // otherwise move the candidate rectangle while marked text is owned.
        ObjC.Send(_hostScroll, ObjC.Sel("setAutoresizingMask:"), (nint)0);
        _editor = ObjC.Send(ObjC.Send(ObjC.Class(RegisterInputClass()), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(0, 0, _width, 24));
        if (_editor == 0) throw new InvalidOperationException("AppKit could not create the input host.");
        // A source file is plain text; attributed clipboard payloads must not
        // smuggle formatting or an unbounded object graph into the input host.
        ObjC.Send(_editor, ObjC.Sel("setRichText:"), 0);
        ObjC.Send(_editor, ObjC.Sel("setImportsGraphics:"), 0);
        ObjC.Send(_editor, ObjC.Sel("setEditable:"), 1);
        ObjC.Send(_editor, ObjC.Sel("setSelectable:"), 1);
        ObjC.Send(_editor, ObjC.Sel("setAllowsUndo:"), 0);
        ObjC.Send(_editor, ObjC.Sel("setHorizontallyResizable:"), 1);
        ObjC.Send(_editor, ObjC.Sel("setVerticallyResizable:"), 0);
        ObjC.Send(_editor, ObjC.Sel("setAutoresizingMask:"), (nint)2);
        ObjC.Send(_editor, ObjC.Sel("setMinSize:"), new ObjC.Size(0, 24));
        ObjC.Send(_editor, ObjC.Sel("setMaxSize:"), new ObjC.Size(1_000_000_000, 24));
        ObjC.Send(_editor, ObjC.Sel("setTextContainerInset:"), new ObjC.Size(LeftInset, 1));
        var container = ObjC.Send(_editor, ObjC.Sel("textContainer"));
        // NSTextContainer defaults to another 5 DIP on each line fragment.
        // The canvas already owns the 12-DIP inset; retaining both makes an
        // unscrollable short line appear permanently misaligned.
        ObjC.Send(container, ObjC.Sel("setLineFragmentPadding:"), 0d);
        ObjC.Send(container, ObjC.Sel("setContainerSize:"), new ObjC.Size(1_000_000_000, 24));
        ObjC.Send(container, ObjC.Sel("setWidthTracksTextView:"), 0);
        ObjC.Send(_editor, ObjC.Sel("setAutomaticQuoteSubstitutionEnabled:"), 0);
        ObjC.Send(_editor, ObjC.Sel("setAutomaticDashSubstitutionEnabled:"), 0);
        ObjC.Send(_editor, ObjC.Sel("setAutomaticTextReplacementEnabled:"), 0);
        ObjC.Send(_editor, ObjC.Sel("setContinuousSpellCheckingEnabled:"), 0);
        ObjC.Send(_hostScroll, ObjC.Sel("setDocumentView:"), _editor);
        ObjC.Send(_view, ObjC.Sel("addSubview:"), _hostScroll);
        _ribbonLabel = ObjC.Send(ObjC.Send(ObjC.Class("NSTextField"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(8, 7,
                RibbonLabelWidth - 16, RibbonHeight - 12));
        ObjC.Send(_ribbonLabel, ObjC.Sel("setEditable:"), 0);
        ObjC.Send(_ribbonLabel, ObjC.Sel("setSelectable:"), 0);
        ObjC.Send(_ribbonLabel, ObjC.Sel("setBezeled:"), 0);
        ObjC.Send(_ribbonLabel, ObjC.Sel("setDrawsBackground:"), 0);
        ObjC.Send(_ribbonLabel, ObjC.Sel("setFont:"), ObjC.Send(ObjC.Class("NSFont"),
            ObjC.Sel("systemFontOfSize:"), 11d));
        ObjC.Send(_ribbonLabel, ObjC.Sel("setStringValue:"), ObjC.String("Input @ 0"));
        ObjC.Send(_view, ObjC.Sel("addSubview:"), _ribbonLabel);
        TraceStage("C1-view-ready");
        return _view;
    }

    /// <summary>
    /// Replaces the host's one-line source window. Reentrant controller bindings
    /// are applied immediately after textDidChange returns, before another OS input.
    /// A binding uses at most 8 Ki; the remaining native capacity is reserved
    /// for AppKit's transient edit before the controller publishes a new binding.
    /// </summary>
    internal void Bind(NativeCanvasBinding binding)
    {
        TraceStage("B0-bind-enter");
        ArgumentNullException.ThrowIfNull(binding);
        if (_reportedFailure)
            throw new InvalidOperationException("The canvas input host failed; reopen the editor.");
        if (binding.BaseVersion != binding.Snapshot.Version ||
            binding.Frame.Version != binding.Snapshot.Version ||
            binding.InputSourceText.Length > MaxBindingLength ||
            binding.InputSourceStart < 0 ||
            binding.InputSourceStart > binding.Snapshot.Length - binding.InputSourceText.Length ||
            binding.InputSourceText.IndexOfAny(['\r', '\n']) >= 0 ||
            !string.Equals(binding.Snapshot.GetText(binding.InputSourceStart,
                binding.InputSourceText.Length), binding.InputSourceText, StringComparison.Ordinal))
            throw new ArgumentException("Input binding must be an exact single-line source slice with native edit reserve.",
                nameof(binding));
        if (IsComposing) throw new InvalidOperationException("Cannot rebind during marked text.");
        if (_inChange)
        {
            _deferredBinding = binding;
            return;
        }
        _binding = binding;
        CancelUserSelectionGesture();
        _selectionRepairCount = 0;
        _snapshot = binding.Snapshot;
        _frame = binding.Frame;
        _projection = new NativeTextProjection(binding.InputSourceText, NativeLineEndingMode.Preserve);
        if (_editor == 0) return;
        _setting = true;
        try
        {
            ObjC.Send(_editor, ObjC.Sel("setString:"), ObjC.String(_projection.Display));
            var local = Math.Clamp(binding.Active - binding.InputSourceStart, 0,
                _projection.Display.Length);
            ObjC.Send(_editor, ObjC.Sel("setSelectedRange:"), new ObjC.Range((nuint)local, 0));
            _compositionDirty = false;
            _nativeChangeObserved = false;
            _replacementRangeBeforeEdit = null;
            TraceStage("B1-before-place-host");
            PlaceHost();
        }
        finally { _setting = false; }
        Invalidate();
        TraceStage("B2-bind-ready");
    }

    /// <summary>
    /// Shows an immutable source snapshot with a disabled but visible ribbon
    /// when no safe bounded grapheme input window exists. Old callbacks are inert.
    /// </summary>
    internal void SetUnavailable(TextSnapshot snapshot, CanvasFrame frame, string reason)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (frame.Version != snapshot.Version)
            throw new ArgumentException("Unavailable canvas frame must match its snapshot.",
                nameof(frame));
        if (IsComposing) throw new InvalidOperationException("Cannot remove input during marked text.");
        _binding = null;
        CancelUserSelectionGesture();
        _deferredBinding = null;
        _projection = null;
        _nativeChangeObserved = false;
        _replacementRangeBeforeEdit = null;
        _snapshot = snapshot;
        _frame = frame;
        _hostAligned = false;
        if (_editor != 0)
        {
            _setting = true;
            try
            {
                ObjC.Send(_editor, ObjC.Sel("setEditable:"), 0);
                ObjC.Send(_editor, ObjC.Sel("setString:"), ObjC.String(string.Empty));
                ObjC.Send(_hostScroll, ObjC.Sel("setHidden:"), 0);
                SetRibbonLabel("Input unavailable");
            }
            finally { _setting = false; }
        }
        Invalidate();
    }

    /// <summary>Updates paint and global selection without touching native text.</summary>
    internal void SetFrame(CanvasFrame frame)
    {
        if (IsComposing)
        {
            // Viewport geometry may change under a marked candidate, but the
            // NSTextView ribbon and its text/selection remain untouched.
            _frame = frame;
            Invalidate();
            return;
        }
        CancelUserSelectionGesture();
        _selectionRepairCount = 0;
        _frame = frame;
        PlaceHost();
        Invalidate();
    }

    /// <summary>Applies only matching-version semantic ranges to the visible canvas.</summary>
    internal void SetSemantics(NativeCanvasSemantics semantics)
    {
        _semantics = semantics;
        if (!IsComposing) Invalidate();
    }

    /// <summary>Changes paint and host font only when no marked text exists.</summary>
    internal void SetTheme(IThemePolicy theme)
    {
        if (_editor == 0) { _theme = theme; return; }
        if (IsComposing) throw new NativeThemeDeferredException();
        var updateFonts = _font == 0 || _theme is null ||
            _theme.Typography != theme.Typography || _theme.Spacing != theme.Spacing;
        _theme = theme;
        if (updateFonts)
        {
            if (_font != 0)
            {
                CoreTextNative.Release(_font);
                _font = 0;
            }
            var family = theme.Typography.EditorFontFamilies.Split(',',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(static name => name.Equals("Menlo", StringComparison.OrdinalIgnoreCase))
                ?? "Menlo";
            var nameString = CreateString(family);
            try { _font = CoreTextNative.FontCreate(nameString, theme.Typography.EditorFontSize, 0); }
            finally { CoreTextNative.Release(nameString); }
            _fontAttribute = CoreTextNative.FontAttributeName;
            _contextColorAttribute = MacOnScreenCanvasNative.ForegroundColorFromContextAttributeName;
            _trueValue = MacOnScreenCanvasNative.BooleanTrue;
            if (_font == 0 || _fontAttribute == 0 || _contextColorAttribute == 0 || _trueValue == 0)
                throw new InvalidOperationException("CoreText could not resolve the canvas font.");
            var font = ObjC.SendObjectDouble(ObjC.Class("NSFont"), ObjC.Sel("fontWithName:size:"),
                ObjC.String(family), theme.Typography.EditorFontSize);
            if (font == 0)
                font = ObjC.Send(ObjC.Class("NSFont"),
                    ObjC.Sel("monospacedSystemFontOfSize:weight:"),
                    theme.Typography.EditorFontSize, 0d);
            ObjC.Send(_editor, ObjC.Sel("setFont:"), font);
        }
        ObjC.Send(_editor, ObjC.Sel("setTextColor:"), Color(theme.Palette.EditorForeground));
        ObjC.Send(_editor, ObjC.Sel("setBackgroundColor:"), Color(theme.Palette.PanelBackground));
        ObjC.Send(_editor, ObjC.Sel("setInsertionPointColor:"), Color(theme.Palette.Cursor));
        ObjC.Send(_hostScroll, ObjC.Sel("setBackgroundColor:"),
            Color(theme.Palette.PanelBackground));
        if (_ribbonLabel != 0)
            ObjC.Send(_ribbonLabel, ObjC.Sel("setTextColor:"),
                Color(theme.Palette.MutedForeground));
        if (updateFonts) PlaceHost();
        Invalidate();
    }

    /// <summary>Forwards only final NSTextView text, never marked preedit.</summary>
    internal void OnTextChanged()
    {
        if (_setting || _binding is null) return;
        CancelUserSelectionGesture();
        _nativeChangeObserved = true;
        if (IsComposing)
        {
            _compositionDirty = true;
            return;
        }
        CommitFinal();
        ReplayResize();
    }

    /// <summary>
    /// Captures the actual pre-edit native replacement range and prevents any
    /// paste, accessibility, service, or ordinary input from enlarging the
    /// bounded NSTextView beyond 16 Ki UTF-16 units.
    /// </summary>
    internal bool BeforeTextChange(ObjC.Range range, nint replacement)
    {
        if (_setting || _binding is null || replacement == 0) return true;
        var current = checked((int)ObjC.Send(ObjC.Send(_editor, ObjC.Sel("string")),
            ObjC.Sel("length")));
        if (range.Location > (nuint)current || range.Length > (nuint)current - range.Location)
            return false;
        var incoming = checked((int)ObjC.Send(replacement, ObjC.Sel("length")));
        if ((long)current - (long)range.Length + incoming > MaxInputLength)
        {
            _error("Input exceeds the bounded canvas host (16 Ki UTF-16 units). " +
                "No text was inserted; use the standard editor for this payload.");
            return false;
        }
        if (!_compositionDirty && !IsComposing)
            _replacementRangeBeforeEdit = range;
        return true;
    }

    /// <summary>Converts a bounded host caret/selection to global source coordinates.</summary>
    internal void OnSelectionChanged()
    {
        _selectionNotifications++;
        if (_setting || IsComposing || _binding is null || _projection is null)
        {
            _lastSelectionOutcome = "setting-or-composing";
            return;
        }
        var range = ObjC.SendRange(_editor, ObjC.Sel("selectedRange"));
        if (range.Location > (nuint)_projection.Display.Length ||
            range.Length > (nuint)_projection.Display.Length - range.Location) return;
        if (_frame is not null)
        {
            var expected = ProjectedHostSelection();
            if (range == expected)
            {
                CancelUserSelectionGesture();
                _lastSelectionOutcome = "matches-global";
                return;
            }
            if (!_userSelectionPending)
            {
                _selectionEchoes++;
                _lastSelectionOutcome = "programmatic-echo";
                if (_selectionRepairCount++ < 2)
                {
                    _setting = true;
                    try { ObjC.Send(_editor, ObjC.Sel("setSelectedRange:"), expected); }
                    finally { _setting = false; }
                }
                else DisableAfterFailure("Native selection did not reconcile with the source selection.");
                return;
            }
        }
        CancelUserSelectionGesture();
        _selectionUserEvents++;
        _lastSelectionOutcome = "user-accepted";
        var anchor = _binding.InputSourceStart + _projection.ToSourceBoundary((int)range.Location);
        var active = _binding.InputSourceStart +
            _projection.ToSourceBoundary((int)(range.Location + range.Length), true);
        _selection(anchor, active);
    }

    /// <summary>Accepts marked text synchronously or vetoes save/open/close.</summary>
    internal bool CommitPendingText()
    {
        if (_editor == 0 || _binding is null) return true;
        if (IsComposing)
        {
            _compositionDirty = true;
            ObjC.Send(_editor, ObjC.Sel("unmarkText"));
            if (IsComposing) return false;
        }
        CommitFinal();
        ReplayResize();
        return !_compositionDirty && !IsComposing;
    }

    private void ReplayResize()
    {
        if (!_pendingResize || IsComposing) return;
        _pendingResize = false;
        PublishBodyHeight();
    }

    /// <summary>
    /// Handles Backspace/Delete for any global selection, including an empty
    /// native host where AppKit would emit no textDidChange at all.
    /// </summary>
    internal bool TryDeleteGlobalSelection()
    {
        if (_binding is null || _frame is null || IsComposing ||
            _frame.SelectionLength == 0) return false;
        EmitDirect(new TextChange(_frame.SelectionStart,
            _frame.SelectionLength, string.Empty), _frame.SelectionStart);
        return true;
    }

    private void EmitDirect(TextChange change, int active)
    {
        if (_binding is null || _inChange || IsComposing)
            throw new InvalidOperationException("The native input binding cannot accept a direct edit.");
        var binding = _binding;
        _inChange = true;
        try
        {
            _edit(new CanvasCommittedEdit(binding.DocumentGeneration, binding.BaseVersion,
                binding.BindingNonce, change, active));
        }
        finally
        {
            _inChange = false;
            if (_deferredBinding is { } next)
            {
                _deferredBinding = null;
                Bind(next);
            }
        }
        if (_binding?.BindingNonce == binding.BindingNonce)
            throw new InvalidOperationException("The controller did not acknowledge the direct canvas edit.");
    }

    private void CommitFinal()
    {
        if (_binding is null || _projection is null || _inChange) return;
        if (!_nativeChangeObserved && !_compositionDirty) return;
        TraceStage("F0-before-native-diff");
        var final = ObjC.ManagedString(ObjC.Send(_editor, ObjC.Sel("string")));
        var change = _projection.Difference(final);
        TraceStage("F1-native-diff-ready");
        var wasComposition = _compositionDirty;
        _compositionDirty = false;
        _nativeChangeObserved = false;
        var binding = _binding;
        var anchor = _frame?.SelectionAnchor ?? binding.Anchor;
        var activeSelection = _frame?.SelectionActive ?? binding.Active;
        if (change is null && (anchor == activeSelection || wasComposition)) return;
        var local = change ?? new TextChange(0, 0, string.Empty);
        var start = binding.InputSourceStart + local.Start;
        var delete = local.DeleteLength;
        var insert = local.InsertText;
        if (anchor != activeSelection)
        {
            // Minimal differences trim equal prefix/suffix characters and do
            // not reveal the actual replacement payload. Recover it from the
            // native pre-edit range, or reject rather than corrupt global text.
            try
            {
                var nativeRange = _replacementRangeBeforeEdit ?? throw new
                    InvalidOperationException("The selected native replacement range is unknown.");
                insert = ExactReplacement(_projection.Display, final, nativeRange);
            }
            catch
            {
                Bind(binding);
                throw;
            }
            start = Math.Min(anchor, activeSelection);
            delete = Math.Abs(anchor - activeSelection);
        }
        var selected = ObjC.SendRange(_editor, ObjC.Sel("selectedRange"));
        var active = binding.InputSourceStart + Math.Clamp((int)selected.Location,
            0, final.Length);
        if (anchor != activeSelection) active = start + insert.Length;
        _inChange = true;
        try
        {
            TraceStage("F2-before-controller-edit");
            _edit(new CanvasCommittedEdit(binding.DocumentGeneration, binding.BaseVersion,
                binding.BindingNonce, new TextChange(start, delete, insert), active));
            TraceStage("F3-controller-edit-returned");
        }
        finally
        {
            _inChange = false;
            if (_deferredBinding is { } next)
            {
                _deferredBinding = null;
                Bind(next);
            }
        }
        if (_binding?.BindingNonce == binding.BindingNonce)
        {
            Bind(binding);
            throw new InvalidOperationException("The controller did not acknowledge the native canvas edit.");
        }
    }

    private static string ExactReplacement(string before, string after, ObjC.Range nativeRange)
    {
        if (nativeRange.Location > (nuint)before.Length ||
            nativeRange.Length > (nuint)before.Length - nativeRange.Location)
            throw new InvalidOperationException("The native replacement range is outside the input window.");
        var start = checked((int)nativeRange.Location);
        var end = checked((int)(nativeRange.Location + nativeRange.Length));
        var tail = before.Length - end;
        if (after.Length < start + tail ||
            !after.AsSpan(0, start).SequenceEqual(before.AsSpan(0, start)) ||
            !after.AsSpan(after.Length - tail).SequenceEqual(before.AsSpan(end)))
            throw new InvalidOperationException("The selected native replacement is not a single exact edit.");
        return after.Substring(start, after.Length - start - tail);
    }

    /// <summary>
    /// Keeps the OS text client in one always-visible bottom input ribbon.
    /// Source panning never moves this view or its IME candidate rectangle.
    /// </summary>
    private void PlaceHost()
    {
        TraceStage("P0-place-host");
        if (_editor == 0 || _binding is null || _frame is null || IsComposing ||
            !TryGetRibbonContext(_binding, _frame, out var label, out var projected)) return;
        ObjC.Send(_hostScroll, ObjC.Sel("setFrame:"), new ObjC.Rect(
            RibbonLabelWidth, 4, Math.Max(1, _width - RibbonLabelWidth - 8),
            RibbonHeight - 8));
        ObjC.Send(_hostScroll, ObjC.Sel("setHidden:"), 0);
        ObjC.Send(_editor, ObjC.Sel("setEditable:"), 1);
        SetRibbonLabel(label);
        _setting = true;
        try
        {
            ObjC.Send(_editor, ObjC.Sel("setSelectedRange:"), projected);
            TraceStage("P1-before-size-to-fit");
            ObjC.Send(_editor, ObjC.Sel("sizeToFit"));
            TraceStage("P2-before-scroll-range");
            ObjC.Send(_editor, ObjC.Sel("scrollRangeToVisible:"),
                new ObjC.Range(projected.Location + projected.Length, 0));
            TraceStage("P3-before-native-caret");
            var local = checked((int)(projected.Location + projected.Length));
            var clip = ObjC.Send(_hostScroll, ObjC.Sel("contentView"));
            var clipX = MacOnScreenCanvasNative.GetRect(clip,
                ObjC.Sel("bounds")).Origin.X;
            _hostAligned = TryNativeCaretX(local, out var x) &&
                x - clipX >= 0 && x - clipX <= _width - RibbonLabelWidth - 8;
            TraceStage("P4-host-ready");
        }
        finally { _setting = false; }
    }

    private void SetRibbonLabel(string value)
    {
        if (_ribbonLabel != 0)
            ObjC.Send(_ribbonLabel, ObjC.Sel("setStringValue:"), ObjC.String(value));
    }

    /// <summary>Gets a bounded native glyph edge in NSTextView local coordinates.</summary>
    private bool TryNativeCaretX(int local, out double x)
    {
        x = 0;
        if (_editor == 0 || _binding is null || local < 0 ||
            local > _binding.InputSourceText.Length) return false;
        var origin = MacOnScreenCanvasNative.SendPoint(_editor,
            ObjC.Sel("textContainerOrigin"));
        if (local == 0 && _binding.InputSourceText.Length == 0)
        {
            x = origin.X;
            return double.IsFinite(x);
        }
        var container = ObjC.Send(_editor, ObjC.Sel("textContainer"));
        var layout = ObjC.Send(_editor, ObjC.Sel("layoutManager"));
        if (container == 0 || layout == 0) return false;
        ObjC.Send(layout, ObjC.Sel("ensureLayoutForTextContainer:"), container);
        var atEnd = local == _binding.InputSourceText.Length;
        var character = atEnd ? local - 1 : local;
        if (character < 0) return false;
        var glyph = checked((nuint)ObjC.Send(layout,
            ObjC.Sel("glyphIndexForCharacterAtIndex:"), (nint)character));
        TraceStage("G0-before-glyph-rect");
        var rect = RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? GetGlyphRectStret(layout, glyph, container)
            : SendGlyphRectDirect(layout, ObjC.Sel("boundingRectForGlyphRange:inTextContainer:"),
                new ObjC.Range(glyph, 1), container);
        TraceStage("G1-after-glyph-rect");
        x = origin.X + rect.Origin.X + (atEnd ? rect.Size.Width : 0);
        return double.IsFinite(x) && x >= 0;
    }

    private static ObjC.Rect GetGlyphRectStret(nint layout, nuint glyph, nint container)
    {
        SendGlyphRectStret(out var rect, layout,
            ObjC.Sel("boundingRectForGlyphRange:inTextContainer:"),
            new ObjC.Range(glyph, 1), container);
        return rect;
    }

    /// <summary>
    /// Resolves ribbon text and native selection from one current source frame.
    /// A retained binding owns the input interval, not the current caret; its
    /// original Active must not label a newer same-version frame selection.
    /// Null or mismatched state leaves the installed native host unchanged.
    /// This pure boundary does not call AppKit or settle provisional input.
    /// </summary>
    internal static bool TryGetRibbonContext(NativeCanvasBinding? binding,
        CanvasFrame? frame, out string label, out ObjC.Range selection)
    {
        label = string.Empty;
        selection = default;
        if (binding is null || frame is null || frame.Version != binding.Snapshot.Version)
            return false;
        label = $"Input @ {frame.SelectionActive.ToString("N0", CultureInfo.InvariantCulture)}";
        selection = ProjectedHostSelection(binding, frame);
        return true;
    }

    /// <summary>Projects the current frame without changing the native input interval.</summary>
    private ObjC.Range ProjectedHostSelection() => ProjectedHostSelection(_binding!, _frame!);

    /// <summary>Clamps source selection to the exact retained single-line input slice.</summary>
    private static ObjC.Range ProjectedHostSelection(NativeCanvasBinding binding, CanvasFrame frame)
    {
        var first = Math.Min(frame.SelectionAnchor, frame.SelectionActive);
        var last = Math.Max(frame.SelectionAnchor, frame.SelectionActive);
        var start = Math.Clamp(first - binding.InputSourceStart, 0,
            binding.InputSourceText.Length);
        var end = Math.Clamp(last - binding.InputSourceStart, 0,
            binding.InputSourceText.Length);
        if (start == end)
            start = end = Math.Clamp(frame.SelectionActive - binding.InputSourceStart,
                0, binding.InputSourceText.Length);
        return new ObjC.Range((nuint)start, (nuint)(end - start));
    }

    private void Draw()
    {
        var bindingAtEntry = _binding;
        var frameAtEntry = _frame;
        var ticket = !_setting && bindingAtEntry is not null &&
            frameAtEntry?.Version == bindingAtEntry.BaseVersion &&
            _snapshot?.Version == bindingAtEntry.BaseVersion
            ? _drawTrace?.BeginDraw(bindingAtEntry.DocumentGeneration, bindingAtEntry.BaseVersion) ?? 0 : 0;
        var context = ObjC.Send(ObjC.Send(ObjC.Class("NSGraphicsContext"),
            ObjC.Sel("currentContext")), ObjC.Sel("CGContext"));
        if (context == 0) return;
        var rect = MacOnScreenCanvasNative.GetRect(_view, ObjC.Sel("bounds"));
        if (rect.Size.Height != _height || rect.Size.Width != _width ||
            Math.Abs(_reportedBodyHeight - BodyHeight) > 0.01)
            PublishBodyHeight();
        var palette = _theme?.Palette;
        Fill(context, palette?.EditorBackground ?? new ThemeColor(30, 30, 30),
            new ObjC.Rect(0, 0, _width, _height));
        var ribbon = Math.Min(RibbonHeight, _height);
        Fill(context, palette?.PanelBackground ?? new ThemeColor(37, 37, 38),
            new ObjC.Rect(0, 0, _width, ribbon));
        if (_height > RibbonHeight)
        {
            var border = palette?.Border ?? new ThemeColor(70, 70, 70);
            MacOnScreenCanvasNative.SetStrokeColor(context, border.Red / 255d,
                border.Green / 255d, border.Blue / 255d, 1);
            MacOnScreenCanvasNative.SetLineWidth(context, 1);
            MacOnScreenCanvasNative.MoveToPoint(context, 0, RibbonHeight - 0.5);
            MacOnScreenCanvasNative.AddLineToPoint(context, _width, RibbonHeight - 0.5);
            MacOnScreenCanvasNative.StrokePath(context);
        }
        if (_frame is not { } frame || _snapshot is not { } snapshot || _font == 0 ||
            frame.Version != snapshot.Version) return;
        if (frame.RowWindows.Length != frame.Slices.Count) return;
        var paintedBody = Math.Max(0, _height - RibbonHeight);
        if (paintedBody == 0) return;
        MacOnScreenCanvasNative.SaveState(context);
        try
        {
          MacOnScreenCanvasNative.ClipToRect(context,
              new ObjC.Rect(LeftInset, RibbonHeight,
                  Math.Max(0, _width - LeftInset), paintedBody));
          foreach (var row in frame.RowWindows)
          {
            var slice = row.Slice;
            if (slice.SourceLength > MaxInputLength)
                throw new InvalidOperationException("Canvas row exceeds the bounded shaping interval.");
            var text = snapshot.GetText(slice.SourceStart, slice.SourceLength);
            if (text.Length == 0)
            {
                var emptyOrigin = LeftInset - row.IntraClusterPixels;
                DrawSelection(context, frame, slice, 0, emptyOrigin);
                if (_semantics is { } emptySemantics && emptySemantics.Version == frame.Version)
                    DrawDiagnostics(context, 0, slice, 0, emptyOrigin, emptySemantics);
                DrawSourceCaret(context, frame, slice, 0, emptyOrigin, text);
                continue;
            }
            WithLine(text, line =>
            {
                if (!TryRowOrigin(line, row, text, out var origin)) return;
                var selected = DrawSelection(context, frame, slice, line, origin);
                var baseline = _height - slice.TopY - 4 - (_theme?.Typography.EditorFontSize ?? 13);
                DrawLine(context, line, origin, baseline,
                    palette?.EditorForeground ?? new ThemeColor(216, 218, 223));
                if (_semantics is { } semantic && semantic.Version == frame.Version)
                    DrawSemantic(context, line, slice, origin, baseline, semantic);
                if (selected is { } selectedRect)
                {
                    MacOnScreenCanvasNative.SaveState(context);
                    try
                    {
                        MacOnScreenCanvasNative.ClipToRect(context, selectedRect);
                        DrawLine(context, line, origin, baseline,
                            palette?.SelectionForeground ?? new ThemeColor(255, 255, 255));
                    }
                    finally { MacOnScreenCanvasNative.RestoreState(context); }
                }
                if (_semantics is { } diagnostics && diagnostics.Version == frame.Version)
                    DrawDiagnostics(context, line, slice, origin, baseline, diagnostics);
                DrawSourceCaret(context, frame, slice, line, origin, text);
            });
          }
        }
        finally { MacOnScreenCanvasNative.RestoreState(context); }
        if (ticket != 0 && ReferenceEquals(bindingAtEntry, _binding) && ReferenceEquals(frameAtEntry, _frame))
            _drawTrace?.CompleteDraw(ticket, bindingAtEntry!.DocumentGeneration, bindingAtEntry.BaseVersion);
    }

    private void DrawSemantic(nint context, nint line, ViewportSlice slice,
        double origin, double baseline,
        NativeCanvasSemantics semantics)
    {
        if (_theme is null) return;
        foreach (var token in semantics.Tokens)
        {
            var start = Math.Max(slice.SourceStart, token.Span.Start);
            var end = Math.Min(slice.SourceStart + slice.SourceLength,
                token.Span.Start + token.Span.Length);
            if (token.Span.Start >= slice.SourceStart + slice.SourceLength) break;
            if (start >= end) continue;
            var first = CoreTextNative.OffsetForIndex(line, start - slice.SourceStart, out _);
            var last = CoreTextNative.OffsetForIndex(line, end - slice.SourceStart, out _);
            MacOnScreenCanvasNative.SaveState(context);
            try
            {
                MacOnScreenCanvasNative.ClipToRect(context,
                    new ObjC.Rect(origin + Math.Min(first, last),
                        _height - slice.TopY - slice.Height,
                        Math.Max(1, Math.Abs(last - first)), slice.Height));
                DrawLine(context, line, origin, baseline, _theme.SemanticColor(token.Kind));
            }
            finally { MacOnScreenCanvasNative.RestoreState(context); }
        }
    }

    private void DrawDiagnostics(nint context, nint line, ViewportSlice slice,
        double origin, double baseline, NativeCanvasSemantics semantics)
    {
        if (_theme is null) return;
        var sliceEnd = slice.SourceStart + slice.SourceLength;
        foreach (var diagnostic in semantics.Diagnostics)
        {
            if (diagnostic.Span.Start > sliceEnd) break;
            var start = Math.Max(slice.SourceStart, diagnostic.Span.Start);
            var end = Math.Min(sliceEnd, diagnostic.Span.End);
            if (start >= end && !(diagnostic.Span.Length == 0 &&
                diagnostic.Span.Start >= slice.SourceStart &&
                diagnostic.Span.Start <= sliceEnd)) continue;
            var first = line == 0 ? 0 : CoreTextNative.OffsetForIndex(
                line, start - slice.SourceStart, out _);
            var last = line == 0 ? 0 : CoreTextNative.OffsetForIndex(
                line, end - slice.SourceStart, out _);
            var color = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => _theme.Palette.Error,
                DiagnosticSeverity.Warning => _theme.Palette.Warning,
                _ => _theme.Palette.Info
            };
            MacOnScreenCanvasNative.SetStrokeColor(context, color.Red / 255d,
                color.Green / 255d, color.Blue / 255d, 1);
            MacOnScreenCanvasNative.SetLineWidth(context, 1.5);
            var x = origin + Math.Min(first, last);
            var width = Math.Max(6, Math.Abs(last - first));
            var y = line == 0 ? _height - slice.TopY - slice.Height + 2 : baseline - 2;
            MacOnScreenCanvasNative.MoveToPoint(context, x, y);
            MacOnScreenCanvasNative.AddLineToPoint(context, x + width, y);
            MacOnScreenCanvasNative.StrokePath(context);
        }
    }

    private ObjC.Rect? DrawSelection(nint context, CanvasFrame frame,
        ViewportSlice slice, nint line, double origin)
    {
        var palette = _theme?.Palette;
        var background = palette?.SelectionBackground ?? new ThemeColor(38, 79, 120);
        var start = Math.Max(frame.SelectionStart, slice.SourceStart);
        var end = Math.Min(frame.SelectionStart + frame.SelectionLength,
            slice.SourceStart + slice.SourceLength);
        var y = _height - slice.TopY - slice.Height;
        ObjC.Rect? selected = null;
        if (start < end)
        {
            var first = CoreTextNative.OffsetForIndex(line, start - slice.SourceStart, out _);
            var last = CoreTextNative.OffsetForIndex(line, end - slice.SourceStart, out _);
            selected = new ObjC.Rect(origin + Math.Min(first, last), y,
                Math.Max(1, Math.Abs(last - first)), slice.Height);
            Fill(context, background, selected.Value);
        }
        var snapshot = _snapshot!;
        if (!slice.HasHiddenSuffix && slice.Line + 1 < snapshot.LineCount)
        {
            var delimiterStart = slice.SourceStart + slice.SourceLength;
            var delimiterEnd = snapshot.GetLineStartOffset(slice.Line + 1);
            if (delimiterStart < delimiterEnd && frame.SelectionStart < delimiterEnd &&
                frame.SelectionStart + frame.SelectionLength > delimiterStart)
            {
                var x = origin + (line == 0 ? 0 : CoreTextNative.OffsetForIndex(
                    line, slice.SourceLength, out _));
                Fill(context, background, new ObjC.Rect(x, y, 9, slice.Height));
            }
        }
        return selected;
    }

    private void DrawLine(nint context, nint line, double origin,
        double baseline, ThemeColor color)
    {
        FillColor(context, color);
        MacOnScreenCanvasNative.SetTextMatrix(context,
            new MacOnScreenCanvasNative.Affine(1, 0, 0, 1, 0, 0));
        CoreTextNative.SetTextPosition(context, origin, baseline);
        CoreTextNative.LineDraw(line, context);
    }

    /// <summary>Shows the source caret in the canvas while AppKit owns input in the ribbon.</summary>
    private void DrawSourceCaret(nint context, CanvasFrame frame, ViewportSlice slice,
        nint line, double origin, string text)
    {
        if (frame.SelectionLength != 0 || _theme is null) return;
        var local = frame.SelectionActive - slice.SourceStart;
        if (!IsGraphemeBoundary(text, local) ||
            local == text.Length && slice.HasHiddenSuffix) return;
        var x = origin + (line == 0 ? 0 : CoreTextNative.OffsetForIndex(line, local, out _));
        if (x < LeftInset || x > _width) return;
        var color = _theme.Palette.Cursor;
        MacOnScreenCanvasNative.SetStrokeColor(context, color.Red / 255d,
            color.Green / 255d, color.Blue / 255d, 1);
        MacOnScreenCanvasNative.SetLineWidth(context, 1.5);
        var bottom = _height - slice.TopY - slice.Height + 2;
        MacOnScreenCanvasNative.MoveToPoint(context, x, bottom);
        MacOnScreenCanvasNative.AddLineToPoint(context, x, bottom + slice.Height - 4);
        MacOnScreenCanvasNative.StrokePath(context);
    }

    private int HitSource(double x, double topY)
    {
        if (_frame is not { } frame || _snapshot is not { } snapshot ||
            frame.RowWindows.Length == 0 || topY < 0 ||
            topY >= Math.Max(0, _height - RibbonHeight)) return -1;
        var row = frame.RowWindows[0];
        var found = false;
        foreach (var candidate in frame.RowWindows)
        {
            if (topY < candidate.Slice.TopY ||
                topY >= candidate.Slice.TopY + candidate.Slice.Height) continue;
            row = candidate;
            found = true;
            break;
        }
        if (!found && topY >= row.Slice.TopY) row = frame.RowWindows[^1];
        var slice = row.Slice;
        var text = snapshot.GetText(slice.SourceStart, slice.SourceLength);
        if (text.Length == 0) return slice.SourceStart;
        var local = 0;
        WithLine(text, line =>
        {
            if (!TryRowOrigin(line, row, text, out var origin)) return;
            var index = CoreTextNative.IndexForPosition(line,
                new CoreTextNative.Point(Math.Max(0, x - origin), 0));
            local = checked((int)Math.Clamp(index, 0, text.Length));
        });
        var snapped = local;
        if (!IsAscii(text))
        {
            snapped = 0;
            foreach (var boundary in StringInfo.ParseCombiningCharacters(text))
            {
                if (boundary > local) break;
                snapped = boundary;
            }
            if (local == text.Length) snapped = local;
        }
        var source = slice.SourceStart + snapped;
        if ((source == slice.SourceStart && slice.HasHiddenPrefix) ||
            (source == slice.SourceStart + slice.SourceLength && slice.HasHiddenSuffix))
        {
            var lineStart = snapshot.GetLineStartOffset(slice.Line);
            var lineEnd = slice.Line + 1 < snapshot.LineCount
                ? snapshot.GetLineStartOffset(slice.Line + 1) : snapshot.Length;
            source = SnapGraphemeAt(snapshot, lineStart, lineEnd, source) ??
                frame.SelectionActive;
        }
        if (source > 0 && source < snapshot.Length)
        {
            var pair = snapshot.GetText(source - 1, 2);
            if (char.IsHighSurrogate(pair[0]) && char.IsLowSurrogate(pair[1]) ||
                pair[0] == '\r' && pair[1] == '\n') source--;
        }
        return source;
    }

    /// <summary>Maps one bounded shaped row to the immutable frame's left edge.</summary>
    private static bool TryRowOrigin(nint line, HorizontalRowWindow row, string text,
        out double origin)
    {
        var local = row.LeftEdgeSourceBoundary - row.Slice.SourceStart;
        if (local < 0 || local > row.Slice.SourceLength ||
            !IsGraphemeBoundary(text, local) ||
            !double.IsFinite(row.IntraClusterPixels) || row.IntraClusterPixels < 0)
        {
            origin = 0;
            return false;
        }
        var edge = CoreTextNative.OffsetForIndex(line, local, out _);
        origin = LeftInset - edge - row.IntraClusterPixels;
        return double.IsFinite(origin);
    }

    private static bool IsGraphemeBoundary(string text, int local)
    {
        if (local < 0 || local > text.Length) return false;
        if (local == text.Length) return true;
        if (IsAscii(text)) return true;
        return Array.BinarySearch(StringInfo.ParseCombiningCharacters(text), local) >= 0;
    }

    private static bool IsAscii(string text)
    {
        foreach (var character in text)
            if (character > 0x7f) return false;
        return true;
    }

    /// <summary>Rejects a dual-edge bidirectional caret rather than guessing its visual side.</summary>
    private static bool TryCaretOffset(nint line, int local, out double x)
    {
        x = CoreTextNative.OffsetForIndex(line, local, out var secondary);
        return double.IsFinite(x) && double.IsFinite(secondary) &&
            Math.Abs(x - secondary) < 0.25;
    }

    /// <summary>
    /// Converts one device-independent horizontal scroll step into a source
    /// anchor plus a local cluster residual; no whole-line prefix is shaped.
    /// </summary>
    private void PanHorizontal(double pixels)
    {
        if (!double.IsFinite(pixels) || pixels == 0 || IsComposing ||
            _binding is not { } binding || _frame is not { } frame ||
            _snapshot is not { } snapshot || _font == 0) return;
        var referenceLine = snapshot.GetLineIndexFromOffset(frame.Horizontal.SourceBoundary);
        var target = frame.RowWindows.FirstOrDefault(row => row.Slice.Line == referenceLine);
        if (target.Slice.Height <= 0 && frame.RowWindows.Length > 0)
            target = frame.RowWindows[0];
        foreach (var row in frame.RowWindows)
        {
            if (row != target) continue;
            var slice = row.Slice;
            var text = snapshot.GetText(slice.SourceStart, slice.SourceLength);
            if (text.Length == 0) return;
            WithLine(text, line =>
            {
                var edgeIndex = row.LeftEdgeSourceBoundary - slice.SourceStart;
                if (edgeIndex < 0 || edgeIndex > text.Length ||
                    !TryCaretOffset(line, edgeIndex, out var edge)) return;
                var desired = edge + row.IntraClusterPixels + pixels;
                var end = CoreTextNative.OffsetForIndex(line, text.Length, out _);
                if (!double.IsFinite(end) || end < 0) return;
                if (desired < 0 && slice.HasHiddenPrefix)
                {
                    // Cross the bounded left seam by reanchoring; the next
                    // frame supplies its own local shaping context.
                    var lineStart = snapshot.GetLineStartOffset(slice.Line);
                    var start = Math.Max(lineStart, slice.SourceStart - 128);
                    var previous = snapshot.GetText(start, slice.SourceStart - start);
                    var clusters = StringInfo.ParseCombiningCharacters(previous);
                    if (clusters.Length == 0 || clusters[^1] == 0 && start > lineStart) return;
                    SendHorizontal(binding, start + clusters[^1], 0);
                    return;
                }
                if (desired > end && slice.HasHiddenSuffix)
                {
                    var lineStart = snapshot.GetLineStartOffset(slice.Line);
                    var lineEnd = slice.Line + 1 < snapshot.LineCount
                        ? snapshot.GetLineStartOffset(slice.Line + 1) : snapshot.Length;
                    var safe = SnapGraphemeAt(snapshot, lineStart, lineEnd,
                        slice.SourceStart + text.Length);
                    if (safe is { } boundary) SendHorizontal(binding, boundary, 0);
                    return;
                }
                desired = Math.Clamp(desired, 0, end);
                if (desired >= end - 0.01)
                {
                    var boundary = slice.SourceStart + text.Length;
                    if (slice.HasHiddenSuffix)
                    {
                        var lineStart = snapshot.GetLineStartOffset(slice.Line);
                        var lineEnd = slice.Line + 1 < snapshot.LineCount
                            ? snapshot.GetLineStartOffset(slice.Line + 1) : snapshot.Length;
                        var safe = SnapGraphemeAt(snapshot, lineStart, lineEnd, boundary);
                        if (safe is null) return;
                        boundary = safe.Value;
                    }
                    SendHorizontal(binding, boundary, 0);
                    return;
                }
                var hit = checked((int)Math.Clamp(CoreTextNative.IndexForPosition(line,
                    new CoreTextNative.Point(desired, 0)), 0, text.Length));
                var ascii = IsAscii(text);
                var boundaries = ascii ? null : StringInfo.ParseCombiningCharacters(text);
                var position = ascii ? hit : Array.BinarySearch(boundaries!, hit);
                if (position < 0) position = ~position - 1;
                position = Math.Max(0, position);
                var local = ascii ? position : boundaries![position];
                var x = CoreTextNative.OffsetForIndex(line, local, out var secondary);
                if (!double.IsFinite(x) || !double.IsFinite(secondary) ||
                    Math.Abs(x - secondary) >= 0.25) return;
                if (x > desired && position > 0)
                {
                    --position;
                    local = ascii ? position : boundaries![position];
                    x = CoreTextNative.OffsetForIndex(line, local, out secondary);
                }
                if (!double.IsFinite(x) || Math.Abs(x - secondary) >= 0.25) return;
                var next = ascii ? Math.Min(text.Length, position + 1) :
                    position + 1 < boundaries!.Length ? boundaries![position + 1] : text.Length;
                var nextX = CoreTextNative.OffsetForIndex(line, next, out secondary);
                if (!double.IsFinite(nextX) || Math.Abs(nextX - secondary) >= 0.25 ||
                    nextX < x) return;
                var residual = Math.Clamp(desired - x, 0, Math.Max(0, nextX - x));
                if (next > local && residual >= nextX - x - 0.01)
                {
                    SendHorizontal(binding, slice.SourceStart + next, 0);
                    return;
                }
                if (local == edgeIndex && Math.Abs(residual - row.IntraClusterPixels) < 0.01)
                    return;
                SendHorizontal(binding, slice.SourceStart + local, residual);
            });
            return;
        }
    }

    private void SendHorizontal(NativeCanvasBinding binding, int boundary, double residual) =>
        _horizontal(new CanvasHorizontalAnchorRequest(binding.DocumentGeneration,
            binding.BaseVersion, boundary, HorizontalCaretAffinity.Leading, residual));

    private static int? SnapGraphemeAt(TextSnapshot snapshot, int lineStart,
        int lineEnd, int boundary)
    {
        var start = Math.Max(lineStart, boundary - 128);
        var end = Math.Min(lineEnd, boundary + 128);
        var text = snapshot.GetText(start, end - start);
        var edges = StringInfo.ParseCombiningCharacters(text);
        var local = boundary - start;
        var position = Array.BinarySearch(edges, local);
        if (position < 0) position = ~position - 1;
        if (local == text.Length) return boundary;
        if (position < 0 || (position == 0 && start > lineStart)) return null;
        return start + edges[position];
    }

    private void WithLine(string text, Action<nint> draw)
    {
        var source = CreateString(text);
        var attributed = CoreTextNative.AttributedCreateMutable(0, 0);
        if (attributed == 0)
        {
            CoreTextNative.Release(source);
            throw new InvalidOperationException("CoreText attributed string creation failed.");
        }
        try
        {
            CoreTextNative.AttributedReplace(attributed, new CoreTextNative.Range(0, 0), source);
            CoreTextNative.AttributedSetAttribute(attributed,
                new CoreTextNative.Range(0, text.Length), _fontAttribute, _font);
            CoreTextNative.AttributedSetAttribute(attributed,
                new CoreTextNative.Range(0, text.Length), _contextColorAttribute, _trueValue);
            var line = CoreTextNative.LineCreate(attributed);
            if (line == 0) throw new InvalidOperationException("CoreText line creation failed.");
            try { draw(line); }
            finally { CoreTextNative.Release(line); }
        }
        finally
        {
            CoreTextNative.Release(attributed);
            CoreTextNative.Release(source);
        }
    }

    private static nint CreateString(string text)
    {
        var chars = Marshal.StringToHGlobalUni(text);
        try
        {
            var value = CoreTextNative.StringCreate(0, chars, text.Length);
            return value != 0 ? value : throw new InvalidOperationException("CFString creation failed.");
        }
        finally { Marshal.FreeHGlobal(chars); }
    }

    private static nint Color(ThemeColor color) => ObjC.Send(ObjC.Class("NSColor"),
        ObjC.Sel("colorWithSRGBRed:green:blue:alpha:"),
        color.Red / 255d, color.Green / 255d, color.Blue / 255d, 1d);

    private static void FillColor(nint context, ThemeColor color) =>
        CoreTextNative.SetFillColor(context, color.Red / 255d, color.Green / 255d,
            color.Blue / 255d, 1);

    private static void Fill(nint context, ThemeColor color, ObjC.Rect rect)
    {
        FillColor(context, color);
        MacOnScreenCanvasNative.FillRect(context, rect);
    }

    private void Invalidate()
    {
        if (_view != 0) ObjC.Send(_view, ObjC.Sel("setNeedsDisplay:"), 1);
    }

    private void Mouse(nint eventObject, bool extend)
    {
        if (IsComposing) return;
        var window = MacOnScreenCanvasNative.SendPoint(eventObject, ObjC.Sel("locationInWindow"));
        var local = MacOnScreenCanvasNative.SendPoint(_view,
            ObjC.Sel("convertPoint:fromView:"), window, 0);
        var source = HitSource(local.X, _height - local.Y);
        if (source < 0) return;
        if (!extend) _dragAnchor = source;
        _selection(_dragAnchor, source);
    }

    private void PastePlain()
    {
        if (_binding is null || IsComposing)
        {
            _error("Finish text composition before pasting into the canvas.");
            return;
        }
        var board = ObjC.Send(ObjC.Class("NSPasteboard"), ObjC.Sel("generalPasteboard"));
        var value = ObjC.Send(board, ObjC.Sel("stringForType:"),
            ObjC.String("public.utf8-plain-text"));
        if (value == 0)
            value = ObjC.Send(board, ObjC.Sel("stringForType:"),
                ObjC.String("NSStringPboardType"));
        if (value == 0)
        {
            _error("The clipboard has no plain text to paste.");
            return;
        }
        var length = checked((int)ObjC.Send(value, ObjC.Sel("length")));
        var current = checked((int)ObjC.Send(ObjC.Send(_editor, ObjC.Sel("string")),
            ObjC.Sel("length")));
        var selected = ObjC.SendRange(_editor, ObjC.Sel("selectedRange"));
        if (selected.Location > (nuint)current ||
            selected.Length > (nuint)current - selected.Location)
            throw new InvalidOperationException("The native selection is outside its input window.");
        if ((long)current - (long)selected.Length + length > MaxInputLength)
        {
            var frame = _frame ?? throw new InvalidOperationException("The canvas frame is unavailable.");
            var globalSelected = frame.SelectionLength > 0;
            var start = globalSelected ? frame.SelectionStart :
                _binding.InputSourceStart + checked((int)selected.Location);
            var delete = globalSelected ? frame.SelectionLength : checked((int)selected.Length);
            if ((long)_binding.Snapshot.Length - delete + length > int.MaxValue)
            {
                _error("The clipboard text exceeds the maximum document length; no text was inserted.");
                return;
            }
            var plain = ObjC.ManagedString(value);
            EmitDirect(new TextChange(start, delete, plain), start + plain.Length);
            return;
        }
        ObjC.Send(_editor, ObjC.Sel("insertText:replacementRange:"), value,
            new ObjC.Range(nuint.MaxValue, 0));
    }

    private void InvokeSafely(Action action)
    {
        if (_reportedFailure) return;
        try { action(); }
        catch (Exception error)
        {
            DisableAfterFailure(error.Message);
        }
    }

    /// <summary>Fails closed so an editable OS buffer never diverges silently from the engine.</summary>
    internal void DisableAfterFailure(string reason)
    {
        if (_reportedFailure) return;
        TraceStage("Q0-disable-input-enter");
        _reportedFailure = true;
        _binding = null;
        _projection = null;
        try
        {
            if (_editor != 0) ObjC.Send(_editor, ObjC.Sel("setEditable:"), 0);
            if (_editor != 0)
            {
                _setting = true;
                try { ObjC.Send(_editor, ObjC.Sel("setString:"), ObjC.String(string.Empty)); }
                finally { _setting = false; }
            }
            if (_hostScroll != 0) ObjC.Send(_hostScroll, ObjC.Sel("setHidden:"), 0);
            SetRibbonLabel("Input stopped");
            Invalidate();
        }
        catch { /* Keep callback unwind safe even if AppKit is already failing. */ }
        try { _error($"The experimental canvas input stopped: {reason}"); }
        catch { /* Never unwind a managed exception through an AppKit IMP. */ }
        TraceStage("Q1-disable-input-returned");
    }

    private static string RegisterClass()
    {
        var cls = ObjC.AllocateClassPair(ObjC.Class("NSView"), ViewClass, 0);
        if (cls == 0) return ViewClass;
        Add(cls, "drawRect:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, ObjC.Rect, void>)&DrawRect,
            "v@:{CGRect={CGPoint=dd}{CGSize=dd}}");
        Add(cls, "viewDidChangeEffectiveAppearance",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&ViewAppearanceChanged,
            "v@:");
        Add(cls, "scrollWheel:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&ScrollWheel, "v@:@");
        Add(cls, "mouseDown:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MouseDown, "v@:@");
        Add(cls, "mouseDragged:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MouseDragged, "v@:@");
        ObjC.RegisterClassPair(cls);
        return ViewClass;
    }

    private static string RegisterInputClass()
    {
        var cls = ObjC.AllocateClassPair(ObjC.Class("NSTextView"), InputClass, 0);
        if (cls == 0) return InputClass;
        Add(cls, "viewDidChangeEffectiveAppearance",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&InputAppearanceChanged,
            "v@:");
        Add(cls, "paste:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Paste, "v@:@");
        Add(cls, "pasteAsPlainText:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Paste, "v@:@");
        Add(cls, "keyDown:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&InputKeyDown, "v@:@");
        Add(cls, "mouseDown:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&InputMouseDown, "v@:@");
        Add(cls, "mouseDragged:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&InputMouseDragged, "v@:@");
        Add(cls, "scrollWheel:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&ScrollWheel, "v@:@");
        ObjC.RegisterClassPair(cls);
        return InputClass;
    }

    private static string RegisterHostScrollClass()
    {
        var cls = ObjC.AllocateClassPair(ObjC.Class("NSScrollView"), HostScrollClass, 0);
        if (cls == 0) return HostScrollClass;
        Add(cls, "scrollWheel:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&ScrollWheel, "v@:@");
        ObjC.RegisterClassPair(cls);
        return HostScrollClass;
    }

    private static void ForwardInputEvent(nint self, nint selector, nint eventObject)
    {
        var current = s_current;
        if (current is null) return;
        var key = s_traceStages && selector == ObjC.Sel("keyDown:");
        if (key) TraceStage("K0-key-down-enter");
        current.InvokeSafely(() =>
        {
            current.ArmUserSelectionGesture();
            var superclass = new MacOnScreenCanvasNative.Super(self, ObjC.Class("NSTextView"));
            if (key) TraceStage("K1-before-appkit-key-down");
            MacOnScreenCanvasNative.SendSuper(ref superclass, selector, eventObject);
            if (key) TraceStage("K2-appkit-key-down-returned");
        });
    }

    private static void Add(nint cls, string selector, nint implementation, string encoding)
    {
        if (!ObjC.AddMethod(cls, ObjC.Sel(selector), implementation, encoding))
            throw new InvalidOperationException($"Could not register canvas {selector}.");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DrawRect(nint self, nint selector, ObjC.Rect dirty)
    { var current = s_current; current?.InvokeSafely(current.Draw); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ViewAppearanceChanged(nint self, nint selector)
    {
        try
        {
            var superclass = new MacOnScreenCanvasNative.Super(self, ObjC.Class("NSView"));
            SendSuperNoArgument(ref superclass, selector);
            s_current?.EffectiveAppearanceChanged?.Invoke();
        }
        catch { /* Appearance failures must not disable text input or unwind an AppKit IMP. */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void InputAppearanceChanged(nint self, nint selector)
    {
        try
        {
            // This post-propagation child callback backs up the canvas view's
            // callback. The shell classifies the canvas renderer's appearance
            // and deduplicates both notifications without changing text input.
            var superclass = new MacOnScreenCanvasNative.Super(self, ObjC.Class("NSTextView"));
            SendSuperNoArgument(ref superclass, selector);
            s_current?.EffectiveAppearanceChanged?.Invoke();
        }
        catch { /* Never unwind managed code through an AppKit IMP. */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ScrollWheel(nint self, nint selector, nint eventObject)
    {
        var current = s_current;
        if (current is null || current.IsComposing || current.BodyHeight <= 0) return;
        current.InvokeSafely(() =>
        {
            var delta = MacOnScreenCanvasNative.SendDouble(eventObject,
                ObjC.Sel("scrollingDeltaY"));
            var horizontal = MacOnScreenCanvasNative.SendDouble(eventObject,
                ObjC.Sel("scrollingDeltaX"));
            var precise = ObjC.Send(eventObject, ObjC.Sel("hasPreciseScrollingDeltas")) != 0;
            var lineHeight = Math.Max(1, (current._theme?.Typography.EditorFontSize ?? 13) *
                (current._theme?.Typography.LineHeightMultiplier ?? 1.4));
            var scale = precise ? 1 : lineHeight;
            if (horizontal != 0) current.PanHorizontal(-horizontal * scale);
            if (delta != 0) current._scroll(-delta * scale);
        });
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MouseDown(nint self, nint selector, nint eventObject)
    { var current = s_current; current?.InvokeSafely(() => current.Mouse(eventObject, false)); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MouseDragged(nint self, nint selector, nint eventObject)
    { var current = s_current; current?.InvokeSafely(() => current.Mouse(eventObject, true)); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Paste(nint self, nint selector, nint sender)
    { var current = s_current; current?.InvokeSafely(current.PastePlain); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void InputKeyDown(nint self, nint selector, nint eventObject)
    { ForwardInputEvent(self, selector, eventObject); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void InputMouseDown(nint self, nint selector, nint eventObject)
    { ForwardInputEvent(self, selector, eventObject); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void InputMouseDragged(nint self, nint selector, nint eventObject)
    { ForwardInputEvent(self, selector, eventObject); }
}
