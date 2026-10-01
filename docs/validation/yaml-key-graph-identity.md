# Ordinary YAML key-graph identity validation

## Contract and scope

YAML 1.2.2 representation-graph equality is tag-aware: scalar canonical values
and tags matter, sequences retain order, and mappings do not derive identity
from presentation order. Aliases refer to their bound original node; later
anchor-name reuse does not retarget earlier aliases. Reference:
[YAML 1.2.2 §§3.2.1.3 and 7.1](https://yaml.org/spec/1.2.2/).
Cycles and unknown scalar canonicalizers remain explicitly unsupported by mote;
this slice does not claim full cyclic YAML equality. Duplicate entries within an
invalid mapping retain multiplicity for compatibility, rather than silently
turning the mapping into a set.

Independent tests in `tests/Mote.Tests/YamlKeyGraphIdentityTests.cs` derive these
expectations from the representation graph, not from the new identity integers.
The structural intern table is a production mechanism, never a test oracle.
Scope is ordinary-size public `YamlPolicy` and the small incremental-session
route. No large streaming-engine optimization, native GUI, physical input,
Native AOT performance, or cross-platform execution is claimed here.

## Frozen baseline and exact output comparison

Baseline source HEAD: `f1b929843563254ff15fb6586041d0ea2a069038`.
The Release formats output was copied before production edits into
`.cache/validation/yaml-key-identity/baseline/`; runtime DLL SHA-256:
`5F0539285F87B77A06A8DA6E42A69EE2724F9C098898BE6624586751614463CC`.
Candidate production commit: `cd6b011942acf56eaeb4b8c3e6c767ca7f26341d`;
`YamlPolicy.cs` SHA-256:
`C472344C11088604300FC5FD77DBD56D4197F3725527167502C7DC27D4B847BB`.

A separate console harness directly references the frozen formats/engine DLLs,
SharpYaml 3.13.1, and the dedicated test file for fixture generation. Each process
records the hash of the actually loaded `typeof(YamlPolicy).Assembly.Location`.
It serializes exact SourceText, all diagnostics (severity/code/message/span and
order), all tokens (kind/span and order), preorder tree rows (depth, kind, name,
value, span, child count), and `Format` output. The preorder traversal follows
syntax children, not alias expansion.

The 21 bounded sources comprise the 14 `EqualityCases`, DAG depth 8, custom-tag
keys, a self-cycle, supplementary-Unicode duplicate keys, spacing normalization,
undefined nested alias, and malformed flow syntax. Baseline and first candidate
`EB35EFDEE22E49B5AE2C365EEFC85C3208BC45D600B167E7F7D835F83FDC15E5`
matched **21/21 exact serialized rows**. After intervening commits rebuilt the
assembly, final test-runtime DLL
`339F307E3309FCDC7CCBD1E51FB4A0216E10263B53FDB818BE2E105504C820C7`
was independently checked against the same frozen baseline: **21/21 exact**.
Raw files: `baseline.json`, `candidate.json`, `committed-candidate.json` under
`.cache/validation/yaml-key-identity/`. Hash changes are not pooled or hidden.

## Tests, failure retained, and supported verdict

Environment: Windows x64 10.0.26200, .NET SDK 10.0.400 / MSBuild 18.9.6,
portable net10.0 Release. No GUI or platform state changes.

Initial dedicated run: **24/25 passed**; original supplementary-Unicode
full-collection-span assertion failed. Expected `[猫, 😀]` length 7, observed
length 6, excluding the closing `]`. Frozen baseline and candidate both have
this omission; therefore it is an **inherited flow-collection source-span defect**,
not introduced by graph interning. A separate BMP probe `[猫, 犬]` failed the
same full-span requirement, proving this is not solely surrogate handling.
Both failed TRX files remain in the artifact directory:
`yaml-key-graph.trx`, `yaml-key-graph-qualified.trx`.

The final test `Existing_flow_key_span_omits_closing_bracket` explicitly
characterizes both existing defects; it does not relabel incomplete coverage as
correct. Full source-span correctness remains unverified/contradicted for these
flow collections and requires a separate production correction. Other span
checks establish bounded offsets, not universal complete construct coverage.

Final affected regression command:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore -v q `
  --filter 'FullyQualifiedName~YamlKeyGraphIdentityTests|FullyQualifiedName~YamlStreamDifferentialTests|FullyQualifiedName~YamlResourceIsolationTests|FullyQualifiedName~FormatPolicyTests|FullyQualifiedName~IncrementalPolicyTests' `
  --logger 'trx;LogFileName=yaml-affected-qualified.trx' `
  --results-directory .cache/validation/yaml-key-identity
```

**153/153 passed, 0 skipped** (single combined run, not summed repeated runs):

| Class | Cases |
| --- | ---: |
| YamlKeyGraphIdentityTests | 26 |
| YamlStreamDifferentialTests | 14 |
| YamlResourceIsolationTests | 1 |
| FormatPolicyTests | 64 |
| IncrementalPolicyTests | 48 |

The new cases include different anchor-sharing topologies with equal expansion,
ordered-sequence mismatch, mapping permutations and changed pair values,
scalar/collection tags, duplicate malformed inner entries, document boundary,
anchor redefinition, cyclic/custom unsupported keys with Provisional sessions,
DAG depth 35 Complete session, conservative/idempotent Format, pre-cancellation
and next-call recovery, and a shallow-cached identity later reached through a
path exceeding the inherited comparison limit. Cancellation during interning
itself is not claimed from a pre-cancelled token test.

**Verdict:** scoped acyclic identity/equality and public-output compatibility
are supported. Existing flow-key full-span coverage is defective. This is not
complete YAML certification or a claim of end-user latency.

## Small allocation witness (not a latency SLA)

A separate Release harness ran one warmup and five retained measurements per
fixture per implementation, using `GC.GetAllocatedBytesForCurrentThread` and
Stopwatch around public Analyze; every row is retained. Each process records
actual DLL identity. The baseline DLL is the frozen hash above; candidate is
`EB35EFDEE22E49B5AE2C365EEFC85C3208BC45D600B167E7F7D835F83FDC15E5`,
the initial same-source candidate, not the subsequently rebuilt final DLL.
No cold-start inference, statistical confidence, p95, native paint/input latency,
or machine-independent timing promise is made.

| Fixture | UTF-16 chars | Baseline allocation median | Candidate allocation median | Baseline / candidate time medians |
| --- | ---: | ---: | ---: | ---: |
| 600 flat scalar entries | 8,290 | 2,542,800 B | 2,368,344 B | 14.9036 / 14.0921 ms |
| Two equal binary alias DAGs, depth 12 | 618 | 33,021,848 B | 252,840 B | 16.3023 / 0.8279 ms |
| Same construction, depth 35 | 1,837 | **not executed** | 626,232 B | not executed / 2.1623 ms |

All DAG rows report one duplicate and zero unsupported-key warnings. No baseline
DAG above depth 12 was executed: the expanded representation grows exponentially
and intentionally risking OOM is not useful validation. Candidate depth 35 has
an enormous expanded tree but only a tiny acyclic source graph. The evidence is
that a concrete tiny-file alias-sharing pathology no longer requires materialized
expansion, not that giant-file UX has been validated. The flat fixture shows no
allocation regression in this sample; it does not rule out table overhead in
other inputs. Allocations still include syntax tokenization, parsing, and semantic
tree production. Raw five-row groups: `baseline-allocation.json` and
`candidate-allocation.json`; harness source and frozen inputs remain within the
same repository-local artifact directory.
