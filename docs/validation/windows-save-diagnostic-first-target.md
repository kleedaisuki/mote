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

## Independent instrumentation follow-up

After this report's initial artifact audit, the driver owner added content-free
`close_error_stage`, `close_reason`, `close_predicates`, and deepest bounded
exception type/HResult fields. Independent source review confirmed the serialized
row contains derived booleans/counts/Static styles/lengths only, not the
`MoteSaveDialogObservation.Text` field, title, fixture path, or exception message.
The exact PID/owner/title/purpose/button guards and existing bounded WM_GETTEXT
mechanism remain intact; no speculative dialog representation change was made.

An independent narrow helper harness extracts the four pure functions using the
PowerShell AST and never executes the driver body or Win32 calls:

```powershell
pwsh -NoProfile -File .temp/windows-save-first-target-audit/predicates.ps1
```

Four cases passed: unknown private title/text is absent from serialized output;
current Save prefix and exact discard text produce only their intended flags;
and a private-message wrapped UnauthorizedAccessException exposes only type
and HResult `-2147024891`. Output is retained in
`.temp/windows-save-first-target-audit/predicate-evidence.json`. This supplements,
not repeats, the owner's broader self-test/compiler checks. It does not validate
hosted dialog enumeration, button acknowledgement, orderly exit, or trace drain.
At initial review teardown PostMessage return values were still ignored. The
owner subsequently added `post_ack`/`main_close_posted` and closed reason
`message-post-failed`; independent source review confirmed a false post result
throws rather than advancing or retrying. This acknowledges successful message
queue insertion, not proof that the button actually processed the message.

## Second hosted target delta: failure narrowed to button lookup

Run `36777561397` used HEAD `c38da1c4d52ce8916c44828bb7f6aac68e7d036c`,
diagnostic directory `290525ea78ac434291220c29a542c2e4`, PID 3512, and executable
SHA-256 `27934fb28a6247c86a496bfcd88d87022057bdff3274546f3bd451a2505ec408`
(7,039,488 bytes). The retained artifact root is
`.cache/ci-36777561397-save/` (unlike the first download, no `artifact/` segment).
The instrumented script's CRLF source matches its recorded input pin
`9b3170876f7a64431bf440cb419346ec302ea39fdfa374ca63b7bebbc351d1af`.

| New observation | Consequence |
| --- | --- |
| `owned_match=true`, `title_is_mote=true`, `save_failure_prefix=true` | Failure modal identity/title/purpose guards all passed for this run. |
| Static styles `[3,0]`, lengths `[0,443]`, aggregate 443, cap false | Icon-style Static contributed no text; text Static supplied the recognized prefix. No icon-prefix or truncation failure here. |
| `close_error_stage=failure-ok-lookup`, `button_found=false`, `close_reason=button-missing` | The exact `GetDlgItem(modal,1)` lookup returned zero; no failure-button post occurred. |
| Deepest captured RuntimeException/HResult -2146233087 | This is the script's explicit missing-button guard exception, not the editor's Save HRESULT. |
| Elapsed 0.5834669 s, forced exit, empty traces | Still an immediate failed control teardown, not timed-out Save or complete drain. |

The second finalized fixture and recovery files again compare byte-for-byte
equal to original and X+original respectively; both trace copies are zero bytes.
This was independently checked against the second archive, not inferred from
the first result:

```powershell
python .temp/windows-save-first-target-audit/audit.py `
  .cache/ci-36777561397-save `
  .temp/windows-save-first-target-audit/second-evidence.json
```

### API contract versus observed window topology

The parent investigator supplied checked primary Microsoft documentation:
[GetDlgItem](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdlgitem)
returns NULL when the specified child identifier is absent or the parent handle
is invalid, and can be used for parent/child pairs outside dialog boxes.
[MessageBoxW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-messageboxw)
specifies one OK button for MB_OK and IDOK=1 as a return result. Neither supplied
contract establishes child creation timing or the hosted MessageBox descendant
topology. In particular, a documented return result is not by itself proof of
an immediate child having that control ID.

The archived P/Invoke `IntPtr GetDlgItem(IntPtr h, int id)` matches HWND and int
arguments and HWND result. No incorrect pointer width, CharSet-dependent text
marshaling, or exception indicating an absent entry point is evident. It does
not declare `SetLastError=true`, so a later managed last-error read would not be
usable evidence; the current row contains no GetDlgItem-specific OS error.

What is now ruled out for the second run: title/purpose guard rejection, an icon
polluting the recognized prefix, text-cap truncation, failure-button PostMessage
rejection, and any later recovery/discard warning mismatch. These do not
retroactively identify the first run's unknown guard.

What remains unresolved:

- **Initialization race:** title and Static text may be ready before a discoverable
  button; no successive lookup or button inventory exists. The sub-second time
  is compatible with this, not proof of it.
- **Nested descendant:** an ID1 button might be below an intermediate parent;
  the immediate lookup alone does not inventory the descendant tree. The current
  recursive Static enumeration proves only the presence of those Static nodes,
  not any button's location.
- **Different control ID/class:** the displayed MB_OK action might be implemented
  using a different control identifier or class. MB_OK/IDOK return semantics do
  not establish the implementation's child layout from these artifacts.
- **Handle lifetime:** the modal might become invalid between recognition and
  lookup. No post-lookup IsWindow/owned-modal recheck was recorded. There is no
  positive evidence of this transition.

### Minimal next probe, still fail-closed

Before choosing a behavioral workaround, add a capped, content-free descendant
inventory when expected-button lookup is zero. EnumChildWindows already provides
the needed traversal. Record at most a small fixed number of descriptors with:

- exact `Button` class match (boolean, not arbitrary class strings);
- numeric GetDlgCtrlID, immediate-parent match, same-PID match;
- visibility/enabled booleans and bounded button style;
- total descriptor count, cap/ambiguity flag, expected-ID candidate count;
- whether the modal still exists and still passes its exact PID/owner/title/purpose
  validation after discovery.

Do not serialize button captions, HWND values, text, paths, or screenshots. Do
not select the first child, use localized `OK` captions, post WM_COMMAND(IDOK)
directly to bypass discovery, or send global keyboard input.

A short bounded observational retry, within the existing close budget, can
distinguish `missing -> direct ID1 appears` from a stable missing lookup; retain
initial and final classifications plus poll count. This is not a Save retry.
If inventory proves a nested button, a prospective fallback must require one
unique visible/enabled `Button` descendant with exact expected ID and same PID,
an uncapped unambiguous inventory, and fresh validation of the same owned modal
before clicking. No ID match, duplicates, unknown class, invalid modal, or unknown
purpose remains a refusal. First collect topology evidence rather than claiming
this fallback is already necessary or sufficient.

Successful posting remains only an acknowledgement of queue insertion; dismissal
and normal process exit must still be observed. The second run narrows the
diagnostic defect, but does not yet establish numeric Save failure evidence or
ordinary Save reliability.

## Independent button-certification candidate validation

The owner subsequently implemented `Select-DialogButton` and
`Wait-CertifiedButton`, plus a 32-control native descendant inventory. The
candidate inspected here has script SHA-256
`5075c801c5094bd85649dac3c78c713336cc7436e442e83244ff9fca549beabc`.
Only the pure selector was executed: the harness extracts that function through
the PowerShell AST without invoking the driver body, native helper or GUI.

```powershell
pwsh -NoProfile -File .temp/windows-save-first-target-audit/buttons.ps1
```

The acceptance basis is the safety contract, not the old GetDlgItem assumption:
one exact-ID, same-PID, real descendant, visible/enabled Button is required;
overflow, ambiguity, and contradictory direct lookup cannot certify a click.
An otherwise matching hidden duplicate is still ambiguous, not grounds to select
the visible one. A wrong-ID or non-Button child cannot stand in for the action.

| Independent fake snapshot | Expected and observed |
| --- | --- |
| Unique direct ID1; unique direct ID6 | Exact handle accepted as direct |
| Unique nested ID1 with no direct result | Exact handle classified descendant |
| Two exact Button IDs, including one hidden duplicate | Zero handle, ambiguous |
| Wrong ID; non-Button; empty inventory | Zero handle, missing |
| Foreign PID; not a descendant | Zero handle, unsafe |
| Hidden; disabled | Zero handle, not-ready |
| Direct result disagrees with unique inventoried Button | Zero handle, direct-invalid |
| Overflow even with a valid direct candidate | Zero handle, overflow |
| Unrelated other-ID/non-Button controls plus one valid direct candidate | Only valid direct handle accepted |

All **15 cases passed**. Evidence is
`.temp/windows-save-first-target-audit/button-selection-evidence.json`.
The fake handles never enter an OS call. This is certification-policy coverage,
not proof of the native inventory's actual hosted contents.

Source review of the non-pure readiness wrapper separately confirmed:

- Each polling attempt rechecks the same PID/owner-filtered dialog, exact title
  and current purpose before inspecting buttons. Owner/title/purpose changes
  cause refusal, not relaxed matching.
- Overflow, ambiguity, unsafe ownership/ancestry, and contradictory direct
  results refuse immediately.
- Direct construction is allowed a grace interval: descendant acceptance requires
  more than one attempt and at least 250 ms. Missing/not-ready observations can
  be polled at 25 ms intervals without issuing any clicks.
- Acceptance is checked again against the local 1,000 ms and child 38-second
  limits. No candidate may be returned after those checks fail; expiration
  marks the button missing and throws.
- Serialized button metadata includes numeric IDs and bounded booleans, not
  raw handles or captions. The inventory's 32-item cap causes fail-closed
  selection. Button text is never read by the inventory.

The 1-second local readiness budget is an acceptance deadline, **not** a hard
wall-clock bound: preceding bounded cross-process text calls may consume time
beyond it, and an in-flight call is not preempted by the local loop. The existing
owned-child 45-second watchdog remains the independent lifetime safeguard.

No material defect was found in this checked slice. In accordance with the
request to execute only pure extraction, the owner/title/purpose transition
branches and timeout branch were source-reviewed but **not executed** here.
Actual native enumeration, modal lifetime between certification and posting,
MessageBox readiness, successful dismissal and orderly tracing drain remain
target-platform checks. The second artifact does not prove the fallback was
needed: the next hosted inventory must distinguish direct-readiness recovery
from a genuinely nested candidate or continued refusal.

## Local source references

- [Diagnostic contract](../windows-save-diagnostic-driver.md).
- [Atomic Save investigation and buffered-trace boundary](../windows-atomic-save-investigation.md).
- `tests/Invoke-WindowsSaveDiagnostic.ps1`: `Text`, `DialogText`, close `finally`, and control predicate.
- `src/Mote.Native/NativeEditorController.cs`: `SaveFailureMessage`, `WarnRetainedRecovery`, `OnClosing`.
- `src/Mote.Native/Windows/WindowsEditorShell.cs`: `ShowError`, `ConfirmDiscard`.
- `src/Mote.Telemetry/JsonlTraceSink.cs`: buffered append and orderly final session/flush.
