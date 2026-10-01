# Published Windows canvas: external UIA diagnostic

This is a **separate WPF UIAutomationClient process** querying the real HWND of a
published, one-file Native AOT `mote.exe`. It supports the ordinary continuous
product and the preserved `--canvas-experimental` diagnostic A/B. It is not a
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
blocker remains in the historical baseline route. That route is **not** the
ordinary product and cannot establish a current default-product blocker.
An exit code of zero requires all checks, no inconclusive focus, and no
release blockers; do **not** redefine an expected failure as a passing test.

For the **ordinary product**, add `-ProductContinuous` and use a distinct report
such as `.cache/windows-ax-external/win-x64/product-report.json`. This switch
belongs to the client wrapper, **not mote**: the editor receives exactly the
synthetic fixture path and no presentation flags. The report has
`Mode=product-continuous` and `PresentationArguments=[]`; all source-fragment
identity, input-HWND routing, exact text, stale-range and focus checks apply.
`-ProductContinuous` and `-FragmentExperiment` are mutually exclusive.

| Wrapper mode | Exact editor presentation arguments | Source provider expectation |
| --- | --- | --- |
| No mode switch (preserved baseline) | `--canvas-experimental` | Historical canvas Document plus bounded RichEdit Document |
| `-FragmentExperiment` | `--canvas-experimental --uia-fragment-experimental` | Pane with one source Document |
| `-ProductContinuous` | None; fixture path only | Ordinary product Pane with one source Document |

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
`focus-consistent-mote-foreground` or a focus release blocker. Baseline expects
the physical RichEdit host as UIA focus; fragment mode expects the mapped
source Document. A foreign or
changing foreground reports `focus-inconclusive-external-foreground` in the
JSON `Focus.Status` and `Inconclusive`, causing a nonzero diagnostic exit but
**not** attributing a product defect. This distinction is necessary on hosted
ARM64, where the runner's privacy-settings window can take global focus while
the editor's local provider still reports focus for its last input HWND.
The probe also reacquires **fresh** canvas/source/input UIA elements inside
that foreground time window and reads `HasKeyboardFocus` (UIA property ID
30008) four ways on each: `Current`, explicit `GetCurrentPropertyValue`,
`Cached`, and explicit `GetCachedPropertyValue` after `GetUpdatedCache` with a
fresh `CacheRequest`. These values are reported even when foreground belongs
to another process; in that case they are diagnostic evidence, not a focus
acceptance pass. An older local x64 AOT binary returned `true` on all four
source and input paths while an unrelated window was foreground, consistent
with UIA host fallback; that negative-control JSON is
`.cache/windows-ax-external/win-x64/old-30008-external.json`. The later
provider property-ID correction must be checked against a newly published
binary, not inferred from the direct COM unit test alone.

The current client reads only the desktop-focused element's owner PID before
authorizing identity inspection. Unless foreground is stably mote-owned and
the focused element belongs to mote, `Focused` is
`<focus-identity-not-inspected>`: no foreign name, runtime ID, HWND, type or
patterns are inspected/serialized. This does not turn foreign foreground into
a focus pass. Older reports predate this guard; do not copy their unrelated
application metadata into new documentation.

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

## Scope correction and ordinary launch evidence (2026-10-01)

Run [36794910486](https://github.com/kleedaisuki/mote/actions/runs/36794910486)
still demonstrates the retained **diagnostic A/B**, not ordinary launch. Both
RIDs have 19/19 checks in both modes. Baseline is 2/2/2 Documents with a tree
blocker; fragment is 1/1/1 with no tree blocker. Per-RID executable hashes match
across A/B: x64 `9FF531651ED116C509FF21696B7BAF0BAC6599120886093D510F5AD634E086E9`,
ARM64 `2FDE6E7E2EAFB70747E2D6E8F5874CE34F00210C612CB3D5A5EAEC1182D4F296`.
x64 fragment focus is consistent; ARM64 fragment focus is inconclusive due to
foreign foreground. Retained artifacts are under
`.cache/windows-uia-scope-36794910486/`.

Source routing separately proves ordinary `Product(Continuous)` always passes
`UsesWindowsSourceFragment=true` through `NativeShellFactory` to the Windows
shell, while `--legacy-page` and historical Canvas A/B remain unchanged.
`NativePresentationProfileTests` exercise that contract; this is not a substitute
for a target-host ordinary-launch test.

The new ordinary-launch client was exercised locally against the existing
strict-AOT `.cache/windows-grid-accessibility/aot/mote.exe`, SHA-256
`BE2D17B41853A586F080F4DA3094203C2E9DD0A2AF57FF6B653DDB5B8708D0A0`,
Windows build 26200, x64. It observed 19/19 checks, one Document in each view,
physical RichEdit length 16 and source prefix length 128. Focus may become
inconclusive when an unrelated foreground app wins the race; this is not an
editor defect or permission to steal focus. The final privacy-guarded report
is `.cache/windows-uia-scope-client/product-local.json`. This existing local
binary is **not a build of the current repository HEAD**. Fresh hosted x64 and
ARM64 ordinary-launch validation, screen-reader speech and real IME coexistence
remain separate gates; do not call the duplicate diagnostic baseline a default
product release blocker.

### Later fresh hosted ordinary launch (run 36797859586)

The separate `-ProductContinuous` reports from
[36797859586](https://github.com/kleedaisuki/mote/actions/runs/36797859586),
commit `833ef480f00dd82a99814aae11197b9722c21e9d`, now supply the ordinary
launch evidence missing above: **19/19**, **1/1/1 Documents**, source-prefix128
from the input HWND, physical host16, and no behavioral/tree blockers on both
Windows RIDs. x64 source focus was consistent and the client succeeded;
ARM64 source focus was false under foreign foreground, the focused identity
was redacted and the client explicitly exited **1** for inconclusive focus.
The baseline remains 2/2/2; the fragment diagnostic remains 1/1/1. Each RID's
three reports share the same executable hash. See the independent
[target audit](../../docs/reviews/windows-source-uia-scope-correction.md#fresh-ordinary-product-hosted-evidence-run-36797859586)
for raw-log exit classification, source/client pins, exact binary/fixture
hashes, inventory and remaining reader/IME/focus limits. This does not close
ARM64 desktop-global focus or complete accessibility release acceptance.
