# Ordinary Native AOT startup and source-readiness probe

## Question and boundary

Does the **ordinary** `mote <file>` Continuous product path become source-interactive promptly for a 1 MiB and then a 100 MiB Markdown-shaped file? This benchmark is separate from the older `--smoke-gui` lifecycle benchmark in `docs/native-performance-baseline.md` and from the external BitBlt/WGC presentation experiments. `--smoke-gui` closes immediately and cannot measure file-open readiness; a screen capture includes observer/compositor costs and cannot substitute for the editor's synchronous input acknowledgement.

`Measure-WindowsOrdinary.ps1` launches a fresh one-file, Windows x64 Native AOT process per sample with the ordinary positional filename argument (no `--legacy-page`, `--canvas-experimental`, or smoke flag). It externally observes, with one parent monotonic stopwatch:

1. `Process.Start` return and first **visible** top-level HWND owned by that process;
2. window title naming the file;
3. a bounded RichEdit source input island under `MoteInteractiveCanvas` containing the exact fixture prefix;
4. `EM_SETSEL(1,1)` / `EM_GETSEL` acknowledgement;
5. `WM_CHAR('X')` -> dirty-title acknowledgement, then native Save menu -> exact `SHA-256(X + original bytes)`.

These are distinct milestones. A visible HWND or title does not prove the source is bound. Source binding and successful message interaction do not prove a painted frame, OS-delivered physical keystroke, IME, or complete background semantic analysis. `WM_CHAR` is sent to the target control by `SendMessageTimeoutW`; it deliberately bypasses foreground keyboard dispatch. The script records the foreground PID at source readiness. If it belongs to another process, the timing may characterize launch/source binding and direct control calls, **not user-perceived foreground responsiveness**. Even with target foreground, the script does not assert native keyboard first-responder or pixel presentation. The independent WGC experiment must retain its own label.

The Markdown-ish fixture consists of repeated exact 1024-byte ASCII records starting with `STARTUP-MARKER # note`, at exactly 1 or 100 MiB. A **unique pathname per process** avoids an external delete/recreate of a previously opened file; all bytes are identical. Generation, initial expected SHA streaming, and Native AOT publishing happen **outside** each launch timer. Each fixture was just written before its invocation and the OS file cache was **not evicted**, so neither first nor subsequent samples are disk-cold. Later samples are fresh processes with the same `MOTE_HOME`; executable/configuration and OS caches may be warmer, while each pathname is new. The report distinguishes these classes rather than asserting a controlled cache hit. Tracing is disabled. The fixture, home, and scratch files stay below root `.temp/native-startup/`; numeric JSONL results stay below `.cache/native-startup/`; no file content is written to the report. The script checks reparse-point ancestors before recursive scratch cleanup.

Memory and CPU observations are child-process counters at source readiness and after exact Save; `PeakWorkingSet64` is retained if positive. A point working set is not lifetime peak; `PeakVirtualMemorySize64` is address space, not committed resident memory. CPU time is accumulated across all process threads and not latency. Parent-side polling and Windows timer/scheduling jitter are included in wall intervals. The maximum polling granularity is approximately 5 ms plus PowerShell overhead, so differences below that scale are not a reliable optimization signal. There is no automatic latency threshold or regression gate.

## Reproduce

Run in an unobstructed interactive Windows desktop, without another GUI benchmark. Publish current source into a repo-local directory with a strict one-file inventory:

```powershell
dotnet publish src/Mote.Native/Mote.Native.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishAot=true -p:DebugType=none `
  -p:ContinuousIntegrationBuild=true -warnaserror `
  -o .cache/native-startup/publish-win-x64
Get-ChildItem .cache/native-startup/publish-win-x64 -Force
./benchmarks/NativeStartup/Test-Inventory.ps1 `
  -ExecutablePath .cache/native-startup/publish-win-x64/mote.exe -CheckLaunchFailure
./benchmarks/NativeStartup/Measure-WindowsOrdinary.ps1 `
  -ExecutablePath .cache/native-startup/publish-win-x64/mote.exe `
  -SizeMiB 1 -Runs 5 -ReadinessOnly
./benchmarks/NativeStartup/Measure-WindowsOrdinary.ps1 `
  -ExecutablePath .cache/native-startup/publish-win-x64/mote.exe `
  -SizeMiB 100 -Runs 3 -ReadinessOnly
# Omit -ReadinessOnly to add direct WM_CHAR, dirty title, and exact Save.
# A failing Save preserves its synthetic fixture and home only with
# -KeepFailureArtifacts; -DiagnosticTrace changes timing and is diagnostic only.
```

The script requires the publish directory's **entire `Get-ChildItem -Force` inventory** to consist of exactly one real, non-reparse-point file named `mote.exe`; hidden/system sidecars and subdirectories fail preflight. The focused `Test-Inventory.ps1` validates one-file acceptance, hidden-sidecar rejection, nested-directory rejection, and **zero new `.temp/native-startup/<GUID>` scratch directories** after inventory-only calls; it can optionally check a malformed executable's failed JSONL row without a GUI launch. The benchmark records the executable SHA-256 and Git HEAD in every row and fails if source island size exceeds 16,384 characters. The default full mode additionally requires exact Save SHA. `-ReadinessOnly` deliberately stops after native source selection to isolate startup from an independently observed intermittent Windows `File.Replace` failure described below. A single process/small sample proves **capability**, not p95; compare repeated raw samples only within the same binary, fixture, and host conditions. Avoid cross-comparing hosted VMs or inferring a cold-start SLA from a cache-uncontrolled local run.

## Local evidence: 2026-09-30 Windows x64

The current-source Native AOT publish completed at **2026-09-30 09:55:47 UTC** with `-warnaserror` and a strict inventory of **one** `mote.exe` (6,175,232 bytes; SHA-256 `73cd0392852bd87ce43fa6085a9ba23e31178db55d1ceb247fd2be4515ef578b`). The source checkout HEAD then was `416a381`; `src/Mote.Native/**` had no uncommitted changes in the status observed immediately before publish. Later document and preview-development edits changed HEAD and working-tree source, but **not this executable**; every row carries its exact binary SHA. Host: Windows 10.0.26200, Intel i9-12900H / 20 logical processors, 31.75 GiB physical RAM, .NET SDK 10.0.400. The WGC observer agent explicitly released the local GUI slot before target runs and was notified after all targets were reaped.

The **selected latency batch** consists of the eight JSONL rows in `.cache/native-startup/ordinary-windows.jsonl` at UTC **10:21:53–10:22:08**, with `measurement_mode=source-readiness-and-selection`, `diagnostic_trace=false`, this exact SHA, and unique synthetic pathnames. Do not use a tail-row selector: later inventory/launch-failure tests may append other rows. All **5/5 1 MiB** and **3/3 100 MiB** selected samples passed: correct source prefix, bounded 21-character RichEdit island, and native selection acknowledgement. The OS foreground PID was `28636` on every row, **not the editor PID**. Thus no timing here proves focused keyboard responsiveness or first visible pixel. The uncorrected earlier 10:06 batch is retained as functional evidence only: a Git subprocess ran inside its stopwatch before window polling, contaminating its latency values. Do **not** aggregate its latency with the selected batch.

Values are **observed minimum / median / maximum** within one local batch. `n=5` or `n=3` does not estimate a dependable p95; no confidence interval is claimed.

| Milestone or resource | 1 MiB, n=5 | 100 MiB, n=3 | Measurement boundary |
| --- | ---: | ---: | --- |
| Parent launch → `Process.Start` return | 7.8 / 10.0 / 11.5 ms | 7.7 / 11.6 / 12.4 ms | All metadata and fixture generation are outside timer |
| Parent launch → visible HWND | 307.0 / 329.6 / 364.8 ms | 299.4 / 307.5 / 313.5 ms | Visible process-owned top-level HWND, **not paint** |
| Parent launch → filename title | 367.4 / 379.5 / 409.4 ms | 587.0 / 595.2 / 630.2 ms | Caption names the opened file, not full parse completion |
| Parent launch → exact source prefix bound | 368.0 / 386.3 / 418.4 ms | 590.6 / 600.4 / 643.8 ms | Native input island contains fixture prefix |
| Visible HWND → source bound | 46.4 / 53.7 / 76.9 ms | 291.2 / 292.9 / 330.3 ms | Pairwise difference within each run |
| Source bound → selection acknowledgement | 13.1 / 17.4 / 35.7 ms | 17.2 / 18.9 / 29.1 ms | `EM_SETSEL` and `EM_GETSEL`; includes PowerShell/Win32 dispatch |
| Working set at source bound | 84.4 / 84.6 / 84.8 MiB | 286.8 / 286.9 / 287.1 MiB | Point resident set, not peak |
| Child peak working set through selection | 88.0 / 88.2 / 88.4 MiB | 290.4 / 290.5 / 290.6 MiB | Windows process counter, not allocation or commit |

The observed median **window→source** interval is ~239 ms longer at 100 MiB; launch→visible-HWND medians differ by only ~22 ms in the other direction. This local pattern locates most size-dependent source-readiness work after window creation, but does **not** identify whether file I/O, engine construction, Markdown policy work, or GUI projection dominates. Child accumulated CPU at source readiness was 328.1–375.0 ms for 1 MiB versus 578.1–593.8 ms for 100 MiB. The ~202 MiB additional resident set is material, but cannot be apportioned among engine text ownership, view, native control, and runtime from these counters. No production optimization is justified without a phase profile and a foreground/presentation-capable workflow.

### Functional edit/Save evidence and negative result

Before the stopwatch correction, a unique-path full workflow passed **5/5 1 MiB** and **3/3 100 MiB** source/selection/direct `WM_CHAR`/dirty/exact-Save checks. The saved SHA-256 values were `2d12a1cbc6df198198d7b6de8c38a3e3f3bd8ffaf3bb73025effa9026c1d75f0` and `24e2689e1073ff41755c2e80324b6f5b6df35394161293145edbfb3a75decc85` respectively. This proves those *particular fresh processes* could edit and save; it does **not** prove reliable Save. Their latency rows remain excluded.

The corrected-clock, no-trace 1 MiB full workflow passed sample 0 but **failed sample 1**: the native editor was source-bound and dirty, yet after 30 seconds the original file length and dirty title remained. A later `-DiagnosticTrace -KeepFailureArtifacts` 1 MiB batch passed four exact Saves, then failed its fifth. The failure had a responsive main HWND (`WM_NULL` under 3 seconds) and a modal `#32770` dialog whose static text was `Save failed; the original file was retained. 无法删除要被替换的文件。` (Windows reports inability to delete the file being replaced). The original synthetic bytes remained unchanged. The failing fixture and empty-on-killed-child trace files are retained under `.temp/native-startup/22cba43cb823445e9fb696425a9fadc3/`; the JSONL row is UTC `10:17:47`. `MOTE_TRACE=1` did not flush useful trace data before the child was killed, so those rows are **diagnostic**, not a tracing-on performance comparison.

The error message points to the Windows `File.Replace` call in `Document.CommitTempAsync` after writing, flushing, fingerprinting, and verifying the original target. It does **not** identify whether the denial came from an external scanner/handle, filesystem behavior, or a product-held handle; this requires a controlled handle/Win32-error capture. An earlier same-path rapid delete/recreate protocol also had **two Save failures among seven attempted**; unique-path files did **not** eliminate the failure, disproving the initial fixture-replacement explanation. The separate previously published, committed strict one-file binary `.cache/preview-name-win-x64/mote.exe` (SHA-256 `b532e5cee1a1bb21facffc561479d0fa858f4d723f3daf058ce7075165d70128`) passed **5/5** unique-path 1 MiB full workflows at UTC `10:20:06–10:20:08`. This small unequal failure count does **not** establish a regression between binaries or rule out a common intermittent Windows condition. Do not add a blind Save retry: preserve the existing target-content recheck and diagnose the exact sharing/delete error first. The separate [Windows atomic-save investigation](../../docs/windows-atomic-save-investigation.md) records the current Win32-error inference and controlled sharing probes.

An initial `WM_GETTEXT` probe falsely timed out because its `StringBuilder` P/Invoke marshaled as ANSI while explicitly calling `SendMessageTimeoutW`; adding `CharSet.Unicode` corrected this external harness defect. Another harness review found Git HEAD lookup inside the stopwatch; it was moved before `Process.Start`, and only subsequent readiness-only rows are used for the latency table. Both corrections are recorded so future comparisons do not rediscover these misleading failures.

### Decision and next discriminating test

The strict one-file ordinary path reaches a bounded native source and acknowledges selection at both sizes under this local fixture; selected rows do **not** prove a usable foreground editor or first paint. Full-workflow success exists, but intermittent Windows replace denial blocks a reliable Save acceptance claim on this host. The next discriminating **correctness** test is to capture the Win32 `File.Replace` failure code and concurrent file-handle owners, then compare the same exact binary/fixture on another Windows machine or hosted runner while preserving target-fingerprint safeguards. The next **performance** test, after Save is understood, is target-foreground first-responder plus independent frame observation and opt-in phases around file read, engine open, and projection. Only then should enough same-RID process repetitions be collected for tails. The already documented [engine versus GUI separation](../../docs/native-performance-baseline.md) and [large-file visual-control counterexample](../../docs/large-file-ui-evaluation.md) explain why source readiness, direct input acknowledgement, Save, and presented pixels remain separate contracts.
