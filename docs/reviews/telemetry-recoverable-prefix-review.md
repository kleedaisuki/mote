# Recoverable telemetry prefix: independent review

Date: 2026-10-01. Scope: writer-owned periodic flushing in `src/Mote.Telemetry/JsonlTraceSink.cs`, focused `TelemetryRecoverablePrefixTests`, existing producer/drain contracts, and documentation. This is not a GUI acceptance or power-loss durability certificate.

## Assessment

No substantive production defect identified in the reviewed periodic-flush implementation. Frozen source identity and completed test evidence are recorded below. The review does not convert configured budgets into a hard wall-clock deadline.

## Writer and wait ownership

- A sole writer owns file creation, JSON formatting, both writes comprising a physical JSONL row, periodic flushing, rotation, pruning, and final close. No producer performs file I/O or takes a new writer lock.
- The channel readiness task and timer tick task are retained when they lose `Task.WhenAny`. A new timer wait is created only after its predecessor has been awaited and cleared. The channel readiness task is similarly consumed before replacement. This satisfies the single-consumer `PeriodicTimer` rule and does not accumulate orphan reads on idle ticks or orphan timer waits on records.
- On completion or broken I/O, the nested `finally` cancels the retained waits and awaits them, consuming expected cancellation. The timer is disposed after this cleanup. A normal idle writer waits for readiness/timer; it does not spin on an already completed retained task. Continuous traffic can stay inside queue draining, but `AppendAsync` independently checks both byte/time flush triggers.
- Timer ticks can coalesce; a completed tick followed by a coalesced tick is a finite catch-up, not an unbounded idle busy loop. The timer is never independently disposed during normal loop execution, so its `false` result cannot continuously recur here.

## Flush, rotation, and lifetime

`dirtyBytes` increments only after the JSON bytes and LF write complete. A flush resets the dirty count only after `FlushAsync` completes. New streams reset byte/time state. Rotation awaits the existing durable close before opening the next stream; periodic flushing cannot race rotation or stream disposal. A partially emitted last row remains possible if a process dies or I/O fails between the two writes.

The optional `Func<string, FileStream>` constructor seam is internal, invoked only by the writer on creation, and retains the production default stream flags. It enables a real `FileStream` subclass to stall asynchronous flushing without changing producer mechanics, public API, persisted operations, or JSON schema. Production callers do not supply the seam.

Producer admission, queue overflow accounting, fault containment, and bounded shutdown remain the existing contract. A stalled writer can outlive a caller's shutdown deadline; this is intentional nonblocking shutdown, not evidence of drain completion. Fault health remains distinct from dropped-record counts. Final rotation/shutdown retains `Flush(true)` on the writer; no per-record durable flush or UI-thread flush was introduced.

## What 250 ms / 64 KiB means

This is a writer policy: under healthy scheduling/I/O, flush managed buffering to the OS on the timer or after a complete record crosses 64 KiB, with an elapsed-time fallback during sustained queue draining. It is **not**:

- a 250 ms guarantee from producer admission/enqueue to external readability when the queue or writer is stalled;
- a disk-media durability, power-loss, kernel-crash, or filesystem-corruption guarantee;
- recovery of an unfinished scope whose record is emitted only on disposal;
- proof that absent command callbacks did not execute;
- preservation of arbitrarily old records removed by existing per-session retention.

A complete readable prefix can survive a subsequent process termination while the OS remains alive. The missing tail/session terminal remains censored evidence. Start/checkpoint instrumentation is a separate necessary boundary improvement, not something periodic flushing invents.

## Focused test contract

The tests use repository-contained `RepoTemp` directories, not user data. The process-kill test starts its own `dotnet vstest` process with one exact test filter and a child-only environment discriminator; it kills only that owned process tree after independently opening and parsing the live JSONL prefix. Output streams are drained and exit waits are bounded. The unfinished Save is deliberately held: after termination its absence is expected, not an inferred Save failure.

The idle test observes a live prefix without orderly closure. The broken-directory test and gated asynchronous flush test exercise nonwaiting producers, overflow, writer fault/timeout containment, and eventual resumed drain. The prefix parser hard-fails malformed LF-complete rows, including the final complete row; it ignores only an unterminated physical tail. These tests establish transport behavior, not natural input, GUI receipt, Native AOT, macOS, or performance distributions.

The polling timeouts intentionally tolerate loaded CI scheduling; they do not empirically certify an exact 250 ms deadline. Existing rotation/shutdown tests remain relevant. A deterministic threshold test would add independent coverage of the 64 KiB trigger; absence of that isolated test is a scope limit, not an observed implementation failure.

## Validation evidence

Reviewed commits: `ba5b89f` (implementation, five focused tests, validation document) and `6a31dea` (README tail-contract correction and EOF whitespace only). Independently recomputed working-file SHA-256:

| File | SHA-256 |
| --- | --- |
| `src/Mote.Telemetry/JsonlTraceSink.cs` | `29BECF76763311134B801EBD16AF80D77136FCEBE575D8B87F2F5CDDC8A70798` |
| `tests/Mote.Tests/TelemetryRecoverablePrefixTests.cs` | `8BD90C1C5C39EDD692A5DF0AD30285449B01EE1762FD410D7ECB273AD6AE02C1` |

Implementation owner reported **24/24 passed**, .NET SDK 10.0.400, Windows Release, `TreatWarningsAsErrors=true`, filter `FullyQualifiedName~Telemetry`, including five new tests. See [validation procedure and outcomes](../validation/telemetry-recoverable-prefix.md). The console-only logger initially left no TRX or console-log file in `.cache/telemetry-prefix-tests`; this reviewer distinguishes the owner's execution report from independently retained raw test output. No completed test was redundantly rerun by this reviewer.

The README was corrected during review: malformed LF-complete final rows must fail, not be discarded as if every malformed tail were safely ignorable. This documentation concern is resolved by `6a31dea`. No production defect required a corrective patch.

## References

- [Microsoft PeriodicTimer.WaitForNextTickAsync](https://learn.microsoft.com/en-us/dotnet/api/system.threading.periodictimer.waitfornexttickasync?view=net-10.0): one consumer, coalesced ticks, cancellation applies to one wait.
- [Microsoft FileStream.FlushAsync](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flushasync?view=net-10.0): drains .NET buffering without flushing intermediate OS buffering to media; `Flush(true)` is a distinct operation.
- [Existing end-to-end coverage audit](observability-end-to-end-audit.md): identifies wholly censored abnormal sessions and missing command receipt/admission as distinct gaps.

