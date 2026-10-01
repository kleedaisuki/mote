# Ordinary native Canvas: visual product assessment and acceptance

Date: 2026-10-01. Status: **product assessment and proposed acceptance contract,
not production changes or a visual-release pass**. Scope: ordinary Windows
Continuous Canvas in the strict single-binary `Mote.Native` product. Source
inspection checkpoint: `c3296b15508bbd3a194983671146d7182e8ac98f`.

## 1. Product decision

**Make the source document the unmistakable primary editing locus. Normalize
typography and scale before decorating chrome.** Retain the restrained bundled
dark/light policy colors and the single-file window, without workspace panels,
browser rendering, framework sidecars, runtime plugins, or bundled fonts.

The current screenshots demonstrate a working, simple native shell and a scoped
theme transition. They do not demonstrate a polished editing experience. The
most important issue is not that the window is empty or the colors are bizarre:
it is that the same selected text appears in the source and an independently
styled bottom input ribbon, without a user-facing explanation of their
relationship. Product acceptance must decide whether this interaction is
understandable during ordinary editing and real IME use. A palette pass cannot
make that decision.

This extends [themes](../themes.md), [continuous editor design](../virtual-editor-design.md),
[input-host compositing rationale](../input-host-compositing.md), and
[release gaps](../release-gaps.md); it does not supersede their source, safety,
performance or accessibility contracts. The older compositing document calls
the ribbon opt-in/not implemented; that is historical design context. The
current ordinary development startup uses Continuous Canvas, as the newer
virtual-editor document and the retained host discriminator confirm.

## 2. Evidence and limits

Inspected retained **win-x64** artifacts from
[CI 36809964231](https://github.com/kleedaisuki/mote/actions/runs/36809964231),
published source `8d57965`:

```text
.cache/ci-run-36809964231-theme/x64/native-canvas-theme/win-x64/
  0b5df903fee64482958c7de26b0acbd6/
    dark-before.png / dark-before.json
    light.png       / light.json
```

Both PNGs are **1044 x 760 pixels**, captured from the target window with
`PrintWindow`, not photographed displays or compositor-present measurements.
SHA-256 hashes were independently checked:

| Capture | SHA-256 |
| --- | --- |
| dark-before | `C1CE17B5997DFB94DFAF541DCECF9946F52FC595F5C1672AF26855AAE9DC1AB8` |
| light | `39CFC9D38CFBE24153F4A03BA31EF1A05AC2436D39C9DEDBCAC3F63305E74A2D` |

This is a disposable **two-line plain-text synthetic fixture** (`alpha`,
`beta`, 11 UTF-16 units), not a representative Markdown/JSON/CSV document.
It is a real opened fixture, but not evidence of a real user's file or typical
structured content. The fixed source selection is `[1,3)`. The report identifies
one owned `mote.source.document`, visible native input child 301
`RICHEDIT50W`, and the direct Canvas host. Source-body/foreground and
caption/status samples match their policy colors. The independent
[hosted theme audit](../validation/windows-canvas-live-theme.md#first-complete-bounded-native-pass-run-36809964231)
covers both Windows RIDs; only the specified x64 images were visually assessed
here. No native workload was rerun for this assessment.

| Observation | Interpretation and confidence |
| --- | --- |
| Most of the window is empty | Expected for two short lines. **Not** evidence of wasted layout, missing syntax, a failed renderer or slow large-file handling. |
| Dark source is charcoal with restrained light text; light source is white with dark text | Familiar neutral starting point consistent with the requested VS Code/JetBrains direction. These plain-text images cannot judge semantic color balance or diagnostic readability. |
| `alpha` and its selection appear both at the document top and in a bottom strip | Actual visible duplication, architecturally explained by a bounded native input projection. **Not** proof of two source models or duplicated committed edits. It nevertheless raises a coherent-editing-locus product question. |
| Ribbon reads `Input @ 3` | Actual developer-oriented absolute-offset label, confirmed in source. It does not explain to an unfamiliar user that typing edits the selected source location, particularly when that location is offscreen. |
| Ribbon text and status text look larger than source text | Actual apparent hierarchy inversion, with a concrete unit inconsistency below. Image inspection is **not** a measured glyph-height/baseline study, nor proof of particular installed fonts. |
| Dark-mode menu and scroll rails remain light | Actual visual discontinuity, partly deliberate system-native chrome. No evidence here of unreadable menu text, broken keyboard access or a reason to use undocumented dark-menu hooks. |
| Status says `Plain text · Complete · v0 No diagnostics.` | Useful format/diagnostic truth mixed with an internal version identifier. The technical status is not a product-wide explanation of caret location, saving or partial analysis. |
| No line-number gutter appears | An observation, not a release failure by itself. Stable source location and diagnostic navigation matter more than adding an always-on decorative gutter. |

## 3. Typography has a concrete scale mismatch

At the inspected source checkpoint:

* `src/Mote.Themes/ThemePolicies.cs:26-29` supplies `EditorFontSize = 13`,
  `UiFontSize = 12`, and line-height multiplier `1.45` in one shared policy.
* `src/Mote.Native/Windows/Canvas/WindowsRichEditIsland.cs:470-474`
  passes the editor value unchanged to `WindowsDirectWriteCanvas`.
* `src/Mote.Native/Windows/Canvas/WindowsDirectWriteCanvas.cs:46-51`
  passes that size to `IDWriteFactory::CreateTextFormat`.
* `src/Mote.Native/Windows/Canvas/WindowsRichEditIsland.cs:1132-1135`
  instead passes `-Round(EditorFontSize * 96 / 72)` to `CreateFontW` for
  the input host: 13 becomes **17 logical units**, after rounding.
* `src/Mote.Native/Windows/WindowsEditorShell.cs:759-760` applies the
  same point-size conversion for native UI fonts; a policy value of 12
  becomes 16 logical units.

Microsoft specifies DirectWrite font size in device-independent pixels (DIPs),
1/96 inch, while its GDI point conversion is `PointSize * deviceDpi / 72`.
Therefore this code treats the same **13** as **13 DIP** in Canvas and as
**13 pt**, nominally **17.33 pixels at 96 DPI**, rounded to **17**, in the
RichEdit font request. This is a source-level unit inconsistency, not merely a
taste preference. Actual glyph em/baseline/fallback metrics and effective DPI
remain to be measured; this calculation does not certify on-screen glyph height
or behavior at 200% scaling.
([DirectWrite contract](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/nf-dwrite-idwritefactory-createtextformat),
[GDI contract](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-createfontw).)

**Acceptance direction:** give editor/UI font sizes an explicit common logical
unit, apply platform/DPI conversion once at native boundaries, and derive
padding/line height from actual metrics. Do not fix the effect by independently
guessing smaller ribbon numbers, or blindly enlarge Canvas to the ribbon's
current size. Start a real-document comparison with a 14-DIP source/editor
candidate and a quieter platform UI font; that is a test candidate, not an
assertion of universally ideal size. Preserve existing user-configured size
semantics through an explicit migration if their established unit differs.

## 4. Prioritized product direction

| Priority | Direction | User value / guardrail |
| --- | --- | --- |
| P0 | One understandable edit destination across source, input projection and IME | Clicking/selecting source, typing, Undo and Save must act on the same global source interval. No hidden second document, duplicate commit, two blinking carets, source-row overlay or unannounced offscreen editing. |
| P0 | Consistent typography units and DPI behavior | Source is readable and primary; projection text has the same intended editor em size, not an accidental 4/3 enlargement. No clipping or target-coordinate drift when scaling changes. |
| P1 | Explain the input host without promoting it to a second editor | While the reserved ribbon is necessary, label it as source editing/composition context with a human location such as `Editing line 1, column 4`; distinguish offscreen target explicitly. Reserve technical absolute UTF-16 offsets for diagnostics. Keep it subordinate, bounded and clearly separate from document rows. |
| P1 | Calm, useful status hierarchy | Prefer location plus format/diagnostic state and actionable Save/recovery notice. Keep `Analyzing`/`Partial analysis` honest; absence of diagnostics in a covered viewport is not full-document health. Do not let routine analysis erase a recovery notice. Put engine version in an explicit diagnostic view, not permanent product chrome. |
| P1 | Native menu/scroll behavior with coherent surroundings | Keep standard menu accelerators, focus, OS high-contrast colors and scroll semantics. Adjust app-owned padding/surfaces first. A native system-colored menu is preferable to an undocumented customization that breaks accessibility; a coherent dark menu is desirable only through supported, verified native behavior. |
| P2 | Optional source navigation cues | Offer lightweight line numbers/current-line emphasis if supported by useful tasks and configuration, without allocating one object per line. No minimap, project tree or toolbar added just to fill empty space. |

The ribbon is a safety-driven rendering compromise, not a cosmetic feature:
the recorded mixed-script dual-shaper discrepancy was up to **63.3 px**, despite
one aligned caret. Hiding the focused native host, painting two engines over
one row, or moving the host during preedit is not an acceptable shortcut.
The final product direction is caret-local, single-source editing; a narrower
single-painter native composition integration can be investigated separately.
It must pass the existing geometry/semantics/input/accessibility gates before
replacing the safe bounded host. Do not multiply short-line/long-line/offscreen
special cases solely to imitate an IDE screenshot.

## 5. Observable acceptance contract

| Area | Pass observation |
| --- | --- |
| Main journey | Open one file without workspace creation; source becomes usable independently of preview/analysis. Click a real source location, type once, Undo/Redo, Save and fresh-process reopen with exact expected bytes. Ribbon never takes ownership of a different committed selection. |
| Editing locus | A user new to mote can identify where typing will change the file, both with caret visible and after scrolling it away, without technical coaching. Record their predicted location before typing. Any wrong-target edit or inability to identify the target is a failed case, not averaged away by a satisfaction score. |
| Composition | Actual Microsoft Pinyin commit/cancel preserves one source transaction/zero canceled transactions, candidate visibility, focus and selected interval. Source marker and host caret do not present two independently editable documents. Candidate distance/eye travel is evaluated with real use; screenshots alone cannot approve it. |
| Scale | At 100% and 200% display scaling, source/ribbon intended em size agrees within normal native metric/rounding differences; measured requested units and actual em/baselines are recorded. No clipped descenders, selected glyphs, labels, status or menu hit targets. Monitor transition/resize does not move source selection or discard marked text. |
| Readability | Default text/semantic/selected text retain the documented 4.5:1 normal-text policy contract; essential focus/boundaries retain 3:1. Inspect actual selected glyphs, diagnostic underline/message and fallback CJK glyphs, not just RGB constants. Diagnostics include non-color cues and actionable text. |
| Chrome | File identity and dirty state are visible; Save failure/recovery notices remain discoverable while analysis updates. File/Edit/View keyboard access and OS contrast remain usable. Long paths/status and narrow windows do not cover source or silently hide required input. |
| Accessibility | One source-backed Document in Windows Raw/Control/Content trees, exact whole-source ranges and global selection; Narrator can read location, edit and confirm the result without duplicated source speech or a page-local substitute. Keep native candidate accessibility. A tree-only pass does not certify spoken workflow. |
| Performance | Same bounded-source/input architecture for small and large files; no full mirror, full-file scan, or semantic reanalysis on theme change. Record native source-ready and edit/present measurements using existing performance gates; this assessment neither supplies numbers nor relaxes their thresholds. |

Microsoft's production guidance explicitly treats default contrast, font/text
scaling, graphics text and UI Automation roles as related accessibility
requirements, and asks for Narrator validation rather than using high contrast
as a cure for an unreadable default. These principles apply to the custom
Canvas even when examples use framework controls.
([Accessible text requirements](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements),
[inclusive Windows design](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/designing-inclusive-software).)

VS Code's production theme API separates editor, selection, line-number,
diagnostic and surrounding chrome roles. Borrow that hierarchy, not its
workspace UI or a demand to duplicate every feature. The existing mote policy
already captures this valuable split.
([VS Code theme roles](https://code.visualstudio.com/api/references/theme-color),
[JetBrains scheme/UI distinction](https://www.jetbrains.com/help/idea/configuring-colors-and-fonts.html).)

Recent HCI evidence supports keeping visual and assistive checks separate:
Huh and Pavel's **DesignChecker** (UIST 2024, DOI
`10.1145/3654777.3676369`) reports that blind/low-vision web developers could
produce accessible websites while still struggling to identify visual
readability/alignment issues; its comparison workflow helped identify more
visual problems. This is not an experiment on native editors or proof that
the ribbon is confusing. Its applicable lesson is methodological: inspect
representative rendered tasks alongside accessibility behavior, not replace
one with the other or trust automated color validation as a complete review.
([Author manuscript and study scope](https://arxiv.org/abs/2407.17681).)

## 6. Small discriminating real-document matrix

Use **three representative structured files x two themes x two scales = 12
initial visual states**, on one controlled Windows host, not a combinatorial
feature checklist. These can be inspected manually locally while CI retains
reproducible native state/artifact checks. All fixtures and captures stay under
`.cache/native-canvas-visual/` or `.temp/native-canvas-visual/`; use generated
nonprivate content, not a user's files or configuration.

| File/task | Content to include | Discriminating observation |
| --- | --- | --- |
| Markdown, about 100 lines | Headings, prose, list, fenced block, link; CJK, tabs, emoji and combining text | Read source, select mixed-script phrase, type, view preview and navigate back to source. Semantic hierarchy should aid reading without replacing text; source/candidate/preview targets must not be confused. |
| JSON, about 100 lines | Nested object/array, repeated keys, strings/numbers; one deliberately invalid value | Locate diagnostic, fix it, verify current-version diagnostics and exact Save. Diagnostic and status must remain legible in both selected/unselected states. |
| CSV, about 100 rows | Quoted comma, multiline cell, CJK header, long cell | Compare source with optional Grid, select quoted source, edit and reopen. Source ribbon is not a Grid-cell editor or second selection; labels and navigation must explain the active surface. |

For each: `mote-dark`/`mote-light`, 100%/200% OS scaling; record source build,
theme, effective DPI, installed/resolved font and requested unit, file hash,
selection, active surface and capture method. Capture (1) ordinary ready state,
(2) mixed-text selection/edit state, and (3) relevant diagnostic/composition
state. Compare the same document and viewport, not unrelated screenshots.
Do not change OS scale/theme in unattended hosted CI without an isolated
restoration contract. A static 200%-scale image is not a per-monitor transition
test or an IME test.

Then run **one focused large-file stress extension**, reusing existing
100 MiB JSON and 50 MiB unbroken-line fixtures: scroll/pan away without clicking,
predict the unchanged edit target, return, make one edit, Save/reopen. Keep
the existing exact-byte and bounded-resource oracles. Add a small narrow-window
and actual high-contrast check after the core matrix, not instead of it.

The most informative low-cost human test is a short observed session with Klee
or another intended structured-text user: ask them to edit a marked phrase,
scroll away/back, enter/cancel Pinyin and explain what the bottom strip edits.
Record errors and whether the strip is understood. This is formative evidence,
not a population-wide usability claim; no telemetry service is needed.

## 7. Safe delivery/rollback boundary

Typography/chrome work must remain independent of canonical source ownership,
format policy, Save/Undo and single-binary packaging. Preserve user configuration
and keyboard behavior; theme changes repaint matching analysis rather than
reparse or reset selection. Do not make font parity a promise of identical
DirectWrite/RichEdit glyph geometry.

If a replacement composition treatment fails source, real IME or reader gates,
retain the currently safe visible bounded ribbon rather than hide the native
host. The existing explicit `--legacy-page` route is the known rollback option;
switching modes must settle preedit and preserve unsaved canonical source,
selection/history where supported, or require a safe Save/reopen boundary.
Do not silently restart, discard dirty edits, or call the rollback path fully
release-certified without its target-host evidence. Keep failed captures and
task observations, and leave the visual gate open until the failure is fixed.

**Conclusion:** the palette and single-file restraint are good foundations.
The next product milestone is one coherent, readable native editing experience
with intentional scale and input semantics—not additional workspace features
or a more elaborate theme.
