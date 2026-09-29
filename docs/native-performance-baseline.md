# Native performance baseline and evidence boundaries (2026-09-29)

## Status and decision

The repository now has a **scheduled/manual, non-gating** benchmark path in `.github/workflows/benchmarks.yml`. It publishes a process-isolated **Native AOT** engine benchmark and the native editor on Windows x64 and macOS arm64, then uploads raw JSONL rather than failing on a timing threshold. The hosted workflow has **not yet run for this revision**; there are no macOS measurements to report. The Windows figures below are inexpensive local sanity evidence, not a release performance claim or a cross-OS comparison.

This deliberately measures two different contracts:

| Instrument | What it actually measures | What it does **not** measure |
| --- | --- | --- |
| `tests/Mote.Benchmarks` Native AOT executable | `Document.OpenAsync` wall time after a synthetic file exists; sequential end/start/middle engine edits; optional 1 MiB Markdown analysis; process peak working set | Native GUI projection, parse-to-presentation, actual keystroke latency, first editable frame or paint |
| `tests/Measure-NativeStartup.ps1` with Native AOT `mote --smoke-gui` | Parent-observed process launch through GUI smoke **exit**, sampled working-set lower bound, telemetry initialization/write on vs off | Cold disk-cache miss, open-file-to-editable, physical first paint, IME, edit-to-paint, exact lifetime peak RSS |

The separation matters. A fast engine edit is not proof that the UI remains smooth: [the large-file UI probe](large-file-ui-evaluation.md) found that a standalone rope edit and an attached GUI edit can differ by orders of magnitude. Likewise, `--smoke-gui` emits its ready marker after the native window is shown and immediately closes; timing its entire process lifetime is **not** a first-frame measurement.

## Reproducible methodology

The engine benchmark generates deterministic ASCII files under repository `.temp/benchmarks/`, then excludes generation from `OpenMs`. `--open-edit` uses a line-bearing Markdown-like pattern; `--long-line` uses one unbroken `a` line. The requested size is exactly 1, 10 or 100 MiB of ASCII bytes, equivalent to that many UTF-16 code units before edits. Each invocation is a new process. After open, it records three one-character-location insertions (each inserts seven ASCII characters) **sequentially**: near end first, near start second, middle third. The retained `EditMs` field aliases the original first/end edit for JSON consumers; `SchemaVersion=2` adds named edit fields and `OriginalLengthUtf16`. Do **not** infer that one position is intrinsically faster from those three timings: order and first-edit initialization are confounded. The weekly/manual hosted workflow repeats each size/shape combination three times, plus three 1 MiB Markdown full-analysis runs. It does not attempt a 100 MiB full semantic parse as a startup/edit proxy.

The benchmark records `PeakWorkingSetBytes` from the running process and after-open/after-edit working set. On Windows these are process working-set counters; macOS's corresponding .NET counters should be interpreted as process resident-memory observations, not an OS-independent memory budget. A row carries OS, architecture, runtime version and logical processor count. JSON serialization is source-generated so the same harness works under Native AOT. Microsoft describes [Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) as ahead-of-time native compilation without runtime JIT; benchmarking a JIT harness would conflate a different runtime regime with the shipped native executable.

The startup script takes one **first invocation** after publish, then ten warm telemetry-off/on pairs in alternating order. "Cold" remains the old JSON field name for compatibility, but `cold_definition` explicitly says the OS file cache was **not evicted**. It launches the GUI-subsystem executable with `Start-Process`, waits for exit, verifies the exact `mote-native-gui-ready` marker, and times from just before process creation through observed exit. During life it polls `PeakWorkingSet64`; because a short process can allocate after the last poll, `observed_peak_working_set_bytes` is a **lower bound**, not an exact peak. Child stdout, stderr, temporary `MOTE_HOME`, and opt-in JSONL traces stay under `.temp/benchmarks/`; per-run and aggregate results stay under `.cache/benchmarks/`. The script checks the resolved scratch directory before recursive cleanup. `MOTE_TRACE=1` is applied only to the telemetry-on child; the local test generated 885 trace bytes after three on-runs, so it did exercise trace writing. That is still a startup-smoke overhead comparison, not tracing overhead while typing.

Hosted runners are fresh VMs and their labels, images, and available CPU/memory can change; [GitHub documents the hosted-runner model](https://docs.github.com/en/actions/reference/runners/github-hosted-runners) and [runner-image labels](https://github.com/actions/runner-images). The workflow pins `windows-2025` / `win-x64` and `macos-26` / `osx-arm64` instead of cross-comparing unlabelled `-latest` changes, but pinning the image generation does **not** pin physical hardware or background load. [Kalibera and Jones, ISMM 2013](https://kar.kent.ac.uk/33611/45/p63-kaliber.pdf) explain why nested repetitions and uncertainty, rather than a lone best run, matter for performance studies. Three engine process repetitions and ten startup pairs are a useful baseline, **not** sufficient for an automatic p95 latency SLA across heterogeneous runners. Keep the uploaded raw samples and compare changes within the same RID/image family and similar workload/commit context.

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

The engine rows append to `.cache/benchmarks/results.jsonl`; the startup summary and individual samples append to `native-startup.jsonl` and `native-startup-samples.jsonl`. Each scheduled/manual GitHub Actions run uploads the corresponding files as 30-day artifacts. To establish a production-relevant baseline, dispatch `Benchmarks` after the workflow lands, retain Windows and macOS artifacts, then compare *same-RID* samples over several runs. A GUI open/edit first-frame baseline requires a native UI marker or automation that observes an actual rendered/editable frame; neither this script nor a headless `--check-runtime` is such a marker. Do not add an arbitrary CI timing gate before that distinction and runner noise are measured.
