# Mac Canvas AX stage-5 failure: persistent warning overwritten by analysis

Date: 2026-10-01. Investigation only: no production, probe, harness, CI or test
edits; no build, rerun, dispatch, staging, commit or push. This note examines the
new hosted evidence and the **frozen run revision**, not concurrent working-tree
edits owned by the Grid / analysis teams.

## Conclusion

This is a **product status-lifecycle defect**, not a stale requirement that should
be removed from the AX probe. The provider is detached and the second New binding
is empty, consistent and editable on both target architectures; the persistent
unavailable warning has disappeared. The controller explicitly promises to keep
that failure visible across New/Open, and existing managed tests assert that
contract. Native analysis status replaces the document status that carries the
warning. The pending-analysis replacement happens synchronously inside New;
subsequent ready analysis repeats the overwrite.

The target evidence directly identifies the missing predicate. The precise
overwriting call is established by source analysis, not a recorded per-call native
trace. This distinction does not justify weakening the warning assertion.

## Target evidence and scope

[CI 36756839425](https://github.com/kleedaisuki/mote/actions/runs/36756839425),
created 2026-09-30 18:11:53 UTC, used revision
`7420d0bf08612fe8f10207e123c84d1f446e7841` (verified with `gh run view --json
headSha,url,createdAt`). Exact retained files for each `RID` (`osx-x64`,
`osx-arm64`):

```text
.cache/ci-36756839425-ax/<RID>/.cache/ci-inventory/<RID>/mac-canvas-ax-metrics.txt
.cache/ci-36756839425-ax/<RID>/.cache/ci-inventory/<RID>/mac-canvas-ax.json
.cache/ci-36756839425-ax/<RID>/.cache/ci-inventory/<RID>/mac-canvas-ax-stderr.txt
.cache/ci-36756839425-ax/<RID>/.cache/ci-inventory/<RID>/mac-canvas-ax-stdout.txt
```

| Observation | x64 | ARM64 |
| --- | --- | --- |
| Stage 3 -> 4 | 721 ms | 2,066 ms |
| Second New returned; stage 5 | 1,144 ms | 2,259 ms |
| Stage-5 deadline | 90,015 ms | 90,069 ms |
| Provider attached, at New return and deadline | False | False |
| Snapshot present / empty, at both samples | True / True | True / True |
| Binding generation / version, at both samples | 4 / 0 | 4 / 0 |
| Snapshot version matches binding | True | True |
| Same generation as immediately before New | False | False |
| Native input editable / focused | True / True | True / True |
| Unavailable token / exact suffix present | False / False | False / False |
| Wrapper input SHA unchanged / metrics present | True / True | True / True |
| Native metrics lines / evidence error | 11 / empty | 11 / empty |

Both wrapper reports remain `unverified`, with child exit 1 and stderr `Mac
canvas AX stage 5 failed.` The final M insertion is **not executed**, so these
runs do not establish edit-after-second-New or successful held-element teardown
acceptance. Stage 4 establishes that insertion N became visible after the fault.
The diagnostic snapshots inspect the shell's pending source binding, not an
independent controller/disk read. The independent file SHA comparison establishes
that the input fixture was not changed.

The same missing token is recorded immediately when the second New request
returns and at the deadline, with no intervening changed-state poll retained.
This is not evidence that every intermediate native state was sampled, but it
rules out explaining this failure merely as a still-attached provider or absent
empty binding at the final deadline.

## Intended contract is explicit, and remains applicable

The probe creates an experimental-canvas shell and a controller with the default
null product profile; this run is **not** the distinct Continuous-profile warning
wording route. `CanvasAccessibilityFailed` sets `_accessibilityUnavailable=true`
and posts `ShowDocument`; its documentation explicitly says the warning remains
visible across subsequent Open/New. The flag is not reset by document replacement.
`ShowDocument` includes `Accessibility provider unavailable` for this profile.
The Continuous profile instead emits `AX unavailable: save, restart --legacy-page`;
preserving that different contract is necessary in any fix. See frozen
[controller fault handling](https://github.com/kleedaisuki/mote/blob/7420d0bf08612fe8f10207e123c84d1f446e7841/src/Mote.Native/NativeEditorController.cs#L1026-L1035)
and [document status composition](https://github.com/kleedaisuki/mote/blob/7420d0bf08612fe8f10207e123c84d1f446e7841/src/Mote.Native/NativeEditorController.cs#L1343-L1364).

`Canvas_accessibility_runtime_failure_preserves_edit_undo_and_warning_across_new_open`
asserts the warning after fault, New and Open. Its intent is therefore independent
of the native probe's polling behavior. The contract concerns a session-level
capability failure: New does not reattach the failed provider, so forgetting the
warning would misrepresent the editor's accessibility health. See the frozen
[managed regression test](https://github.com/kleedaisuki/mote/blob/7420d0bf08612fe8f10207e123c84d1f446e7841/tests/Mote.Tests/NativeControllerTests.cs#L1319-L1355).

## Exact overwrite mechanism

```text
AX-only fault
  Mac AccessibilityFaulted: dispose provider, set provider=null, raise failure
  Controller: latch accessibilityUnavailable=true; post ShowDocument
  ShowDocument -> ShowCanvasDocument -> SetCanvasChrome(status includes warning)

second New
  ReplaceDocument -> CancelAnalysis: presentedPreview=null
  replace document; increment generation; preserve accessibilityUnavailable
  ShowDocument -> SetCanvasChrome(status includes warning)
  ScheduleAnalysis: no retained presented preview, so publish pending analysis
  PresentAnalysis -> shell.SetAnalysis(view with no accessibility warning)
  Mac ApplyAnalysis -> SetStatus(view.Status + diagnostics)
  SetStatus: replace statusText; RenderStatus uses that replacement
  New returns: empty binding is visible, but warning token is already absent

later matching analysis
  PresentAnalysis -> ApplyAnalysis -> replace statusText again without warning
```

Frozen source anchors:

- [ReplaceDocument](https://github.com/kleedaisuki/mote/blob/7420d0bf08612fe8f10207e123c84d1f446e7841/src/Mote.Native/NativeEditorController.cs#L551-L588)
  calls `ShowDocument` immediately before `ScheduleAnalysis`.
- [CancelAnalysis](https://github.com/kleedaisuki/mote/blob/7420d0bf08612fe8f10207e123c84d1f446e7841/src/Mote.Native/NativeEditorController.cs#L1933-L1942)
  clears `_presentedPreview`; the [pending-analysis publication](https://github.com/kleedaisuki/mote/blob/7420d0bf08612fe8f10207e123c84d1f446e7841/src/Mote.Native/NativeEditorController.cs#L1478-L1502)
  therefore runs after replacement without carrying the health suffix.
- [PresentAnalysis](https://github.com/kleedaisuki/mote/blob/7420d0bf08612fe8f10207e123c84d1f446e7841/src/Mote.Native/NativeEditorController.cs#L1980-L1986)
  adjusts presentation metadata but does not compose accessibility health.
- [Mac ApplyAnalysis](https://github.com/kleedaisuki/mote/blob/7420d0bf08612fe8f10207e123c84d1f446e7841/src/Mote.Native/Mac/MacEditorShell.cs#L428-L437)
  overwrites ordinary status after preview installation. [SetStatus / RenderStatus](https://github.com/kleedaisuki/mote/blob/7420d0bf08612fe8f10207e123c84d1f446e7841/src/Mote.Native/Mac/MacEditorShell.cs#L1564-L1575)
  render the replaced value plus the separate optional persistent notice.
- `UpdateThemeNotice` composes theme/reload/preview/settings notices but not AX
  health, so the existing persistent notice channel does not rescue this warning.

### Why managed tests missed it

The test fake's `SetCanvasChrome` sets `CanvasStatus`, but `SetAnalysis` merely
stores analysis and invokes a callback; it does **not** replace `CanvasStatus`
as the native Mac shell does. Its warning assertions observe the last chrome
publication, not the effective post-analysis status. See frozen
[fake chrome and analysis methods](https://github.com/kleedaisuki/mote/blob/7420d0bf08612fe8f10207e123c84d1f446e7841/tests/Mote.Tests/NativeControllerTests.cs#L1566-L1596).
This is a concrete fidelity gap, not evidence that the managed test is useless.

## Recommended next change (coordinate ownership first)

No new public API is intrinsically required. The **smallest coherent design that
avoids warning-string duplication** is to use the already-established persistent
`SetStatusNotice` channel, which its interface explicitly guarantees survives
ordinary status refresh. Extend/rename the single controller notice composer
currently called `UpdateThemeNotice`; build its complete notice value from
latched AX/theme/reload/preview/settings state on every update. Include the AX
message exactly once in that value, rather than appending to old rendered text.
Remove the old AX suffix/prefix from ordinary document-status composition when
migrating, so source chrome and persistent notice do not display it twice.

Publish the composed notice after both attachment-failure and runtime-failure
paths latch the flag. Preserve the existing composition guard and composition-end
refresh; do not force an IME commit to show a health notice. Preserve the distinct
Continuous-profile restart guidance and coexistence with settings/theme details.
New/Open leave the session flag and notice alive. Analysis and theme replay can
continue refreshing their ordinary status without knowing AX policy. The existing
single composer must remain the only controller writer of the shared notice;
calling `SetStatusNotice(AXWarning)` separately would erase other notices.

This migration necessarily changes where the effective status is observed:
`ProbeCanvasStatus` currently reads raw `_statusText`, excluding `_statusNotice`.
Factor the existing platform status rendering into a read-only effective-status
value and let the diagnostic inspect that same value (without logging its text).
Update the fake to model combined ordinary status plus persistent notice. Preserve
the stage-5 **visible warning** assertion; do not silently drop it because the raw
status no longer contains the token. No acceptance assertion requires this
particular warning to be the final suffix; `status_exact_suffix` is diagnostic
evidence only, and can be false when multiple notices legitimately coexist.

If preserving raw-status placement is an integration requirement, the alternative
is one controller-owned health composer used by source chrome and every analysis
publication. It must operate on original base status, not previously enriched
retained views, or explicitly guarantee idempotence on theme replay. That requires
more care to avoid policy duplication/string stripping than the existing notice
channel. Do not fix only New, only the plain-text branch, only the pending
publication, or only Mac: health must survive ready/deferred/error analysis,
theme replay and Open.

Required verification after the selected fix:

1. Add a managed regression whose fake models native analysis-status replacement;
   assert the effective warning immediately after New and after a matching ready
   analysis, plus Open, analysis failure/defer and retained theme replay where
   relevant. Verify both experimental and Continuous profile wording, and no
   document text/path leakage or duplicated warnings.
2. Preserve existing detach, source/edit/Undo and version guards. Keep the native
   probe's complete stage-6 and stale-held-element acceptance requirements.
3. After independent safety review of any affected diagnostic/executable delta,
   rerun only the focused published-Mach-O in-process Canvas AX probe on fresh
   hosted x64/ARM64 runners; require exact success marker, all stages and measured
   unchanged input SHA. Do not use non-gating job color as acceptance.

This analysis does not resolve the earlier ARM64 stage-3 variation; ordinary
analysis overwrite can explain a transiently missing warning there, but those
older runs lack the necessary deadline state to prove it. It establishes neither
external AXUIElement behavior nor VoiceOver, real IME or physical-paint readiness.
Production controller / shell areas are currently owned by other agents; this
note is the handoff, not permission to modify them concurrently.
