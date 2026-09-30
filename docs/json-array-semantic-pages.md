# JSON root-array semantic pages: the next large-file slice

Decision proposal, 2026-10-01. **Not implemented.** The small partition experiment
below is executed evidence, not evidence of incremental production performance.
Scope: a 100 MiB JSON export with a root array and ordinarily bounded elements.
This is the next concrete slice of [semantic ownership](semantic-ir-evolution.md),
not a replacement parser, a universal IR, JSON Lines support, or a generic roadmap.

## Why this slice

`JsonIncrementalSession.Analyze` currently ignores edit history and streams the
entire snapshot on **every** Visible and Full request. Its exact per-object key
table compares decoded UTF-16 names on hash collisions; retain that behavior.
Bounded output does not imply bounded work. The documented 100 MiB unique-key
object cold Full pass reaches Complete in seconds, but says nothing about reuse
after a keypress. [Current source](../src/Mote.Formats/JsonIncrementalSession.cs),
[existing measurements](release-gaps.md).

Choose a root array of independently meaningful export records, including nested
objects/arrays, arbitrary accepted strings, escaped names, and primitive values.
This expands useful **full JSON grammar inside elements**, rather than inventing
another punctuation-restricted certificate. A large root object is a materially
different problem: a changed property name can collide with an offscreen sibling.
Root-array elements have no such cross-element name dependency in mote's present
JSON policy. This workload choice is a product hypothesis, not measured user demand.

The proof follows the JSON array production: a sequence of values separated by
commas. Name uniqueness is an object-local interoperability rule, not uniqueness
across objects in an array. `JSON_DUPLICATE_KEY` remains mote's existing Error
policy; RFC 8259 does not make every duplicate name a JSON grammar error.
[RFC 8259 sections 4–5](https://www.rfc-editor.org/rfc/rfc8259.html#section-4).

## Chosen representation: pages, not one object per record

Engine continues to own the immutable source. JSON owns a private, version-tagged
array certificate. Public `IFormatSession`, `DocumentAnalysis`, `SemanticNode`,
configuration, and the strict one-binary delivery contract remain unchanged.

| Private state | Retained facts and invariant |
| --- | --- |
| Array shell | Verified leading JSON whitespace + `[`, closing `]` + trailing whitespace; their source intervals and current version. No trailing content. |
| Semantic page | Source interval containing one or more complete root-array values; count of elements; exact local diagnostic count; final/nonfinal boundary mode. No AST, decoded keys, source string or per-element entry retained. |
| Dirty page | Mapped interval with its old fixed outer seams; previous summary is **not current truth**. Unchanged pages are reusable candidates, conditional on repair of every dirty page and preservation of the shell. |
| Uncertified | No reusable root-array proof. Cold/boundary-changing analysis uses bounded provisional output and later authoritative Full. |

Cut a page at a **validated root-array value boundary** after roughly 65,536
UTF-16 units. A nonfinal page begins at a value token and ends at the next value
token; it includes the last separating comma and whitespace. The final page ends
immediately before the root `]`, including trailing whitespace. Leading whitespace
belongs to the shell. Each separator has exactly one owner. The empty root array
has a shell and zero pages. New page boundaries come only from the parser, never
from a line break, a brace search, or a hash.

A page's local grammar is `value (ws ',' ws value)*` followed by either
`ws ',' ws` (nonfinal) or `ws` (final). A hard range end and the expected boundary
mode must both match exactly. Invoke the **same** production value/key routines,
with the same root-array depth context, escape semantics and resource limits.
Do not wrap production source with synthetic brackets or translate a copied string:
add a range-backed page entry point to the current UTF-16 parser.

For ordinary elements no larger than 64 Ki units, a target-size page is at most
approximately 128 Ki units. A giant element makes a giant page: Full may still
validate it, but an interactive reparse must refuse expensive work rather than
pretend it is a small owner. Page metadata scales with **source size**, not element
count. A 100 MiB ASCII `[0,0,...]` file therefore does not require tens of millions
of index entries.

Use an immutable array of page summaries and copy/map its entries on an edit.
At this scope approximately 1,600 pages cost little to shift; do not begin with an
interval tree or a persistent B-tree. Cap the page count at 16,384; a cap refusal
discards the reusable index, **not the existing ability to stream a correct Full**.
The cap is a proposed design value. Actual object layout and retained bytes must
be measured, not inferred from `sizeof` a sketch.

## State transitions and correctness boundary

```text
Cold/Uncertified
  Visible -> bounded Provisional; exact total unknown; arrange existing idle Full
  Full    -> full current parser + candidate pages
          -> root-array proof only if shell, every seam/value, key identity and limits are proved

Certified(v) + contiguous edits
  map affected page spans -> repair dirty pages with a hard source-work budget
  all valid + shell intact -> Complete(v+1), exact sum, bounded current projection
  local syntax failure    -> Dirty(v+1), Provisional, no exact global total
  seam/shell edit or gap  -> Uncertified(v+1), bounded provisional + idle Full

Dirty(v) + local repair
  preserve mapped outer seams -> reparse dirty pages
  every page valid again      -> regain Complete without whole-file rescan
```

Map **each** `VersionedEdit` in its BeforeVersion coordinates, not a collapsed
final edit range. An edit strictly inside a page can change its nested structure,
element count, or local duplicate count: reparse that entire page. At an exact page
boundary, in the shell, or across pages, initially revoke the reusable certificate
and defer rebuild. This conservative boundary rule is part of the first complete
slice, not an excuse to call stale spans certified. A missing edit chain also
revokes reuse. Same-version navigation projects from the existing certificate.

On temporarily invalid typing, keep **mapped candidate** unaffected pages and the
dirty page's seams, but expose no whole-file completion. A following edit that
restores exact local grammar can repair the dirty page and restore the certificate.
An unterminated string might absorb a later comma/bracket; only a successful parse
that stops at the hard seam can re-establish independence. Never accept delimiter
counting or “unchanged suffix hash” as this proof. Boundary edits may rebuild, but
inserting/deleting a quote inside one page must not permanently lose the useful
unchanged-source summaries.

For this reusable path, syntax must be successfully validated without recovery.
Object-local duplicate diagnostics may remain and still have an exact count.
Any syntax recovery, uncertain key decoding, depth or hash-table budget failure
revokes that page's compositional proof. The existing Full parser remains the
authoritative fallback, including its established malformed-input diagnostic and
completeness behavior. Do **not** narrow the old API's Complete domain to fit pages.

Candidate mapping/repair/projection are call-local; the last cancellation check
precedes one state replacement. Canceled work cannot mutate committed pages/counts.
Dirty state may advance to the current version only as a successful provisional
publication; subsequent edit mapping must start at that version. Undo/Redo are
ordinary new versions. Cache retains no historic snapshots solely to compare keys.

Full semantic coverage is independent of projection. Reparse only pages intersecting
the existing at-most-256-Ki projected range, emit the same document/root-array shape,
current absolute source spans and clipped children/tokens/diagnostics. The root
array and document spans cover their full constructs; returned descendants are a
bounded subset. Duplicate counts are summed **once per owner**, never per viewport.

## Startup/edit work contracts and integration

Proposed source-work budgets are hard algorithmic limits, **not measured GUI times**:

| Turn | Work contract |
| --- | --- |
| Cold large Visible | At most 256 Ki units of parser input; if requested context cannot be proved from that prefix, return Provisional, not a fabricated independent JSON subtree. Stop even inside a giant string. |
| Warm current-version projection | Parse only intersecting certified pages, at most 512 Ki total including page overshoot; oversized pages give truthful provisional projection. A Complete certificate may still justify the exact global total when only display is omitted. |
| Warm edit + projection | At most 512 Ki total validation/projection source work; excess leaves dirty/provisional state and schedules existing idle Full. No synchronous hidden whole-file rescan. |
| Idle Full | Existing cancellable authoritative scan, plus page summaries; unchanged-size clean array pages need no second full scan. Streaming fallback remains available if index admission fails. |

Count budget visits in parser/key-comparison loops, not merely `range.Length`:
collision verification can revisit source. Add cancellation checks at least at the
existing approximately 4,096-unit cadence and at page boundaries. Never swallow
cancellation as a syntax error. Cold large Visible becomes less globally informative
but more responsive; this is an explicit policy change, while ≤1 MiB exact Full and
small-file behavior remain unchanged. Preserve old Complete facts for all domains
when the caller explicitly requests authoritative Full.

First implementation has three coordinated file areas, not a new framework:

1. **Formats:** `JsonIncrementalSession.cs` gains strict bounded page parsing,
   array-shell harvesting during the current Full scan, atomic versioned page
   state, dirty repair and bounded cold Visible. A private `JsonArrayCertificate.cs`
   is justified only if it keeps the parser short; Engine must know none of it.
   Existing exact `KeyTable`/`KeyReader` and recovery diagnostics remain the oracle.
2. **Native:** `NativeIdleFullAnalysis` already admits JSON; use the existing
   `NativeAnalysisDispatcher` serialization/cancellation and current-version
   publication. Add integration tests that a new JSON provisional Visible schedules
   Full, actual edits cancel it, and navigation does not restart it. No new worker
   pool, policy registry capability, LSP or config knob is needed.
3. **Tests/acceptance:** add `JsonArrayCertificateTests` for owner composition,
   page boundaries, exact offscreen counts, dirty repair, lost edit chain, Undo/Redo,
   mid-scan/precommit cancellation and no historical snapshot retention. Extend the
   native acceptance harness with the root-array fixtures below. Four-RID AOT
   source/build success is necessary but not an input-to-screen result.

## Executed inexpensive discriminator

On 2026-10-01, `.temp/JsonArrayOwnerProbe/{Probe.csproj,Program.cs}` ran:

```powershell
dotnet run --project .temp/JsonArrayOwnerProbe/Probe.csproj -c Release --nologo
```

Seed **64517**, **1,000** generated root arrays, **5,568** partitions: **zero
mismatches**. Environment: Windows 10.0.26200 x64, .NET SDK 10.0.400,
Intel i9-12900H; Release CoreCLR, not Native AOT. Scratch `Program.cs` SHA-256:
`3BAF436650155E54DADC23857D93543017A77B3530782B77E91D57F131BAD0F3`.
Each array has 1–30 recursively generated values (depth ≤4), including
objects with repeated `"a"` and escaped-equivalent `"\u0061"` keys, nested arrays,
numbers, booleans/null, literal Chinese/non-BMP characters, escaped surrogate pairs,
and strings containing escaped quotes/backslashes and `,]}`. Partitions contain 1–5
whole generated elements. Whole syntax and each bracket-wrapped partition are
independently accepted by `System.Text.Json.JsonDocument.Parse`. Fresh **actual
JSON sessions** then agree exactly on all element subtree facts (kind/name/value,
child count, translated UTF-16 spans), ordered token facts, ordered diagnostic
severity/code/message/spans, and summed exact diagnostic counts.

This probe intentionally copies/wraps small test inputs; production must not. It
tests the **semantic independence premise**, using known generated boundaries.
It does not test boundary discovery, a modified range parser, version mapping,
resource/cancellation behavior, malformed-input recovery, 100 MiB performance,
Native AOT or GUI startup. Those remain discriminators below. No performance
number or general proof follows from these finite cases.

## Decisive experiment before promotion

Use deterministic file-backed 1/10/100 MiB corpora and fresh processes, with all
scratch and outputs under `.temp/JsonArrayPages` and `.cache/json-array-pages`.
Generate a valid final array rather than truncating arbitrary repeated bytes:
repeat complete records until the target, then adjust a final string's padding.
Record both UTF-8 bytes and UTF-16 units, seed, runtime, CPU, RID, source/binary
SHA-256 and baseline/candidate commit. Reuse identical source files for comparison.

| Corpus / edit | Discriminates |
| --- | --- |
| Ordinary ~512-unit nested records with repeated/escaped-equivalent keys; LF and CRLF | Main product case; page-local exact duplicate semantics and cold/warm latency. |
| Compact primitive array, no newline | Page-count/memory bound independent of element count. |
| 64 Ki-boundary escaped quote/surrogate/number, delimiter and EOF edits | Correct UTF-16 range/depth/boundary handling, not line-based splitting. |
| 50 Mi-unit one-element string plus ordinary neighbors | Interactive hard budget and cancellation; no false cheap-owner claim. |
| Offscreen duplicate inside another element; same key in a different element | Unchanged error totals survive near-start edit; no cross-element false duplicate. |
| Quote delete → close repair; whole element change → additional elements inside page; seam delete | Dirty-state recovery versus honest rebuild/provisional; exact count delta. |

For ≥100 directed edits plus 1,000 seeded edits on small analogues, compare every
accepted reusable Complete with a fresh existing session's tree/token/diagnostic
projection and global count. Independently validate syntax with `System.Text.Json`;
it does **not** oracle mote's duplicate policy. Also exercise viewport-away/back,
contiguous batches and invalid intermediate versions. Finite oracle equality is
evidence; the hard seam/context invariant is the correctness argument.

For 100 MiB ordinary/compact corpora, perform 200 near-start/middle/end edits after
one Full and 5,000 dispersed sequential edits. Count actual visited source units,
page count and retained state. Require no warm ordinary edit to re-stream 100 MiB,
≤512 Ki visited units per interactive turn, certificate retention ≤2 MiB at this
scope, exact count/version agreement and bounded memory after GC. Proposed latency
targets: managed/AOT analysis p95 ≤10 ms for ordinary small edits on declared hosts;
these are experimental acceptance targets, not current promises. Compare cold Full
time/allocation against the same baseline in at least five fresh-process pairs;
reject >5% sustained regression without an explained measurement/trade-off.

Finally drive the **ordinary native profile**, published AOT, on Windows/macOS:
open→first editable and first draw endpoints; 100 external edits while idle Full
is running; input→actual draw p50/p95; exact Save/fresh reopen; cancellation after
5/10 ms; retained/peak process memory. Keep parent automation time separate. The
main architectural claim fails if local Complete is fast but background Full still
causes sustained native input stalls. Native input/readiness/paint probes, not
parser microbenchmarks, decide product promotion.

## External evidence and deliberately rejected machinery

- **Production:** Microsoft's JSON language service separates a parsed JSON
  document from validation/symbol consumers; preserve that separation without
  adopting a language server. Its public API is not evidence of 100 MiB incremental
  editing. Monaco explicitly disables memory-intensive features for large files
  and limits long-line tokenization: bounded resource admission is established
  practice, but mote must expose reduced semantic confidence rather than silently
  dropping promised facts. [Microsoft service](https://github.com/microsoft/vscode-json-languageservice),
  [Monaco options](https://microsoft.github.io/monaco-editor/typedoc/interfaces/editor_editor_api.editor.IGlobalEditorOptions.html).
- **Recent peer-reviewed design:** Keiser and Lemire, *Software: Practice and
  Experience* 54(6), 2024, show a lazy source-backed JSON interface can avoid DOM
  materialization in selective workloads. This supports bounded projections, not
  skipping global validation under Complete. simdjson's own On-Demand design
  explicitly validates only consumed values; array counting alone is not full
  validation. Do not use its throughput claims as mote predictions or introduce
  a UTF-8/native dependency just to obtain this representation.
  [Paper](https://doi.org/10.1002/spe.3313),
  [implementation semantics](https://github.com/simdjson/simdjson/blob/master/doc/ondemand_design.md).
- **Incremental frontier:** Yedidia and Chong, SLE 2021, demonstrate shifted
  interval memoization for fast large-input PEG reparsing. This is evidence for
  explicit reuse/invalidation, not JSON duplicate-key correctness. Start with a
  few thousand source-sized pages; adopt a tree only if the measured 5,000-edit
  suffix-mapping cost exceeds the stated budget. [Paper and reproducible artifact](https://people.seas.harvard.edu/~chong/pubs/gpeg_sle21.pdf).

Do not add schema inference/JSON Schema validation in this slice: `uniqueItems`,
cross-record IDs and external `$ref` introduce different dependencies, potentially
destroying the array-independence proof. Do not replace the parser, build a full
retained AST or general-purpose memoization engine. The useful contribution is
**complete validation plus source-sized, independent semantic owners**, with
bounded interaction and a truthful fallback on every unsupported reuse boundary.
