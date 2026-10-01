# Normal-exit trace drop: evidence, mechanism, and bounded repair

## Hosted observation (not repaired target acceptance)

CI run `36804628122`, source `42462d764fb5183d6ad8f03f30f9957bd54ebad5`,
Native AOT macOS x64 ordinary-product JSON 1 MiB pilot reached edit, exact
explicit Save, and normal process exit. The pilot correctly rejected causal
trace evidence before attempting reopen because the trace contains one
`telemetry.dropped` record with count 1. Do not turn this into a passed pilot.

Retained artifact under repository `.cache/ci-run-36804628122-json-x64`:
`.../1/home/traces/mote-trace-7ea6222236a34c059e5e66b4cb8013d4-000001.jsonl`.
It contains **32 complete records**, one file/sequence, 30 ordinary records,
then `telemetry.dropped` count 1 and terminal `mote.session`.
The last ordinary record is `view.layout`, version 1; the Save scope and
`save.completed` are present. The default queue capacity is 4096.

With normal drain, a genuinely full 4096-slot queue would produce far more
retained rows than these 32; there are no later rotated files or earlier
sequence gaps to explain away a queue-sized accepted backlog. The code has
no callers explicitly emitting `TelemetryEvent.DroppedEvents`. Therefore
capacity overflow does not explain this particular retained sequence.

The original sink counts both a full queue and a completed queue's failed
`TryWrite` as dropped. A background scope that is already active when
shutdown seals the channel can reproduce exactly this mechanism. Controller
disposal cancels background analysis but does not await all background tasks.
The hosted trace does **not** identify the rejected operation, however; a
held parse scope is a demonstrated causal mechanism, not proof that the
particular hosted missing record was a parse scope rather than an old mark.

## Deterministic discrimination

The public regression test starts a scope, invokes `ShutdownAsync`, then
disposes the scope **after shutdown starts**. Async execution reaches its
first incomplete await synchronously; no sleep or scheduler ordering guess
is used. Before the change the test failed in 52 ms because its persisted
`analysis.parse` record was absent. After the repair both direct `Start` and
explicit-parent `StartChild` preserve that record before the terminal session
with no dropped aggregate.

Additional controlled tests cover:

- synchronous `ActivityStarted` listener beginning shutdown inside Activity
  construction: admission must precede Activity creation;
- a throwing Activity listener: creation failure releases its lease;
- zero-budget closure of an otherwise empty queue: a held scope's later
  disposal increments dropped by exactly 1, not an overflow; double Dispose
  neither duplicates loss nor releases admission twice;
- an old delayed mark rejected after closure: it remains truthfully counted;
- rejected fresh admission and facade calls after shutdown: no accepted work,
  no spurious dropped count;
- a held scope beyond a positive shutdown budget: closure remains bounded;
- oversized and most-negative deadlines: existing nonthrowing shutdown
  behavior is retained.

The existing 80,000-event concurrent capacity-pressure test remains distinct.
It verifies positive drop accounting under load, but is **not** described as
a deterministic capacity-saturation ordering test. No writer scheduler hook,
thread-pool starvation, or filesystem stall was added merely to force it.

## Implementation and race argument

The sink uses one atomic integer: the sign bit closes producer admission and
the remaining bits count admitted producers. Acquisition compares/exchanges
only an open state; closure atomically sets the sign bit. Thus every
acquisition linearizes either before close (counted, must finish) or after
close (not admitted). A zero count at close, or the last release after close,
signals a single asynchronous completion source. This is internal only;
no public API, trace schema, operation name, or user configuration changed.

Scope admission happens before creating an Activity. Exceptions release it;
exactly-once scope disposal enqueues its final record before release. Direct
events hold transient admission through serialization-free enqueue attempts.
Shutdown detaches the facade as before, closes admission, waits already
admitted producers, then completes/drains the channel. Producer wait and
writer drain use **one caller-selected budget**, default two seconds, not
two consecutive budgets. No producer waits for disk or takes the writer lock.
If admitted work remains when its budget expires, a flag is published before
channel completion. The terminal session then has existing `cancelled`
status, not success, even if an actual rejected enqueue occurs after the
writer's final drop aggregate. This identifies incomplete evidence without
guessing how many future records the unfinished scope would produce.

The acceptance boundary is atomic admission, not an earlier nullable sink
read. Fresh Start/StartChild/event calls losing admission are inert, like
disabled tracing; scopes return null and no accepted record is lost.
Delayed marks are intentionally not indefinite lifetime leases: owners must
finish/cancel them before process shutdown. Rejected retained marks and
scopes exceeding the budget retain loss accounting. A loss after the final
aggregate cannot be persisted retroactively. Zero recorded drops is not a
certificate of every concurrent instrumentation attempt or unfinished mark.

This follows .NET's producer/consumer channel model: producer completion is
a lifecycle boundary, not merely an empty/full-queue state. See official
[channel APIs and completion behavior](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels).
Existing design remains aligned with the low-overhead common instrumentation
and bounded pipeline motivation documented in
[Dapper](https://research.google/pubs/dapper-a-large-scale-distributed-systems-tracing-infrastructure/);
no sampling or unbounded buffering is introduced to hide a lifecycle loss.

## Verification

Windows x64 local .NET 10 Release JIT, repository-local `.temp/tests` scratch:
SDK `10.0.400`. Retained 1 MiB trace SHA-256:
`AAC3FE871369CFB466E02F0B51B6DD40CE375BF246A76D2363E528A2AF63BE9E`.

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore `
  --filter 'FullyQualifiedName~TelemetryTests|FullyQualifiedName~NativePaintTraceTests'
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-build
```

Focused suite: **25/25 passed**, including the existing disabled tracing
zero-managed-allocation hot-path test and native draw contracts. Full suite
after the final terminal-status implementation: **1166/1166 passed**, 50 s.
Independent review is tracked in
`docs/reviews/trace-drop-normal-exit.md`.

Native AOT macOS execution of this repair is still required. A rerun must
preserve exact Save/normal exit, retain all required causal records with no
drop aggregate, then finish exact GUI reopen. Do not claim the particular
hosted missing record was fixed merely from local mechanism tests.
