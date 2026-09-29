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

## Read-only on-screen NSView experiment

`MacOnScreenCanvasProbe.Run(manyLinePath, longLinePath, outputDirectory, theme)`
now creates a real top-level AppKit `NSWindow` with an Objective-C-backed
`NSView`. `drawRect:`, `scrollWheel:`, and mouse down/drag/up handlers share
`CanvasInteraction` over the immutable engine snapshot. Each paint reads only
the currently visible `ViewportSlice` strings (at most 16 Ki UTF-16 units per
slice), shapes them with CoreText, and paints with CoreGraphics using the
selected theme policy. Selected text is redrawn through a clipped graphics
state in the policy's explicit `SelectionForeground`, rather than leaving
ordinary syntax foreground on the selection surface. No `NSTextView` copy of the 100 MiB or 50 MiB source
exists. The synthetic probe invokes the same wheel/pointer methods that OS
events call, verifies a global selection crossing the former 64 Ki boundary,
seeks to a remote bounded slice in a single long line, and requires at least
three visible-window `drawRect:` callbacks. It captures distinct numbered-row
rasters before and after the wheel gesture so visual QA can compare line
ordinals, not merely scroll-model counters; a separate raster shows the
cross-seam selection. A small third fixture exercises
pointer hit-testing on intermediate empty LF and CRLF rows, selecting through
both to capture the trailing newline markers. It writes five AppKit view-raster
PNGs under the specified repository `.cache/` or `.temp/` directory.
For each theme, `mac-canvas-{theme-id}-metadata.txt` records the exact shell
command, macOS version, backing-display scale, resolved CoreText PostScript
font name, published executable SHA-256, visible paint count, and absolute PNG
paths. This metadata is a CI artifact, never a file shipped with the binary.

The PNGs come from `NSView.cacheDisplayInRect:toBitmapImageRep:` after the
window has displayed, avoiding Screen Recording permissions. They verify the
view's drawing path and are inspectable artifacts, **not** proof of physical
compositor presentation or measured frame latency. Selection painting is
configured with CoreText's `kCTForegroundColorFromContextAttributeName =
kCFBooleanTrue`: without it, CoreText's attributed-string default painted
black glyphs despite the themed CGContext fill (observed in hosted arm64
run 36557417180). The fix passed three-theme PNG contrast and visual review
on both Mac Native AOT RIDs in
[run 36558583763](https://github.com/kleedaisuki/mote/actions/runs/36558583763),
and became a blocking read-only canvas gate in
[run 36559682956](https://github.com/kleedaisuki/mote/actions/runs/36559682956).
Selection geometry is
single-rectangle-per-row and not yet correct for discontiguous bidirectional
selections; grapheme snapping only sees a bounded slice, whose hidden prefix
could begin inside a cluster. Horizontal trackpad scrolling and text input are
not implemented **in this read-only probe**. Its passing pixel/scroll fixture
is not a timing, input, or IME result. The default shipped editor remains
`NSTextView`; the separate opt-in interactive canvas below has its own gates.

## Primary API references

- [CTLineGetOffsetForStringIndex](https://developer.apple.com/documentation/coretext/ctlinegetoffsetforstringindex%28_%3A_%3A_%3A%29)
- [CTLineGetStringIndexForPosition](https://developer.apple.com/documentation/coretext/ctlinegetstringindexforposition%28_%3A_%3A%29)
- [CTLineDraw](https://developer.apple.com/documentation/coretext/ctlinedraw%28_%3A_%3A%29)
- [CGBitmapContextCreate](https://developer.apple.com/documentation/coregraphics/cgbitmapcontextcreate)
- [NSView cacheDisplayInRect:toBitmapImageRep:](https://developer.apple.com/documentation/appkit/nsview/cachedisplay%28in%3Ato%3A%29)
- [NSEvent scrollingDeltaY](https://developer.apple.com/documentation/appkit/nsevent/scrollingdeltay)
- [kCTForegroundColorFromContextAttributeName](https://developer.apple.com/documentation/coretext/kctforegroundcolorfromcontextattributename)

## Opt-in interactive canvas and input ribbon

`mote --canvas-experimental [path]` is an explicit, reversible path; the default
editor remains the established `NSTextView` shell. The experimental source body
is one AppKit `NSView` that paints **every** visible logical row from bounded
`CanvasFrame.RowWindows` with CoreText/CoreGraphics. A separate, always-visible
36-DIP bottom ribbon contains a focused, plain-text `NSTextView` whose source
window is at most 16 Ki UTF-16 units. It owns AppKit input, IME candidate
position, and local undo-independent text composition, not a whole-file copy or
a second rendered source row. The source body height is the physical canvas
height minus the ribbon, floored at zero; a zero-height body publishes no
visible source rows. The minimum window size keeps an ordinary usable body,
while programmatic tiny resize is still handled without hiding first
responder. Source panning never moves or rebinds the ribbon during marked text.

The controller supplies generation-, snapshot-version-, and nonce-tagged input
bindings. The island converts a final native edit into one absolute source
`TextChange` and emits it synchronously; the controller must validate and
publish a new binding before the next input. The `NSTextView` pre-change
delegate captures the exact replacement range, since a minimal text diff does
not preserve selected repeated-text payloads (`abc`, select `ab`, type `a`
must yield `ac`). Global deletion also bypasses a native no-op when the active
end is an empty LF/CRLF row. Delayed selection notifications can only override
a global source selection when armed by a real native user gesture; repeated
unreconcilable echoes disable the host rather than allowing silent divergence.
Large plain-text paste is committed directly to the engine without growing the
bounded host; rich formatting is ignored. Native marked text remains
AppKit-owned, with binding/theme/ribbon movement deferred until composition
settles or save/open/new/close is vetoed. A same-text IME commit versus cancel
under an off-host global selection is still an explicit real-CJK acceptance
gate.

The source body uses one left-edge transform for CoreText painting, selected
foreground/background, semantic colors, diagnostic marks, caret geometry, and
pointer hit-testing. Horizontal wheel deltas request a versioned source-bound
anchor; no 50 MiB line is shaped. Geometry returns unknown instead of claiming
visibility at ambiguous grapheme/bidirectional or hidden-suffix boundaries.
The ribbon caret uses its own TextKit/NSClipView geometry and is deliberately
**not** compared to the CoreText source-row X coordinate. AX exposes one
source-backed text area with a parent-space frame equal to the body rectangle,
excluding the input ribbon; the focused `NSTextView` remains usable for OS
input. This is API-level accessibility, not VoiceOver acceptance.

### Current target-host verdict

- [Run 36591084601](https://github.com/kleedaisuki/mote/actions/runs/36591084601)
  restored macOS arm64 opt-in startup after an ABI fault: `NSFont
  fontWithName:size:` requires an Objective-C object pointer followed by a
  `CGFloat`. The generic two-double `objc_msgSend` signature put the NSString
  pointer in a floating-point register on arm64 and caused a native crash.
  `ObjC.SendObjectDouble(nint,nint,nint,double)` now expresses the exact ABI;
  the blank GUI and small clipboard LLDB probes no longer receive a signal.
  Both macOS RIDs passed the strict mixed 40 Ki / 50 MiB clipboard, LF/CRLF
  selection-delete, and in-process AX workflows in this run. Those probes do
  not emulate a physical trackpad, actual CJK IME, or a screen reader.
- [Run 36594818528](https://github.com/kleedaisuki/mote/actions/runs/36594818528)
  passed the **in-process AppKit** horizontal workflow on both published Mac
  AOT RIDs. Distinct before/after PNGs show source offset 3000 in a 16 Ki row
  and offset 40 MiB in a 50 MiB row; bounded slices, source hit-test, global
  selection/copy, and unchanged fixture SHA-256 all passed. New plus native
  insertion of `abc` leaves its source caret at local X = 12 DIP and visible;
  the earlier left-edge regression in run 36591084601 put it at -11.480 DIP.
  At the configured minimum window content size, the measured canvas was
  268×90 DIP, with a 54-DIP source body and 120-DIP ribbon clip: source caret,
  host alignment, visibility, and focus passed. A separately forced zero-body
  canvas exposed zero rows and no source caret, then recovered source and
  focused input after restoring normal geometry. These are OS-backed view and
  selector checks, but the pan/click calls are diagnostic invocations, not an
  external physical trackpad or keyboard trace.
- External long-line keyboard/save routing, real CJK candidate/commit/cancel
  and resize behavior, VoiceOver navigation, bidirectional selection geometry,
  and practical latency remain release gates. Do not infer product parity from
  in-process AppKit selectors or PNG capture alone.
