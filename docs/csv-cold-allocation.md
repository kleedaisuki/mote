# CSV cold Full: borrowed rope chunks instead of copied reader windows

Status: implemented and locally measured, 2026-10-01; [independent source review](reviews/csv-cold-allocation-review.md) and [differential validation](validation/csv-cold-allocation-differential.md) found no scoped blocker at source SHA-256 `FB145C77F4D7402D74BABC1F0DBAAB5F52430CEBB40F0BFC6EB26D9084416E9F`. This work changes only the private CSV reader; Native/Grid contracts and Engine APIs are unchanged. Integration and target-platform performance acceptance remain separate.

Frozen source SHA-256 is `E71D8FAB61976A25114FAE2596E383BFE878C43A194B3F675A001A8CC9B07A56`; its only difference from the measured/reviewed hash is an XML ownership-comment clarification. Executable source is unchanged, so no additional benchmark/test run is claimed or required for that comment.

## Causal diagnosis

The existing oversized-record certificate makes warm source/Grid windows bounded, but authoritative cold or post-edit `Full` must still validate all source. Its `SnapshotCursor` fetched each 8 Ki UTF-16 reader window through `TextSnapshot.GetText`. `RopeNode.Slice` allocates both a `StringBuilder` backing buffer and a final string. Thus reading 104,857,600 source units allocates about four bytes per unit even though no decoded giant cell or whole-file string is retained.

A reader-only probe on the same already-created snapshot isolates the allocation cause:

| Reader, 100 Mi UTF-16 units | Cumulative current-thread allocation | Units visited | Single observed time |
| --- | ---: | ---: | ---: |
| `GetText` in 8 Ki windows | 420,659,200 B | 104,857,600 | 53.7240 ms |
| borrowed `GetChunks` traversal | 440 B | 104,857,600 | 3.3071 ms |

This probe measures traversal/materialization, **not parsing**, and its time is one diagnostic observation rather than a distribution. Source construction was excluded; both routes had an untimed warm-up. Probe source/output are under `.temp/csv-cold-allocation/reader-probe/` and `.cache/csv-cold-allocation/reader-cause.csv`.

## Mechanism and ownership

`ScanFull` now constructs a disposable private forward reader over existing `TextSnapshot.GetChunks()`. For current Engine string-backed immutable chunks, `MemoryMarshal.TryGetString` borrows the backing string, offset and length; the lexer retains direct string indexing. Chunk boundaries have no grammatical meaning. Escaped quotes, CRLF, UTF-16 coordinates and the same CSV001–004 parser/counting logic are unchanged. A future non-string-backed immutable chunk falls back to copying **that chunk only**, not an entire record or document.

The snapshot owns its immutable rope strings for the whole call. Neither parsing nor later `Document.Apply` can mutate them; newer snapshots share only unchanged nodes. The reader borrows at most the current chunk plus the traversal stack, and its enumerator is disposed by `using` on success or exception. Certificates still retain only source spans/ordinals/error facts, not source strings. Candidate-cache publication remains after projection and the final cancellation check; cancellation cannot publish a partially validated new graph.

`GetChunks` has no absolute-offset starting API. A resumed Full scan walks to its certified prefix offset without copying or incrementing parsed-source instrumentation. Prefixes are bounded by the existing visible-prefix policy. Random sparse/source/Grid projection cursors retain their existing bounded 8 Ki range reads and certified seeks: **no enumeration from source start for every distant warm window**. This deliberately avoids an Engine API expansion or a generic parser framework.

## Negative results and acceptance discipline

The first zero-copy draft used `ReadOnlyMemory<char>.Span` on each character with a separate reader check. It cut allocation to ~61 KiB but its preliminary 100 Mi quoted cold median rose from 323.3887 to 429.5786 ms. Splitting the hot window test from the slow refill and inlining Peek/Read improved that draft to 270.4741 ms, but three alternating process pairs still showed a **16 Ki ordinary cold regression**: process medians 0.7089–0.8012 ms before versus 0.8897–1.0993 ms after (15 cold samples/process). This was not accepted merely because giant allocation improved.

The final reader borrows string backing instead of performing a memory-span operation per character. Final comparisons use equal repetitions and separate before/after binaries. A larger small-file repetition count is necessary because tiered JIT transition within a process changes these submillisecond distributions; compare matching runs, not the earlier 15-sample medians against a 31-sample result. Raw preliminary files remain under `.cache/csv-cold-allocation/{before,after,after-inline,string,pair-*}*` so the rejected designs are not erased.

## Final comparable measurements

Each table time is the **median of three process p50 values**, not a pooled-sample percentile. Parentheses give the minimum–maximum process p50; source units and allocation are per call. Each giant process has 5 cold/edit samples and 11 warm samples, each small-file process 31 samples for all routes. Raw results: `.cache/csv-cold-allocation/final-{1,2,3}-{before,after}-{quoted,commas,ordinary}.csv`.

| Workload / route | Before process-p50 median (range), ms | Final process-p50 median (range), ms | Before → final allocation, B | Final scanned source units |
| --- | ---: | ---: | ---: | ---: |
| 100 Mi quoted cold Full | 277.8548 (276.4551–405.5366) | 202.0270 (195.2301–220.6709) | 420,720,254–420,721,884 → 61,531–62,769 | 104,857,600 |
| 100 Mi quoted prefix→Full | 274.4769 (266.1907–340.0287) | 201.4253 (199.0139–211.5488) | 420,720,168 → 61,440 | 104,857,600 |
| 100 Mi commas cold Full | 57.8094 (56.8810–85.9319) | 14.9043 (14.6895–15.2987) | 420,776,496–420,777,014 → 117,772–119,011 | 104,857,681 |
| 100 Mi commas prefix→Full | 53.1137 (41.9220–76.5010) | 14.7371 (14.6110–15.1202) | 420,776,404 → 117,676–118,915 | 104,857,681 |
| 16 Ki ordinary cold Full | 0.7510 (0.7498–0.7605) | 0.7071 (0.4223–0.8416) | 170,225 → 104,890 | 16,482 |
| 16 Ki ordinary prefix→Full | 0.4305 (0.4143–0.4722) | 0.4038 (0.2747–0.4728) | 133,464 → 100,656 | 8,299 |
| 100 Mi quoted warm two windows | 0.0335 (0.0269–0.0416) | 0.0394 (0.0341–0.0423) | 35,328 → 35,344 | 0 |
| 100 Mi commas warm two windows | 0.1199 (0.1108–0.1555) | 0.1268 (0.1195–0.1318) | 319,533 → 319,549 | 65,617 |
| 16 Ki ordinary warm two windows | 0.0507 (0.0471–0.0677) | 0.0504 (0.0480–0.0633) | 77,096 → 77,176 | 214 |

The scan counts include bounded output reconstruction: the comma fixture consumes its 104,857,600 units plus 81 projected units; ordinary likewise includes delivery replay. Counts are equal before/after. Retained-index estimates remain equal (quoted 92,812 B; commas 111,884 B; ordinary 92,416 B); the improvement is transient cumulative reader allocation, not a changed index representation.

Small cold Full pairs are respectively `0.7605→0.4223`, `0.7510→0.7071`, and `0.7498→0.8416` ms. The last pair is **12.2% slower** and remains visible; the consistent +31% span-reader regression was not reproduced by the string-backed representation. Final small p95 is 0.9860–1.5944 ms versus baseline 1.3923–1.7487 ms. These noisy observations support no universal small-file speedup claim. Giant warm times overlap and retain their bounded scan behavior; the tiny cursor-size allocation increase is 16 B per cursor. The comma warm p95 remains **12.3047–12.5850 ms** despite its ~0.127 ms median, so this is not a tail-latency SLA.

Post-edit idle quoted Full has mixed CPU results: head process medians 428.1245–472.2532→440.8215–472.2373 ms; middle 282.6142–328.1929→195.7311–318.7861 ms; tail 275.1229–332.5008→190.0610–349.8322 ms. Median-of-process medians is slightly worse for these routes; ranges overlap and at least one pair improves for every route. **No blanket idle CPU improvement or absence of latency regression is established.** The deterministic benefit is their allocation drop from ~420.7 MB to 94,584 B (head), 61,440 B (middle), 61,360 B (tail). Cold Full gains are consistent across the three giant pairs. Post-edit comma Full process medians fall from 40–80 ms to 11–16 ms with ~117–119 KiB allocation.

Final quoted cold process RSS was 991,854,592–995,950,592 B versus baseline 699,518,976–953,495,552 B at this benchmark's sampling points. The fixture creates many documents across routes, does not pin GC collection timing, and already owns ~200 MiB UTF-16 source plus rope copies; **RSS improvement is not demonstrated** by lower parser cumulative allocation. The product optimization is worthwhile for removing hundreds of megabytes of unnecessary GC work and consistently lowering giant cold validation time, not as proof of a lower peak process footprint or improved UI paint.

## Verification

- Existing CSV-focused Release suite: 219/219 on the initial zero-copy draft before the performance-directed reader-representation revision. This is compatibility context, not a claim that this exact final hash reran all 219 cases.
- Owner final `CsvColdAllocationTests`: 8/8, Release `-warnaserror`, zero skipped. Covers 1 Mi/8 Mi quoted allocation guards, zero-scanned warm disjoint windows, fresh/resumed/fragmented chunk seams with CSV001–004 and UTF-16 source anchors, and deterministic pre-canceled new-version Full preserving old cache truth. Evidence: `.cache/csv-cold-allocation/test/cold-allocation-final.trx`.
- Independent reviewer final reader-representation seam/resume probe: 72/72, exact tree/decoded values/tokens/diagnostics against `CsvPolicy`.
- Independent validator: all 11 unique cases have successful final-hash evidence across initial and failure-directed harness repairs, including >100 Mi units sparse mixed rows below a 64 MiB cumulative Full allocation guard and a final six-case explicitly incomplete prefix→Full run. No parser defect was found; illegal surrogate-bisecting edits and one handwritten offset error were corrected in the harness, not in production. See the linked validation record for exact logs and limits.
- Final Formats Release `-warnaserror` build: zero warnings/errors. No controlled mid-scan cancellation, Native AOT/macOS/ARM timing, UI paint, or peak-memory claim is inferred from these results.

## Reproduction and evidence boundaries

Environment: Windows 10.0.26200, win-x64, Intel Core i9-12900H (14 cores/20 logical processors), .NET SDK 10.0.400, managed Release. Host load/CPU affinity/frequency were not controlled; three alternating before/after processes characterize uncertainty rather than establish an SLA. Baseline is commit `526cc9b8246c49be2c12035d0ac026895d21685c` archived under `.temp/csv-cold-allocation/`; its CSV session equals the original source after line-ending normalization. The final binary directory is `.temp/csv-cold-allocation/after/`.

```powershell
dotnet build tests/CsvSparseBenchmark/CsvSparseBenchmark.csproj -c Release -warnaserror
# Each command repeated in three alternating baseline/final processes.
dotnet run --project tests/CsvSparseBenchmark/CsvSparseBenchmark.csproj -c Release -- 100 11 quoted 5
dotnet run --project tests/CsvSparseBenchmark/CsvSparseBenchmark.csproj -c Release -- 100 11 commas 5
dotnet run --project tests/CsvSparseBenchmark/CsvSparseBenchmark.csproj -c Release -- 0 31 ordinary 31
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --filter FullyQualifiedName~CsvColdAllocationTests -warnaserror
```

Cold means a fresh **format cache**, not a fresh machine/OS cache, first executable launch or Native AOT first paint. Fixture creation, document I/O/decode/rope construction, rendering and compositor work are excluded. The benchmark also measures cold Visible, prefix→Full, warm one/two windows and head/middle/tail Apply→Visible→idle Full. Its allocation measure is cumulative current-thread bytes, not retained heap, peak RSS or total-process allocation. ScannedSourceUnits counts parsing, not rope traversal or chunk-memory access. Whole-file Full remains O(file length), and version-obsolete Full work still needs cancellation/scheduling in the Native controller.

## External context

[Apache Arrow's production CSV reader](https://arrow.apache.org/docs/cpp/csv.html) supplies the streaming-reader contrast; it does not prove this editor's source provenance or bounded certificates. [VS Code's text-buffer reimplementation](https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation) emphasizes that real text shape, buffer representation and implementation boundaries require measurements rather than abstract complexity alone. [Fast Incremental PEG Parsing (SLE 2021)](https://people.seas.harvard.edu/~chong/pubs/gpeg_sle21.pdf) provides the selective incremental-retention research context; no PEG framework or academic benchmark is transplanted here. The specific result is removing avoidable reader copies while preserving the existing authoritative lexer and warm certified seeking.
