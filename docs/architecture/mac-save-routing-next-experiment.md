# Mac Save routing: next discriminating experiment

Date: 2026-10-01. Status: design, **not implemented or target-run**. Scope: the
ordinary single-file JSON GUI path on macOS x64 and ARM64; no production retry,
activation change, new Save API, native sidecar, or acceptance relaxation.

## Decision and evidence boundary

Run a small, same-host, same-published-binary comparison of two **independent
fresh-process** Save routes:

- **K:** the existing single attempted `CGEvent.postToPid` Command-S down/up pair.
- **M:** one `AXUIElementPerformAction(..., kAXPressAction)` on the target
  application's existing, uniquely identified **File -> Save** leaf.

Keep the original one-edit, one-Save, exact-byte/normal-exit/trace/fresh-reopen
oracle for each process. Retain the existing original-only selector/admission
witness identically in both routes, label every sample diagnostic-on, and leave
the ordinary pilot's results separate. M must never rescue a failed K process.

**Unopened-menu feasibility is an explicit prerequisite, not a proven fact.**
The repository's `MacEditorShell.CreateMenu` creates an AppKit File submenu with
`Save` / key equivalent `s` / explicit delegate target `moteSave:`.
`MacClient.swift` currently observes only the owned window/source graph. Neither
its raw reports nor the relevant validation documents establish that this
host's unopened `AXMenuBar` exposes the Save leaf. A first small fresh-process
pair must establish that fact before expanding the matrix. Failure to discover
that leaf is **route unavailable**, not product Save failure. Do not press File,
open menus, switch to global menu lookup, or fall back to K in the same process.

Why now: [CI 36818175897](https://github.com/kleedaisuki/mote/actions/runs/36818175897)
at `1171d0f9b3c02c14c0d6e57b6a7f6d8d2fd3246d` retains three of four Mac exact
workflow passes. Their original streams are
`ready -> selector_entered -> controller_admitted -> completed`. X64 / 1 MiB
times out with original disk bytes, forced cleanup and **ready only**. Its
transport is censored; ready is not a post-action drain watermark and absence
is not callback-nonexecution proof. Prior timeouts occurred at different sizes
and on ARM64 too. A larger/smaller-file or ARM-only explanation is unsupported.
See the audited [witness boundary](../validation/mac-json-save-witness-implementation.md#first-actual-hosted-witness-collection-36818175897),
[routing observations](../validation/mac-json-save-routing-discriminator.md),
[independent outcomes](../validation/native-json-large-ci.md#follow-up-viable-original-only-witness-failed-small-case-stays-censored)
and [release gaps](../release-gaps.md).

## What the intervention separates

```text
K: external PID-specific Quartz post -> event/key-equivalent routing --+
                                                                     |
M: external PID-specific AXPress -> native menu-item action ------------+-> moteSave:
                                                                         -> composition settlement
                                                                         -> controller admission
                                                                         -> asynchronous Save / clean
                                                                         -> exact bytes / normal close
                                                                         -> fresh-process reopen
```

The routes converge at the same existing selector; they differ in delivery and
native dispatch mechanisms. AXPress still involves AX messaging, menu validation
and native application processing. Thus an M pass/K failure does **not** isolate
Quartz transport from key-equivalent processing, prove an ordinary physical
shortcut is broken, or prove a permanent harness correction.

| Hypothesis | Discriminating evidence | Limit |
| --- | --- | --- |
| H1: the K delivery/key-equivalent route is a material failure boundary | Repeated order-balanced K timeout/M exact success under comparable guards, with M selector/admission positives | Fresh processes are not identical counterfactuals; cannot separate delivery from key equivalent or eliminate environment timing |
| H2: failure is downstream of the shared selector | A failing process retains positive selector/admission, then no accepted disk result | Selector alone still leaves composition/admission open; admission alone still leaves worker/I/O/result publication open |
| H3: the environment/native dispatch is intermittently stalled more generally | Both routes fail to certify outcomes, possibly with bounded AX failures or unavailable routes | Censored missing markers cannot establish shared nonexecution or an AppKit hang |
| H4: AX route is not externally available on this graph | Exact-PID unopened menu lookup cannot yield one supported, enabled Save leaf | Feasibility failure, not negative evidence about K or the Save engine |

Target active/frontmost/main/focused facts remain read-only metadata and are
sequential, not atomic. Preserve nullable values; do not gate on newly invented
`window AXFocused=true` requirements, since successful existing cases also report
false. No global activation or permission manipulation makes a comparison pass.

## Bounded unopened-menu lookup and one-action contract

Extend the exact-process Swift client, not the product, for M. Resolve from
`AXUIElementCreateApplication(original_child_pid)` and **only that object's
AXMenuBar**, never a system-wide AX element or the currently frontmost app.

1. Verify the retained subprocess is alive and the app element's PID matches it.
   Keep all existing source readiness, Complete v1, dirty state, exact length,
   first-responder and selection `10:0` guards. Recheck them immediately before
   either route; no retained readiness alone authorizes the action.
2. Traverse only the fixed app menu-bar -> File menu-item -> menu -> Save
   menu-item path. Accept an intervening `AXMenu` container, not arbitrary graph
   search. Validate CF types and PID on every element. Require one File match
   and one exact Save match; `Save As...`/`Save As…` never match. Assert the known
   native roles. A source commit showing labels is not a runtime match proof.
3. Bound traversal to depth 4 below the menu bar, 48 visited elements, at most
   16 children per container and 16 action names. Count before copying arrays;
   reject over-bound/unknown counts, type errors, foreign/stale elements,
   ambiguity, disabled items and unsupported Press. Inspect only static menu
   titles, discard them after matching and persist enums/counts/Booleans/errors.
   Validate strings at 64 UTF-16 units after API return. This validation does
   **not** pretend the AX string-return API bounds the allocation itself.
4. Use the current 150 ms element-local AX messaging timeout on every traversed
   element. Bound the whole lookup transaction to 5 s with the parent client
   process timeout. No read-loop/menu-opening action seeks hidden descendants.
   Record an explicit path stage for missing/not-implemented/count/timeout/
   ambiguous/disabled cases. These are route prerequisites, not Save attempts.
5. Immediately before Press, recheck exact PID, role, identity path, enabled and
   supported action plus unchanged source guards. Set that element's timeout to
   the same 150 ms. Increment/retain the parent attempt state **before** making
   the one call. A client crash/timeout after attempted entry leaves action
   outcome unknown; it never authorizes a repeat or another route.
6. Retain the raw AX error and call-return elapsed time. After any completed
   action call, including an error, continue the unchanged bounded outcome
   observation while the original child is alive. A lost helper return is
   separately unknown and may still be followed by read-only observations; no
   other action is dispatched. Do not read the working file during Save polling.

`AXUIElementPerformAction` requests an action. Apple explicitly notes that
`kAXErrorCannotComplete` may occur even though the action did not fail, for
example when action processing exceeds a messaging timeout. Therefore an AX
return is **not** the persistence oracle and timeout must not cause a retry.
Even `.success` is an AX action acknowledgement, not `moteSave:` receipt,
controller admission, successful I/O or exact bytes.
([Apple API contract](https://developer.apple.com/documentation/applicationservices/1462091-axuielementperformaction?language=objc))

This approach has production precedent: Hammerspoon resolves menu paths from an
application's AXMenuBar and invokes AXPress on the leaf. It supports choosing
this inexpensive route over a new IPC endpoint, but does not certify mote's
unopened hosted graph or byte correctness. Our bounds and ambiguity refusal are
intentionally stricter than that general-purpose automation.
([Hammerspoon implementation](https://github.com/Hammerspoon/hammerspoon/blob/master/extensions/application/libapplication.m),
consulted 2026-10-01, `_findmenuitembypath` and `application_selectmenuitem`).

## Representative session and invariants

Each arm gets its own `working.json`, private home/cache/traces and fresh
subprocess under repository `.temp/`. Fixture and executable hashes are pinned;
the fixture is immutable. Run arms serially on each runner, with no concurrent
GUI diagnostic competing for the desktop.

```text
launch -> original-only witness attachment -> exact source v0 / Complete
       -> one existing edit -> exact source v1 / Complete / dirty
       -> unopened-menu metadata lookup (both arms, equal diagnostic preparation)
       -> recheck existing source guards -> one K pair OR one M Press
       -> original clean acknowledgement -> streaming full-byte hash
       -> owned normal close / original terminal trace and witness finalization
       -> fresh GUI read-only reopen (witness switch stripped) / exact source
       -> owned normal close / reopen trace -> immutable-fixture/binary checks
```

Equal lookup preparation reduces a lookup-delay confound between arms, but
changes K instrumentation relative to the ordinary pilot. Label it a **paired
diagnostic K**, never silently replace the ordinary K baseline or pool timings.
Store eligibility/results for all planned arms, including lookup failures; never
drop failures from the denominator or select only a convenient passing pair.
K can retain its ordinary one-shot result if M is unavailable, but that pair is
not a feasible K/M causal comparison and cannot count as an M failure.

Preserve the existing post-command 60 s clean-acknowledgement deadline and
per-phase guards. Record external call overhead separately; do not extend the
deadline on an AX error. Exact acceptance still requires the independent edited
byte oracle, normal original and reopen exits and valid original/reopen traces.
Witness health and selector/admission facts are separate report dimensions, not
substitutes. An M exact success with AX error is possible and must be reported
as both facts, not normalized to a success return.

On failure retain the final bounded source/window facts, pre/post-action guard
reports, disk hash **after** the outcome wait, raw witness frames, cleanup cause
and original process identity. Reuse the existing single owned-close cleanup
request and bounded forced cleanup. Closing a dirty window may create a prompt;
never press its Save, Discard or Cancel button to rescue the workload. A forced
kill censors unreceived stages despite EOF or an initially live writer.

## Sample plan and spending limit

This is a discriminating pilot, **not a failure-rate estimate, p95 benchmark or
release reliability gate**. Predeclare order and total budget before launch:

| Per RID | Pair 1 | Pair 2 | Fresh original processes |
| --- | --- | --- | ---: |
| x64 / 1 MiB | K then M (feasibility pair) | M then K | 4 |
| x64 / 100 MiB | M then K | K then M | 4 |
| ARM64 / 1 MiB | M then K (feasibility pair) | K then M | 4 |
| ARM64 / 100 MiB | K then M | M then K | 4 |

Maximum: **16 original processes**, and at most 16 read-only reopen processes
for accepted original Saves, on two runners. No workflow re-run-until-green.
First execute one 1 MiB feasibility pair per RID. If a RID cannot establish the
unopened leaf without an action, stop its additional paired allocation and
retain route-unavailable evidence; do not consume large-file repetitions trying
to make a fundamentally unavailable route appear. Do not stop/repeat according
to whether the first eligible Save happened to pass. A child crash or wrong
bytes stops further actions on that child; evidence collection/cleanup remains
bounded and independent planned fresh arms may continue if ownership is sound.

Use a 6 min whole-arm cap (original plus reopen, including its existing phase
deadlines), 5 s client-invocation cap, and a 55 min workflow-step cap per RID;
outer expiry is a censored budget outcome, not a Save timeout with a fabricated
full 60 s observation. These are proposed safety caps, not measured runtime
predictions; verify phase/cleanup budgets against the current driver before
integration. Compile once per RID and reuse the same Swift client and executable.
This costs more than one existing two-size pilot but buys a controlled bypass
of a disputed boundary, instead of another unconstrained repeat of the same
ambiguous timeout. Avoid Windows repeats for this Mac-only question.

## Outcomes and the next decision

| Observed result | Conclusion permitted | Next action |
| --- | --- | --- |
| Unopened Save leaf unavailable/ambiguous/disabled | M feasibility not established | Stop that RID's expansion; record exact bounded path errors. Review a separate target-owned native menu-selector control, not a fallback in K |
| K fails / M exact-passes with positive selector/admission in both order directions | Stronger evidence for route-specific failure before/shared-selector entry; still not delivery-vs-key-equivalent isolation | Keep original K failure visible; design a positive event-receipt discriminator or physical-shortcut target-host test, not a product retry |
| K and M exact-pass | Both routes work in these samples; recurrence not captured | Do not declare repaired. Preserve experiment as finite evidence and choose further repetitions only with a justified reliability question |
| M returns success but exact outcome fails | Native AX acknowledgement did not certify Save | Use positive selector/admission to choose composition/admission vs worker/I/O boundary; do not blame AX or engine from absence |
| M returns cannotComplete but exact workflow passes | Messaging timeout and successful persistence coexist | Report both; no retry, no changed timeout needed for the outcome oracle |
| Both fail with positive selector/admission | Downstream failure boundary is observable | Design bounded worker-entry/result/completion evidence, preserving original outcome oracle |
| Both fail with ready-only censored streams | Boundary remains unresolved | Do not infer no receipt/handler. Consider separately reviewed target-local witness, with its own blind spots and loss accounting |
| Mixed route-dependent outcomes in only one order or one pair | Timing/order/environment explanation remains plausible | Retain all arms; no architectural patch or distribution claim from the small sample |

Fresh-process pairing cannot remove differing run-loop state, process startup,
AX traffic or UI timing. Order balance controls a simple temporal trend, not
all environment variation. The value is a changed path with the **same exact
outcome contract**, not a statistical guarantee from four cases per route/RID.

## Optional target-local event receipt witness: feasible API, deferred bridge

Apple's `NSEvent.addLocalMonitorForEvents(matching:handler:)` observes events
dispatched through the application's `sendEvent:` and allows returning the
**same event object** unchanged. It does not require installing a global event
tap or inspecting foreign apps. Apple also states that nested tracking loops,
including menu/control tracking, can consume events without this monitor's
handler. Consequently a received Command-S-matching marker is a positive local
dispatch-boundary fact; no marker is never universal non-delivery proof.
([local-monitor contract](https://developer.apple.com/documentation/appkit/nsevent/addlocalmonitorforevents(matching:handler:)),
[Apple lifecycle/threading guide](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/EventOverview/MonitoringEvents/MonitoringEvents.html))

Chromium uses this production pattern, returns the event unchanged and removes
the monitor on teardown; its window-specific filtering/weak lifetime handling
are relevant precedents, not evidence about mote's synthetic shortcut.
([Chromium EventMonitorMac](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/ui/views/event_monitor_mac.mm),
consulted 2026-10-01.)

Do **not** include it in the first K/M comparison. The current `ObjC.cs` bridge
has no Objective-C block construction/ownership facility. A C# unmanaged method
pointer is not an Objective-C block; correct copied-block ABI, retained callback
lifetime and teardown on x64/ARM64 would require independently validated
interop. A new Swift/ObjC sidecar violates strict one-binary delivery; swizzling
`sendEvent:` or changing the NSApplication singleton class adds much greater
global process risk than this experiment warrants.

If K/M makes this witness worth its cost, a separate opt-in design must:

- Install only in the original diagnostic child, before input, on the UI owner;
  emit `monitor_installed` only after a nonnull monitor is obtained, and remove
  it on the owner thread before producer lifetime closure. Runtime-control,
  default and reopen must remain unchanged.
- Inspect only type/keyCode/modifier bits/window identity for the controlled
  Save key; never `characters`, source text, arbitrary keystrokes or titles.
  Always return the exact incoming object. Emit only a fixed enum through the
  current nonblocking diagnostic channel; no callback clock, string, disk,
  allocation-heavy conversion or background task.
- Label an untagged match **matching local event**, not proof of the specific
  posted pair. A deliberately tagged `CGEvent` could improve attribution only
  after cross-RID controls prove the tag survives Quartz-to-NSEvent conversion;
  do not assert that propagation without testing or expose arbitrary metadata.
- Validate observation positive controls, monitor lifetime, nested-loop blind
  spots, original-only isolation and transport overflow/forced-kill censorship.
  Even healthy stream completion would not extend the monitor's API coverage.
- Avoid TCC prompts/changes, Accessibility database writes, global monitors or
  hooks; preserve the unchanged one-shot Command-S route and exact-byte oracle.
  Treat enabled measurements as diagnostic-on, not performance samples.

The immediate route comparison needs no product hook beyond the already-reviewed
selector/admission witness, so it is cheaper and less perturbing.

## Rejected shortcuts and concrete implementation handoff

- No K->M retry, repeated Save key, artificially activated/key window, global
  AppleScript/System Events command, global menu press, broader trust grant or
  longer acceptance deadline. These change the investigated condition and can
  duplicate a non-idempotent operation after an ambiguous return.
- No direct cross-process `objc_msgSend` to `moteSave:`: pointers and selectors
  belong to the target address space. Existing `ProbeInvokeMenu` is in-process
  delegate dispatch; it is a narrower causal control, not external GUI routing.
- If AX feasibility fails, consider a separately reviewed opt-in UI-owner
  invocation of the *existing menu item's* `performActionForItem(at:)`, with
  exact identity/guards and one call, rather than exposing a new production IPC
  Save endpoint. Apple documents this action's target dispatch and notes that
  it does not trigger validation automatically. Any such control must preserve
  explicit enabled/validation checks and be labeled in-process selector control,
  not keyboard/AX Save acceptance.
  ([Apple NSMenu contract](https://developer.apple.com/documentation/appkit/nsmenu/performactionforitem(at:)))

Implementation, if approved, should be one bounded client route enum and a
separate comparison entry point/report; avoid changing existing `sample`/ordinary
pilot defaults or composing two modifying methods into a fallback. Coordinate
client report schema/parent parser/README/CI source-hash pins as one contract.
Portable tests must establish: exact Save vs Save As and duplicate refusal,
foreign/stale PID refusal, bounded path/array/type handling, no menu-opening
calls, attempt-before-call accounting, raw AX-error retention, no repeat after
cannotComplete/helper timeout, fresh homes/working files per route, both route
orders, unchanged byte/trace predicates, original-only witness isolation and
failure cleanup against the retained original child. Hosted feasibility then
establishes actual AppKit graph/action behavior on both RIDs; portable mocks do
not claim it. An independent artifact audit must check nested step outcomes and
all planned-arm denominators, not green `continue-on-error` job summaries.

Methodological connection: Pivot Tracing demonstrates why cross-boundary causal
observations are more informative than unrelated component logs. We borrow the
principle of identifying the last **positive** boundary and keeping association
explicit; its distributed Java tracing mechanisms/overhead results are not
adopted or transferred to this GUI. Here single original-child association and
one controlled Save permit a much smaller fixed-stage protocol.
([Mace, Roelke and Fonseca, SOSP 2015](https://jonathanmace.github.io/papers/mace2015pivot.pdf))
