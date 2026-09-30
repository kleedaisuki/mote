# CSV grid accessibility model correctness review

Date: 2026-10-01. Scope: `src/Mote.Native/GridAccessibility.cs`,
`tests/Mote.Tests/GridAccessibilityTests.cs`, and the supplied
`docs/csv-grid-accessibility-contract.md`. Adapter/provider callers were traced
only where necessary to establish observable consequences. No production or
test source was modified by this reviewer. This is not external UIA/AX acceptance.

## Assessment

The rectangular **set** algorithm is correct for ordinary nonnegative endpoints,
including reversed orientation and full retained off-window endpoints. An
independent exhaustive set oracle passed 8,192 cases. Layout caps and wide
selection-area arithmetic are sound. Two behavior issues were reported to the implementation owner and resolved in the current model. No remaining substantive model defect was identified; external native acceptance is still unverified.

## Findings

### Resolved P2 / high confidence: a Pending slot is exposed as a focused cell

Location: `GridAccessibility.cs`, `NativeGridAccessibility.Create` final focus
normalization (reviewed line 141); downstream Windows provider focus publication,
`HasKeyboardFocus`, and `GetFocus` in `WindowsGridUiaProvider.cs` (reviewed lines
35, 182, 246).

Trigger: install a bounded pending window with an in-window focused target and
native table focus. `Create` strips ready/projection authority correctly, but
retains `FocusedCell` because it tests only `Contains`. The Windows provider
likewise checks only `Contains`, so it exposes that Pending/null slot as the
focused cell instead of the table/container.

Reproduction: the review harness constructs a one-cell pending window at
absolute `(1000,16)` with null projection/ready and focus at that coordinate.
Observed: `State=Pending; FocusedCell=(1000,16)`. Contract section 5 explicitly
requires pending/unavailable coordinates to report the table/container, not a
false ready cell focus. The pending target itself must still be retained for
later delivery.

Correction: normalize public focused-cell semantics to a delivered non-Pending
cell, or have every platform focus getter/event consistently fall back to the
table while retaining the internal pending target. Include both null slots and
explicit Pending descriptors; Missing is a proved layout cell and must not be
incorrectly treated as Pending. Verify focus events and property readback, not
only helper state.

### Resolved P2 / medium-high confidence: successful mutation reverses endpoint roles and moves unrelated active focus

Location: `GridAccessibility.cs`, `NativeGridAccessibility.Mutate` successful
AddCell/RemoveCell reconstruction; Windows adapter `MutateSelection` assigns
`next.Active` to `_row/_column`, and `PublishAccessibility` uses those fields as
its default semantic focus.

Trigger: a reversed one-row selection, anchor `(8,5)`, active `(8,3)`. Adding
`(8,6)` returns anchor `(8,3)`, active `(8,6)`. Removing the original anchor
`(8,5)` likewise normalizes the remaining endpoints and moves active from the
surviving `(8,3)` to `(8,4)`. The exact selected set is correct, but endpoint roles
and the still-selected active cell are changed without a focus request.

Impact: the next source Reveal/Replace can target a different active cell, and
Windows default focused-cell reporting changes after a selection-only request.
The contract requires preserving anchor/active semantics and says unsupported
or failed admission never changes focus; it also explicitly separates selection
and focus operations. This is an endpoint/active-state issue, not a failed exact
set union/difference.

Correction: reconstruct successful edge changes preserving orientation and
surviving endpoint roles. When adding beyond the anchor edge, extend the anchor
rather than swapping it with the active endpoint; when removing an endpoint,
move only that endpoint. If product policy instead intentionally moves active on
every mutation, specify that policy and preserve independent focus explicitly;
do not let rectangle normalization choose the policy accidentally. Add reversed
horizontal/vertical endpoint regression tests with focus/command readback.

Resolution: `Create` now drops a focused cell when its captured state is Pending.
Successful non-singleton Add reconstructs endpoints according to their original
axis orientations; singleton Add retains old anchor and makes target active.
Remove clamps each original endpoint individually to the surviving range.
This preserves the still-present active endpoint rather than swapping roles.
The owner reported 15/15 focused tests passing, including reversed endpoint
regressions and a finite-set oracle.

## Verification evidence

Reproducible harness: `.temp/grid-accessibility-model-review/Review.csproj`,
`Program.cs`, and `result.txt`. Run:

```powershell
dotnet run --project .temp/grid-accessibility-model-review/Review.csproj -c Release
```

The harness enumerates every ordered pair of endpoints in a 4-by-4 coordinate
domain, every target in that domain, and Add/Remove. It materializes the expected
set independently, checks rectangle representability by its bounding area, and
compares both result codes and the exact resulting set. Unsupported/NoChange
also must preserve original endpoints. Output: **8,192 cases passed**. This
checks reversed endpoints without assuming implementation normalization.

Other verified reasoning and probes:

- Full retained endpoints are used; the model does not intersect the current
  window before computing mutation. WholeRows Add/Remove is conservatively
  Unsupported, which the supplied contract permits.
- Selection width/height are each at most 2^31 for valid int endpoints. Their
  product is at most 2^62, so signed long arithmetic cannot overflow.
- `GridRange` rejects negative/overflowing ranges. An admitted slot at
  `(int.MaxValue-1,int.MaxValue-1)` maps and names correctly as ordinal
  `2147483647`; no wrapped labels or end values were observed.
- Pending layouts enforce 256-row, 64-column, and 8,192-cell caps independently
  of whether a projection exists. `ValidateRectangle` refuses oversized arrays
  before allocating its HashSet; deduplication still requires a complete exact
  rectangle and does not infer WholeRows.
- Pending navigation and ready-document/version mismatches remove both ready
  and projection read authority. Missing/Pending/Oversized remain null values,
  and Complete empty string remains explicitly distinct. Projection constructors
  already validate display slices and resource bounds.

An initial focused test build identified nonexistent `AnalysisCompleteness.Partial`
and enum InlineData/int parameter mismatch errors. These were promptly reported
and the owner confirmed correction to `CoveredRegion` and explicit int casts,
with **12/12 focused Release tests passing**. This reviewer did not repeat that
completed test run. The exhaustive harness compiled and ran successfully against
the current Native/Formats code.

## External semantics and limits

[Microsoft's AddToSelection contract](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-iselectionitemprovider-addtoselection)
defines exact addition to the selected collection. It supports the independent
set oracle; it does not by itself specify the product's rectangle endpoint
orientation. That part of the review is grounded in the repository's explicit
anchor/active and focus contract plus the actual adapter assignments.

No OS screen-reader session, native COM event observation, AX runtime execution,
composition/reentrancy experiment, controller source command, or clipboard test
was performed. No whole-file materialization was required. Provider lifetime,
registration, geometry, and external acceptance remain separate review scopes.

Final targeted verification: after the owner stabilized the Windows provider,
the failure-specific harness completed successfully with exit code 0:

```powershell
dotnet run --project .temp/grid-accessibility-model-review/Review.csproj -c Release -- --regressions-only
```

Observed: reversed Add preserves active `(8,3)` and extends anchor to `(8,6)`;
reversed Remove preserves active `(8,3)` and moves removed anchor to `(8,4)`;
Pending state exposes null `FocusedCell` (table focus). All three assertions
passed. Near-int-max coordinate/name mapping and large-endpoint rejection also
remained correct. Output is retained in
`.temp/grid-accessibility-model-review/regressions-result.txt`.

The temporary Node-overload build conflict was resolved and is not an outstanding
finding. The owner subsequently reported **16/16 focused native tests passing**.
The previously completed 8,192-case independent oracle was not repeated. No
remaining substantive model finding was identified within this review scope.
