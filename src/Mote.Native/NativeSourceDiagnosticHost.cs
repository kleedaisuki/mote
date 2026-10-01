using Mote.Themes;

namespace Mote.Native;

/// <summary>A native/display UTF-16 range, not a canonical source offset or selection direction witness.</summary>
internal readonly record struct NativeSourceDiagnosticRange(int Start, int Length);

/// <summary>
/// One owned view's scroll observation, not canonical source offsets. Windows
/// records its first visible native line because EM_GETSCROLLPOS pixel fields
/// have a documented 16-bit limit; matching them alone cannot prove distant
/// pixel-perfect restoration. Platforms without that line witness use -1.
/// </summary>
internal readonly record struct NativeSourceDiagnosticViewport(
    double Horizontal, double Vertical, int FirstVisibleLine = -1);

/// <summary>Attribute-only foreground publication in the exact installed display string.</summary>
internal readonly record struct NativeSourceDiagnosticStyle(int Start, int Length, ThemeColor Foreground);

/// <summary>
/// Disposable, full-resident native source capability surface. This is not a
/// product profile, bounded NativeDocumentView, input framework or text engine.
/// All calls occur synchronously on its native UI owner thread. The caller owns
/// canonical source/history; this surface performs no file I/O or engine edits.
/// </summary>
internal interface INativeSourceDiagnosticHost : IDisposable
{
    /// <summary>Observed native layout backend, or an explicit unknown value; never a platform marketing inference.</summary>
    string LayoutBackend { get; }

    /// <summary>Imports the complete display replica; installation must subsequently pass exact readback.</summary>
    void Install(string display);

    /// <summary>Returns the complete final native string, failing rather than substituting/truncating unknown input.</summary>
    string ReadText();

    /// <summary>Sets display UTF-16 boundaries; this controlled probe does not certify physical selection direction.</summary>
    void SetSelection(string display, int anchor, int active);

    /// <summary>Reads the native sorted range using the exact current display to resolve platform offset maps.</summary>
    NativeSourceDiagnosticRange ReadSelection(string display);

    /// <summary>Performs one controlled native replacement; this is not a universal input or IME journal.</summary>
    void Insert(string text);

    /// <summary>Captures only this owned control's scroll position without changing selection.</summary>
    NativeSourceDiagnosticViewport CaptureViewport();

    /// <summary>Scrolls the owned view to its final document region without relocating its selection.</summary>
    void ScrollToEnd(string display);

    /// <summary>Restores a position captured from this same native view.</summary>
    void RestoreViewport(NativeSourceDiagnosticViewport viewport);

    /// <summary>
    /// Applies complete semantic foreground spans as attributes only. Preserve
    /// text, native selection, scroll and native undo state; fail on invalid spans.
    /// The probe times this full publication separately from analysis and import.
    /// </summary>
    void PublishStyles(string display, IReadOnlyList<NativeSourceDiagnosticStyle> styles);

    /// <summary>Returns after owned native layout/draw submission, not compositor presentation or physical paint.</summary>
    void FlushDraw();
}
