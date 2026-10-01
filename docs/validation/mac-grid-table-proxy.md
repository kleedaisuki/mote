# macOS bounded semantic Table proxy candidate

Date: 2026-10-01. Status: **both Mac Native AOT/in-process targets executed; external row/cell/selection
transport passes, external coordinate-menu discovery fails; no full AX acceptance**.
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
