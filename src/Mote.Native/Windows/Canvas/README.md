# Windows DirectWrite geometry probe

`WindowsDirectWriteCanvas` is a read-only platform geometry adapter for the
source-backed `ViewportSlice` model. It copies at most 16 Ki UTF-16 code units
from one immutable snapshot into an OS `IDWriteTextLayout`; the slice excludes
CR/LF delimiters. It does not own text, input, selection, scroll, IME, UIA, or
rendering. It currently creates a layout per hit-test call and is a feasibility
probe, **not a production canvas control** or evidence of smooth scrolling.

The adapter creates one shared `IDWriteFactory` and text format, invokes the
base DirectWrite COM interfaces through explicit pointer-sized vtable entries,
and releases every COM reference. The slot numbers were checked against the
Windows SDK `dwrite.h` base interfaces. `DWriteCreateFactory` comes from the
OS-provided `dwrite.dll`, not a shipped native library. Pointer arguments use
`nint` or native pointers; Win32 `BOOL` uses four-byte `int`. The published
Native AOT diagnostic has now executed on both Windows x64 and Arm64.

`WindowsCanvasProbe.Run()` exercises factory creation and hit-test position ↔
point round trips for ASCII, a second line after CRLF, CJK, RTL and emoji. For
multi-code-unit clusters it verifies containment rather than falsely requiring
a one-code-unit caret. DirectWrite returns geometry in device-independent pixels;
the adapter adds `ViewportSlice.TopY` for viewport-relative Y.

## Evidence (2026-09-29)

- [Fully green GitHub Actions run 36553927870](https://github.com/kleedaisuki/mote/actions/runs/36553927870)
  reconfirmed the strict one-file Native AOT publish and
  `--check-native-windows-canvas` diagnostic on **win-x64 and win-arm64**.
  Both reported `mote-native-windows-canvas-ready cases=5 ascii=2 clusters=5`.
- [Earlier run 36552700358](https://github.com/kleedaisuki/mote/actions/runs/36552700358):
  the strict one-file Native AOT publish and `--check-native-windows-canvas`
  diagnostic also passed on both Windows RIDs; its overall workflow failure
  came from a separate macOS test-script syntax error before editor input.
  These are target-host DirectWrite geometry runs, not merely cross-compilation.

- `dotnet build src/Mote.Native/Mote.Native.csproj --no-restore`:
  0 warnings, 0 errors.
- `dotnet run --project .temp/win_canvas_probe/Probe.csproj`:
  `directwrite-ready cases=5 exact-ascii=2 clusters=5`.
- `dotnet publish .temp/win_canvas_probe/Probe.csproj -c Release -r win-x64
  -p:PublishAot=true -p:SelfContained=true -o .cache/win_canvas_aot` and run
  `.cache/win_canvas_aot/Mote.Tests.exe`: same five cases, exit code 0.

The temporary geometry probe's `.cache` output is not the product's
single-binary deliverable. These checks verify hit-test feasibility, not an
editable canvas or frame latency. The separate on-screen workflow below adds
real HWND paint evidence. Before using either adapter for editing, measure
layout reuse and real visible-row scrolling; verify wrapping, DPI changes,
font fallback, bidi caret affinity, remote horizontal geometry, IME, and
screen-reader range semantics.

## On-screen read-only canvas checkpoint

`WindowsOnScreenCanvasProbe.Run(manyLinePath, longLinePath, outputDirectory,
theme)` opens a real top-level Win32 window and pumps its OS message loop.
`WM_PAINT` renders `CanvasInteraction.Frame()` with bounded DirectWrite layouts
on a Direct2D DC target, then blits the same 32-bpp DIB to the window. Each
paint reads only visible source slices of at most 4096 UTF-16 code units;
there is no second full-document text mirror. Native wheel and vertical
scrollbar messages move the continuous source anchor. Pointer capture and drag
select in global source coordinates, including across the former 64 KiB page
seam. Theme foreground, background, selection foreground and selection
background are applied. A selected CRLF or intermediate blank line fills the
visible row remainder. PNGs encode the exact DIB passed to `BitBlt`.

The probe additionally checks a remote 50 MiB long-line slice, six live
position/point hit-test round trips (including CJK, RTL and emoji), an
intermediate blank selected row, and a CRLF-only selected row. It never edits
source text. Its output directory is restricted to repository `.cache` or
`.temp` under the current working directory.

Windows x64 Native AOT evidence (2026-09-29):

```powershell
dotnet publish src/Mote.Native/Mote.Native.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishAot=true -o .cache/win_canvas_onscreen_aot
& tests/NativeCanvasWindowWorkflow.ps1 `
  -ExecutablePath (Resolve-Path '.cache/win_canvas_onscreen_aot/mote.exe').Path `
  -RuntimeIdentifier win-x64
```

The workflow generated a 104,857,600-byte mixed-script CRLF file and a
52,428,800-byte single-line file under `.temp/canvas-window/win-x64/`.
All three themes passed with `before=0`, `after=65550`,
`selection=29+65680`, `long-slice=4096`, `hits=6`, and seven PNGs per theme.
The report and PNG hashes are in `.cache/ci-inventory/win-x64/canvas-window.json`;
inspect screenshots in `.cache/canvas-window/win-x64/<theme>/` for `mote-dark`,
`mote-light`, and `mote-high-contrast-dark`. The workflow's repeated Unicode
rows produce identical before/after pixels despite the measured source change;
a numbered-line supplementary fixture in `.temp/win_onscreen_large/` produced
visibly different before/after screenshots in `.cache/win_onscreen_dark/`.

The tested AOT publish directory held exactly one `mote.exe` (5,886,464 bytes),
SHA-256 `0D01E7E87CCD113B69249AB1098AADE8CAD5549A5AF348DCC54BA2DFF3530D38`.
Host: Windows NT 10.0.26200.0, 96 DPI. Cascadia Code was the requested and
installed font family; the actual fallback faces for CJK/emoji were not
introspected. Dark/light/high-contrast selection, empty-row and Unicode
screenshots were visually checked. This establishes a read-only OS paint
callback and DIB capture, **not** physical screen presentation latency,
editable IME, UIA, or on-screen Windows Arm64 behavior.

## Primary references

- [DirectWrite factory and sharing](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/nn-dwrite-idwritefactory)
- [CreateTextLayout](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/nf-dwrite-idwritefactory-createtextlayout)
- [IDWriteTextLayout hit-testing](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/nn-dwrite-idwritetextlayout)
- [Direct2D `DrawTextLayout`](https://learn.microsoft.com/en-us/windows/win32/direct2d/how-to--draw-text)
- [DirectWrite rendering to GDI surfaces](https://learn.microsoft.com/en-us/windows/win32/directwrite/render-to-a-gdi-surface)
