# Independent review: Markdown reference dependency design

**Current assessment after revision:** the follow-up below supersedes the initial
"no demonstrated counterexample" judgment for touching atoms. The original review
is retained to make the hypothesis change traceable.

Reviewed 2026-09-30. Scope: `docs/markdown-reference-index-design.md`, current
`MarkdownPolicy.cs` / `MarkdownIncrementalSession.cs`, and targeted source/results
in `.temp/MarkdownReferenceIndexProbe/`. No production changes, probe reruns, or
incremental implementation validation were performed.

## Judgment

**Proceed to bounded implementation, not to a claim of certified correctness.**
The proposed private definition environment, ordered duplicate lists, missing-key
read sets, use multiplicities, and lazy viewport binding are a coherent way to
extend the existing restricted certificate. I found no demonstrated semantic
counterexample to the explicitly closed, nonnested full-reference domain *when
its symbolic admission conditions actually hold*. Those conditions are a
remaining implementation obligation, not a consequence of the finite probes.
The document generally makes this distinction accurately.

## What the evidence establishes

- [CommonMark definitions](https://spec.commonmark.org/0.31.2/#link-reference-definitions)
  and [links](https://spec.commonmark.org/0.31.2/#links) support document-global
  resolution, first-definition precedence, and normalized label matching. Keeping
  unresolved readers is necessary: a later declaration can change earlier output.
- Pinned [Markdig 1.3.2 definition group](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Syntax/LinkReferenceDefinitionGroup.cs)
  uses an invariant comparer and `TryAdd`, while retaining declaration children.
  Source order, not owner insertion order, must select winners. Excluding Unicode
  from the first normalized-key domain avoids the demonstrated accent/comparer
  tension; it does not solve that tension for general Markdown.
- The current projection emits one unsafe-link warning per `LinkInline` and uses
  the decoded URL through its actual `SafeUrl` predicate. Thus the proposed
  multiplicity formula is justified *if* each admitted atom produces exactly one
  explicit-key link when defined and no other warning-producing link when missing.
  It is not justified merely by counting bracket pairs or unsafe declarations.
- `Program.cs` compares recursive consumer IR, tokens, and diagnostic code/spans;
  its recorded 7,468 matches support selective winning-definition suffix context
  for that generated corpus. It uses the whole parser's winner environment, so it
  does not independently validate declaration indexing or normalization.
- `ChangeProbe.cs` independently reconstructs ASCII winners and checks the stated
  nine versions and warning totals. It rebuilds every version, as disclosed; it
  does not test staged cache transitions, cancellation, reverse-index maintenance,
  owner moves, or lazy epochs. `scale.jsonl` supports the reported exploratory
  numbers, not a retained-index budget or native responsiveness guarantee.

## Implementation-critical clarifications

1. **Make the symbolic admission test compositional or explicitly exhaustive.**
   The random probe has one atom per paragraph; the edit probe repeats only the
   same atom/key. Neither establishes interaction between different neighboring
   atoms under mixed missing/resolved environments. Pinned
   [LinkInlineParser](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Parsers/Inlines/LinkInlineParser.cs)
   consults shortcut candidates and consumes attempted explicit labels, so testing
   one atom in isolation or only the current environment is insufficient as a
   proof method. This is an untested obligation, **not a demonstrated failure**.
   Specify exactly which candidate-key assignments are checked, or prove why
   an admitted plain separator isolates atom behavior. Apply the same label
   alphabet, normalization, nonempty, and length constraints to consumer labels
   as to declarations. Safe/unsafe representative contexts must use the exact
   policy safety predicate, not a scheme-only approximation.

2. **Resolve declaration projection before certifying output equivalence.**
   Selecting the real paragraph/heading owner correctly prevents synthetic group
   leakage. However, current whole analysis also exposes real definition children
   beneath a `construct` group. The design explicitly leaves their bounded IR
   representation undecided. Preserve the established legacy result or document
   an additive bounded-projection contract; do not silently erase real definition
   semantics while advertising exact whole-tree equivalence. The current tests
   compare consumers, not the entire declaration projection.

3. **Treat precise locations as a prerequisite with independent source checks.**
   The experimental oracle intentionally changes the parser option; agreement with
   that copy cannot establish correctness of today's production coordinates.
   Exact substring/source-offset assertions, including non-BMP prefixes and CRLF,
   are needed before enabling source-mapped reference navigation. This concern is
   already acknowledged correctly in the design.

## Smallest decisive next investigation

Before stateful caching, enumerate two-atom paragraphs/headings such as
`[a][b][c][d]` and `[a][b] X [c][d]`, repeated/equal keys, and case/tab/space
variants. Enumerate presence/absence of every distinct candidate key and use safe
and unsafe winning destinations. Compare exact real-owner links, values, spans,
and warnings against whole precise Markdig; require the symbolic count to agree,
or reject that candidate grammar. This directly tests the currently missing
compositional assumption without another large-file timing run. Then implement
versioned differential edits with winner moves, safe-to-safe URL changes,
nonwinner edits, cancellation/retry, and Undo/Redo. Only that second stage can
validate the proposed incremental certificate.

## Follow-up: two-atom evidence and declaration contract

Reviewed the revised design, `TwoAtomProbe.cs`, `two-atoms.jsonl`, and the raw
`adjacent-counterexample.md` / `.json`, without rerunning the suite or modifying
production. The investigation performed the discriminating test requested above
and produced a **real counterexample**, not merely a speculative concern:

```markdown
Alpha [a][b][c][d] Omega

[C]: javascript:bad

[c]: https://duplicate.test
```

The full precise parser resolves the middle `[b][c]` and emits one warning;
pre-cut targets `b` and `d` predict zero. The selective-context projection itself
still agrees with whole parsing. This cleanly separates two hypotheses: bounded
resolution context can be correct while per-pre-cut-atom aggregation is wrong.

The recorded 9,504 cases check candidate keys in Missing/safe/unsafe states,
opposite-safety duplicates, several key-equality/normalization patterns, paragraph
and heading owners, LF/CRLF, definition placement and ordering. Counts and probe
source agree with the revised report: touching atoms account for all 288 count
failures and 576 link-shape failures; 6,336 space-separated cases have neither;
consumer context projection has zero mismatches across all cases. The design now
explicitly rejects touching atoms for this multiplicity certificate, including
ones whose *current* environment happens to resolve as intended. **That resolves
the demonstrated flaw for the proposed revised admission boundary.** The revised
text correctly treats positive spaced results as finite evidence rather than a
proof for arbitrary numbers of atoms.

The declaration contract is now explicit: the new bounded route source-sorts
real definition leaves and flattens only the typed definition-group wrapper;
legacy whole analysis is unchanged, duplicate/nonwinner leaves are preserved,
and synthetic suffix leaves are excluded. `CheckDeclarations` independently
projects isolated raw definition lines and compares their shifted signatures and
declaration-only windows against full-AST children; the recorded zero declaration
mismatches support that leaf contract. This resolves the earlier *design
ambiguity*, subject to root approval of the additive bounded shape. It does not
validate production output ordering, combined consumer/declaration budgets, or
the full legacy topology, and the revised text does not claim those results.

**Updated recommendation:** implement the explicitly separated-atom candidate
with a compositional parser argument or a bounded whole-owner environment check;
do not revive touching-atom counts from successful current links. If exhaustive
admission is used, bound its candidate-key count and return `Provisional` on
exhaustion rather than allowing exponential admission cost. Next decisive work
is the actual staged session: safe-to-safe changes, missing-to-resolved changes,
duplicate moves/promotion, edit removal of the separating space and repair,
cancel/retry, and combined declaration/consumer projections against fresh whole
oracles. Source-position correction remains a separately owned prerequisite;
this review does not revalidate that implementation.
