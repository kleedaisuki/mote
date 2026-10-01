# Independent review: normal-exit trace producer lifetime

## Verdict and scope

Reviewed the worker-frozen change on 2026-10-01 in
`src/Mote.Telemetry/JsonlTraceSink.cs`, `MoteTelemetry.cs`, `README.md`,
`AssemblyInfo.cs`, `tests/Mote.Tests/TelemetryTests.cs`, and
`docs/validation/trace-drop-normal-exit.md`. No remaining substantive defect
was found within this scope. Production and test files were not modified by
the reviewer. This is a local managed-code review, not macOS Native AOT or
four-RID product acceptance.

## Resolved during review

1. **Shutdown exception compatibility.** The initial producer wait caught
   only `TimeoutException`, allowing an oversized positive deadline to escape
   before completing the channel. The frozen change restores the existing
   nonthrowing exception handling and seals the queue. The most-negative
   `TimeSpan` initially overflowed during remaining-budget subtraction; the
   frozen change returns after sealing for every nonpositive deadline.
   Dedicated oversized/negative deadline tests cover both paths.
2. **Truthful terminal evidence.** A timeout could originally emit a successful
   terminal session before a held producer disposed and was counted as lost.
   The frozen change publishes an incomplete-drain flag before channel
   completion and writes terminal `cancelled` when admitted work remains.
   It deliberately does not guess a drop count. Zero/positive-budget held-scope
   tests check the persisted terminal status, not merely in-memory accounting.

## Concurrency and lifecycle assessment

- Acquisition CAS and closing `Interlocked.Or` act on the same state. A
  successful acquisition linearizes before closure and owns one producer
  lease; a failed acquisition after closure owns none. Setting the sign bit
  preserves admitted count. Closure at zero or the last release after closure
  completes the asynchronous completion source.
- `Start`/`StartChild` acquire before Activity creation and release on creation
  exceptions. Scope disposal uses its existing exactly-once exchange and
  releases in `finally` after the final enqueue attempt. Direct event,
  elapsed-mark, and Save-failure paths release their transient leases in
  `finally`. No new producer-side disk wait or writer lock was introduced.
- The acceptance boundary is lease acquisition, not a stale facade sink read.
  Fresh scopes/events losing admission are intentionally inert; retained marks
  represent pre-existing intervals and are counted when their completion loses
  admission. The README describes this distinction, so zero drops must not be
  interpreted as a census of every concurrent call or unfinished mark.
- A retained mark is not an indefinite lease. Its owner must complete/cancel
  it before shutdown. A loss arriving after final writer aggregation cannot be
  appended retroactively. The change documents this material limitation rather
  than claiming a complete trace merely from an absent drop aggregate.
- Producer wait and writer drain share one monotonic budget. A stalled scope
  does not gain a second writer-sized budget. On timeout the writer may finish
  in the background, as before. A cancelled terminal status is conservative
  evidence of unfinished admitted work at sealing, not an exact record-loss
  count or a claim of physical file durability after the caller's deadline.
- Disabled paths still return before producer state, Activity creation, or
  allocation. Public signatures, schema fields, operation allowlists and
  privacy boundaries remain unchanged; the terminal status uses the existing
  enum. The new friend assembly exposes internals only to `Mote.Tests` for
  lifecycle assertions.

## Evidence and verification

Independent final command, Windows x64 local .NET 10 Release JIT:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-build `
  --filter 'FullyQualifiedName~TelemetryTests|FullyQualifiedName~NativePaintTraceTests' `
  --logger 'console;verbosity=minimal'
git diff --check -- src/Mote.Telemetry tests/Mote.Tests/TelemetryTests.cs
```

Result: **25/25 passed**, zero failed/skipped; diff check clean (only the
repository's CRLF normalization warning). Coverage includes explicit-parent
held scopes, creation/shutdown reentrancy, throwing Activity listener lease
rollback, exactly-once disposal, late marks, nonpositive/oversized deadlines,
bounded positive timeout, capacity pressure, privacy, and disabled allocation.
The earlier preliminary snapshot passed its then-current 9 telemetry tests;
the final 25-test run supersedes that result. Full-suite execution belongs to
the worker's validation record and is not claimed independently here.

The owner document distinguishes 32 retained hosted rows and a count-one drop
from the deterministic held-scope mechanism. The hosted trace does not identify
which operation was rejected. This repair must still be tested in macOS Native
AOT with required causal records, normal exit, exact Save and GUI reopen; local
tests do not establish that the particular hosted missing record is repaired.

Runtime contracts checked against primary references:
[atomic CompareExchange](https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked.compareexchange?view=net-10.0),
[Task.WaitAsync and usage exceptions](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.waitasync?view=net-10.0).
The production/academic tracing rationale already recorded in the owner
document remains applicable; no sampling or unbounded buffering was added.
