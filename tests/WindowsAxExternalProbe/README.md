# Published Windows canvas: external UIA diagnostic

This is a **separate WPF UIAutomationClient process** querying the real HWND of a
published, one-file Native AOT `mote.exe --canvas-experimental`. It is not a
managed provider unit test, an in-process COM vtable call, or a screen-reader
speech/IME acceptance test. The executable under test is never rebuilt here.

## CI/local command

```powershell
pwsh -NoProfile -File tests/WindowsAxExternalProbe/Run.ps1 `
  -ExecutablePath src/Mote.Native/bin/Release/net10.0/win-x64/publish/mote.exe `
  -Rid win-x64 `
  -ReportPath .cache/windows-ax-external/win-x64/report.json
```

Use `-Rid win-arm64` and the ARM64 published executable on the ARM64 hosted
runner. The wrapper builds **only this WPF client**, putting its bin/obj under
`.cache/windows-ax-external/<rid>/`, creates a fixture under
`.temp/windows-ax-external/<rid>/`, and always writes a JSON report under
`.cache`, including a minimal infrastructure-error report if compilation fails.
Allow approximately one minute and run it in a Windows interactive runner
session. CI should use `continue-on-error: true` and `if: always()` artifact
upload for the report; nonzero is intentional while the duplicate-Document
release blocker remains. An exit code of zero requires all checks and no
release blockers; do **not** redefine an expected failure as a passing test.

For the **separately opt-in** UIA fragment-tree experiment, run the *same
published binary* in a fresh process with `-FragmentExperiment` and a distinct
`-ReportPath`. The wrapper adds `--uia-fragment-experimental` after
`--canvas-experimental`. In that mode, it requires a descendant whose stable
AutomationId is `mote.source.document`, whose type is Document, and whose
TextPattern passes the same full-source/offscreen checks. It never selects the
first generic Document (which may be RichEdit). The report enumerates all
Document nodes under the canvas in Raw/Control/Content views, plus canvas,
source and host `HasKeyboardFocus` states. Each tree identity includes its
RuntimeId, HWND, type, name, AutomationId, Control/Content filters, focus and
TextPattern availability. In baseline mode, `FromHandle(input)` exposes the
RichEdit's own 16-unit UIA range; in fragment mode it must resolve to the
source Document and return an exact 128-unit source prefix even though the
physical RichEdit remains 16 units long. A fragment mode result is not a
Narrator/NVDA or IME acceptance result.

Focus is sampled with `GetForegroundWindow`/owner PID **before and after**
the UIA `FocusedElement` and `HasKeyboardFocus` reads. Only when the same mote
foreground HWND is stable across that interval does the fragment report
`focus-consistent-mote-foreground` or a focus release blocker. A foreign or
changing foreground reports `focus-inconclusive-external-foreground` in the
JSON `Focus.Status` and `Inconclusive`, causing a nonzero diagnostic exit but
**not** attributing a product defect. This distinction is necessary on hosted
ARM64, where the runner's privacy-settings window can take global focus while
the editor's local provider still reports focus for its last input HWND.

The fixture is 9,000 LF rows plus `TAIL_AX_MARKER_世界😀` (>65,536 UTF-16
units). The client checks exact 128-unit document prefix, source versus bounded
RichEdit length, selected/visible ranges before and after oversized
`DocumentRange.GetText(-1)`, fresh-versus-**same cached TextPattern** behavior,
offscreen tail after the real canvas scrollbar reaches bottom, same-HWND New
generation invalidation, close invalidation, and Raw/Control/Content/focused
tree identities. The expected policy-budget UIA client HRESULT is
`0x80131509` (`InvalidOperationException`); no successful truncated text is
accepted. Stale range operations must expose `0x80040201`.

## Reproducible A/B evidence (Windows NT 10.0.26200.0, 2026-09-29)

| Published product binary | Oversize client result | Same cached TextPattern | Fresh TextPattern | Other checks | Release blocker |
| --- | --- | --- | --- | --- | --- |
| `.cache/ax_shell_aot/mote.exe`, SHA-256 `D514ABFA75B962D8E3A734A89E4061F3273C8E885A2BB96423CF65334581F60C` | `COMException 0x8000FFFF` from the prior `E_OUTOFMEMORY` provider mapping | `GetSelection` and `GetVisibleRanges` both `ElementNotAvailableException 0x80040201` | both usable | exact prefix, bounded host, offscreen tail, New/close invalidation passed | canvas and RichEdit each Document; input has focus |
| `.cache/ax_hresult_aot/mote.exe`, SHA-256 `6550D7D06EA5A2369F1A5A8626A3D5905459F427E4275E8F8264799B3E73B039`, opt-in `MOTE_AX_OVERSIZE_HRESULT=invalid-operation` | `InvalidOperationException 0x80131509` | selection=1 and visible=1 after failure | both usable | all 18 behavioral checks passed | same duplicate Document tree |

Machine-readable reports are `.cache/windows-ax-external/win-x64/report.json`
and `alternate-hresult.json` in this workspace. The second binary is a
historical A/B build with an opt-in diagnostic environment switch; current
source maps budget refusal to invalid-operation by default and no longer has
the switch. The A/B shows a WindowsDesktop UIA **client/proxy interaction**:
mapping alone changed proxy survival. It does not prove that all UIA clients
or actual Narrator/NVDA recover, nor does it establish that a 64-Ki budget is
acceptable for every reader. Duplicate nodes are a separate **product tree**
release blocker, not a probe setup failure.

## Hosted strict-AOT evidence (run 36572346343)

The same committed diagnostic ran on published **win-x64 and win-arm64**
single-file binaries. Both reports contain 18/18 passing behavior checks:
`InvalidOperationException 0x80131509` for oversized `GetText(-1)`, usable
cached and fresh TextPattern proxies afterward, exact source prefix, 16-unit
RichEdit host, offscreen tail after scroll, and stale-range invalidation after
same-HWND New and close. Both reports also retain **one release blocker**: the
canvas and RichEdit are two Document elements in Raw/Control/Content view.
On x64, `FocusedElement` was RichEdit; on ARM64 it was an unrelated Windows
privacy-settings Button, so the ARM64 global focused-element observation
cannot establish the editor's focus behavior. Exact report artifacts are
`windows-ax-external-win-x64` and `windows-ax-external-win-arm64` in CI run
36572346343. The workflow job was green because the probe is non-gating; this
does **not** convert the duplicate-tree blocker into a pass.

After extending the diagnostic to enumerate *all* Document descendants,
the historical invalid-operation win-x64 binary still produced exactly two
Raw, two Control, and two Content Document nodes with 18/18 behavioral checks
passing; `.cache/windows-ax-external/win-x64/baseline-after-tree.json` holds
the machine-readable local reproduction.

## Same-binary fragment experiment (local win-x64)

A strict one-file Native AOT binary at `.cache/uia_fragment_aot/mote.exe`
(SHA-256 `3F8C4028E8266BB57376D608919859279FB1C1707667CE41DAA3FDE6A94FEEF9`)
was run in fresh processes in both modes. Baseline: 19/19 behavior checks,
but 2 Document nodes in each Raw/Control/Content view and 1 release blocker.
With the paired `--uia-fragment-experimental` switch: 19/19 behavior checks,
zero reported failures/blockers, exactly **one** Document per view, a Pane
canvas root (`Mote canvas`) and source Document child with AutomationId
`mote.source.document`; that child reported `HasKeyboardFocus=true`. The
physical RichEdit host still had 16 UTF-16 units, while `FromHandle(input)`
resolved to the **source Document** and returned its 128-unit prefix, rather
than exposing RichEdit's 16-unit text. Independent validator paired reports
are `.cache/windows-ax-external/win-x64/validator-baseline.json` and
`validator-fragment.json`.

This is evidence for the **tree experiment on one local x64 host**, not yet
win-arm64, nor actual Pinyin candidate behavior or Narrator/NVDA speech. Keep
the fragment flag opt-in and CI diagnostic non-gating until those independent
gates are checked. The baseline and experiment share a binary, so the tree
difference is attributable to the flag rather than a changed AOT build.

## Hosted paired fragment evidence (run 36577240864)

Both win-x64 and win-arm64 published strict-AOT binaries returned **19/19**
source-text behavior checks in baseline and fragment modes. Baseline retained
two Document nodes per Raw/Control/Content view and a release blocker; the
flagged mode returned one source Document per view, 128-unit source text from
`FromHandle(input)`, a 16-unit physical host, and no tree blocker. Each RID's
two reports have the same executable SHA-256. Reports are under the four
`windows-ax-{external,fragment}-win-{x64,arm64}` artifacts in run 36577240864.

The original run's global focus on x64 was the source Document in fragment
mode, but on ARM64 it was a Windows OOBE privacy Button while the provider's
source `HasKeyboardFocus` read true. That is **not** a demonstrated focus pass.
The observation alone was ambiguous because an unrelated foreground window
could race the query; subsequent provider source inspection identified a
concrete risk that `GetGUIThreadInfo` reports thread-retained input focus even
when mote is not foreground. An AX-only foreground gate has since been
implemented and locally tested, but **has not yet passed a hosted ARM64 rerun**.
The race-aware external sample above separately records stable mote foreground
versus an inconclusive foreign/changing foreground without inventing a product
verdict. Neither hosted run exercised Pinyin candidates or actual Narrator/NVDA
speech.
