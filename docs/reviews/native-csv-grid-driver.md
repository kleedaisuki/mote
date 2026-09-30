# Native CSV Grid driver integration

## Contract

`NativeFormatSessionDriver.AnalyzePresentationAsync` accepts an optional fourth
`CsvGridRequest` argument. `NativeFormatPresentation` carries an optional `Grid`.
When the existing session implements `ICsvGridFormatSession`, an explicit Grid
request calls `AnalyzeGrid` exactly once on the existing semaphore-serialized
lane. Source highlighting and diagnostics come from the returned `Source`, not
from a second `Analyze` call. A Grid presentation has an empty Flow preview.

The legacy `DocumentAnalysis` source contract carries the first certified
interval, or `(0, 0)` when none exists. Independent intervals are never merged
into a false convex-hull certificate. Grid preserves its richer coverage contract.
Both bundle versions and Grid source length are checked before committing the
driver baseline. A mismatched bundle retires uncertain session state.

Analysis-only `AnalyzeAsync` remains unchanged, including idle Full semantics.
Existing three-argument presentation calls still produce Flow. Formats lacking
the CSV capability preserve Flow even if a caller supplies a Grid request.
Ordered edits, cancellation, session rebuild and document lifetime use the
existing driver mechanisms; no additional parser or cache is introduced.

## Focused tests

`NativeCsvGridDriverTests` wraps the actual CSV session and counts capability
calls. It checks one-update delivery and source diagnostics; ordered source edits
and same-version refresh; analysis-only Full; existing CSV Flow; Markdown Flow
fallback; deterministic pre-cancellation without consuming edits; and rejection
of a mismatched source version followed by session rebuild.

Verification command:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore --verbosity quiet `
  --filter "FullyQualifiedName~NativeCsvGridDriverTests|FullyQualifiedName~NativeFallbackFlowTests|FullyQualifiedName~NativeRangeNotificationTests|FullyQualifiedName~NativeIdleAnalysisTests"
```

Initial execution was blocked by concurrent controller symbols still being
implemented; a second execution was blocked by concurrent Mac Grid command-type
names. Neither invocation reached tests. After those owner changes arrived,
the command passed **19/19**, zero failures or skips, on Windows .NET 10 Release
(2026-09-30). This includes all seven new driver tests and twelve existing
Flow/range-notification/idle tests. The concurrent Windows Grid file emitted
CS0414 for `_installing`; this is outside driver ownership and was reported to
the integration owner. This work does not establish native GUI behavior,
four-RID AOT acceptance or performance measurements.
