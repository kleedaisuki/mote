# Ordinary YAML recovery completeness certificate

## Observed failure

The small-document session delegates to `YamlPolicy.Analyze`. Its completeness
certificate rejects `yaml.key-equality-unsupported`, `yaml.depth-limit` and
`yaml.syntax`, but does not reject all semantic errors: a successfully scanned
invalid value does not necessarily prevent mapping-key uniqueness checks.
Previously, `CheckMappingKeys` silently skipped a key whose source span contained
an existing error. Consequently a missing alias used directly or inside a
sequence key could produce `Complete`, despite no comparison of that key.

Before-edit reproduction against the isolated graph-identity production build:

| Input | Certificate | Existing diagnostics |
| --- | --- | --- |
| Two direct `*missing` keys | Complete, count 2 | Two undefined-alias errors |
| Two `[*missing]` keys | Complete, count 2 | Two undefined-alias errors |
| `!!int nope` key | Complete, count 1 | Invalid-tagged-scalar error |
| Ordinary `*missing` value | Complete, count 1 | Undefined-alias error |
| Ordinary `!!int nope` value | Complete, count 1 | Invalid-tagged-scalar error |
| Anchored `[*missing]` value, then alias used as key | Provisional, unknown count | Undefined-alias error and existing unbound-alias equality warning |

Reproduction artifacts are under `.temp/yaml-small-recovery-worker` and
`.cache/yaml-small-recovery-worker-repro-before.log`. The later alias key's
source span [27,7] does not contain the original missing-child error [15,8];
the existing identity traversal resolves the original anchor and detects the
unbound child. No new special case is needed for that path.

## Minimal policy change

Retain the existing source-span containment test, skip and no-guessed-duplicate
behavior. Before continuing past an error-containing key, append the existing
warning code `yaml.key-equality-unsupported` at that key's existing semantic
span with the fixed message:

> Cannot verify this mapping key's uniqueness: key contains a semantic error.

Original errors retain their IDs, messages, spans and relative order. Existing
valid identities, cycle/custom-tag warnings, alias resolution, syntax recovery,
tokens, semantic tree and formatter behavior are unchanged. The ordinary session
already converts this warning into `Provisional` with unknown diagnostic count;
no session code or public contract shape changes are necessary.

This is deliberately not a blanket 'any error means provisional' rule. An
undefined alias or invalid scalar used only as an ordinary value leaves unrelated
key comparisons available. `Complete` describes completed analysis coverage,
not error-free source. A key whose identity was skipped must instead make that
coverage uncertainty visible.

[YAML 1.2.2 sections 3.2.1.3 and 3.3](https://yaml.org/spec/1.2.2/)
require key uniqueness and describe incomplete representation when identities
cannot be resolved. Mote's `Complete`/`Provisional` editor certificates are its
own API terminology, not literal YAML specification states.

## Bounds and verification

This change preserves the current containment semantics, including existing
flow-collection source spans. It does not repair parser spans, broaden syntax
recovery, change the large-file streaming policy, or claim that every unsupported
YAML construct now has complete recovery. Independent tests and before/after
compatibility checks are recorded by the separate validator in
`docs/yaml-alias-key-recovery.md`.

### Implementation verification

The isolated Release build passed with 0 warnings and 0 errors:

```powershell
dotnet build src/Mote.Formats/Mote.Formats.csproj -c Release `
  --artifacts-path .cache/yaml-small-recovery-worker/build --nologo
```

The resulting Formats DLL SHA-256 is
`D2AE8D8C84E226375632D71540A54B280C8DEAFC8EF0980D16F214672871B6C4`.
The same six repro cases after the change show direct/nested missing-alias keys
and invalid-tagged scalar keys becoming `Provisional` with unknown counts and
the new warning. Ordinary invalid values remain `Complete` with their original
one error. The anchored later-key case retains its original two diagnostics and
`Provisional` certificate. Actual output is retained in
`.cache/yaml-small-recovery-worker-repro-after.log`; these checks are not a
substitute for the independent validator's regression and differential tests.
