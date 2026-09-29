# Windows DirectWrite geometry probe

## Experimental interactive input island (not default)

`WindowsEditorShell(experimentalCanvas: true)` implements `INativeCanvasShell`
inside the existing Win32 main window. A child DirectWrite/Direct2D canvas paints
bounded `CanvasFrame.Slices` from the immutable engine snapshot, while a **visible**
`RICHEDIT50W` control at the current caret row holds at most one controller-supplied
logical-line input window (16 Ki UTF-16 units). No whole-document text is copied
into a native control. The main window retains its menus, file pickers, preview,
status bar and clipboard integration; the default constructor still uses the
established bounded RichEdit page path.

RichEdit `EN_CHANGE` is deferred until native dispatch completes and coalesced
into one `CanvasCommittedEdit` in **absolute source coordinates**, tagged with
document generation, immutable base version and binding nonce. IME preedit is
never sent to the engine; composition vetoes commands and input rebinding.
Controller rebinding after commit refreshes the input interval. Canvas wheel,
scrollbar and pointer events operate on global source selection and do not
rebind the native input text. Semantic colors use DirectWrite drawing-effect
brushes on visible intervals; selected glyphs are overdrawn in the theme's
selection foreground. Rendering remains read-only outside the input island.
Global Delete/Backspace bypasses the bounded host when a selection crosses its
interval or lands on an empty line. A confirmed character replacement also
submits the global transaction when the host's own text remains identical
(for example, selecting `a\nb` and typing `b` over its trailing `b`). Without
these rules RichEdit may emit no `EN_CHANGE` despite a real document edit.

The island's native input hard limit is 32 Ki, but paste bypasses it: `WM_PASTE`
is intercepted and the exact `CF_UNICODETEXT` payload is read and submitted as
one source-coordinate controller edit. RichEdit is never permitted to prefer
an alternate `CF_RTF` payload or truncate the checked text. If plain Unicode
clipboard text cannot be read, paste is visibly rejected **before any native
mutation**. Very large clipboard payloads still allocate one managed string;
streaming paste is a future performance improvement, not a data-safety gate. Actual
Chinese Pinyin candidate placement/cancel, UIA ranges outside the island,
Windows Arm64 interactive input and physical frame latency remain separate
release gates. Do not infer those from synthetic window messages.

Horizontal canvas navigation is source-anchored, not a 50 MiB pixel-width
measurement. Each visible row derives one DirectWrite origin from its bounded
`HorizontalRowWindow`; paint, pointer hit-testing, selection, diagnostics and
caret geometry use that same transform. `WM_MOUSEHWHEEL`, Shift+wheel and the
horizontal scrollbar request version-tagged source boundaries; the scrollbar
thumb is source-proportional and therefore approximate for variable-width text.
The active RichEdit host additionally uses native caret pixel geometry. A
DirectWrite-visible caret is reported as *unknown*, not visible, if native
`EM_POSFROMCHAR` disagrees with it. On a 16 Ki ASCII host, RichEdit's multiline
default wrapped source offset 3000 to Y=1233; `ES_AUTOHSCROLL` removed that
early wrap, but a far index still crossed an internal line-width limit. The
Windows controller therefore must bind a shorter platform-specific input
interval. Real IME candidate placement remains unverified and must not be
inferred from synthetic caret messages. Win32 contracts: [rich-edit styles],
[caret coordinates], and [pixel scroll position].

The input control is borderless and one visual row tall. The current opt-in
experiment reserves a themed **bottom input ribbon** outside the source canvas;
the canvas is the sole glyph painter for document rows, while the physical
RichEdit caret and IME candidate stay in the explicitly labeled input strip.
The ribbon shrinks the source viewport by one row. A tiny window whose client
height cannot contain both becomes input-only until enlarged: source paint and
source hit-test are suppressed and caret visibility is unknown, rather than
letting the two regions silently overlap. `ViewportResized` reports the actual
nonnegative body height, including zero; the controller's zero-height frame has
no painted source slices. Reflow is deferred during IME preedit.
This is a reversible diagnostic UX experiment, not a settled product layout:
the candidate appears away from its source-text caret.

Why the ribbon is being tested: a focused HWND probe on Windows build 26200
compared the actual
RichEdit `EM_POSFROMCHAR` positions (mapped into canvas client coordinates)
with DirectWrite hit tests on 41 valid grapheme boundaries surrounding the
active caret in a repeated `iW中🧪éمرحبا\t` line. The active caret was aligned,
but nearby glyph positions diverged by as much as **63.3 px**; offsets visible
inside the canvas also diverged. Two independent glyph painters must not both
draw this row in a promoted editor. An active-row sole RichEdit painter could
be reconsidered if it can cover the source window and own syntax/diagnostics,
but the 2 Ki host alone does not establish that. The opt-in mode must not be
described as default-ready without physical candidate and visual acceptance.

The historical differential used the one-row overlay AOT SHA-256
`93CBA034818E0CB9CA9371E8B5D4C4FD7C9AFCEA476BB0C83084A2ABFB7DD190`:
for each of 41 `CanvasInputWindowSelector`-certified boundaries in active ±24,
it compared `EM_POSFROMCHAR` + `MapWindowPoints(input,canvas)` with
`DirectWrite.HitTest(source)` + the same row origin. The current focused HWND
probe `.temp/WindowsHorizontalProbe/Probe.csproj` instead verifies that the
new ribbon host is physically below the mixed-script source caret, so those
two coordinate spaces must **not** be directly equated.
The strict-one-file win-x64 AOT external workflow
`tests/NativeWindowsHorizontalWorkflow.ps1` passed with binary SHA-256
`93CBA034818E0CB9CA9371E8B5D4C4FD7C9AFCEA476BB0C83084A2ABFB7DD190`
on Windows build 26200. Its 50 MiB unique tail selection copied source offset
52,428,677 (43 UTF-16 units), then replaced that interval, saved and reopened
with exact bytes; the RichEdit mirror remained at or below 2048 UTF-16 units.
The earlier one-row overlay AOT corrected physical canvas PNGs are under
`.temp/windows-horizontal/e138dbb44fda44898a8a705cb25d8351/` (old
three-row host) and `.temp/windows-horizontal/a3f5a0040d534108b081b31feec19df0/`
(one-row overlay host). These predate the bottom-ribbon experiment. A first
new-binary clipboard-copy attempt timed out before a
same-binary retry and three later runs passed; the intermittent failure is not
explained and the external workflow remains diagnostic rather than a CI gate.

[rich-edit styles]: https://learn.microsoft.com/en-us/windows/win32/controls/rich-edit-control-styles
[caret coordinates]: https://learn.microsoft.com/en-us/windows/win32/controls/em-posfromchar
[pixel scroll position]: https://learn.microsoft.com/en-us/windows/win32/controls/em-setscrollpos

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
