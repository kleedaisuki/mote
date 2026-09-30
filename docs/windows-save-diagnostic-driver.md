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

Current-source GUI, positive held-handle Save, actual orderly shutdown/drain,
and hosted runner capability are **not yet exercised**. They must not be claimed
from fake traces. The publish producer has not run locally because concurrent
source edits make its deliberately strict clean-source precondition fail. The
new CI job uses a fresh checkout and disposable Windows runner, with a bounded
positive control before ordinary samples, 15-minute job budget, seven-day
synthetic artifact retention, and job-level non-gating status. Its first target
report must be inspected even if the strict workflow remains green.
