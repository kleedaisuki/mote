# Native JSON requested-file Open attribution

Date: 2026-10-01. Scope: attribution of the ordinary Native AOT JSON pilot's
requested-file Open, followed by bounded, isolated Windows x64 Engine probes.
**Decision: reject two over-scanning prototypes; nominate a minimal direct-return
candidate for an independently reviewed, uninstrumented actual Engine experiment.**
The first two candidates improve one representative large LF JSON file but
reproducibly regress supported shapes. A final audit removed a redundant CR scan;
that third candidate preserves the large JSON signal without a coherent small-
shape regression in these bounded controls. It is not a demonstrated production
or cross-RID win. No production source or CI was changed.

## 1. Question and endpoint separation

The [second hosted pilot](../validation/native-json-large-second-hosted.md),
[CI 36800944850](https://github.com/kleedaisuki/mote/actions/runs/36800944850),
source `99fbe395308816e3d1d2381ace58c3d5800811ec`, establishes scoped Native AOT
Windows open/edit/Save capability, not a latency tail SLA. The next question is:
**which work makes a requested 100 MiB JSON file editable, and which inexpensive
change could reduce it without moving cost or breaking line semantics?**

```text
Parent launch → native requested-file acknowledgement    automation clock
  child post-config → initial blank editable source     startup span
  accepted requested-file Open
    ├─ Engine read + SHA + decode + chunks + rope        document.open
    ├─ UI queue / replacement / bounded source binding
    └─ requested-file editable endpoint                  open_to_editable
         └─ matching source draw callback return        open_to_draw_submission

Later visible analysis and opportunistic full certification are separate lanes.
```

`mote.startup_to_editable` is the initial empty buffer, not the requested file;
its size dimension is `<1KiB`. Configuration and parent process creation are
outside it. The [trace contract](../end-to-end-tracing.md) defines `document.open`
as combined read/decode/rope work and `open_to_editable` as source installation,
without waiting for full semantic certification. A draw callback is not physical
paint or compositor completion. The automation observer uses 50 ms polling and
bounded native messages, not physical input.

## 2. Raw hosted traces identify the dominant boundary

No GUI workflow was rerun for this artifact attribution. The extractor follows
`trace_id` and `parent_span_id`, not JSONL adjacency. Exactly one accepted Open,
one nested Engine Open and one matching draw span were found per process; the
source layout below is the span parented by that matching draw interval.
All entries are **individual observations**, including reopen, not p50/p95.

| RID / file / process | Engine Open | Requested-file editable | Non-engine residual | Bound source layout | Draw return |
| --- | ---: | ---: | ---: | ---: | ---: |
| x64 / 1 MiB / original | 7.613 ms | 19.311 ms | 11.698 ms | 4.260 ms | 30.831 ms |
| x64 / 1 MiB / reopen | 13.617 | 24.764 | 11.147 | 4.794 | 32.694 |
| x64 / 100 MiB / original | **410.667** | **421.494** | **10.827** | 4.500 | 429.436 |
| x64 / 100 MiB / reopen | 413.661 | 423.199 | 9.538 | 4.480 | 431.136 |
| ARM64 / 1 MiB / original | 4.396 | 16.376 | 11.980 | 5.786 | 25.094 |
| ARM64 / 1 MiB / reopen | 3.737 | 14.090 | 10.353 | 4.183 | 20.539 |
| ARM64 / 100 MiB / original | **297.695** | **306.876** | **9.181** | 4.585 | 313.892 |
| ARM64 / 100 MiB / reopen | 297.705 | 306.942 | 9.237 | 4.625 | 314.070 |

Engine Open accounts for **97.4%** of the original x64 100 MiB child interval
and **97.0%** of the original ARM64 interval. This is a scoped wall-time
attribution, not a CPU utilization fraction. The residual combines background
scheduling, UI queue delay, controller replacement and source projection; only
the bounded layout is independently timed. Do not label the entire residual
as rendering. Matching child intervals share the same Open start, so their
reported durations can be subtracted: x64 original editable→draw-return is
**7.942 ms**, ARM64 **7.016 ms**. Parent launch duration is **never** subtracted
from a child duration or used to reconstruct config time.

The x64 100 MiB blank-shell startup span is **104.107 ms**, not a competing
estimate of the 421.494 ms requested-file Open. Parent launch→requested source
acknowledgement is 578.2432 ms, which includes process startup, polling and IPC.
Files were just written and hashed; neither original nor reopen is disk-cold.
The machines differ by architecture/image/hardware; no x64-vs-ARM speed ranking
is justified. macOS readiness failed before requested source acknowledgement,
so there is no accepted Mac latency sample from this run.

The 100 MiB version-0 `analysis.parse` observations of **2.767 ms** (x64) and
**2.374 ms** (ARM64) are the scheduled **visible** policy turns. They are not full
file parsing. A later `analysis.published` belongs to idle Full certification,
whose worker has no corresponding independent `analysis.parse` span here.
That later semantic work is not on the source-editable critical path. Its full
cost and delay cannot be inferred from the tiny visible parse or from UTC
publication timestamps; they remain a separate actionable measurement gap.

## 3. Isolated Native AOT probe and controls

The source-controlled
[`benchmarks/NativeJsonOpenAttribution`](../../benchmarks/NativeJsonOpenAttribution/README.md)
preparation script copies Engine source into root `.temp`, changes only its
namespace, inserts five fixed phase timers and preserves the actual unchanged
Engine reference as baseline. Source substitutions require exactly one match;
CRLF source is normalized before substitutions. A first scratch revision missed
the multiline read replacement because of CRLF and reported zero read calls;
its rows are retained separately and **excluded from read attribution**. The
corrected probe requires positive read counts and records the terminal EOF read.

The modes are real baseline, disabled copied timer control, enabled copied timer
phases, two explicitly rejected scanner candidates, and the minimal direct-return
discriminator. Open keeps the original
BOM/strict UTF-8 decoding, SHA-256 source identity, metadata-before/after guard,
16 Ki UTF-16 immutable chunks, and balanced persistent rope. No semantic parse,
GUI, telemetry, Save, project/workspace or language server is invoked.

Environment: **Windows 10.0.26200 x64**, **.NET 10.0.11 Native AOT**, **20 logical
processors**, one local developer host. Fixture generation/hash scans precede
timing; **OS cache is not evicted**. Each sample is a fresh process, each timed
child is bounded to 30 seconds; modes rotate per repetition. Source-copy
Document SHA: `567587427f8ef091f1cd50454bd2ea5e1c4ad7155790c80155fd1ae066b89dfc`;
RopeNode SHA: `09db1be43d13f244936d34b83e8a5fadf80b98e8ece6279dad0881898d59a2be`.
All source hashes and each compiled probe SHA remain in the cohort metadata.

Exact ASCII fixtures match the hosted pilot:

| Size | Original SHA-256 | UTF-16 length / lines |
| --- | --- | --- |
| 1 MiB | `9004bc8156e480461b02205536ee607414186c776078a8f35b20716c7e668355` | 1,048,576 / 20,972 |
| 100 MiB | `11c596afa32f508d22cf7704eb458200fd66c8ec05af3d9f4a384aeb0570c5db` | 104,857,600 / 2,097,152 |

### First corrected attribution cohort

Three processes per mode at 1 MiB; six per mode at 100 MiB. Median and **observed
range** below do not establish tails. Phase medians must not be summed to invent
a synthetic sample; the residual is computed independently per process.

| Metric / phase | 1 MiB median [range] | 100 MiB median [range] |
| --- | ---: | ---: |
| Actual uninstrumented Engine Open | 6.040 [5.211, 8.924] ms | **262.258 [260.969, 268.231] ms** |
| Copy-control Open | 5.133 [4.865, 8.904] | 272.112 [255.444, 285.201] |
| Instrumented Open | 6.113 [5.964, 7.571] | 262.095 [254.934, 277.274] |
| Read-await, including final EOF | 0.282 [0.254, 0.593] | **51.297 [44.505, 54.105]** |
| Incremental SHA-256 append | 0.485 [0.476, 0.494] | **49.838 [47.926, 50.304]** |
| Strict UTF-8 decode | 0.083 [0.069, 0.085] | **6.258 [5.923, 6.985]** |
| String chunks / list additions | 0.452 [0.449, 0.479] | **77.594 [75.807, 82.103]** |
| Rope nodes / leaf break statistics / tree | 0.788 [0.735, 1.050] | **73.624 [70.987, 81.856]** |
| Per-process uninstrumented residual | 3.916 ms | 4.839 ms |

100 MiB calls are `[1601,1600,1600,1600,1]`; 1 MiB
`[17,16,16,16,1]`. Read-await includes cached I/O completion and thread scheduling;
it is not isolated physical storage service time. Chunk and rope phases include
GC/scheduling within their scopes. Rope time is **not purely line scanning**:
it also creates ~12,799 nodes and a balanced tree. UTF-8 decode is small relative
to read, hashing, allocation and rope setup; optimizing a lexer cannot remove
this initial Open cost.

The controls have overlapping ranges and unstable ordering. They do not support
a numerical overhead correction or a claim of zero overhead. They do support
using coarse phase attribution instead of treating tens of nanoseconds of
stopwatch work as a shipping speedup. This developer-host Engine cohort is not
a paired performance comparison with hosted 410.667 ms native GUI Open.

100 MiB baseline whole-open managed allocation median is **211,084,136 bytes
(201.305 MiB)**. The immutable text itself is 200 MiB in UTF-16; this is not an
extra 200 MiB whole-file copy. Post-yield forced-GC live heap is **210,643,120
bytes (200.885 MiB)**, and point working set **225,609,728 bytes (215.158 MiB)**.
The process peak counter read then equals that working set for these samples,
not a GUI/full-parse memory peak. All six baseline rows have GC collection deltas
`[19,18,3]`; collections occur inside measured phases. Process CPU is coarse
(15.625 ms increments on this host) and can exceed wall time; its 265.625 ms
median is not a reliable subphase attribution. All-process precise cumulative
allocation is used because async continuations migrate threads;
[Microsoft's API contract](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalallocatedbytes)
distinguishes this from native allocation and retained heap.

## 4. Discriminating scanner experiments: keep the negative evidence

Hypothesis: line metadata's scalar scan is avoidable using platform span
operations without changing the rope. Candidate A counts LF, searches CR and
adds only CR not followed by LF. Candidate B first checks for CR; no-CR leaves
use A, while CR leaves keep the original scalar implementation. Both preserve
leaf trailing-CR counting and branch cross-leaf CRLF correction. No ISA-specific
intrinsics or additional dependency were introduced.

Correctness: exhaustive strings over `x`, CR, LF through length 8; whole and split
leaves; >64 KiB LF/CRLF/CR/none; 16,383/16,384/16,385-unit fragmentation; CRLF
straddling leaves. Each candidate matches original line counts, every line's
start/content, every small-string offset and 1,000 sampled large-string offsets.
Together: **39,392 scalar/candidate snapshots and 1,142,794 queries**. The separate
Python byte oracle also validates **240/240** measured prototype observations'
length/line counts, without rerunning Open. This is differential contract
validation, not proof of all future edits or an accepted production patch.

Each table cell uses a **separate matched cohort** of six fresh processes per
mode on the same local host/binary. Do not compare baseline columns across
cohorts as a speed change. Candidate median differs from the median paired
change, so both are reported. No p95 is used.

| Workload | A baseline → candidate Open median | A mean paired difference | B baseline → candidate Open median | B mean paired difference |
| --- | ---: | ---: | ---: | ---: |
| Actual 100 MiB JSON, LF | 254.080 → 229.757 ms | **−24.734 ms** | 257.078 → 239.554 ms | **−14.380 ms** |
| 10 MiB `a LF` | 24.477 → 24.143 | −0.797 | 24.777 → 27.328 | **+2.681** |
| 10 MiB `a CRLF` | 22.133 → 42.907 | **+20.138** | 23.787 → 23.530 | +0.298 |
| 10 MiB `a CR` | 26.470 → 54.104 | **+29.639** | 25.862 → 25.951 | −0.004 |
| 10 MiB no-newline | 23.738 → 24.289 | +1.045 | 24.844 → 25.782 | +0.498 |

A's dense CRLF and CR regressions occur **6/6** matched repetitions. B removes
those large regressions but dense LF still regresses **6/6**. B's JSON improvement
is not sufficient grounds to burden supported dense LF text with a repeatable
~11% median Open regression. These adversarial files are valid editor inputs,
not hypothetical invalid syntax.

For auditability, matched candidate−baseline Open differences in repetition
order are retained below (ms):

- A, JSON: `−25.760, −26.554, −25.899, −29.154, −24.382, −16.656`.
- A, dense CRLF: `18.614, 20.467, 20.307, 19.927, 22.002, 19.511`.
- A, dense CR: `28.548, 37.073, 25.322, 32.043, 26.000, 28.851`.
- B, JSON: `−4.775, −13.899, −16.147, −18.265, −14.017, −19.179`.
- B, dense LF: `2.649, 3.173, 2.949, 1.225, 4.210, 1.881`.

Exploratory paired bootstrap: 20,000 resamples, seed 73219, percentile 95%
interval of **mean paired difference**. A JSON `[−27.312,−21.231]`, A CRLF
`[19.355,21.022]`, A CR `[26.724,32.968]`; B JSON `[−17.641,−10.237]`, B dense LF
`[1.937,3.439]` ms. With six pairs these are descriptive within-host resampling
intervals, **not** independent machine/image uncertainty, causal scheduling
proof or a population SLA. Copy-control and timer-enabled controls remain
available in all raw rows. The decision is supported by directionally consistent
end-to-end matched regressions, not by a microbenchmark or the interval alone.
[Kalibera and Jones, ISMM 2013](https://kar.kent.ac.uk/33611/) motivate maintaining
nested variation and uncertainty rather than advertising one favorable sample.

**Reject A and B as implemented.** Do not add a newline-density heuristic merely
to make a benchmark pass. Final source audit found an important mechanism error
in B: after checking absence of CR, it still entered A and performed a second CR
search after LF counting. Its negative result is real but does not refute the
minimal no-CR fast path. Production data layout and line contracts remain unchanged.

### Final discriminator C: no-CR → CountLF directly

C checks `Contains(CR)` once and returns `Count(LF)` immediately for no-CR spans;
otherwise it executes the original scalar method. It does not enter A's CR loop,
add density sampling, alter leaf sizes or change cached boundary algebra.
Its own six-pair cohort uses one compiled binary, real unchanged Engine baseline,
branch-equivalent copied scalar controls and timer-enabled copied candidate.

| C workload | Baseline → candidate Open median | Mean paired difference | Exploratory 95% paired interval |
| --- | ---: | ---: | ---: |
| 100 MiB actual LF JSON | **256.597 → 230.325 ms** | **−27.428 ms** | **[−33.717,−19.503] ms** |
| 10 MiB dense LF | 23.448 → 22.463 | −0.507 | [−2.080,1.010] |
| 10 MiB dense CRLF | 23.414 → 24.241 | +0.611 | [−1.526,2.299] |
| 10 MiB dense CR | 24.033 → 23.908 | −0.639 | [−2.498,0.950] |
| 10 MiB no-newline | 25.238 → 24.448 | +2.015 | [−0.992,6.936] |

C's large JSON improvement is **6/6** matched pairs, differences
`−30.027, −9.661, −25.896, −36.345, −36.843, −25.797` ms. Within the same copied
instrumented cohort, scalar `phases` has Open median **250.056 ms**, C **230.325**;
rope phase medians **67.303 → 47.858 ms**. The same-cohort phase comparison
supports locating the change in rope statistics rather than relabeling a
separate faster machine. These are instrumented copies, not direct production
AOT editor before/after measurements.

The no-newline cohort contains a **38.814 ms** C outlier, with matched difference
**+13.865 ms**; it is retained, never discarded as presumed noise. Its remaining
five samples are 23.928–25.109 ms. No trace establishes that outlier's cause.
Only this unresolved shape received a bounded followup: **12 new pairs**, same
compiled binary, rotation/controls preserved, no pooling with the first cohort.
Baseline median/range **23.653 [21.944,24.299] ms**, C **23.327 [22.226,25.566] ms**;
mean paired difference **−0.048 ms**, exploratory interval **[−0.560,0.486]**.
The followup does not reproduce a coherent no-newline regression, but cannot
exclude rare tails or retrospectively explain the original sample. Background
load/OS cache on this developer host were not comprehensively controlled.

C is therefore **worth a narrowly scoped integration experiment**, not release
acceptance: use the unchanged real Engine with uninstrumented old/new line
counting, preserve all line/edit/CRLF contracts, then obtain same-RID randomized
Native AOT evidence and cross-RID correctness. Do not quote the copy's ~10%
median JSON improvement as a shipping requested-file speedup. If a target
regression appears, retain the current implementation.


## 5. Grounded next decision, not a platform rewrite

After C is independently reviewed, the next mechanism discriminator is a matched
isolated Engine Open probe
varying **caller byte read size and FileStream buffering**, preserving exact
BOM/strict decoder state, SHA input order, short-read handling, cancellation and
metadata guards. Current sequential Open issues ~1,601 reads at 64 KiB; read-
await is ~51 ms of the ~262 ms local instrumented Open, not the whole bottleneck.
Keep 16 Ki text leaves fixed while changing only byte reads; rotate controls,
include 1/100 MiB LF/CRLF/long-line and multibyte UTF-8, and measure whole Open,
allocation/GC, cancellation and retained heap. Reject a faster average if it
creates material supported-shape or memory regressions.

This recommendation is an **experiment**, not a claim that a bigger buffer or
synchronous I/O is faster. Microsoft's production
[FileStream investigation](https://devblogs.microsoft.com/dotnet/file-io-improvements-in-dotnet-6/)
shows caller buffers and OS-specific async mechanisms can materially affect
cost, and explains the offset-based
[RandomAccess API](https://learn.microsoft.com/en-us/dotnet/api/system.io.randomaccess).
The current algorithm is sequential with stateful strict decoding; offset APIs
are available, but do not by themselves justify parallel reads, replacing the
stream or complicating file lifetime. Start with one bounded mechanism change,
not mmap, a new engine representation or a dependency.

The mature [VS Code text-buffer implementation account](https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation)
emphasizes line metadata and real workload profiling, not a universal tree that
wins every operation. A research-level lens is measured persistent sequences:
[Hinze and Paterson, JFP 2006](https://www.staff.city.ac.uk/~ross/papers/FingerTree.html)
shows how cached algebraic measures support efficient sequence queries. Mote's
cached length/break counts and boundary flags already follow that principle;
the CRLF boundary algebra is an invariant to preserve, not an incidental branch.
More recent [dynamic-string research (2024)](https://arxiv.org/abs/2403.13162)
explores efficient collection updates and substring queries; it does **not**
measure strict whole-file ingestion or native editor startup. It is a research
representation option, not evidence for replacing this rope in the face of the
measured read/allocation cost. No architecture rewrite is recommended here.

Full semantic-certification latency is a distinct future question: add a
causally named idle-worker span before attributing its time, rather than
relabeling the visible parse. It must remain distinct from editable source and
physical display latency.

## 6. Artifacts and reproduction

Raw hosted extraction: `.cache/native-json-open-attribution/hosted-phase-attribution.json`;
extractor `.temp/NativeJsonOpenAttribution/extract.py`. Original traces remain
under `.cache/ci-36800944850-native-json-{win-x64,win-arm64}/`. First corrected
local cohort: `local-rows.jsonl`, `local-metadata.json`; excluded preliminary
rows: `local-rows-v1-read-uninstrumented.jsonl`. Scanner cohorts:
`prototype-rows.jsonl`, `prototype-metadata.json`, `hybrid-rows.jsonl`,
`hybrid-metadata.json`, `direct-rows.jsonl`, `direct-metadata.json`; C
`direct-summary.json`, `direct-line-oracle-audit.json`; unresolved-shape followup
`direct-none-followup-rows.jsonl`, `direct-none-followup-summary.json`,
`direct-none-followup-metadata.json`; paired bootstrap `prototype-summary.json`; independent
post-hoc line check `legacy-line-oracle-audit.json`, all in that same `.cache`
directory. Prepared sources and utility scripts remain in `.temp/NativeJsonOpenAttribution`.
The newly tracked harness reconstructs them without requiring this scratch.

```powershell
python -B benchmarks/NativeJsonOpenAttribution/prepare.py `
  --directory .temp/native-json-open-attribution-fresh
dotnet publish .temp/native-json-open-attribution-fresh/Probe.csproj `
  -c Release -r win-x64 -p:PublishAot=true `
  -o .temp/native-json-open-attribution-fresh/publish
python -B benchmarks/NativeJsonOpenAttribution/run.py `
  --directory .temp/native-json-open-attribution-fresh `
  --output .cache/native-json-open-attribution/fresh `
  --suite attribution --repeats 1
```

Fresh-directory validation is a reproducibility/functionality check, not an
additional performance cohort or a replacement for the measurements above.

Fresh-directory Native AOT preparation/publish passed, followed by **6/6**
real baseline/copy-control/phases processes (1 and 100 MiB, one each), exact
independent line/length oracles and positive read hooks. Fresh binary differential
check again passed **39,392** snapshots / **1,142,794** queries. The bounded runner
finished normally and wrote metadata/rows under `.cache/native-json-open-attribution/fresh/`.
Portable protocol checks passed **6/6** (four initial checks for artifact confinement,
fail-closed source shape, fresh preparation and cross-block independent CRLF
oracle; two subsequently added checks for source-drift rejection and preserving
existing evidence). The new guards reject before starting a child process. These are
functionality checks, not new tail or optimization evidence.

The final harness rejects nonempty preparation directories (it never deletes
published binaries), live Engine or generated-source/template drift, and existing
suite result files. A unique preparation fingerprint is embedded in the compiled
probe; a bounded `identity` command must match the manifest **before** correctness
or fixture generation, and every accepted row carries the same identity. This
is an accidental-stale-build guard, not signed supply-chain attestation. Legacy
exploratory cohorts predate that guard; their individual publish logs, binary
hashes and copied-source records remain retained and are not retroactively
claimed to have embedded fingerprints.

Final fingerprint-enabled fresh preparation/publish also passed the real bounded
runner: **6/6** attribution children (1/100 MiB baseline, copy-control, phases),
matching embedded preparation identities, exact line/length oracles and positive
read hooks. The final three-candidate differential oracle passed **59,088**
snapshots / **1,714,191** queries. The three-candidate direct suite's independent
fresh-directory functionality run passed **20/20** child processes across all
five shapes before the fingerprint guard was added; no performance inference
is drawn from its one repetition. Portable protocol checks now pass **7/7**,
including nonempty-directory rejection; affected source-drift/result guards
were checked after their final inventory enhancement. A real compiled binary
with a deliberately mismatched preparation manifest was rejected before fixture
creation or correctness execution; the original manifest was restored. Final
fingerprint-enabled artifacts are in `.cache/native-json-open-attribution/attested/`;
this validation does not retroactively strengthen older experimental provenance.

[Independent review](../reviews/native-json-open-perf.md) found no unresolved substantive issue in the bounded diagnostic or final build-identity guards. Candidate C remains eligible only for the next real-Engine experiment, not a production latency claim.
