# Semantic IR evolution: certified owners, bounded projections

Status: architecture decision and falsifiable implementation plan, 2026-09-30. No production change is implied by this document. The immediate purpose is to make **one file's** semantic facts reusable across edits and several visible source ranges without introducing a universal parser, an LSP, or another owner of text.

## Decision in one paragraph

Keep `Document`/`TextSnapshot` as the sole source of text. Keep each `IFormatSession` as the sole owner of its format's syntax and semantic state. The next useful intermediate representation (IR) is **not** a common JSON/TOML/YAML/Markdown/CSV tree: it is a small, versioned **projection batch** backed by policy-private **certified semantic owners**. An owner is the smallest source unit for which that policy can prove (1) an exact boundary, (2) which local facts it owns, and (3) which outside facts those facts read. A projection batch asks once for a normalized set of visible intervals and returns bounded nodes, tokens, and diagnostics in absolute UTF-16 coordinates; Native turns those source-mapped nodes into bounded preview runs. The proof belongs to the format, never to a generic hash or viewport heuristic. Where no proof exists, the owner is the whole document or the answer is `Provisional`—the same safe behavior as today. Implement the first narrow version in CSV, whose record checkpoints and row-width dependency already exist; promote the envelope only after it survives differential and 100 MiB tests.

## Current ground truth and historical correction

The architectural boundary in [architecture.md](architecture.md) is right: `Mote.Engine` owns snapshots/edits; `Mote.Formats` owns policies; the Native shell sees semantic projections. But [incremental-plan.md](incremental-plan.md) still opens by saying only Plain/CSV/Markdown have sessions. **That sentence is stale:** `IncrementalContracts.cs` now defines `IIncrementalDocumentPolicy`, `IFormatSession`, `AnalysisRequest`, `DocumentAnalysis`, and truthful `Provisional`/`CoveredRegion`/`Complete`, and all six formats have session files. API shape is not incremental implementation. CSV reuses a logical-record index and Markdown reuses a restricted certified block index. JSON and YAML stream the entire authoritative snapshot on large-file analysis, TOML restreams logical statements on large `Full`, and Plain needs no parser cache. At committed HEAD, the Native controller requests **one** `VisibleRange` and `NativePreviewBuilder` emits at most 16 Ki UTF-16 preview characters/120 lines with `NativePreviewSpan` source maps, but there is **no committed preview-activation callback**. An in-flight, unmerged `NativePreviewActivation`/`NativePreviewNavigation` route proposes generation/version checks; an initial Windows probe's `0xC0000005` was isolated to an invalid cross-process pointer-bearing `EM_EXSETSEL` probe, **not** a proven product-subclass crash. A safe scalar-probe rerun and target-OS navigation acceptance remain pending. The older [release-gaps.md](release-gaps.md) preview-interaction gate therefore remains open.

Existing coverage has two distinct axes: `Complete` asserts whole-document **semantic validation** with an exact total diagnostic count, while the returned node/token projection may remain bounded. `CoveredRegion` asserts semantic facts only inside its single contiguous `Coverage`; `Provisional` asserts no semantic validity. A multi-range result cannot be faithfully squeezed into that one `Coverage` span. Do not fake a convex hull of disjoint windows, and do not sum diagnostic counts from overlapping projections.

| Format | Candidate owner and current proof | Dependency that defeats naive local parsing |
| --- | --- | --- |
| Plain | Physical lines, with no semantic dependency; whole-file zero diagnostics is already `Complete`. | None for current policy; search results are a separate derived index. |
| CSV | Quoted logical record with verified end delimiter; existing `Segment` stores row start/end/after, width, and error count. | Expected width comes from the first record; an edit there can change `CSV004` on every later record even when their syntax is unchanged. Header/dialect inference, if added, is another global dependency. |
| Markdown | Restricted Markdig-verified heading/paragraph/closed-fence block with an adjacency certificate; see [block evidence](markdown-block-certification.md). | A reference definition can resolve an earlier use; two adjacent paragraphs merge. Existing block certificate deliberately rejects such input. |
| TOML | Validated logical statement plus the current ownership trie; see [ownership evidence](toml-large-semantics.md). | Headers/dotted keys and array-table current-element context determine path ownership. An independently valid statement is not independently meaningful. Current large `Full` still restreams. |
| JSON | Container/value is a possible future owner, **not** a current reusable certificate. Current large parser streams the whole snapshot, retains bounded projections, and can certify many complete documents. | A quote/comma/bracket change can move the enclosing boundary; duplicate-key checks belong to the object scope. |
| YAML | Document or proved collection may eventually own syntax; today streaming `Full` may certify supported input but edits restream. | Anchors/aliases and mapping-key equality are nonlocal. Undefined aliases in keys correctly revoke completion; see [recovery evidence](yaml-alias-key-recovery.md). |

This table is intentionally asymmetric. The commonality is **ownership and evidence**, not syntax shape or identical invalidation cost. In particular, `Complete` for TOML/Markdown is narrower than all valid language inputs; unsupported paths remain `Provisional`. The current [release gate](release-gaps.md) is whole-file semantics on each claimed domain, not a universal parser promise.

## Data and state model

The following is an illustrative **additive** C# shape, not a mandate to add every type now. The old `IDocumentPolicy` and `IFormatSession.Analyze` signatures stay intact. The optional batch capability belongs in `Mote.Formats`, so Native can request it without teaching Engine any grammar. There is no shipped plugin ABI under Native AOT; format modules are statically linked. A legacy or small-file caller still invokes `Analyze` unchanged.

```csharp
/// <summary>Optional projection capability; a policy may instead use the old one-window API.</summary>
public interface IWindowedFormatSession : IFormatSession
{
    /// <summary>Analyzes one version and projects several disjoint source windows once.</summary>
    WindowedAnalysis AnalyzeWindows(TextSnapshot snapshot,
        IReadOnlyList<VersionedEdit> changes, IReadOnlyList<TextSpan> windows,
        AnalysisScope scope, CancellationToken cancellationToken = default);
}

/// <summary>Versioned bounded presentation, not a second source or format AST.</summary>
public sealed record WindowedAnalysis(
    long Version, AnalysisCompleteness Completeness,
    IReadOnlyList<TextSpan> CertifiedCoverage,
    int? TotalDiagnosticCount,
    SemanticNode Root,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<SemanticToken> Tokens);
```

`CertifiedCoverage` is a sorted, nonoverlapping interval set, **not** a claim about gaps. For `Complete` it is exactly `[0, snapshot.Length)`, even if `Root.Children` are sparse. For `CoveredRegion`, each interval must have a policy proof independent of facts outside the set, or all such dependencies must have been checked in the same version; otherwise return `Provisional`. `TotalDiagnosticCount` is non-null **if and only if** `Complete`. Root children/tokens/diagnostics are deduplicated by source owner and sorted by source start; zero-width error spans get an explicit owning unit. Every span is in the result's one version and inside the file. A source map for preview remains an output run (`preview offset -> source span`); it is not parser state and cannot outlive the matching document generation/version. Any future navigation consumer must make decorative/clipped preview text non-navigable and reject stale stamps; these are design requirements, not accepted product behavior.

Do **not** add a public `SemanticUnit` class that every language must populate. Within a policy, the cheap index entry needs only what that policy can actually certify. For example, a CSV `Row` already contains delimiter/boundary, width and error count; a Markdown `FlatBlock` contains kind, heading level and source interval plus the restricted-grammar admission proof. If a reusable implementation wants to name the idea, use a private `OwnerSummary` with `(span, boundary proof, local facts, dependency reads/writes)`; its proof is a policy-specific predicate, not `ulong hash == hash`. Source offsets of an unchanged suffix can be shifted lazily by the existing segment/run mechanism. Avoid retaining decoded values, a second full source string, or all preview text per owner.

The state transition for one edit is:

```text
snapshot v + edit(v→v+1)
  → locate first possibly affected owner and its required predecessor/context
  → reparse until the policy proves a safe unchanged boundary
  → invalidate semantic dependents, even outside the reparsed syntax range
  → construct requested sparse projection and source maps for v+1
  → commit private cache only after cancellation check and complete validation
  → Native publishes only when document identity + policy + generation + version match
```

If the edit chain has a gap, a boundary cannot be proved, a resource limit is reached, or the parser's recovery skips unknown structure, do not reuse the old certificate. Rebuild from the authoritative snapshot where safe, or publish an honest bounded provisional result. Undo/Redo are new versions, not a rollback of analysis state. A canceled call commits neither cache nor count. These rules are more important than any specific data structure.

### One counterexample that forces dependency ownership

For CSV, begin with `a,b\r\n1,2\r\n3,4\r\n`. Row 1 establishes width 2, so later rows have no `CSV004`. Insert `,c` into row 1 only: `a,b,c\r\n1,2\r\n3,4\r\n`. All later row boundaries and source bytes remain reusable after a coordinate shift, **but both later rows now have width-mismatch diagnostics**. Reusing a suffix of row syntax is safe; reusing its diagnostic truth is not. One global `expectedWidth` dependency invalidates just the width-derived facts, without reparsing every later row's cells. A Markdown analogue is `[use][id]` followed by an offscreen `[id]: https://example.test`: a local block AST alone cannot resolve the earlier link. The existing Markdown certificate rightly refuses that case rather than calling it `Complete`.

## First implementation: CSV sparse projection, not a framework

Extend only `CsvIncrementalSession` with an optional `AnalyzeWindows` implementation and a **single scan/index update** followed by projection of a normalized union of at most eight windows. Native initially asks for the viewport plus one bounded preview-interest window, not a speculative full-document graph. Existing `Analyze` delegates to the same core with a one-window list, preserving its results and binary/source compatibility. With a complete row index, binary-search segments/rows intersecting each window rather than loop through all rows for every viewport; calculate the existing exact global diagnostic count once from segment summaries. For each projected row, capture only cells/tokens that intersect an admitted window, preserve the row's global absolute source span, and deduplicate a long row that intersects two windows. Keep `NativePreviewBuilder`'s 16 Ki/120-line output cap; it may consume the already-bounded projection and must not infer navigation from display text.

The CSV proof is concrete: a committed record boundary has quote state **outside** a quoted field and an exact delimiter; the existing incremental parser restarts at a preceding record and reparses until a translated old delimiter matches. The semantic summary is `(row width, row-local errors, expected width of first row)`. If the first row's width changes, recompute width-derived diagnostics/count from cached widths; if a quote/delimiter edit merges records, continue reparsing until convergence or end. A cold `Visible` still scans only its bounded prefix and must not promise an offscreen window it has not reached: it returns `Provisional` or the actually certified intervals. An idle `Full` can build/validate the index and then serve distant windows cheaply. If retained index capacity is exceeded, a **streaming current-version Full** may still be `Complete` and return bounded projections, but the session must discard the oversized reusable index; the next edit may rescan. Never turn a cache budget into a false semantic claim.

These are acceptance **budgets to test**, not measured performance facts: at most eight source windows and 512 Ki total requested UTF-16 width per call; at most 4,096 projected rows and 8,192 projected cells across the entire batch; at most 16 Ki preview characters/120 lines as today; at most 32 MiB retained format index for a 100 MiB source, with honest no-cache fallback. The exact figures should be tuned against benchmark distributions. User-visible status must distinguish semantic completion from projection truncation; `Complete` does not mean every row is rendered. Start with two windows because that is the dominant interaction; do not add arbitrary query planning machinery.

### Differential and performance gate

1. **Oracle:** after each edit, compare CSV row/cell values, exact source spans, `CSV004` codes/count, completeness, and two-window preview map against a fresh one-window/full parse of the same snapshot. Compare semantic sets, not output ordering accidents. Include first-row width changes, quoted CRLF spanning windows, escaped quotes, empty cells, one 100 MiB record, CR/LF/CRLF boundaries, emoji/UTF-16 offsets, malformed in-progress quotes, and Undo/Redo chains.
2. **Viewport discrimination:** request `[head, tail]` on a >2 MiB file with an offscreen bad-width row. `Complete` after `Full` must preserve the exact global count while outputting only requested rows; a cold `Visible` must not claim that offscreen row checked. Overlap/touching windows merge once; disjoint gaps remain uncertified under `CoveredRegion`.
3. **State safety:** cancel during reparse and retry; inject a missing edit; switch policy and close; verify old-version projections and preview activations are rejected. Snapshot bytes and Save hash must remain unchanged by analysis.
4. **Cost:** run warm near-head/middle/tail edit and two-window scroll on 1/10/100 MiB mixed CSV plus giant quoted-record and comma-run adversaries, both LF and CRLF, on Windows and macOS Native AOT GitHub jobs. Report p50/p95 analysis latency, scanned UTF-16 units, projected rows/cells, allocations, RSS/working-set availability, and executable size. Keep first paint and input-to-compositor measurements separate from parser latency. Reject a change that regresses the existing one-window route without a measured benefit in the intended two-window workload.

After this slice, the next *semantic* experiment should be a restricted Markdown reference-definition/consumer index (ASCII one-line definitions and explicit `[text][id]` uses first), with first-definition precedence and unsafe-link diagnostics checked against whole-document Markdig. That would test nonlocal invalidation rather than syntax reuse alone. It is a proposed experiment, not current completeness. TOML ownership and YAML alias equality should remain policy-specific certificates; JSON can wait for evidence that persistent container indexing beats its current streaming scan on real edits.

## External evidence and rejected alternatives

| External signal | What it supports | What it does **not** buy mote |
| --- | --- | --- |
| [Tree-sitter advanced parsing](https://tree-sitter.github.io/tree-sitter/using-parsers/3-advanced-parsing.html) | Production syntax-tree edit/reparse with structural sharing; range edits must exactly track source, and old externally held nodes need position updates. | Changed syntactic ranges alone do not validate CSV width, Markdown references, TOML ownership or YAML aliases. A native C grammar/binding set also needs a four-RID AOT, size and one-binary audit before adoption. |
| [rust-analyzer architecture](https://rust-analyzer.github.io/book/contributing/architecture.html) | Syntax and derived semantic summaries are separate; its `ItemTree` is stable across body edits, and query-driven invalidation is valuable where dependencies are numerous. | Its project-scale Salsa database/LSP machinery is not justified for six independent single files. Borrow explicit summary/dependency boundaries, not the whole framework. |
| [Typst compiler architecture](https://github.com/typst/typst/blob/main/docs/dev/architecture.md) | Real production renderer keeps source spans stable across local reparses and separates parse, evaluation, layout and export; this is a strong analogue for bounded source-mapped preview. | Typst's expressive language and `comemo` dependency tracker do not imply that mote needs dynamic tracked functions or a whole-document render graph. |
| [Sijm's TU Delft ISGLR evaluation (2021)](https://repository.tudelft.nl/record/uuid:6ddf9fbd-c39e-4aae-b6ce-13389def6a9f) | An academic result: average 99% parse reuse, 9× incremental speedup for <1% edits, but 24% cold-parse overhead and poorer incrementality with nondeterministic disambiguation in its measured workloads. | Those numbers are **not** mote benchmarks and do not include semantic dependencies, Native AOT integration, startup or giant-row behavior. |

Rejected now: (a) a universal lossless AST for six unrelated grammars; (b) treating Tree-sitter changed ranges as semantic validity; (c) exposing private owner IDs as durable public API; (d) a general incremental query engine before one format-specific dependency win; (e) flattening disjoint windows into the old single `Coverage` hull; (f) caching every rendered cell/value. The falsifying result for this decision would be a well-controlled four-RID experiment showing that even the CSV sparse-batch implementation costs materially more in one-window editing and does not improve two-window analysis or preview navigation. In that case, retain the current session indexes and keep projection requests separate rather than preserving an abstraction for its own sake.
