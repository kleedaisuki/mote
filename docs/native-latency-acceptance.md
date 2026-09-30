# Native latency acceptance: the next measurable slice

## Decision and present bottleneck evidence

Do not implement another speculative text-store or parser optimization to make
the “opens instantly / edits smoothly” contract sound credible. Build a common
workload/evidence layer over the existing reviewed native drivers, then profile
the slow **actual endpoint**. The new
[`NativeAcceptance` tool](../benchmarks/NativeAcceptance/README.md) safely provides
that layer's synthetic corpus and causal trace auditor, **not** a finished GUI
driver or performance gate. No production code, existing driver, or CI workflow
is changed by this slice.

Relevant prior evidence was reused rather than repeated:

- [Native baseline](native-performance-baseline.md): process-isolated 100 MiB
  engine open was 357.567 ms p50 on the recorded Windows hosted runner and
  196.120 ms on the recorded Mac runner; these are different machines, not OS
  comparisons. Engine edits were tens of microseconds, not GUI input latency.
- [Ordinary Windows launch](../benchmarks/NativeStartup/README.md): the selected
  same-binary local batch's launch-to-source medians were 386.3 ms (1 MiB, n=5)
  and 600.4 ms (100 MiB, n=3). Visible-window-to-source grew by about 239 ms;
  launch-to-visible-window did not grow with size. Foreground belonged to another
  process. This locates size-dependent work **after window construction**, but
  cannot attribute it to decode/rope, policy analysis, source installation, or
  GUI layout and cannot prove user-perceived responsiveness.
- [Large-file UI counterexample](large-file-ui-evaluation.md): the historical
  full-control representation scaled poorly in time and memory despite cheap
  rope edits. It supports bounding both viewport text and logical-line layout;
  it is not a current native-canvas benchmark.
- [End-to-end tracing](end-to-end-tracing.md) now separates canonical edit,
  analysis, semantic handoff, and version-matched native source draw return.
  Existing AppKit draw probes are attribution/correctness checks, not natural
  workload timings. WGC investigations do not authorize treating a future
  compositor metadata timestamp as an exact observed first-present time.

**Competing explanations for current latency:** read/decode/rope construction,
native source installation/layout, intentionally delayed analysis, and external
automation/focus/run-loop observation. The next decisive observation is one
ordinary-file launch with externally certified source readiness plus current
`document.open`, `open_to_editable`, and source-draw intervals. A large native
layout interval calls for a platform profile; a large engine-open interval calls
for I/O/decode/allocation profiling. A large external remainder calls for
launcher/observer calibration. Existing phases combine read/decode; do not invent
separate read/decode numbers by subtraction. No optimization is made until these
possibilities are distinguished on a current published binary.

## Keep six endpoint families separate

| Endpoint | Start/end contract | How to observe | Excluded claim |
| --- | --- | --- | --- |
| Process launch/control | Parent monotonic launch → observed child ready/exit | Alternate same-binary `--check-runtime` with GUI cases outside fixture generation/hash/config setup | Runtime-control exit is not editable GUI; parent includes launch/observer overhead |
| Configuration | Child entry → configuration/theme/trace setup complete | Not covered by current startup trace; use a separate opt-in phase profile if the remainder matters | `mote.startup_to_editable` begins **after** configuration and cannot measure it |
| File open → editable | Accepted open → installed canonical source returns; external native selection/action acknowledgement separately | `document.open_to_editable`, focused host/source oracle; record generation/revision acceptance externally | A `Shown` callback or title is not editability; installing source is not first pixels |
| Semantic ready | Accepted open/edit → current policy result **published**, full/partial/skipped classification | Parse/handoff records plus independent controller-result completeness oracle | Parse success alone does not imply publication or full semantics; debounce is part of path |
| Edit/draw | External action dispatch → exact source revision observed; separately canonical edit → matching source draw callback return | Reviewed UIA/AppKit/native-message driver plus `document.edit_to_draw_submission` | Direct WM_CHAR/selector differs from physical key; draw return differs from compositor/photons |
| Scroll | External scroll command → source anchor/range changes; separately matching native draw and optional independently observed frame | Native wheel/scrollbar/trackpad paths recorded as different action types; source anchor and bounded host oracle | Anchor change alone is not smooth pixels; existing edit draw trace does **not** time scroll |

Two clocks must not be subtracted unless a documented clock mapping has been
verified. Child durations use its monotonic clock; parent automation intervals
use the observer's clock. Persist both as separate endpoint families. The causal
audit uses span IDs and revisions, never UTC timing subtraction. A first
successful source draw can occur **before** semantic readiness.

## Workload and fair-control matrix

Primary scope: published strict-single-binary Native AOT **win-x64** and
**osx-arm64**, ordinary/default launch policy and isolated convention-default
configuration/theme. Add win-arm64/osx-x64 only after their capability/focus path
is certified; do not infer them from another ABI. Preserve the binary SHA,
strict inventory report, publish flags/SDK, commit and dirty-source status,
OS/runner image/hardware, display/DPI/refresh, focus state, observer version,
timer/poll overhead and run identity outside the timing window.

| Dimension | Primary cases | Necessary controls |
| --- | --- | --- |
| Format | JSON, CSV, Markdown | Treat CSV Grid/source and Markdown Flow/source as distinct surfaces; do not pool them |
| Byte size | Exactly 1, 10, 100 MiB | Generate and SHA outside every measured interval |
| Layout | Many short lines; one unbroken line **for each size/format** | One long CSV field or JSON string is valid input, not a malformed truncated document |
| Launch state | First fresh process after publish; subsequent fresh processes | Both OS caches **uncontrolled**; just-written corpus is not disk-cold |
| Home state | Fresh repo-local `MOTE_HOME`; reused repo-local home | Different strata: config/home cache reuse must not masquerade as process warming |
| Tracing | Off for primary external latency; on for causal diagnosis | Alternate off/on in same fixture/host/binary block; report paired deltas, not difference of medians |
| Input pattern | Isolated edit after idle; separate burst under analysis load | Idle spacing > debounce; burst cancellation counts/tail failures reported separately |
| Position | Start, middle, end in separate processes/rotated order | First-edit initialization and action order otherwise confound position |
| Scroll | Native page, near-end jump, repeated viewport steps; long-line horizontal | Relative-anchor and bounded-layout oracles; do not label synthetic scroll command a real trackpad |

Every input is synthetic and owned by the experiment. The corpus tool generates
the 18 size/shape/format cases in 666 MiB only when explicitly requested; default
is six 1 MiB cases. Do not place benchmark fixtures in a user document directory.
`MOTE_HOME` must be an isolated repo `.temp` child; cache/data/trace paths resolve
within that home. Disable traces for the primary observation and enable only
the paired diagnosis. No clipboard, input-source switch, TCC change, screenshots,
network telemetry, root cache eviction, or signed/notarized-distribution claim
is needed. A visible foreground desktop remains required for a user-like GUI
claim; if unavailable, fail/classify that condition, not silently accept a
background native-message workflow.

Maintain small/control correctness before scaling: exact source revision,
expected action count, native bounded island/frame, unchanged fixture before
explicit Save, exact Save/reopen oracle when applicable, and no modal/focus drift.
Do not resend a non-idempotent edit after a timeout. Failure/skip/cancellation
and incomplete/never-drawn intervals are retained separately from successes.
The most recent scroller probe's hosted outcome remains a separate integration
gate; this artifact tool does not rerun or supersede it.

## Repetitions and uncertainty

1. **Capability pilot:** one 1 MiB case per format on each target, one long-line
   negative/layout control. It answers whether the driver and endpoint work;
   it is not a tail baseline. Do not generate all 666 MiB until this passes.
2. **Phase pilot:** paired trace-off/on processes for the slow case, then profile
   the dominant phase only. Keep instrumented timing separate from production-like
   off timing; tracing overhead can shift both memory and scheduling.
3. **Candidate baseline:** use at least three independent hosted/local machine
   instances per RID, 30 fresh process launches per selected stratum, and at least
   100 endpoint observations for descriptive tails. Do not immediately multiply
   all 18 cases by all controls: focus on discriminating representative/limiting
   cases after the pilot, retaining the full corpus as explicit coverage.
4. Report raw samples, per-instance median/MAD/range, successful p50 and
   nearest-rank p95, and outcome/censor counts. Repeated edits in one process are
   clustered observations; uncertainty comparisons resample **process blocks**
   first (then actions), not 100 correlated actions as independent machines.
   Keep paired before/after binaries on the same host with randomized/alternating
   order and the same observer. Between-host comparison is stratified, not an
   unqualified cross-OS speed claim.

For intuition about tails: under the strong assumption of independent samples,
the chance that n samples contain at least one observation beyond the population
p95 is `1 - 0.95^n` (about 78.5% at n=30 and 99.4% at n=100). That does **not**
make nearest-rank sample p95 a calibrated SLA estimator, especially under
within-process or VM clustering. The tool's `small_sample` flag is a guardrail,
not a statistical confidence interval. Prefer stable within-host effect sizes
larger than calibrated observer noise; a one-run favorable median cannot justify
a production optimization. Product latency limits require a representative
hardware class and an owner-approved endpoint/budget; none is fabricated here.

## Memory and allocation: do not substitute one for the other

- Record Windows child working set at editable/semantic-ready/post-edit/scroll,
  positive process peak working set where available, and sampled-parent RSS
  lower bounds with poll interval. A point resident set is not lifetime peak.
- On macOS, unavailable managed `Process` peak/private counters remain **null**.
  A platform `getrusage`/`ps` observer can be added to the reviewed driver after
  its units/lifetime semantics are calibrated; do not treat sampled `ps` RSS as
  an exact peak or synthesize a zero memory result.
- Native AOT allocation is not inferred from RSS, executable size, or GC heap
  snapshots. Existing synchronous semantic allocation benchmarks answer a
  narrow phase question. Whole-app allocation profiling requires a separately
  instrumented Native AOT build (EventPipe explicitly enabled where supported),
  symbols/OS profiler, and an overhead control. That diagnostic build is **not**
  the production-like primary timing binary.
- A lower allocation count can coexist with higher RSS, as the CSV cold-path
  investigation already observed. Preserve retained-root/cache growth, CPU,
  end-to-end tail and correctness tradeoffs when choosing an optimization.

## CI wiring proposal — not modified or target-run

Keep correctness gates distinct from noisy shared-runner timing. Add a **new
focused non-gating suite** to the scheduled/manual benchmark workflow, not to
every six/seven-job product build. Proposed jobs:

| Job | First wiring | Exit/artifact policy |
| --- | --- | --- |
| Artifact contract | Python unit tests and six 1 MiB corpus grammar/hash checks on Windows + macOS; no mote launch | Deterministic correctness gate; upload tool-version/test evidence |
| Windows capability/phase pilot | Publish strict one-file AOT once; reviewed current default-source driver consumes generated JSON/CSV/Markdown fixtures with fresh `.temp` home | Per-case timeout and source oracle; upload raw external envelope and local trace; timing non-gating |
| macOS capability/phase pilot | Same corpus/binary inventory; reviewed AppKit/external source driver; no global setting/permission mutation | Explicit unsupported/focus-unverified endpoint flags; never turn missing endpoint into success |
| Causal audit | Feed each **known process** and all rotation files into the tool with predetermined operation counts | Integrity failures visible independently of workflow parent success; raw trace and exact report retained |

**Required before those GUI jobs can run:** coordinate adapters for the existing
drivers to accept the new fixtures and separate current default/legacy/CSV Grid
mode identity; add natural-close/reap and metadata/timeouts as needed; independently
review input/focus/source/privacy safety. Do not simply pass a new extension to a
hard-coded Markdown corpus driver and call it format-aware acceptance. Current
native draw probes are in-memory forced tests and cannot supply workload timing.
Current telemetry has no scroll interval/config phase/full-semantic completeness
certificate; those need explicitly coordinated contracts if the pilot shows
they matter. No `src/Mote.Native`, existing test, or `.github/workflows` change
was made in this slice.

## Production and academic grounding

[Microsoft Native AOT diagnostics](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/diagnostics)
documents the explicit EventPipe opt-in and the role of native symbols. This
supports keeping a minimal shipped binary and a separate diagnostic experiment,
instead of quietly changing the measured runtime regime. Apple's
[launch guidance](https://developer.apple.com/documentation/xcode/reducing-your-app-s-launch-time)
distinguishes first-frame launch from work needed before interaction; the
general lesson is to retain both “source installed” and independently observed
usable/frame endpoints, not to transplant an iOS metric to AppKit uncritically.
[GitHub hosted-runner documentation](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
supports recording runner/image identity; an image label does not calibrate
physical hardware, focus, or GUI observer jitter.

Two research directions inform the design without providing mote-specific
thresholds. [Kalibera and Jones, ISMM 2013](https://kar.kent.ac.uk/33611/45/p63-kaliber.pdf)
motivate nested repetition/uncertainty rather than a best-run comparison. The
more recent [Henning et al., FSE 2025 Industry Track](https://arxiv.org/abs/2504.11826)
studied end-to-end cloud benchmark variability over many runs and instances;
their observed stability for a stream-processing workload is **not** evidence
that GUI latency on GitHub VMs has the same variance. Its practical implication
here is to measure workload-specific variance and choose repetitions to resolve
the intended effect size, rather than assuming all cloud results are hopelessly
noisy or automatically stable. [Schmid et al., 2023](https://epub.uni-regensburg.de/55003/)
use stronger latency experiments for interactive systems; their gaming result
does not define an editor's acceptable latency. The methodological connection
is endpoint validity: software drawing and physical input-to-light require
different instruments.

## Completed contribution and next informative move

Completed: exact synthetic corpus generator; bounded causal reader/auditor with
explicit cancellation/loss/missing-evidence treatment; six deterministic tests;
independent review corrections; compatibility re-analysis of four already
retained Mac draw traces. This advances **measurement validity**, not measured
user latency. No new target-native timing/allocation/RSS run or optimization was
performed, and no speed claim is warranted.

Next: coordinate one reviewed driver adapter and one published-AOT **1 MiB JSON
ordinary-open** capability/phase run per primary RID, retaining the external
editable oracle and three existing child endpoint families. If that fails,
fix the driver/endpoint contract before scaling. If it works, use the current
100 MiB limiting case to separate size-dependent engine open from native layout
and automation remainder. That single observation can change the next engineering
decision; a broad uncontrolled timing matrix cannot.
