# macOS bounded semantic Table proxy candidate

Date: 2026-10-01. Status: **both Mac native targets execute guarded independent external navigation,
retirement and normal close successfully; original AXShowMenu acknowledgment
still fails, so no whole external AX acceptance**.
The established default and native physical input implementation are unchanged.

## Why this representation changed

The native-first modern Table selectors passed in-process diagnostics, but the
unchanged external AX client observed default unlabeled rows on both Mac RIDs
in CI 36787947202 / 5977250. The `AXRows`-only legacy discriminator also failed
both external targets in CI 36789139005 / 3aae8d5. See the preserved
[experiment and falsification](mac-grid-rows-bridge-experiment.md).

These are real cross-process contract failures, not a label-extractor problem:
both Description and Title were absent for rows, while our column labels,
identifiers and help transported correctly. The exact private framework path
is still not identified. Further legacy attribute patches were rejected under
the experiment's stopping rule. No oracle or external helper was weakened.

## One semantic graph, one physical input implementation

```text
Existing source AXTextArea (unchanged sibling)
Existing Grid View / AXGroup
  stable NSAccessibilityElement / AXTable  "CSV grid window"
    current epoch column ordinal headers
    current epoch AXRows
      row ordinal header
      current epoch AXCells
  actual logical row scroller
  actual logical column scroller
  actual selected-cell detail

Rendering, native first responder, keyboard and context menu: NSTableView
```

`_accessibilityTable` is stable for one adapter attachment. It uses the existing
main-thread `Instances` lookup, not a child window identity. Child rows,
columns, headers and lazily created cells retain their existing never-reused
window serial. No row/column/cell wrappers or values are derived from recycled
NSTableRowView/NSTextField objects.

| Relationship | Authoritative object |
| --- | --- |
| Group children | Exactly proxy Table, real logical row/column scrollers, detail; native scroll subtree excluded |
| Proxy parent | Existing Grid View |
| Row/column/column-header parent | Stable proxy |
| Cell/row-header parent | Its exact current semantic row |
| Row children | Its ordinal header and the same current cells used by local lookup/selection |
| Table local counts | Current admitted bounded frame only; no whole-file/prefix total masquerading as Table size |
| Native Table parent/focus/hit | Projects proxy/current cell; it is not a semantic Table in the exposed child graph |

Physical NSTableView is explicitly not an accessibility element and has empty
accessible children. The containing Group's explicit child array is the
important subtree replacement: ignored-element flags alone could promote
native implementation rows. Native scroll rendering and its view hierarchy
are not removed or hidden. No native parent/child property stores a proxy/View
retaining cycle; relationships are read from the live owner lookup.

## Preserved behavior and guarded bridges

- Native `makeFirstResponder:` and its readback still target NSTableView.
  Current semantic focus is a cell, or the stable proxy for explicit Table-only
  focus/Pending focus. Source/scroller focus returns no Grid focus.
- Proxy and physical Table focus queries use the same adapter facts. Existing
  composition refusal, Pending focus refusal, focus/selection independence and
  native arrow/Shift routing remain in place.
- Proxy selection setters use the existing current-epoch full-rectangle
  validation and sole selection owner. No default native row-selection policy
  is reported as semantic cell selection.
- Proxy `accessibilityPerformShowMenu` forwards only to the established native
  Table menu. That menu freezes its existing identity/coordinates; controller
  admission, source action refusal and command acknowledgment limits are not
  changed. No AX press/Copy/Replace/editable-value source action was added.
- Proxy/physical/group point queries use one bounded hit lookup. Actual native
  cell and header bounds determine hits; blank viewport space returns proxy,
  not an invented cell. Outside the real scroll viewport, the Group directly
  dispatches to its real scrollers/detail when their clipped bounds contain
  the point. Blank Group space returns the Group, outside space nil; no
  superclass traversal can re-enter the hidden native scroll subtree.
- The Table footprint uses the real visible NSScrollView viewport including
  native headers, excluding separate logical scrollers. Both root and children
  use actual view-to-window-to-screen conversion and architecture-correct
  CGRect ABI; row headers without a painted gutter still have empty geometry.
- Layout/selected-cell notifications target proxy; focus notifications target
  the current semantic focus node. Metadata is published before notification,
  and superseded frames stop outer event delivery.

Retiring an installation removes all child lookup entries before native
releases, clears the old frame, and preserves only the stable attachment root.
Detaching zeroes that handle and removes its owner entry before releasing it;
an externally retained root cannot reach the adapter, parent, rows or document.
Callbacks reject off-main access before mutable owner lookup and never decode
source, await workers or allocate children in proportion to the whole file.

## Internal API and delivery scope

Production changes are confined to Mac Grid accessibility registration and
lifetime seams. `AccessibilityTable` is an internal semantic-root getter;
`MacEditorShell.ProbeGridAccessibilityTable` is diagnostic readback only. The
existing `ProbeGrid` tuple still exposes the physical Table and identity.
There is no Engine/Formats/public API, source provider, source input island,
helper/oracle/workflow change, extra native library or new product payload.
The legacy `accessibilityAttributeValue:` experiment has been removed.

The opt-in native probe now addresses semantic selectors on the proxy, but
dispatches synthetic keys/native selected-row mutations to the actual Table.
It checks group/root/row/cell parent identities, empty physical children,
off-main refusal, native focus readback, proxy/physical/group point agreement,
root stability across child retirement, and a retained root after disposal.
The existing Missing frozen-menu/native keyboard regression remains intact.
These are new target checks to run, not claimed native results.

## Validation and next acceptance

On Windows/.NET SDK 10.0.400, the Mac accessibility focused filter compiles the
candidate and passes **10/10** managed cases. These tests cover pure fixture,
geometry, ABI layout and native-intent policy; they do not load AppKit. Evidence:
`.cache/mac-grid-table-proxy/table-proxy.trx`. Independent review:
[`mac-grid-table-proxy-review.md`](../reviews/mac-grid-table-proxy-review.md).

Next, freshly publish both `osx-x64` and `osx-arm64` under the strict single-file
Native AOT inventory. Root must update the in-process probe source pin to the
frozen bytes before running it. Run both existing base/opt-in native diagnostics
and the unchanged external harness, recording the entire child exit and report.

The external tree must have exactly one Table, our absolute Row 1 identity,
matching local cell ranges/parent/selection, full independent source, guarded
rebase/retirement and normal close. Point/global-focus entry paths must not
resurrect native rows. A Table-local focus getter alone does not establish the
application/window global AX focus query. Neither a partial row success nor
green containing non-gating CI jobs establish acceptance. VoiceOver, real IME,
multi-monitor geometry and performance remain separately unclaimed release gates.

## Primary-source rationale

Apple's [custom-controls guide](https://developer.apple.com/library/archive/documentation/Accessibility/Conceptual/AccessibilityMacOSX/ImplementingAccessibilityforCustomControls.html)
supports custom NSAccessibilityElement representations with explicit
role/label/parent/children and notifications. Its
[standard-controls guide](https://developer.apple.com/library/archive/documentation/Accessibility/Conceptual/AccessibilityMacOSX/EnhancingtheAccessibilityofStandardAppKitControls.html)
explains that excluding an element promotes its children; this is why the
Group's authoritative child graph is required. The
[modern NSAccessibility API](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol)
separates informational getters from setters/actions. The
[retired key-based guide](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/Accessibility/cocoaAXManipulateHierarchy/cocoaAXManipulateHier.html)
discourages the legacy API, strengthening the maintenance case against an
expanding attribute workaround.

The repository's [bounded contract](../csv-grid-accessibility-contract.md)
continues to supply the semantic and source-ownership invariants. Its cached
frame/reader-task rationale remains valid; native platform representation is
the engineering variable changed in response to actual external evidence.

## First proxy target execution: CI 36791254056

[CI run](https://github.com/kleedaisuki/mote/actions/runs/36791254056),
commit `f657c00`, freshly publishes the proxy candidate on both Mac RIDs. Raw
job logs (x64 job 110144423559, ARM job 110144423638) contain both actual outputs
`Mac CSV Grid AX selector probe passed; external AX/VoiceOver/geometry gates remain untested.`
and `mote-native-mac-csv-grid-ready` in the combined selector step, without a
step error. The separate base native CSV Grid readiness marker also appears.
These are actual child outputs, not merely strings printed in workflow source.

| External target | x64 | ARM64 |
| --- | --- | --- |
| Host | macOS 15.7.9 | macOS 26.6.2 |
| Fresh one-binary bytes | 16,744,800 | 16,396,440 |
| Swift typecheck / trust | passed / true | passed / true |
| Passed checks through context menu action | 26 | 26 |
| First failure | `unique-coordinate-menu-item` after bounded 3-second readiness wait | 12,000 admission budget exhausted while searching coordinate menu |
| Phase / Swift exit | logical-navigation / 1 | logical-navigation / 1 |
| Input unchanged / forced cleanup | true / true | true / true |
| Normal close | not exercised | not exercised |

Both external clients now observe **Row 1** in AXDescription, the exact custom
window-axis identifier and AXHelp. Exactly one semantic Table, independent full
source/offscreen tail, bounded axis counts, ordinal headers, exact cell values,
Empty versus Missing, cell parent/local ranges, rectangle selection, atomic
sparse/over-budget/row-setter refusal, duplicate deduplication, source selection
independence and clear selection all passed. This resolves the previously
observed native row-wrapper metadata transport defect for this fixture on both
platform targets; it does not prove arbitrary files or assistive-reader behavior.

`AXShowMenu` returned success on both RIDs, but that alone proves neither a
visible menu nor its reachable semantic items. x64 recorded only 1,638 admissions
and then failed the exact menu predicate; ARM hit 12,000 before predicate failure.
The shared missing-menu observation means an admission-limit increase alone
cannot establish acceptance. Existing reports have no per-phase counters or
client elapsed timing; the raw enclosing external steps lasted approximately
14.2 seconds x64 and 9.3 seconds ARM **including Swift compilation and cleanup**,
not editor latency measurements. No evidence identifies a native AX hang.

Artifacts and downloaded raw logs are under
`.cache/ci-36791254056-mac-proxy/{x64,arm64}/mac-grid-ax-external.json` and sibling
`x64-job.log` / `arm64-job.log`. Binary SHA-256: x64
`5B81F07D746656702CF5A92C924299FA01E36F3CD78A48723A994432B0F05DCA`;
ARM `6AEB81A852D6095ECF0B039A8E741749B255E8F49DB6D828702DA89A77BE47C2`.
The unchanged fixture hash is documented in the external harness contract.
Logical jump, retirement, mixed stale selection and normal close were not reached.
Containing non-gating steps report success despite the real exit-1 diagnostics.

The next test-only discriminator adds bounded content-free phase admission/poll/
traversal counters, maximum tree nodes, current AXMenu/AXMenuItem counts, exact
coordinate-item label versus AXTitle match counts, and per-check admission/time
snapshots. It also records only type/error/bounded count of the documented
`AXShownMenuUIElement` relation on the verified Table and application. A returned
single element additionally has ownership classification, known-role classification
and bounded child count queried only after exact editor PID validation (no menu
content or child traversal). This tests whether a contextual menu is exposed
through a relation excluded by the intentionally Table-pruned app traversal.
It preserves all semantic predicates, 12,000 admissions, 55-second
lifetime, 256-node tree, array bounds, exact PID isolation and cleanup. Native
Swift typecheck of this instrumentation remains pending the next Mac execution;
portable fixture preflight alone is not native evidence.

## Shown-menu forwarding target: CI 36792454502

[Run 36792454502](https://github.com/kleedaisuki/mote/actions/runs/36792454502)
at `0b85a0e` freshly builds the informational shown-menu forwarding and the
unchanged-budget, instrumented external helper. Both Mac jobs again produce the
actual selector-success and enclosing readiness markers, with no combined-step
error (ARM job 110148263182; x64 job 110148263198). Swift instrumentation typechecks
on both real Mac targets. The in-process marker relation exercise therefore runs,
but it is not actual contextual menu tracking.

| External fact | osx-x64, macOS 15.7.9 | osx-arm64, macOS 26.6.2 |
| --- | --- | --- |
| Binary bytes | 16,750,456 | 16,398,040 |
| Actual external status / Swift exit | failed / 1 | failed / 1 |
| Last passed assertion | context-menu-accessible (26 passed) | context-menu-accessible (26 passed) |
| First failure | unique-coordinate-menu-item false | admission budget exhausted during menu search |
| AXShownMenuUIElement Table / app | absent, -25204 / -25204 | absent, -25205 / -25205 |
| Total admissions / client elapsed | 5,956 / 6.768s | 12,000 / 4.487s |
| Logical-navigation admissions | 4,322 | 9,585 |
| Logical-navigation polls / traversals | 7 / 7 | 13 / 13 |
| Maximum app-tree nodes | 94 | 113 |
| Last tree AXMenu / AXMenuItem counts | 7 / 50 | 8 / 50 |
| Last exact coordinate label / title matches | 0 / 0 | 0 / 0 |

Both relation queries occur immediately after the successful AXShowMenu action,
before the polling search. Neither candidate relation produced an AX element,
so no returned-menu PID/role/child fields were available. Error -25204 on x64 is
a transport completion failure: it must **not** be reported as proof that the
product getter returned nil. ARM -25205 is attribute unsupported, but does not establish
whether the native menu never opened, closed before the query, or lacked the
shown-menu relationship. Label-versus-title diagnostics both finding zero gives
no evidence for simply relaxing the established label oracle.

The ARM limit is demonstrably repeated bounded traversal pressure, not the
55-second lifetime limit: 9,585 admissions in 13 menu-search traversals consumed
most of 12,000 total in 4.487s. Neither tree exceeded its 256-node bound. x64
reaches a real failed menu predicate without hitting the admission limit.
Per-check timings show AXShowMenu returned at 1.600s x64 and 1.137s ARM; they
are client-relative synthetic timestamps, not edit/startup performance measures.
The 3-second readiness bound does not cancel a callback's already admitted
bounded AX calls, so total client duration can exceed that readiness interval.

Both original files remain byte-identical; both require forced editor cleanup.
Navigation, stale-node retirement and normal close remain unexercised. The
containing green non-gating steps conceal real exit-1 reports, confirmed by raw
job logs. Artifacts/logs are under `.cache/ci-36792454502-mac-menu/`.
Binary SHA-256: x64
`AFD933789E8A8AFB0E894B68F57D60721BC68F6BC52C0A401B525BB2419D9F67`;
ARM `C0ECC093152D9EA432D8C2F0D45FB96DDBC8E8201270FC36FB9C81586EAA1828`.

**Verdict:** the explicit getter bridge passes its in-process marker contract,
but is insufficient for actual external coordinate-menu acceptance. The next
informative discriminator is content-free native open/close/show lifecycle
accounting, not another speculative budget increase or weakened menu predicate.

## Correct BOOL and lifecycle target: CI 36794910486

[Final run 36794910486](https://github.com/kleedaisuki/mote/actions/runs/36794910486)
at `e439942` exercises the corrected native BOOL roundtrip and bounded lifecycle
trace. Both x64 job 110156619460 and ARM job 110156619500 print actual selector
success and enclosing `mote-native-mac-csv-grid-ready` markers without a combined
step error. External Swift typecheck and exact-PID trust pass on both hosts.

The real external result is **failed / exit 1 on both RIDs**, with 25 passed
checks and the first false assertion `context-menu-accessible`, detail
`AX=-25205; no key-injection fallback`. x64 consumes 1,635 admissions / 1.339s;
ARM 2,409 / 1.226s. Logical-navigation consumes exactly one admission each:
the action. No menu polling or shown-menu relation query is reached. Thus the
empty relation-observation array means **not exercised**, not absent relation.

Both native traces are exactly:

```text
mote-grid-menu-v1 phase=show-enter seq=1 requests=1 opens=0 closes=0 open=0 result=-1 configured=1 items=12 coordinate=1 shown=0 key=1 first=0 active=1
mote-grid-menu-v1 phase=native-return seq=2 requests=1 opens=0 closes=0 open=0 result=0 configured=1 items=12 coordinate=1 shown=0 key=1 first=0 active=1
```

The bounded facts show the exact coordinate command exists in a configured
12-item native menu, the window is key and adapter active, but the inherited
native action returns BOOL false. No `will-open` or `did-close` callbacks are
recorded and the shown-menu flag stays false. This localizes the failure to
native action admission/presentation, not semantic row transport, menu titles,
query pressure or timeout. The physical Table not being first responder is an
observed fact, not by itself a proved cause. Earlier apparent AXShowMenu success
must not be used to override this corrected BOOL/lifecycle evidence.

Original files remain byte-identical and both editors require forced cleanup.
Logical jumps, retirement and normal close remain unexercised. Artifacts and
raw logs: `.cache/ci-36794910486-mac-menu/`. Binary bytes/SHA-256:

- x64: 16,824,064;
  `82E337CCE49915857BB80F697C59E26F4EA25199A541018C4D37D29F631017A0`.
- ARM64: 16,466,616;
  `3FB538EE18B799EC333A87A248B53D37FBAD2846085AC9AE4DEA056EABB68EE2`.

The predecessor run 36794858729 / `181a9b2` was cancelled. Its completed ARM
external diagnostic corroborates the same two-row refusal (2,378 admissions /
1.597s, exit 1); its x64 cancellation artifact says published executable absent,
with no editor PID, Swift execution or native rows. That prelaunch x64 artifact
is unavailable target evidence, **not** an editor defect. Final-run evidence
supersedes the cancelled run for two-platform accounting.

The next candidate may explicitly schedule the existing exact native menu on
the owner UI thread; a queued acknowledgment is not completion. Its target
acceptance must still prove the unchanged external coordinate menu/prompt/jump/
retirement/close oracle, with no synthetic key fallback or relaxed bounds.

## Owned next-turn popup target: CI 36796725674

[Run 36796725674](https://github.com/kleedaisuki/mote/actions/runs/36796725674)
at `ab6224a` freshly publishes the explicit deferred native popup candidate.
Actual combined selector and enclosing readiness outputs pass on both Mac
RIDs (ARM job 110161759808; x64 job 110161759910), without a combined-step error.
The unchanged separate Swift client typechecks and is trusted on both hosts.

Both external reports still fail / exit 1 at **context-menu-accessible**,
`AX=-25205; no key-injection fallback`: Apple's
[AXError definition](https://developer.apple.com/documentation/applicationservices/axerror/attributeunsupported)
classifies -25205 as **attributeUnsupported**, not actionUnsupported (-25206)
or cannotComplete (-25204). This is not evidence of a timeout. 25 prior checks pass, the action assertion
is the 26th and false. x64 has 1,640 admissions / 1.017s; ARM 2,437 / 1.540s.
Logical-navigation has one admission (the action), no polling/traversal and no
shown-menu observations. The later coordinate item/prompt, jump, retired-cell
selection and close assertions are **not exercised**.

Unlike the preceding native refusal, both bounded traces now contain these
four exact phases and values:

| Phase | Result | Opens / closes / open / shown |
| --- | --- | --- |
| show-enter | -1 | 0 / 0 / 0 / 0 |
| schedule-return | 1 | 0 / 0 / 0 / 0 |
| popup-begin | -1 | 0 / 0 / 0 / 0 |
| will-open | -1 | 1 / 0 / 1 / 1 |

Every row reports `configured=1 items=12 coordinate=1 key=1 first=0 active=1`,
one request and serials 1..4. There are no `did-close` or `popup-return` rows
before forced cleanup. Thus the candidate admits the request and enters real
native menu tracking; its delegate records opening and the shown-menu state.
This is actual product-side lifecycle progress, **not** external success: the
framework client still receives action failure despite queued admission true.
The exact reason (action transport, callback type metadata or other framework
behavior) is not yet identified by this evidence. A changed label predicate,
query budget or synthetic key fallback would not resolve this first boundary.

The shown-menu relation is not queried because the preserved action predicate
fails first. Empty observations must not be reported as absent external menu.
The four-row trace is below the 16-row cap; missing close/return rows therefore
cannot be attributed to whitelist capacity. Forced process cleanup interrupts
native tracking; this is not a graceful cancellation/close acceptance result.
Both original files remain byte-identical. Real screen reader, IME, tracking-
disposal stress and performance remain outside this synthetic gate.

Artifacts and raw job logs: `.cache/ci-36796725674-mac-menu/`. Binary bytes / SHA:

- x64: 16,841,952;
  `89FC8C3827C509A701009BD3CD04ACC48E58EB9878B580C753A273F3D551B76D`.
- ARM64: 16,488,600;
  `5D50ED0F726B5F87DCC18D5FB882F57566F09BCC443B754F37B2186B8B106BB7`.

**Verdict:** native popup presentation now occurs on both targets, but the
external action-result contract still fails and full external AX acceptance
remains blocked at that exact assertion. Containing green non-gating jobs do
not change the real Swift exit or failed check.

## Read-only action discriminator: CI 36797859586

[Run 36797859586](https://github.com/kleedaisuki/mote/actions/runs/36797859586)
at `833ef48` natively typechecks and executes the Swift/AppKit diagnostic on
both targets. Actual combined selector-success and enclosing readiness markers
pass without a combined-step error (ARM job 110165317397; x64 110165317431).
Both external clients remain **failed / exit 1**, preserving 25 passed checks
and the original `context-menu-accessible` falsifier, AX=-25205
(attributeUnsupported). The read-only followup completes on both; it does not
retry or press any menu, nor advance to navigation.

| Observed fact | osx-x64 | osx-arm64 |
| --- | --- | --- |
| AppKit showMenu action constant equals literal | true | true |
| Action names error / bounded count / advertised | 0 / 1 / true | 0 / 1 / true |
| Actual AppKit shownMenu raw wire key | AXShownMenuUIElement | AXShownMenuUIElement |
| Table relation (both API-origin labels) | error 0, owned expected AXMenu element | -25205, absent |
| Returned Table menu children / exact coordinate titles | 12 / 1 | not available |
| Application relation (both origin labels) | -25205, absent | -25205, absent |
| Single app-tree exact coordinate label / title matches | 0 / 0 | 0 / 0 |
| App-tree maximum nodes / last menus / items | 95 / 7 / 50 | 114 / 8 / 69 |
| Followup admissions / traversals | 700 / 1 | 779 / 1 |
| Total admissions / client elapsed | 2,345 / 3.455s | 3,215 / 0.644s |

The **presumed modern-versus-Carbon wire-key distinction is falsified** on both
actual hosts. The stored categories say which source expression supplied the
key; they are duplicate reads of the same key, not independent modern/legacy
relations. This corrects the earlier investigative hypothesis and documents
negative evidence rather than inventing another wire key.

The x64 relation is a positive cross-process transport result: exact editor PID,
AXMenu role, 12 bounded children and exactly one known coordinate title. It
proves that a Table-pruned general app traversal may miss a menu reachable
through its explicit relationship. It does **not** repair the failing action
acknowledgment or establish a successful menu press/prompt/navigation workflow.
ARM queries occur in a faster client run and find no supported relation in those
snapshots; timing is a possible explanation, not a demonstrated mechanism.
Neither platform's single no-wait traversal proves a menu was absent throughout
its lifetime. No readiness retries were added to manufacture convergence.

Both traces remain exactly four phases: show-enter, schedule-return(result=1),
popup-begin, will-open(opens=1/open=1/shown=1). All report configured=1/items=12/
coordinate=1/key=1/first=0/active=1. No did-close/popup-return appears before
forced cleanup. Both original files remain byte-identical. Logical jump,
retirement and normal close are still unexercised, and no external pass is
inferred from a visible menu or containing green job.

Artifacts/raw logs are under `.cache/ci-36797859586-mac-menu/`. Binary bytes/SHA:

- x64: 16,841,952;
  `EB433FF17F8FA08F4179C2FC44F16645A3CF8A4F773BC582BB78618FCFE62C36`.
- ARM64: 16,488,600;
  `D856B3BBAE0D3A68BD5F9FCACFFC6829E9114DEEB126F67796F39072AD7AC855`.

**Verdict:** valid action naming/advertisement and, on x64, correct explicit
menu transport are verified independently of the still-failing external action
reply. A useful next investigation targets that acknowledgment boundary rather
than a guessed wire-name difference, arbitrary label fallback or budget increase.

## Live selector-permission discriminator: CI 36799464145

[Run 36799464145](https://github.com/kleedaisuki/mote/actions/runs/36799464145)
at `d7b2473` executes the unchanged native action and external assertion with
additional fixed selector-permission trace facts. Both actual combined native
probes print inner selector-success and outer readiness markers without a
combined-step error (x64 job 110170316578; ARM job 110170316569). Swift typecheck
passes on both. The external result remains failed / exit 1, 25 passed checks
then **context-menu-accessible false**, original AX=-25205 attributeUnsupported.

Both traces contain exactly show-enter, schedule-return(result=1), popup-begin
and will-open. **Every recorded phase, including the live will-open callback,
reports allowaction=1 and allowshown=1.** At will-open the attachment remains
active, its window key, native tracking open and shown-menu state true;
configured=1/items=12/coordinate=1. There are no did-close/popup-return rows.
These are live main-thread owner facts, not a nil receiver, detached owner,
off-main default or early initialization result. The physical Table not being
first responder remains a separate observed fact, not a proven cause.

| Read-only followup fact | x64 | ARM64 |
| --- | --- | --- |
| ShowMenu constant equality / advertised action | true / true | true / true |
| Action names error / count | 0 / 1 | 0 / 1 |
| Table shown-menu relation | owned AXMenu, error 0 | owned AXMenu, error 0 |
| Relation children / exact coordinate-title count | 12 / 1 | 12 / 1 |
| Application relation | -25205 | -25205 |
| Single app-tree coordinate label/title matches | 0 / 0 | 0 / 0 |
| Followup admissions | 700 | 849 |
| Total admissions / client elapsed | 2,335 / 1.265s | 3,267 / 1.131s |

The relation uses AXShownMenuUIElement for both constant/literal source labels,
not independent wire keys. ARM's now-positive owned menu relation extends the
previous x64 transport evidence and shows the preceding absent ARM snapshot
was not an invariant inability to transport this relation. This does not prove
its timing cause or supply an external action acknowledgment. The Table-pruned
single general tree still misses the exact command that the explicit relation
correctly exposes on both targets.

**The live selector-permission refusal hypothesis is not supported:** both
relevant selectors are allowed while the actual menu opens. A permissive
selector override would not address a demonstrated refusal in this state and
is not justified by these data. The external error's framework mechanism
remains unresolved; neither a timeout nor a wire-key mismatch is established.

The followup is read-only and preserves the original failed assertion; no menu
press, prompt, logical jump, node retirement or normal close is exercised.
Both input hashes remain unchanged and owned editor cleanup is forced. Those
paths and screen-reader/IME/performance acceptance remain unclaimed.
Artifacts/raw logs: `.cache/ci-36799464145-mac-menu/`. Binary bytes/SHA-256:

- x64: 16,842,440;
  `EF98590AA12322EF0CA1F8D000FE27308BEB59C77A29174C1C9C60CE6DED9305`.
- ARM64: 16,505,608;
  `0AA07F3FEA66377E28582462B11611CD561B47E565A434BFF5D909B364403F96`.

## Guarded independent downstream execution: CI 36800944850

[Run 36800944850](https://github.com/kleedaisuki/mote/actions/runs/36800944850)
at `99fbe39` freshly builds both native targets and exercises the frozen guarded
Swift workflow. Actual combined native selector/outer readiness markers pass
without a combined-step error (x64 job 110174903182; ARM job 110174903133).
Native Swift typecheck and client trust pass on both.

**Both whole reports remain failed / Swift exit 1**. Exactly one of 41 checks
is false: the original context-menu-accessible action acknowledgment,
AX=-25205 attributeUnsupported. Forty checks pass, including the independent
workflow; there is no conversion of visible native menu into original action
success. The separate downstream object says completed / phase complete /
originalActionError -25205 on both targets.

Admission occurs only after 25 exact prior checks pass, advertised action and
fresh exact-PID Table shown-menu AXMenu <=128 children with exactly one command
matching both known Title and established label. Table relation facts are owned
AXMenu / 12 children / exactly one coordinate title. No ShowMenu retry or
app-tree menu discovery occurs; the synthetic prompt is identified through
existing bounded app traversal only after pressing the already verified item.

The following independently observed external workflow succeeds on both:

1. Guarded unique coordinate item and exact AXPress.
2. Unique numeric-coordinate prompt, setter **1001:17**, and exact Go press.
3. New first row **Row 1001**, first column **Column 17**, value **r01001c17**.
4. Cell ranges remain local 0:1, not absolute ordinals disguised as ranges.
5. Retained original cell cannot expose old value/range (both errors -25202).
6. Remote selection applies; mixing retired original node is refused (-25201)
   without changing valid remote selection.
7. Independent full source still returns **r01100c24** after navigation.
8. Single fixture window, AXClose press and no old value after closure.
9. Wrapper separately observes **normal owned-editor exit 0**, not just transport
   failure after close. Both `editor_normal_exit=true`, forced cleanup=false.

The closed-cell transport error -25204 alone is not lifetime evidence; the
actual normal-exit fact makes the conditional close assertion meaningful.
Original fixture hashes remain identical. Wrapper errors are empty.

| Bounded evidence | x64 | ARM64 |
| --- | --- | --- |
| Total admissions / client elapsed | 3,848 / 2.187s | 4,513 / 4.312s |
| Independent downstream | completed | completed |
| Original action / whole verdict | -25205 / failed | -25205 / failed |
| Actual editor normal exit / forced cleanup | true / false | true / false |
| Native menu request / open / close counts | 1 / 1 / 1 | 1 / 1 / 1 |

Both native traces contain six phases: show-enter, schedule-return(result=1),
popup-begin, will-open, did-close and popup-return(result=1). Shown/open change
from 0 to 1 at will-open and back to 0 at did-close; request count stays one.
All phases retain allowaction=1/allowshown=1 and configured 12-item exact-command
facts. The trace is below its 16-row cap and includes real graceful tracking
completion, unlike earlier forced-cleanup snapshots.

**Supported verdict:** given the independently verified owned native menu,
the bounded coordinate navigation, selection, stale-node retirement and normal
close workflow works on both RIDs for this synthetic fixture. The original
external AXShowMenu acknowledgment remains a real failing contract, so whole
external AX acceptance and release readiness are still not established.
VoiceOver/real IME, arbitrary file handling, multi-monitor geometry, tracking-
disposal stress and input/paint performance are outside this one workflow.
Client-relative elapsed values are not editor startup or interaction benchmarks.

Artifacts/raw logs: `.cache/ci-36800944850-mac-grid/`. Binary bytes/SHA-256:

- x64: 16,842,720;
  `9E60AEA84E588339CB9A7CE9D36AFB2A9B27629C73463EBE2C4F9A593A3564A1`.
- ARM64: 16,489,288;
  `EC1E61195123AB387558873887CDD90819323ED74B5CF254E6E45F8D52351D5B`.

### Fresh integration corroboration: CI 36802378381

[Run 36802378381](https://github.com/kleedaisuki/mote/actions/runs/36802378381)
/ `c453506` freshly republishes both targets after unrelated integration changes.
Both actual combined native markers and Swift typecheck pass (ARM job
110179342771, x64 job 110179342948). Both external reports retain failed / exit 1
with 41 checks, exactly 40 true and the sole original context-menu-accessible
false (-25205). Independent downstream again reaches complete, including the
exact numeric prompt/jump, absolute value/selection, old-node retirement and
normal close. Wrapper observes actual editor exit 0 on both, no forced cleanup,
unchanged input and no error. This corroborates the prior conditional workflow;
it does not fix or accept the primary action reply.

Native trace again records one request/open/close with six phases through
popup-return(result=1); both permissions remain true. All original budgets hold:
x64 3,843 admissions / 2.204s; ARM 5,594 / 3.334s. These are synthetic client
measurements, not interaction-tail benchmarks or a reliability distribution.
No regression is observed in the exercised workflow. Artifacts/raw logs are
under `.cache/ci-36802378381-mac-grid/`. Binary SHA-256: x64
`1B24D7357ECCCA349E73884221037AB3FEAF2F19781E6A168CD85A6621126D21`;
ARM `3CC3683D0BB7F57920762EA08E131B37433FC09236F8680DD9AE563A3A180E91`.
Containing green non-gating jobs remain distinct from the intentionally failed
whole external AX gate.
