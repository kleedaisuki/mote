# CSV oversized-record certified projection

Status: implemented and locally measured on 2026-09-30; [independent review](reviews/csv-oversized-projection-review.md) cleared the corrected source (SHA-256 `2FC53504ED0FA96E03D6CDC72B370F56FA3EACC84D8B80BB0A91C5BB55D25F49`). Integration/four-RID acceptance remains separate. This extends the committed [bounded record index](csv-index-budget-design.md), not the Native UI or its performance acceptance. The policy still owns no text: `Document`/`TextSnapshot` is authoritative.

## Failure and representation

The bounded row index solved retained memory, but projection replay of a single 100 Mi UTF-16 quoted record still scanned all 104,857,600 units and allocated ~420,661,312 bytes per warm call. Replaying two windows in the same row did not solve that. An edit to the same dense one-row cache could spend hundreds of milliseconds on immediate Visible analysis.

Authoritative Full validation now collects a private `LargeRecord` only when its logical source extent exceeds 64 Ki units. It retains the verified Row, sparse **field-start** offsets roughly every 64 Ki source units, and one `GiantField` summary for each field longer than 64 Ki. Each summary contains absolute source span, quote kind, closing/content boundary, CSV001/CSV002 spans and field error count. No source strings, decoded values, arbitrary newline seek points, or guessed in-quote states are retained. Escaped quotes and embedded CRLF are validated by the same parser before a certificate exists. Dictionary entries, boundary arrays and giant fields have a conservative structural charge; their estimate is included in CacheStatistics and the combined 32 MiB dense-switch threshold. Metadata cardinality is O(source length / 64 Ki), including the one record object per oversized record.

Warm sparse seeking uses a certified oversized row to reach its `After` without replay. Projection seeks field boundaries, skips giant field bodies, emits the same absolute quoted semantic token and CSV001/CSV002 spans, and omits oversized decoded cells with explicit ProjectionTruncated. CSV003 diagnostics in a giant unquoted field are scanned only in intersecting bounded interests; zero-width interests retain inclusive preceding/current-character ownership and shared diagnostic edges are emitted once. Exact whole-file diagnostic count, including offscreen CSV004 width mismatches, comes from the unchanged Full validation, not from this partial delivery.

Each requested window budgets certified checkpoint lookbehind separately from its delivery width: 2 × 64 Ki + min(window width, 512 Ki) + one endpoint unit. A final ordinary field can overshoot by at most its bounded <=64 Ki extent; unrestricted legacy interests may therefore deliver only a truncated payload. Existing 4,096 row and 8,192 cell delivery budgets still apply. The legacy Analyze API/signature remains intact, and ordinary small-file Full project-all behavior is unchanged. SourceIndexed/Complete means validation, never every cell or diagnostic was rendered. The certified empty field at a comma-delimited EOF must survive even when replay consumption reaches its allowance exactly there.

A review-discovered regression conflated checkpoint lookbehind with delivery: 131,073 commas lost two tail cells even though the tiny requested tail should fit its cell quota. Separating these allowances corrected the neighbor family rather than adding another special EOF condition. A further quota-skip correction makes skipped head delivery explicitly Truncated while preserving its tail's fair allocation.

## Editing, cancellation and limits

No old oversized certificate is reused under a new version. A Visible edit to an oversized cache falls back to a bounded fresh prefix with honestly Provisional coverage and null global count; idle Full revalidates and certifies the new snapshot. Dense ordinary edits retain incremental suffix reuse. A quote-changing edit that merges short records cannot cause an unrestricted immediate scan: incremental Visible is capped relative to its restart record and falls back to the same prefix on exhaustion. True EOF reached before the allowance is not treated as exhaustion. Full after an edit is still O(file length); this change does not establish bounded idle semantic completion or GUI input-to-paint latency.

All prospective records/summaries are local until output and the final cancellation check succeed. Cancellation retains the previous committed version, exact total and summary graph. Dispose releases the graph. ScannedSourceUnits counts consumed source, not certified skips, bytes fetched, decoded output, or RSS. Giant unquoted diagnostics over a very wide legacy interest can be omitted after bounded delivery; their whole-file count stays exact and ProjectionTruncated makes omission explicit.

## Verification and measurements

Release Windows x64, .NET SDK 10.0.400/runtime 10.0.11, same local environment as the index-budget measurements. `CsvOversizedFieldTests` passed 27/27 before the cancellation-test correction below: head/tail/two-window quoted certificates, escaped quotes/CRLF, CSV001/002/003 coordinates against fresh CsvPolicy, zero-width diagnostic deduplication, sparse giant EOF seeking, million commas, checkpoint/EOF neighbor families (65,535/65,536/65,537/131,071/131,072/131,073 commas), fair dense-head quota with a distant tail, ordinary-field lookbehind, ordinary dense EOF edits, quote-merging edits, cancellation invariants, and 100 Mi-unit warm allocation/scan bound. The final CSV-focused Release suite passed 74/74 and Formats Release warn-as-error compilation had zero warnings/errors. Independent review added 1,500 giant-field and 449 disjoint-window differential cases plus eight failure-directed EOF neighbor lengths, with no remaining material blocker.

### Cancellation race correction (2026-09-30)

[Strict macOS CI run 36721907721, job 109908857165](https://github.com/kleedaisuki/mote/actions/runs/36721907721/job/109908857165) failed only `Mid_scan_cancellation_preserves_old_giant_certificate`: `Assert.Throws()` observed no exception (line 271), with 485 other Mote.Tests cases passed. The test incorrectly assumed `CancelAfter(1)` guarantees cancellation during a synchronous Full scan. Timer callback scheduling can occur before scanning, during it, or after the final cancellation check/commit; even cancellation just after that check does not retroactively invalidate a successful result. The log establishes a test-contract failure, not a reproduced partial-cache publication defect.

The replacement `Cancellation_race_preserves_transactional_giant_certificate` accepts only two legal outcomes. An `OperationCanceledException` must carry the requested token, cancellation must be requested, and every prior cache-statistics field must remain unchanged; the old snapshot must still yield Complete/indexed CSV001 and its exact original span. A successful call must publish the new cache/result version, Complete/indexed truth, zero diagnostics, and exact new token/row spans. Appending a closing quote changes the old unterminated CSV001 count from one to zero, making stale semantic truth observable. The timer remains a race exercise, while a separate `Timeout.Infinite` case deterministically exercises successful completion. The existing pre-canceled case deterministically requires an exception and the unchanged old cache, now also checking original diagnostic/token spans and Complete truth. No production source or synchronization hook was changed. **Controlled mid-scan cancellation is not proven** by either timer outcome and is no longer claimed.

Focused local verification on Windows 10.0.26200/win-x64, SDK 10.0.400: `dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --filter FullyQualifiedName~CsvOversizedFieldTests --logger 'trx;LogFileName=csv-cancel-race.trx' --results-directory .cache/csv-cancel-race` passed **28/28**, skipped zero, build without reported warnings/errors. Compiler parsing succeeded and `git diff --check -- tests/Mote.Tests/CsvOversizedFieldTests.cs docs/csv-oversized-projection.md` found no whitespace defects. Raw TRX and the independently fetched failed macOS job log are under `.cache/csv-cancel-race/`. The corrected commit `6970825` subsequently passed all six strict hosted jobs in [CI 36723167675](https://github.com/kleedaisuki/mote/actions/runs/36723167675): Windows/macOS solution tests and four Native AOT RID builds. This validates the corrected cross-platform test contract, not controlled mid-scan cancellation or GUI performance.

Benchmark commands (all artifacts under repository .cache):

```powershell
dotnet run --project tests/CsvSparseBenchmark/CsvSparseBenchmark.csproj -c Release -- 100 11 quoted 1
dotnet run --project tests/CsvSparseBenchmark/CsvSparseBenchmark.csproj -c Release -- 100 11 commas 1
dotnet run --project tests/CsvSparseBenchmark/CsvSparseBenchmark.csproj -c Release -- 3 11 ordinary 1
dotnet run --project tests/CsvSparseBenchmark/CsvSparseBenchmark.csproj -c Release -- 3 11 quoted 1
# New size 0 denotes exactly 16 Ki UTF-16 units, not zero-length source.
dotnet run --project tests/CsvSparseBenchmark/CsvSparseBenchmark.csproj -c Release -- 0 31 ordinary 3
```

| Scenario | Historical before | Current after | Allocation after / scanned units |
| --- | ---: | ---: | ---: |
| 100 Mi quoted, warm optional one | p50 382.9268 ms (n=5) | p50 0.0304 ms (n=11) | 34,976 B / 0 |
| 100 Mi quoted, warm two windows | prior whole-record replay | p50 0.0257 ms (n=11) | 35,328 B / 0 |
| 100 Mi commas, warm two windows | p50 31.5499 ms (n=7) | p50 0.1691 ms (n=11) | 323,088 B / 65,617 |
| 16 Ki ordinary, warm one | p50 0.0753 ms (n=31) | p50 0.0641 ms (n=31) | identical 71,000 B |
| 16 Ki ordinary, warm two | p50 0.0863 ms (n=31) | p50 0.0803 ms (n=31) | identical 77,096 B |
| 16 Ki ordinary cold Full | p50 0.8752 ms (n=3) | p50 0.8457 ms (n=3) | 170,290 B |

The small-file before build uses committed HEAD 8dc8018 in `.temp/csv-oversized/baseline`, with only benchmark size-0 input extension; no parser modifications. Small-file medians are noisy and support no universal speedup claim, but no material warm allocation/latency regression appeared. Current 3 Mi ordinary warm two-window p50/p95 is 0.0956/0.1180 ms (n=11). The 100 Mi commas p95 is **16.6211 ms**, not its 0.1691 ms median: retain the outlier, not an SLA assertion.

100 Mi quoted Apply→Visible head/middle/tail is **2.1228/0.4297/0.3032 ms**, one observation each, with Provisional results. The final quoted cold Full remains **292.4 ms and 420,726,616 allocated bytes**, and post-edit idle Full 241.8–395.0 ms/~420.7 MB. An earlier draft run was 438.0 ms; wall time is noisy. Idle allocation is still an important unresolved cost, although cold Full no longer replays the giant row for delivery (~841 MB before). Retained structural estimate is **92,808 B** for giant quoted and **105,480 B** for giant commas; this is not a heap-dump or process-memory claim.

Raw files: `.cache/csv-oversized-final-{100-quoted,100-commas,3-ordinary,3-quoted}.csv`, `.cache/csv-oversized-small-{before,after}.csv`; historical `.cache/csv-bounded-index-benchmark/100-{quoted,commas-before}.csv`. Warm cases include an untimed warm-up; source creation/I/O/rendering/paint are excluded. No four-RID Native AOT, actual viewport source-map integration, GUI scheduling, compositor timestamp, or perceptual smoothness follows from these managed format-session results.

## External basis

[CSV RFC 4180](https://datatracker.ietf.org/doc/html/rfc4180) explains why physical newlines are not valid seek certificates. [Apache Arrow CSV](https://arrow.apache.org/docs/cpp/csv.html) provides the production streaming/bounded-memory contrast, not this editor-specific projection algorithm. [Fast Incremental PEG Parsing (SLE 2021)](https://people.seas.harvard.edu/~chong/pubs/gpeg_sle21.pdf) motivates selective certified retention rather than memoizing every node; its grammar/benchmarks do not prove these CSV counts or AOT timing. Our actionable contribution is policy-private verified field facts that turn repeated giant-body replay into bounded output without promoting viewport-only parsing to Complete semantics.
