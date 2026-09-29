# Horizontal navigation for the opt-in continuous canvas

Status: **focused design, not implemented or accepted**, 2026-09-29. This is the no-wrap, 50 MiB single-logical-line slice of [virtual-editor-design.md](virtual-editor-design.md), not a second editor architecture. The engine snapshot remains the only text authority; the bounded RichEdit/NSTextView island remains the only native composition host. The experimental canvas must not replace the current small-file workflow until target-OS input and accessibility gates pass.

## Why the present model cannot pan

`ContinuousViewport.SetHorizontalOffset(double)` stores a number but has no call sites. `CanvasInteraction.Frame()` asks `GetVisibleSlices(... focusSourceOffset: TopAnchor.SourceOffset)`; `CanvasFrame` carries no horizontal position. Thus a 50 MiB one-line file always paints the bounded slice around its *vertical* source anchor, and wheel/scrollbar input cannot reveal remote columns. The production Windows island handles `WM_MOUSEWHEEL`/`WM_VSCROLL` but not `WM_MOUSEHWHEEL`/`WM_HSCROLL`; the Mac island uses only `scrollingDeltaY` and disables its host's horizontal scroller. The current Windows painter, selection/diagnostic geometry, pointer hit-test, and Mac painter each assume a fixed left text origin. Merely wiring `SetHorizontalOffset` would not move source slices or make those independent coordinates agree.

The 50 MiB line makes two shortcuts invalid: shaping the whole line is unbounded, and a global pixel X cannot be inferred from a far UTF-16 offset without measuring the prefix. At 8 px per ASCII unit, the hypothetical width is about 400 million px; passing that as a `float` layout origin would also lose local-pixel precision. Keep **source positions absolute and local drawing coordinates small**.

## One view-owned horizontal anchor

Represent the left viewport edge as a source boundary in one reference logical line plus a fractional pixel displacement into the following shaped cluster:

```csharp
// Target internal contract, not current signatures.
record HorizontalAnchor(
    int ReferenceLineStart,       // source line start at last explicit horizontal action
    int SourceBoundary,           // grapheme edge in that line's content, UTF-16
    CaretAffinity Affinity,       // visual side where one source boundary has two carets
    double IntraClusterPixels,    // [0, next visual caret advance), DIP
    double? MeasuredXFromStart);  // exact only if the complete prefix was measured

record HorizontalRowWindow(
    ViewportSlice Slice,
    int LeftEdgeSourceBoundary, CaretAffinity Affinity,
    double IntraClusterPixels);
```

`ReferenceLineStart <= SourceBoundary <= contentEnd`, and `SourceBoundary` must be a grapheme/shape-safe boundary, never between UTF-16 surrogate halves or CR/LF of a line ending. `IntraClusterPixels` is finite and nonnegative; normalize it by moving to a neighboring visual caret when it reaches the cluster advance. The source anchor and pixel residual are **one state transition**, not a bare `HorizontalOffset` plus an unrelated slice. `MeasuredXFromStart` is nullable rather than a guessed global width. A 50 MiB line normally has no exact measured prefix. Canvas frames are immutable/versioned and must include the horizontal anchor and each visible row's resolved window; a shell cannot reconstruct a different window from its own caret or native-host page.

Replace the unused `SetHorizontalOffset(double)` state with this anchor (or expose a derived pixel estimate for diagnostics only). Keeping both independently mutable would recreate the original discrepancy between chosen source slice and painted X. Horizontal events never change `ViewportAnchor` or `Document.Version`.

For every row, choose a source window that includes its left-edge anchor and enough rightward text to cover the viewport plus overscan. The platform shaper returns a local `x(anchor)` and hit-test map for that **bounded** window. Paint, semantic underlines, selection, caret, island placement, point hit-test, and AX range geometry use the same transform:

```text
screenX(source) = textInset + shapedLocalX(source)
                  - shapedLocalX(leftEdgeAnchor) - intraClusterPixels
```

The inverse for a pointer is `localX = pointerX - textInset + shapedLocalX(anchor) + intraClusterPixels`. Clip to the editor viewport, not the full layout width. Existing `ViewportSlice.HasHiddenPrefix/Suffix` continues to mean hidden *source* on that logical row; it is not permission for UIA visible ranges to advertise unpainted text. Current code paths in `WindowsRichEditIsland.Paint/HitSource/DrawSelection/DrawDiagnostics` and the Mac island painter must take this same row transform, replacing each hard-coded `TextLeft`/left inset usage together. This is the critical integration seam.

### Bounded panning and remote jumps

1. On a small wheel/trackpad/scrollbar-line delta `dx`, hit-test `anchor + residual + dx` within the current locally shaped row and normalize to a new cluster boundary/residual. If it crosses the window edge, shape one adjacent bounded window with context and continue; ordinary deltas should require one window. Keep a strict per-event work budget and schedule continuation rather than blocking paint on a pathological cluster.
2. On `Go to offset`, Find, End, or scrollbar-thumb jump, choose the target source boundary directly with rope line lookup, snap to a valid visual edge, and shape only around that target. Do **not** march through 50 MiB of prior text or compute an invented exact global X. The revealed caret's *local* geometry is exact; the far thumb position is approximate until width checkpoints exist.
3. Request at most 4-16 Ki UTF-16 units of visible text per row, plus bounded context/overscan, under a frame-level shape budget. Continue to forbid `GetLine()` on this path. Segment seams need a verified shaping rule: for contextual scripts and bidi, compare the segment against unsliced platform shaping on moderate fixtures. If the necessary context exceeds the bound, show a truthful slow-path/unsupported state rather than fabricating caret geometry. Monospace ASCII is a fast path, not the Unicode contract.

For ordinary short lines that fit the shaping budget, fully measured width gives an exact `MeasuredXFromStart`; moving vertically can preserve that common pixel X. For an exceptional unmeasured line, preserve the reference line's **source column** (`SourceBoundary - ReferenceLineStart`) and pixel residual as a fallback on the newly visible row, clamped to that row's content edge. This is intentionally not an exact cross-line pixel alignment claim. Crucially, vertical `ScrollBy` never changes the horizontal reference anchor: scrolling away from and back to the 50 MiB line must reveal the same remote text. A later lazy width-checkpoint index may improve cross-line pixel mapping and thumb estimates, but is not required for locally exact panning. Never run a full-line prefix scan synchronously for that improvement.

## Event and ownership map

| Action | Windows shell | macOS shell | Single controller transition |
| --- | --- | --- | --- |
| Horizontal wheel/trackpad | Handle [`WM_MOUSEHWHEEL`](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousehwheel); positive wheel delta is rightward. Accumulate fractional deltas instead of discarding sub-120 input. Optionally map Shift+`WM_MOUSEWHEEL` by an explicit precedence rule, not by native-host accidental scrolling. | Read [`NSEvent.scrollingDeltaX`](https://developer.apple.com/documentation/appkit/nsevent/scrollingdeltax) and `hasPreciseScrollingDeltas` in the canvas `scrollWheel:` path. Do not manually invert natural-scroll direction twice; calibrate sign with a device-level probe. | `PanHorizontal(dxDIP)` updates the source+pixel anchor, generates a new frame, repositions the island if safe, and repaints. No document edit/version change. |
| Horizontal scrollbar | Handle `WM_HSCROLL` line/page and thumb. For `SB_THUMBTRACK`, use [`GetScrollInfo(SIF_TRACKPOS)`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getscrollinfo), not `HIWORD(wParam)`'s 16-bit position. | Use a **canvas-owned** `NSScroller` action/value, not the bounded NSTextView host's scroller; Apple documents [`NSScroller` knob/value](https://developer.apple.com/documentation/appkit/nsscroller). | Line/page means a local pixel pan. A far thumb chooses a source fraction of the reference line and shapes near that boundary. The thumb is source-proportional/estimated until width summaries are measured; do not label it an exact pixel-width fraction. |
| Keyboard navigation | Preserve OS-native key/IME dispatch in RichEdit; global End/Home/Find/selection commands already route through controller. | Preserve `NSTextView` command/IME dispatch; controller receives the committed global source selection. | `RevealCaret(offset, affinity)` shifts horizontally **only if** the caret is outside the painted interval, with modest margin. Shift-selection remains global; a remote End jumps directly. Do not rebase the island while composing. |
| Pointer drag/autopan | `WM_LBUTTONDOWN/MOUSEMOVE/UP` must inverse-hit-test through the same row origin; dragging past left/right edge schedules bounded pan increments. | `mouseDown:/mouseDragged:/mouseUp:` uses the same inverse Core Text coordinate map; outside-edge drag schedules bounded increments. | Only global UTF-16 `Anchor/Active` change. Selection paint and clipboard remain source-backed even across unpainted middle intervals. |

The controller owns horizontal state together with `CanvasInteraction`; shells own only OS event normalization, glyph shaping, and paint. During IME composition, reject/defer horizontal rebase and scrollbar jumps that would move the input host or candidate rectangle. The current vertical path already suppresses scrolling while composing; horizontal navigation should use the same settle/veto rule. After a committed edit, transform the reference source boundary with the engine `TextChange` and right affinity, recompute its line start, clamp to content, invalidate local layout, and preserve the pixel residual only if the same adjacent visual cluster survives; otherwise reset residual to zero. An edit on another line does not reset horizontal pan. Font/DPI/tab/theme typography changes keep the source boundary but remeasure the residual/geometry; mere recoloring does not. New/Open resets the anchor to column zero; Save does not.

For a short line, both platforms already expose exact local source/caret mappings through [DirectWrite hit-testing](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/nf-dwrite-idwritetextlayout-hittestpoint) and [Core Text index/offset mapping](https://developer.apple.com/documentation/coretext/ctlinegetoffsetforstringindex%28_%3A_%3A_%3A%29). The new row transform must be used for the native island's placement and for UIA `RangeFromPoint`/bounding rectangles and AppKit `accessibilityFrameForRange:` once those APIs are completed. Accessibility `DocumentRange` stays `0..snapshot.Length`; `VisibleRanges` contains only painted source intervals (Mac's documented whole-line visible-range convention remains separately noted in [accessibility-provider-design.md](accessibility-provider-design.md)). A horizontal pan cannot alter global selection or silently normalize CR/LF/CRLF. `SourceOffset` is a UTF-16 boundary; ordinary pointer/keyboard caret positions follow grapheme and visual affinity, while source APIs may still address any legal engine boundary.

The ongoing AX handoff uses `AccessibleCanvasState(generation, snapshot, CanvasFrame)`: extend **that same immutable frame** with horizontal row windows. Provider visible ranges and future point/range geometry must derive from it, never from an independently updated provider-side pan coordinate. Otherwise a reader can observe the old source interval with the new painted X after a wheel event.

## Verification that can falsify the design

| Probe | Pass criterion; failure means revise the model, not add another offset field |
| --- | --- |
| Pure state property test | Generate line lengths including 0, 1, 16 Ki, and 50 MiB; pan locally, vertical-scroll away/back, resize/reflow, edit before/at/after anchor. Reference source boundary stays in content; generation/version advance only on edits; no vertical action resets horizontal anchor. Compare each emitted row window against the anchor invariant. |
| Boundedness/instrumentation | On a 50 MiB unique-marker line, cold Go-to-offset at 40 MiB, ten wheel pans, far thumb jumps, and remote End never call `GetLine()` or allocate/shape proportional to 50 MiB. Record bytes allocated, source units shaped, UI-thread p50/p95/p99, input-to-draw submission, and OS working set separately. Initial target: <=16 Ki visible units plus bounded context per row, <=64 MiB total view cache, no synchronous full-prefix scan; tune time thresholds from four-RID measurements, not a desktop-host guess. |
| Pixel/source oracle | On moderate mixed ASCII, tabs, CR/LF/CRLF, surrogate emoji, combining/ZWJ, Arabic/Hebrew/Indic fixtures, compare sliced and unsliced DirectWrite/CTLine source-to-X and X-to-source at every seam and at both bidi affinities. A mismatched caret/selection/AX rectangle invalidates the selected seam rule. |
| Real OS interaction | In published one-file win-x64/win-arm64 and osx-x64/osx-arm64 binaries, drive physical/automation horizontal wheel and trackpad, line/page/thumb scrollbar, Home/End and Shift-selection, pointer drag with edge autopan, vertical away/back, and `Find` at 40 MiB. Screenshot/hash a distinct remote marker; click it and verify the **global** UTF-16 selection/copy/save/reopen bytes. Test the island during a real CJK candidate; a lost preedit, focus jump, or duplicate AX speech fails. |
| Thumb honesty | With width checkpoints absent, inspect behavior on a 50 MiB variable-width line: remote thumb is monotone in source fraction and visible local text is correct, but exact pixel fraction is **not claimed**. If this approximation is unacceptable in user trials, implement lazy width summaries or withhold the thumb for exceptional rows; do not approximate global width inside the hit-test/AX path. |

Implementation order is deliberately narrow: (1) horizontal anchor and row-window frame in the OS-neutral viewport, with pure properties; (2) one shared row-origin transform through paint/hit-test/selection/diagnostics/island placement; (3) wheel and keyboard reveal on both shells; (4) canvas-owned scrollbars/edge autopan; (5) target-OS/AX/IME acceptance. The existing non-experimental editor remains the rollback path throughout. No new cache or IR belongs in `Mote.Engine` for this view concern.
