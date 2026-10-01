# JSON root-array pages: reproducible managed performance investigation

Investigation date: 2026-10-01. **Managed parser/session investigation completed.**
This document concerns the parser/session work contract in
[the semantic-page design](json-array-semantic-pages.md), not Native AOT GUI input
latency or release promotion. It deliberately preserves failed candidates.

## Workload and environment

- Windows 10.0.26200 x64; Intel Core i9-12900H, 14 cores / 20 logical processors;
  .NET SDK **10.0.400**, CoreCLR **10.0.11**, Release. No CPU affinity, frequency
  lock, filesystem-cache eviction, or Native AOT was used. Power/thermal/scheduling
  noise is not eliminated; observed extrema and paired deltas are reported.
- The frozen baseline is tracked Engine/Formats source from commit
  `91a77f886058802be179185b057919025c315a37`, archived into
  `.temp/JsonArrayPages/baseline`. Baseline parser SHA-256:
  `2A4F1001704462406FD7FDFEB591F55F6CB5467B35761C718CE430EBC8BDF47F`.
  Candidate builds use the new source; run environment files record source and
  assembly identities because the candidate is initially an uncommitted worktree.
- Nine exact-byte-sized file-backed ASCII UTF-8 corpora: **1, 10, 100 MiB** for
  ordinary LF, ordinary CRLF, and compact primitive arrays with no line breaks.
  UTF-8 bytes and UTF-16 units coincide for these fixtures, including literal ASCII
  JSON escapes; this is not a Unicode byte/unit ratio experiment.
- An ordinary record is exactly **512 UTF-16 units** before its comma/newline:
  nested object/array, numbers/booleans/null, escaped quote/backslash/delimiters and
  an escaped surrogate pair, with `"a"` and `"\u0061"` in the **same nested object**.
  One exact duplicate diagnostic per record is expected. The compact corpus is
  repeated `0` elements. Final string padding makes the array valid and its size
  exact; no arbitrary byte truncation is used.
- Fixture seed label **64517** is a fixed literal, not an assertion of randomized
  generation. Dispersed edits use deterministic stride **104729** modulo record
  count. Each metadata file records count, edit geometry and complete source hash.

| 100 MiB corpus | Ordinary records / primitive values before final padding value | Source SHA-256 |
| --- | ---: | --- |
| Ordinary LF | 204,003 | `D4FF1A6FB4AFE5F2CEEA0A772D9A3A8B885B84380B6F3A295D488239D5D96CC9` |
| Ordinary CRLF | 203,606 | `FE69FA81ABCF65845AF52D41F704E8D5BEFAAC2E4080C985100EEB6D047FD205` |
| Compact primitives | 52,428,798 | `686B5BAD7387992E3CF99ABE030560C0DCC8F0A46F369B1D55FE44E409C6EE77` |

## Method and boundaries

Reusable code: [benchmark executable](../benchmarks/JsonArrayPages/Program.cs),
[isolated-process driver](../benchmarks/JsonArrayPages/Run.ps1),
[summary calculation](../benchmarks/JsonArrayPages/Summarize.ps1).

Cold Full is the **first Analyze in a fresh process**, without a preceding Visible
pass. Open, session creation, forced GC, reflection, fixture hashing and report
serialization stay outside the Stopwatch interval. Consequently the number is
first-analysis latency with JIT effects, not process startup or cold disk I/O.
The driver alternates baseline/candidate order across five paired fresh processes
per corpus, reusing identical source files. It records current-thread allocated
bytes and an approximate all-thread allocation delta; the latter's sampling can
lag and is not the primary allocation comparison.

Warm acceptance runs one Full followed by **200** equal-length edits rotating
near the start/middle/end and **5,000** dispersed sequential edits, with a bounded
2,048-unit projection near the edit. Ordinary edits change only string padding;
compact edits change a primitive digit, excluding exact page seams. Boundary,
malformed typing, giant owners and semantic-count-changing edits belong to the
separate correctness suite, not this ordinary local-edit latency workload.
Every measured analysis checks Complete and the exact global duplicate total;
candidate warm turns also check actual visits <=524,288, zero dirty pages, and
certificate version equal to the current snapshot version. No latency outlier is
excluded. p95 uses nearest rank, including the first warm edit's additional JIT.

Per-turn work/page/version instrumentation is reflected **after** the timed call.
The final specialization deliberately leaves unbounded Full source visits
**unmeasured (`null`)**, not zero; every budgeted interactive visit remains counted.
Benchmark recording allocations can still affect subsequent GC behavior even
though they are not charged to Analyze's allocation/time interval.

Certificate retention uses the actual private immutable certificate and its page
array: after warming a MemberwiseClone delegate and Array.Clone, measure their
equal-shaped replacement allocations with `GC.GetAllocatedBytesForCurrentThread`.
`Unsafe.SizeOf<T>` obtains the runtime's managed page stride via reflection, not
Marshal's different interop bool layout. This measures certificate+array object
bytes, **not** whole session retention, source snapshots, fixed WeakReference,
engine Undo history, semantic projection or benchmark result storage. Full and
post-edit forced-GC live bytes/working-set/peak are reported separately and must
not be mislabeled certificate-only memory.

The final harness explicitly executes `await Task.Yield()` after Open and before
initial GC/timing, allowing the asynchronous producer callback to unwind. This
is an observed scheduling control, not a guarantee that a hop universally ends
every producer stack lifetime. Earlier
inline-continuation heap measurements were **not steady-state**: Open's hoisted
decode-chunk list could remain rooted during the entire edit loop. Their latency
and exact-count/work observations remain finite evidence under that recorded
execution, but their retained heap must not be interpreted as a page/history leak.

## Failed candidate 1: per-visit cancellation in the hot cursor

The initial candidate completed five pairs for the ordinary corpora before the
run was stopped for optimization; compact results are incomplete and are not
used as a complete nine-corpus experiment. For 100 MiB:

| Corpus | Baseline Full median [min, max], ms | Initial candidate, ms | Ratio of medians |
| --- | --- | --- | ---: |
| Ordinary LF | 564.79 [546.03, 569.61] | 770.14 [756.50, 817.75] | +36.36% |
| Ordinary CRLF | 577.09 [563.93, 592.56] | 744.80 [713.33, 765.54] | +29.06% |

This disjoint observed distribution is a material cold regression, not merely the
first 929 ms smoke result. Hot `SourceWork.Visit` cancellation/control overhead
was removed/inlined while preserving existing approximately 4,096-unit grammar
and key-loop cancellation points. Artifact directory:
`.cache/json-array-pages/first-candidate-pairs`.

## Failed candidate 2: inlined counting is still too expensive

All nine corpora completed five fresh-process pairs with candidate parser source
SHA-256 `DC291589ECF7F16179EF12AFBC95F45174A23B1C612267B9F15DE787A6051080`.
Artifacts: `.cache/json-array-pages/inlined-candidate-pairs`.
This precedes the cancellation-threshold correction and final unbounded-work
specialization; it is **not** evidence for the final source.

| Corpus / MiB | Baseline Full median [min, max], ms | Inlined candidate, ms | Median paired change |
| --- | --- | --- | ---: |
| Ordinary CRLF / 1 | 62.75 [58.61, 63.84] | 63.32 [59.22, 66.13] | +4.50% |
| Ordinary CRLF / 10 | 291.42 [282.19, 324.00] | 302.24 [295.79, 313.31] | +5.06% |
| Ordinary CRLF / 100 | 572.26 [555.28, 589.54] | 610.67 [589.41, 615.10] | +5.45% |
| Ordinary LF / 1 | 62.56 [59.31, 63.66] | 63.41 [61.61, 66.96] | +6.92% |
| Ordinary LF / 10 | 290.60 [280.51, 295.43] | 308.51 [291.94, 318.69] | +5.36% |
| Ordinary LF / 100 | 548.62 [538.40, 684.63] | 611.54 [578.33, 729.42] | +7.46% |
| Primitives / 1 | 289.72 [256.96, 313.36] | 304.24 [271.72, 312.63] | +4.37% |
| Primitives / 10 | 306.51 [295.65, 312.71] | 321.38 [314.70, 324.94] | +5.85% |
| Primitives / 100 | 1,525.54 [1,415.57, 1,696.76] | 1,610.64 [1,486.29, 1,838.96] | +7.58% |

Five pairs do not justify precise statistical confidence claims, but consistently
positive deltas across every corpus and seven median paired deltas above the
proposed 5% threshold justify the next discriminator. (A ratio of independent
medians differs from a median of pairwise ratios; the final column uses the latter.)
The final design charges actual source visits only on **budgeted interactive**
paths, retaining cold Full's established cancellable streaming path without a
per-character shared counter. No unavailable Full count is fabricated.

## Candidate 3: cold time recovered, allocation regression rejected

The specialization source `09C4E8F3D3337941DD440FBC8E9B9BAE6BD1A4C659F970B5D9DEA05BE5486238`
completed nine-corpus five-process pairs under
`.cache/json-array-pages/specialized-final-pairs` and 5,200 warm turns per 100 MiB
corpus under `specialized-final-warm`. At 100 MiB, Full median time was LF
578.88 -> 581.47 ms, CRLF 582.70 -> 590.85 ms, primitives 1,416.15 -> 1,381.00 ms.
However ordinary cold allocation rose **6.31% LF / 6.29% CRLF**: approximately
282.69 -> 300.54 MiB and 282.20 -> 299.95 MiB. This was a material regression, not
hidden by the successful time target. New per-object KeyTable and per-duplicate
KeyReader fields inflated millions of ephemeral owners/readers. Passing budget
state call-locally restores the previous object layouts while retaining exact
source charging and cancellation. The failed allocation source is not the final
production source.

## Final matching parser: cold Full

Production parser commit **`d9aadd34c36ac896440f7147deee168a44e9f8a1`**; parser SHA-256
`726DEC289A54C2B58338A7049273475BC763FFD270B9B54C555BD4CE435473EC`.
Certificate source SHA-256
`AB45C33FCF5E55D081BAC534A7B499B23498677E7FDDB85796A40C40F6A1F0D4`;
range-backed TextSnapshot SHA-256
`0DF08C43EC6FE23E788FDED941BB00F7617FB35A8EEFF9608EE92015BD595CE3`.
The nine-corpus run began before the commit with the same exact source; its
run-start HEAD is not a claim that the worktree was already committed.

Five fresh-process pairs per corpus, **90** processes total, under
`.cache/json-array-pages/field-free-final-pairs`. The primary change column remains
the **median paired ratio**, not a switch to an independent-median criterion.

| Corpus / MiB | Baseline Full median [min, max], ms | Final candidate, ms | Median paired change | Allocation median, baseline -> candidate, MiB |
| --- | --- | --- | ---: | --- |
| Ordinary CRLF / 1 | 62.25 [60.77, 65.41] | 59.69 [57.76, 62.23] | -1.79% | 18.140 -> 18.140 |
| Ordinary CRLF / 10 | 289.18 [281.36, 292.36] | 265.82 [259.95, 277.65] | -8.33% | 28.931 -> 28.944 |
| Ordinary CRLF / 100 | 577.92 [552.90, 590.71] | 593.21 [550.33, 728.64] | **+7.29%** | 280.934 -> 281.510 |
| Ordinary LF / 1 | 63.06 [60.60, 66.13] | 59.53 [58.33, 62.74] | -5.12% | 18.175 -> 18.175 |
| Ordinary LF / 10 | 308.12 [296.79, 328.39] | 269.49 [261.13, 278.92] | -9.63% | 28.989 -> 29.001 |
| Ordinary LF / 100 | 583.23 [567.48, 590.91] | 582.57 [571.44, 599.99] | 0.00% | 282.328 -> 281.617 |
| Primitives / 1 | 300.51 [254.61, 320.80] | 285.43 [271.83, 292.47] | -2.68% | 120.001 -> 120.002 |
| Primitives / 10 | 310.51 [308.26, 315.25] | 302.00 [296.09, 304.97] | -4.20% | 0.236 -> 0.249 |
| Primitives / 100 | 1,409.11 [1,368.68, 1,426.80] | 1,372.93 [1,358.45, 1,377.14] | -2.27% | 0.236 -> 0.345 |

The CRLF result required another discriminator rather than discarding its 728.64
ms / **+23.35% paired tail**. Ten additional focused fresh-process pairs under
`field-free-crlf-10pairs` used the **same frozen binaries**: baseline 574.22
[563.17, 586.13] ms, candidate 579.62 [562.29, 595.41] ms; paired median **+0.406%**,
range [-2.82%, +4.51%]. Combining all **15** pairs without excluding the first
tail gives paired median **+0.609%**, range [-2.88%, +23.35%]. The earlier material
hot-counter slowdown is not sustained in the final focused data; the tail remains
reported, not explained away as a proven scheduling cause.

The earlier specialized 1 MiB primitive paired +7% was also investigated, not
silently dismissed. Ten focused pairs on final field-free source under
`field-free-primitive1-10pairs`: baseline 289.18 [273.63, 318.79] -> candidate
282.81 [268.59, 303.75] ms, median paired **-3.120%**, with the +11.01% paired tail
retained. This does not reproduce a sustained small-file regression. Five/ten
pairs still do not establish precise population confidence or universal speedups.

Final ordinary 100 MiB allocation is **296,042,504 -> 295,297,168 B LF (-0.25%)**,
**294,580,568 -> 295,184,792 B CRLF (+0.21%)**. Compact primitives increase
**247,320 -> 361,640 B (+46.22%, absolute +114,320 B / 111.6 KiB)**. This is an
explicit, accepted one-time source-sized page-building trade-off, not universal
allocation nonregression: it retains only 1,600 summaries rather than 52 million
entries, and enables bounded interactive reuse; cold time does not regress in
that workload. Cold peak working-set medians: CRLF **259.29 -> 259.27 MiB**,
LF **258.04 -> 259.41 MiB**, primitives **237.95 -> 239.06 MiB**.

Final cold benchmark source SHA-256 (before await-unwind control):
`61E6D6CD68A7A2C0D0703E10CB88C692842EB483A9A6F0C8D38BCA19DC09C96D`.
Formats assemblies baseline
`7572BD91AD75CD5D1BDA805DD540D0323B4AE7F143C9DAA7EF184BD9EE7EA897`, candidate
`43270757ABC6CCC6945F5A7F02B2C6311A438640FFB76D9765856EAE9B598F43`.
Cold runs measure first Analyze; the later await hop changes benchmark binary
identity but neither production parser source nor the meaning of these frozen
cold measurements. Do not label both harness versions the same binary.

## Final warm acceptance and steady-state retention control

Frozen Engine/Formats source from `d9aadd3` was rebuilt with the explicit await
hop, **without** the later Engine producer-lifetime fix, to isolate the stack
lifetime discriminator. Final benchmark Program SHA-256:
`1BF0C0214AA3A1670EB2B2A3643A160907AFAE0A36909F87B580C9F8061082F0`.
Artifacts: `.cache/json-array-pages/yield-control-warm`.
All **15,600** edit turns (3 x [200 + 5,000]) return Complete with exact diagnostic
total, current certificate version, zero dirty pages and actual visits <=512 Ki.

| Corpus / phase | p50, ms | p95, ms | Max, ms | Max actual visits | Median analysis allocation, B |
| --- | ---: | ---: | ---: | ---: | ---: |
| CRLF / 200 local | 0.340 | 0.548 | 8.433 | 84,081 | 279,000 |
| CRLF / 5,000 dispersed | 0.335 | 0.595 | 13.245 | 165,872 | 279,000 |
| LF / 200 local | 0.328 | 0.554 | 8.534 | 83,953 | 279,112 |
| LF / 5,000 dispersed | 0.345 | 0.631 | 12.733 | 165,616 | 279,112 |
| Primitives / 200 local | 1.070 | 2.010 | 10.219 | 524,288 | 328,328 |
| Primitives / 5,000 dispersed | 1.397 | 2.251 | 10.964 | 524,288 | 328,240 |

The managed **p95 <=10 ms** ordinary target is met on this declared host; maxima
above 10 ms remain visible. Near a primitive projection boundary some turns reach
the hard budget exactly and omit excess display work while the successfully
repaired global certificate still proves the exact total. This is not a claim
that every requested display descendant was materialized. No Native AOT or
input-to-draw p95 follows from this parser microbenchmark, and no warm-baseline
latency ratio is asserted from a cold-versus-warm comparison.

| 100 MiB corpus | Pages | Managed stride, B | Measured certificate+array graph, B | Post-edit forced-GC live, MiB | Whole-workload live delta after Full, MiB | Process peak, MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| CRLF | 1,591 | 20 | 31,896 | 220.71 | 19.44 | 416.87 |
| LF | 1,594 | 20 | 31,952 | 221.82 | 20.55 | 416.80 |
| Primitives | 1,600 | 20 | 32,072 | 221.72 | 20.35 | 402.05 |

The measured certificate graph is far below the proposed **2 MiB** admission
target and remains constant after the 5,200 equal-length edits. The whole-workload
live delta includes engine bounded Undo, shared source chunks, the current
projection and retained benchmark measurements; it is not attributed to JSON
metadata. LF reachable source/history graph: **512** history entries, **45,881**
unique rope nodes, **11,915** unique strings and **113,241,403** source UTF-16 units.
This is bounded in this finite experiment, not a universal memory proof.

For comparison, the original inline engine-only control (no Analyze or per-turn
reports) grew 211,308,024 -> 377,928,296 B after GC, while the parser-inclusive
inline LF experiment grew by 169,362,232 B. The await-unwind LF control instead
grows **211,043,880 -> 232,591,000 B after Full/edits (+21,547,120 B)**. Source/history
graph identities are unchanged. These results expose the asynchronous producer
stack lifetime, not a JSON-certificate or unbounded-history leak. The
[dedicated Engine heap investigation](engine-retained-memory.md) owns causal
calibration: clearing only the decode list did not resolve all inline stack
roots, and **no steady-state lifetime leak or supported production fix is
established** by this experiment. This benchmark does not modify Engine to force
the desired number.

Await-control binary identities (all three reports agree): Formats
`5C81324F4A688FE6B5C0BB7A1691727FA9357EDF851C6CB77AEEF91F5C9170F9`, benchmark
`DF168175619E86755A6A2EB76FC0C713B595C099D20D8996150C4D175D28154D`, Engine
`7CFCE385002E437E25B0BB812FA9AAB8C6D8EA31EFF6AC7351B40B853B33AFEB`.

## Interpretation and remaining product discriminator

The worthwhile contribution is **globally complete validation with independently
certified, source-sized owners**, not a faster replacement JSON grammar. Actual
ordinary warm work is roughly 84k visits, even after 5,000 dispersed edits in a
100 MiB document, and offscreen duplicate totals remain exact. Compact arrays
demonstrate metadata scaling with source bytes rather than element count.
Cold hot-counter and per-object field regressions were measured and removed
rather than traded silently for incremental speed. Compact cold allocation has
an explicit small absolute metadata cost; full-process peak and tails are not
hidden by the small retained index.

This supports the **managed analysis slice** on the declared workload/host.
Parser correctness requires the separate oracle/cancellation suite, and product
promotion still requires the design's ordinary native four-RID AOT interaction,
idle-Full cancellation, actual input-to-draw, and Save/fresh-reopen measurements.
Those results cannot be substituted by this microbenchmark or a green build.

## Reproduction

From the repository root, archive the declared baseline and keep outputs inside
the repository:

```powershell
New-Item -ItemType Directory -Force .temp/JsonArrayPages/baseline | Out-Null
git archive -o .temp/JsonArrayPages/baseline-source.zip 91a77f886058802be179185b057919025c315a37 src/Mote.Engine src/Mote.Formats
Expand-Archive .temp/JsonArrayPages/baseline-source.zip .temp/JsonArrayPages/baseline -Force
dotnet build benchmarks/JsonArrayPages/JsonArrayPages.csproj -c Release -o .temp/JsonArrayPages/baseline-bin "-p:FormatsProject=$PWD/.temp/JsonArrayPages/baseline/src/Mote.Formats/Mote.Formats.csproj"
dotnet build benchmarks/JsonArrayPages/JsonArrayPages.csproj -c Release -o .temp/JsonArrayPages/candidate-bin
dotnet .temp/JsonArrayPages/candidate-bin/JsonArrayPages.dll generate
pwsh -File benchmarks/JsonArrayPages/Run.ps1 -Mode Pairs -RunName specialized-final-pairs
pwsh -File benchmarks/JsonArrayPages/Run.ps1 -Mode Warm -RunName specialized-final-warm
pwsh -File benchmarks/JsonArrayPages/Summarize.ps1 -InputDirectory .cache/json-array-pages/specialized-final-pairs
pwsh -File benchmarks/JsonArrayPages/Summarize.ps1 -InputDirectory .cache/json-array-pages/specialized-final-warm
```

Initial baseline-only 45-process measurements remain under
`.cache/json-array-pages/initial-baseline`; use the interleaved paired baseline,
not those earlier samples, for comparative conclusions. Raw fixture metadata and
complete process results provide source identities and every measured sample.
The separate fixed-source await control was built from an archive of `d9aadd3`
under `.temp/JsonArrayPages/yield-control-source`; direct warm invocations of
`.temp/JsonArrayPages/yield-control-bin/JsonArrayPages.dll` preserve that exact
Engine source even if a later producer-lifetime fix lands. The checked-in harness
now includes the await hop, so future reproduction uses the corrected lifetime
measurement. Preserve old inline and failed-candidate artifacts as controls, not
as steady-state memory or final-source claims.
