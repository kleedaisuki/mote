# Windows source UIA route scope correction

Date: 2026-10-01. Scope: source-provider tree only, not CSV Grid or a product
provider/IME rewrite. The report that motivated this investigation had 19/19
behavior checks and 2/2/2 source-subtree Documents. Filename-led retrieval of
`windows-uia-tree-design.md`, `EditorPresentationProfile.cs` and the existing
external client identified a scope mismatch rather than a missing provider.

## Ownership and actual routing

`NativeLaunchRoute.Product(Continuous)` requires Canvas and the Windows source
fragment. `NativeShellFactory` passes both decisions to `WindowsEditorShell`,
which constructs `WindowsRichEditIsland` with the fragment enabled. Its root
is a Pane; its one reachable Document provides source-backed TextPattern and
overrides only the known RichEdit input HWND. Physical keyboard/IME ownership
remains RichEdit; its subclass is not broadly intercepted. `--legacy-page`
remains unchanged. Historical `--canvas-experimental` deliberately permits
the older root-Document + native RichEdit Document tree as an A/B control.

The existing client always added `--canvas-experimental`, optionally followed
by `--uia-fragment-experimental`. Therefore neither historical report was an
ordinary-product launch. Adding conditional native-node hides or a second
provider would repair the wrong route and risk the working input boundary.

Microsoft's [HWND override contract](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-irawelementproviderhwndoverride-getoverrideproviderforhwnd)
requires a returned override to participate in the fragment tree. Its
[tree overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-treeoverview)
also distinguishes native/default-provider integration from filtered views.
These contracts support the existing coherent root/child ownership; they do
not license swallowing all `WM_GETOBJECT` requests. The production
[VS Code bounded textarea adapter](https://github.com/microsoft/vscode/blob/main/src/vs/editor/browser/controller/editContext/textArea/textAreaEditContext.ts)
reinforces separating source semantics from bounded native/browser input,
but is not evidence for HWND provider precedence. No new academic claim is
needed to correct a test launch; reader/composition research and acceptance
remain in `windows-input-accessibility-fallback.md`.

## Target evidence retrieved, not re-inferred from green jobs

Run [36794910486](https://github.com/kleedaisuki/mote/actions/runs/36794910486)
reports are retained under `.cache/windows-uia-scope-36794910486/`.

| RID / route | Behavior checks | Raw / Control / Content Documents | Focus | Tree blockers |
| --- | --- | --- | --- | --- |
| x64 baseline diagnostic | 19/19 | 2 / 2 / 2 | consistent physical host | 1 |
| x64 fragment diagnostic | 19/19 | 1 / 1 / 1 | consistent source | 0 |
| ARM64 baseline diagnostic | 19/19 | 2 / 2 / 2 | inconclusive foreign foreground | 1 |
| ARM64 fragment diagnostic | 19/19 | 1 / 1 / 1 | inconclusive foreign foreground | 0 |

Within each RID the two reports share an executable SHA-256 (see the probe
README). ARM64 is not a desktop-global focus pass; the non-gating conclusion
is not an assertion that the diagnostic exited zero.

## Implemented validation correction

`Run.ps1 -ProductContinuous` launches **only** the synthetic fixture path.
The client records `Mode=product-continuous`, `PresentationArguments=[]` and
requires the source fragment's identity, input-HWND routing and focus law.
The old baseline/fragment commands remain exact. Conflicting wrapper modes
throw before launch. The pure route/focus decisions are linked into managed
tests rather than duplicating their logic.

Desktop-global focus previously serialized an unrelated app's element name.
The updated client reads only the focused owner PID before authorizing
metadata. Stable target foreground and target-owned focus are both required;
otherwise identity is `<focus-identity-not-inspected>`. It does not steal focus
or change foreign-foreground classification. A local transient pre-guard
report was removed rather than persisted as reusable user metadata.

Focused route/factory/fragment tests passed 26/26 before the privacy change;
the final new route/privacy suite passed 11/11. The WPF client built with zero
warnings/errors and wrapper PowerShell AST parsing passed. The final guarded
ordinary launch against existing local AOT SHA-256
`BE2D17B41853A586F080F4DA3094203C2E9DD0A2AF57FF6B653DDB5B8708D0A0`
(Windows build 26200, x64) passed 19/19, returned 1/1/1 Documents, source
prefix128 and physical host16, then exited **1** solely for inconclusive
foreign foreground. Its report is
`.cache/windows-uia-scope-client/product-local.json`; editor closed normally.
An earlier same-binary invocation had consistent focus/exit0, but the final
retained report is the guarded inconclusive one, not a manufactured retry pass.
This binary predates current HEAD; no fresh hosted ordinary launch is claimed.

No product files changed. Parent owns CI integration: add a separate ordinary
product step/report/artifact on both Windows RIDs, leave diagnostic A/B intact,
and correct the old workflow comment's default-product attribution. Complete
TextPattern, Narrator/NVDA speech, composition coexistence and fresh hosted
ordinary identity/focus remain independent release gates.
