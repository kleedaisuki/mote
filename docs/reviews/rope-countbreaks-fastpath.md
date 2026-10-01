# Rope logical-break fast path: independent review

Date: 2026-10-01. Scope: the private `RopeNode.CountBreaks` guard, focused
`EngineLineBreakFastPathTests`, actual-Engine Native AOT comparison retained in
`.cache/native-json-open-fastpath`, and the accompanying performance report.
This review changes no product, test, benchmark or workflow source.

## Verdict

**No unresolved substantive finding in the frozen three-file change.** Accept
the private fast path and focused regression tests for ordinary cross-platform
correctness verification, with the documented dense-LF trade-off explicitly
accepted. The performance report is calibrated to its local evidence. This is
not release acceptance or approval of a four-RID latency claim. Reviewed frozen
`RopeNode.cs` SHA-256:
`b3950a8c52469b363576f2f4686804a5cab1984506533d4b41c1e785411f235d`.

## Correctness and design

The new guard has a simple equivalence argument: when a UTF-16 span contains no
CR, the old scalar loop counts precisely its LF characters. Framework `Count`
counts those same values; it does not normalize text, interpret graphemes, or
allocate replacement strings. Every span containing CR still executes the
unchanged scalar CR/LF/CRLF algorithm. Empty spans return zero.

The cached `StartsWithLf` / `EndsWithCr` facts, branch subtraction for CRLF split
across leaves, prefix-count adjustment in `TextSnapshot.GetLineIndexFromOffset`,
and `NthBreakEnd` remain unchanged. A prefix ending after CR but before LF keeps
the existing temporary CR count, corrected by the snapshot query. The guard
does not alter immutable leaf ownership, AVL balancing, snapshot sharing,
UTF-16 offsets, surrogate-edit rejection, or any public API. There are no new
state fields, scanner modes, thresholds, native intrinsics, or format-specific
exceptions. That is preferable to density heuristics selected from this corpus.

The platform owns span-operation implementations and hardware specialization;
this is not a promise that every RID emits the same SIMD instructions.
[Microsoft's Count contract](https://learn.microsoft.com/en-us/dotnet/api/system.memoryextensions.count)
and [runtime implementation](https://source.dot.net/System.Private.CoreLib/src/runtime/src/libraries/System.Private.CoreLib/src/System/MemoryExtensions.cs.html)
support this division of responsibility.

Focused tests cover all 9,841 small strings over `x`, CR and LF (lengths 0–8),
every small offset, UTF-16/surrogate leaf boundaries, split CRLF, newline-pair
edits and immutable snapshots through Undo/Redo, and UTF-8/UTF-16LE/UTF-16BE file
Open. The line-start oracle scans the whole source independently of rope
metadata. Reviewer execution: **9/9 passed**, reported test duration **481 ms**;
TRX is `.cache/review-rope-countbreaks/focused.trx`. These are managed local
semantic tests, not Native AOT four-RID or GUI acceptance.

## Performance evidence and trade-off

The reviewer inspected the two harness project references, unchanged harness
source, publish logs showing native-code generation, paired runner/summary
scripts, source manifests and retained rows. All nine baseline Engine copied
source/project hashes and all nine candidate Engine hashes match the recorded
manifest. Baseline uses the unchanged actual Engine namespace and implementation,
not the earlier timed/copied candidate-switch experiment. Candidate references
the changed actual Engine. Timing contains `Document.OpenAsync`, not parsing,
GUI binding, physical paint, or process launch.

Both binaries are fresh-process Windows x64 Native AOT, .NET 10.0.11, on one
developer host. Fixture generation and SHA/oracle reads precede timing; this is
warm OS-cache ingestion, not cold storage. Each fixture has equally many
baseline-first and candidate-first pairs with seeded shuffled order. That
reduces order bias without making samples independent hosts or eliminating
background activity. Source and binary hashes plus publish logs provide useful
provenance; this scratch cohort has no embedded build attestation.

| Retained observation | Judgment |
| --- | --- |
| 100 MiB LF JSON: medians 254.140 / 230.869 ms; all 12 paired differences favorable; mean difference -21.852 ms | Strong scoped source-level improvement, about 23 ms median / 9.2%; not a hosted GUI or cross-RID result |
| Dense 10 MiB `a\n` corpus, 50% LF: initial medians 23.327 / 24.088 ms; followup 23.515 / 23.988 ms | Accept a demonstrated approximately 0.5–0.8 ms median regression rather than claiming universal improvement |
| Synthetic realistic-length LF Markdown: followup medians 26.383 / 24.309 ms; CSV 27.261 / 25.345 ms | Supports the chosen line-density trade-off for these controls, not a representative user distribution or semantic-parser speedup |
| CRLF, CR, no-break and multibyte controls have mixed paired directions | No coherent regression established by these small cohorts; not equivalence or tail guarantees |
| 4 KiB LF: candidate 71.478 ms initial outlier; followup baseline 68.243 ms and another large baseline sample | Preserve all rows and keep followup separate. Cause remains unknown; neither discard as noise nor claim tail closure |
| 100 MiB allocation medians 211,084,152 / 211,084,160 bytes; live heap both 210,642,232; each mode has nine GC deltas `[19,18,3]` and three `[18,17,3]` | No material allocation improvement or regression shown; median GC delta is `[19,18,3]`; counters and GC remain distinct from latency and full-product memory peak |

The LF fast path scans for CR then counts LF, so it does two span operations.
The dense-LF regression is plausible, but the cohort does not establish an
instruction-level cause. CR-containing spans can add a prefix search before
the old scalar loop; supported CR/CRLF inputs therefore still need future
platform coverage. The minimal guard has a favorable large structured-file
benefit with a small absolute cost in an intentionally newline-dense supported
case. Accepting that trade-off is a practical judgment, not permission to hide
negative measurements. Broader density dispatch would increase complexity
without evidence that it improves dominant workflows.

Within-host bootstrap intervals are exploratory descriptions, not a production
tail SLA, independent-host uncertainty, or a multiple-candidate selection
correction. The retained followup does not erase the first cohort. Framework
specialization and Native AOT layout can differ on Windows ARM64 and both macOS
RIDs; no speedup is asserted there. The next hosted validation should preserve
semantic tests and ordinary requested-file Open endpoints instead of applying
the local percentage to previously collected GUI traces.

## Review limits

No large cohort or GUI acceptance was rerun by this reviewer. No CPU-disassembly
attribution, full-product memory peak, rare-tail bound, or four-RID performance
claim is supplied. This review is independent of concurrent native accessibility
and Windows preview changes; those areas are outside its scope.
