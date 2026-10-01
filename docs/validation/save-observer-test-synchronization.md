# Save observer test synchronization

## Retained failure evidence

Hosted run `36827565407`, Windows strict job, built successfully with zero
warnings/errors, then reported `DocumentSaveObservationTests.Queued_save_captures_later_version_after_gate`
failing at line 57: the initial `ManualResetEventSlim.Wait(5 s)` returned false.
The retained raw log is
`.cache/ci-36827565407-menu-evidence/strict-windows.log`. The assembly result was
1 failed / 1353 passed / 0 skipped (1354 total). The corresponding macOS strict
suite passed 1354 cases, as recorded by the parent investigation.

This proves the first save did not signal commit entry within the test's deadline.
It does **not** prove a production save-gate defect, ThreadPool starvation, or a
Windows-specific scheduler cause: no worker scheduling trace was retained.
Blocking a test worker while waiting for a pool-scheduled save creates avoidable
scheduler pressure, so it is a plausible mechanism rather than an established
root cause. The original entry assertion was also outside the cleanup `try`,
so failure could leave the owned save running while its document and scratch
directory were disposed. That cleanup defect follows directly from the source.

## Bounded repair and invariants

Only `tests/Mote.Tests/DocumentSaveObservationTests.cs` changes:

- Commit entry is a `TaskCompletionSource` with asynchronous continuations.
  Awaiting its task yields the test worker; the deadline remains **5 seconds**.
- The initial wait is inside `try/finally`. Every exit releases the intentional
  commit hold and awaits both owned saves before resource disposal.
- The synchronous filesystem `Move` test seam still holds the first save's gate,
  with its existing bounded **10-second** release wait. No production API changes,
  global ThreadPool settings, parallelism settings or timing retries are added.
- Before editing, the second save must have exactly its gate-entry observation
  and must remain pending. Only then is the document changed from `one` to `two`
  and the first commit released.
- The oracle still checks the later snapshot version and balanced phase edges.
  It additionally verifies first-save bytes `one`, alongside second-save bytes
  `two`, preserving the immutable-snapshot and post-gate capture distinction.
- The disposable hold primitive is disposed only after draining owned tasks.

## Focused verification

Local Windows, .NET SDK `10.0.400`:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj --configuration Release `
  --filter FullyQualifiedName~DocumentSaveObservationTests `
  --logger 'trx;LogFileName=save-observer-focused.trx' `
  --results-directory .cache/save-observer-synchronization
```

Result: **19 passed / 0 failed / 0 skipped**, test duration **322 ms**. Raw TRX:
`.cache/save-observer-synchronization/save-observer-focused.trx`.
`git diff --check` passed. This focused success verifies the repaired test's
normal ordering and byte/version oracles locally; it does not demonstrate the
unknown hosted scheduling condition has been reproduced. Hosted strict-suite
validation remains the integration check owned by the parent agent.

## Integration review

The parent independently inspected `5742d55` and the retained TRX: all 19 cases
executed and passed, with no failed, aborted or unexecuted cases. The five-second
entry deadline and ten-second intentional hold are unchanged; the second-task
pending assertion and exact first-save bytes strengthen the existing oracle.
`Task.WhenAll` in cleanup observes every started Save before either its document
or synchronization primitive is disposed. No completed test was rerun for this
review. XML comments now state the signal and hold lifetime invariants. A later
hosted pass must be recorded as new evidence, not assumed from this review.
