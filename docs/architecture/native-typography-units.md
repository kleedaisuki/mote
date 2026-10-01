# Native Canvas typography: correct one unit boundary, preserve existing contracts

Date: 2026-10-01. Status: **proposed correction and experiment contract; no
production, test, CI, default-size or configuration change**. Source inspection
checkpoint: `3ae4d80280f00df661aa3eb764b88fce0552fb0a`. Scope: ordinary Windows
Continuous Canvas and its visible bounded RichEdit input ribbon. macOS is a
comparison/control, not a target for a Windows conversion patch.

This follows [the visual product assessment](../product/native-canvas-visual-acceptance.md),
[themes](../themes.md), [configuration](../configuration.md),
[input-host compositing](../input-host-compositing.md), the
[Windows Canvas README](../../src/Mote.Native/Windows/Canvas/README.md) and
[Mac Canvas README](../../src/Mote.Native/Mac/Canvas/README.md). Historical
README claims about experimental/default routing do not supersede the current
ordinary Continuous startup profile. This document does not certify a native
visual, real IME, screen-reader or per-monitor-DPI release gate.

## 1. Decision

**Keep the ordinary source's established 13-DIP request; make its input ribbon
request the same editor em size in the current Canvas client coordinate space.**
Do this at `WindowsRichEditIsland.SetInputAppearance`, not by changing shared
`ThemeTypography` values or shrinking a second independent ribbon policy.
Keep Windows LegacyPage, UI/status fonts, RTF/Flow preview, CSV Grid, macOS and
the Avalonia prototype unchanged in this correction.

At the current fixed-96-DPI Canvas/DC boundary, the candidate GDI negative
character-height request is `-Round(EditorFontSize)`, instead of
`-Round(EditorFontSize * 96 / 72)`. The unchanged value 13 therefore requests
13 client logical units rather than 17. **First instrument the actual native
font and client transforms to verify that RichEdit applies this request.** A
matching arithmetic constant alone is not an applied-native-font pass.

This is a deliberate visible bug correction to the ribbon, not a claim that
every historical theme field always meant DIPs. It does not enlarge Canvas to
17.33 DIP, lower the global default, introduce a font-size setting, or silently
convert existing user files. A 14-DIP source/editor is a separate readability
candidate from the product review, not part of the unit correction.

## 2. Proven source facts and uncertain native effects

| Owner/path | Current interpretation | Compatibility consequence |
| --- | --- | --- |
| `Mote.Themes/ThemeContracts.cs` | `ThemeTypography` is a public positional record with two family strings, two `double` sizes and a multiplier; size units are not documented. `ThemeSpacing` explicitly documents DIPs. | Preserve constructor/deconstruction order, property names/types and record equality. Do not add a positional unit argument or replace doubles with new public types. |
| `ThemePolicies.cs` | All bundled policies share editor 13, UI 12, line multiplier 1.45. | Do not change bundled values or theme IDs to fix one adapter. |
| `WindowsRichEditIsland.NewGeometry` and `WindowsCanvasPainter` in `WindowsOnScreenCanvasNative.cs` | Both pass editor size unchanged to DirectWrite. Painter creates its DC render target at 96x96 DPI; paint and hit testing share source-row origins. | Ordinary source size and source hit mapping already agree at this boundary. Keep them unchanged in the first comparison. |
| `WindowsRichEditIsland.SetInputAppearance` | Requests GDI character height `-Round(size * 96 / 72)` and sends `WM_SETFONT`. | A nominal 13-point conversion produces 17 logical units at 96 DPI, whereas source requests 13 DIPs. This is the narrow inconsistent boundary. |
| `WindowsEditorShell.CreateThemeFont` | Uses the same point-like conversion for editor and UI fonts. | Freeze in this correction; changing it also changes LegacyPage/status/other controls. |
| `WindowsEditorShell.SetCharacterFormat` | Uses `size * 15` for `CHARFORMAT.yHeight`; 15 twips per DIP, not 20 per point. | Legacy semantic formatting is already a different unit path; do not describe LegacyPage as uniformly point-sized. |
| `RichEditRtf` and `WindowsFlowRtf` | Use `size * 2` for RTF half-point font size. | Preview/RTF also cannot be globally reinterpreted without separate rendered baselines. |
| `MacTextInputIsland.SetTheme` | Passes the same editor value to `CTFontCreateWithName` and `NSFont fontWithName:size:`; current Canvas resolves Menlo. | CoreText and input already request matching point sizes. No Windows 96/72 conversion belongs here. |
| `ThemeOverrides.ComposedTheme` / `ThemeEffectiveValues` | Color overrides inherit base typography and spacing; equality includes their values. | Same-ID color reload must not unnecessarily rebuild native font resources. |
| `MoteConfigLoader` | No font-size/family/unit key is accepted. Theme data overrides are colors only. | There is no persisted valid font-size setting to migrate today. Unknown size-like keys must remain unknown, not acquire new semantics by accident. |

No explicit process DPI-awareness API, Native application DPI manifest or
`WM_DPICHANGED` handler was found in the inspected Native source/project.
**That is not a measurement of the published process's actual awareness:**
generated executable resources, host environment and compatibility overrides
can affect it. Record window/thread awareness and effective window DPI before
claiming 100%/200% or per-monitor behavior.

The current screenshots establish an apparent hierarchy inversion, not actual
em heights, selected fallback faces, baseline alignment or physical display
size. The conversion identifies a mechanism worth testing; it does not prove
that every native control applies `WM_SETFONT` identically or that equal em
requests make DirectWrite and RichEdit shape CJK/Arabic/emoji identically.

## 3. Platform contracts that determine the boundary

Microsoft specifies DirectWrite font size in DIPs (1/96 inch). GDI's negative
height requests character/em height in the DC's logical units, while its
point-size formula uses device DPI divided by 72. Those are different input
units, not competing renderers' aesthetic preferences.
[DirectWrite CreateTextFormat](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/nf-dwrite-idwritefactory-createtextformat),
[GDI CreateFontW](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-createfontw).

RichEdit `CHARFORMAT.yHeight` uses twips, with 20 twips per printer's point;
its native formatting can be inspected with `EM_GETCHARFORMAT`. Therefore
inspect both default and selected/input characters after binding and after one
edit; `WM_GETFONT` alone does not prove all text uses that size.
[CHARFORMATW](https://learn.microsoft.com/en-us/windows/win32/api/richedit/ns-richedit-charformatw),
[EM_GETCHARFORMAT](https://learn.microsoft.com/en-us/windows/win32/controls/em-getcharformat).

Apple specifies point size for CoreText/NSFont. AppKit handles view coordinates
and backing pixels separately; use view/window conversion APIs, not a Windows
DIP conversion or blindly multiply the font size by Retina scale. A Mac point
and Windows DIP are each useful platform logical units, **not a promise of
equal physical inch sizes between operating systems**.
[NSFont](https://developer.apple.com/documentation/appkit/nsfont?language=objc),
[CTFontCreateWithName](https://developer.apple.com/documentation/coretext/ctfontcreatewithname%28_%3A_%3A_%3A%29?language=objc),
[Apple high-resolution coordinate APIs](https://developer.apple.com/library/archive/documentation/GraphicsAnimation/Conceptual/HighResolutionOSX/APIs/APIs.html).

## 4. Data/state model and testable invariant

Do not introduce a new theme plugin, global unit registry or policy subtype.
If the candidate is approved, introduce at most one internal, explicitly named
conversion boundary (for example `CanvasDipToInputCharacterHeight`) with English
XML documentation of its input/output units. Keep the conversion independent
of colors, source length, format and persisted config. Existing public theme
and native source-range contracts remain unchanged.

Let `s` be the positive finite DirectWrite editor em request in DIPs. Let `a`
be the actual Canvas DIP-to-client-unit scale and `b` the input DC
logical-unit-to-client-unit scale, measured in the same HWND/thread context.
The intended GDI negative height is:

```text
inputHeightLogical = -Round(s * a / b)
abs(abs(inputHeightLogical) * b - s * a) <= b / 2
```

For the inspected 96-DPI DC renderer and ordinary MM_TEXT input mapping,
`a = b = 1` is the candidate to verify: source 13, input request -13. Use the
existing rounding rule and validate finite positive transforms and an admissible
request range before conversion. Reject requests that would round to zero
(GDI's default-font request) or overflow; do not clamp a sub-unit request and
then claim the rounding invariant. No new rounding convention is required.

**Acceptance at current effective DPI:**

1. Both DirectWrite source paint and source geometry retain exactly the same
   `s`, family request and row origin; their metrics and source offset results
   are unchanged from baseline A below.
2. Requested input em obeys the formula. For a resolved primary scalable font
   in the measured MM_TEXT context, `(tmHeight - tmInternalLeading) * b`
   agrees with `s * a` within one client unit, or a documented native-mapper
   discrepancy is investigated rather than silently widening the tolerance.
3. Query actual `CHARFORMAT` for bound/newly typed text and record its applied
   size, mask, host zoom and layout DPI context. Do not infer physical pixels
   from twips using a guessed monitor DPI. A contradictory applied size fails.
4. Fallback CJK/emoji/combining ink, selected text and descenders are not clipped;
   line boxes, caret and candidate remain inside their proper source/ribbon
   rectangles. This is a separate ink/geometry check, not an identical-ink-height
   or identical X-coordinate claim across shapers.

The ribbon is intentionally spatially separate. Do not compare its caret X
with the source caret X as a font-parity assertion, or reintroduce dual painting
over the same row. Equal em requests do not remove the recorded mixed-script
dual-shaper discrepancy.

State/lifetime remains existing UI-thread ownership:

```text
Ready -> prepare candidate HFONT -> apply to same owned RichEdit
      -> verify/recompute native host fit -> repaint
Composition/settlement pending -> retain installed font/frame -> coalesce request
      -> apply only after canonical final commit/cancel has settled
Failure -> retain canonical source/history/selection; restore old native font
```

Keep old HFONT alive until installation succeeds and the control no longer uses
it; release exactly once on replacement/disposal. Do not recreate the input HWND,
rebind the source interval, reset RichEdit text, alter native focus, settle IME by
force, or manufacture an engine edit solely for font/paint changes.

## 5. Why not the alternatives?

| Option | Correctness/compatibility | Decision |
| --- | --- | --- |
| Fix only Canvas's input conversion to its existing source unit | Corrects the demonstrated 4/3 request mismatch; keeps source layout and shared policy/config signatures stable. | **Recommended first correction**, subject to native instrumentation. |
| Multiply Canvas paint and geometry by 96/72 | Larger source, new viewport/hit-test/scroll metrics and insufficiently sized rows; potentially matches ribbon while preserving accidental inversion elsewhere. | Reject as unit fix. Could only be an explicit new readability/default-size decision with coordinated reflow. |
| Globally declare every ThemeTypography double to be DIPs and change all adapters/RTF | Cleaner eventual vocabulary, but changes LegacyPage, preview, status, Mac/Avalonia and undocumented direct callers together. | Reject for this task; inventory and compatibility baseline required first. |
| Add a public unit enum/value type now | Expands constructor/equality/deconstruction/adapter contracts despite no persisted size setting or runtime plugin need. | Reject; internal boundary names express the necessary invariant without public churn. |
| Give the ribbon an unrelated smaller fixed font | Looks quieter but disconnects editor size policies and repeats the inconsistency for any future font size. | Reject. |
| Add GetDpiForWindow to only the GDI conversion | Can enlarge ribbon while Canvas still rasterizes at fixed 96 DPI. | Reject; monitor DPI is not the renderer transform. |

## 6. DPI and line height: separate coordinated work, not a one-line fix

At 200% OS scale an unaware process can still report 96 DPI and be externally
scaled. `GetDpiForWindow` depends on awareness, and `WM_DPICHANGED` describes a
per-monitor transition with a suggested window rectangle. Record awareness,
internal/client dimensions and physical pixels separately.
[GetDpiForWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow),
[WM_DPICHANGED](https://learn.microsoft.com/en-us/windows/win32/hidpi/wm-dpichanged).

Do not enable per-monitor awareness as a side effect of the ribbon correction.
It requires one coordinated window/Canvas/input/accessibility transform:

- Prefer an embedded Native executable manifest for process awareness, not a
  late API call after HWND creation. It remains inside one executable.
  [Microsoft awareness guidance](https://learn.microsoft.com/en-us/windows/win32/hidpi/setting-the-default-dpi-awareness-for-a-process).
- Keep source frames and DirectWrite geometry in one documented logical space;
  convert window client size and pointer/scroll inputs exactly once at its
  boundary. Scale native child positions/fonts and physical UIA screen rectangles
  with that same transform. The current controller/frame interfaces frequently
  call these values pixels; audit producers and consumers before changing units.
- Set painter DPI or an equivalent explicit transform once; do not multiply
  the font and the render target independently. At renderer scale 2, source
  13 DIP and GDI 26 client pixels are coherent only if all body/ribbon/hit/AX
  transforms also use scale 2.
  [Direct2D SetDpi](https://learn.microsoft.com/en-us/windows/win32/api/d2d1/nf-d2d1-id2d1rendertarget-setdpi).
- During real composition, retain the host font/frame/focus and queue the
  latest scale/reflow request. Adapt surrounding top-level bounds if required
  without moving or recreating the candidate-owning control; exercise the actual
  OS event sequence and veto release if candidate placement cannot remain safe.
  A static scaled screenshot is not this transition test.

For the first 13-DIP boundary correction, keep current source row height
`13 * 1.45 = 18.85` logical units and existing minimums/reserved ribbon geometry.
Measure DirectWrite line height/baseline and RichEdit native line/caret/format
rectangles. GDI `TEXTMETRIC` exposes cell/leading; DirectWrite line metrics expose
line height/baseline. Font em is not line height.
[TEXTMETRICW](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-textmetricw),
[DWRITE_LINE_METRICS](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/ns-dwrite-dwrite_line_metrics).

If any representative run is clipped, derive host fit from its actual native
line box plus insets, outside composition; do not simply shrink its rectangle
because the nominal em shrank. Source fallback row refinements must remain
bounded to visible slices and reuse `ContinuousViewport`'s sparse height model.
The existing controller initialization/reveal paths derive row height directly
from policy values, and theme application does not establish a general live
typography reflow contract. A future 14-DIP or DPI policy change must coordinate
these owners explicitly; the first correction avoids changing source metrics.

## 7. Instrumented comparison: 12 paired visual states, then actual IME

All generated fixtures, measurements and captures belong under
`.temp/native-typography/` and `.cache/native-typography/`. Use a synthetic
`MOTE_HOME`; never rewrite user config or install/bundle fonts. No global OS
theme/scale/input-source mutation in unattended CI without a separate explicit,
restorable contract. No implementation or new CI step is authorized by this
design alone.

Use the product review's three files (roughly 100-line mixed-script Markdown,
nested JSON with one diagnostic, quoted/multiline CSV), two bundled themes,
and 100%/200% Windows scale: **12 states**, each paired on the same host:

| Build | Changes | Purpose |
| --- | --- | --- |
| A | Current source/input requests, exact retained SHA | Establish actual 13-DIP/17-unit applied mismatch, not screenshot inference. |
| B | Only Canvas input unit boundary; source remains 13 DIP, UI remains unchanged | Falsify/confirm the proposed mechanism without conflating palette/default-size/label changes. |
| C, optional later | Coherent source/input 14 logical units with coordinated row height | Product readability candidate only if B fixes parity but source readability still needs improvement. Do not run another full matrix before the paired A/B evidence is evaluated. |

Retain per state: exact binary/fixture SHA-256, OS/RID, theme/effective-values,
preview/active surface, awareness/effective DPI, DC mapping/transform, Canvas
render DPI and source em, requested/resolved primary face where observable,
GDI LOGFONT/TEXTMETRIC, RichEdit default and typed-character CHARFORMAT/zoom,
row baseline/line box, source and ribbon rects, global selection/stamp, host
selection/focus, and capture method. Mark any unavailable resolved run face as
unknown rather than echoing the requested family as its answer. Captures need
ready, mixed-text selection/edit and diagnostic/composition states; fixture
text may be retained because it is generated and nonprivate.

Source-hit checks use certified UTF-16/grapheme boundaries for ASCII, CJK,
surrogates/ZWJ, combining sequences, tabs and bidi cases. B's source geometry
should be unchanged from A. Verify click -> exact global selection, one edit,
Undo/Redo, Save and fresh-process reopen bytes. On long-file extension reuse
100 MiB JSON and 50 MiB single-line fixtures; font/theme changes must not read
or mirror their whole text or allocate per logical line.

Run one observed **real Microsoft Pinyin** session on the controlled Windows
host at both scales: candidate growth, commit and cancel; repeat after scrolling
the source caret offscreen, at a narrow window, and during a monitor move or
resize. Candidate rectangle belongs to the same focused RichEdit, never a
source geometry guess. Commit advances the canonical transaction once; cancel
advances it zero times. Keep existing Save/New/Close composition settlement
contracts. No injected marked-text selector or AX/UIA tree pass substitutes for
this session. Do not add `IMC_SETCANDIDATEPOS`: RichEdit owns composition;
Microsoft documents that command for apps drawing composition themselves.
[IME candidate command](https://learn.microsoft.com/en-us/windows/win32/intl/imc-setcandidatepos).

On a Mac target host, record matching CoreText/NSFont point requests and native
`firstRect`/candidate visibility with an actual CJK input method; there is no
Mac unit change. Retina/backing transitions are independent observations, not
Windows 200%-DPI evidence. Reuse existing synthetic probes only for their stated
ABI/selector scope.

Use one intended user to predict the edit destination before typing and judge
source/ribbon hierarchy, retaining mistakes rather than averaging them away.
This is formative usability evidence, not a population-level claim. The UIST
2024 DesignChecker study motivates paired visual and assistive inspection, but
its blind/low-vision web-developer setting cannot establish an optimal editor
font size or native IME correctness.
[Author manuscript/study scope](https://arxiv.org/abs/2407.17681).

## 8. Existing contracts/tests to preserve and new checks to add after approval

| Existing artifact | Protected contract |
| --- | --- |
| `tests/Mote.Themes.Tests/ThemePolicyTests.cs` | Bundled IDs/values, contrast and resolution; no theme registry/config churn. |
| `ThemeOverrideCompositionTests.cs`, configuration/theme override suites | Override validation/precedence and inherited typography; unknown keys remain diagnostics, no user config rewrite. |
| `NativeThemeRuntimeTests.cs`, `NativeThemeOverrideControllerTests.cs`, `NativeThemeOverrideFileWorkflowTests.cs`, `NativeThemeOverrideReviewReproTests.cs` | Same-ID effective-value detection, coalesced composition deferral, no source/version/selection/history change, failure rollback and reload behavior. |
| `NativeThemeOverrideWindowsTests.cs` | Real LegacyPage RichEdit controls, semantic/plain preview, native Undo/Redo and same-ID background replacement; freeze their typography behavior. This is not the Canvas font-parity oracle. |
| `NativeViewportTests.cs`, `HorizontalViewportTests.cs`, `CanvasInputWindowTests.cs`, `CanvasInteractionTests.cs` | Source-anchor/reflow behavior, grapheme/CRLF-safe bounded windows, zero-body and remote-offset semantics; keep bounded input caps. |
| `tests/NativeWindowsCanvasThemeWorkflow.ps1` and worker; `Test-NativeWindowsCanvasThemeContract.ps1` | Ordinary Continuous host, exact source/selection/history, dark/light/dark, owner restoration/bounded child execution and privacy-safe artifacts. Existing two-line screenshots are insufficient typography coverage. |
| `tests/NativeWindowsHorizontalWorkflow.ps1`, `WindowsUiaRangeExternal.ps1` ordinary product mode | Exact remote source copy/edit/Save/reopen, one source-backed Document and full-source ranges; ribbon remains input projection, not another Document. |
| `tests/NativeCompositionThemeWorkflow-Mac.ps1`, Mac horizontal workflows | Existing point-sized Mac input, composition deferral, source transaction/selection and target-host geometry/ABI evidence; preserve exact Objective-C object-plus-CGFloat calls. |
| Existing native/large-file performance gates | Source-ready/edit-to-draw causality and bounded work; do not relax thresholds for visual polish. |

After authorization, add a small pure conversion test (positive finite inputs,
13 -> -13 at the measured unit transform, fractional rounding/nonzero/range
contract) and a **native ordinary-Canvas** font-applied/fit test, not a modified
LegacyPage test that happens to pass. Native checks must sample after initial
binding, typed text and a color-only theme/reload; same-ID color changes should
not regenerate fonts. Keep strict test failures strict; non-gating visual probes
cannot confer a full-suite pass.

Measure paired Native AOT cold source-ready and edit/draw p50/p95/p99 on the
same RID/host using existing causal trace definitions. Report sample counts,
distribution/uncertainty and unchanged resource bounds; view-raster capture is
not physical presentation latency. A single font conversion adds no parser,
network/config I/O, font enumeration or per-frame work. Font metrics are queried
on creation/reflow or diagnostic demand, not per keystroke or full source scan.
Investigate any repeatable regression before accepting it; this document does
not invent a new looser performance allowance.

## 9. Execution order, compatibility and rollback

1. Root reviews this document and assigns production/probe/test file areas
   before any edit; keep the ongoing CI run untouched.
2. Build the narrow instrumented A/B comparison in repository scratch space.
   Verify effective transforms and the applied-native-em invariant first.
3. If B succeeds, assign the production island conversion and focused tests;
   retain policy values, LegacyPage/UI/preview behavior and shared public APIs.
4. Review the 12-state paired results and actual Pinyin gates. Separately assign
   quieter UI/label hierarchy or explicit 14-DIP readability work if supported
   by observations, rather than mixing them into the unit defect.
5. Coordinate a complete per-monitor transform change only after the separate
   DPI evidence identifies current awareness and boundary behavior.

There is no disk migration or config-version bump: existing theme IDs, color
overrides, path/telemetry/preview precedence and strict one-executable packaging
are unchanged. Existing direct managed callers can still construct/deconstruct
the same `ThemeTypography`; no public binary/layout signature changes. Such
callers observe the corrected Canvas ribbon size, while other adapters retain
their historical interpretation. If a later font setting is added, document its
unit in its key/contract and explicitly convert any *actually shipped* predecessor
setting; do not retroactively invent a valid old font key today.

Rollback is the narrow island conversion commit, not a policy/config downgrade
or a silent mode switch. A failed fit/IME/native-geometry experiment retains
the known bounded visible ribbon and source/history; settle real composition
before replacing native resources. Do not auto-restart a dirty editor or discard
marked text. `--legacy-page` remains an explicit separate user route, not a
certificate that fallback is visually/IME release-approved. Preserve failed
measurements and leave the corresponding release gate open.

**Result:** one actionable, falsifiable unit-boundary correction; no claim that
font parity alone resolves input-locus usability, all platform DPI behavior or
the broader native product acceptance.
