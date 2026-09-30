# Markdown flat cold Full allocation: copying removal and bounded proof reuse

Implemented and measured 2026-10-01. Scope: Markdown-private cold flat
certification only. Engine, public format/session contracts, exact small-file
analysis, reference certification, Native and CI are unchanged. The committed
source index still contains offsets/kinds, not a second document string.

## Question, workload and causal diagnosis

The known no-reference control in `markdown-reference-production.md` allocated
about 664 MB during idle cold Full on a 100 MiB file. Opening/Visible was already
bounded. The relevant target is avoidable background-analysis allocation and GC
pressure, not a claim that all Markdown is incrementally parsed or that native
startup is fixed by this change.

The baseline flat loop materialized every physical line through
`TextSnapshot.GetText`; `RopeNode.Slice` uses `StringBuilder` plus `ToString`.
It then sliced away the delimiter, allocating another body string, and parsed
every identical body through Markdig. A warm attribution probe measured each
operation independently over 3,000 repetitions of the real 4,011-unit raw line:

| Operation | Allocated bytes per nonempty owner |
| --- | ---: |
| `GetText(0, 4011)` including builder and resulting string | 16,144 |
| `raw[..^1]`, 4,010-unit body | 8,048 |
| `IsVerifiedFlatBlock(body)`, production Markdig pipeline | 1,056 |
| Total, before blank lines/index/projection | 25,248 |

Thus copying, not retained AST size, explains most of the cumulative allocation.
The 26,136 repeated owners predict 659,881,728 bytes from those three operations
alone, close to the observed 664,085,232-byte complete Analyze interval. Attribution
is warm, uninstrumented delegate invocation of the private verifier, **not** an
end-to-end cold latency measurement.

## Implementation and invariants

1. A sequential `SnapshotTextReader` copies each existing physical-line range
   into one reusable `char[]`, starting at 4,096 units and growing geometrically
   to at most 65,538. Existing line offsets, length limits and LF/CRLF stripping
   remain authoritative. This is not a new newline scanner or admission grammar.
2. A private stage-local verifier keeps only the last successfully verified
   paragraph and heading strings. `ReadOnlySpan<char>.SequenceEqual` requires
   exact ordinal source equality; no hashes, normalized text, skeletons or
   whole-file dictionary determine a hit. The cached heading level belongs to
   that exact source. Different sources still invoke the original verifier.
3. Every physical owner's adjacency rule still runs after verification. Repeated
   paragraphs without a separator refuse even on cache hits. Every fence still
   gets its whole original-source parser check. Original source, not cached
   display text, is used for viewport projection.
4. Scratch and cache belong only to one private certification call. At most two
   strings of 65,536 UTF-16 units are retained during that call; none enter
   committed `FlatBlock`/`FlatRun` state. Failed/canceled work cannot publish
   them. Reader disposal and all preexisting cancellation/publication gates
   remain intact. Opaque Markdig calls are still not internally preemptible.

Proof reuse relies on the already admitted dependency-free grammar and the
fixed production parser pipeline, not arbitrary Markdown locality. Markdig
documents reuse of an immutable pipeline, while its API builds an AST from a
string. Mutable extension callbacks would invalidate this inference and require
revalidation. [Markdig pipeline architecture](https://xoofx.github.io/markdig/docs/advanced/pipeline/),
[Markdig Parse implementation](https://github.com/xoofx/markdig/blob/main/src/Markdig/Markdown.cs).
Research such as Yedidia and Chong's SLE 2021 GPeg explores memoization and
pruning to bound incremental parsing cost. Here the useful design lesson is
reuse with an explicit lifetime/identity contract; adopting a general PEG or
memo table would not establish CommonMark reference independence and is not
needed to remove these copies. [Fast Incremental PEG Parsing](https://doi.org/10.1145/3486608.3486900).

## Matched fresh-process measurements

Environment: Windows 10.0.26200 win-x64, Intel i9-12900H (14 cores/20 logical
processors), 34,087,665,664 bytes physical RAM, .NET SDK 10.0.400, managed runtime
10.0.11 Release, Markdig 1.3.2. This is **not Native AOT or native GUI acceptance**.
No CPU affinity, isolated host or fixed power profile was imposed.

The final control pins the exact same probe DLL, Engine DLL and every other
dependency byte; `controlled-fixed` is a copy of baseline with only the fixed
Formats DLL/PDB overlaid. `controlled-binary-inventory.json` verifies this pin.
Five fresh-process pairs per large corpus alternated
baseline-first/fixed-first order; ten pairs used the 1 MiB small control. Source
files were identical between the two versions and the OS file cache was warm.
Each process opened the UTF-8 file through `Document.OpenAsync`, timed cold
Visible, forced GC outside the interval, then timed the first Full. Consequently
"cold Full" means cold global certification **after** first Visible, not the
first ever parser/JIT invocation. Timing includes Analyze/projection, excludes
creation/opening/serialization/forced GC. No forced GC occurs inside Analyze.

The following are medians with observed min..max latency across five processes,
not confidence intervals or p95:

| 100 MiB corpus | Baseline cold Full ms | Fixed cold Full ms | Baseline allocated MiB | Fixed allocated MiB |
| --- | ---: | ---: | ---: | ---: |
| Same 4,010-unit paragraph, blank separators | 812.23 (803.24..865.52) | 613.91 (598.05..642.61) | 633.321 | 1.434 |
| Every paragraph unique, same bounded shape | 818.02 (780.48..832.18) | 787.16 (779.43..794.64) | 633.169 | 228.230 |
| Alternating identical ATX heading/paragraph, no blanks | 882.80 (854.46..934.41) | 631.33 (629.06..707.11) | 649.814 | 2.816 |
| Repeated closed JSON fence, opaque body | 1261.20 (1253.37..1523.02) | 1221.23 (1212.39..1260.13) | 848.261 | 438.503 |
| Existing explicit-reference control | 272.92 (259.56..278.20) | 271.35 (260.37..278.14) | 2.974 | 2.982 |

Flat Analyze bytes are exactly **664,085,232 -> 1,503,248** in all five pairs:
99.77% less cumulative same-thread allocation, with a 24.42% reduction in median
managed cold Full latency. Unique content cannot hit the cache, but copying
removal alone saves 63.95% of its allocation. Fence parser work is unchanged;
removing per-physical-line copies saves 48.31%, without establishing a significant
fence latency improvement. All admitted Full results remain Complete, total
diagnostics zero except the reference control's exact warning count.

Peak working set is the OS process high-water value observed after Full,
including document opening and JIT, not just the certifier:

| Corpus | Baseline peak MiB | Fixed peak MiB | Baseline/fixed retained managed delta KiB |
| --- | ---: | ---: | ---: |
| Flat repeated | 264.160 | 243.512 | 420.000 / 420.023 |
| Unique | 263.863 | 266.078 | 419.398 / 419.492 |
| Adjacent | 265.527 | 244.859 | 818.742 / 818.836 |
| Fence | 263.820 | 264.250 | 419.773 / 419.773 |
| Reference | 245.457 | 245.574 | 837.320 / 837.273 |

The unique-content peak is **2.215 MiB higher**, about 0.84%; its live retained
index delta differs by only 96 bytes. This small process-heap/GC-capacity tradeoff
does not outweigh the 405 MiB allocation reduction, but allocation reduction is
not a universal RSS-reduction claim. Fence RSS is essentially flat. The repeated
flat and adjacency peaks are lower by about 20 MiB.

### Small files, Visible and edits

The 1,051,144-unit control takes the unchanged exact route: first-call Visible
median 51.601 ms (49.988..54.381) vs 51.871 ms (49.381..56.472), about 8.47 MiB
allocation in both; cached Full allocates exactly 88,384 bytes in both. Cached
Full timing is 0.194 vs 0.191 ms (ranges overlap); this sub-millisecond jitter is
not evidence of a meaningful small-file regression. Retained delta is identical
at 32,728 bytes; peak RSS is 45.201 vs 45.246 MiB. Large cold Visible stays bounded
Provisional on both binaries and is not routed through this whole-file work.

Ten same-shape near-start edits/process preserve Complete in every corpus.
Flat edit allocation is unchanged at 35,560 bytes, but the pooled observed edit
medians are **0.052 -> 0.117 ms**, not an improvement claim. Adjacent medians are
0.081 -> 0.115 ms; unique/fence/reference/small controls are approximately stable.
These are correlated within-process samples, not independent tail estimates.
The edit implementation is unchanged, but eliminating thousands of parser calls
also changes managed tiering/warm-up history. A discriminating three-pair
`DOTNET_TieredCompilation=0` control reduces the flat edit gap to
0.0248 -> 0.0373 ms (same 35,520 bytes), without eliminating it completely.
This supports a warm-up sensitivity interpretation, **not** proof that all
timing differences are JIT-caused. No artificial parser warm-up is added to
production: a 0.07 ms managed format-only edit difference is outweighed by the
large cold allocation benefit, and Native AOT input/render tails still require
separate measurement. Disabled-tiering cold Full remains improved:
676.71 -> 513.63 ms.

### Ablation and negative scope

A repo-local, generated no-cache variant retains sequential scratch reading but
reparses every paragraph/heading. Three exploratory fresh runs on flat yield
239,436,272 bytes and median 868.13 ms (792.97..879.22). These runs are **not**
interleaved with the initial baseline (median 914.45 ms), so they attribute
allocation, not an independent timing ratio. They show copying removal saves
about 425 MB and exact-source reuse saves a further 238 MB on repeated flat input.
No-cache files are experimental, not production implementation.

The fix does not admit new grammar, repair general Markdown incremental parsing,
reduce Engine document memory, cache fences, or accelerate hostile long-line
refusals. The repeated fixture is favorable to exact-source reuse; the unique
control prevents presenting that hit rate as universal. We stop here rather
than inventing a global dictionary or replacing the line index for theoretical
extra gains.

## Correctness and independent review

- Isolated Formats Release build/publish: zero warnings/errors.
- Independent new suite: **15/15** passed, including >16 Mi UTF-16 mixed
  repeated/unique Unicode bodies, LF/CRLF and actual CRLF rope seam, inclusive
  65,536-unit limit and first over-limit unit, unterminated EOF, opaque fences,
  unsupported intermediates, reference-to-flat transition, active-call
  cancellation/old-state retention/retry. Active cancellation does not identify
  an exact reader line/phase. [Validation](validation/markdown-flat-cold-allocation.md).
- Independent private-certifier baseline/fixed differential: **2,741/2,741**
  matching admission, bad-line span and every block start/length/kind/level,
  including seeded cases, source fragmentation and boundary adversaries.
  [Review](reviews/markdown-flat-cold-allocation-review.md).
- Combined Formats-only `Markdown*.cs` regression harness, excluding
  Native-dependent `MarkdownPreciseSourceTests.cs`: **91/91**, zero failures,
  zero skips, includes the 15 new cases, therefore **not additive**. Its first
  two harness builds lacked the Native exclusion and `Mote.Tests` friend
  assembly name; both harness-only defects were corrected without production
  changes. Full-solution/AOT/native UX validation is not claimed by this run.

### Target build/test compatibility checkpoint

The integrated change `cac8179` is included in HEAD
`5977250330fd47a633b95692b5a3ed8e1c988495` of
[CI run 36787947202](https://github.com/kleedaisuki/mote/actions/runs/36787947202).
All nine strict jobs completed successfully: Windows/macOS solution tests,
strict single-binary Native AOT for win-x64, win-arm64, osx-x64 and osx-arm64,
and the three dedicated native CSV Grid clipboard jobs. This establishes
cross-platform build/test compatibility of the integrated optimization. It does
**not** establish 100 MiB Markdown native GUI acceptance, native allocation/RSS,
startup/first paint, input latency or render-tail performance. The managed
measurements and their scope above remain unchanged; no local validation was
repeated for this checkpoint.

## Provenance and reproduction

An initial round built both folders from unchanged probe/Engine source, but
concurrent repository commits changed their AssemblyInformationalVersion/SourceLink
metadata and DLL hashes. Those preliminary `paired-*` results are retained,
**not** used for the final headline. The final controlled repeat above corrects
that provenance weakness; cumulative allocation is unchanged and the latency
benefit persists. No functional rerun was needed for that metadata correction.
The exact shared probe SHA-256 is
`7E317A89FDFE53A910273559894B74E7BEEC7C0BC85EA1846996CD34B2D7196B`;
shared Engine SHA-256 is
`F13BFB8C602EE4A3D360643162828F542C5407A5069E0B1BEE4F25777139C759`.


Source baseline: repository revision `f10a741`, physical source SHA-256
`4A3B32EC006BDDF2419C00EB55A8665544DC52E32347746550D980FC21679915`.
Reviewed fixed source:
`AF377F39007ED90788CCDEA9892D4288D1A7A29DF99E0866E42CB2380151EA73`.
The measured fixed DLL predates only the XML-summary wording correction, with
identical executable source. DLL SHA-256 values:

- Baseline: `46AAE9419D5AC07E53668A17D4277B3AB41347048B44A455DE5577AD0B25E2E7`.
- Fixed: `BF2FD5C79BCCE3E0ED9E8745889A3A87060ECE463C8279357BD1625302A4D85F`.
- Scratch/no-cache ablation: `934D71A9AC1F405B6282EA1805E5794B0930A90FE2F0DCE24A0991938636F6ED`.

Raw evidence is under `.cache/markdown-flat-cold-allocation/`: pinned publish
folders, `controlled-{baseline,fixed}-{mode}-{1..5}.jsonl`, ten controlled small rows,
`controlled-summary.json`, `controlled-binary-inventory.json`, initial/ablation and
preliminary `paired-*` runs, `controlled-notier-*`, `attribution.jsonl`,
and `markdown-regression.trx`. The temporary measurement source is under
`.temp/MarkdownFlatColdAllocationProbe`; the persisted standalone tool is
`benchmarks/MarkdownFlatColdAllocation`, deliberately outside `mote.sln` and CI.
It adds argument/root checks and an input identity record **after** measurement;
the timed Analyze procedure and fixtures are the same. It is not a unit test or
release gate. All generated corpora remain under root `.temp`, results/publishes
under root `.cache`.

Run from repository root:

```powershell
dotnet publish benchmarks/MarkdownFlatColdAllocation -c Release -warnaserror -o .cache/markdown-flat-cold-allocation/replay
dotnet .cache/markdown-flat-cold-allocation/replay/MarkdownFlatColdAllocation.dll flat 100
dotnet .cache/markdown-flat-cold-allocation/replay/MarkdownFlatColdAllocation.dll unique 100
dotnet .cache/markdown-flat-cold-allocation/replay/MarkdownFlatColdAllocation.dll flat 1
dotnet .cache/markdown-flat-cold-allocation/replay/MarkdownFlatColdAllocation.dll flat 100 attribution
# Other controls: adjacent, fence, reference. Attribution is warm and flat-only.
```

For a fresh historical baseline without changing the working checkout:

```powershell
git worktree add --detach .temp/markdown-flat-baseline f10a741
$baselineFormats = Join-Path $PWD '.temp/markdown-flat-baseline/src/Mote.Formats/Mote.Formats.csproj'
dotnet publish benchmarks/MarkdownFlatColdAllocation -c Release -warnaserror "-p:FormatsProject=$baselineFormats" -o .cache/markdown-flat-cold-allocation/baseline-replay
dotnet publish benchmarks/MarkdownFlatColdAllocation -c Release -warnaserror -o .cache/markdown-flat-cold-allocation/fixed-replay-build
New-Item -ItemType Directory -Force .cache/markdown-flat-cold-allocation/fixed-replay | Out-Null
Copy-Item -Path .cache/markdown-flat-cold-allocation/baseline-replay/* -Destination .cache/markdown-flat-cold-allocation/fixed-replay -Recurse -Force
Copy-Item -LiteralPath .cache/markdown-flat-cold-allocation/fixed-replay-build/Mote.Formats.dll -Destination .cache/markdown-flat-cold-allocation/fixed-replay/Mote.Formats.dll -Force
# Optionally overlay its PDB too. Verify all non-Formats DLL/file hashes match,
# then alternate fresh children from baseline-replay and fixed-replay.
```

The flat 100 MiB input is 104,857,632 bytes/UTF-16 units, SHA-256
`3DAB69468A62EC2938DE7F239A40D9772C2E3E184D5B594AD17F377EB183B42A`.
The 1 MiB control is 1,051,144 bytes, SHA-256
`500AA4C7A2952BF1E45C25932C2AF0223EBD84B07C4C24C7CE22E85133CDDB17`.
Large ASCII fixture shapes are specified in the persisted generator; input
records permit checking actual bytes before comparing new runs.
