# Current causal tracing: a bounded Windows x64 overhead guard

Date: 2026-10-01. Owner: performance experiment only; no production or CI changes.

## Question and frozen artifact

The permanent Save provenance chain and periodic prefix flush materially change
opt-in tracing. The older [frozen-baseline experiment](native-trace-overhead.md)
cannot establish the cost of the new implementation. This experiment compares
tracing **off/on in the same current Native AOT executable**, preserving exact
source and sampled screen-state oracles. It does not compare before/after product
versions or measure production foreground typing.

The measured source is `5ebbdaf726f6bd531282661b95d2e697e18caed8`, including
`b2f4661` causal Save integration and the nonfatal Save-completion containment fix.
Warning-strict publish completed with exit 0:

```powershell
dotnet publish src/Mote.Native/Mote.Native.csproj -c Release -r win-x64 `
  -o .cache/causal-overhead-current/publish -warnaserror `
  -p:TreatWarningsAsErrors=true
```

The published directory contains only `mote.exe`, **7,155,712 bytes**, SHA-256
`b4e07156c7caca1d16820b61cbe5fc060b4db13e87fc072011bc8d145466c7c9`,
last write 2026-10-01 06:09:41.6165373 UTC. An earlier preliminary publish was
never exercised and is not part of the evidence. Build logs, SDK/environment,
source/harness hashes and binary identity are in repository-local
`.cache/causal-overhead-current/manifest.json` and `publish-final.log`.

## Workload, controls and evidence boundaries

The experiment reuses the exact-source
[screen-observer driver](../../benchmarks/NativePaintLatency/Measure-WindowsScreen.ps1)
and its unchanged `WindowsScreenObserver.cs`, `CanvasFixture.cs`, and
`Win32Probe.cs`. Only an ignored `.temp/causal-overhead-current/` copy was adapted:
trace mode, isolated home/trace retention, whole-process CPU/lifetime, explicit
normal-exit/forced-cleanup fields, and removing both explicit foreground-activation
attempts. No document body or screenshot is persisted.

The workload uses one fresh process/home and deterministic 1 MiB CRLF plain text
per sample. Original source SHA-256 is
`5a900aeab7463e7b2fcf7481453882043dee41ca15f8a9bb8fa367e5c8c43e1f`.
After a quiet synthetic `WM_NULL` control, one leading `X` is sent by bounded
cross-process `WM_CHAR`. Disk bytes must remain unchanged until explicit Save.
The exact disk/screen chain is X Save -> original Undo Save -> X Redo Save,
then owned normal close. Three actual native Save requests are therefore
available for provenance validation in every enabled process, with captured
versions 1, 2 and 3.

The host is Windows 10.0.26200, Intel i9-12900H, 20 logical processors,
34,087,665,664 bytes physical RAM, 2560x1440 primary display, target 96 DPI,
1057x988 Canvas client. OS file cache is not evicted; this is a warmed-file-cache
experiment. No affinity, priority, power-profile, input-source, registry, global
input injection, or external-window manipulation was introduced.

| Observation | Interpretation |
| --- | --- |
| `input_ack_ms` | External monotonic synthetic `WM_CHAR` dispatch to synchronous reply; not physical keyboard latency |
| `launch_to_source_ready_ms` | Process creation to bounded nonempty input-island availability, with 20 ms polling; not first complete editable frame |
| `first_changed_capture_ms` | First sampled source-specific changed screen ROI; not compositor presentation or physical photons |
| `process_cpu_ms` | Target user+privileged CPU for the complete open/edit/three-Save/Undo/Redo/draw/writer/close workload; excludes observer CPU |
| `process_lifetime_ms` | Whole process including deliberate settle/control waits, not interactive latency |
| Save-chain evidence | Native callback receipt -> admission -> worker -> exact captured persistence phases -> UI callback/completion, not external key delivery |
| Allocation/peak memory | Not measured; disabled-path allocation tests are not enabled-overhead evidence |

The original five-point ROI ownership guard remains: it cannot prove every
interior pixel belongs to the target. Native draw return and sampled screen
change remain distinct software endpoints, not input-to-light latency.

## Qualification failures and protocol correction (retained, not filtered)

1. The first non-topmost OFF qualification failed **before the timed edit**:
   `negative-control` reported the synthetic Canvas ROI obscured. The exact
   launched child was forcibly reaped and generated source removed. No ON
   counterpart or timing estimate exists. This is a local display-qualification
   failure, not a tracing or product performance result. Raw report:
   `.cache/causal-overhead-current/screen/74799642749a40f180acc90877d3a639/`;
   index `occluded-qualification-index.jsonl`.
2. The parent authorized the existing local-only synthetic topmost operation,
   with no foreground stealing. The reused driver initially still contained
   **two `SetForegroundWindow` attempts**. This violated the no-activation
   bound despite sampled foreground=false. A topmost smoke pair and six indexed
   paired processes ran before the parent identified it; one additional OFF
   process had started and subsequently completed normally without an index row
   after its scheduler was stopped (nine successful attempted-activation reports
   in total); scheduling was then
   stopped by verified owned scheduler identity, allowing the active child to
   close normally. All their reports and the original driver are retained
   under `activation-attempt-*` and `screen/` (the unindexed OFF report is
   `2931816f4bf246ffa2bcdc678cdadf04`), **excluded from inference**.
   A sampled false foreground flag does not prove the earlier calls never
   transiently activated the target. No favorable timing from this interrupted
   protocol is used below.
3. Both explicit activation calls were removed before a new series. The only
   driver window-state adjustment is owned synthetic `MakeSyntheticTopmost`,
   `SetWindowPos(HWND_TOPMOST,...,0x0013)`: `SWP_NOSIZE | SWP_NOMOVE |
   SWP_NOACTIVATE`. Closing the owned process removes its topmost window. No
   external window is activated/minimized/restored. A fresh OFF/ON smoke pair
   passed all oracles and normal exit 0 in the same visible, nonforeground,
   topmost state; it is **preexcluded** from paired inference.

This final state differs from the old non-topmost background experiment. Results
are not pooled with it, nor used as a numerical before/after regression estimate.

## Predeclared paired procedure

The final comparison planned 20 adjacent off/on pairs (40 fresh processes):
odd pairs off->on, even pairs on->off, ABBA across adjacent pairs. The driver
stops scheduling on any failure or foreground-state drift; no adaptive filtering
or focus repair is allowed. All final samples must be visible-background,
no explicit activation calls, owned synthetic topmost, exact-source correct,
normal exit 0, no forced cleanup, and the identical frozen executable.

The temporary driver and repetition script are
`.temp/causal-overhead-current/{Measure-WindowsScreen.ps1,Run-Pairs.ps1}`;
analysis is `summarize.py`. Raw reports/retained traces are
`.cache/causal-overhead-current/screen/<run-id>/`, with `paired-index.jsonl`,
`samples.json`, `summary.json`. Generated fixtures/homes remain under repository
`.temp` and are safely removed after reaping each owned child.

```powershell
# The temporary adapted driver has both SetForegroundWindow calls removed.
pwsh -NoProfile -File .temp/causal-overhead-current/Run-Pairs.ps1 -Smoke
# Inspect qualification before starting a NEW, nonexisting pair index.
pwsh -NoProfile -File .temp/causal-overhead-current/Run-Pairs.ps1 -Pairs 20
python -B .temp/causal-overhead-current/summarize.py
```

The native [causal reader](../../tests/causal_save_trace_reader.py) uses
`MOTE_SAVE_CONTRACT`, **not the weaker recovery-harness contract**. It checks
retained newline-complete JSONL, receipt/terminal kind and ancestry, successful
Engine phases, route-aware move/replace commit coverage, saved-stamp/bookkeeping,
UI start/completion, and consistency with the authoritative captured version.
A terminal success alone cannot certify complete instrumentation.

All paired deltas are on minus adjacent off. Conditional distribution-free
median intervals use sorted paired ranks 6 and 15 at n=20, coverage
`1 - 2*sum(C(20,j)/2^20,j=0..5) = 95.8606%` under independent identical paired
sampling. This single-host conditional interval does not model correlated load
or machine-to-machine variance. Nearest-rank p95 at n=20 is exploratory only;
no distribution-free 95% finite upper p95 bound is available at that sample size.

## Completed observations

**Performance comparison did not qualify.** The fresh no-activation series
stopped after its first pair: OFF was visible-background, but ON was foreground
at both sampled focus/pre-edit observations. Both were behaviorally correct
and exited normally. The observer did not deliberately activate either window;
this state change arose despite removal of its two activation calls. Its cause
was not investigated and is not attributed to tracing. No further pairs were
scheduled; no unequal samples were filtered or repaired.

There are **zero equivalent inferential pairs**. The planned median/CPU delta
and confidence interval are **unavailable**, not zero. The preexcluded smoke
pair is not promoted to a performance result after the series failed.

For inspectability, these are the four no-activation raw observations; the
numbers must **not** be used to calculate an off/on effect:

| Series / mode | Sampled state | Input ack ms | Source-ready ms | Screen observation ms | Whole-workload CPU ms | Process lifetime ms |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Qualification / off | Background | 3.827 | 339.546 | 33.095 | 390.625 | 2733.551 |
| Qualification / on | Background | 4.399 | 340.655 | 23.766 | 296.875 | 2713.681 |
| Attempted pair 1 / off | Background | 4.098 | 374.446 | 20.781 | 406.250 | 2768.150 |
| Attempted pair 1 / on | Foreground | 4.804 | 336.244 | 28.653 | 406.250 | 2732.459 |

All four passed quiet negative controls, unchanged source before Save, exact
X/original/X disk hashes and Undo/Redo screen shapes, owned normal exit 0, and
safe generated-source cleanup. Both disabled processes produced no trace files.
Both enabled processes retained **135 complete valid JSONL rows** (44,622 and
44,616 bytes), normal successful session terminals, and no emitted drop record.

The native contract independently validates **six of six real Save request
chains**, three per enabled process: received Save -> composition settled ->
controller/admission -> worker -> serialized persistence -> local UI completion.
Every chain includes the required Engine phase successes and route-aware commit
coverage, exact captured versions **1/2/3**, correctly linked receipt ancestry,
no conflicting saved version, and no orphan Save-stage records. Reader coverage
is `instrumented_chain_only`, not an absence/durability certificate. There were
no forced exits in these four processes. This is positive current-binary
native provenance/semantic evidence, **not a tracing-overhead result**.

## Decision and next discriminating experiment

The run found no measured tracing bottleneck because it obtained no valid
comparable performance series. It therefore supports neither "tracing is free"
nor "tracing regressed" and justifies no instrumentation optimization or loss of
failure checkpoints. The permanent trace chain was active and source-correct;
its enabled latency/CPU cost remains unresolved. Managed allocations, peak
memory, sustained typing, foreground key/IME behavior, large semantic files,
macOS, and ARM remain outside this guard.

The useful next experiment is the same frozen-source/byte-oracle paired workload
on a **disposable consistently foreground hosted Windows desktop**, with actual
foreground state a declared prerequisite and failures retained. Do not repeatedly
retry this live desktop until favorable pairs appear, forcibly activate another
user window, or infer physical paint from screen-copy observations. A finer Save
latency analysis can use the now-complete captured persistence chain, but needs
a common external off/on endpoint and controlled state; tracing-only spans have
no uninstrumented counterpart. The local display has been released and there
are no active owned experiment processes.

## Methodological basis

### Retained next-step harness (not new runtime measurements)

The next hosted experiment is now represented by
[`Measure-WindowsTracePairs.ps1`](../../benchmarks/NativePaintLatency/TracePairs.md):
same published binary SHA, fresh default homes, fixed 1 MiB source, alternating
off/on order and unchanged exact-source/screen oracles. Both sampled exact
foreground controls are mandatory; unlike the interrupted no-activation local
series above, it uses the original driver's existing target activation attempts.
It stops at the first failure/equivalence mismatch and retains rejected evidence.
Whole-series qualification precedes any paired estimate. Retained on traces
require three native captured-version 1/2/3 complete Save chains; off requires
zero traces. CPU stays null when unavailable. Portable fixtures/parser checks
are not native measurements; **current AppKit monitor cost remains unmeasured**.

Microsoft defines
[`Process.TotalProcessorTime`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.totalprocessortime?view=net-10.0)
as user plus privileged target CPU and permits post-exit retrieval on Windows
with an available process handle. Google's production
[Dapper report](https://research.google/pubs/dapper-a-large-scale-distributed-systems-tracing-infrastructure/)
motivates shared causal instrumentation and empirical overhead control; its
service-scale sampling is not a desktop latency budget. Kalibera and Jones's
peer-reviewed [ISMM study](https://kar.kent.ac.uk/33611/) motivates repeated
independent executions and effect-size uncertainty. The peer-reviewed
[2023 input-to-light study](https://epub.uni-regensburg.de/55003/), linked in our
[screen-observer notes](../../benchmarks/NativePaintLatency/README.md), uses
hardware input/photodiode evidence and reinforces why these software endpoints
cannot be called physical paint. These sources constrain interpretation; none
supplies measurements for mote.
