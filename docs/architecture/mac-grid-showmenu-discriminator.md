# macOS Grid ShowMenu: shared-client identity and request-history discriminator

Date: 2026-10-01. Status: **design only; no product, client, workflow or API change**.
Scope: the original external `AXUIElementPerformAction(..., AXShowMenu)` reply,
not downstream coordinate navigation, VoiceOver, physical input or release
acceptance. Implementation of the opt-in observation/cleanup seams below must
be agreed with the integration owner before editing them.

## Evidence this experiment must preserve

The [product proxy ledger](../validation/mac-grid-table-proxy.md) and
[seven-control audit](../validation/mac-grid-showmenu-action-contract-control.md)
already establish the following on both Mac RIDs in CI
[36811953139](https://github.com/kleedaisuki/mote/actions/runs/36811953139):

- The product advertises ShowMenu; its modern callback enters, queues once and
  returns true. The popup opens/closes; the original external reply is -25205.
  The established 41-check report still fails its original action predicate.
- All seven independent native controls return AX success with exactly one
  modern admission and one menu open/close. Runtime B/c metadata, the misspelled
  enabled getter and a generic need for legacy dispatch do not explain that
  difference in those controls. They do not eliminate a product-specific
  Native AOT callback/bridge difference.
- The control and product clients currently differ, as do their preparations.
  In particular, `tests/MacGridAxExternalProbe/Probe.swift::run` performs
  `AXSelectedCells` and `AXSelectedRows` **setters** before ShowMenu. Its first
  25 assertions are not a read-only query prefix. A minimal/full-prefix
  comparison cannot, by itself, establish an attribute-order cause.

Apple defines
[accessibilityPerformShowMenu](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol/accessibilityperformshowmenu())
in terms of successfully triggering an action. The experiment therefore keeps
next-turn admission; it does not substitute synchronous menu completion or
reinterpret a visible popup as a successful external reply.

## Recommendation: start with two sessions, not a speculative fix

Compile **one native C/Objective-C external client executable** and use its
same discovery, admission, read and action functions for both targets. Freeze
the executable/source hashes and action wire key. Use a standalone single
`runtime-B` control derived from `Contract.m`, not all seven controls in one
owner. Give that synthetic Table the same fixed identifier classification
`mote.csv.table`; do not change the existing seven-control test.

| Session | Fresh target process | Prelude | Action target |
| --- | --- | --- | --- |
| C0 | One runtime-B control; one window/Group/Table | Shared minimal discovery | Retained discovered Table |
| P0 | Ordinary synthetic CSV product launch, Grid AX opt-in | Same minimal discovery | Retained discovered Table |

Each session also gets a **fresh external client process**. No attachment,
framework cache, selection or retained AX handle is reused across sessions.
The product uses the existing external probe's fixed 1,100-record fixture,
private `MOTE_HOME`, disabled general tracing and strict one-file inventory.
Input hashes are checked before/after; no source edit or save is permitted.

The disposable owner publishes one fixed, session-bound `ready` marker only
after the owned window is visible, the stable semantic root is attached and
queued menu admission is internally available. For the product this includes
an installed bounded frame and no installation in progress; for the empty
control it includes its initialized stable root and owner view. The client
waits for that marker within the same readiness bound before discovering AX
objects. This avoids making an early product frame-null refusal look like the
already observed admitted-callback failure, without warming Rows/selection
through AX. Readiness is a target-specific internal predicate, not evidence
that control and product frame lifecycles are identical. Preserve ready
publication and any later epoch changes. The marker seam requires the same
coordination/opt-in constraints as cleanup below.

Shared minimal discovery is: query trust without prompting; create the exact
target-PID AX application; require one owned window; bounded breadth-first
discovery under that window using fixed Identifier/Role classifications and
count-before-copy Children; prune Table/Row/Column descendants. Require one
owned Table matching the fixed identifier and AXTable role. Keep the same
ownership/timeout checks before every API invocation. Actual traversal lengths
may differ because the graphs differ; preserve their counts rather than claim
that both targets received an identical number of discovery requests.

Read action names once, require bounded string array and exact ShowMenu
membership, then call the **same frozen action function exactly once**.
Do not query Enabled, Parent, Rows, selection, shown-menu or source text before
that call in C0/P0. There is no retry, second target element, alternate action,
menu press, logical navigation or synthetic event fallback.

### Put graph/identity auditing after the reply

Retain the original AXUIElement through the session. Immediately after the
action returns, run one bounded re-discovery with the same finder. Preserve
`CFEqual(original, rediscovered)` only when exactly one valid owned current
Table is found; otherwise equality is **unknown**, not false. No raw pointer,
identifier string or framework-private object ID is persisted.

Compare the pre-action discovered window/Group handles with post-action Table
Parent, Window and TopLevelUIElement. Check reciprocal Parent.Children
membership once, bounded counts and a maximum six-link parent walk to the
owned window, stopping on duplicates. Record roles as fixed enums and equality
classes; never walk menu contents or copy cell/source values. If a parent was
not uniquely discoverable, that expected-parent comparison is unknown.

This avoids priming extra graph attributes before the primary reply. It detects
observable wrapper/graph changes, but **post-action equality does not prove
identity was unchanged at every instant during transport**, and CFEqual does
not certify a managed owner or internal frame epoch. Those need server facts.

## Small opt-in server observation, not an alternative implementation

The product/control server records the same fixed fact vocabulary, without
adding an accessibility attribute or replacing any method return. The existing
product root/Group/child dispatchers and admission/lifetime seams are the
observation sites. Do not swizzle AppKit, add legacy methods, install fallback
selectors, call `description` or request new native attributes while recording.

At every action entry, including refusal paths, record: main-thread status;
owner lookup outcome; receiver equals current attachment root; attached;
installation in progress; frame present; attachment generation equal to the
one at discovery-ready publication; current frame epoch equal to that baseline;
queue result; returned Boolean or caught-exception classification. Establish
baseline facts at the first ready publication and explicitly report whether
the client arrived before or after it. Initial asynchronous frame publication
is not automatically retirement or an error. Off-main observation must not
read the mutable owner dictionary; record its refusal through a safe fixed
counter, with inaccessible fields unknown.

On queue/dispatch/menu-open/menu-close/detach, record only generation/epoch
**equality flags**, root-current/attached flags and bounded counters. Never
serialize native handles, document revisions, absolute coordinates or field
content. A callback trace without complete entry instrumentation cannot prove
that a missing entry means no callback.

To inspect request order, instrument **already implemented** information
dispatchers with fixed selector IDs (Role, Parent, Window, TopLevel, Children,
Rows, Columns, selected variants, Help, Identifier, ShownMenu, FocusedElement,
counts, Boolean queries and frame). Record entry/return sequence, receiver
category, return **kind** (nil, known-role, owned-parent/window/menu, bounded
array/count, Boolean, finite-frame, exception) and the admission equality flags.
For string-valued Help/Identifier/labels, retain only `string-present` versus
nil, never content or a hash. Mirrored control observations use the same IDs.

Use fixed numeric storage: last 512 pre-action records in a circular buffer
and first 512 records from the first action entry onward. Preserve discarded
prelude count and post-entry overflow count. Format/export only outside native
callbacks. The trace distinguishes `before-action-entry`, `inside-action-IMP`
and `after-action-IMP-return`; **the latter is not proof that the external AX
call has returned**. Client API begin/end sequence is retained separately.
An absent selector in a complete observed-dispatcher trace only excludes those
instrumented overrides: inherited/private AppKit reads remain unobserved.
Do not add a control override solely to make an unimplemented selector
observable: doing so would change the control being compared.

## Conditional comparisons: one factor, only where the prelude is feasible

Run C0/P0 first and stop to interpret them. Do not schedule a full factorial
matrix. Further arms use the same action function and fresh target/client
processes with one action each:

| Trigger | Next fixed comparison | What it can distinguish |
| --- | --- | --- |
| Stable C0 success / P0 failure | C1/P1: minimal discovery plus one fixed read-only graph block before action | Whether explicit graph-query preparation changes the target-specific reply; post-action audit remains identical |
| C1/P1 differs from C0/P0 | C2/P2: same graph reads in reversed independent order | Order versus mere presence of reads, only if every read is individually feasible in both targets |
| Re-discovered Table differs or retained one becomes invalid | P3/P4: identical two-discovery prelude; act on first retained versus second fresh handle | Retained-handle choice; both arms have the same query history and record CFEqual before action |
| Product minimal succeeds, original established probe still fails | P5: replay the original preparation through the frozen client, then one action | Original preparation bundle versus minimal, **not** read-order causation |

The fixed graph block is exactly Role, Parent, Window, TopLevel, then the
validated Parent's bounded Children; reverse only the first four independent
Table reads, leaving dependent ownership/Children validation last. Discovery
already necessarily reads Role; log that fact. No getter change, focus setter,
activation or graph rewrite is part of this comparison.

For P3/P4, hold the first handle for the same prescribed 150 ms interval and
perform the same second finder/read block in both arms. A result difference
supports a handle-choice interaction only when equality/currentness facts and
state agree. If first/second handles are CFEqual, this comparison cannot isolate
distinct native object lifetimes; report that limitation.

Do **not** replay product selection setters against an empty control Table.
Their preconditions are not shared. If P5 establishes a preparation dependence,
separate the original read-only query block from the selection-mutation block
in new product sessions. Preserve mutation order, local synthetic cells and
exact readback preconditions; failed preparation is unresolved, not a successful
ShowMenu case. First test read-only preparation without setters, then the
original setter bundle with its necessary lookup/readback scaffolding. Only
after a repeatable divergence reduce that bundle by valid dependency groups.
Keep the existing full probe untouched as the reference.

This is a bounded application of the failure-preserving reduction principle in
[Zeller and Hildebrandt, IEEE TSE 2002](https://doi.org/10.1109/32.988498), not
an assumed monotonic binary search: AppKit request histories may be stateful.
Retain success, the **same -25205 failure**, and unresolved as distinct outcomes;
do not accept a different error or unavailable preparation as reproduction.

## Bounds and cleanup without another external action

Keep the original product limits as ceilings, never enlarge them: 55-second
client monotonic deadline, 1-second installed per-element timeout, 12,000 total
admissions, one window, 256 discovery nodes/depth 12, Children <=128, no Table
descendant traversal. Readiness uses <=20 attempts at 150 ms separation within
the original 12-second readiness interval. Post-action audit has one traversal,
no readiness/convergence retry, <=128 additional admissions and <=3 seconds
within the same overall deadline. Action-name copy has no count-before-copy
API; enforce <=16/type validity after the unavoidable framework allocation.
Budget/deadline exhaustion makes that observation unknown, never a pass.

Provide an explicitly disposable diagnostic cleanup seam in both owners:
after the external client has persisted the original action result and completed
or exhausted its read-only audit, it writes one fixed `finish` marker to the
owner's session-local `.cache/` directory. A diagnostic-only main-thread timer
in default/tracking run-loop modes consumes that exact marker, cancels only the
already owned menu with `cancelTracking`, exports fixed facts and closes the
synthetic window normally. Marker path is normalized/repository-confined;
session/token/size are fixed and checked; no arbitrary command dispatch exists.
This is **not** a second AX action or input injection. It must be opt-in, absent
on ordinary launches and coordinated before implementation. Use the existing
native menu probe's tracking-mode cancellation mechanics, not an app-wide menu
or global event. Cancel cleanup timers/selectors on exit.
The ready/finish timer and instrumentation can perturb scheduling even when
they preserve semantic returns. Record the instrumented configuration and keep
the unchanged ordinary gate as the independent reference; an instrumented
success alone cannot close its failure.

Do not give one target its existing 150-ms pre-scheduled cancellation and the
other no cancellation: that would introduce a lifecycle/timing difference.
The shared `finish` mechanism must not cancel until the original external action
has returned. If the action never returns, a 75-second outer watchdog kills the
owned process tree, marks forced cleanup and leaves action result unknown.
Check normal target exit <=10 seconds after finish, client exit, trace overflow
and unchanged fixture hash independently. Forced cleanup never becomes clean
experiment completion. No TCC prompt/change, app activation, foreign-process
inspection, system-wide AX root or global key/mouse event is permitted.

Per RID: first pair uses two sessions. At most two additional comparative pairs
and one reverse-order confirmation of a discriminating pair are admitted in
one diagnostic invocation: <=8 fresh sessions. If that budget cannot answer the
question, preserve findings and propose the next bounded invocation; do not
silently keep spawning cases until a success appears. Confirmation reverses
the target-session launch order, not the recorded query history.

## Raw artifact contract

Store under `.cache/mac-grid-showmenu-discriminator/<run>/<rid>/<session>/`.
Schema below is a **shape**, not fabricated execution evidence. Nullable fields
mean unobserved/unavailable; enum values are fixed and whitelist validated.

```json
{
  "schema": "mote-grid-action-discriminator-v1",
  "session": "P0",
  "target": "product",
  "prelude": "minimal",
  "status": "reply-observed",
  "rid": "osx-arm64",
  "source_sha256": null,
  "client_sha256": null,
  "binary_sha256": null,
  "trusted": null,
  "owned_target": null,
  "discovery": { "attempts": 0, "nodes": 0, "admissions": 0 },
  "actions": { "attempts": 0, "names_error": null, "names_count": null,
    "advertised": null, "original_error": null },
  "identity": { "rediscovery_error": null, "unique_current_table": null,
    "retained_equal_current": null, "parent_equal_discovered_group": null,
    "window_equal_discovered_window": null, "top_level_equal_window": null,
    "reciprocal_child_occurrences": null, "parent_cycle": null },
  "server": { "observation_available": null, "callback_entries": null,
    "on_main_thread": null, "owner_lookup": null, "receiver_equal_current_root": null,
    "attached": null, "installing": null, "frame_present": null,
    "ready_baseline_available": null, "attachment_equal_baseline": null,
    "epoch_equal_baseline": null, "queue_result": null, "method_return": null,
    "opens": null, "closes": null, "prelude_discarded": null, "post_entry_overflow": null },
  "client_calls": [],
  "server_calls": [],
  "cleanup": { "finish_after_reply": null, "normal_exit": null,
    "forced": null, "fixture_unchanged": null }
}
```

Each client call row contains sequence, fixed phase, operation ID, receiver
category, numeric AX error and result-kind/count/equality classification only.
Server rows contain sequence, fixed phase/selector ID, category, result kind,
bounded counts and equality/admission flags only. Rows never contain arbitrary
native strings, menu/document text, coordinates, exception text or pointer IDs.
Parser tests reject unknown fields/enums, over-bound arrays and inconsistent
one-action counters. Include wrapper/client/owner exit facts and source/host
version metadata separately. A completed experiment can preserve a failing
reply; use `completed-failure-observed`, not product `passed`.

## Falsifiers and exact threshold before a product patch

| Observed outcome | Supported conclusion / next action |
| --- | --- |
| Same frozen client: C0=0, P0=-25205, live/current root and admitted callback | Client language and original 25-check preparation are not necessary for the observed failure; inspect fixed observed dispatcher differences, do not add a legacy bridge |
| C0 also fails with admitted callback | Control's prior successful environment was not reproduced; check shared-client/cleanup/readiness changes before attributing a product cause |
| P0 succeeds but original probe still fails | Preparation/client interaction remains plausible; run P5 and then split reads from setters, do not claim repaired product |
| Graph/equality or attachment facts disagree around action | Concrete graph/lifetime interaction is present; reproduce the exact disagreement before choosing a repair |
| Only getter order changes reply in matched fresh sessions | Request-history dependence is supported; reduce that fixed valid sequence, not more retries |
| All observed identity/graph facts agree; P0 still fails | Recorded graph/lifetime facts do not discriminate; unobserved framework reads or Native AOT-specific bridge behavior remain open |
| Missing entry instrumentation, overflow, untrusted client or timeout | Unresolved for the affected causal claim; no absence inference or product pass |

Before a corrective product patch, require: (1) a valid same-client baseline
failure and control success, or an explicitly isolated prelude dependence;
(2) a concrete violated documented invariant or a single-factor mechanism that
predicts a falsifying outcome; (3) two fresh comparative observations with
reversed session launch order, and both RIDs investigated for a shared-platform
claim; (4) complete relevant entry/order facts, no forced-cleanup dependence;
(5) an explicit patch proposal preserving admission, semantic roles, selection,
source contracts and strict one-binary delivery. A graph discrepancy alone is
not proof that fixing it repairs ShowMenu, and a selector trace discrepancy is
correlation until the controlled intervention changes the prediction.

After any approved patch, the **unchanged original 41-check external gate** must
return original AX success, pass all assertions and exit normally on both Mac
RIDs, alongside existing native menu/retirement/source tests. Native control
success or an instrumented comparison never substitutes for that acceptance.

## External basis and implementation anchors

- Apple: [accessibilityParent](https://developer.apple.com/documentation/appkit/nsaccessibilityelementprotocol/accessibilityparent())
  and [accessibility hierarchy](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/Accessibility/cocoaAXOverview/cocoaAXOverview.html)
  motivate explicit parent/child/current-root checks rather than guessing that
  an NSView inheritance graph equals its external semantic graph.
- Production precedent: the [pinned Chromium Cocoa adapter](https://github.com/chromium/chromium/blob/4a27de5bfd26225308f44e393bf2c560d31e8043/ui/accessibility/platform/ax_platform_node_cocoa.mm)
  contains modern/legacy interoperability handling. The seven-control evidence
  rejects adopting that handling merely by analogy; this plan isolates the
  actual product boundary first.
- Research method: [failure-inducing difference isolation](https://doi.org/10.1109/32.988498)
  supports valid failure-preserving reduction, but does not supply an AppKit
  mechanism or justify assuming deterministic/monotonic histories.
- Code anchors: `tests/MacGridAxActionContractProbe/Contract.m` (owned native
  control/client), `tests/MacGridAxExternalProbe/Probe.swift::run` (preserved
  original history), `MacCsvGrid.Accessibility.cs` (root registration,
  information dispatch, action admission, lifetime),
  `MacCsvGridAccessibilityProbe.cs::CheckMenuPopup` (owned tracking-mode
  cancellation), and `MacCsvGridMenuDiagnostic.cs` (fixed-vocabulary evidence).
