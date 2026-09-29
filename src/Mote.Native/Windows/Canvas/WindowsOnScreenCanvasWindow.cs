using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Engine;
using Mote.Native.Viewport;
using Mote.Themes;

namespace Mote.Native.Windows.Canvas;

/// <summary>
/// A real, top-level, read-only Win32 text window. The engine snapshot owns text;
/// every OS paint requests only the currently visible bounded source slices.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsOnScreenCanvasWindow : IDisposable
{
    private const string ClassName = "MoteDirectWriteCanvasProbe";
    private const int ScrollRange = 1_000_000;
    private const float TextLeft = 24;
    private static readonly Win32.WindowProcedure WindowProcedure = Dispatch;
    private static WindowsOnScreenCanvasWindow? _creating;
    private static WindowsOnScreenCanvasWindow? _active;
    private readonly WindowsDirectWriteCanvas _geometry;
    private readonly WindowsCanvasPainter _painter;
    private readonly double _lineHeight;
    private CanvasInteraction _interaction;
    private Action<WindowsOnScreenCanvasWindow>? _automation;
    private nint _window;
    private nint _memoryDc;
    private nint _bitmap;
    private nint _priorBitmap;
    private nint _bits;
    private int _width;
    private int _height;
    private int _paintCount;
    private int _maxShapedSlice;
    private bool _dragging;
    private bool _disposed;
    private Exception? _error;

    /// <summary>Creates the read-only canvas over one immutable engine snapshot.</summary>
    internal WindowsOnScreenCanvasWindow(TextSnapshot snapshot, IThemePolicy theme)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(theme);
        _lineHeight = Math.Max(16, theme.Typography.EditorFontSize * theme.Typography.LineHeightMultiplier);
        _interaction = new CanvasInteraction(snapshot, _lineHeight, 700, maxSliceLength: 4096);
        _geometry = new WindowsDirectWriteCanvas(
            theme.Typography.EditorFontFamilies.Split(',', 2)[0].Trim(),
            (float)theme.Typography.EditorFontSize);
        _painter = new WindowsCanvasPainter(theme);
    }

    /// <summary>The window's actual OS handle while visible.</summary>
    internal nint Handle => _window;

    /// <summary>The current source-backed viewport and global selection.</summary>
    internal CanvasFrame Frame => _interaction.Frame();

    /// <summary>Number of completed WM_PAINT frames, excluding offscreen-only shaping.</summary>
    internal int PaintCount => _paintCount;

    /// <summary>Largest source slice sent to DirectWrite by this window.</summary>
    internal int MaxShapedSlice => _maxShapedSlice;

    /// <summary>Starts the real Win32 message loop and runs bounded automation on WM_APP.</summary>
    internal void Run(Action<WindowsOnScreenCanvasWindow> automation)
    {
        ArgumentNullException.ThrowIfNull(automation);
        if (_window != 0) throw new InvalidOperationException("The canvas window is already open.");
        _automation = automation;
        var instance = Win32.GetModuleHandleW(null);
        var windowClass = new Win32.WindowClass
        {
            WindowProc = WindowProcedure,
            Instance = instance,
            ClassName = ClassName
        };
        var atom = Win32.RegisterClassW(ref windowClass);
        if (atom == 0 && Marshal.GetLastPInvokeError() != 1410)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot register canvas window.");
        _creating = this;
        _active = this;
        try
        {
            var created = Win32.CreateWindowExW(0, ClassName, "mote DirectWrite canvas probe",
                Win32.WS_OVERLAPPEDWINDOW | Win32.WS_VISIBLE | Win32.WS_VSCROLL,
                Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT, 1100, 760,
                0, 0, instance, 0);
            if (created == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot create canvas window.");
            _window = created;
            Win32.ShowWindow(created, 5);
            Win32.UpdateWindow(created);
            Win32.PostMessageW(created, CanvasWin32.WmAppProbe, 0, 0);
            while (true)
            {
                var result = Win32.GetMessageW(out var message, 0, 0, 0);
                if (result == 0) break;
                if (result < 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                Win32.TranslateMessage(ref message);
                Win32.DispatchMessageW(ref message);
            }
            if (_error is not null) throw new InvalidOperationException("Canvas OS dispatch failed.", _error);
        }
        finally
        {
            _creating = null;
            _active = null;
            if (_window != 0) Win32.DestroyWindow(_window);
            ReleaseBitmap();
        }
    }

    /// <summary>Changes the bound document without retaining or copying its full text.</summary>
    internal void Bind(TextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _interaction = new CanvasInteraction(snapshot, _lineHeight,
            Math.Max(_height, 1), maxSliceLength: 4096);
        _maxShapedSlice = 0;
        Invalidate();
    }

    /// <summary>Moves the continuous source anchor to a remote source boundary.</summary>
    internal void Reveal(int sourceOffset)
    {
        _interaction.Reveal(sourceOffset);
        UpdateScrollbar();
        Invalidate();
    }

    /// <summary>Checks a source cluster against the geometry of this visible HWND viewport.</summary>
    internal void ProbeRoundTrip(int? sourceOffset = null)
    {
        var frame = _interaction.Frame();
        var slice = frame.Slices.FirstOrDefault(item => item.SourceLength > 0 &&
            (sourceOffset is null || sourceOffset >= item.SourceStart &&
                sourceOffset < item.SourceStart + item.SourceLength));
        if (slice.SourceLength == 0)
            throw new InvalidOperationException("No visible glyph cluster can be hit-tested.");
        var offset = sourceOffset ?? slice.SourceStart;
        var hit = _geometry.HitTest(_interaction.Snapshot, slice, offset);
        var x = hit.X + (hit.BidiLevel % 2 == 0 ? 1 : -1) *
            Math.Max(0.05f, hit.Width * 0.25f);
        var point = _geometry.HitTestPoint(_interaction.Snapshot, slice,
            x, hit.Y + hit.Height * 0.5f);
        if (!point.IsInside || offset < point.SourceStart ||
            offset >= point.SourceStart + Math.Max(point.SourceLength, 1))
            throw new InvalidOperationException("Visible DirectWrite hit-test lost its source cluster.");
    }

    /// <summary>Completes pending WM_PAINT and writes the displayed DIB pixels as PNG.</summary>
    internal void Capture(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_window == 0) throw new InvalidOperationException("Canvas window is not visible.");
        Win32.UpdateWindow(_window);
        if (_paintCount == 0 || _bits == 0)
            throw new InvalidOperationException("No on-screen WM_PAINT frame was produced.");
        var bytes = new byte[checked(_width * _height * 4)];
        Marshal.Copy(_bits, bytes, 0, bytes.Length);
        WindowsOnScreenCanvasPng.Write(path, _width, _height, bytes);
    }

    /// <summary>Closes the OS window after probe automation.</summary>
    internal void Close()
    {
        if (_window != 0) Win32.DestroyWindow(_window);
    }

    /// <summary>Releases the DirectWrite/Direct2D resources and any OS bitmap.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_window != 0) Win32.DestroyWindow(_window);
        ReleaseBitmap();
        _painter.Dispose();
        _geometry.Dispose();
    }

    private static nint Dispatch(nint window, uint message, nuint wParam, nint lParam)
    {
        var canvas = _creating ?? _active;
        if (canvas is null || (canvas._window != 0 && canvas._window != window))
            return Win32.DefWindowProcW(window, message, wParam, lParam);
        try { return canvas.HandleMessage(window, message, wParam, lParam); }
        catch (Exception ex)
        {
            canvas._error = ex;
            Win32.PostQuitMessage(1);
            return 0;
        }
    }

    private nint HandleMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case Win32.WM_CREATE:
                _window = window;
                return 0;
            case CanvasWin32.WmSize:
                _width = Math.Max(1, (int)(ushort)((long)lParam & 0xFFFF));
                _height = Math.Max(1, (int)(ushort)(((long)lParam >> 16) & 0xFFFF));
                _interaction.Resize(_height);
                ReleaseBitmap();
                UpdateScrollbar();
                Invalidate();
                return 0;
            case CanvasWin32.WmPaint:
                Paint();
                return 0;
            case CanvasWin32.WmMouseWheel:
                var delta = (short)(((ulong)wParam >> 16) & 0xFFFF);
                _interaction.ScrollBy(-delta / 120d * 3 * _lineHeight);
                UpdateScrollbar();
                Invalidate();
                return 0;
            case CanvasWin32.WmVScroll:
                Scrollbar((int)(wParam & 0xFFFF));
                return 0;
            case CanvasWin32.WmLButtonDown:
                _dragging = true;
                CanvasWin32.SetCapture(window);
                _interaction.BeginSelection(HitSource(lParam));
                Invalidate();
                return 0;
            case CanvasWin32.WmMouseMove:
                if (_dragging)
                {
                    var y = SignedHigh(lParam);
                    if (y < 0 || y >= _height)
                        _interaction.ScrollBy((y < 0 ? -1 : 1) * _lineHeight);
                    _interaction.ExtendSelection(HitSource(lParam));
                    UpdateScrollbar();
                    Invalidate();
                }
                return 0;
            case CanvasWin32.WmLButtonUp:
                if (_dragging)
                {
                    _interaction.ExtendSelection(HitSource(lParam));
                    _interaction.EndSelection();
                    _dragging = false;
                    CanvasWin32.ReleaseCapture();
                    Invalidate();
                }
                return 0;
            case CanvasWin32.WmAppProbe:
                _automation?.Invoke(this);
                return 0;
            case Win32.WM_CLOSE:
                Win32.DestroyWindow(window);
                return 0;
            case CanvasWin32.WmDestroy:
                _window = 0;
                Win32.PostQuitMessage(0);
                return 0;
            default:
                return Win32.DefWindowProcW(window, message, wParam, lParam);
        }
    }

    private void Paint()
    {
        var dc = CanvasWin32.BeginPaint(_window, out var paint);
        if (dc == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "BeginPaint failed.");
        try
        {
            EnsureBitmap();
            _painter.Bind(_memoryDc, _width, _height);
            _painter.Begin();
            var frame = _interaction.Frame();
            foreach (var slice in frame.Slices)
            {
                if (slice.SourceLength > 16 * 1024)
                    throw new InvalidOperationException("Visible shaping exceeded its hard bound.");
                _maxShapedSlice = Math.Max(_maxShapedSlice, slice.SourceLength);
                var selected = DrawSelection(slice, frame);
                var text = _interaction.Snapshot.GetText(slice.SourceStart, slice.SourceLength);
                _painter.Text(text, TextLeft, (float)slice.TopY, selected);
            }
            _painter.End();
            if (!CanvasWin32.BitBlt(dc, 0, 0, _width, _height,
                _memoryDc, 0, 0, CanvasWin32.Srccopy))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "BitBlt failed.");
            _paintCount++;
        }
        finally { CanvasWin32.EndPaint(_window, ref paint); }
    }

    private (float Left, float Top, float Right, float Bottom)? DrawSelection(
        ViewportSlice slice, CanvasFrame frame)
    {
        var selectionEnd = frame.SelectionStart + frame.SelectionLength;
        var contentEnd = slice.SourceStart + slice.SourceLength;
        var start = Math.Max(slice.SourceStart, frame.SelectionStart);
        var end = Math.Min(contentEnd, selectionEnd);
        (float Left, float Top, float Right, float Bottom)? bounds = null;
        if (end > start)
        {
            var left = _geometry.HitTest(_interaction.Snapshot, slice, start);
            var right = _geometry.HitTest(_interaction.Snapshot, slice, end);
            bounds = (
                Left: TextLeft + Math.Min(left.X, right.X),
                Top: (float)slice.TopY,
                Right: TextLeft + Math.Max(left.X, right.X),
                Bottom: (float)(slice.TopY + slice.Height));
            _painter.Selection(bounds.Value.Left, bounds.Value.Top,
                bounds.Value.Right, bounds.Value.Bottom);
        }

        // Source line endings are excluded from ViewportSlice text but still selectable.
        // Painting through the right margin makes an empty selected row visible too.
        var nextLine = slice.Line + 1 < _interaction.Snapshot.LineCount
            ? _interaction.Snapshot.GetLineStartOffset(slice.Line + 1) : contentEnd;
        if (!slice.HasHiddenSuffix && nextLine > contentEnd &&
            frame.SelectionStart < nextLine && selectionEnd > contentEnd)
        {
            var endX = slice.SourceLength == 0 ? TextLeft :
                TextLeft + _geometry.HitTest(_interaction.Snapshot, slice, contentEnd).X;
            _painter.Selection(endX, (float)slice.TopY,
                Math.Max(endX + 8, _width - 8), (float)(slice.TopY + slice.Height));
        }
        return bounds;
    }

    private int HitSource(nint lParam)
    {
        var x = Math.Max(0, SignedLow(lParam) - TextLeft);
        var y = Math.Clamp(SignedHigh(lParam), 0, Math.Max(0, _height - 1));
        var frame = _interaction.Frame();
        if (frame.Slices.Count == 0) return frame.TopAnchor.SourceOffset;
        ViewportSlice? matched = null;
        foreach (var row in frame.Slices)
        {
            if (y < row.TopY || y >= row.TopY + row.Height) continue;
            matched = row;
            break;
        }
        var slice = matched ?? frame.Slices[^1];
        if (slice.SourceLength == 0) return slice.SourceStart;
        var hit = _geometry.HitTestPoint(_interaction.Snapshot, slice, x, y);
        var offset = hit.SourceStart + (hit.IsTrailing ? hit.SourceLength : 0);
        return Math.Clamp(offset, slice.SourceStart, slice.SourceStart + slice.SourceLength);
    }

    private void Scrollbar(int command)
    {
        var delta = command switch
        {
            0 => -_lineHeight,
            1 => _lineHeight,
            2 => -_height,
            3 => _height,
            4 or 5 => ThumbDelta(),
            6 => -_interaction.Frame().ScrollY,
            7 => TotalHeight() - _height - _interaction.Frame().ScrollY,
            _ => 0
        };
        _interaction.ScrollBy(delta);
        UpdateScrollbar();
        Invalidate();
    }

    private double ThumbDelta()
    {
        var info = new CanvasWin32.ScrollInfo { Size = (uint)Marshal.SizeOf<CanvasWin32.ScrollInfo>(), Mask = 0x10 };
        if (!CanvasWin32.GetScrollInfo(_window, 1, ref info)) return 0;
        return info.TrackPosition / (double)ScrollRange * TotalHeight() - _interaction.Frame().ScrollY;
    }

    private void UpdateScrollbar()
    {
        if (_window == 0) return;
        var total = TotalHeight();
        var frame = _interaction.Frame();
        var info = new CanvasWin32.ScrollInfo
        {
            Size = (uint)Marshal.SizeOf<CanvasWin32.ScrollInfo>(),
            Mask = 0x17, // SIF_RANGE | SIF_PAGE | SIF_POS | SIF_TRACKPOS.
            Minimum = 0, Maximum = ScrollRange,
            Page = (uint)Math.Clamp(_height / total * ScrollRange, 1, ScrollRange),
            Position = (int)Math.Clamp(frame.ScrollY / total * ScrollRange, 0, ScrollRange)
        };
        CanvasWin32.SetScrollInfo(_window, 1, ref info, true);
    }

    private double TotalHeight() => Math.Max(_height, _interaction.Snapshot.LineCount * _lineHeight);

    private void EnsureBitmap()
    {
        if (_memoryDc != 0 && _bitmap != 0) return;
        _memoryDc = CanvasWin32.CreateCompatibleDC(0);
        if (_memoryDc == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateCompatibleDC failed.");
        var info = new CanvasWin32.BitmapInfo
        {
            Header = new CanvasWin32.BitmapInfoHeader
            {
                Size = 40, Width = _width, Height = -_height,
                Planes = 1, BitCount = 32, Compression = 0
            }
        };
        _bitmap = CanvasWin32.CreateDIBSection(0, ref info, 0, out _bits, 0, 0);
        if (_bitmap == 0 || _bits == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateDIBSection failed.");
        _priorBitmap = CanvasWin32.SelectObject(_memoryDc, _bitmap);
    }

    private void ReleaseBitmap()
    {
        if (_memoryDc != 0 && _priorBitmap != 0)
            CanvasWin32.SelectObject(_memoryDc, _priorBitmap);
        _priorBitmap = 0;
        if (_bitmap != 0) CanvasWin32.DeleteObject(_bitmap);
        _bitmap = 0;
        _bits = 0;
        if (_memoryDc != 0) CanvasWin32.DeleteDC(_memoryDc);
        _memoryDc = 0;
    }

    private void Invalidate()
    {
        if (_window != 0) Win32.InvalidateRect(_window, 0, false);
    }

    private static short SignedLow(nint value) => (short)((long)value & 0xFFFF);

    private static short SignedHigh(nint value) => (short)(((long)value >> 16) & 0xFFFF);
}
