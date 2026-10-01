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

## Third hosted target delta: unique Button exists, but has child ID2

Run `36778718268`, HEAD `61413809a5c24fecd8c6e74510ee4031f045f428`,
diagnostic run `2a8ec3ca26fe42f49d5da31f75bb3d2a`, PID 3756, started at
`2026-09-30T21:21:25.6034591Z`. Its executable pin is SHA-256
`4ee015871aac458691ced20bde8048a6c8f3b4a7234b2e95eb7d22bc1fe01cf6`,
7,039,488 bytes. OS/SDK/runtime/filesystem remain 10.0.26100.0 / 10.0.401 /
win-x64 10.0.12 / NTFS; runner image is **20260922.246.2**, different from the
first two targets' 20260925.250.1. Do not describe these as identical environments.

The third script is the independently tested candidate above; CRLF expansion
matches archived input SHA-256
`b48c829e13034ac9d15fef6d3a6248772997ac6306d4905ebc835193dd844143`.
At the recorded HEAD, `ShowError` still uses
`MessageBoxW(_window, message, "mote", MB_OK | MB_ICONERROR)`.

| Exact row field | Observation |
| --- | --- |
| `close_error_stage`, `close_reason` | failure-ok-lookup, button-missing |
| Modal predicates | owner, title and Save prefix true; Static types [3,0], lengths [0,443]; no cap |
| `button_lookup` | expected_id=1, attempts=28, mode=missing, direct_present=false, ready=false, overflow=false |
| Inventoried Button | child ID2; same PID, direct child and descendant, visible and enabled |
| Other children | IDs 20 and 65535; both non-Button, owned/direct/visible/enabled |
| Timing / termination | 1.4960979 s total; normal_exit=false; exit_code=-1; incomplete-forced-exit |
| Trace | zero records; no failure HResult or terminal session |

The row retains **the last inventory**, not each poll's inventory. Twenty-eight
attempts and the loop's guards establish that no certifiable expected-ID button
was accepted during the readiness period and modal purpose checks did not fail.
They do not establish that ID2 or every topology field was identical at all 28
polls. A transient expected-ID candidate could also have been not-ready. This
distinction matters when interpreting an append-free final row.

Independent full-byte comparison again confirms original target and exact
X+original recovery; both archived trace copies are empty:

```powershell
python .temp/windows-save-first-target-audit/audit.py `
  .cache/ci-36778718268-save `
  .temp/windows-save-first-target-audit/third-evidence.json
```

### What this resolves, and what it does not

There is a real visible/enabled direct Button in the final owned dialog. This
narrows the problem beyond an empty/incomplete button tree or a purely nested
ID1 fallback. Extending the same ID1 wait alone has no supporting evidence as a
fix. The current exact-ID discovery safely refused rather than clicking ID2.

Microsoft's documented **MessageBox return values** IDOK=1 and IDCANCEL=2 are
semantic results of MessageBoxW. The API's MB_OK contract specifies one OK
button. Neither is a guarantee that GetDlgCtrlID of its displayed child matches
that semantic return value. Therefore:

- Child ID2 does **not** prove the sole displayed action is Cancel.
- MB_OK source plus one Button makes an OK action plausible, but the archive
  has no caption or independently certified action-role observation.
- ID2 must not be installed as a global OK alias or accepted for arbitrary
  dialogs. No actual MessageBox return value was recorded in this run.

The existing P/Invoke is not contradicted by finding ID2. The observation instead
exposes the diagnostic's additional, unestablished assumption that an MB_OK
action must be discoverable as child ID1 on this hosted platform.

### Smallest fail-closed caption/action certification

For the already validated **Save-failure or retained-recovery-warning** modal
(both production MB_OK calls), a narrow next probe can inspect the unique Button
caption **in memory only**, using the existing bounded Unicode WM_GETTEXT helper
with a small fixed buffer. Require an uncapped inventory containing exactly one
Button, expected PID/ancestry, visible/enabled state, unchanged owned modal,
exact title, and exact existing purpose recognition. Do not choose merely the
first or only button without the semantic predicate.

For the English hosted case, an explicit tiny classifier may accept exact `OK`
and exact `&OK` (access-key variant). Everything else—including unknown/localized
labels, empty text, cap-hit text, Cancel/Yes/No, whitespace variants, and additional
buttons—must refuse until independently specified. Do not use substring,
case-insensitive, arbitrary ampersand stripping or fuzzy label normalization.
Record only `caption_matches_ok`, bounded caption length/cap flag and a closed
selection mode; never raw caption, host-derived hash, HWND, screenshot or
exception message. A numeric UI-language identifier can contextualize a refusal
without exposing text.

After discovery, freshly revalidate the same modal and the selected Button's
ownership/ancestry/visible/enabled state and purpose before posting its existing
BM_CLICK. A positively certified unique OK action is a safe same-modal
acknowledgement for these two synthetic MB_OK purposes; it is not an authorization
for arbitrary close dialogs, overwrite confirmation or dirty-buffer decisions.
The discard branch should retain its separately specified Yes contract until
target evidence warrants its own change. Do not bypass certification by sending
WM_COMMAND(IDOK), clicking child ID2 by convention, global Enter/Escape, or
another unobserved shortcut.

This classifier is a prospective narrowly scoped contract, not a claim that the
third Button's label was OK. The next artifact should retain its boolean result
and lookup mode, then still require observed dismissal, normal exit and complete
positive-control trace. A successful same-modal click does not itself establish
Save phase/HResult or ordinary reliability.

## Independent single-OK-caption candidate validation

The next frozen candidate, script SHA-256
`bdffde0952018a1497e2e76904b8c92ae7f9ad0fbcbdc6677f1c5b657bff4b59`,
adds `Select-SingleAcknowledgementCandidate` and
`Test-AcknowledgementCaption`. Independent execution extracted only these pure
helpers and the existing ID selector through the AST:

```powershell
pwsh -NoProfile -File .temp/windows-save-first-target-audit/acknowledgements.ps1
```

**37 checks passed**, with evidence in
`.temp/windows-save-first-target-audit/acknowledgement-evidence.json`:

- 18 layout/purpose cases cover both permitted MB_OK purposes; discard/unknown
  purpose; wrong requested ID; existing direct ID1; overflow; multiple Buttons
  including a hidden second Button; foreign PID; non-descendant/nested;
  disabled/invisible; wrong child ID; non-Button; empty tree; and the observed
  ID2 plus non-Button IDs20/65535 layout.
- 17 caption cases accept only exact `OK` and `&OK`. Cancel, Yes, private unknown
  text, Chinese localized label, changed case, whitespace, prefix/suffix
  substrings, alternative ampersands, empty/cap-sized strings, embedded NUL and
  trailing newline all refuse.
- Two different fake candidate handles remain distinguishable, supporting the
  explicit comparison used by the runtime wrapper. The existing direct ID6
  discard selector still returns its exact certified Yes candidate.

Private/localized caption values do not appear in the harness's serialized
evidence. Source inspection also confirmed the driver's row assignments contain
only caption length/cap/allowed predicate, candidate ID and closed mode: neither
the first nor confirmed raw caption is serialized. Native
`AcknowledgementCaption` verifies exact Button class, ID2, expected PID,
ancestry/direct parent and visible/enabled state before bounded Text(button,16).

The wrapper waits for its grace interval before using the caption fallback;
then rechecks same-modal ownership/title/purpose, takes a fresh complete snapshot,
requires the same candidate handle, and repeats identity/caption certification.
An exact expected-ID direct/descendant path remains preferred. Discard cannot
enter the caption fallback, even with an apparent OK label. The final local and
child deadlines are checked before returning an actionable handle; the caller
still checks PostMessage and waits for dismissal.

No material defect was found in this candidate's checked policy/guard path.
Runtime owner/title/purpose changes, native handle replacement, enumeration
races and timeout were **source-reviewed, not executed**. The pure replacement
case establishes comparable identities, not runtime rejection coverage. No GUI,
native calls or retained executable were run. Successful hosted acknowledgement,
normal trace drain and ordinary Save remain unverified by these helper tests.

## Local source references

- [Diagnostic contract](../windows-save-diagnostic-driver.md).
- [Atomic Save investigation and buffered-trace boundary](../windows-atomic-save-investigation.md).
- `tests/Invoke-WindowsSaveDiagnostic.ps1`: `Text`, `DialogText`, close `finally`, and control predicate.
- `src/Mote.Native/NativeEditorController.cs`: `SaveFailureMessage`, `WarnRetainedRecovery`, `OnClosing`.
- `src/Mote.Native/Windows/WindowsEditorShell.cs`: `ShowError`, `ConfirmDiscard`.
- `src/Mote.Telemetry/JsonlTraceSink.cs`: buffered append and orderly final session/flush.

## Fourth hosted run: independent retained-artifact audit

[Run 36780934192](https://github.com/kleedaisuki/mote/actions/runs/36780934192)
at `f76c56e787562127744158122ccc20be5e5cdd42` supplied the first complete
positive-control and four ordinary Save traces. Independent audit command:

```powershell
python .temp/windows-save-fourth-audit/audit.py
```

The audit and machine-readable results remain in
`.temp/windows-save-fourth-audit/`; inputs are the downloaded artifacts under
`.cache/ci-36780934192-save/`. It reconstructs the specified 1 MiB ASCII fixture
without invoking the driver's helpers, then compares actual retained file bytes
(not just reported digests). Expected new bytes are exactly ASCII `X` followed
by the original fixture. It independently parses all JSONL records, checks the
terminal successful root `mote.session`, one session/trace identity, unique span
IDs, resolved parent IDs, no `telemetry.dropped`, and failure-to-Save parentage.

### Provenance and environment

- All **491** recorded publish input digests match the pinned Git commit's blobs,
  allowing only ordinary LF-to-CRLF Windows checkout conversion. The actual
  retained `publish.log` digest also matches its manifest.
- Publish and diagnostic manifests agree on commit, executable digest/size,
  publication timestamp, SDK, runtime pack and exact isolated Native AOT command.
  Reported executable SHA-256 is
  `e6fa70c077d76352fecb46999c5243b67b9285379cf5b74c1c6225744dbfdafc`,
  size **7,068,672 bytes**. **The downloaded diagnostic artifacts do not retain
  the executable**, so this is internally consistent manifest attribution, not
  an independently recomputed executable hash.
- Hosted environment: Windows `10.0.26100.0`, NTFS, runner image
  `20260922.246.2`, SDK `10.0.401`, win-x64 runtime pack `10.0.12`.

| Row | Retained target bytes | Recovery bytes | Save trace | Trace records | Exit / terminal |
| --- | --- | --- | --- | ---: | --- |
| 0: no-Delete-share control | Exact original, 1,048,576 | Exact new, 1,048,577 | One `save.failure.replace`; failed `document.save` | 25 | Normal exit 0; complete |
| 1 | Exact new, 1,048,577 | No recovery slot | Successful `document.save`; no Save failure | 20 | Normal exit 0; complete |
| 2 | Exact new, 1,048,577 | No recovery slot | Successful `document.save`; no Save failure | 20 | Normal exit 0; complete |
| 3 | Exact new, 1,048,577 | No recovery slot | Successful `document.save`; no Save failure | 20 | Normal exit 0; complete |
| 4 | Exact new, 1,048,577 | No recovery slot | Successful `document.save`; no Save failure | 20 | Normal exit 0; complete |

Original SHA-256:
`69f4396b7a4d42b022627414f8711cc4b62292f35fbd415b34daa201fac08aea`.
New SHA-256:
`2d12a1cbc6df198198d7b6de8c38a3e3f3bd8ffaf3bb73025effa9026c1d75f0`.
Row 0 has exactly the deterministic recovery slot derived from `FIXTURE.MD`;
its bytes equal the new buffer exactly. Row 0 remains dirty after failed Save;
ordinary rows are clean after successful Save. Each retained final trace copy
is byte-identical to the corresponding trace-only evidence copy. No close-stage,
child error, or forced-exit outcome is reported.

### Interpretation and remaining boundaries

The captured failure is now **observed**, not inferred:
`save.failure.replace`, HResult **-2147024864 / 0x80070020** (Win32 low code 32),
causally parented to the failed `document.save`. This is the intentional held
no-Delete-share control; it **does not reproduce or explain** the historic
message-derived 1175, nor exercise the documented 1176 target-missing outcome.
The successful normal close and terminal trace close the earlier diagnostic
teardown/zero-byte-trace gap without weakening the expected target/recovery
bytes.

The final row 0 close predicates and button inventory describe the later dirty
buffer discard dialog (ID6); they overwrite earlier acknowledgement state.
Therefore these artifacts establish successful overall acknowledgement/close,
**not which ID1 or certified ID2-caption fallback branch handled each preceding
MB_OK modal**. The branch remains supported by source/helper validation rather
than a separately retained hosted per-modal event history.

**Verdict:** all checked retained-artifact contracts pass; no material product
defect was found. Four ordinary synthetic saves in one hosted run are not a
reliability estimate, latency benchmark, physical durability/power-loss test,
restart recovery/export test, or proof about arbitrary user files, filesystems,
security software or platform variants.

## Current-source follow-up: pre-Save operation failure (2026-10-02)

[Run 36899695843](https://github.com/kleedaisuki/mote/actions/runs/36899695843),
job `110495482950`, at `6827cd1a19142c9ad766d296c18d0770e600e79c`
has aggregate run conclusion `success`, but the separate non-gating Windows Save
replacement diagnostic job failed its positive-control assertion. This is **not
a product Save failure**: no Save command was posted. Publication succeeded;
the failed assertion concerns the subsequently launched diagnostic child.

The downloaded log and original eight artifact files are retained under
`.cache/ci-36899695843-save-diagnostic/`. Independent archive-only audit:

```powershell
python -B .temp/windows-save-682-audit/audit.py
```

The script and `evidence.json` reconstruct the synthetic fixture independently,
compare complete archived bytes, parse both trace copies, check span identities
and parent resolution, and verify all **802** recorded source-input digests
against the exact Git commit (permitting LF-to-CRLF checkout conversion only).
There are no input mismatches. The retained publish log also matches its recorded
digest. Publish and diagnostic manifests agree on executable SHA-256
`c6041a5007813211639069b4e34960cee624fa82a0fd3d3c6c083ab96b9692da`,
**8,134,144 bytes**; the executable itself is not retained, so this remains
manifest-consistent attribution, not independently recomputed binary identity.

| Observation | Actual retained evidence |
| --- | --- |
| Environment | Windows 10.0.26100.0, NTFS, image 20260925.250.1, SDK 10.0.401, win-x64 runtime pack 10.0.12 |
| Publish completion | Native output logged at 17:30:36 UTC; manifest completion 17:30:37.1122838 UTC |
| Child | PID 1384, start 17:30:52.2852291 UTC, positive control ordinal 0 |
| Main operation | `infrastructure-failure`; outer `System.Management.Automation.MethodInvocationException` only |
| Control holder / Save | `held_ack=false`, `save_posted=false`; neither reached |
| Teardown | Main close posted; normal exit 0; no forced termination or close error |
| Elapsed child operation | 1.0750093 seconds, not a Save deadline timeout |
| Archived target | Exactly original 1,048,576 bytes; SHA-256 `69f4396b7a4d42b022627414f8711cc4b62292f35fbd415b34daa201fac08aea` |
| Trace | Two byte-identical copies, each 2,990 bytes and 9 valid rows; successful terminal `mote.session`, no dropped record |
| Later work | No ordinary children, no summary, no recovery sidecar |

Trace SHA-256 is
`77815af184bacf757dc944b63b643c44abc195e9b6927cadccba8ec43935bd81`.
It records initial empty-document layout/analysis and startup, successful
`document.open`, then `document.open_to_editable` cancellation during teardown.
There are no `document.edit`, `document.save` or `save.failure.*` rows in this
complete captured session. The explicit driver's `save_posted=false` is the
stronger evidence that this diagnostic did not attempt Save; trace absence is
not generalized to other sessions or uninstrumented operations.

### Exact attribution boundary and next safe action

The inner operation catch recorded only the outer exception type. A
`MethodInvocationException` does **not** identify its wrapped exception or the
API that threw. The source contains bounded text reads, acknowledged UI sends,
process identity reads and native lookups before opening the control holder.
The archived row cannot distinguish these. In particular, a RichEdit readiness
or WM_GETTEXT timeout is a hypothesis, not an established cause. Normal teardown
cancelled an in-flight document-open-to-editable operation; that cancellation
does not establish an independent product opening fault.

The follow-up changes only `tests/Invoke-WindowsSaveDiagnostic.ps1` evidence:

- `operation_stage` is a source-defined literal identifying the currently
  attempted operation/readiness predicate, distinct from `close_error_stage`.
  A `*-check` label denotes a compound short-circuit predicate, not proof that
  every constituent native call executed. `complete` is set only after ordinary
  outcome classification; failed stages remain present during teardown.
- `error_exception` stores only the existing bounded deepest exception type and
  numeric HResult, while `error_type` preserves the outer wrapper. No exception
  message, stack, invocation text, dialog text or native handle is serialized.
- The original pre-exit and final rows retain these fields. No publication,
  polling, acknowledgement, deadline, byte oracle, failure control, telemetry
  drain or termination behavior is changed; no retries are added.

Local `pwsh -NoProfile -File tests/Invoke-WindowsSaveDiagnostic.ps1 -SelfTest`
passed, including new actual-helper checks for a known operation stage, an
independent teardown stage, nested exception extraction, the two-field error
payload, and exclusion of both outer and inner synthetic private messages.
Removing only the new helper, self-test statements, stage assignments and
evidence fields reproduces the exact frozen driver text after line-ending
normalization. `git diff --check` passed. No GUI/native diagnostic, retained
binary or new CI run was executed locally.

**Remaining acceptance:** a new exact-source hosted execution must identify the
failing operation/deepest exception if it recurs, or establish complete positive
control followed by ordinary Save samples. Do not weaken the control, enlarge
timeouts, or change production Save based on this pre-Save failure.
