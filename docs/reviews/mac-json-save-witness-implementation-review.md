# Mac JSON Save witness: independent transport implementation review

Date: 2026-10-01. Reviewed the working-tree implementation based on `60f7bd4`:
`src/Mote.Native/NativeSaveDiagnostic.cs`, the `MacEditorShell.Run` / Save
selector additions, `NativeEditorController.StartSave`, and
`tests/Mote.Tests/NativeSaveDiagnosticTests.cs`. This review owns only this
artifact; it does not modify product code, collectors or workflows.

## Verdict

**No substantive defect found in the reviewed target transport and hook changes.**
The implementation satisfies design-review R2 for the stated two UI-thread
producer boundaries. It is suitable for the scoped diagnostic-on integration,
subject to the independent parent-collector review (R1) and actual hosted Mac
Native AOT build/launch validation. This is not Save reliability acceptance, a
performance result, or proof that an unwitnessed callback did not execute.

No mandatory product correction is requested by this review.

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
