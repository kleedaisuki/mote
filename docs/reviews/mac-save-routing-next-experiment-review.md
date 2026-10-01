# Mac Save K/M route comparison: independent design review

Date: 2026-10-01. Reviewed
`docs/architecture/mac-save-routing-next-experiment.md`, the current ordinary
Mac menu/source client and Save oracle, and the persisted 36818175897 witness
audit. No product, probe or CI changes and no native rerun were performed.

## Decision and current priority

**The proposed route comparison is technically plausible, but not ready for
unqualified implementation.** The three corrections below are necessary before
a later experiment. Unopened exact-PID menu availability is an empirical
prerequisite, not a platform guarantee. No fundamental need for a product IPC
endpoint, extra permissions or activation was found.

**Do not implement or dispatch K/M now.** The user has reprioritized durable
end-to-end telemetry and causal provenance over more variant probes. Preserve
this review as a future handoff; the plan's existence is not authorization to
spend its process budget. If resumed, start with one 1 MiB feasibility pair on
each Mac RID (four original processes total), inspect the actual graph/action
evidence, and only then explicitly approve expansion. The 16-original ceiling
is a reasonable finite later ceiling, not the right first allocation before
unopened-leaf feasibility has been established.

## Necessary corrections

### R1 — Action-name allocation cannot be prebounded using attribute-count APIs

**Location:** bounded lookup item 3, “Count before copying arrays,” including
the 16-action-name limit. **Confidence: high.**

Apple's `AXUIElementCopyActionNames` returns the complete array and accepts no
start index or maximum count. `AXUIElementGetAttributeValueCount` counts an
array-valued *attribute*, not that action-name result. Children can use a
count-then-bounded-copy contract; supported action names cannot honestly inherit
the same preallocation guarantee.
([Apple action-name contract](https://developer.apple.com/documentation/applicationservices/1462053-axuielementcopyactionnames),
[Apple attribute-count contract](https://developer.apple.com/documentation/applicationservices/1459066-axuielementgetattributevaluecoun?language=objc)).

Correct the design to require a complete action-name return followed immediately
by CF type/count validation, reject more than 16 names, and inspect only bounded
strings. Explicitly acknowledge that this limits subsequent processing/retention,
not the AX framework's initial allocation. This is acceptable for the exact owned
native Save leaf in a disposable diagnostic helper; do not invent a count API or
claim a hostile-process allocation bound. The architecture already makes the
analogous honest distinction for title-string returns. Wrong types, excess count
and unavailable action metadata must prohibit Press and remain route-unavailable.

### R2 — Route contrast cannot override a positive K admission witness

**Location:** outcomes row “K fails / M exact-passes with positive
selector/admission in both order directions” and its before/shared-selector
conclusion. **Confidence: high for the inference limit.**

Clarify which arm owns the positive markers. M positives and K censored absence
support an early route hypothesis, not its proof. If the failing **K** retains
`controller_admitted`, it has positively crossed the shared selector and
synchronous controller boundary. M success cannot relocate that K failure before
the selector merely because the arms used different external mechanisms.

Use the last positive boundary of each failing process first, then the route
contrast: K admitted -> worker/I/O/result-observation remains; K selector only ->
composition/admission or witness loss; K ready only after forced cleanup ->
unresolved early-or-later boundary. Route-dependent downstream scheduling remains
possible. The original design's H1/H2 caveats otherwise correctly avoid identifying
Quartz delivery versus key-equivalent routing or physical-keyboard reliability.

### R3 — Outer time caps require explicit cleanup/report reservations

**Location:** sample-plan 6-minute whole-arm and 55-minute per-RID step caps.
**Confidence: high for existing phase arithmetic; implementation-dependent
for cleanup.**

Existing driver phase maxima alone exceed six minutes: runtime control 15 s,
source readiness 60 s, initial semantics 60 s, edit acknowledgement 15 s,
edited semantics 60 s, Save clean observation 60 s, original close 15 s,
reopen readiness 60 s, reopen semantics 60 s and reopen close 15 s total
**420 s**, before client-call overshoot, lookup, hashing, witness drain or
failure cleanup. Thus 360 s is a valid separate safety ceiling only if exhaustion
is explicitly an outer-budget censored result, not proof that every original
phase got its advertised deadline. The plan acknowledges censorship; the later
implementation must make it executable.

Use one parent monotonic whole-arm budget, stop admitting new work before its
reserved teardown interval, cap calls by remaining budget, and preserve the
phase/outcome reason separately. Retain bounded owned cleanup/kill, collector
finalization and report publication outside the workload allocation but inside
the enclosing step's reserved margin. A helper timeout after an attempted action
still allows bounded read-only outcome observation if budget remains; it cannot
raise out directly and skip that observation as current `save_exact` would if
`driver.save()` throws. Never label an early budget expiry a full 60-second Save
timeout. Outer step cancellation must not be the primary child reaper or the
only mechanism for producing reports.

At eight original arms per RID, 8 x 6 minutes is 48 minutes; a 55-minute ceiling
can fit one two-minute Swift compilation plus bounded overhead/reservations,
but only after they are enumerated. The current source polling helper `wait`
checks its deadline before executing a client call, so a call may overrun that
phase; explicitly preserve/report or tighten this behavior rather than claiming
a hard bound that the existing code does not implement.

## Source and API-backed feasibility assessment

- `MacEditorShell.CreateMenu` builds the File submenu with exact `Save` and
  `Save As…` labels, different selectors, and key equivalents (`:1281`).
  `AddMenu` explicitly targets the native `moteSave:` item at the delegate.
  `moteSave:` enters the same ordinary composition/Save path reviewed previously.
  This establishes an in-process menu item, **not** its unopened external AX
  representation or enabled state on the hosted runner.
- The current client creates the exact application AX element and checks owned
  PIDs with `AXUIElementGetPid`; it does not inspect the application menu bar.
  `AXUIElementCreateApplication` and `AXUIElementGetPid` support retaining that
  ownership model ([Apple AX API overview](https://developer.apple.com/documentation/applicationservices/axuielement_h?language=objc)).
  A fixed role-checked app/menu-bar/File/menu/Save path is preferable to global
  traversal. Match only exact Save, reject ambiguity and no-action hidden graph;
  do not press File or expand a menu to manufacture feasibility.
- Hammerspoon's `_findmenuitembypath` traverses application AXMenuBar children
  and `application_selectmenuitem` presses the resolved leaf. This is genuine
  production precedent, but its general traversal/count assumptions are not our
  bounds and are not evidence that mote's hosted unopened graph works.
  ([Source](https://github.com/Hammerspoon/hammerspoon/blob/master/extensions/application/libapplication.m)).
- `AXUIElementPerformAction` requests action execution; `.success` does not
  certify Save persistence and a messaging error is not safe retry permission.
  Preserve raw action return separately from bytes/normal exit/reopen.
  ([Apple action contract](https://developer.apple.com/documentation/applicationservices/1462091-axuielementperformaction?language=objc),
  [Apple messaging-error overview](https://developer.apple.com/documentation/applicationservices/axuielement_h?language=objc)).

## Contracts that should remain unchanged

| Area | Required contract / interpretation |
| --- | --- |
| One action | Count the parent attempted transaction before launching/invoking the action helper; the helper also records before Press. Return loss cannot justify another Press, K fallback or rescue of that child. Lookup calls are metadata, not Save attempts. |
| Ownership | Original retained child must still be alive; validate PID/types/roles/path and source guards immediately before action. This is not atomic identity protection: changes after checks remain an observation limitation. |
| Permissions | AX requires existing Accessibility trust; K additionally uses the existing Quartz posting permission. Retain both facts in both arms for comparable eligibility. No trust prompts, TCC writes, global objects or explicit app activation. An AX action may itself influence native state; do not promise state invariance. |
| Preparation | Run the same unopened metadata lookup in both arms, fresh private home/working file per arm, same executable/client/fixture identity and serial desktop use. Label K paired-diagnostic, not the unmodified ordinary baseline. |
| Inference | Equal preparation and balanced order reduce known delay/trend differences, not process/run-loop counterfactual differences. Two samples per route/size do not estimate reliability or tail latency. |
| Privacy | Only fixed known menu labels transiently for matching; never arbitrary document bodies/titles, foreign menu identity, strings/exceptions/raw stderr in report. Preserve whitelist stage/count/error/Boolean fields. |
| Result | Clean chrome is only prerequisite for one independent full-byte oracle. Original/reopen normal exits and separate trace integrity remain mandatory. Save witness health is separate, original-only, not a result substitute. |
| Failure | Disk digest only after outcome waiting; one existing owned close cleanup, no dirty-dialog buttons, no repeat Save. Forced-kill EOF does not convert missing markers into nonexecution evidence. |

## Proportional future allocation and refinements

The first four originals can establish external leaf discovery, one-call behavior
and a positive M convergence witness on both RIDs; they cannot establish a route
failure-rate contrast. Gate expansion on **route feasibility**, not which eligible
Save outcome happened to pass. Retain all planned/attempted arms and explicit
unallocated-after-infeasibility arms, rather than collapsing unavailable M into
a Save failure or silently removing it. Reversing order in later pairs is useful,
but doubling two file sizes before resolving feasibility is not.

The deferred event-local monitor is correctly excluded: it requires separately
validated Objective-C block ABI/lifetime support, has nested-loop blind spots,
and is unnecessary for the first route contrast. Do not implement a sidecar,
swizzle the application singleton or build an IPC Save API for this experiment.
The proposed architecture's Pivot Tracing connection is appropriately a causal
boundary principle, not a transplanted system or transferred overhead claim.

Minor refinement: distinguish lookup eligibility per process from pair eligibility.
If only one arm's metadata lookup times out, any already attempted K outcome must
remain visible but the pair is not a comparable K/M pair. Never retry read-only
lookup indefinitely to select a conveniently eligible process. Verify common
5-second helper cap against the ordinary client's current 6-second cap and mark
the change as diagnostic-specific, not silently changed ordinary acceptance.

## Scope and limits

This review reused the committed 368181 witness/outcome audit rather than repeating
completed native validation. No actual unopened AX graph, Press, paired timing,
privacy runtime capture or teardown cap was experimentally verified here. The
required corrections are design/implementation blockers for a future K/M handoff,
not demonstrated product Save defects. Current work remains durable end-to-end
telemetry and causal provenance; this route experiment is deferred.
