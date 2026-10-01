# TOML large-file ownership certification

## Compact edit reuse and uniform policy (2026-10-01)

Large Complete analyses now retain validated logical-statement summaries and exactly
one committed snapshot identity. A real single edit repairs only affected owners,
expanding to a certified mapped seam; exact unchanged prefix/suffix and inserted
payload are checked without hashes. Namespace-effect equality reuses valid ownership;
key/header changes replay all off-screen dependencies. Missing/bogus/multiple edit
history rebuilds safely. Mapping/projection remain O(statement count), and resource
refusals remain real gaps. Cold Visible is still Provisional; a cached Complete source
may return Complete Visible. The 57 retained reuse controls pass after the current
reader/recovery changes.

The public small/string policy now uses the same normative ownership model with an
unbounded validation mode rather than Tomlyn's disputed whole-file nested-AoT ownership.
It preserves the lossless grammar tree, per-statement validated local semantics and
all independently recoverable diagnostics, including rollback and invalid-header scope
quarantine. Final current-source evidence is **801/801** after independently finding
and fixing 28 EOF span regressions. The prior first-witness draft and failed evidence
are documented, not hidden. See
[`architecture/toml-statement-semantic-reuse.md`](architecture/toml-statement-semantic-reuse.md)
and [`validation/toml-statement-reuse.md`](validation/toml-statement-reuse.md).

Matched <=8 MiB managed experiments show real edit reuse and retained-metadata/cold
cost tradeoffs; final public-policy duplicate parsing also incurs measurable work.
Exact source/binary hashes, adverse observations, excluded control mistakes and scope
are in [`performance/toml-statement-cost.md`](performance/toml-statement-cost.md).
No local GUI, AOT/macOS execution or arbitrary-size completeness is claimed here.

## Normative ownership correction (2026-10-01)

The large Full path now uses the complete table/key ownership transition model
within its existing syntax/resource domain. Parent array-of-tables re-entry after
nested headers appends an independent latest-element scope; it is no longer
blanket Provisional. Dotted traversal through an explicitly declared table or an
array-of-tables is a proved `TOML_OWNERSHIP` error, not unsupported uncertainty.
Implicit header parents retain the previously correct `ImplicitHeader → Dotted`
transition. The [TOML 1.1 table and array rules](https://toml.io/en/v1.1.0#table)
are authoritative where Tomlyn's whole-document nested-array validator disagrees.
Individual statements still receive Tomlyn syntax/inline-ownership validation.

All trivia now receives parser validation **before** a no-key/table shortcut.
The prior C# `IsNullOrWhiteSpace` / unvalidated-comment branch falsely certified
form feed, vertical tab and trailing bare CR. The complete pinned toml-test 1.1
manifest exposed these actual production defects: before, 214/218 valid Complete
and 3/485 decoded-invalid Complete; after, **218/218 valid Complete and 0/485
decoded-invalid Complete**. Nine ill-formed UTF-8 fixtures are separately refused
by the fixture decoder, not counted as analyzer rejections. Invalid decoded files
remain **Provisional**, not complete diagnostic enumeration. Exact 712 fixture byte
sequences and the upstream license are embedded in the test assembly.

The retained 40,000-case Python/Rust corpora were replayed for the changed ownership
model: seed 6451 gives 3,301 Complete, seed 7741 gives 5,706 Complete, zero false
Complete and zero oracle disagreement. These finite validity checks do not verify
expected parsed values or prove all grammar. Full modeling, cache invariants,
source references and corpus provenance are in
[`architecture/toml-statement-semantic-reuse.md`](architecture/toml-statement-semantic-reuse.md).

The first focused run had **795/796** passing tests: the one failed controller
control used now-proved-valid nested AoT re-entry as an uncertainty fixture. Its
suffix was replaced with a genuinely unclosed array outside the viewport; all
presentation/lifecycle assertions remained unchanged, and that sole failed test
then passed **1/1**. Results are `.cache/toml-normative/{toml-normative,
toml-idle-correction}.trx`. The 712 passing corpus cases were not rerun solely
for the controller fixture edit. Independent review found no material production
defect (`reviews/toml-normative-ownership-review.md`).

Statement/binding/line/length resource caps, small-file Tomlyn behavior, cold Visible
provisional semantics and full re-streaming after edits are unchanged at this
checkpoint. The following 2026-09-30 section records the earlier restricted model
and measurements; its explicit-table/AoT uncertainty claims are superseded above.

## Decision and boundary (2026-09-30)

`TomlIncrementalSession` can now certify a valid source-order pattern that it previously
reported as `Provisional`: a dotted assignment may define an **implicit parent** created
by either an ordinary table header or an array-of-tables header. For example:

```toml
[a.x.y]
[a]
x.z = 3
```

The same transition works if the first header is `[[a.x.y]]`. In both cases `x` exists
implicitly after the first header; the later dotted assignment adds `z` under `x` and
defines `x`. It is then invalid to explicitly redeclare `[a.x]`. Conversely, if `[a.x]`
was explicitly declared before `x.z = 3`, the dotted assignment cannot extend that
explicit table. A duplicate `x.z` remains invalid. TOML's [table rules](https://toml.io/en/v1.1.0#table)
distinguish implicit table creation, dotted-key definition, and explicit table headers;
its [array-of-tables rules](https://toml.io/en/v1.1.0#array-of-tables) bind nested
references to the most recent array element.

The ownership index uses one existing state transition, `ImplicitHeader → Dotted`,
when a dotted assignment traverses an implicit parent. This is more accurate and simpler
than the previously contemplated separate ordinary-header and array-header origin types:
both follow the same rule for this transition. The parent's child scope is retained, so
duplicate and scalar-prefix conflicts continue to be detected. Later explicit opening
is rejected because only an *unconsumed* `ImplicitHeader` may become `ExplicitTable`.

This is a narrow certification extension, **not** general TOML conformance. Each logical
statement must still pass Tomlyn 2.10.1 validation; the whole-file index must remain
exhaustive (at most 200,000 bindings); statement length, line count, and total count
retain their existing caps. Dotted traversal through an already explicit header or an
array element remains `Provisional` rather than guessed. Parent array-element re-entry
after a nested header remains `Provisional` because Tomlyn's validated whole-file
behavior conflicts with two independent processors there. The change does not create a
source copy, relax the 4 MiB path threshold, alter UTF-16 source spans, or add an AOT
dependency. Large-file Full still re-streams after edits; `Visible` remains provisional.

## Differential evidence

Direct, exact fixture checks used Python 3.14.6 `tomllib` and Rust 1.98.1 with
`toml = 1.1.6` (source in `.temp/TomlCertRust/src/main.rs`):

| Source-order pattern | Python | Rust | New large-file result |
| --- | --- | --- | --- |
| `[a.x.y]` → `[a]` → `x.z=3` | valid | valid | `Complete` |
| `[[a.x.y]]` → `[a]` → `x.z=3` | valid | valid | `Complete` |
| `[a.x]` → `[a]` → `x.z=3` | invalid | invalid | `Provisional` |
| `[a.x.y]` → `[a]` → `x.z=3` → `[a.x]` | invalid | invalid | `Provisional` |
| `[[a.x.y]]` → `[a]` → `x.z=3` → `[a.x]` | invalid | invalid | `Provisional` |
| `[[a.x.y]]` → `[a]` → `x.z=3` → `x.z=4` | invalid | invalid | `Provisional` |

The project-local `.temp/TomlCertProbe/Program.cs` replays production
`ProcessStatement`/`Continues`/`TomlOwnershipIndex` against the pre-existing
`.temp/TomlNested` corpora without padding each small input above 4 MiB. Python and
Rust had identical validity decisions on all 40,000 generated cases. After the change,
the 10,000-case seed-6451 corpus returned 3,228 `Complete`, all oracle-valid; the
30,000-case seed-7741 adversarial corpus returned 5,434 `Complete`, all oracle-valid.
The previously documented restricted certifier returned 3,223 and 5,425 respectively.
Thus 14 additional *sampled* valid documents are certified, with zero false `Complete`
in this finite probe. This is differential evidence, **not a proof** for the full grammar;
the model's restrictive fallback remains part of its correctness argument.

`dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --filter
"FullyQualifiedName~Toml" --no-restore -v quiet` passed **25/25**, including >4 MiB
fixtures, quoted-key equivalence, source-span anchoring, explicit-header conflicts, and
duplicate descendants. A local Release rerun of the unchanged 100 MiB nested-AoT
fixture returned `Complete` in 1,512 ms with 1,143.9 MiB cumulative thread allocation,
651.7 MiB process peak working set, and cancellation observed at 32 ms after a 10 ms
timer. Those one-run values are not a before/after performance conclusion; the prior
fixture recorded 1,483 ms, 1,144 MiB, and 577 MiB peak working set under uncontrolled
conditions. The substantial allocation cost remains an open performance issue.

## Correction to earlier scratch finding

The earlier `src/Mote.Formats/README.md` and `.temp/TomlNested/README.md` describe
`[a.x.y]` → `[a]` → `x.z=3` as invalid. That specific minimal is actually **valid** in
both independent oracles. Inspecting the eleven older corpus mismatches shows the
invalid examples additionally had an explicit `[a.x]` before the dotted assignment or
after it. The previous conservative `Provisional` result was safe, but its explanatory
counterexample was wrong. The shared formats README should be reconciled when its
current writer completes; do not use the obsolete example as a negative conformance
fixture.
