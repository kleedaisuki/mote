# Windows compositor-frame measurement for ordinary Continuous mote

Status: **design plus an inconclusive local WGC edit capability probe** (2026-09-30). This note deliberately does
not promote the existing GDI screen-observer results into paint or present
latency. It targets the ordinary `mote <file>` Native AOT path, not the former
`--canvas-experimental` switch or `--legacy-page` rollback path. No production
instrumentation or CI gate is implied by this design.

## The question and the clocks

The user-visible question is: after launching a 1 or 100 MiB document, and
after an edit in that same editor, when is **the corresponding source image**
first present in the Windows desktop image? This is not the time the input
callback returns, the semantic-publication trace ends, a `WM_PAINT` handler
returns, or a screenshot copy completes. A physical input-to-photon claim
would additionally require a calibrated input trigger and display photodiode.

| Mark | Meaning | Mechanism | Claim allowed |
| --- | --- | --- | --- |
| `t_launch` | Before ordinary process creation | external QPC | launch-to-frame start, includes process creation |
| `t_dispatch`, `t_ack` | Before bounded `WM_CHAR` send and its return | external QPC | synthetic-message/edit-callback interval; not physical keypress |
| `t_draw_return` | Optional *future* source-stamped successful `EndPaint` mark | process QPC, separate opt-in channel | CPU draw completion only |
| `t_wgc` | Compositor rendered a captured exact-HWND frame | WGC `SystemRelativeTime` | first observed **window-capture compositor frame** containing source state |
| `t_window_desktop` | First captured desktop update with the exact mote window footprint | prelaunch DXGI plus known HWND geometry | first visible window shell, even if text is blank; attribution is weaker than a glyph match |
| `t_desktop` | Last update of a desktop image | DXGI Desktop Duplication `LastPresentTime` | first observed **desktop-image update** containing source state, only with no coalesced/missed frames |
| photon | Panel emitted light | hardware photodiode | physical display latency, **not** available here |

Microsoft defines WGC `SystemRelativeTime` as the QPC time at which the
compositor rendered the frame, and DXGI `LastPresentTime` as the QPC timestamp
of the last desktop-image update. These are OS timestamps **stored with a
frame**, independent of how long an observer later spends copying its pixels.
They are different endpoints: WGC can capture an obscured window that never
became visible on the desktop; Desktop Duplication observes the desktop but
cannot establish physical scan-out. All time deltas use the same boot-scoped
QPC clock and `QueryPerformanceFrequency`; wall-clock time and callback arrival
time are reported separately. Microsoft's QPC guidance says cross-process
readings are generally consistent, with ±1 tick ordering ambiguity across
threads.

## Two complementary observers, not a GDI replacement in disguise

**Edit path: exact-window WGC.** Use
`IGraphicsCaptureItemInterop::CreateForWindow` on the verified exact-PID
`MoteNativeEditorWindow`, with `Direct3D11CaptureFramePool.CreateFreeThreaded`
and two or three buffers. Attach and prime the capture **before** the edit.
For every frame, save its `SystemRelativeTime` immediately, copy only a small
first-row synthetic-glyph rectangle to a reusable D3D11 staging texture, and
store a bounded in-memory signature. Callback arrival/readback QPCs remain
diagnostics, never the presented endpoint. Do not disable the system capture
border to evade consent. If WGC or a real D3D device is unavailable on a hosted
runner, report `capture_unsupported`, not a GDI-derived substitute.

**Startup and visible desktop path: prelaunch DXGI Desktop Duplication.** An
exact-HWND WGC capture can only start *after* its HWND exists; thus the first
WGC frame might not be the first frame mote displayed. Prime Desktop
Duplication on the target monitor **before** launching mote, with a finite
`AcquireNextFrame` timeout and an already running acquisition loop. Store
`LastPresentTime`, `AccumulatedFrames`, and a bounded ROI signature for each
desktop-image update; skip pointer-only frames (`LastPresentTime == 0`). After
the HWND appears, determine its canvas first-row rectangle in physical pixels
and correlate the prelaunch frame ring with that rectangle. Until the HWND
geometry is known, a small ring of in-memory *desktop frames* might be needed;
this is only acceptable on the disposable hosted desktop, capped by frame
count/bytes and immediately discarded after geometry is resolved. An
alternative is an out-of-context WinEvent hook installed before launch to
discover the HWND early, but that hook is asynchronous and must not be assumed
race-free. If prelaunch frames cannot be retained safely, the startup result
is `first_frame_unobservable` rather than a fabricated latency.

Desktop Duplication requires the D3D device to match the selected adapter and
output. Handle access loss, desktop/mode switches, disconnected sessions and
timeouts explicitly; always `ReleaseFrame`. `AccumulatedFrames > 1` means
updates were combined while the observer was processing a previous image.
Such a record is a **late upper bound**, not the first-present event, and a
strict first-frame result becomes `coalesced_inconclusive`. A nonzero desktop
timestamp by itself is not proof mote caused that update: the source-specific
ROI oracle below is mandatory.

One optional corroboration is PresentMon's ETW frame-event analysis, which
recognizes GDI composition paths. It may attribute a desktop composition to
DWM rather than mote, and it does not identify the text revision contained in
the pixels. Therefore it is a cross-check of timing/present mode, not the
primary source-state oracle. `DwmGetCompositionTimingInfo` similarly lacks
source-revision identity.

## Source-state oracle and bounded run protocol

Reuse the existing synthetic file scheme: exact 1 and 100 MiB CRLF files with
a distinctive first-row glyph sentinel, fresh per-run `MOTE_HOME`, tracing off,
and a verified Native AOT executable SHA. The capture rectangle is fixed in
canvas-client coordinates only after resolving HWND, DPI, monitor and client
mapping. It must include the inserted first glyph and shifted neighboring
glyphs, while excluding the caret column where possible. Keep all source bytes
and pixel signatures inside the benchmark process; persist numeric outcomes
and hashes only, never a screenshot, frame, document body or user desktop.
DXGI desktop capture is **hosted-disposable-desktop only** because the API
temporarily sees the whole screen; local opt-in may use exact-window WGC only.

1. Record hardware/OS/display/DPI/refresh, monitor/adapter, AOT SHA and source
   fixture hash. Prime Desktop Duplication before ordinary `mote <path>` launch.
   Record QPC just before `CreateProcess`; keep the usual unsuspended launch.
   A `CREATE_SUSPENDED`/resume variant may test race sensitivity, but its
   resume-to-frame time is **not** equivalent to normal launch-to-frame and
   must be named separately.
2. Locate the exact-PID top-level and canvas HWND. Require foreground,
   unobstructed desktop ROI and unchanged display geometry on hosted runs.
   From retained frames, separately classify the first visible mote **window
   footprint** (which may be blank) and the first initial-A source glyph.
   Window-footprint attribution requires the later verified exact HWND rect,
   a prelaunch desktop baseline, distinctive shell pixels, and no other
   hosted window transition in that region; otherwise omit that weaker mark.
   A frame with A pixels and no prior coalescing can identify the first
   captured source-bearing desktop update. If the first A frame could have
   been missed, record only a late bound or `first_frame_unobservable`.
3. Attach exact-window WGC, drain its pool, and establish a settled A signature.
   Send bounded `WM_NULL`; require no B-source signature. Send one bounded
   `WM_CHAR` with `t_dispatch` recorded before send and `t_ack` after return.
   Collect successive WGC compositor frames without performing slow disk I/O
   in callbacks. Simultaneously collect DXGI desktop frames when available.
4. Construct a *stable B reference after the timed window*: X Save, Undo Save
   to exact original bytes, Redo Save to exact X bytes. Require baseline A and
   edited B glyph signatures to be distinct and reversible in captured frames.
   Search the retained timed frame signatures for the **earliest B match**.
   Do not equate the first arbitrary visual change with an edited frame: caret,
   focus, a transient blank, or an unrelated window can change pixels.
5. Mark the result strict only when B matching is source-specific, the target
   remained foreground/unobstructed for desktop results, there was no desktop
   accumulation or observer buffer drop before B, and exact file/source
   oracles passed. Otherwise emit a bounded/inconclusive status and reason.
   A WGC B match can support a compositor-window result even if desktop
   visibility fails; never silently relabel it as desktop present.
6. Reap the child, restore no global settings, and delete only verified
   repository `.temp` fixture paths. Reuse a fixture per size safely or delete
   each after a run to avoid the earlier multi-GiB accumulation. JSONL outputs
   live under `.cache` and contain no pixel arrays.

The edit duration of interest is `t_wgc(B) - t_dispatch` and, where valid,
`t_desktop(B) - t_dispatch`. The startup durations are
`t_window_desktop - t_launch` (first shell, if attributable) and
`t_desktop(first A) - t_launch` (first source-bearing frame). A shell appearing
quickly but remaining blank for a long time must not be reported as fast
document opening. Record `t_ack - t_dispatch`, callback-arrival
lag, GPU ROI-copy/readback duration, and `AccumulatedFrames` as separate
diagnostics. Using a pre-send timestamp gives a conservative synthetic-input
interval; it is not a keyboard-event timestamp. Report native display refresh
period alongside durations because one missed refresh can dominate a short
edit.

## Falsification and calibration before performance claims

* **Capability gate:** prove WGC and/or DXGI works on the actual hosted runner
  and published AOT binary; a runner without an interactive DWM session is
  unsupported, not a zero-latency success. Log HRESULT/failure class only.
* **Clock contract:** sample QPC and WGC/DXGI timestamps on a known animated
  synthetic window; verify monotonicity and plausible ordering at the same
  frequency. Do not convert `TimeSpan` to QPC counts with floating-point
  assumptions about a fixed 10 MHz counter; use checked rational conversion.
* **Source negative controls:** `WM_NULL`, a changed unrelated hosted test
  window, a transient blank, and Undo A must not be accepted as B. Redo B must
  match. A deliberately delayed synthetic draw should shift the OS frame
  timestamp by approximately that delay; delaying CPU ROI readback alone
  should shift callback completion but **not** the frame's metadata timestamp,
  until frame accumulation begins.
* **Observer perturbation:** compare mote CPU/working set and input-ack latency
  with observer disabled, WGC alone and DXGI alone. A capture path that changes
  the workload materially is diagnostic rather than a release gate. Observe
  queue drops, `AccumulatedFrames`, D3D device reset and display changes.
* **Distribution:** use fresh process instances and randomized/interleaved
  1/100 MiB order; distinguish cold/warm OS page cache and first versus later
  edits. Record every run and inconclusive reason. Quantiles (especially p95)
  require enough independent runs and uncertainty, not two or three samples.

The prior hosted screen observer's ~31 ms first changed-capture result is **not**
evidence of ~31 ms product paint: the matching `BitBlt` itself occupied
~30 ms. The ABBA smaller-rectangle control did not remove that cost. This
design eliminates that specific timestamp conflation by reading OS frame
metadata, though it may expose new frame-drop or capture-perturbation limits.

## Release-gate interpretation

For the current release, the existing GDI observation can assert only that a
verified edited image was eventually sampled, not a first-present SLA. Do not
gate ordinary Continuous on an edit-to-present threshold until the exact
ordinary route, 1/100 MiB cases, source-state oracle, capture capability and
drop-free frame sequence are demonstrated on representative Windows hosts.
Even then call the metrics **synthetic message-to-compositor frame** and
**synthetic message-to-desktop-image update**, not physical input-to-photon.
Independent photodiode work is required for the latter claim. On macOS, an
analogous WindowServer/ScreenCaptureKit frame timestamp needs its own
platform validation; this Windows design is not a cross-platform measurement.

## Implemented WGC edit capability and local evidence (2026-09-30)

`WgcEditObserver.cpp` is a **benchmark-only** C++/WinRT process. It enforces
exact mote top-level/canvas/input HWND class and PID ownership, uses PMv2 DPI
coordinates plus DWM extended frame bounds to crop a 220×24 glyph region,
creates a hardware D3D11/WGC exact-window capture session, and emits only
QPC/WGC timestamps, integer pixel-change counts and 64-bit synthetic-ROI
signatures. Captured pixel arrays live in a 256-frame bounded memory vector;
the **parent PowerShell harness** imposes a 20-second `WaitForExit` deadline
on each helper process and never persists an image.
`Build-WgcObserver.ps1` builds under repository `.cache` using the installed
Windows SDK C++/WinRT headers and MinGW-w64 g++. `Measure-WindowsWgc.ps1`
generates exact 1 or 100 MiB synthetic source under repository `.temp`,
launches **ordinary** `mote <file>` Native AOT, and checks:

* bounded `WM_NULL` quiet control and one bounded `WM_CHAR` input;
* unchanged original bytes before Save;
* exact full-source X Save → Undo original Save → Redo X Save;
* initial/Undo A signatures equal, timed edited B/Redo B signatures equal,
  A/B distinct, at least 128 changed glyph pixels, and stable nonblank A,
  Undo A and B glyph ink measured against the modal ROI background color;
* matching ROI **x/y offsets** across the separate timed, Undo and Redo WGC
  sessions; full DWM/canvas geometry is checked within each session, not
  compared across sessions; target child reaped, and safe
  `.temp` cleanup. Native target PID/class, input focus, foreground,
  visibility and DWM/canvas geometry are independently checked immediately
  before `WM_CHAR`, at every frame callback, and after the timed candidate
  interval. The reporting oracle rejects **any invalid sampled frame**, even
  if later frames recover; it checks baseline, quiet-control and all timed
  samples rather than only the selected B frame. This is *sampled* continuity,
  not proof of uninterrupted state between callbacks. Capture errors/overflow and timestamp-order anomalies are
  reported as inconclusive, never silently replaced with GDI timing.

The state mask is deliberately inspectable: bit 1 = exact HWND class/PID and
parentage, bit 2 = visible/not minimized, bit 4 = native-input focus, bit 8 =
exact foreground HWND, and bit 16 = unchanged physical DWM/canvas geometry.
`31` is required for a foreground result. A stable local `23` is evidence of
valid identity/focus/geometry but **not** visible desktop foreground.

Reproduction on a **synthetic file only**:

```powershell
./benchmarks/NativePaintLatency/Build-WgcObserver.ps1
./benchmarks/NativePaintLatency/Measure-WindowsWgc.ps1 `
  -ExecutablePath .cache/preview-name-win-x64/mote.exe `
  -Cases many-1 -Repetitions 1 -AllowLocal
```

The helper is a development measurement tool, **not** a mote runtime sidecar;
the product's strict one-file Native AOT delivery is unchanged. Automatic
runs are restricted to a disposable GitHub-hosted Windows desktop; local runs
require explicit `-AllowLocal`. The JSONL is under
`.cache/benchmarks/native-wgc-latency/<run-id>/`; it contains no screenshot,
pixel array, document body, user path or telemetry from ordinary usage.

The local first proof used strict win-x64 AOT SHA-256
`B532E5CEE1A1BB21FACFFC561479D0FA858F4D723F3DAF058CE7075165D70128`
(the branch's Preview-Name-enhanced ordinary Continuous build), Windows 11
10.0.26200, Intel i9-12900H and an NVIDIA RTX 3070 Ti Laptop GPU listed by
WMI. The listed GPU is **not** proof which adapter created the WGC device.
The first source-verified run is retained at
`.cache/benchmarks/native-wgc-latency/f28b91c0009543d9933def2d104fe542/`:
the A/B glyphs differed in 2,321 ROI pixels, all four byte/source oracles
passed, control changed 0 pixels, and capture reported no vector overflow or
processing error. Its raw WGC-metadata-minus-dispatch difference was
30.7384 ms and input acknowledgment 4.4808 ms. A second source-verified run
at `.cache/benchmarks/native-wgc-latency/a002d9c3e1724b998e7517fbcd8d6986/`
again changed 2,321 glyph pixels and passed the same oracles, with raw
metadata-minus-dispatch 40.198 ms, input acknowledgment 12.1874 ms, and
observer-side ROI processing 0.4037 ms for the first B frame. These are
**diagnostic observations, not latency estimates or a distribution**.

Both local runs were *not foreground*, so exact-window WGC can describe its
capture composition but not a visible desktop presentation. More importantly,
in the latter run the frame's `SystemRelativeTime` was **0.8281 ms later**
than the callback-arrival QPC sampled before reading that frame; the earlier
run also exhibited a future metadata timestamp exceeding 1 ms. That ordering
conflicts with treating the two values as straightforward same-origin
render-before-arrival events. The current harness flags any future difference
above 1 µs and leaves `first_observed_b_metadata_delta_ms` null when it or
foreground validation fails. The old stored JSONL used a looser threshold;
its `clock_order_anomaly=false` in the second run must be reinterpreted as
**anomaly true** under the corrected contract. No p95 or mote rendering
bottleneck follows from two such runs.

The failure sequence was informative: a first edit attempt without verified
native-input focus left the source and WGC ROI unchanged; another had
misaligned ROI coordinates until the helper became PMv2 DPI-aware. These
negative results motivated explicit input-focus and cross-session ROI
contracts, rather than permissive pixel matching. WGC exposes no reliable
per-frame loss count; the helper's `sample_vector_overflow=0` only proves its own
fixed vector did not fill. Callback-arrival gaps and total observer ROI
processing duration are recorded, but a long idle gap does not itself prove
loss, nor does a short gap exclude it. Software WARP fallback is diagnostic
only and cannot authorize a hardware latency claim. Thus even a future valid record should be named
**first observed source-B WGC frame**, not guaranteed first compositor frame.

One **post-review contract validation** (not a distribution or latency
benchmark) is stored at
`.cache/benchmarks/native-wgc-latency/6f056981c37f4840ac20c0bb4f7caf46/`.
On the same strict AOT binary, exact source-state oracles and all three
source-ROI signatures passed; A and Undo A each had 1,583 modal-background
contrasting glyph pixels, B and Redo B each 1,590, with 2,321 A→B changed
pixels. Target-state flags were 23 at the **three persisted marks** (before
edit, first B callback and after the candidate interval) versus 31 required:
identity, visibility, native-input focus and geometry were valid at those
marks, but the local target never became foreground. This artifact predates
the all-sampled-frame reporting gate; it must not be cited as proof of every
intermediate callback's state. The pure no-GUI
`Test-WgcStateOracle.ps1` verifies that an intermediate 31→23→31 recovery is
rejected, whereas all-31 sampled frames pass and stable 23 stays ineligible.
Quiet control changed zero pixels, observer-side maximum
per-frame ROI processing was 0.5368 ms, and no local-vector overflow or
callback error occurred. The 540.6613 ms maximum inter-callback gap occurred
across sparse updates and is **not** evidence of a lost or slow product frame.
The raw WGC-metadata-minus-dispatch number was 24.5638 ms and the frame
metadata again lay 0.7665 ms **after** callback arrival. Thus the result
remains `source_verified_but_timing_inconclusive`; reportable latency is null.

### Independent self-painted clock check

`WgcClockProbe.cpp` removes mote from the causal chain entirely. It creates
its **own** 320×240 solid-color Win32 window, synchronously completes each
red/blue `WM_PAINT` before proceeding, and uses exact-HWND WGC to read only its
center pixel. Numeric QPC brackets and frame metadata can be reproduced with
`Measure-WgcClock.ps1`; the benchmark-only C++ files are built to `.cache`,
with compiler temporaries redirected to repository `.temp`. Neither raw
screen pixels nor screenshots are written.

The first bounded local run is retained as numeric CSV at
`.cache/benchmarks/native-paint-latency/wgc/clock-f3f896fa4d114580b11117b75f0fbf49.csv`.
The offline parser's machine-readable summary is at
`.cache/benchmarks/native-paint-latency/wgc/clock-bbe3cb1e3189411da77043b13f4491ab/summary.json`;
it matched all six toggles and counted six future metadata timestamps.
QPC frequency was 10 MHz; WGC used hardware D3D, yielded one initial frame
plus all six deterministic toggles, with no local-vector overflow or capture
error. The matching source color was observed for every toggle. The frame
metadata was **later than callback arrival in all six frames**, not merely
in mote's GDI/Canvas path:

| Toggle | WGC metadata minus callback arrival | WM_PAINT completion to WGC metadata |
| ---: | ---: | ---: |
| 0 | +0.32 ms | 16.77 ms |
| 1 | +1.90 ms | 16.53 ms |
| 2 | +2.55 ms | 14.56 ms |
| 3 | +0.44 ms | 13.83 ms |
| 4 | +1.09 ms | 23.26 ms |
| 5 | +3.38 ms | 24.72 ms |

This falsifies the explanation that mote itself made the earlier future
timestamps. It does **not** establish the exact cause: WGC's timestamp may
represent a future composition/display scheduling point rather than callback
completion, there may be an undocumented clock mapping offset, or the driver
may behave differently from the API's terse description. The variability
precludes blindly subtracting a single constant offset. The paint-to-WGC
numbers are **not** input-to-present or physical display latency; the
calibration window uses synchronous `UpdateWindow`, not a keyboard event.

Next, compare WGC with an independent
OS desktop timestamp (DXGI `LastPresentTime` or ETW) on a disposable foreground
host before admitting any numeric edit-to-compositor release gate. The
ordinary 100 MiB WGC case remains implemented but unmeasured at this milestone.

The follow-up [WGC timestamp-order investigation](WgcTimestampOrdering.md)
adds same-CPU, delayed-readback and global DWM cadence controls. On the local
240 Hz host, metadata exactly matched DWM compose/vblank marks in 15/18 normal
transitions and still lay after completed color readback in 10/18. This rejects
simple unit/cross-core/GPU-readiness explanations without establishing a first
desktop-present endpoint. No offset correction or release timing gate follows.

## Sources

* Microsoft, [WGC `Direct3D11CaptureFrame.SystemRelativeTime`](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframe.systemrelativetime?view=winrt-28000), [`IGraphicsCaptureItemInterop::CreateForWindow`](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow), and [`CreateFreeThreaded`](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded?view=winrt-26100).
* Microsoft, [DXGI frame information](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/ns-dxgi1_2-dxgi_outdupl_frame_info), [`AcquireNextFrame`](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutputduplication-acquirenextframe), and [Desktop Duplication overview](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api).
* Microsoft, [Acquiring high-resolution time stamps](https://learn.microsoft.com/en-us/windows/win32/sysinfo/acquiring-high-resolution-time-stamps); [DWM frame timing overview](https://learn.microsoft.com/en-us/windows/win32/dwm/frametiming-ovw).
* Intel/GameTechDev, [PresentMon console and present-mode documentation](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md).
* Schmid and Wimmer, [Yet Another Latency Measuring Device](https://epub.uni-regensburg.de/45570/1/yet-another-latency-measuring-device.pdf) (2021), and Schmid et al., [Latency Variation in Digital Games](https://epub.uni-regensburg.de/55003/1/schmid_halbhuber_latency_variation_2023.pdf), *Proceedings of the ACM on Human-Computer Interaction* (2023), on externally triggered photodiode measurement.
