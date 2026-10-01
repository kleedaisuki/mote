# Markdown references: a policy-private dependency certificate

Status: research and bounded experiments, 2026-09-30; **not implemented in production**. This extends the question left open in [markdown-block-certification.md](markdown-block-certification.md) and [semantic-ir-evolution.md](semantic-ir-evolution.md). No public contract, Engine code, Native code or production format code changed during this investigation.

## Decision and contribution

Introduce a Markdown-private **definition environment plus reference-use summaries**, not a generic AST, a workspace, an LSP or a graph framework. A block's syntax boundary and its external label lookups are separate certificates. A definition can change earlier consumers without changing their source or boundaries. Preserve both successful and unsuccessful lookups; update the exact global warning count from per-label use multiplicities; materialize only requested consumers against the new environment.

The useful implementation slice is an extension of the existing certified flat/heading/fence grammar with isolated, single-line ASCII reference definitions and plain explicit full reference atoms. **Touching atoms are excluded:** the two-atom experiment below found actual cross-atom reassociation that invalidates a pre-cut multiplicity summary. Spaced atoms remain a candidate domain requiring a compositional/symbolic admission proof. The slice must handle references before definitions, duplicates, missing definitions, safe/unsafe destination changes and deletion of the winning definition. This is a meaningful semantic extension, not merely another highlighting pattern. It does **not** establish general incremental CommonMark completeness. Unsupported syntax remains `Provisional`, including shortcut/collapsed forms until their own dependency certificate is implemented.

Two preconditions were exposed by the initial experiments: the investigated production pipeline did not request precise inline source positions, and Markdig retains a non-rendered definition group in the AST. The independent precise-position correction is tracked in [markdown-precise-source-locations.md](markdown-precise-source-locations.md); this research does not own or validate that correction. Ignoring either precondition would produce a misleading source-coordinate oracle or leak synthetic context into the IR.

## Existing system and authoritative semantics

At investigated source revision `e147f0b`, `MarkdownIncrementalSession` permits full Markdig parsing below its admitted small/sparse thresholds. Above those thresholds, an idle `Full` may build the restricted flat certificate; a cold `Visible` does not scan the whole file. Local cached edits require an unchanged block kind. The large-file certificate rejects all reference syntax. It uses absolute UTF-16 spans, bounded block parsing and delayed suffix shifts; its zero diagnostic count is valid only because its admitted grammar has no unsafe links.

The parser is **Markdig 1.3.2**, with `DisableHtml()` and no extensions. The reference experiment must match that pipeline, not a GitHub-flavored dialect. CommonMark is the language specification; pinned Markdig plus mote's projection is the implementation oracle. Differences between them are evidence to investigate, not permission to silently substitute new semantics.

Relevant rules: [CommonMark 0.31.2 §4.7](https://spec.commonmark.org/0.31.2/#link-reference-definitions) and [§6.3](https://spec.commonmark.org/0.31.2/#links) specify document-wide definitions, first-definition precedence and normalized label matching. Links can precede definitions. A definition does not interrupt an existing paragraph. Full, collapsed and shortcut references differ; a missing definition is text, not a Markdown validity error. Titles/destinations can span lines. Label matching case-folds and collapses whitespace; parsing displayed inline text is not label normalization.

Pinned upstream implementation evidence:

- [`MarkdownParser.Parse`](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Parsers/MarkdownParser.cs) completes block processing before inline processing. Its `DocumentProcessed` hook runs after inlines, so it is too late to inject definitions for initial link resolution.
- [`LinkReferenceDefinitionGroup`](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Syntax/LinkReferenceDefinitionGroup.cs) retains every definition and uses `TryAdd` for first-winner lookup. Its .NET comparer includes invariant-culture case/nonspacing-mark insensitivity. This must not be assumed equivalent to full Unicode case folding.
- [`LinkInlineParser`](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Parsers/Inlines/LinkInlineParser.cs) obtains destinations from the document's reference environment; [`LinkReferenceDefinitionExtensions`](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Syntax/LinkReferenceDefinitionExtensions.cs) exposes the group. Definition AST objects are ephemeral parser output, not a new owner of source text.
- [`UsePreciseSourceLocation`](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/MarkdownExtensions.cs) is a supported upstream pipeline option. No reflection, private-parser modification or runtime code generation is needed for bounded suffix-context parsing.

## Reproducible probes and negative evidence

Files are repository-local under `.temp/MarkdownReferenceIndexProbe/`: `Program.cs`, `ChangeProbe.cs`, `TwoAtomProbe.cs`, `ScaleProbe.cs`, `OraclePolicy.cs`, the `.csproj`, `results.jsonl`, `edits.jsonl`, `two-atoms.jsonl`, `scale.jsonl`, the raw negative fixture `adjacent-counterexample.md` / `.json`, and the generated `references-100.md`. The probe references the existing Release Engine/Formats DLLs directly, so building it does not rebuild or write production assemblies. The installed dependency is Markdig 1.3.2. Commands:

```powershell
dotnet run --project .temp/MarkdownReferenceIndexProbe/MarkdownReferenceIndexProbe.csproj -c Release
dotnet run --project .temp/MarkdownReferenceIndexProbe/MarkdownReferenceIndexProbe.csproj -c Release -- edits
dotnet run --project .temp/MarkdownReferenceIndexProbe/MarkdownReferenceIndexProbe.csproj -c Release -- two-atoms
dotnet run --project .temp/MarkdownReferenceIndexProbe/MarkdownReferenceIndexProbe.csproj -c Release -- scale
```

Environment: Windows x64, Windows version `10.0.26200`, .NET SDK `10.0.400`, target `net10.0`, Release. Production `MarkdownPolicy.cs` source SHA-256 was `C49E26DD9B5831F83DF85A5EF51ED659BD12D24D6CAF7DF6E7B179D8118A9088`. The experimental `OraclePolicy` is a source copy with a separate namespace/class, a delegated unused `CreateSession`, and **only** `UsePreciseSourceLocation()` added to the parser pipeline; the projection implementation is unchanged. This is explicitly a proposed-oracle correction, not a production fix or a claim that current output already has correct inline coordinates.

### Adversarial observations

| Case | Observed pinned implementation | Design consequence |
| --- | --- | --- |
| A use before its definition | Resolves in whole-document parsing, not isolated-block parsing | Parse consumer against the current global environment |
| Safe first definition, unsafe duplicate | First destination wins; zero warnings | Retain duplicate order; never diagnose every definition as if used |
| Unsafe first definition, safe duplicate | One warning on each resolved use | Global count belongs to consumers, not definitions |
| An unused unsafe definition | Zero warnings | Do not count URL safety once per declaration |
| Delete a first winner | Next duplicate may become effective, or use becomes unresolved | Keep all declarations, not only the winning URL |
| An apparent definition following paragraph text without an empty separator | Stays in the paragraph; does not define a reference | Definition recognition requires a block-boundary certificate |
| Definition-looking fence interior | No definition exported | Opaque certified fences must not feed the environment |
| `javascript&#58;bad` destination | Resolves to decoded `javascript:bad`; warning on use | Raw source-prefix scheme tests are not a safety oracle |
| ASCII case/space/tab variations | Matching reference resolves | Normalize only the admitted label alphabet, and differential-test it |
| `[cafe]` with `[café]: /relative` | Resolves under pinned .NET Markdig | Non-ASCII matching has a concrete spec/implementation tension; reject it from this first certificate, do not guess |
| Escaped labels and multiline definitions | Different parser paths, some apparent labels remain literal | These remain outside first admission; do not entity-decode/backslash-strip keys speculatively |
| Shortcut `[id]` and collapsed `[id][]` | Resolve, but have different syntactic dependency candidates | Positive full-atom evidence does not admit them automatically |

The initially investigated default pipeline produced a link span `(0,1)` for `[use][id]`, even when the use was distant. Enabling precise source positions produces the full atom's real span. This defect is independent of the reference-index design: an exact-node differential must not bless wrong positions simply because the same default parser is used twice. The separately owned production fix plus position regressions is a prerequisite to shipping source-mapped reference projection.

Markdig also retains a root `LinkReferenceDefinitionGroup` projected by mote as a `construct` with an artificial start of zero. Its children are real declaration spans, but the group's own range is not a normal physical block boundary. The first synthetic-suffix experiment incorrectly filtered nodes merely by `Span.Start < consumer.Length` and included that synthetic group; whole/local comparison failed. The corrected experiment selects the admitted **rendered paragraph owner**, never a group by numerical overlap. Production must explicitly exclude synthetic group/declaration output **without erasing real declarations**; the source-owner bounded projection contract below makes that distinction. The legacy whole-document group tree is unchanged.

Another negative observation: when a full reference fails, Markdig can retain a delimiter `construct`; display text is not necessarily the original bracketed source. `[use][id]` with only a `[use]` definition did **not** become a shortcut link in the tested pinned pipeline. Consequently the proposal does not synthesize display text by stripping brackets or invent a fallback rule. Requested blocks are still parsed/projected by Markdig. Conservative candidate dependencies include link text as well as the explicit label until the restricted symbolic certificate proves a smaller read set safe.

### Positive differential experiment

Seed `72841`; 2,500 independently parsed documents with alternating LF/CRLF, randomized interleaving of 1–5 consumer paragraphs and 0–8 definitions. Labels were `id`, `OTHER`, `use`, and `two words`; duplicate and missing keys were common. Destinations were safe HTTPS URLs or `javascript:bad`. For each consumer, the local source was followed by an empty separator and only its relevant winning definitions. Full-document precise Markdig remained the independent resolution oracle.

**7,468 consumer projections matched with zero mismatches**, including recursive public IR kind/name/value/absolute UTF-16 spans, tokens and warning codes/spans. The selective suffix contained only the explicit label and conservative link-text candidates, deduplicated; it was not a copy of the entire document environment. Consumer nodes were selected by admitted block kind, synthetic definition groups excluded, then shifted by the real owner's source offset. One observation took 467.72 ms for the full differential loop.

This proves a finite equivalence check for bounded **context projection**, not the correctness of an incremental cache, Unicode semantics, a whole-file certifier or arbitrary Markdown. Random documents do not model versioned edits, cancellation or resource exhaustion.

A separate real-Engine-edit sequence (`-- edits`) used its own restricted ASCII definition inventory (`Dictionary.TryAdd` in physical source order), not Markdig's winner table, then projected consumers with selective suffixes. Initial version plus eight edits all matched a fresh whole-document precise oracle: safe→unsafe→safe, delete winner/promote unsafe duplicate, delete final definition, append a late definition, insert an earlier unsafe duplicate, rename winner/promote safe duplicate, and add another consumer. The paragraph had two uses and another paragraph one use; symbolic/global warnings were respectively `0,3,0,3,0,0,3,0,0`, and multiplicity rose from three to four at the final edit. This tests deletion/order/missing-read and multiplicity semantics, **not** incremental cache correctness: the experimental environment is freshly rebuilt each version. Actual declaration moves, cancellation and Undo/Redo remain acceptance work.

### Discriminating two-atom experiment: the initial multiplicity hypothesis fails

Independent review identified the missing test: previous random paragraphs had one atom, and the edit test repeated only one key. `TwoAtomProbe.cs` now enumerates eight templates: four distinct candidate keys; shared target; repeated atom; same text/target; neighboring text equal to a target; crossed keys; case/tab/space normalization. For every distinct normalized candidate key, enumerate **Missing / safe winner / unsafe winner**, with an opposite-safety duplicate for each present key. Also vary adjacent / single-space / ` X ` separation, paragraph / ATX heading, LF / CRLF, definitions before / after consumers, and forward / reversed key-group order. The owner has a nonzero source offset. Warning representatives are classified through the actual copied policy's diagnostics, not a replacement scheme-prefix predicate.

**9,504 context cases** were compared against a fresh whole-document precise Markdig oracle. Selective context reproduced recursive real-owner IR, tokens and warning spans with **zero mismatches**, but the proposed pre-cut atom warning count failed in **288 cases**, and its expected link shape failed in **576 cases**. Raw fixtures/output are preserved under the repository-local probe directory.

| Separator | Cases | Context-projection mismatches | Pre-cut warning-count failures | Pre-cut link-shape failures |
| --- | ---: | ---: | ---: | ---: |
| No characters | 3,168 | 0 | 288 | 576 |
| One ASCII space | 3,168 | 0 | 0 | 0 |
| ` X ` | 3,168 | 0 | 0 | 0 |

Concrete negative fixture:

```markdown
# Prefix

Alpha [a][b][c][d] Omega

[C]: javascript:bad

[c]: https://duplicate.test
```

Neither pre-cut target `b` nor `d` is defined, so the initial formula predicts zero warnings. Markdig instead recognizes the **middle `[b][c]`** as one link with unsafe winner `c`, emits one `markdown.unsafe-link` at absolute UTF-16 span `(19,6)`, and displays `Alpha [a]b[d] Omega`. The bounded-context result matches this correctly. Thus exact context projection **does not license** per-precut-atom counting. Recording candidate keys alone would ensure value invalidation, but would not fix the wrong multiplicity.

**Revised boundary:** touching bracket atoms cannot receive the first full-reference multiplicity certificate, even when their current definitions happen to make them parse as intended. They remain `Provisional` on this extension. A candidate separator certificate requires at least one ASCII space outside every balanced, nonnested atom, no intervening active syntax, and the same label/length constraints on consumers as definitions. The positive spaced cases are finite evidence, not a proof for arbitrary numbers of atoms. Implementation must establish compositionality from the restricted grammar/parser behavior or explicitly check all distinct candidate assignments for the bounded owner; checking each atom in isolation or only its current environment is insufficient. With two atoms, the exhaustive check has at most four distinct candidates and 81 Missing/safe/unsafe assignments. Do not permit an exponential per-owner check to grow without a bound.

An alternative future certificate for touching runs would store a whole bracket-run owner, all candidate reads, and actual parser-derived resolution/count summaries per admissible environment; a winner/presence change can require reparsing that run. Extracting only currently successful Markdig links is insufficient because missing/changed definitions can reassociate future links. This is a separate design/measurement obligation, not authorization to declare adjacent runs complete now.

### Real declaration projection: explicit bounded source-owner contract

`IDocumentPolicy.Analyze(string)` and the existing small-file whole-document route retain their established root/group/definition tree, including duplicate definition children. No generic `construct` filtering is allowed: unresolved inline delimiters are also `construct` nodes.

The **new large-file bounded reference route** uses an explicitly documented source-owner projection, within the existing bounded `DocumentAnalysis` semantics:

- `Root.Children` contains each requested rendered block and each requested **real `LinkReferenceDefinition` leaf**, source-sorted. The definition leaf preserves exactly the pinned projection's kind (`construct`), absolute span, null name/value and children; winners, nonwinners and unused unsafe definitions are all equally eligible. It does not acquire a synthetic link value, token or declaration-site warning.
- Only the parser-specific **`LinkReferenceDefinitionGroup` wrapper** is omitted/flattened in this bounded source-owner view. Its artificial range cannot select offscreen declarations. This does not erase its real children. It also does not change the legacy tree or claim literal parent/group topology equality between bounded and whole-tree output.
- The independent bounded oracle normalizes **only that typed group** into its real definition leaves, keeps other constructs untouched, selects intersecting real owners, and compares exact node signatures. The same normalization is applied only for testing the declared large-file projection contract, not to existing public whole analysis.
- A real definition-only viewport returns its actual leaf, including a duplicate that does not win; a consumer-only viewport returns no real definitions unless their true source spans intersect it. A synthetic suffix contributes **neither group nor definition leaves**, regardless of its numeric overlap. In combined views both source-owner categories count against the existing node budget; truncation is explicit where supported and never changes global completeness/count.
- Declared leaves are structural source facts, not preview links or navigable destination text. Consumers continue to resolve through the full current environment even when no definition leaf is projected. Changing real declaration visibility does not change global warnings.

The new two-atom probe independently parses every raw real definition line, shifts its leaf to its source offset, and compares it with each corresponding full-AST declaration child, then checks every declaration-only viewport. **All 9,504 cases matched**, including safe/unsafe duplicates before/after consumers, reversed key order, LF/CRLF and normalized labels. Synthetic groups were not used as owners. This establishes finite leaf/source-window equivalence for the proposed contract; an implementation still must enforce its budget, output ordering and stale-version rules. Root review must approve this explicit additive bounded shape before integration; no new public type or ABI change is proposed.

### 100 MiB exploratory cost

The file-backed fixture contains 25,451 isolated paragraphs, each with `Alpha [manual][guide]` plus 4,096 plain letters, followed by a safe winning `[guide]` definition and an unsafe case-variant duplicate. File size/source length: **104,858,184 bytes/UTF-16 units** (ASCII). A first pass reads/indexes physical owners and parses the declarations; a second bounded parse validates every consumer against the winner. It is a cost probe, not the production grammar-admission implementation.

| Measurement | One observed value |
| --- | ---: |
| Cold two-pass owner scan plus bounded consumer validation | 1,218.30 ms |
| Whole-file consumer warnings | 0 |
| One tail consumer projection with the winner suffix | 0.1076 ms |
| Naive safe→unsafe winner change: reparse all consumers | 156.02 ms |
| Warnings after naive unsafe re-evaluation | 25,451, exactly the consumer count |
| Whole-process RSS after sequence | 259.43 MiB |
| Managed bytes reported by `GC.GetTotalMemory(false)` | 206.42 MiB |

This supports bounded materialization and identifies a consequential cost: definition fanout should not force every consumer to be parsed again just to update a count. The safe/unsafe runs also exercise different code paths and warm-up states, so their timings are **not** a fair comparative speedup. RSS includes the authoritative text snapshot and runtime; it does not establish a retained-index budget. No p95, Native AOT, GUI input latency, compositor timing or four-RID claim follows.

## Proposed first admission: closed semantic domain

Keep the existing flat/ATX/fence certificate unchanged for files without references. For the extended certificate, every source unit must satisfy an explicit predicate, and all external dependencies must be enumerated:

1. **Boundaries:** LF/CRLF, bounded physical lines/blocks, current fence proof and existing permitted heading adjacency. New reference-consuming paragraphs and definition owners require genuinely empty lines before/after (or document edges). Do not initially admit adjacent definitions, definition→paragraph runs, multiline paragraphs, containers or titles, even though many are valid CommonMark. Blank separator edits must revoke/rebuild the boundary proof.
2. **Definitions:** column-zero `[label]: destination`, one line, no title, no extra trailing text. Label contains ASCII letters/digits and space/tab only, nonempty after normalization, at most 999 admitted UTF-16 units. Destination is a nonempty bounded ASCII string from a deliberately simple URL alphabet; exclude whitespace, angle brackets, escapes, entities, quotes and parentheses in the first implementation. Permit `javascript:bad`/other unsafe schemes: warnings are semantics, not an admission failure. Parse the entire line with pinned Markdig and require exactly one definition with the expected full source range and decoded destination, no rendered leftover paragraph.
3. **Consumers:** one bounded isolated paragraph/heading with the existing flat text alphabet plus nonnested full atoms `[plain text][label]`. Plain text contains no inline-active syntax, bracket, backslash or entity. Apply the declaration label alphabet, normalization, nonempty and 999-unit bound to each consumer label. All brackets must be consumed by recognized atoms, and **at least one ASCII space must separate neighboring atoms**; touching atoms are rejected after the demonstrated reassociation counterexample. Disallow `!` immediately before an atom (image), nested atoms, code spans, inline links, shortcut/collapsed forms and unmatched bracket intermediates. The local whole block must parse as the expected single rendered owner and no unexpected export or inline kind.
4. **Symbolic safety:** admission verifies the **whole owner**, including interactions between its atoms, under controlled definition contexts; it cannot merely verify isolated atoms or the currently resolved AST. Establish that each atom yields exactly one target-derived link at its own whole source span when its explicit label is defined, and no unsafe-link-bearing construct when missing. Use the actual policy's URL-safety outcome. If conservative candidate contexts expose extra links, cross-atom reassociation or different grammar, reject the certificate. This is what licenses the multiplicity count below. A bounded exhaustive two-atom certificate checks all at-most-81 distinct candidate assignments; larger owners need a compositional proof or another explicitly bounded strategy, not unbounded enumeration. Require same/different link-text keys, multiple atoms, missing labels and negative adjacent cases.
5. **Opaque units:** certified fences cannot export definitions or reads. Existing plain blocks have empty read/write sets. Unsupported active syntax anywhere revokes whole-file completion; its offscreen location does not exempt it.

Destination and label limits above are proposed admission constraints, not CommonMark validity limits or new user-facing errors. A rejected file still opens and edits; it has honest `Provisional` semantic status on the unsupported large-file route. Small-file legacy analysis is unchanged apart from a separately reviewed precise-position correction.

## Private state and invariants

Use compact policy-private structures, retaining no complete source string and no persistent Markdig AST:

```text
OwnerId -> (existing source span/run shift, certified kind,
            reference atom summaries, read KeyIds, optional write KeyId)
KeyId   -> normalized admitted label, source-ordered Definition OwnerIds,
           current winner OwnerId or Missing, winner epoch,
           decoded safety status, total certified use multiplicity
KeyId   -> consumer OwnerIds (including unresolved consumers)
```

Reference atoms retain local ranges and `KeyId`, not duplicated destinations. Definitions retain bounded raw destination/source spans; the effective decoded URL can be ephemeral or one bounded value per winner, never copied into every consumer. Stable owner identity is distinct from source order: run shifts move coordinates lazily, whereas moved definitions must update per-key order. The winner is the earliest current declaration in actual source order, not insertion time or dictionary order. ASCII normalization trims/collapses admitted spaces/tabs and folds ASCII case; validate equivalence against the pinned parser before admission. Unicode/escapes are excluded, not approximated.

For the proven full-atom domain, with `u(k)` the exact number of certified use atoms for key `k`:

```text
total unsafe warnings = Σk u(k) × unsafe(effective decoded URL of winner(k))
unsafe(Missing) = 0
```

This formula is conditional on the symbolic admission proof. `u(k)` is the count of **certified parser-equivalent atoms**, not arbitrary pre-cut bracket pairs; adjacent atoms have a demonstrated counterexample and are excluded. It must count uses, not just distinct blocks or labels; a certified paragraph with three uses emits three warnings only after the larger-owner compositional proof holds. It cannot be reused for images, autolinks or ambiguous fallback syntax without extending the proof. Compare this total against an independent full parse in regressions. Visible diagnostics are obtained from the actual projected Markdig AST with proper spans, not fabricated from the count.

Resource limits must cover object/dictionary/string overhead, not only array payload. Initial testable budgets: 100,000 certified owners/atoms, 16,384 distinct keys, at most 64 Ki units per real owner, at most 64 Ki of **selective** context, and 32 MiB retained private index. Existing projection limits remain. These are budgets to measure, not already achieved numbers. If retained owner capacity is exceeded but the exact symbol environment fits, an honest two-pass no-cache `Full` may finish/count and discard the reusable owner index; edits then rescan. If even the required symbol environment cannot be represented within the allowed strategy, return `Provisional`, not a fake complete result.

## Analysis and incremental transition

### Cold request

- A cold `Visible` preserves current bounded behavior and must not resolve a missing offscreen definition from an incomplete prefix as if globally absent. No complete environment means `Provisional` unless the requested facts have a separate checked dependency proof.
- An idle `Full` first certifies every owner boundary and declaration, builds all declaration lists/winners and inventories every consumer read. A second pass certifies the resolved/unresolved grammar, accumulates exact multiplicities/count, then materializes the bounded requested owners using only their current winning definitions. It does not call whole-file `Markdown.Parse`.
- Synthetic context is appended after an empty separator so real-owner coordinates remain local. Copy the winning definition's admitted raw line (or a proven equivalent no-title serialization) rather than inventing URL escaping. Only dependency-relevant winners are appended. Check the source/context cap before calling Markdig; oversize local calls do not bypass cancellation/resource policy.
- Consumer projection outputs only the verified real rendered owner; discard the synthetic group and all synthetic source spans. Separately project requested real declaration leaves under the explicit source-owner contract above, preserving duplicates and source coordinates. Translate real spans once by the owner's current start; never use a parser group range as a declaration source owner.

### One contiguous versioned edit

1. Validate the same document generation and exact before/after version, lengths and edit chain. A gap starts a fresh scan or a truthful bounded fallback.
2. For an edit wholly inside one owner, reparse/revalidate only that bounded owner. Require unchanged boundary/kind and neighbor relation for the local fast path. Remove its previous reads/writes/multiplicities in staged state and add the new ones. Local label changes update both old and new reverse entries, including missing keys.
3. If a declaration changes, recompute affected winners by current source order. A nonwinner destination edit does not affect other uses; deleting/renaming the winner may promote a duplicate. Compare the full effective resolution outcome (presence and decoded URL; title once supported), not just `SafeUrl` Boolean. Increment the key epoch when that outcome changes.
4. Update the global warning total by the affected key's multiplicity delta; update changed consumer multiplicities/counts similarly. A safe→safe URL edit leaves the count unchanged but invalidates visible link values. Consumers outside the viewport need no eager AST rebuild. Their source summaries remain valid; projection binds them to the current key epoch when requested.
5. An unchanged plain/fence owner still uses the current local fast path. An insertion/deletion crossing boundaries or moving declarations is a structural change: recertify from a proved predecessor to convergence, or initially rebuild the full two-pass certificate. It is acceptable to use full recertification for moves in the first implementation; it is not acceptable to keep an old winner because shifted source bytes look unchanged.
6. Check cancellation before committing any owner/environment/count/projection state. Publish one coherent current-version result; stale lazy projections cannot claim completion. Undo/Redo are new edits/versions. Canceled analysis leaves the previous committed state intact, so the same edit chain can be retried.

`_uncertifiedLine` currently caches an intrinsic or relational grammar obstruction. It must **not** become a cache of a failed semantic lookup or incomplete environment: an unrelated offscreen definition edit may repair those. Budget exhaustion is likewise not a permanently invalid source line. Keep the failure reason/dependency distinction explicit.

## Engineering and research connection

[GitHub's production stack-graph design](https://github.github.com/stack-graph-docs/) separates locally extracted binding facts from later resolution. For this single-file, single global-label namespace, the relevant lesson is delayed resolution over owner-local facts—not importing its graph machinery or its cross-repository cost model. A dictionary plus ordered declarations and reverse readers is sufficient.

[Zwaan, van Antwerpen and Visser, PACMPL/OOPSLA 2022](https://doi.org/10.1145/3563303) investigate sound reuse of semantic analyses via scope-graph queries. Their treatment of unsuccessful queries motivates retaining missing-label reads: insertion can invalidate a previously empty result. This is a design analogy, not a theorem proving mote's Markdown certifier. Our certificate additionally needs grammar/boundary proof and warning multiplicity, which must be checked directly. Syntax reuse alone is not semantic independence.

## Independent acceptance and performance gate

Implement no production optimization before root review of this boundary and the separate source-position issue. The minimal integrated deliverable is the admitted full-reference domain with exact global warnings and lazy visible binding, not a universal Markdown parser. Required evidence:

| Dimension | Discriminating checks |
| --- | --- |
| Position oracle | Independently locate full reference source spans at head/middle/tail, LF/CRLF and after non-BMP text; whole and bounded precise Markdig must agree; synthetic group cannot enter tokens/navigation |
| Winner semantics | Use before/after definition, mixed-case duplicates; insert earlier duplicate, edit/delete/rename winner, edit nonwinner, move declarations across uses; missing→resolved→missing; safe→safe URL value changes |
| Counts | Unused unsafe definition, unsafe nonwinner, repeated and distinct spaced atoms, offscreen unsafe winner, decoded destination hazard; global total exactly equals independent whole-document oracle; the adjacent `[a][b][c][d]`/unsafe `c` counterexample cannot enter the multiplicity certificate |
| Admission revocation | Touching atoms, shortcut/collapsed/image, Unicode/escaped labels, multiline titles/destinations, containers, bracket/code ambiguity, unclosed fences, invalid separator/kind edit and repair; no unsupported path inherits `Complete` |
| State safety | Cancel between passes/during consumer validation/just before commit and retry; missing edit chain, multiple-edit fallback, Undo/Redo and disposal; no stale version or old winner epoch published |
| Output bounds | Whole-document viewport still yields bounded nodes/tokens/diagnostics; selective context and retained state stay capped; real declaration leaves (including nonwinners) match the typed-group-normalized source-owner oracle; synthetic groups/declarations cannot enter output or become false source-map owners |
| Scale | 1/10/100 MiB: sparse references, many distinct keys, one very popular key, duplicate chains, near-head/middle/tail edits and scroll; compare current flat path and exact small-file path where both admit the corpus |

For a 100 MiB independent whole-document oracle, use sparse references and large plain/fence ballast to bound AST node count; do not repeat the known dense mixed-Markdig 2 GiB memory failure. On tractable sizes, seeded edit sequences must compare every real rendered owner, count and requested token/diagnostic against a fresh global oracle after each edit. A separate high-fanout corpus measures aggregate-count behavior without confusing local oracle speed with GUI latency.

Use GitHub Native AOT jobs for Windows/macOS x64/ARM64 once the managed certificate passes independent review. Report cold `Full`, cold `Visible`, cancellation latency, warm local edit, winning-definition edit, nonwinner edit, two distant projections, scanned UTF-16 units, effective-key changes, owners parsed, allocations, retained-index estimate and process memory. Include p50/p95 only with repetitions and stated controls. Keep startup/source readiness, frame presentation and keyboard latency separate. The proposed key-count route must actually avoid per-consumer parsing on a popular-label destination edit; instrumentation should prove it. Preserve no-ref flat performance and previous external contracts.

## Next decision

Root should review (1) the precise-inline-coordinate correction as an independent correctness change; (2) the explicit handling of real versus synthetic definition groups; and (3) the symbolic full-reference admission/count proof. If accepted, implement this Markdown-private slice with differential edits and count instrumentation. If bounded context or symbolic admission fails for a candidate, narrow that admission explicitly and keep it provisional while investigating—not a silently incomplete “complete” cache. Shortcut/collapsed, images, Unicode and multiline definitions remain subsequent semantic work toward the full product goal.
