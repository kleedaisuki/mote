using System.Runtime.Versioning;
using Mote.Engine;
using Mote.Native.Viewport;

namespace Mote.Native.Windows.Canvas;

/// <summary>Evidence returned by the Windows DirectWrite geometry smoke probe.</summary>
internal readonly record struct WindowsCanvasProbeResult(int Cases, int ExactAsciiHits,
    int ClusterRoundTrips);

/// <summary>
/// Exercises OS-backed DirectWrite geometry against bounded source slices without
/// opening a window, owning input, or shipping a native companion library.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsCanvasProbe
{
    /// <summary>
    /// Verifies factory creation, UTF-16 source mapping and hit-test round trips for
    /// ASCII, line-ending exclusion, CJK, RTL and surrogate-pair text.
    /// </summary>
    /// <example><code>var result = WindowsCanvasProbe.Run();</code></example>
    public static WindowsCanvasProbeResult Run()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var canvas = new WindowsDirectWriteCanvas();
        var cases = 0;
        var ascii = 0;
        var roundTrips = 0;

        Check("ABCDE", 2, exact: true);
        Check("ab\r\ncd", 4, exact: true);
        Check("你好世界", 1, exact: false);
        Check("אבג", 1, exact: false);
        Check("A😀B", 1, exact: false);
        return new WindowsCanvasProbeResult(cases, ascii, roundTrips);

        void Check(string content, int sourceOffset, bool exact)
        {
            using var document = new Document(content);
            var snapshot = document.Snapshot;
            var viewport = new ContinuousViewport(snapshot, 22);
            var slice = viewport.GetVisibleSlices(100).Single(item =>
                sourceOffset >= item.SourceStart &&
                sourceOffset < item.SourceStart + item.SourceLength);
            if (slice.SourceLength == 0 ||
                snapshot.GetText(slice.SourceStart, slice.SourceLength).IndexOfAny(['\r', '\n']) >= 0)
                throw new InvalidOperationException("ViewportSlice included a line delimiter.");
            var geometry = canvas.HitTest(snapshot, slice, sourceOffset);
            if (!float.IsFinite(geometry.X) || !float.IsFinite(geometry.Y) ||
                !float.IsFinite(geometry.Width) || !float.IsFinite(geometry.Height) ||
                geometry.Width <= 0 || geometry.Height <= 0)
                throw new InvalidOperationException("DirectWrite produced invalid glyph geometry.");
            var insideX = geometry.X + (geometry.BidiLevel % 2 == 0 ? 1 : -1) *
                Math.Max(0.05f, geometry.Width * 0.25f);
            var point = canvas.HitTestPoint(snapshot, slice, insideX,
                geometry.Y + geometry.Height * 0.5f);
            if (!point.IsInside || sourceOffset < point.SourceStart ||
                sourceOffset >= point.SourceStart + Math.Max(point.SourceLength, 1))
                throw new InvalidOperationException(
                    $"DirectWrite hit-test did not return the source cluster: {content}.");
            if (exact)
            {
                if (point.SourceStart != sourceOffset || point.SourceLength != 1)
                    throw new InvalidOperationException("ASCII hit-test was not exact.");
                ascii++;
            }
            cases++;
            roundTrips++;
        }
    }
}
