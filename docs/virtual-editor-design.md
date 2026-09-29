# Continuous virtual editor view: design and acceptance contract

Status: **partially implemented behind `--canvas-experimental`; not the default editor**, 2026-09-30. Scope is the strict one-on-disk-binary `Mote.Native` Win32/AppKit product. This document specifies the view/input migration; [architecture.md](architecture.md) owns the wider engine/policy boundary, [incremental-plan.md](incremental-plan.md) owns semantic sessions, and [native-input-accessibility.md](native-input-accessibility.md) owns the current input/AX audit. Published-binary geometry and selected workflows have passed on all four target RIDs, but neither the opt-in canvas nor its bottom input ribbon has completed external IME, reader, and latency acceptance.

## Decision and the user-visible contract

Replace page navigation with **one continuous logical editor** backed directly by `Document.Snapshot`. A small file and a 100 MiB file use the same global source coordinates, selection, scroll, search, undo, and save behavior. Size changes only how much view work is cached. The view never owns a second full mutable document. OS-native text controls remain a **bounded input-composition host** around the active caret until a narrower custom text-input implementation is justified by evidence. A viewport canvas draws and hit-tests the rest. This is an architecture target, not an instruction to remove the current working native controls before their replacements pass the gates below.

The **default** implementation remains a useful bridge, not this contract: `TextSnapshot` is a persistent UTF-16 rope with CR/LF/CRLF indexing; `NativeNavigationModel` owns global anchor/active offsets and rope-wide search, while RichEdit/NSTextView contains a 64 Ki-unit source page plus 8 Ki units of edit slack. The opt-in path now has bounded continuous source slices, OS-painted rows, source-backed horizontal anchors, a visible bottom native input ribbon, and an experimental whole-document UIA/AX provider. It is not yet an equivalent replacement for the page editor. A synthetic 128 Ki-unit unbroken-line edit measured p95 around 7.19 ms in the bounded Windows path, but says little about continuous scrolling or reader navigation. A previous 100 MiB **full** AvaloniaEdit mirror had ~1.44 GiB RSS and ~1.24 s to first measured visual lines; its bounded projection had ~359 MiB RSS and ~9.6 ms to visual lines. These are development-host measurements, **not** a Native canvas first-paint or p95 result; see [large-file-ui-evaluation.md](large-file-ui-evaluation.md). Do not solve the page defect by reintroducing a full text-control mirror.

### Representative workflows that must become ordinary

| User action | Required state transition, independent of file size |
| --- | --- |
| Wheel/trackpad scrolls from line 65,000 through 65,001 | Viewport anchor moves through adjacent source-backed visual rows; no page load, focus reset, caret teleport, or change to engine version. |
| Shift-select starts on one screen and ends many screens later | The same global `Anchor`/`Active` source offsets extend; paint only visible selection pieces; copy/cut uses the entire interval. |
| User jumps to an offscreen diagnostic or search hit | Navigation changes the global active offset and reveals its visual fragment; result version is checked; caret geometry is accurate once revealed. |
| User edits a 50 MiB CSV record or unbroken line | Shape only a bounded window around the caret; apply one source edit to `Document`; no `GetLine()` or full native control replacement for the entire logical line. |
| User starts Chinese/Japanese/Korean conversion and then saves or opens another file | Marked/preedit text remains OS-owned until commit/cancel; asynchronous document swap, semantic restyle, save, or close cannot silently discard or persist an intermediate candidate. |

## State model and ownership

```text
Document / TextSnapshot (engine, immutable versioned source)
       | source offsets, line starts, bounded slices, versioned edits
       v
EditorViewState (one per window)
  Selection {anchor, active, affinity, desiredX}
  Viewport  {topSourceAnchor, visualSubrow, topPixel, horizontalAnchor}
  LayoutIndex {implicit line-height base + sparse measured corrections}
  TileCache {bounded shaped visible/near-visible fragments}
  OverlayView {versioned, visible semantic spans/diagnostics}
       | draw commands, hit-test/geometry, OS input events
       v
Win32 DirectWrite/Direct2D or AppKit Core Text/Core Graphics canvas
       ^
       | bounded caret-local RichEdit/NSTextView composition host
       +-- provisional input only; final replacement -> Document.Apply(...)
```

`EditorViewState` is **view state**, not format policy state. It does not retain a whole source string. Format sessions continue to produce immutable, absolute UTF-16 `DocumentAnalysis` snapshots with explicit `Coverage` and `AnalysisCompleteness`; the view accepts only results matching the current engine version. The shell owns OS handles and graphics lifetimes. Closing a document cancels analysis, clears tiles, releases input composition safely, then disposes the document. All mutation is serialized on the UI thread (or enters it through one ordered queue); background shaping/analysis reads immutable snapshots and may publish only if version and typography epoch still match.

The current `TextSnapshot.Length` and `LineCount` are `int`; this design targets **large files within that representation**, not arbitrarily large >2 Gi-unit documents. If that external requirement arises, widen the engine API and native AX/scroll contracts deliberately; do not silently overflow a viewport count.

### Source coordinates and line endings

All persistent offsets and semantic spans are half-open UTF-16 source ranges. `GetLineStartOffset` and `GetLineIndexFromOffset` define logical lines; CR, LF, and CRLF delimit one line while preserving exact source text. A visual wrap is **not** a source newline. The view's `SourcePosition` is `(offset, affinity)` so two visual edges can refer to the same source boundary at a soft wrap or bidirectional run. `Selection` stores source anchor and active boundary, and a `desiredX` in device-independent pixels for repeated vertical motion. Keyboard movement and mouse hit-testing snap to extended grapheme-cluster boundaries, not arbitrary UTF-16 units; the engine still rejects surrogate-pair splits. Unicode [UAX #29](https://www.unicode.org/reports/tr29/) defines the default extended grapheme boundary and treats CRLF as one cluster. At a CRLF the engine may represent the between-CR-and-LF offset, but ordinary caret movement presents the delimiter as one break and does not manufacture a visible mid-delimiter stop. Programmatic/source offsets remain round-trippable rather than being silently rewritten.

The canvas must render directly from source slices and never normalize stored newlines. A bounded OS input host may still require Windows RichEdit's internal single-CR coordinates or macOS native text conventions; isolate that in a **local** bijective-enough projection with the existing `RichEditOffsetMap`/`NativeTextProjection` law: after translating a native edit, `Project(Apply(source, edit)) == editedDisplay`, including a slice that meets a CR+LF seam. The input host's coordinate map is discarded when its window is rebound; it must not leak into global selection, diagnostics, or save.

## Layout without a per-line object graph

### Fast dominant case: no wrap

For an ordinary source editor, no-wrap is the default. Every logical line contributes exactly one visual row at the base line height (unless explicit, future inline blocks are enabled). The rope already gives line count and arbitrary line start; the view does not allocate one object per line. `y -> line` is arithmetic plus sparse exceptions, and `line -> y` is arithmetic plus their prefix sum. Theme color changes repaint without rebuilding text geometry; font, DPI, tab width, and wrap changes increment a typography epoch and invalidate affected layout caches. Use `double` or checked 64-bit fixed-point for accumulated document height; do not pass whole-document pixel heights blindly through a 32-bit native scrollbar.

### Variable-height mode: wraps, fonts, and possible inline blocks

Maintain a sparse ordered **height-delta tree** keyed by logical line or bounded source fragment, with subtree sums. Its implicit baseline is `LineCount * baseHeight`; only measured lines/fragments store `actualHeight - baseline`. A viewport anchors to a **source boundary plus intra-fragment pixel offset**, not to an absolute global Y. When lazy measurement changes heights above the viewport, compensate the scroll offset so the anchored glyph stays put. A user scrolls in visual pixels/rows; converting approximate global scrollbar position to a source line may refine a bounded neighborhood and then redraw. The visible rows and caret must be exact after refinement even if the thumb's far-off position is estimated.

This deliberately separates two promises: **exact local layout and continuous navigation** are required; an exact scrollbar fraction for every line in a newly opened, wrapped 100 MiB file would require measuring much of that file and is not free. For wrap mode, use coarse range estimates (line-length samples or background measurements) and converge without visual jumps. The first implementation should keep no-wrap exact and wrap estimates explicit; a probe must determine whether users perceive thumb drift as unacceptable. Do not instantiate millions of `LineLayout` or height-tree nodes just to obtain an exact thumb. Zed's production [display-coordinate model](https://zed.dev/blog/zed-decoded-text-coordinate-systems) and [sum-tree account](https://zed.dev/blog/zed-decoded-rope-sumtree) corroborate separating source positions, display transforms, and aggregates; they are design precedent, not proof that this particular sparse estimator is adequate. VS Code's [piece-tree postmortem](https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation) documents the memory cost of a per-line model in multi-million-line files.

### Bounded shaping, including pathological long lines

`TextSnapshot.GetLine()` is forbidden on the viewport hot path: one logical line may be tens of MiB. Request bounded source slices around visible fragments (initial experiment: 4-16 Ki UTF-16 units, with measured context overlap), shape them with Windows [DirectWrite `IDWriteTextLayout`](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/nn-dwrite-idwritetextlayout) or macOS [Core Text `CTLine`](https://developer.apple.com/documentation/coretext/ctline). Both expose hit-testing and caret offsets. OS APIs are used from the strict single executable; no font-shaping library is shipped. Word-wrap break opportunities should follow platform shaping plus [Unicode UAX #14](https://www.unicode.org/reports/tr14/), preserving clusters even in emergency breaks. A `VisualFragment` records `(sourceStart, sourceEnd, wrapOrdinal, baseline, height, shapedRuns, hitTestMap)`; it never claims an invented newline. Tabs and bidi require source-to-x and x-to-source mappings with affinity.

**Nontrivial limit:** shaping is contextual. Blindly cutting a long Arabic, Indic, Thai, emoji-ZWJ, or bidi line every 4 Ki units can change glyphs, wrap, and caret geometry at the seam. Chunk boundaries must be chosen at validated grapheme/shape-safe boundaries with context overlap; compare against unsliced native shaping for moderate lines. If no bounded overlap gives parity for a script, the view must enlarge a local run or take a measured slow path, not silently corrupt editing coordinates. For a 50 MiB no-wrap line, jumping to an arbitrary far UTF-16 offset also cannot yield an exact global pixel X without prefix-width information. Maintain lazy width summaries at bounded checkpoints; immediate local caret geometry is exact, while far horizontal-thumb metrics may be estimated until background checkpoint construction catches up. A monospace ASCII fast path is useful but must not become the correctness assumption for Unicode.

Soft-wrapping a single 50 MiB logical line has a second dependency: the exact wrap phase at a far offset depends on earlier break choices. Rather than scanning 50 MiB synchronously on a jump, divide such an exceptional line into bounded **emergency display segments** at validated cluster boundaries and reset the wrap phase at those segment boundaries. This adds occasional visual-only breaks (never source newlines), so it is a disclosed typography trade-off, not an exact equivalent of unbounded paragraph wrapping. Test the first/last rows of adjacent segments and keep the ordinary-line path unchanged. If users reject those extra breaks, the alternative is background prefix-break indexing with a temporarily approximate far jump; that option needs its own latency/scroll-stability evidence before adoption.

The cache key is `(snapshot identity/version, source interval, typography epoch, wrap width, platform)`. Reuse immutable unaffected entries after edit only if their source interval and shaping context are unchanged; otherwise discard. Initially prefer conservative invalidation of edited line and adjacent context; profile before a more elaborate dependency graph. Use LRU/memory-pressure eviction and a hard budget (working hypothesis: **<=64 MiB** for all view glyph/layout/overlay caches, excluding engine and OS font caches). Heavy shaping runs off the UI thread only where platform object threading permits; COM/Core Foundation ownership must be explicit. A bounded synchronous fallback must always keep the caret visible if background work is stale.

## Painting, input, and accessibility are three separate contracts

### Paint and semantic overlays

Build visible draw commands from current-version fragments: background, selection, text glyphs, semantic foreground/weight, diagnostics underlines, caret, and optional whitespace markers. Clip every semantic `TextSpan` to fragment source intervals; a partial/provisional analysis never implies the rest of the file is valid. Avoid one native-control mutation per token: the existing Windows RichEdit experiment found expensive per-span styling and a bounded one-pass replacement still can exceed 50 ms on long/Unicode samples; see [native-richtext-performance.md](native-richtext-performance.md). The canvas repaints overlays without replacing the input host's text. Paint callbacks must not call `GetText()` for the whole snapshot or synchronously wait for analysis.

Use OS graphics directly: DirectWrite/Direct2D draw/hit-test on Windows, Core Text/Core Graphics on macOS. A `PaintSubmitted` trace after the OS paint callback or `EndDraw` is **not** proof that a compositor presented pixels. Keep telemetry stages separate: input received, engine commit, layout ready, draw submitted, and observed present when the platform/test rig can measure it. This distinction prevents a fast model callback from being mislabeled as smooth visual editing.

On Windows, DirectWrite/Direct2D and a custom UIA provider cross COM interfaces. Native AOT **does not support .NET's built-in COM interop**; use explicit ABI-safe vtable bindings or source-generated `ComWrappers`, and validate calling convention, lifetime, HRESULT handling, and struct layout on **both x64 and ARM64** before selecting a wrapper. This is a product feasibility gate, not incidental glue ([Native AOT warning IL3052](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/il3052), [source-generated COM interop](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/comwrappers-source-generation)). The shell paints inside `WM_PAINT`/Direct2D on Windows and an AppKit drawing callback on macOS; both must restore resources after device/scale changes and avoid text shaping during the paint callback when a cache miss would exceed the frame budget.

### Input composition host

Keep RichEdit/NSTextView as a **small native input island**, never a second full document. The current opt-in experiment keeps it focused and visible in a [reserved bottom ribbon](input-host-compositing.md), while the canvas alone paints every source row. This avoids demonstrably divergent native/canvas glyph shaping and the macOS focus loss caused by hiding the first responder; it is not a final decision that candidates should appear below the document. On `Idle -> Composing`, freeze any rebase, style replacement, document swap, and typography change that would destroy OS composition. On OS-confirmed commit, compute one localized edit against the host's pre-composition source window, apply it once to `Document`, transform global selection/viewport anchors, and rebind the host. On cancel, discard the provisional state with **no** engine version change. A Save/Open/Close command synchronously settles or vetoes composition before I/O or document replacement. Local real Microsoft Pinyin tests passed selected Windows x64 cases, while cold first-key loss and macOS real-IME behavior remain open; do not generalize from in-process input probes.

The island is a high-risk recommendation, not a solved integration: font fallback, line wrapping, focus, candidate placement, and selection rendering may disagree with the canvas. Apple's [`NSTextInputClient.firstRect`](https://developer.apple.com/documentation/appkit/nstextinputclient/firstrect%28forcharacterrange%3Aactualrange%3A%29) shows that candidate geometry is an explicit contract. Microsoft's [Text Services Framework text-store](https://learn.microsoft.com/en-us/windows/win32/tsf/text-stores) and [composition](https://learn.microsoft.com/en-us/windows/win32/tsf/compositions) documentation indicate the complexity of replacing the OS host. If island parity fails, retain the current control for small files while evaluating a full custom TSF/NSTextInputClient implementation or another OS-native adapter; do not ship an invisible IME regression for architectural purity.

### Whole-document accessibility

A page-local native control cannot stand in for a global text provider. Implement one engine-backed editor accessibility element whose document range covers `0..snapshot.Length`, with global selection, visible ranges, line/word navigation, offscreen text requests, and `ScrollIntoView` against the same viewport model. Windows needs UI Automation `ITextProvider`/`ITextRangeProvider` semantics; Microsoft's [provider guidance](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingtextandtextrange) explicitly includes document/visible ranges and point hit-testing. macOS should implement AppKit's [text-specific accessibility methods](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol), including selected/visible range, string-for-range, frame-for-range, and line/range mappings. Bound the *work per request* and avoid materializing the whole file merely to expose an AX value. The active input island must not appear as a second competing editor to screen readers; test actual UIA/AX trees and spoken output, not just interface presence. Some assistive clients may request enormous ranges; measure and define a responsive chunking or platform-compliant failure behavior before claiming support.

Clipboard follows the same global interval. Small selections can use existing native clipboard strings. Large selections need a separate experiment: Windows [delayed rendering](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-operations) and macOS [`NSPasteboardItemDataProvider`](https://developer.apple.com/documentation/appkit/nspasteboarditemdataprovider) postpone allocation, but a consumer may still require one contiguous payload. Never silently truncate; show a recoverable error if allocation is impossible. Capture an immutable snapshot for lazy clipboard data and release it when the OS relinquishes ownership.

## Minimal interfaces to keep the architecture enforceable

These are **target contracts**, not current C# signatures. Avoid teaching `Mote.Engine` about UI rendering or `Mote.Formats` about OS handles.

```csharp
/// Global source selection and exact local geometry for one immutable snapshot.
internal readonly record struct ViewAnchor(int SourceOffset, int WrapOrdinal, double IntraRowY);

/// Platform-neutral measured fragment; Native owns only bounded OS glyph handles.
internal readonly record struct FragmentKey(long Version, int Start, int Length,
    long TypographyEpoch, int WidthBucket);

/// Resolves source/visual position only for the requested viewport neighborhood.
internal interface IViewportLayout
{
    VisibleLayout Resolve(TextSnapshot snapshot, ViewAnchor anchor, ViewportSize size);
    SourceHit HitTest(VisibleLayout layout, double x, double y);
    CaretRect GetCaretRect(VisibleLayout layout, int sourceOffset, CaretAffinity affinity);
    void ApplyEdit(TextSnapshot before, TextSnapshot after, TextChange change);
}

/// Carries the bound snapshot identity, so a delayed commit cannot target the wrong file state.
internal readonly record struct CommittedInput(long BaseVersion, TextChange Change);

/// OS adapter owns focus/marked text; it emits committed source edits, never full pages.
internal interface IInputIsland
{
    bool IsComposing { get; }
    void Bind(TextSnapshot snapshot, SourceHit caret, CaretRect screenRect);
    bool TrySettleComposition();
    event Action<CommittedInput>? Committed;
}
```

`Resolve` must not allocate a per-document line array or call `GetText()` without a range. `SourceHit` carries offset plus visual affinity; `CaretRect` is in a documented coordinate space and transformed to screen coordinates only by the shell. `Committed` is allowed exactly once per OS commit and references the bound snapshot version; a stale commit is rejected/reconciled, not replayed at a guessed offset. The UI thread owns view state; a worker may return a version-tagged immutable `VisibleLayout` but never OS handles with invalid thread affinity.

## Migration and rollback without breaking small-file users

| Slice | Evidence now / shortest remaining acceptance | Coexistence and rollback |
| --- | --- | --- |
| 1. Source-backed viewport and read-only paint | Implemented: bounded slices, global selection/horizontal anchors, four-RID one-binary AOT and read-only paint probes. CI [36594818528](https://github.com/kleedaisuki/mote/actions/runs/36594818528) also passed **in-process** AppKit short/50 MiB pan, hit/copy, minimum/zero-body/restore on both Mac RIDs; this is not an external trackpad or editor workflow. | `--canvas-experimental` remains explicit; default native page editing remains unchanged. |
| 2. External source edit before more canvas machinery | Windows x64 external 50 MiB horizontal copy, one-character Save/reopen and selected real Pinyin cases passed. Mac ARM external 1/100 MiB many-line and, in [CI 36598787518](https://github.com/kleedaisuki/mote/actions/runs/36598787518), **50 MiB single-line** one-key Save/exact-byte/fresh-reopen passed after PID/window/source-AX focus and reversible selection challenges. The prior [365962 timeout](https://github.com/kleedaisuki/mote/actions/runs/36596274416) was traced to an input binding that exactly filled its 16 Ki native hard cap; reserving an 8 Ki source binding budget left ordinary edit/IME headroom while keeping the hard cap and fail-closed rejection. The successful run showed one `X` producing source length `52,428,800 → 52,428,801`, selection `1/0`, and native allowed-change/commit stages. **Next falsifier:** repeat external long-line pointer/pan/edit/Save on Mac x64 and Windows ARM64, and separate first-pixel/p95 from these automation-inclusive one-run timings. | A failed key route or byte mismatch blocks default promotion; do not silently switch modes during marked text or a dirty edit. Keep the source snapshot, diagnose the platform adapter, and leave the page mode as the user-selectable rollback. |
| 3. Input, semantics, and accessibility parity | Exercise global select/copy/cut/paste/undo/redo, find/go-to, format diagnostics and preview across the former page seam and the 50 MiB line; verify visible semantic coverage on separated long slices. Run real CJK candidate commit/cancel plus Save/Open/Close/theme/resize while composing on **both** OS families. Require external UIA/AX source-range tasks, reader speech and one logical document without duplicate ribbon text; distinguish Apple visible *logical line* range from physically painted row windows. | The canvas remains opt-in if native candidate focus, AX reading, or an ordinary command differs from default mode, even if screenshots and in-process probes pass. No hidden full native mirror may be introduced as a repair. |
| 4. Performance and strict delivery | On identical OS/RID/hardware/fixtures compare default and opt-in cold open, input-to-paint p50/p95, 60-second wheel/pan/edit, allocations and RSS for 1/10/100 MiB many-line and 16/50 MiB single-line files while background Full semantics runs. Inventory must remain exactly one executable on win-x64/arm64 and osx-x64/arm64; inspect actual GUI workflow, not only `--check-runtime`. Set frame-budget thresholds from measured 60/120 Hz target devices rather than one synthetic callback. | Reject a demonstrated edit regression or unbounded long-line allocation without a justified user benefit. Diagnostic and crash artifacts stay in repository `.cache/`/`.temp/`; failed target-host evidence is retained. |
| 5. Deliberate default switch | Only after rows 2–4 pass, use the same canvas path for small and large files and remove Prev/Next page UX. Keep a documented, explicit **legacy page-mode opt-out** for at least one compatible release; keep existing file arguments, encoding/newline bytes, Save/Save As, undo, configuration, theme IDs and telemetry semantics. The opt-out mechanism is a proposed migration contract, not an implemented flag today. | Rollback is chosen before opening/binding a document, never by an automatic mid-composition swap. If a post-launch adapter failure occurs, preserve the dirty engine snapshot and offer safe recovery/reopen rather than silently discarding preedit or rewriting the file. |

The release decision is conjunctive: **external byte-exact edit + real input/reader behavior + measured fluency + strict one-binary delivery**. A green build or synthetic view probe cannot substitute for any missing term. Do not add a parallel text engine, full hidden native mirror, or size-threshold dispatch as the final architecture. A temporary page-mode fallback is a migration safety valve, not a second long-term product model. An elaborate display-map framework for folds/inlays is also premature: there are no established user-visible fold/inlay contracts yet. The sparse height index and fragment mapping should admit those later without paying their complexity now.

## Discriminating experiments and product gates

All fixture generators, probes, screenshots, traces, and locally produced reports must live under repository `.temp/` or `.cache/`; durable conclusions belong in `docs/`. Compare against the current native path on the **same target machine**, release Native AOT build, viewport, font, DPI, and fixture. Report median/p95 and maximum, allocations, working set, and OS versions; distinguish cold open, warm scroll, edit callback, draw submission, and observed presentation. Hosted CI can run deterministic model/AOT tests; GUI/IME/AX needs target-OS automation and human acceptance.

| Gate | Cheapest falsifying test | Pass criterion / decision it enables |
| --- | --- | --- |
| Source/view correctness | Fuzz edits around rope chunks, CR/LF/CRLF seams, surrogate pairs, combining marks, emoji ZWJ, bidi runs; compare source bytes before/after save and source↔hit-test round trips. | No lost/duplicated source units; caret stops at valid user-perceived boundaries; internal between-CRLF source offset remains representable. |
| Continuous vertical geometry | Generate 10 million short lines plus variable wrapped paragraphs; wheel, drag thumb, PageDown, and select across former 64 Ki page seams; change width/font mid-scroll. | No page boundary event/focus reset; visible region exact after refinement; anchor glyph does not jump when estimates are replaced. Measure thumb drift separately. |
| Long-line shaping | Compare sliced vs unsliced DWrite/Core Text geometry on moderate Latin, Arabic, Devanagari, Thai, CJK, emoji and mixed-bidi lines; then 50 MiB unbroken line seek/edit/scroll. | All compared glyph/caret locations agree within one device pixel and logical hit-tests agree; 50 MiB path does not shape/materialize the whole line or allocate proportional UI memory. If not, change segment/context algorithm before rollout. |
| IME/focus | Real Pinyin, Japanese conversion/reconversion, Korean Hangul, dead keys on both OSes; save/open/scroll/theme change during active candidate. | Exactly one committed engine edit, zero on cancel; correct candidate rectangle, no lost focus/marked text, no provisional save; verified by reopened bytes and event trace. |
| Accessibility | UIA/VoiceOver query offscreen ranges, read line/word, select across seam, jump to diagnostic, scroll into view; inspect tree and spoken output. | One logical editor, global document range and selection, no duplicated island speech; offscreen text can be reached without loading a fake page. |
| Memory/latency | Fresh-process 1/8/100 MiB many-line and 16/50 MiB one-line, 60-second scroll/edit traces; record engine baseline separately. | View-owned caches stay under the provisional 64 MiB budget and do not grow with total lines/long-line length; no regression versus current native path without a documented benefit. Set final p95 input-to-draw/present thresholds from measured hardware and 60/120 Hz target, not the current 7.19 ms synthetic callback. |
| Distribution | Hosted AOT publish for win-x64/arm64 and osx-x64/arm64; inventory and GUI workflow. | One executable, zero shipped sidecars on each RID, using only OS libraries. Existing CI artifact/launch checks are necessary but do not replace open-edit-save/IME/AX checks. |

The initial bounded read-only viewport experiment has passed four-RID geometry probes, and the separate input ribbon now has byte-exact external large-file edits on Windows x64 and Mac ARM. The **next** cheapest falsifiers are the remaining platform/architecture external workflows and real IME/reader behavior; another synthetic canvas benchmark will not decide the default switch. Academic dynamic-string results such as [Lipták, Masillo and Navarro, ESA 2024](https://drops.dagstuhl.de/storage/00lipics/lipics-vol308-esa2024/LIPIcs.ESA.2024.86/LIPIcs.ESA.2024.86.pdf) inform source-update/index choices but **do not** establish text shaping, composition, or accessibility performance. The contribution sought here is a verified separation of canonical source, sparse display geometry, native composition, and semantic overlays—not another fast rope benchmark.
