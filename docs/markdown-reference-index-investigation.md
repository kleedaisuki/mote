# Bounded Markdown reference presence certificates

Status: research/prototype, started 2026-09-30; refined 2026-10-01. **No production format, Native, Engine, or
test files were changed. Production remains gated.** This advances
[the reference-index design](markdown-reference-index-design.md), specifically
the failed adjacent-atom multiplicity hypothesis, without claiming general
incremental CommonMark completeness. Independent source-level proof and
certificate-code inspection are recorded in
[the presence-certificate review](reviews/markdown-presence-certificate-review.md).

## Result and decision

There is a sound, explicitly bounded admission method for a useful cross-block
reference domain: enumerate every **presence** environment of each complete
source owner, rather than assume that spaces isolate delimiter state. With at
most four distinct candidate keys, this takes at most 16 bounded Markdig parses.
Safe/unsafe URL assignments do not require another state dimension: in the
pinned default reference path they are output payload, not parser control input.

After admission, exact document-wide warning totals can be computed from
per-key consumer multiplicities and actual effective destination safety. A
popular-label destination edit need not reparse its consumers. Requested owners
are still materialized by the real parser against current actual winners; this
does not synthesize display text or unresolved delimiter IR.

The prototype gives a safe path to production, not production authorization:

- 819 exhaustive one-to-three-atom source patterns and 17,685 independently
  parsed missing/safe/unsafe/duplicate contexts had zero count/real-owner IR
  differences.
- Real Engine versions exercised first-winner promotion, rename/move, safe-to-safe
  value changes, missing reads, Undo/Redo, admission revocation/repair, exact
  consumer/declaration/token/diagnostic provenance and deterministic cancellation.
- The initial index copied all owner text and linearly searched all owners for
  every viewport. These were real defects, not merely possible optimizations.
  A revised span-only index with ordered lookup removed retained owner source
  copies and the viewport scan. On a fresh file-backed 100 MiB process, measured
  index live-memory delta was about 1.03 MiB above the Engine document.
- A bounded chunk-stream line cursor then removed the remaining repeated-corpus
  line-copy allocation: 100 MiB cold allocated 2.52 MB cumulatively, with a
  264 ms cold build. **Unique four-key owners remain costly:** a 10 MiB probe
  required 41,696 admission parses and allocated 549 MB cumulatively. This is
  a concrete production gate, not extrapolated universal speed.

## Precise scope and proof obligation

The experimental owner is one independently boundary-certified physical line,
either a paragraph or ATX heading. Its inline alphabet is ASCII letters, digits,
and ordinary spaces, plus nonnested full atoms `[text][label]`. Both text and
label have nonempty normalized keys, raw length at most 64, and that same ASCII
alphabet. Adjacent atoms, shortcut/collapsed links, images, entities in the
consumer, escaping, Unicode labels, inline code/emphasis/autolinks, containers
and multiline consumers are rejected. The line is at most 4,096 UTF-16 units,
with at most 32 atoms and four distinct candidate keys. The prototype additionally
requires empty separators between all nonempty physical lines and a plain/heading
prefix for consumers; it is narrower than the intended extension of the existing
flat/fence boundary certificate.

All real definitions anywhere in the admitted document must also use that
ASCII key universe. They are isolated single lines, no title, with a destination
at most 2,048 units. The prototype delegates definition recognition, decoded
destination and unsafe classification to the pinned parser/exact existing
policy projection. It rejects leftover owners, multiple declarations, escaping,
angle-delimited destinations and title syntax. Destination entities such as
`javascript&#58;bad` are supported by that parser-driven classification. Merely
checking raw scheme prefixes would incorrectly admit/count some destinations.

Let `N` fold ASCII case and trim/collapse ordinary spaces. For owner `s`, define:

```text
K(s) = union_i { N(text_i), N(label_i) }, |K(s)| <= 4
u_s(q) = number of explicit atoms i with N(label_i) = q
U(q) = sum over certified owners s of u_s(q)
warnings(E) = sum_q U(q) * unsafe(decoded URL of first effective E(q))
unsafe(Missing) = 0
```

The proof has three distinct obligations:

1. **Closed reads.** Every definition query made by the restricted owner is for
   an original `[text]` or `[label]` key. Missing queries and text keys are not
   discarded. Non-ASCII definitions would violate this abstraction: pinned
   Markdig can equate `cafe` and `café` through its invariant comparer.
2. **Payload erasure.** For the same presence answers on `K(s)`, changing actual
   URLs/titles preserves cursor movement, delimiter topology/activity and link
   span/key shape. Markdig's default reference branch copies/unescapes these
   fields; neither decides the next syntax transition. This argument excludes
   callbacks, extensions, trivia processing and context hooks.
3. **Complete finite checking.** For every one of the `2^|K|` masks, construct
   an isolated suffix containing one definition per present key with a distinct
   safe HTTPS sentinel. Verify actual exported keys/URLs/no title/no callback.
   Select the one real typed paragraph/heading with its exact source range.
   Recursively require exactly the expected explicit-target links, matching
   full atom range, normalized label, sentinel URL, real winner object identity,
   `IsShortcut == false`, and `IsImage == false`. No extra link is allowed.

For any allowed actual environment, one mask has the same presence restriction;
Steps 1–2 transfer the checked shape and provenance. Restoring actual winner
payloads and applying the exact policy warning predicate yields the formula.
This is a **qualified finite abstraction theorem for this domain**, not a
theorem about arbitrary Markdig/CommonMark. The implementation checks `LabelSpan`
containment inside the atom, not an independent exact explicit-label coordinate
regression. The atom coordinates themselves are checked exactly.

The known counterexample is permanently rejected before enumeration:

```markdown
Alpha [a][b][c][d] Omega

[c]: javascript:bad
```

Markdig can resolve the middle `[b][c]`; targets `b,d` from a pre-cut pair count
would be wrong. Spaces are not a delimiter-stack reset, so even the admitted
spaced grammar uses **whole-owner** masks, never isolated-atom checks.

## Prototype structure and explicit budgets

Files are isolated under `.temp/MarkdownPresenceCertificateProbe/`:

| Artifact | Contract |
| --- | --- |
| `Certificate.cs` | Lexical bounds, all presence masks, exact link/winner provenance; no AST survives admission |
| `Session.cs` | Staged source-owner summaries, ordered duplicate inventory, exact policy safety classification, multiplicities, atomic publication, bounded viewport materialization |
| `Program.cs` | Exhaustive adversarial contexts, Engine versions, deterministic cancel/retry, output checks, file-backed scale probes |
| `baseline-results.jsonl`, `baseline-scale.jsonl`, `Session.cs.baseline` | Preserved initial copy-retaining/linear-viewport negative implementation and observations |
| `results.jsonl`, `scale-1.jsonl`, `scale-10.jsonl`, `scale-100.jsonl` | Revised isolated results |
| `span-owner-scale-{1,10,100}.jsonl`, `Session.cs.span-owner` | Preserved span-only but line-copying intermediate route |
| `scale-1-unique.jsonl`, `scale-10-unique.jsonl` | Distinct four-key owner cost, with no certificate-cache hits |
| `span-owner-inprocess-scale.jsonl` | Intermediate impure sequential-corpus memory measurements; superseded for retained-index interpretation by fresh-process file-backed probes |

Retained owners contain only `(Start, Length, Certificate)`, plus the Engine
snapshot. The transient exact-text certificate interning cache is capped at
2 Mi UTF-16 units; the previous-state seeding read is independently capped at
2 Mi units. Neither cache text nor sentinel ASTs survive publication. The
hash-indexed cache always confirms exact span equality, so hash collisions affect
lookup work, not correctness. A `GetChunks` line cursor uses one 4,096-unit
buffer, including CR/LF/CRLF across rope seams, and only creates a source string
for cache misses or actual declarations. The known seam fixture puts CR at
16,383 and LF at 16,384 and matches the fresh whole oracle.
The prototype still retains bounded raw declaration lines and decoded payloads;
production should retain raw declaration **spans**, materializing context from
the Engine snapshot, and retain only needed semantic winner payloads.

Additional hard limits are 100,000 owners, 32,768 declarations, 4,096 total keys
(including missing/text candidates), and 128,000 owner-key incidences. Exhaustion
returns unsupported rather than publishing a misleading Complete result.
These count caps bound work, but a production byte-budget admission gate is still
needed: 32,768 maximum-size declaration copies would be substantial despite a
legal count. No claim is made that these initial caps match mote's final policy.

Narrow owner and declaration viewport lookup use binary search over disjoint,
source-sorted spans. Materialization stops at 128 top-level owners/leaves and
reports truncation; each owner is limited to 32 atoms. The prototype currently
prioritizes consumer owners before real definitions when a combined oversized
viewport exhausts that cap, then sorts returned nodes. **Production must merge
the two ordered streams before applying shared node/token/diagnostic budgets**;
small combined windows were checked, but correct source-prefix truncation was
not established. Synthetic suffix group/declaration nodes never escape.

## Versions, cancellation and output evidence

`Build` stages all owners, declarations, winners and counts privately. Source
content reuse requires exact string equality with the same fixed pipeline;
offsets are always rebuilt from the current snapshot. Its final cancellation
and monotonic-version check precede a single state publication. Unsupported or
canceled builds leave the prior state untouched. The caller must not display
that older state as current: Complete is valid only for the matching snapshot.
One `Session` is assumed to belong to one Engine `Document`; the prototype is
not a cross-document stamp validator or a concurrent session implementation.

Observed version warning totals were `1,1,3,3,1,1,3,3,1,3,1,3`; changes included
safe-to-safe URL replacement, unsafe first winner, safe nonwinner, deletion and
promotion, rename to missing, earlier insertion, and moving a winner across
consumers through two real edits. Undo/Redo create new versions. Removing the
space between atoms refused certification at version 13, retaining version 12;
repair published version 14. Adding a new consumer published version 15 with
four warnings. The `results.jsonl` includes each version/count/cost record.

Cancellation was injected deterministically at begin, scan-line, environment,
aggregate-owner, before-commit and a new owner's presence-mask step. Reference
identity of the committed state remained unchanged, and retries passed the
fresh full oracle. The same-size declaration payload route was canceled at
begin, definition-parsed and before-commit, then retried successfully. These
prove tested transaction boundaries, **not** bounded wall-clock cancellation
latency inside Markdig, which is non-preemptible for each bounded parse.

For each small committed version, every real consumer-only, declaration-only
and whole-document window matched fresh whole-policy output: recursive node
kind/name/value/absolute UTF-16 spans, semantic tokens and diagnostic code/spans,
plus exact global warning total. Only the typed definition-group wrapper is
normalized to real leaves; duplicates and unused unsafe declarations remain
visible as real source facts. LF/CRLF, ASCII space/case normalization, decoded
unsafe entities and Unicode/title/multiline/fence/separator refusals were also
checked. The generic construct tree was not globally flattened.

## Performance observations and negative results

Environment: Windows x64 `10.0.26200`, SDK `10.0.400`, runtime `10.0.11`, Release,
Markdig **NuGet 1.3.2** (its assembly version prints `1.3.0.0`). No production
project was rebuilt by the probe: Engine/Formats are direct Release DLL
references; the oracle projection is the already existing experimental source
copy with precise locations. `results.jsonl` records the Format assembly SHA.

Each size below uses a fresh process, a repository-local UTF-8 corpus opened
through `Document.OpenAsync`, one cold build, then 21 alternating equal-size
safe/unsafe whole-definition-line edits and 21 head/tail two-window projections.
Discard the first warm observation; report the lower median and nearest-rank
p95 of 20 remaining samples. Engine edit execution itself is **outside** rebind
timing. The corpus repeats one 4,028-unit owner and one distant definition;
interning means only four admission parses. It is not a dense-unique stress test.

| Requested size | Consumers | Cold build ms (one observation) | Cold cumulative allocation | Index live delta after full GC | Rebind p50 / p95 ms | Two-window p50 / p95 ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 MiB | 260 | 75.18 | 159,200 B | 55,648 B | 0.0183 / 0.0688 | 0.1039 / 0.1980 |
| 10 MiB | 2,601 | 123.29 | 385,320 B | 149,000 B | 0.0330 / 0.0913 | 0.1106 / 0.1273 |
| 100 MiB | 26,019 | 264.09 | 2,524,016 B | 1,080,432 B | 0.0178 / 0.0520 | 0.0932 / 0.1067 |

All successful rebinds scanned one 51-unit definition, ran zero consumer grammar
parses and one recorded definition admission, updating the exact warning total
between zero and all consumers. Each rebind allocated 6,304 B at median.
`DefinitionParses` in the prototype is a declaration-admission counter, not the
literal number of Markdig invocations: one admission performs an isolated AST
parse plus an exact-policy consumer parse for decoding/safety classification.
Thus the fast route runs two bounded parser invocations, not one. Each
two-window projection allocated 109,360 B at median, including current owner
reads and actual parser projection. At 100 MiB, full-GC document-only live
memory was 210,951,824 B; final process RSS was 255.71 MiB. The index delta is a
GC difference observation, not a precise per-object size proof.

The earlier baseline's 100 MiB cold run allocated **1,459,749,320 B**, retained
104,804,532 UTF-16 units of owner source copies, and took about 1,357 ms. Its
two-window p50 rose to 1.217 ms because of the full owner-table scan. The revised
source-span/ordered route removes those two mechanisms. Intermediate
same-process giant-string corpus measurements showed misleading 0.88–1.19 GiB
live totals; they mixed corpus construction/lifetimes and multiple sizes. They
are preserved, not interpreted as index size or silently compared with fresh
file-backed measurements. No statistically causal speedup percentage is claimed
between these differently controlled exploratory runs.

The span-only intermediate cold 423 MB allocation came largely from line
materialization (`GetLine`/rope range builder), not its four cached admission
parses. The chunk cursor eliminates that repeating-source mechanism without
changing the certificate. Unique owners cannot depend on interning:

| Distinct four-key corpus | Owners | Mask parses | Cold ms (one observation) | Cold allocation | Index live delta |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 MiB | 260 | 4,160 | 221.86 | 54,706,136 B | 195,264 B |
| 10 MiB | 2,606 | 41,696 | 952.13 | 549,210,584 B | 1,546,560 B |

Each owner has two atoms, four keys, and a different ten-digit plain-text
suffix; the admission cache has zero hits. Its two-window p50 at 10 MiB was
0.035 ms and rebind p50 0.0144 ms, but the cold 549 MB allocation is material.
No 100 MiB unique-owner result was collected; do not multiply the 10 MiB timing
into a claimed 100 MiB measurement **in the first iteration**. The proposed next
discriminating approach was to certify
a source-mapped **inline skeleton** that removes inert alphanumeric ballast
while preserving all bracket atoms/separators and exact mapped link spans. That
requires a separate parser-state simulation proof, including surviving failed
delimiters; at that point it was a hypothesis, not authorization. The second
iteration below establishes a narrower version and records actual 100 MiB
measurements. A simpler initial
production safety gate is a total admission-work budget (mask count/parsed units)
that refuses Complete on exhaustion without poisoning the intrinsic bad-line
cache. No results here establish startup time, Native AOT throughput,
frame presentation, actual typing latency or GUI responsiveness.

## Second iteration: source-mapped inert-run skeleton

Completed 2026-10-01. The dominant first-iteration negative had two independent
causes: every distinct owner repeated all 16 environment parses, and those parses
repeatedly materialized a long inert alphanumeric suffix. A source-mapped skeleton
removes both mechanisms without inventing an atom-independence rule.

### New admission lemma and actual implementation

The adopted transform is deliberately narrower than arbitrary outside-run
replacement:

```text
outside maximal ASCII alphanumeric run -> one literal X
outside ASCII space                    -> the same space, byte-for-byte
ATX marker prefix                      -> unchanged
raw [text][label] atom                  -> unchanged, byte-for-byte
empty outside run                      -> still empty
```

`SourceMappedSkeleton.Describe` first validates the **original** 4,096-unit,
32-atom, four-key lexical domain. It never materializes the full original owner
string: it reads the ephemeral bounded line span, creates only the skeleton and
bounded label/key facts, and stores original atom offsets. Exact raw atoms and
all spaces remain intact. Thus a touching-atom original cannot acquire a
synthetic separating space. Four-space indentation also remains four spaces;
the existing typed exact-owner parser check rejects its code block rather than
upgrading it to a paragraph. The prototype's exact `Span.Start == 0` check also
rejects indented paragraphs, a safe narrower boundary rather than a claim of
general indentation support.

The independent theorist's [new lemma and implementation inspection](reviews/markdown-presence-certificate-review.md)
relate original and skeleton parser states at corresponding bracket events.
Inert alphanumeric collection changes only literal payload/length; the next
opening character, contiguous-node coalescing, saved labels, delimiter ancestry
and active flags correspond. Failed reference openers remain in this relation;
the proof does not reset them at spaces. All environment reads come from the
same intact raw bracket labels. Successful link events have the same key/winner
and corresponding full-atom intervals. The previous presence-mask theorem can
therefore transfer from a checked skeleton to every allowed original owner
that maps to it.

`Bind` verifies the template's atom count, skeleton start, unchanged atom length,
text key and target key, then installs **original** atom ranges. This is an
atom-local translation, not a bijective map of virtual `X` or full owner ranges.
The cache compares the exact skeleton text after hash lookup, within one build
and one fixed pipeline. Neither hash collisions nor a short skeleton allow
original bounds to be bypassed. The current shared arrays/dictionaries are
treated as immutable by the prototype, but production should enforce this in
its types rather than rely on convention.

**Only link event shape/count/key/winner provenance transfer.** Entire AST/IR,
literal/display text, owner ranges and headings' displayed content do not.
Requested output still reads and parses the original Engine snapshot against
real current definitions. The skeleton never supplies render text, diagnostics'
final coordinates, or navigation targets. The independent review found no
blocking deviation in this implementation; it did not validate general session
transitions or rerun the experiments.

### Discriminating semantics and mixed-edit evidence

`SkeletonProbe.cs` tests 3,279 original owners and **22,272 actual presence
environments** against original and skeleton Markdig parses. There were zero
link shape/key/value/source-map differences. Patterns include one-to-three
mixed/repeated atoms; paragraph and multiple ATX levels; long and varying inert
prefix/suffix runs; gaps with inert words; a 3,000-unit prefix whose original
atom coordinates differ sharply from skeleton coordinates; direct `Alpha[a]`
prefix; and trailing spaces. Seven explicit refusals include the adjacent
counterexample, five-plus keys, four-space indentation, emphasis, collapsed
syntax, tab separators and non-ASCII labels. Cache hits require a newly checked
original descriptor; they do not simply reuse the first owner's source offsets.

A separate 80-owner document checked 14 stages across Engine versions 0–13:
varying original prefix length, inert tail growth, longer safe-to-safe URL,
entity-decoded unsafe winner, nonwinner change, winner deletion/promotion,
rename to missing, earlier definition insertion, Undo/Redo, adjacent-atom refusal
and repair, then a new skeleton/key pattern. All successful stages compared
every original consumer/declaration window and combined small output to fresh
whole-parser node/token/diagnostic provenance and exact global counts. Final
warning count was 161. Adjacent version 11 refused publication, preserving the
prior committed state; repair version 12 succeeded. Cancellation at
presence-mask, environment and before-commit left the committed state intact;
retry succeeded. These checks validate the tested staged rebuild behavior,
not an optimized arbitrary-length declaration edit path.

### Fair cold-work comparison and actual 100 MiB probe

For a new comparison, run baseline and skeleton modes alternately in three
fresh-process pairs on exactly the same 10 MiB file-backed four-key corpus,
with the same pinned parser, Release binary, Engine open path and original
source. Each owner has unique inert digit text, so baseline exact-owner cache
hits remain zero; all skeletons share the same syntax facts. Cold build excludes
file creation and Engine opening. `paired-{baseline,skeleton}-10-{1,2,3}.jsonl`
preserves all observations:

| Route | Cold ms, three processes | Cold allocated bytes, three processes | Mask calls | Charged mask context units |
| --- | --- | --- | ---: | ---: |
| Complete original owner | 888.35 / 887.14 / 938.11 | 548,845,888 / 548,626,816 / 548,488,512 | 41,696 | Not instrumented in baseline |
| Mapped skeleton | 122.01 / 123.12 / 114.94 | 4,534,080 / 4,540,272 / 4,540,272 | 16 | 1,488 |

The measured cold medians are 888.35 versus 122.01 ms, and allocation medians
548,626,816 versus 4,540,272 B. This supports a strong corpus-specific mechanism
improvement, not a universal Markdown speedup or a statistically powered claim.
The original texts remain unique: sharing is justified by the new parser-state
lemma, not hash equality or accidental source equality.

The final **budget-enabled actual 100 MiB unique-owner run** in
`skeleton-budgeted-scale-100-unique.jsonl` contained 104,855,523 UTF-16 units and
26,064 consumers:

| Metric | Observation |
| --- | ---: |
| Cold build, one process | 455.27 ms |
| Cold cumulative allocation | 43,845,504 B |
| Mask parses / charged context units | 16 / 1,488 |
| Exact original-owner source copies retained | 0 |
| Index live delta after full GC | 7,948,024 B |
| Equal-size definition rebind p50 / p95 | 0.0229 / 0.0853 ms |
| Rebind median allocation / consumer grammar parses | 6,304 B / 0 |
| Original head+tail projection p50 / p95 | 0.1665 / 0.2784 ms |
| Two-window median allocation | 110,896 B |

The larger retained delta compared with the repeated-owner first iteration
comes from per-owner bound atom/map/key metadata, not copied owner text. This
still needs key interning/compact immutable range storage and a real index-byte
budget before production. Do not compare the warm values above as if they were
end-to-end editing, keyboard or frame latency. No 100 MiB baseline four-key
full-mask run was performed, so no measured 100 MiB speedup ratio is claimed.

### Explicit work budget and hostile-shape refusal

`AdmissionWorkBudget` limits each skeleton build to **1,024 mask parser calls**
and **1,048,576 UTF-16 context units**. Charge occurs before `Markdown.Parse`.
An exhausted attempt aborts its private stage, records the separate
`admission-work-budget` reason and leaves both committed `Current` and successful
`Last` metrics unchanged. Resource exhaustion is not cached as an intrinsic bad
source line. Context strings/suffixes are already constructed when charged, each
under the original bounded owner/key limits; this is **not** a pre-allocation
total-memory budget. Source scanning, descriptor construction, real definition
admission and requested projection are outside this mask budget.

A hostile 388,270-unit document varied raw spaces inside labels while keeping
the same four normalized keys, forcing distinct legal skeletons. The final
probe refused after exactly **1,024 calls / 94,208 charged units**, in 13.29 ms
with 4,885,648 B cumulative allocation. Attempt version 1 did not replace
committed version 0. Undo repair published version 2 and matched the whole
oracle. A separate 50-unit context budget refused after 1 call / 17 units;
the same source passed with the normal budget. This distinguishes bounded
resource failure from semantic invalidity and demonstrates that many individually
valid owners cannot create unbounded cold mask work.

### Material residual negative: spaces and descriptor work

The lemma preserves every outside space, so it does not magically bound
construction of long cached skeleton strings. A negative-control 10 MiB corpus
with 2,629 unique owners containing 3,950 outside spaces each still shared only
16 parses, but cold allocated **48,045,232 B**. Its cold time was 90.04 ms and
index live delta 508,528 B in the observed process. This is substantially more
allocation than the alphanumeric-ballast case despite a small mask count.

The bounded line buffer prevents whole-file source copies and the mask budget
prevents exponential parser work; neither is a total descriptor-allocation
budget. A production integration must either cap total descriptor/materialized
skeleton work, construct/compare skeletons without one long string per cache
hit, or prove a separate whitespace-run contraction rule. **Do not silently
apply the broader arbitrary-run-to-` X ` transform:** the adopted proof and
independent implementation review do not authorize it. This is an actionable
remaining gate, not a reason to discard the demonstrated alphanumeric-sharing
result. Definition payload storage, ordered combined-output budgets and general
incremental shifts also remain from the first iteration.

### Second-iteration reproduction

```powershell
dotnet build .temp/MarkdownPresenceCertificateProbe -c Release -warnaserror
dotnet run --project .temp/MarkdownPresenceCertificateProbe -c Release --no-build -- skeleton > .temp/MarkdownPresenceCertificateProbe/skeleton-results.jsonl
dotnet run --project .temp/MarkdownPresenceCertificateProbe -c Release --no-build -- scale 100 unique skeleton > .temp/MarkdownPresenceCertificateProbe/skeleton-budgeted-scale-100-unique.jsonl
foreach ($trial in 1,2,3) {
    dotnet run --project .temp/MarkdownPresenceCertificateProbe -c Release --no-build -- scale 10 unique > ".temp/MarkdownPresenceCertificateProbe/paired-baseline-10-$trial.jsonl"
    dotnet run --project .temp/MarkdownPresenceCertificateProbe -c Release --no-build -- scale 10 unique skeleton > ".temp/MarkdownPresenceCertificateProbe/paired-skeleton-10-$trial.jsonl"
}
```

`skeleton-results.jsonl` includes semantic transfer, mixed versions, budget
refusal/repair and the space-ballast negative. `skeleton-build.log` records the
isolated Release `-warnaserror` build: zero warnings/errors. Production remains
unchanged and gated; this iteration establishes a sound sharing lemma and a
bounded parser-work mechanism, not complete native product acceptance.

## Production path and remaining gates

1. Implement a Markdown-private certificate type, fixed parser/pipeline identity,
   keys/atom ranges/multiplicities and exact global source-boundary coverage.
   Extend the existing flat/fence certificate rather than replace it with a
   second general parser. Definition-looking text inside opaque fences never
   enters the environment. Keep all unsupported paths provisional.
2. Store owners and declarations as source spans, with key interning, ordered
   duplicate lists and bounded semantic payload budgets. A tracked byte estimate
   plus per-kind count limits should gate admission; validate real retained
   allocations on all four RIDs before claiming a memory guarantee.
3. Stage actual edit-chain transitions. General declaration destination edits
   must update ordered offsets through the existing run/suffix-shift mechanism,
   compare presence **and decoded value**, adjust global counts and invalidate
   only materialized dependent projections through key epochs. Source-shift
   work must not reparse all consumers. The experiment's equal-size whole-line
   shortcut proves the aggregation mechanism only, not this general transition.
4. Merge consumer/declaration source streams before common output budgets; compare
   independently normalized whole-parser source owners at partial/oversized
   windows, exact token/diagnostic bounds and synthetic-context exclusion.
5. Independently validate unique four-key owners, long duplicate chains, missing
   text-key reads, maximum destinations, multi-edit fallback, disposal, stale
   same-version presentation, cancel latency and local source-edit allocation.
   Preserve no-reference flat performance and legacy whole-analysis topology.
6. Only then integrate managed tests and four-RID Native AOT CI. Production
   remains gated until ownership, cold allocation and ordered combined projection
   obligations are resolved, even though the admission/count proof is useful now.

## Research and production connection

The relevant production lesson from [GitHub stack graphs](https://github.github.com/stack-graph-docs/)
is extraction of local facts before delayed name resolution. A global Markdown
label namespace needs dictionaries and ordered declarations, not graph machinery.
The prior [OOPSLA 2022 sound incremental name-resolution work](https://doi.org/10.1145/3563303)
motivates retaining unsuccessful reads; it is an analogy, not the proof above.
The new contribution here is separating **presence-dependent syntax shape** from
**destination-dependent payload safety**, then checking the former exhaustively
under a hard owner/key bound. The adjacent-atom counterexample directly dictated
this mechanism; more favorable random tests would not have repaired its flaw.

Authoritative parser evidence is pinned
[LinkInlineParser](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Parsers/Inlines/LinkInlineParser.cs),
[LinkHelper](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Helpers/LinkHelper.cs),
and [LinkReferenceDefinitionGroup](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Syntax/LinkReferenceDefinitionGroup.cs).
Raw copies and SHA-256 hashes are under `.cache/markdown-reference-primary/`.
[CommonMark 0.31.2](https://spec.commonmark.org/0.31.2/#link-reference-definitions)
remains the language specification; pinned Markdig plus mote projection is the
implementation oracle, with differences explicitly retained.

## Reproduction

```powershell
dotnet build .temp/MarkdownPresenceCertificateProbe -c Release -warnaserror
dotnet run --project .temp/MarkdownPresenceCertificateProbe -c Release --no-build > .temp/MarkdownPresenceCertificateProbe/results.jsonl
foreach ($mib in 1,10,100) {
    dotnet run --project .temp/MarkdownPresenceCertificateProbe -c Release --no-build -- scale $mib > ".temp/MarkdownPresenceCertificateProbe/scale-$mib.jsonl"
}
foreach ($mib in 1,10) {
    dotnet run --project .temp/MarkdownPresenceCertificateProbe -c Release --no-build -- scale $mib unique > ".temp/MarkdownPresenceCertificateProbe/scale-$mib-unique.jsonl"
}
```

Do not use the old no-size `scale` command to infer three-size results: revised
scale intentionally runs one corpus per process. All experiments, corpus files,
logs and primary-source caches stay under repository `.temp/`/`.cache/`.
