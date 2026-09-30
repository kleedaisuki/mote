# Review: bounded whole-owner Markdown presence certificate

Reviewed 2026-09-30. Scope: the proposed bounded presence-mask admission
certificate, `markdown-reference-index-design.md`, its existing independent
review, current `MarkdownPolicy` projection, and pinned upstream Markdig 1.3.2
source. This is a source-level proof argument, not a new differential run or
validation of an implemented session. No production or test files changed.

## Judgment

**Qualified approval of the certificate method.** For a lexically closed owner
with at most four distinct candidate keys, checking all `2^k <= 16` presence
environments is sufficient to certify environment-independent explicit-target
link multiplicities. Safe/unsafe URL assignments need not enlarge this to
`3^k`: in the pinned default reference path, a definition's URL/title is output
payload rather than input to parser control flow. This conclusion requires the
conditions below; it does not assert that every space-separated candidate
passes, or that spaces reset Markdig's delimiter state.

I found no counterexample to the properly closed method. The known touching
fixture remains a counterexample to pre-cut counting, and an explicit negative
admission case. The existing spaced finite experiment need not be repeated to
justify this different, per-owner exhaustive method.

## Precise domain and proposition

Let an owner `s` be one boundary-certified, single physical line, parsed as one
paragraph or ATX heading. Its inline body is ASCII letters/digits/spaces and
nonnested full atoms `[t_i][l_i]`, with at least one ordinary ASCII space between
neighboring atoms. The heading prefix is handled separately by the existing
heading grammar. Every bracket belongs to an atom; inline parentheses,
backslashes, exclamation marks, backticks, ampersands, angle brackets and
emphasis punctuation are absent. Require owner length at most 4,096 UTF-16
units. Block indentation and heading structure must independently satisfy the
expected-owner check; a four-space indented line cannot be assumed a paragraph.

For the simplest unambiguous initial contract, require **both** `t_i` and `l_i`
to have a nonempty normalized key and raw length at most 64, using only ASCII
letters/digits/spaces. Define `N` as trimming/collapsing ASCII spaces and folding
ASCII case. Let

```text
K(s) = { N(t_i), N(l_i) : every atom i }, k = |K(s)| <= 4.
u_s(q) = number of atoms i with N(l_i) = q.
```

If longer or blank link text is desired, specify it separately: a valid parsed
text-label candidate must be included even if it is not an explicit target.
A syntactically impossible/unsupported key can be treated as constantly absent
only after proving that no admitted definition can match it. Do not silently
truncate text candidates to 64 or omit them because they are currently unused.

The environment `E` contains only globally admitted definition keys from the
same ASCII domain. It maps each key to Missing or its **first source-ordered
winning definition**. Each real definition has already passed the independent
single-line/no-title parser admission, with a bounded destination at most 2,048
units. There are no extension-produced definitions, `CreateLinkInline`
callbacks, parser-context hooks, or document-postprocessing hooks that inspect
or modify reference payloads. Use the same pinned HTML-disabled, precise-location,
non-trivia pipeline and options for certificate and actual materialization.

For every mask `p : K(s) -> {0,1}`, construct a suffix environment `S_p` with one
definition per present key and a distinct safe sentinel URL per key. Each suffix
definition is separated from the real owner and other definitions by genuinely
empty lines. Check that the synthetic definitions actually exported exactly
the requested keys and sentinel values; failed setup is rejection, not evidence
about the owner.

Define the checked link list recursively over the **real rendered owner**, not
by numerical overlap of root/group spans. Require precisely the following list,
with one entry per atom whose explicit target bit is present:

```text
(whole atom source span, N(LinkInline.Label), sentinel URL for N(l_i))
```

Also require `IsImage == false`, `IsShortcut == false`, a full-reference label
source range consistent with `l_i`, and no additional `LinkInline` anywhere in
the owner's descendant tree. Normalize parsed labels before equality; Markdig
preserves label casing. Matching URL/reference provenance is important, not an
optional replacement for matching spans. Owner kind/range and the permitted
inline-kind tree must agree with the lexical domain in every mask. An exception,
cancellation, budget limit, unexpected owner, unexpected exported definition or
unexpected inline shape rejects admission.

**Proposition.** If these finite checks succeed, then for every admitted real
environment `E`, the real owner contains exactly one non-image full-reference
link at atom `i`'s span iff `E(N(l_i))` is present. That link uses the effective
definition for `N(l_i)`. There are no other warning-bearing links. Consequently
the owner's warning count is

```text
sum_q u_s(q) * unsafe(decoded URL of E(q)), with unsafe(Missing) = 0.
```

The proposition certifies multiplicity and resolution provenance. It does not
authorize manufacturing displayed text or the entire unresolved IR from atoms;
actual viewport owners must still be parsed/projected with actual winners.

## Source-level proof

### 1. Closure of all environment reads

The pinned parser makes existence queries while handling original opening `[`;
lookup attempts then use an explicit parsed label or the saved opener label.
These are the only reference-environment query sites in the default
`LinkInlineParser`. See its `Match` and `ProcessLinkReference`, and the dictionary
accessors in [LinkReferenceDefinitionExtensions](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Syntax/LinkReferenceDefinitionExtensions.cs).

In this nonnested alphabet every original opening bracket begins either `t_i`
or `l_i`. Both label helpers terminate at its first unescaped closing bracket;
they cannot collect separator text or another atom into the same key. A shortcut
attempt uses the label saved at that same original opener, not an arbitrary
substring extending to a later closing bracket. Thus every successful or
unsuccessful query has normalized key in `K(s)`. The closure claim includes
openers still active after a failed full-reference attempt. The label helper
mechanism is visible in [LinkHelper](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Helpers/LinkHelper.cs).

No other default inline parser can introduce a reference lookup in this alphabet:
there is no entity, escape, code, emphasis or autolink opener. Owner boundaries
are an independent hypothesis, so block-context syntax cannot enlarge the
owner's inline read set. The default parser inventory is recorded in
[MarkdownPipelineBuilder](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/MarkdownPipelineBuilder.cs).

### 2. Payload erasure gives control-flow equivalence

Consider two valid environments with the same presence answers on `K(s)`.
Relate parser states by equal cursor, delimiter topology/activity, saved labels,
source positions and inline-node kinds/children, but erase URL/title payloads,
definition object identities and definition-source spans from this relation.

An induction over inline parser transitions preserves this relation: syntax
steps see identical source, and every dictionary query sees the same Boolean
presence. On a successful default reference lookup, the implementation creates
the same `LinkInline` kind/span/label and moves the same delimiter children;
it merely copies/unescapes the chosen URL/title. Neither value governs the
subsequent delimiter processing or success result. The excluded callback branch
would invalidate this argument because it may return a different inline kind
or execute arbitrary control. The exact control points are in
[LinkInlineParser](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Parsers/Inlines/LinkInlineParser.cs).

The default post-inline emphasis processing depends on delimiter/source shape,
not definition URLs; this owner contains no emphasis opener anyway. No rendered
HTML is reparsed as Markdown. Destination safety is tested later by mote's
projection, once per resulting `LinkInline`, not by reference syntax resolution.
This is why safe sentinels suffice even when actual winners are unsafe.

### 3. Exhaustiveness and aggregation

For any real `E`, its restriction to `K(s)` determines one enumerated mask `p`.
By Step 2, its link shape and resolution keys match `S_p`; by the finite check,
that shape is exactly the explicitly present atoms and each link's provenance
is its explicit target. Restore actual winning URL payloads, and apply the
policy's actual safety predicate to obtain the stated count. Definitions of
keys outside `K(s)` cannot alter the shape by Step 1. Summing owners gives the
global multiplicity formula only after **all** source units have the independent
boundary/grammar certificates and every reference owner passes admission.

This is finite exhaustive abstraction, not induction on the number of atoms or
a claim of arbitrary-Markdown compositionality. Its bounded computation replaces
the missing compositional proof without silently extrapolating two-atom probes.

## Material caveats and implementation consequences

| Issue | Required behavior |
| --- | --- |
| Missing reads | Retain all `K(s)` candidates, including text keys and missing targets; a future definition insertion changes a query answer. |
| Prior delimiters | An ordinary space is not a reset operation. A failed full attempt can consume its explicit label and leave an opener alive; later success can mark parent openers inactive. Test the whole owner for every mask. |
| Adjacent atoms | Reject lexically before certification under this first domain; keep `[a][b][c][d]` with only `c` defined as a permanent negative case. |
| Normalization | Trimming/collapsing spaces plus ASCII case folding is suitable only for the globally restricted ASCII universe. Do not compare `LinkInline.Label` to a folded key without folding it. |
| Unicode definitions | `[cafe]` can match `[café]` in pinned Markdig's invariant IgnoreNonSpace comparer. Such definitions anywhere in the document defeat a local ASCII-only environment unless whole-file admission rejects them. |
| Sentinel provenance | Use distinct bounded safe HTTPS URLs for distinct keys; verify each resolved node's exact sentinel value, label and atom range, not only link count. |
| Payload versus source | Once a real definition is admitted, its destination content does not affect reference control flow; its raw source still must not inject a title, line, extra definition or leftover rendered owner into synthetic context. |
| Winner order | Presence masks do not prove indexing. Independently preserve earliest physical source order, duplicate promotion, rename/move behavior and safe-to-safe value invalidation. |
| Synthetic AST | Select the verified real owner by typed identity/source-owner contract. Definition-group artificial spans are not a provenance rule; no synthetic group/definition can be projected as real content. |
| Resource handling | Reject `k > 4` before enumeration. Check owner/context caps and cancellation before every parse. Do not turn parser failure into a successful empty-link mask. |
| Version scope | Cache only under an unchanged owner source/boundary plus fixed parser-version/options identity; an edited separator or pipeline change requires readmission. Presence/value epochs still govern materialized links. |

The pinned [LinkReferenceDefinitionGroup](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Syntax/LinkReferenceDefinitionGroup.cs)
retains declarations separately from its first-winner dictionary. Its comparer
motivates the global ASCII restriction; it must not be treated as a Unicode
case-folding specification. [MarkdownParser](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Parsers/MarkdownParser.cs)
builds the definition environment before processing inlines, which explains why
a suffix can resolve an earlier owner. These mechanisms do not establish the
incremental index or real declaration projection; those remain separate tests.

## Bounds and follow-up gate

With `k <= 4`, admission makes at most 16 bounded parses regardless of the number
of repeated atoms. If sentinel destinations are explicitly capped at 64 units,
one source including CRLF empty separators is at most approximately 4,640 units
(`4096 + 4 * (64-label + 4-punctuation + 64-URL + 4-separator)`). Materialization
with four actual 2,048-unit destinations is at most approximately 12,576 units.
Use checked arithmetic and the configured source/context cap; these are source
length bounds, not a claim about parser allocation or linear running time.

The finite key bound removes **unbounded** exponential work, but 16 parses per
owner can still materially affect cold scans with many owners. Measure actual
admitted-mask counts, parsed units and cold admission cost before shipping.
Retaining only `K(s)`, atom local ranges/target IDs and multiplicities after
successful admission is sufficient; sentinel ASTs and the 16 environments need
not survive admission. No production parser modification is required.

The smallest remaining implementation gate is a new, independently inspected
presence-mask harness/implementation that enforces setup verification, normalized
labels, recursive exact link list and distinct sentinel provenance. Include
three-or-more repeated/mixed atoms, text/target collisions, all-missing masks,
space normalization, heading/LF/CRLF, max bounds, and the touching negative case.
Then validate the versioned index separately. This review is a qualified proof
of the admission rule, not a substitute for those implementation validations.

## Follow-up: prototype certificate inspection

Inspected `.temp/MarkdownPresenceCertificateProbe/Certificate.cs` on 2026-09-30;
no experiment reruns, and no inspection/approval of `Session.cs` state or
performance. **No blocking multiplicity/provenance mismatch was found.**

- `Scan` and `IsLabel` enforce the closed ASCII alphabet, nonempty normalized
  text/target labels, raw 64-unit bounds, and separated complete atoms. Admission
  rejects owners over 4,096 units, more than four distinct text/target keys, and
  additionally more than 32 atoms. The extra atom bound is conservative narrowing,
  not unsound acceptance.
- The mask loop covers every assignment including all-missing and all-present,
  parsing the complete owner each time. `AllLinks` recursively descends every
  container and preserves preorder/source order, so nested extra links are not
  hidden by top-level-only inspection.
- Setup checks compare dictionary cardinality to mask population and every
  present key's URL, null title, and absent callback. In combination with the
  lexical scanner (the real owner cannot contain a definition) and one suffix
  line per distinct normalized key, this verifies the required environment.
  It does not select the synthetic group as a real owner.
- Real-owner selection is typed `ParagraphBlock`/`HeadingBlock`, followed by
  unique-owner, expected-kind, zero-start and exact full-owner-length checks.
  These checks prevent numerical overlap of the definition group's artificial
  span from satisfying owner selection. Because the scanner admits no multiline
  or leftover-block syntax, filtering this typed subset cannot conceal another
  real rendered block under the present domain.
- Each expected link checks exact atom span, normalized label, unique per-key
  sentinel URL, full-reference/non-image flags and `ReferenceEquals` against
  the actual setup winner object. This is stronger than URL/count-only evidence
  and closes the requested sentinel reference-provenance obligation.

Two qualifications should remain explicit. First, `LabelSpan` is checked only
for containment in the atom, not for equality to an independently derived exact
explicit-label source range. This does **not** undermine the proved link-span,
target-provenance or multiplicity result; do not advertise it as an independent
exact label-coordinate regression. Such a regression requires a separate
source-position assertion, with whitespace normalization handled consistently
with the pinned helper. Second, `Admit` propagates parse/checkpoint exceptions;
it does not locally convert them to rejection. Its caller must abort admission
without publishing a certificate, rather than catch-and-treat the failed mask
as an empty successful result. The bounded constructed suffix makes its source
length cap automatic here; arbitrary real payload admission/materialization
remains outside this inspected file.

## Second iteration: inert-alphanumeric skeleton lemma

Reviewed 2026-10-01 as a source-level lemma, without running experiments or
inspecting a new implementation. The adopted transform is **narrower** than
replacing an arbitrary outside run by ` X `:

1. Independently admit the original physical block boundary, block-start kind,
   and original owner/resource limits. In particular four-space code indentation
   is still rejected; a skeleton cannot make an unsupported source admissible.
2. Preserve the ATX marker/prefix and level. Preserve every outside ASCII space
   byte-for-byte. Preserve every entire raw full atom `[text][label]`
   byte-for-byte, including its internal spaces/case. Keep atom order, empty
   intervals and the prohibition on touching atoms.
3. Replace each maximal outside ASCII alphanumeric run by exactly one `X`.
   No other character, newline, punctuation or label is transformed.
4. Record each atom's original start `o_i`, skeleton start `v_i`, and unchanged
   length `n_i`. Cache only an immutable successful admission summary under
   exact skeleton text and fixed pipeline/parser/options identity. Every cache
   hit still requires original lexical/boundary/resource admission and its own
   atom map. Do not reuse cached source coordinates or sentinel AST objects.

### Statement

Let `s` satisfy the closed original domain and let `v = T(s)` be this skeleton.
For any fixed allowed reference environment, parsing the original and skeleton
with pinned Markdig's default HTML-disabled, precise-location, non-trivia
pipeline produces the same ordered reference-link events: same originating
opening/closing bracket identities, normalized resolution key, full/shortcut
and image flags, winning-definition provenance, and reference payload.
Endpoints inside atom `i` transfer by

```text
sourceOffset = o_i + (skeletonOffset - v_i),
for v_i <= skeletonOffset <= v_i + n_i.
```

Consequently, if every presence mask on `v` passes the earlier exact full-atom
sentinel certificate, every mask on `s` passes its link-shape/count/provenance
property with atom spans translated by this map. Whole atoms retain length;
the per-target multiplicities and warning formula transfer unchanged.

This is **not** an equality of complete AST/IR, literal values, paragraph or
heading display text, or owner source ranges. Only atom-local coordinates have
the stated translation. A virtual `X` does not represent navigable original
text, and no bijective character map for contracted alphanumeric runs is claimed.
Real projected owners must continue to be parsed from original source.

### Why the simulation is sound

Use bracket-parser events as synchronization points, allowing literal
collection between them to be a variable-length inert step. Relate the states
by corresponding bracket/delimiter nodes, identical delimiter parents/activity,
saved labels and reference keys, and corresponding source cursor locations;
erase outside literal values and contract their coordinate intervals.
Corresponding definition provenance means the same logical environment winner,
not object identity across two independent ASTs; the exact `ReferenceEquals`
sentinel check remains an intra-parse check.

**Literal collection.** Outside alphanumeric characters and spaces are not
opening characters of any default inline parser. The literal collector advances
to the same next active character and cannot manufacture brackets. Every
nonempty alphanumeric run stays nonempty, every space stays where it was in the
token order, and contiguous-versus-separated source intervals remain contiguous
versus separated. Thus literal creation/coalescing can change content and numeric
length but not the open-container ancestor used by the next bracket. Merging a
literal with the preceding literal depends on source-buffer identity and
contiguity, both preserved within each parse's corresponding source sequence;
it does not inspect the literal's letters. The default collector's optional
`PostMatch` hook must remain absent. See
[LiteralInlineParser](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Parsers/Inlines/LiteralInlineParser.cs).

**Opening brackets and labels.** All atom bytes are unchanged, so the label
helpers inspect the same bracket-local string, return the same normalized key,
and receive the same presence answer. Their reads do not extend through an
outside run. The saved opener label and its activity therefore agree.

**Closing-bracket lookahead.** Immediately after an internal `]`, the next `[` of
the explicit label is unchanged. Immediately after an atom, the next character
is an unchanged space or end-of-inline-slice; even if a relaxed lexical domain
allowed an alphanumeric boundary, it would remain in the same inert class.
Hence the `(` inline-link branch, `[` full/collapsed branch, saved-label shortcut
branch and failed-reference `]`/`[` boundary checks make identical decisions.
There is no unproved whitespace-skipping assumption: these tests inspect the
current character, while label-helper reads stay inside the untouched atom.
The checks are in
[LinkInlineParser](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Parsers/Inlines/LinkInlineParser.cs).

**Surviving failed delimiters.** The relation retains them rather than treating
a space or `X` as a stack reset. After a missing full-reference attempt, the
same opener can survive across the inert segment in both parses. The subsequent
literal is attached under the corresponding still-open container. At the next
bracket, nearest-parent selection and any later `MarkParentAsInactive` walk
therefore see the same delimiter ancestry. Successful default links move the
same children and finish in the same state modulo literal payload/coordinates.
This is why the lemma covers the interaction that defeated isolated-atom tests.
The processor's attachment/ancestor behavior is exposed in
[InlineProcessor](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Parsers/InlineProcessor.cs).

**Block trims and finalization.** Original indentation and heading markers are
unchanged, so the source-independent lexical checks must establish the same
paragraph/ATX block kind before transfer. Leading/trailing space trimming removes
the same spaces at the same relative token boundaries; it cannot consume an
atom or turn an interior nonempty gap into adjacency. No closing-heading `#`,
setext marker or definition colon exists in the transformed inline alphabet.
The transformations of heading/paragraph display content are intentional and
outside the lemma. See
[ParagraphBlockParser](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Parsers/ParagraphBlockParser.cs)
and [HeadingBlockParser](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Parsers/HeadingBlockParser.cs).

Induction over these synchronized events proves the statement; the earlier
presence-mask/payload-erasure argument then transfers exhaustive admission.
No new compositional-independence assumption about neighboring atoms is needed.

### Boundaries and non-adopted broader transform

The lemma relies on the fixed default parser inventory with no global inline
parser, callback, trivia processor, literal hook or extension sensitive to text
values/columns. Adding such a parser invalidates the cache identity/lemma.
Do not expand the alphanumeric class using Unicode `IsLetterOrDigit`, reduce
label content, or insert a gap where the original had none. The touching-atoms
counterexample demonstrates why adjacency cannot be repaired by the skeleton.
The original 4,096-unit, 32-atom and four-key limits must be checked **before** a
cache hit; arbitrarily long original text can otherwise share a short skeleton.

The originally suggested ` abc123 ` to ` X ` replacement also appears compatible
with the limited link-event abstraction once block/trimming cases are handled,
but it can create/remove outside literal nodes at whitespace-only prefix/suffix
positions. A stronger proof would need an explicitly weaker literal-state
relation. It is **not adopted or approved by this lemma**. Preserving every
space and shortening only nonempty alphanumeric runs avoids that additional
obligation and is the recommended construction.

Independent implementation tests remain necessary for the transform and atom
map, including original 0/1/2/3/4-space indentation, all-missing masks followed by
resolved atoms, text/target collisions, space-only gaps/suffixes, mixed long
alphanumeric ballast, headings and original-limit cache-hit rejection. They
should compare original link events and counts against an independent fresh
parse, not compare full IR/display values to the intentionally altered skeleton.

## Second-iteration implementation inspection

Inspected `SourceMappedSkeleton.cs`, the skeleton construction/cache/binding
branch of `Session.Build`, the original-source materialization choice in
`Session.Project`, and `AdmissionWorkBudget.cs` / its `Certificate.Admit` call
site under `.temp/MarkdownPresenceCertificateProbe/`, on 2026-10-01. No
experiments were rerun. General session edit validity, performance, projection
budgets and winner-state behavior are not approved by this inspection.

**No blocking skeleton/cache-coordinate or mask-budget bypass was found.**

- `Describe` checks original nonempty length at most 4,096 before construction;
  its scanner caps atoms at 32 and checks original nonempty/raw-64-unit ASCII
  text/target labels, separated complete atom structure and at most four keys
  before returning. `Session.Build` always calls this scanner before `Find`,
  including a cache hit. A long or lexically unsupported original cannot borrow
  a short accepted skeleton.
- Every outside space is appended unchanged, each outside maximal ASCII
  alphanumeric run emits one `X`, the heading prefix is appended verbatim, and
  each full atom is copied as its original complete slice. Recorded atom starts
  are `output.Length` immediately before that copy, while original starts are
  the original cursor. This implements the narrower transform, not the broader
  arbitrary-run-to-` X ` proposal.
- `Describe` does not itself reject four-space indentation in a separate
  conditional. This is not a bypass: all initial spaces survive, so original
  code indentation remains code indentation in the skeleton; `Admit` requires a
  paragraph/heading with the expected source range. The cache contains only
  successful `Admit` outputs under exact skeleton text. A hit therefore cannot
  turn a preserved four-space prefix into an accepted paragraph. Indented ATX
  headings are conservatively rejected by the scanner rather than approximated.
- The cache hash only selects a bucket; `SequenceEqual` resolves identity
  exactly. Skeleton-mode builds do not seed it from old original-owner
  certificates. It is local to one build and uses the fixed static pipeline, so
  the absence of an explicit pipeline tag in the key does not introduce
  cross-pipeline reuse here. A persistent/configurable future cache must include
  the pipeline/transform identity explicitly. Its source-text retention cap is
  a separate 2 Mi-unit bound, not an AST or total managed-memory bound.
- `Bind` checks atom count and, for each atom, the stored skeleton start,
  unchanged length and normalized text/target keys before constructing a
  certificate with **original** atom offsets. Exact skeleton cache identity plus
  the scanner's raw copying establishes raw-atom identity as well. `Bind` alone
  does not authenticate an arbitrary externally supplied certificate's origin;
  its inspected caller provides that guarantee through exact-text cache lookup
  or fresh successful admission.
- Shared template keys/multiplicity dictionaries are only read by this inspected
  path; new owner atom arrays contain original coordinates. The record types
  are not deeply immutable, so future code must not mutate these shared arrays
  or dictionaries. `Project` obtains owner text from `state.Snapshot`, not the
  skeleton. It neither renders virtual `X` nor shifts skeleton IR into original
  text. This supports the intended separation between count certification and
  real materialization without claiming complete-IR equivalence.

### Per-build admission-work budget

The skeleton build creates one private `AdmissionWorkBudget`, defaulting to
1,024 mask calls and 1,048,576 total context UTF-16 units. Each fresh mask calls
`Charge(context.Length)` **before** `Markdown.Parse`. Under the actual positive
bounded lengths, `Parses >= maxParses` and `units > maxSourceUnits - SourceUnits`
reject the next call without counter overflow; admitted charges cannot exceed
either configured bound. Cached successful templates incur no mask call and
therefore correctly incur no mask-source charge.

Exhaustion throws the dedicated exception. `Build` catches it around the private
scan/admission stage, records `admission-work-budget` in the separate attempted-
work evidence, and returns false before state publication. Cache and staged
owners are local and discarded; `Current` and the last committed `Last` metrics
are not assigned on that path. No intrinsic grammar-failure entry is installed.
This verifies the resource-failure distinction for that path, not cancellation
or every general session failure route.

Two scope limits matter. The context string is constructed **before** charging,
so this is a pre-parser work budget, not a pre-allocation memory limit; individual
construction is independently bounded by the source/key/sentinel caps. Also,
real definition parses, original source scans/skeleton construction, requested
materialization and other session work do not consume this counter. Do not
advertise the mask budget as a bound on all parser calls, all scanned units or
whole-process memory. Those broader controls remain independent implementation
and measurement obligations.
