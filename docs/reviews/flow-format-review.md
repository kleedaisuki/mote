# Independent Format Flow review

Date: 2026-09-30. Scope: additive `RenderContracts.cs`, `MarkdownRenderProjection.cs`,
`MarkdownIncrementalSession.cs`, `RenderProjectionTests.cs`, and `flow-render-projection.md`.
Production code was not edited. This review does not approve native integration, image/resource
handling, Grid rendering, target accessibility or a product performance SLA.

## Resolved finding (no remaining demonstrated blocker in this scope)

### Resolved P2: List-item marker ownership lost empty/nested-only items

Location: `src/Mote.Formats/MarkdownRenderProjection.cs`, `Add`, list-item/container branch
(lines 39–45 at review time). Confidence: high; independently executed reproduction.

An empty `ListItemBlock` has zero children. Its marker is passed from the parent `ListBlock`,
but the only emission path is recursively attaching that marker to child zero. The loop
therefore emits nothing for an empty item. With `- ` the result is empty display text and
zero paragraphs. With `1. \n2. two` the result is exactly `2. two\n`: the first ordered item
is missing. Both results are marked `Complete` and not truncated, so there is no presentation
omission indication. This is a fidelity defect in the admitted CommonMark list feature,
not an image/Grid extension or a semantic-certification defect.

Follow-up: the worker added marker-only emission for zero-child list items. Independently rerunning the same probe confirms `- ` and `1. \n2. two` now preserve their markers. However the same ownership defect remains for a nested-only item: `1. - child` yields only `• child\n` with a depth-2 paragraph, losing the outer `1.` item; `- - child` similarly loses its outer bullet. Reference-definition-only and definition-first-with-body cases were probed and work with the current Markdig AST shape. The remaining nested-only correction was subsequently implemented and independently verified (see below).

Remedy: make list-item presentation own the marker independently of child existence. Emit
a bounded marker-only list-item paragraph for an empty item, and ensure exactly one outer
marker is preserved when the first visible child is nested or an initial child is hidden.
Keep markers non-navigable and source origins item-precise. Add empty first/middle/final
ordered/unordered and nested-only/reference-definition-first regressions, including caps.

Specification: [CommonMark 0.31.2, list items and lists](https://spec.commonmark.org/0.31.2/#list-items)
explicitly admits empty items and keeps their list structure; its ordered start belongs to
the first logical item, not the first visible nonempty paragraph.

Reproduction project: `.temp/flow-review-probe/Probe.csproj` (repository-local, untracked).
Command: `dotnet run --project .temp/flow-review-probe/Probe.csproj -c Release --artifacts-path .temp/flow-review-probe/artifacts`.
The probe also exercised escaped literal text, unresolved bracket/image delimiters, nested
lists, entities, inline styles and thematic breaks. Its builds completed successfully.

## Assessment of remaining examined contracts

- Disjoint run ordering, immutable array copies, bounded text/paragraph/run admissions and
  reserved non-navigable notices are coherent. Surrogate clipping/normalization and verified
  equality prevent the examined literal runs from claiming guessed affine origins.
- Ordered literal and nested strong/emphasis flags are derived from private syntax, not
  guessed from semantic-node values. Existing precise-location pipeline is unchanged.
- Exact retained syntax preserves reference resolution across viewport requests. Large local
  reparses remain explicitly provisional; distant definitions do not earn complete evidence.
- Source displacement is carried separately from retained syntax, avoiding mutation of parser
  coordinates. Local replacement arenas are admitted at 64 Ki units and at most sixteen
  successful reused edits before rebuilding. Cancellation precedes state/counter commitment;
  Dispose clears retained run references. These are admission bounds, not measured heap/RSS.
- Projection lifetime excludes snapshots/private AST/native handles. Calling Analyze then
  Render without concurrent/intervening session mutation remains a caller contract.
- No additional demonstrated blocker was found in these examined paths. Existing reported
  62/62 unit validation was read as owner evidence, not rerun wholesale. This review adds
  the independently executed empty-list counterexample missing from that suite.

## Explicit integration/performance limits

No Windows/macOS rich-text rendering or Native AOT publication was executed by this review.
The new retained full AST/source arena is a real memory cost; its documentation correctly
avoids claiming measured residency or latency. An exact paragraph larger than the display
budget is projected from the paragraph beginning, even if source interest is far inside it;
Native source-follow usability should explicitly exercise that case rather than infer it
from bounded output. This observation is an integration limitation, not an additional
correctness finding under the current whole-block projection contract.



## Focused resolution re-review

The structural correction now makes each `ListItemBlock` own its marker. A bounded search
finds its first visible descendant without crossing a nested-list boundary. A descendant
leaf shares a paragraph with the marker; an empty or nested-only item receives its own
marker-only paragraph before descent. Container forwarding consumes a marker only after
actual display output, not by child index. Search depth/count limits are finite; emitted
paragraphs still pass through the existing reserved-budget admission path.

Independently executed the repository-local probe against current production code after
the correction. Results now include `1. \n• child\n` for `1. - child`, two bullet paragraphs
for `- - child`, and `1. \n• child\n` for `1. > - child`. Direct quoted leaves still share
the correct outer marker; nested-first then trailing paragraph does not duplicate it.
Initial empty and reference-definition-only cases remain correct. Inspected source spans,
depths and marker metadata in serialized probe results. The worker's seven focused tests
are additional owner evidence, not a substitute for these independent executions.

The reported P2 is resolved. No further substantive finding arose from this focused
re-review. The earlier native integration/performance limits remain unchanged.

## Shared hidden-syntax budget re-review

Inspected the final additive `MaxSyntaxVisits = 8192` change and its focused hidden-node
regression. `Full` now includes this counter; `Add` charges before skipping invisible
reference groups, `Inline` charges each admitted inline, and marker-owner inspection charges
against the same counter. Limits are checked before increments, preventing counter overflow
or separate unbounded marker inspection. Existing enclosing loops stop when Full becomes true;
cap exhaustion creates a non-navigable omission notice without changing semantic completeness.

The focused test intentionally constructs private reference-definition groups because no
visible text/run/paragraph cap would detect their traversal. It admits exactly 8192 groups,
asserts Full, attempts one more and verifies explicit truncation/notice while semantics remain
Complete. This directly targets the newly identified bypass rather than reasserting a text cap.
Worker execution evidence: focused hidden-node plus empty/nested-item cases 7/7 Release with
warnings as errors. Reviewed source/test; did not repeat that completed validation.

No substantive defect found in this targeted budget change. Format-only review is complete:
previous P2 resolved; no remaining demonstrated blocker within the inspected scope.
