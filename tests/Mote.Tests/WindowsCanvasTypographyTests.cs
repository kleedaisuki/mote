using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Mote.Engine;
using Mote.Native;
using Mote.Native.Viewport;
using Mote.Native.Windows;
using Mote.Native.Windows.Canvas;
using Mote.Themes;
using Xunit.Abstractions;

namespace Mote.Tests;

/// <summary>Measures the actual hidden Canvas RichEdit font without changing OS scale, focus or input sources.</summary>
[Collection(nameof(WindowsCanvasTypographyCollection))]
public sealed class WindowsCanvasTypographyTests(ITestOutputHelper output)
{
    /// <summary>Native requested and applied font sizes agree, including bound and newly inserted text.</summary>
    [Fact]
    public void Canvas_input_native_font_matches_source_em_and_preserves_binding()
    {
        if (!OperatingSystem.IsWindows()) return;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { if (OperatingSystem.IsWindows()) Probe(); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Native typography owner thread did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>The boundary preserves established nearest-even rounding without point scaling.</summary>
    [Theory]
    [InlineData(1, -1)]
    [InlineData(1.5, -2)]
    [InlineData(12.5, -12)]
    [InlineData(13, -13)]
    [InlineData(13.6, -14)]
    public void Input_em_conversion_rounds_in_source_client_units(double em, int expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(expected, WindowsRichEditIsland.CanvasDipToInputCharacterHeight(em));
    }

    /// <summary>Zero/default-font requests, nonfinite values and overflowing heights are never silently installed.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(2147483648d)]
    public void Input_em_conversion_rejects_unrepresentable_native_requests(double em)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            if (OperatingSystem.IsWindows()) WindowsRichEditIsland.CanvasDipToInputCharacterHeight(em);
        });
    }
    /// <summary>Uses one owned hidden HWND hierarchy; all native resources remain on its STA owner.</summary>
    [SupportedOSPlatform("windows")]
    private void Probe()
    {
        var library = Win32.LoadLibraryW("msftedit.dll");
        Assert.NotEqual(0, library);
        var parent = Win32.CreateWindowExW(0, "STATIC", "", 0, 0, 0, 600, 300,
            0, 0, Win32.GetModuleHandleW(null), 0);
        try
        {
            Assert.NotEqual(0, parent);
            var theme = ThemePolicies.Get(ThemePolicies.DefaultId);
            using var document = new Document("alpha 中文 😀 e\u0301");
            var snapshot = document.Snapshot;
            using var island = new WindowsRichEditIsland(parent, theme);
            CanvasCommittedEdit? committed = null;
            var commitCount = 0;
            island.EditCommitted += edit => { committed = edit; commitCount++; };
            var frame = new CanvasInteraction(snapshot, 13 * 1.45, 200).Frame() with
            { SelectionAnchor = 1, SelectionActive = 3 };
            island.Bind(new(7, snapshot.Version, 11, snapshot, frame,
                0, snapshot.GetText(), 1, 3, "Synthetic typography", "", false));
            var input = island.InputHandle;
            var focus = GetFocus();
            var before = Selection(input);
            var sourceMetrics = SourceMetrics(island, snapshot, frame);
            var measured = Measure(island);
            var defaultFormat = Format(input, 0);
            var boundFormat = Format(input, 1);
            island.SetTheme(ThemePolicies.Get(ThemePolicies.LightId));
            Assert.Equal(input, island.InputHandle);
            Assert.Equal(focus, GetFocus());
            Assert.Equal(before, Selection(input));
            Assert.Equal(snapshot.GetText(), WindowsNativeSourceSafety.Read(input));
            Assert.Same(snapshot, document.Snapshot);
            Assert.False(island.IsCompositionPending);
            Assert.Equal(measured, Measure(island));
            Assert.Equal(sourceMetrics, SourceMetrics(island, snapshot, frame));
            Assert.Null(committed);
            Assert.Equal(0, commitCount);
            // Actual native character insertion into the selected synthetic ASCII range.
            Win32.SendMessageW(input, (int)Win32.WM_CHAR, 'Z', 0);
            var typedRange = new Win32.CharacterRange { Min = 1, Max = 2 };
            Win32.SendMessageW(input, Win32.EM_EXSETSEL, 0, ref typedRange);
            var typedFormat = Format(input, 1);
            Assert.True(island.FlushPendingText());
            Assert.NotNull(committed);
            Assert.Equal(1, commitCount);
            Assert.Equal(new TextChange(1, 2, "Z"), committed.Value.Change);
            Assert.Equal(7, committed.Value.DocumentGeneration);
            Assert.Equal(11, committed.Value.BindingNonce);
            Assert.Same(snapshot, document.Snapshot); // Probe observes, never applies Engine edits.
            var report = new
            {
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                source_em_dip = theme.Typography.EditorFontSize,
                input_window_dpi = GetDpiForWindow(input),
                window_awareness = GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(input)),
                thread_awareness = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()),
                sourceMetrics, measured, defaultFormat, boundFormat, typedFormat,
                zoom = Zoom(input),
                focus_unchanged = focus == GetFocus(),
                palette_selection_unchanged = new { start = before.Start, end = before.End },
                engine_snapshot_unchanged = ReferenceEquals(snapshot, document.Snapshot),
                committed_change = committed.Value.Change
            };
            output.WriteLine(JsonSerializer.Serialize(report));
            const int expectedHeight = 13; // Same em as the unchanged 96-DPI source renderer.
            Assert.Equal(-expectedHeight, measured.RequestedHeight);
            Assert.InRange(measured.EmHeight, expectedHeight - 1, expectedHeight + 1);
            Assert.Equal(1, measured.MapMode); // MM_TEXT, one logical unit per client pixel.
            Assert.Equal(measured.WindowExtent, measured.ViewportExtent);
            foreach (var format in new[] { defaultFormat, boundFormat, typedFormat })
            {
                Assert.NotEqual(0u, format.Mask & 0x80000000u); // CFM_SIZE.
                var expectedTwips = (int)Math.Round(expectedHeight * 1440d / measured.DcDpiY);
                Assert.InRange(format.Height, expectedTwips - 5, expectedTwips + 5);
            }
        }
        finally
        {
            if (parent != 0) Win32.DestroyWindow(parent);
            FreeLibrary(library);
        }
    }

    /// <summary>Queries native DirectWrite em sizes and Direct2D DPI, rather than echoing policy requests.</summary>
    [SupportedOSPlatform("windows")]
    private static SourceMeasurement SourceMetrics(WindowsRichEditIsland island, TextSnapshot snapshot, CanvasFrame frame)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var geometry = (WindowsDirectWriteCanvas)typeof(WindowsRichEditIsland).GetField("_geometry", fields)!.GetValue(island)!;
        var painter = typeof(WindowsRichEditIsland).GetField("_painter", fields)!.GetValue(island)!;
        var geometryFormat = (nint)geometry.GetType().GetField("_format", fields)!.GetValue(geometry)!;
        var paintFormat = (nint)painter.GetType().GetField("_textFormat", fields)!.GetValue(painter)!;
        var target = (nint)painter.GetType().GetField("_renderTarget", fields)!.GetValue(painter)!;
        var readDpi = Marshal.GetDelegateForFunctionPointer<GetDpiAbi>(Slot(target, 52));
        readDpi(target, out var dpiX, out var dpiY);
        var geometrySize = Marshal.GetDelegateForFunctionPointer<GetSizeAbi>(Slot(geometryFormat, 25))(geometryFormat);
        var paintSize = Marshal.GetDelegateForFunctionPointer<GetSizeAbi>(Slot(paintFormat, 25))(paintFormat);
        Assert.Equal(13f, geometrySize);
        Assert.Equal(13f, paintSize);
        Assert.Equal(96f, dpiX);
        Assert.Equal(96f, dpiY);
        return new(geometrySize, paintSize, dpiX, dpiY, geometry.HitTest(snapshot, frame.Slices[0], 1));
    }

    /// <summary>Native ABI slots are read only; no source layout or renderer settings change.</summary>
    private static nint Slot(nint instance, int slot) => Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size);
    /// <summary>IDWriteTextFormat.GetFontSize returns the installed em request in DIPs.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate float GetSizeAbi(nint self);
    /// <summary>ID2D1RenderTarget.GetDpi returns the actual target DIP-to-pixel transform.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetDpiAbi(nint self, out float x, out float y);
    /// <summary>Geometry and paint native requests plus one fixed source hit metric.</summary>
    private sealed record SourceMeasurement(float GeometryEmDip, float PainterEmDip, float PainterDpiX,
        float PainterDpiY, WindowsCanvasHit Hit);
    /// <summary>RichEdit returns a zero/zero ratio for its normal, unzoomed state.</summary>
    private static object Zoom(nint input)
    {
        var numerator = 0;
        var denominator = 0;
        var result = ReadZoom(input, 0x04E0, ref numerator, ref denominator);
        Assert.NotEqual(0, result);
        Assert.Equal(0, numerator);
        Assert.Equal(0, denominator);
        return new { numerator, denominator, normal = true };
    }
    /// <summary>Reads selected/default CHARFORMAT without changing the selected source interval.</summary>
    private static AppliedFormat Format(nint input, nuint scope)
    {
        var format = new Win32.CharacterFormat
        { Size = (uint)Marshal.SizeOf<Win32.CharacterFormat>(), FaceName = "" };
        Win32.SendMessageW(input, Win32.EM_GETCHARFORMAT, scope, ref format);
        return new(format.Mask, format.Height, format.FaceName);
    }

    /// <summary>Reads the exact native UTF-16 selection without changing it.</summary>
    private static (int Start, int End) Selection(nint input)
    {
        var range = new Win32.CharacterRange();
        Win32.SendMessageW(input, Win32.EM_EXGETSEL, 0, ref range);
        return (range.Min, range.Max);
    }

    /// <summary>Separates the logical HFONT request from the font mapper's selected face and actual em metrics.</summary>
    [SupportedOSPlatform("windows")]
    private static NativeFont Measure(WindowsRichEditIsland island)
    {
        var input = island.InputHandle;
        var exposedFont = Win32.SendMessageW(input, 0x0031, 0, 0); // WM_GETFONT may be unavailable on RichEdit.
        var font = (nint)typeof(WindowsRichEditIsland).GetField("_inputFont", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(island)!;
        Assert.NotEqual(0, font);
        Assert.Equal(Marshal.SizeOf<LogFont>(), GetObjectW(font, Marshal.SizeOf<LogFont>(), out var logical));
        var dc = GetDC(input);
        Assert.NotEqual(0, dc);
        var prior = SelectObject(dc, font);
        try
        {
            Assert.True(GetTextMetricsW(dc, out var metric));
            var face = new StringBuilder(64);
            Assert.NotEqual(0, GetTextFaceW(dc, face.Capacity, face));
            Assert.True(GetWindowExtEx(dc, out var window));
            Assert.True(GetViewportExtEx(dc, out var viewport));
            return new(exposedFont == font, logical.Height, logical.Face, face.ToString(), metric.Height,
                metric.InternalLeading, metric.Height - metric.InternalLeading,
                GetMapMode(dc), GetDeviceCaps(dc, 90), $"{window.X},{window.Y}", $"{viewport.X},{viewport.Y}");
        }
        finally
        {
            SelectObject(dc, prior);
            ReleaseDC(input, dc);
        }
    }

    /// <summary>Requested logical family is not asserted to be the mapper's resolved family.</summary>
    private sealed record NativeFont(bool GetFontExposesOwnedHandle, int RequestedHeight, string RequestedFace, string ResolvedFace,
        int CellHeight, int InternalLeading, int EmHeight, int MapMode, int DcDpiY,
        string WindowExtent, string ViewportExtent);
    /// <summary>Native RichEdit formatting reports twips, not client pixels.</summary>
    private sealed record AppliedFormat(uint Mask, int Height, string Face);
    /// <summary>Native Unicode LOGFONT layout; face is the requested logical family.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct LogFont
    {
        public int Height, Width, Escapement, Orientation, Weight;
        public byte Italic, Underline, StrikeOut, CharSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Face;
    }
    /// <summary>Native Unicode TEXTMETRIC layout; em excludes internal leading.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct TextMetric
    {
        public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AverageWidth,
            MaximumWidth, Weight, Overhang, DigitizedAspectX, DigitizedAspectY;
        public char First, Last, Default, Break;
        public byte Italic, Underlined, StruckOut, PitchAndFamily, CharSet;
    }
    /// <summary>Native DC extent in logical/device units.</summary>
    [StructLayout(LayoutKind.Sequential)] private struct Extent { public int X, Y; }

    /// <summary>Reads the owned native zoom without changing it.</summary>
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint ReadZoom(nint window, int message, ref int numerator, ref int denominator);
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetObjectW(nint value, int size, out LogFont font);
    [DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern bool GetTextMetricsW(nint dc, out TextMetric metric);
    [DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetTextFaceW(nint dc, int count, StringBuilder face);
    [DllImport("gdi32.dll")] private static extern int GetMapMode(nint dc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(nint dc, int index);
    [DllImport("gdi32.dll")] private static extern bool GetWindowExtEx(nint dc, out Extent extent);
    [DllImport("gdi32.dll")] private static extern bool GetViewportExtEx(nint dc, out Extent extent);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindowDpiAwarenessContext(nint window);
    [DllImport("user32.dll")] private static extern nint GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(nint context);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint module);
}






/// <summary>Serializes native Canvas tests against other collections because the current native adapter has process-wide dispatch state.</summary>
[CollectionDefinition(nameof(WindowsCanvasTypographyCollection), DisableParallelization = true)]
public sealed class WindowsCanvasTypographyCollection;
