using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Engine;
using Mote.Native.Viewport;

namespace Mote.Native.Windows.Canvas;

/// <summary>One DirectWrite hit-test result mapped back to an immutable source slice.</summary>
internal readonly record struct WindowsCanvasHit(
    int SourceStart, int SourceLength, float X, float Y, float Width, float Height,
    int BidiLevel, bool IsText, bool IsInside, bool IsTrailing);

/// <summary>
/// Shapes and hit-tests bounded source-backed rows with OS DirectWrite. This is a
/// read-only geometry adapter, not an input method or editor text owner.
/// </summary>
/// <remarks>
/// COM is called through its stable vtable ABI to avoid generated interop assemblies
/// or runtime COM wrappers in Native AOT. Indices are for the base IDWriteFactory,
/// IDWriteTextFormat and IDWriteTextLayout interfaces in the Windows SDK; extended
/// interfaces are deliberately not cast to these slots. One instance owns a shared
/// factory and text format. A layout exists only for one bounded slice call.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsDirectWriteCanvas : IDisposable
{
    private static readonly Guid FactoryId = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
    private nint _factory;
    private nint _format;
    private bool _disposed;

    /// <summary>Creates an OS-backed no-wrap text shaper for visible line slices.</summary>
    public WindowsDirectWriteCanvas(string fontFamily = "Consolas", float fontSize = 14)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        if (!float.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Check(DWriteCreateFactory(0, in FactoryId, out _factory), "DWriteCreateFactory");
        try
        {
            fixed (char* family = fontFamily)
            fixed (char* locale = "en-us")
            {
                // IUnknown(3) + IDWriteFactory::CreateTextFormat(12).
                var create = (delegate* unmanaged[Stdcall]<nint, char*, nint, int, int, int,
                    float, char*, nint*, int>)Slot(_factory, 15);
                nint format = 0;
                Check(create(_factory, family, 0, 400, 0, 5, fontSize, locale, &format),
                    "IDWriteFactory::CreateTextFormat");
                _format = format;
            }
            // IUnknown(3) + IDWriteTextFormat::SetWordWrapping(2); 1 = no wrap.
            var wrapping = (delegate* unmanaged[Stdcall]<nint, int, int>)Slot(_format, 5);
            Check(wrapping(_format, 1), "IDWriteTextFormat::SetWordWrapping");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Hit-tests a UTF-16 boundary in one slice. The returned cluster can cover more
    /// than one code unit for a ligature, combining sequence or surrogate pair.
    /// </summary>
    /// <example><code>
    /// using var canvas = new WindowsDirectWriteCanvas();
    /// var hit = canvas.HitTest(snapshot, slice, slice.SourceStart + 2);
    /// </code></example>
    public WindowsCanvasHit HitTest(TextSnapshot snapshot, ViewportSlice slice, int sourceOffset)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSlice(snapshot, slice);
        if (sourceOffset < slice.SourceStart || sourceOffset > slice.SourceStart + slice.SourceLength)
            throw new ArgumentOutOfRangeException(nameof(sourceOffset));
        using var layout = CreateLayout(snapshot, slice);
        var position = checked((uint)(sourceOffset - slice.SourceStart));
        float x = 0, y = 0;
        var metric = new HitTestMetric();
        // IUnknown(3) + IDWriteTextFormat(25) + IDWriteTextLayout::HitTestTextPosition(37).
        var hit = (delegate* unmanaged[Stdcall]<nint, uint, int, float*, float*,
            HitTestMetric*, int>)Slot(layout.Value, 65);
        Check(hit(layout.Value, position, 0, &x, &y, &metric),
            "IDWriteTextLayout::HitTestTextPosition");
        return Map(slice, metric, x, y, inside: true, trailing: false);
    }

    /// <summary>Maps a visual point in one row back to a UTF-16 source cluster.</summary>
    public WindowsCanvasHit HitTestPoint(TextSnapshot snapshot, ViewportSlice slice,
        float x, float viewportY)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSlice(snapshot, slice);
        if (!float.IsFinite(x) || !float.IsFinite(viewportY))
            throw new ArgumentOutOfRangeException(nameof(x));
        using var layout = CreateLayout(snapshot, slice);
        var y = viewportY - (float)slice.TopY;
        int trailing = 0, inside = 0;
        var metric = new HitTestMetric();
        // IUnknown(3) + IDWriteTextFormat(25) + IDWriteTextLayout::HitTestPoint(36).
        var hit = (delegate* unmanaged[Stdcall]<nint, float, float, int*, int*,
            HitTestMetric*, int>)Slot(layout.Value, 64);
        Check(hit(layout.Value, x, y, &trailing, &inside, &metric),
            "IDWriteTextLayout::HitTestPoint");
        return Map(slice, metric, metric.Left, metric.Top, inside != 0, trailing != 0);
    }

    /// <summary>Releases the format and shared factory references owned by this adapter.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_format != 0) { Release(_format); _format = 0; }
        if (_factory != 0) { Release(_factory); _factory = 0; }
    }

    private ComHandle CreateLayout(TextSnapshot snapshot, ViewportSlice slice)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var text = snapshot.GetText(slice.SourceStart, slice.SourceLength);
        fixed (char* pointer = text)
        {
            // IUnknown(3) + IDWriteFactory::CreateTextLayout(15).
            var create = (delegate* unmanaged[Stdcall]<nint, char*, uint, nint, float,
                float, nint*, int>)Slot(_factory, 18);
            nint layout = 0;
            Check(create(_factory, pointer, checked((uint)text.Length), _format,
                100_000, 10_000, &layout), "IDWriteFactory::CreateTextLayout");
            return new ComHandle(layout);
        }
    }

    private static WindowsCanvasHit Map(ViewportSlice slice, HitTestMetric metric,
        float x, float y, bool inside, bool trailing) =>
        new(slice.SourceStart + checked((int)metric.TextPosition),
            checked((int)metric.Length), x, (float)slice.TopY + y,
            metric.Width, metric.Height, checked((int)metric.BidiLevel),
            metric.IsText != 0, inside, trailing);

    private static void ValidateSlice(TextSnapshot snapshot, ViewportSlice slice)
    {
        if (slice.SourceStart < 0 || slice.SourceLength < 0 ||
            slice.SourceStart > snapshot.Length - slice.SourceLength)
            throw new ArgumentOutOfRangeException(nameof(slice));
        if (slice.SourceLength > 16 * 1024)
            throw new ArgumentOutOfRangeException(nameof(slice), "Shape only bounded visible text.");
    }

    private static void Check(int hr, string operation)
    {
        if (hr < 0) throw new COMException($"{operation} failed.", hr);
    }

    private static nint Slot(nint instance, int index) => ((nint*)(*(nint*)instance))[index];

    private static void Release(nint instance)
    {
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)Slot(instance, 2);
        release(instance);
    }

    [DllImport("dwrite.dll", ExactSpelling = true)]
    private static extern int DWriteCreateFactory(uint factoryType, in Guid iid, out nint factory);

    [StructLayout(LayoutKind.Sequential)]
    private struct HitTestMetric
    {
        internal uint TextPosition;
        internal uint Length;
        internal float Left;
        internal float Top;
        internal float Width;
        internal float Height;
        internal uint BidiLevel;
        internal int IsText;
        internal int IsTrimmed;
    }

    private readonly struct ComHandle(nint value) : IDisposable
    {
        internal nint Value { get; } = value;
        public void Dispose() { if (Value != 0) Release(Value); }
    }
}
