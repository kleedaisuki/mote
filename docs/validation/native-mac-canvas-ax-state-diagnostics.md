# Mac Canvas AX fault / New timeout state diagnostics

Date: 2026-10-01. Scope: diagnostic-only changes to `MacCanvasAccessibilityProbe.cs`
and `NativeMacCanvasAxWorkflow.ps1`. No production shell/controller, Program route,
CI, format policy, or acceptance transition changes.

## Evidence and unresolved question

The [CI audit](ci-36752189587-mac-nongating-audit.md) records a current x64
stage-5 timeout and ARM64 stage-3 timeout, with both baseline RIDs failing stage 5.
The retained current x64 native metrics independently show the second New request
at 996 ms followed by failure at 89,991 ms. Existing metrics do not distinguish
an absent/nonempty binding from lost unavailable status at that deadline. The
failed wrapper's former `false` / `0` fields were unmeasured defaults, not evidence
of file mutation or an empty native metrics file.

## Diagnostic contract

Stages 3 and 5 now record content-free state at their existing deadline, plus
deduplicated changed-state polls. The shared poll budget is 32; forced initial
fault-request, second-New-request and deadline records are not suppressed by that
budget. A poll count equal to its limit means the intermediate sequence may be
truncated; the final deadline sample remains available. Exceptions during deadline
sampling report only exception type and still finish unsuccessfully.

The added fields distinguish snapshot absent versus empty, snapshot version versus
binding version, provider attached, unavailable status token/suffix, and input
editability/focus. Generation/version equality fields retain their existing
**pre-fault** reference. New `before_new_*_equal` fields use the stamp immediately
before invoking the second New, after insertion N. Request-returned flags do not
mean that deferred detachment or New completed. `ProbeCanvasSnapshot` and
`ProbeCanvasStamp` inspect the shell's pending source binding, not an independent
controller document read; these observations cannot by themselves prove disk or
controller canonical state.

| Deadline evidence | What it distinguishes (not a proved root cause) |
| --- | --- |
| Stage 3, provider attached | Deferred detach has not become visible to this probe. |
| Stage 3, provider absent, status false | Detach is visible but required status is not. |
| Stage 5, snapshot absent/nonempty | The expected empty New binding is not visible. |
| Stage 5, empty snapshot, status false | Empty binding is visible but the required status is not. |
| Stamp changed versus before-New | A different identity is visible; does not certify all New work completed. |
| Snapshot version differs from stamp | Binding identity/source-version consistency needs investigation. |
| Input not editable/focused | Native input-host state is separately implicated. |

No source text, file path, status string, document hash, native pointer, clipboard
value, or home directory is added to output. Existing source-hash equality remains
a boolean only. Sampling reuses existing read-only probe getters; no new native
selector, detach request, menu action, edit, or fault is introduced. Stage guards,
90-second deadline, 45-ms scheduling, fixture, insertion N/M, success marker and
assertions are unchanged.

## Failed-invocation evidence preservation

The wrapper deletes only the exact previous metrics file before launching the
child. It independently measures input SHA equality and fresh metrics presence/
line count in `finally`, including when the child exits unsuccessfully. Original
errors and success requirements are preserved. `null` means unmeasured;
`metrics_present=false` means a prepared invocation produced no metrics file.
`evidence_error` contains only an exception type if evidence collection fails.
An invocation failing before metrics preparation leaves metrics fields unmeasured;
do not attribute a pre-existing uploaded file to such an invocation. Native metrics
are normally written after the AppKit loop returns; a killed/crashed child need
not write them. This patch cannot manufacture missing target evidence.

## Verification and safety boundary

- `dotnet build src/Mote.Native/Mote.Native.csproj -c Release --no-restore -warnaserror`:
  passed locally on Windows, zero warnings/errors. This is managed compilation,
  not a published Mach-O, Native AOT execution, or AppKit selector check.
- PowerShell AST parse of `tests/NativeMacCanvasAxWorkflow.ps1`: passed.
- Scoped `git diff --check`: passed.
- No probe/harness execution, external AX client, VoiceOver, input source change,
  clipboard operation, or hosted dispatch was performed for this change.

**Independent safety review is still required before invocation.** Review the
exact source/harness delta and approved published executable/invocation on a fresh
hosted Mac runner. The harness already launches the opt-in in-process AppKit AX
probe against a repository `.temp` fixture with isolated `MOTE_HOME`; it is not an
external AXUIElement client. Existing CI is unchanged; inspect actual process
markers, native timeout state and wrapper report, not the non-gating step color.
Both target RIDs remain unverified for this delta, and the original recovery
failure is unresolved, not fixed.
