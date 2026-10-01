# Native CSV Grid clipboard workflow safety review

Date: 2026-10-01. Independent static review on the Windows development host.
No real clipboard read/write, desktop input, test execution, staging, or commit
was performed by this reviewer. Scope: the drafted
`tests/Mote.Tests/NativeCsvGridClipboardWorkflowTests.cs`, reachable controller,
Windows table and clipboard publisher, `RepoTemp`, and the invocation document.

## Current decision

**Approved only for the frozen test hash below and the reviewed dedicated
disposable GitHub-hosted Windows invocation. No remaining substantive safety
blocker was found in this scope.** This is execution safety approval, not actual
clipboard acceptance. The invocation safeguards below are conditions of approval.

## Required invocation safeguards

1. Run only on a fresh disposable GitHub-hosted Windows runner, never a local or
   self-hosted machine. Environment strings are deliberate consent checks, not
   an authenticated boundary against somebody deliberately spoofing them.
2. Require `GITHUB_ACTIONS=true`, `RUNNER_ENVIRONMENT=github-hosted`,
   `MOTE_DISPOSABLE_GRID_CLIPBOARD=1`, and `GITHUB_WORKSPACE` matching the checked-out
   repository that contains the test binary. The repository-local approval file
   must contain exactly `disposable-github-hosted-windows-only`.
3. Start with no old report or approval marker, verify repository-local scratch
   containment, then create the marker for this run only. A missing opt-in makes
   the test return normally; therefore exit code zero alone is not evidence of
   real clipboard execution. Require a freshly created report with
   `status=passed`, `nativeClipboard=true`, all six expected cases, plus test
   process exit zero. Do not restore a cached report directory.
4. Use the exact fully qualified real-test filter, not the whole suite, and keep
   exit status plus TRX and JSON evidence. Retain a test-host timeout and an
   external CI/job timeout: the managed 30-second polling loops do not bound an
   unmanaged synchronous call or a process crash.
5. Remove the opt-in environment variable and approval marker in `finally`.
   Do not attempt to save/restore a developer clipboard. The runner is disposable
   precisely because publication destroys its old clipboard contents.
6. Verify the frozen source SHA-256 before invoking. A later source mutation
   invalidates this approval and requires a delta review.

## Reachability and state ownership

| Area | Inspected behavior |
| --- | --- |
| Fake mode | `Exercise(false)` creates owned hidden HWNDs and production table/controller instances. `GridShell.SetClipboardText` only calls `_publisher.SetClipboardText` when `_realClipboard` is true, and native readback is similarly conditional. Publisher construction merely stores mode flags. No native clipboard path is reachable in this branch. |
| Real gate order | All OS, CI, hosted-runner, checked-out workspace and explicit marker assertions precede `Exercise(true)`, the first possible clipboard mutation. Report writing itself happens only under the gated repository-local directory. |
| Clipboard mutation | Each of six checks deliberately publishes a Unicode sentinel, then allows the controller to publish a prepared result for positive cases. Negative cases await an observed error, assert no additional publisher calls, and independently read the sentinel. Clipboard is not restored or cleared during cleanup. |
| Preparation | The real production controller prepares on a background task, checks document/reference/serial/presentation identity before publication, and reports preparation rejection without invoking the publisher. Select only invalidates prior copy authority and does not move source selection. |
| Ownership | Publisher uses an owned nonzero hidden parent HWND with `OpenClipboard`, then `EmptyClipboard`, then movable global memory transfer through `SetClipboardData`. Successful transfer sets the local handle to zero so cleanup does not free system-owned memory. Readback locks only until before `CloseClipboard`, never frees or retains the borrowed handle. |
| Files | Source fixtures and isolated configuration roots are generated beneath a GUID child of `.temp/tests`. Fixed `records.csv` remains lexically contained; recursive cleanup is guarded by the verified `.temp/tests` prefix. Fresh trusted checkout/scratch without reparse points is an invocation assumption. No user configuration or original user file is touched. |
| UI routes | HWNDs are hidden and owned by the test. WM_NOTIFY is forwarded to the owned table. Reflection calls private `Select` and `Emit`, bypassing actual pointer hit testing and modal context-menu tracking. No `SendInput`, global input hook, visible dialog or source edit is requested. |
| Cleanup | Controller disposal precedes shell disposal, then fixture cleanup. The shell destroys its table/subclass/parent handles; it does not destroy somebody else's window. Clipboard Unicode data has already transferred to the OS. |
| Outcomes | Literal expected strings check quoted multiline/tab/quote decoding, final empty record, missing-cell refusal, explicit missing padding, NUL refusal and payload-cap refusal. Canonical text, disk text, version, modified flag, navigation and undo/redo flags are checked unchanged. Readback failures and asserted publisher errors fail the test rather than producing passed JSON. |

## Evidence limits

This is managed test-host execution of actual controller/table/publisher code,
not the published Native AOT executable. It does not prove physical shortcut or
context-menu routing, real composition, accessibility, macOS NSPasteboard,
clipboard contention recovery, natural paint latency, or whole-product acceptance.
The publisher cannot restore the prior clipboard if an OS failure occurs after
`EmptyClipboard`; that is acknowledged publication failure, not preparation
refusal or an atomic rollback guarantee. External clipboard contention can cause
a legitimate test failure; it must not be converted into a pass.

## Microsoft contracts verified

- [SetClipboardData](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setclipboarddata): movable memory transfers to system ownership after successful publication; a null clipboard owner cannot publish after EmptyClipboard.
- [Clipboard Operations](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-operations): EmptyClipboard destroys previous content and associates ownership with the opening window.
- [GetClipboardData](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboarddata): the returned handle is system-owned and must not be freed or used after closing the clipboard.

The test's null HWND is used only for readback, without EmptyClipboard or
SetClipboardData, so the null-owner publishing prohibition does not apply.

## Frozen-draft delta review

Frozen test SHA-256:
`FE4609F6CBCACF6C02AA40B538695B886E268F4BA3E1A4A7934D26C3DC9B4299`.
The final draft additionally requires a nonempty CI run/attempt/commit identity,
checks its separate approval-run marker before mutation, and reports that run
identity, current source hash and UTC time. It waits for exact certified row
extent before copying and establishes fixture selection directly in the canonical
navigation object; neither change introduces clipboard access into fake mode.

The first invocation draft computed the current hash without comparing it with
the reviewed hash, so a later edited test would have self-certified. Required
correction: pin the reviewed hash before creating approval markers. Also require
the six exact case names and capture a repository-local TRX. These are invocation
corrections, not an established defect in the frozen test or production publisher.
The source hash identifies the reviewed source, not the loaded assembly by itself;
the exact checkout must be built immediately before `--no-build`, with no cached
or independently downloaded test DLL substituted.

## Final invocation review and approval

The final `docs/validation/native-csv-grid-clipboard.md` wiring corrects all
requested invocation issues: pinned reviewed hash before markers, exact real-test
filter, exact-file cleanup of old JSON/TRX/markers, fresh run identity plus UTC
and source hash validation, six exact case names, repository-local TRX, and
`finally` removal of opt-in/approval markers. The dedicated fresh GitHub-hosted
Windows job must set **`timeout-minutes: 5`** and build the exact checked-out
source before the `--no-build` command. Do not opt in the whole-suite step or
reuse report/build artifacts from another checkout. Approve this bounded route
under those conditions; no real clipboard mutation was executed by this review.

Final approved test SHA-256 is unchanged:
`FE4609F6CBCACF6C02AA40B538695B886E268F4BA3E1A4A7934D26C3DC9B4299`.
Raw SHA-256 includes line endings. A Git checkout with different line-ending
conversion will fail safely at the hash gate; investigate/review the byte
representation instead of removing the pin or silently accepting any hash.
No other agent's modifications or the user's IDE settings are part of this
approval. The parent owns actual workflow integration and must retain the
reviewed invocation conditions when translating the documentation into YAML.

## Materialized invocation script: targeted delta approval

The approved inline invocation has now been materialized as
`tests/Invoke-NativeCsvGridClipboardWorkflow.ps1`. This section supersedes the
single-raw-test-hash representation restriction above, without widening the
allowed runtime route. The test's executable content is unchanged.

The reviewer independently derived the LF-only test hash from the frozen CRLF
bytes by replacing CRLF pairs with LF, and inspected every statement in the
final script. Admission accepts exactly those two fixed raw hashes, not a hash
computed from arbitrary current content, Git clean filters, whitespace/BOM
normalization or a mutable approved-hash marker. The test report independently
contains the actual raw file hash, which must equal the admitted fixed value.

| Approved artifact | LF raw SHA-256 | CRLF raw SHA-256 |
| --- | --- | --- |
| Test harness | `3D3A18BE04C7327869FED237CB4EC7F6E62662981EA812DBB22FD1977C7C04A2` | `FE4609F6CBCACF6C02AA40B538695B886E268F4BA3E1A4A7934D26C3DC9B4299` |
| Invocation script | `51143072924B56AA2A2CE13BB86DA7128DAA3EF31901A480EDA64A49642520D5` | `E5D846C70974497829E3042EB7FEACB6B5B6907810BBADEC1C7FE854BF9A8C5E` |

The script additionally checks nonempty CI identity, its location in the exact
workspace, `HEAD == GITHUB_SHA`, tracked script/test files and clean `src`/`tests`
(including nonignored untracked files). It rejects inherited opt-in before any
build, builds the current checkout with warnings as errors, and only then creates
approval markers and grants permission to the exact selected `dotnet test
--no-build --no-restore` subprocess. Fresh JSON identity/time/raw hash/six cases
and TRX existence are required after exit zero. Its inner `finally` removes the
opt-in variable and both permission markers; its outer `finally` restores the
working directory. All cleanup remains four exact repo-local file names, not
recursive deletion.

**Approve this exact materialized script/test pair on a dedicated fresh
GitHub-hosted Windows job with `timeout-minutes: 5`.** Build/report caches from
another checkout must not be restored, and no parallel clipboard writer may
share this job. Keep the hosted-runner environment checks, exact filter, fixed
hash constants and artifact validation intact when wiring CI. The script does
not cryptographically authorize itself: this review binds the script hashes,
while the script pins the separately reviewed test. A changed script requires
new review before execution even if the test hash remains unchanged.

The reviewer did not execute this script, a test host, or any OS clipboard
operation. Trusted fresh checkout and scratch without reparse points remain the
containment assumptions; environment strings are operator gates, not an
adversarial isolation boundary. This approves execution safety only, not native
clipboard correctness or published Native AOT acceptance.

The final timestamp portability delta is included in those script hashes:
`ConvertFrom-Json` can provide a `DateTime` value on newer PowerShell; the script
constructs `DateTimeOffset` directly for that case, otherwise uses invariant
string parsing. This avoids loss of fractional precision through culture-specific
implicit string conversion. It neither widens permission nor weakens the
fresh-report comparison. No remaining substantive blocker was found.

Approved workflow command (PowerShell 7, dedicated Windows hosted job):

```powershell
& ./tests/Invoke-NativeCsvGridClipboardWorkflow.ps1
```

The step must fail on a thrown error; do not catch it and report success. Upload
`.cache/native-grid-clipboard/report.json` and `clipboard.trx` with an always-run
artifact step, but artifact existence alone must not override the script result.
