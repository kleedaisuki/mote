# Mac Canvas AX timeout-state diagnostics: independent review

Reviewed: 2026-10-01 on Windows. Scope: frozen diagnostic delta in
`src/Mote.Native/Mac/Canvas/MacCanvasAccessibilityProbe.cs`,
`tests/NativeMacCanvasAxWorkflow.ps1`, and its validation note, against
`docs/validation/ci-36752189587-mac-nongating-audit.md`. Surrounding shell
read-only probe getters, the existing Program route, and existing workflow
invocation/upload were inspected. No AX/AppKit execution, host mutation,
production/harness edit, staging, commit, push, or dispatch was performed.

## Decision

**No substantive blocker found in this diagnostic-only delta. Approve the
reviewed sources for the existing bounded in-process diagnostic on fresh,
disposable GitHub-hosted macOS x64/ARM64 runners, subject to the execution
conditions below.** This is not approval of fault recovery behavior: both
original failures remain unresolved and target execution of this delta remains
unverified. Non-gating step/job success is not acceptance evidence.

## Exact frozen file identities

SHA-256 of raw working-tree bytes (line-ending conversion changes these hashes):

| File | SHA-256 |
| --- | --- |
| `src/Mote.Native/Mac/Canvas/MacCanvasAccessibilityProbe.cs` | `A1D73E0FA6E10215BEE3AF25E491FE18FDA4AE62B21C1819D8AB974AC8CC950F` |
| `tests/NativeMacCanvasAxWorkflow.ps1` | `8E4572045DCC01443247C6554E0C05AFCD1007E2A378A6525E4C5B622A027BAE` |
| `docs/validation/native-mac-canvas-ax-state-diagnostics.md` | `3DC5255E6670D635A36BF55360711C4EC519EEFF638E0A7EC1D9DF5231661A72` |

The published executable has not been built or hashed by this reviewer. Bind
execution evidence separately to its actual revision, RID, and Mach-O SHA-256;
the baseline/current executable hashes in the audit are not hashes of this delta.

## Evidence assessment

- Stage 3 now retains the existing final deadline snapshot; stage 5 gains the
  same snapshot. The unchanged guards still require provider detach plus the
  unavailable-status token at stage 3, and a present empty snapshot plus the
  token at stage 5. This differentiates attached provider, missing status, absent
  binding, and nonempty binding without declaring any one to be the root cause.
- Presence flags disambiguate absent snapshots/stamps from negative sentinel
  values. Snapshot version versus binding stamp is recorded independently.
  Before-New stamp capture occurs after insertion N and immediately before the
  existing discard/New calls; its equality fields distinguish that identity
  from the pre-fault reference. Request-return flags explicitly do not certify
  deferred completion. The binding getters inspect `_pendingCanvasBinding`, not
  an independent controller/disk read; the validation note correctly limits
  their interpretation.
- Changed-state polls share a 32-record limit across stages 3 and 5. Equality
  deduplication precedes retention; forced fault request, second-New request,
  and deadline records bypass the poll limit. Poll limit/count remain visible.
  The output ledger is bounded (at most 32 changed polls plus the three forced
  fault-state records on the normal path), and the existing 90-second deadline
  and 45-ms schedule remain unchanged. The limit bounds retained output, not
  getter execution on every tick.
- No stage guard, edit N/M, fault request, New/discard action, source selector
  assertion, success marker, final detached check, or input hash assertion was
  removed or weakened. Added sampling invokes existing read-only getters;
  editable/focus getters use the already existing native selectors. Added
  sampling can fail the diagnostic if a getter throws, but does not turn a
  failed transition into success. Deadline sampling exceptions expose only the
  exception type and still finish unsuccessfully.
- Added native records contain booleans, lengths, versions/generations, timing,
  and fixed labels only. They do not emit text, paths, status strings, hashes,
  native pointers, clipboard values, or home-directory values. The preexisting
  in-memory hash comparison remains only a boolean in the ledger. Existing
  wrapper error handling can include an executable path or stderr; this delta
  does not introduce that behavior, and the content-free claim is appropriately
  scoped to added native state fields rather than all historical artifacts.

## Failed-wrapper evidence

The former unmeasured `false` and `0` initial values are now null. The wrapper
removes only its exact previous RID metrics file before launch and sets the
preparation flag only after that succeeds. It measures input SHA equality and
fresh metrics presence/line count in `finally`, including a nonzero child exit.
A pre-preparation failure cannot attribute stale metrics to that invocation;
`metrics_present=false` means measured absence, while a null count does not claim
an observed empty file. Original child/marker/assertion failure is preserved.

Evidence collection is best-effort: a hash-read exception can prevent the later
metrics measurement within the shared try block. Such fields remain null and an
exception-type `evidence_error` is recorded, so this does not misstate absence or
mutation. A killed/crashed child may never write native metrics because normal
writing occurs after the AppKit loop returns. The patch and validation note do
not claim otherwise. Existing post-success hash/metrics assertions remain intact.

## Execution envelope and permission conditions

Run only the reviewed existing harness command, separately for each published
RID on a fresh idle GitHub-hosted Mac runner:

```powershell
& ./tests/NativeMacCanvasAxWorkflow.ps1 `
  -ExecutablePath "src/Mote.Native/bin/Release/net10.0/$rid/publish/mote" `
  -RuntimeIdentifier $rid
```

- Use the matching published executable from a recorded checkout; do not reuse
  the audit's older binary or treat managed Windows compilation as Mac proof.
- Repository root is the working directory. Fixture and isolated `MOTE_HOME`
  remain under root `.temp`; report/stdout/stderr/native metrics remain under
  root `.cache/ci-inventory/<rid>`. No hostile/concurrent symlink swapping or
  shared concurrent writer to the same fixed RID artifact names is permitted.
  This harness does not enforce a hostile filesystem containment boundary.
- Allowed effects are the existing process-owned AppKit window/focus, synthetic
  fixture edits, fault injection, New/discard and close, plus repository-local
  diagnostic artifacts. No external AXUIElement client, VoiceOver, TCC grant,
  input-source change, clipboard operation, user configuration modification,
  signing/notarization action, or personal interactive-desktop execution is
  authorized by this review.
- Preserve the existing two-minute child timeout/four-minute workflow limit and
  always-upload behavior. Do not weaken assertions to obtain a green step.
- Inspect raw child exit, exact marker, wrapper status/integrity/evidence fields,
  and native stage-3/stage-5 records together. Green non-gating steps alone do not
  resolve the recorded recovery defect. The uploaded synthetic fixture is an
  existing artifact, not a new disclosure of user source text.
- Changes to these frozen source/harness semantics or the route/execution
  envelope require delta review; unrelated concurrent working-tree changes are
  outside this approval.

## Checks and limits

Reviewer ran only scoped `git diff --check` (passed) and PowerShell AST parsing
(zero errors). The validation note reports a warning-free managed Release build
by the owner; that was not repeated. No target executable or runtime assertion
was validated here. The review artifact is the only file written by this reviewer.
