# Mac JSON Save witness: independent transport implementation review

Date: 2026-10-01. Reviewed the working-tree implementation based on `60f7bd4`:
`src/Mote.Native/NativeSaveDiagnostic.cs`, the `MacEditorShell.Run` / Save
selector additions, `NativeEditorController.StartSave`, and
`tests/Mote.Tests/NativeSaveDiagnosticTests.cs`. This review owns only this
artifact; it does not modify product code, collectors or workflows.

## Verdict

**Initial transport review found no loss-watermark defect; the follow-up review below identifies one necessary writer-wakeup correction.**
The implementation satisfies design-review R2 for the stated two UI-thread
producer boundaries. It is suitable for the scoped diagnostic-on integration,
subject to the independent parent-collector review (R1) and actual hosted Mac
Native AOT build/launch validation. This is not Save reliability acceptance, a
performance result, or proof that an unwitnessed callback did not execute.

The initial verdict is superseded by finding F1 below until its correction is reviewed.

## Evidence and lifetime analysis

| Contract | Source-backed assessment |
| --- | --- |
| Default-off path | The static field is initially null. `Record` does only a volatile reference read and conditional dispatch; no environment lookup, clock, channel creation, thread startup or formatting occurs in the hook. Environment opt-in is read by Mac `Run` before AppKit setup. Windows never initializes the session. |
| Selector meaning | `MacEditorShell.Save` records before resolving `s_current` or invoking `NotifyAfterComposition`; it establishes selector entry, not a valid shell or settled composition. The ordinary composition/callback order is unchanged. |
| Admission meaning | `StartSave` records immediately after `_saving=true`, after composition, already-saving, pending-recovery, picker/path and identity checks; it precedes the existing `Task.Run`. It establishes synchronous admission, not worker execution or successful Save. |
| Producer boundedness | Channel capacity is 16, full mode is Wait, but producers use only `TryWrite`; synchronous continuations are disabled. The fixed enum whitelist rejects unknown stages as loss. No producer performs raw I/O, joins a writer, or creates a task per marker. |
| Closure/loss ordering | CAS sets the high admission bit and prevents later admission. Admitted producers publish loss before their finally decrement. Only closure with zero admitted producers, or the last decrement to `int.MinValue`, completes the channel. The writer cannot observe completed drain before all admitted loss writes. |
| Completion meaning | Writer drain precedes its final stable loss read. Overflow is emitted before completed. Successful writes of completed certify the closed admitted-producer watermark, not Save success or trace/reopen acceptance. |
| Broken/stuck pipe | Open/write exceptions are caught on the dedicated background writer. Shutdown does not dispose its stream, wait for producer release, or perform pipe I/O; it attempts a 100 ms join and sets abandonment when that expires. A stuck background thread does not keep the process alive. |
| Teardown | The diagnostic closes in the outer `Run` finally, after AppKit loop return and owner-thread accessibility/canvas/grid/pool teardown; `s_current` is cleared before diagnostic shutdown. The two production hooks are ordinary UI-thread callbacks, with no new worker producers. |
| Native AOT | The source uses ordinary static calls, fixed enums/byte literals, built-in channels and a direct Thread delegate, with no dynamic code, reflection-based activation, serialization metadata or new native sidecar. Source compatibility is not an actual Mac AOT publish result. |

### R2 race, explicitly traced

Suppose a producer is admitted just before shutdown and has not yet called
`TryWrite`. Shutdown closes admission with a nonzero low-bit count, so it does
not complete the channel. That producer either enqueues the stage or sets
`_lost=1`, then decrements. The last admitted producer completes the channel.
The writer finishes reading before checking loss and writing terminal records.
Consequently a failed admitted enqueue cannot race behind a healthy completed
producer watermark. Producers rejected after closure are outside that watermark,
not silently accepted work.

The real hooks are serialized on the AppKit owner thread and close after native
teardown. Thus the admission gate is conservative support for accidental overlap;
it does not introduce a dependency on producer progress into the shutdown caller.
This conclusion depends on keeping future worker hooks out of this diagnostic
unless their lifetime is explicitly modeled.

### Terminal timing nuance

The abandonment flag suppresses a terminal write that has not passed its final
check. It cannot cancel a raw write already in progress, and the implementation
correctly avoids disposing the stream underneath that write. A completed record
which finishes late remains only a producer watermark; it must not be interpreted
as evidence that the writer join met 100 ms, or that normal product close/Save
succeeded. This is a scope limit, not a demonstrated false-watermark defect: all
admitted producers and loss publication still precede that terminal write.

## Test assessment

The supplied tests cover disabled warmed-hook allocation, duplicate/order
preservation, deterministic capacity-16 overflow, blocked initial write with
bounded abandonment and writer-owned disposal, failed opening/broken writing,
invalid-enum loss, and producer/closure concurrency. Their assertions distinguish
thread join from a completed transport record; failed-pipe join returning true
cannot fabricate completed output.

The concurrency test is scheduler-dependent: shutdown may close admission before
any scheduled producer enters, and it does not deterministically pause a producer
between admission and enqueue/loss publication. The source ordering above supports
the contract, but the test alone is not exhaustive evidence for that interleaving.
A future targeted test could expose an internal test seam or use a suitably
controlled admission/enqueue boundary; adding machinery solely for that test is
not required for this serialized two-hook scope.

The blocked-write test uses a 10 ms injected budget and a broad sub-second upper
bound; the production facade explicitly supplies 100 ms. It proves bounded join
and no concurrent disposal, not an operating-system wall-clock scheduling guarantee
or the behavior of an actual Mac inherited pipe. I did not rerun the already
reported eight passing focused tests, per repository instructions against
redundant validation. I inspected their assertions and executable paths instead.

## R1 and integration limits

I briefly inspected the concurrently developed collector's bounded byte-read /
frame-discard shape, but did not review its complete launch/kill/reopen integration
or independently certify its tests. R1 remains assigned to the separate collector
review. In particular, collector health must require clean EOF, valid framing/order,
no target overflow, no retention loss and a real terminal record; positives after
forced kill remain useful while absent records stay censored. The original-process
collector must finish before replacement with a reopen child.

No AppKit execution, Native AOT publish, hosted experiment, workflow dispatch,
physical input, trust/TCC change or Save/trace/reopen acceptance was performed by
this review. The diagnostic adds enabled-mode scheduling work and must retain its
explicit diagnostic-on/not-performance-sample labeling. The current Mac Save
failure cause remains unresolved.


## Follow-up: collector and workflow integration

Reviewed collector/probe refinement `9f439b0`, workflow commit `77547c7`, and
transport/tests commit `19aeabf`. No tests were rerun by this reviewer; the owner
reported nine focused C# tests plus seven existing Save/recovery tests passing.
The deterministic blocked-terminal test now establishes the late-terminal scope
limit described above rather than claiming that an already-started syscall can
be canceled.

### F1 — Writer wake still depends on the shared thread pool (necessary correction)

**Location:** `NativeSaveDiagnosticSession.WriteLoop`,
`WaitToReadAsync().AsTask().GetAwaiter().GetResult()`. **Confidence: high.**

When the queue is empty, WaitToReadAsync returns a pending channel operation.
AsTask registers a continuation to complete its Task. With synchronous
continuations disabled, channel producer completion queues that continuation on
the shared thread pool. The dedicated writer blocks on this Task and cannot wake
until that pool executes the completion continuation. Therefore thread-pool
starvation can delay both markers and terminal drain even though the actual
stream writes execute on the dedicated thread. This undermines the explicit
purpose of avoiding the Save task pool as a prerequisite for positive witnesses.
It does not falsify received positive stages or the producer/loss watermark.

The version-matched .NET 10 sources show the chain:
[BoundedChannel.WaitToReadAsync](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Threading.Channels/src/System/Threading/Channels/BoundedChannel.cs)
creates the waiting operation using its asynchronous-continuation setting;
[ValueTask.AsTask](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/ValueTask.cs)
registers the conversion continuation;
[AsyncOperation.SignalCompletion](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Threading.Channels/src/System/Threading/Channels/AsyncOperation.cs)
queues that continuation when forced asynchronous. Source inspection establishes
the dependency; this review did not reproduce actual Save-worker starvation.

**Remedy:** keep bounded TryWrite/TryRead and producer admission semantics, but
replace asynchronous readiness waiting with a dedicated synchronous wake or
bounded diagnostic-only polling. A wake must cover successful enqueue and final
closure without missed signals or concurrent disposal against producers. Do not
fix this by enabling synchronous channel continuations on the UI hook or by
adding another Task.Run. Review the corrected wake and its tests before enabling
the hosted diagnostic. This finding was communicated promptly to the owner, who
is implementing the correction.

### R1 collector boundedness and privacy: no substantive issue found

- Reads request 1024 bytes from an unbuffered owned subprocess pipe. The frame
  accumulator never exceeds 64 bytes; a longer frame is discarded through its
  next LF before any whole-line allocation. Only a bounded frame is converted
  to bytes for whitelist lookup.
- Only five fixed stage names, bounded counters/flags and parent-local durations
  are retained. Unknown/non-ASCII/partial/overlong frames never enter artifacts.
  Records cap at 16; counters saturate at 65535; record retention and counter loss
  remain explicit. The reader continues draining after either cap.
- Thread-start/read/close errors are content-free state. The daemon is joined for
  at most two seconds; a blocked reader is not concurrently closed. Snapshots
  copy the rows/counts under the lock and cannot subsequently mutate.
- Health requires independent original normal exit, EOF, correct ordering, exactly
  one ready/completed, no overflow/retention/counter loss, no malformed framing,
  no read/attach error and no join timeout. Completed plus EOF after forced kill
  is explicitly censored, even if the terminal preceded that kill.

The tests exercise generated multi-megabyte no-newline streams with a bounded
memory check, recovery after LF, rejection privacy, fragment boundaries, caps with
continued draining, ordering, errors, bounded joins/detached snapshots and a real
child kill after positive records including completed were received. The actual
kill test distinguishes positive retention from normal-exit certification.

### Original-child association and acceptance: no substantive issue found

Collector state exists before Popen and draining attaches immediately afterward.
`original_child` remains distinct from the mutable `child` used for reopen;
`original_forced_cleanup` is recorded before its kill. Successful original
collection finishes before trace audit and reopen, and is not overwritten by
reopen evidence. Failure cleanup happens before collection; an original exit-code
zero can certify transport completeness without changing a failed workload to
Save acceptance. Popen failure retains censored preinitialized state.

Environment construction strips an inherited witness switch. Runtime control
runs before explicit original-child opt-in; reopen constructs a fresh stripped
environment and retains DEVNULL stderr. Windows/default runs create no collector.
The CLI rejects the Mac witness flag on a non-Mac host/RID. Reports retain driver,
collector and binary hashes plus source commit and explicit diagnostic-on labels.

No edits were made to Save input dispatch, one-attempt counting, exact-byte
comparison, source readiness/semantics, normal-exit checks, trace checks, reopen
checks, trust/focus guards or original phase deadlines. The separate bounded
collector join does not postpone killing. Witness availability never changes the
workload's pass/fail predicate.

### Workflow scope

Commit `77547c7` changes only the existing non-gating JSON step: test discovery
includes both portable suites, a Mac-only CLI flag enables original-child witness,
and successful Mac output explicitly denies reliability-fix/performance claims.
The two-case report predicates, 20-minute step budget, continue-on-error status
and existing artifact upload remain unchanged. This commit does not change Grid
steps; the unrelated Grid hash update elsewhere in the branch is outside this
review. A green job still cannot substitute for raw diagnostic/report acceptance.

Actual hosted Mac pipe behavior, Native AOT compilation, launch, complete report
and the unresolved Save failure remain outside this source review. Overall
integration verdict is **await F1 writer-wakeup correction**, not a failure of
R1 framing or R2 producer/loss accounting.
