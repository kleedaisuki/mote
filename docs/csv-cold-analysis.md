# CSV cold-open analysis: bounded first result, exact later validation

Status: implemented and measured locally on Windows, 2026-09-29. This note describes the CSV format session, **not** measured GUI paint or released Native AOT behavior. See `src/Mote.Formats/CsvIncrementalSession.cs`, `tests/Mote.Tests/CsvColdAnalysisTests.cs`, and the reproducible probe in `tests/CsvColdBenchmark/`.

## Decision and correctness boundary

The previous session built a complete logical-record index before answering even a `Visible` request. On a 100 MiB streamed CSV, a near-top edit was only a few milliseconds after indexing, but the first index took seconds. It was incorrect to call that an instant first analysis. The new session stages work:

1. Cold `Visible` scans a prefix of at most 64 Ki UTF-16 units, with a minimum target of 8 Ki and a small token-boundary overshoot. It commits only *whole logical records*. If the requested viewport is covered, it returns `CoveredRegion`; otherwise it returns `Provisional`, with null total diagnostic count. A single quoted record longer than the budget can therefore return an empty provisional projection rather than an incorrect row.
2. A same-version `Full` request resumes at the committed record boundary and scans the rest of the snapshot. It returns `Complete` and an exact global diagnostic count. The work remains synchronous **within the session call**; the Native composition root must schedule it off the input/presentation path, serialize calls for that session, cancel obsolete work, and publish only matching document versions. A CSV-only change cannot establish UI responsiveness by itself.
3. After completion, ordinary contiguous edits use the shared-segment record index and lexical delimiter convergence. A change to a partial index safely rebuilds a bounded visible prefix; a later `Full` request completes it. No full source string is retained or created by the session.

The scanner indexes **logical records**, not physical lines, because quoted fields can contain CRLF. This is required by [RFC 4180](https://datatracker.ietf.org/doc/html/rfc4180). Initial `Visible` analysis of a distant viewport cannot safely assert that an arbitrary physical line begins a record: a quote opened earlier may still be active. Returning `Provisional` is preferable to inventing local validity. The same source-position discipline is familiar from [Tree-sitter's incremental edit API](https://tree-sitter.github.io/tree-sitter/using-parsers/3-advanced-parsing.html), although this CSV scanner is simpler and does not use Tree-sitter. VS Code's [text-buffer reimplementation](https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation) is production evidence that large-file editing requires explicit attention to buffer ownership and size-specific benchmarks; it does not prove mote's GUI latency.

`Full` completion scans all records and maintains exact width/error counts, but for large inputs its returned semantic tree is a bounded viewport projection capped at 4,096 rows, not a giant second copy of the CSV—even if a caller passes the entire document as `VisibleRange`. Small `Full` requests retain full legacy projection for compatibility and differential testing. A canceled scan commits neither a new version nor a false `Complete` result.

## Reproducible local probe

From the repository root:

```powershell
dotnet run --project tests/CsvColdBenchmark/CsvColdBenchmark.csproj -v:q -- 1
dotnet run --project tests/CsvColdBenchmark/CsvColdBenchmark.csproj -v:q -- 10
dotnet run --project tests/CsvColdBenchmark/CsvColdBenchmark.csproj -v:q -- 100
dotnet run --project tests/CsvColdBenchmark/CsvColdBenchmark.csproj -v:q -- 100 utf16
```

Each process generates a separate on-disk fixture under root `.temp/CsvColdBenchmark/`, then measures `Document.OpenAsync`, first `Visible`, resumed `Full`, and one near-top edit. The pattern contains a 500-character field, a quoted multiline field, one uneven-width record, an emoji, and CRLF record delimiters; UTF-16LE includes a BOM. The probe verifies that the exact global row-width diagnostic count equals the number of patterns. Reported heap is `GC.GetTotalMemory(true)` after each phase; this is **retained managed heap**, not peak RSS or cumulative allocations. The following were one local Windows `.NET 10` Debug run per case, not statistical p95 and not Native AOT:

| Input encoding / target disk size | Open + decode / rope | First visible result | Resume full validation | Near-top edit | Retained heap open → full |
| --- | ---: | ---: | ---: | ---: | ---: |
| UTF-8 / 1 MiB | 22.0 ms | 9.19 ms | 51.5 ms | 1.89 ms | 2.5 → 2.7 MiB |
| UTF-8 / 10 MiB | 73.5 ms | 9.51 ms | 494.5 ms | 2.52 ms | 20.6 → 22.4 MiB |
| UTF-8 / 100 MiB | 664.5 ms | 10.03 ms | 7,234.2 ms | 4.36 ms | 200.7 → 218.7 MiB |
| UTF-16LE / 100 MiB | 469.6 ms | 9.57 ms | 2,448.1 ms | 3.87 ms | 101.0 → 110.1 MiB |

The UTF-16LE file has roughly half as many UTF-16 code units as the 100 MiB UTF-8 file, so the full-scan times are **not** an encoding-only comparison. The key separation is that file I/O/decode/rope construction and CSV parser CPU are independently visible. For this mixed UTF-8 fixture, the 100 MiB full parser still costs about 7.2 seconds; a repeat under concurrent local load took **11.9 seconds**, with retained heap 218.7 MiB and process working set 268.7 MiB after Full. These are single-run observations, not a stable performance distribution. Staging moves work after a valid first viewport result rather than making it disappear. Parallel record parsing was not adopted: quoted multiline fields make safe partition points nontrivial, and the measured first result is already bounded. Page summaries are likewise unnecessary until profiling a concrete bottleneck in repeated full scans.

### Adversarial single-record result (negative evidence)

Run `dotnet run --project tests/CsvColdBenchmark/CsvColdBenchmark.csproj -v:q -- 100 giant-row` to generate a 100 MiB UTF-8 CSV with **one** record made entirely of commas. This isolates the no-record-boundary case. A local Windows Debug run yielded: open 708.7 ms; first `Visible` 6.70 ms with honest `Provisional` and zero coverage; `Full` 26,937.7 ms; near-top edit after Full 23,469.7 ms. Retained heap stayed near 201.5 MiB and process working set rose from 234.7 to 247.2 MiB at Full. Thus the projection no longer explodes to one node per cell, but CPU time remains unacceptable for a huge single row because record-level convergence cannot occur inside it and the scanner currently advances through each empty field separately. A bounded span-batched delimiter count is a plausible measured next optimization; this was **not** included in the cold-staging change or assumed to work before a benchmark. The 1 MiB version produced only visible cell nodes despite 500,001 logical cells; its Full took 299.9 ms and near-top edit 389.9 ms in a separate run.

Cancellation was probed on the 100 MiB mixed UTF-8 file: `CancelAfter(100 ms)` interrupted resumed Full in 106.6 ms. A same-version retry completed with the exact 194,903 global warnings; the prior visible result remained `CoveredRegion`. This demonstrates parser cooperation, not a GUI cancellation guarantee.

## Validation and remaining gates

- `tests/Mote.Tests/CsvColdAnalysisTests.cs`: cold covered result, giant first-record and distant-viewport provisional results, edit after a partial index, canceled full retry, bounded giant projection (including a million-character one-row CSV), and UTF-16/CRLF full-oracle equivalence. Seven tests passed in an isolated CSV test project while unrelated Native test source was being edited concurrently.
- Existing full-projection oracle probe under `.temp/CsvProbe/` matched the legacy `CsvPolicy.Analyze(string)` after 10,000 seeded random edit transitions. This verifies small-document compatibility, including malformed intermediate quoting and CR/LF changes, but is not a proof for all inputs.
- The Native idle scheduler now admits CSV `Full` work with a conservative memory advisory based on source length and physical-line count. The previously failing 2.2 MiB controller case now promotes its offscreen ragged-row result to exact `Complete`; the affected Native filter passed 105/105. A fresh Release solution build had zero warnings/errors, and all 367 tests passed (344 core, 14 theme, 9 configuration).
- Actual Windows/macOS Native AOT UI still must demonstrate that `Visible` is published before idle `Full`, edits are not blocked by a running full scan, cancellation/version checks prevent stale results, and provisional states are labeled rather than displayed as “0 problems.” Those measurements must include input-to-paint and memory/RSS, not only parser wall time. The memory estimate is an admission advisory, not an OOM guarantee.
