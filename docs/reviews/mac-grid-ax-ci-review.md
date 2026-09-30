# macOS Grid AX selector CI review

Date: 2026-10-01. Scope: the uncommitted `Diagnose native macOS CSV Grid AX selectors (non-gating)` addition in `.github/workflows/ci.yml`, its two marker producers, and the diagnostic entry point. No production or workflow edits were made by this reviewer.

## Verdict

No substantive defect found in this scoped change. Suitable for a non-gating hosted observation on both macOS Native AOT architectures; this is not external accessibility or release acceptance.

## Evidence

- PyYAML parsed the workflow and found exactly one new step in `native-aot`; `continue-on-error` is boolean true and the timeout is three minutes. The macOS RID condition excludes Windows.
- Extracted the actual `run` block and parsed it with the installed PowerShell AST parser: zero syntax errors. The published executable path follows the existing matrix publish convention.
- Initial `MacCsvGridAccessibilityProbe.cs` SHA-256 was `0E2CD0B186477128ABBCA4741AA85B0835DE72E8B08AE470301C9A401CCA198A` in both the working tree and Git index. Git reported index/worktree LF, so this pin was not a Windows CRLF-only hash.
- The opt-in environment variable is set only inside this step's PowerShell process and removed in `finally`. The diagnostic branch in `Program.Main` precedes ordinary configuration loading. `MacCsvGridProbe.Run` owns its shell; the AX probe owns a bounded synthetic table and invokes synthetic events directly on that table, not global input injection.
- The AX marker is printed only after selector, NSRange, selection, composition, focus, and retirement assertions complete. The general readiness marker is printed only after the enclosing probe returns success. Both exact markers must occur once using ordinal comparison, and the captured native exit code must be zero. A skipped AX probe, duplicate marker, or later enclosing-probe failure cannot pass.
- Inspected the probe paths: no user document I/O, input-source mutation, pasteboard write, network request, or TCC grant. The process-owned visible window can affect the disposable runner's foreground state; this does not establish physical focus or geometry acceptance.

## Limits

This review did not execute AppKit on Windows, independently validate the full accessibility implementation, or observe external AX clients, VoiceOver, physical IME, geometry, performance, or ARM behavior. Hosted results must retain those distinctions. A failed preceding publish can cause this `always()` diagnostic to fail without new functional evidence; that failure remains non-gating.

Reference: [GitHub Actions workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax), particularly step shell, condition, timeout, and continue-on-error semantics.

## Targeted semantic Table proxy pin refresh

Reviewed the later uncommitted macOS selector-step pin update independently. The current source SHA-256 is `F2BB23D9388486510F18BE8C4F06F2B34DE041E703AC5EFC851A70E04448FBD1` in both the working tree and Git index; the index contains 191 LF line endings and no CRLF, and `git ls-files --eol` reports `i/lf w/lf`.

Re-parsed the complete YAML and extracted PowerShell AST successfully. Compared the parsed selector step with `HEAD`: its only change is the reviewed SHA literal above. Platform condition, three-minute timeout, non-gating status, opt-in environment cleanup, ordinal exact marker multiplicity checks, executable path, and exit-code check remain unchanged. Other workflow edits, including Windows external-probe pins, are outside this targeted review.

No substantive CI defect found in this pin refresh. The new semantic Table proxy's hosted AppKit target execution is still pending: this verifies the pin and CI contract, not the new proxy's full implementation or external AX/VoiceOver, physical input, geometry, or release readiness. The initial source-path observations above must not be read as independent revalidation of all revised production behavior.
