# First hosted Windows Save diagnostic: failed positive-control teardown

## Verdict and scope (2026-10-01)

Run `36776080244`, job `110094208431`, did exercise the current-source Native
AOT editor through startup, acknowledged synthetic edit, and a Save with an
acknowledged observer-held no-Delete-share handle. It did **not** establish the
complete positive trace control: normal close failed in the diagnostic, the
owned child was forcibly terminated, and both archived trace copies are empty.
No ordinary Save children ran. This is neither a reliability rate nor a
reproduction of the historical inferred Win32 1175 failure.

This investigation inspected only retained local artifacts and source. It did
not publish, launch a GUI, rerun the diagnostic, modify production/script code,
or stage/commit/push. The original archived files were not changed.

## Evidence and reproducibility

- Job log: `.cache/ci-36776080244-save/job.log`.
- Artifact root: `.cache/ci-36776080244-save/artifact/`.
- Diagnostic run directory: `windows-save-diagnostic/eb39683a3f744ac3bce13a22c867209b/`.
- Publish directory: `windows-save-diagnostic-publish/8156f73982284c7cb9630f3acbe38d55/`.
- Independent synthetic-byte audit: `.temp/windows-save-first-target-audit/audit.py`;
  results: `.temp/windows-save-first-target-audit/evidence.json`.

Reproduce the archive audit without executing any retained binary:

```powershell
python .temp/windows-save-first-target-audit/audit.py
git show 31b8cada7517a92fd3293b0ae53d2d161f66f1ca:tests/Invoke-WindowsSaveDiagnostic.ps1
```

Audit environment: local Windows PowerShell shell, Python 3.14.6. Byte expectations
are generated independently from the documented fixture contract: a 1,024-byte
ASCII record with the synthetic startup prefix, `a` padding, and final LF,
repeated 1,024 times; the new snapshot is one ASCII X followed by those bytes.
The check compares full bytes, not just recorded digest claims. Reading finalized
archive copies is safe: these are synthetic post-termination artifacts, not an
uncertain live Save target.

The source at recorded HEAD is exactly the current diagnostic script after line
ending normalization. Its LF SHA-256 is `17efcf3d86fe6b77368d84b1099f2ea31c94b813211aa0d6207c189dc3751bb3`;
expanding LF to CRLF produces the exact published input pin
`8365bf91540a0486ff7d8075d1f164323068da829b70b7c04e30eaec051e5619`.
The differing local raw hash is therefore not evidence of a stale/different
hosted script.

## Hosted environment and sequence

| Observation | Retained evidence |
| --- | --- |
| Source HEAD | `31b8cada7517a92fd3293b0ae53d2d161f66f1ca` |
| Executable pin | SHA-256 `ec09444eb6d551dca5134e7c6e7b53344a7bc9d2685b3e95d921b2eea4c1903e`, 7,039,488 bytes |
| Publish | Release, win-x64, self-contained Native AOT; warnings-as-errors; unique artifacts root |
| Runtime | SDK 10.0.401; `Microsoft.NETCore.App.Runtime.win-x64/10.0.12` |
| Runner | windows-2025-vs2026 image 20260925.250.1; OS 10.0.26100.0; NTFS |
| Driver self-test | Passed at 20:56:29 UTC; no GUI launched by self-test |
| Publish | Attested start timestamp 20:56:33.925 UTC; native publish completion logged 20:57:37.184 UTC |
| Child | PID 2532; start 20:57:47.4884321 UTC; ordinal 0, positive control |
| Edit/Save | `held_ack=true`, `save_posted=true`, `error_type=null` |
| Outcome observation | Owned `#32770` modal, dirty title, successful responsiveness message, exact original target |
| Teardown | `close_error_type=System.Management.Automation.RuntimeException`; `normal_exit=false` |
| End | `exit_code=-1`, `trace_capture=incomplete-forced-exit`; entire child operation 0.4824103 s |
| Batch | Positive-control assertion throws at line 423; step exit 1; artifact uploaded |

The artifact has only row 0 and no summary. The script's control check occurs
before any ordinary launch, so this failed control prevented ordinary samples.
The row's `save_failure_observed=true` means a modal was observed; it does not
prove the dialog passed the frozen `Save failed.` text validation, which is
performed only in teardown.

## Independent archived-byte and trace results

| Final archived file | Bytes | Full-byte classification | SHA-256 |
| --- | ---: | --- | --- |
| `fixture.md` | 1,048,576 | Exactly original; not new | `69f4396b7a4d42b022627414f8711cc4b62292f35fbd415b34daa201fac08aea` |
| `.mote-save-678d157aa51a02dbf8a8f2d48c5d63a9533cc79b7adcba1237c49d4fc9538219.recovery` | 1,048,577 | Exactly X + original; not original | `2d12a1cbc6df198198d7b6de8c38a3e3f3bd8ffaf3bb73025effa9026c1d75f0` |
| Pre-kill JSONL | 0 | No records | — |
| Post-kill JSONL | 0 | No records | — |

The target is preserved and a complete attempted snapshot survives in the
recovery sidecar for this sample. This does not prove every possible Replace
failure has those outcomes. The independent comparison strengthens the row's
disk classification without assigning an exception phase or numeric HRESULT.

`trace.parsable=true` with zero records is vacuous parse success, not successful
capture. There is no hidden `save.failure.replace`, HResult, or terminal
`mote.session` to recover. The existing `JsonlTraceSink.WriteLoopAsync` opens a
64 KiB buffered stream and flushes on rotation/orderly drain, not every event.
Immediate kill is consistent with losing this small session's buffered records;
it is not proof of the exact writer state or a telemetry defect.

## Why normal close failed: known mechanism, unknown guard

The inner Save block completed its observations without error. Its `finally`
released the held handle, then attempted the recognized failure-modal dismissal
and ordinary close. A caught exception in that close block was reduced to its
outer type. The driver then deliberately preserved the pre-exit row/traces and
killed the owned process, bypassing orderly telemetry drain. Thus the immediate
cause of incomplete evidence is a failed diagnostic teardown followed by its
safety fallback, not a 30-second Save stall.

The whole child operation took less than half a second. It cannot have reached
the 34-second failure-dismiss timeout, 40-second close deadline, or 45-second
watchdog deadline. An immediate dialog-validation throw is consistent with the
timing and `RuntimeException`, but the artifact does not identify which branch
ran. PowerShell may also wrap invocation failures; outer type alone cannot
distinguish an explicit guard throw from an underlying interop exception.

Potential immediate branches include:

1. Recorded modal no longer matches the current exact PID/owner-filtered modal,
   or its title is not exactly `mote`, or its Static text does not begin with
   `Save failed.`.
2. Recognized failure modal lacks IDOK.
3. Later close warning/discard dialog fails exact recognition, or lacks its
   expected IDOK/IDYES button.
4. A PowerShell/interop operation throws during these checks.

There is no archived title, Static-child count, recognition flag, close stage,
button result, or deepest exception type. Therefore failure-modal rejection is
a useful first instrumentation target, **not an established root cause**.
Possible initialization races, Static enumeration order/icon effects, or line
ending changes are hypotheses only. Do not weaken recognition based on them.

### A specifically excluded diagnosis

The pinned `Text` helper does **not** call `GetWindowTextW`. It calls
`SendMessageTimeoutW` with message 13 (`WM_GETTEXT`), a StringBuilder buffer,
Unicode marshaling, and a 500 ms bound. `DialogText` invokes this helper for
Static children. Replacing cross-process GetWindowText with WM_GETTEXT is not a
correction to this archived driver: the latter mechanism is already present.

The production Save error starts with `Save failed.` and is shown via
`MessageBoxW(owner, message, "mote", MB_OK | MB_ICONERROR)`.
`WarnRetainedRecovery` precedes `ConfirmDiscard`, and its expected template is
already represented in the script. Source-contract alignment does not substitute
for knowing which dialog/check failed on the hosted desktop.

## Minimal actionable next change

Do not change production Save, add a retry, accept arbitrary dialogs, use global
keyboard input, disable protection, or flush the production sink per event to
compensate for broken teardown. First add content-free diagnostic stage evidence
while preserving the existing exact PID/owner/title/template/button gates.

Recommended compact row fields:

| Field | Bounded allowed values / semantics |
| --- | --- |
| `close_stage` | Closed enum: release-holder, failure-find, failure-title, failure-text, failure-button, failure-post, failure-gone, close-post, close-title, close-text, recovery-button/post, discard-button/post, exit-wait, complete |
| `close_reason` | Closed enum per guard, e.g. modal-changed, title-mismatch, failure-prefix-mismatch, missing-idok, unknown-close-dialog, missing-idyes, message-post-failed, caught-exception |
| recognition evidence | Booleans for owned-modal match, title match, Save prefix match, retained-recovery template match, discard template match; Static count and aggregate bounded length only |
| exception evidence | Outer and deepest exception type and numeric HResult only; never exception Message/Data/stack or invocation text |

Set the stage **before** each operation, split the compound failure guard into
named decisions, and derive flags from one bounded in-memory observation rather
than re-reading solely for logging. Retain stage/reason in pre-exit and final
rows. Check acknowledgement PostMessage return values, without widening any
allowlist. Keep the content-free failure event evidence distinct from the
editor's Save exception HResult: a close exception is not a Save exception.

If stage evidence proves a modal-readiness race, the smallest safe behavioral
correction is a bounded retry of recognition within the existing close budget,
still requiring the exact owned window, title, template, and button before any
acknowledgement. Unknown content must remain undisclosed and undismissed.
If it instead proves a newline/Static-text mismatch, correct only that observed
representation contract and extend non-GUI helper tests; do not broadly accept
arbitrary text.

The next target result must first prove complete orderly positive-control drain
with exactly one `save.failure.replace/-2147024864` and unchanged original
bytes. Only then can the bounded ordinary samples provide non-reproduction
evidence. Current artifacts alone cannot answer that numeric-control contract.

## Local source references

- [Diagnostic contract](../windows-save-diagnostic-driver.md).
- [Atomic Save investigation and buffered-trace boundary](../windows-atomic-save-investigation.md).
- `tests/Invoke-WindowsSaveDiagnostic.ps1`: `Text`, `DialogText`, close `finally`, and control predicate.
- `src/Mote.Native/NativeEditorController.cs`: `SaveFailureMessage`, `WarnRetainedRecovery`, `OnClosing`.
- `src/Mote.Native/Windows/WindowsEditorShell.cs`: `ShowError`, `ConfirmDiscard`.
- `src/Mote.Telemetry/JsonlTraceSink.cs`: buffered append and orderly final session/flush.
