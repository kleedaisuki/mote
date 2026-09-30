# YAML alias-key recovery certificate

## Contract and failure

`YamlIncrementalSession` streams files above 256 KiB and normally certifies a Full
analysis as `Complete` after consuming all events. YAML 1.2.2 requires an alias to
refer to an earlier anchor and defines mapping-key uniqueness through recursive
node equality, not spelling ([YAML 1.2.2 §§3.2.1.3, 3.3.1, 7.1](https://yaml.org/spec/1.2.2/)).
An unbound alias in a key is therefore both an error and an unavailable canonical
key. The previous stream checker reported `yaml.undefined-alias` but left its
completeness certificate intact; `? *missing\n: first\n? *missing\n: second\n`
could report `Complete` without deciding whether the two keys collide. The same
problem occurred when an alias was nested inside a sequence or mapping key.

## Mechanism

`ParseNode` already receives `needsCanonical`: true for mapping keys and nodes
anchored for possible later alias use. When it finds an unbound alias in that
context, it retains the existing `yaml.undefined-alias` error and also emits
`yaml.key-equality-unsupported`, downgrading the whole result to `Provisional`
with unknown `TotalDiagnosticCount`. It does **not** invent a duplicate-key
error. An unbound alias in an ordinary value still receives the existing error;
there is no new assertion that it affects key uniqueness. This is a recovery
certificate, not a YAML grammar extension or a promise to continue after parser
syntax exceptions.

## Evidence and limits

Release `YamlStreamDifferentialTests` passed 14/14: valid canonical scalar,
sequence, mapping and anchored-alias keys still match the whole-source oracle;
unbound direct, sequence-nested and mapping-nested keys downgrade. An anchored
ordinary value containing an unbound child alias also downgrades before a later
alias to that anchor is used as a key. One offscreen alias key after >2 MiB of
comments produces both diagnostics at the exact absolute `*missing` span.
The valid input path stayed `Complete`. A single Windows x64 Release run using
`.temp/yaml-session-bench` observed the existing 16 MiB and 100 MiB valid
structured files as `Complete`: cold 701 ms / 2,818 ms, cumulative allocation
107.7 / 674.5 MiB, 100 MiB peak working set 259.4 MiB. The 100 MiB full-edit
re-stream took 3,337 ms and allocated 674.5 MiB; Visible took <1 ms and remained
`Provisional`; cancellation requested after 25 ms was observed at 27 ms. These
are single samples, **not** a before/after speedup or p95 latency claim.

The existing structural-density, scalar-size, anchor-summary and diagnostic
budgets still force bounded provisional results; edits still re-stream rather
than reuse parsed subtrees. Undefined aliases inside ordinary values are errors
but are not independently treated as an incomplete uniqueness proof unless a
canonical form is requested. Further recovery work should compare supported
malformed key graphs against the small-document oracle, retaining provisional
status whenever a key comparison is skipped.
