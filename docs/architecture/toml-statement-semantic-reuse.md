# TOML statement semantics and edit reuse

Status: proposed implementation, 2026-10-01. Read together with
[`../toml-large-semantics.md`](../toml-large-semantics.md). This is a route toward
general large-file semantics, not a claim that existing resource refusals are complete.

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
   it must not be advertised as fully sublinear incremental analysis.
7. Unknown syntax, resource refusal, cancellation or malformed boundary invalidates
   the staged candidate. Cold Visible remains bounded and Provisional; reuse of an
   already certified complete snapshot may provide Complete Visible output.

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
