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
binding is at most 8 Ki UTF-16 units, leaving room below the 16 Ki native
pre-change hard limit for a transient ordinary edit or IME commit before
synchronous rebinding. It owns AppKit input, IME candidate
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
- [Run 36598787518](https://github.com/kleedaisuki/mote/actions/runs/36598787518)
  passed the revised published-Mach-O in-process horizontal probe on both
  Mac RIDs after separating the 8 Ki requested input binding from the 16 Ki
  native hard cap. At the start of the 50 MiB single-line document, an actual
  `NSTextView insertText:replacementRange:` call inserted one character into
  the canonical source; mote's Undo command restored the original length and
  remote marker before the 40 MiB pan, hit-test, selection/copy, four PNG, tiny-body,
  and source-file SHA checks all passed. The adapter now rejects any incoming
  normal binding larger than 8 Ki, so a future controller change cannot
  silently refill the native host to its pre-change limit. This closes the
  prior **in-process** ordinary-insert veto, not by itself external keyboard
  routing or real CJK marked-text behavior.
- The separate, non-gating **external** System Events + AX workflow in the
  same run passed once on `osx-arm64` for a 50 MiB single line: after verified
  same-process focus and selection, one keyboard `X` changed the source AX
  count from 52,428,800 to 52,428,801, showed the dirty marker, then native
  Save and a fresh process reopen checked exact bytes. Its privacy-safe trace
  includes pre-change allow (`T1`) and synchronous controller commit
  (`F2`/`F3`), rather than the old `T2` veto/AX dialog. This does **not**
  establish external long-line behavior on `osx-x64` or other input methods.

### Process-local live appearance evidence

[Run 36672506098](https://github.com/kleedaisuki/mote/actions/runs/36672506098)
passed the non-gating `--check-native-mac-theme` workflow using the **published
single Mach-O** on both `osx-x64` and `osx-arm64`. Each mode ran in a fresh
process with a synthetic Markdown fixture and an explicit repository `.temp`
configuration home; no user `~/.mote` configuration or global macOS appearance
setting was read or changed. The probe changed only its `NSWindow` appearance
DarkAqua → Aqua → DarkAqua. Both default `NSTextView` and opt-in canvas modes
observed three effective-appearance callbacks and resolved
`mote-dark → mote-light → mote-dark`. The native preview heading's foreground
was `#8DB9ED → #215FAD → #8DB9ED`; the default editor's **interior heading
glyph** was `#9CC6E8 → #245E9B → #9CC6E8`. The Markdown `#` marker was
observed separately and was not used as the semantic-heading oracle.

On both architectures, document generation/version and selection stayed
unchanged through the three appearance transitions. Undo restored the source
before the probe's one native edit, Redo restored the exact edited source, and
the fixture SHA-256 remained unchanged. Three AppKit view-raster PNGs per mode
changed dark→light and returned to the original dark hash. A canvas PNG hash
change establishes a **physical raster difference**, not correct individual
semantic pixels. An earlier hosted attempt exposed a callback/policy mismatch
consistent with parent-before-child appearance propagation: the child input
view reported dark while the resolved policy remained light. The shell now
classifies the canvas renderer's own effective appearance, the same view that
emits the notification. The input view also provides a post-propagation backup
signal, with deduplication.

This evidence covers process-local AppKit callbacks, palette application,
native attributed preview/editor colors, source/selection/Undo stability, and
bounded PNG capture. It does **not** establish response to an actual global
macOS light/dark preference change, appearance switching during real CJK IME
composition, VoiceOver behavior, or complete canvas semantic-pixel correctness.
The callback is Apple's
[`viewDidChangeEffectiveAppearance`](https://developer.apple.com/documentation/appkit/nsview/viewdidchangeeffectiveappearance%28%29?language=objc);
the diagnostic override uses Apple's named
[`NSAppearanceNameDarkAqua`](https://developer.apple.com/documentation/appkit/nsappearance/name-swift.struct/darkaqua?language=objc)
and Aqua appearances on one window only.

The same PNG series exposed a measurement pitfall in the bottom status strip.
The earlier black-looking cached pixels were actually **alpha 0**: they did
not prove that the external compositor displayed a black strip. Setting only
`NSWindow.backgroundColor` changed window backing but did not make the
content-view cache self-contained. A decorative, accessibility-hidden 30-DIP
`NSView` now fills the exposed strip with `Palette.WindowBackground` behind
the transparent status `NSTextField`; it does not own input or source text.
An intermediate probe incorrectly converted `NSBitmapImageRep.colorAtX:y:`
through sRGB and reported dark `#2B2C30` even though the retained PNG stored
opaque `#202124`. The final probe uses Apple's raw
[`getPixel:atX:y:`](https://developer.apple.com/documentation/appkit/nsbitmapimagerep/getpixel%28_%3Aatx%3Ay%3A%29?language=objc)
samples with explicit 8-bit RGB/alpha format checks. In
[run 36676668596](https://github.com/kleedaisuki/mote/actions/runs/36676668596),
both Mac RIDs and both editor modes returned exact opaque status samples
`#202124 → #F1F2F4 → #202124`, while source/selection/Undo and the original
theme checks remained green. The light-mode cached AppKit view image has a
readable status strip. This validates the **content-view raster** and its
native draw callback, not the physical display compositor or global OS theme.

### Synthetic marked-text appearance diagnostic

`--check-native-mac-composition-theme` is a separate, non-gating published-binary
diagnostic for a process-local window appearance change **during** marked text.
It creates a small synthetic Markdown fixture under repository `.temp` and
stores JSON and up to five AppKit view rasters under an empty `.cache` child. The
default and experimental canvas modes run in separate processes. The probe
calls `NSTextView`'s `setMarkedText:selectedRange:replacementRange:` with a
synthetic candidate, switches only its window from dark to light, and requires
the OS effective appearance to change while the applied policy, canonical
source/version, and input-file hash remain unchanged. It then uses
`unmarkText`, for which [Apple explicitly says the text view should accept
the marked text](https://developer.apple.com/documentation/appkit/nstextinputclient/unmarktext%28%29?language=objc),
and requires one exact source insertion, final light palette, a settled
composition callback, and functional Undo/Redo. Per-phase JSON records
versioned source hashes and selection coordinates, not document text.
The retained images are for visual inspection: because the synthetic
candidate appears and disappears, different PNG hashes alone cannot prove
that themed pixels changed correctly. The automated color oracle reads the
native preview heading's attributed foreground, not the compositor.

Apple cautions that [`NSTextInputClient` methods](https://developer.apple.com/documentation/appkit/nstextinputclient?language=objc)
are primarily for the text-input system and generally unsuitable for unrelated
programmatic editing. This diagnostic is explicitly synthetic: a passing run
would not validate a physical keyboard, a real Chinese input method, candidate
window geometry, or real input-method cancellation. The follow-up synthetic
cancellation phase first clears the provisional marked range and asks `NSTextInputContext` to
discard its conversion session, then requires an unmarked restored native
host and **no extra engine version**. Its target-host outcome is reported
below; `unmarkText` is not a cancellation mechanism.

[Hosted run 36680070533](https://github.com/kleedaisuki/mote/actions/runs/36680070533)
passed this non-gating **commit-only** diagnostic on published `osx-arm64`
and `osx-x64` Mach-O executables, in both default and canvas modes. During
synthetic marked text, the window reported light while the applied policy
stayed dark, preview heading remained `#8DB9ED`, and canonical source stayed
at generation 2/version 0 with its original SHA-256. After `unmarkText`, one
settled callback preceded the latest light policy and preview accent
`#215FAD`; the one UTF-16-unit candidate was inserted at source offset 38
in the default view and 20 in the bounded canvas host, advancing to version
1. Default marked/native selection was 39, whereas canvas global selection
remained 0 until commit and then moved to 21. Undo and Redo restored exact
source and caret positions (38→39 default; 20→21 canvas), while the on-disk
fixture hash remained unchanged. The three retained PNGs per mode are
inspectable but are not used as a semantic-pixel or compositor oracle.
No real CJK input method or cancellation was exercised.

The first follow-up synthetic cancellation extension did **not** pass.
[Run 36682437200](https://github.com/kleedaisuki/mote/actions/runs/36682437200)
passed the strict jobs, but its non-gating composition diagnostic failed in
both Mac RIDs and both editor modes at `stage 7 check cancelled-dark` after
capturing `dark-marked`. The committed candidate remained at generation 2,
version 1 in that captured phase; three appearance callbacks and two
composition-settled callbacks were observed by the failing probe. The stage-7
check originally combined canonical source, native host, selection, and preview
assertions, so this evidence does **not** identify which property failed. A
content-free check-code rerun was needed. This is a synthetic AppKit protocol
result, not evidence about an actual Chinese input method or its cancellation.

The check-code rerun,
[run 36684299172](https://github.com/kleedaisuki/mote/actions/runs/36684299172),
isolated the first failure to **native host restoration** in all four cases.
Canonical generation/version/source and the input-file hash stayed equal to
the committed state (source length 39, version 1), and analysis remained ready.
The native host length returned to its pre-cancellation length (39 in default,
21 in canvas) but its contents did not match. Default native selection also
missed its expected caret; canvas global selection remained correct. This
supports neither a successful cancellation nor a claim of engine data loss.
The next diagnostic recorded mismatch positions and native range coordinates
without logging characters or paths, to distinguish a direct-selector range
interpretation error before changing the test sequence.

[Run 36685502550](https://github.com/kleedaisuki/mote/actions/runs/36685502550)
found the same result in both architectures and modes: after the synthetic
clear request, the host was exactly `hostBefore[1..] + cancelCandidate`, with
its first mismatch at offset zero. The direct `NSTextView` selector call with
`replacementRange=(0,1)` therefore replaced document offset zero in this
probe, while the canonical source/version/file remained unchanged. Apple's
[`NSTextInputClient` specification](https://developer.apple.com/documentation/appkit/nstextinputclient/setmarkedtext%28_%3Aselectedrange%3Areplacementrange%3A%29)
describes the replacement range relative to marked text; this observation is
about this direct programmatic `NSTextView` call, **not** a claim that the
protocol generally uses absolute ranges. The corrected probe passes the native
`markedRange()` returned by that same text view and retains every source,
host, selection, callback, theme, and Undo/Redo assertion.

[Run 36686933536](https://github.com/kleedaisuki/mote/actions/runs/36686933536)
passed the non-gating **synthetic commit-and-cancel diagnostic** on published
`osx-arm64` and `osx-x64` Mach-O executables in both default and canvas modes.
All four mode/RID cases captured five phases, including `dark-marked` and
`dark-cancelled`, with three appearance callbacks and two settled callbacks.
During the second candidate, the canonical source stayed at generation 2,
version 1; after clearing the marked range, the native host, canonical source,
file hash, and exact caret returned to the post-commit state with **no extra
engine version**. Default native selection moved 39→40→39; canvas global
selection stayed 21 throughout the second mark. The latest dark policy and
native preview accent applied after cancellation, and Undo/Redo still restored
the original committed edit. This validates only the controlled programmatic
AppKit sequence; it is not evidence that a real CJK input method, conversion
session, candidate window, or user cancellation follows the same path.

- Real CJK candidate/commit/cancel and resize behavior, VoiceOver navigation,
  bidirectional selection geometry, and practical latency remain release
  gates. Do not infer product parity from an in-process AppKit selector or PNG
  capture alone.
