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
release blocker, not a probe setup failure. No win-arm64 external UIA result
is claimed until that hosted diagnostic artifact is inspected.
