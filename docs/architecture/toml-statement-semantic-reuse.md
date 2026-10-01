# TOML statement semantics and edit reuse

Status: ownership correction and compact edit reuse implemented, 2026-10-01. Read together with
[`../toml-large-semantics.md`](../toml-large-semantics.md). This is a route toward
general large-file semantics, not a claim that existing resource refusals are complete.

## Uniform public policy and diagnostic recovery

`TomlPolicy.Analyze` and `Format` now share the normative ownership model with
large sessions. Actual current Tomlyn 2.10.1 rejected two source-order minimals
accepted by both retained independent processors, even though it accepted all
218 published valid corpus files. The lossless whole syntax tree therefore parses
with `validate:false` for grammar/tokens/projection; every logical statement still
passes Tomlyn `validate:true` for local grammar, numeric syntax and inline ownership.
The whole-file ownership index uses decoded keys and independently owned latest
array elements. There are no fixture-name exceptions and no discarded local checks.

The legacy public string path reads a `StringReader` without building a second
`Document`/rope. It does not inherit large-session statement/line/length/binding
budgets and releases each temporary statement summary/tree. Its ownership index
has an opt-in operation journal; the bounded cache path allocates no journal.
After an ownership error, added bindings/origin/latest-scope mutations are rolled
back. Invalid headers quarantine subsequent assignments until a successful header
establishes scope. Invalid assignments do not taint implicit parents or erase the
known valid current scope. Headers cannot span lines, so malformed header units can
recover at physical newlines; malformed multiline values can still hide later units.
Broad whitespace/control/header-tree classification is used only for *quarantine*,
never to accept invalid trivia.

Public diagnostics retain all whole-grammar witnesses plus independently recoverable
local and ownership witnesses, deduplicating exact diagnostic records. They preserve
`TOML_PARSE` and current absolute UTF-16 anchors. Statement-local EOF sentinel spans
are clamped to the actual unit before shifting. The first draft stopped at one error;
root review caught the user-visible loss of independent duplicates and it was corrected
before landing. The expanded corpus span checks then exposed 28 real out-of-bounds
EOF diagnostics (773/801 pass), fixed with unchanged assertions (801/801 pass).
These failures remain in `.cache/toml-uniform/`; they were not converted into passes
by filtering or weakening expectations.

Small sessions with any diagnostic still return Provisional and unknown total; public
`FormatAnalysis` has no completeness field. Recovery is not asserted to enumerate every
error under malformed multiline syntax. All 703 decoded corpus cases retain correct
validity, all 485 invalid decoded files remain byte-for-text unchanged by `Format`,
all 218 valid files pass format/reanalysis/idempotence/semantic-content checks, and
the two historical valid minimals now format safely. Nine bad UTF-8 fixtures remain
separate decoder-boundary evidence. The final 801 cases also include the existing
57 cache controls, multi-error/quarantine/rollback and all public diagnostic/token bounds.

The added small-file parse work is a material cost, not a free abstraction. Matched
final-source measurements and frozen hashes are recorded in
[`../performance/toml-statement-cost.md`](../performance/toml-statement-cost.md).
Current large cache measurements were separately frozen at `b93c4c2`; their whole-call
edit medians include source verification/mapping/projection, while the narrower counters
do not. None of these JIT experiments certifies Native AOT or end-to-end native latency.

## Representation and normative ownership

TOML has two independent state machines: logical-statement syntax and source-ordered
namespace ownership. A validated statement becomes a compact immutable summary:
its absolute source interval, decoded key components, statement action (trivia,
assignment, ordinary header, array header), local key/value spans and value category.
Do not retain Tomlyn syntax trees, per-statement source strings or value contents.

The namespace index uses one scope per ordinary table and one **latest element**
scope per array-of-tables binding. Earlier elements cannot be revisited by later
headers; replacing the latest scope at an array-header append is therefore sufficient
for future duplicate detection. A binding's state is implicit-header, dotted-defined,
explicit-header, array-table, or sealed-value. Inline tables and ordinary values are
equally sealed for *external* namespace ownership; internal inline ownership is
validated by the statement parser.

| Operation | Existing binding | Result |
| --- | --- | --- |
| Header traversal | implicit/dotted/explicit table | Traverse its scope |
| Header traversal | array table | Traverse latest element |
| Any traversal | sealed value | Ownership error |
| Dotted traversal | implicit-header | Define it as dotted; retain descendants |
| Dotted traversal | dotted-defined | Traverse |
| Dotted traversal | explicit-header/array table | Ownership error, not uncertainty |
| Ordinary header leaf | absent/implicit-header | Create/define explicit table |
| Ordinary header leaf | any other binding | Ownership error |
| Array header leaf | absent/array table | Create/append fresh latest-element scope |
| Array header leaf | any other binding | Ownership error |
| Assignment leaf | absent | Create sealed value |
| Assignment leaf | any binding | Ownership error |

These transitions follow [TOML 1.1 tables](https://toml.io/en/v1.1.0#table),
[inline tables](https://toml.io/en/v1.1.0#inline-table) and
[arrays of tables](https://toml.io/en/v1.1.0#array-of-tables). In particular, a dotted
key cannot redefine an explicitly declared table; a later parent array header
appends an independent element even after the prior element had nested headers.
The latter rule is explicit in the standard's fruits example. Previously retained
Python 3.14.6 and Rust toml 1.1.6 outputs agree on the two disputed re-entry files;
Tomlyn 2.10.1's whole-file validator disagrees. Its `_currentArrayIndex` is one global
validator field, whereas nested arrays need per-binding element identity. This
mechanism is an inference from source, not a confirmed upstream bug report.
Tomlyn remains the individual-statement syntax/inline-ownership parser, not a
normative whole-file oracle for these disputed source-order cases.

### Full published corpus baseline and correction

The TOML 1.1 manifest at toml-test revision
`ff49d109861c1ad25af53f687f2aef19ab650600` contains 712 `.toml` fixtures:
218 valid and 494 invalid (the manifest also includes valid JSON oracles).
The project-local `.temp/toml-conformance/Program.cs` invoked the actual production
`TryAnalyzeLarge` method on every decoded source without 4 MiB ballast. This checks
that algorithm's validity claim, not the size dispatcher, GUI or decoded values.
`.temp/toml-conformance/{baseline,normative}.tsv` preserve every fixture result.

| Expected class | Baseline Complete / Provisional | Corrected Complete / Provisional | Encoding boundary |
| --- | ---: | ---: | ---: |
| Valid | 214 / 4 | 218 / 0 | 0 |
| Invalid | **3** / 482 | **0** / 485 | 9 |

The three false Complete results were `invalid/control/bare-cr.toml`,
`invalid/control/only-ff.toml`, and `invalid/control/only-vt.toml`. The cause was
the unconditional `string.IsNullOrWhiteSpace` / comment shortcut: C# whitespace
includes non-TOML characters and comments skipped lone CR validation. Correctness
now takes precedence: validate syntax before allowing a no-key/table statement.

The four valid refusals were `valid/array/array-subtables.toml`,
`valid/spec-1.1.0/common-52.toml`, `valid/table/array-nest.toml`, and
`valid/table/array-table-array.toml`. All append a parent/latest element after
nested headers, including the standard's fruits example. The uniform latest-scope
transition removes this blanket refusal, not individual fixture special cases.

Nine invalid fixtures contain ill-formed UTF-8 and were refused by the probe's
strict decoder before an engine UTF-16 snapshot could be created. They are separate,
never credited as analyzer rejections. Engine encoding policy requires its own I/O
tests. All 712 exact source byte sequences and the upstream MIT license are retained
in `tests/Mote.Tests/Fixtures/Toml/`; total upstream source size is 46,700 bytes.
This finite conformance test checks validity/namespace ownership, **not** expected
decoded values or all possible syntax. Separate tests exercise the >4 MiB dispatcher.

## Incremental dataflow and invariants

1. A successful cold Full pass builds summaries and checks all ownership transitions.
   Commit only after syntax, ownership, projection and cancellation checks succeed.
2. A same-object snapshot reuses the summaries for a new viewport. A version number
   alone never identifies source; unrelated documents can share a version.
3. A local edit candidate requires a contiguous version chain and actual source
   agreement outside the declared replacement. Compare immutable rope slices by
   shared memory identity, falling back to exact UTF-16 comparison, never a hash.
   A missing/bogus chain or changed text elsewhere forces authoritative rebuilding.
4. Reparse from a certified preceding statement seam, through the changed region,
   until a mapped old seam coincides with a newly validated top-level seam. Multiline
   delimiters/collections can expand the repaired range; no guessed newline reuse.
5. Unchanged summaries retain decoded names and local spans; mapped statement
   starts change absolute coordinates. All visible projection spans use the current
   snapshot. No stale diagnosis or old-version node may escape.
6. If the action/decoded-key stream is identical, namespace effects are identical,
   even when values change category or quoted spellings change. Reuse the global
   valid ownership result. Otherwise replay summaries in source order, including
   every off-screen dependent header/assignment. This first representation still
   has O(statement count) mapping and potentially O(binding count) ownership replay;
   it must not be advertised as fully sublinear incremental analysis. A record containing
   position plus shared summary maps the source without cloning every semantic object.
7. Unknown syntax, resource refusal, cancellation or malformed boundary invalidates
   the staged candidate. Cold Visible remains bounded and Provisional; reuse of an
   already certified complete snapshot may provide Complete Visible output.

The first implemented repair path accepts one contiguous actual edit. Multiple committed
edits, missing history or a mismatched chain safely use Full rebuilding; broader edit-chain
reuse remains a performance opportunity, not a correctness fallback gap. Exact inserted
payload agreement is checked as well as unchanged prefix/suffix. A repaired ownership error
keeps its current first witness and retires the cache without reparsing already validated
syntax. Only one committed snapshot reference is retained; cancellation leaves that prior
certificate intact and never publishes staged state. Disposal and analysis are serialized
and non-reentrant. A small-file call retires large cached roots.

The lexer projection now reads the requested viewport plus 4,096 UTF-16 units of context
on either side (at most 256 KiB), rather than automatically reading 256 KiB for a tiny
viewport. It is still a bounded lexical projection, not a new context-sensitive highlighting
proof for windows beginning inside multiline values. Namespace semantics and projected
value categories are certified; richer cached token context is a separate rendering task.
`LastParsedCharacters` / `LastScannedCharacters` / `LastOwnershipTransitions` count only
their named work. Source verification, metadata mapping, projection and lexical parsing
remain real work outside those counters; zero parser input is not zero-cost analysis.

Validation: existing changed-algorithm regression **766/766**, retained independent reuse
controls **57/57** (`.cache/toml-reuse/{toml-ir-existing,toml-reuse-final}.trx`), plus earlier
stateful integration **968/968**. A cold same-implementation oracle checks reuse equivalence,
while pinned normative fixtures supply independent validity expectations. Independent
reviews are `docs/reviews/toml-normative-ownership-review.md` and
`docs/reviews/toml-statement-reuse-review.md`; no GUI, AOT, macOS or arbitrary-size language
claim is inferred from those portable checks.

Existing diagnostic IDs, UTF-16 anchoring, public policy/session interfaces and AOT
dependencies stay unchanged. Existing statement/binding budgets remain explicit
semantic-completeness gaps, not definitions of the desired final language. A future
page/persistent ownership index can replace flat metadata and full ownership replay
without changing these invariants.

## External grounding and alternatives

The production [toml-test](https://github.com/toml-lang/toml-test) suite supplies
version-specific valid/invalid cases and expected semantic data. It is preferable
to a self-generated corpus alone. Pin its revision when downloading into repository
cache; classify syntax-version and parser disagreements instead of quietly filtering
failing cases. Existing finite Python/Rust corpora are reusable differential evidence,
not a proof of general conformance.

[Tree-sitter edit reuse](https://tree-sitter.github.io/tree-sitter/using-parsers/3-advanced-parsing.html)
separates edited syntax ranges from semantic dependencies. We borrow certified seams
and reuse units, not a second native grammar/library. Rust's
[incremental compilation model](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation-in-detail.html)
distinguishes changed inputs from changed query results; ownership-effect equality
is the corresponding small, domain-specific red/green boundary here.
[DBSP, VLDB 2023](https://arxiv.org/abs/2203.16684) formalizes incremental view
maintenance for rich queries. Its distinction between source delta and maintained
result motivates a future ownership dependency index, but a general streaming
algebra runtime would add unjustified machinery to this six-format editor.

Rejected immediate options: retain every syntax tree (memory grows with all value
contents), parser-event-only validation (known duplicate omissions/full source copy),
trust hashes or version chains (can certify changed source incorrectly), and replace
Tomlyn's grammar immediately (large correctness and maintenance surface without
first isolating the actual cost). Focused <=8 MiB probes will separate syntax,
boundary and ownership costs before choosing deeper parser work.
