# Ordinary editing: one source surface, not a prettier input ribbon

> Current delivery decision, 2026-10-02: the user permits multi-file application
> packages while retaining Native AOT. Original one-executable constraints and
> provisional numerical candidate targets below are historical; use the
> [release acceptance contract](../product/release-acceptance-contract.md) for
> current task and qualification authority. Existing CLI routes, source/history,
> pending-input safety and `~/.mote` contracts remain protected.


Date: 2026-10-01. Status: **architecture recommendation and discriminating
implementation contract; not a default change or native acceptance result**.
Source inspection checkpoint: `6736b0d26031454d25eced015fca401ee3f59bd1`.
Scope: strict-single-binary C# Native AOT Win32/AppKit mote, with the original
six-format semantic goal intact. No production files, native experiments,
global input settings or existing test runs were changed for this assessment.

## 1. Decision

**Investigate a genuinely visible native source text view as the sole source
painter and input destination before adding more Canvas/native alignment machinery.**
Use one explicitly selected, window-lifetime diagnostic adapter, initially with
the complete source resident in RichEdit/NSTextView. Measure the ordinary
sub-1-MB/few-MiB journey and exact engine integration. This is an experiment
boundary, not permission to ship a full-file mirror for every workload, switch
presentation automatically by file size, or silently restore LegacyPage.

The desired product behavior is not conditional: click/select the document,
type at that location, see one caret, compose there, Undo, Save and reopen. The
native replica supplies OS text services; **the engine remains the sole committed
text, byte-encoding, I/O, history and semantic authority**. A native control must
not independently save, format the source, own a competing undo history or
interpret a native paragraph offset as a source offset.

The first question is whether this simpler representation works well for the
actual dominant task. If it does, it supplies a useful reference implementation
and a concrete candidate for the ordinary source surface. If it does not,
retain the failure and its cost attribution before investing in a native
active-fragment or custom text-input surface. Do not disguise that alternative
as a two-line cosmetic patch.

**Keep ordinary launch, Continuous, LegacyPage and historical Canvas diagnostic
contracts unchanged during this investigation.** Promotion requires a separate
reviewed integration decision covering every established external contract and
the existing capacity regressions. Few-MiB success alone cannot certify the
complete product or 100-MiB behavior.

## 2. Evidence reused and current contradictions

Relevant internal knowledge was selected by filename and reused:

- [Ordinary visual assessment](../product/native-canvas-visual-acceptance.md)
  records duplicate visible source/input text and the editing-destination issue.
- [Native typography units](native-typography-units.md) and
  [Windows typography validation](../validation/windows-canvas-typography.md)
  locate the already-fixed 13-to-17-unit conversion. Do not repeat that fix.
- [Input-host compositing](../input-host-compositing.md) records **63.3 px**
  neighboring-boundary disagreement between DirectWrite and RichEdit on mixed
  script, despite matching one caret. Its opt-in/default statements are dated
  design context, not current startup behavior.
- [Input-island contract](../input-island-design.md),
  [continuous editor](../virtual-editor-design.md), and
  [native rich-text performance](../native-richtext-performance.md) establish
  source/version, composition, global selection and native style costs.
- [Demand assessment](../product/large-file-demand-and-experience.md) records
  Klee's self-report: files rarely reach 1 MB; a completed novel's TXT is
  3.54 MiB. This is direct evidence from one intended user, not prevalence data.
- [Mac ribbon context correction](../validation/mac-canvas-ribbon-context.md)
  corrects stale labels without resolving the architectural editing locus.

Current code is more advanced than the early island proposals:

| Current owner | Actual inspected behavior | Consequence |
| --- | --- | --- |
| `EditorPresentationProfile.cs` | Ordinary startup chooses `Continuous`; `--legacy-page` and historical diagnostic routes remain explicit. Profile lifetime is the window, not a document-size decision. | Do not change default parser behavior as an incidental UI correction. |
| `WindowsRichEditIsland` / `MacTextInputIsland` | Canvas paints source rows; visible bounded native input receives committed/preedit text separately. | A label/font change can clarify the compromise, not create a single editing locus. |
| `NativeEditorController.Edited` | Legacy native edits read a complete bounded display page and `NativeTextProjection.Difference` maps its change into source; page-size/rebinding logic remains involved. | Increasing `PageSize` is not a designed full-native adapter and must not be presented as continuous-product completion. |
| `WindowsEditorShell.OnTextChanged` | Reads the complete installed native page, rebuilds `RichEditOffsetMap`, then raises `TextChanged`; rejects uncertified/read-only import. | Existing exactness techniques are reusable; their cost grows if the page becomes a full document. |
| `MacEditorShell.TextDidChange` | Reads the native string and withholds engine commits while marked text remains. | Reuse settlement behavior, but do not assume full-file callbacks are incremental. |
| `NativeDocumentView` / `INativeEditorShell` | Contracts explicitly describe a bounded page and page-local selection. | Reusing these types silently with a full file obscures semantics. A new diagnostic binding should state its extent explicitly. |

The prior 100-MiB full-control measurements concern an **Avalonia** surface,
not Win32 RichEdit or AppKit. They warn about duplication/layout, but cannot
prove a 3.54-MiB native text view is too slow. Conversely, a RichEdit 128-KiB
style pass cannot prove that a few-MiB dense structured document is fast.

## 3. Alternatives and recommendation

| Design | Strength | Dominant risk | Judgment |
| --- | --- | --- | --- |
| **A. Native source control, sole visible painter** | Mature shaping, hit testing, caret, visible composition and native selection share one text engine. Eliminates Canvas/native glyph-parity seam. No additional shipped library. | Full editable replica/readback/style costs; newline normalization; all edit ingress and engine history integration; source-backed accessibility identity. | **Recommended next discriminating implementation.** Measure full native ordinary files before designing virtualization or assuming a failure. |
| **A2. One native painter over a continuously virtualized source window** | Bounded native work while retaining global engine coordinates and one visible source surface; no user-facing pages. | Native controls do not automatically become external rope-backed stores. Rebinding on scroll can invalidate selection/candidate context; a distant global caret must not become a viewport-local typing target. | Full-product extension worth investigating if A's measured costs require it, **not** a trivial enlargement of LegacyPage or an already-qualified OS facility. |
| **B. Canvas plus native caret/preedit overlay** | Preserves bounded source rendering and engine-oriented semantic overlays. | Two shapers do not agree around the caret; clipping can corrupt hit/AX/candidate geometry; placement/ownership transitions during composition. | Not approved as the next product implementation. A later separately qualified single-painter active-fragment design is possible. |
| **C. Canvas plus current reserved input ribbon** | Physically visible native host; no source-row paint collision; source commits remain bounded. | Two locations and eye travel, especially with offscreen caret; reader/candidate usability unproven. | Keep as an explicitly described existing fallback/diagnostic baseline, not an accepted final editing experience. |

For B, merely skipping a Canvas row is insufficient. The native host must own
the **entire occupied fragment's** layout, styling, selection, diagnostics and
accessibility geometry, and Canvas must not paint that fragment. Whole visible
interval coverage, bidi/context at its boundaries and viewport transition have
to be proved. Equal font sizes or one coincident caret do not remove the recorded
63.3-px counterexample. Do not use a transparent/offscreen/1-pixel host, hide a
focused first responder, or move/rebind the host mid-composition to pass a
screenshot. That introduces more mechanisms before proving a need for them.

### Full-product path: continuous native virtualization, not manual pages

A's complete resident replica is an inexpensive representation to test, not the
full architecture's permanent memory commitment. The target remains continuous
global navigation/editing across all supported file sizes. A2 would keep one
native surface/layout authority, with an immutable window binding containing
source interval, native text map, logical viewport anchor and global selection.
Its scroll rails represent the engine document; rewindowing is an internal
guard-band operation, never Next Page, a selection jump, or a change to source.
All visible rows must be painted and hit-tested by the same native layout.

This requires actual capability evidence, not a design pattern name:

1. AppKit custom `NSTextStorage`/TextKit content backing can potentially delegate
   character access to the engine snapshot while native layout is viewport-based.
   Mutation must still be admitted/versioned through the engine. Evaluate the
   existing deployment floor and Objective-C AOT lifetime/exception contracts;
   do not assume a custom content manager inherits NSTextView input/AX services.
2. Windows RichEdit's documented import/readback controls are not an external
   rope-backed storage interface. A windowed binding therefore has to preserve
   native scroll geometry, global selections and composition itself. Test that
   mechanism separately before claiming native virtualization is ready.
3. During marked text, keep the installed native window, typography, selection
   and input context stable. A viewport transition cannot rebind that window.
   Scrolling away from an unchanged distant caret must not make subsequent text
   land at the visible window's local selection. Before admitting input, reveal
   and bind the **existing global caret** without changing it; if OS event order
   cannot certify this before composition starts, that A2 construction fails.
4. Long logical lines need bounded context without cutting a CRLF, surrogate,
   grapheme or required shaping context. Paragraph-complete layout may be
   unbounded; grapheme-safe seams alone do not prove bidi/shaping parity. Source
   points, native glyph bounds, semantic decorations and AX visible ranges must
   all use the same installed window identity.

The cheap A experiment tells us whether these extra responsibilities are
necessary for ordinary experience. It does **not** remove them from the complete
product if required to preserve existing capacity. Do not add a hidden size
threshold that turns a familiar native editor into a different ribbon/page
interaction. Any chosen representation is a coherent window-lifetime profile,
with the same global user contracts; implementation refinements inside it must
not change where editing occurs. If A fails at ordinary sizes, or A2 cannot
meet the selection/composition invariants, the next boundary is an explicitly
qualified native single-painter fragment/custom text-service design, not false
default promotion.

## 4. Native-source binding: minimal state and invariants

Reuse `NativeDocumentStamp`, existing source projection/offset semantics and
engine `TextChange`; do not create a parallel editor engine or generalized
plugin framework. The diagnostic binding needs these explicit facts:

```text
NativeSourceBinding
  document stamp (generation, version)
  installation nonce
  immutable canonical snapshot reference
  source extent = [0, snapshot.Length)
  exact native/display-to-source map
  installed native text identity
  global anchor/active selection and direction

Native final edit
  original binding stamp and nonce
  one source TextChange
  resulting source selection
  settlement outcome (changed / unchanged / unavailable)
```

The replica can temporarily contain uncommitted native input, but cannot become
the engine's baseline simply because `EN_CHANGE` or `textDidChange` fired.
One controller admission applies one changed final edit against the exact
original stamp; unchanged final text is a no-op. Repeated notification/echo
cannot apply it twice. A stale generation or failed native readback never
authorizes an edit to a different file.

```text
Uninstalled -> verified import/readback -> Ready(G,V,nonce)
Ready -> native mutation -> reconcile -> admitted engine change -> Ready(G,V+1)
Ready -> marked text -> Composing(frozen G,V,nonce,baseline selection)
Composing -> final native text stable -> Settling -> reconcile -> Ready
Composing -> unchanged/cancel -> Ready (no engine version or history change)
any state -> import/map/readback fault -> retain engine; visibly disable input
```

- UI thread owns native control, binding and composition state. Engine snapshots
  and semantic results are immutable/versioned. Worker results carry document,
  policy and theme identity; stale results are discarded.
- Save/New/Open/Close/Undo/Format keep existing settlement/veto semantics. There
  is no native input-state handoff between profiles or a size-based control
  replacement during a window lifetime.
- Accepted native input must not trigger a whole-control text replacement when
  its installed text already matches the accepted engine projection. Engine-
  originated Undo/Redo/Format use an explicit guarded publication; verify text,
  selection and scroll afterward. All user-reachable native Undo ingress must
  route to engine history, including menus/context commands and accessibility.
- CR/LF/CRLF and UTF-16 source boundaries keep their existing meaning. RichEdit
  paragraph offsets and CRLF readback offsets differ; `RichEditOffsetMap` plus
  `NativeTextProjection` remain distinct maps. Never change source newlines just
  to match native storage. NUL/other uncertifiable native input remains an
  explicit safe display/read-only boundary; exact engine Save is preserved.
- A semantic style publication changes attributes only, not engine source or
  history. It waits outside marked text/settlement, preserves native selection,
  focus and viewport, and cannot set the full source string per keystroke.

### Windows delta capture is a real unresolved boundary

[`EN_CHANGE`](https://learn.microsoft.com/en-us/windows/win32/controls/en-change)
is an after-screen-update action notification, **not a changed-range payload**.
`EN_SELCHANGE` is selection, not an edit interval.
[`ENPROTECTED`](https://learn.microsoft.com/en-us/windows/win32/api/richedit/ns-richedit-enprotected)
describes attempted modification of protected text and the current selection;
it is not a documented universal committed-text journal. Do not infer all edits
from `WM_CHAR`, caret collapse, a keyboard hook or one paste command. IME,
reconversion, native replace, context commands and assistive technology differ.

For the first correctness reference, read the complete final native text and
derive its exact source edit through the existing projection approach. This is
deliberately **O(n) readback/map/diff**; call it that and measure it. Keep at most
one installed baseline and one final candidate, not a queue of full-file copies.
It is not the final large-file strategy and no sublinear claim follows. A
localized ingress optimization may be added only after its unchanged-prefix/
suffix and actual replaced-range proof is available; otherwise retain exact
readback or veto with a clear notice. Never silently accept a guessed delta.

AppKit's `NSTextStorage` supplies edited range/change-in-length notifications
([processing contract](https://developer.apple.com/documentation/appkit/nstextstorage/processediting%28%29)).
That is a better incremental observation boundary, but coalesced character and
attribute edits, marked input and natural bidi selections still need tests.
Do not assume the current Mac shell already uses this contract.

### Native styles: do not repeat the old bottleneck

The retained RichEdit experiment measured ~16 ms ASCII and ~58 ms mixed-script
publication **at 128 KiB / 4,096 spans**, with restoration. Repeated selection/
formatting cost seconds. Initial full-RTF import may be a measured reference,
not a per-key design. Microsoft documents that
[`EM_SETTEXTEX`](https://learn.microsoft.com/en-us/windows/win32/controls/em-settextex)
uses an RTF reader for RTF input and
[`ST_DEFAULT`](https://learn.microsoft.com/en-us/windows/win32/api/richedit/ns-richedit-settextex)
clears undo/formatting and replaces all text. Preserve guards and selection
restoration; a full import is not an attribute-only update.

The probe must independently time native import/layout, exact edit reconciliation
and semantic attribute publication. If dense ordinary files fail, choose a
native range/viewport style mechanism or different source adapter from evidence;
do not remove semantic correctness or widen latency thresholds to make it pass.

## 5. One decisive hosted experiment, not another giant-file benchmark

Build one frozen Native AOT diagnostic source-adapter implementation. Use
ordinary default configuration/themes, plus an isolated dark/light control;
no user input-source changes. Keep its launch selection test-only until profile
integration is approved. Compare against the same frozen current Continuous
binary, not a historical mutable build. Preserve executable SHA, four-RID
single-binary inventory and exact fixture bytes.

| Fixture | Purpose |
| --- | --- |
| Small mixed CJK/emoji/combining/bidi TXT, including CR/LF/CRLF | Detect wrong source/native maps, shaping/caret and selection errors cheaply. Include the prior `iW中🧪éمرحبا\t` counterexample. |
| Deterministic 3.54-MiB novel-shaped TXT with normal paragraphs | Representative size/reading shape from the user report; synthetic, not the user's actual novel and not copyrighted content. |
| 512-KiB structured fixture with dense semantics/diagnostics | Separate native style publication from text layout; retain exact engine analysis outcomes. Small correctness fixtures cover each of all six formats. |
| Existing long-line/capacity regressions | Preserve existing safety evidence; do not commission a new 100-MiB optimization suite for this slice. |

Stage 1 is read-only import/layout/selection/scroll plus one **controlled**
native edit ingress into the real engine, Undo, Save and fresh-process reopen.
This cheaply rejects the representation before integrating all input ingress.
Stage 2 admits full native input and commands only after the same binding passes
event-order and settlement tests. Controlled `EM_REPLACESEL`/AppKit insertion
does not prove physical-keyboard delivery, real IME or screen-reader usability.

Record through existing bounded local tracing, with source-free attributes:
native install/readback, commit admission, map/diff duration, source draw
submission, semantic publish, selection/scroll restoration, Save completion and
actual numeric exits. Measure working set and managed allocation separately;
draw submission is not compositor-present or physical input-to-glyph latency.
No raw text, candidate, path, handle or thread/PID value in telemetry. Use the
existing owned-process hard-deadline supervisor and preserve timeout artifacts.

**Pass/fail facts, not a green workflow impression:**

1. One visible edit destination and one source-backed accessibility Document;
   no duplicated source/control speech, same established AutomationId and native
   source identity contracts. Native caret/range geometry comes from the native
   source layout, not former Canvas offsets. UIA/AX tree checks are necessary
   but spoken-reader workflows remain a separate gate.
2. Exact engine/source/native selection agreement before/after edit, scroll,
   style, Undo/Redo and Save/reopen. Scroll never edits or moves global selection.
   Reopen compares full expected bytes/encoding, not just a changed substring.
3. Engine remains unchanged throughout marked text; final changed input commits
   once and unchanged final text commits zero times. Synthetic marked callbacks
   and real Pinyin are labeled separately; unavailable human/IME access is unknown,
   not pass. No global keyboard reset, activation rescue or clipboard changes.
4. No unbounded copy queue, startup hidden mirror, repeated whole-RTF per key,
   source-size-induced profile switch or loss of existing recovery behavior.
5. Native endpoint distributions on the same RID/reference machine improve or
   meet the existing candidate experience targets; retain every run, identity
   mismatch and fault. Full reconciliation cost must be visible. Do not derive
   a p99 or physical-present SLA from a few synthetic events.

## 6. Practical ownership and integration order

Assign separate writers; coordinate the binding/selection contract once.

| Owner area | First implementation responsibility |
| --- | --- |
| New internal native-source binding/model + portable tests | Stamp/nonce admission, unchanged/duplicate/stale outcomes, projection exactness, composition freezes, engine history and failure recovery. No refactor of all existing shell interfaces. |
| Windows native-source diagnostic adapter | Sole visible `RICHEDIT50W`; verified full import/readback; exact baseline reconciliation; native layout geometry; no direct reuse of LegacyPage's page rebinding as full-source behavior. |
| Mac native-source diagnostic adapter | Sole visible `NSTextView`; normal AppKit selection/input; explicit actual text-layout backend; edited-range evidence and composition-safe semantics. No new minimum OS silently imposed. |
| Source UIA/AX adapter + integration tests | Preserve existing source identity/global ranges while replacing geometry ownership; prevent duplicate native text providers; retain Grid/Flow/Block focus/navigation contracts. |
| Hosted harness/validation | Same frozen binary/fixture identity, per-stage times, owned-process lifetime, screenshots and native selection/layout facts, exact Save/reopen; no local GUI or repeated old probes. |

Implement the source binding and read-only native adapter first, then the exact
controlled edit reference and all input ingress, then source geometry/semantics
and accessibility integration. Review a candidate default change only after
native correctness, actual input, ordinary-file latency and capacity regressions
are qualified. If a probe fails, fix/reject the representation, not the oracle.
Existing flags, `.mote` conventions, policy APIs, disk bytes and one-executable
delivery do not change. A rollback restores a window's previous presentation
only at a safe lifecycle boundary; never abandon marked text during a live swap.

## 7. Platform and research implications

Apple's production [TextKit 2 introduction](https://developer.apple.com/videos/play/wwdc2021/10061/)
separates content, layout fragments and viewport rendering, emphasizes complex
script correctness, and permits custom content backing. Its
[WWDC26 text experience session](https://developer.apple.com/videos/play/wwdc2026/370/)
explicitly contrasts mature framework text-view services with the cost of custom
views. These are useful directions for eventual engine-backed native layout,
not proof that a new API is available on mote's supported macOS baseline or that
an existing `NSTextView` creation selects the desired backend. Inspect actual
runtime backend/availability before deciding an OS floor or TextKit migration.

[Yedidia and Chong, SLE 2021](https://people.seas.harvard.edu/~chong/abstracts/YedidiaC2021.html)
show that incremental PEG analysis can make common reparsing local and fast.
That frontier complements versioned semantic policies, but does not solve
native replica readback, synchronous style calls, source/candidate geometry or
editing-locus usability. Profile these as separate mechanisms before optimizing
the parser again. The useful unifying boundary is **one canonical source,
one admitted transaction, one layout authority for each visible editing region**.

No additional geometry router, text-service framework, dynamic plugin layer,
full-file specialization, or native text-store COM implementation is justified
by the currently observed ordinary editing-locus problem.

## 8. Product integration decision after the resident-reference experiment

Integration assessment: 2026-10-02, inspected source
`6827cd1a19142c9ad766d296c18d0770e600e79c`. This section advances the next
**product implementation**, not a new disposable-window experiment and not a
claim that ordinary launch is already ready to change. The reference in section
5 actually reopens a **fresh engine Document in the same process**; it does not
certify fresh-process reopen. Preserve that distinction in acceptance reports.

### 8.1 What the completed hosted run proves, and rejects

CI [36899695843](https://github.com/kleedaisuki/mote/actions/runs/36899695843)
executed the exact frozen source above. The independently inspected artifacts
are under `.cache/ci-36899695843-codec-source/`; `audit-result.json` indexes
the four `native-codec-source-evidence-<rid>` reports. Each source probe exited
normally with numeric zero, 301 report rows, 120 completed paired timed phases,
three exact saved-byte comparisons and fresh-Document reopens, and a 121-row
normally ended trace. Those are controlled-edit/reference correctness facts,
not physical-input, IME, screen-reader or product-latency qualification.

| Observed phase, milliseconds | win-x64 | win-arm64 | osx-x64 | osx-arm64 |
| --- | ---: | ---: | ---: | ---: |
| Dense JSON initial semantic publication | 7587.9403 | 5223.9037 | 480.7010 | 142.4718 |
| Dense JSON post-edit semantic publication | 10256.3977 | 7796.1089 | 228.6858 | 80.9661 |
| Novel initial native import | 4898.8882 | 3875.6692 | 5.9238 | 1.9959 |
| Novel native import after Undo | 4667.2545 | 3818.7237 | 3.8722 | 2.1165 |
| Novel native import after Redo | 4621.8596 | 3810.1724 | 3.9611 | 1.8053 |

These are single retained phase observations on hosted machines, not comparable
hardware-normalized platform rankings, distributions, or tail-latency SLAs.
Detached Windows TOM ranges remove the previous selection-loop cost but leave
seconds of synchronous dense attribute publication. Full import on each engine
history transition repeats seconds of ordinary novel work on Windows. Therefore:

1. **Integrate one real native source control into the existing product shell.**
   Do not keep issuing independent capability probes instead of wiring commands,
   composition, selection, preview navigation, and recovery to the actual engine.
2. **Do not transplant the reference's full style pass or repeated full import
   into the edit loop.** Publish visible attributes only and synchronize engine
   mutations as ranges. The experiment's complete exact-readback path remains
   the correctness fallback, with its O(n) cost observable.
3. **Keep default promotion separate from implementation.** Windows resident
   import already misses an instant-open experience on a representative file.
   Range updates solve repeated history imports, not that first-import debt.
   The remaining initial-import/layout mechanism needs attributed measurements
   and a real improvement before promoting this representation as the default.

### 8.2 One closed candidate profile, not a second editor application

Add `EditorPresentationProfile.NativeSource` as an explicitly selected,
window-lifetime product candidate. A proposed `--native-source` launch selects
it for real interactive journeys through the existing command/controller path.
This is an additive route pending root approval; do not silently reinterpret
the existing parser or choose it by source size. Retain ordinary `Continuous`,
`--legacy-page`, historical Canvas/UIA diagnostics and standalone probes exactly
until the explicit default-promotion review. Unknown/mixed flags continue to
receive the existing one-file/error handling; no workspace, project or server.

`NativeShellFactory` passes the closed profile to `WindowsEditorShell` and
`MacEditorShell`. In the candidate, the existing full-body RichEdit/NSTextView
is the only source painter and input receiver. Do not construct a Canvas/input
ribbon behind it or create another top-level probe window. Existing native
menus, status, file choosers, encoding chooser, Flow/Block/Grid panes and focus
ownership remain shell responsibilities. Native source scroll and selection use
native layout; `CanvasFrame` cannot remain their geometry authority.

Keep `NativeDocumentView` explicitly bounded. Its old events and methods retain
LegacyPage behavior. Add one optional internal `INativeSourceShell` capability
to `NativeShell.cs` (or a dedicated `NativeSourceContracts.cs`), so existing fake
shells need not pretend to implement full source. Reject a profile/capability
mismatch in the controller constructor, just as the Canvas mismatch is rejected
today. Candidate native callbacks must not also emit bounded `TextChanged` or
page-local `SelectionChanged` events.

The minimum contract consists of these operations and facts, not a generalized
view framework:

| Contract seam | Semantics and owner |
| --- | --- |
| `NativeSourceInstallation` | Controller-owned immutable snapshot, generation/version, fresh installation nonce, whole-source projection, and global selection. Extent is explicitly `[0, snapshot.Length)`. |
| `InstallSource` | UI-thread full initial import for New/Open/recovery; returns exact readback/certificate or unavailable. A partial import is never editable. |
| `NativeSourceCandidate` event | Original stamp/nonce, final native display string and observed native selection. No engine mutation or file I/O inside the shell. Native callbacks emit only after composition is settled. |
| `AcknowledgeSource` | Controller advances the certified binding after admission without replacing already matching native text. Duplicate/unchanged notification does not reinstall source. |
| `ApplySourceChange` | Guarded engine-originated replacement described by the exact Before/After snapshots and `TextChangeRange`; suppresses edit echoes and certifies the resulting projection. |
| Source selection/viewport events | Identity-bound actual native observations mapped to global source; viewport sequence is separate from document mutation version. Scroll never rewrites source or moves selection. |
| `SetSourceSelection` / `RevealSource` | Explicit engine-command/navigation publication using that exact binding, not page rebinding or Canvas offsets. |
| `SetSourceSemantics` | Version/policy/theme/viewport-bound absolute source tokens and diagnostics, with completeness and coverage retained; attribute-only visible publication. |
| `SetSourceChrome` | Title/modified/status changes independently of source string installation. |

The UI controller owns a production `NativeSourceBinding`: one immutable engine
snapshot, one complete projection/certificate, one installation nonce, and
canonical selection. Reuse the scalar-safe `NativeTextProjection.Difference`
and the reference's exact import/map/admission tests. Do not just rename the
diagnostic host into a product interface: it has no command/IME callbacks and
its sorted-range contract explicitly does not certify selection direction.
Nor should its `Reconcile(Document, ...)` bypass the controller's `ApplyTraced`
and ordinary admission/telemetry path. Factor a pure candidate preparation
operation and let the controller own the one `Document.Apply`.

### 8.3 Edits, composition, history and persistence

Implement this controller area in `NativeEditorController.Source.cs`, with small
dispatches in the existing constructor, `ShowDocument`, `DocumentChanged`,
selection/reveal methods and semantic publication. Do not independently rewrite
Open, Save, format workers or policy/session machinery.

1. **Native edit:** capture final readback against the installed binding. Validate
   identity, exact projection and native boundaries; derive one source change;
   call `ApplyTraced` once. Mark its synchronous `ChangedRange` notification as
   already reflected natively, so it records policy invalidation/navigation
   without applying the replacement a second time. Advance/acknowledge the
   binding, preserve the native caret and viewport, invalidate find as today,
   then schedule semantic work. An unchanged final string is no history entry.
2. **Engine edit:** `DocumentChangedRangeEventArgs` already contains Before,
   After and replacement extents, including Undo/Redo. Read inserted characters
   only from `After.GetText(Start, InsertLength)`. Map replacement boundaries
   through the installed projection and RichEdit paragraph map. Newline context
   at replacement seams may require a minimally expanded boundary-safe native
   replacement; certifying full readback is still allowed. Never infer display
   length from source `InsertLength` when CRLF projection changes it.
3. **Echo/reentrancy:** an explicit controller native-origin admission scope and
   shell programmatic-mutation guard distinguish the two paths. Original stamp
   plus nonce is checked again after native callbacks. Native failure after an
   engine commit disables that source input and retains the committed document;
   it does not roll history backward or silently discard the native edit.
4. **Marked text:** UI-thread state is Ready, Composing, Settling, or Unavailable.
   Freeze installed identity/baseline during native composition. No engine Apply,
   attribute publication, typography change, range replacement or document swap
   while marked text remains. Final settlement commits once; cancellation with
   unchanged text commits zero times. Selection callbacks during provisional
   input must not publish a committed-source position from the changed replica.
5. **Commands:** reuse `CommitPendingText` and `CompositionSettled` for Save,
   Save As, New/Open/Close, Undo/Redo, format, and modal navigation. The Save
   request continues through `NativeEditorController.Save.cs`, preserving its
   receipt, busy, picker reentrancy, external-change, encoding and recovery
   behavior. A settlement veto saves nothing; an admitted settled change is in
   the engine before Save takes its snapshot. Settings/theme use existing
   deferred application, not force-commit to make colors update.
6. **History:** native undo stays disabled. Route keyboard, main/context menu,
   NSTextView responder `undo:`/`redo:`, RichEdit `EM_UNDO`/`EM_REDO` and supported
   accessibility actions to engine history, rather than intercepting only the
   existing toolbar shortcuts. New/Open may install anew; Undo/Redo/Format and
   Grid replacement must not default to full-control import.
7. **Selection:** copy/cut/search/Select All remain canonical engine operations
   over the whole document. Preserve existing native selection if accepted text
   already matches; do not collapse every edit to `Start + InsertText.Length`.
   Ordered range alone is not an anchor/active direction certificate. Retain a
   previously known endpoint only when the observation supports it; expose
   unknown direction otherwise. A command requiring the active caret needs an
   actual native endpoint witness, not the old Mac heuristic promoted to proof.
   Distant Find/Go To/preview Reveal explicitly installs native global selection
   and scrolls it into view without rewindowing to a page.

Readback/map/diff and successor projection remain O(n) in this initial product
candidate, particularly Windows `EN_CHANGE`. At most one baseline and one final
candidate are retained; no asynchronous queue of complete strings. Do not guess
all input deltas from keyboard/paste events. A later AppKit edited-range fast
path must separately qualify character-versus-attribute edits and composition.

### 8.4 Semantics and native geometry without full style stalls

The format/session driver still analyzes the canonical snapshot using existing
full/incremental/session contracts. **Restricting decoration to visible ranges
does not restrict semantic truth to those ranges.** Keep global diagnostics,
coverage, completeness, offscreen conflicts and Flow/Block/Grid source maps.
Use actual source-view interest instead of legacy `_pageStart/_pageLength` as
an accidental full-resident analysis interval. Do not set `PageSize` to document
length or lift the existing formatting limit as a UI implementation shortcut.

The shell obtains actual native visible character ranges, maps them to source,
and requests versioned semantics with a small guard band. Style work intersects
those ranges, coalesces adjacent equal effective colors, and retains the last
installed viewport/palette identity. On an edit/theme change, invalidate stale
visible attributes and apply current neutral/pending presentation until current
results arrive; do not leave a known wrong token color certified as current.
Offscreen attribute cache is revocable and never semantic authority. Scrolling
publishes the new visible styles without reparsing the whole file just to color
it. Long paragraphs must not silently expand visible styling to the complete
logical line. Bound work per UI turn and coalesce superseded results, rather
than issue tens of thousands of synchronous native attribute calls.

Windows uses the already-qualified detached TOM foreground path but applies
only intersected native ranges, preserving keyboard/selection/undo state. TOM
[range GetPoint](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextrange-getpoint)
provides native endpoint geometry; its availability/return status must still be
checked. On macOS use the observed actual TextKit backend's visible-layout
range and glyph geometry; do not select TextKit 2 by assumption or trigger a
legacy-manager conversion just to inspect it. Character ranges and glyph ranges
are distinct, particularly for combining text/bidi. Existing platform shaping
and TextKit sources in sections 4 and 7 remain the implementation foundation.

Only the source native layout answers hit testing, caret bounds, visible ranges
and accessible source geometry. Source AX/UIA must preserve established identity
and canonical global ranges through the exact source/native map. Do not attach
the existing Canvas geometry provider to the candidate or expose both a custom
Document and a duplicate native source Document. Flow/Block/Grid remain distinct
read-only semantic panes with their current identities and navigation. A native
control's built-in reader support is useful production machinery, **not** proof
that its CR-normalized ranges meet mote's source-backed external contract.

### 8.5 Assignable implementation and acceptance sequence

| Single-writer area | Concrete next delivery |
| --- | --- |
| Shared source contracts/binding and controller | Add optional capability, source state and candidate admission; implement engine range synchronization, global selection/reveal, chrome-only refresh and semantic-interest dispatch. Portable tests use a fake source shell and actual Document/history. |
| `EditorPresentationProfile.cs`, launch tests and factory | Add explicit candidate route after API agreement; keep every existing launch valid and old diagnostic parsing unchanged. Profile fixed for window lifetime; no file-size switching. |
| Windows shell/source helper | Full-body product RichEdit with existing menus/panes; composition and arbitrary native input ingress, exact maps, guarded range replacement, viewport TOM styling, native source draw trace and source identity. Reuse qualified algorithms, not probe-window lifetime. |
| Mac shell/source helper | Product NSTextView with existing menu/responder/marked-text lifetime; guarded character replacement, attribute-only visible styling, selection observations, source geometry/AX and source draw trace. |
| Accessibility integration | Source-backed ranges and one native source Document, real focus/Reveal and preserved semantic pane navigation. Coordinate shell geometry seam before attaching providers. |
| Hosted acceptance | Real candidate launch, controlled and externally delivered edit, history/selection/scroll/theme, Save and **new process** exact reopen. Existing single-binary, capacity and LegacyPage/Continuous regressions remain independent checks. |

First close portable admission invariants and interactive candidate engine
integration, then actual four-RID commands/geometry/correct bytes, then real
composition/readers and same-machine latency distributions. Retain the existing
reference run as the counterfactual for full import and full style cost; do not
rerun it solely because the new product route exists. Trace install, readback,
reconciliation, engine admission, range publication, semantic publication and
source draw through existing bounded local telemetry. Attribute failures to the
actual phase; draw return is still not physical presentation. Include stale
callback, dirty Open cancellation, style/Undo during marked text, encoding
failure, external file modification, and recovery export journeys.

The first product candidate is **not** an accepted 100-MiB representation. It
preserves whole canonical text and exact Save, but duplicates resident source,
uses full readback and inherits native layout/import limits. Measure existing
long-line/capacity regressions before promotion; never truncate source, turn off
semantics secretly, switch to LegacyPage by size or claim few-MiB success covers
them. If range/viewport work cannot meet the full capacity contract, continue
the A2 or genuinely single-layout text-service design from section 3 as a real
implementation, not an indefinite promise that large files use the old ribbon.

Embedded NUL is an explicit unsupported editable-import boundary of the current
reference. Candidate Open must retain the existing dirty document if import
cannot be certified, or retain an already opened canonical document with a clear
non-editable failure/recovery state; no truncation or falsely editable partial
text. An explicit user choice may open a separate established profile, never an
automatic live swap. Default promotion additionally requires a reviewed exact
representation for established NUL/capacity behavior, or an explicitly agreed
product contract change; warning text alone does not make compatibility vanish.

The promotion decision changes ordinary launch only after the one-locus product
journey, input/selection/reader contracts, daily-file performance and inherited
capacity are actually qualified. A safe explicit rollback profile remains
available, but is not a substitute for completing the intended source surface.
