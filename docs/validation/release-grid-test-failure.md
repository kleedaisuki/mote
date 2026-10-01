# Release Grid concurrency test failure

Date: 2026-10-02. Scope: test synchronization repair, not a production dispatch
change or accessibility release qualification.

## Hosted failure and limits

GitHub Actions run `36921665382`, source `9fecef2`, reported Windows full-suite
3660/3661 passing. The only failing test was
`External_selection_timeout_throw_and_focus_refusal_have_no_late_effects`:
`Assert.True(entered.Wait(5000))` at original line 185. Retained evidence:

- `.cache/release-ci-36921665382/failed.log`
- `.cache/release-ci-36921665382/tests/release-tests-windows-latest/runneradmin_runnervmfi6oq_2026-10-01_20_29_17_net10.0.trx`

The TRX duration is 5.1190445 seconds. Failure occurred before the assertions
about concurrent request refusal, timeout return, or late mutation suppression.
The hosted evidence does not record request start or thread-pool occupancy, so it
cannot prove the exact hosted scheduler state. Earlier hosted full passes do not
invalidate this failure and were not substituted for new verification.

## Concrete defect and discriminating reproduction

The test created an independent STA HWND owner, then scheduled the external
caller through `Task.Run` and synchronously blocked the test runner thread waiting
for owner admission. `Task.Run` requires an available thread-pool worker. Under
pool saturation the intended external call can remain queued until after the
five-second assertion has already failed. This is an unwanted test dependency:
the product contract under test is HWND-owner admission and cancellation, not
availability of the test runner's pool.

A standalone, fresh-process harness invokes the actual compiled public test
method through reflection, bounds the process-local pool to one worker, and
occupies that worker behind a release event before invoking the test on the main
thread. No global machine setting is changed. Source and logs are retained under
`.cache/release-grid-test-failure/pool-probe/` and `pool-before.log` /
`pool-after.log`. The assembly resolver loads dependencies from the specified
test output directory. The harness does not copy or reimplement the test or the
production dispatcher.

| Experiment | Result |
| --- | --- |
| Original test, ordinary fresh test host | 1/1 passed, reported 567 ms |
| Original actual test, one occupied pool worker | Same original line 185 assertion failed after 5059.72 ms |
| Repaired actual test, same one occupied pool worker | Passed after 569.31 ms |
| Repaired targeted ordinary fresh test host | 1/1 passed, reported 557 ms |
| Final rebuilt Grid accessibility / selection / focus neighbors | 30/30 passed, zero skipped, reported 826 ms |

The controlled test proves the scheduling defect and its removal. It does not
claim that the hosted runner definitely had exactly one available pool worker,
nor rule out every possible OS scheduling delay. No production defect was
established by this failure.

## Repair and preserved contract

Only `tests/Mote.Tests/WindowsGridAccessibilityTests.cs` changes executable code.
The external request uses an explicit independent `Thread`, not `Task.Run`. The
test is synchronous and joins the caller within the original five-second
completion budget, preventing a continuation from reintroducing pool dependence.
Caller exceptions are captured with `ExceptionDispatchInfo` and rethrown on the
test thread, instead of escaping an unmanaged test thread or being ignored.
Cleanup releases the blocked owner, joins the caller, then posts owner shutdown;
a nested `finally` preserves owner cleanup even if the caller join assertion
fails. An OS guard inside the caller preserves analyzer/platform correctness.

Unchanged assertions prove:

1. Owner admission actually enters the blocking composition callback.
2. A concurrent external mutation is refused as `Unsupported`.
3. The stalled request returns `Unavailable` after the real production bounded
   native call, which still uses a 500 ms `SendMessageTimeoutW` budget.
4. After owner release, an exact no-change request crosses behind the expired
   message and the original selection remains unchanged.
5. An owner admission exception returns `Unavailable` without mutation.
6. External focus remains unsupported.

No production timeout, retry, assertion, expected result, admission cancellation
check, or release gate was relaxed. No production source, CI workflow, or GUI
state was edited.

## Reproduction commands

```powershell
# Ordinary test host / retained baseline uses the pre-repair assembly.
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-build --no-restore `
  --filter FullyQualifiedName~External_selection_timeout_throw_and_focus_refusal_have_no_late_effects `
  --results-directory .cache/release-grid-test-failure/baseline --logger trx

# Controlled single-worker probe; use the corresponding old or repaired assembly.
dotnet run --project .cache/release-grid-test-failure/pool-probe/PoolProbe.csproj `
  -c Release --no-build -- tests/Mote.Tests/bin/Release/net10.0

# Final build and neighboring contracts.
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore `
  --filter 'FullyQualifiedName~WindowsGridAccessibilityTests|FullyQualifiedName~WindowsGridSelection|FullyQualifiedName~WindowsGridFocus' `
  --results-directory .cache/release-grid-test-failure/neighbors --logger trx
```

Local SDK was 10.0.400 on Windows. The final build emitted no warnings or errors.
An earlier build failed with MSB3027/MSB3021 because an independently owned local
GUI child (PID 11416) temporarily held native-shell dependency DLLs. That child
exited; the rebuild addressed that concrete setup failure, not the test failure.
One intermediate build emitted CA1416 for the new caller lambda; its explicit
Windows guard fixed the warning before final verification.

Local targeted evidence is not a replacement for the final hosted release
workflow, macOS runs, real assistive-technology testing, or Native AOT inventory.
No commit, push, or hosted workflow was initiated by this assignment.
