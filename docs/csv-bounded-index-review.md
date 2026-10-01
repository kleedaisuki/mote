# Independent review: bounded CSV retained index

Date: 2026-09-30. Scope: the uncommitted `CsvIncrementalSession.cs` bounded-index change, `CsvIndexBudgetTests.cs`, existing windowed contracts/tests, and `csv-index-budget-design.md`. Review did not change production or owner tests, repeat the owner's completed CSV suite, or exercise Native AOT target hosts.

## Outcome

No remaining production blocker found in the inspected revision after the `DenseFull` legacy-projection gate was added. This is a correctness/structural review, not a four-RID performance or managed-retained-graph acceptance.

### Resolved material finding

The initial `Project` implementation tested legacy whole-projection eligibility against `cache.Segments` alone. A `SparseFull` cache has an empty segment list, so the <=4,096 rows / <=8,192 cells predicates were vacuously true for a source below 1 Mi UTF-16 units. A 262,145-record `a\n` source (524,290 units) could bypass bounded legacy projection. The owner independently identified this and added `cache.Mode == CsvCacheMode.DenseFull` before this review's reproduction ran. The corrected production probe returned 4,096 rows / 4,096 cells, `Complete`, total zero. Preserve whole-range and narrow-range regression tests for this transition.

## Checks and evidence

- `ScanFull` accumulates exact row errors plus width mismatches while building new certified checkpoints from offset zero; a same-version certified prefix is accounted before resume. Dense blocks are abandoned once the 256-block cap is reached, before retaining another block, and parsing continues in the same authoritative scan.
- Dense incremental candidates flush at 1,024 rows. Both parsed-block insertion and reused suffix appending enforce the segment cap before retaining an excess segment. Overflow requests an authoritative streaming full scan rather than pretending partial reused summaries form a complete result.
- New-version `SparseFull` input does not reuse its old total/checkpoints. Visible analysis returns the bounded prefix and explicitly provisional output where necessary; full analysis reconstructs fresh whole-file validation. Empty prior dense caches fall back to full scanning after insertion.
- Candidate cache/projection are built locally; the last cancellation check precedes the single `_cache` replacement. `ScanWork` is call-local and its statistics are published only after success. No inspected path mutates a previous committed row list/checkpoint array.
- Sparse seeks use a verified previous checkpoint on exact equality. This preserves preceding-record ownership at zero-width checkpoints and EOF. Unterminated/quoted-CRLF records cannot supply a checkpoint inside a logical record.
- The 256-live-segment estimate conservatively charges a full 1,024-row backing array and histogram per segment, including slices sharing an array; the reachable number of distinct row arrays cannot exceed live segment count. The estimate is not a measured CLR/AOT layout contract. The owner must still provide structural/retained-graph evidence and target-host measurements as specified in the design.
- Independent deterministic differential probe: 24 malformed/random 254-unit suffixes containing comma, quote, CR, and LF, behind 262,145 valid `a,b\n` prefix records. Sparse head/tail suffix windows were compared to translated small dense-session row spans, cell spans/values, diagnostic codes/spans, and exact totals. **24 comparisons, zero mismatches** (CoreCLR Release, Windows, `Random(9173)`). This is an independent projection oracle over a common parser, not an independent CSV grammar implementation.
- `git diff --check -- src/Mote.Formats/CsvIncrementalSession.cs` found no whitespace error; only the repository's CRLF-to-LF notification.

Reproduction artifacts: `.temp/csv-bounded-review/Probe.csproj` and `Program.cs` (current program is the 24-suffix differential probe). Run `dotnet run --project .temp/csv-bounded-review/Probe.csproj -c Release`. The earlier legacy-bound probe was superseded by this program; its exact fixture and result are recorded above.

## Limits and remaining acceptance

The reviewed tests initially exercised cancellation before entry, rather than a controlled cancellation immediately before commit. Inspection supports atomic cache publication, but this review does not claim a deterministically injected mid-scan/precommit cancellation experiment. Long quoted rows remain intrinsically expensive to reparse; no GUI fluidity or input-to-compositor guarantee follows from correctness here. Target-host allocations/RSS, CoreCLR session-retained graph, distinct-width histogram stress, and all-four-RID results remain the owner's measurement work, not cleared by this review.
