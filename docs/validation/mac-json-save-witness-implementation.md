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
pipe and does not wait for queue space. Enabled admission signals a synchronous
`AutoResetEvent` after enqueue; producer closure also signals it. The writer drains
with `TryRead`, blocks only its own thread in `WaitOne`, and performs one stable
final drain after observing all producers released. No async channel waiter is
registered. Fixed ASCII frames are emitted only on
one dedicated background thread, which opens and owns the inherited stderr
wrapper and disposes it on that same thread. No stream is disposed underneath a
blocked write by the UI.

An initial implementation used `WaitToReadAsync().AsTask().GetResult`. Source
review found that the .NET 10 channel schedules its readiness continuation on the
shared thread pool when synchronous continuations are disabled. This is a material
dependency for a diagnostic investigating Save scheduling, even though actual
pipe writes stayed on a dedicated thread. The synchronous wake removes that
dependency. Wake handle retirement occurs only after a successful writer join;
abandonment keeps the handle alive. A final producer release can race retirement
of a redundant wake, which is caught and cannot escape a native callback. Raw
stream disposal remains writer-owned.

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

- New transport tests **10/10** passed: zero warmed disabled-hook allocation,
  ordered fixed/duplicate records on a background thread, deterministic saturation
  with `overflow` before `completed`, blocked startup write abandonment and
  writer-owned disposal, broken/open-failed streams, invalid enum loss, 40 rounds
  of concurrent producer/close stress, and a deterministically blocked final write
  demonstrating late watermark receipt without timely-join proof, and sequential
  selector/admission wakes observed while producer admission remains open (so a
  close wake cannot mask a missed record wake).
- Existing focused Save/recovery regression tests **7/7** passed. These are
  controller/fake-shell tests, not synthetic native input acceptance.
- The race stress test is scheduler-dependent and is not a proof that every
  possible producer instruction interleaving executed. The stable watermark
  argument above follows the atomic state transitions and release ordering;
  deterministic saturation and blocked-write cases exercise material boundaries.
- Managed compilation, including Native AOT/trim analyzers, succeeded. These
  portable tests do not establish actual macOS Native AOT compile/launch or
  retained raw witness/unchanged Save outcomes. The first hosted result follows.

Independent source review is recorded in
`../reviews/mac-json-save-witness-implementation-review.md`; parent framing and
child lifecycle remain a separate integration review.

## First hosted run: Mac AOT passes, witness pilot never starts

Artifact/log audit of [CI 36816778414](https://github.com/kleedaisuki/mote/actions/runs/36816778414),
source **`d6b355b`**, 2026-10-01. Both Mac AOT jobs published successfully,
verified system-library imports and passed the strict single-payload inventory.
Inventory artifacts independently retain these reported sizes:

| RID | AOT executable | Other payloads / bundled native libraries | Portable collector/probe suite | Actual nested JSON invocation |
| --- | ---: | --- | --- | --- |
| osx-x64 | `mote`, 16,920,632 B | 0 / 0 | 41/41 | **argparse exit 2, before pilot launch** |
| osx-arm64 | `mote`, 16,583,384 B | 0 / 0 | 41/41 | **argparse exit 2, before pilot launch** |

The API/job UI marks these non-gating steps successful and the whole run is green,
but both raw logs explicitly contain:

```text
probe.py: error: unrecognized arguments: - - m a c - s a v e - w i t n e s s
Native JSON source pilot incomplete (exit 2)
Process completed with exit code 1.
No files were found with the provided path: .../native-json-large.json
```

There are **no Mac JSON pilot artifacts or original-child witness records** in
this run. No 1/100 MiB Save, selector/admission/overflow/terminal/EOF, normal or
forced pilot cleanup, trace or reopen outcome can be claimed. The argparse
failure occurs before `main` inventories, compiles its Swift client or launches
the original GUI process. Thus native original/reopen environment isolation was
not exercised by this pilot; the 41 portable tests establish only their tested
mocked/process-protocol contracts. A missing artifact here is a prelaunch harness
defect, not a missing callback or a further product Save timeout.

Cause: the workflow assigned an `if` expression's pipeline output:
`$witness = if ($IsMacOS) { @('--mac-save-witness') } else { @() }`.
PowerShell unrolled the one-element output into a scalar string; native-command
`@witness` splatting consequently supplied its individual characters. This is a
CI wrapper defect introduced by the witness integration, not a target transport
or Save implementation failure.

### Narrow correction and argv regression guard

The corrected construction preserves an actual array in both branches:

```powershell
$witness = @()
if ($IsMacOS) { $witness = @('--mac-save-witness') }
```

`benchmarks/NativeJsonLargeAcceptance/test_invocation.ps1` reads these exact two
production workflow lines, substitutes only a test-owned platform Boolean, and
passes the resulting splat to a real Python argv-echo child. It asserts array
identity and exact native argument vectors for **osx-x64, osx-arm64, win-x64 and
win-arm64**. Both Mac branches must contain precisely one whole opt-in flag;
both Windows branches must contain none. No AppKit/input/file experiment runs.
All **4/4** argv controls pass on the local PowerShell host. CI runs the same
guard before its portable Python suite and uses reviewed LF/CRLF source pins:

- LF SHA-256 `8F88F1A8228E8B31DB511A694F0D7487E98FB0FE6BF0740A48C1FE2BFA6A5F17`.
- CRLF SHA-256 `3FF77AD3C8DA40FE781374138DE56F529D57B6E09549B71539F17408CDF3A64F`.

The guard changes no pilot input, Save attempt count, byte/trace/reopen oracle,
phase deadline or target permission. **The corrected invocation and actual Mac
witness collection require a fresh hosted run.** A future instrumented pass
would be a successful diagnostic-on sample, not a reliability repair.

Raw Mac job logs and downloaded inventory artifacts are retained under
`.cache/ci-36816778414-save-witness/`. Product executables were not uploaded, so
this is reported inventory plus successful hosted build/strict-step evidence,
not independent local binary rehash or execution. No native pilot was rerun
during this artifact-only audit; only the four portable argv controls were run.

## Platform references

The [official .NET channel documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)
specifies immediate `TryWrite=false` when a Wait-mode bounded channel is full.
The [background-thread contract](https://learn.microsoft.com/en-us/dotnet/api/system.threading.thread.isbackground)
ensures a stuck diagnostic thread cannot keep the application alive. These
platform contracts justify the mechanism; they do not themselves establish
native workload reliability or arbitrary interleaving test coverage.

The wake correction is justified by version-matched .NET 10 runtime source:
[channel completion scheduling](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Threading.Channels/src/System/Threading/Channels/AsyncOperation.cs)
and [ValueTask.AsTask](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/ValueTask.cs).
Its thread-pool independence is a source-backed property of using only synchronous
event waits and queue operations, not a claim that the portable suite globally
starved the runtime task pool. Such global mutation would interfere with unrelated
parallel tests and is deliberately not used as validation.
