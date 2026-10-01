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
