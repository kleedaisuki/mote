# Production: bounded Markdown reference semantics

Implemented 2026-10-01 in the Markdown-private Formats area. This is an additional
certified domain for large documents, **not general incremental CommonMark**.
Public policy/session, Engine, Native, executable and configuration contracts are unchanged.

## Design and admission domain

Existing small/sparse whole-document Markdig analysis and no-reference flat/fence
certification remain first-class paths. Cold Visible never invokes the new whole
source scan. Idle Full can certify an isolated source-owner stream:

- Every nonempty physical owner is separated by a genuinely empty physical line.
  CR, LF and CRLF, including rope seams, preserve original UTF-16 coordinates.
- Explicit consumers are single physical-line paragraphs/ATX headings of at most
  4,096 UTF-16 units. Outside nonnested `[text][label]` atoms, only ASCII letters,
  digits and ordinary spaces are allowed. Text and target labels are nonempty,
  ASCII letters/digits/spaces, raw length <=64. At most 32 atoms and four distinct
  normalized candidate keys (including display-text keys) are allowed per owner.
  Adjacent atoms, collapsed/shortcut references, Unicode labels, escaping, entities,
  emphasis and code in a consumer are not admitted. Column-zero `[` is reserved
  for declarations; consumers need a plain or heading prefix.
- Real declarations are isolated single lines `[label]: destination`, no title,
  escaped or angle-delimited destination. Destination <=2,048 units and visible
  ASCII with selected syntax exclusions. Markdig validates each declaration and
  exports its exact key; entities such as `javascript&#58;bad` are decoded/classified
  by the real policy, not raw-prefix heuristics.
- Bracket-free independently parser-checked paragraphs/headings can coexist.
  Exact closed column-zero ` ```json ` / ` ``` ` fences (without the explanatory
  spaces) are admitted up to 65,536 source units; interior definition-looking text
  is opaque. Containers, other fences and unclosed/intermediate constructs refuse.
- At least one actual explicit-reference consumer selects this additional path.
  A no-reference file continues using existing exact/flat behavior.

The presence-certificate proof and pinned parser assumptions are in
[the investigation](markdown-reference-index-investigation.md) and
[the certificate review](reviews/markdown-presence-certificate-review.md).
The implementation uses `MarkdownPolicy.Pipeline` itself (Markdig 1.3.2,
HTML disabled, precise source positions, no extension/callback additions).
A future pipeline/parser update requires revalidating this certificate domain.

### Mechanism

| Component | Ownership and invariant |
| --- | --- |
| `MarkdownReferenceCertificate` | Enumerates all <=16 candidate-presence masks for an entire owner, checks exported sentinels, real owner kind/range, all resolved links/full-reference provenance and permitted literal/dormant-delimiter/link tree. Never counts pre-cut atoms independently. |
| `MarkdownReferenceSkeleton` | Collapses only proved-inert outside alphanumeric run lengths to `X` in fixed scratch. Original atom offsets remain a separate exact geometry. Hash hits require exact active source or geometry equality. |
| `MarkdownReferenceIndex` | Source-ordered span-only rows, exact shared templates/geometries, canonical labels, source-ordered first declarations and only effective decoded winner payloads. No per-owner/declaration raw source retained. |
| `MarkdownReferenceBudget` | One private-stage ledger for all rows/templates/keys/geometries, declaration and independent-block parser calls, mask calls, work and measured same-thread allocation. |
| Session integration | Candidate index and bounded real-owner projection finish before the session publishes the snapshot/version and certificate together. Failed/canceled work cannot promote old facts to the requested version. |

The exact warning total is the sum of each owner's certified per-target
multiplicity times actual winner payload safety, plus independently checked
reference-free block warnings. Missing targets contribute zero. All duplicates
remain source-spanned rows, but only the first declaration controls resolution.
Actual output **always reparses original source owners against original current
winning declarations**. Skeleton `X` text, sentinel URLs and synthetic suffix
source spans never become display text, diagnostics or navigation coordinates.
Definition-group wrappers are normalized to individual real declaration source
owners on this new sparse projection; existing whole-analysis topology is unchanged.

## Edits, cancellation and lifetime

An exact contiguous one-edit chain confined to one non-fence physical owner,
with no inserted newline and untouched separator boundaries, is staged locally.
It re-admits only the changed owner, preserves immutable unchanged certificates,
updates ordered row spans and re-resolves first winners. Arbitrary-length winner
and nonwinner destination changes run **zero consumer admission parsers**;
the changed declaration and any newly effective payload use the real parser.
Removing a whole declaration body promotes the next real duplicate. Renaming a
key, changing a consumer or changing heading/plain kind is re-admitted normally.

This transition still copies/visits ordered row metadata: it is O(number of
owners), not an O(1) edit claim. It does not rescan the whole source or reparse
all consumers. After sixteen reused transitions, idle Full rebuilds compact
state; Visible refuses a transition needing whole-source rebuild and stays
Provisional until idle Full. Multi-owner edits, newline/separator changes and
missing edit chains safely rebuild on Full or remain Provisional on Visible.

The exact snapshot object, not an equal numeric version from another document,
is the reference projection generation. Session calls reject reentrancy as
required by the serialized session contract. Private admission hooks used for
validation additionally exercise stale outer-stage rejection after a newer
reentrant stage publishes. Cancellation is checked before/after opaque bounded
parser calls and before session commit; Markdig itself cannot be preempted inside
one call. All source streaming, stage reuse and aggregate loops poll cancellation.

Unsupported/budget outcomes return current-version bounded Provisional output
with unknown global count. Failure categories distinguish unsupported grammar,
resource budget, missing edit chain, cancellation and stale version. Refusals are
memoized only for the exact current immutable snapshot; budget failure is never
an intrinsic `_uncertifiedLine` conclusion. History-dependent transition failure
does not memoize a refusal of a fresh Full build. Exact/flat/reference commits,
changed-version partial commits and disposal release obsolete refusal snapshots.

## Resource and output policy

| Admission allowance | Default |
| --- | ---: |
| Modeled retained next-state accounting | 8 MiB |
| Observed cumulative worker-thread allocation | 32 MiB |
| Weighted deterministic work | 1 Gi units |
| All opaque admission parser calls | 4,096 |
| All admission parser input source | 4 Mi UTF-16 units |
| Interned normalized keys | 4,096 |

Modeled accounting is conservative policy accounting, **not measured managed
heap size**. Observed allocation is a detection gate, not a preemptive hard cap:
a single bounded in-progress parser/container step can overshoot before the
next poll. Native memory, other threads, Engine text/history and outer bounded
projection are outside that allocation stamp. Limits are internal format policy,
not user options that can make semantics falsely Complete.

Consumer, declaration and independent-owner output share one source-ordered
owner-atomic prefix allowance: 512 recursive nodes, 512 tokens, 128 diagnostics,
131,072 value units, 262,144 real-owner-plus-context parser source units. Exhaustion
omits a whole next owner rather than emitting tokens for an absent node. Complete
means exact global certification/count, not materialization of every document
owner. Flow independently honors its existing explicit truncation flags and caps.
Ordered binary lookup avoids a whole-owner scan per viewport.

## Acceptance evidence

- Formats Release `-warnaserror`: 0 warnings/errors.
- Implementer certificate suite: 25/25 initially; two added reviewer-lifetime and
  stale-reentrant publication regressions passed separately (27 total exercised
  cases, not a new combined-run claim).
- Independent integration validator: 25 cases all exercised; existing broad
  Markdown regression filter 101/101 includes earlier 23 integration cases and
  therefore is **not additive**. See
  [independent validation](validation/markdown-reference-production.md).
- Independent reviewer reproduced a real P2 obsolete refusal snapshot retention
  issue after giant-file replacement by `small`; corrected and independently
  rerun with the old snapshot reference now null. See
  [production review](reviews/markdown-reference-production-review.md).
- The final two reviewer regressions ran via repository-local isolated validation
  project after concurrent Native changes temporarily prevented a shared rebuild;
  no neighboring production area was edited to suppress those errors.

Relevant commands/evidence:

```powershell
dotnet build src/Mote.Formats -c Release -warnaserror --no-restore
dotnet test tests/Mote.Tests -c Release -warnaserror --no-restore --filter FullyQualifiedName~MarkdownReferenceCertificateTests --results-directory .cache/markdown-reference-production
dotnet test .temp/MarkdownReferenceProductionValidation -c Release -warnaserror --filter 'FullyQualifiedName~Reentrant_private_stage|FullyQualifiedName~Successful_exact_commit' --results-directory .cache/markdown-reference-production
```

TRX artifacts are under `.cache/markdown-reference-production/` and
`.cache/markdown-reference-validation/`. Runtime repros and measurement source/results
are under `.temp/MarkdownReferenceProductionReview/` and
`.temp/MarkdownReferenceProductionProbe/`; no experiment artifacts are outside the repository.

## Managed file-backed measurements (not native UI acceptance)

Environment: Windows 10.0.26200 win-x64, .NET SDK 10.0.400 / Release managed runtime,
2026-10-01. Three fresh `dotnet run --no-build` processes opened the same UTF-8
file through `Document.OpenAsync`, then separately measured cold Visible and Full
using `Stopwatch` and `GC.GetAllocatedBytesForCurrentThread`. The stopwatch excludes
file creation/opening, includes Analyze projection, and cold Visible includes the
first parser/JIT work. No forced GC occurs within a timed call.

Corpus: `[b]: https://first.test` and `[d]: javascript&#58;bad`, blank separators,
then repeated `Alpha [a][b] [c][d] ` + 4,000 `a` units + ` End`, blank separators.
Repeat until >=100 Mi ASCII bytes. Exact file has 104,861,246 UTF-16 units, 26,048
rows including two declarations, one skeleton template and one geometry.
Viewport is one source unit at document midpoint.

| Measurement | Observed result |
| --- | --- |
| Three cold Full calls | 260.47, 285.07, 284.86 ms; median 284.86 ms |
| Cold Full Analyze allocation | 3,118,944 bytes each; admission stamp 3,054,336 bytes |
| Cold admission work | 20 parser calls / 1,504 parser input units; 2,521,030 modeled retained bytes |
| Exact warnings | 26,046 initially; 52,092 when both winners become unsafe |
| Thirty length-changing destination edits, including first transition in each process | median 4.164 ms, nearest-rank p95 13.945 ms, max 15.443 ms; ~3.012 MB Analyze allocation per call |
| Same edit evidence excluding first transition per process (27 samples) | median 4.080 ms, nearest-rank p95 9.092 ms |
| Destination edit admission | 2 declaration/payload parser calls; zero consumer mask parsers |
| Separate forced-GC managed live delta above already-open document | 853,672 bytes; one run, not four-RID acceptance |
| Cold Visible | 32.47–39.39 ms, 206–212 KB; Provisional/unknown total, no reference index; not an instant-startup claim |

Destination edits replace the entire first winner destination (not a one-unit
synthetic shortcut), alternating `javascript&#58;changed` and
`https://different.test/longer`; ten edits/process. The 100 MiB warm p50/p95
research targets are met on these 30 managed samples; first-transition max
exceeds 15 ms, and sample sizes are not production tail-latency guarantees.

Negative measurement: giving each consumer a unique display-label key produces
105,068,899 source units; its first Full returns Provisional/unknown total at
134.80 ms with 19,913,512 Analyze allocated bytes, rather than admitting an
unbounded certificate. Existing no-reference flat corpus remains on the old
path: measured cold Full 905.49 ms / 664 MB and first local edit 1.15 ms / 35.6 KB.
These are **different corpora**, not a new/old performance ratio or evidence
that old flat cold allocation was fixed. No baseline binary comparison was run;
no-reference nonregression rests here on route preservation and existing tests.

Reproduction source is `.temp/MarkdownReferenceProductionProbe/Program.cs`:
`dotnet run --project .temp/MarkdownReferenceProductionProbe -c Release -- reference`
(and `--no-build` fresh repetitions; `flat` and `unique` select controls).
Raw JSONL is retained as `reference-{1,2,3}.jsonl`, `reference-live.jsonl`,
`flat-current.jsonl` and `unique-current.jsonl` in that directory.

## Remaining boundaries

Four-RID hosted Native AOT integration, accepted-corpus live memory on each RID,
real first paint/input/render tail latency and general CommonMark are not
established by this work. Source-backed generation and finite presence checking
are used instead of introducing a generic dependency graph: this follows the
production lesson of local-fact extraction before resolution and the sound
incremental name-resolution literature discussed in the linked investigation.
Existing no-reference flat cold allocation is a separate known optimization
opportunity. There is no telemetry/network/signing/platform mutation in this change.
