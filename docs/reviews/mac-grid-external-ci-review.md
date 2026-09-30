# macOS external CSV Grid AX CI integration review

Date: 2026-10-01. Scope: only the uncommitted external macOS CSV Grid AX
diagnostic and upload steps in `.github/workflows/ci.yml`, evaluated against the
frozen `tests/MacGridAxExternalProbe/Probe.swift`, `Run.ps1`, and
`docs/validation/mac-grid-external-ax.md`. Production providers and the broader
workflow are not re-reviewed here.

## Verdict

**No substantive blocking issue found in this integration.** This is approval
for a non-gating hosted diagnostic, not evidence of native execution or product
accessibility acceptance. Swift typecheck, separate-process AX behavior, and both
hosted RID outcomes remain unobserved by this review.

## Evidence and checks

- Both helper SHA-256 pins match the working tree **and committed Git blob
  bytes**, not Windows-converted text. `git ls-files --eol` reports `i/lf w/lf`
  for both executable helper sources; normal Mac checkout therefore preserves
  the reviewed pins:
  - `Probe.swift`: `0CB3E56BF68EBB22DA13316FE9AF1520D055DF0E41433F699359BCAC5E8FF36D`.
  - `Run.ps1`: `0A867687F62795790989B20A347A70EBEB93888923A666157DB45F5F5F816CEA`.
- Python PyYAML parsed the workflow. The extracted new `run` body parsed with
  the PowerShell AST parser with zero errors. Scratch extraction stays in
  `.cache/reviews/mac-grid-external-ci/step.ps1`.
- The matrix includes native Intel `macos-15-intel / osx-x64` and ARM
  `macos-latest / osx-arm64`; the new steps select both using
  `always() && startsWith(matrix.rid, 'osx-')`. Execution is bounded by a
  six-minute step timeout and the pinned wrapper/client watchdogs.
- The wrapper receives the fresh publish executable path from the existing
  Native AOT job. Its strict one-file inventory runs before compiling the
  separate Swift helper under repository `.temp`; the helper is not copied to
  publish or product payload. It isolates editor configuration, disables
  inherited tracing, and owns only synthetic fixture and exact child processes.
- The pinned wrapper requires a Swift exit code of zero, trusted/completed
  successful AX checks, matching separate editor/client PIDs, and a close
  attempt before promoting `passed`. It then demands actual editor exit code
  zero and finally unchanged fixture bytes. Cleanup failures demote status.
- The workflow independently requires exact ordinal `passed`, binary SHA
  matching the published executable, normal editor exit, and unchanged fixture.
  Eight synthetic evaluations of its actual extracted gate admitted only the
  valid report and refused permission-unavailable, failed, hash mismatch,
  abnormal close, changed fixture, wrong status case, and null evidence.
- TCC-unavailable returns a warning from the wrapper but is **rejected by the
  workflow status gate**, not reclassified as native acceptance. The success
  message explicitly excludes VoiceOver, IME, geometry, and release gates.
- Diagnostic and report upload both have `continue-on-error: true`. Upload
  uses `always()`, the RID-specific JSON path/name, `if-no-files-found: warn`,
  and fourteen-day retention. Failure before report creation cannot produce
  acceptance and does not make the existing strict product job fail.
- Existing portable fixture/path preflight was run successfully:
  `mac-grid-ax-portable-fixture-and-path-checks-passed`. This is not native AX
  evidence.

## Interpretation and limits

An overall green job may contain a failed non-gating diagnostic. Read its raw
step result and uploaded JSON rather than calling the overall CI outcome AX
acceptance. A step timeout can prevent the wrapper's final report/cleanup; the
upload is best-effort and the missing report remains no verdict, not a pass.
This review makes no claim about real VoiceOver speech, Pinyin composition,
geometry, notification delivery, performance distributions, or release readiness.

Primary workflow behavior references:
[GitHub Actions workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)
and [actions/upload-artifact documentation](https://github.com/actions/upload-artifact).
