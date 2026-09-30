# Markdown flat cold-allocation review

## Scope and verdict

Independent focused review of the working change in
`src/Mote.Formats/MarkdownIncrementalSession.cs`, performed 2026-10-01.
Reviewed source SHA-256: `AF377F39007ED90788CCDEA9892D4288D1A7A29DF99E0866E42CB2380151EA73`.
The change is limited to the cold flat-certification reader and a private two-entry
exact-source parser certificate cache. **No substantive correctness defect found
in this scope.** This is not approval of unrelated concurrent changes or a claim
of whole-product validation.

## Evidence and invariants

- `SnapshotTextReader.Read(Span<char>)` fills the requested span across immutable
  rope chunks until the declared range ends. The physical-line ranges computed
  from consecutive `GetLineStartOffset` values are contiguous from offset zero,
  so sequential reading preserves original absolute UTF-16 positions. Its chunk
  loop checks cancellation; the certifier also checks before each line and at
  completion. `using` disposes the enumerator on success, refusal and exceptions.
- Blank bodies are filtered before `FlatBlockVerifier.Verify`, so indexing
  `source[0]` has a nonempty-call invariant. Positive cached sources have already
  passed `IsFlatBlock` and production Markdig verification. An accepted heading
  necessarily starts with `#`; an accepted paragraph does not. Exact span/string
  `SequenceEqual` comparison is ordinal, not a hash-only or culture-sensitive
  equivalence. The saved heading level belongs to that exact heading string.
- Cache hits still pass the unchanged per-physical-owner adjacency gate. Repeated
  paragraphs without blank separation still refuse; a heading/paragraph seam
  remains admitted only under the preexisting relation. Fence opening, closing,
  whole-fence Markdig verification, count/length budgets and obstruction spans
  remain outside the cache and unchanged.
- Each verifier lives only inside one `TryCertifyFlat` call, with at most one
  accepted paragraph and one heading string, each at most 65,536 UTF-16 units.
  These strings are not stored in `FlatBlock`, `FlatRun` or session fields.
  Failed/canceled certification cannot publish this scratch or candidate index.
  Existing `ProjectFlat`/commit sequencing is unchanged.
- CR-only physical delimiters remain refused by this flat path, as in baseline;
  LF and CRLF remain supported. The separate reference-certified path's broader
  CR support does not change this flat path's admission contract.

The fixed production parser pipeline and dependency-free admitted grammar are
essential assumptions for duplicate-proof reuse. A future pipeline extension
with mutable callbacks or nonlocal state would require reevaluating that
assumption; no such extension is present in this change.

## Independent differential probe

A reviewer-owned isolated probe lives at
`.temp/MarkdownFlatColdReviewProbe/Program.cs`, built against the pinned Engine
binary without production project references or shared build outputs. It invokes
private `TryCertifyFlat` through reflection in separate baseline/fixed processes,
then records admission, obstruction span and all `FlatBlock` record fields
(start, length, kind, heading level). No production files were edited.

Pinned Formats binaries:

| Binary | SHA-256 |
| --- | --- |
| `.cache/markdown-flat-cold-allocation/baseline/Mote.Formats.dll` | `46AAE9419D5AC07E53668A17D4277B3AB41347048B44A455DE5577AD0B25E2E7` |
| `.cache/markdown-flat-cold-allocation/fixed/Mote.Formats.dll` | `BF2FD5C79BCCE3E0ED9E8745889A3A87060ECE463C8279357BD1625302A4D85F` |

Reproduction:

```powershell
dotnet build .temp/MarkdownFlatColdReviewProbe/MarkdownFlatColdReviewProbe.csproj -c Release --nologo
dotnet .temp/MarkdownFlatColdReviewProbe/bin/Release/net10.0/MarkdownFlatColdReviewProbe.dll .cache/markdown-flat-cold-allocation/baseline/Mote.Formats.dll
dotnet .temp/MarkdownFlatColdReviewProbe/bin/Release/net10.0/MarkdownFlatColdReviewProbe.dll .cache/markdown-flat-cold-allocation/fixed/Mote.Formats.dll
```

**2,741 cases matched exactly**, including empty/trailing lines, repeated cache-hit
paragraphs/headings, differing heading levels, adjacent paragraphs, LF/CRLF/CR,
Unicode letters, references, malformed headings/fences, embedded NUL,
4,095/4,096/4,097 and 65,535/65,536/65,537/65,538-unit line boundaries,
and 2,500 seeded mixed-line cases (seed 92146). Half the nontrivial documents
receive an insertion/removal pair before analysis to exercise fragmented rope
reads. Both processes produced the same summary SHA-256:
`A9C6DF7D08C5A9043CECF0C92F95CBAE338CFE8648BBE5E72CB541DBD3CFD6AA`.
The isolated probe build passed with zero warnings/errors.

This checks equivalence to the previous private admission implementation, not
independent full CommonMark correctness. It does not time native startup,
measure p95, establish steady-state allocation improvement, or directly test
mid-parse cancellation/transaction retry. Those require the owner's paired
measurements and separately owned validation; source tracing found no changed
publication path. No performance conclusion is drawn from unpaired runs.

## Documentation correction

The old helper summary said Markdig checks every certified block, which becomes
imprecise when identical sources reuse a proof. The owner updated it to describe
one candidate. Existing `docs/markdown-block-certification.md` should likewise
state that every owner is checked, with stage-local proof reuse only for exactly
identical independent source. This is a documentation clarification, not a
blocking implementation defect. Relevant existing contracts were read in
`docs/markdown-block-certification.md` and `docs/markdown-reference-production.md`.

## Supplement: persistent benchmark and results consistency

Reviewed `benchmarks/MarkdownFlatColdAllocation/{Program.cs,MarkdownFlatColdAllocation.csproj,Directory.Build.props}`
and `docs/markdown-flat-cold-allocation.md` after the focused source review.
**No blocking benchmark or report issue found.** No existing functional cases
were rerun for this supplement.

The standalone tool keeps corpus generation/opening, forced GC, input SHA-256
and JSON emission outside timed Analyze intervals, records fixture identity
only after measurement, uses the same midpoint projection and ten near-start
edit sequence as the temporary probe, and permits historical Formats references
without rewriting production files. Its build outputs are redirected to root
`.cache`, generated source files remain under root `.temp`, and it is not an
implicit solution/CI gate. An existing corpus is intentionally reused rather
than regenerated: future comparisons must check the emitted input hash/size,
not assume the filename alone proves the fixture shape.

Independently recomputed the documented corpus Full latency/allocation, peak
working-set and retained-delta table values from `paired-summary.json`; they
match the reported rounded values. The prediction is exactly
`26136 * 25248 = 659881728` bytes. Recomputed the disabled-tiering flat medians
from the six `notier-{baseline,fixed}-flat-*.jsonl` files: Full
708.6939 -> 535.1442 ms, edits 0.02755 -> 0.0398 ms. The investigation correctly
discloses rather than hides the ordinary pooled edit increase
0.0559 -> 0.1253 ms and the unique-corpus peak increase
263.898 -> 266.188 MiB while allocation drops 633.169 -> 228.230 MiB.

The document explicitly separates cumulative allocation from retained memory
and process high-water RSS, managed format timing from native GUI/AOT timing,
paired five-process measurements from exploratory no-cache timings, and
correlated edit samples from independent tails. Its warm-up explanation is
presented as a supported interpretation, not an established exclusive cause.
No p95, startup or universal RSS claim is made. The remaining edit-latency
increase is an explicitly justified sub-millisecond tradeoff in this scoped
managed workload, not a demonstrated user-facing responsiveness acceptance.

### Provenance qualification pending controlled rerun

The owner subsequently identified different Engine/probe binary hashes between
initial publish folders despite identical executable source, caused by changing
assembly/source metadata during concurrent repository work. Accordingly, the
numeric consistency check above applies **only to preliminary metadata-different
paired evidence**. It does not certify identical harness/Engine binaries or final
controlled performance ratios. The owner is rerunning with the entire baseline
folder preserved and only the reviewed Formats DLL overlaid for fixed. Final
controlled metrics require a fresh report consistency check. Focused source and
private-certifier functional differential conclusions remain unchanged.

### Final controlled rerun: qualification resolved

Re-reviewed the final investigation and controlled evidence. Independently
compared the actual file sets and bytes in `baseline/` and `controlled-fixed/`,
then recomputed every recorded SHA-256 in `controlled-binary-inventory.json`.
Only **`Mote.Formats.dll` and `Mote.Formats.pdb` differ**. The shared probe hash is
`7E317A89FDFE53A910273559894B74E7BEEC7C0BC85EA1846996CD34B2D7196B`;
the shared Engine hash is
`F13BFB8C602EE4A3D360643162828F542C5407A5069E0B1BEE4F25777139C759`.
This resolves the previous harness/Engine metadata qualification. The earlier
supplement's figures remain preliminary history, not final headline results.

Independently recomputed Full medians and allocation medians for all six
corpora from raw `controlled-{baseline,fixed}-*.jsonl` files, checked their
sample counts and exact equality against `controlled-summary.json`, and checked
that the final document reports the same rounded values. Main repeated-flat
Full is **812.2272 -> 613.9072 ms**, with exactly
**664,085,232 -> 1,503,248 allocated bytes**. Peak working set is
264.16015625 -> 243.51171875 MiB. The unique-corpus peak increase is correctly
disclosed: 263.86328125 -> 266.078125 MiB (+2.21484375 MiB), while cumulative
allocation falls 633.169479 -> 228.229507 MiB and retained delta differs by
96 bytes. These are distinct metrics, not contradictory claims.

Final flat pooled edits are **0.0520 -> 0.1166 ms**, explicitly disclosed in
the document. Recomputed controlled disabled-tiering raw-file medians are
676.7109 -> 513.6301 ms for Full and 0.02475 -> 0.03725 ms for edits. The
remaining edit cost is not explained away or claimed to disappear. Small
first-Visible medians 51.60115 -> 51.87065 ms and overlapping ranges retain
the appropriate unchanged-path/jitter interpretation, not a speedup claim.

The reproduction instructions now pin baseline dependencies and overlay only
fixed Formats, preventing the initial provenance weakness from silently
recurring. Final document scope remains managed format-only, finite
fresh-process observations, not GUI/AOT or p95 acceptance. **No blocking issue
found in the final controlled benchmark/report.** No functional cases were
rerun and frozen production source was not edited.
