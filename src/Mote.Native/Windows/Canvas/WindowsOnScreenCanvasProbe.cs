using System.Runtime.Versioning;
using Mote.Engine;
using Mote.Native.Viewport;
using Mote.Themes;

namespace Mote.Native.Windows.Canvas;

/// <summary>Runs a real HWND/WM_PAINT DirectWrite canvas workflow without editing text.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsOnScreenCanvasProbe
{
    /// <summary>
    /// Opens two real files, scrolls the first canvas across the former 64 KiB page
    /// seam with native wheel messages, drags a source-coordinate selection, then
    /// renders a remote bounded window of a long line. PNGs capture the exact DIB
    /// blitted by WM_PAINT and are written only under <paramref name="outputDirectory"/>.
    /// </summary>
    /// <example><code>
    /// var result = WindowsOnScreenCanvasProbe.Run(many, longLine,
    ///     Path.Combine(".cache", "canvas"), ThemePolicies.Get(ThemePolicies.DarkId));
    /// </code></example>
    internal static CanvasWindowProbeResult Run(string manyLinePath, string longLinePath,
        string outputDirectory, IThemePolicy theme)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        ArgumentException.ThrowIfNullOrWhiteSpace(manyLinePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(longLinePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(theme);
        var directory = Path.GetFullPath(outputDirectory);
        var relative = Path.GetRelativePath(Path.GetFullPath(Environment.CurrentDirectory),
            directory).Replace('\\', '/');
        if (relative is not (".cache" or ".temp") &&
            !relative.StartsWith(".cache/", StringComparison.OrdinalIgnoreCase) &&
            !relative.StartsWith(".temp/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "Canvas probe output must be inside repository .cache or .temp.",
                nameof(outputDirectory));
        Directory.CreateDirectory(directory);
        using var many = Document.OpenAsync(manyLinePath).GetAwaiter().GetResult();
        using var longLine = Document.OpenAsync(longLinePath).GetAwaiter().GetResult();
        using var window = new WindowsOnScreenCanvasWindow(many.Snapshot, theme);
        var screenshots = new List<string>(7);
        var before = 0;
        var after = 0;
        var selectionStart = 0;
        var selectionLength = 0;
        var maxSlice = 0;
        var roundTrips = 0;
        window.Run(canvas =>
        {
            if (canvas.PaintCount == 0)
                throw new InvalidOperationException("The canvas window never completed WM_PAINT.");
            before = canvas.Frame.TopAnchor.SourceOffset;
            Capture("many-before.png");
            canvas.ProbeRoundTrip();
            roundTrips++;

            var top = (int)Math.Round(theme.Typography.EditorFontSize *
                theme.Typography.LineHeightMultiplier);
            var down = Pack(72, Math.Max(5, top));
            var drag = Pack(180, Math.Max(40, top * 6));
            // Begin before the former page seam and keep OS pointer capture while wheeling.
            Win32.SendMessageW(canvas.Handle, CanvasWin32.WmLButtonDown, 0, down);

            // One OS wheel event represents ten notches, not a direct model seek.
            var wheel = (nuint)((uint)unchecked((ushort)-1200) << 16);
            for (var i = 0; i < 500 && canvas.Frame.TopAnchor.SourceOffset <= 64 * 1024; i++)
                Win32.SendMessageW(canvas.Handle, CanvasWin32.WmMouseWheel, wheel, 0);
            after = canvas.Frame.TopAnchor.SourceOffset;
            if (after <= 64 * 1024)
                throw new InvalidOperationException("OS wheel scrolling did not cross the 64 KiB source seam.");
            Capture("many-after-wheel.png");
            canvas.ProbeRoundTrip();
            roundTrips++;

            Win32.SendMessageW(canvas.Handle, CanvasWin32.WmMouseMove,
                (nuint)CanvasWin32.MkLButton, drag);
            Win32.SendMessageW(canvas.Handle, CanvasWin32.WmLButtonUp, 0, drag);
            selectionStart = canvas.Frame.SelectionStart;
            selectionLength = canvas.Frame.SelectionLength;
            if (selectionStart >= 64 * 1024 ||
                selectionStart + selectionLength <= 64 * 1024)
                throw new InvalidOperationException(
                    "OS pointer drag did not select across the former 64 KiB source seam.");
            Capture("many-selection.png");

            canvas.Bind(longLine.Snapshot);
            canvas.Reveal(longLine.Snapshot.Length / 2);
            Capture("long-line-middle.png");
            maxSlice = canvas.MaxShapedSlice;
            if (maxSlice <= 0 || maxSlice > 16 * 1024)
                throw new InvalidOperationException("Long-line paint violated bounded shaping.");
            canvas.ProbeRoundTrip(longLine.Snapshot.Length / 2);
            roundTrips++;

            using var unicode = new Document("ABC 你好 אבג 😀 Z");
            canvas.Bind(unicode.Snapshot);
            foreach (var value in new[] { "你", "א", "😀" })
            {
                var source = unicode.Snapshot.GetText().IndexOf(value,
                    StringComparison.Ordinal);
                canvas.ProbeRoundTrip(source);
                roundTrips++;
            }
            Capture("unicode.png");

            using var blank = new Document("a\r\n\r\nb");
            canvas.Bind(blank.Snapshot);
            var blankStart = Pack(30, Math.Max(3, top / 2));
            var blankEnd = Pack(180, top * 2 + Math.Max(3, top / 2));
            Win32.SendMessageW(canvas.Handle, CanvasWin32.WmLButtonDown, 0, blankStart);
            Win32.SendMessageW(canvas.Handle, CanvasWin32.WmMouseMove,
                (nuint)CanvasWin32.MkLButton, blankEnd);
            Win32.SendMessageW(canvas.Handle, CanvasWin32.WmLButtonUp, 0, blankEnd);
            if (canvas.Frame.SelectionStart > 1 ||
                canvas.Frame.SelectionStart + canvas.Frame.SelectionLength < 5)
                throw new InvalidOperationException("Blank-line drag lost a selected CRLF row.");
            Capture("blank-line-selection.png");

            using var crlfOnly = new Document("\r\n");
            canvas.Bind(crlfOnly.Snapshot);
            var firstEmpty = Pack(30, Math.Max(3, top / 2));
            var secondEmpty = Pack(30, top + Math.Max(3, top / 2));
            Win32.SendMessageW(canvas.Handle, CanvasWin32.WmLButtonDown, 0, firstEmpty);
            Win32.SendMessageW(canvas.Handle, CanvasWin32.WmMouseMove,
                (nuint)CanvasWin32.MkLButton, secondEmpty);
            Win32.SendMessageW(canvas.Handle, CanvasWin32.WmLButtonUp, 0, secondEmpty);
            if (canvas.Frame.SelectionStart != 0 || canvas.Frame.SelectionLength != 2)
                throw new InvalidOperationException("CRLF-only pointer selection was not source-exact.");
            Capture("crlf-only-selection.png");
            canvas.Close();

            void Capture(string name)
            {
                var path = Path.Combine(directory, name);
                canvas.Capture(path);
                screenshots.Add(path);
            }
        });
        return new CanvasWindowProbeResult(theme.Id, before, after, selectionStart,
            selectionLength, maxSlice, roundTrips, screenshots);
    }

    private static nint Pack(int x, int y) =>
        (nint)((y & 0xFFFF) << 16 | (x & 0xFFFF));
}
