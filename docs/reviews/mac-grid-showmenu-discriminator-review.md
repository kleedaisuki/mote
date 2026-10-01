# Review: macOS Grid ShowMenu discriminator

Date: 2026-10-01. Reviewer scope: design and existing implementation/evidence,
not implementation approval or an executed C0/P0 experiment.

## Verdict

**The shared-client C0/P0 comparison is feasible and scientifically useful.**
The current design correctly preserves the original action error, avoids a
second external action, puts extra graph queries after the reply, acknowledges
unobserved inherited AppKit dispatch, and does not equate a visible popup with
AX success. No demonstrated product defect or Native AOT incompatibility is
established by this review.

There is **one necessary clarification before deriving the new control**:
explicitly prohibit the existing control's hidden direct/NSInvocation action
warmup. The complete selector recorder is an optional second-stage experiment,
not necessary to run the first shared-client pair. The finish-marker mechanism
is feasible but is a changed control condition, not an already validated native
baseline; preserve that distinction in interpreting C0 failure/timeout.

Only this review document was changed. No product, probe or workflow edit and
no push were performed.

## Evidence inspected

- Design: `docs/architecture/mac-grid-showmenu-discriminator.md`, especially
  lines 38-82 (first pair), 86-147 (observation), 190-234 (bounds/cleanup),
  284-307 (inference/patch threshold).
- Control: `tests/MacGridAxActionContractProbe/Contract.m`, lines 39-59
  (next-turn admission and 150 ms cancellation), 137-163 (metadata),
  277-330 (owner lifecycle).
- Product: `src/Mote.Native/Mac/MacCsvGrid.Accessibility.cs`, lines 224-257
  (root runtime registration), 557-597 (action admission), 613-646 (dispatch),
  651-679 (owned-menu lifecycle/cancellation).
- Original request history: `tests/MacGridAxExternalProbe/Probe.swift`, lines
  430-451: multiple SelectedCells mutations and a SelectedRows setter precede
  ShowMenu. They are not a read-only prefix.
- CI [36811953139](https://github.com/kleedaisuki/mote/actions/runs/36811953139)
  raw JSON under `.cache/ci-36811953139-mac-action-control/`: both control
  `client.json` files report `control-completed`, all seven `actionError=0`,
  and both `server.json` files report normal shutdown, one admitted request,
  one modern admitted call, one menu open/close and zero legacy admitted calls
  per variant. Both original product reports remain failed with the sole
  `context-menu-accessible` false check and AX -25205; this agrees with the
  independent control audit. These are admitted-effect counters, **not total
  action method invocation counters**, because metadata suppresses accounting.

## Necessary correction / implementation prerequisite

### P2 — Exclude direct action warmup when deriving C0 (high confidence)

**Location:** design lines 40-43 and 78-82; control `Contract.m:137-153,301`.

`Server` calls `Metadata(element)` before publishing its graph. `Metadata`
sets `inspecting=YES`, calls the action IMP directly through `objc_msgSend`,
then invokes the same action through `NSInvocation`, and only afterward clears
`inspecting`. `admitMenu` returns true without queuing while inspecting; both
modern counters deliberately suppress these two calls. Consequently the
existing seven-control result really proves one external request and one
admitted menu, but not one total action method invocation.

The design says to derive a standalone runtime-B control and call the frozen
external action exactly once; it does not explicitly say whether that metadata
setup must be omitted. Copying the current owner setup unchanged introduces
two hidden action entries on C0 only. Even when those entries have no menu
effect, they invalidate a minimal matched request-history comparison and
could warm framework method-signature/dispatch paths that P0 does not warm.
This is a **conditional implementation defect**, not evidence that those calls
caused the currently observed product error.

**Correction:** explicitly prohibit all pre-action direct action calls,
`NSInvocation`, `isAccessibilitySelectorAllowed:` probes and `Metadata()` reuse
in C0/P0. Pure class registration and non-invoking metadata inspection may be
performed symmetrically if required, but are not necessary for the first pair.
Count every actual action entry from process startup, including inspection and
refusal, separately from admitted requests. Preserve the original seven-control
probe unchanged; its introspection remains valid for its own ABI question.

## Leaner first experiment (recommended, not a blocker)

The design's information dispatcher recorder covers roughly twenty selector
families across root/Group/child paths, plus entry/return kinds and two 512-row
buffers (`:129-147`). That is bounded and appropriately content-free, but is
not required to answer the first question: **does the same client still obtain
C0=0 and P0=-25205 after minimal preparation?** Broad instrumentation also
perturbs the transport being studied and costs substantially more than the
minimal discriminator.

Start with exactly the proposed two fresh sessions and:

1. A frozen shared exact-PID client, bounded discovery, one action-name read,
   one action attempt, and persisted begin/end/error facts.
2. Ready/finish markers and strict owned-menu cleanup, with no action warmup.
3. Complete action entry/refusal/return observation, main-thread/current-root/
   attachment/frame/installing facts and admitted/open/close counters. Use
   fixed preallocated numeric fields; export outside callbacks.
4. The already specified single post-reply bounded identity/parent audit.

Leave `server_calls` empty and mark dispatcher observation unavailable in this
first stage rather than fabricate completeness. If C0 succeeds/P0 reproduces
-25205 with admitted/current root, record that result and add the fixed
selector-history recorder only for the next discriminator. This preserves the
first pair's negative inference about client language/original preparation;
it deliberately cannot make selector-absence or request-order claims.
The design's final patch threshold remains unmet until the needed second-stage
facts and falsifying intervention are available. Do not relax that threshold
merely because the first instrumented baseline ran.

## Other assessed risks and supported limits

| Area | Assessment / required interpretation |
| --- | --- |
| One action | No external retry/navigation/menu press in the design. Marker-driven cancellation is owner cleanup, not another AX action. The metadata warmup above is the missing explicit exclusion. |
| Cleanup timing | Shared finish-after-reply removes the prior asymmetric 150 ms cancellation. It also removes a behavior of the successful old control. If AX transport waits on menu tracking, finish cannot arrive; the outer watchdog must yield unknown/forced, not imply a product cause. The design already says this. |
| Ready marker | Feasible using existing main-thread state and `TryAccessibilityMenu` conditions. Evaluate readiness without calling the action or reserving queue admission. Ready is a baseline, not a guarantee that attachment/frame remain unchanged. |
| Read history | Different discovery graph lengths remain unavoidable and are explicitly acknowledged. C0/P0 eliminates the original 25-check bundle as a necessary condition only if P0 reproduces the same failure; it does not prove all preparatory framework reads irrelevant. |
| Identity | Post-reply CFEqual and parent checks are observations at sampled times. They cannot certify transport-time lifetime or internal managed identity. Unknown on failed rediscovery is correct. |
| Negative inference | An absent action entry needs complete entry accounting including off-main/refusal paths. Missing inherited selectors cannot be ruled out by recording only implemented overrides. Preserve prefix-discarded and overflow facts; a truncated trace cannot support full-history absence. |
| Privacy | Fixed IDs, enums, equality flags, counts, no strings/content/hashes/pointers/coordinates, and no source traversal are appropriate. Recording Boolean foreign identity or arbitrary exception text is not part of this experiment. |
| Bounds | 55 s client deadline plus 75 s outer watchdog is coherent; cooperative AX bounds are not hard process bounds. Unknown on post-audit exhaustion is correct. Explicit count-before-copy and overflow tests still need implementation validation. |
| Native AOT | Existing byte-returning Cdecl `UnmanagedCallersOnly` root IMP and fixed class registration provide a viable seam. Avoid new reflection-generated callbacks, closures exported as IMPs, boxing/JSON in callbacks, or mutable-owner lookup off main. Native C control success does not validate the managed callback bridge. |
| Acceptance | Neither control completion nor a new instrumented success replaces the original 41-check gate on both RIDs. No VoiceOver or real-user input acceptance follows from this pair. |

## Official external contracts checked

Apple's [accessibilityPerformShowMenu](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol/accessibilityperformshowmenu())
documents the Boolean as whether the action was triggered, not whether the
subsequent action completed successfully. Thus next-turn admission is not
intrinsically contradicted by that return contract; the external -25205 remains
the failure under investigation.

Apple's [NSMenu.cancelTracking](https://developer.apple.com/documentation/appkit/nsmenu/canceltracking())
documents menu dismissal and termination of tracking. This supports cancellation
of the specific owned menu, but does not promise that an external AX call must
return before tracking begins, or that finish-file polling runs in a tracking
loop. Those scheduling properties need the bounded experiment's actual facts.

Both official Markdown endpoints were retrieved on 2026-10-01. Existing
failure-preserving reduction guidance in the design is appropriately scoped:
stateful AppKit history must not be treated as monotonic delta debugging.

## Review limits

This is a source/design/raw-artifact review on Windows. The new shared client,
ready/finish seams and recorder do not yet exist, and no macOS execution,
typecheck, observer-overhead measurement or marker-path adversarial test was
performed for them. Implementation must still prove its fixed bounds, complete
entry recording, one total action entry, owned normal cleanup and absence from
ordinary launches. No additional speculative product repair is justified.

## Hosted execution appendix — CI 36818175897

Date: 2026-10-01. This appendix supersedes the earlier statement that the lean
client/owner seams do not yet exist; the initial design assessment remains a
historical review, not an assertion about today's implementation.

**Independent verdict:** the schema-only repair now admits all four raw
session reports, and the lean shared-client comparison completed normally on
both Mac RIDs: **C0 AX=0 / P0 AX=-25205**. No substantive schema/evidence
inconsistency was found in the inspected artifacts. This is a reproduced
external-reply difference with matched admitted menu effects, not an identified
product root cause or a passed product accessibility gate.

### Provenance and method

Run [36818175897](https://github.com/kleedaisuki/mote/actions/runs/36818175897)
completed with overall success at commit
`1171d0f9b3c02c14c0d6e57b6a7f6d8d2fd3246d`. Inspected raw downloads are under
`.cache/ci-36818175897-grid-pair/`: per-RID owner inventory, separate C0/P0
`client.json` and `server.json`, original external Grid references, and native
job logs. Both jobs record portable guards `checks=37` and the actual first-pair
terminal output `completed-failure-observed`.

The reviewer loaded only `Assert-Keys`, `Assert-Integer`, `Assert-Boolean`,
`Assert-ClientReport`, and `Assert-ServerReport` from the PowerShell AST of
`tests/MacGridShowMenuDiscriminator/Run.ps1`, without executing its native owner.
Its SHA-256 matches both hosted owner reports:
`EC842CF260BD91034B5D37DF79E4161D4C1A0B24AF204840B131E67F3B22C872`.
All four independent raw client/server pairs pass these strict parsers, and
JSON-normalized raw objects exactly equal their embedded owner copies. This
local verification parses preserved evidence; it does not rerun native AX.

Owner-report SHA-256 values:

- x64: `0BFFE4059BBCC3B5BADF8AB62F00045DF32F95E5C49829B67BC92751B4DF272E`.
- ARM64: `934E1D14510D70211CD19369CCF5B724BDC724B1E2A1C9DAADCCDC1DF96B8545`.

### Four-session result

| RID / session | Original AX reply | Client / target exit | Action attempts / callback entries | Requests / dispatches / opens / closes | Post-reply identity audit |
| --- | ---: | --- | --- | --- | --- |
| x64 C0 | 0 | 0 / 0, normal | 1 / 1 | 1 / 1 / 1 / 1 | 128 admissions, not exhausted; sampled identity/parent checks agree |
| x64 P0 | -25205 | 0 / 0, normal | 1 / 1 | 1 / 1 / 1 / 1 | 128 admissions, exhausted; all identity conclusions null |
| ARM64 C0 | 0 | 0 / 0, normal | 1 / 1 | 1 / 1 / 1 / 1 | 107 admissions, not exhausted; sampled identity/parent checks agree |
| ARM64 P0 | -25205 | 0 / 0, normal | 1 / 1 | 1 / 1 / 1 / 1 | 128 admissions, exhausted; all identity conclusions null |

All four sessions report trust, exact target ownership, ready observed, one
advertised action with action-name AX error 0, action begin/end recorded,
finish-after-reply and finish-after-audit, finish consumed, normal server
shutdown, unchanged fixture and no forced cleanup. Session/owner error strings
are empty. Each client-call list contains exactly one `show-menu` row matching
its preserved original reply.

Every action-entry record is on-main with owner lookup/current-root/attachment/
frame/ready-baseline/generation/epoch flags true, installing false, queue result
and returned Boolean true, and caught exception false. Off-main entries and
entry/lifetime overflows are zero. Queue, dispatch, open and close retain the
baseline/current attachment facts; the sole detach records false flags during
normal teardown. Both targets therefore actually admitted and opened/closed
one menu. These facts do not explain why the external replies differ.

Both native compilation/typechecks succeed, source files remain unchanged and
product inventories contain strictly one file. Per RID, the wrapper freezes
one client binary used by both sessions; cross-RID binaries are not identical
and are not claimed to be. Fixture SHA-256 on all sessions is
`8BACC6F97A374853C5F04EE176384738D8BFF7F71B1C7E768D088310C98DD8C2`
(263,760 UTF-16 units, 1,100 records, at most 24 columns), also matching the
original external gate fixture. `Control.m:160-191` initializes the runtime-B
control without the old `Metadata()`/NSInvocation action warmup; one recorded
entry is therefore no longer an inspection-suppressed admission count.

### Unknown identity and green-job masking

The product post-reply finder consumes its complete 128-admission ceiling on
both RIDs before completing the identity audit. `Client.m:56,134-168` preserves
budget exhaustion and null conclusions, and the strict parser accepts those
as unknown. Null means **unobserved**, not false, stale, or equal. Product
sampled native-wrapper identity, reciprocal parent graph and parent-window
reachability remain unresolved. The product's server current-root/epoch flags
are narrower facts and cannot substitute for the external CFEqual/graph audit.
Control x64 completes exactly at the ceiling without attempting another
admission; `audit_admissions=128` alone is not exhaustion.

Dispatcher observation is explicitly unavailable (`false`, `server_calls=[]`)
in all sessions. The first stage makes no getter-order or missing-selector
inference. It also ran only C0 then P0 once per RID, not reversed launch-order
confirmation or a reliability distribution.

Both unchanged original external Grid reports still say **failed**, Swift
exit 1, **40/41** checks true, sole false predicate `context-menu-accessible`
with AX -25205. Both report normal editor exit, no forced cleanup, unchanged
input and empty cleanup/general error strings. The jobs are green because
these diagnostics are non-gating; moreover the first-pair owner deliberately
returns exit 0 for a fully observed failing reply (`Run.ps1:296-317`). Green
therefore means the bounded comparison completed, not product AX success.

### Supported conclusion and next-step boundary

The same native client/action function reproduces the product failure after
minimal preparation while its single runtime-B control succeeds on both RIDs.
Thus Swift-client language and the original selection-setter preparation bundle
are **not necessary conditions** for this observed product failure. Different
bounded discovery graph lengths and unobserved AppKit reads remain; no exact
transport mechanism, Native AOT bridge defect or graph/lifetime defect has been
isolated. Preserve the failure and unknown graph identity rather than guessing
a product patch. The original 41-check gate remains the acceptance obligation.

## Next-experiment design review — truthful ShownMenu override

Date: 2026-10-01. Reviewed architectural revision `9c4bbb2`, specifically
`docs/architecture/mac-grid-showmenu-discriminator.md:189-301`, against the
current `Control.m:48-100,160-191` and product
`MacCsvGrid.Accessibility.cs:224-257,347-361,651-664`. No previous raw audit was
rerun. **Design is suitable to preserve for later; no implementation or CI run
is authorized by this review.** The integration owner has reprioritized
end-to-end telemetry/provenance work.

### Verdict: meaningful narrow factor, not a product-forwarding reproduction

**No blocking design defect found.** C-base versus C-shown is a legitimate
small sufficiency experiment provided the implementation keeps everything
except the explicit getter unchanged. The intervention is the complete
representation choice: an explicitly exposed getter with truthful dynamic
return behavior. It is not a pure test of method presence independently of
return values, and the proposal correctly says so (`:232-236`).

The control already owns the actual popup strongly before display, updates
`opens` and `closes` through its existing exact-menu delegate, and clears
`ownedMenu` after popup return. An object-returning runtime IMP using
`opens > closes && attached` can return the exact owned menu during that
observed interval and nil otherwise without adding a setter, a second native
view, queue behavior or another action. Keep the same runtime superclass and
class initialization path; condition only the `class_addMethod` operation.
Do not create different subclasses with different inherited metadata.

Adding the getter without setter publication or NSTableView forwarding tests
a useful but deliberately narrower question: **is the truthful explicit
relation representation alone sufficient to change the original AX reply in
the successful native control?** A positive result would locate a native
representation interaction worth reducing. Both replies remaining zero would
reject sufficiency in this control, not eliminate product forwarding, setter
notifications/storage, frame/graph interactions or the managed callback.
Those distinctions are explicit in `:273-283`; no product root cause follows.

### Official-contract check

Apple documents [accessibilityShownMenu](https://developer.apple.com/documentation/appkit/nsaccessibility-c.protocol/accessibilityshownmenu)
as a nullable relation to the menu currently displayed for the element.
Apple's [NSAccessibilityProtocol overview](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol)
explicitly supports overriding a dynamic informational getter and calculating
its current value on demand; setter override is not a prerequisite for exposing
a getter. Both official Markdown endpoints were checked on 2026-10-01.
Consequently omitting a new setter is not a contract defect in this experiment.
Neither source specifies a dependency of AXShowMenu success on this relation,
so the experiment remains a hypothesis test rather than a contract repair.

### One material interpretation constraint; cheaper first control

The optional 16-sample observer (`:244-252`) exists only in the treatment
getter, since instrumenting the inherited C-base getter by overriding it would
remove the baseline being tested. If enabled, the exact treatment therefore
includes those bounded observation writes as well as the getter. Do not turn
a divergent **instrumented** outcome into a claim about an uninstrumented
getter alone. This is not a reason to reject the comparison; it is a constraint
on the causal label of its positive result.

The cheapest clean first pair is to **omit getter counters initially**, retain
the existing action/lifetime facts and compare the original replies. That
preserves the single representation intervention and needs no new narrow
artifact schema. It cannot tell whether AppKit invoked the getter, but entry
observation is not required to determine whether the representation treatment
changes AX reply. If call-entry information is needed, a separately labelled
bounded observer variant can follow. Alternatively retain the proposed
observer and call the intervention an instrumented getter; require a fresh
matched observation-disabled pair before attributing divergence to the plain
representation. Never add a pre-action getter read to obtain convenient facts.

### Preserved boundaries and implementation checks

- Same frozen client and same ready/finish lifecycle, fresh target/client
  processes, one action, no direct getter/action warmup, no menu traversal.
  The proposal does not silently add an external action or a synthetic event.
- Real nil/owned-menu/nil state must come from the existing delegate transitions,
  not `ownedMenu != nil` alone: that property becomes non-nil before popup open
  and may remain non-nil briefly after close. Keep cancellation/teardown intact.
- If a getter ledger is implemented, off-main refusal must use safe atomic or
  otherwise thread-safe fixed accounting without accessing mutable menu facts;
  unknown/unobserved inherited C-base calls must not be reported as zero.
- C-base must reproduce AX0 before interpreting any treatment failure. Only the
  same -25205 with one admitted/current action and matching actual open/close
  is the target failure; another error, extra entry or forced cleanup is unknown.
- Reverse launch-order confirmation after divergence is appropriate; one
  observation in each order still is not a reliability distribution.
- Do not bundle the proposed retained-parent-before-rediscovery audit reorder
  (`:289-296`) into this experiment. Product external identity remains unknown.
- No outcome licenses suppressing the product's live-menu relation. Preserve
  truthful semantics and the unchanged original 41-check acceptance gate.

This review recommends no additional broad recorder, native-host bundle or
product patch. The design can be revisited when the higher-priority tracing
infrastructure work has completed.
