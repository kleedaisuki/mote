# Native Save input provenance: the next permanent boundary

Date: 2026-10-01. Status: **design only; no implementation or experiment**.
Owner: architecture. Scope: macOS target-owned Save-family input boundaries,
not an input recorder, command framework, or change to Save routing.

## Decision

First add a transparent, opt-in override of `performKeyEquivalent:` on
**mote's own main `NSMenu` instance**. This is the recommended next implementation
slice. Keep a public **local `NSEvent` key-down monitor** as a separately reviewed
second slice only if menu-only hosted evidence still leaves the important gap
unresolved; its new Block ABI is not a prerequisite for the menu boundary.
Keep the existing selector receipt as the first actual Save request. The monitor
recognizes only the shipped Command-S family and returns the identical event.
The menu override calls its superclass exactly once with the identical event and
returns the identical Boolean. Neither boundary initiates Save.

The menu-only slice adds useful native-route visibility using an established
interop mechanism; a retained menu positive already proves a target-owned
AppKit boundary saw an event. The later monitor can add an earlier independent
target-event checkpoint. Neither extends the existing **end-to-end causal
chain** across input routing. Missing
records still cannot establish nonexecution. Do not expand this into logging
all keyboard input or repairing a suspected routing defect.

The [hosted audit](../validation/causal-observability-hosted.md) of
[CI 36823606282](https://github.com/kleedaisuki/mote/actions/runs/36823606282),
source `3f3e59a816ec497e2605d4d417cb8b7339202601`, establishes 16/16 recovery
controls and six of eight ordinary native Save chains complete. The macOS
x64/100 MiB and ARM64/1 MiB timeouts retain readable forced-exit prefixes but
`requests=[]`. The audit retains raw traces and source/report identity; this
design does not perform another audit. No claim of callback absence, event loss,
or Save-engine root cause follows from those failed traces. The overall run
failed separate compatibility/pin gates; a follow-up repair run is not presumed
successful here.

## Why not an application subclass?

The current `MacEditorShell.PrefersDark` calls `NSApplicationLoad` and
`NSApplication.sharedApplication` before `Run`; `Run`, native probes, and Canvas
surfaces also use those APIs. Apple's
[NSApplicationLoad contract](https://developer.apple.com/documentation/appkit/nsapplicationload)
says it initializes the shared application if necessary. Therefore a subclass
created inside `Run` cannot simply replace this existing singleton.

Introducing a custom application principal class would require a coordinated
startup redesign before the first AppKit load, while `object_setClass` or global
method replacement would modify SDK-owned runtime state. Neither is justified
for this telemetry boundary. The supported local monitor works with the current
singleton, and the menu is already allocated and owned by mote.

| Option | Useful boundary | Cost/risk | Decision |
| --- | --- | --- | --- |
| Local key-down monitor | AppKit is dispatching an event to this application | New Block ABI bridge; enabled per-key callback; excludes nested tracking loops | Conditional second slice |
| Owned main-menu subclass | Main-menu key-equivalent method entered and returned | One existing runtime subclass pattern, unchanged superclass forwarding | Choose first, opt-in only |
| Owned window/view override | This responder received a route | Earlier responders or main-menu handling can bypass it | Insufficient as the first boundary |
| Application subclass | Application event/action dispatch | Current singleton is initialized earlier; broader startup change | Defer, not a drop-in |
| Global monitor/event tap | External/system event stream | Permissions, privacy, scope; not target receipt | Reject |
| Global swizzling / `isa` replacement | Arbitrary SDK-object calls | Alters SDK-owned dispatch/lifetime and observability itself | Reject |
| `NSMenuDelegate.menuHasKeyEquivalent` | Delegate consulted during matching | Outputs can influence target/action; not complete application receipt | Do not use for passive tracing |

Apple's [monitor API](https://developer.apple.com/documentation/appkit/nsevent/addlocalmonitorforevents(matching:handler:))
explicitly excludes events consumed by control/menu/window-drag tracking loops.
The [monitoring guide](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/EventOverview/MonitoringEvents/MonitoringEvents.html)
documents main-thread callbacks, unchanged-event return, and explicit removal.
The ordinary no-open-menu Save acceptance is the initial scope; physical typing,
modal tracking and AX menu activation are not made equivalent to it.
Apple's [NSMenu.performKeyEquivalent contract](https://developer.apple.com/documentation/appkit/nsmenu/performkeyequivalent(with:))
defines the action/Boolean boundary, not a guarantee that every Save route calls
mote's main-menu instance. A successful external Command-S must positively
demonstrate this owned method is actually used before claiming ordinary-route
coverage; a direct method probe establishes ABI only. If the platform bypasses
it, classify the boundary unavailable for that route rather than adding more
subclasses or forcing menu dispatch.

## Data model and causal ownership

```text
external Quartz post attempt                       [separate observer clock]
    :                                              [edge unknown]
local AppKit monitor                               [conditional second slice]
    native.input.save_family_candidate              [anchor E, session parent]
    :                                              [edge unknown]
mote main-menu performKeyEquivalent(event)
    native.menu.save_family.entered                 [anchor M, session parent]
    +-- super.performKeyEquivalent(same event)       [exactly once]
    +-- native.menu.save_family.returned_true/false  [child of M]
    :                                              [edge unknown]
existing moteSave: / moteSaveAs:
    command.save.received / command.save_as.received [R, unchanged session parent]
    +-- existing request -> Engine -> UI chain      [unchanged causal graph]
```

**Choose independent positive boundaries for this slice.** A monitor returns
before normal dispatch, so it has no lexical ownership of the later selector.
Do not keep a pending event pointer, retain NSEvent objects, install an ambient
Activity, or hold a producer lease across that boundary. A menu call owns only
its local mark and return record; it holds no telemetry state in the shell after
return. Nested calls naturally have distinct local marks.

`eventNumber` alone is not a documented globally unique keyboard-delivery ID;
timestamps, `NSApplication.currentEvent`, and nearest observed candidates are
not a causal handoff. Even a lexical menu frame plus a matching current-event
pointer leaves possible nested modal/AX processing with a stale outer event.
Do not attach a mouse/AX/direct selector to that frame and call it a proven key
origin. No explicit-parent request API change is needed. The existing request
graph and strict original-sink phase rules remain untouched.

This deliberately gives up total key-to-Save causality rather than invent it.
In a one-attempt fixture, a retained candidate is stronger evidence than only an
external post attempt, but association with that helper action is still a scoped
observer inference, not a persisted causal parent. The reader must report
candidate, menu and request evidence as three separate inventories, with their
cross-boundary edges unknown. The input/menu marks own no document and cannot
cancel Save.

### Candidate classification, not command interpretation

Install a key-down-only mask. Within that callback, first check the event type
and Command modifier; reject Control/Option combinations. For those candidates
only, inspect `charactersIgnoringModifiers` natively, require NSString length
one, and compare the single UTF-16 code unit to fixed `s`/`S`. Do not marshal a
managed string or persist characters, raw key codes, modifiers, window titles,
event timestamps or event addresses. A raw property access may have native
framework allocation cost; this design does not claim it bounds that cost.

Record a **Save-family candidate**, not Save versus Save As or a requested
operation. Shift, Caps Lock, keyboard layout and user remapping can complicate
matching; only the actual selector determines the request kind. Other layouts
or remapped keys may reach the selector without a candidate row, legitimately.
Apple documents that
[charactersIgnoringModifiers preserves Shift](https://developer.apple.com/documentation/appkit/nsevent/charactersignoringmodifiers)
and that [uppercase/Shift key-equivalent matching has exceptions](https://developer.apple.com/documentation/appkit/nsmenuitem/keyequivalentmodifiermask).
Do not implement a competing key-binding engine to make telemetry universal.

## Fixed vocabulary and truthful interpretation

Proposed operation strings are closed constants, not caller-provided strings:

| Row | Positive meaning | Does not certify |
| --- | --- | --- |
| `native.menu.observation.ready` | Owned instrumented main menu installed | Main menu is used by every Save route |
| `native.input.monitor.ready` | Monitor installed and owned instrumented main menu installed | Future input completeness or delivery |
| `native.input.monitor.unavailable` | Instrumentation setup could not complete | Editor command failure |
| `native.input.save_family_candidate` | Local handler observed a matching candidate | External helper delivery identity, physical keystroke, Save request |
| `native.menu.save_family.entered` | Owned main-menu method received a matching-family event | Menu item enabled/matched, selector invocation |
| `native.menu.save_family.returned_true` | Superclass returned true for this menu call | Which item it handled, successful Save |
| `native.menu.save_family.returned_false` | Superclass returned false for this menu call | Event globally ignored, no other responder handled it |
| Existing typed request receipt | Actual owned Save selector entered | Earlier route necessarily came from a physical shortcut |

All instantaneous positives use zero duration/success to denote the checkpoint,
not enclosing operation success. A false menu return is not a failed Save.
If duration is useful, emit a distinct child terminal under the entry anchor,
never reuse its SpanId. The ready/unavailable row belongs to the session, not a
fictional Save request. Input-only evidence must not appear as `requests=[]`
without a separate input-boundary inventory in the new reader summary.
Add a similarly fixed `native.menu.observation.unavailable` for the first slice;
do not emit monitor-unavailable when the monitor is intentionally not installed.
Current `BeginPhase` accepts only Save phases: implement an explicitly typed
current-session menu-boundary entry/terminal API (or equally narrow overload),
using the existing mark/original-sink machinery. Do not widen it to accept
arbitrary operation names or create an ambient Activity. Existing request
receipts and all Engine APIs are unchanged.

## Sequence, loss and schema-v1 compatibility

No new sequence or arbitrary attribute is required: anchor SpanId and exact
parent identity carry within-operation causality; file order and timestamps are not proof of an
edge. Existing v1 records/enums keep their values; append vocabulary without
renumbering old enum members. The typed `causal_save_trace_reader.py` tolerates
compatible future operations but cannot certify these new stages. This is not a
blanket property of repository readers: the generic NativeAcceptance reader has
a fixed operation vocabulary and previously required migration in `028af4a`.
Update such strict auditors intentionally with regression fixtures; do not
silently relax their allowlists. Add an optional **input-boundary summary**,
not new mandatory stages to the ordinary Engine/Save success contract.

Continue the bounded queue, original-sink rejection accounting, periodic prefix
flush, terminal session health and dropped-event reporting. There is no pending
event state or claim of a lossless event history. No
global sequence/watermark exists yet; ready or a later draw row is not an input
drain watermark. A forced-exit prefix containing an input positive but no menu
positive narrows the last *observed* boundary; it does not prove the menu did not
execute. Normal-exit/no-observed-drops evidence also does not cover paths outside
the monitor API, filtering, setup failure or correlation loss.

## Native ABI, lifecycle and default-off cost

For the menu-first slice, use the existing owned runtime-subclass/BOOL-supercall
approach; add no Block API or singleton initialization change. Candidate filtering
still executes in the enabled menu callback, but ordinary key-down events which
AppKit never routes to that menu do not gain a managed monitor callback.

For the conditional monitor slice, there is no existing Block bridge in this adapter. Use one **no-capture,
process-lifetime global native Block** with an `UnmanagedCallersOnly` invoke
function and stable unmanaged literal/descriptor/signature storage. Reference
the current single shell through `s_current`, not a movable managed capture or
GCHandle. The [Clang Block ABI specification](https://clang.llvm.org/docs/Block-ABI-Apple.html)
defines the global-block descriptor/signature and no-capture layout. Resolve
only the system Blocks runtime data symbol; introduce no copied native library,
dynamic application plugin, Objective-C source sidecar or `.app` dependency.
This is a real ABI integration task, not an ordinary delegate cast.

Initialize only when the existing trace configuration is enabled and healthy.
Create the owned instrumented main menu before any conditional monitor; emit
`native.menu.observation.ready` for menu-only setup and monitor-ready only when
both steps succeed. On setup failure preserve a normal NSMenu
and existing selector tracing; report unavailable if the sink still works.
Remove the monitor on the UI thread before native views/pool/shell are torn
down; retain no candidate events or ambient frames. Use `removeMonitor:` and the
documented token ownership, not an invented release of the non-owned token.
Keep the no-capture block storage process-lifetime so callbacks cannot reference
freed descriptor memory during teardown. It is a fixed one-time enabled cost.

Tracing off installs no monitor, uses today's NSMenu class, allocates no block
storage/candidate, and adds no managed callback to the per-key path. Tracing on
examines key-down metadata locally; only the Save family enqueues rows. There is
no producer-side disk access, flush, waiting or native event modification.
Every managed instrumentation callback contains exceptions at the unmanaged ABI;
telemetry failure never suppresses the identical-event return or the exactly-once
super dispatch. Objective-C exceptions are not assumed catchable by managed
`catch`; call documented getters only for their valid event types.

## Executable integration path and discriminating tests

1. Review the menu-first boundary/vocabulary contract before assigning writers. Keep
   Native Mac interop/input owner, Telemetry vocabulary/API owner, and independent
   reader/test owner separate; no global write lock.
2. Implement the owned-menu boundaries as production infrastructure, **not a
   new Save route experiment**. Run an owned in-process Native AOT callback
   probe on macOS x64 and ARM64. Verify once-only super forwarding, identical
   Boolean return, independent nested marks and normal teardown. The monitor's
   Block ABI/signature and identical-event/late-callback checks belong to its
   conditional second slice, not this first implementation.
3. Cover candidates/noncandidates, Save/As, Caps Lock/Shift ambiguity, direct
   selectors, menu-click/AX without key parents, no retained event pointers,
   nested calls, rejected stale sinks, setup failure and
   exception containment. Include schema-v1 unknown-operation compatibility and
   strict privacy fixtures. No event pointer or input text may reach JSONL.
4. Retain a normal one-attempt ordinary workflow on both Mac RIDs and both sizes,
   preserving exact bytes, dirty/clean acknowledgement, original normal exit
   and fresh reopen. Inspect raw traces and report all three evidence levels
   independently: menu route, actual request/commit, and input candidate only if
   the conditional monitor is implemented. No retry,
   AX rescue, activation change or strengthened focus precondition is added.
5. Verify a readable positive input/menu anchor survives the existing owned kill
   protocol without a fabricated terminal. Saturation/fault cases stay degraded
   or censored; do not convert them to negative route evidence.
6. Only if the menu-only evidence remains insufficient, review/implement the
   monitor as a separate slice, including its ABI controls before a hosted
   workload. Measure tracing-off/on cost for each actually implemented slice on
   the **same new Mac binary** with non-Save
   typing as well as Save. Current Windows overhead measurements do not certify
   a new AppKit callback. Zero warmed managed allocation for disabled paths and
   no disabled monitor are source/test invariants; enabled latency/CPU/native
   allocation require actual measurements.

If the global Block bridge cannot meet ABI/lifetime checks on either supported
RID, do **not** ship a partial observation as complete coverage or fall back to
global swizzling. Retain the already designed independent K/M route comparison
in [mac-save-routing-next-experiment.md](mac-save-routing-next-experiment.md),
first testing unopened-menu feasibility. That bounded alternate can separate
route outcomes but cannot certify target key receipt or replace this permanent
boundary. Do not run it as a rescue of a failed K process.

## Production and research grounding

Apple's public local-monitor and superclass menu contracts define what can be
observed without changing routing. Mote already uses owned Objective-C runtime
subclasses for editor/preview drawing and delegates; extend that established
mechanism rather than replacing application-wide methods. The shared singleton
constraint is concrete repository evidence, not an abstract preference.

[Dapper](https://research.google/pubs/dapper-a-large-scale-distributed-systems-tracing-infrastructure/)
motivates propagated causal identity and bounded overhead, not timestamp-based
inference of input delivery. The peer-reviewed NSDI 2026 study
[*Observability Is Eating Your Cores*](https://www.usenix.org/conference/nsdi26/presentation/cornacchia)
shows that telemetry itself has coverage/resource trade-offs; its cloud IPU
design is not appropriate here. The practical consequence is two informative
boundaries, default-off callbacks, bounded state and measured enabled cost.
Reuse the existing [observability architecture](observability-provenance.md),
not a parallel diagnostic business pipeline.
