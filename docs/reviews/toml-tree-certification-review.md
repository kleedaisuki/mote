# TOML clean-tree certification: independent integration review

Date: 2026-10-01. Production reviewed at
`a8138522f94c3d75b1dcd28f8c8dfa790f841578`; independent retained tests/documentation
at `9028e3e`. Scope: `TomlTreeCertification` and its `TomlPolicy` Analyze/Format
integration, not earlier cache, native-input, Windows focus or CI changes.

## Verdict

**No substantive defect found in the examined clean-tree certification slice.**
This is a source-level assessment corroborated by retained managed compatibility
evidence, not a proof for arbitrary TOML programs or an executed Native AOT,
macOS, GUI, or performance certificate. No production/test changes, test reruns,
native experiments or pushes were performed for this review.

## Concrete invariants checked

| Obligation | Source-level finding |
| --- | --- |
| Grammar confidence | `TryCertify` checks cancellation first, then requires **zero** whole-parser diagnostics, including warnings. Recovered or otherwise uncertain grammar cannot acquire a fast certificate. |
| Required structure | Assignment key/value/equal token, dotted components, table name/brackets/newline before items, array/inline delimiters and nonfinal commas, scalar tokens and supported date kinds are explicitly checked. Decoded quoted key names are required; empty quoted keys remain legal. |
| Source-order ownership | Root assignments, table/AoT headers and their grouped assignments are checked for monotonic source offsets and replayed through the existing normative index with `int.MaxValue` binding budget. No new public statement/length/depth cap is added. |
| Local namespaces | Each inline table creates a fresh index, using the same dotted-parent and sealed-value semantics. Arrays and separately nested values never share an inline namespace. An explicit stack certifies every nested value without CLR recursion in this helper. |
| Publication and lifetime | The helper returns only a Boolean, retains no state between calls, emits no partial diagnostics and does not set/reparent syntax nodes or tokens. An outer global binding is admitted only after its complete value traversal succeeds. Intermediate local-index mutations on failure are discarded with that call, never reused by fallback. |
| Exact fallback | Any false result invokes the **unchanged** `TomlDocumentValidation` path. Existing grammar witnesses, exact-record deduplication and source sorting remain authoritative; no alternative diagnostic messages or key-only inline spans are published by certification. |
| Cancellation | Cancellation is checked at entry, table/pair/key traversal, container loops and each popped value, and propagates as an exception. It is not converted into false, success or a recovery result. Existing parser-call cancellation limitations are not redefined. |
| Formatting | Original and candidate each use the same certificate-or-existing-validation decision. False certification does not reject a previously valid input merely because the fast path is uncertain. Existing horizontal-gap edits, lossless projection equivalence, invalid-source preservation and idempotence contract remain intact. |

Inline assignments are staged before the separately stacked nested values are
visited, but this cannot publish a partial certificate: any nested refusal returns
false for the entire source and discards all temporary indices. LIFO order across
independent nested values therefore does not change observable diagnostic order;
only the established fallback produces diagnostics. Array homogeneity and an
invented Int64 scalar bound are deliberately not introduced.

## Matching dependency evidence, not cached HEAD

Reused [the single-grammar investigation](../architecture/toml-single-grammar-validation.md)
and read `.temp/toml-single-grammar/SyntaxValidator.cs` downloaded from the NuGet
2.10.1 repository revision `6cc82dd23a2c0aefb858f92a51c1b184e2812103`, rather than
the different cached upstream checkout. Its local SHA-256 independently matches
`CD37339A75DCF5B6531530DB53BB773570783F4D3F18B7FE53DCBF8F716249C5`.
The matching [upstream validator](https://github.com/xoofx/Tomlyn/blob/6cc82dd23a2c0aefb858f92a51c1b184e2812103/src/Tomlyn/Syntax/SyntaxValidator.cs)
checks structural/token presence, supported values and namespaces; primitive
spelling/decoding belongs to the grammar lexer/parser. The new helper replaces
ownership with mote's already reviewed normative model, not a copy of the old
global array-index algorithm. There is no private reflection, dynamic code or AST
adoption. These are Native AOT-compatible source mechanisms, not an AOT execution
result.

## Independently inspected retained results

Parsed the actual TRX files in `.cache/toml-tree-validation/`, without executing
the suites again:

| Artifact | Executed / passed / failed / unexecuted | Interpretation |
| --- | ---: | --- |
| `tree-directed.trx` | 73 / 72 / 1 / 0 | Retained test fingerprint serialization failure; not hidden |
| `tree-integration.trx` | 915 / 915 / 0 / 0 | Includes the same 73 directed tests, now all passing |
| `tree-incremental.trx` | 48 / 48 / 0 / 0 | Existing incremental workflow compatibility |

Independently read both frozen comparison JSONLs, checked their hashes and
compared all decoded observation rows: **703/703 exactly equal**, excluding their
different identity headers. This comparison covers full diagnostics and ordering,
tokens, source, recursive projected values and exact Format output, not merely
validity. Nine invalid UTF-8 entries remain separate decoder evidence.

* Loaded baseline DLL: `C8F3A5FFA6A0BC42B58F2A5D91EBC3AF35074B1F16191A9FB16A0C6125F296E2`.
* Loaded candidate DLL: `F97E4CCEFECFD320B3A5C173EC90A1C0C50669CCE237DA88D274CD2EDC11C2EF`.
* Baseline JSONL: `666599abc4391f84d91a5ec543db44f8b94d0f03c0f700476ef029f185afbc53`.
* Candidate JSONL: `1a8a0107e49eb51368ffed80c592a9870792c36c74e4e8429709e61bbc3d4507`.

Read-only fixtures preserve node/token identity, parents, spans and serialization
on both outcomes; directed structural refusals do not claim arbitrary fabricated
or concurrently mutated trees are supported. The initial recursive-escaped JSON
test fingerprint and miscopied candidate DLL are documented validation setup
failures; neither is credited as production correctness evidence. Inspected
`cancellation.jsonl`: five asynchronous cancellations threw and preserved source,
followed by successful independent certification of the same tree. This is not a
cancellation-latency bound or an instruction-level scheduling witness. Full
methodology remains in [the validator artifact](../validation/toml-tree-certification.md).

## Remaining costs and limits

Successful certification eliminates the second public grammar construction;
semantic-invalid sources can incur a failed traversal **plus** the original
full recovery work. No timing, allocation improvement or nonregression conclusion
is made here before the independently owned frozen paired measurements. Parser
depth limits and unchanged recursive projection/format equivalence are outside
this helper's explicit-stack claim. Arbitrary malformed recovery completeness,
large-session census, decoded scalar representation and native product behavior
remain governed by their separate contracts and evidence.
