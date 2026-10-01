# Real Engine LF-only break-count fast path: controlled production experiment

Date: 2026-10-01. Scope: `src/Mote.Engine/RopeNode.cs` private break counter,
one new public-contract regression test file, and this experiment record.
**Recommendation: retain this narrow candidate for ordinary cross-platform
correctness verification, with an explicit small dense-LF trade-off.** The
uninstrumented real Engine experiment shows a meaningful local Windows x64
100 MiB JSON Open improvement, not a universal or cross-RID performance win.
No public API, file representation, Native GUI, telemetry or CI is changed.

## Contract and mechanism

The [reviewed attribution experiment](native-json-open-attribution.md) nominated
C only after rejecting two over-scanning implementations. Here C is applied to
the real Engine, not the flag-driven namespace-isolated copy:

```csharp
if (!text.Contains('\r')) return text.Count('\n');
// Existing scalar CR / CRLF implementation follows unchanged.
```

The helper serves both leaf construction and `BreaksBefore` prefix queries.
When no CR exists, logical break count is exactly LF count. Framework span
counting can use its platform implementation; no custom ISA/intrinsics or
hardware-specific dependency is added. CR-containing spans retain the prior
scalar code, including a trailing CR counted locally. Parent metadata still
corrects `left.EndsWithCr && right.StartsWithLf`, so cross-leaf CRLF remains one
logical break. `GetLineIndexFromOffset` still adjusts an offset between CR/LF.
Empty prefixes, source UTF-16 offsets, surrogate-boundary edit validation,
immutable snapshots, Undo/Redo, BOM detection and source hashing are unchanged.
This removes neither all linear ingestion work nor the immutable UTF-16 storage
cost; there is no eager global line-offset array or newline-density heuristic.

Private-helper XML comments explain the semantic and boundary invariant rather
than promising a particular vector width. The .NET runtime's
[CountValueType implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/SpanHelpers.T.cs)
contains vector and scalar paths; framework availability is not evidence that
every workload or machine gets faster. Mote's measurements decide adoption.

## Real Engine correctness before and after

The new `EngineLineBreakFastPathTests` passed **9/9** on the unchanged Engine
before modification. After the change, that file plus existing `EngineTests`
and `EngineRangeChunksTests` passed **53/53** in the Release managed test host.
An independent reviewer also exercised the focused 9 cases; this is correctness
verification, not Native AOT edit/paint latency.

The new independent whole-string oracle validates:

- all 9,841 strings over ordinary text / CR / LF through length 8, including
  every offset, all line starts/content and the complete immutable text;
- CR/LF/CRLF/none sources containing `é`, `中` and `😀`, including a surrogate pair
  split at 16 Ki UTF-16 leaf boundaries;
- edits removing/reinserting CR or LF at leaf boundaries, Unicode prefix edits,
  prior-snapshot validity and Undo/Redo after every mutation;
- rejection of edits inside a surrogate pair;
- real file opens using strict UTF-8, BOM UTF-16LE and BOM UTF-16BE with newline
  boundaries beyond the 64 Ki input buffer.

All test artifacts remain in `.temp/tests/engine-line-break-fastpath/`.
A regression in these contracts is not justified by a benchmark gain.

## Uninstrumented Native AOT setup and provenance

Baseline is a byte-for-byte source copy of the unchanged real Engine, **same
namespace, same assembly/project**, stored at
`.temp/native-json-open-fastpath/baseline-engine/`. Candidate references the
actual modified `src/Mote.Engine/Mote.Engine.csproj`. A small identical harness
is separately published from `bench/` and `candidate-bench/`; it performs only
`await Document.OpenAsync(path)` between stopwatch timestamps. There are no
phase timers, copied namespaces, candidate-mode flags or scanner microbenchmark.
Snapshot length/line reads, result serialization, yield, forced GC and memory
observations are outside timed Open. Native process creation is also outside
this child interval. Fresh build logs both show **Generating native code**.

| Identity | Baseline | Candidate |
| --- | --- | --- |
| `RopeNode.cs` worktree SHA-256 | `09db1be43d13f244936d34b83e8a5fadf80b98e8ece6279dad0881898d59a2be` | `b3950a8c52469b363576f2f4686804a5cab1984506533d4b41c1e785411f235d` |
| Native benchmark SHA-256 | `8977341169931aa7941c34313f4a6de14230f0849a17ce664abafad17c8045ac` | `f6f8c97a5e853f365dba46f7ef3487300d3ab997fe7295163f5df8816e9a7c07` |
| Native executable bytes | 2,949,632 | 2,949,632 |

All other Engine source/project hashes match, as do both harness source bytes;
complete inventories are retained in metadata. Baseline source and first-cohort repository HEAD were captured at
`48a371108a67370bc46cea52299dc5c6561e9cc5`. Candidate Engine was a
tracked but uncommitted source change; its source hashes, not that HEAD alone,
identify the experiment. Other concurrent project changes were outside Engine.
The actual binary hashes were checked before/after the cohorts. This is retained
local build provenance, not signed supply-chain attestation or byte-identical
cross-machine reproducibility.

Environment: **Windows 10.0.26200 x64**, **.NET 10.0.11 Native AOT**, **20 logical
processors**, developer host. OS cache was **not evicted**; fixture generation,
streaming SHA and independent line/UTF-16 oracle scans precede timed children.
Each child is a fresh process and has a 30-second deadline. First cohort uses
**12 matched pairs per shape**, randomly permuting six baseline-first and six
candidate-first pairs (seed **610001**). Followup uses the same compiled binaries
and a distinct seed **610002**. Background load is not comprehensively controlled.
These are cache-warm Engine ingestion observations, not cold-disk, GUI startup,
physical key latency or a frame/paint measurement.

## First actual-Engine cohort: retain every observation

**240/240** processes across ten shapes passed exact length and logical-line
oracles and exited normally. Every cell below is a median of 12 observations;
paired mean is computed before aggregation. Exploratory bootstrap: 20,000
within-host resamples of the twelve differences, percentile 95% interval, seed
610001. It does not account for independent machine/image variation, variant
selection or rare tails. There is no p95 or performance gate.

| Workload | Baseline → candidate Open median | Mean candidate−baseline | Exploratory paired interval |
| --- | ---: | ---: | ---: |
| **100 MiB actual LF JSON** | **254.140 → 230.869 ms** | **−21.852 ms** | **[−24.257,−19.628] ms** |
| 1 MiB actual LF JSON | 5.753 → 5.062 | −0.456 | [−1.082,0.155] |
| 10 MiB dense `a LF` | 23.327 → 24.088 | **+0.981** | **[0.106,2.014]** |
| 10 MiB dense `a CRLF` | 23.391 → 23.471 | −1.850 | [−5.741,0.668] |
| 10 MiB dense `a CR` | 23.906 → 23.114 | −0.480 | [−1.440,0.395] |
| 10 MiB no-newline | 23.460 → 23.599 | +0.120 | [−0.742,0.752] |
| 4 KiB dense LF | 3.295 → 3.579 | **+5.692** | [−0.310,17.234] |
| 4 KiB dense CRLF | 4.368 → 4.317 | −0.095 | [−0.558,0.310] |
| 4 KiB no-newline | 3.577 → 3.495 | +0.197 | [−0.627,1.057] |
| Unicode LF text, ~10 MiB bytes | 22.802 → 22.972 | +0.172 | [−0.477,0.899] |

100 MiB JSON improves in **12/12** matched pairs, baseline observed range
245.484–262.106 ms, candidate 226.885–243.860 ms; median improvement ~**9.2%**.
The fixture is exactly 104,857,600 ASCII bytes/UTF-16 units, 2,097,152 lines,
SHA `11c596afa32f508d22cf7704eb458200fd66c8ec05af3d9f4a384aeb0570c5db`.
These values support a real Engine improvement on this host/workload, not the
same percentage on a different hosted GUI or another architecture.

Unicode fixture is **10,484,320 bytes**, **5,718,720 UTF-16 units**, **953,121
lines**, repeated `中é😀x LF`, not exactly 10 MiB and not ASCII. Its exact streaming
oracle distinguishes byte count from document length, preserving decoder-state
coverage absent from the original ASCII attribution probe.

Two material uncertainties were not hidden: first dense LF has eight of twelve
pairs slower, median +0.761 ms / ~3.3%; the 4 KiB LF candidate has a **71.478 ms**
outlier, making its mean unfavorable. An unrelated baseline dense-CRLF sample
is **44.609 ms**. All are retained; none is labeled presumed OS noise, removed,
winsorized or used to assert an unobserved cause.

100 MiB managed cumulative allocation medians are **211,084,152 → 211,084,160
bytes** (**+8 bytes**, not literally equal), while post-yield forced-GC live
heap medians are exactly equal at **210,642,232 bytes**. Both have median GC
collection deltas `[19,18,3]`; each mode has nine such rows and three `[18,17,3]` rows. There is no material proportional allocation or
retained-memory increase in this scoped case. Precise process-wide allocation
is not per-thread allocation or native/GUI memory; forced-GC live observations
and point working set do not establish lifetime GUI peaks. A yield encourages
producer unwind but is not a producer-lifetime barrier.

## Targeted uncertainty followup and representative line lengths

The first JSON/CR/none/Unicode workloads were **not rerun**. Followup addresses
only unresolved dense LF and tiny LF, with 24 new pairs each, plus 12 new pairs
for representative Markdown and CSV source ingestion. Every pair balances order,
uses the same binaries, and stays a **separate cohort**, never pooled to erase
first-run outliers. **144/144** processes pass independent oracles.

| Source shape | Size / logical lines | LF fraction / average units per LF | Baseline → candidate median |
| --- | --- | --- | ---: |
| Adversarial dense LF | 10,485,760 B / 5,242,881 lines | **50%**, **2 units** including LF | **23.515 → 23.988 ms** |
| Tiny dense LF | 4,096 B / 2,049 lines | 50%, 2 units | 3.540 → 3.464 |
| Markdown section/paragraph/link/emphasis | 10,485,760 B / 338,251 lines | **3.226%**, **31.000 units** | **26.383 → 24.309** |
| CSV record with numeric/text/boolean fields | 10,485,760 B / 194,181 lines | **1.852%**, **54.000 units** | **27.261 → 25.345** |

The Markdown fixture repeats a 124-byte record with heading, blank lines and a
long ordinary paragraph; CSV repeats a 54-byte record. They provide production-
like line-length controls, not a claim to represent every user-file distribution.
Only Engine ingestion is measured, not Markdown rendering or CSV semantics.

Dense LF remains modestly slower: 15/24 pairs unfavorable, median **+0.474 ms /
~2.0%**. Its paired mean **+2.756 ms**, interval `[−0.013,7.550]`, is affected by
one **76.609 ms** candidate observation (matched difference +52.653 ms); that
outlier remains unexplained and retained. Tiny LF followup has baseline outliers
**68.243 and 41.529 ms**, candidate maximum **7.536 ms**; paired mean **−4.186 ms**,
interval `[−11.160,0.343]`. Outliers occurring on both binaries in different
cohorts are a counterexample to assigning every long sample to the new scanner,
but do **not** explain any particular earlier event or prove tail equivalence.

Representative Markdown mean paired difference is **−1.319 ms**,
`[−2.247,−0.310]`; CSV **−0.930 ms**, `[−2.335,0.749]`. Markdown has nine of twelve
pairs favorable, as does CSV. The CSV interval crosses zero, so do not advertise
its median difference as a dependable isolated speedup.

## Product judgment and boundaries

This is a reasoned trade-off, **not a claim of no regression**:

- The intended dominant workload is ordinary structured/text files, with tens
  or hundreds of characters per line, not one-character payloads on 5.2 million
  lines. The 100 MiB representative JSON saves ~23 ms median in real Engine
  ingestion, and realistic Markdown supports the same direction.
- Supported dense LF is valid input, and its ~0.5–0.8 ms median cost is real.
  Relative percentages alone overstate its product impact: the observed absolute
  median cost is below one millisecond at 10 MiB, versus tens of milliseconds
  saved on a dominant large structured-file workload. Preserve this risk; if
  actual users or same-RID hosted repetitions establish a material regression,
  retain the original scanner. No density branch is introduced to rescue a table.
- No memory representation, API, source/hash/Save contract or external dependency
  changes, and strong public line/edit/history tests remain green. The change
  is two span operations before the existing pair-sensitive scalar loop.
- Current evidence supports **local Windows x64 real Engine benefit with a tiny
  dense-LF median trade-off**, not cross-platform performance, burst editing,
  disk-cold startup, rare-tail safety or a native editor SLA. Ordinary CI must
  still establish four-RID compatibility; no signing/Gatekeeper claim is implied.

The ".NET vector primitive must be faster" argument was specifically tested and
rejected for earlier A/B implementations. Framework features are mechanisms,
not exemptions from workload profiling. The mature
[VS Code text-buffer account](https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation)
likewise centers actual line operations and workload profiling. Algebraically
measured persistent sequences in
[Hinze and Paterson, JFP 2006](https://www.staff.city.ac.uk/~ross/papers/FingerTree.html)
provide a useful model for cached aggregate/boundary invariants; they are not
an instruction to replace the existing AVL rope. For uncertainty, retained
nested samples follow the reasoning of
[Kalibera and Jones, ISMM 2013](https://kar.kent.ac.uk/33611/), not a best-run claim.
No architecture rewrite or additional library is warranted by this experiment.

## Artifacts and reproduction

All local experiment artifacts remain under repository roots:

- `.temp/native-json-open-fastpath/`: byte-exact unchanged baseline Engine,
  identical harness source in `bench/` and `candidate-bench/`, separate AOT
  publish directories, `matched.py`, `targeted.py`, summaries and fixtures.
- `.cache/native-json-open-fastpath/`: source manifest, separate baseline/
  candidate publish logs, before/after focused test logs, first
  `matched-{metadata,rows,summary}` and separate `targeted-{metadata,rows,summary}`.
  Raw row files use `.jsonl`; manifests/summaries `.json`.

From repository root, preserving a fresh unchanged baseline **before** applying
the private scanner change, the two actual build commands are:

```powershell
dotnet publish .temp/native-json-open-fastpath/bench/Bench.csproj `
  -c Release -r win-x64 -p:PublishAot=true `
  -p:EngineProject=D:\Code\mote\.temp\native-json-open-fastpath\baseline-engine\Mote.Engine.csproj `
  -o .temp/native-json-open-fastpath/baseline-publish
dotnet publish .temp/native-json-open-fastpath/candidate-bench/Bench.csproj `
  -c Release -r win-x64 -p:PublishAot=true `
  -p:EngineProject=D:\Code\mote\src\Mote.Engine\Mote.Engine.csproj `
  -o .temp/native-json-open-fastpath/candidate-publish
python -B .temp/native-json-open-fastpath/matched.py
python -B .temp/native-json-open-fastpath/targeted.py
```

Use new scratch/output names for new cohorts; never overwrite retained evidence.
The harness source starts its monotonic stopwatch immediately before awaiting
real `Document.OpenAsync`, ends immediately after it returns, and serializes a
source-generated fixed result outside that interval. Independent Python source
oracles run before timing and validate every returned UTF-16 length/line count;
process-wide allocation and GC deltas are retained separately. Complete source,
configuration, commands, identities and individual observations above make
this local experiment checkable; identical hardware/bitwise binaries or general
latency tails are not promised.

Reproduce public correctness independently with:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore `
  --filter 'FullyQualifiedName~EngineLineBreakFastPathTests|FullyQualifiedName~EngineTests|FullyQualifiedName~EngineRangeChunksTests'
```

[Independent review](../reviews/rope-countbreaks-fastpath.md), committed as `2f18086`, found no unresolved substantive issue in the frozen private helper, tests, provenance or explicitly accepted dense-LF trade-off.
No product performance claim should be expanded beyond the observed scope.
