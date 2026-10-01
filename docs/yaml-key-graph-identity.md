# Ordinary YAML key graph identity

## Scope and contract

`YamlPolicy.Analyze` previously computed recursive length-prefixed canonical
strings for mapping keys. Length prefixes made equality exact, but an acyclic
alias DAG could expand the same canonical subtree repeatedly: a few KiB of
source with successively doubled anchored sequences could request exponentially
large strings. This affects ordinary configuration-sized inputs, not merely
large-file throughput. The streaming `YamlIncrementalSession` is unchanged.

[YAML 1.2.2 sections 3.2.1.3 and 3.2.2.2](https://yaml.org/spec/1.2.2/)
require tag-aware canonical scalar equality, ordered sequence equality,
unordered mapping equality, and alias binding to the most recent preceding
anchor. Cyclic equality is implementation-defined. Existing custom-scalar,
cycle, unbound-alias and comparison-depth warning messages remain unchanged.
The public syntax tokens, semantic tree, source spans and formatter contract
are not changed by this private equality representation.

## Data and lifetime

Each parser projector owns two dictionaries cleared at every document boundary:

* A reference-keyed successful-node memo stores `(identity ID, expanded height)`.
* An interning table stores immutable structural entries: node kind, normalized
  tag, canonical scalar value, and an array of child identity IDs.

Scalar entries retain their existing canonicalizer. Sequence child IDs retain
source order. Mapping entries sort complete `(key ID, value ID)` pairs, retaining
repeated pairs; numeric ID order is arbitrary but shared within that document,
so two equal pair multisets have the same sorted representation. Dictionary
hashes are only bucket selectors: equality compares every structural field and
every child ID. Hash collisions cannot make unequal keys equal. No process-wide
cache, alias-name identity, probabilistic fingerprint or expanded string exists.
Aliases resolve by their recorded original anchor offset, including reused names.

Each alias adds one to memoized expanded-path height without changing node
identity. A cached success is reused only if the current visiting depth plus
height fits the existing maximum of 257 visited nodes. This preserves the old
`visiting.Count > 256` comparison limit instead of accidentally letting memoized
subtrees evade it. Cyclic and unsupported results are not cached: their first
failure and depth context still determine the existing warning reason.

Cancellation is checked during graph traversal, pair sorting, pair flattening,
identity hashing and equality. `Array.Sort` wraps comparer exceptions; the
implementation restores cancellation as `OperationCanceledException` rather
than exposing that wrapper to callers. Existing library syntax parsing and
scalar canonicalization are still not individually preemptible.

## Cost and external context

For supported acyclic keys, representation storage tracks explicit source-node
edges and canonical scalar lengths rather than the recursively expanded alias
tree. Sorting a mapping with `m` entries costs `O(m log m)`; dictionary hashing
and full collision equality still have their ordinary nonconstant costs.
Unsupported or cyclic failures are deliberately not memoized, so this is not a
promise of linear work for arbitrary malformed graphs or adversarial hashes.
Existing projection recursion, depth policy, syntax parsing, diagnostic scans,
HTML rendering and the large-file streaming policy remain separate concerns.

Production YAML libraries recognize alias amplification as a security concern:
[SnakeYAML 2.5 LoaderOptions](https://javadoc.io/static/org.yaml/snakeyaml/2.5/org/yaml/snakeyaml/LoaderOptions.html)
provides a collection-alias limit. Mote instead avoids expanded representations
for this bounded ordinary-file equality operation; this does not certify all
YAML operations as safe or remove other resource safeguards. The general
memoization/interning connection is discussed in Braibant, Jourdan and Monniaux,
[Implementing and reasoning about hash-consed data structures in Coq](https://doi.org/10.1007/s10817-014-9306-0),
Journal of Automated Reasoning 53(3), 271–304 (2014). Its proof and performance
results do not transfer directly to this C# editor implementation.

Recent work by Zhu et al., [Efficient Symbolic Computation via Hash Consing](https://arxiv.org/abs/2509.20534v2)
(2025 preprint), uses a global weak-reference table in JuliaSymbolics. It is a
relevant example of avoiding expression swell, not evidence of mote performance
or a reason to introduce a global cache. Mote's document-local strong entries
have a simpler ownership boundary and are discarded with the projector.

## Separate correctness boundaries

The independent candidate checks exposed an inherited flow-collection span
coverage gap: the diagnostic/key span for `[猫, 😀]` omits the closing bracket.
A BMP-only `[猫, 犬]` control also reproduces the omission, so this is **not**
evidence of broken supplementary-character offset accounting. Baseline and
candidate both exhibit it; graph equality does not correct source-span
projection. The failed requirement checks and subsequent characterization are
recorded in the [independent validation](validation/yaml-key-graph-identity.md).

The identity-only checkpoint retained the existing silent skip after an error
contained in a key. A subsequent [ordinary recovery certificate correction](architecture/yaml-small-recovery-certificate.md)
keeps the skip but makes its unavailable equality proof explicit through the
existing unsupported-key warning. The small session therefore no longer claims
Complete for that path. This does not claim that every invalid YAML input
receives exhaustive diagnostics or broaden parser syntax recovery.

## Verification checkpoint

The production implementation built successfully with 0 warnings and 0 errors:

```powershell
dotnet build src/Mote.Formats/Mote.Formats.csproj -c Release `
  --artifacts-path .cache/yaml-key-graph-worker/build --nologo
```

Independent behavior, differential and allocation validation are owned by the
parent's separate validation task. This build alone does not prove semantic
compatibility, native GUI latency, cancellation timing, or release readiness.
