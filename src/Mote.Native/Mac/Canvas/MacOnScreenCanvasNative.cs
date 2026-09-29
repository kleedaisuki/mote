using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mote.Native.Mac.Canvas;

/// <summary>Additional statically reachable AppKit/CoreGraphics calls for the visible canvas.</summary>
[SupportedOSPlatform("macos")]
internal static class MacOnScreenCanvasNative
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreText = "/System/Library/Frameworks/CoreText.framework/CoreText";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary>
    /// CoreText normally paints black from the attributed string; this key with
    /// kCFBooleanTrue delegates glyph color to the current CGContext fill color.
    /// Both exported values are immortal framework constants, not owned CF objects.
    /// </summary>
    internal static nint ForegroundColorFromContextAttributeName =>
        ResolveConstant(CoreText, "kCTForegroundColorFromContextAttributeName");

    /// <summary>The immortal CoreFoundation true value used by CoreText attributes.</summary>
    internal static nint BooleanTrue => ResolveConstant(CoreFoundation, "kCFBooleanTrue");

    private static nint ResolveConstant(string framework, string symbol)
    {
        var library = NativeLibrary.Load(framework);
        try
        {
            var value = Marshal.ReadIntPtr(NativeLibrary.GetExport(library, symbol));
            return value != 0 ? value : throw new InvalidOperationException($"{symbol} is null.");
        }
        finally { NativeLibrary.Free(library); }
    }

    /// <summary>Returns a retained PostScript font name for provenance metadata.</summary>
    [DllImport(CoreText, EntryPoint = "CTFontCopyPostScriptName")]
    internal static extern nint FontCopyPostScriptName(nint font);

    /// <summary>A six-scalar CoreGraphics affine transform.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Affine(double A, double B, double C,
        double D, double Tx, double Ty);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern ObjC.Point SendPoint(nint receiver, nint selector);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern ObjC.Point SendPoint(nint receiver, nint selector,
        ObjC.Point point, nint fromView);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern double SendDouble(nint receiver, nint selector);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static extern nint Send(nint receiver, nint selector, ObjC.Rect rect, nint value);

    [DllImport(Graphics, EntryPoint = "CGContextFillRect")]
    internal static extern void FillRect(nint context, ObjC.Rect rect);

    [DllImport(Graphics, EntryPoint = "CGContextSetTextMatrix")]
    internal static extern void SetTextMatrix(nint context, Affine matrix);

    [DllImport(Graphics, EntryPoint = "CGContextSaveGState")]
    internal static extern void SaveState(nint context);

    [DllImport(Graphics, EntryPoint = "CGContextRestoreGState")]
    internal static extern void RestoreState(nint context);

    [DllImport(Graphics, EntryPoint = "CGContextClipToRect")]
    internal static extern void ClipToRect(nint context, ObjC.Rect rect);
}
