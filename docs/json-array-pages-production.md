# JSON root-array semantic pages: production contract

2026-10-01. Implements the scoped design in
[json-array-semantic-pages.md](json-array-semantic-pages.md). This is a parser and
versioned semantic-reuse implementation, not proof of native startup/input/paint
acceptance or general large-JSON incremental coverage.

## Representation and invariants

`JsonIncrementalSession` keeps one private `JsonArrayCertificate`. A certificate
contains a version, document length, root-array shell extent, and immutable staged
page summaries: start/end UTF-16 offsets, root element count, exact object-local
duplicate count, dirty flag. It retains no AST, decoded names, source string or
strong snapshot reference. A weak current-snapshot reference checks same-version
identity; reusing a different document with the same numeric version fails closed.

The ordinary authoritative Full parser harvests pages in the same pass, at validated
root-array value boundaries after roughly 64 Ki units. Every separator has one
owner. A final page ends at the root closing bracket; a nonfinal page ends at the
next value token after its comma and whitespace. There is no per-element index.
A giant value remains indivisible. Admission stops at 16,384 pages without weakening
the established Full recovery domain. A syntax error, recovery uncertainty, depth
limit or exact-key-index refusal prevents composition; duplicate keys alone do not.

`TextSnapshot.GetChunks(start, length)` borrows immutable leaf ranges directly,
including middle/end pages, without copying a document prefix or creating a
synthetic bracket wrapper. Root-array children invoke the same production value,
escape, depth and exact decoded-key routines with the root-array depth accounted
for. Existing public format/session/result APIs and Native AOT dependencies stay
unchanged.

## Versioned edits and malformed intermediates

Every edit is mapped in its own BeforeVersion coordinates. Only replacements
strictly inside one page are admitted. Shell edits, exact seams, crossing owners,
missing chains and impossible lengths revoke reuse. An admitted edit dirties its
owner and shifts later metadata. Local parsing must consume the hard interval and
prove the expected final/nonfinal grammar before its count becomes current truth.
An unterminated quote cannot borrow a later owner's comma or bracket.

Dirty candidates survive successful provisional publication so a following quote
repair can restore Complete without a whole-file rescan. Call-local mapping,
repair, diagnostics and projection are followed by a final cancellation check and
one state replacement. Cancellation does not publish a new version, consume the
edit chain or mutate prior pages. Undo/Redo are ordinary new versions.

## Work and output boundaries

| Request | Production behavior |
| --- | --- |
| Small document (at most 1 Mi units) | Existing authoritative parser/recovery behavior; Full keeps the complete small tree. |
| Cold large Visible | At most 256 Ki charged source visits; failure to establish the whole grammar returns Provisional and unknown total. No independent subtree is fabricated near EOF. |
| Certified large Visible | Dirty owners and intersecting projection owners share a 512 Ki visit ceiling. Offscreen owner counts are reused exactly once. |
| Oversized dirty owner | Budget refusal keeps dirty state Provisional; explicit Full can validate authoritatively. |
| Clean certificate with oversized display owner | Complete global validation/count remains justified; the bounded display may omit descendants. |
| Explicit large Full | Reuse strict clean/repairable owners; otherwise stream the established authoritative fallback, harvesting a new candidate in that pass. No interactive work ceiling. |

Charged visits include repeated grammar lookahead, range decoding copies, exact
key comparisons and bounded key-reader lookahead, not only interval lengths.
Unbounded Full deliberately does **not** maintain a per-character accounting
counter; `LastVisitedUnits` is null for those turns, not a claimed zero. This avoids
an experimentally observed cold-path regression while retaining hard interactive
limits. Exact-key budget context is passed call-locally rather than adding fields
to every transient key table/reader; the enlarged-layout candidate also incurred a
measured 6.3% cold allocation regression and was rejected. Full remains cancellable
at grammar/key loop checkpoints. String
cancellation uses position thresholds rather than exact modulo equality, because
six-unit Unicode escapes can skip every modulo boundary.

Projection always uses current absolute source spans. The document/root-array
spans cover their full constructs; descendants/tokens/diagnostics are a bounded
subset. Complete denotes whole-document semantics and an exact global count,
not an unbounded displayed tree. Provisional never carries a stale exact total.

## Validation and measurements

- [Directed and seeded tests](validation/json-array-semantic-pages-tests.md).
- [Independent source review](reviews/json-array-semantic-pages-review.md).
- [Reproducible benchmark and negative candidates](json-array-pages-performance.md).

The early counted-every-read candidate had a material 100 MiB cold Full regression:
ordinary LF median 564.79 to 770.14 ms (+36.36%), CRLF 577.09 to 744.80 ms (+29.06%).
Inlining reduced but did not eliminate it; paired medians still exceeded 5% on
several corpora. The first null-Full-counter specialization recovered 100 MiB
latency (LF +0.45%, CRLF +1.40%, compact -2.48% median ratios), but enlarged transient
key object layouts still added approximately 6.3% ordinary cold allocations. Those
candidates were rejected, not promoted as a complete performance win.
The final source specializes accounting to bounded interactive turns and passes
key budget context without enlarging transient key objects. Final matched-source
CoreCLR measurements support this bounded parser slice:

- 100 MiB LF cold Full median 583.234 to 582.567 ms (-0.11%); CRLF 577.919 to
  593.208 ms (+2.65%) in the first five pairs. CRLF's pairwise median was +7.29%
  with a +23.35% outlier; ten additional focused pairs yielded +0.406% pairwise
  median (all ten at most +4.51%). The original outlier is retained, not excluded.
- Ordinary cold allocation is effectively unchanged (LF -0.25%, CRLF +0.21%
  medians). Compact primitive Full adds 114,320 bytes (+46.22% relative to its
  tiny baseline allocation), an explicit source-sized index admission trade-off,
  while its 100 MiB cold latency improves. No universal allocation win is claimed.
- After one Full, 200 local plus 5,000 dispersed edits on each 100 MiB corpus stay
  Complete with exact counts/version and at most 512 Ki charged visits. With
  the file-open producer unwound before timing, dispersed p95 is 0.631 ms LF,
  0.595 ms CRLF and 2.251 ms compact; these are managed Analyze times, not input
  or paint latency. The certificate graph is approximately 32 KiB.
- A first inline-await benchmark retained original file-open producer chunks,
  confounding steady-state memory. An engine-only control reproduced that growth;
  the explicit-unwind discriminator reduced LF post-edit forced-GC growth to
  21,547,120 bytes. Certificate retention is measured separately. The Engine owner
  handles the producer lifetime issue independently.

Full measurements, identities, tails and reproducibility are in the linked
benchmark evidence. Native AOT builds and actual native interaction/paint remain
separate promotion gates; no GUI latency follows from these parser timings.
