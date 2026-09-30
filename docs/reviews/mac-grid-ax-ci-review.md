# macOS Grid AX selector CI review

Date: 2026-10-01. Scope: the uncommitted `Diagnose native macOS CSV Grid AX selectors (non-gating)` addition in `.github/workflows/ci.yml`, its two marker producers, and the diagnostic entry point. No production or workflow edits were made by this reviewer.

## Verdict

No substantive defect found in this scoped change. Suitable for a non-gating hosted observation on both macOS Native AOT architectures; this is not external accessibility or release acceptance.

## Evidence

- PyYAML parsed the workflow and found exactly one new step in `native-aot`; `continue-on-error` is boolean true and the timeout is three minutes. The macOS RID condition excludes Windows.
- Extracted the actual `run` block and parsed it with the installed PowerShell AST parser: zero syntax errors. The published executable path follows the existing matrix publish convention.
- `MacCsvGridAccessibilityProbe.cs` SHA-256 is `0E2CD0B186477128ABBCA4741AA85B0835DE72E8B08AE470301C9A401CCA198A` in both the working tree and Git index. Git reports index/worktree LF, so this pin is not a Windows CRLF-only hash.
- The opt-in environment variable is set only inside this step's PowerShell process and removed in `finally`. The diagnostic branch in `Program.Main` precedes ordinary configuration loading. `MacCsvGridProbe.Run` owns its shell; the AX probe owns a bounded synthetic table and invokes synthetic events directly on that table, not global input injection.
- The AX marker is printed only after selector, NSRange, selection, composition, focus, and retirement assertions complete. The general readiness marker is printed only after the enclosing probe returns success. Both exact markers must occur once using ordinal comparison, and the captured native exit code must be zero. A skipped AX probe, duplicate marker, or later enclosing-probe failure cannot pass.
- Inspected the probe paths: no user document I/O, input-source mutation, pasteboard write, network request, or TCC grant. The process-owned visible window can affect the disposable runner's foreground state; this does not establish physical focus or geometry acceptance.

## Limits

This review did not execute AppKit on Windows, independently validate the full accessibility implementation, or observe external AX clients, VoiceOver, physical IME, geometry, performance, or ARM behavior. Hosted results must retain those distinctions. A failed preceding publish can cause this `always()` diagnostic to fail without new functional evidence; that failure remains non-gating.

Reference: [GitHub Actions workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax), particularly step shell, condition, timeout, and continue-on-error semantics.
