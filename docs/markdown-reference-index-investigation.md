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
into a claimed 100 MiB measurement. A next discriminating approach is to certify
a source-mapped **inline skeleton** that removes inert alphanumeric ballast
while preserving all bracket atoms/separators and exact mapped link spans. That
requires a separate parser-state simulation proof, including surviving failed
delimiters; it is a hypothesis, not current authorization. A simpler initial
production safety gate is a total admission-work budget (mask count/parsed units)
that refuses Complete on exhaustion without poisoning the intrinsic bad-line
cache. No results here establish startup time, Native AOT throughput,
frame presentation, actual typing latency or GUI responsiveness.

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
