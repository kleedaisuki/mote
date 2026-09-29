using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mote.Native.Mac;

/// <summary>A small, statically linked bridge to the Objective-C runtime and system AppKit.</summary>
/// <remarks>No framework or managed assembly is copied next to the Native AOT executable.</remarks>
[SupportedOSPlatform("macos")]
internal static class ObjC
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

    /// <summary>Native AppKit point and size in device-independent coordinates.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Point(double X, double Y);

    /// <summary>Native AppKit size in device-independent coordinates.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Size(double Width, double Height);

    /// <summary>Native AppKit rectangle, passed by value under both x64 and arm64 ABIs.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Rect(Point Origin, Size Size)
    {
        /// <summary>Constructs a rectangle from coordinates and dimensions.</summary>
        internal Rect(double x, double y, double width, double height)
            : this(new Point(x, y), new Size(width, height)) { }
    }

    /// <summary>A UTF-16 range; NSInteger and NSUInteger are pointer-sized on macOS.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Range(nuint Location, nuint Length);

    [DllImport(Runtime, EntryPoint = "objc_getClass")]
    private static extern nint GetClassNative([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Runtime, EntryPoint = "sel_registerName")]
    private static extern nint SelectorNative([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Runtime, EntryPoint = "objc_allocateClassPair")]
    internal static extern nint AllocateClassPair(nint superclass,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nuint extraBytes);

    [DllImport(Runtime, EntryPoint = "objc_registerClassPair")]
    internal static extern void RegisterClassPair(nint cls);

    [DllImport(Runtime, EntryPoint = "class_addMethod")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool AddMethod(nint cls, nint selector, nint implementation,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string types);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern nint Send(nint receiver, nint selector);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern nint Send(nint receiver, nint selector, nint arg);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern nint Send(nint receiver, nint selector, nint arg1, nint arg2);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern nint Send(nint receiver, nint selector, nint arg1, nint arg2, nint arg3);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern nint Send(nint receiver, nint selector, nint arg1, nint arg2, byte arg3);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern nint Send(nint receiver, nint selector, Rect rect);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern nint Send(nint receiver, nint selector, Rect rect, nuint style,
        nuint backing, byte defer);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern nint Send(nint receiver, nint selector, nint arg, Rect rect);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern nint Send(nint receiver, nint selector, double value);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern nint Send(nint receiver, nint selector, double value1, double value2);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern nint Send(nint receiver, nint selector, double a, double b, double c, double d);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern nint Send(nint receiver, nint selector, nint arg, Range range);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern nint Send(nint receiver, nint selector, Range range);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern nint Send(nint receiver, nint selector, Size size);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern nint Send(nint receiver, nint selector, nint arg1, nint arg2, nint arg3,
        nint arg4, nint arg5, nint arg6);

    [DllImport(AppKit, EntryPoint = "NSApplicationLoad")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ApplicationLoad();

    /// <summary>Resolves a system class. Missing classes fail near their first use.</summary>
    internal static nint Class(string name) => GetClassNative(name) is var cls && cls != 0
        ? cls : throw new PlatformNotSupportedException($"Objective-C class {name} is unavailable.");

    /// <summary>Interns an Objective-C selector in the process runtime.</summary>
    internal static nint Sel(string name) => SelectorNative(name);

    /// <summary>Allocates and initializes a standard Objective-C object.</summary>
    internal static nint New(string className) => Send(Send(Class(className), Sel("alloc")), Sel("init"));

    /// <summary>Creates an autoreleased NSString from managed UTF-16 text.</summary>
    internal static nint String(string value)
    {
        var chars = Marshal.StringToHGlobalUni(value);
        try { return Send(Class("NSString"), Sel("stringWithCharacters:length:"), chars, value.Length); }
        finally { Marshal.FreeHGlobal(chars); }
    }

    /// <summary>Copies NSString contents into a managed string.</summary>
    internal static string ManagedString(nint value)
    {
        if (value == 0) return string.Empty;
        var length = checked((int)Send(value, Sel("length")));
        if (length == 0) return string.Empty;
        var chars = Marshal.AllocHGlobal(checked(length * sizeof(char)));
        try
        {
            Send(value, Sel("getCharacters:range:"), chars, new Range(0, (nuint)length));
            return Marshal.PtrToStringUni(chars, length) ?? string.Empty;
        }
        finally { Marshal.FreeHGlobal(chars); }
    }
}
