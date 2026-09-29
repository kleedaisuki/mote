# Interim Avalonia interaction prototype

# This adapter is not the release desktop host

The strict release contract is **one on-disk executable per platform**.
This Avalonia adapter is an interim interaction/performance prototype and is
**noncompliant** with that contract. The OS-native shell is the primary release
target; do not package or advertise this adapter as a compliant distribution.

The prototype targets .NET 10 and Avalonia 12.1.3. AvaloniaEdit 12.0.0 is
used for editing and IME rather than a custom keyboard/IME implementation.
Its text buffer is a **projection** of the engine-owned `Document`: every UI
replacement is immediately applied to the engine using UTF-16 offsets, and
undo/redo are executed by the engine. Programmatic projection replacement is
guarded against recursive replay.

## Large files

Files above 8 Mi UTF-16 code units enter paged editing: AvaloniaEdit receives
at most 256 Ki code units, while the engine retains its chunked source. Prev/
Next Page navigate the file without materializing the entire text. Automatic
analysis runs only on the current page. Even below that threshold, Markdown
files over 1 Mi characters and other structured files over 4 Mi characters
get a 256 Ki partial analysis by default to bound parser allocations. The
**Inspect** command requests a cancellable full-file pass only up to 2 Mi
Markdown characters or 8 Mi other structured characters. The UI marks
partial diagnostics as such; it never reports whole-file validity from a
slice. Page diagnostics may be incomplete for constructs that cross page
boundaries. The inspector caps presentation at 2,500 items.
This is an explicit quality/performance trade-off, not a claim of full semantic
correctness in paged mode.

This projection can double text storage for files below the threshold.
The isolated Windows Native AOT AvaloniaEdit probe documented in
`docs/large-file-ui-evaluation.md` measured a 100 MiB full buffer at
approximately 1.24 s to first measured visual lines, 1,444 MiB RSS, and
42.4 ms offscreen mid-edit; a 256 KiB page projection measured 9.6 ms,
359 MiB, and 0.2 ms respectively. A 16 MiB single line still reached
2.65 GiB RSS and 3.58 s layout in full-buffer mode, while even its bounded
page took 67.8 ms layout. The current page UI does not seamlessly scroll across page
boundaries; navigation controls keep every part editable. A single enormous
line still has high text-layout cost even within a page.

## Native AOT deployment

`PublishAot=true` enables the .NET analyzers and platform-specific AOT publish.
The resulting managed application is native, but Avalonia's graphics/platform
backends include native libraries. The measured win-x64 output contains
`Mote.Desktop.exe`, `av_libglesv2.dll`, `libSkiaSharp.dll`, and
`libHarfBuzzSharp.dll`; the macOS backend uses `libAvaloniaNative.dylib`.
**This desktop adapter does not satisfy a strict single-filesystem-binary
distribution contract.** It is retained only as an interaction/reference
implementation while the OS-native shell becomes the release path. There is
no dynamic language-plugin DLL loading.

Relevant primary documentation:

- [Avalonia Native AOT](https://docs.avaloniaui.net/docs/deployment/native-aot)
- [Avalonia macOS backend](https://docs.avaloniaui.net/docs/platform-specific-guides/macos)
- [.NET Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [AvaloniaEdit editor capabilities](https://github.com/AvaloniaUI/AvaloniaEdit)

## Rendering

The Rendered tab projects parsed semantic nodes into native Avalonia controls.
This provides block-level Markdown typography and a structured data view
without a runtime browser/HTML interpreter. It is intentionally not an HTML
engine; `IDocumentPolicy.RenderHtml` remains the authoritative safe HTML output
for future export/browser integration. Rendering unsupported Markdown
constructs as styled blocks would be misleading, so those remain literal.

## Configuration and diagnostics

`MoteConfigLoader.Load()` resolves a conventional `~/.mote` root before
building the window. The static `Mote.Themes` policy registry supplies dark,
light, and high-contrast palettes and semantic colors; controls explicitly
apply selection foreground/background rather than inheriting syntax colors.
Tracing is opt-in by config or `MOTE_TRACE=1`, and writes beneath the
configured trace directory. UI trace health never displays paths or content.

`--check-runtime` probes engine + JSON analysis without initializing
Avalonia. `--smoke-ui` initializes the actual GUI, waits for a positive-size
window at render priority, then closes; it does not prove physical display
paint. Both flags are intended for platform-specific Native AOT CI.
