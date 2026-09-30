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
