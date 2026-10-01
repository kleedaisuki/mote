# Engine copy-free range-chunk review

## Scope and result

Reviewed commit `2b17b9c595888e44c112fd15a7aa1bb6b4a402aa` on 2026-10-01: the additive
`TextSnapshot.GetChunks(int start, int length)` API, its `RopeNode.Chunks` overload,
surrounding immutable-node construction/edit paths, document disposal, API documentation,
and `EngineRangeChunksTests`. No substantive correctness or compatibility defect was found.
No production files were changed. This is a source-level review, not a new runtime or
Native AOT performance acceptance result; the already completed 44/44 validation was
not rerun.

## Correctness argument

- The public overload is not an iterator method. `ValidateRange` executes at the call
  site before returning the deferred internal sequence. Negative arguments are rejected;
  `start > Length || length > Length - start` short-circuits before an invalid subtraction
  and never computes `start + length`. Positive overflow-shaped inputs cannot bypass it.
- For nonempty valid ranges, descent maintains `0 <= start < current.Length`.
  Taking the right branch subtracts exactly the skipped left length. Taking the left
  branch pushes the next, unvisited right subtree. At a leaf, the emitted length is
  `min(remaining, leaf.Length - start)`, necessarily positive and within that string.
- After an emission, either the requested range is exhausted or the stack contains a
  successor subtree. If it did not, the current leaf would be the final leaf of the
  root, contradicting validity of the original range. The successor starts at zero;
  left descent finds its first leaf. Consequently no preceding leaves are scanned,
  no empty slices are emitted, and slices cover precisely the requested range in order.
- Null roots and zero-length ranges return no slices, including an empty range at the
  document end. CRLF and surrogate pairs may straddle slices or requested boundaries;
  this is explicitly a UTF-16 code-unit read API, consistent with existing range reads.

## Lifetime and compatibility

The deferred iterator captures the immutable rope root, not the mutable document.
Rope nodes have get-only references/metadata and string leaves; Replace constructs
new paths rather than changing an old root. Dispose clears document-owned state but
cannot invalidate captured roots or strings. A yielded `ReadOnlyMemory<char>` keeps
its own backing string alive independently of the iterator. Re-enumeration creates
independent traversal state and retains the same snapshot contents.

The parameterless public and internal overloads are unchanged, as are existing range
validation semantics. Adding the two-argument overload does not change the existing
parameterless method's signature or binary binding. The XML and engine README document
both eager validation and retained snapshot behavior. Consumer code must not mistake
read-only memory for permission to mutate backing strings through unsafe mechanisms.

## Cost and consumer implications

For AVL height h, the initial seek touches at most h nodes. Subsequent traversal walks
the contiguous requested leaf interval: complete internal subtrees contribute O(k)
nodes for k emitted leaves, with at most O(h) partially visited boundary paths. Thus
time is O(h + k), or O(log n + k) under the existing balanced-rope invariant, and the
pending-subtree stack is O(h). `AsMemory(start, count)` slices the leaf string without
copying text. Iterator and stack allocations still exist: copy-free does not mean
allocation-free. This proof does not establish measured latency or a performance win
for any particular consumer.

A retained *sequence* roots the original rope, including text outside a small range;
a materialized slice alone roots its backing leaf. This is consistent with snapshot
retention, not a newly introduced disposal bug. Long-lived range consumers should
account for retained snapshot memory and avoid creating one enumeration per code unit.

## Test assessment and limits

The added tests check exact range contents on a nested edited rope, 150 deterministic
random ranges, backing-string identity (not merely equal text), cross-leaf CRLF and
surrogate code units, eager malformed-range exceptions, empty ranges, and deferred
re-enumeration/backing memories after edits, undo/redo, and disposal. Those assertions
are derived from source-string and lifetime contracts rather than iterator internals.

The lifetime test edits between complete enumerations rather than pausing an active
iterator mid-stream. Source inspection establishes the same immutable-root guarantee
for a paused iterator; this gap is not a demonstrated defect. Tests do not instrument
node visits, and no new allocation, concurrent-enumerator, or Native AOT benchmark was
run as part of this review. No unrelated preexisting rope implementation issue is
claimed to be fixed or comprehensively verified here.
