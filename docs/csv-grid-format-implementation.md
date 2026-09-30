# CSV Grid format implementation and acceptance evidence

Date: 2026-09-30. Status: **production format/data layer implemented; native Grid UI is not installed**.
This extends [the reviewed architecture](csv-grid-architecture.md), not its native,
clipboard, accessibility or structured-replacement acceptance. The existing Flow,
CSV Analyze/AnalyzeWindows and policy interfaces remain compatible.

## Ownership and public integration

`CsvPolicy.CreateSession()` now additionally implements `ICsvGridFormatSession`.
`AnalyzeGrid(snapshot, edits, request, token)` returns `CsvGridAnalysis` containing
independent source decoration (`WindowedAnalysis`) and Grid delivery. Both use one
private `CacheState` candidate and one authoritative CSV grammar. Neither call is
performed via a second public Analyze or a second session. Candidate construction,
source projection, Grid projection and the final cancellation check all precede
publication of `_cache` and scan statistics. Dispose still retires all cache facts.
Native must serialize the existing document lane and supply exact committed edit
chains; a returned projection is a ready immutable value, not a thread-safe parser.

```csharp
using var session = new CsvPolicy().CreateSession();
var gridSession = (ICsvGridFormatSession)session;
var request = new CsvGridRequest(
    [new TextSpan(0, 32)], new CsvGridAnchor.Row(100),
    rowLimit: 32, columns: new GridRange(0, 8), scope: AnalysisScope.Full);
var bundle = gridSession.AnalyzeGrid(snapshot, [], request);
// Native callbacks may read bundle.Grid.Rows/DisplayText, never call AnalyzeGrid.
```

The source example range must be clipped to the actual snapshot by its caller.
All coordinates are zero-based UTF-16/record/column coordinates; a user-facing
adapter can label them one-based. `RequestedAnchor` retains source-follow versus
ordinal intent. An unindexed source anchor yields no invented rows and an empty
`RequestedRows=(0,0)`, rather than claiming the prefix row count is that source's
actual owner. An explicit ordinal retains its requested range even if unavailable.
A CR/LF/CRLF delimiter belongs to the preceding record; EOF belongs to the last
existing record. Empty source has zero rows, and a trailing delimiter creates no
synthetic extra row. The first record is data, never an inferred header.

## Coordinate proof and retained state

- Dense row blocks remain shared/sliced/translated; cumulative segment counts
  derive logical record ordinals without a second per-row graph.
- Sparse record checkpoints are `(SourceStart, RowOrdinal)`; whole-file scans
  also compute exact `RowCount` and `MaxWidth` scalars. Prefix scans publish only
  the number of certified prefix records. Record boundaries are grammar-proved,
  not counted physical lines or guessed comma/newline positions.
- Oversized-record checkpoints are `(SourceStart, ColumnOrdinal)` and giant-field
  summaries include their column ordinal. The authoritative parser counts bulk
  skipped empty fields. Grid seeks to the preceding certified field start and
  reconstructs actual selected columns, never enumerates semantic child indices.
- Record checkpoint estimates charge 8 bytes (previously 4); field checkpoint
  estimates charge 12 bytes (previously 8), with conservative record/giant-summary
  estimates retained. New scalars and segment maxima fit the existing aggregate
  allowance. Dense-to-sparse admission also charges checkpoint facts before new
  dense blocks. The structural target remains below 32 MiB/256 dense segments.
  This estimate is not a CLR layout guarantee, peak RSS, or transient-allocation cap.
- Sparse or giant-cache edits invalidate old coordinates under the existing
  rebuild/provisional-prefix rules. No shifted old ordinal certificate is revived.
  Ordinary dense record reuse preserves its previous delimiter-boundary proof.

A giant first field followed by `,tail` emits column 0 as Oversized and tail as
column 1 with its exact syntax origin. It cannot turn the tail into column 0 even
when source-window semantic children omit the giant. No CSV cell strings or
snapshot references enter the retained policy index or the delivered model.

## Bounded ready data and truthfulness

| Resource | Implemented bound/semantics |
| --- | --- |
| Source interests | Existing 1–8 windows, summed requested width <=512 Ki UTF-16; normalization merges overlaps |
| Grid rows/columns | <=256 rows, <=64 requested actual columns, admission product <=8,192 |
| Display arena | <=64 Ki UTF-16 total, <=1,024 units per field |
| Field decoding | Source fields >64 Ki are Oversized, exact full syntax origin, no decoded copy; ordinary values use a bounded <=2,050-unit prefix |
| Grid replay | 128-Ki source-unit admission, checked between proved records/fields; an ordinary record/field or giant diagnostic sample may finish with <=64-Ki bounded overshoot |
| Diagnostics | <=8,192 exact source spans; per-row/global omission flags, separate exact global total for Complete |
| Delivery objects | Copied immutable rows/cells/coverage/diagnostics; no snapshot, cursor, cache or native handle |

Source-window projection has its existing separately charged checkpoint replay;
`CacheStatistics.ScannedSourceUnits` combines source and Grid parser work. It is
not bytes fetched and does not count all bounded display GetText copies. Full
semantic validation still scans the entire file. The replay guard prevents a
wide record of ordinary 60,000-unit fields from reading all selected columns in
one request. Proved-but-undelivered columns become Pending (no invented origin),
not Missing. A native adapter can issue a new bounded column request to reach them.

`GridValueState` distinguishes Complete decoded delivery, Clipped display,
Oversized proved syntax, Pending unavailable delivery, and Missing (only columns
outside a proved row width). A real empty field has an exact zero-length source
origin and Complete empty display; arena exhaustion instead has a full origin and
Clipped empty display. Missing/Pending never become editable source origins.

Display clipping preserves surrogate-pair boundaries. TAB/CR/LF become visible
`⇥`/`␍`/`↵`; other control or malformed surrogate display units become U+FFFD.
`DisplaySanitized` marks this transformation. Display text is never exact clipboard
data, and whole-grapheme clipping is not implemented. Source text is never changed.

Semantic completeness and exact extent remain independent from display omission.
A fully validated file can have clipped/oversized/pending display cells and still
be Complete, with exact diagnostic total and row/max-width facts. Requested-record
syntax diagnostics outside the chosen columns or budget are explicitly omitted;
row/global `DiagnosticsTruncated` communicate this. Diagnostic-cap exhaustion
also marks a dropped CSV004 width warning, even when the preceding syntax count
lands exactly on 8,192. Native status must not sum source/Grid totals, duplicate
errors, or treat omission flags as language errors.

## Verification and adversarial review

- Contracts: 15 focused Release tests cover copying, ownership, bounds, Unicode,
  extent/completeness separation and unresolved source intent.
- Independent differential validation: 15 tests, 1,920 seeded valid requests,
  80 adversarial edits, all short-example CR/LF/CRLF/EOF owners, 262,150-row sparse
  index, narrow/ragged columns, malformed CSV diagnostic spans and precancel
  atomicity. [Full methodology](reviews/csv-grid-differential-validation.md).
- Owner projections: 9 tests cover 100-Mi giant warm two-window bounds, exact giant
  first/tail identity, 500,000 empty columns, 64-column scalar clipping, controls,
  giant diagnostic omission, pending wide fields, timed cancellation's two legal
  outcomes, and 8,192 cells sharing a 64-Ki arena.
- Independent reviewer: 1 deterministic diagnostic-cap regression. Reviewer found
  a dropped CSV004 without omission flags; the producer was fixed and the same
  reproduction passed. [Review](reviews/csv-grid-format-code-review.md).
- Latest combined Grid run before the final new arena test: 39/39 passed; arena
  regression then 1/1 passed. Existing CSV compatibility plus then-current contracts
  passed 91/91 before later Grid-only coverage. No completed unrelated validation
  was repeated. Formats Release `--no-restore -warnaserror`: zero warnings/errors.

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore `
  --filter FullyQualifiedName~CsvGrid --logger 'console;verbosity=minimal'
dotnet build src/Mote.Formats/Mote.Formats.csproj -c Release --no-restore -warnaserror
```

Precancellation proves rejection before work. The timed 10-Mi test accepts either
cancellation with unchanged previous cache/statistics or normal exact new-version
completion; it does not force a scheduler-controlled mid-scan cancellation or
claim that timing alone proves a particular cancellation phase. No candidate or
ready model is published before the final token check, as independently reviewed.

## Managed performance probe and negative evidence

Artifacts remain under `.temp/csv-grid-benchmark/{Probe.csproj,Program.cs,*.jsonl}`.
Environment: Windows 10.0.26200, Intel Core i9-12900H, .NET SDK 10.0.400/runtime
10.0.11, managed Release (not Native AOT). One process per scenario; each makes one
cold Full call and 40 warm Visible calls, discards the first five warm samples,
then reports sorted median/nearest index p95 for 35 retained calls. Allocation uses
`GC.GetAllocatedBytesForCurrentThread`, scan units use session instrumentation.
Both source interests are narrow disjoint windows; Grid uses an independent row
and column interest. Document construction is outside the timed interval.

| Scenario | Cold Full ms / allocated B | Warm p50 / p95 ms | Warm median allocated B | Max warm parsed source units | Retained index estimate B |
| --- | --- | --- | --- | --- | --- |
| 100 Mi quoted giant, ordinary tail, 1×2 Grid | 469.2144 / 420,762,752 | 0.0118 / 0.0301 | 70,272 | 12 | 92,824 |
| 100 Mi comma run + tail, 1×3 distant columns | 75.4265 / 421,295,176 | 0.0665 / 0.2121 | 563,488 | 131,083 | 111,884 |
| 500,000 `a,b CRLF` rows, distant 32×2 Grid | 203.5035 / 16,633,992 | 1.2256 / 3.1733 | 199,904 | 19,045 | 568 |

Reproduction:

```powershell
dotnet run --project .temp/csv-grid-benchmark/Probe.csproj -c Release -- giant
dotnet run --project .temp/csv-grid-benchmark/Probe.csproj -c Release --no-build -- wide
dotnet run --project .temp/csv-grid-benchmark/Probe.csproj -c Release --no-build -- sparse
```

These small samples establish bounded managed warm projection, not p95 native
input/paint/accessibility latency. Cold full scanning remains O(file) and cumulatively
allocates approximately 421 MB for either 100-Mi case. The wide-comma warm request
allocates ~563 KB despite its tiny ready display, chiefly including certified
lookbehind/source-interest replay; do not describe it as zero-allocation scrolling.
Peak memory, retained object graphs, OS I/O, first paint, Mac performance and AOT
are not measured here. Compare future optimization against these exact source
shapes and distinguish parser replay from ready table callbacks.

## Native handoff / remaining work

This feature intentionally does not write Native, Configuration, Themes or CI.
A native adapter should use the reviewed architecture's bounded owner-data table,
actual coordinates and installation identity; no native callback may parse. It
still needs prepared-model/coalescing integration, continuous logical scrolling,
selection/reveal/copy/replace contracts, resource limits, accessibility and target
four-RID strict-single-binary acceptance. Until that integration ships, existing
CSV preview behavior remains the product UI. Format-layer success is not native
Grid release acceptance and does not close the overarching mote goal.
