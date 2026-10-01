using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Telemetry;

namespace Mote.Native.Mac;

/// <summary>Opt-in observation on mote's owned main menu; business dispatch is unchanged.</summary>
[SupportedOSPlatform("macos")]
internal static unsafe class MacMenuObservation
{
    /// <summary>System-only runtime; no native library is packaged with mote.</summary>
    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    /// <summary>Process-lifetime class owned exclusively by this adapter.</summary>
    private const string ClassName = "MoteObservedMainMenu";
    /// <summary>Lexical NSMenu superclass, initialized once on the AppKit UI thread.</summary>
    private static nint s_baseClass;

    /// <summary>Objective-C superclass dispatch frame; no event is retained beyond this call.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Super
    {
        /// <summary>Unchanged receiver of the owned method.</summary>
        internal nint Receiver;
        /// <summary>First class searched, the declaring method's superclass.</summary>
        internal nint Class;
    }

    /// <summary>Looks up the inherited method before choosing its BOOL ABI.</summary>
    [DllImport(Runtime, EntryPoint = "class_getInstanceMethod")]
    private static extern nint InstanceMethod(nint cls, nint selector);
    /// <summary>Returns the runtime-owned method encoding; read only during setup.</summary>
    [DllImport(Runtime, EntryPoint = "method_getTypeEncoding")]
    private static extern nint TypeEncoding(nint method);
    /// <summary>Disposes only an unregistered class after optional setup fails.</summary>
    [DllImport(Runtime, EntryPoint = "objc_disposeClassPair")]
    private static extern void DisposeClass(nint cls);
    /// <summary>Exact native signature for BOOL performKeyEquivalent:(NSEvent*).</summary>
    [DllImport(Runtime, EntryPoint = "objc_msgSendSuper")]
    private static extern byte SendSuper(ref Super frame, nint selector, nint originalEvent);
    /// <summary>Reads a single unichar without materializing a managed string.</summary>
    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    private static extern ushort CharacterAt(nint value, nint selector, nuint index);

    /// <summary>Allocates stock NSMenu unless healthy tracing explicitly enables observation.</summary>
    internal static nint Create()
    {
        var health = MoteTelemetry.Health;
        if (!health.Enabled || health.SinkFaulted) return ObjC.New("NSMenu");
        try
        {
            EnsureClass();
            var menu = ObjC.New(ClassName);
            if (menu != 0) return menu;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Optional instrumentation must not prevent construction of the ordinary menu.
        }
        Record(TelemetryEvent.NativeMenuObservationUnavailable);
        return ObjC.New("NSMenu");
    }

    /// <summary>Records installation only after the application accepted the owned menu.</summary>
    internal static void Installed(nint menu)
    {
        try
        {
            if (s_baseClass != 0 && ObjC.Send(menu, ObjC.Sel("isKindOfClass:"), ObjC.Class(ClassName)) != 0)
                Record(TelemetryEvent.NativeMenuObservationReady);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Record(TelemetryEvent.NativeMenuObservationUnavailable); }
    }

    /// <summary>Registers only our class; SDK classes and their methods are never modified.</summary>
    private static void EnsureClass()
    {
        if (s_baseClass != 0) return;
        var baseClass = ObjC.Class("NSMenu");
        var method = InstanceMethod(baseClass, ObjC.Sel("performKeyEquivalent:"));
        var encoding = method == 0 ? null : Marshal.PtrToStringUTF8(TypeEncoding(method));
        if (encoding is null || encoding.Length == 0 || encoding[0] is not ('c' or 'B'))
            throw new PlatformNotSupportedException("Menu Boolean ABI is unavailable.");
        RegisterObservedSubclass(baseClass, ClassName, encoding);
        s_baseClass = baseClass;
    }

    /// <summary>Installs the production callback on our owned class.</summary>
    private static nint RegisterObservedSubclass(nint baseClass, string name, string encoding)
    {
        var cls = ObjC.AllocateClassPair(baseClass, name, 0);
        if (cls == 0) throw new InvalidOperationException("Menu observation class unavailable.");
        if (!ObjC.AddMethod(cls, ObjC.Sel("performKeyEquivalent:"),
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, byte>)&PerformKeyEquivalent, encoding))
        {
            DisposeClass(cls);
            throw new InvalidOperationException("Menu observation method unavailable.");
        }
        ObjC.RegisterClassPair(cls);
        return cls;
    }

    /// <summary>Filters without marshaling strings; documented character getter is key-down-only.</summary>
    private static bool IsCandidate(nint originalEvent)
    {
        if (originalEvent == 0) return false;
        var type = (nuint)ObjC.Send(originalEvent, ObjC.Sel("type"));
        var modifiers = (nuint)ObjC.Send(originalEvent, ObjC.Sel("modifierFlags"));
        if (!NativeSaveFamilyCandidate.HasCandidateMetadata(type, modifiers)) return false;
        var text = ObjC.Send(originalEvent, ObjC.Sel("charactersIgnoringModifiers"));
        var length = (nuint)ObjC.Send(text, ObjC.Sel("length"));
        return length == 1 && NativeSaveFamilyCandidate.Matches(type, modifiers, length,
            CharacterAt(text, ObjC.Sel("characterAtIndex:"), 0));
    }

    /// <summary>Contains optional instrumentation faults without altering native dispatch.</summary>
    private static void Record(TelemetryEvent kind)
    {
        try { MoteTelemetry.RecordNativeMenuCheckpoint(kind); }
        catch (Exception error) when (error is not OutOfMemoryException) { }
    }

    /// <summary>Passes the same event exactly once to NSMenu and preserves its exact BOOL byte.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte PerformKeyEquivalent(nint self, nint selector, nint originalEvent) =>
        Forward(self, selector, originalEvent, s_baseClass);

    /// <summary>Uses the superclass of the method's declaring class, never the receiver's dynamic class.</summary>
    /// <remarks>The ABI control supplies its own lexical superclass through a separate tiny wrapper.</remarks>
    internal static byte Forward(nint self, nint selector, nint originalEvent, nint declaringSuperclass)
    {
        var candidate = false;
        try { candidate = IsCandidate(originalEvent); }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        if (candidate) Record(TelemetryEvent.NativeMenuSaveFamilyEntered);
        var frame = new Super { Receiver = self, Class = declaringSuperclass };
        var handled = SendSuper(ref frame, selector, originalEvent);
        if (candidate) Record(handled == 0 ? TelemetryEvent.NativeMenuSaveFamilyReturnedFalse :
            TelemetryEvent.NativeMenuSaveFamilyReturnedTrue);
        return handled;
    }
}
