# YAML flow collection span correction review

## Assessment

No substantive defect found in the scoped source review. The change corrects an
observable half-open source-range error rather than preserving the old omitted
delimiter. It is appropriate to accept the intended +1 span changes. This is not
full YAML conformance, native rendering, or large-file performance acceptance.

Review baseline: `065585ca31db41ad0a0236cefc6a3ced33a58f49`, with the working-tree
YAML changes. Independently read the production diff, changed graph-identity
assertions, new flow-span tests, the validation ledger and retained artifacts.
No production or test changes, test replay, GUI execution, CI dispatch, commit,
or push were performed by this review.

Reviewed source SHA-256:

| Source | SHA-256 |
| --- | --- |
| `src/Mote.Formats/YamlPolicy.cs` | `2A1398DF9FB3174AB4FDE6AF1125A604F3BD6FDB322CDDA56DE49C3F23119C3C` |
| `src/Mote.Formats/YamlIncrementalSession.cs` | `4EE140AF568F6CBF759D409A4653CD4511A952B2E1385AD4DBB17F6ACAC0E8E1` |

## Independent mechanism analysis

- The retained primary source for [SharpYaml 3.13.1 Scanner](https://github.com/xoofx/SharpYaml/blob/3.13.1/src/SharpYaml/Scanner.cs)
  shows `FetchFlowCollectionEnd` taking the current mark, consuming the closer,
  then constructing `FlowSequenceEnd(start, start)` or
  `FlowMappingEnd(start, start)`. The retained [Parser](https://github.com/xoofx/SharpYaml/blob/3.13.1/src/SharpYaml/Parser.cs)
  forwards these token marks to explicit collection-end events. Therefore the
  old event end is the delimiter's position, not the position after it.
- Ordinary `Projector.CollectionRange` requires Flow style, a zero-width event,
  and the matching actual source character before extending the bounded span by
  one UTF-16 code unit. `]` and `}` are single-unit ASCII, so supplementary text
  before the collection does not require scalar counting or surrogate adjustment.
  Closing-delimiter location comes from the parser, not a scan through quoted
  strings or comments. Nested values naturally propagate their corrected end to
  enclosing entries and item wrappers; anchors and key identity do not use a
  changed starting offset.
- The implicit mapping in `[a: b]` is created with Flow style and default start
  marks, but its end is the sequence-end token. The `}` source check does not
  accept that `]`. The inherited implicit start stays unchanged. Block styles
  bypass correction. An already nonzero end event also bypasses correction, so
  the helper does not double-extend a future half-open dependency event.
- The streamed projector now holds the same immutable snapshot that its parser
  reads. It bounds the raw mark before `GetChunks(mark.Index, 1)`, whose documented
  contract yields nonempty slices over the requested UTF-16 range. Consequently
  `chunk.Span[0]` is safe, including at rope leaf boundaries. Snapshot lifetime
  and later document mutation cannot change this source witness. No source copy
  is introduced. Enumeration is disposed by `foreach`, including early return.
- Streamed `CollectionEnd` updates `_lastNodeEnd` even when no canonical shape
  is retained. A containing mapping thus uses the corrected collection-key end
  for its duplicate diagnostic; retained shapes still depend on tags and decoded
  content, not ranges. The synthetic mark increments column by one without
  changing line, appropriate for an ASCII closer rather than a newline.
- Existing node-level cancellation checks, depth checks, exception handling,
  canonical key budgets and diagnostic caps remain in place. The added source
  witness is one bounded rope-range lookup per flow end, not an unbounded scan or
  retention of source text. It does add traversal/enumerator work; there is no
  measured latency claim. Cancellation is not checked inside this single-unit
  lookup, but existing recursion and final publication checks still apply.

## Evidence inspected, not re-executed

Read actual retained TRX counters: baseline 0/17, separately corrected implicit
start control 1/1, ordinary-only candidate 73/75, aligned candidate 75/75, and
subsequent streamed fixtures 12/12. The first baseline includes one invalid test
expectation, so it is not 17 demonstrated production failures. The two candidate
failures correctly exposed the initially uncorrected streaming path. The final
75 and 12 runs are distinct, not a claimed single 87-case run.

Read the frozen comparison script and `compatibility.json`. The script recursively
compares all row fields, allows only equal-start spans with length/end exactly +1,
and checks the old end's actual UTF-16 source character is `]` or `}`. The retained
report records 21 rows and 92 allowed changes, with no other field changes. This
supports the scoped compatibility claim; it is not a universal proof for every
YAML input. The script's corrected UTF-16 indexing is essential for the astral
fixtures and matches the project's source-offset contract.

## Limits and remaining boundaries

Tests cover explicit empty/nested/tagged collections, BMP and astral text,
misleading quoted/comment punctuation, enclosing entry ends, and the ordinary
versus streamed duplicate diagnostic. No new isolated fake-parser test exercises
an already half-open dependency event, but the nonzero-width predicate is direct
and was source-reviewed. This correction does not make streamed visible nodes
full collection trees: existing `AddVisible` start-event projections are unchanged.
It does not repair implicit mapping start marks, alter punctuation-token
production, make unsupported key equality complete, or certify cancellation
latency under large workloads. These are boundaries, not regressions introduced
by this patch.

Validation details and reproduction paths: [YAML flow collection spans](../validation/yaml-flow-collection-spans.md).
