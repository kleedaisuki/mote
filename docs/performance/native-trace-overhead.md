# Opt-in native trace overhead: a controlled, bounded Windows experiment

Date: 2026-10-01. This document owns only an experiment record; no production,
tracked benchmark, or CI changes were made for it. Raw samples, the temporary
adaptation, and the analysis stay under repository `.cache/` and `.temp/`.

**Result:** 20 alternating off/on pairs completed with identical exact source
oracles, same visible-background state, no mode failures or state drift, and
20 complete enabled trace sessions with no emitted drops. The paired synthetic
input-ack median difference is **+0.150 ms**, conditional 95.86% median interval
**[−0.280, +0.716] ms**. This experiment does not resolve a repeatable tracing
penalty or benefit; it does not prove zero overhead or foreground typing safety.

## Question and evidence boundary

Disabled-path zero allocations and successful draw callbacks do **not** establish
the cost of enabled tracing during native editing. Existing
[`end-to-end-tracing.md`](../end-to-end-tracing.md) already identifies this gap.
The startup-smoke experiment in
[`native-performance-baseline.md`](../native-performance-baseline.md) alternates
tracing off/on but measures process-launch through immediate smoke exit, not
native editing. It is not repeated here.

This experiment asks whether opt-in tracing adds a resolvable latency or CPU
penalty to **one source-verified synthetic edit in a visible-background native
Windows x64 Canvas**, using the same Native AOT executable in both modes.
It is not foreground typing, an IME workload, sustained typing, Markdown/JSON
semantic analysis, large-file acceptance, physical presentation, or a release
performance SLA. A startup-file opening observation is also recorded, but its
endpoint is bounded input-island availability, not the complete source image.

## Reused probe and bounded adaptation

The experiment reuses the existing
[`Measure-WindowsScreen.ps1`](../../benchmarks/NativePaintLatency/Measure-WindowsScreen.ps1),
`WindowsScreenObserver.cs`, `CanvasFixture.cs`, and `Win32Probe.cs`. It retains
the exact Save/Undo/Redo and source-specific screen-state oracles, negative
control, finite cross-process waits, target-ownership checks, normal close, and
safe workspace-bound cleanup. No screenshots or document bodies are persisted.
The existing local opt-in's five-point occlusion check remains an acknowledged
limit: it cannot prove every interior pixel is owned by the target.

With parent authorization, **only a `.temp` copy** of the PowerShell driver was
adapted to:

- Set `MOTE_TRACE=0` or `1` through a `TraceMode` parameter.
- Keep each process's home and generated file isolated under repository `.temp`.
- Preserve opt-in JSONL in that sample's repository `.cache` directory before
  deleting generated state.
- Record launch-to-input-island readiness, process CPU/lifetime, terminal
  session status, reported dropped records, JSON parsing errors, and the enabled
  version-1 source draw interval.
- Keep all existing behavioral/screen oracles identical between modes.

The adapted driver, transformation, manifest, repetition driver, and analysis
are `.temp/native-trace-overhead/{Measure-WindowsScreen.ps1,adapt.py,manifest.py,
Run-Pairs.ps1,summarize.py}`. The exact-hash manifest and raw results are
`.cache/native-trace-overhead/{manifest.json,samples.json,summary.json,
paired-index.jsonl,continuation-index.json}` and `screen/<run-id>/`.

The measured executable is `.cache/uia-range-aot/mote.exe`, 7,130,112 bytes,
SHA-256 `20b5bd467899eb7618af6737dcd10e875f90fa5b1e71a5b384a4f1052b9d8ced`,
published 2026-10-01 03:10:11 UTC. Its original-source STA integration is
documented in
[`windows-uia-range-external.md`](../validation/windows-uia-range-external.md),
at the `a7c66df` source context. **It is not the current HEAD executable**:
later typography, Mac Save witness, and in-flight telemetry prefix/outcome
changes are absent. This is a frozen diagnostic baseline, not evidence for
the overhead of those newer changes.

## Endpoint and resource contracts

| Recorded value | Comparable off/on? | Exact interpretation |
| --- | --- | --- |
| `input_ack_ms` | Yes, same-state paired processes | External monotonic dispatch of synthetic `WM_CHAR` through its synchronous reply; not physical input or an isolated engine edit |
| `launch_to_source_ready_ms` | Yes, same polling contract | Before target process creation through title/canvas/bounded nonempty input-island observation; includes at most approximately one 20 ms polling interval of observer delay, not first editable frame or complete source draw |
| `first_changed_capture_ms` | Yes, only with existing source/screen oracles | First sampled source-specific changed screen ROI; sampling-late API observation, not compositor/physical paint |
| `process_cpu_ms` | Yes, whole-workload context | Target process user plus privileged CPU through exit; includes open, edit, Save/Undo/Redo, subsequent drawing, writer, and shutdown; excludes external observer CPU |
| `process_lifetime_ms` | Yes, whole-workload context | Launch through the same normal close; includes deliberate settle/control waits, so not interactive latency |
| `trace_first_edit_draw_us` | **No** | Diagnostic-on-only accepted canonical version-1 mutation through matching source draw callback return; absent in off mode, cannot be used as an off/on estimate |
| Trace records/bytes/drops/terminal | Health evidence only | Successful terminal session and valid JSONL with no emitted drop aggregate; not proof every application boundary was instrumented or crash-time durability |
| Managed allocations/peak memory | **Unavailable here** | No allocation/GC observer or comparable memory sampling was added; disabled-path allocation tests must not be relabeled enabled-path measurements |

No UI callback waits for trace disk writes. Enabled producers still create
causal identities/activities and enqueue records; the consumer consumes CPU,
serializes, and writes concurrently with the UI. Those are plausible enabled
costs, not measured per-component attribution. Whole-process CPU can constrain
a material workload penalty but cannot identify its source.

## Experimental procedure and state qualification

The local host reports Windows 10.0.26200, Intel i9-12900H, 20 logical processors,
34,087,665,664 bytes physical RAM, 2560×1440 primary display, 96-DPI target,
1057×988 Canvas client area. All accepted paired samples use one deterministic
1 MiB CRLF plain-text fixture with sentinel, source SHA-256
`5a900aeab7463e7b2fcf7481453882043dee41ca15f8a9bb8fa367e5c8c43e1f`.
The workload inserts one leading `X`, verifies the disk is unchanged before
explicit Save, then exact X Save → original Undo Save → X Redo Save and their
distinct/matching glyph shapes. Each sample uses a new process and fresh home.
The operating-system file cache is **not evicted**; this is a warmed-file-cache
experiment, not a cold-start claim. No affinity, priority, power-profile,
foreground-stealing workaround, registry mutation, or global synthetic input
was introduced.

An initial smoke pair is retained separately and **preexcluded from paired
inference**: off was exact foreground while on was visible-background. Both
were behaviorally correct, but their focus states were not equivalent. Two
further alternating pairs were consistently visible-background; the experiment
was then authorized to continue up to 20 pairs in that explicitly weaker state.
Odd pairs run off→on; even pairs on→off (ABBA across adjacent pairs). The driver
stops immediately on any mode failure or state drift rather than adaptively
filtering samples or changing focus behavior.

Reproduction from the repository root, preserving the adapted driver:

```powershell
python -B .temp/native-trace-overhead/adapt.py
pwsh -NoProfile -File .temp/native-trace-overhead/Measure-WindowsScreen.ps1 `
  -ExecutablePath .cache/uia-range-aot/mote.exe -Cases many-1 `
  -Repetitions 1 -AllowLocal -TraceMode off
# Repeat on, inspect exact outcomes/focus, then alternate modes in fresh processes.
python -B .temp/native-trace-overhead/summarize.py
```

The ignored `.temp` adaptation is not a portable shipped harness. To reproduce
after losing these artifacts, make the bounded parameter/preservation/counter
changes listed above to a copy of the linked existing driver; do not modify the
production application to make the benchmark pass. The manifest preserves the
source and adaptation SHA-256 hashes for this run.

## Statistics and decision rules

All paired deltas are on minus adjacent off. Median intervals use order
statistics, not a Gaussian assumption: for 20 independent identically
distributed paired deltas, sorted ranks 6 and 15 cover the population median
with `1 - 2 * sum(C(20,j)/2^20, j=0..5) = 95.8606%` probability. This conditional
single-host interval does not account for cross-machine variability or
uncontrolled correlated background load; CPU's ties/quantization further limit
fine-grained interpretation.

The displayed nearest-rank p95 at n=20 is the nineteenth sorted observation.
It is **exploratory**, not a trustworthy tail guarantee. Indeed, even the
observed maximum is below the population p95 with probability `0.95^20 ≈ 35.85%`
under independent sampling. There is no finite distribution-free 95% upper
confidence limit for p95 from these 20 samples. At least 59 independent samples
are required merely for the maximum to provide a one-sided 95% p95 bound;
that is not a recommendation to manufacture more activity on an uncontrolled
desktop.

## Completed observations

All 40 paired processes completed normal exit 0, a quiet `WM_NULL` control,
unchanged original disk bytes before Save, exact X/original/X saved bytes,
distinct Undo screen shape, and matching Redo screen shape. Every process used
the same executable/source SHA and Canvas geometry. All 40 were consistently
visible-background at focus and immediately before the timed mutation; no state
drift was filtered. The separately retained smoke pair also passed its oracles
but remains excluded for the predeclared foreground mismatch.

All 20 enabled paired sessions have 45 valid JSONL records, one successful
terminal `mote.session`, exactly one successful version-1 source draw interval,
and zero emitted dropped records or JSON parse errors. Trace bytes per process
are 15,421–15,433 (median 15,428). All 20 disabled processes produced no trace
files. This verifies the diagnostic path was actually enabled, not an off/on
label with both paths inert. Terminal/drop checks are not external access to the
in-process `Health.SinkFaulted` property and are not an audit of uninstrumented
operations.

| Comparable workload observation | Off p50 / exploratory p95 | On p50 / exploratory p95 | Paired on−off median | Conditional 95.86% median interval |
| --- | ---: | ---: | ---: | ---: |
| Synthetic `WM_CHAR` acknowledgement | 4.225 / 5.898 ms | 4.560 / 5.641 ms | +0.150 ms | −0.280 to +0.716 ms |
| Launch → bounded source input ready | 374.868 / 430.130 ms | 379.567 / 412.668 ms | −3.052 ms | −19.550 to +13.909 ms |
| First source-verified changed screen capture | 27.248 / 37.368 ms | 23.892 / 41.157 ms | −3.794 ms | −6.352 to +4.300 ms |
| Target CPU, complete open/edit/Save/Undo/Redo/close workload | 453.125 / 562.500 ms | 460.938 / 531.250 ms | +7.813 ms | −31.250 to +62.500 ms |
| Process lifetime, including deliberate waits | 2800.146 / 2923.590 ms | 2793.567 / 2955.055 ms | +3.154 ms | −36.714 to +24.070 ms |

The median of pair differences is not the difference between independent mode
medians. All paired median intervals straddle zero. Target CPU observations are
in 15.625 ms increments here; sub-millisecond CPU conclusions would be false
precision. The external screen observer's largest completion gap per sample is
16.771–23.377 ms, which overwhelms the displayed screen median difference.
Neither a negative screen median nor the smaller on p95 acknowledgement is a
tracing speedup claim.

Enabled-only source draw-return intervals are p50 **12.811 ms**, exploratory p95
**17.748 ms**, range **11.293–17.898 ms**. They help explain the enabled timeline
but have no uninstrumented counterpart. Their clock starts later than external
`WM_CHAR` dispatch, and their draw return precedes the sampled screen endpoint;
do not subtract them to invent input processing or compositor duration.

### Every paired difference (milliseconds, on minus off)

Raw report paths and run IDs remain in `samples.json` and `summary.json`.
No failed/mismatched paired samples were dropped; the following is the entire
predeclared 20-pair series.

| Pair | Mode order | Input ack Δ | Source-ready Δ | Screen observation Δ | Target CPU Δ |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1 | off → on | +0.716 | −7.567 | −7.569 | +15.625 |
| 2 | on → off | −1.303 | +13.909 | +4.300 | +0.000 |
| 3 | off → on | −0.253 | −10.866 | +5.465 | −31.250 |
| 4 | on → off | +1.263 | −2.870 | +5.405 | +78.125 |
| 5 | off → on | +0.064 | +1.597 | −6.352 | +62.500 |
| 6 | on → off | +0.690 | +25.866 | +18.907 | −31.250 |
| 7 | off → on | +0.236 | −3.235 | −6.212 | +109.375 |
| 8 | on → off | −0.280 | +9.701 | −4.632 | −46.875 |
| 9 | off → on | +0.351 | +22.555 | +3.770 | +31.250 |
| 10 | on → off | −1.729 | +4.290 | −3.020 | +78.125 |
| 11 | off → on | −0.136 | −19.550 | −5.649 | −109.375 |
| 12 | on → off | +0.583 | +22.627 | +1.378 | +15.625 |
| 13 | off → on | +1.627 | −30.378 | −4.569 | +62.500 |
| 14 | on → off | +0.970 | +49.805 | −14.093 | +140.625 |
| 15 | off → on | +1.417 | +32.242 | +8.887 | +0.000 |
| 16 | on → off | −1.017 | −27.148 | −25.905 | −15.625 |
| 17 | off → on | −1.088 | −88.019 | −11.093 | −156.250 |
| 18 | on → off | +0.975 | −58.987 | +14.949 | −93.750 |
| 19 | off → on | −0.846 | −7.246 | −6.665 | +46.875 |
| 20 | on → off | +0.058 | −21.836 | +0.238 | −46.875 |

## Interpretation and next discriminating measurement

There is no demonstrated material enabled-tracing bottleneck in this frozen
single-edit plain-text background workload. Therefore this experiment does
**not** justify sampling away important failure boundaries, replacing JSONL,
adding a database, or optimizing instrumentation based on intuition. It also
does not justify claiming tracing is free: the upper median interval still
permits a roughly 0.7 ms acknowledgement penalty in this workload, and the
enabled writer/activities have real unmeasured allocation costs.

The decisive remaining measurement is the **current instrumented binary** on a
disposable, consistently foreground Windows desktop, with the same exact-source
oracles and a bounded sustained-edit workload (then a representative large-file
case). Foreground state must be a declared prerequisite, not repaired through
intrusive focus workarounds. Record whole-process CPU and trace completion;
measure allocation separately if a real CPU/GC penalty appears. A common
external acknowledgement endpoint remains useful; finer draw-to-present claims
require a validated observer with adequate timing resolution, not more samples
from this screen-copy loop. Mac/ARM need their own native-platform experiment.

## External basis

Google's production
[Dapper report](https://research.google/pubs/dapper-a-large-scale-distributed-systems-tracing-infrastructure/)
argues for common instrumentation points and empirically controlled overhead;
its distributed-service sampling results are not a desktop editor latency
budget. Kalibera and Jones's peer-reviewed
[ISMM 2013 study](https://kar.kent.ac.uk/33611/45/p63-kaliber.pdf)
motivates independent repetitions, effect-size uncertainty, and retaining
variation rather than selecting favorable runs. Microsoft's
[`Process.TotalProcessorTime` contract](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.totalprocessortime)
defines the target CPU counter and permits reading it after exit on Windows
when the process handle is available. The existing
[screen-observer documentation](../../benchmarks/NativePaintLatency/README.md)
records the industry capture contracts and peer-reviewed hardware input-to-light
methods explaining why this software endpoint must not be called physical paint.
