# macOS Grid ShowMenu: shared-client identity and request-history discriminator

Date: 2026-10-01. Status: **first pair executed; next experiment proposed only**.
This document's revisions do not change product, client, workflow or APIs.
Scope: the original external `AXUIElementPerformAction(..., AXShowMenu)` reply,
not downstream coordinate navigation, VoiceOver, physical input or release
acceptance. Implementation of the opt-in observation/cleanup seams below must
be agreed with the integration owner before editing them.
The [independent design review](../reviews/mac-grid-showmenu-discriminator-review.md)
is incorporated below: exclude action warmup and keep the first pair's server
observation lean. The seven-control audit remains unchanged.

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
- These seven-control `modernCalls=1` results are **non-inspection modern
  admission-attempt counts**, corroborated by `requests=1`, not total action
  method invocation counts. The old owner's `Metadata()` invokes ShowMenu
  directly through `objc_msgSend` and through `NSInvocation` while
  `inspecting=YES`; those two dry-run entries return true without queuing or
  incrementing the modern counter. Thus its ABI/introspection experiment
  remains valid, but its owner setup is not a minimal request-history baseline.
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

### Executed checkpoint: CI 36818175897

The [first-pair ledger](../validation/mac-grid-showmenu-first-pair.md) and
[independent raw audit](../reviews/mac-grid-showmenu-discriminator-review.md)
establish C0=AX0/P0=AX-25205 on **both** Mac RIDs using the same frozen client
per RID, fresh owners/clients and no hidden dry-run warmup. All four sessions
record one action entry, admitted request, dispatch, open, close and detach,
normal actual target/client exits, no forced cleanup and unchanged fixtures.
The original external product report remains 40/41, failing ShowMenu.

The product's post-reply rediscovery exhausted its 128-admission ceiling on
both RIDs: external identity/parent conclusions are **unknown**, not agreed or
stale. Native current-root/epoch equality is a narrower positive fact. Getter
observation remains unavailable. The contrast eliminates the original Swift
client and selection-setter bundle as necessary conditions for this reproduced
failure; it does not identify a native/managed bridge or graph defect. The pair
ran C0 then P0 once per RID, not a reversed-order reliability experiment.

The next recommendation below supersedes an immediate broad getter-recorder
implementation or an automatic graph-prelude matrix. Earlier C0/P0 procedure
remains its design/provenance; the optional recorder remains a later tool.

## Implemented first-stage procedure: two sessions, not a speculative fix

Compile **one native C/Objective-C external client executable** and use its
same discovery, admission, read and action functions for both targets. Freeze
the executable/source hashes and action wire key. Use a standalone single
`runtime-B` control derived from `Contract.m`, not all seven controls in one
owner. Give that synthetic Table the same fixed identifier classification
`mote.csv.table`; do not change the existing seven-control test.

**Do not reuse `Metadata()` or its dry-run setup.** Neither C0 nor P0 may call
ShowMenu directly, invoke it through `NSInvocation`, probe
`isAccessibilitySelectorAllowed:`, or otherwise warm action dispatch before
the external attempt. Pure class registration is necessary; non-invoking
metadata inspection is not necessary for the first pair and should be omitted.
Count **every actual action method entry from process startup**, including
inspection, refusal and unexpected framework entries, separately from admitted
requests. Expected total is one; any additional entry must be preserved and
explained, not hidden behind an admitted-effect counter. One external attempt
does not, by itself, prove one total method entry.

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
Evaluate readiness through existing read-only attachment/menu conditions:
never invoke the action, reserve its queue admission or call `TryQueue` to
test readiness.

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

## Stage 1: lean opt-in server observation

The product/control server records the same fixed fact vocabulary, without
adding an accessibility attribute or replacing any method return. The existing
product action admission/lifetime seams are the observation sites for C0/P0;
do not instrument root/Group/child information getters in this first pair.
Do not swizzle AppKit, add legacy methods, install fallback
selectors, call `description` or request new native attributes while recording.

At every action entry from process startup, including inspection and refusal
paths, increment total entries independently of admitted requests and record:
main-thread status;
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

Use fixed preallocated numeric fields and a small bounded action/lifetime event
ledger, formatting/exporting only outside native callbacks. Preserve any
counter overflow or unavailable entry path; it precludes an absence inference.
C0/P0 contains the frozen client's action begin/end/error facts, ready/finish,
complete action/refusal/lifetime accounting and the post-reply identity audit
only. Set `dispatcher_observation_available=false`, leave `server_calls=[]`
and leave dispatcher discard/overflow fields null. This first stage cannot
claim that a particular information selector was absent or that request order
caused the reply.

## Next smallest experiment: one explicit, truthful shown-menu relation

**Prefer C0 versus C-shown before broad information-dispatcher tracing.** The
reason is an inspectable representation difference, not the -25205 error name:

- `Control.m::PairTable` has no explicit `accessibilityShownMenu` override. Its
  existing strong `ownedMenu` and native open/close counters already identify
  the actually displayed menu; inherited getter behavior is not yet observed.
- The product explicitly runtime-registers that getter as object-returning
  `@@:` and forwards through `AccessibilityTableRead` to the physical
  NSTableView. `AccessibilityMenuTransition` publishes that exact menu on
  will-open and clears the native relationship on did-close. These are explicit
  getter/edge/forwarding differences, not proof of a broken contract.
- Apple's [shown-menu contract](https://developer.apple.com/documentation/appkit/nsaccessibility-c.protocol/accessibilityshownmenu)
  describes the currently displayed menu and permits nil. Its
  [protocol overview](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol)
  explains that a getter override exposes read-only informational access.
  Thus this override can change AppKit's advertised/readable information even
  with unchanged ShowMenu admission. Apple does **not** document that it is
  required for action success or that its absence/presence yields -25205.

### One factor, not a bundled miniature rewrite

Use the existing first-pair control source/client/ready/finish procedure in
fresh processes. Build one control executable with a fixed variant choice;
both variants share the same superclass, action IMP, menu, owner Group, graph,
frame, query history and lifecycle accounting. The sole intended behavior
difference is whether the runtime Table class has this explicit getter:

| Variant | Explicit getter | Menu creation/publication/cancellation |
| --- | --- | --- |
| C-base | Existing inherited behavior; no added getter | Existing control behavior unchanged |
| C-shown | Object-returning `accessibilityShownMenu`, `@@:` | Same existing control behavior unchanged |

C-shown returns its exact strongly owned menu **only while the already
recorded native lifecycle is open** (opens > closes and live attachment);
otherwise nil. It refuses off-main access without reading mutable owner facts.
Do not always return nil while a real menu is open, or expose a configured but
not yet shown menu: those introduce a deliberately incorrect relation rather
than mirror the observable product contract. There is no new setter,
NSView/NSTableView host, early menu construction, queue logic, selection/focus
change, expiry timer, synchronous popup or pre-action getter inspection.

This mirrors the product's **observable nil/current-menu/nil relation**, not
its physical NSTableView forwarding or Native AOT callback implementation.
Explicit getter presence and its truthful return behavior are the tested
representation factor; do not claim that this pair isolates presence from
return value or reproduces every product-specific bridge detail.

Keep the frozen external client unchanged: one action-name read, one action,
atomic original reply preservation, original bounded post-reply audit and
finish. In particular, do not request ShownMenu before the action or traverse
any menu afterward. No pre-action direct getter invocation, Metadata,
NSInvocation, selector-permission probe or second ShowMenu is allowed.

Add at most a **narrow getter-observation counter/16-sample numeric ledger**
inside the new getter, if needed: whether it was called; on-main/attached flags;
requests/opens/closes snapshot; returned nil versus the exact owned menu;
overflow. No calls are made just to populate those facts. Export after shutdown.
Untested/inherited calls in C-base remain unobserved, never zero by inference.
Do not instrument twenty other selectors or allocate two 512-row buffers.
Action/lifetime instrumentation and its overflow obligations remain unchanged.
Use a separately versioned narrow-observation artifact extension/strict parser;
do not relabel it complete dispatcher observation or alter old report schemas.

Run C-base then C-shown once on each RID, with fresh owner/client processes.
If the reply differs, confirm C-shown then C-base in fresh processes on both
RIDs. These <=4 control sessions per RID remain within the existing bounded
invocation ceiling. Keep the unchanged product gate as an independent reference;
do not modify product behavior or claim a new P0 result from control outcomes.

### Information gain and risk compared with alternatives

| Candidate | What a discriminating outcome would show | Cost/risk and priority |
| --- | --- | --- |
| Explicit truthful ShownMenu getter, C-base/C-shown | Whether this representation factor is sufficient to alter the original AX reply in a previously successful native control | One override using existing lifecycle; highest initial causal information per change; recommended |
| Add a native host, its setter publication and forwarding simultaneously | A bundle involving native graph, relationship ownership, callback/read ordering and getter changes | More realistic-looking but causally ambiguous; reject as the next single-factor test |
| Direct truthful relation versus native-host forwarding, in two otherwise identical controls | Whether crossing that native getter boundary changes the reply despite matched live relation values | Useful conditional followup if C-shown succeeds; publish/clear host state identically in both arms, establish new baseline, design separately before implementation |
| FocusedUIElement/Enabled/Help or another arbitrary getter | A particular added method changes behavior, if it does | No current call observation selects those methods; wrong-enabled sufficient-cause hypothesis already failed seven controls; lower priority than action's documented menu relation |
| Twenty-selector / 1,024-row dispatcher recorder | Correlated query sequence and observed nil/type/guard differences | Larger observer surface, inherited reads still unknown, no causal intervention by itself; reserve for no narrow discriminator or a specific need |
| Increase post-reply rediscovery budget | More external identity evidence if it completes | Does not discriminate the original action cause; do not manufacture convergence by expanding limits |

Interpret the original reply, admitted effects and getter facts independently:

- **C-base=0/C-shown=-25205**, one admitted/current action and menu in each:
  after reverse-order confirmation, the explicit truthful relation is a
  sufficient *control-level* factor for this failure in that environment.
  A complete observed getter count can say whether its implementation was
  actually entered; even zero with complete instrumentation does not reveal
  private AppKit metadata inspection. This is not yet product causation.
- **Both=0:** adding that observable relation is not sufficient in this tested
  control. It does not eliminate the product's physical forwarding, nil timing,
  managed callback ABI, graph or other-method interactions. Next consider a
  separately matched direct-versus-native-forwarded relation, not a product
  patch or an unbounded search through getters.
- **Both fail, different error, incomplete readiness, extra action entry,
  overflow or forced cleanup:** the former successful baseline or target
  failure was not cleanly reproduced. Preserve unresolved facts; do not count
  any failure as -25205 reproduction.

Regardless of outcome, P0's external identity stays unknown. If that question
becomes necessary, a separate **post-reply** observation experiment can query
the retained Table's Parent/Window/TopLevel and bounded reciprocal-parent
membership **before** whole-window rediscovery, within the same 3s/128-call cap.
Those can yield partial graph facts without establishing global uniqueness or
retained/current CFEqual; rediscovery exhaustion must keep the latter unknown.
Keep its client hash/configuration distinct and do not bundle this read-order
change into the first C-base/C-shown comparison.

No control result licenses removing the product getter, returning nil during a
live menu or hiding the relation to force action success. Any eventual product
repair must preserve the truthful shown-menu contract and pass the unchanged
41-check gate under the patch threshold already stated below.

## Later optional information-dispatcher history

Only after a stable shared-client difference (C0=0/P0=-25205 with admitted,
current attachment) or another explicit followup requiring selector history,
propose this additional recorder as a **separate configuration/experiment**.
It is not a prerequisite for C0/P0 and must not be retroactively described as
part of its instrumentation. Re-establish the discriminating baseline with
the recorder enabled before interpreting its sequence; broad instrumentation
can itself perturb the transport. It does not relax the patch threshold below.
The executed first pair satisfies that prerequisite, but does **not** make the
broad recorder the preferred next step over the narrower relation intervention
specified above.

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
| Stable C0 success / P0 failure after the recommended narrow relation test does not discriminate | C1/P1: minimal discovery plus one fixed read-only graph block before action | Whether explicit graph-query preparation changes the target-specific reply; post-action audit remains identical |
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
  "instrumentation": "lean",
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
    "total_method_entries": null, "inspection_entries": null,
    "refusal_entries": null, "admitted_requests": null,
    "on_main_thread": null, "owner_lookup": null, "receiver_equal_current_root": null,
    "attached": null, "installing": null, "frame_present": null,
    "ready_baseline_available": null, "attachment_equal_baseline": null,
    "epoch_equal_baseline": null, "queue_result": null, "method_return": null,
    "opens": null, "closes": null, "dispatcher_observation_available": false,
    "prelude_discarded": null, "post_entry_overflow": null },
  "client_calls": [],
  "action_lifetime_events": [],
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
In stage 1, `server_calls` is empty and the separate fixed action/lifetime
ledger and counters carry entry/admission/cleanup facts; empty getter rows are
**unavailable instrumentation**, not an observed absence. In stage 2, the
dispatcher flag and bounded rows/discard/overflow facts are populated.
`action_lifetime_events` has a 16-event ceiling with an independent overflow
count; every method-entry counter is updated before any guard, separately from
that event-ledger ceiling. Its fixed phases are entry, refusal, queue-return,
method-return, dispatch, open, close and detach. Numeric counters must saturate
and retain an overflow flag rather than wrap. No discarded entry can support
an inference of exactly one total invocation.
Parser tests reject unknown fields/enums, over-bound arrays and inconsistent
one-action counters. Include wrapper/client/owner exit facts and source/host
version metadata separately. A completed experiment can preserve a failing
reply; use `completed-failure-observed`, not product `passed`.

## Falsifiers and exact threshold before a product patch

| Observed outcome | Supported conclusion / next action |
| --- | --- |
| Same frozen client: C0=0, P0=-25205, live/current root and admitted callback | Client language and original 25-check preparation are not necessary for the observed failure; propose the next targeted dispatcher-history experiment, do not claim stage-1 getter observations or add a legacy bridge |
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
