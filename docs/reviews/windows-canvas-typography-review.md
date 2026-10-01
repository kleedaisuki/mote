# Independent review: Windows Canvas input em boundary

Date: 2026-10-01. Reviewed commit: `4c7fe6e6ab80727364f4254861b624fdd5da5819`.
Scope: its production diff, surrounding font/theme/input lifecycle, new tests,
the [architecture decision](../architecture/native-typography-units.md), and
retained local evidence described by
[paired native validation](../validation/windows-canvas-typography.md).

## Verdict

**No substantive defect found in this correction.** The implementation fixes
the demonstrated Canvas-only size conversion at the intended adapter boundary.
Accept the narrow unit correction; do not equate it with native visual, physical
IME, high-DPI or Native AOT release acceptance. No production remediation is
required from this review. This is not verification of unexamined editor paths.

## Contract and implementation checks

| Concern | Evidence and judgment |
| --- | --- |
| Scope and compatibility | The only behavioral production change is `WindowsRichEditIsland.cs:1135` plus its internal helper at `:1165`. Shared theme records/default sizes, public configuration, other renderers/platforms and source geometry are untouched. |
| Numerical validity | `Math.Round(double)` retains nearest-even rounding. Nonfinite/nonpositive values, values rounding below 1, and rounded heights above `int.MaxValue` throw before `CreateFontW`. For an accepted rounded value, conversion to `int` and unary negation cannot overflow: the result is between `-2147483647` and `-1`, never zero or `int.MinValue`. `NaN` cannot evade validation because `IsFinite` rejects it. |
| Applied size, not nominal HFONT only | Baseline and corrected JSON independently report selected/default/inserted `CHARFORMAT` heights 255 -> 195 twips, with `CFM_SIZE` set; measured DC DPI is 96. These are applied 17 -> 13 logical em requests. `WM_GETFONT` does not expose the owned handle on either build; the evidence properly does not treat it as an installation oracle. |
| Source rendering and hit geometry | Native DirectWrite geometry/painter em remain 13, target DPI 96x96. Offset-1 hit rectangle remains exactly `(7.6171875, 1.9677734, 7.6171875, 15.107422)` in both raw observations. The change does not resize source or alter the geometry constructor. |
| Font ownership | Existing `SetInputAppearance` creates the new font before replacing `_inputFont`, sends `WM_SETFONT`, then deletes the previous font. Existing `Dispose` destroys the HWND hierarchy before deleting the retained font. The patch does not change this lifecycle or add a font resource per edit/paint. |
| Theme reload | `SetTheme` (`:381`) retains typography/spacing equality gating. Dark/light bundled policies have equal metrics, so color-only reload preserves the font/HWND; the native test observes identical font metrics, text, native selection and source geometry. The rollback branch remains unchanged. Invalid helper input occurs before new font creation; it does not leak an additional HFONT. |
| Input and composition | Composition deferral occurs before theme resources are recreated. `_settingText`, color-only character formatting and native-selection restoration are unchanged. The test attaches its edit observer before Bind/reload, verifies zero edits across both, then verifies one source-coordinate callback `(1,2,"Z")` with the original generation/nonce. This is synthetic native input, not physical IME evidence. |
| Native AOT compatibility | The production addition uses static arithmetic/validation and existing P/Invoke only; no reflection, dynamic code, new dependency or marshalling ABI is introduced. Reflection and delegate ABI queries exist solely in the JIT test assembly. Code inspection finds no new AOT obstacle, but no published AOT execution was independently performed for this commit. |

The maximum-int accepted boundary and immediately adjacent fractional values
are not explicit theory cases. The above representable-range proof is sufficient
for this small helper; adding these cases would be optional coverage, not a
necessary correction. Current theory cases cover midpoint behavior, normal
values, zero/negative, round-to-zero, both infinities, NaN and integer overflow.

## Calibri after insertion: existing observation, not a size regression

Both `.cache/native-typography/baseline.json` and `corrected.json` contain:

| Format | Baseline face / height | Corrected face / height |
| --- | --- | --- |
| Default and selected bound ASCII | Cascadia Code / 255 twips | Cascadia Code / 195 twips |
| Newly inserted ASCII `Z` | Calibri / 255 twips | Calibri / 195 twips |

The test sends owned `WM_CHAR` through the actual native subclass/default
procedure, then selects exactly `[1,2)` before reading `SCF_SELECTION`
(`WindowsCanvasTypographyTests.cs:109-112`). The source callback confirms that
this character replaced the expected `[1,3)` range. It is therefore a genuine
native inserted-character formatting observation, not accidentally reading
the neighboring CJK character or the wrong source range.

However, the hidden-window direct-message probe does not reproduce foreground
physical keyboard/IME routing. The cause of the face switch is **not established**;
the data cannot decide whether it also occurs in normal physical typing.
Microsoft documents keyboard-dependent automatic font selection and RichEdit's
script/repertoire-dependent font binding, making native binding a possible
mechanism rather than a proved explanation.
[EM_GETLANGOPTIONS](https://learn.microsoft.com/en-us/windows/win32/controls/em-getlangoptions),
[RichEdit Font Binding](https://devblogs.microsoft.com/math-in-office/richedit-font-binding/).

It does **not** undermine the measured em-size correction: inserted text has
`CFM_SIZE` and its actual 195-twip size matches the corrected 13-unit request.
It **does** prohibit claiming that every inserted glyph uses the source's
primary face, has matching glyph widths/baselines or visually identical ink.
The validation document already preserves that distinction. Do not disable font
binding or change IME language options speculatively in this patch. If visual
acceptance encounters the mismatch, compare foreground physical ASCII input
and this synthetic control, capturing insertion-point face/charset/language and
read-only `EM_GETLANGOPTIONS` before/after, without changing the user's layout.

## Independent evidence audit and limits

- Inspected the complete production diff and relevant theme, Bind, input
  subclass, disposal and formatting code. `git diff 4c7fe6e^ 4c7fe6e --check`
  returned clean.
- Independently hashed current production/test source, paired JSON, final
  typography TRX and integration TRX; hashes agree with the validation document.
  The retained final-hashes manifest also matches current managed binaries.
- Parsed retained final typography TRX: 13 total/executed/passed, zero failed.
  Parsed integration TRX: 49 total/executed/passed, zero failed. Retained final
  test build log reports zero warnings/errors. These are audited prior results,
  **not newly rerun tests**; completed validation was not repeated.
- Test expectations do not merely echo the helper: they query native applied
  character size, mapper metrics, DirectWrite em and source hit geometry. The
  twip tolerance is +/-5; it distinguishes the measured 60-twip defect clearly.
- Native probing is Windows-only; non-Windows returns are not cross-platform
  font verification. Hidden STA ownership/serialization is appropriate to the
  existing process-wide native dispatch state, but cannot certify foreground
  focus, physical typing, candidate UI or composition commit/cancel behavior.
- ARM/AOT runtime, 200%/per-monitor scale, fallback clipping/baselines, screen
  readers, complete edit/Undo/Save journeys and end-to-end performance remain
  unverified by this review. Equal requested em size is not a visual identity
  claim across DirectWrite and RichEdit.
