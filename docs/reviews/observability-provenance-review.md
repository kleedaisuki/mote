# Observability provenance architecture: independent review

Date: 2026-10-01. Reviewed repository state: `8a24e90b16ca9886d25274a2ce1921b6d9cecaca`.
Scope: `docs/architecture/observability-provenance.md` against the independent
coverage audit, current Telemetry transport/context APIs, Engine Save ownership,
and native controller dispatch/completion. This reviews a design contract, not
an implementation or hosted acceptance certificate. No production, test, or CI
files were changed and no completed tests were rerun.

## Verdict

**No substantive architecture blocker identified.** The proposal addresses the
demonstrated evidence gaps without a second business-state pipeline: one explicit
request, existing writer, narrow Engine observer, fixed checkpoint operations,
and conservative reader semantics. Proceed with the coordinated implementation.
The integration requirements below are material because current APIs do not yet
enforce all of the proposed contracts; they are not claims that proposed code
already violates them.

The central useful guarantee is the last positively executed, retained boundary.
Neither periodic flushing nor a healthy writer proves that an absent native
callback did not execute. The design explicitly avoids that incorrect inference.

## Existing implementation versus planned coverage

| Capability | Reviewed current evidence | Additional work in this contract |
| --- | --- | --- |
| Periodic readable prefix | `JsonlTraceSink` has writer-owned 250 ms/64 KiB dirty flushing; existing focused validation records 24/24 Telemetry tests and separate owned-child recovery evidence | Reuse it; no second writer or different flushing policy |
| Truthful declined-overwrite/idle-publication outcomes | Existing outcome correction and reentrant idle ownership follow-up have separate review/validation artifacts | Do not redo those repairs or treat them as command causality |
| Command receipt/admission | Internal shell still exposes separate parameterless Save and Save As events; controller returns silently on several guards | One typed request from first target callback, explicit guard outcomes |
| Worker/I/O/UI lineage | `StartSave` starts coarse Save inside `Task.Run`; completion remains a separate session event | Explicit request children and immutable document/generation capture |
| Saved snapshot identity | Engine already captures immutable snapshot and state under `_gate` after `_saveGate` admission | Emit that captured version outside `_gate`, not a UI estimate |
| Evidence health/sequence | Current health contains enabled/fault/drop fields; no built-in sequence/flush acknowledgement/session-start evidence mode | Add optional closed attributes and evidence-mode reader handling |

References for already completed validation are
[recoverable prefix](../validation/telemetry-recoverable-prefix.md),
[prefix review](telemetry-recoverable-prefix-review.md),
[outcome validation](../validation/native-telemetry-outcomes.md), and
[outcome review](native-telemetry-outcomes-review.md). Their recorded results do
not certify this future request/observer/reader slice or all four Native AOT RIDs.

## Required integration gates

### 1. Reject stale explicit contexts; do not silently reparent them

Current `MoteTelemetry.StartChild` falls back to `Start` when the supplied mark's
sink differs from the currently configured sink. That established behavior may
remain for its existing callers, but is **not suitable for new request-owned
Save instrumentation**. Concrete trigger: close session A while a Save worker or
UI callback is delayed, configure session B, then resume the old callback. Using
the fallback creates a seemingly ordinary span in B with no A request ancestry.

The new explicit request child/event/terminal operations must be bound to the
original request sink and reject/count loss there after admission closes. They
must not consult `Activity.Current` to repair parentage and must not transfer an
old request to a new session. A completed request may still have actual late
engine children after lifetime cancellation; ending the request and closing the
transport are different events. Implement this once in the explicit API rather
than repeating sink checks across native callbacks.

This is an integration gate, not a request to change the behavior of every
legacy `StartChild` caller. Existing tests covering legacy parentage remain
valuable, but do not prove the new stale-session contract.

### 2. Observe outside the state lock, but recognize the Save semaphore remains held

The proposed snapshot capture is correct: snapshot/version, encoding, BOM,
expected fingerprint and saved-state identity are captured together under
`Document._gate`, after acquiring `_saveGate`. Report the captured version after
releasing `_gate`; subsequent edits do not change which snapshot is written.
An internal saved-state ID is not a text version and should remain unpersisted.

Moving observer callbacks outside `_gate` avoids lock-held external code, but
does **not** release `_saveGate`. All phase callbacks still run within the Save
attempt. A public observer that synchronously invokes and waits for another
Save on the same document will deadlock on that semaphore. The existing proposed
nonblocking callback contract rules this out; make its XML documentation explicit:
no synchronous wait on same-document Save, no UI dispatch-and-wait, no controller
lock acquisition, and no policy/I/O decision. The production adapter should only
capture/close marks and attempt bounded enqueues. Do not add a callback queue or
unlock Save early just to support a prohibited observer behavior.

Contain nonfatal observer exceptions at each Engine notification boundary. An
observer throw after replacement must not change a successful Save into failure;
an observer throw while reporting a primary filesystem error must not replace
that error or alter recovery/cleanup. Keep actual commit and failure-inspection
observations truthful. Engine's existing catch filters and exception identity
must remain unchanged apart from this isolated observer containment.

### 3. Request cancellation is not a filesystem outcome

`EndOnce` correctly selects one request terminal record; it must not become a
cancellation source for an already running commit. Closing/replacing the view
can cancel its request lifetime while its captured old document is still saved.
That cancelled root may have later successful commit children. Do not force
parent/child time enclosure, relabel the actual Engine phase as cancelled, or
promote the root to current-view success.

The current controller completion checks disposal but then selects policy and
refreshes current state from the worker's captured document without a
reference/generation guard. The proposal explicitly fixes this boundary. Keep
the guard before **any** current-document policy, analysis, view, status or
SaveCompleted publication, not just before recording the terminal trace. A
matching numeric version is insufficient for replacement-document identity.
Busy-state ownership must belong to the admitted request so an old completion
cannot release a newer request's ownership if the integration permits one.

### 4. Watermarks acknowledge prior flushes, not their own records

The proposed one-cycle-lag watermark is sound and requires no recursive flush:
write a checkpoint with the previous returned-flush sequence, flush pending
records, then update the in-memory watermark. The next checkpoint can acknowledge
it. For an idle held operation the writer must continue low-frequency checkpoint
turns; a finite next turn acknowledges the entry even when no more producer
records arrive. Do not require a new producer event to unlock this acknowledgement.

Sequence assignment belongs to the sole writer after selecting a record to
serialize. It cannot reveal channel-rejected records; the cumulative drop
snapshot is separately required. Advance the successful-flush watermark only
after the awaited flush returns. On rotation, a returned close flush can advance
it; opening a new file must not reset the session-wide record sequence or
fabricate a flush of the new file. A bounded batch drain is necessary for the
new health emission cadence even though existing `AppendAsync` already prevents
busy traffic from starving dirty-buffer flushing.

Health values and terminal drop totals are observations at their capture time,
not proofs about all future delayed attempts. An old request rejected after a
session terminal cannot retroactively change that terminal's serialized total.
Consequently a reader must require the actual expected stages, normal terminal
drain, known retention completeness and observed zero loss for a scoped complete
chain; session success/sequence continuity alone is insufficient. A cancelled
or incomplete request stays incomplete even if the session terminal is success.

The writer must never wait for a producer, controller, UI callback, or reader
acknowledgement to advance a watermark. Tests waiting for a watermark run outside
the target process and must time out as evidence degradation, not assert callback
nonexecution. This avoids an instrumentation-induced wait cycle.

## Compatibility and complexity assessment

- The proposed required three-argument `SaveAsync(path, cancellationToken,
  observer)` preserves existing two-argument calls including `SaveAsync(path,
  default)`. Apply the same required-argument shape to `SaveOverAsync`; do not
  add a competing two-argument observer overload or new optional argument that
  changes overload resolution. Existing signatures delegate to the shared core.
- One internal typed Save event is simpler than dual legacy/telemetry events,
  feature negotiation or a generic command registry. Migrate all fake/probe
  implementers with the production shells and controller in the same integration.
- Child checkpoints get new IDs; the request duration owns its distinct ID and
  one terminal. Checkpoint success means execution of that boundary only. Do not
  sum checkpoints or overlapping coarse/phase durations as disjoint total cost.
- Fixed optional schema-v1 attributes are a reasonable additive change provided
  exact privacy/type allowlists and old fixtures remain checked. Evidence mode
  must be explicit in the participating readers/workflows; legacy files are
  health-unknown, not newly certified. Unsupported versions fail acceptance.
- Keep the proposed native post receipt a follow-up unless Save evidence actually
  stops at the UI handoff. `Post` returning is currently only a local-return fact.
  Windows `PostMessageW` queue acceptance, Mac unacknowledged wake request and UI
  callback entry are distinct; no successful wake should be invented from return.
- Do not replace the bounded JSONL writer, introduce a general event bus,
  permanently retain diagnostic witness business states, or add a dedicated
  writer thread without measured scheduler evidence. The design already has
  sufficient ownership information to implement the first useful chain.

## Flush and span semantics: official grounding

[FileStream.Flush(Boolean)](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-10.0)
documents the explicit intermediate-buffer-to-disk option. Periodic async
buffer draining is a readable-prefix mechanism, not portable target-directory
power-loss durability. Engine's existing staged-file `Flush(true)` is a separate
Save phase; a returned replacement and a later saved-stamp/bookkeeping failure
must remain distinguishable. No new fsync, second hash read or commit retry is
needed for tracing.

[PeriodicTimer.WaitForNextTickAsync](https://learn.microsoft.com/en-us/dotnet/api/system.threading.periodictimer.waitfornexttickasync?view=net-10.0)
requires one consumer and coalesces ticks. Preserve the reviewed retained-wait
ownership when adding low-frequency health emission; do not create orphan waits
or equate a configured timer period with a scheduling deadline.

[OpenTelemetry tracing API](https://opentelemetry.io/docs/specs/otel/trace/api/)
supports explicit context and distinguishes ended span operations from their
children. Mote's independent request/child lifetimes are therefore coherent, but
its JSONL checkpoints remain a local convention, not an OTLP interoperability
claim. The architecture's research/production references motivate restrained
overhead; only the proposed paired native measurements can establish mote cost.

## Scope limits and next review

The next independent implementation review should trace native receipt through
each guard, Engine snapshot/commit, UI ownership and original-sink rejection;
inspect writer/reader watermark handling together; and reuse the assigned
deterministic tests and owned-child kill artifacts. This document does not
certify crash durability, physical input/draw latency, platform event delivery,
all-format semantics, GUI usability or the still intermittent Mac Save route.
Do not resume route permutations merely because the architecture review has no
blocker: implement and validate the permanent runtime evidence chain first.
