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
run 36557417180). The correction requires new target-host screenshot review.
Selection geometry is
single-rectangle-per-row and not yet correct for discontiguous bidirectional
selections; grapheme snapping only sees a bounded slice, whose hidden prefix
could begin inside a cluster. Horizontal trackpad scrolling and text input are
not implemented. The custom view is read-only, and `NSTextView` remains the
shipped editor. The new on-screen probe still needs macOS arm64/x64 Native AOT
execution and visual inspection before calling this path verified.

## Primary API references

- [CTLineGetOffsetForStringIndex](https://developer.apple.com/documentation/coretext/ctlinegetoffsetforstringindex%28_%3A_%3A_%3A%29)
- [CTLineGetStringIndexForPosition](https://developer.apple.com/documentation/coretext/ctlinegetstringindexforposition%28_%3A_%3A%29)
- [CTLineDraw](https://developer.apple.com/documentation/coretext/ctlinedraw%28_%3A_%3A%29)
- [CGBitmapContextCreate](https://developer.apple.com/documentation/coregraphics/cgbitmapcontextcreate)
- [NSView cacheDisplayInRect:toBitmapImageRep:](https://developer.apple.com/documentation/appkit/nsview/cachedisplay%28in%3Ato%3A%29)
- [NSEvent scrollingDeltaY](https://developer.apple.com/documentation/appkit/nsevent/scrollingdeltay)
- [kCTForegroundColorFromContextAttributeName](https://developer.apple.com/documentation/coretext/kctforegroundcolorfromcontextattributename)

## Opt-in interactive input island (unverified)

The experimental `--canvas-experimental` shell embeds a continuous, source-backed
AppKit `NSView` in the existing editor window. A real, visible `NSTextView` inside
an `NSScrollView` occupies only the active logical row and receives a single
controller-supplied source window of at most 16 Ki UTF-16 units. It is not a
hidden whole-document mirror. Off-host rows are read from immutable snapshots
as bounded `ViewportSlice` values and shaped by CoreText. Wheel and pointer
callbacks report source coordinates to the controller; semantic tokens are
clipped to visible off-host rows and redrawn in theme policy colors. Bounded
diagnostics receive severity-colored underlines or zero-width markers on
those rows. The normal editor
still uses its previous `NSTextView` path unless explicitly opted in.

Each final native text change is converted by `NativeTextProjection` to one
absolute source `TextChange` tagged with document generation, snapshot version,
and binding nonce. The synchronous controller event must either publish a new
binding before returning or reject the operation; a reentrant binding is
applied only after the current AppKit delegate callback unwinds. Marked text
stays in AppKit: candidate text is not submitted, input rebinds are rejected,
theme changes are deferred, and save/open/close synchronously unmark and settle
the final text or veto the command. Global selection outside the host replaces
the controller's full source interval rather than only the local text range.
The `shouldChangeTextInRange:replacementString:` delegate captures the exact
native pre-edit range. For selected text, the inserted payload is reconstructed
from the unchanged prefix/suffix of the before/after host strings; using the
minimal-difference inserted text would corrupt repeated-text cases such as
`abc`, select `ab`, type `a` (correct result: `ac`). A failed exact mapping or
controller acknowledgement restores the canonical host; an unexpected AppKit
callback failure disables/hides input rather than leaving a divergent editor.
Backspace/Forward Delete on a global selection dispatches one absolute source
deletion before AppKit's page-local command runs. This covers selections ending
on an empty LF/CRLF row, where the native host has no characters and would
emit no `textDidChange`; a dedicated AppKit probe tests both selection
directions and both line-ending spellings.
The host is plain-text-only. Its custom AppKit `paste:` and
`pasteAsPlainText:` methods preflight the OS pasteboard's plain-text UTF-16
length before insertion. Small pastes use native input; a larger plain-text
paste becomes one exact absolute controller edit without ever entering the
bounded host. Rich formatting is ignored. An impossible resulting Int32 source
length, an edit above the controller's 32 MiB undo-history budget, or rich-only
clipboard is rejected visibly without partial insertion.
These paths still need target-host mixed-format/large-payload testing.
The same pre-change delegate rejects any non-paste input that would exceed the
bound, with a visible explanation; real IME preedit behavior near the bound
still requires interactive testing. The published-binary in-process clipboard
probe exercises mixed rich/plain 40 Ki direct paste with byte-exact source
verification and Undo, 50 MiB explicit rejection with unchanged engine/native
host/disk and retained Redo history, bounded host retention, no-edit Select
All→Save/Close, exact selected repeated-text replacement, and small accepted
Save As/reopen. A future persistent-root history policy may permit 50 MiB
paste with Undo, but silently accepting a non-undoable edit is not allowed.
During a window resize under marked text, only the existing host frame moves
to keep AppKit's candidate rectangle attached; viewport reflow is delivered
after composition settles. Real CJK candidate positioning remains a gate.
When composition returns the original host string under a nonempty global
selection, the current shell conservatively treats it as cancellation rather
than deleting off-host text: it cannot distinguish an identical final IME
candidate from cancel using text equality alone. A real CJK commit/cancel test
and explicit final `insertText:replacementRange:` evidence are required before
claiming semantic parity for this case; `unmarkText` alone is not proof.

This path is **not yet product-ready**. In particular, this code has not passed
target-host interactive open/edit/undo/save/reopen, real Chinese/Japanese/Korean
IME candidate/cancel tests, or VoiceOver review. The native input host exposes
only its bounded row through AppKit accessibility; document-wide accessibility
semantics require a separate design. The opaque caret-row `NSTextView` currently
uses the base theme foreground, so its local row lacks per-token colors and
inline diagnostic underlines; preview/status still expose diagnostics. Do not
infer any of these behaviors from the read-only
canvas PNG probe.
