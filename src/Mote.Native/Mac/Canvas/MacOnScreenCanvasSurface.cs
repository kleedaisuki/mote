using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Native.Viewport;
using Mote.Themes;

namespace Mote.Native.Mac.Canvas;

/// <summary>
/// One visible, read-only AppKit canvas backed solely by immutable engine slices.
/// Native draw, wheel and pointer callbacks all use the same CanvasInteraction.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacOnScreenCanvasSurface
{
    private const int WindowWidth = 1024;
    private const int WindowHeight = 640;
    private const int MaxSliceLength = 16 * 1024;
    private const double LeftInset = 12;
    private static MacOnScreenCanvasSurface? s_current;

    private readonly IThemePolicy _theme;
    private CanvasInteraction _interaction;
    private Action<MacOnScreenCanvasSurface>? _workflow;
    private Exception? _failure;
    private nint _application;
    private nint _window;
    private nint _view;
    private nint _font;
    private nint _fontAttribute;
    private nint _foregroundFromContextAttribute;
    private nint _trueValue;
    private bool _capturing;
    private int _screenDrawCount;
    private int _hitTests;
    private int _roundTrips;
    private bool _stopping;
    private string _resolvedFont = string.Empty;
    private double _displayScale = 1;

    /// <summary>Constructs a fixed-size viewport with a strict 16 Ki-unit shaping bound.</summary>
    internal MacOnScreenCanvasSurface(CanvasInteraction interaction, IThemePolicy theme)
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    /// <summary>Current shared scroll and selection model.</summary>
    internal CanvasInteraction Interaction => _interaction;

    /// <summary>Number of real visible-window drawRect callbacks, excluding PNG recapture.</summary>
    internal int ScreenDrawCount => _screenDrawCount;

    /// <summary>Number of native CoreText hit tests used by pointer gestures.</summary>
    internal int HitTestCount => _hitTests;

    /// <summary>Exact source→CoreText caret X→pointer source checks completed.</summary>
    internal int RoundTripCount => _roundTrips;

    /// <summary>PostScript name resolved by CoreText after system font fallback.</summary>
    internal string ResolvedFont => _resolvedFont;

    /// <summary>Backing pixels per AppKit point on the window's current screen.</summary>
    internal double DisplayScale => _displayScale;

    /// <summary>Number of paint callbacks from the visible window.</summary>
    internal int VisibleDrawCount => _screenDrawCount;

    /// <summary>Runs a visible top-level NSWindow and executes the probe on its AppKit run loop.</summary>
    internal void Run(Action<MacOnScreenCanvasSurface> workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (s_current is not null) throw new InvalidOperationException("A canvas probe is already running.");
        s_current = this;
        _workflow = workflow;
        ObjC.ApplicationLoad();
        var pool = ObjC.New("NSAutoreleasePool");
        try
        {
            _application = ObjC.Send(ObjC.Class("NSApplication"), ObjC.Sel("sharedApplication"));
            ObjC.Send(_application, ObjC.Sel("setActivationPolicy:"), 0);
            CreateFont();
            CreateWindow();
            ObjC.Send(_window, ObjC.Sel("makeKeyAndOrderFront:"), 0);
            ObjC.Send(_application, ObjC.Sel("activateIgnoringOtherApps:"), 1);
            var screen = ObjC.Send(_window, ObjC.Sel("screen"));
            if (screen != 0)
                _displayScale = MacOnScreenCanvasNative.SendDouble(screen,
                    ObjC.Sel("backingScaleFactor"));
            ObjC.Send(_view, ObjC.Sel("performSelectorOnMainThread:withObject:waitUntilDone:"),
                ObjC.Sel("moteRunCanvasWorkflow:"), 0, 0);
            ObjC.Send(_application, ObjC.Sel("run"));
            if (_failure is not null) throw new InvalidOperationException("Visible canvas probe failed.", _failure);
        }
        finally
        {
            s_current = null;
            if (_window != 0)
            {
                ObjC.Send(_window, ObjC.Sel("close"));
                ObjC.Send(_window, ObjC.Sel("release"));
            }
            if (_font != 0) CoreTextNative.Release(_font);
            ObjC.Send(pool, ObjC.Sel("release"));
        }
    }

    /// <summary>Rebinds the same native view to another immutable document session.</summary>
    internal void SetInteraction(CanvasInteraction interaction)
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        Invalidate();
    }

    /// <summary>Uses the exact wheel/trackpad mechanism shared by the AppKit handler.</summary>
    internal void SimulateWheel(double deltaY, bool precise) => HandleWheel(deltaY, precise);

    /// <summary>Uses the exact pointer-hit path shared by the AppKit drag handler.</summary>
    internal void SimulateDrag(double startX, double startTopY, double endX, double endTopY)
    {
        BeginPointer(startX, startTopY);
        ExtendPointer(endX, endTopY);
        _interaction.EndSelection();
        Invalidate();
    }

    /// <summary>Starts a pointer drag before a separate wheel event changes visible rows.</summary>
    internal void SimulatePointerDown(double x, double topY) => BeginPointer(x, topY);

    /// <summary>Extends a pointer drag after a scroll while retaining its source anchor.</summary>
    internal void SimulatePointerDrag(double x, double topY) => ExtendPointer(x, topY);

    /// <summary>Ends synthetic pointer capture through the same interaction state.</summary>
    internal void SimulatePointerUp() { _interaction.EndSelection(); Invalidate(); }

    /// <summary>Checks one visible source offset through the same pointer hit tester.</summary>
    internal void ProbeRoundTrip(int sourceOffset)
    {
        var frame = _interaction.Frame();
        var slice = frame.Slices.FirstOrDefault(row =>
            sourceOffset >= row.SourceStart && sourceOffset <= row.SourceStart + row.SourceLength);
        if (slice.SourceLength == 0)
            throw new InvalidOperationException("Round-trip source offset is not visible.");
        var text = _interaction.Snapshot.GetText(slice.SourceStart, slice.SourceLength);
        var x = 0d;
        WithLine(text, line => x = CoreTextNative.OffsetForIndex(line,
            sourceOffset - slice.SourceStart, out _));
        var hit = HitSource(LeftInset + x, slice.TopY + slice.Height / 2);
        if (hit != sourceOffset)
            throw new InvalidOperationException("CoreText pointer hit does not return its source caret.");
        _roundTrips++;
    }

    /// <summary>Forces a visible-window drawRect, not merely an offscreen line draw.</summary>
    internal void Display()
    {
        Invalidate();
        ObjC.Send(_view, ObjC.Sel("displayIfNeeded"));
    }

    /// <summary>
    /// Draws the already displayed NSView into an AppKit bitmap representation
    /// and writes a PNG without Screen Recording privileges.
    /// </summary>
    internal void CapturePng(string path)
    {
        var rect = new ObjC.Rect(0, 0, WindowWidth, WindowHeight);
        var bitmap = ObjC.Send(_view, ObjC.Sel("bitmapImageRepForCachingDisplayInRect:"), rect);
        if (bitmap == 0) throw new IOException("AppKit could not allocate a view bitmap.");
        _capturing = true;
        try
        {
            MacOnScreenCanvasNative.Send(_view, ObjC.Sel("cacheDisplayInRect:toBitmapImageRep:"),
                rect, bitmap);
        }
        finally { _capturing = false; }
        var properties = ObjC.Send(ObjC.Class("NSDictionary"), ObjC.Sel("dictionary"));
        // NSBitmapImageFileTypePNG = 4.
        var png = ObjC.Send(bitmap, ObjC.Sel("representationUsingType:properties:"), 4, properties);
        if (png == 0 || ObjC.Send(png, ObjC.Sel("writeToFile:atomically:"),
            ObjC.String(path), (byte)1) == 0)
            throw new IOException("AppKit could not write a canvas PNG.");
        var header = new byte[8];
        using var file = File.OpenRead(path);
        file.ReadExactly(header);
        if (!header.AsSpan().SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new IOException("AppKit view capture is not a PNG.");
    }

    private void CreateFont()
    {
        var families = _theme.Typography.EditorFontFamilies.Split(',',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var family = families.FirstOrDefault(static name =>
            name.Equals("Menlo", StringComparison.OrdinalIgnoreCase)) ??
            families.FirstOrDefault() ?? "Menlo";
        _resolvedFont = family;
        var name = CreateString(family);
        try { _font = CoreTextNative.FontCreate(name, _theme.Typography.EditorFontSize, 0); }
        finally { CoreTextNative.Release(name); }
        _fontAttribute = CoreTextNative.FontAttributeName;
        _foregroundFromContextAttribute =
            MacOnScreenCanvasNative.ForegroundColorFromContextAttributeName;
        _trueValue = MacOnScreenCanvasNative.BooleanTrue;
        if (_font == 0 || _fontAttribute == 0 ||
            _foregroundFromContextAttribute == 0 || _trueValue == 0)
            throw new InvalidOperationException("The native theme font is unavailable.");
        var postScript = MacOnScreenCanvasNative.FontCopyPostScriptName(_font);
        if (postScript != 0)
        {
            try { _resolvedFont = ObjC.ManagedString(postScript); }
            finally { CoreTextNative.Release(postScript); }
        }
    }

    private void CreateWindow()
    {
        var className = RegisterViewClass();
        _window = ObjC.Send(ObjC.Send(ObjC.Class("NSWindow"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithContentRect:styleMask:backing:defer:"),
            new ObjC.Rect(100, 100, WindowWidth, WindowHeight), 1 | 2, 2, 0);
        if (_window == 0) throw new InvalidOperationException("AppKit could not create a canvas window.");
        ObjC.Send(_window, ObjC.Sel("setReleasedWhenClosed:"), 0);
        ObjC.Send(_window, ObjC.Sel("setTitle:"), ObjC.String("mote read-only canvas probe"));
        _view = ObjC.Send(ObjC.Send(ObjC.Class(className), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(0, 0, WindowWidth, WindowHeight));
        if (_view == 0) throw new InvalidOperationException("AppKit could not create a canvas view.");
        ObjC.Send(_window, ObjC.Sel("setContentView:"), _view);
        ObjC.Send(_window, ObjC.Sel("setDelegate:"), _view);
        ObjC.Send(_window, ObjC.Sel("makeFirstResponder:"), _view);
        ObjC.Send(_view, ObjC.Sel("release"));
    }

    private void Draw()
    {
        var frame = _interaction.Frame();
        var context = ObjC.Send(ObjC.Send(ObjC.Class("NSGraphicsContext"),
            ObjC.Sel("currentContext")), ObjC.Sel("CGContext"));
        if (context == 0) throw new InvalidOperationException("NSView has no graphics context.");
        Fill(context, _theme.Palette.EditorBackground,
            new ObjC.Rect(0, 0, WindowWidth, WindowHeight));
        foreach (var slice in frame.Slices)
        {
            if (slice.SourceLength > MaxSliceLength)
                throw new InvalidOperationException("The canvas attempted to shape an unbounded slice.");
            var text = _interaction.Snapshot.GetText(slice.SourceStart, slice.SourceLength);
            if (text.Length == 0)
            {
                DrawSelection(context, frame, slice, 0);
                continue;
            }
            WithLine(text, line =>
            {
                var selected = DrawSelection(context, frame, slice, line);
                var baseline = WindowHeight - slice.TopY - 4 - _theme.Typography.EditorFontSize;
                FillColor(context, _theme.Palette.EditorForeground);
                MacOnScreenCanvasNative.SetTextMatrix(context,
                    new MacOnScreenCanvasNative.Affine(1, 0, 0, 1, 0, 0));
                CoreTextNative.SetTextPosition(context, LeftInset, baseline);
                CoreTextNative.LineDraw(line, context);
                if (selected is { } rect)
                {
                    MacOnScreenCanvasNative.SaveState(context);
                    try
                    {
                        MacOnScreenCanvasNative.ClipToRect(context, rect);
                        FillColor(context, _theme.Palette.SelectionForeground);
                        CoreTextNative.SetTextPosition(context, LeftInset, baseline);
                        CoreTextNative.LineDraw(line, context);
                    }
                    finally { MacOnScreenCanvasNative.RestoreState(context); }
                }
            });
        }
        if (!_capturing && ObjC.Send(_window, ObjC.Sel("isVisible")) != 0)
            _screenDrawCount++;
    }

    private ObjC.Rect? DrawSelection(nint context, CanvasFrame frame,
        ViewportSlice slice, nint line)
    {
        var start = Math.Max(frame.SelectionStart, slice.SourceStart);
        var end = Math.Min(frame.SelectionStart + frame.SelectionLength,
            slice.SourceStart + slice.SourceLength);
        var y = WindowHeight - slice.TopY - slice.Height;
        ObjC.Rect? textRect = null;
        if (start < end)
        {
            var first = CoreTextNative.OffsetForIndex(line, start - slice.SourceStart, out _);
            var last = CoreTextNative.OffsetForIndex(line, end - slice.SourceStart, out _);
            textRect = new ObjC.Rect(LeftInset + Math.Min(first, last), y,
                Math.Max(1, Math.Abs(last - first)), slice.Height);
            Fill(context, _theme.Palette.SelectionBackground, textRect.Value);
        }

        // Logical line delimiters are excluded from slices. Give LF and CRLF a
        // visible trailing marker, including a zero-length intermediate row.
        var source = _interaction.Snapshot;
        if (!slice.HasHiddenSuffix && slice.Line + 1 < source.LineCount)
        {
            var delimiterStart = slice.SourceStart + slice.SourceLength;
            var delimiterEnd = source.GetLineStartOffset(slice.Line + 1);
            if (delimiterStart < delimiterEnd && frame.SelectionStart < delimiterEnd &&
                frame.SelectionStart + frame.SelectionLength > delimiterStart)
            {
                var x = LeftInset + (line == 0 ? 0 : CoreTextNative.OffsetForIndex(
                    line, slice.SourceLength, out _));
                Fill(context, _theme.Palette.SelectionBackground,
                    new ObjC.Rect(x, y, Math.Max(8, _theme.Typography.EditorFontSize * 0.6),
                        slice.Height));
            }
        }
        return textRect;
    }

    private void HandleWheel(double deltaY, bool precise)
    {
        var lineHeight = Math.Max(1, _theme.Typography.EditorFontSize *
            _theme.Typography.LineHeightMultiplier);
        _interaction.ScrollBy(-deltaY * (precise ? 1 : lineHeight));
        Invalidate();
    }

    private void BeginPointer(double x, double topY)
    {
        _interaction.BeginSelection(HitSource(x, topY));
        Invalidate();
    }

    private void ExtendPointer(double x, double topY)
    {
        _interaction.ExtendSelection(HitSource(x, topY));
        Invalidate();
    }

    private int HitSource(double x, double topY)
    {
        var frame = _interaction.Frame();
        if (frame.Slices.Count == 0) return 0;
        var slice = frame.Slices[0];
        var found = false;
        foreach (var row in frame.Slices)
        {
            if (topY < row.TopY || topY >= row.TopY + row.Height) continue;
            slice = row;
            found = true;
            break;
        }
        if (!found && topY >= frame.Slices[0].TopY)
            slice = frame.Slices[^1];
        var text = _interaction.Snapshot.GetText(slice.SourceStart, slice.SourceLength);
        if (text.Length == 0)
        {
            _hitTests++;
            return slice.SourceStart;
        }
        var local = 0;
        WithLine(text, line =>
        {
            var index = CoreTextNative.IndexForPosition(line,
                new CoreTextNative.Point(Math.Max(0, x - LeftInset), 0));
            local = checked((int)Math.Clamp(index, 0, text.Length));
        });
        _hitTests++;
        // Grapheme snapping is bounded by the visible slice, never the full file.
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        var snapped = 0;
        foreach (var boundary in boundaries)
        {
            if (boundary > local) break;
            snapped = boundary;
        }
        if (local == text.Length) snapped = local;
        var source = slice.SourceStart + snapped;
        if (source > 0 && source < _interaction.Snapshot.Length)
        {
            var pair = _interaction.Snapshot.GetText(source - 1, 2);
            if (char.IsHighSurrogate(pair[0]) && char.IsLowSurrogate(pair[1]) ||
                pair[0] == '\r' && pair[1] == '\n') source--;
        }
        return source;
    }

    private void WithLine(string text, Action<nint> action)
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
                new CoreTextNative.Range(0, text.Length),
                _foregroundFromContextAttribute, _trueValue);
            var line = CoreTextNative.LineCreate(attributed);
            if (line == 0) throw new InvalidOperationException("CoreText line creation failed.");
            try { action(line); }
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
            return value != 0 ? value : throw new InvalidOperationException("CoreFoundation string creation failed.");
        }
        finally { Marshal.FreeHGlobal(chars); }
    }

    private static void Fill(nint context, ThemeColor color, ObjC.Rect rect)
    {
        FillColor(context, color);
        MacOnScreenCanvasNative.FillRect(context, rect);
    }

    private static void FillColor(nint context, ThemeColor color) =>
        CoreTextNative.SetFillColor(context, color.Red / 255d, color.Green / 255d,
            color.Blue / 255d, 1);

    private void Invalidate()
    {
        if (_view != 0) ObjC.Send(_view, ObjC.Sel("setNeedsDisplay:"), 1);
    }

    private void StopAndWake()
    {
        if (_stopping) return;
        _stopping = true;
        ObjC.Send(_application, ObjC.Sel("stop:"), 0);
        var wake = ObjC.SendOtherEvent(ObjC.Class("NSEvent"),
            ObjC.Sel("otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:"),
            15, new ObjC.Point(0, 0), 0, 0, 0, 0, 0, 0, 0);
        if (wake != 0) ObjC.Send(_application, ObjC.Sel("postEvent:atStart:"), wake, (byte)1);
    }

    private static string RegisterViewClass()
    {
        const string name = "MoteOnScreenCanvasProbeView";
        var cls = ObjC.AllocateClassPair(ObjC.Class("NSView"), name, 0);
        if (cls == 0) return name;
        Add(cls, "drawRect:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, ObjC.Rect, void>)&DrawRect,
            "v@:{CGRect={CGPoint=dd}{CGSize=dd}}");
        Add(cls, "scrollWheel:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&ScrollWheel, "v@:@");
        Add(cls, "mouseDown:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MouseDown, "v@:@");
        Add(cls, "mouseDragged:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MouseDragged, "v@:@");
        Add(cls, "mouseUp:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&MouseUp, "v@:@");
        Add(cls, "acceptsFirstResponder",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, byte>)&AcceptsFirstResponder, "c@:");
        Add(cls, "windowWillClose:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&WindowWillClose, "v@:@");
        Add(cls, "moteRunCanvasWorkflow:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&RunWorkflow, "v@:@");
        ObjC.RegisterClassPair(cls);
        return name;
    }

    private static void Add(nint cls, string selector, nint implementation, string encoding)
    {
        if (!ObjC.AddMethod(cls, ObjC.Sel(selector), implementation, encoding))
            throw new InvalidOperationException($"Could not register canvas {selector}.");
    }

    private void InvokeSafely(Action action)
    {
        if (_failure is not null) return;
        try { action(); }
        catch (Exception error) { _failure = error; StopAndWake(); }
    }

    private void HandleMouse(nint eventObject, Action<double, double> action)
    {
        var windowPoint = MacOnScreenCanvasNative.SendPoint(eventObject,
            ObjC.Sel("locationInWindow"));
        var local = MacOnScreenCanvasNative.SendPoint(_view,
            ObjC.Sel("convertPoint:fromView:"), windowPoint, 0);
        action(local.X, WindowHeight - local.Y);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DrawRect(nint self, nint selector, ObjC.Rect dirty)
    { s_current?.InvokeSafely(s_current.Draw); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ScrollWheel(nint self, nint selector, nint eventObject)
    {
        var current = s_current;
        current?.InvokeSafely(() => current.HandleWheel(
            MacOnScreenCanvasNative.SendDouble(eventObject, ObjC.Sel("scrollingDeltaY")),
            ObjC.Send(eventObject, ObjC.Sel("hasPreciseScrollingDeltas")) != 0));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MouseDown(nint self, nint selector, nint eventObject)
    { var current = s_current; current?.InvokeSafely(() => current.HandleMouse(eventObject, current.BeginPointer)); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MouseDragged(nint self, nint selector, nint eventObject)
    { var current = s_current; current?.InvokeSafely(() => current.HandleMouse(eventObject, current.ExtendPointer)); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MouseUp(nint self, nint selector, nint eventObject)
    { var current = s_current; current?.InvokeSafely(() => current._interaction.EndSelection()); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte AcceptsFirstResponder(nint self, nint selector) => 1;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void WindowWillClose(nint self, nint selector, nint notification)
    { s_current?.StopAndWake(); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void RunWorkflow(nint self, nint selector, nint sender)
    {
        var current = s_current;
        if (current is null) return;
        current.InvokeSafely(() => current._workflow?.Invoke(current));
        current.StopAndWake();
    }
}
