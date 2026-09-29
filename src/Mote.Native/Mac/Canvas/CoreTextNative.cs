using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mote.Native.Mac.Canvas;

/// <summary>Only the system CoreFoundation, CoreText, and CoreGraphics ABI used by the canvas probe.</summary>
[SupportedOSPlatform("macos")]
internal static class CoreTextNative
{
    private const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string Text = "/System/Library/Frameworks/CoreText.framework/CoreText";
    private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    /// <summary>A CoreFoundation UTF-16 character range.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Range(nint Location, nint Length);

    /// <summary>A CoreGraphics point in context coordinates.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Point(double X, double Y);

    [DllImport(Foundation, EntryPoint = "CFStringCreateWithCharacters")]
    internal static extern nint StringCreate(nint allocator, nint characters, nint length);

    [DllImport(Foundation, EntryPoint = "CFAttributedStringCreateMutable")]
    internal static extern nint AttributedCreateMutable(nint allocator, nint maxLength);

    [DllImport(Foundation, EntryPoint = "CFAttributedStringReplaceString")]
    internal static extern void AttributedReplace(nint attributed, Range range, nint text);

    [DllImport(Foundation, EntryPoint = "CFAttributedStringSetAttribute")]
    internal static extern void AttributedSetAttribute(nint attributed, Range range,
        nint attributeName, nint value);

    [DllImport(Foundation, EntryPoint = "CFRelease")]
    internal static extern void Release(nint value);

    [DllImport(Text, EntryPoint = "CTFontCreateWithName")]
    internal static extern nint FontCreate(nint name, double size, nint matrix);

    [DllImport(Text, EntryPoint = "CTLineCreateWithAttributedString")]
    internal static extern nint LineCreate(nint attributed);

    [DllImport(Text, EntryPoint = "CTLineGetStringRange")]
    internal static extern Range LineStringRange(nint line);

    [DllImport(Text, EntryPoint = "CTLineGetTypographicBounds")]
    internal static extern double LineBounds(nint line, out double ascent,
        out double descent, out double leading);

    [DllImport(Text, EntryPoint = "CTLineGetOffsetForStringIndex")]
    internal static extern double OffsetForIndex(nint line, nint index, out double secondary);

    [DllImport(Text, EntryPoint = "CTLineGetStringIndexForPosition")]
    internal static extern nint IndexForPosition(nint line, Point point);

    [DllImport(Text, EntryPoint = "CTLineDraw")]
    internal static extern void LineDraw(nint line, nint context);

    [DllImport(Graphics, EntryPoint = "CGColorSpaceCreateDeviceRGB")]
    internal static extern nint ColorSpaceCreateRgb();

    [DllImport(Graphics, EntryPoint = "CGColorSpaceRelease")]
    internal static extern void ColorSpaceRelease(nint colorSpace);

    [DllImport(Graphics, EntryPoint = "CGBitmapContextCreate")]
    internal static extern nint BitmapCreate(nint data, nuint width, nuint height,
        nuint bitsPerComponent, nuint bytesPerRow, nint colorSpace, uint bitmapInfo);

    [DllImport(Graphics, EntryPoint = "CGContextRelease")]
    internal static extern void ContextRelease(nint context);

    [DllImport(Graphics, EntryPoint = "CGContextSetRGBFillColor")]
    internal static extern void SetFillColor(nint context, double red, double green,
        double blue, double alpha);

    [DllImport(Graphics, EntryPoint = "CGContextSetTextPosition")]
    internal static extern void SetTextPosition(nint context, double x, double y);

    /// <summary>Resolves the exported system CFStringRef for the CoreText font attribute.</summary>
    internal static nint FontAttributeName
    {
        get
        {
            var framework = NativeLibrary.Load(Text);
            try { return Marshal.ReadIntPtr(NativeLibrary.GetExport(framework, "kCTFontAttributeName")); }
            finally { NativeLibrary.Free(framework); }
        }
    }
}
