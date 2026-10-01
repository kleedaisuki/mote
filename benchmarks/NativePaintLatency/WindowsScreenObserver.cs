#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

/// <summary>
/// Samples a fixed ROI inside a synthetic mote canvas on the Windows desktop.
/// Five-point occlusion checks cannot prove every interior pixel is target-owned;
/// no captured pixels are persisted. This is not a paint or photon timestamp.
/// </summary>
public static class MoteWindowsScreenObserver
{
    private const int Width = 256;
    private const int Height = 32;
    // A local pilot repeatedly observed a 31-pixel change in the WM_NULL
    // control phase. Require a substantial first-row change; the synthetic
    // X shift changed thousands of pixels in the same ROI.
    private const int ChangedPixelThreshold = 128;
    private const uint Srccopy = 0x00CC0020;
    private const uint BiRgb = 0;
    private const int DibRgbColors = 0;

    /// <summary>Display geometry and DPI observed for the synthetic target HWND.</summary>
    public sealed class DisplayInfo
    {
        /// <summary>Physical-pixel canvas client width.</summary>
        public int CanvasWidth { get; set; }
        /// <summary>Physical-pixel canvas client height.</summary>
        public int CanvasHeight { get; set; }
        /// <summary>DPI reported for the target native canvas window.</summary>
        public uint Dpi { get; set; }
        /// <summary>Primary display width reported by Win32.</summary>
        public int PrimaryWidth { get; set; }
        /// <summary>Primary display height reported by Win32.</summary>
        public int PrimaryHeight { get; set; }
    }

    /// <summary>Alternating fixed-area screen-copy control on an unchanged source.</summary>
    public sealed class CaptureProfile
    {
        /// <summary>Number of screen copies of each ROI geometry.</summary>
        public int SamplesPerGeometry { get; set; }
        /// <summary>Median full 256×32 capture cost in milliseconds.</summary>
        public double FullMedianMs { get; set; }
        /// <summary>Median small 64×16 capture cost in milliseconds.</summary>
        public double SmallMedianMs { get; set; }
        /// <summary>Median BitBlt component of the full capture.</summary>
        public double FullBitBltMedianMs { get; set; }
        /// <summary>Median BitBlt component of the small capture.</summary>
        public double SmallBitBltMedianMs { get; set; }
    }

    /// <summary>
    /// Alternates full and small screen copies (ABBA order) without input;
    /// both DIBs stay allocated for all samples and pixels are discarded.
    /// </summary>
    public static CaptureProfile ProfileCopyArea(IntPtr canvas, int pairs = 20)
    {
        if (pairs is < 2 or > 100) throw new ArgumentOutOfRangeException(nameof(pairs));
        var prior = SetThreadDpiAwarenessContext(new IntPtr(-4));
        if (prior == IntPtr.Zero) throw new InvalidOperationException("PMv2 DPI context unavailable.");
        try
        {
            using var full = new RegionCapture(canvas);
            using var small = new RegionCapture(canvas, 64, 16);
            var fullCosts = new List<double>(pairs * 2);
            var smallCosts = new List<double>(pairs * 2);
            var fullBlts = new List<double>(pairs * 2);
            var smallBlts = new List<double>(pairs * 2);
            for (var i = 0; i < pairs; i++)
            {
                Add(full, fullCosts, fullBlts);
                Add(small, smallCosts, smallBlts);
                Add(small, smallCosts, smallBlts);
                Add(full, fullCosts, fullBlts);
                Thread.Sleep(1);
            }
            fullCosts.Sort(); smallCosts.Sort(); fullBlts.Sort(); smallBlts.Sort();
            return new CaptureProfile
            {
                SamplesPerGeometry = pairs * 2,
                FullMedianMs = fullCosts[fullCosts.Count / 2],
                SmallMedianMs = smallCosts[smallCosts.Count / 2],
                FullBitBltMedianMs = fullBlts[fullBlts.Count / 2],
                SmallBitBltMedianMs = smallBlts[smallBlts.Count / 2]
            };
        }
        finally { SetThreadDpiAwarenessContext(prior); }

        static void Add(RegionCapture capture, List<double> total, List<double> blt)
        {
            capture.Copy(out var cost);
            total.Add(cost.TotalMs);
            blt.Add(cost.BitBltMs);
        }
    }

    /// <summary>Reports geometry under the same PMv2 coordinate convention as capture.</summary>
    public static DisplayInfo Describe(IntPtr canvas)
    {
        var prior = SetThreadDpiAwarenessContext(new IntPtr(-4));
        if (prior == IntPtr.Zero) throw new InvalidOperationException("PMv2 DPI context unavailable.");
        try
        {
            if (!GetClientRect(canvas, out var rect))
                throw new InvalidOperationException("Canvas client geometry unavailable.");
            return new DisplayInfo
            {
                CanvasWidth = rect.Right - rect.Left,
                CanvasHeight = rect.Bottom - rect.Top,
                Dpi = GetDpiForWindow(canvas),
                PrimaryWidth = GetSystemMetrics(0),
                PrimaryHeight = GetSystemMetrics(1)
            };
        }
        finally { SetThreadDpiAwarenessContext(prior); }
    }

    /// <summary>Requires that synthetic mote owns the foreground window.</summary>
    public static bool IsForeground(IntPtr editor) => GetForegroundWindow() == editor;

    /// <summary>
    /// Makes only the disposable synthetic editor topmost for an explicitly
    /// opted-in local screen test; closing its process removes the state.
    /// </summary>
    public static void MakeSyntheticTopmost(IntPtr editor)
    {
        const uint noMoveNoSizeNoActivate = 0x0013;
        if (!SetWindowPos(editor, new IntPtr(-1), 0, 0, 0, 0, noMoveNoSizeNoActivate))
            throw new InvalidOperationException("Could not place the synthetic editor above local windows.");
    }

    /// <summary>One bounded capture phase, including dispatch and sampler cadence.</summary>
    public sealed class PhaseResult
    {
        /// <summary>Elapsed time until the cross-process SendMessage returned.</summary>
        public double InputAckMs { get; set; }
        /// <summary>First changed desktop capture after dispatch; null for a quiet phase.</summary>
        public double? FirstChangedCaptureMs { get; set; }
        /// <summary>Number of pixels changed at the first qualifying capture.</summary>
        public int ChangedPixels { get; set; }
        /// <summary>First threshold-crossing sample before post-edit frame matching.</summary>
        public double? FirstRawChangedCaptureMs { get; set; }
        /// <summary>Raw first-change pixels; may be an intermediate frame.</summary>
        public int FirstRawChangedPixels { get; set; }
        /// <summary>Pixels differing between the settled and initial screen ROI.</summary>
        public int SettledChangedPixels { get; set; }
        /// <summary>Successful desktop-region captures, including the first baseline.</summary>
        public int Captures { get; set; }
        /// <summary>High-contrast pixels proving the source row was visibly rendered before dispatch.</summary>
        public int BaselineInkPixels { get; set; }
        /// <summary>Largest observed gap between successive capture completions.</summary>
        public double MaxCaptureGapMs { get; set; }
        /// <summary>Median wall time spent in BitBlt plus readback per capture.</summary>
        public double MedianCaptureCostMs { get; set; }
        /// <summary>Median cost of five target-ownership checks and pointer exclusion.</summary>
        public double MedianOwnerCheckMs { get; set; }
        /// <summary>Median cost of copying the screen ROI into the persistent DIB.</summary>
        public double MedianBitBltMs { get; set; }
        /// <summary>Median cost of copying the DIB bytes into managed memory.</summary>
        public double MedianReadbackMs { get; set; }
        /// <summary>Capture cost on the first source-state candidate frame.</summary>
        public double FirstChangedCaptureCostMs { get; set; }
        /// <summary>BitBlt component on the first source-state candidate frame.</summary>
        public double FirstChangedBitBltMs { get; set; }
        /// <summary>Whether a later state read proved an actual changed pixel region.</summary>
        public bool Changed => FirstChangedCaptureMs.HasValue;
        /// <summary>Whether any sampled frame crossed the pixel threshold.</summary>
        public bool RawChanged => FirstRawChangedCaptureMs.HasValue;
        /// <summary>Original synthetic source screen pixels retained only in process memory.</summary>
        internal byte[]? BaselinePixels { get; set; }
        /// <summary>Candidate edited screen pixels retained only for the reversal oracle.</summary>
        internal byte[]? EditedPixels { get; set; }
    }

    /// <summary>Three independent synthetic ROI observations of a known source state.</summary>
    public sealed class StateMatch
    {
        /// <summary>All three captures retained visible glyphs and matched the expected state.</summary>
        public bool Matches { get; set; }
        /// <summary>Largest pixel difference from the expected in-memory source-state image.</summary>
        public int MaxDifferentPixels { get; set; }
        /// <summary>Smallest count of high-contrast glyph pixels among the three samples.</summary>
        public int MinInkPixels { get; set; }
    }

    /// <summary>Stable, visible source geometry contrasted against the timed candidate.</summary>
    public sealed class StateContrast
    {
        /// <summary>Three later screen images were mutually stable and retained glyphs.</summary>
        public bool StableVisible { get; set; }
        /// <summary>Pixel differences in foreground shape, independent of pure recoloring.</summary>
        public int ShapeDifferentPixels { get; set; }
        /// <summary>Maximum changed pixels among three settled state samples.</summary>
        public int MaxSampleDifferentPixels { get; set; }
    }

    /// <summary>
    /// Observes an independently byte-verified Undo state and requires a
    /// stable visible glyph shape distinct from the timed X candidate.
    /// </summary>
    public static StateContrast ContrastUndoState(IntPtr canvas, PhaseResult edit)
    {
        if (edit.EditedPixels is null)
            throw new InvalidOperationException("No timed edited-image candidate exists.");
        var prior = SetThreadDpiAwarenessContext(new IntPtr(-4));
        if (prior == IntPtr.Zero) throw new InvalidOperationException("PMv2 DPI context unavailable.");
        try
        {
            using var capture = new RegionCapture(canvas);
            var initial = capture.Copy();
            var state = new StateContrast
            {
                StableVisible = InkPixels(initial) >= 100,
                ShapeDifferentPixels = ShapeDifferentPixels(initial, edit.EditedPixels)
            };
            for (var i = 0; i < 2; i++)
            {
                Thread.Sleep(50);
                var next = capture.Copy();
                var difference = ChangedPixels(initial, next);
                state.MaxSampleDifferentPixels = Math.Max(state.MaxSampleDifferentPixels, difference);
                if (difference > 64 || InkPixels(next) < 100) state.StableVisible = false;
            }
            return state;
        }
        finally { SetThreadDpiAwarenessContext(prior); }
    }

    /// <summary>
    /// Compares three later desktop captures to the original or edited source
    /// image. Use only after an independent exact-byte Undo/Redo Save oracle.
    /// </summary>
    public static StateMatch MatchState(IntPtr canvas, PhaseResult edit, bool edited)
    {
        var expected = edited ? edit.EditedPixels : edit.BaselinePixels;
        if (expected is null) throw new InvalidOperationException("No candidate source-state image exists.");
        var prior = SetThreadDpiAwarenessContext(new IntPtr(-4));
        if (prior == IntPtr.Zero) throw new InvalidOperationException("PMv2 DPI context unavailable.");
        try
        {
            using var capture = new RegionCapture(canvas);
            var state = new StateMatch { Matches = true, MinInkPixels = int.MaxValue };
            for (var i = 0; i < 3; i++)
            {
                var pixels = capture.Copy();
                var difference = ChangedPixels(expected, pixels);
                var ink = InkPixels(pixels);
                state.MaxDifferentPixels = Math.Max(state.MaxDifferentPixels, difference);
                state.MinInkPixels = Math.Min(state.MinInkPixels, ink);
                if (difference > 64 || ink < 100) state.Matches = false;
                if (i < 2) Thread.Sleep(50);
            }
            return state;
        }
        finally { SetThreadDpiAwarenessContext(prior); }
    }

    /// <summary>
    /// Sends a real WM_CHAR or a no-op WM_NULL to the focused input island while
    /// a second thread samples first-row source pixels. A no-op must remain quiet.
    /// </summary>
    public static PhaseResult Observe(IntPtr canvas, IntPtr input, bool edit, int timeoutMs)
    {
        if (canvas == IntPtr.Zero || input == IntPtr.Zero || timeoutMs < 100)
            throw new ArgumentOutOfRangeException(nameof(canvas));
        var prior = SetThreadDpiAwarenessContext(new IntPtr(-4)); // PMv2 physical coordinates.
        if (prior == IntPtr.Zero)
            throw new InvalidOperationException("Could not establish physical-pixel DPI context.");
        try
        {
            using var capture = new RegionCapture(canvas);
            var baseline = capture.Copy();
            var baselineInk = InkPixels(baseline);
            if (baselineInk < 100)
                throw new InvalidOperationException("The initial screen ROI has no visible source glyphs.");
            var second = capture.Copy();
            if (ChangedPixels(baseline, second) >= ChangedPixelThreshold)
                throw new InvalidOperationException("Screen baseline changed before input.");

            var result = new PhaseResult
            { Captures = 2, BaselineInkPixels = baselineInk, BaselinePixels = baseline };
            long sendTick = 0;
            Exception? observerError = null;
            var costs = new List<double>(256);
            var ownerCosts = new List<double>(256);
            var bltCosts = new List<double>(256);
            var readbackCosts = new List<double>(256);
            var changedFrames = new List<(long At, byte[] Pixels, CaptureCost Cost)>();
            using var ready = new ManualResetEventSlim();
            using var stop = new ManualResetEventSlim();
            var observer = new Thread(() =>
            {
                try
                {
                    // Do not invoke any mote API from the observer thread. Its first
                    // ready signal precedes dispatch, preventing a post-edit-only poll.
                    var last = Stopwatch.GetTimestamp();
                    ready.Set();
                    long firstRawTick = 0;
                    while (!stop.IsSet)
                    {
                        var start = Stopwatch.GetTimestamp();
                        var pixels = capture.Copy(out var cost);
                        var end = Stopwatch.GetTimestamp();
                        result.Captures++;
                        costs.Add(Ms(end - start));
                        ownerCosts.Add(cost.OwnerMs);
                        bltCosts.Add(cost.BitBltMs);
                        readbackCosts.Add(cost.ReadbackMs);
                        result.MaxCaptureGapMs = Math.Max(result.MaxCaptureGapMs, Ms(end - last));
                        last = end;
                        var origin = Volatile.Read(ref sendTick);
                        if (origin != 0 && end >= origin)
                        {
                            var changed = ChangedPixels(baseline, pixels);
                            if (changed >= ChangedPixelThreshold)
                            {
                                changedFrames.Add((end, pixels, cost));
                                if (firstRawTick == 0)
                                {
                                    firstRawTick = end;
                                    result.FirstRawChangedCaptureMs = Ms(end - origin);
                                    result.FirstRawChangedPixels = changed;
                                }
                            }
                            if (firstRawTick != 0 && Ms(end - firstRawTick) >= 100) break;
                            if (Ms(end - origin) >= timeoutMs) break;
                        }
                        Thread.Sleep(1);
                    }
                }
                catch (Exception ex) { observerError = ex; }
                finally { ready.Set(); }
            }) { IsBackground = true, Name = "mote-external-screen-observer" };
            observer.Start();
            if (!ready.Wait(2000)) throw new InvalidOperationException("Screen sampler did not start.");
            var sentAt = Stopwatch.GetTimestamp();
            Volatile.Write(ref sendTick, sentAt);
            Exception? dispatchError = null;
            try
            {
                SendBounded(input, edit ? 0x0102u : 0x0000u,
                    edit ? (UIntPtr)(uint)'X' : UIntPtr.Zero, 3000);
            }
            catch (Exception ex) { dispatchError = ex; stop.Set(); }
            result.InputAckMs = Ms(Stopwatch.GetTimestamp() - sentAt);
            if (!observer.Join(timeoutMs + 1500))
            {
                stop.Set();
                if (!observer.Join(1500))
                    throw new InvalidOperationException("Screen sampler did not stop.");
            }
            if (observerError is not null)
                throw new InvalidOperationException("Desktop capture failed.", observerError);
            if (dispatchError is not null)
                throw new InvalidOperationException("Native input dispatch failed.", dispatchError);
            var settled = capture.Copy();
            result.SettledChangedPixels = ChangedPixels(baseline, settled);
            // This is only a provisional visual-change candidate. The script
            // later proves its source specificity by exact Save, Undo, Redo
            // byte oracles and reversible screen-state comparisons.
            if (result.SettledChangedPixels >= ChangedPixelThreshold &&
                InkPixels(settled) >= 100)
                foreach (var candidate in changedFrames)
                {
                    if (InkPixels(candidate.Pixels) >= 100 &&
                        ChangedPixels(candidate.Pixels, settled) <= 64)
                    {
                        result.FirstChangedCaptureMs = Ms(candidate.At - sentAt);
                        result.ChangedPixels = ChangedPixels(baseline, candidate.Pixels);
                        result.FirstChangedCaptureCostMs = candidate.Cost.TotalMs;
                        result.FirstChangedBitBltMs = candidate.Cost.BitBltMs;
                        result.EditedPixels = candidate.Pixels;
                        break;
                    }
                }
            costs.Sort();
            ownerCosts.Sort();
            bltCosts.Sort();
            readbackCosts.Sort();
            result.MedianCaptureCostMs = costs.Count == 0 ? 0 : costs[costs.Count / 2];
            result.MedianOwnerCheckMs = ownerCosts.Count == 0 ? 0 : ownerCosts[ownerCosts.Count / 2];
            result.MedianBitBltMs = bltCosts.Count == 0 ? 0 : bltCosts[bltCosts.Count / 2];
            result.MedianReadbackMs = readbackCosts.Count == 0 ? 0 : readbackCosts[readbackCosts.Count / 2];
            return result;
        }
        finally { SetThreadDpiAwarenessContext(prior); }
    }

    private static double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;

    private static int ChangedPixels(byte[] before, byte[] after)
    {
        var changed = 0;
        for (var i = 0; i < before.Length; i += 4)
        {
            if (Math.Abs(before[i] - after[i]) >= 24 ||
                Math.Abs(before[i + 1] - after[i + 1]) >= 24 ||
                Math.Abs(before[i + 2] - after[i + 2]) >= 24) changed++;
        }
        return changed;
    }

    private static int InkPixels(byte[] pixels)
    {
        var background = Background(pixels);
        var ink = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (Math.Abs(pixels[i] - (background & 255)) >= 32 ||
                Math.Abs(pixels[i + 1] - ((background >> 8) & 255)) >= 32 ||
                Math.Abs(pixels[i + 2] - ((background >> 16) & 255)) >= 32) ink++;
        }
        return ink;
    }

    private static int ShapeDifferentPixels(byte[] before, byte[] after)
    {
        var beforeBackground = Background(before);
        var afterBackground = Background(after);
        var count = 0;
        for (var i = 0; i < before.Length; i += 4)
        {
            var oldInk = IsInk(before, i, beforeBackground);
            var newInk = IsInk(after, i, afterBackground);
            if (oldInk != newInk) count++;
        }
        return count;
    }

    private static bool IsInk(byte[] pixels, int index, int background) =>
        Math.Abs(pixels[index] - (background & 255)) >= 32 ||
        Math.Abs(pixels[index + 1] - ((background >> 8) & 255)) >= 32 ||
        Math.Abs(pixels[index + 2] - ((background >> 16) & 255)) >= 32;

    private static int Background(byte[] pixels)
    {
        var counts = new Dictionary<int, int>();
        var background = 0;
        var mostCommon = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var rgb = pixels[i] | pixels[i + 1] << 8 | pixels[i + 2] << 16;
            counts.TryGetValue(rgb, out var count);
            count++;
            counts[rgb] = count;
            if (count > mostCommon) { background = rgb; mostCommon = count; }
        }
        return background;
    }

    /// <summary>One capture's internal costs on the same monotonic timer.</summary>
    private readonly record struct CaptureCost(double OwnerMs, double BitBltMs,
        double ReadbackMs)
    {
        /// <summary>Sum of the three measured capture phases.</summary>
        internal double TotalMs => OwnerMs + BitBltMs + ReadbackMs;
    }

    /// <summary>Returns a checked result or fails after a finite cross-process timeout.</summary>
    public static IntPtr SendBounded(IntPtr window, uint message, UIntPtr wParam, uint timeoutMs)
    {
        const uint abortIfHung = 0x0002;
        const uint errorOnExit = 0x0020;
        if (SendMessageTimeoutW(window, message, wParam, IntPtr.Zero,
            abortIfHung | errorOnExit, timeoutMs, out var result) == IntPtr.Zero)
        {
            var code = Marshal.GetLastPInvokeError();
            if (code is 0 or 1460)
                throw new TimeoutException($"Win32 message 0x{message:X} timed out or target exited after {timeoutMs} ms (Win32 {code}).");
            throw new InvalidOperationException($"Win32 message 0x{message:X} failed (Win32 {code}).");
        }
        return result;
    }

    /// <summary>Queries the bounded native text host without an unbounded cross-process send.</summary>
    public static int InputLengthBounded(IntPtr input) => checked((int)SendBounded(
        input, 0x000E, UIntPtr.Zero, 3000).ToInt64());

    /// <summary>Moves the native host selection to its first source character.</summary>
    public static void SelectStartBounded(IntPtr input) =>
        SendBounded(input, 0x00B1, UIntPtr.Zero, 3000);

    /// <summary>Reads at most 63 synthetic host characters with a finite dispatch wait.</summary>
    public static string InputPrefixBounded(IntPtr input)
    {
        var text = new StringBuilder(64);
        const uint abortIfHung = 0x0002;
        const uint errorOnExit = 0x0020;
        if (SendMessageTimeoutTextW(input, 0x000D, (UIntPtr)text.Capacity,
            text, abortIfHung | errorOnExit, 3000, out _) == IntPtr.Zero)
        {
            var code = Marshal.GetLastPInvokeError();
            if (code is 0 or 1460)
                throw new TimeoutException($"Bounded native host text query timed out or exited (Win32 {code}).");
            throw new InvalidOperationException($"Bounded native host text query failed (Win32 {code}).");
        }
        return text.ToString();
    }

    /// <summary>Owns a top-down 32-bit DIB for an in-window, screen-composited ROI.</summary>
    private sealed class RegionCapture : IDisposable
    {
        private readonly IntPtr _screen;
        private readonly IntPtr _memory;
        private readonly IntPtr _bitmap;
        private readonly IntPtr _old;
        private readonly IntPtr _bits;
        private readonly IntPtr _canvas;
        private readonly int _x;
        private readonly int _y;
        private readonly int _width;
        private readonly int _height;

        internal RegionCapture(IntPtr canvas, int width = Width, int height = Height)
        {
            _canvas = canvas;
            _width = width;
            _height = height;
            if (!GetClientRect(canvas, out var rect) ||
                rect.Right - rect.Left < _width + 64 || rect.Bottom - rect.Top < _height + 48)
                throw new InvalidOperationException("Canvas is too small for the first-row screen ROI.");
            var origin = new Point();
            if (!ClientToScreen(canvas, ref origin))
                throw new InvalidOperationException("Canvas screen coordinates unavailable.");
            _x = origin.X + 42; // Excludes the independently blinking caret at x=24.
            _y = origin.Y + 4;
            CheckOwner();

            _screen = GetDC(IntPtr.Zero);
            if (_screen == IntPtr.Zero) throw new InvalidOperationException("Desktop DC unavailable.");
            _memory = CreateCompatibleDC(_screen);
            if (_memory == IntPtr.Zero) { Dispose(); throw new InvalidOperationException("Memory DC unavailable."); }
            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = _width,
                    Height = -_height, Planes = 1, BitCount = 32, Compression = BiRgb
                }
            };
            _bitmap = CreateDIBSection(_screen, ref info, DibRgbColors, out _bits,
                IntPtr.Zero, 0);
            if (_bitmap == IntPtr.Zero || _bits == IntPtr.Zero)
            { Dispose(); throw new InvalidOperationException("Desktop DIB unavailable."); }
            _old = SelectObject(_memory, _bitmap);
            if (_old == IntPtr.Zero) { Dispose(); throw new InvalidOperationException("DIB selection failed."); }
        }

        internal byte[] Copy() => Copy(out _);

        internal byte[] Copy(out CaptureCost cost)
        {
            var start = Stopwatch.GetTimestamp();
            CheckOwner();
            var ownedAt = Stopwatch.GetTimestamp();
            if (!BitBlt(_memory, 0, 0, _width, _height, _screen, _x, _y, Srccopy))
                throw new InvalidOperationException("Desktop BitBlt failed.");
            var blittedAt = Stopwatch.GetTimestamp();
            var pixels = new byte[_width * _height * 4];
            Marshal.Copy(_bits, pixels, 0, pixels.Length);
            var copiedAt = Stopwatch.GetTimestamp();
            cost = new CaptureCost(Ms(ownedAt - start), Ms(blittedAt - ownedAt),
                Ms(copiedAt - blittedAt));
            return pixels;
        }

        private void CheckOwner()
        {
            CheckPoint(_x + 2, _y + 2);
            CheckPoint(_x + _width - 3, _y + 2);
            CheckPoint(_x + 2, _y + _height - 3);
            CheckPoint(_x + _width - 3, _y + _height - 3);
            CheckPoint(_x + _width / 2, _y + _height / 2);
            if (GetCursorPos(out var cursor) &&
                cursor.X >= _x && cursor.X < _x + _width &&
                cursor.Y >= _y && cursor.Y < _y + _height)
                throw new InvalidOperationException("Pointer overlaps the screen ROI.");
        }

        private void CheckPoint(int x, int y)
        {
            var owner = WindowFromPoint(new Point(x, y));
            if (owner != _canvas && !IsChild(_canvas, owner))
                throw new InvalidOperationException("The synthetic canvas ROI is obscured.");
        }

        public void Dispose()
        {
            if (_old != IntPtr.Zero && _memory != IntPtr.Zero) SelectObject(_memory, _old);
            if (_bitmap != IntPtr.Zero) DeleteObject(_bitmap);
            if (_memory != IntPtr.Zero) DeleteDC(_memory);
            if (_screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, _screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, ImageSize; public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    { public BitmapInfoHeader Header; public uint Red, Green, Blue; }

    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,
        IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message,
        UIntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutTextW(IntPtr window, uint message,
        UIntPtr wParam, StringBuilder text, uint flags, uint timeoutMs, out IntPtr result);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int width,
        int height, IntPtr source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc,
        ref BitmapInfo info, int usage, out IntPtr bits, IntPtr section, uint offset);
}
