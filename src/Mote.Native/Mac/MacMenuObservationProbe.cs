using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mote.Native.Mac;

/// <summary>Owned in-process ABI control; events are never posted to an application or the system.</summary>
[SupportedOSPlatform("macos")]
internal static unsafe class MacMenuObservationProbe
{
    /// <summary>System runtime used by the isolated in-process check.</summary>
    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    /// <summary>Base-call count for the current bounded assertion.</summary>
    private static int s_calls;
    /// <summary>Expected native pointer, confined to this check and never serialized.</summary>
    private static nint s_expectedEvent;
    /// <summary>At most one nested synthetic event; cleared before dispatch.</summary>
    private static nint s_nestedEvent;
    /// <summary>Accumulated identity assertion, including the nested invocation.</summary>
    private static bool s_sameEvent;
    /// <summary>Exact controlled superclass BOOL result.</summary>
    private static byte s_result;
    /// <summary>Lexical superclass of the separate check wrapper.</summary>
    private static nint s_controlClass;

    /// <summary>Retrieves NSMenu's declared Boolean method signature.</summary>
    [DllImport(Runtime, EntryPoint = "class_getInstanceMethod")]
    private static extern nint InstanceMethod(nint cls, nint selector);
    /// <summary>Reads the runtime-owned encoding during isolated setup.</summary>
    [DllImport(Runtime, EntryPoint = "method_getTypeEncoding")]
    private static extern nint TypeEncoding(nint method);
    /// <summary>Invokes a Boolean menu method using its exact scalar/object ABI.</summary>
    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    private static extern byte SendBoolean(nint receiver, nint selector, nint originalEvent);
    /// <summary>Constructs an autoreleased key event without any application/global posting.</summary>
    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    private static extern nint KeyEvent(nint receiver, nint selector, nuint type, ObjC.Point location,
        nuint modifiers, double timestamp, nint windowNumber, nint context, nint characters,
        nint ignoringModifiers, byte repeat, ushort keyCode);

    /// <summary>Exercises the shared production forwarding core and BOOL ABI on the UI thread.</summary>
    internal static void VerifyForwarding()
    {
        var selector = ObjC.Sel("performKeyEquivalent:");
        var encoding = Marshal.PtrToStringUTF8(TypeEncoding(InstanceMethod(ObjC.Class("NSMenu"), selector)))
            ?? throw new InvalidOperationException("Menu ABI control has no Boolean signature.");
        if (encoding[0] is not ('c' or 'B')) throw new InvalidOperationException("Menu ABI control signature.");
        var control = ObjC.AllocateClassPair(ObjC.Class("NSMenu"), "MoteMenuAbiControl", 0);
        if (control == 0 || !ObjC.AddMethod(control, selector,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, byte>)&CountedBase, encoding))
            throw new InvalidOperationException("Menu ABI control registration.");
        ObjC.RegisterClassPair(control);
        s_controlClass = control;
        var observedClass = ObjC.AllocateClassPair(control, "MoteMenuAbiObserved", 0);
        if (observedClass == 0 || !ObjC.AddMethod(observedClass, selector,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, byte>)&Observed, encoding))
            throw new InvalidOperationException("Menu ABI observed control registration.");
        ObjC.RegisterClassPair(observedClass);
        var descendantClass = ObjC.AllocateClassPair(observedClass, "MoteMenuAbiDescendant", 0);
        if (descendantClass == 0) throw new InvalidOperationException("Menu ABI descendant registration.");
        ObjC.RegisterClassPair(descendantClass);
        var menu = ObjC.New("MoteMenuAbiObserved");
        var descendant = ObjC.New("MoteMenuAbiDescendant");
        try
        {
            var save = CreateKey("s");
            var saveAs = CreateKey("S");
            var other = CreateKey("x");
            Check(menu, save, 0, 1);
            Check(menu, saveAs, 1, 1);
            Check(menu, other, 1, 1);
            s_nestedEvent = saveAs;
            Check(menu, save, 1, 2);
            // Inherited IMP must start at the declaring class's superclass, not recurse through itself.
            Check(descendant, save, 0, 1);
            Check(descendant, saveAs, 1, 1);
        }
        finally
        {
            s_expectedEvent = 0;
            s_nestedEvent = 0;
            ObjC.Send(menu, ObjC.Sel("release"));
            ObjC.Send(descendant, ObjC.Sel("release"));
        }
    }

    /// <summary>Tiny control wrapper supplies its lexical superclass, just as the production wrapper does.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte Observed(nint self, nint selector, nint originalEvent) =>
        MacMenuObservation.Forward(self, selector, originalEvent, s_controlClass);

    /// <summary>Creates one autoreleased synthetic event; native key code is never persisted.</summary>
    internal static nint CreateKey(string character)
    {
        var text = ObjC.String(character);
        var result = KeyEvent(ObjC.Class("NSEvent"), ObjC.Sel(
            "keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:characters:charactersIgnoringModifiers:isARepeat:keyCode:"),
            10, new ObjC.Point(0, 0), 1u << 20, 0, 0, 0, text, text, 0, 1);
        if (result == 0) throw new InvalidOperationException("Menu ABI control event unavailable.");
        return result;
    }

    /// <summary>Requires exact byte result, unchanged pointer and one base call per invocation.</summary>
    private static void Check(nint menu, nint originalEvent, byte result, int calls)
    {
        s_calls = 0;
        s_sameEvent = true;
        s_expectedEvent = originalEvent;
        s_result = result;
        var observed = SendBoolean(menu, ObjC.Sel("performKeyEquivalent:"), originalEvent);
        if (observed != result || s_calls != calls || !s_sameEvent)
            throw new InvalidOperationException("Menu ABI forwarding control failed.");
    }

    /// <summary>Checks the forwarded event, optionally nesting one independent invocation.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte CountedBase(nint self, nint selector, nint originalEvent)
    {
        s_calls++;
        s_sameEvent &= originalEvent == s_expectedEvent;
        var nested = s_nestedEvent;
        if (nested != 0)
        {
            s_nestedEvent = 0;
            s_expectedEvent = nested;
            var result = SendBoolean(self, selector, nested);
            s_sameEvent &= result == s_result;
        }
        return s_result;
    }
}
