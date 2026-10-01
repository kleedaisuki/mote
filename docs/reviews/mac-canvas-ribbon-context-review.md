# Independent review: Mac ribbon current-frame context

Date: 2026-10-01. Production scope: pending `MacTextInputIsland.cs` diff against
`b292cf7`, SHA-256
`707058E7E688FD20AD7ACF746ABF56F8C0619A7FF2AC93F19CC77099F1D9083D`.
Test scope: `MacCanvasRibbonContextTests.cs`, including the exact-source binding
fixture correction in `6736b0d`. This is a scoped source and retained-evidence
review, not native GUI validation.

## Verdict

**No substantive production defect found in this correction.** The change
corrects an actual stale offset label under admitted binding reuse without
changing the input interval, native selection projection, marked-text guard,
public contracts, theme configuration, or typography units. No production
remediation is required from this review.

## Executable path and invariants

| Concern | Evidence and assessment |
| --- | --- |
| Trigger is reachable | `NativeEditorController.ShowCanvasDocument` retains the binding when generation/version match and the active caret remains inside its input interval; it publishes a newer frame via `SetCanvasFrame`. `MacEditorShell.SetCanvasFrame` forwards that frame, while the retained binding's `Active` still records its original caret. |
| Correct source of the label | `PlaceHost` now obtains both label and projected selection from the same `_frame`. The label uses `frame.SelectionActive`, not retained `binding.Active`. Invariant `N0` formatting and the existing label prefix remain unchanged. |
| Native admission order | The existing short-circuit order remains `_editor == 0`, missing binding, missing frame, then `IsComposing`. The pure helper runs only after these guards. Missing state does not acquire a new native `hasMarkedText` call; marked text still refuses host placement. |
| Projection compatibility | Extracted static projection retains the original min/max intersection, endpoint clamps, collapsed outside-selection behavior, and source UTF-16 units. The instance wrapper used by `OnSelectionChanged` delegates to the same arithmetic. Forward/reverse selection semantics are not redesigned. |
| Native interaction | Existing `setFrame:`, `setHidden:`, `setEditable:`, label setter, `setSelectedRange:`, `sizeToFit`, `scrollRangeToVisible:`, and caret-alignment calls retain their relative order, selectors and argument forms. The patch adds no AppKit/Objective-C entry point or interop signature. |
| Composition and lifetime | `SetFrame` still permits paint-frame updates during marked text without placement. The pure helper neither commits nor cancels composition, mutates state, creates native objects, or owns resources. Existing unavailable/failed-input paths remain unchanged. |
| Version scope | The helper refuses mismatched frame/snapshot versions, preserving the original placement guard. This is only a local version check, **not** a global document-generation certificate: generation/binding admission remains the controller's responsibility. |
| Scope | No public ABI, configuration, default theme units, document mutation, input nonce, or generation semantics change. |

## Test assessment and evidence

The independent tests use literal expected projected ranges, cover same-version
movement with a retained original caret, forward/reverse and outside selections,
missing/mismatched state, and invariant grouping under a non-English task-local
culture. The test helper now constructs input text from the actual snapshot,
matching the production exact-source-slice binding invariant. The stale-label
assertion discriminates the original implementation without mutating production.

Inspected the validator's final exact-source run log and TRX: **18/18 passed**,
zero failed/skipped (57 ms), with no compiler warnings. Corrected test source
SHA-256 is `C61B0B83EACACC777CB13BECA83F2591BB0119BEB63BADC367C0A531713CA149`;
its recorded qualified assembly SHA-256 is
`606169045ACBFD571932DADD4D9C87F376E5C1175540F31B111EDF7DE70B3249`.
The production source hash above is unchanged. The earlier 18/18 run remains
historical evidence, not certification of the corrected fixture. This reviewer
did not rerun completed validation. See the
[validation record](../validation/mac-canvas-ribbon-context.md).

## Limits

No macOS native execution, AppKit installation, screenshot, physical IME,
accessibility, high-DPI typography, or end-to-end composition behavior was
verified here. Portable arithmetic tests cannot prove the actual ribbon is
painted or that its affordance is understandable. The original bounded input
ribbon design and its release gaps are not reassessed by this narrow correction.
