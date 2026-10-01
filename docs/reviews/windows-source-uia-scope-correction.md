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
This binary predates current HEAD; that local experiment alone did not claim
fresh hosted ordinary launch. The later hosted result below supplies that
specific missing evidence.

No product files changed. Parent integrated a separate ordinary product CI
step/report/artifact on both Windows RIDs, leaving diagnostic A/B intact and
correcting the old baseline comment's default-product attribution. Complete
TextPattern, Narrator/NVDA speech and composition coexistence remain independent
release gates; hosted ARM64 desktop-global focus remains unresolved below.

## Fresh ordinary-product hosted evidence: run 36797859586

Independently audited the six source UIA JSON artifacts and each Windows job's
raw log from [run 36797859586](https://github.com/kleedaisuki/mote/actions/runs/36797859586),
source commit `833ef480f00dd82a99814aae11197b9722c21e9d`. Evidence is retained
under `.cache/windows-uia-scope-36797859586/`; logs came from the individual
completed jobs (x64 `110165317237`, ARM64 `110165317456`), not the still-running
overall run's status. Successful/non-gating step conclusions were not used
as proof of diagnostic success.

The ordinary product's source/client preflight SHA checks matched the reviewed
files; no pin error preceded either actual report. Independently hashing the
Git blobs at the tested commit gives:

| Client source file | LF SHA-256 |
| --- | --- |
| `Program.cs` | `7D4E0AFDD791BC9F54D6DEF3F0E2541DF156C693BC17DE377565893615BA48D8` |
| `ProbeLaunchRoute.cs` | `6D9C7770FC316965089582D47CF82B1914C22CFDB8BA23D01962AF85B0C5916D` |
| `Run.ps1` | `AAD02A5340E8D2DB3DEDC444AC32A6C022A4439613E17B51A39E5D622961CDB7` |

Windows checkout may use the explicitly pinned CRLF equivalent; this does not
change the client contract. Product reports have `Mode=product-continuous`
and **empty** `PresentationArguments`, unlike the historical diagnostic
reports. Thus this evidence finally queries ordinary `mote <fixture>`, not
an opt-in fragment flag that might conceal a default routing error.

| RID / route | Checks passed | Documents Raw / Control / Content | Tree blockers | Global focus | Client exit |
| --- | --- | --- | --- | --- | --- |
| x64 baseline | 19/19 | 2 / 2 / 2 | 1 | consistent native host | 1 |
| x64 fragment | 19/19 | 1 / 1 / 1 | 0 | consistent source | 0 |
| x64 **ordinary product** | **19/19** | **1 / 1 / 1** | **0** | **consistent source** | **0** |
| ARM64 baseline | 19/19 | 2 / 2 / 2 | 1 | inconclusive foreign foreground | 1 |
| ARM64 fragment | 19/19 | 1 / 1 / 1 | 0 | inconclusive foreign foreground | 1 |
| ARM64 **ordinary product** | **19/19** | **1 / 1 / 1** | **0** | **inconclusive foreign foreground** | **1** |

Both product reports have no behavioral failures, physical RichEdit length
**16**, `FromHandle(input)` routed to source `mote.source.document` with exact
source prefix **128**, cached and fresh TextPattern usable after oversize
failure `0x80131509`, the offscreen tail present after scroll, same-HWND New
invalidating old ranges, normal editor exit0 and post-Close range rejection.
The independently reconstructed synthetic fixture is **153,020 UTF-16 units**,
UTF-8 SHA-256 `B872F046C6644D960F1FB62A22989A12FC00567A255FBA88F26F01693DA52845`;
both reports match it. This validates the tested fixture, not arbitrary large
documents or reader speech.

All three routes share their executable SHA-256 within each RID:

- x64: `2236907D16D3FA3723375950F30FAF20B9CC22C159C29C28E9CC126B4D7DF838`,
  OS build **26100**, client **X64**, .NET **10.0.12**.
- ARM64: `D50C3347B2074CA68DF52385476D148F0B9B9962D9935DC004B44F267ADADD51`,
  OS build **26200**, client **Arm64**, .NET **10.0.12**.

The separate publish inventories contain exactly `mote.exe` per RID
(x64 **7,105,024 bytes**, ARM64 **7,244,288 bytes**), no non-executable payload
and no bundled native library. Inventory does not itself contain a binary
hash; executable identity above comes from each external client's SHA-256.

x64 ordinary focus sampled stable target PID **1560** before/after, with source
and input-mapped source true across all four property paths and focused
identity `mote.source.document`. Its wrapper reached the following upload
without an error; the reviewed client/wrapper success path implies exit0.
ARM64 sampled stable foreign foreground PID **5116** while mote PID was
**1988**; source and input-mapped source were false on all four paths and
`Focused=<focus-identity-not-inspected>`. Its artifact has exactly one
`Inconclusive` item; the raw product-step log explicitly records exit **1**.
This is correct negative-foreground behavior, **not** a global focus acceptance
pass or a demonstrated provider defect. No foreign control metadata is in the
current focused-identity field.

**Conclusion:** fresh published x64 and ARM64 ordinary launches each have one
source Document and pass the tested source/lifetime behavior. The historical
baseline's duplicate Document is not a default-product release blocker.
Global source focus is supported on this x64 run only; ARM64's foreground
acceptance remains unverified. Do not promote this bounded result into full
TextPattern, Narrator/NVDA, real IME coexistence or general accessibility
release readiness.
