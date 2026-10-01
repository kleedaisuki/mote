# Ordinary YAML key graph identity review

## Scope and conclusion

Reviewed production commit `cd6b011942acf56eaeb4b8c3e6c767ca7f26341d`
against baseline `f1b929843563254ff15fb6586041d0ea2a069038`.
The production change is limited to `src/Mote.Formats/YamlPolicy.cs`; its
architecture note is `docs/yaml-key-graph-identity.md`. The reviewed source file
SHA-256 is `C472344C11088604300FC5FD77DBD56D4197F3725527167502C7DC27D4B847BB`.

**No substantive defect found in this scoped source review.** The replacement
removes expanded canonical strings for supported acyclic key graphs while
preserving exact equality, document-local ownership and the existing contextual
comparison-depth policy. This is not approval of all YAML resource behavior or
a release-completion claim.

## Evidence and reasoning

* `Projector.ParseDocuments` clears anchor bindings, scalar identities, node
  memo and intern table before each document. Key checks finish before the next
  clear. IDs never escape into the returned semantic nodes or diagnostics.
* `IdentityComparer` compares kind, normalized tag, canonical scalar string,
  child count and every child ID. Hashes select dictionary buckets only; equal
  hash values cannot establish equality. Private child arrays are filled before
  interning and are not subsequently mutated.
* `IdentifySequence` retains child order. `IdentifyMapping` sorts complete
  `(key ID, value ID)` pairs and retains multiplicity. By induction on acyclic
  graph height, equal children receive the same IDs in the shared document pool;
  therefore arbitrary numeric ID ordering produces equal representations for
  equal mapping pair multisets. Repeated malformed entries are not collapsed.
* Scalar canonicalization and collection-tag normalization are unchanged.
  Aliases still resolve the original recorded source offset, not their current
  anchor name. Reusing an anchor name cannot redirect an earlier alias.
* The former guard tests `visiting.Count > 256` before adding a node, permitting
  at most 257 canonical nodes on a successful path. Cached height counts the
  current node, each alias edge and collection descent, but not projection-only
  `item`/`entry` wrappers. The new `visiting.Count + cached.Height > 257` guard
  reproduces that boundary instead of accepting deep cached subgraphs.
* Only successful identities are memoized. Their reachable graph is acyclic and
  fully supported; a successful cached subgraph cannot contain an active
  ancestor, since that would imply a previously undetected cycle. Unsupported,
  cyclic and depth-context failures are recomputed in original child traversal
  order, retaining the existing first failure reason. `finally` removes the
  current node from the visiting set on both success and failure.
* Cancellation checks cover traversal, mapping pair comparison/flattening,
  hashing and structural equality. Sorting explicitly unwraps cancellation via
  the original token, retaining `OperationCanceledException` rather than
  leaking the sort wrapper. Parser/scalar conversion preemption is unchanged.
* The duplicate-key loop still skips the same keys with existing errors and
  emits the same codes, messages, severity and key spans. Parsing, token
  production, tree construction and formatter equivalence logic are outside
  the changed representation. The streaming session is untouched.

These choices match [YAML 1.2.2 sections 3.2.1.3, 3.2.2.1, 3.2.2.2 and
7.1](https://yaml.org/spec/1.2.2/): tag-aware scalar equality, ordered sequences,
unordered mappings, most-recent preceding anchor binding, and
implementation-defined equality for cyclic nodes. Keeping repeated pairs is
an existing malformed-input recovery convention, not a new normative YAML rule.

## Limits and validation separation

This review inspected the stable diff and its surrounding parser, anchor
registration, diagnostic traversal and formatter. It did not rerun the corpus,
build or focused tests already owned by the independent validator/worker. No
demonstrated defect justified a separate reproduction. Behavioral compatibility,
allocation measurements and cancellation experiments must be credited to their
actual validation artifacts, not to this source review.

Failed identities are intentionally not memoized, so repeated unsupported or
cyclic graphs may still require repeated work. Existing diagnostic scans,
recursive projection, syntax-library parsing, scalar conversion and streaming
canonical strings are separate limits. The implementation note explicitly
states these boundaries; this review does not certify arbitrary malicious YAML
as bounded-time or certify native typing, rendering or startup latency.
