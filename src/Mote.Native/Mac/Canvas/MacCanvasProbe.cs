using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Engine;
using Mote.Native.Viewport;

namespace Mote.Native.Mac.Canvas;

/// <summary>One bounded CoreText shaping and CoreGraphics paint observation.</summary>
internal sealed record MacCanvasCaseResult(
    string Name, int SourceStart, int SourceLength, double Width,
    int LeftHitSource, int MiddleHitSource, int RightHitSource,
    int PaintedBytes);

/// <summary>OS-backed measurements for the four representative text directions/scripts.</summary>
internal sealed record MacCanvasProbeResult(IReadOnlyList<MacCanvasCaseResult> Cases);

/// <summary>
/// Shapes only source-backed visible slices, hit-tests their CoreText lines,
/// and paints them into an offscreen system CoreGraphics bitmap.
/// </summary>
/// <remarks>
/// This is a read-only geometry probe, not an editor canvas, IME surface, or
/// accessibility implementation. All coordinates are UTF-16 within one slice.
/// </remarks>
[SupportedOSPlatform("macos")]
internal static class MacCanvasProbe
{
    private const int BitmapHeight = 64;
    private static readonly string[] Lines =
        ["ASCII alpha 123", "中文混合文本", "مرحبا بالعالم", "A👩‍💻Z"];

    /// <summary>
    /// Runs bounded OS shaping/paint assertions and returns inspectable results.
    /// </summary>
    /// <example><code>var observations = MacCanvasProbe.Run();</code></example>
    internal static MacCanvasProbeResult Run()
    {
        using var document = new Document(string.Join('\n', Lines));
        var snapshot = document.Snapshot;
        var viewport = new ContinuousViewport(snapshot, 24, 4096);
        var slices = viewport.GetVisibleSlices(120, maxSlices: 8);
        if (slices.Count != Lines.Length)
            throw new InvalidOperationException("The bounded viewport omitted a probe line.");

        var fontName = CreateString("Menlo");
        try
        {
            var font = CoreTextNative.FontCreate(fontName, 18, 0);
            if (font == 0) throw new InvalidOperationException("CoreText could not create a font.");
            try
            {
                var attribute = CoreTextNative.FontAttributeName;
                if (attribute == 0) throw new InvalidOperationException("CoreText font attribute is unavailable.");
                var results = new List<MacCanvasCaseResult>(Lines.Length);
                for (var i = 0; i < slices.Count; i++)
                    results.Add(Measure(Lines[i], slices[i], snapshot, font, attribute));
                return new MacCanvasProbeResult(results);
            }
            finally { CoreTextNative.Release(font); }
        }
        finally { CoreTextNative.Release(fontName); }
    }

    private static MacCanvasCaseResult Measure(string name, ViewportSlice slice,
        TextSnapshot snapshot, nint font, nint fontAttribute)
    {
        if (slice.SourceLength != name.Length ||
            slice.SourceStart != snapshot.GetLineStartOffset(slice.Line) ||
            snapshot.GetText(slice.SourceStart, slice.SourceLength) != name)
            throw new InvalidOperationException("Viewport slice lost its source identity.");

        var cfText = CreateString(name);
        var attributed = CoreTextNative.AttributedCreateMutable(0, 0);
        if (attributed == 0)
        {
            CoreTextNative.Release(cfText);
            throw new InvalidOperationException("CoreFoundation could not create attributed text.");
        }
        try
        {
            CoreTextNative.AttributedReplace(attributed, new CoreTextNative.Range(0, 0), cfText);
            CoreTextNative.AttributedSetAttribute(attributed,
                new CoreTextNative.Range(0, name.Length), fontAttribute, font);
            var line = CoreTextNative.LineCreate(attributed);
            if (line == 0) throw new InvalidOperationException("CoreText could not shape a line.");
            try { return MeasureLine(name, slice, line); }
            finally { CoreTextNative.Release(line); }
        }
        finally
        {
            CoreTextNative.Release(attributed);
            CoreTextNative.Release(cfText);
        }
    }

    private static MacCanvasCaseResult MeasureLine(string name, ViewportSlice slice, nint line)
    {
        var range = CoreTextNative.LineStringRange(line);
        if (range.Location != 0 || range.Length != name.Length)
            throw new InvalidOperationException("CoreText line range does not match the source slice.");
        var width = CoreTextNative.LineBounds(line, out var ascent, out var descent, out var leading);
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(ascent) ||
            !double.IsFinite(descent) || !double.IsFinite(leading))
            throw new InvalidOperationException("CoreText returned invalid line geometry.");

        var midIndex = name.Length / 2;
        if (midIndex > 0 && midIndex < name.Length &&
            char.IsLowSurrogate(name[midIndex]) && char.IsHighSurrogate(name[midIndex - 1]))
            midIndex--;
        foreach (var index in new[] { 0, midIndex, name.Length })
        {
            var x = CoreTextNative.OffsetForIndex(line, index, out var secondary);
            if (!double.IsFinite(x) || !double.IsFinite(secondary))
                throw new InvalidOperationException("CoreText returned a non-finite caret offset.");
        }

        var left = Hit(line, slice, 0, name.Length);
        var middle = Hit(line, slice, width / 2, name.Length);
        var right = Hit(line, slice, width, name.Length);
        var painted = Paint(line, width);
        if (painted == 0) throw new InvalidOperationException("CoreText drew no pixels.");
        return new MacCanvasCaseResult(name, slice.SourceStart, slice.SourceLength,
            width, left, middle, right, painted);
    }

    private static int Hit(nint line, ViewportSlice slice, double x, int length)
    {
        var index = CoreTextNative.IndexForPosition(line, new CoreTextNative.Point(x, 0));
        if (index < 0 || index > length)
            throw new InvalidOperationException("CoreText hit test escaped the source slice.");
        return slice.SourceStart + checked((int)index);
    }

    private static int Paint(nint line, double lineWidth)
    {
        var width = Math.Clamp(checked((int)Math.Ceiling(lineWidth)) + 20, 32, 2048);
        var bytesPerRow = checked(width * 4);
        var byteCount = checked(bytesPerRow * BitmapHeight);
        var pixels = Marshal.AllocHGlobal(byteCount);
        try
        {
            Marshal.Copy(new byte[byteCount], 0, pixels, byteCount);
            var colorSpace = CoreTextNative.ColorSpaceCreateRgb();
            if (colorSpace == 0) throw new InvalidOperationException("CoreGraphics RGB is unavailable.");
            try
            {
                // kCGImageAlphaPremultipliedLast; bytes are never written to disk.
                var context = CoreTextNative.BitmapCreate(pixels, (nuint)width,
                    BitmapHeight, 8, (nuint)bytesPerRow, colorSpace, 1);
                if (context == 0) throw new InvalidOperationException("CoreGraphics bitmap creation failed.");
                try
                {
                    CoreTextNative.SetFillColor(context, 1, 1, 1, 1);
                    CoreTextNative.SetTextPosition(context, 8, 24);
                    CoreTextNative.LineDraw(line, context);
                }
                finally { CoreTextNative.ContextRelease(context); }
            }
            finally { CoreTextNative.ColorSpaceRelease(colorSpace); }
            var data = new byte[byteCount];
            Marshal.Copy(pixels, data, 0, byteCount);
            return data.Count(value => value != 0);
        }
        finally { Marshal.FreeHGlobal(pixels); }
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
}
