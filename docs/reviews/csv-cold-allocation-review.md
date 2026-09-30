# CSV cold Full borrowed-chunk cursor review

## Disposition and scope

2026-10-01: **No material production blocker found in the revised frozen source.** The initial memory-span hot-path draft was rejected after a reproducible ~31% 16 Ki ordinary cold Full regression. The revised string-backed reader was reviewed separately; its finite process-pair evidence no longer shows that consistent regression, but remains noisy. Reviewed `src/Mote.Formats/CsvIncrementalSession.cs` against the bounded sparse/giant certificate and Grid contracts, `TextSnapshot.GetChunks`, `RopeNode.Chunks`, and `tests/Mote.Tests/CsvColdAllocationTests.cs`. No production or checked-in test edits were made by the reviewer.

Reviewed SHA-256:

- CSV session: `FB145C77F4D7402D74BABC1F0DBAAB5F52430CEBB40F0BFC6EB26D9084416E9F`.
- New tests: `FA787C0E088BDA8C65AF01F2899BBBCBABF7863FB3E7567F4AB2755D4D72F471`.
- Base HEAD: `526cc9b8246c49be2c12035d0ac026895d21685c`.

This is a managed source/contract review and a narrow independent semantic probe, not Native AOT, GUI, peak-heap, or cross-platform acceptance. The already completed owner 8/8 new tests and 219/219 CSV suite were not repeated.

## Ownership, lifetime, and transaction

`ScanFull` alone selects `streamChunks: true` and declares the cursor with `using` (lines 287-288). Both successful return and exceptions/cancellation dispose the traversal. Other cursors are still nonstreaming: their optional enumerator is null, so omitting disposal there does not retain traversal resources.

`TextSnapshot.GetChunks` yields `ReadOnlyMemory<char>` backed by immutable leaf strings; `RopeNode.Chunks` traverses the persistent tree with a stack. The cursor retains its immutable snapshot throughout enumeration, and the current memory independently references its string. Document edits create new snapshots and cannot overwrite these borrowed strings. Neither cursor nor borrowed text enters `CacheState`: committed summaries retain numeric spans/counts, not this enumerator or payload memory. This is a genuine ownership-preserving zero-copy read path, not a borrow of recyclable pooled storage.

`BuildCandidate` creates prospective summaries locally. `AnalyzeWindowsCore` still projects the candidate, checks cancellation, and only then assigns `_cache` and `_lastScannedSourceUnits` (lines 98-100). The change does not add publication during enumeration. The pre-canceled test verifies an old giant certificate survives and a later successful Full publishes the updated semantics. Controlled mid-scan cancellation remains unproven by that test; this review does not upgrade the earlier timer-race evidence into such a claim.

## Boundaries, seeks, and accounting

The revised reader obtains immutable string backing through `MemoryMarshal.TryGetString`, retaining both the memory offset and length. Direct character indexing includes that offset; comma vector searches use the same offset and are bounded by the memory length, not the backing string length. Current Engine chunks are whole leaf strings, but the calculations also respect string-backed submemory. The fallback copies only a non-string-backed chunk if a future Engine changes representation.

The streaming window starts at absolute zero. On a resumed Full, `LoadWindow` advances through leaves until the requested start falls inside a leaf; it preserves the absolute chunk base rather than resetting it to the requested position. Exact leaf endpoints advance to the next leaf. EOF is returned by `Peek` before trying to advance an exhausted iterator. Quoted escape lookahead and CRLF lookahead therefore cross arbitrary leaf seams via the same absolute position used before this change.

`SkipCommas` restricts a vector search to the current memory, at most 4,096 units, and the caller's source limit. Its position/count update remains identical, and crossing a memory boundary simply loads another immutable leaf. Borrowing/fetching earlier leaves does not increment `ScanWork`; the instrumentation remains consumed parser source, not fetched bytes or traversal nodes.

Full parsing has no certificate `Seek` calls: it passes `capture: false`, a builder, and no certificate/Grid. All random sparse/certificate/Grid projection cursors retain the existing bounded `GetText` path. A distant viewport therefore does not acquire a new O(prefix-leaf-count) walk from file start. Resumed Full does perform a prefix traversal to reach its start, but its reused Visible prefix is bounded by the existing 64 Ki source budget, and authoritative Full already necessarily visits the remaining input. Highly fragmented ropes can increase traversal work; no arbitrary chunk-size guarantee is assumed.

## Independent seam probe

Artifacts: `.temp/csv-cold-review/Probe.csproj` and `Program.cs`.

Command:

```powershell
dotnet run --project .temp/csv-cold-review/Probe.csproj -c Release
```

Result: **72/72 passed** on the original memory-span draft, and **72/72 passed** on the revised string-backed source. The second run was directed at the representation change after the demonstrated performance failure, not a repeat of completed unrelated suites. Six prefix lengths (8,191; 8,192; 16,382; 16,383; 16,384; 32,767), six tail grammars, and fresh/resumed Full were tested. Every tail character was individually replaced with the same character, deliberately creating small rope leaves around commas, CR/LF/CRLF, escaped quotes, invalid quote suffixes, unquoted quotes, and unterminated quotes. Exact diagnostics, semantic tokens, global counts, and recursively flattened node spans/values matched fresh `CsvPolicy` analysis. This extends the checked-in fixed seam cases without rerunning unrelated completed suites.

## Measurement interpretation

The benchmark `Measure` encloses only the synchronous action in current-thread cumulative allocation and elapsed-time sampling; source/Document construction and later process/cache-statistics sampling are outside. This is appropriate for isolating the format-session cost. Cold means a fresh session, not a cold process, application startup, or disk read. The owner reader-only probe is useful causal evidence for removal of `GetText` copies; it is not an independent reviewer rerun.

One available alternating process pair reports 100 Mi-unit quoted cold Full allocation **420,720,254 B -> 61,531 B**, while medians are **282.1349 ms -> 270.0183 ms** (five samples each). Removing copied windows plausibly explains the allocation difference. RSS in those same files is higher after (945,061,888 B -> 1,014,636,544 B), so do not claim process-memory reduction from allocation counters. The files are `.cache/csv-cold-allocation/pair-1-{before,after}-quoted.csv`.

No universal latency improvement follows from these finite, same-machine JIT samples. Retain process-pair distributions, first-sample effects, small-file results, and the rejected slow-loader draft when reporting. Full validation remains O(file length), and certificate/checkpoint metadata can grow with input structure. In particular, the new test summary saying allocation is "independent of giant quoted payload length" should be read narrowly as avoiding payload-sized text copies: the two tested sizes do not establish constant total allocation for arbitrarily large files. This wording is an optional precision improvement, not a correctness blocker.


### Revised string-backed performance evidence

The three completed final ordinary pairs use matching 31 fresh-session cold samples per process. Per-process cold Full medians (before -> after) are **0.7605 -> 0.4223 ms**, **0.7510 -> 0.7071 ms**, and **0.7498 -> 0.8416 ms**. After allocation is consistently **104,890 B**, before **170,225 B**. The last process pair is **12.2% slower** and must not be suppressed; the median of the three process medians decreases ~5.8%. This small, noisy evidence does not establish either a universal latency speedup or a persistent small-file regression. It does not repeat the initially consistent +31% memory-span result and supports keeping direct string indexing over the rejected draft.

Files: `.cache/csv-cold-allocation/final-{1,2,3}-{before,after}-ordinary.csv`. At review completion, final giant pairs were still being collected: the first complete quoted pair is 405.5366 -> 195.2301 ms with 420,721,884 -> 62,769 B allocated (n=5 each). Do not promote this first process pair into the final aggregate; the owner must retain all completed pairs in the implementation report. Earlier RSS-negative evidence above remains relevant to the interpretation regardless of latency.
