# Opt-in ordinary Windows Save diagnostic driver

## Scope and authorization

`tests/Invoke-WindowsSaveDiagnostic.ps1` implements the bounded hosted diagnostic
contract in [the investigation](windows-atomic-save-investigation.md). The
separate non-gating CI diagnostic is now wired but has not yet run; this is
**not** a benchmark, a retry fix, a user-document tool, or a filesystem
causality collector. No GUI experiment was performed during implementation.
Run only in an explicitly available interactive Windows desktop, not alongside
another GUI test. Do not disable protection, indexing, or sync.

The entire batch is one separate positive control plus 1–4 ordinary children
(default 4), not five ordinary children plus a control. The positive control's
expected Save failure is exempt from the ordinary first-failure stop rule. A
failed control prevents all ordinary launches. Any ordinary error, unverified
Save, failure event, or incomplete trace stops further children.

## Current-publish provenance

First freeze current source and build inputs. Source/build files must be clean;
any tracked input change after the publish, including documentation changes,
invalidates the manifest. Existing unrelated dirty source edits are not bypassed.

```powershell
# Publish only: this does not launch mote or use the GUI.
pwsh -NoProfile -File tests/New-WindowsSaveDiagnosticPublish.ps1
# Review its final publish-manifest.json path and pinned executable first.
pwsh -NoProfile -File tests/Invoke-WindowsSaveDiagnostic.ps1 `
  -PublishManifest .cache/windows-save-diagnostic-publish/<id>/publish-manifest.json `
  -OrdinaryRuns 4
```

The producer publishes Release/win-x64/self-contained/Native AOT with warnings as
errors to a **new** unique cache directory. It records HEAD, SHA-256, bytes, SDK,
resolved runtime pack from restore assets, publish start/completion UTC, explicit
command, complete tracked-input fingerprints, and publish-log fingerprint. It
refuses input changes during publish and any inventory except one real mote.exe.
The diagnostic rechecks the pin, current input bytes, clean source, resolved SDK,
manifest/log fingerprints, timestamps (publish within two hours), and one-file
inventory before launching. It checks executable SHA again before each child.
The SDK's [artifacts output layout](https://learn.microsoft.com/en-us/dotnet/core/sdk/artifacts-output)
isolates all project intermediate/output directories in this publish's unique
repo-cache build root, so incremental outputs from a prior publish are not reused.
The producer itself also rejects an executable timestamp predating this build.
This is reproducibility and accidental-stale-binary protection, **not a signed
supply-chain attestation** against a malicious caller rewriting the manifest.
Do not hand-author or reuse an old manifest.

All fixture/home paths are below `.temp/windows-save-diagnostic/<run-id>/`;
all reports and finalized captures are below `.cache/windows-save-diagnostic/<run-id>/`.
The publish producer uses `.cache/windows-save-diagnostic-publish/<id>/`.
Preflight rejects repository-local reparse ancestors and owners other than the
current Windows SID, Administrators, or SYSTEM. ACL ownership is an accidental
misrouting guard, not proof that other principals cannot write the directory.
Fresh checkout/controlled runner remains a required assumption. No recursive
deletion or cleanup is performed: uncertain recovery sidecars remain preserved.

## Observation and safe teardown

| Stage | Contract |
| --- | --- |
| Fixture | Exactly 1 MiB ASCII Markdown-shaped bytes, generated in memory; expected new bytes are X followed by the original. |
| Bind/edit | Exact synthetic prefix, bounded RichEdit input island, acknowledged selection, then WM_CHAR X at offset zero and dirty title. No clipboard, user fixture, foreground input, or physical-key assertion. |
| Positive control | The diagnostic process itself is the separate holder helper: it opens Read with ReadWrite sharing and no Delete, acknowledges CanRead before Save, and holds until failure/outcome handling completes. This adds no mote child. |
| Save | One Save command only. During the pending interval, HWND/title/modal inspection only; no target/temp content reads or identity handles. |
| Oracle | Once clean-title completion or a child-owned modal is observed, compare actual bytes exactly against both original and new. Missing/neither is high severity regardless of dialog wording or dirty state. |
| Failure close | Only recorded PID + owned main HWND + #32770 + title mote + frozen current `Save failed.` prefix + IDOK may be acknowledged. Unknown dialogs are never dismissed. |
| Dirty close | WM_CLOSE, then only child-owned #32770 with exact `Discard unsaved changes?` and IDYES. This discards only the generated document and never requests another Save. |
| Retained recovery warning | Before the dirty prompt, acknowledge IDOK once only for the exact current warning template, title mote, child PID/owner, and deterministic recovery sidecar path belonging to this fixture. A different path or warning is not dismissed. Retained bytes are not deleted. |
| Normal drain | Wait for real process exit, require exit code zero and a parsable final `mote.session`; exactly one terminal session and no `telemetry.dropped` record. |
| Forced exit | Before forced termination preserve traces only and a primary pre-exit row. Never recursively copy fixtures during an indeterminate live Save. Kill only the owned child, confirm termination, then compare/copy all fixtures and recovery files. Mark incomplete-forced-exit even if a terminal record exists. |

The normal UI/outcome budget is 30 seconds; failure dismissal and close/drain
reserve time up to 40 seconds. A separate managed watchdog targets only that
Process instance at 45 seconds from child start, including UI and capture work.
No new child launches after 240 seconds; polling stops at 285 seconds. Windows
scheduling, kernel-blocked I/O, and artifact-copy stalls cannot be promised a
hard real-time total wall clock; the watchdog bounds child lifetime independently
of ordinary PowerShell polling. Evidence-copy errors never bypass the outer
process teardown. Both the pre-exit trace snapshot and post-exit complete fixture
directory are retained; the scratch originals also remain available.

Rows report PID/start UTC, fixture digests, Save-post state, observed modal,
dirty state, responsiveness acknowledgement, exact disk classification, normal
exit/code, trace status, allowlisted failure operation/HResult, and bounded error
types (not exception messages or arbitrary exception data). The manifest reports
OS build, optional runner image version, filesystem type, and fixed executable
provenance. The CI artifact contains only this job's synthetic fixtures, traces,
publish manifest and log; it contains no user document or clipboard content.
Artifact access follows the repository's GitHub permissions, **not** a step
name or a promise of confidentiality. Never adapt this upload to real user
documents or arbitrary raw filesystem traces. No collector is downloaded or
invoked here.

The control must show original bytes, dirty state, normal complete drain and
exactly one `save.failure.replace` with signed HRESULT -2147024864 (0x80070020).
It establishes the diagnostic path, **not reproduction of historical 1175**.
All ordinary passes mean bounded non-reproduction for this executable/environment,
not a reliability estimate. Missing failure records never overrule a failure
modal or uncertain bytes. A Cleanup event does not establish successful Replace;
preserve all failure records without reconstructing a hidden primary code.
If a new replacement failure is observed, the next layer is the separately
reviewed all-PID filtered Procmon/ETW protocol in the investigation, not a retry.

## Local validation evidence (2026-10-01)

`pwsh -NoProfile -File tests/Invoke-WindowsSaveDiagnostic.ps1 -SelfTest` uses only
new disposable repository-local fixtures. It checks missing/original/new/other
exact byte oracles, complete normal terminal trace, forced-exit incompleteness,
malformed JSON, missing terminal/operation/HResult, dropped records, and current
Save-prefix versus unrelated recovery-export dialog, unreadable-target unknown
classification, and owned versus foreign retained-recovery warning paths. It launches no GUI or mote
binary. The PowerShell scripts and embedded C# are additionally parser/compiler
checked without Win32 calls. The initial self-test exposed PowerShell's reserved
`$HOME` variable; the implementation uses `$TraceHome` instead.
SDK 10.0.400 property-only MSBuild evaluation also confirmed that both
`BaseIntermediateOutputPath` and `MSBuildProjectExtensionsPath` resolve to the
isolated artifacts root's `obj/Mote.Native/` directory; no restore/build ran.
The local validation summary is `.cache/windows-save-diagnostic-validation/evidence.json`.

At the local implementation checkpoint, current-source GUI, positive held-handle
Save, orderly shutdown/drain and hosted capability had not been exercised. They
must not be claimed from fake traces. The publish producer has not run locally because concurrent
source edits make its deliberately strict clean-source precondition fail. The
new CI job uses a fresh checkout and disposable Windows runner, with a bounded
positive control before ordinary samples, 15-minute job budget, seven-day
synthetic artifact retention, and job-level non-gating status. Its first target
report must be inspected even if the strict workflow remains green.

## First hosted positive-control failure and targeted observability

Hosted workflow run `36776080244` used HEAD
`31b8cada7517a92fd3293b0ae53d2d161f66f1ca`, SDK 10.0.401, runtime pack 10.0.12,
NTFS/Windows 10.0.26100, and fresh AOT executable SHA-256
`ec09444eb6d551dca5134e7c6e7b53344a7bc9d2685b3e95d921b2eea4c1903e`
(7,039,488 bytes). The retained row is
`.cache/ci-36776080244-save/artifact/windows-save-diagnostic/eb39683a3f744ac3bce13a22c867209b/row-0.json`;
the job log is `.cache/ci-36776080244-save/job.log`.

The held-share control reached held acknowledgement, dirty document, responsive
main HWND, child-owned failure modal and exact original target bytes. Close then
threw a PowerShell RuntimeException; total child duration was 0.4824103 seconds,
followed by forced exit and zero trace records. Independent post-exit byte audit
also found the retained recovery sidecar exactly X+original (1,048,577 bytes),
not missing attempted-save content. No ordinary child ran. This is a failed
diagnostic-control lifecycle, not a measured replacement latency or complete
HRESULT capture. Its subsecond duration excludes the 34/40/45-second timeout
paths but does not identify the failing dialog guard.

The archived implementation already used Unicode WM_GETTEXT through
SendMessageTimeoutW, 500 milliseconds/call: the tempting cross-process
GetWindowTextW hypothesis was checked and rejected. The targeted follow-up does
**not** switch text APIs, filter Static controls speculatively, or relax unknown
dialog refusal. It records content-free predicates to locate the next failure:

- `close_error_stage`: one of the fixed owner/title/Static read, purpose guard,
  button lookup/post, failure dismissal, main-close or normal-exit stages.
- `close_reason`: only owner/title/purpose mismatch, missing/uncertifiable button/main HWND,
  message-post failure, incomplete normal exit, or API/runtime exception.
- `close_predicates`: owner match, title-is-mote, exact/prefix purpose matches,
  bounded aggregate and per-Static text lengths, Static count and type bits,
  text-at-cap, button-found and PostMessage acknowledgement when applicable.
- `close_exception`: deepest wrapper-unwrapped exception type and signed
  HResult only. No dialog title/body/path, exception message, or derived text
  digest is serialized. Reads still use the existing bounded message API.

The Static type bits come from the fixed `SS_TYPEMASK` field of
GWL_STYLE and may distinguish an icon control from text controls in a future
row; this does not establish that an icon caused the first failure. No causal
claim is made from the missing old predicate fields. Fake checks cover unknown
purpose refusal, no raw text serialization, preserved Static metadata, and
deepest exception codes. Independent fake evidence is retained under
`.temp/windows-save-first-target-audit/`, without native calls or driver entry.
A fresh hosted rerun remains necessary to learn which predicate/API actually
fails and to establish normal-drain control capability.
Microsoft's [WM_GETTEXT contract](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-gettext)
distinguishes non-text Static controls from ordinary strings, which motivates
recording control-kind metadata rather than assuming every Static child is body
text. The [Static-style documentation](https://learn.microsoft.com/en-us/windows/win32/controls/static-control-styles)
also cautions that SS_TYPEMASK does not represent all styles: reported low type
bits are diagnostic metadata only, not a complete style classification or a
new authorization predicate.

## Second hosted control: certified button readiness

Run `36777561397` retained its failing row below
`.cache/ci-36777561397-save/windows-save-diagnostic/` (see the independent
audit [first-target validation](validation/windows-save-diagnostic-first-target.md)).
The content-free follow-up localized the fault to `failure-ok-lookup`:
owner/title/Save-prefix all matched; Static types were `[3, 0]`, lengths
`[0, 443]`, and body length 443 with no cap hit. `GetDlgItem(modal, 1)` returned
null, so the driver refused to post any button action and forced exit at total
0.5834669 seconds. Target and recovery bytes were respectively exact original
and X+original; trace remained zero/incomplete. This rejects the proposed
Static/prefix explanation for **this run**, but does not establish whether the
button was absent transiently, nested, or had another unsupported topology.

The minimal follow-up adds a one-second, fail-closed readiness budget for the
specified ID, shared by failure OK, retained-warning OK and discard Yes:
This is a selection deadline, not a hard wall-clock guarantee: an in-progress
500 ms bounded text call may finish after it, but the final elapsed-time check
forbids returning a late button for posting.

1. Recheck the **same** modal PID/owner/class, exact title and previously allowed
   purpose on every attempt. A changed/unknown purpose aborts without dismissal.
2. Inspect direct `GetDlgItem` and a capped snapshot of at most 32 descendants.
   During the initial 250 milliseconds, and until at least a second observation,
   only a certified direct button can be accepted; this permits ordinary child
   initialization without assuming the first null is a stable layout.
3. Thereafter accept a descendant only when there is **exactly one** `Button`
   with the requested `GetDlgCtrlID` (IDOK=1 or IDYES=6), same PID, actual
   `IsChild(modal, button)` ancestry, visible and enabled. Duplicate identities,
   overflow, ownership/ancestry failures or inconsistent direct-handle identity
   refuse selection immediately. Missing/disabled/hidden controls can become
   ready only within the original finite budget.
4. Never use a caption, first arbitrary Button, default-button assumption,
   dialog-wide WM_COMMAND, global key press, or a different modal as fallback.
   The original 45-second owned-child watchdog remains unchanged, and readiness
   refuses actions at/after child second 38 to reserve normal-drain time.

`button_lookup` contains requested ID, attempts, fixed selection mode, ready
flag, elapsed readiness time on success, descendant IDs and bounded per-control
class-is-Button/owned/ancestry/direct-parent/visible/enabled booleans. Handles and
captions remain in memory only. No raw text is added to reports. A descendant
selection is not proof that nesting caused the old row; the new mode and timing
are precisely the evidence needed to discriminate that question.

Microsoft's [GetDlgItem contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdlgitem)
allows a null result for an invalid dialog or nonexistent control;
[EnumChildWindows](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumchildwindows)
explicitly includes nested descendants but not controls created during that
enumeration. [GetDlgCtrlID](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdlgctrlid)
identifies actual child controls. These contracts support bounded reobservation
and exact identity checks; they do not prove the old failure was an initialization
race or authorize changing the expected ID. Local fake selection tests and C#
compilation run without native calls; fresh hosted closing/drain remains unproven.
