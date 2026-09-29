using System.Runtime.InteropServices;
using Mote.Themes;

namespace Mote.Native.Windows.Canvas;

/// <summary>Blittable Windows structures and OS entry points for the on-screen canvas.</summary>
internal static unsafe class CanvasWin32
{
    internal const int WmPaint = 0x000F;
    internal const int WmSize = 0x0005;
    internal const int WmDestroy = 0x0002;
    internal const int WmMouseWheel = 0x020A;
    internal const int WmLButtonDown = 0x0201;
    internal const int WmMouseMove = 0x0200;
    internal const int WmLButtonUp = 0x0202;
    internal const int WmVScroll = 0x0115;
    internal const int WmAppProbe = Win32.WM_APP + 20;
    internal const int MkLButton = 1;
    internal const uint Srccopy = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    internal struct PaintStruct
    {
        internal nint Dc;
        internal int Erase;
        internal Win32.Rect Paint;
        internal int Restore;
        internal int IncrementalUpdate;
        internal fixed byte Reserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        internal uint Size;
        internal int Width;
        internal int Height;
        internal ushort Planes;
        internal ushort BitCount;
        internal uint Compression;
        internal uint ImageSize;
        internal int XPelsPerMeter;
        internal int YPelsPerMeter;
        internal uint ColorsUsed;
        internal uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        internal BitmapInfoHeader Header;
        internal uint FirstColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ScrollInfo
    {
        internal uint Size;
        internal uint Mask;
        internal int Minimum;
        internal int Maximum;
        internal uint Page;
        internal int Position;
        internal int TrackPosition;
    }

    [DllImport("user32.dll")]
    internal static extern nint BeginPaint(nint window, out PaintStruct paint);
    [DllImport("user32.dll")]
    internal static extern bool EndPaint(nint window, ref PaintStruct paint);
    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint window);
    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint window, nint dc);
    [DllImport("user32.dll")]
    internal static extern nint SetCapture(nint window);
    [DllImport("user32.dll")]
    internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    internal static extern int SetScrollInfo(nint window, int bar, ref ScrollInfo info, bool redraw);
    [DllImport("user32.dll")]
    internal static extern bool GetScrollInfo(nint window, int bar, ref ScrollInfo info);
    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")]
    internal static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")]
    internal static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint colorUse,
        out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")]
    internal static extern bool BitBlt(nint dest, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, uint operation);
}

/// <summary>One bounded DirectWrite style range relative to a painted slice.</summary>
internal readonly record struct WindowsCanvasColorSpan(int Start, int Length, ThemeColor Color);

/// <summary>Direct2D DC rendering plus DirectWrite layout, using system COM interfaces only.</summary>
internal sealed unsafe class WindowsCanvasPainter : IDisposable
{
    private static readonly Guid D2DFactoryId = new("06152247-6f50-465a-9245-118bfd3b6007");
    private static readonly Guid DWriteFactoryId = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
    private nint _d2dFactory;
    private nint _dwriteFactory;
    private nint _textFormat;
    private nint _renderTarget;
    private nint _textBrush;
    private nint _selectionBrush;
    private nint _selectedTextBrush;
    private readonly Dictionary<ThemeColor, nint> _semanticBrushes = new();

    /// <summary>Creates a reusable painter for one OS theme and font policy.</summary>
    internal WindowsCanvasPainter(IThemePolicy theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var hr = D2D1CreateFactory(0, in D2DFactoryId, 0, out _d2dFactory);
        Check(hr, "D2D1CreateFactory");
        try
        {
            Check(DWriteCreateFactory(0, in DWriteFactoryId, out _dwriteFactory),
                "DWriteCreateFactory");
            var familyName = theme.Typography.EditorFontFamilies.Split(',', 2)[0].Trim();
            if (familyName.Length == 0) familyName = "Consolas";
            fixed (char* family = familyName)
            fixed (char* locale = "en-us")
            {
                var createFormat = (delegate* unmanaged[Stdcall]<nint, char*, nint, int,
                    int, int, float, char*, nint*, int>)Slot(_dwriteFactory, 15);
                nint format = 0;
                Check(createFormat(_dwriteFactory, family, 0, 400, 0, 5,
                    (float)theme.Typography.EditorFontSize, locale, &format),
                    "IDWriteFactory::CreateTextFormat");
                _textFormat = format;
            }
            var noWrap = (delegate* unmanaged[Stdcall]<nint, int, int>)Slot(_textFormat, 5);
            Check(noWrap(_textFormat, 1), "IDWriteTextFormat::SetWordWrapping");
            var properties = new RenderTargetProperties
            {
                Type = 0,
                PixelFormat = new PixelFormat { Format = 87, AlphaMode = 1 },
                DpiX = 96,
                DpiY = 96,
                Usage = 0,
                MinLevel = 0
            };
            var createTarget = (delegate* unmanaged[Stdcall]<nint, RenderTargetProperties*,
                nint*, int>)Slot(_d2dFactory, 16);
            nint target = 0;
            Check(createTarget(_d2dFactory, &properties, &target),
                "ID2D1Factory::CreateDCRenderTarget");
            _renderTarget = target;
            _textBrush = CreateBrush(theme.Palette.EditorForeground);
            _selectionBrush = CreateBrush(theme.Palette.SelectionBackground);
            _selectedTextBrush = CreateBrush(theme.Palette.SelectionForeground);
            Background = Color(theme.Palette.EditorBackground);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Editor background color used by Clear on every paint.</summary>
    internal ColorF Background { get; }

    /// <summary>Associates the OS render target with the currently selected 32-bpp DIB.</summary>
    internal void Bind(nint dc, int width, int height)
    {
        var rect = new Win32.Rect { Right = width, Bottom = height };
        var bind = (delegate* unmanaged[Stdcall]<nint, nint, Win32.Rect*, int>)Slot(_renderTarget, 57);
        Check(bind(_renderTarget, dc, &rect), "ID2D1DCRenderTarget::BindDC");
    }

    /// <summary>Begins an OS paint and clears the prior frame.</summary>
    internal void Begin()
    {
        var begin = (delegate* unmanaged[Stdcall]<nint, void>)Slot(_renderTarget, 48);
        begin(_renderTarget);
        var background = Background;
        var clear = (delegate* unmanaged[Stdcall]<nint, ColorF*, void>)Slot(_renderTarget, 47);
        clear(_renderTarget, &background);
    }

    /// <summary>Draws one highlighted selection rectangle before its text glyphs.</summary>
    internal void Selection(float left, float top, float right, float bottom)
    {
        if (right <= left || bottom <= top) return;
        var rect = new RectF { Left = left, Top = top, Right = right, Bottom = bottom };
        var fill = (delegate* unmanaged[Stdcall]<nint, RectF*, nint, void>)Slot(_renderTarget, 17);
        fill(_renderTarget, &rect, _selectionBrush);
    }

    /// <summary>Draws a severity-colored two-pixel diagnostic mark on a visible glyph run.</summary>
    internal void DiagnosticUnderline(float left, float top, float right, ThemeColor color)
    {
        if (right <= left) right = left + 8;
        var rect = new RectF { Left = left, Top = top, Right = right, Bottom = top + 2 };
        var fill = (delegate* unmanaged[Stdcall]<nint, RectF*, nint, void>)Slot(_renderTarget, 17);
        fill(_renderTarget, &rect, SemanticBrush(color));
    }

    /// <summary>Shapes and paints one bounded visible UTF-16 slice at its viewport origin.</summary>
    internal void Text(string text, float x, float y,
        (float Left, float Top, float Right, float Bottom)? selected,
        IReadOnlyList<WindowsCanvasColorSpan>? colors = null)
    {
        if (text.Length == 0) return;
        fixed (char* characters = text)
        {
            var create = (delegate* unmanaged[Stdcall]<nint, char*, uint, nint, float,
                float, nint*, int>)Slot(_dwriteFactory, 18);
            nint layout = 0;
            Check(create(_dwriteFactory, characters, checked((uint)text.Length), _textFormat,
                100_000, 10_000, &layout), "IDWriteFactory::CreateTextLayout");
            try
            {
                if (colors is not null)
                {
                    var setEffect = (delegate* unmanaged[Stdcall]<nint, nint,
                        TextRange, int>)Slot(layout, 38);
                    foreach (var span in colors)
                    {
                        if (span.Start < 0 || span.Length <= 0 ||
                            span.Start > text.Length - span.Length) continue;
                        var brush = SemanticBrush(span.Color);
                        Check(setEffect(layout, brush, new TextRange
                        {
                            Start = (uint)span.Start, Length = (uint)span.Length
                        }), "IDWriteTextLayout::SetDrawingEffect");
                    }
                }
                var origin = new PointF { X = x, Y = y };
                var draw = (delegate* unmanaged[Stdcall]<nint, PointF, nint, nint,
                    int, void>)Slot(_renderTarget, 28);
                draw(_renderTarget, origin, layout, _textBrush, 0);
                if (selected is { } bounds && bounds.Right > bounds.Left &&
                    bounds.Bottom > bounds.Top)
                {
                    var clip = new RectF
                    {
                        Left = bounds.Left, Top = bounds.Top,
                        Right = bounds.Right, Bottom = bounds.Bottom
                    };
                    var push = (delegate* unmanaged[Stdcall]<nint, RectF*, int, void>)
                        Slot(_renderTarget, 45);
                    var pop = (delegate* unmanaged[Stdcall]<nint, void>)
                        Slot(_renderTarget, 46);
                    push(_renderTarget, &clip, 0);
                    try { draw(_renderTarget, origin, layout, _selectedTextBrush, 0); }
                    finally { pop(_renderTarget); }
                }
            }
            finally { Release(layout); }
        }
    }

    /// <summary>Ends one Direct2D paint, surfacing device-loss or COM failures.</summary>
    internal void End()
    {
        var end = (delegate* unmanaged[Stdcall]<nint, ulong*, ulong*, int>)Slot(_renderTarget, 49);
        Check(end(_renderTarget, null, null), "ID2D1RenderTarget::EndDraw");
    }

    /// <summary>Releases all COM references in reverse ownership order.</summary>
    public void Dispose()
    {
        foreach (var brush in _semanticBrushes.Values) Release(brush);
        _semanticBrushes.Clear();
        Release(_selectedTextBrush); _selectedTextBrush = 0;
        Release(_selectionBrush); _selectionBrush = 0;
        Release(_textBrush); _textBrush = 0;
        Release(_renderTarget); _renderTarget = 0;
        Release(_textFormat); _textFormat = 0;
        Release(_dwriteFactory); _dwriteFactory = 0;
        Release(_d2dFactory); _d2dFactory = 0;
    }

    private nint CreateBrush(ThemeColor color)
    {
        var rgba = Color(color);
        var create = (delegate* unmanaged[Stdcall]<nint, ColorF*, nint, nint*, int>)
            Slot(_renderTarget, 8);
        nint brush = 0;
        Check(create(_renderTarget, &rgba, 0, &brush), "ID2D1RenderTarget::CreateSolidColorBrush");
        return brush;
    }

    private nint SemanticBrush(ThemeColor color)
    {
        if (_semanticBrushes.TryGetValue(color, out var brush)) return brush;
        brush = CreateBrush(color);
        _semanticBrushes.Add(color, brush);
        return brush;
    }

    private static ColorF Color(ThemeColor color) => new()
    {
        Red = color.Red / 255f, Green = color.Green / 255f,
        Blue = color.Blue / 255f, Alpha = 1
    };

    private static nint Slot(nint value, int index) => ((nint*)(*(nint*)value))[index];

    private static void Release(nint value)
    {
        if (value == 0) return;
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)Slot(value, 2);
        release(value);
    }

    private static void Check(int hr, string operation)
    {
        if (hr < 0) throw new COMException($"{operation} failed.", hr);
    }

    [DllImport("d2d1.dll", ExactSpelling = true)]
    private static extern int D2D1CreateFactory(int type, in Guid iid, nint options,
        out nint factory);
    [DllImport("dwrite.dll", ExactSpelling = true)]
    private static extern int DWriteCreateFactory(int type, in Guid iid, out nint factory);

    [StructLayout(LayoutKind.Sequential)]
    internal struct ColorF
    {
        internal float Red, Green, Blue, Alpha;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormat
    {
        internal int Format, AlphaMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RenderTargetProperties
    {
        internal int Type;
        internal PixelFormat PixelFormat;
        internal float DpiX, DpiY;
        internal int Usage, MinLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointF
    {
        internal float X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectF
    {
        internal float Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextRange
    {
        internal uint Start, Length;
    }
}
