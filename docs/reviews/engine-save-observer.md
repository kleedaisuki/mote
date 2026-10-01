# Engine Save observer: independent review

Date: 2026-10-01. Change reviewed: `f2f9e6359d5253144d4669180dde1b27cbcffd71`
against its parent, restricted to `Document.cs`, `DocumentSaveObservation.cs`,
and the observer tests. Local verification ran with repository HEAD
`ae03a42a324afc2ebd23d19bdd80aa0b8a5c23ab`; unrelated concurrent native changes
were not reviewed. No production code or existing byte-oracle tests were changed.

## Verdict

**No substantive defect identified in this Engine observer slice.** The added
observations preserve the Save ordering, overwrite authorization, original
exception identity, cancellation checks, and recovery ownership examined here.
They do not yet certify native command-to-UI causality, a working trace adapter,
all Native AOT RIDs, or measured whole-Save performance.

The architecture and earlier design review were read first:
[observability/provenance](../architecture/observability-provenance.md) and
[independent contract review](observability-provenance-review.md). Their narrow
observer/no-policy contract is reflected in the implementation.

## Source comparison and invariants

| Boundary | Old-to-new evidence | Assessment |
| --- | --- | --- |
| Serialization | `SaveObservedAsync` awaits `_saveGate.WaitAsync` before entering its release `finally`; cancellation before acquisition cannot release an unowned gate | Preserved |
| State capture | Snapshot, encoding, BOM, expected stamp/hash, file path, and saved state ID are still captured together under `_gate`; `Complete(snapshot.Version)` executes after leaving that lock | Correct actual saved version, not a controller estimate |
| Callback locking | Every `Begin`, `Complete`, and `Fail` is outside `_gate`; callbacks during the attempt may still hold `_saveGate` | Correct documented restriction, not reentrant Save support |
| Bytes and temporary ownership | The existing `FileMode.CreateNew`, stream encoding, chunk writes, writer flush/disposal, disk flush, and exact-byte SHA256 remain ordered the same; only the existing `created` callback transfers ownership | Preserved; no observer in the chunk loop |
| Commit | New-file move stays non-overwriting; existing targets retain final content verification before `Replace`; `committing` still precedes the actual move/replace call | Preserved |
| Primary failure | Existing persistence catches still annotate/rethrow the same instance; observation `Fail` clears its active phase before invoking user code; nonfatal callback errors are contained in `Emit` | Observer failures cannot substitute for the primary error |
| Cleanup/recovery | Cleanup still requires `owned && !commitAttempted`; failed/uncertain commits keep the owned recovery path; existing `RecordSaveOutcomeAsync` remains best effort | Preserved, including secondary inspection failures |
| Returned commit | `SavedStamp` and bookkeeping follow the returned commit; no cancellation check was inserted after commit; observer errors here cannot turn a returned commit into a nonfatal Save failure | Preserved |
| Lifetime/concurrent edits | Bookkeeping still applies the captured saved state ID only if the document is live; disposal becomes `Skipped`, while concurrent edits remain dirty | Preserved |
| Observation balance | Each executed ordinary phase ends via `Complete` or the inner/outer `Fail`; outer `Fail` is a no-op after an already-closed phase, so it does not double-terminate the primary failure | Correct for examined nonfatal paths |

The `await using var` hash reader changed to an explicit `await using` block.
Both forms dispose the reader before `WriteTempAsync` returns; the new completion
edge is emitted only after that disposal. No additional commit, retry, overwrite,
directory creation, or deletion was introduced.

## Independent executable probes

Added a separate verification file,
`tests/Mote.Tests/EngineSaveObserverIndependentTests.cs`, without modifying the
author's tests or established save-byte oracles. These three cases target
boundaries not established merely by the author's phase-sequence assertions:

1. Cancel the request token from the **successful returned move** edge. Save
   still returns success, exact bytes remain installed, and bookkeeping succeeds;
   no fictional late cancellation is emitted.
2. Inject a non-filesystem `InvalidOperationException` from post-commit stamp
   reading, while the observer itself throws `OperationCanceledException` on
   every edge. The original provider exception instance survives; installed bytes
   are exact, dirty/path state matches the prior narrow catch policy, no fictional
   cleanup/inspection is emitted, and a later Save demonstrates gate usability.
3. Inject a primary commit `IOException` and a distinct unavailable inspection
   provider, while every observer callback throws. The primary instance survives,
   the exact staged bytes remain owned as recovery, inspection degrades to
   `Unknown`, the secondary numeric error stays secondary, and there is no
   unsafe cleanup after a commit attempt.

Environment: Windows 10.0.26200, win-x64; .NET SDK 10.0.400. Command:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj --no-restore `
  --filter FullyQualifiedName~EngineSaveObserverIndependentTests `
  --logger 'trx;LogFileName=independent-engine-save-observer.trx' `
  --results-directory .cache/engine-save-observer-review
```

Result: **3 passed, 0 failed, 0 skipped**. Raw result:
`.cache/engine-save-observer-review/independent-engine-save-observer.trx`.
These probes use the working implementation, not an executable differential
baseline; the parent comparison above is source inspection. The author's
19 observer cases and previously completed 65 regressions were inspected as
supporting evidence, not rerun or relabelled independent coverage.

## Cost and scope limits

With a null observer, the conditional construction in `SaveObservedAsync`
allocates no `DocumentSaveObservationState`; conditional phase calls do not
construct observation records. There is no per-chunk observation branch, callback,
trace allocation, path copying, or new hashing pass. This is a source-level
disabled-observer property, **not proof that the whole Save allocates nothing**.
In particular, approved overwrite now uses an additional async wrapper around
`SaveObservedAsync`; no allocation/timing benchmark was conducted, and no
zero-overhead or equal-throughput claim is made for that route.

Fatal `OutOfMemoryException` is intentionally not swallowed. Balance and
successful persistence-outcome isolation apply to nonfatal observer failures;
they are not process-survival guarantees. Observer code that blocks, waits for a
same-document Save, mutates filesystem state, or otherwise supplies policy
violates the documented contract; this review does not claim containment of such
behavior.

`FailureInspection.Succeeded` means the existing best-effort inspection method
returned, including an `Unknown` byte outcome. It must not be interpreted as
successful recovery, successful commit, or verified durable target bytes by the
native adapter/reader. Likewise a phase `Succeeded` edge is a phase outcome,
not an enclosing Save outcome.

Remaining acceptance belongs to the integration owners: fixed-schema adapter
mapping and parentage, trace loss/censoring, native callback-to-worker-to-UI
coverage, retained prefixes after forced termination, and hosted Native AOT
execution. This review does not authorize relaxing those gates.
