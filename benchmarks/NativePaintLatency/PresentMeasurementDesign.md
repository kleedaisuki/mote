# Windows compositor-frame measurement for ordinary Continuous mote

Status: **design, not a measurement** (2026-09-30). This note deliberately does
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

## Sources

* Microsoft, [WGC `Direct3D11CaptureFrame.SystemRelativeTime`](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframe.systemrelativetime?view=winrt-28000), [`IGraphicsCaptureItemInterop::CreateForWindow`](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow), and [`CreateFreeThreaded`](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded?view=winrt-26100).
* Microsoft, [DXGI frame information](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/ns-dxgi1_2-dxgi_outdupl_frame_info), [`AcquireNextFrame`](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutputduplication-acquirenextframe), and [Desktop Duplication overview](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api).
* Microsoft, [Acquiring high-resolution time stamps](https://learn.microsoft.com/en-us/windows/win32/sysinfo/acquiring-high-resolution-time-stamps); [DWM frame timing overview](https://learn.microsoft.com/en-us/windows/win32/dwm/frametiming-ovw).
* Intel/GameTechDev, [PresentMon console and present-mode documentation](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md).
* Schmid and Wimmer, [Yet Another Latency Measuring Device](https://epub.uni-regensburg.de/45570/1/yet-another-latency-measuring-device.pdf) (2021), and Schmid et al., [Latency Variation in Digital Games](https://epub.uni-regensburg.de/55003/1/schmid_halbhuber_latency_variation_2023.pdf), *Proceedings of the ACM on Human-Computer Interaction* (2023), on externally triggered photodiode measurement.
