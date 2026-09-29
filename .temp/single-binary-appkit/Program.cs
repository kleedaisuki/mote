using System.Runtime.InteropServices;

/// <summary>Checks whether a C# Native AOT executable can use macOS system AppKit without companion libraries.</summary>
internal static class Program
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    /// <summary>AppKit rectangle passed by value to Objective-C initializers.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Rect(double X, double Y, double Width, double Height);

    [DllImport(ObjC, EntryPoint = "objc_getClass")]
    private static extern nint GetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC, EntryPoint = "objc_getProtocol")]
    private static extern nint GetProtocol([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC, EntryPoint = "class_conformsToProtocol")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ConformsToProtocol(nint cls, nint protocol);

    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern nint Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint target, nint selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendPtr(nint target, nint selector, nint value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendPtrPtr(nint target, nint selector, nint first, nint second);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendInt(nint target, nint selector, nint value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendRect(nint target, nint selector, Rect value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendWindowInit(nint target, nint selector, Rect rect, nint style, nint backing, byte defer);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendUtf8(nint target, nint selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint target, nint selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidPtr(nint target, nint selector, nint value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidDelay(nint target, nint selector, nint requestedSelector, nint value, double seconds);

    /// <summary>Runs a runtime-only protocol check or displays a native text window for two seconds.</summary>
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsMacOS()) return 99;

        var textViewClass = GetClass("NSTextView");
        var inputClient = GetProtocol("NSTextInputClient");
        if (textViewClass == 0 || inputClient == 0)
        {
            Console.Error.WriteLine("Missing AppKit text classes or protocol.");
            return 1;
        }

        Console.WriteLine($"NSTextView class={textViewClass != 0}, NSTextInputClient protocol={inputClient != 0}, conforms={ConformsToProtocol(textViewClass, inputClient)}");
        if (args.Length == 0 || args[0] != "--window") return 0;

        var pool = Send(Send(GetClass("NSAutoreleasePool"), Selector("alloc")), Selector("init"));
        var app = Send(GetClass("NSApplication"), Selector("sharedApplication"));
        SendInt(app, Selector("setActivationPolicy:"), 0);

        var frame = new Rect(100, 100, 640, 420);
        var window = SendWindowInit(Send(GetClass("NSWindow"), Selector("alloc")),
            Selector("initWithContentRect:styleMask:backing:defer:"), frame, 15, 2, 0);
        var text = SendRect(Send(textViewClass, Selector("alloc")), Selector("initWithFrame:"), frame);
        var hello = SendUtf8(GetClass("NSString"), Selector("stringWithUTF8String:"), "mote AppKit native text input probe — 编辑测试");
        SendVoidPtr(text, Selector("setString:"), hello);
        SendVoidPtr(window, Selector("setContentView:"), text);
        SendVoidPtr(window, Selector("setTitle:"), hello);
        SendVoidPtr(window, Selector("makeKeyAndOrderFront:"), 0);
        SendInt(app, Selector("activateIgnoringOtherApps:"), 1);

        // Schedule termination on the AppKit run loop rather than touching UI from a worker thread.
        SendVoidDelay(app, Selector("performSelector:withObject:afterDelay:"), Selector("terminate:"), 0, 2.0);
        Console.WriteLine("Window created; entering AppKit event loop for two seconds.");
        SendVoid(app, Selector("run"));
        SendVoid(pool, Selector("drain"));
        return 0;
    }
}
