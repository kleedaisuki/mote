# Native theme override / reload independent review

Date: 2026-09-30. Baseline HEAD at review: `3c778d8` plus the in-progress Native override/reload working tree. Review author owns only this artifact and the explicitly authorized new `tests/Mote.Tests/NativeThemeOverrideReviewReproTests.cs`; no production edits or commits.

## Result

**No unresolved material defect found in the reviewed snapshot after the corrections below.** This is a scoped controller/coordinator/platform-safety review, not full product acceptance. macOS execution and four-RID AOT validation remain pending external CI.

## Findings corrected during review

1. **P2: rejected reload diagnostics were lost on unrelated OS appearance events.** `SettingsLoaded` originally put a rejected read's notice in `_settingsNotice`; `ResolveRequestedTheme` then regenerated that field from the previous accepted configuration, clearing the warning without a successful reload. Contrast-rejected maps had the same failure. The architecture requires persistent rejected-map diagnostics. Production owner separated `_reloadFailureNotice` from current composition issues; a deliberately accepted reload clears the failure. Independent `Review_rejected_reload_notice_survives_appearance_change` verifies rejected read -> light transition -> dark transition without notice loss.
2. **P2: a partially installed preview preference was rolled back in configuration only.** `ApplyPendingSettings` originally restored `PreviewLayout` after `PresentAnalysis` failed, without reinstalling the old presentation in the shell. Both platform adapters can mutate native layout/retained analysis before later work fails (Windows `SetAnalysis`: `_analysis`, `_showPreview`, resize, then preview import). Controller map restoration alone could leave native visibility and presentation identity ahead of the controller. Owner now reinstalls `PresentAnalysis` with the restored preference, while reporting rollback failure honestly. Theme rollback also reinstalls analysis to restore its presentation identity. Independent `Review_failed_preview_reload_reinstalls_previous_native_presentation` injects one exception *after* the fake adapter has retained the new pane state; the previous source-only view is reinstalled.
3. **P2: preview failure diagnostics had the same appearance-event erasure path.** Owner separated `_previewFailureNotice`; the rollback regression additionally asserts its persistence through `ChangeAppearance(false)`.

The fixes landed before the first independent test invocation. No pre-fix negative test execution is claimed: original source traces above are the defect evidence, and the tests are post-fix discriminating regressions.

## Independent verification

Commands (Release, no restore):

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~Review_rejected_reload_notice|FullyQualifiedName~Review_failed_preview_reload' --logger 'console;verbosity=normal'
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~Review_failed_preview_reload' --logger 'console;verbosity=normal'
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~Review_new_request_invalidates' --logger 'console;verbosity=normal'
```

Results: initial two regressions **2/2 passed**; strengthened preview diagnostic regression **1/1 passed**; newly added stale deferred snapshot regression **1/1 passed**. These are three unique tests, not four distinct cases. Existing unrelated suites were not rerun.

The third test deliberately completes snapshot A during native preedit, requests B whose worker read is held by an event, settles preedit while B remains unavailable, and checks A is *not* applied. Releasing B installs exactly its requested color. This discriminates request identity from mere read-completion order and does not rely on cancellation timers.

## Coordinator and controller assessment

- One worker read at a time; request serial invalidates stale completions and disposal publication; 100 requests can coalesce to one final rerun. UI-lane publication owns configuration/native state, not Engine locks or source snapshots.
- New requests clear a previous composition-deferred configuration immediately. Native appearance and settings installation defer while composition owns the control.
- Effective-value comparison covers palette, semantic families, typography, spacing and dark-mode state, not theme ID. Canonical `_theme` is committed only after shell installation and preview reinstallation succeed; rollback is best effort and explicitly warned.
- File-level rejection retains the previous entire configuration. Invalid maps retain prior requested appearance, while preview preferences remain independent. Changed cache/data/trace destinations and trace enablement are reported as next-launch; startup writer destinations remain effective and no data migration is invented.
- Theme/reload does not call Engine edit/undo, document save, directory creation, or source-file reads. Existing controller tests cover Engine history; platform-specific evidence must remain distinct from this managed review.

## Windows TOM / native-history mitigation

Reviewed `WindowsRichEditUndoScope.cs`, `WindowsEditorShell.SetTheme`, `SetAllEditorColor`, and semantic RTF reimport. No blocking lifetime/ABI defect found:

- Windows SDK `10.0.26100.0/um/tom.h` independently confirms `tomSuspend = -9999995`, `tomResume = -9999994`; enumerating the `ITextDocumentVtbl` members confirms `Undo` is zero-based slot **22** with 32-bit Windows `long` arguments and nullable output count.
- OLE reference -> QueryInterface -> document ownership is balanced on success/failure. Nested leases suspend once and final idempotent release resumes and releases the owned document interface in `finally`. Acquisition fails before palette/resource mutation. No runtime COM wrapper, dynamic reflection binding or shipped native dependency is introduced.
- `ST_KEEPUNDO` is appropriate for same-text semantic reimport; `ST_DEFAULT` would clear history regardless of TOM recording suspension. No `EM_EMPTYUNDOBUFFER`, history-limit reset or `tomFalse` is used.
- The worker's actual-HWND native undo/redo and nested exception tests are recorded in `native-theme-overrides-platform.md`; they were inspected, not redundantly rerun by this review. External callers' independent TOM suspension is outside the helper's owned-scope contract.

Primary references: [SETTEXTEX flags](https://learn.microsoft.com/en-us/windows/win32/api/richedit/ns-richedit-settextex), [ITextDocument::Undo signature](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextdocument-undo), and the installed SDK header (the web Undo page does not enumerate the suspend/resume constants).

## macOS diagnostic safety verdict

`MacThemeOverrideProbe.cs` plus exact no-argument `Program --check-native-mac-theme-overrides` dispatch is **safe for opt-in/non-gating CI execution** within the inspected scope:

- The dispatch occurs before the ordinary application configuration/telemetry startup path.
- Configuration is constructed in memory; synthetic `.temp/mac-theme-probe-unused-home` paths are normalized but neither read nor created. An injected loader returns the second in-memory snapshot.
- Operations are process-local AppKit view/menu actions, source insertion, sRGB NSColor component reads, source/stamp/selection checks and routed Engine Undo/Redo. No clipboard, input source, TCC permission or persistent system-setting mutation occurs.
- The event-loop workflow has a 20-second bound and closes only its own untitled process-local document using the existing one-shot discard approval.

No macOS runtime result is claimed here. The probe does not establish physical keyboard/IME behavior, real high-contrast transitions, accessibility reader operation, signed/notarized distribution, or input-to-visible latency.
