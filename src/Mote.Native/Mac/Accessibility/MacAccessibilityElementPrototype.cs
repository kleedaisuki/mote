using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Native.Accessibility;
using Mote.Native.Mac.Canvas;

namespace Mote.Native.Mac.Accessibility;

/// <summary>
/// Opt-in AppKit accessibility-element prototype for one engine-backed canvas.
/// It is not attached to the product view or input island by default.
/// </summary>
/// <remarks>
/// A future shell can call Attach(canvasView, inputEditor) on the UI thread and
/// dispose the element before destroying the view. This prototype supplies
/// source count/ranges/strings, but lacks exact glyph geometry, point hit-testing,
/// selection setters, notifications, and measured VoiceOver behavior. The
/// register/attach ABI must be tested on both x64 and arm64 target Macs before use.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacAccessibilityElementPrototype : IDisposable
{
    private const string ClassName = "MoteSourceAccessibilityElement";
    private static MacAccessibilityElementPrototype? s_current;
    private static bool s_registered;
    private static readonly bool s_traceStages =
        Environment.GetEnvironmentVariable("MOTE_NATIVE_MAC_STAGE_TRACE") == "1";
    private readonly MacAccessibilityTextCore _core;
    private nint _element;
    private nint _canvasView;
    private nint _inputEditor;
    private nint _previousChildren;
    private ObjC.Rect _bodyRect;
    private bool _hasBodyRect;
    private bool _inputWasAccessible;
    private int _faultReported;

    /// <summary>
    /// Reports one unexpected native-selector failure. The shell must schedule
    /// provider detachment on the AppKit UI thread without disabling input.
    /// </summary>
    internal event Action<Exception>? Faulted;

    /// <summary>
    /// Attached source element for target-host selector diagnostics only. The
    /// caller must not retain it beyond Dispose or mutate its AX tree.
    /// </summary>
    internal nint Element => _element;

    /// <summary>Creates a source-backed AppKit element without materializing the document.</summary>
    internal MacAccessibilityElementPrototype(AccessibleDocument document,
        IAccessibleViewport viewport)
    {
        _core = new MacAccessibilityTextCore(document, viewport);
    }

    /// <summary>
    /// Attaches one logical AX text-area child to the canvas and hides the
    /// bounded NSTextView from the AX tree without changing keyboard focus.
    /// </summary>
    internal nint Attach(nint canvasView, nint inputEditor,
        ObjC.Rect? initialBodyRect = null)
    {
        TraceStage("A00-attach-enter");
        if (canvasView == 0 || inputEditor == 0)
            throw new ArgumentException("Canvas and input views must exist before AX attachment.");
        if (s_current is not null) throw new InvalidOperationException("Only one AX prototype may be attached.");
        // Validate before recording an owned input view: failed attachment must
        // not let Dispose change its pre-existing accessibility flag.
        TraceStage("A01-before-body-validate");
        if (initialBodyRect is { } initial) ValidateBodyRect(canvasView, initial);
        TraceStage("A02-after-body-validate");
        RegisterClass();
        TraceStage("A03-after-class-register");
        s_current = this;
        try
        {
            _canvasView = canvasView;
            _inputEditor = inputEditor;
            if (initialBodyRect is { } body)
            {
                _bodyRect = body;
                _hasBodyRect = true;
            }
            TraceStage("A04-before-children-read");
            _previousChildren = ObjC.Send(canvasView, ObjC.Sel("accessibilityChildren"));
            TraceStage("A05-after-children-read");
            if (_previousChildren != 0)
                ObjC.Send(_previousChildren, ObjC.Sel("retain"));
            TraceStage("A06-after-children-retain");
            TraceStage("A07-before-input-flag-read");
            _inputWasAccessible = ObjC.Send(inputEditor, ObjC.Sel("isAccessibilityElement")) != 0;
            TraceStage("A08-after-input-flag-read");
            TraceStage("A09-before-element-alloc");
            var allocated = ObjC.Send(ObjC.Class(ClassName), ObjC.Sel("alloc"));
            TraceStage("A10-after-element-alloc");
            _element = ObjC.Send(allocated, ObjC.Sel("init"));
            TraceStage("A11-after-element-init");
            if (_element == 0) throw new InvalidOperationException("AppKit could not allocate the AX element.");
            TraceStage("A12-before-set-parent");
            ObjC.Send(_element, ObjC.Sel("setAccessibilityParent:"), canvasView);
            TraceStage("A13-after-set-parent");
            TraceStage("A14-before-set-frame");
            UpdateFrameFromView();
            TraceStage("A15-after-set-frame");
            TraceStage("A16-before-children-array");
            var children = _previousChildren != 0
                ? ObjC.Send(ObjC.Class("NSMutableArray"), ObjC.Sel("arrayWithArray:"), _previousChildren)
                : ObjC.Send(ObjC.Class("NSMutableArray"), ObjC.Sel("array"));
            TraceStage("A17-after-children-array");
            TraceStage("A18-before-array-edit");
            ObjC.Send(children, ObjC.Sel("removeObjectIdenticalTo:"), inputEditor);
            ObjC.Send(children, ObjC.Sel("addObject:"), _element);
            TraceStage("A19-after-array-edit");
            TraceStage("A20-before-set-children");
            ObjC.Send(canvasView, ObjC.Sel("setAccessibilityChildren:"), children);
            TraceStage("A21-after-set-children");
            TraceStage("A22-before-hide-input");
            ObjC.Send(inputEditor, ObjC.Sel("setAccessibilityElement:"), 0);
            TraceStage("A23-after-hide-input");
            return _element;
        }
        catch
        {
            TraceStage("A24-managed-exception");
            Dispose();
            throw;
        }
    }

    /// <summary>Privacy-safe AX attachment breadcrumbs for opt-in crash diagnosis.</summary>
    private static void TraceStage(string code)
    {
        if (s_traceStages) Console.Error.WriteLine($"mote-mac-stage:{code}");
    }

    /// <summary>
    /// Sets the physically painted source body in canvas-local coordinates.
    /// The input ribbon is not part of the source text area's AX frame.
    /// </summary>
    internal void UpdateBodyRect(ObjC.Rect bodyRect)
    {
        if (_element == 0 || _canvasView == 0)
            throw new InvalidOperationException("AX body geometry requires an attached canvas.");
        ValidateBodyRect(_canvasView, bodyRect);
        ObjC.Send(_element, ObjC.Sel("setAccessibilityFrameInParentSpace:"), bodyRect);
        _bodyRect = bodyRect;
        _hasBodyRect = true;
    }

    /// <summary>Refreshes parent-space AX geometry after the canvas resizes or reflows.</summary>
    internal void UpdateFrameFromView()
    {
        if (_element == 0 || _canvasView == 0) return;
        var frame = _hasBodyRect ? _bodyRect :
            MacOnScreenCanvasNative.GetRect(_canvasView, ObjC.Sel("bounds"));
        ObjC.Send(_element, ObjC.Sel("setAccessibilityFrameInParentSpace:"), frame);
    }

    private static void ValidateBodyRect(nint canvasView, ObjC.Rect rect)
    {
        var bounds = MacOnScreenCanvasNative.GetRect(canvasView, ObjC.Sel("bounds"));
        const double tolerance = 0.001;
        if (!double.IsFinite(rect.Origin.X) || !double.IsFinite(rect.Origin.Y) ||
            !double.IsFinite(rect.Size.Width) || !double.IsFinite(rect.Size.Height) ||
            rect.Size.Width < 0 || rect.Size.Height < 0 ||
            rect.Origin.X < bounds.Origin.X - tolerance ||
            rect.Origin.Y < bounds.Origin.Y - tolerance ||
            rect.Origin.X + rect.Size.Width > bounds.Origin.X + bounds.Size.Width + tolerance ||
            rect.Origin.Y + rect.Size.Height > bounds.Origin.Y + bounds.Size.Height + tolerance)
            throw new ArgumentOutOfRangeException(nameof(rect),
                "AX source body must stay within the canvas view bounds.");
    }

    /// <summary>Releases the one owned AppKit element and unmanaged callback root.</summary>
    public void Dispose()
    {
        if (s_current == this) s_current = null;
        _core.Detach();
        if (_inputEditor != 0)
            ObjC.Send(_inputEditor, ObjC.Sel("setAccessibilityElement:"), _inputWasAccessible ? 1 : 0);
        if (_canvasView != 0)
            ObjC.Send(_canvasView, ObjC.Sel("setAccessibilityChildren:"), _previousChildren);
        if (_previousChildren != 0) ObjC.Send(_previousChildren, ObjC.Sel("release"));
        if (_element != 0) ObjC.Send(_element, ObjC.Sel("release"));
        _element = _canvasView = _inputEditor = _previousChildren = 0;
        _bodyRect = default;
        _hasBodyRect = false;
    }

    private static void RegisterClass()
    {
        if (s_registered) return;
        var cls = ObjC.AllocateClassPair(ObjC.Class("NSAccessibilityElement"), ClassName, 0);
        if (cls == 0) throw new InvalidOperationException("AppKit AX class registration failed.");
        Add(cls, "accessibilityRole", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint>)&Role, "@@:");
        Add(cls, "accessibilityLabel", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint>)&Label, "@@:");
        Add(cls, "isAccessibilityFocused",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, byte>)&IsFocused, "c@:");
        Add(cls, "setAccessibilityFocused:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, byte, void>)&SetFocused, "v@:c");
        Add(cls, "accessibilityNumberOfCharacters",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint>)&NumberOfCharacters, "q@:");
        Add(cls, "accessibilitySelectedTextRange",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, ObjC.Range>)&SelectedRange,
            "{_NSRange=QQ}@:");
        Add(cls, "accessibilityVisibleCharacterRange",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, ObjC.Range>)&VisibleRange,
            "{_NSRange=QQ}@:");
        Add(cls, "accessibilityStringForRange:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, ObjC.Range, nint>)&StringForRange,
            "@@:{_NSRange=QQ}");
        Add(cls, "accessibilityLineForIndex:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint>)&LineForIndex, "q@:q");
        Add(cls, "accessibilityRangeForLine:",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, ObjC.Range>)&RangeForLine,
            "{_NSRange=QQ}@:q");
        ObjC.RegisterClassPair(cls);
        s_registered = true;
    }

    private static void Add(nint cls, string selector, nint implementation, string signature)
    {
        if (!ObjC.AddMethod(cls, ObjC.Sel(selector), implementation, signature))
            throw new InvalidOperationException($"Could not register AX selector {selector}.");
    }

    private static ObjC.Range NativeRange(AccessibleRange range) =>
        new((nuint)range.Start, (nuint)range.Length);

    private static MacAccessibilityElementPrototype? Current(nint self) =>
        s_current is { } owner && owner._element == self ? owner : null;

    private static void ReportFault(nint self, Exception exception)
    {
        var owner = Current(self);
        if (owner is null || Interlocked.Exchange(ref owner._faultReported, 1) != 0) return;
        try { owner.Faulted?.Invoke(exception); }
        catch { /* A shell fault handler must not unwind through Objective-C. */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint Role(nint self, nint selector)
    {
        try { return Current(self) is null ? 0 : ObjC.String("AXTextArea"); }
        catch (Exception ex) { ReportFault(self, ex); return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint Label(nint self, nint selector)
    {
        try { return Current(self) is null ? 0 : ObjC.String("Mote editor"); }
        catch (Exception ex) { ReportFault(self, ex); return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte IsFocused(nint self, nint selector)
    {
        try
        {
            if (Current(self) is not { } owner || owner._inputEditor == 0) return 0;
            var window = ObjC.Send(owner._inputEditor, ObjC.Sel("window"));
            return window != 0 && ObjC.Send(window, ObjC.Sel("firstResponder")) == owner._inputEditor
                ? (byte)1 : (byte)0;
        }
        catch (Exception ex) { ReportFault(self, ex); return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SetFocused(nint self, nint selector, byte focused)
    {
        try
        {
            if (focused == 0 || Current(self) is not { } owner || owner._inputEditor == 0) return;
            var window = ObjC.Send(owner._inputEditor, ObjC.Sel("window"));
            if (window != 0) ObjC.Send(window, ObjC.Sel("makeFirstResponder:"), owner._inputEditor);
        }
        catch (Exception ex) { ReportFault(self, ex); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint NumberOfCharacters(nint self, nint selector)
    {
        try { return Current(self)?._core.CharacterCount ?? 0; }
        catch (InvalidOperationException) { return 0; }
        catch (Exception ex) { ReportFault(self, ex); return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static ObjC.Range SelectedRange(nint self, nint selector)
    {
        try { return Current(self) is { } owner ? NativeRange(owner._core.SelectedTextRange) : new(0, 0); }
        catch (InvalidOperationException) { return new(0, 0); }
        catch (Exception ex) { ReportFault(self, ex); return new(0, 0); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static ObjC.Range VisibleRange(nint self, nint selector)
    {
        try { return Current(self) is { } owner ? NativeRange(owner._core.VisibleCharacterRange) : new(0, 0); }
        catch (InvalidOperationException) { return new(0, 0); }
        catch (Exception ex) { ReportFault(self, ex); return new(0, 0); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint StringForRange(nint self, nint selector, ObjC.Range range)
    {
        var owner = Current(self);
        if (owner is null) return 0;
        if (range.Location > (nuint)int.MaxValue || range.Length > (nuint)int.MaxValue ||
            range.Length > (nuint)(int.MaxValue - (int)range.Location)) return 0;
        try
        {
            var sourceRange = owner._core.MakeRange((int)range.Location,
                (int)range.Location + (int)range.Length);
            return ObjC.String(owner._core.StringForRange(sourceRange));
        }
        catch (ArgumentOutOfRangeException) { return 0; }
        catch (AccessibleRequestTooLargeException) { return 0; }
        catch (InvalidOperationException) { return 0; }
        catch (Exception ex) { ReportFault(self, ex); return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint LineForIndex(nint self, nint selector, nint index)
    {
        if (Current(self) is not { } owner || index < 0 || index > int.MaxValue) return -1;
        try { return owner._core.LineForIndex((int)index); }
        catch (ArgumentOutOfRangeException) { return -1; }
        catch (InvalidOperationException) { return -1; }
        catch (Exception ex) { ReportFault(self, ex); return -1; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static ObjC.Range RangeForLine(nint self, nint selector, nint line)
    {
        if (Current(self) is not { } owner || line < 0 || line > int.MaxValue)
            return new(nuint.MaxValue, 0);
        try { return NativeRange(owner._core.RangeForLine((int)line)); }
        catch (ArgumentOutOfRangeException) { return new(nuint.MaxValue, 0); }
        catch (InvalidOperationException) { return new(nuint.MaxValue, 0); }
        catch (Exception ex) { ReportFault(self, ex); return new(nuint.MaxValue, 0); }
    }
}
