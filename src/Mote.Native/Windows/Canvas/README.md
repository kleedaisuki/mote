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

The temporary probe's `.cache` output is not the product's single-binary
deliverable. The hosted gate verifies **read-only hit-test feasibility**, not
an on-screen painted canvas or frame latency. Before using this adapter for
editing, measure creation and reuse of layouts on real visible rows; verify
text range selection, wrapping, DPI changes, font fallback, bidi caret
affinity, remote horizontal geometry, IME, and screen-reader range semantics
on both Windows architectures.

## Primary references

- [DirectWrite factory and sharing](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/nn-dwrite-idwritefactory)
- [CreateTextLayout](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/nf-dwrite-idwritefactory-createtextlayout)
- [IDWriteTextLayout hit-testing](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/nn-dwrite-idwritetextlayout)
