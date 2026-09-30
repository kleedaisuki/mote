# CSV Grid format implementation review

Date: 2026-09-30. Scope: frozen working draft of `CsvGridContracts.cs`,
`CsvGridProjection.cs`, and the Grid-related `CsvIncrementalSession.cs` diff.
This is an independent code review, not native Grid acceptance.

## Resolved finding: P2 — diagnostic cap silently omits a width warning

Location: `CsvGridProjection.cs`, `ProjectGrid`, the `rowTruncated` calculation
and subsequent conditional `CSV004` insertion (draft lines 150–156).

`rowTruncated` compares delivered syntax diagnostics with `Row.ErrorCount`
**before** attempting the row-width warning. If requested syntax diagnostics
exactly fill the 8,192-entry global arena, a subsequent width warning cannot be
added, but neither the row nor the projection reports diagnostic truncation.
This violates the distinct, explicit diagnostic-omission contract. Exact
whole-file totals remain correct, making the inconsistent delivery observable.

Reproduction: 255 records containing 32 unquoted `a"` fields each, followed by
one record with the same 32 malformed fields and one additional clean field.
Request 256 rows × columns 0–31, Full. The result contains 8,192 syntax
diagnostics, `TotalDiagnosticCount == 8193`, but
`DiagnosticsTruncated == false`; the last row flag is false too. The omitted
diagnostic is its actual `CSV004` width warning. No giant allocation or timing
assumption is necessary.

Independent regression: `tests/Mote.Tests/CsvGridReviewReproTests.cs`,
`Diagnostic_cap_reports_omitted_width_warning`. Command:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CsvGridReviewReproTests --verbosity minimal
```

Observed before correction: 1 failed, 0 passed; failure at the global
`Assert.True(grid.DiagnosticsTruncated)` after exact total/count assertions
passed. Confidence: high, demonstrated deterministic defect.

Correction: include the width-warning obligation and its actual emission in
the row omission calculation. Do not infer omission solely from the
whole-document total, since offscreen records legitimately contribute to it.
Keep the regression as a normal assertion of the desired public contract.

Resolution: the implementation owner now sets `rowTruncated` when a required
width warning cannot fit the arena. The same independent focused command was
rerun after this targeted correction: **1 passed, 0 failed**, including both
global and final-row omission flags. The finding is closed. The unrelated
concurrent Native build emitted CS0169 for `_sourceDrawStamp`; it is outside
this format review and does not alter the reproduction result.

## Other inspected paths

- `AnalyzeGrid` builds a candidate privately, constructs both source and Grid
  deliveries, then checks cancellation before publishing `_cache` and work
  instrumentation. No candidate mutation of committed cache collections was
  found in the inspected paths.
- Checkpoint searches use actual certified row/column ordinals rather than
  physical newlines or delivered-child indexes. Giant-field origins are kept
  separate from bounded display data; source skipping is excluded from parser
  instrumentation by `SnapshotCursor.Seek`.
- Requested cells preserve actual coordinates; undelivered proved cells are
  Pending, fields beyond proved row width are Missing, and actual empty fields
  retain zero-length syntax origins. Oversized values do not decode a giant
  value into the display arena.
- Same-version cache reuse and changed-version dense/sparse invalidation reuse
  the shared candidate path, while legacy `AnalyzeWindows` retains its previous
  normalized-window delivery path.

No other substantive issue was established by this review. Existing owner
tests were inspected rather than rerun indiscriminately. This does not verify
native control installation, clipboard/replacement commands, GUI latency,
physical IME behavior, or exhaustive scheduling-dependent mid-scan cancellation.

The final inspected draft also preserves the original source-versus-row anchor
in `RequestedAnchor`; unresolved source-follow delivery uses an empty requested
row range rather than guessing a logical ordinal. Its absence of rows is
represented separately from whole-file semantic completeness. The per-record
certificate replay stop is checked before progressing through additional
ordinary fields, and unavailable requested fields remain Pending with no
invented origin. Managed parser benchmark values are not GUI latency evidence.
