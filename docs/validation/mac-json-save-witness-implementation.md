# Mac JSON Save selector/admission witness implementation

Date: 2026-10-01. Implements the narrow positive-boundary discriminator reviewed
in `../reviews/mac-json-save-witness-design-review.md`. This diagnostic is **not
a Save reliability fix, a production performance sample, or Save acceptance**.
See `mac-json-save-routing-discriminator.md` for the recurring failure evidence
and `native-json-large-ci.md` for independent four-RID workload outcomes.

## Hooks and lifetime

- `MacEditorShell.Run` initializes only if `MOTE_NATIVE_MAC_SAVE_TRACE=1`, before
  AppKit startup. Initialization failures are contained. The default process has
  no diagnostic channel, writer thread or stream; hooks only read a null field.
- `MacEditorShell.Save` records `selector_entered` before looking up `s_current`
  or settling composition. It establishes selector entry, not successful routing
  of a particular CGEvent or subsequent Save admission.
- `NativeEditorController.StartSave` records `controller_admitted` immediately
  after `_saving=true`, before the unchanged `Task.Run`. It establishes passing
  synchronous composition/saving/recovery/path guards, not worker execution or I/O.
- The `Run` finally resets `s_current` and closes the diagnostic after the AppKit
  loop and owner-thread teardown. The two hooks are UI-owner producers; no native
  callback may produce after this lifetime boundary. No worker hooks are added.
- Normal Save ordering, composition settlement, native callback subscriptions,
  source identity, error handling and existing telemetry flushing are unchanged.

## Bounded transport and stable watermark

`NativeSaveDiagnosticSession` uses a capacity-16 enum channel with `FullMode=Wait`,
but producers call **only `TryWrite`**. Synchronous continuations are disabled.
The UI reads no clock, formats no string, starts no thread/task, writes no disk or
pipe and does not wait for queue space. Fixed ASCII frames are emitted only on
one dedicated background thread, which opens and owns the inherited stderr
wrapper and disposes it on that same thread. No stream is disposed underneath a
blocked write by the UI.

The protocol is `mote-save-diag-v1:<stage>\n`; the only stages are `ready`,
`selector_entered`, `controller_admitted`, `overflow`, `completed`. No source,
file path, title, exception text, foreign process identity or pointer is emitted.
Duplicate valid hooks remain separate records, not inferred physical key counts.

A CAS gate packs closed admission and the in-flight producer count. A producer
linearizes admission before attempting an enqueue. Failed/invalid admitted
enqueue sets a saturated loss bit **before** its release. Closure forbids later
admission; channel completion follows the last admitted release. The writer
drains the queue, reads the now-stable loss bit, emits `overflow` if needed and
then `completed`. Thus `overflow, completed` is a drained **lossy** stream, not a
healthy lossless witness. Calls rejected after closure are outside the closed
UI-producer lifetime; they cannot establish an observed callback boundary.

Owner shutdown joins for at most 100 ms. On timeout it abandons the background
writer and suppresses a terminal write that has not yet started. An already
started synchronous terminal write cannot safely be cancelled and may complete
later. **`completed` denotes only the stable producer-closed, queue-drained,
loss-accounted watermark. It does not prove a 100 ms writer join, timely process
exit, normal shutdown or Save success.** Neither the owner nor collector waits
indefinitely or closes an in-use handle to invent cancellation.

The parent separately certifies original-child normal exit and EOF when labeling
full-session transport complete. Forced kill, loss, framing errors or unavailable
startup censor absence. Missing stages always mean **not observed**, never
"callback did not run". Already received positive facts can remain useful after
forced exit. The parent collector and exact report contract are documented in
`../../benchmarks/NativeJsonLargeAcceptance/README.md`.

## Acceptance remains independent

The Python pilot enables only the original Mac GUI child via explicit
`--mac-save-witness`. Runtime control/default/reopen strip inherited opt-in state;
original evidence is finalized before replacing its subprocess with reopen.
Samples are labeled `diagnostic-on-not-performance-sample`. No global activation,
event tap, permission/TCC change, second key pair, retry, deadline relaxation,
target file reads during Save, byte-oracle weakening or telemetry flush change
is made. Exact saved bytes, normal exit, terminal trace integrity and fresh GUI
reopen still decide workload acceptance; witness health is separate metadata.

## Local portable validation

Windows development host, .NET 10, repository-local build outputs:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj --filter FullyQualifiedName~NativeSaveDiagnosticTests --no-restore --verbosity minimal
dotnet test tests/Mote.Tests/Mote.Tests.csproj --filter 'FullyQualifiedName~NativeControllerTests.Native_save|FullyQualifiedName~NativeControllerTests.Native_recovery' --no-restore --verbosity minimal
```

- New transport tests **9/9** passed: zero warmed disabled-hook allocation,
  ordered fixed/duplicate records on a background thread, deterministic saturation
  with `overflow` before `completed`, blocked startup write abandonment and
  writer-owned disposal, broken/open-failed streams, invalid enum loss, 40 rounds
  of concurrent producer/close stress, and a deterministically blocked final write
  demonstrating late watermark receipt without timely-join proof.
- Existing focused Save/recovery regression tests **7/7** passed. These are
  controller/fake-shell tests, not synthetic native input acceptance.
- The race stress test is scheduler-dependent and is not a proof that every
  possible producer instruction interleaving executed. The stable watermark
  argument above follows the atomic state transitions and release ordering;
  deterministic saturation and blocked-write cases exercise material boundaries.
- Managed compilation, including Native AOT/trim analyzers, succeeded. **Actual
  macOS Native AOT compile/launch and retained raw witness/unchanged Save outcome
  validation remain hosted CI work**, not established by these portable tests.

Independent source review is recorded in
`../reviews/mac-json-save-witness-implementation-review.md`; parent framing and
child lifecycle remain a separate integration review.

## Platform references

The [official .NET channel documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)
specifies immediate `TryWrite=false` when a Wait-mode bounded channel is full.
The [background-thread contract](https://learn.microsoft.com/en-us/dotnet/api/system.threading.thread.isbackground)
ensures a stuck diagnostic thread cannot keep the application alive. These
platform contracts justify the mechanism; they do not themselves establish
native workload reliability or arbitrary interleaving test coverage.
