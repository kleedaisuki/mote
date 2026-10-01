# YAML explicit flow-collection source spans

## Scope and mechanism

This correction follows the retained BMP and supplementary-text failures in
[the key-graph identity validation](yaml-key-graph-identity.md). It repairs actual
source coverage; it does not change representation-graph equality, alias binding,
canonical keys, recovery policy, resource budgets, or diagnostic ordering.

The pinned SharpYaml 3.13.1
[scanner](https://github.com/xoofx/SharpYaml/blob/3.13.1/src/SharpYaml/Scanner.cs)
`FetchFlowCollectionEnd` consumes the closing character but constructs both end
tokens with `(start, start)`. The
[parser](https://github.com/xoofx/SharpYaml/blob/3.13.1/src/SharpYaml/Parser.cs)
passes these zero-width marks to collection-end events. Consequently mote's
half-open event ranges excluded `]` and `}`. This occurs with BMP text too;
adding surrogate arithmetic is not the repair.

The ordinary projector retains the existing source-string reference and extends
a collection range by exactly one UTF-16 unit only when its style is Flow, the
end event is zero-width, and the actual end code unit is its matching delimiter.
The streamed projector uses the same predicate, reading only
`snapshot.GetChunks(end.Index, 1)` and adjusting its last-node-end mark. It never
materializes a source copy. These reads are bounded per collection, but have
rope traversal/enumerator cost; no large-file latency or allocation claim is made.

Source verification matters: SharpYaml represents an implicit mapping in
`[a: b]` with Flow style but no `}` of its own. Its existing start is zero and
end is five, so that mapping's range remains `[0, 5)` while the explicit parent
sequence becomes `[0, 6)`. No attempt is made here to repair the implicit
mapping's inherited start mark. Block collections are untouched. If a future
dependency already emits a nonzero half-open end, it is not extended again.

Semantic collection spans, their key tokens and diagnostics, and enclosing
entries whose value ends at that collection now include the closer, but not
subsequent spaces/comments. Existing punctuation-token production is unchanged.
This is an intentional observable span correction, not byte-identical output.

## Reproducible evidence

Environment: Windows x64, .NET SDK 10.0.400, net10.0 Release; no GUI, native
execution, CI replay, platform changes, commit or push. Repository baseline:
`065585ca31db41ad0a0236cefc6a3ced33a58f49`.
Artifacts and harnesses are retained under `.cache/validation/yaml-flow-span/`.

1. Before the production edit, the new focused run reported **0/17**. Sixteen
   failures were actual omitted-delimiter regressions. The seventeenth was an
   incorrect test expectation for the implicit mapping's start; the pinned parser
   constructs its start with default marks. That assertion was corrected to
   preserve the inherited start, and the baseline control passed **1/1** separately.
   Both original failure and corrected control remain as `baseline.trx` and
   `baseline-implicit-control.trx`; this is not claimed as 17 qualified failures.
2. The small-projector-only candidate passed **73/75** affected cases. Two original
   streaming differential assertions correctly failed because that path still
   omitted the closer (`candidate.trx`). They were not weakened. Root approved
   extending file ownership to the matching streamed correction.
3. Aligned production passed **75/75**, zero skips, in
   `candidate-aligned.trx`: 17 focused source-span cases, 26 graph-identity cases,
   18 small-recovery certificate cases, and 14 original stream differential cases.
   The test command was:

   ```powershell
   dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore -v q `
     --filter 'FullyQualifiedName~YamlFlowCollectionSpanTests|FullyQualifiedName~YamlKeyGraphIdentityTests|FullyQualifiedName~YamlSmallRecoveryCertificateTests|FullyQualifiedName~YamlStreamDifferentialTests' `
     --logger 'trx;LogFileName=candidate-aligned.trx' `
     --results-directory .cache/validation/yaml-flow-span
   ```

4. Subsequently added streamed fixtures passed **12/12**, zero skips, separately
   (`streamed-new.trx`), not an invented single 87-case run. They use a 300 KiB
   comment prefix to select streaming, and independently require exact full spans
   and Complete/one diagnostic. The shared key sources include BMP/astral,
   empty sequence/map, nested mixed collections, quoted closers/apostrophes,
   multiline comments containing closers, explicit tags and implicit mapping.
   The existing two-case bug characterization now asserts complete coverage.

The last test build also observed unrelated CA1416 warnings in a concurrent
Windows native foreground test file; no YAML compile error or warning occurred.

## Frozen public-output comparison

The previous bounded key-graph serializer was reused against DLLs, not against
new identity integers or test assertions. Current baseline formats DLL SHA-256:
`088B8189B52E484E7811DC9D40D3E67052CA805141694D280E3C400084390A8A`.
Final loaded candidate formats DLL SHA-256:
`CDBBE2C2F6F1F7B9B3EF3E98741BB7045490973486861887A1C4668BDBDD65F3`.
The serializer records the actual loaded assembly hash in each output.

**21/21 rows** match except for **92 explicitly verified closing-delimiter span
corrections**. Every differing span has identical start, length/end exactly +1,
and the old end points at `]` or `}` in the original UTF-16 source. All other
serialized fields are exact: source, diagnostics' IDs/messages/severity/order,
token kind/order, tree shape/names/values/alias binding offsets, and Format output.
Raw data: `baseline-output.json`, `final-output.json`, `compatibility.json`;
reproducer: `harness/` and `compare.py`.

The first Python comparison accidentally indexed decoded code points as UTF-16
and rejected the astral witness. That harness failure was inspected and corrected
to index encoded UTF-16 units; it was not a production failure.

Final production source hashes:

| File | SHA-256 |
| --- | --- |
| `YamlPolicy.cs` | `2A1398DF9FB3174AB4FDE6AF1125A604F3BD6FDB322CDDA56DE49C3F23119C3C` |
| `YamlIncrementalSession.cs` | `4EE140AF568F6CBF759D409A4653CD4511A952B2E1385AD4DBB17F6ACAC0E8E1` |

## Supported conclusion and boundaries

Explicit flow collections now cover their actual closing delimiter consistently
in the ordinary and streamed duplicate-key paths, including supplementary text
and misleading punctuation in quotes/comments. The narrow frozen comparison
supports semantic/output compatibility apart from these intentional corrections.
It is not universal YAML span certification, cyclic-key support, proof of large
streaming performance, native rendering acceptance, or full YAML conformance.
