using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Engine;
using Mote.Formats;
using Mote.Native.Viewport;
using Mote.Themes;

namespace Mote.Native.Mac.Canvas;

/// <summary>
/// A source-backed AppKit canvas with one visible, bounded NSTextView input host.
/// The engine snapshot owns all off-host text; marked text remains in AppKit until commit.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacTextInputIsland
{
    private const string ViewClass = "MoteInteractiveCanvasView";
    private const string InputClass = "MoteInteractiveCanvasInputView";
    private const int MaxInputLength = 16 * 1024;
    private const double LeftInset = 12;
    private static MacTextInputIsland? s_current;

    private readonly Action<CanvasCommittedEdit> _edit;
    private readonly Action<double> _scroll;
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
    private bool _pendingResize;
    private int _dragAnchor;
    private bool _reportedFailure;
    private bool _userSelectionPending;
    private int _selectionRepairCount;
    private int _selectionNotifications;
    private int _selectionEchoes;
    private int _selectionUserEvents;
    private string _lastSelectionOutcome = "none";

    /// <summary>Creates callbacks that keep document mutations in the controller.</summary>
    internal MacTextInputIsland(Action<CanvasCommittedEdit> edit, Action<double> scroll,
        Action<double> resize, Action<int, int> selection, Action<string> error)
    {
        _edit = edit;
        _scroll = scroll;
        _resize = resize;
        _selection = selection;
        _error = error;
    }

    /// <summary>The real visible NSTextView receiving AppKit text input.</summary>
    internal nint Editor => _editor;

    /// <summary>Whether the OS currently owns provisional candidate text.</summary>
    internal bool IsComposing => _editor != 0 && ObjC.Send(_editor, ObjC.Sel("hasMarkedText")) != 0;

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
        if (_font != 0) CoreTextNative.Release(_font);
        _font = 0;
        _font = 0;
    }

    /// <summary>Creates the canvas and bounded input host as one left-pane view.</summary>
    internal nint CreateView(ObjC.Rect frame)
    {
        if (s_current is not null) throw new InvalidOperationException("Only one interactive canvas may exist.");
        s_current = this;
        _width = frame.Size.Width;
        _height = frame.Size.Height;
        _view = ObjC.Send(ObjC.Send(ObjC.Class(RegisterClass()), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), frame);
        if (_view == 0) throw new InvalidOperationException("AppKit could not create the canvas.");
        ObjC.Send(_view, ObjC.Sel("setAutoresizingMask:"), (nint)18);
        _hostScroll = ObjC.Send(ObjC.Send(ObjC.Class("NSScrollView"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(0, 0, _width, 24));
        ObjC.Send(_hostScroll, ObjC.Sel("setBorderType:"), 0);
        // The clip view still scrolls the caret into view programmatically;
        // a scrollbar would consume almost the entire one-row input island.
        ObjC.Send(_hostScroll, ObjC.Sel("setHasHorizontalScroller:"), 0);
        ObjC.Send(_hostScroll, ObjC.Sel("setAutohidesScrollers:"), 1);
        ObjC.Send(_hostScroll, ObjC.Sel("setAutoresizingMask:"), (nint)2);
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
        ObjC.Send(container, ObjC.Sel("setContainerSize:"), new ObjC.Size(1_000_000_000, 24));
        ObjC.Send(container, ObjC.Sel("setWidthTracksTextView:"), 0);
        ObjC.Send(_editor, ObjC.Sel("setAutomaticQuoteSubstitutionEnabled:"), 0);
        ObjC.Send(_editor, ObjC.Sel("setAutomaticDashSubstitutionEnabled:"), 0);
        ObjC.Send(_editor, ObjC.Sel("setAutomaticTextReplacementEnabled:"), 0);
        ObjC.Send(_editor, ObjC.Sel("setContinuousSpellCheckingEnabled:"), 0);
        ObjC.Send(_hostScroll, ObjC.Sel("setDocumentView:"), _editor);
        ObjC.Send(_view, ObjC.Sel("addSubview:"), _hostScroll);
        return _view;
    }

    /// <summary>
    /// Replaces the host's one-line source window. Reentrant controller bindings
    /// are applied immediately after textDidChange returns, before another OS input.
    /// </summary>
    internal void Bind(NativeCanvasBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (_reportedFailure)
            throw new InvalidOperationException("The canvas input host failed; reopen the editor.");
        if (binding.BaseVersion != binding.Snapshot.Version ||
            binding.Frame.Version != binding.Snapshot.Version ||
            binding.InputSourceText.Length > MaxInputLength ||
            binding.InputSourceStart < 0 ||
            binding.InputSourceStart > binding.Snapshot.Length - binding.InputSourceText.Length ||
            binding.InputSourceText.IndexOfAny(['\r', '\n']) >= 0 ||
            !string.Equals(binding.Snapshot.GetText(binding.InputSourceStart,
                binding.InputSourceText.Length), binding.InputSourceText, StringComparison.Ordinal))
            throw new ArgumentException("Input binding must be an exact bounded single-line source slice.",
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
            PlaceHost();
        }
        finally { _setting = false; }
        Invalidate();
    }

    /// <summary>
    /// Shows an immutable source snapshot without a text host when no safe
    /// bounded grapheme input window exists. Old native callbacks become inert.
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
        if (_hostScroll != 0) ObjC.Send(_hostScroll, ObjC.Sel("setHidden:"), 1);
        Invalidate();
    }

    /// <summary>Updates paint and global selection without touching native text.</summary>
    internal void SetFrame(CanvasFrame frame)
    {
        if (IsComposing) return;
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
        _theme = theme;
        if (_editor == 0 || IsComposing) return;
        if (_font != 0) CoreTextNative.Release(_font);
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
        var font = ObjC.Send(ObjC.Class("NSFont"), ObjC.Sel("monospacedSystemFontOfSize:weight:"),
            theme.Typography.EditorFontSize, 0d);
        ObjC.Send(_editor, ObjC.Sel("setFont:"), font);
        ObjC.Send(_editor, ObjC.Sel("setTextColor:"), Color(theme.Palette.EditorForeground));
        ObjC.Send(_editor, ObjC.Sel("setBackgroundColor:"), Color(theme.Palette.EditorBackground));
        ObjC.Send(_editor, ObjC.Sel("setInsertionPointColor:"), Color(theme.Palette.Cursor));
        PlaceHost();
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
        _resize(_height);
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
        var final = ObjC.ManagedString(ObjC.Send(_editor, ObjC.Sel("string")));
        var change = _projection.Difference(final);
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
            _edit(new CanvasCommittedEdit(binding.DocumentGeneration, binding.BaseVersion,
                binding.BindingNonce, new TextChange(start, delete, insert), active));
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

    private void PlaceHost(bool preserveComposition = false)
    {
        if (_editor == 0 || _binding is null || _frame is null ||
            (IsComposing && !preserveComposition) ||
            _frame.Version != _binding.Snapshot.Version) return;
        var line = _binding.Snapshot.GetLineIndexFromOffset(_binding.InputSourceStart);
        foreach (var slice in _frame.Slices)
        {
            if (slice.Line != line) continue;
            var height = Math.Max(20, slice.Height);
            ObjC.Send(_hostScroll, ObjC.Sel("setFrame:"),
                new ObjC.Rect(0, _height - slice.TopY - height, _width, height));
            ObjC.Send(_hostScroll, ObjC.Sel("setHidden:"), 0);
            // Keep the actual NSTextView/candidate rect attached on resize;
            // marked text and selection remain entirely owned by AppKit.
            if (IsComposing) return;
            var projected = ProjectedHostSelection();
            _setting = true;
            try
            {
                ObjC.Send(_editor, ObjC.Sel("setSelectedRange:"), projected);
                ObjC.Send(_editor, ObjC.Sel("scrollRangeToVisible:"),
                    new ObjC.Range(projected.Location + projected.Length, 0));
            }
            finally { _setting = false; }
            return;
        }
        ObjC.Send(_hostScroll, ObjC.Sel("setHidden:"), 1);
    }

    private ObjC.Range ProjectedHostSelection()
    {
        var frame = _frame!;
        var binding = _binding!;
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
        var context = ObjC.Send(ObjC.Send(ObjC.Class("NSGraphicsContext"),
            ObjC.Sel("currentContext")), ObjC.Sel("CGContext"));
        if (context == 0) return;
        var rect = MacOnScreenCanvasNative.GetRect(_view, ObjC.Sel("bounds"));
        if (rect.Size.Height > 0 && rect.Size.Width > 0 &&
            (rect.Size.Height != _height || rect.Size.Width != _width))
        {
            _height = rect.Size.Height;
            _width = rect.Size.Width;
            if (IsComposing) _pendingResize = true;
            else _resize(_height);
            PlaceHost(preserveComposition: true);
        }
        var palette = _theme?.Palette;
        Fill(context, palette?.EditorBackground ?? new ThemeColor(30, 30, 30),
            new ObjC.Rect(0, 0, _width, _height));
        if (_frame is not { } frame || _snapshot is not { } snapshot || _font == 0 ||
            frame.Version != snapshot.Version) return;
        foreach (var slice in frame.Slices)
        {
            if (slice.SourceLength > MaxInputLength)
                throw new InvalidOperationException("Canvas row exceeds the bounded shaping interval.");
            // The opaque native input host paints its own caret row and is the
            // only text-input/accessibility owner for that bounded source span.
            if (_binding is { } binding &&
                slice.Line == snapshot.GetLineIndexFromOffset(binding.InputSourceStart) &&
                ObjC.Send(_hostScroll, ObjC.Sel("isHidden")) == 0) continue;
            var text = snapshot.GetText(slice.SourceStart, slice.SourceLength);
            if (text.Length == 0)
            {
                DrawSelection(context, frame, slice, 0);
                if (_semantics is { } emptySemantics && emptySemantics.Version == frame.Version)
                    DrawDiagnostics(context, 0, slice, 0, emptySemantics);
                continue;
            }
            WithLine(text, line =>
            {
                var selected = DrawSelection(context, frame, slice, line);
                var baseline = _height - slice.TopY - 4 - (_theme?.Typography.EditorFontSize ?? 13);
                DrawLine(context, line, baseline,
                    palette?.EditorForeground ?? new ThemeColor(216, 218, 223));
                if (_semantics is { } semantic && semantic.Version == frame.Version)
                    DrawSemantic(context, line, slice, baseline, semantic);
                if (selected is { } selectedRect)
                {
                    MacOnScreenCanvasNative.SaveState(context);
                    try
                    {
                        MacOnScreenCanvasNative.ClipToRect(context, selectedRect);
                        DrawLine(context, line, baseline,
                            palette?.SelectionForeground ?? new ThemeColor(255, 255, 255));
                    }
                    finally { MacOnScreenCanvasNative.RestoreState(context); }
                }
                if (_semantics is { } diagnostics && diagnostics.Version == frame.Version)
                    DrawDiagnostics(context, line, slice, baseline, diagnostics);
            });
        }
    }

    private void DrawSemantic(nint context, nint line, ViewportSlice slice, double baseline,
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
                    new ObjC.Rect(LeftInset + Math.Min(first, last),
                        _height - slice.TopY - slice.Height,
                        Math.Max(1, Math.Abs(last - first)), slice.Height));
                DrawLine(context, line, baseline, _theme.SemanticColor(token.Kind));
            }
            finally { MacOnScreenCanvasNative.RestoreState(context); }
        }
    }

    private void DrawDiagnostics(nint context, nint line, ViewportSlice slice,
        double baseline, NativeCanvasSemantics semantics)
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
            var x = LeftInset + Math.Min(first, last);
            var width = Math.Max(6, Math.Abs(last - first));
            var y = line == 0 ? _height - slice.TopY - slice.Height + 2 : baseline - 2;
            MacOnScreenCanvasNative.MoveToPoint(context, x, y);
            MacOnScreenCanvasNative.AddLineToPoint(context, x + width, y);
            MacOnScreenCanvasNative.StrokePath(context);
        }
    }

    private ObjC.Rect? DrawSelection(nint context, CanvasFrame frame,
        ViewportSlice slice, nint line)
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
            selected = new ObjC.Rect(LeftInset + Math.Min(first, last), y,
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
                var x = LeftInset + (line == 0 ? 0 : CoreTextNative.OffsetForIndex(
                    line, slice.SourceLength, out _));
                Fill(context, background, new ObjC.Rect(x, y, 9, slice.Height));
            }
        }
        return selected;
    }

    private void DrawLine(nint context, nint line, double baseline, ThemeColor color)
    {
        FillColor(context, color);
        MacOnScreenCanvasNative.SetTextMatrix(context,
            new MacOnScreenCanvasNative.Affine(1, 0, 0, 1, 0, 0));
        CoreTextNative.SetTextPosition(context, LeftInset, baseline);
        CoreTextNative.LineDraw(line, context);
    }

    private int HitSource(double x, double topY)
    {
        if (_frame is not { } frame || _snapshot is not { } snapshot ||
            frame.Slices.Count == 0) return 0;
        var slice = frame.Slices[0];
        var found = false;
        foreach (var row in frame.Slices)
        {
            if (topY < row.TopY || topY >= row.TopY + row.Height) continue;
            slice = row;
            found = true;
            break;
        }
        if (!found && topY >= slice.TopY) slice = frame.Slices[^1];
        var text = snapshot.GetText(slice.SourceStart, slice.SourceLength);
        if (text.Length == 0) return slice.SourceStart;
        var local = 0;
        WithLine(text, line =>
        {
            var index = CoreTextNative.IndexForPosition(line,
                new CoreTextNative.Point(Math.Max(0, x - LeftInset), 0));
            local = checked((int)Math.Clamp(index, 0, text.Length));
        });
        var snapped = 0;
        foreach (var boundary in StringInfo.ParseCombiningCharacters(text))
        {
            if (boundary > local) break;
            snapped = boundary;
        }
        if (local == text.Length) snapped = local;
        var source = slice.SourceStart + snapped;
        if (source > 0 && source < snapshot.Length)
        {
            var pair = snapshot.GetText(source - 1, 2);
            if (char.IsHighSurrogate(pair[0]) && char.IsLowSurrogate(pair[1]) ||
                pair[0] == '\r' && pair[1] == '\n') source--;
        }
        return source;
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
        _reportedFailure = true;
        _binding = null;
        _projection = null;
        try
        {
            if (_editor != 0) ObjC.Send(_editor, ObjC.Sel("setEditable:"), 0);
            if (_hostScroll != 0) ObjC.Send(_hostScroll, ObjC.Sel("setHidden:"), 1);
            Invalidate();
        }
        catch { /* Keep callback unwind safe even if AppKit is already failing. */ }
        try { _error($"The experimental canvas input stopped: {reason}"); }
        catch { /* Never unwind a managed exception through an AppKit IMP. */ }
    }

    private static string RegisterClass()
    {
        var cls = ObjC.AllocateClassPair(ObjC.Class("NSView"), ViewClass, 0);
        if (cls == 0) return ViewClass;
        Add(cls, "drawRect:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, ObjC.Rect, void>)&DrawRect,
            "v@:{CGRect={CGPoint=dd}{CGSize=dd}}");
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
        ObjC.RegisterClassPair(cls);
        return InputClass;
    }

    private static void ForwardInputEvent(nint self, nint selector, nint eventObject)
    {
        var current = s_current;
        if (current is null) return;
        current.InvokeSafely(() =>
        {
            current.ArmUserSelectionGesture();
            var superclass = new MacOnScreenCanvasNative.Super(self, ObjC.Class("NSTextView"));
            MacOnScreenCanvasNative.SendSuper(ref superclass, selector, eventObject);
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
    private static void ScrollWheel(nint self, nint selector, nint eventObject)
    {
        var current = s_current;
        if (current is null || current.IsComposing) return;
        current.InvokeSafely(() =>
        {
            var delta = MacOnScreenCanvasNative.SendDouble(eventObject,
                ObjC.Sel("scrollingDeltaY"));
            var precise = ObjC.Send(eventObject, ObjC.Sel("hasPreciseScrollingDeltas")) != 0;
            var lineHeight = Math.Max(1, (current._theme?.Typography.EditorFontSize ?? 13) *
                (current._theme?.Typography.LineHeightMultiplier ?? 1.4));
            current._scroll(-delta * (precise ? 1 : lineHeight));
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
