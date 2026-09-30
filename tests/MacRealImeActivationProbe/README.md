# Disposable macOS TIS restoration trace

`Activate.m` is an opt-in **source-state** experiment, not a real-input test. Its
mutation entry point checks for a GitHub-hosted macOS session to prevent ordinary
local misruns; environment variables are not a security boundary against a
deliberately forged session. `--observe` is a
separate, read-only process mode that calls only TIS queries; it emits source
identifiers and Boolean properties, never user text, key events, or documents.
The runner must still be disposable because the first x64 and Arm64 activation
attempts left ITABC enabled despite `TISDisableInputSource` returning `noErr`.
Dispatch only after reviewing this risk and typing the exact confirmation. The
workflow refuses non-`main` refs and the helper checks for a disposable hosted
macOS runner. Independent static review found no blocking safety issue in the
revised helper, but its revised restoration behavior has not been exercised.

The v1 JSON schema retains its existing fields. Mutation requires the known
hosted baseline: x64 begins on ABC, Arm64 on US, the original source is
enabled/selected and distinct from ITABC, and both SCIM/ITABC begin disabled
and unselected. Any changed image or user-session state is inconclusive and
refuses activation **without issuing any TIS mutation, including a restore
selection**; this is intentionally narrower than merely finding an installed
Pinyin mode. The helper records whether it actually issued each enable/select
call. Cleanup disables only sources affected by its own enable attempts
(including a mode indirectly enabled by its parent), and it only reselects the
original source when its own selection attempt left ITABC current. An unrelated
source transition is reported as failed restoration rather than overwritten.

`restore_trace` adds monotonic
`elapsed_ms` samples at the boundaries before/after the ITABC disable call,
after its bounded wait, before/after SCIM parent disable, after its bounded
wait, and at final observation. It also samples every second for **five
seconds after the child-disable wait, before the helper disables SCIM** (the
trace records whether SCIM actually remains enabled), then every
second for **five seconds after the parent-disable wait**. Each interval has
fresh-process observations at seconds 0, 3, and 5 in `observer_series`, with
the adjacent local sample and an agreement flag. `mode_disable_wait_reached` and
`parent_disable_wait_reached` preserve the previously discarded wait results
when a disable is actually attempted. `observer_before`,
`observer_after_mode_disable`, and `observer_after` plus the series are
independently launched read-only snapshots, each capped at 1.5 seconds. The pre-mutation observer
must agree with the helper's initial source and enabled/selected flags before
activation is allowed; final restoration requires agreement from both
processes, including `present` as well as enabled/selected bits so an absent
source cannot masquerade as a restored disabled source.
`trace_complete=false` for a missing, timed-out, or divergent
intermediate observer or an incomplete observation interval, while
`restoration_passed` continues to mean the final source state actually matched.
Either false result makes the diagnostic fail. Cleanup always proceeds through
the `@finally` path even when an intermediate observer fails. The helper cap
is 45 seconds, still below the manual workflow's two-minute step cap. The
PowerShell wrapper drains both redirected streams concurrently to avoid
deadlocking on the enlarged JSON report, writes the complete JSON artifact,
and truncates only stderr included in a thrown error message.

The child sample immediately after its disable call plus the bounded-wait and
five-second local/fresh-process series can distinguish a mode that never
*appeared* disabled from one that was seen disabled and later became enabled.
The samples before and after parent disable can localize a re-enable transition to that interval,
but cannot prove that the parent call caused it: an asynchronous service may
reconcile at the same time. A false `mode_disable_wait_reached` with an enabled
observer strengthens the ineffective-disable hypothesis; a false-to-true
transition around the parent call strengthens a parent-interaction hypothesis;
disagreement between helper and observer instead points to process-local
caching or propagation. No causal conclusion is valid without a new, reviewed
disposable-runner report. The probe neither retries mutation nor changes the
restore order to force a pass.
