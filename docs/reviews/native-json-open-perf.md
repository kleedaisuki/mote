# Native JSON Open attribution: independent performance review

Date: 2026-10-01. Reviewed after the owner's explicit final freeze. Scope:
`benchmarks/NativeJsonOpenAttribution`,
`docs/performance/native-json-open-attribution.md`, relevant Engine Open/rope
implementation, and retained local metadata, rows and summaries. No product,
harness or CI source was modified by this review.

## Verdict

**No unresolved substantive finding in the frozen diagnostic change.** Accept it
as a bounded attribution/reproducibility artifact, not a product optimization or
latency gate. Reject A/B as implemented. Candidate C merits a narrowly scoped
real-Engine, uninstrumented integration experiment; this review does not approve
shipping C or assert a cross-platform performance gain.

## Resolved finding: stale binaries could acquire new preparation metadata

Original location: `prepare.py:prepare` and `run.py:run`. Preparation overwrote
copied sources and `prepared.json` while preserving `publish/Probe.exe`. A caller
who prepared again but missed republishing could execute an older binary under
new source/template identities. Recording the binary SHA identified an artifact
but did not bind it to the preparation. Impact: incorrect experimental provenance
and potentially attributing an old scanner/timer implementation to a new one.
Confidence: high, direct executable path; P2 for a diagnostic benchmark.

The frozen implementation requires empty scratch, embeds a unique preparation
fingerprint, checks the compiled `identity` before oracle/fixture generation and
checks it on each accepted row. It additionally verifies Engine inventory/hashes,
template hashes and generated-source hashes. The retained actual mismatched-
manifest experiment and fresh fingerprint-enabled Native AOT runner support the
correction. This is an accidental-stale-build guard, not cryptographic build
attestation. No unresolved finding remains on that path.

## Why the controls are usable, and what they do not establish

- Baseline references the real unchanged Engine. Copy-control disables timers
  while retaining the isolated copy's altered control flow and candidate branches.
  `phases` uses that same copy with timers enabled. They expose copy/timing effects
  rather than assuming instrumentation is free. Native code layout and inlining
  need not be identical between the real and copied implementations.
- Modes rotate within each fixture/repetition and pairs use that repetition,
  avoiding all-baseline-first ordering. This is deterministic rotation, not
  randomization. Six repetitions do not fully balance four-mode positions and do
  not establish scheduler causality, independent-host uncertainty or a tail SLA.
- Phase timers have non-overlapping intended sequential scopes; asynchronous
  waits and GC inside a scope remain its wall time. Read includes EOF; rope is
  not exclusively a newline scan. Residuals remain separate. Unstable baseline/
  copy/control ordering cannot support subtracting a fixed timer overhead.
- Fixtures are generated and SHA-scanned before fresh-process samples. This is
  OS-cache-warm ingestion, not disk-cold or JIT-warmup measurement. Native AOT has
  no runtime JIT, but first-use initialization and OS/cache/scheduling costs remain.
  The documented Native AOT publish commands and retained publish logs matter;
  `.NET` in a result's framework description alone is not an AOT attestation.
  [Microsoft's Native AOT contract](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
  explicitly separates publish-time compilation from runtime JIT.
- Hosted GUI durations and isolated local Engine cohorts are correctly separated.
  Blank-source startup, requested-file editable, draw submission and physical
  presentation are different endpoints. Local phase ratios must not be applied
  as a numerical decomposition of a different hosted machine's Open.
- CPU, process allocation, point working set and forced-GC live heap are distinct
  counters. Yield/forced GC are outside Open; yield is not a formal producer-
  lifetime barrier. No claim of retained memory equaling cumulative allocation or
  a complete GUI peak is made.

## Corpus and semantic checks

The ASCII corpus gives byte length equal to UTF-16 length. The 100 MiB JSON
fixture is 104,857,600 units / 2,097,152 lines. Each 10 MiB shape is exactly
10,485,760 bytes; dense LF and CR have 5,242,881 lines, dense CRLF 3,495,254,
and no-newline one. Pattern truncation is intentional; the independent byte
oracle corrects CRLF spanning its 64 KiB blocks. None of these fixtures is a
multibyte or invalid-decoder workload; broader decoder coverage is a future
integration obligation, not evidence supplied by this experiment.

The differential oracle tests aggregate line metadata, every small-string offset,
all line starts/content and sampled larger offsets with adversarial leaf splits.
It switches scanner flags for offset queries too. For C, absence of CR makes the
original break count exactly the number of LF characters; CR-containing spans
retain the old scalar algorithm. Cached branch CRLF correction is unchanged.
This provides strong scoped equivalence, not full edited-document acceptance.

Independent portable protocol checks: **7/7 passed**. Reviewed final attested
fresh-run metadata/rows and retained line-check report; owner reports 6/6 bounded
attribution children and 59,088 differential snapshots / 1,714,191 queries.
This review did not rerun the large Native AOT cohorts or the GUI pilot.

## Candidate C judgment

| Observation | Supported interpretation |
| --- | --- |
| JSON: baseline median 256.597 ms, C 230.325; mean paired -27.428 ms, all six differences favorable | Strong same-host candidate signal, not shipping GUI speedup |
| Same-cohort `phases` Open 250.056 ms vs C 230.325; rope phase 67.303 vs 47.858 | Mechanism is consistent with cheaper rope statistics; timer/copy effects are still present |
| LF/CRLF/CR six-pair intervals span zero with mixed directions | No demonstrated coherent regression, and no equivalence proof for performance |
| Original none C sample 38.814 ms, paired +13.865 ms | Retain unexplained outlier; do not label it OS noise or delete it |
| Same-binary none followup, 12 pairs: mean -0.048 ms, interval [-0.560,0.486] | Does not reproduce a coherent none regression; does not explain the earlier outlier or bound rare tails |

The followup metadata binds the same binary and fixture, and the report keeps it
separate rather than pooling away the first outlier. Small bootstrap intervals
are explicitly descriptive within-host resampling; repeated selection among
three variants and one developer host prevents population-level inference.

**Next discriminating experiment:** compare real old/new Engine AOT binaries
without phase/candidate-switch scaffolding, randomize matched order within each
RID and retain exact length/line/edit/CRLF tests. Include representative LF JSON,
dense CR/CRLF/LF, no-newline and multibyte inputs, short/partial reads and relevant
line-offset/edit queries. Measure whole Open and allocation/GC, not just scanning.
Do not demand an architecture rewrite to evaluate this two-line fast path, and
do not reject it merely because inconclusive small-shape differences have mixed
signs. A demonstrated material supported-workload regression is grounds to keep
the original implementation.

## Limits

This review validates coherence of the diagnostic design, recorded interpretation
and bounded control fixes. Legacy scratch cohorts predate the fingerprint guard;
their retained binary/publish/source records were inspected but not rebuilt as
identical binaries. No macOS/ARM64 performance, physical interaction latency,
semantic-certification duration, release readiness or safety of an unexamined
future production integration is implied.
