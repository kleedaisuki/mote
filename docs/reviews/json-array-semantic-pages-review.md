# JSON root-array semantic pages: independent implementation review

Date: 2026-10-01. Scope: uncommitted `src/Mote.Formats/JsonIncrementalSession.cs`
and `src/Mote.Formats/JsonArrayCertificate.cs`, compared with
`docs/json-array-semantic-pages.md`; surrounding `TextSnapshot` range contracts
and the current `JsonArrayCertificateTests` were inspected. Production source was
not modified. This is static review, not a new execution of the already reported
28 expanded JSON page tests, a Native AOT result, or product acceptance.

## Findings

**No unresolved substantive finding after the source correction below.**

### Resolved P2: escaped strings could skip every periodic cancellation checkpoint

Location: `Parser.StringValue`, loop-entry cancellation check
`(_position & 4095) == 0`; `SourceWork.Visit` no longer checks cancellation.
Trigger: a giant string consisting of repeated `\uXXXX` escapes whose first
content offset is odd, for example an array beginning `[ "` followed by repeated
`\u0061`. Every string-loop entry advances six UTF-16 units and remains odd.
Consequently no entry equals a multiple of 4,096. Full can read the entire giant
escaped string without observing a concurrently canceled token until subsequent
grammar or publication. Visible remains source-budget bounded, but this violates
the proposed cancellable Full cadence and matters to background analysis.

Executed arithmetic discriminator: first content offset 3, 200,000 six-unit
escapes, 1,200,000 raw units: zero loop-entry checkpoint hits.

Confidence: high, direct arithmetic/control-flow path; not a measured timing
claim. The legacy position-modulo check already had this limitation, but removing
the new per-visit check leaves it uncorrected in this implementation. Use a
threshold/countdown or bounded iteration cadence that does not rely on visiting
an exact absolute offset. Add a directed odd-offset escaped-string cancellation
case. Key-table growth checks each 4,096 entries, keywords have at most five
units, and large-document displayed scalar decoding is bounded to 16,384 units.
These are bounded but are not uniformly 4,096 raw source visits.

Resolution re-reviewed in the current uncommitted source: `StringValue` now
initializes a next-position threshold and checks `_position >= nextCancellation`,
then sets the next threshold 4,096 units later using a long intermediate. Unicode
escape steps therefore cannot evade a checkpoint; overshoot is at most five units.
Final allocation refinement re-reviewed: `KeyTable` and `KeyReader` no longer
retain added work/token fields. The production Add call passes the nullable shared
budget into exact comparisons, reader Next/Read, and hot-key comparisons; duplicate
label reads also pass it. Source visits and bounded window copies remain charged.
`EqualsDecoded` checks cancellation against a long threshold on the sum of both
reader positions. It checks immediately and then after approximately 4,096 raw
consumed units, with at most eleven units of paired Unicode-escape overshoot.
Lookahead copies can add two 4,096-unit window fetches between those consumption
checks; this is a cursor-consumption cadence, not exactly 4,096 total memory reads.
The duplicate-label path consumes at most 257 decoded characters (at most 1,542
raw escape units), so it cannot hide an unbounded comparison without its own token.
A refused bounded budget checks cancellation before raising its internal budget
exception. String thresholds and key-table-growth cancellation remain independent
of nullable accounting. No unrelated tests were rerun for this static correction
review.

The remainder of this review identifies no additional substantive source defect.
Pending performance/native experiments remain outside this static assessment.

### Hard seam and strict proof

- Full harvesting occurs only in the root array (`_depth == 1`) and creates owners
  after an entire value plus its separating comma/whitespace. The final owner ends
  before the closing root bracket. A global syntax/recovery/resource refusal
  prevents publication of any harvested certificate.
- `ParsePage` retains root-array depth context and calls the actual production
  value, object-key and decoded-key comparison routines on a hard range. Final
  owners require EOF after a value/whitespace; nonfinal owners require EOF after
  the comma/whitespace. Missing delimiters, truncated strings/literals, trailing
  commas inside values, unexpected characters and recovery mark the page invalid.
  A changed quote cannot be certified merely because an old suffix is unchanged.
- The `_syntaxError` flag is a new admission restriction, not a restriction on
  legacy authoritative `Parse()` completeness. Duplicate diagnostics deliberately
  do not set it. Root-object and malformed-input Full continue through the old
  `_uncertain`/recovery domain. Dirty Full that cannot regain strict page validity
  falls back to the authoritative parser rather than inventing an exact count.

### Mapping and publication

- Mapping consumes each edit in its BeforeVersion coordinates, checks version
  chaining and final length, rejects shell/exact-seam/cross-owner edits, marks the
  owner dirty and shifts later owners plus the array end. Dirty metadata is never
  summed as current authoritative truth.
- Mapping clones the committed owner array. Repair clones the mapped array; parser
  output and repaired counts are call-local. Only `Publish`, after the hook and
  final cancellation check, replaces the committed candidate, weak snapshot
  reference and visit metric. Exceptions/cancellation before it preserve the prior
  version and let the same edit chain be retried.
- Same-version reuse requires exact snapshot identity and no edits. The snapshot
  reference is weak; owner metadata stores integers/flags only. Parsers, decoded
  hot keys, comparison windows and projection trees are not retained in the
  certificate. No historic source is retained by this new cache.

### Work limits and projection

- Cold large Visible uses a shared 256 Ki grammar-read budget. Warm repair and
  projection share 512 Ki; cached local output avoids reparsing a repaired visible
  owner. Budget refusal cannot be translated into cancellation or a strict proof.
- Exact-key collision readers and raw hot-key comparisons charge the same budget.
  Grammar/key loops also retain cancellation checks. Full remains unbounded by
  the interactive limit and cancellable.
- Projection reparses only intersecting owners, retaining absolute offsets and
  production child/token/diagnostic clipping. Document/root-array spans remain
  full construct spans. Duplicate totals sum each clean owner once, independently
  of viewport. A clean certificate can truthfully retain its exact global count
  when an oversized owner's display is budget-refused; dirty refusal instead
  publishes Provisional without an exact total.

## Material acceptance limits, not source findings

1. For bounded interactive turns, `LastVisitedUnits` measures grammar character accesses and exact-key comparison
   accesses, source slices and comparison-window prefetch in the updated source.
   It does **not** count `JsonDocument` decoding of a bounded displayed string, metadata
   mapping, hash-table growth or projection allocations. It is an algorithmic
   parser/key-read bound, not total memory traffic or a GUI-time guarantee. Reports
   should preserve this distinction; profile total time/allocation separately.
2. The expanded independent suite reports **28/28 passing**, including 100 directed
   actual seam/shell edits and 1,000 seeded nested grammar replacements. Accepted
   Complete results compare current projection/counts with a fresh authoritative
   Full plus independent syntax and hand-maintained duplicate expectations.
   Additional cases cover two distant dirty owners, whole-value replacements,
   final-owner refusal/repair, nominal-cut escapes/numbers and weak snapshot GC.
   See [test evidence](../validation/json-array-semantic-pages-tests.md). The
   reviewer inspected the expanded test source/report, but did not rerun it.
   These results are not the 5,000 sequential-edit, 100 MiB retained-byte,
   16,384-page-cap or native input-to-draw experiments.
3. Stage-hook cancellation tests exercise page entry and precommit cancellation,
   not asynchronous cancellation while inside a giant string or decoded collision
   comparison. The corrected threshold/refill checks are present; measured 5/10 ms responsiveness remains
   a separate acceptance obligation.
4. This review does not execute or establish cold Full performance. Separate
   [production evidence](../json-array-pages-production.md) records rejected
   counted-every-read candidates with 29–36% slowdown, followed by an inlined
   candidate still showing sustained 5–7% slowdown on several corpora. The final
   source specializes accounting: `Parser._work` is null only for unbounded turns;
   bounded Visible parsers, slices, exact-key comparisons and lookahead retain the
   shared hard ceiling. `LastVisitedUnits` is null for unmeasured Full, not zero.
   Cancellation remains independent in the corrected grammar/key loops and final
   publication check. This specialization introduces no observed correctness or
   cancellation defect on static re-review, but its speed benefit is unverified
   here until the separate matched final-source benchmark completes. A subsequent
   refinement removes the additional per-object/per-reader accounting fields,
   after the implementation owner reported a 6.3% cold-allocation regression.
   The budget is now passed call-locally; static tracing finds no missing production
   propagation. This report does not independently establish the final allocation
   or time improvement.

## Recommendation

Proceed to the delegated measurements and target-platform validation without
adding speculative parser machinery. Preserve the hard final/nonfinal seams and
single cancellation-gated state replacement. Promote only the claims supported
by measured artifact results; retain truthful provisional display/global-count
separation and legacy Full fallback in user-facing documentation.





