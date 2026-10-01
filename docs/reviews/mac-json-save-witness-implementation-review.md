# Mac JSON Save witness: independent transport implementation review

Date: 2026-10-01. Reviewed the working-tree implementation based on `60f7bd4`:
`src/Mote.Native/NativeSaveDiagnostic.cs`, the `MacEditorShell.Run` / Save
selector additions, `NativeEditorController.StartSave`, and
`tests/Mote.Tests/NativeSaveDiagnosticTests.cs`. This review owns only this
artifact; it does not modify product code, collectors or workflows.

## Verdict

**Final verdict: no outstanding substantive issue in the reviewed diagnostic integration. F1 was identified and corrected in `83f469b`; its evidence and resolution are retained below.**
The implementation satisfies design-review R2 for the stated two UI-thread
producer boundaries. It is suitable for the scoped diagnostic-on integration,
subject to the independent parent-collector review (R1) and actual hosted Mac
Native AOT build/launch validation. This is not Save reliability acceptance, a
performance result, or proof that an unwitnessed callback did not execute.

The final verdict includes collector refinement `9f439b0`, scoped workflow `77547c7`, and independently reviewed wake correction `83f469b`. No mandatory correction remains in this scope.

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

**Remedy (implemented in `83f469b`):** keep bounded TryWrite/TryRead and producer admission semantics, but
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


## F1 resolution and final integration verdict

Reviewed the exact wake correction committed as `83f469b` before commit; the
owner subsequently reported 10/10 focused transport tests passing. Collector
refinement `9f439b0` has 41/41 reported portable collector/probe tests passing.
These are owner execution results, not redundant reruns by this reviewer.

The writer now drains only via TryRead and waits synchronously on AutoResetEvent.
No WaitToReadAsync, ValueTask conversion or task-pool readiness continuation
remains. Successful enqueue signals before producer release. A retained event
signal bridges the interval between observing an empty queue and WaitOne; a
collapsed duplicate signal is harmless because the reader drains all available
stages. Final producer closure signals too. Observing `int.MinValue` proves
closed admission and no remaining admitted producer; the second drain after
that observation catches any enqueue between the preceding empty read and
closure observation. Loss publication still precedes the stable watermark.

Only successful writer join retires the wake handle, once. Abandonment retains
it rather than disposing beneath a live WaitOne. A final producer can publish
its decrement just before its redundant channel-complete/signal and race an
already-joined writer's wake retirement; its Set exception is contained and
cannot escape the unmanaged hook or modify the already-stable loss watermark.
Raw output disposal remains writer-owned. Repeated Shutdown joins do not
redispose the wake handle.

The new open-lifetime test waits for selector output before recording admission,
and then waits for admission output before shutdown. Thus a closure wake cannot
mask a missing record wake. The existing blocked-terminal test preserves honest
late-write semantics. The source removes the identified shared-thread-pool wake
dependency; neither this test nor this review is a global ThreadPool starvation
experiment, nor proof that the historical Mac Save failure was starvation.

**Final verdict: F1 resolved; no outstanding substantive finding in reviewed
R1/R2 target transport, original-child collector, acceptance integration or scoped
JSON workflow.** Proceed with the planned hosted diagnostic-on validation; do not
call its eventual success a causal Save repair, ordinary performance sample or
complete release acceptance. Previously stated Mac/native/physical-input review
limits remain in force. No production, probe or workflow file was edited by this
reviewer and no workflow was dispatched or branch pushed.

## Hosted invocation defect and narrow wrapper correction

Follow-up review, 2026-10-01: inspected the retained original Mac job logs under
`.cache/ci-36816778414-save-witness/` for
[run 36816778414](https://github.com/kleedaisuki/mote/actions/runs/36816778414),
source `d6b355b`. Both report 41 portable tests passing, then argparse exit 2 with
`unrecognized arguments: - - m a c - s a v e - w i t n e s s`, followed by the
wrapper's failure and missing JSON report warning. These are **prelaunch harness
failures**, not product Save failures or absence of selector/admission execution.
No original JSON pilot process, Save outcome or witness stream from this run can
be inferred. The prior source review missed this PowerShell scalar-output/splat
integration defect; its successful verdict did not establish actual hosted argv.

Reviewed the owner's narrow working-tree correction to `.github/workflows/ci.yml`
and new `benchmarks/NativeJsonLargeAcceptance/test_invocation.ps1`. No substantive
issue found in the correction:

- The workflow first assigns `$witness = @()`, then assigns the one-element array
  inside the Mac branch. It no longer assigns an if statement's enumerated output
  to the variable, so neither the Mac scalar string nor the Windows null branch
  is splatted. The actual `@witness` invocation remains unchanged.
- The guard requires one exact production two-line block; it replaces only the
  automatic platform variable with its test-owned Boolean. The scriptblock's
  unary-comma return prevents guard output from flattening the returned array.
  It checks array identity and passes the actual splat to a real Python argv-echo
  child for both Mac RIDs and both Windows RIDs. Expected argv is exactly one
  complete flag on Mac and zero optional arguments on Windows, not a substring
  or PowerShell-only simulation.
- I independently recalculated the guard's normalized LF and CRLF SHA-256 values;
  both match the workflow pins. A guard throw is terminating under Stop error
  handling, and therefore prevents the following Python suite/pilot invocation.
  Native argv-control failures are explicitly checked too.
- The correction changes only invocation construction and its guard, not target
  hooks, collector/probe code, Save attempt count, exact-byte/trace/reopen oracle,
  phase deadlines or Grid workflow steps. The owner reports 4/4 local argv
  controls passing; this reviewer did not duplicate that completed test run.

The array/output behavior is consistent with Microsoft's
[PowerShell array documentation](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_arrays)
and [unary comma operator documentation](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_operators).

**Correction approved for commit.** Hosted execution of the corrected wrapper and
actual original-child Mac witness collection remain unverified until the next
run. Neither the initial green parent job nor the local argv controls establish
Mac Save acceptance. This review edited only this artifact and did not push or
dispatch any workflow.
