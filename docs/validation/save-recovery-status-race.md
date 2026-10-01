# Save recovery export status ordering regression

## Observed hosted failure (2026-10-01)

GitHub Actions run [36799464145](https://github.com/kleedaisuki/mote/actions/runs/36799464145), strict job `Test / macos-latest` (job 110170316515), failed `Native_recovery_export_then_explicit_save_keeps_versions_and_identity_distinct` at its existing ten-second effective-status wait. The raw log reports `analysis=Plain text · Complete · v2; shell-errors=2`; 1155 Mote.Tests cases passed and one failed. This is not an optional diagnostic job, and its exit code was 1. Prior green runs do not disprove an ordering-dependent defect.

The raw log was retrieved with `gh api repos/kleedaisuki/mote/actions/jobs/110170316515/logs` into `.cache/save-recovery-status-race/macos-job.log`. Artifacts remain inside the repository.

## Mechanism and contract

The recovery export worker removes the owned sidecar and posts a UI completion. That callback releases `_saving`, assigns the recovery instruction to `_operationStatus`, and calls `ShowDocument`. Independently, the analysis triggered by the later buffer edit may publish its current-version result after this completion. `FakeShell.Pump` drains the entire UI queue before testing the wait predicate, so a warning can be installed and replaced in the same pump.

This is not solely a fake-shell mismatch. `MacEditorShell.ApplyAnalysis` calls `SetStatus` with the analysis status/diagnostic summary (both continuous and canvas branches); `WindowsEditorShell.SetAnalysis` calls `UpdateStatus` with that same semantic status. Before the correction, `NativeEditorController.PresentAnalysis` did not compose the controller-owned recovery warning into this presentation. Analysis does not clear `_operationStatus`, but its shell-facing status loses the instruction. The old `Document.Status` still containing the warning proves publication occurred, not that the effective user-visible status retained it.

The existing recovery contract states that export writes the earlier attempted snapshot to a distinct path, does not save later edits or change identity, and requires a later explicit Save. Keeping the instruction visible while the buffer remains unsaved matters; simply substituting `Document.Status` in the wait would hide the presentation defect. There is no evidence that the busy flag remained set or that export/Save data were lost.

## Deterministic reproduction

The new test `Native_recovery_export_notice_survives_queued_analysis_and_explicit_save_is_available` uses the actual controller/engine and the existing fake shell:

1. Inject a commit failure for v1, retaining a complete attempted snapshot, and settle the v1 analysis.
2. Edit to v2 and hold the queued current-version analysis callback.
3. Request recovery export and wait until its completion is also queued, without draining either.
4. Deliver callbacks in reverse order: export completion first, current-version analysis second.
5. Verify exact earlier bytes in export, original target untouched, identity unchanged and dirty state retained.
6. Independently issue explicit Save and verify latest buffer bytes reached the original target and dirty state cleared.
7. Require the captured effective status after analysis to retain both `Recovery exported` and `Current buffer is not saved`; require no stale export warning after the successful later Save.

The ten-second asynchronous setup cap is unchanged. The discriminating failure is an immediate status assertion, not a longer timeout or probabilistic retry.

## Red evidence

Environment: Windows 10.0.26200, win-x64, .NET SDK 10.0.400; Release managed/JIT test build. Baseline production HEAD `d7b24730b17b51d49dc0f384fff5dc2e27c99f98` with the new isolated test.

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Native_recovery_export_notice_survives --logger 'trx;LogFileName=deterministic-red.trx' --results-directory .cache/save-recovery-status-race
```

Result: 0 passed, 1 failed in 311 ms at the final captured-status assertion. Effective status was `Plain text · Complete · v2 · No diagnostics`, without `Recovery exported`. All preceding byte/identity/dirty-state checks and the subsequent explicit Save checks passed. Reproduction artifact: `.cache/save-recovery-status-race/deterministic-red.trx`. This reproduces the consequential status loss locally without depending on hosted macOS scheduling. It does not claim real native shell execution or Native AOT validation.

## Fix validation scope

The production owner used the existing persistent status-notice channel rather than putting operation text into cached semantic views. The final regression is a two-case theory: the clean successful later Save clears the recovery warning; a Save of v2 followed by an edit to v3 before the UI completion retains the warning because v3 remains dirty. It also exercises Copy while recovery remains relevant and direct shell replay of the previously cached semantic view after a clean Save, which must not resurrect cleared notices. The original hosted-failing test is unchanged.

The first expanded validation command exercised recovery controls and runtime theme guards (25 cases). Both new theory cases and the original failing case passed, but the existing `Runtime_failed_theme_warning_does_not_interrupt_unsettled_preedit` failed because initial idle document/analysis publications now emitted two no-op null notices. This was a production notification-churn regression, not evidence of failed recovery bytes; the test was not weakened. Artifact: `.cache/save-recovery-status-race/focused-green.trx` (24 passed, one failed). The owner was notified to retain the established idle/composition contract.

The first broader `NativeControllerTests|NativeTheme|NativeSettings|SaveFailureRecoveryTests` command passed 174/175 cases after the initial-empty-notice guard. It exposed a second contract regression: `Theme_override_reload_uses_values_and_preserves_source` timed out at line 31, which waits for the acknowledgement publication after a second equal-valued explicit settings reload. Suppressing every initial empty notice also suppressed this intentional acknowledgement. This was communicated to the production owner as distinct from an idle paint no-op: an explicit settings completion must still publish. Artifact `.cache/save-recovery-status-race/final-expanded-green.trx`; the filename is historical and does **not** mean its contents passed. No test assertion or timeout was loosened.

The final production refinement applies `skipInitialEmpty: true` only to the newly added idle `ShowDocument`/`PresentAnalysis` refreshes. Historical explicit status events retain their empty publications; after a notice has been published, clearing it still reaches the shell. Final narrowed contract checks passed 5/5 in 875 ms:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~Native_recovery_export|FullyQualifiedName~Theme_override_reload_uses_values_and_preserves_source|FullyQualifiedName~Runtime_failed_theme_warning_does_not_interrupt_unsettled_preedit' --logger 'trx;LogFileName=final-narrow.trx' --results-directory .cache/save-recovery-status-race
```

This covers the unchanged hosted-failing recovery test, both new ordering/lifetime cases, and both status-publication regressions found during implementation. Production source SHA-256 at final validation: `F9D7C46870A68AF9B7F00F6BBB45EF47C4C56959AF4C132B61D440CADE4B635C` (`src/Mote.Native/NativeEditorController.cs`, local bytes).

Final broader validation used that same built assembly without rebuilding:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~NativeControllerTests|FullyQualifiedName~NativeTheme|FullyQualifiedName~NativeSettings|FullyQualifiedName~SaveFailureRecoveryTests' --logger 'trx;LogFileName=final-expanded.trx' --results-directory .cache/save-recovery-status-race
```

Result: **175/175 passed**, zero skipped, 40 seconds. This includes controller editing/selection/semantic lifetimes, theme and settings publication contracts, and engine recovery controls. The final narrow and broader counts overlap and must not be summed. `git diff --check` on the owned test/document paths was clean.

## Verdict and limits

The reported hosted failure revealed a reproducible user-visible recovery-warning ordering defect, not an unreleased Save busy flag or a demonstrated filesystem export failure. The persistent-channel correction satisfies the deterministic ordering and warning lifetime tests on local Windows managed builds, preserving the existing explicit reload and preedit contracts. Cached native analysis replay cannot restore the old warning because the raw cached view contains no controller notice text. Actual native Mac/Windows shell behavior is supported by source inspection, not locally executed GUI validation here. Hosted macOS managed rerun and four-RID Native AOT acceptance remain separate target validation; no release-wide or stochastic reliability conclusion follows from this scoped test set.

## Hosted strict-target audit: run 36800944850

Audited [run 36800944850](https://github.com/kleedaisuki/mote/actions/runs/36800944850) at exact source `99fbe395308816e3d1d2381ace58c3d5800811ec`. Both strict test jobs execute the unchanged unfiltered command `dotnet test mote.sln --configuration Release --no-build`. Target logs were retrieved independently, rather than inferring diagnostic correctness from overall run color:

```powershell
gh api repos/kleedaisuki/mote/actions/jobs/110174903080/logs > .cache/save-recovery-status-race/ci-36800944850/macos-job.log
gh api repos/kleedaisuki/mote/actions/jobs/110174903027/logs > .cache/save-recovery-status-race/ci-36800944850/windows-job.log
```

| Strict target | Observed result | Coverage interpretation |
| --- | --- | --- |
| `Test / macos-latest`, job 110174903080 | Completed success; Mote.Tests **1158 passed, 0 failed, 0 skipped**, duration 1m2s; Themes 14/14 and Configuration 9/9 | Full managed suite succeeded on macOS 26.6.2/25G83, `macos-26-arm64` image 20260907.0351.1. Source contains the unchanged original export/Save test and both new deterministic theory cases. |
| `Test / windows-latest`, job 110174903027 | Completed failure; Mote.Tests test host aborted at native fatal error **0xC0000005**; process exit 1. Only a partial **365 passed** summary was emitted. Themes 14/14 and Configuration 9/9 had completed. | Not a completed Mote.Tests suite. Windows recovery/theme/settings acceptance for this run cannot be claimed from the partial count. |

The default console logger does not emit individual named PASS records. Accordingly, the macOS result establishes the complete, unfiltered suite outcome and the inclusion of these source cases, not an invented per-case raw marker or a reliability rate. No recurrence of the original `NativeSaveRecoveryTests.cs:88` timeout or managed theme/settings assertion appears in the completed macOS log. The two local compatibility regressions documented above are covered by the full successful macOS suite; this does not excuse the Windows abort.

### Windows first exact failure

At 2026-10-01T01:27:36.639Z, the raw Windows log reports:

```text
The active test run was aborted. Reason: Test host process crashed : Fatal error.
0xC0000005
at Mote.Native.Windows.Win32.SetWindowTextW(IntPtr, System.String)
at Mote.Native.Windows.WindowsEditorShell.ImportPreviewPayload(System.String, Boolean)
at Mote.Native.Windows.WindowsEditorShell.InstallPreview(Mote.Native.NativeAnalysisView)
at Mote.Native.Windows.WindowsEditorShell.SetTheme(Mote.Themes.IThemePolicy)
at Mote.Tests.NativeThemeOverrideWindowsTests.Same_id_override_updates_native_background_without_source_or_history_changes(Boolean)
```

Target environment: Windows Server 2025 Datacenter 10.0.26100, image `windows-2025-vs2026` version 20260925.250.1. This is an access violation in a native preview/theme path, not the recovery status assertion. The log does not identify which theory Boolean was active, nor prove a root cause such as a particular pointer lifetime, reentrancy, or runner failure. It does not identify whether the recovery cases ran before the abort. The first exact failure was reported immediately for independent diagnosis; no retry or assertion weakening was performed during this audit.

Local artifact SHA-256 values (retrieved text bytes): macOS log `D503C0AEB1D4E9FF660E8C3CFD4869BCB300356ABB9B3F56DEA859D9A31BD8BF`; Windows log `F3BEE6A38AD9A17795B9090F758B4C94192DD32071ECA3D8CBF5ADEBA8303E68`.

**Updated scoped verdict:** the recovery correction now has successful complete hosted macOS managed-suite evidence in addition to the deterministic local red-to-green evidence. The same run does not establish complete Windows managed-suite acceptance, and it does not establish native GUI recovery or four-RID Native AOT release acceptance. The Windows fatal preview/theme failure is a separate material blocker requiring its own causal reproduction; it must not be dismissed as environmental solely because prior runs passed.
