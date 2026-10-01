# Scoped release candidate visual review

Date: 2026-10-02 (Asia/Singapore). Owner: root release coordinator.
This is direct image inspection, not automated semantic, physical-pixel,
latency, input-method or accessibility certification.

## Actual hosted macOS captures

Source `9fecef2`, run [36921665382](https://github.com/kleedaisuki/mote/actions/runs/36921665382).
The independent artifact audit in `release-acceptance.md` checks all twelve PNG
identities. Root directly inspected these three, not all twelve:

| Capture under `.cache/release-ci-36921665382/evidence/` | Observation |
| --- | --- |
| `release-evidence-osx-arm64/product/markdown/native-product.png` | 1024x642; light source/preview split; heading blue, dark body, neutral background; Chinese heading legible; rendered bold and headings; status Complete v3. Checkbox notation is textual rather than an interactive task widget. |
| `release-evidence-osx-x64/product/json/native-product.png` | 1120x760; differentiated keys/scalars/numbers and legible Chinese; right pane is a structural outline, not a formatted JSON duplicate. Repeated profile/name labels are verbose; this is a visual-polish limitation, not evidence of changed saved bytes. |
| `release-evidence-osx-arm64/product/text/native-product.png` | 1024x645; full-width source with no empty preview; Chinese/accent/emoji appear; neutral light palette; status Complete v3. |

These are actual application content-view captures, not mockups. The probe
captures before its final marked Chinese-character commit, so the images show
the edited prefix without the saved final `中`. Saved bytes and version-4 Save
chains are independently verified; Complete v3 in the capture must not be
relabeled as final version-4 semantic publication.

## Corrected local Windows observer run

Root also inspected
`.temp/windows-release-corrected-suite-2/markdown/native-product.png` from the
passing six-format **managed** local task run. This is an actual owned window,
not the hosted AOT binary and not release qualification. The screenshot records
the configured theme, source/preview layout and menu/status chrome at its own
capture stage. It cannot establish other DPI scales, physical keyboard/IME,
contrast across every state, or the eventual promoted default route.

## Useful conclusions and limits

- The simple source/preview/full-width conventions are visibly implemented.
- The inspected light captures use restrained roles rather than arbitrary
  multi-color chrome. They do not certify all built-in/overridden themes.
- Structured outlines still need reader-oriented polish; native text Markdown
  rendering is deliberately not browser parity.
- No screenshot proves startup distributions, scroll/typing tails, exact saved
  text, or successful public installation. Those require separate evidence.

## Post-correction actual Windows AOT CSV capture

Source `832dae6`, run [36928548957](https://github.com/kleedaisuki/mote/actions/runs/36928548957).
Root inspected
`.cache/release-ci-36928548957/evidence/release-evidence-win-x64/product/csv/native-product.png`.
The actual default AOT window shows the committed Chinese edit, Complete v5
source status and three populated table records without the former pending rows.
Only part of the three-column table fits the split-pane width: horizontal
scrolling remains necessary. This screenshot alone does not prove offscreen
labels; the separate owned-process callback oracle verifies all nine labels.
The native table/footer chrome remains utilitarian, not certified pixel polish.
Both Windows package jobs pass, but the aggregate run fails on Mac CSV observer
readiness. This capture is not an overall release/default certificate.
