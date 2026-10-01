# Runtime observability and causal provenance

Date: 2026-10-01. Status: **implemented Save causal contract with local
verification; final hosted four-RID acceptance pending**. This document separates
shipped instrumentation, retained evidence, and possible follow-up contracts.
Implemented does not mean every endpoint or transport failure is certified.
Scope: opt-in local tracing of mote's native single-file editor. The first
implementation slice is Save/Save As, recoverable trace prefixes, and truthful
outcomes; existing edit/analysis/source-draw tracing is reused, not replaced.

## Current implementation and verification entry point

The committed implementation is in
[`MoteTelemetry.Requests.cs`](../../src/Mote.Telemetry/MoteTelemetry.Requests.cs),
[`DocumentSaveObservation.cs`](../../src/Mote.Engine/DocumentSaveObservation.cs),
[`NativeEditorController.Save.cs`](../../src/Mote.Native/NativeEditorController.Save.cs),
and the two native shell dispatch sites. The single typed `SaveRequested` event
is migrated across production and fake/probe shells; this is no longer a proposed
API. The Engine's existing two-argument Save methods remain compatible with
additive explicit three-argument observer overloads.

The implemented chain is target callback receipt -> controller admission ->
worker entry -> Engine gate and exact captured snapshot -> actual staged-file
I/O and route-specific commit -> local UI post return -> guarded UI completion.
Its fixed receipt names are **`command.save.received`** and
**`command.save_as.received`**; the historical design's generic
`command.received` name is not emitted.
Entry rows retain their own identity, while terminal duration rows are distinct
children measured from the entry timestamp. A lost terminal does not erase the
positive meaning of a retained entry.

| Evidence | Retained result | Limit |
| --- | --- | --- |
| [Engine observer review](../reviews/engine-save-observer.md) | Exact captured version, outside-state-lock observations, unchanged persistence policy and contained nonfatal observer faults | Not a native UI certificate |
| [Telemetry request review](../reviews/telemetry-causal-requests.md) | Typed anchors, original-sink rejection, nonambient contexts, once-only request terminal | Bounded lossy transport, not total delivery |
| [Native final review](../reviews/native-causal-save-final-review.md) | Modal reentry/lifetime guards and asynchronous completion exception containment | Not proof of external OS input or every unrelated native callback |
| Local integration TRX `.cache/causal-integration-tests/causal-integration.trx` | **1329 executed / 1329 passed**, zero failed, aborted or not-executed | Baseline before completion-containment follow-up |
| Focused TRX `.cache/save-completion-containment/save-completion-containment.trx` | **4 executed / 4 passed**, zero failed, aborted or not-executed | New callback fault cases; not summed into a final full-suite certificate |
| [Ordinary Save reader review](../reviews/native-save-causal-acceptance-review.md) and [CI summary review](../reviews/causal-ci-evidence-review.md) | Route-aware phases, exact saved-version consistency, original-process association and conservative failure/censorship classification | Final hosted reports/raw traces still require audit |

`tests/causal_save_trace_reader.py` distinguishes the **native Save contract**
from the **recovery probe contract**. A held/receipt-only owned-process kill
validates recoverable prefix handling, not Engine persistence. Ordinary native
1/100 MiB acceptance retains exact bytes, clean acknowledgement, normal original
exit, fresh reopen and unchanged fixture requirements, additionally requiring
the native causal chain. A green non-gating Actions job is not that certificate.
Historical audits and failed hosted observations retain their original scope;
they are not rewritten as tests of these newly integrated APIs.

## 1. Decision and evidence motivating it

The implementation keeps `Mote.Telemetry`'s bounded channel, fixed-schema JSONL,
monotonic clock, and explicit marks, and adds two previously missing capabilities:

1. **One target-owned request identity** beginning at the native Save callback,
   carried through composition settlement, controller admission, worker
   scheduling, engine snapshot capture and Save phases, and UI completion.
2. **A live, recoverable readable prefix** maintained by the existing writer,
   with low-frequency entry/checkpoint records and periodic buffer flushing.
   Conservative watermarks remain an unimplemented follow-up, not a certificate.
   Retained entry records provide positive evidence without awaiting a terminal.

Do not add a database, exporter, generic command bus, global native input hook,
per-key file writes, or telemetry-owned business state. Tracing stays default
off, local only, under the configuration-resolved `~/.mote/traces` convention
with the existing path override. No startup network dependency or delivery
sidecar is introduced. Test artifacts remain in repository `.temp/` or `.cache/`.

The [independent audit](../reviews/observability-end-to-end-audit.md) establishes
real accepted-edit-to-analysis/source-draw coverage but missing command receipt,
admission, queued Save, snapshot version, linked completion, and failure-prefix
visibility. In [CI 36818175897](https://github.com/kleedaisuki/mote/actions/runs/36818175897),
three Mac workflows succeed with normal traces, while the x64 1 MiB timeout has
an **empty trace file** and a separately censored ready-only witness. This is
demonstrated loss of runtime evidence, not proof of a Save engine defect or
callback nonexecution. The [Mac routing experiment](mac-save-routing-next-experiment.md)
must not be used to paper over this missing infrastructure.

Three kinds of facts remain distinct:

| Evidence | Certifies | Does not certify |
| --- | --- | --- |
| External helper attempted `postToPid` or AX action | Helper entered that call, and optionally its reply | Target callback receipt or a durable Save |
| Target request/phase record | Target instrumented boundary executed | Uninstrumented earlier OS delivery, or correctness of saved bytes |
| Independent exact-byte oracle and fresh reopen | Observed output matched the scoped expected file | Every failure path, physical input, universal durability, or tracing completeness |

Likewise, existing draw submission records end at matching native source draw
return, not compositor presentation, scan-out, or physical pixels. Preserve
the [existing endpoint contract](../end-to-end-tracing.md).

## 2. Ownership and the representative Save workflow

```text
external action attempt                       [observer clock, not target parent]
    |
native owned Save callback
    +-- command.save.received OR command.save_as.received
         |                                    [request context's retained anchor]
         +-- composition settlement / blocked
         +-- controller guards, picker, recovery redirect
         +-- save.admitted                    [event, before Task.Run submission]
         +-- save.worker_started              [event, when delegate actually runs]
         +-- document.save.entered            [coarse worker phase anchor]
         |    +-- overwrite approval / declined
         |    +-- save.gate_wait.entered       [phase anchor; terminal is its child]
         |    +-- save.snapshot_capture.entered [Engine snapshot phase anchor]
         |    +-- save.target_check
         |    +-- save.temp_encode_write
         |    +-- save.temp_flush
         |    +-- save.temp_hash
         |    +-- save.final_target_check      [existing-target route only]
         |    +-- save.commit_move OR save.commit_replace
         |    +-- save.saved_stamp
         |    +-- save.bookkeeping
         |    +-- failure cleanup/inspection   [only if actually executed]
         |    +-- document.save               [distinct duration child of coarse anchor]
         +-- save.snapshot_captured             [instant request child; exact saved version]
         +-- save.ui_post_returned              [local post returned; not native wake receipt]
         +-- save.ui_started
         +-- save.completed OR failure/cancel/stale/deferred result
         +-- command.save / command.save_as   [distinct duration child; once if not censored]
```

An **entry checkpoint is the retained causal anchor itself**, not an unrelated
child of a mark which will exist only when the operation finally finishes:

```text
S  known session context                      (mote.session terminal may be absent after kill)
└─ R  typed command receipt                    span_id=R, parent_span_id=S
   ├─ E  save.admitted / worker/UI checkpoint  fresh event ID, parent_span_id=R
   ├─ D  document.save.entered                span_id=D, parent_span_id=R
   │  ├─ P  save.temp_flush.entered           span_id=P, parent_span_id=D
   │  │  └─ PT save.temp_flush                span_id=Fork(P), parent_span_id=P
   │  └─ DT document.save                     span_id=Fork(D), parent_span_id=D
   └─ RT command.save                         span_id=Fork(R), parent_span_id=R
```

`BeginRequest` serializes the request mark's **own SpanId exactly once** as
`command.save.received` or `command.save_as.received`, parented to the known
session context. `BeginPhase`
creates an explicit child mark and serializes that mark's **own SpanId exactly
once** as the fixed `*.entered` record. The coarse worker phase is a child of
the request anchor; engine phase anchors are children of the coarse anchor.
Terminal durations use `Fork(mark)`, or the equivalent original-timestamp,
fresh-ID child construction under the original active sink. They must **never**
reuse the entry mark's SpanId. The fork keeps the entry timestamp, so the
duration still measures the intended interval, rather than time since exit.

Ordinary instantaneous checkpoints such as `save.admitted`,
`save.snapshot_captured`, and `save.ui_started` each have a fresh event ID and
the explicit request anchor as parent. They do not establish a new phase
context. This distinction avoids dangling request/phase ancestry when a process
is killed after entry but before completion. No `context_span_id` attribute,
duplicate begin/end SpanId, or synthetic terminal is needed. Every serialized
record has a unique span ID within its session/trace; logical terminal uniqueness
is additionally checked **per anchor and fixed terminal operation**, not just by
detecting repeated IDs.

Entry checkpoints have zero duration; they are not partial duration spans and
are never summed into latency totals.
`success` on an entry event means **this boundary executed**, not its enclosing
Save succeeded. Parentage, not file adjacency or timestamp sorting, identifies
the request. Children can appear before their context's terminal duration;
they already have a retained parent entry anchor if transport preserved it.

| Owner | Owns | Must not own |
| --- | --- | --- |
| Windows/macOS shell | Native callback receipt and composition guard; explicit request dispatch | Save scheduling or document snapshot choice |
| Controller | Admission/rejection, at most one active admitted Save request, worker/UI handoff, request lifetime | File commit implementation or guessed saved version |
| Engine `Document` | Save gate, snapshot/state capture, target checks, write/flush/hash/commit/bookkeeping and typed phase observations | Trace destination, serializer, request registry, UI policy |
| Telemetry | Context IDs, monotonic measurements, bounded nonwaiting enqueue, writer transport and health | Retrying commands, aborting file commits, modifying editor state |
| Acceptance reader | Validate prefix, reconstruct causal stages, classify evidence completeness | Promote absent/censored records to proof of nonexecution |

### 2.1 One typed internal native request contract

`INativeEditorShell` is internal. There is no established external binary
contract for its `Action` Save events; migrate its production, test and probe
implementations together rather than retaining dual events and capability flags.

The contract shape is:

```csharp
/// <summary>Fixed commands sharing the same persistence admission path.</summary>
internal enum NativeSaveKind { Save, SaveAs }

/// <summary>
/// Target callback request; Trace is null when persistence is disabled.
/// It contains no path, sender pointer, key text, title, or document contents.
/// </summary>
internal readonly record struct NativeSaveRequest(
    NativeSaveKind Kind, TelemetryRequest? Trace);

/// <summary>
/// Raised once from target command dispatch; controller admission settles native text.
/// A shell-side blocked/failed request ends without controller dispatch.
/// </summary>
event Action<NativeSaveRequest>? SaveRequested;
```

The separate internal Save As event was removed; both kinds now use this
contract, including internal fake/probe shells and controller wiring. This
changes no menu, shortcut, input,
composition, Save As approval, or external user behavior.

Create the request at the very first target-owned Save/Save As callback, before
Mac `NotifyAfterComposition` can return early. Windows receives it at the owned
command dispatch boundary, before controller guards. Both boundaries mean
**native command callback receipt**, not physical key receipt. An absent shell
or handler ends the available request as skipped; callback exceptions end it
as failure and remain contained at the native ABI boundary. Never unwind a
managed error through an unmanaged callback.

### 2.2 Explicit context, not a long-lived ambient Activity

The enabled-only `TelemetryRequest` holds a `TelemetryMark`, fixed command
kind, initial dimensions, and an atomic terminal flag. It contains no document,
snapshot, shell, arbitrary tags, or path. Disabled creation returns null and
allocates nothing; the native request itself is a value type.

Implemented Telemetry API semantics:

- `BeginRequest(fixedOperation, dimensions)` captures identity and start time,
  enqueues the corresponding typed receipt with the mark's own SpanId immediately,
  and returns the enabled-only owner. The anchor must not wait for a terminal scope.
- `RecordChild(fixedEvent, parentMark, dimensions, status)` uses the explicit
  parent even in a later UI callback. Do not rely on `Activity.Current` there.
- `BeginPhase(fixedOperation, parentMark, dimensions)` creates a current-time
  child context and immediately records `*.entered` using that context's own
  SpanId; `EndPhase` records a distinct original-timestamp duration child.
- Existing `StartChild` remains compatible for older local duration scopes. New
  request/phase work uses the explicit original-sink APIs, **not** its legacy
  fallback; asynchronous work captures the request before scheduling.
- `EndOnce(status, fixedReason, dimensions)` atomically selects one terminal
  enqueue attempt, using a distinct forked duration child of the retained
  request anchor. Requests do not keep Activity ambient across the native loop.

Do not hold a producer admission lease throughout a picker, worker or UI
callback lifetime. Use the established short enqueue leases; request lifetime
is controller owned. Delayed completion after telemetry shutdown is rejected
and counted, not redirected into a newly configured session. Existing
`TelemetryMark`/`Fork` behavior remains compatible. No unbounded request table
or per-document-version dictionary is introduced.

The controller retains one active admitted Save request; a worker captures a
reference to it, the exact `Document`, and its internal document generation.
Generation stays in memory and is never serialized. Additional received Save
requests during `_saving` terminate as skipped, not ignored. A failed
composition guard, cancelled picker, or recovery redirect never produces
`save.admitted` or a fictional successful `document.save`.

## 3. Engine observations and immutable saved identity

The only authoritative saved version is the `TextSnapshot` captured **after**
the engine Save gate is acquired, under `Document._gate`, together with encoding,
BOM, state ID and expected target fingerprint. The UI's pre-scheduling or
completion version can differ. Do not freeze the document early for telemetry,
read a later snapshot to infer it, or persist an internal state ID as a version.

`Mote.Engine` remains independent of `Mote.Telemetry`. Its narrow optional typed
observer is exposed through explicit overloads, preserving existing public
signatures and their argument binding:

```csharp
/// <summary>
/// Observes Save milestones without supplying policy or I/O. Callbacks may
/// occur on background threads, are serial for this attempt, and must not block.
/// They are never invoked while Document's state lock is held.
/// </summary>
public interface IDocumentSaveObserver
{
    /// <summary>Receives only a fixed phase/edge and optional numeric snapshot version.</summary>
    void Observe(in DocumentSaveObservation observation);
}

/// <summary>Content-free evidence from one Save attempt.</summary>
public readonly record struct DocumentSaveObservation(
    DocumentSavePhase Phase, DocumentSaveEdge Edge,
    long? SnapshotVersion = null, int? HResult = null);

/// <summary>Preserves the two-argument API; an observer never changes Save policy.</summary>
public Task SaveAsync(string? path, CancellationToken cancellationToken,
    IDocumentSaveObserver? observer);
```

An equivalent three-argument overload applies to `SaveOverAsync`; old methods
delegate with null observer. Do **not** add an optional second observer argument
which makes existing `SaveAsync(path, default)` ambiguous. The committed
public additive overloads preserve that binding and the no-telemetry-dependency
and exact-snapshot rules.

Phase and edge enums are closed: `Entered`, `Succeeded`, `Failed`, `Cancelled`
and `Skipped`. `Skipped` explicitly covers lifetime-ended bookkeeping; it is
not a successful clean-state update. The native adapter maps them to fixed trace
operations under the explicit request/coarse Save parent, using one active
phase mark at a time. Each engine phase has its own retained `*.entered` anchor
under the coarse Save anchor, and a distinct duration child on exit. Exact
`save.snapshot_captured` is an instantaneous request child, not a replacement
for the snapshot-capture phase anchor. No arbitrary operation string is supplied
by an observer.
Use only the main error's allowlisted filesystem HResult; no exception messages,
paths, fingerprints, encoding strings or exception `Data` enumeration.

Observer invocations run outside `_gate`, at existing phase boundaries, and
must not affect return values, exception identity, cancellation or cleanup.
Callbacks can still run under `_saveGate`: they must not synchronously await
another Save on the same document or acquire UI locks.
Contain nonfatal observer failures at the boundary; do not let tracing throw
after a real file replacement and turn a successful Save into an apparent
product failure. A null observer is one branch at each coarse phase, not a
per-chunk delegate, serialization, clock read or allocation.

### 3.1 Phase contracts

| Phase/milestone | Exact evidence | Important exclusion |
| --- | --- | --- |
| Gate wait | Before `WaitAsync` -> acquired/cancelled/failed | Controller thread-pool queue delay |
| Snapshot captured | After engine lock releases; carries captured snapshot version | Current UI version or final clean state |
| Initial target check | Existing stamp/new-path existence checks | Final content identity check |
| Encode/write | Stream creation plus actual chunk writes | No per-chunk telemetry |
| Temp flush | `StreamWriter` flush/disposal and staged-file `Flush(true)` | Does not prove target replacement or portable directory crash durability |
| Temp hash | Existing staged-file reread and SHA-256 | Must not emit the hash or add a second diagnostic reread |
| Final target check | Existing content verification before replacing an existing target | Portable compare-and-swap guarantee; current TOCTOU limit remains |
| Commit move/replace | Immediately before call -> normal return/error | Exception does not prove the target remained original |
| Saved stamp | Existing post-commit stamp read | Not the commit itself |
| Bookkeeping | Saved fingerprint/path/state update under lock, observation outside it | May be skipped if document disposed; not a successful clean-state certificate |
| Failure cleanup/inspection | Existing deletion and actual outcome observation work | Never retry a commit or add unrelated disk inspection for telemetry |

Preserve the existing recovery and external-modification refusal behavior.
Reported `SaveFailureInfo` outcomes, if mapped, are fixed enum values only and
mean the existing observation at its actual time, not an enduring guarantee.

## 4. Outcomes, lifetimes, and truthful completion

| Route | Request terminal status/reason | Additional evidence |
| --- | --- | --- |
| Native or controller composition settlement refused | skipped / composition_blocked | Target receipt and guard checkpoint; no admission |
| Already saving | skipped / already_saving | Independent received request; existing Save unaffected |
| Picker dismissed or overwrite declined | cancelled / picker_cancelled or overwrite_declined | No successful Save scope for declined approval |
| Pending recovery redirects the command | skipped / recovery_redirected | Separate recovery behavior is not relabeled Save |
| Save error | failure / save_failed | Actual failed phase/HResult and commit/inspection observations if available |
| Successful persistence and current-document UI completion | success / completed | Captured saved version; linked `save.completed` |
| Successful persistence, UI view postponed during new composition | success / view_deferred | `save.ui_deferred` checkpoint; no claim of source view installation |
| Closing/replaced-document request | cancelled / lifetime_ended or stale_document | Actual engine child outcome retained if available; do not abort a real commit just for tracing |
| No terminal after abnormal exit | **reader classification: censored** | Last retained positive stage; never synthesize cancelled/failure/success |

`cancelled` request lifetime does not imply cancellation of filesystem work.
If closure wins `EndOnce` before the engine returns, a later successful commit
child may outlive the request terminal. This is honest: UI request lifetime ended
while persistence continued. The reader does not require parents to temporally
enclose every child, and must not promote that root to successful UI completion.

At worker-to-UI handoff, use the controller's existing `TryPost` outcome rather
than ignoring a synchronous posting exception. Record `save.ui_post_returned`
only after `Post` returns; capture the same context in the callback. This means
local posting returned, **not native dispatch acknowledgement**: current Windows
`Post` ignores the `PostMessageW` result and skips its wake when `_window == 0`;
Mac `Post` skips the native wake when `_delegate == 0`. Only `save.ui_started`
certifies callback entry. Do not broaden the posting API solely to claim a
stronger stage which is not needed to identify this gap. At callback entry check
disposal and **reference/generation identity** before selecting policy or
touching the current document view. A late completion cannot operate on a new
document merely because its version number matches. This is an invariant worth
enforcing, not a new tracing-only behavior branch.

These are distinct facts: (a) callback entered the shell's managed queue;
(b) a native wake call was attempted; (c) that call acknowledged message-queue
acceptance **where the platform provides such a result**; (d) the callback
actually entered on the UI thread. None implies the next one. Initial controller
tracing may certify only post return and callback entry; label intermediate
facts unobserved rather than inventing them.

**Unimplemented follow-up, not current API:** if wake failures need attribution,
migrate the internal
`Post(Action)` return from void to a path-free `NativePostReceipt` containing
`Queued` and a closed wake outcome (`NotRequested`, `RequestedUnacknowledged`,
`AcceptedByOs`, `RejectedByOs`). Ordinary existing callers can ignore the result.
Windows maps the **actual** `PostMessageW` Boolean to acceptance/rejection; this
means queued native message, not delivered callback. Mac's current void
`performSelectorOnMainThread` invocation is requested-unacknowledged, never
fabricated acceptance. Zero window/delegate is not-requested. Preserve the
existing single enqueue, no retries, no activation and no new exception solely
for a wake failure. For before-return failure checkpoints, a separate optional
explicit parent argument lets the shell record enqueue/wake entry while it
already owns those boundaries; it does not create a new posting route.

Tests use an injected native wake boundary to return false after one managed
enqueue, and no-window/delegate cases: the receipt/checkpoints show local
enqueue plus failed/not-requested wake, while no UI entry or completed Save is
invented. A successful Windows wake with intentionally held UI pump certifies
only native queue acceptance. A Mac requested-unacknowledged test stays unknown
until the pump actually executes. This exposes existing silent wake loss
without a product retry/behavior change. Prioritize this follow-up over route
variants if the resulting request evidence stops at `ui_post_returned`.

New request/phase scopes are non-success until their explicit normal completion.
Existing generic `MoteTelemetry.Start` default-success behavior is preserved for
compatibility; locally initialize uncertain scopes as cancelled or failure and
set success only at the correct endpoint. The overwrite-declined Save and
throwing idle-analysis publication paths
were corrected with focused tests; idle publication also rechecks ownership
after reentrant native view callbacks. See the
[native telemetry outcomes review](../reviews/native-telemetry-outcomes-review.md).
Outer catches after `using` disposal cannot repair an already emitted
success record.

## 5. Integrate, do not multiply, the edit-to-draw causal chain

Existing accepted-mutation marks remain authoritative. A command may cause
composition settlement to commit an edit before Save admission. That edit keeps
its own target-version semantic and draw endpoints. If it was synchronously
caused by this command, create its mark explicitly under the command context;
otherwise it remains its independent edit chain. Do not steal a pending edit
mark, overwrite it with a Save version, or parent a later unrelated edit under
an earlier Save merely because it shares a document.

Formatting/Undo/Redo may adopt the same request entry pattern after Save's
contract is validated. Ordinary typing retains existing mutation-level spans;
there is no need for a persistent record for every physical key/preedit update.
Analysis parse/publish/discard, source installation and first eligible draw
retain their generation/version guards and one-pending-mark boundedness.

UI completion is not automatically semantic publication or draw completion.
When Save's policy/view update schedules work, its actual scheduling mark may
be used as parent; existing source-draw endpoints remain accurately named. Do
not rename draw submission to paint/photon or infer pixels from a screenshot
timestamp. Physical-presentation observability remains a separate platform
measurement question, not a prerequisite for locating Save queue/I/O failures.

## 6. Recoverable transport prefix without hot-path I/O

### 6.1 Writer-only periodic visibility

Retain one writer and the existing bounded channel, capacity 4096 by default.
Producers only enqueue immutable records with `TryWrite`; all JSON formatting,
rotation, buffer flushes and files remain writer owned. Start/checkpoint events
are low frequency (startup/session, command, Save phases); ordinary edits do not
each require a flush or a persistent begin record.

The implemented writer transport uses **250 ms** dirty flush
turns or **64 KiB** of unflushed written records. Reuse that tested policy; do
not reimplement it with a different threshold. No unconditional session-start
record is added by the transport slice because existing probes depend on exact
record counts. The causal-command slice emits its received checkpoint directly;
the same writer then flushes it even if no further records arrive. Avoid an
infinite `ReadAllAsync` wait which prevents
flushing a small idle buffer. Check the deadline during continuous channel
traffic as well as idle waits; neither a busy queue nor an idle one may defeat
the policy. These are tuning values, not measured SLA guarantees. A later
explicit evidence-session mode can add a content-free health checkpoint at
most once per **1 s**, plus `mote.session.started`. Enable this mode only in
readers/workflows migrated to the added records; keep legacy probe operation
counts stable. It is an opt-in transport-evidence level, not a second sink,
writer, command route or business-state model.

Use a writer-owned `PeriodicTimer`/bounded channel wait with **one outstanding
read wait and one timer wait**, not a new abandoned wait/task per record. Flush
all outstanding waits/timer lifetime correctly on rotation/shutdown. A batch
drain must be bounded so elapsed-time checks still run. No producer waits for a
flush, acquires a writer lock, or calls `Flush(true)`.

Periodic `FileStream.FlushAsync` exposes managed buffered bytes to the OS;
it is **readable-prefix recovery after process termination**, not power-loss
durability. Keep existing explicit `Flush(true)` only on orderly
shutdown/rotation. A stalled writer, slow filesystem, thread-pool starvation,
OS suspension or abrupt termination can exceed the budget; surface stale/faulted
health instead of claiming a hard real-time guarantee. No dedicated tracing
thread is added without evidence that the shared writer scheduler defeats the
required recovery tests; such a thread would still not prove UI liveness.

### 6.2 Current schema v1; unimplemented watermark follow-up

Keep all existing schema-v1 required fields, types, status strings and operation
meanings. The transport-only slice changes none of them. The causal/evidence
slice adds only closed operation names and optional, privacy-safe attributes.
Existing `version` means the stage's observed version; saved-version stages use
the captured snapshot. No free-form `reason` or arbitrary property bag is allowed.

The initial causal slice needs **only** the optional closed terminal `reason`
attribute. It introduces no context-ID, sequence or watermark attribute.
Retained parentage is expressed by existing `span_id` / `parent_span_id` fields
as specified in section 2. The following table describes **possible future
evidence-mode attributes, not
implemented fields** (except the closed terminal `reason` already implemented):

| Attribute | Type / scope | Meaning |
| --- | --- | --- |
| `record_sequence` | positive integer, writer assigned to evidence-mode records | Serialization order within this session, continues across rotated files |
| `session_elapsed_us` | nonnegative integer on new causal/evidence records | Target monotonic elapsed time captured at producer boundary, not inferred from UTC |
| `reason` | allowlisted string enum on request terminal | One of the fixed outcomes in section 4 |
| `flushed_sequence` | nonnegative integer on health checkpoint | Last sequence whose `FlushAsync` actually returned before this checkpoint was formed |
| `dropped_total` | nonnegative integer on health/session terminal | Cumulative observed record rejection/loss, not just latest aggregate |

IDs, record sequences and versions are not aggregation labels. `record_sequence`
has no gaps for successful serialized records; channel-rejected records never
receive it. Therefore sequence continuity **does not mean zero producer loss**.
Use the drop counter too. Files removed by existing per-session retention make
the retained prefix incomplete even when the remaining sequence is continuous.

In that future mode, a periodic cycle would append a health checkpoint carrying
the **previous
successful flush watermark**, then flush all pending records and update the
in-memory watermark. The next checkpoint acknowledges that returned flush.
This needs one periodic flush, not a recursive "flush the acknowledgement of
the acknowledgement" protocol. A new checkpoint's own sequence is not certified
by its payload. A reader that already sees that complete line has positive
readability evidence for it, but not a power-loss durability guarantee. No new
sidecar or transport protocol is required for the conservative watermark.

Extend the path-free health snapshot with written/flushed sequence and last
successful flush age, using atomically readable primitive fields. Keep existing
Enabled/SinkFaulted/DroppedRecords fields and their meaning. Health is a snapshot,
not an atomic proof of all concurrent producers' states. Sink failure remains
in-memory visible even when no failure record can be written. Retain the normal
shutdown admission/drain protocol and its existing two-second total budget; add
terminal drop totals before the final flush, not after the session terminal.

Current privacy tests and readers allow only the closed operation/reason
vocabulary and existing typed dimensions, not arbitrary keys. Any future mode
requires its own explicit allowlist migration.
Historical schema-v1 files lacking health attributes remain readable as
`legacy_health_unknown`; a new causal slice without those attributes also has
unknown transport health, not a fabricated healthy stream. Readers ignore
unknown future fixed operation names
for rendering but may not treat an unknown operation as a certified workflow
stage. Unknown schema versions are unsupported, not successful acceptance.

### 6.3 Exact absence and censorship semantics

| Retained evidence | Reader conclusion |
| --- | --- |
| Empty/missing trace or no readable evidence-mode session start | No usable session-health certificate; not callback absence; any positive records still retain their local meaning |
| Healthy writer checkpoints but no command receipt | Writer progressed; native callback execution is **unknown**, not disproved |
| Positive command receipt but no admission/terminal | Last observed boundary is receipt; guard/picker/UI stall/transport tail unknown |
| Positive admission but no worker entry | Work was admitted; worker start unobserved; cannot yet distinguish queue delay from lost tail |
| Positive phase entry, no phase exit after kill | Operation entered that phase; remainder right-censored, not necessarily still blocked at death |
| Returned commit then failed bookkeeping | File commit returned; request failed later; independent byte/recovery evidence determines observed target outcome |
| Request terminal plus complete normal drain, no drops, expected stages | Instrumented scoped chain complete; no assertion about uninstrumented earlier OS events |
| Dropped records, sink fault, truncated retention, shutdown timeout or absent terminal | Coverage degraded/censored, even if some successful spans remain |

The reader indexes a new request by **the typed receipt anchor's SpanId**,
not by the distinct `command.save` terminal SpanId. A request terminal refers to
that anchor through `parent_span_id`. It follows retained phase ancestry rather
than assuming every child of a duration is attached to a final-only request.
Legacy traces without the new anchors keep their historical causal interpretation;
they cannot be promoted to new request-to-Save coverage merely because an old
`document.save` exists. Optional health attributes and the new operation
vocabulary do not change the required schema-v1 field structure.

Entry anchors still use the same bounded lossy transport: enqueue may fail,
rotation/retention may remove an earlier file, or the tail may never flush.
Retained child/terminal with a missing request or phase anchor is **orphaned
evidence**. Report its positive local operation and degraded/unresolved
ancestry; do not synthesize the anchor, reparent it to a nearby receipt/version,
or certify a complete chain. A positive request anchor without terminal is an
in-flight request for a live reader and a censored request after termination.
A missing session terminal after kill is expected missing session closure,
not proof that retained request/phase anchors failed to execute. Unknown
ancestry above a retained anchor is still not a complete session-health
certificate. Drop count or health absence affects confidence in coverage,
not the positive meaning of a valid retained stage.

An external action attempt and a target writer heartbeat have no shared causal
token by themselves. A heartbeat is not a post-action UI barrier; it cannot
prove the native callback did not run. A future explicitly acknowledged
target-UI barrier would certify only processing up to that barrier and would
require a separate controlled test contract. It is **not** silently inferred
from current readiness, focus, UTC time or FIFO file order.

Use the target monotonic clock for target stage durations. External helper
duration uses its own clock; compare route outcomes, not raw clock subtraction.
Cancelled/censored operations are excluded from successful latency percentiles
and reported separately. The final incomplete JSONL line can be ignored after
process termination; malformed earlier complete lines, duplicate terminal
request IDs or invalid watermark order are integrity failures. Live readers
must retain a partial line for the next read, not discard its eventual suffix.

## 7. Validation contracts and remaining performance gates

Do not repeat completed hosted acceptance merely for a new report. Add focused
deterministic tests and one bounded infrastructure workflow, then reuse actual
ordinary workflow artifacts for integration evidence.

### Deterministic contracts (implementation tests versus future-mode requirements)

Current tests/reviews linked above cover the request, Engine and native-lifetime
contracts. Requirements involving watermarks, sequence continuity or periodic
health remain future-mode requirements; this checklist is not an assertion that
every item has executed on all platforms.

1. **Causality:** two received requests, one admitted and one already-saving;
   children of each remain distinct across `Task.Run` and queued UI callbacks.
   All expected records carry explicit parentage; receipt/phase entry uses its
   context's own SpanId once; distinct forked terminal child retains the original
   start; exactly one terminal per completed request and phase. All serialized
   span IDs are unique. Duplicate terminal operations under one anchor fail even
   if their span IDs differ. No `context_span_id` property is emitted.
2. **Guards/outcomes:** native/controller composition block, picker cancel,
   overwrite declined, recovery redirect, Save failure, cancelled gate wait,
   publication throw, UI enqueue refusal, and disposal. No unintended success
   terminal or `save.completed` remains on these routes.
3. **Snapshot:** delay engine gate/capture, mutate before capture and again during
   write. Saved version equals actual captured snapshot, never latest UI state;
   newer edits remain dirty. Byte oracle remains authoritative.
4. **Lifetime race:** replace document or close while a worker is held at a
   deterministic phase; terminal request is cancelled once, late completion
   cannot mutate the new document or select its policy, and actual old-document
   commit evidence is not relabeled current-document UI success.
5. **Phase fault boundaries:** inject existing `DocumentSaveOperations` failures
   before/after commit and stamp read; observer failure cannot alter the primary
   exception or successful persistence. Do not sleep/retry to force a race.
6. **Writer:** idle small buffer flushes within a tested generous scheduler bound;
   busy traffic also flushes; max queue/slow writer/drop/fault preserves
   nonwaiting producer behavior; watermark only advances after returned flush;
   rotation sequence and retained-file gaps classify correctly; shutdown stays
   bounded with active requests and held writers.
7. **Loss/orphan/privacy/backward compatibility:** drop/remove a request anchor
   or intermediate phase anchor while retaining descendants; reader reports
   orphaned local evidence and never a complete chain. Retain an entry and kill
   before terminal; it remains identifiable by its own SpanId and last-positive
   boundary. Secret source/path/exception text never
   appears; unknown exception-data keys rejected; old schema-v1 fixture still
   loads; new optional attributes are exactly allowlisted; public two-argument
   Save calls compile unchanged.
8. **Disabled path:** warmed received-request/mark/child/checkpoint instrumentation
   loop has zero managed allocation; null observer adds no per-chunk work or I/O.

### Kill/process recovery experiment

Use a repository-local child harness, not a real user's document. Configure a
fresh tracing directory, enter a deliberately held low-frequency operation,
and keep the writer running. For the existing transport-only slice, parent
observes the completed test checkpoint actually readable before terminating;
it cannot assert a built-in watermark which does not exist yet. For the later
evidence-mode slice, parent reads a positive phase entry and a conservative
watermark acknowledging it; only then terminate that **owned child**. Validate
a nonempty parsable prefix, retained entry, no fabricated
terminal and `censored` classification. Repeat with idle small files, busy
traffic, rotation and writer-fault/slow-writer controls. In the controls, failure
to obtain the watermark is transport degradation, not operation nonexecution.

Separately kill without waiting for the budget and permit loss of the tail;
test the stated limit rather than asserting impossible crash durability. Run
the normal kill experiment on Windows/macOS Native AOT RIDs in Actions. Avoid
clipboard, input-source, permission, global activation or user-profile changes.

### Measured performance gates

| Gate | Acceptance contract |
| --- | --- |
| Tracing off | No trace files or writer/timer; zero warmed instrumentation allocation; no hot-path disk I/O |
| Tracing on producer | No waits/locks on writer, serialization or fsync; bounded queue saturation records drops without blocking edit/Save |
| Prefix recovery | Held-phase entry survives owned-process kill after parent observes a readable linked prefix; no watermark is implemented; hosted four-RID audit pending |
| Ordinary workflows | Exact bytes + fresh reopen + causal stage chain + normal drain; any non-gating nested failure remains a failure in its report |
| Native editing overhead | Alternating off/on fresh-process repetitions, identical synthetic edit workload and binary, report median/p95/p99 with sample counts, allocations, drop rate and trace completeness |

Use an initial regression budget of **no more than 5% or 0.5 ms added p95 accepted
mutation-to-draw-submission latency, whichever is larger**, subject to enough
samples and paired uncertainty to support the comparison. This is a proposed
gate, not a measured result or physical-input SLA. Keep absolute startup and
Save wall-time measurements too; tracing startup excludes the loader/config
cost unless separately measured externally. If noise exceeds the budget,
declare the experiment inconclusive instead of manufacturing a pass. Do not
enable broad key logging, sampling/compression or a tracing thread to rescue an
unmeasured regression.

## 8. Implementation partitions and integration order (retained design rationale)

One writer per file area; API agreement precedes integration. No repository-wide
edit lock is necessary.

| Partition / role | Exclusive area | Deliverable / dependency |
| --- | --- | --- |
| Transport worker | `src/Mote.Telemetry/*`, `tests/Mote.Tests/TelemetryTests.cs` and a dedicated request test file | Explicit child events, request owner, fixed schema attributes, periodic writer/health; publish API shape first |
| Engine worker | `src/Mote.Engine/Document.cs`, new typed observation file, focused Save-observer test file | Null-compatible phase observer, authoritative snapshot version; no Telemetry dependency |
| Native orchestration worker | `NativeShell.cs`, `NativeEditorController.cs`, both production shell Save dispatch sites, affected internal fake/probe signatures and focused controller tests | Single typed Save request, all guards/worker/UI context and outcomes; coordinates Telemetry/Engine signatures |
| Validator | New repository-local recovery harness and focused reader fixtures/tests | Independent prefix kill/health/privacy/censorship contract; does not edit production |
| CI/reader worker | Existing JSON workflow reader, trace reader module, assigned workflow steps | Backward-compatible v1 parsing, scoped chain completeness and recoverable-prefix job, actual nested status surfaced |
| Reviewer/curator | Review and existing trace/validation documentation only | Independent invariant, privacy, safety and evidence audit; no production fixes |

The integration followed this dependency order; its remaining hosted/performance
audit is separate from the completed source implementation.
Order: (1) fix proven misleading outcomes; (2) implement/test writer prefix
and explicit context APIs; (3) engine phase observer; (4) migrate native typed
dispatch and end-to-end Save ownership; (5) integrate reader, owned-process kill
and Native AOT Actions; (6) audit ordinary workflow raw artifacts and paired
performance. API-dependent workers may proceed concurrently after agreeing on
types; controller/source-signature edits stay with one native owner. Do not
cherry-pick overlapping edits or stage unrelated user IDE settings.

The milestone is **runtime evidence locates the last positively executed
boundary without lying about missing work**. Only after that is established
should additional Mac route variants or feature work resume. A technical debt
item which obstructs this chain is paid down now; unrelated redesign is not
smuggled into tracing work.

## 9. External grounding and rejected alternatives

- [.NET tracing concepts](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts)
  and the [OpenTelemetry tracing API](https://opentelemetry.io/docs/specs/otel/trace/api/)
  distinguish spans, events and explicit parent context. Use that causal model,
  but retain the existing AOT-safe serializer and local-only pipeline; no SDK
  exporter dependency is required. Mote's checkpoint representation is its
  own schema-v1 event convention, not a claim of full OTLP interoperability.
- [FileStream.Flush](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-10.0)
  distinguishes buffered visibility from explicit flush-to-disk. The current
  [.NET buffered FileStream implementation](https://source.dot.net/System.Private.CoreLib/src/runtime/src/libraries/System.Private.CoreLib/src/System/IO/Strategies/BufferedFileStreamStrategy.cs.html)
  supports the choice of periodic asynchronous buffer draining, with explicit
  durable shutdown kept separate. Validate against the repository's pinned
  runtime/toolchain; do not rely on legacy .NET Framework behavior.
- Google's production [Dapper report](https://research.google/pubs/dapper-a-large-scale-distributed-systems-tracing-infrastructure/)
  motivates common instrumentation boundaries and restrained overhead, not
  automatic capture of desktop input or renderer presentation.
- Cornacchia et al., [*Observability Is Eating Your Cores*, NSDI 2026](https://www.usenix.org/conference/nsdi26/presentation/cornacchia),
  study the cost/coverage tension in cloud metrics and propose IPU-hosted local
  sketches. It is peer-reviewed systems evidence that observability has its
  own resource and fidelity costs. It does **not** establish mote overhead or
  justify an IPU/cloud architecture for a desktop editor. The relevant design
  consequence is measured producer cost and selective informative boundaries,
  not more telemetry volume. Keep metrics sketches/compression out until a
  real local workload demonstrates that plain bounded JSONL is inadequate.

Rejected: SQLite/event sourcing (unneeded dependency and second state model),
per-record fsync (hot-path interference), tracing every OS key/IME string
(privacy/volume and false endpoint semantics), global input hooks (scope and
permission costs), timestamp-only correlation (missing causality), permanent
parallel diagnostic business pipelines (drift), generic command registries
(unnecessary extension point), and fabricated timeout-success or negative
callback evidence (incorrect interpretation).
