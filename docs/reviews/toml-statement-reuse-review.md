# TOML compact statement reuse review

Date: 2026-10-01. Reviewed the coherent pending compact-IR/cache implementation in
`TomlStatementSummary.cs`, `TomlStatementReader.cs`, `TomlStatementCache.cs`,
`TomlIncrementalSession.cs`, and the decoded-key ownership overloads. Read together
with `toml-normative-ownership-review.md` and the approved
`../architecture/toml-statement-semantic-reuse.md` design.

## Verdict

No substantive correctness defect found in this scoped change. It improves the
large-file semantic path without treating source identity, statement seams or
global ownership as mere version/hash guesses. This verdict does not cover the
separate future small-file policy unification or removal of resource refusals.

## Checked invariants and executable paths

1. **Trusted cache origin.** A cache is produced only by a successful whole-source
   statement scan with exhaustive ownership, or by repairing such a cache. Cold
   Visible analysis has no certificate. Same-object reuse is valid because the
   retained snapshot is immutable; an unrelated snapshot with the same version
   cannot take that shortcut.
2. **Actual source agreement.** Repair accepts one forward versioned edit and
   checks old bounds, expected new length, exact unchanged prefix/suffix, and actual
   inserted characters. Shared immutable memory identity is an equality proof;
   otherwise UTF-16 contents are compared. False payloads, offsets, missing history,
   omitted outside edits and unsupported multi-edit histories take cold fallback.
3. **Seam safety.** The repair starts at the preceding owning statement, including
   the preceding newline when an edit starts at a seam. It stops only at a parsed
   current seam that maps exactly to an old end after the entire deleted range.
   Collection/string edits can consume old seams; EOF without a matched seam
   consumes the entire remaining suffix. Retained suffix source is already proved
   identical and begins at a certified old/current boundary, not a guessed newline.
4. **Syntax before dependencies.** Each changed unit is independently validated,
   including inline ownership. Summaries retain decoded keys and local spans, not
   Tomlyn trees or value contents. Scalars, arrays and inline tables all seal their
   external path, so changing a validated value category cannot alter external
   ownership effects. Invalid inline duplicates cannot exploit effect equality.
5. **Global effects.** Only identical ordered non-trivia actions and decoded key
   components skip namespace replay. Changed effects replay every statement in
   source order, including off-screen dependent headers and keys, using the
   normative independently latest array-element scopes. First ownership conflicts
   preserve `TOML_OWNERSHIP` and current UTF-16 key coordinates; incomplete totals
   remain unknown.
6. **Staging and lifetime.** Syntax, ownership and current-coordinate projection
   complete before the final cancellation check and single cache assignment.
   Cancellation/exception during staging leaves the previous cache intact. Published
   non-Complete output clears the prior cache. A switch to the existing small path
   retires it. Reentrant Analyze is rejected and later calls after Dispose fail.
   This is a serialized session design, not a concurrent Analyze/Dispose guarantee.
7. **Resource honesty.** Statement/line/count/binding budgets still refuse Complete.
   Repaired statement count is checked after prefix/suffix mapping. Every accepted
   summary contributes to the resulting stream, and no resource refusal is converted
   into a zero-error certificate.

These semantic distinctions are consistent with the official [TOML 1.1 table rules](https://toml.io/en/v1.1.0#table),
[self-contained inline tables](https://toml.io/en/v1.1.0#inline-table), and
[latest-element array references](https://toml.io/en/v1.1.0#array-of-tables).

## Validation evidence inspected, not repeated

Independently parsed the existing retained TRX files:

| Artifact | Executed / passed | Failed / unexecuted |
| --- | ---: | ---: |
| `.cache/toml-reuse/toml-ir-existing.trx` | 766 / 766 | 0 / 0 |
| `.cache/toml-reuse/toml-reuse.trx` | 44 / 44 | 0 / 0 |

The new retained tests compare repaired and freshly analyzed current-coordinate
results and independently state validity expectations. They cover changed value
categories, duplicate inline keys, decoded aliases, explicit-table/array conflicts,
off-screen suffix dependencies, multiline ownership, newline insertion/deletion,
false histories, same-version unrelated snapshots, warm versus cold Visible,
actual engine undo/redo, cancellation/exception staging, retirement and reentry.
Work assertions distinguish syntax/scanner work from namespace transitions.

The cold comparator shares the production parser/ownership implementation; it is
strong for cache-versus-cold equivalence but not an independent language oracle.
The separately retained published corpus and Python/Rust differential evidence
remain necessary for normative validity confidence. This review did not repeat a
completed suite or run GUI/input experiments.

## Scope and performance limits

Mapping and projection remain O(statement count); changed namespace actions can
require O(binding count) replay. Exact source agreement can compare unchanged text
when storage is not shared. Internal work counters deliberately exclude source
agreement, metadata mapping, projection and viewport lexing, so zero parsed/scanned
characters is not zero total work or constant-time editing. Failed repair work is
included before any subsequent cold Full parser/scanner work.

The cache retains one committed snapshot root plus immutable statement metadata;
it does not retain all historic parser trees or all values. This still adds memory
relative to the previous discard-after-call path. No latency/allocation benchmark,
large arbitrary-language certification, native AOT execution, native GUI result,
or general thread-safety guarantee is implied by these focused checks. Quantify
end-to-end edit latency and retained metadata before making product-level claims.
