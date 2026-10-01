using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Themes;

namespace Mote.Native.Windows;

/// <summary>
/// Full-resident RichEdit capability experiment, deliberately independent of the
/// product shell, canvas, bounded input island and controller. The hosted runner
/// must enforce its execution guard before calling this factory.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsNativeSourceCapabilityProbe
{
    /// <summary>Creates one nonactivating owned window on the current STA UI thread.</summary>
    internal static INativeSourceDiagnosticHost Create(IThemePolicy theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("The native source probe requires an STA owner.");
        return new Host(theme);
    }

    /// <summary>Owns only a system RichEdit library reference and two private HWNDs.</summary>
    private sealed class Host : INativeSourceDiagnosticHost
    {
        /// <summary>Prevents activation even if Windows processes an incidental show request.</summary>
        private const uint NoActivate = 0x08000000;
        /// <summary>SCF_NOKBUPDATE from Richedit.h; 0x0080 is SMARTFONT, not this flag.</summary>
        private const uint NoKeyboardUpdate = 0x0020;
        private const int SetUndoLimit = Win32.WM_USER + 82;
        private const int EmptyUndoBuffer = 0x00CD;
        private const int CanUndo = 0x00C6;
        private const int CanRedo = Win32.WM_USER + 85;
        private const int ReplaceSelection = 0x00C2;
        private const int VerticalScroll = 0x0115;
        private const int ScrollBottom = 7;
        /// <summary>All operations and destruction are confined to this managed owner.</summary>
        private readonly int _owner = Environment.CurrentManagedThreadId;
        /// <summary>Default foreground used to clear previous semantic publication.</summary>
        private readonly ThemeColor _foreground;
        /// <summary>The module remains loaded until its child control has been destroyed.</summary>
        private nint _library;
        private nint _window;
        private nint _source;
        /// <summary>Exact current replica, never canonical Engine text or history.</summary>
        private string _display = "";
        private bool _disposed;

        /// <summary>Creates a fixed-size diagnostic window without setting global focus.</summary>
        internal Host(IThemePolicy theme)
        {
            _foreground = theme.Palette.EditorForeground;
            try
            {
                _library = Win32.LoadLibraryW(Path.Combine(Environment.SystemDirectory, "msftedit.dll"));
                if (_library == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                // STATIC supplies a system-owned window procedure. No product or
                // experiment singleton, controller dispatch or custom painter is reused.
                _window = Win32.CreateWindowExW(NoActivate, "STATIC", "mote native source capability",
                    0x00CA0000 | Win32.WS_CLIPCHILDREN, Win32.CW_USEDEFAULT,
                    Win32.CW_USEDEFAULT, 980, 720, 0, 0, 0, 0);
                if (_window == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (!Win32.GetClientRect(_window, out var bounds))
                    throw new InvalidOperationException("Owned native window client geometry is unavailable.");
                _source = Win32.CreateWindowExW(0, "RICHEDIT50W", "",
                    Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_VSCROLL |
                    Win32.WS_HSCROLL | Win32.ES_MULTILINE | Win32.ES_AUTOVSCROLL |
                    Win32.ES_AUTOHSCROLL | Win32.ES_NOHIDESEL | Win32.ES_WANTRETURN,
                    0, 0, bounds.Right, bounds.Bottom, _window, 0, 0, 0);
                if (_source == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                Configure(theme);
                Win32.ShowWindow(_window, 8); // SW_SHOWNA: never activate or focus.
                FlushDraw();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <inheritdoc />
        public string LayoutBackend => "RichEdit50W";

        /// <summary>Configures ordinary typography and makes Engine-owned history exclusive.</summary>
        private void Configure(IThemePolicy theme)
        {
            Win32.SendMessageW(_source, Win32.EM_EXLIMITTEXT, 0, int.MaxValue);
            Win32.SendMessageW(_source, SetUndoLimit, 0, 0);
            Win32.SendMessageW(_source, EmptyUndoBuffer, 0, 0);
            Win32.SendMessageW(_source, Win32.EM_AUTOURLDETECT, 0, 0);
            Win32.SendMessageW(_source, Win32.EM_SETBKGNDCOLOR, 0,
                (nint)ColorRef(theme.Palette.EditorBackground));
            var face = theme.Typography.EditorFontFamilies.Split(',')[0].Trim().Trim('"', '\'');
            if (face.Length == 0) face = "Consolas";
            if (face.Length >= 32) throw new ArgumentException("RichEdit font face exceeds CHARFORMAT capacity.");
            var format = ColorFormat(_foreground);
            format.Mask |= Win32.CFM_SIZE | Win32.CFM_FACE;
            format.Height = checked((int)Math.Round(theme.Typography.EditorFontSize * 15));
            format.FaceName = face;
            ApplyFormat(0, ref format);
        }

        /// <inheritdoc />
        public void Install(string display)
        {
            CheckOwner();
            ValidateDisplay(display);
            if (!Win32.SetWindowTextW(_source, display))
                throw new InvalidOperationException("RichEdit complete text import failed.");
            Win32.SendMessageW(_source, EmptyUndoBuffer, 0, 0);
            // This is the intended replica until the runner's separately timed
            // full readback proves installation. Do not hide readback in import.
            _display = display;
        }

        /// <inheritdoc />
        public string ReadText()
        {
            CheckOwner();
            return WindowsNativeSourceSafety.Read(_source);
        }

        /// <inheritdoc />
        public void SetSelection(string display, int anchor, int active)
        {
            CheckDisplay(display);
            CheckBoundary(display, anchor);
            CheckBoundary(display, active);
            var map = new RichEditOffsetMap(display);
            var selection = new Win32.CharacterRange
            {
                Min = map.ToNative(Math.Min(anchor, active)),
                Max = map.ToNative(Math.Max(anchor, active))
            };
            SetNativeSelection(ref selection);
        }

        /// <inheritdoc />
        public NativeSourceDiagnosticRange ReadSelection(string display)
        {
            CheckDisplay(display);
            var selection = NativeSelection();
            var map = new RichEditOffsetMap(display);
            var start = map.ToDisplay(selection.Min);
            return new NativeSourceDiagnosticRange(start, map.ToDisplay(selection.Max) - start);
        }

        /// <inheritdoc />
        public void Insert(string text)
        {
            CheckOwner();
            ValidateDisplay(text);
            // FALSE prevents adding native history; the caller records canonical edits.
            Win32.SendMessageW(_source, ReplaceSelection, 0, text);
            _display = WindowsNativeSourceSafety.Read(_source);
            EnsureUndoDisabled();
        }

        /// <inheritdoc />
        public NativeSourceDiagnosticViewport CaptureViewport()
        {
            CheckOwner();
            var position = new Win32.Point();
            if (Win32.SendMessageW(_source, Win32.EM_GETSCROLLPOS, 0, ref position) == 0)
                throw new InvalidOperationException("RichEdit scroll capture failed.");
            var line = checked((int)Win32.SendMessageW(_source, Win32.EM_GETFIRSTVISIBLELINE, 0, 0));
            return new NativeSourceDiagnosticViewport(position.X, position.Y, line);
        }

        /// <inheritdoc />
        public void ScrollToEnd(string display)
        {
            CheckDisplay(display);
            Win32.SendMessageW(_source, VerticalScroll, ScrollBottom, 0);
        }

        /// <inheritdoc />
        public void RestoreViewport(NativeSourceDiagnosticViewport viewport)
        {
            CheckOwner();
            if (viewport.FirstVisibleLine < 0)
                throw new ArgumentException("RichEdit restoration requires its first-visible-line witness.", nameof(viewport));
            var position = new Win32.Point
            {
                X = Pixel(viewport.Horizontal), Y = Pixel(viewport.Vertical)
            };
            Win32.SendMessageW(_source, Win32.EM_SETSCROLLPOS, 0, ref position);
            var currentLine = checked((int)Win32.SendMessageW(_source, Win32.EM_GETFIRSTVISIBLELINE, 0, 0));
            // GETSCROLLPOS documents 16-bit coordinates. Restore the independent
            // native visual-line witness after the available pixel position; the
            // caller must still compare the observed tuple, not infer full pixels.
            Win32.SendMessageW(_source, Win32.EM_LINESCROLL, 0,
                checked(viewport.FirstVisibleLine - currentLine));
        }

        /// <inheritdoc />
        public void PublishStyles(string display, IReadOnlyList<NativeSourceDiagnosticStyle> styles)
        {
            CheckDisplay(display);
            ArgumentNullException.ThrowIfNull(styles);
            ValidateStyles(display, styles);
            var selection = NativeSelection();
            var viewport = CaptureViewport();
            var map = new RichEditOffsetMap(display);
            EnsureUndoDisabled();
            Win32.SendMessageW(_source, Win32.WM_SETREDRAW, 0, 0);
            try
            {
                var baseColor = ColorFormat(_foreground);
                ApplyFormat(Win32.SCF_ALL, ref baseColor);
                foreach (var style in styles)
                {
                    if (style.Length == 0) continue;
                    var range = new Win32.CharacterRange
                    {
                        Min = map.ToNative(style.Start),
                        Max = map.ToNative(checked(style.Start + style.Length))
                    };
                    SetNativeSelection(ref range);
                    var format = ColorFormat(style.Foreground);
                    ApplyFormat(Win32.SCF_SELECTION, ref format);
                }
            }
            finally
            {
                try
                {
                    SetNativeSelection(ref selection);
                    RestoreViewport(viewport);
                }
                finally
                {
                    Win32.SendMessageW(_source, Win32.WM_SETREDRAW, 1, 0);
                    Win32.InvalidateRect(_source, 0, false);
                }
            }
            if (!string.Equals(display, ReadText(), StringComparison.Ordinal) ||
                NativeSelection().Min != selection.Min || NativeSelection().Max != selection.Max ||
                CaptureViewport() != viewport)
                throw new InvalidOperationException("RichEdit attribute publication changed text or view state.");
            EnsureUndoDisabled();
        }

        /// <inheritdoc />
        public void FlushDraw()
        {
            CheckOwner();
            Win32.UpdateWindow(_source);
            Win32.UpdateWindow(_window);
        }

        /// <summary>Destroys the owned parent/child before releasing the library reference.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            if (Environment.CurrentManagedThreadId != _owner)
                throw new InvalidOperationException("Native source destruction requires its owner thread.");
            if (_window != 0 && !Win32.DestroyWindow(_window))
                throw new InvalidOperationException("Owned native window destruction failed.");
            _source = _window = 0;
            if (_library != 0 && !FreeLibrary(_library))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            _library = 0;
            _disposed = true;
        }

        /// <summary>Rejects stale display maps instead of mapping against a different native replica.</summary>
        private void CheckDisplay(string display)
        {
            CheckOwner();
            if (!string.Equals(display, _display, StringComparison.Ordinal))
                throw new ArgumentException("Display does not match the installed native replica.", nameof(display));
        }

        /// <summary>Rejects calls after disposal or from another UI thread.</summary>
        private void CheckOwner()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Environment.CurrentManagedThreadId != _owner)
                throw new InvalidOperationException("Native source operations require their owner thread.");
        }

        /// <summary>Readback uses CRLF expansion; unrepresentable imports are rejected, never normalized silently.</summary>
        private static void ValidateDisplay(string display)
        {
            ArgumentNullException.ThrowIfNull(display);
            for (var i = 0; i < display.Length; i++)
            {
                if (display[i] == '\0' || (display[i] == '\r' &&
                    (i + 1 == display.Length || display[i + 1] != '\n')) ||
                    (display[i] == '\n' && (i == 0 || display[i - 1] != '\r')))
                    throw new ArgumentException("The diagnostic display requires NUL-free CRLF text.", nameof(display));
            }
        }

        /// <summary>RichEdit cannot represent a separate selection boundary inside an expanded CRLF pair.</summary>
        private static void CheckBoundary(string display, int offset)
        {
            if (offset < 0 || offset > display.Length ||
                (offset > 0 && offset < display.Length && display[offset - 1] == '\r' && display[offset] == '\n'))
                throw new ArgumentOutOfRangeException(nameof(offset));
        }

        /// <summary>Checks every span before any native attribute mutation.</summary>
        private static void ValidateStyles(string display, IReadOnlyList<NativeSourceDiagnosticStyle> styles)
        {
            foreach (var style in styles)
            {
                if (style.Length < 0 || style.Start < 0 || style.Start > display.Length - style.Length)
                    throw new ArgumentOutOfRangeException(nameof(styles));
                CheckBoundary(display, style.Start);
                CheckBoundary(display, style.Start + style.Length);
            }
        }

        /// <summary>Reads a sorted native paragraph-offset selection, not physical direction.</summary>
        private Win32.CharacterRange NativeSelection()
        {
            var selection = new Win32.CharacterRange();
            Win32.SendMessageW(_source, Win32.EM_EXGETSEL, 0, ref selection);
            return selection;
        }

        /// <summary>Selection changes never request keyboard focus or foreground activation.</summary>
        private void SetNativeSelection(ref Win32.CharacterRange selection) =>
            Win32.SendMessageW(_source, Win32.EM_EXSETSEL, 0, ref selection);

        /// <summary>Checks RichEdit's success result and suppresses automatic keyboard-layout switching.</summary>
        private void ApplyFormat(uint scope, ref Win32.CharacterFormat format)
        {
            if (Win32.SendMessageW(_source, Win32.EM_SETCHARFORMAT, scope | NoKeyboardUpdate, ref format) == 0)
                throw new InvalidOperationException("RichEdit attribute publication failed.");
        }

        /// <summary>Only opaque foreground changes are semantic publications; text and font are untouched.</summary>
        private static Win32.CharacterFormat ColorFormat(ThemeColor color) => new()
        {
            Size = (uint)Marshal.SizeOf<Win32.CharacterFormat>(),
            Mask = Win32.CFM_COLOR, TextColor = ColorRef(color), FaceName = ""
        };

        /// <summary>Rejects accidental competing native history without clearing evidence after publication.</summary>
        private void EnsureUndoDisabled()
        {
            if (Win32.SendMessageW(_source, CanUndo, 0, 0) != 0 ||
                Win32.SendMessageW(_source, CanRedo, 0, 0) != 0)
                throw new InvalidOperationException("The diagnostic native undo queue must remain disabled.");
        }

        /// <summary>Converts opaque sRGB to Win32 COLORREF, not an ARGB integer.</summary>
        private static uint ColorRef(ThemeColor color) =>
            (uint)(color.Red | color.Green << 8 | color.Blue << 16);

        /// <summary>Accepts only integral native pixel coordinates captured by this adapter.</summary>
        private static int Pixel(double value)
        {
            if (!double.IsFinite(value) || value < 0 || value > int.MaxValue || value != Math.Truncate(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            return checked((int)value);
        }
    }

    /// <summary>Releases only the reference returned by this adapter's LoadLibrary call.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint module);
}
