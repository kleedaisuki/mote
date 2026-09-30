# Windows Save diagnostic independent review

Reviewed 2026-10-01. Scope: `tests/Invoke-WindowsSaveDiagnostic.ps1`,
`tests/New-WindowsSaveDiagnosticPublish.ps1`, current ordinary Windows Save UI,
telemetry serialization/drain, and dedicated documentation when available.
This is static and synthetic-oracle review, not GUI, Native AOT execution, or a
reproduction of error 1175. No production or worker-owned files were modified.

## Certified button readiness / descendant fallback review

Reviewed the uncommitted follow-up to second hosted run `36777561397` from the
actual source, not only its implementation summary. Driver SHA-256:
`5075C801C5094BD85649DAC3C78C713336CC7436E442E83244FF9FCA549BEABC`.
Dedicated driver documentation SHA-256 after archive-path correction:
`B9E3837694744C73B9FF56D1CB1C9DCB30113246C01CFDB34184711C2DADCF6D`.
Worker fake/parser/compiler evidence is
`.cache/windows-save-diagnostic-validation/second-hosted-followup-evidence.json`.

**No substantive implementation blocker found.** This remains a diagnostic
driver repair candidate, not evidence that hosted normal close/drain works or
that nesting/initialization caused the previous missing IDOK. The second
archive narrowed its immediate failure to an absent direct ID1 lookup; it did
not establish the topology needed for a successful fallback.

| Review dimension | Source-level conclusion |
| --- | --- |
| Revalidation | Every readiness attempt finds the same visible `#32770` modal by exact child PID and main-window owner, requires identical HWND and exact title `mote`, and repeats the previously allowed purpose check. Save failure, exact fixture-specific retained warning, and exact discard text use the same helper. No different/unknown modal is substituted. |
| Button identity | The capped 32-control snapshot supplies actual control ID, exact class-is-Button, same PID, `IsChild` ancestry, visibility and enabled state. Selection requires one exact-ID Button. Duplicates, overflow, foreign/non-descendant control, and inconsistent nonzero direct lookup are immediate refusals. A matching direct lookup is cross-checked against that certified descendant, rather than trusted alone. |
| Fallback scope | Direct buttons may be accepted immediately. Descendant fallback requires at least two observations and 250 ms grace. Missing/disabled/invisible candidates can be reobserved, but never clicked unless certified within the finite deadline. No caption, arbitrary first child, ID change, dialog-wide WM_COMMAND, clipboard, or global input is introduced. |
| Time and mutation | The loop admits work only before one second and child second 38, then checks both again before returning a selected handle. The polling helper contains no UI mutation. Read calls may finish after the nominal deadline; such a late-ready result is refused, not used for posting. These are decision deadlines, not hard real-time guarantees. Ordinary process scheduling/handle-lifetime races remain the existing controlled-runner assumption, not a claimed atomic cross-process certificate. |
| No duplicates | Failure OK is posted once before disappearance wait. Retained-warning and discard flags are set only after successful posts and prevent repeat mutation. The helper polls identity/readiness only; it does not retry posting. The existing PostMessage result checks and disappearance/normal-exit requirements remain. |
| Privacy and cleanup | Only bounded IDs, counts, fixed modes, booleans and successful readiness time enter `button_lookup`; HWNDs and labels remain in memory. No raw text is added. Failure still enters the existing guaranteed owned-child teardown and incomplete-forced-exit semantics. |

Independent supplemental pure-selector tests extracted only `Select-DialogButton`
through the PowerShell AST, without running the script entry point or native
imports. Four cases absent from the worker's listed self-test passed: inconsistent
direct handle refuses `direct-invalid`; invisible control stays `not-ready`;
non-descendant refuses `unsafe`; ID1 cannot satisfy requested ID6 (`missing`).
No GUI, publish, old executable, or completed helper suite was rerun.

Microsoft's [IsChild contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-ischild)
defines transitive parent/descendant membership; the explicit check is therefore
appropriate for nested controls. [EnumChildWindows](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumchildwindows)
includes descendant windows but does not include controls created during its
enumeration, supporting bounded reobservation without proving a timing race.

One documentation-only correction was requested: the second archive is directly
under `.cache/ci-36777561397-save/windows-save-diagnostic/`, not an `artifact/`
subdirectory. The worker corrected this and the final documentation hash above
pins the correction. This does not affect executable behavior or certification.
The independent validator additionally reports 15/15 pure-selector cases,
including hidden duplicates and unrelated IDs; these do not execute the native
inspection or readiness loop. Real hosted normal close/drain remains unverified.

## Hosted-failure instrumentation delta review

Reviewed after hosted run `36776080244` / job `110094208431`. The retained
first-target audit is `docs/validation/windows-save-diagnostic-first-target.md`.
That run established a preserved original target and exact attempted snapshot
in its recovery sidecar, but failed diagnostic teardown, forcibly terminated
the positive-control child, retained empty traces, and launched no ordinary
children. It did not establish the numeric Save failure code or complete trace
control. The cause of the immediate close exception remains unproven.

**No substantive blocker found in the uncommitted instrumentation-only delta.**
Reviewed driver SHA-256:
`F5E8F67EC41A637A775A04841A57E19068CF667705726D904216BA62BC240F3E`.
The publisher, production Save UI, and native Save policy are outside this
delta and were not changed by it. No GUI, old executable, publish, or helper
test was rerun by this reviewer; the independent validator's four helper
cases are separate evidence, not a target-execution result.

| Contract | Delta conclusion |
| --- | --- |
| Serialized privacy | `close_error_stage` and `close_reason` are fixed literals. `close_predicates` contains only booleans, text lengths, Static control counts/style categories, truncation indication, and button presence. Exception evidence contains deepest bounded type/HResult only. Dialog title/text, synthetic warning path, exception message, exception data, and text digests are not assigned to the serialized row. The `MoteSaveDialogObservation.Text` field remains in memory; the observation object itself is never serialized. |
| Modal recognition | Original exact child PID, visible class `#32770`, owned main HWND, title `mote`, purpose predicates, fixture-specific retained-warning template, and IDOK/IDYES lookup gates remain intact. Failure-modal owner/title checks were separated for diagnosis, not relaxed. Close-loop `owned_match=true` follows a successful exact PID/owner-filtered `Find`, not an unverified caller assertion. |
| Native observation | `InspectDialog` retains the same Static-child enumeration, bounded WM_GETTEXT reads and concatenation as its predecessor. Added GWL_STYLE masking records only `SS_TYPEMASK` categories; it does not skip icon text, reorder children, broaden recognized text, or introduce global input. |
| Error handling | API/guard exceptions still terminate the close attempt and enter the existing safe teardown. The additional bounded exception unwrapping does not convert a guard failure into success or suppress the outer guaranteed process cleanup. Nulling `close_error_stage` occurs only after real normal exit without watchdog force. |
| Trace/outcome semantics | Forced termination remains `incomplete-forced-exit` even if a terminal record exists. Complete capture still requires normal zero exit, one final session, semantic parse validity, and no dropped events. The positive-control assertion still blocks every ordinary child unless the original-byte/dirty/exact replace-HResult/complete-trace contract passes. |

The delta is suitable for integration and a separately authorized current-source
target run. It is **diagnostic instrumentation, not a demonstrated teardown
fix**. A subsequent failed row can now distinguish owner/title/purpose/button
guards and wrapped API exceptions without exposing raw dialog text. Do not
infer the root cause from the first run's outer RuntimeException alone, and do
not interpret this review as approval to weaken unknown-dialog rejection.

### Final message-post acknowledgement delta

The final worker edit arrived after the initial instrumentation review
(`3C166D12A81A5C84ADC3C18832FBFAFA37465FCA9A5987B54D6005AF47FF6266`).
It changes exactly four previously ignored PostMessage results: failure OK,
main WM_CLOSE, retained-warning OK, and discard Yes. The result is now recorded
as a boolean (`post_ack` or `main_close_posted`); false sets the fixed
`message-post-failed` reason and throws into the existing safe teardown. Warning
and discard acknowledgement flags are set only after a successful post.

**No substantive blocker found in this final delta.** These stricter error
checks neither widen modal recognition nor alter complete/forced trace rules.
Post acknowledgement means queue acceptance, not completed dialog handling or
process exit; the existing disappearance/normal-exit and terminal-trace checks
remain necessary and present.

Independent static verification reconstructed the previously reviewed script
in memory by removing only those four result checks, and obtained exactly its
`3C166D...` SHA-256. This establishes that no other edits were missed between
the two reviewed revisions. The final PowerShell parser returned zero errors,
and script/review `git diff --check` passed. No native message, GUI, publish,
or prior helper test was executed for this check.

## Initial frozen-candidate integration decision

**No remaining substantive blocker found in the revised frozen candidate.**
The driver/publisher/documentation may be integrated. This does not approve or
claim completion of a current-source desktop experiment: clean-source publish,
positive held-share UI control, real orderly trace drain, and hosted execution
still require their own opt-in validation. The reviewer did not execute native
GUI code, publish, or an old executable.

Frozen SHA-256 reviewed:

| File | SHA-256 |
| --- | --- |
| `tests/Invoke-WindowsSaveDiagnostic.ps1` | `17EFCF3D86FE6B77368D84B1099F2EA31C94B813211AA0D6207C189DC3751BB3` |
| `tests/New-WindowsSaveDiagnosticPublish.ps1` | `F849E683B779C7F16F3E63C4DDBADF74DDA1534836183BF042790BF77E5B7997` |
| `docs/windows-save-diagnostic-driver.md` | `F7BCB4EBDD43C5078C2D1B9D5D19632E0CAEF80A82D306BA6469D048C16251DB` |

## Initial actionable findings and correction tracking

| Priority | Finding | Evidence / impact | Status |
| --- | --- | --- | --- |
| P1 | Failure-dialog acknowledgement used historical `Save failed;` prefix | Current `NativeEditorController.SaveFailureMessage` emits `Save failed. ` for structured failure information, including the held-share control. The control could not close normally, so its trace was forced/incomplete and rejected. | Corrected: current prefix and title verification; worker fake controls cover current/unrelated messages. |
| P1 | Full recursive fixture copy ran before terminating an indeterminate Save, and outside guaranteed cleanup | Copy opens target/staged bytes while commit might be running, contradicting the observer safety contract. A copy error could escape before process termination. | Worker now snapshots traces only before termination, checks exit before target reads, and wraps the iteration in an outer process/holder cleanup. Static correction confirmed. |
| P2 | Terminal record alone treated dropped trace events as complete | The real sink uses a bounded nonblocking queue and emits `telemetry.dropped`. A failure event can be lost while normal drain still produces `mote.session`. | Corrected: any dropped record prevents completeness; worker fake control covers it. |
| P2 | `File.Exists == false` treated as confirmed missing bytes | .NET suppresses access/I/O errors in this API. An unreadable target can therefore become an apparent high-severity missing-file result. | Corrected: direct read, only not-found exceptions imply missing, other failures unknown; worker sharing-denial control covers it. |
| P1 | Closing a retained-recovery document shows an unrecognized warning before discard | `NativeEditorController.Closing` calls `WarnRetainedRecovery` before `ConfirmDiscard`. The held-share control necessarily retains pending recovery; its first close dialog is IDOK with `The retained Save recovery will remain after closing this document:`, not the expected discard/IDYES dialog. The candidate kills the child and rejects its positive control. | Corrected: exact template includes the fixture-specific deterministic hashed sidecar path, bounded version, PID/owner/title and IDOK; acknowledged once before discard. Compared against actual Engine path construction and Native warning; worker foreign-path fake control passes. |
| P2 | Incremental publish could produce a manifest rejected by the consumer's fresh-executable timestamp gate | A new publish destination alone does not force new native build outputs. Reusing up-to-date AOT intermediates could copy an older binary into a new directory and create an unusable attestation. | Corrected: unique repository-cache `ArtifactsPath` isolates all project outputs/intermediates; producer itself rejects an executable predating this build. |

The earlier semantic-JSON concern is corrected: valid JSON with missing operation
or a failure with missing numeric HResult is now rejected as incomplete rather
than throwing from strict property access.

## Independent synthetic evidence

The reviewer extracted only `Read-TraceEvidence` through the PowerShell AST;
the script entry point, publish, native imports, and GUI were not executed.
Artifacts: `.temp/windows-save-review-b88b6f067bc44a55bb9f883033b65a52/`.

At the probed revision, two records consisting of
`telemetry.dropped / attributes.count=1` followed by `mote.session` returned
`capture=complete`, `failures=[]`, `terminal_session=true`. This demonstrates
the completeness issue independently of speculative GUI timing.
Separate `{}` and missing-HResult synthetic records were correctly incomplete
after the worker's semantic-validation correction.

An independent in-memory extraction of `New-FixtureBytes` produced these exact
digests, matching the retained historical synthetic fixtures documented in the
investigation:

| Byte sequence | SHA-256 |
| --- | --- |
| Original 1,048,576 bytes | `69f4396b7a4d42b022627414f8711cc4b62292f35fbd415b34daa201fac08aea` |
| ASCII X + original, 1,048,577 bytes | `2d12a1cbc6df198198d7b6de8c38a3e3f3bd8ffaf3bb73025effa9026c1d75f0` |

## Positive contract inspection

- The fixture is generated locally as exactly 1,048,576 ASCII bytes; each
  1,024-byte record begins `STARTUP-MARKER # note\n`, ends LF, and is otherwise
  `a`. The expected save is precisely ASCII `X` prepended to those bytes.
  Comparisons check complete byte arrays, not only hashes or a dirty title.
- The no-Delete-share holder is owned by the diagnostic, not mote, and only
  used for the positive control. Ordinary children hold no target observer.
- Save command 203 matches `WindowsEditorShell.SaveId`. Selection uses
  `EM_SETSEL` / packed `EM_GETSEL`, followed by `WM_CHAR('X')`; no clipboard,
  global key injection, arbitrary user document, or Save As picker is used.
- Windows are found by exact child PID and class; dialogs additionally require
  the owned main-window owner. Messages use bounded `SendMessageTimeoutW`.
  An absent canvas returns false before child lookup: it cannot accidentally
  turn `FindWindowEx(NULL, ...)` into a desktop top-level window lookup.
- Watchdog callbacks catch invalid-process and Win32 failures rather than let
  an unhandled timer-thread exception terminate the PowerShell host. Timed
  exit is checked before target reads even if forced termination fails.
- Synthetic homes and fixtures stay beneath repository `.temp`; retained
  rows, traces and publish output stay beneath `.cache`. Existing ancestors
  are checked for reparse points and permitted owner SIDs. This is a local
  same-user experiment boundary, not protection against an adversary racing
  filesystem object replacement.
- The independent producer requires clean source/build inputs, hashes all
  tracked files before/after publish, pins HEAD, SDK, runtime pack, log,
  binary hash/length, and rejects inventory other than one real `mote.exe`.
  The consumer has no executable fallback and rejects stale/current-source
  mismatches before native execution. Dirty current production changes are
  deliberately not executable through this gate until integrated.
- Normal exit, exit code zero, and last terminal session are all needed for
  a complete capture. A forced child remains explicitly incomplete.
- Scratch and final evidence are retained, including recovery sidecars. No
  cleanup deletes unique attempted-save bytes.

## Evidence limits and references

The manifest is local provenance/attestation, not a cryptographic signature or
an adversarial supply-chain verifier. The bounded ordinary sample is neither a
failure-rate estimate nor a latency benchmark. No fresh publish or desktop run
has been authorized or executed by this reviewer.

Microsoft's [SendMessageTimeoutW contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw)
supports bounded system-message calls and distinguishes API success from the
message result. Microsoft's [Process.WaitForExit contract](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexit?view=net-10.0)
requires checking timed termination rather than assuming Kill is synchronous.
Microsoft's [artifacts output layout](https://learn.microsoft.com/en-us/dotnet/core/sdk/artifacts-output)
supports per-project output/intermediate isolation beneath the unique build
root. The worker additionally evaluated SDK output properties without running
build/restore, recorded under `.cache/windows-save-diagnostic-validation/`.
The new producer's actual restore/publish has not yet been executed.
Internal production evidence is `JsonlTraceSink.TryRecord`, its guaranteed
normal-drain drop aggregate, and `NativeEditorController.SaveFailureMessage`.
Crash durability remains a separate question, as already documented in
`docs/save-failure-recovery.md` with the OSDI 2014 persistence study; this driver
does not attempt to establish that stronger contract.
