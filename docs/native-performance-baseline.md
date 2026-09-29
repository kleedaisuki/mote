# Native performance baseline and evidence boundaries (2026-09-29)

## Status and decision

The repository has a **scheduled/manual, non-gating** benchmark path in `.github/workflows/benchmarks.yml`. It publishes a process-isolated **Native AOT** engine benchmark and the native editor on Windows x64 and macOS arm64, then uploads raw JSONL rather than failing on a timing threshold. [Hosted run `36549790750`](https://github.com/kleedaisuki/mote/actions/runs/36549790750) passed **all four jobs** and uploaded both engine and both startup artifact sets; the environment-specific baseline below is computed from those files. An [earlier run `36549385420`](https://github.com/kleedaisuki/mote/actions/runs/36549385420) uploaded both engine sets, while both startup scripts produced JSON and then the workflow wrapper falsely failed on `$LASTEXITCODE -ne 0` (`Native startup measurement failed ()` in the log), preventing their uploads. The redundant wrapper check was removed before the successful run. None of these numbers is a release performance SLA or a cross-OS speed comparison.

This deliberately measures two different contracts:

| Instrument | What it actually measures | What it does **not** measure |
| --- | --- | --- |
| `tests/Mote.Benchmarks` Native AOT executable | `Document.OpenAsync` wall time after a synthetic file exists; sequential end/start/middle engine edits; optional 1 MiB Markdown analysis; process peak working set | Native GUI projection, parse-to-presentation, actual keystroke latency, first editable frame or paint |
| `tests/Measure-NativeStartup.ps1` with Native AOT `mote --smoke-gui` | Parent-observed process launch through GUI smoke **exit**, sampled working-set lower bound, telemetry initialization/write on vs off | Cold disk-cache miss, open-file-to-editable, physical first paint, IME, edit-to-paint, exact lifetime peak RSS |

The separation matters. A fast engine edit is not proof that the UI remains smooth: [the large-file UI probe](large-file-ui-evaluation.md) found that a standalone rope edit and an attached GUI edit can differ by orders of magnitude. Likewise, `--smoke-gui` emits its ready marker after the native window is shown and immediately closes; timing its entire process lifetime is **not** a first-frame measurement.

## Reproducible methodology

The engine benchmark generates deterministic ASCII files under repository `.temp/benchmarks/`, then excludes generation from `OpenMs`. `--open-edit` uses a line-bearing Markdown-like pattern; `--long-line` uses one unbroken `a` line. The requested size is exactly 1, 10 or 100 MiB of ASCII bytes, equivalent to that many UTF-16 code units before edits. Each invocation is a new process. After open, it records three one-character-location insertions (each inserts seven ASCII characters) **sequentially**: near end first, near start second, middle third. The retained `EditMs` field aliases the original first/end edit for JSON consumers; schema version 2 added named edit fields and `OriginalLengthUtf16`, while version 3 marks unavailable peak/private counters with `null`. Do **not** infer that one position is intrinsically faster from those three timings: order and first-edit initialization are confounded. The weekly/manual hosted workflow repeats each size/shape combination three times, plus three 1 MiB Markdown full-analysis runs. It does not attempt a 100 MiB full semantic parse as a startup/edit proxy.

The benchmark records `PeakWorkingSetBytes` from the running process and after-open/after-edit working set. On Windows these are process working-set counters. On hosted macOS arm64 Native AOT, **all 21** `PeakWorkingSetBytes` and `PrivateBytesAfterEdits` values in run `36549790750` were zero even though `WorkingSetAfterEditsBytes` was positive; zero means those two counters were **unavailable**, not zero memory. A narrow benchmark harness correction now writes `null` for non-positive peak/private counters (schema version 3). The older schema-version-2 macOS artifact retains zeros and must be interpreted as unavailable; the **point-in-time** `WorkingSetAfterEditsBytes` is reported separately below and is not a peak. A row carries OS, architecture, runtime version and logical processor count. JSON serialization is source-generated so the same harness works under Native AOT. Microsoft describes [Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) as ahead-of-time native compilation without runtime JIT; benchmarking a JIT harness would conflate a different runtime regime with the shipped native executable.

The schema-3 correction was validated independently in [hosted run `36550663893`](https://github.com/kleedaisuki/mote/actions/runs/36550663893), which passed all four jobs: all 21 macOS engine rows have `PeakWorkingSetBytes: null` and `PrivateBytesAfterEdits: null`, while all 21 have a positive `WorkingSetAfterEditsBytes`. This confirms the representation of unavailable counters, not a newly established macOS memory peak; the baseline statistics below remain those of run `36549790750`.

The startup script takes one **first invocation** after publish, then ten warm telemetry-off/on pairs in alternating order. "Cold" remains the old JSON field name for compatibility, but `cold_definition` explicitly says the OS file cache was **not evicted**. It launches the GUI-subsystem executable with `Start-Process`, waits for exit, verifies the exact `mote-native-gui-ready` marker, and times from just before process creation through observed exit. During life it polls `PeakWorkingSet64`; a **positive** `observed_peak_working_set_bytes` is a sampled **lower bound**, not an exact lifetime peak. On macOS in run `36549790750`, the property remained zero despite **28–130 polls per process**; all 21 startup samples correctly serialize this as `null` (unavailable), **not zero resident memory**. Earlier run-`36549385420` logs containing zero must likewise be interpreted as unavailable. Child stdout, stderr, temporary `MOTE_HOME`, and opt-in JSONL traces stay under `.temp/benchmarks/`; per-run and aggregate results stay under `.cache/benchmarks/`. The script checks the resolved scratch directory before recursive cleanup. `MOTE_TRACE=1` is applied only to the telemetry-on child; the successful hosted run generated about 2.95 KiB of trace data per platform. That is still a startup-smoke overhead comparison, not tracing overhead while typing.

Hosted runners are fresh VMs and their labels, images, and available CPU/memory can change; [GitHub documents the hosted-runner model](https://docs.github.com/en/actions/reference/runners/github-hosted-runners) and [runner-image labels](https://github.com/actions/runner-images). The workflow pins `windows-2025` / `win-x64` and `macos-26` / `osx-arm64` instead of cross-comparing unlabelled `-latest` changes, but pinning the image generation does **not** pin physical hardware or background load. [Kalibera and Jones, ISMM 2013](https://kar.kent.ac.uk/33611/45/p63-kaliber.pdf) explain why nested repetitions and uncertainty, rather than a lone best run, matter for performance studies. Three engine process repetitions and ten startup pairs are a useful baseline, **not** sufficient for an automatic p95 latency SLA across heterogeneous runners. Keep the uploaded raw samples and compare changes within the same RID/image family and similar workload/commit context.

### Hosted Native AOT baseline: run `36549790750`

All values below come from the uploaded JSONL artifacts of [this successful workflow run](https://github.com/kleedaisuki/mote/actions/runs/36549790750), at commit `d4a2eb754a259f17bb6bb568ac1035399e7b7858`. The Windows engine runner reported Windows 10.0.26100, x64, .NET 10.0.12, and four logical processors; the macOS runner reported macOS 26.6.2, arm64, .NET 10.0.12, and three logical processors. Each engine cell is the median (`p50`) and **nearest-rank `p95`** of three independent processes. With only three runs, `p95` is simply the **maximum observed sample**, not a dependable tail estimate. "Edit" is the first/end engine edit after opening, *not* edit-to-paint.

| Windows engine workload | Size | Open p50 / p95 | First/end edit p50 / p95 | Process peak working set p50 / p95 |
| --- | ---: | ---: | ---: | ---: |
| Line-oriented open/edit | 1 MiB | 4.918 / 5.350 ms | 0.033 / 0.033 ms | 13.98 / 14.68 MiB |
| Line-oriented open/edit | 10 MiB | 33.990 / 34.289 ms | 0.046 / 0.048 ms | 32.45 / 32.50 MiB |
| Line-oriented open/edit | 100 MiB | 357.567 / 373.497 ms | 0.029 / 0.030 ms | 223.16 / 223.16 MiB |
| One long line open/edit | 1 MiB | 7.413 / 8.040 ms | 0.039 / 0.055 ms | 13.96 / 14.01 MiB |
| One long line open/edit | 10 MiB | 32.467 / 34.763 ms | 0.040 / 0.066 ms | 32.43 / 32.44 MiB |
| One long line open/edit | 100 MiB | 337.254 / 341.908 ms | 0.035 / 0.046 ms | 223.16 / 223.22 MiB |

| macOS engine workload | Size | Open p50 / p95 | First/end edit p50 / p95 | **After-edit point-in-time** working set p50 / p95 |
| --- | ---: | ---: | ---: | ---: |
| Line-oriented open/edit | 1 MiB | 2.478 / 2.866 ms | 0.027 / 0.032 ms | 13.70 / 13.72 MiB |
| Line-oriented open/edit | 10 MiB | 21.602 / 22.453 ms | 0.021 / 0.035 ms | 32.23 / 32.23 MiB |
| Line-oriented open/edit | 100 MiB | 196.120 / 204.635 ms | 0.023 / 0.025 ms | 214.59 / 214.59 MiB |
| One long line open/edit | 1 MiB | 2.147 / 2.384 ms | 0.015 / 0.017 ms | 13.70 / 13.70 MiB |
| One long line open/edit | 10 MiB | 21.238 / 21.358 ms | 0.019 / 0.020 ms | 32.23 / 32.23 MiB |
| One long line open/edit | 100 MiB | 186.076 / 188.268 ms | 0.019 / 0.020 ms | 214.59 / 214.61 MiB |

The separate 1 MiB Markdown full-analysis workload took **114.352 / 122.386 ms** p50 / observed maximum on Windows and **108.159 / 109.393 ms** on macOS. Its Windows process peak working set was **55.96 / 55.96 MiB** p50 / maximum. The macOS after-edit point working set was **13.70 / 13.72 MiB**, measured *before* full analysis, so it cannot characterize the analysis memory peak. Open/edit and full-analysis timing are separate measurements; do not add them and call the sum an interactive first-frame latency.

The startup-smoke rows below are from one first invocation and ten alternating warm telemetry-off/on pairs **per platform**. Startup `p95` is again nearest-rank and therefore the maximum of ten warm samples; no cold file-cache eviction was performed. The paired delta is telemetry-on minus its adjacent telemetry-off run, not the difference of the displayed medians.

| Native GUI smoke platform | First invocation | Warm telemetry off p50 / p95 | Warm telemetry on p50 / p95 | Paired on−off median; range | Observed sampled peak working set |
| --- | ---: | ---: | ---: | ---: | --- |
| Windows x64 | 186.0 ms | 74.7 / 83.9 ms | 75.1 / 89.9 ms | −1.3 ms; −14.5 to +15.8 ms | Off 18.8 MiB, on 19.1 MiB median **lower bounds** |
| macOS arm64 | 1571.5 ms | 360.2 / 915.3 ms | 352.9 / 727.0 ms | +6.2 ms; −188.2 to +174.9 ms | Unavailable (`null`) for all 21 samples |

The telemetry pairs change sign on both platforms, so this run **does not establish a reproducible telemetry startup penalty or benefit**. The especially broad macOS first/warm and paired spread is a reason to investigate runner scheduling and GUI lifecycle before making a latency claim, not to select the favorable median. Both telemetry-on runs produced approximately 2.95 KiB of trace output, confirming the path was active. Windows memory bounds are sampled during a short process; macOS returned no usable peak value. Neither table measures GUI first editable frame, paint, or typing responsiveness. The headless 100 MiB engine completion is valuable but does not settle the large-file native presentation cost.

### Local Windows Native AOT sanity snapshot

Environment: Windows 10.0.26200 x64, Intel Core i9-12900H, 32 GiB RAM, .NET SDK 10.0.400. The engine harness was published as `win-x64` Native AOT. Each row below is **one** process, run on a shared development host with warm filesystem caches and concurrent development activity; it is a functionality/performance-order sanity check, not a distribution or uncertainty estimate.

| Workload | Size | `Document.OpenAsync` | First/end edit | Process peak working set |
| --- | ---: | ---: | ---: | ---: |
| Line-oriented open/edit | 1 MiB | 21.3 ms | 0.032 ms | 15.0 MiB |
| One long line open/edit | 1 MiB | 9.3 ms | 0.025 ms | 14.3 MiB |
| Line-oriented open/edit | 10 MiB | 38.5 ms | 0.040 ms | 32.8 MiB |
| One long line open/edit | 10 MiB | 25.4 ms | 0.039 ms | 32.8 MiB |
| Line-oriented open/edit | 100 MiB | 272.3 ms | 0.036 ms | 216.2 MiB |
| One long line open/edit | 100 MiB | 225.0 ms | 0.025 ms | 216.2 MiB |

The Native AOT benchmark build completed without warnings, and all six rows had `SchemaVersion=2` and exact original lengths. Small differences between shapes are not causal evidence: a different file is written, the OS caches it differently, and there is one observation per combination. The 100 MiB result establishes that this *headless engine path* completed on the local host; it says nothing about a GUI loading/rendering 100 MiB at once.

The existing local `mote.exe` at `.cache/final-native-win-x64/mote.exe` was used for a three-pair script sanity run (its SHA-256 is present in `native-startup.jsonl`; it is not the freshly published engine benchmark binary). The first invocation was **95.0 ms**; telemetry-off warm samples were **84.3, 89.1, 104.7 ms**, and telemetry-on samples were **103.5, 107.2, 82.4 ms**. The paired on-minus-off differences were **+19.2, +18.1, −22.3 ms**. A three-pair median difference of +18.1 ms is **not** evidence of a stable telemetry regression, because even the sign changed amid ~20 ms run-to-run movement. Observed peak working sets were roughly 20–23 MiB, with only 2–6 polls per short process; use the uploaded per-run values as bounds, not an exact memory peak. A longer hosted run can test whether any telemetry effect persists.

### Commands and artifacts

```powershell
dotnet publish tests/Mote.Benchmarks/Mote.Benchmarks.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishAot=true -p:DebugType=none `
  -o .temp/benchmarks/engine-win-x64
.temp/benchmarks/engine-win-x64/Mote.Benchmarks.exe 100 --open-edit
.temp/benchmarks/engine-win-x64/Mote.Benchmarks.exe 100 --long-line
./tests/Measure-NativeStartup.ps1 `
  -ExecutablePath .cache/final-native-win-x64/mote.exe `
  -RuntimeIdentifier win-x64 -WarmRuns 3 -CompareTelemetry
```

The engine rows append to `.cache/benchmarks/results.jsonl`; the startup summary and individual samples append to `native-startup.jsonl` and `native-startup-samples.jsonl`. Each scheduled/manual GitHub Actions run uploads the corresponding files as 30-day artifacts. The successful run above establishes an initial environment-specific baseline; retain future Windows and macOS artifacts and compare *same-RID* samples over several runs. A GUI open/edit first-frame baseline requires a native UI marker or automation that observes an actual rendered/editable frame; neither this script nor a headless `--check-runtime` is such a marker. Do not add an arbitrary CI timing gate before that distinction and runner noise are measured.
