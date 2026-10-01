# TOML statement costs: bounded causal probe

## Decision (2026-10-01)

The current large-`Full` statement certifier has two separable allocation hotspots:

1. For single-line statements, repeatedly constructing a lossless Tomlyn parser tree
   and standalone validator dominates cumulative allocation. Ownership is not the
   nested-array-of-tables hotspot.
2. For multiline statements, materializing and rescanning the entire accumulated
   prefix at **every physical newline** dominates allocation. A 46-content-line string
   rescanned 24.53 times its document size. This is concrete evidence for retaining
   scanner state across lines, not for weakening TOML validation.

These observations motivate an incremental semantic model that does not recreate a
   whole document or every unchanged statement after an edit. They do **not** justify
   removing the syntax/semantic validation or changing a `Provisional` result to
   `Complete` without a corresponding correctness model. No production optimization
   was made by this investigation, and no end-to-end editor performance improvement
   has yet been established.

## Frozen baseline and environment

- Baseline repository HEAD at probe completion: `21f4c222bfe2e04bf4ea400331779c1f0134f7e7`.
- Release `Mote.Formats.dll` SHA-256:
  `6B18939C4AF55813DA48AB9CC6CD9A3C1DCA107BF104500A66464408937E7225`.
- `TomlIncrementalSession.cs` SHA-256:
  `A4AB0C96584DA1400629669B50D0AC85F1EADEB46813BC15774813FF58E8E43D`.
- `TomlOwnershipIndex.cs` SHA-256:
  `1466D2298E8E3DD0816FBB6D0F5CD9012ED6F010EBBDF8C5898DA0C66ED09F5A`.
- Windows 10.0.26200, x64; Intel Core i9-12900H, 14 physical/20 logical processors.
- .NET SDK 10.0.400, runtime 10.0.11, workstation GC; Tomlyn NuGet 2.10.1.
- Project-local harness: `.temp/toml-cost-probe/Program.cs` and
  `.temp/toml-cost-probe/toml-cost-probe.csproj`. The executable assembly uses
  `Mote.Tests` solely to access the existing friend-assembly ownership API. No new
  friend declarations or production access modifiers were added.
- Existing upstream checkout inspected for mechanism:
  `.cache/Tomlyn-upstream`, commit `353e75df51929dc20df616cac2ecabe39297a72a`.
  The measured parser is the NuGet dependency, not a rebuilt upstream checkout.

The frozen binary was built once before the measurements. Each fixture/mode runs in
its own fresh process, executes one entire workload as warm-up, then performs five
timed repetitions. Full collections run before and after each repetition; the timed
region excludes those explicit collections. Input generation, `Document` construction,
and (for ownership-only measurements) standalone parsed-tree construction are excluded.
No GUI, filesystem document save, 100 MiB historical replay, or native AOT executable
was involved. Background activity was not isolated or controlled.

The first batch used default tiered JIT and exhibited substantial within-process
timing transitions (for example nested-AoT Full repetition 0 was 257.22 ms, while the
median was 72.06 ms). It is preserved in `.temp/toml-cost-probe/results/`. The table
below uses a **separate, explicitly labelled** `DOTNET_TieredCompilation=0` batch in
`.temp/toml-cost-probe/results-tiering-off/`, reducing that particular confound without
claiming to reproduce Native AOT code generation. Neither batch replaces the other.
See [.NET's compilation settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation).

## Fixtures and exact work

All source is ASCII, so the UTF-8 byte count equals the UTF-16 character count.
Every source is above the existing 4 MiB whole-parser threshold and below 8 MiB,
with fewer than 120,000 logical statements and fewer than 64 lines per statement.
All measured real-session Full calls returned `Complete`, zero diagnostics, using
an 80-character viewport centered in the source. This is a performance fixture check,
not a complete grammar conformance audit.

| Fixture | Exact bytes/chars | Logical statements | Physical lines | Construction |
| --- | ---: | ---: | ---: | --- |
| nested AoT | 5,257,209 | 5,201 | 5,201 | `[[item]]`, then 2,600 repetitions of `[[item.child]]` and `v = '<2000 x chars>'` |
| long quoted | 5,244,370 | 160 | 160 | Unique `k0`…`k159`, each assigned a single-line 32,768-character literal string |
| many short keys | 5,220,000 | 60,000 | 60,000 | `k00000=1 #<76 x chars>` through `k59999` |
| multiline quoted | 5,243,818 | 1,024 | 49,152 | Unique `k0`…`k1023`, triple-literal value containing 46 lines of 110 `x` characters |
| comment lines | 5,242,880 | 80 | 80 | `#` + 65,534 `x` characters + newline, repeated 80 times |

Fixture SHA-256 values are retained in every raw JSONL row, together with runtime,
assembly hash, iteration, exact allocation, counts, elapsed time, and process peak RSS.
The deterministic construction above is sufficient to reconstruct the same source.

## Measurements

Elapsed values are medians and observed **min–max**, not confidence intervals.
Allocation values are median cumulative managed allocation in MiB (1,048,576 bytes).
No statistical latency regression or equivalence claim is made from five repetitions.

| Fixture | Mode | Median ms [min–max] | Cumulative MiB |
| --- | --- | ---: | ---: |
| nested AoT | Parse, syntax only | 46.65 [45.68–49.07] | 42.97 |
| | Parse + standalone validation | 48.54 [46.91–51.24] | 45.71 |
| | Ownership of preparsed statements | 1.40 [0.92–1.78] | 1.11 |
| | Boundary accumulation/scanning | 9.20 [8.92–9.87] | 10.16 |
| | Real Full session | 65.53 [61.54–68.52] | 58.58 |
| long quoted | Parse, syntax only | 42.10 [40.12–43.73] | 32.77 |
| | Parse + standalone validation | 44.65 [41.58–48.80] | 32.85 |
| | Ownership of preparsed statements | 0.02 [0.02–0.06] | 0.03 |
| | Boundary accumulation/scanning | 8.31 [8.20–8.47] | 10.16 |
| | Real Full session | 53.91 [52.73–55.70] | 44.05 |
| many short keys | Parse, syntax only | 86.84 [83.20–87.90] | 139.17 |
| | Parse + standalone validation | 108.39 [104.58–114.29] | 168.47 |
| | Ownership of preparsed statements | 15.47 [13.52–18.96] | 10.75 |
| | Boundary accumulation/scanning | 9.68 [8.45–9.95] | 11.44 |
| | Real Full session | 169.26 [163.35–169.59] | 197.37 |
| multiline quoted | Parse, syntax only | 46.10 [42.55–47.82] | 38.86 |
| | Parse + standalone validation | 46.31 [45.76–47.07] | 39.36 |
| | Ownership of preparsed statements | 0.27 [0.12–0.71] | 0.21 |
| | Boundary accumulation/scanning | 106.00 [103.62–114.14] | 246.56 |
| | Real Full session | 155.21 [145.89–165.94] | 287.27 |
| comment lines | Parse modes, skipped by production's comment precheck | <0.01 | <0.01 |
| | Ownership of empty preparsed statements | <0.01 | <0.01 |
| | Boundary accumulation/scanning | 7.31 [6.72–7.54] | 10.28 |
| | Real Full session | 12.34 [11.89–13.34] | 11.28 |

### What each mode actually includes

- **Parse** invokes `SyntaxParser.Parse` for each already-materialized logical
  statement, optionally with `validate: true`. Pure comments are skipped exactly
  as production skips them. The syntax-only experiment is diagnostic, **not** a
  proposed substitute for production validation.
- **Ownership** invokes the actual `TomlOwnershipIndex` methods on preparsed key/value
  or table syntax, requiring exhaustiveness and certifiability. It keeps only the
  resulting index alive after collection; parser trees already existed at baseline.
- **Boundary** replays character-by-character `StringBuilder.Append`, newline
  `ToString`, and the production private `Continues` function using a cached delegate.
  It verifies the resulting logical statement count. It omits `SnapshotTextReader`,
  its block traversal, cancellation checks, ownership and parsing, so it is **not an
  exact isolated profiler region** from Full.
- **Full** invokes the real public format session against `document.Snapshot`,
  including stream scanning, parsing, ownership, projection and bounded lexical
  highlighting. Only the resulting `DocumentAnalysis` is deliberately retained.

Do not sum the mode elapsed times or treat their differences as exact attribution:
they run in independent processes with different object lifetimes and cache/JIT/GC
conditions. The allocation results similarly include some mode-specific overhead;
they identify the dominant mechanisms, not an exact accounting identity.

## Cumulative allocation is not retained memory

`GC.GetAllocatedBytesForCurrentThread()` counts managed bytes allocated on the current
thread, including bytes that have already been collected; it excludes native memory.
See the [official API contract](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-10.0).
The probe runs analysis synchronously on that thread and reports a separate
post-collection heap delta with the designated result kept alive.

| Retained object | Nested AoT MiB | Long quoted MiB | Many short keys MiB | Multiline MiB |
| --- | ---: | ---: | ---: | ---: |
| Ownership index alone | 0.001 | 0.010 | 3.846 | 0.083 |
| Full analysis result alone | 0.006 | 0.001 | 0.116 | 0.006 |

The small retained AoT index is expected: the current index replaces each completed
array element's scope, retaining only the most recent element needed by its restricted
source-order certification. It does not mean a future arbitrary-reentry incremental
model can safely discard the same facts. Full analysis retains only viewport semantic
nodes/lexical tokens, **not an incremental whole-document index**. Source strings,
snapshots, and preparsed trees are outside these deltas. Tiny positive deltas can
include incidental runtime bookkeeping. Process peak RSS is retained in the raw rows
but deliberately omitted from comparisons: it includes fixture preparation, warm-up,
preparsed trees, and previous repetitions; it is not parser retained memory.

## Mechanism and next design consequence

`ProcessStatement` currently creates a lossless syntax tree and validator anew for
every assignment/header. Tomlyn's `SyntaxParser` creates a parser and then a separate
validator; its parser materializes syntax nodes/trivia, and its validator initializes
object paths and a dictionary. These mechanisms can be inspected in the
[upstream parser API](https://github.com/xoofx/Tomlyn/blob/353e75df51929dc20df616cac2ecabe39297a72a/src/Tomlyn/Parsing/SyntaxParser.cs),
[parser](https://github.com/xoofx/Tomlyn/blob/353e75df51929dc20df616cac2ecabe39297a72a/src/Tomlyn/Parsing/Parser.cs),
and [validator](https://github.com/xoofx/Tomlyn/blob/353e75df51929dc20df616cac2ecabe39297a72a/src/Tomlyn/Syntax/SyntaxValidator.cs).
The measurements support the broad construction cost; they do not isolate a particular
allocation site without a heap allocation profile.

The current scanner copies the growing statement on each line, and `Continues`
starts its scan at character zero each time. The multiline fixture's measured
128,639,968 prefix characters versus 5,243,818 source characters is a direct
work-count witness. Keeping quote/comment/collection state across physical lines
removes that repeated prefix work, provided string escape/triple-quote boundary
semantics are preserved and differential conformance tests cover chunk boundaries.

For single-line documents, parser reuse or a compact validated statement IR can target
the larger tree-construction cost, but any proposed parser/event API must still check
scalar validity and whole-document ownership. The existing upstream reader APIs have
previously been shown to copy input and omit relevant semantic checks; these results
do not change those correctness limitations. The appropriate next experiment is a
same-fixture before/after comparison on the approved new semantic model, with complete
and incremental-edit correctness checked independently, not a repeated 100 MiB run.

## Reproduction

From repository root, use the retained scratch harness and preserve the baseline DLL:

```powershell
dotnet build .temp/toml-cost-probe/toml-cost-probe.csproj -c Release -v quiet
$env:DOTNET_TieredCompilation = '0'
$exe = '.temp/toml-cost-probe/bin/Release/net10.0/Mote.Tests.dll'
foreach ($fixture in @('nested-aot', 'long-quoted', 'many-short-keys',
                      'multiline-quoted', 'comment-lines')) {
    foreach ($mode in @('parse-false', 'parse-true', 'ownership', 'boundary', 'full')) {
        dotnet $exe $fixture $mode
        if ($LASTEXITCODE -ne 0) { throw "Probe failed: $fixture $mode" }
    }
}
```

Building after production changes measures a different binary: retain its new hash
and do not silently label it as this baseline. The baseline output has 25 mode files,
five rows each (125 timed rows), and the preserved default-tiering batch has another
125 rows. There is no automatic favorable-sample filtering or retry.
