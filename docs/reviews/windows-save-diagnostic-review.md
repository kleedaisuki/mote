# Windows Save diagnostic independent review

Reviewed 2026-10-01. Scope: `tests/Invoke-WindowsSaveDiagnostic.ps1`,
`tests/New-WindowsSaveDiagnosticPublish.ps1`, current ordinary Windows Save UI,
telemetry serialization/drain, and dedicated documentation when available.
This is static and synthetic-oracle review, not GUI, Native AOT execution, or a
reproduction of error 1175. No production or worker-owned files were modified.

## Final integration decision

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
