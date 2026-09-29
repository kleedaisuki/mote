namespace Mote.Native.Viewport;

/// <summary>
/// Inspectable result of one target-OS, on-screen read-only canvas workflow.
/// It is evidence about geometry and scrolling, not input, IME, or accessibility.
/// </summary>
internal sealed record CanvasWindowProbeResult(
    string ThemeId,
    int ManyLineBefore,
    int ManyLineAfter,
    int SelectionStart,
    int SelectionLength,
    int LongLineMaxSliceLength,
    int HitTestRoundTrips,
    IReadOnlyList<string> ScreenshotPaths);
