# Mac JSON Save selector/admission witness: independent design review

Date: 2026-10-01. Reviewed the proposed design in
`docs/validation/mac-json-save-routing-discriminator.md` at `bc55a2f`, the
ordinary Mac selector/composition/controller path, existing telemetry shutdown,
and the retained raw JSON pilot reports. This is a design review, not a product
patch, workflow authorization, native rerun, or claim of implementation safety.

## Verdict

**The two positive witnesses are useful and the opt-in transport is proportionate.
No fundamental architectural blocker was found.** Implement only after making
the two boundedness/completion contracts below explicit and testing them. These
are necessary implementation constraints, not demonstrated defects in code that
does not yet exist. A green non-gating parent job is not Save acceptance.

The witness must remain separate from acceptance: exact saved bytes, normal
shutdown, terminal trace integrity and GUI reopen are still required. Its absence
after forced cleanup is censored, not evidence that a selector/controller did not
execute. Instrumentation can change scheduling and must not be advertised as a
Save reliability repair or production performance sample.

## Required implementation contracts

### R1: Bound parsing before allocation and continue draining after retention caps

**Location:** proposed transport's parent collector paragraph. **Confidence: high.**

The design specifies 64-byte lines, 16 retained records and 4 KiB retention, but
does not prescribe framing. Implementing those limits by first calling unbounded
`readline()` or `communicate()` and then rejecting/truncating output would not
bound transient memory. The inherited stderr also carries unknown runtime output;
a long unterminated line must not allocate an unbounded buffer. Stopping reads
after 16 records would fill the pipe and strand the target writer, preventing a
real completion record and making the diagnostic itself a shutdown perturbation.

Use fixed-size byte reads and a bounded ASCII framing accumulator; discard an
overlong frame through its next newline without retaining its content. Continue
draining unknown/excess frames until EOF or bounded teardown. Persist only fixed
whitelisted stage values, booleans/capped counts and parent-local durations, never
raw rejected bytes. A stream with interleaved unknown stderr must yield unavailable
or malformed evidence rather than a fabricated stage. No shell wrapper or foreign
process stderr aggregation should sit between the owned child and collector.

Tests must include a multi-megabyte no-newline stream, unknown/non-ASCII frames,
more than 16 valid frames, partial trailing frame, read error, and EOF after forced
child exit. The retained result must stay bounded and original phase deadlines
must be unchanged. Python's subprocess documentation explicitly distinguishes
pipe deadlock and in-memory buffering; `communicate()` is not a bounded-output
collector ([official documentation](https://docs.python.org/3/library/subprocess.html#subprocess.Popen.communicate)).

### R2: Make terminal completion a producer/loss watermark, not just an empty queue

**Location:** proposed `completed` semantics and normal diagnostic shutdown.
**Confidence: high for the race; conditional on implementation topology.**

An empty queue alone does not prove completion. For example: a hook begins
recording, shutdown closes the channel, the hook's `TryWrite` fails, the writer
sees drain and emits `completed`, and only then the hook sets the loss flag. A
parent could incorrectly regard this as a healthy completed stream and draw a
negative conclusion about a missing admission. Existing normal telemetry has
explicit producer admission/drain accounting for this exact class of problem
(`JsonlTraceSink.ReleaseProducer` and `ShutdownAsync`); queue reuse must not omit
the semantics that make its terminal state meaningful.

The smallest remedy for **only these two UI-thread hooks** is owner-thread
closure after the AppKit loop returns, with documented no-later-producer
invariants, followed by writer drain and a final stable loss read. If initialization,
shutdown, or future worker hooks can overlap producers, use a linearized admission
gate/in-flight accounting, or conservatively never classify `completed` as a
healthy producer watermark. Do not transplant the whole telemetry subsystem just
to instrument two UI-thread points. Distinguish rejection after closure from queue
overflow internally, or conservatively classify either as loss; a saturated boolean
is enough for interpretation.

Only emit terminal `completed` after successful drain and stable loss accounting;
emit any `overflow` before it. If the writer throws, is stuck, drain times out, or
the collector truncates/malforms records, absence remains censored. On timeout do
not emit a success terminal, wait indefinitely, dispose a stream concurrently with
a stuck writer, or run synchronous I/O on the UI thread. A background thread may
be abandoned at process termination; a foreground thread must not keep the app
alive. Test producer-versus-closure races and a blocked/broken inherited pipe.

## Source-backed hook semantics

| Hook / observation | What a received marker establishes | What it does not establish |
| --- | --- | --- |
| Beginning of `MacEditorShell.Save` (`moteSave:`) | Native Save selector entry occurred | Current shell exists; composition settled; Save admitted; posted CGEvent caused that entry |
| Immediately after `NativeEditorController.StartSave` sets `_saving=true` | Synchronous ordinary Save guards passed and path/document were selected | `Task.Run` succeeded; worker executed; Save I/O began or completed |
| Both received, exact Save outcome absent | Failure lies later than the witnessed admission boundary, or outcome observation is incomplete | Worker starvation versus persistence failure versus UI acknowledgement failure |
| Selector only | At least selector entry occurred | Composition/guard rejection versus admission-witness loss |
| Neither after forced exit | Neither was observed | Non-delivery, selector non-entry, or a specific product defect |

Verified path: `MacEditorShell.cs:2161` enters `NotifyAfterComposition`, whose
`CommitPendingText` and error/modal handling precede the `SaveRequested` callback
(`:1664`). `NativeEditorController.cs:664` repeats composition settlement, rejects
already-saving state, branches to pending recovery, selects a path and computes
identity before `_saving=true` and `Task.Run`. The proposed admission location is
therefore accurately named, not a worker-start witness. No callback should be
added/reordered and no exception from the diagnostic may escape the unmanaged
selector. Retain original Save behavior even if initialization, enqueue or writer
startup fails; do not lazily initialize a potentially allocating/thread-starting
singleton from that selector.

The two hooks do not count physical key events. The one-attempt policy remains
the harness's responsibility. Preserve duplicate valid markers as bounded
diagnostic counts/stages rather than silently coalescing them into a claimed single
selector execution. Associate evidence with the exact subprocess session: collect
the original process before replacing `child` for GUI reopen, and keep reopen
evidence separate. A process-lifetime queue needs no document/path identity for
this single-Save fresh-process experiment.

## Raw evidence and causal value

The historical 36809964231 ARM64 100 MiB failure has one attempted Command-S
pair, no certified Save, original final disk bytes, forced cleanup and a zero-byte
normal trace. The four activity fields did not exist in that run. It therefore
cannot establish either target inactivity or absence of Save worker entry.

I additionally inspected the raw **36814164862 x64 1 MiB** report at
`.cache/ci-36814164862-json-osx-x64/.cache/ci-inventory/osx-x64/native-json-large.json`
(source `874a7ecaec291af12b2501fc815024271fbb08c3`), without rerunning the pilot.
It records `status=failed`, `phase=save-exact-bytes`, `TimeoutError`, one two-post
attempt with `execution_acknowledged=false`, failed owned close, forced cleanup,
and original working SHA `9004bc8156e480461b02205536ee607414186c776078a8f35b20716c7e668355`.
Both the pre-Save guard and final failure snapshot have target active/frontmost
true, source focused/dirty/Complete true, main window true and window AXFocused
false. These are sequential samples, not full routing history. They weaken the
simple inactive-at-dispatch explanation, but do not certify event delivery or
handler execution. The same report's x64 100 MiB case passes; this review does not
replace the owner's independent four-RID matrix audit.

Existing keyDown stage tracing does not witness the Save menu selector. Current
pilot `probe.py:449-450` sends stderr to DEVNULL. Normal trace persistence buffers
and flushes on rotation/normal shutdown, so forced-kill absence is uninformative.
The proposed parent-retained pipe can preserve **already received positive facts**
without target file flush, fsync, Save retry or new AX/TCC behavior. It cannot
guarantee delivery of markers that remained queued or in a blocked writer.

## Performance, platform and privacy assessment

- `FullMode=Wait` with **only `TryWrite`** rejects immediately when full;
  `AllowSynchronousContinuations=false` avoids running consumer continuations on
  the producer. Both match the [official .NET channel contract](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels).
  Do not replace the hooks with `WriteAsync`, `WaitToWriteAsync`, Console writes,
  thread joins, locks shared with the writer, or per-marker `Task.Run`.
- Disabled mode should be a preinitialized null/no-op reference and no clock,
  channel, thread or environment lookup on ordinary editing/Save paths. The two
  Save hooks need not instrument keyDown, drawing or AX polling. Enabled mode
  adds a thread and scheduling work and is explicitly a diagnostic.
- A dedicated **background** writer must actually perform writes on that thread;
  an async entry delegate that resumes via the shared thread pool defeats the
  intended independence from Save scheduling. Built-in channels, fixed enums,
  fixed byte literals and static calls fit Native AOT without reflection or
  dynamic code. Actual Mac AOT compile/launch and strict single-file inventory
  still require hosted validation; design inspection is not such validation.
- Open/write the raw inherited stderr stream on the writer, avoid Console's
  formatting/text-writer path and target-side buffered files, and catch transport
  failure. Small records have no document content, paths, pointers or exception
  text. Parent receipt timestamps must not be subtracted from target clocks or
  labeled selector execution time. Unknown stderr must never enter artifacts.
- Initialize collector state before `Popen`, then attach/start continuous draining
  immediately after receiving the child's pipe; the pre-created pipe buffer bridges
  the startup interval. There is no literal child stderr to read before launch.
  Include startup-failure cleanup; a two-second bounded collector join cannot
  turn into a second indefinite read/join or postpone the existing kill.
- No global activation, event tap, extra permissions, second Save, source reads
  during Save, byte-oracle weakening, or change to ordinary telemetry flushing is
  required or justified by this design.

## Optional alternatives and next boundary

Do not require a new heartbeat protocol now: the minimal proposal openly accepts
censored negatives and positive markers already distinguish useful boundaries.
A target-owned AX attribute backed by an enum/bitmap would avoid the writer, but
loses post-kill evidence and depends on responsive AX polling; it is not superior
for this specific failure. A synchronous two-line stderr hook is shorter but can
block the UI on a full pipe and perturb the boundary under test. A bespoke
two-slot queue/event could work, but is not clearly simpler than tested built-in
channels once closure/loss semantics are included.

If both markers appear in a failing run, the next informative extension is a
separately reviewed fixed `worker_entered` marker at the beginning of the existing
Save task, before normal telemetry scope creation. It is not needed for the first
two-boundary implementation, and should not grow into broad logging without a
specific discriminating question. No marker is a substitute for successful Save.

## Review limits

No implementation was available for lifecycle/race validation, and no AppKit,
Native AOT or forced-pipe experiment was run by this review. Required tests above
are contracts for subsequent implementation review. I wrote only this review
artifact, did not edit product/probe/CI, did not dispatch workflows, and did not
relax any acceptance predicate.
