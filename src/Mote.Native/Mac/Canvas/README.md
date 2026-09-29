# macOS CoreText canvas geometry probe

`MacCanvasProbe.Run()` is a small, read-only bridge from the engine's bounded
`ViewportSlice` to macOS system CoreText/CoreGraphics. It does not replace
`NSTextView` or own input, selection, document state, IME, or accessibility.
It adds no bundled font, helper executable, dynamic plugin, or generated asset.

The probe builds a four-line immutable `Document` (ASCII, CJK, Arabic RTL,
emoji ZWJ), obtains visible source-backed slices from `ContinuousViewport`,
and checks that each slice still maps to its exact UTF-16 source interval.
It creates one `CTLine` per slice with a 18-point Menlo base font and system
font fallback, records typographic width, queries caret offsets at source
indices, hit-tests left/middle/right pixel positions, and draws each line to
an in-memory 64-pixel-high CoreGraphics bitmap. All hit indices must remain
within the corresponding slice, and drawing must produce nonzero pixel data.
There is no file output. The result records measurements to make target-OS
behavior inspectable rather than merely reporting a boolean.

**Interpretation limit:** `CTLineGetStringIndexForPosition` returns a UTF-16
insertion index, but grapheme clusters, ligatures, emoji ZWJ, and bidirectional
text may have multiple caret positions or a secondary caret offset. Valid
bounds and nonempty paint are not proof of an editor-quality visual selection
model. Horizontal geometry of a `ViewportSlice` with hidden prefix/suffix is
only *local* to that window; exact remote horizontal placement requires more
context. No keyboard, mouse, IME, screen reader, or on-screen frame is
exercised.

## Target-host evidence (2026-09-29)

[Fully green GitHub Actions run 36553927870](https://github.com/kleedaisuki/mote/actions/runs/36553927870)
confirmed the strict one-file Native AOT probe on both macOS architectures.
The first target-host probe pass, [run 36552700358](https://github.com/kleedaisuki/mote/actions/runs/36552700358),
recorded these inspectable outputs (its overall red result was a separate
AppleScript syntax error before editor input):

| RID | Exact diagnostic stdout | Result |
| --- | --- | --- |
| `osx-arm64` | `mote-native-mac-canvas-ready cases=4 painted=3681` | Passed |
| `osx-x64` | `mote-native-mac-canvas-ready cases=4 painted=3685` | Passed |

The `painted` count is nonzero bytes in a CoreGraphics **offscreen** bitmap,
not displayed frames, frame latency, visual correctness, or editability. These
results establish target-host CoreText/CoreGraphics ABI and bounded geometry
feasibility only. The current `NSTextView` remains the product editor.

## Primary API references

- [CTLineGetOffsetForStringIndex](https://developer.apple.com/documentation/coretext/ctlinegetoffsetforstringindex%28_%3A_%3A_%3A%29)
- [CTLineGetStringIndexForPosition](https://developer.apple.com/documentation/coretext/ctlinegetstringindexforposition%28_%3A_%3A%29)
- [CTLineDraw](https://developer.apple.com/documentation/coretext/ctlinedraw%28_%3A_%3A%29)
- [CGBitmapContextCreate](https://developer.apple.com/documentation/coregraphics/cgbitmapcontextcreate)
