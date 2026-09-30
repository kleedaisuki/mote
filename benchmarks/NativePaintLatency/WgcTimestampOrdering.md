# WGC timestamp ordering: local DWM cadence correlation, not a present endpoint

## Decision (2026-09-30)

Do **not** unlock a mote input-to-present SLA from `SystemRelativeTime`, subtract
a fitted offset, or replace it with callback/readback time. The frame clock is
in the documented QPC domain, but on this host it often equals global DWM
compose/vblank marks later than CPU frame observation, including after
the captured color is already readable. This is an instrumentation endpoint
problem, not evidence of a mote rendering bottleneck.

The strongest new result is **exact tick equality** between the WGC timestamp
and the independently queried global DWM compose/vblank marks in 15/18 normal
color transitions. This supports a shared composition cadence/phase tag. It
does **not** prove internal timestamp assignment, a matching DWM frame ID,
first desktop visibility, or panel scan-out: the DWM query has no captured
window/source-revision identity and is performed after readback.

## Competing explanations and discriminating observations

| Explanation | Observation | Conclusion within this host |
| --- | --- | --- |
| Incorrect units or floating-point conversion | WGC C++/WinRT `TimeSpan.count()` is 100 ns; local QPC is exactly 10 MHz; raw WGC counts equal raw DWM QPC counts in 15/18 normal transitions. Parser also uses decimal rational conversion for other frequencies. | A conversion/epoch mistake does not explain these records. |
| ±1 QPC tick cross-thread ambiguity | Normal positive differences reach 3.42 ms versus a 0.0001 ms QPC tick; same-process single-CPU affinity still gives future timestamps in 17/18 transitions. | Ordinary cross-core ordering ambiguity is not a sufficient explanation. DWM itself is not pinned. |
| Callback arrives before GPU content exists | In 10/18 normal transitions the WGC timestamp remains later than **completed synchronous Map/readback** of the exact synthetic color. | GPU-readiness delay alone is insufficient. |
| Readback delay rewrites the frame clock | Metadata is saved before a controlled 20 ms sleep; 15/18 delayed transitions still have future metadata at dequeue, but none after readback. | Waiting changes observation completion, not the saved frame timestamp; do not subtract readback cost from a frame endpoint. |
| Shared DWM phase tag | Normal WGC time exactly equals later queried `qpcCompose` **and** `qpcVBlank` in 15/18 transitions. Their refresh period is about 4.167 ms. Delayed/single-CPU observers query DWM much later and usually see a newer global mark. | Strong local phase correlation; no guaranteed completed-render or first-visible timestamp follows. |

Microsoft describes [WGC SystemRelativeTime](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframe.systemrelativetime)
as compositor QPC time, while [TimeSpan](https://learn.microsoft.com/en-us/uwp/api/windows.foundation.timespan)
documents the 100 ns count. [QPC guidance](https://learn.microsoft.com/en-us/windows/win32/sysinfo/acquiring-high-resolution-time-stamps)
allows only ±1 tick ambiguity between threads, not millisecond correction.
The [DWM timing structure](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ns-dwmapi-dwm_timing_info)
defines separate composition, vblank and completion/display fields. Successful
global timing queries in these records have zero `qpcFrameComplete` and
`qpcFrameDisplayed`; they cannot rescue a source-specific completion endpoint.

As a production cross-check, [Chromium's Windows VSync provider](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/ui/gl/vsync_provider_win.cc)
uses DWM vblank/refresh values for cadence, validates failure and implausible
driver data, and falls back to display configuration. That is a scheduling
use, not a source-image presentation certificate. No undocumented Windows
internals are adopted as a contract here.

## Reproducible setup and measurements

Run from the repository on an interactive Windows host:

```powershell
./benchmarks/NativePaintLatency/Build-WgcObserver.ps1
./benchmarks/NativePaintLatency/Measure-WgcTimestampOrder.ps1 -SkipBuild
# Focused shutdown/affinity control, rather than another product baseline:
./benchmarks/NativePaintLatency/Measure-WgcTimestampOrder.ps1 `
  -SkipBuild -Conditions same-cpu,delay-readback-20ms -Repetitions 1
```

This captures only a new 320×240 self-painted red/blue HWND and reads its center
pixel, never the desktop or mote. Each process paints six alternating colors
using synchronous `UpdateWindow`, with 140 ms message pumping between toggles.
The callback records QPC after frame dequeue, brackets the metadata property
read, copies/maps one pixel, then brackets a global DWM query. `arrival_qpc`
is a **dequeue observation**, not callback entry. The new parser's
`metadata_minus_property_after_ms` refers to the WGC property bracket; historical
schema-1 pilot reports called this `metadata_minus_query_after_ms`.

Three conditions run in rotating order over three fresh processes each:
ordinary scheduling, process-wide affinity to one available CPU, and 20 ms
sleep after acquiring metadata but before GPU copy. The latter two deliberately
perturb scheduling and are not product performance workloads. The wrapper has
a 10-second per-process watchdog, asynchronously drains both pipes, persists
numeric CSV/stage stderr and rejects failed helpers. Artifacts stay beneath
non-reparse repository `.cache`; compiler scratch stays in `.temp`.

Environment: Windows 10.0.26200 (Windows 11 build), Intel i9-12900H, 20 logical
processors, QPC 10 MHz, hardware D3D11 (no WARP), observed DWM cadence about
240 Hz. Installed adapters: Intel Iris Xe driver 31.0.101.4502 and NVIDIA RTX
3070 Ti Laptop GPU driver 32.0.15.7261; the capture device's adapter was **not**
recorded, so these do not identify which GPU performed WGC. Compiler: MSYS2
MinGW-w64 g++ 16.1.0; Windows SDK C++/WinRT headers 10.0.26100.0.

The nine-process numeric record is
`.cache/benchmarks/native-paint-latency/wgc/timestamp-order-2b84677f29a549a3804591357ac2bd1d/summary.json`.
Measured C++ source SHA-256:
`FB355DA3175A733D1539EFFF2D5EB1731A8FDB21FF25AD45D7595C46149495CD`;
helper SHA-256:
`4F6C40960463D6703F594C7B6D23945211A6821F2669779C4D0F2CAB2C079DF0`.
That source includes stop-before-HWND-destruction but predates the final
shutdown callback-quiescence fix. All nine processes exited zero with zero
capture errors/vector overflows, seven frames each and all six color matches.
The rows are clock controls, not latency-distribution or p95 estimates.

| Condition | Processes / matched transitions | Metadata later than dequeue | Metadata later than completed readback | Exact WGC = DWM compose = vblank | Metadata minus dequeue observed range |
| --- | ---: | ---: | ---: | ---: | ---: |
| Normal | 3 / 18 | 16 / 18 | 10 / 18 | 15 / 18 | −1.8123 to +3.4176 ms |
| Single CPU | 3 / 18 | 17 / 18 | 1 / 18 | 1 / 18 | −0.0335 to +3.5718 ms |
| Delayed readback | 3 / 18 | 15 / 18 | 0 / 18 | 0 / 18 | −1.8691 to +3.2980 ms |

Largest WGC property bracket across matched transitions: 0.0006 ms. Largest
DWM query bracket: 0.0217 ms. Delayed-readback metadata minus completion ranges
from −37.9185 to −22.2947 ms; `Sleep(20)` is a requested delay, not an exact
20 ms CPU or capture pipeline duration. These ranges establish ordering on
one host, not uncertainty bounds or cross-platform accuracy.

## Negative results and benchmark lifecycle correction

The first attempt using `Start-Process -WindowStyle Hidden` exited with WGC
HRESULT `0x80070057` and no frames, retained under
`timestamp-order-pilot-c78fa1f1a4004e37a6e1d9c192ed40d3/`. Inference: Windows
startup show-state can hide the helper's first GUI window despite its visible
style. The working wrapper uses `ProcessStartInfo.CreateNoWindow` only to avoid
a console; the explicitly created synthetic HWND remains visible.

Early controls intermittently hit the 10-second watchdog after all six
transitions had been collected, at `STAGE,capture-stop` after HWND destruction.
The retained failure under
`timestamp-order-839671e131f94700aa95c4647b33cf98/`
contains stage stderr; earlier attempts under `timestamp-order-83f7...`,
`timestamp-order-ff0a...` and `timestamp-order-db014...` lacked complete output.
Do not count any failed-helper rows as successful controls. The precise OS
deadlock mechanism was not diagnosed. The helper now stops capture **before**
destroying the HWND, marks stopping, revokes events, drains active callback
work via a mutex, and closes OS objects outside that mutex. Each callback
drains at most three frames; delayed observers cannot indefinitely refill an
unbounded drain loop. This fixes a benchmark lifetime hazard, not mote.

Final shutdown contract validation is retained at
`.cache/benchmarks/native-paint-latency/wgc/timestamp-order-6a9c7ba73a8749c4899750676b484a9e/summary.json`:
one single-CPU process and one delayed-readback process, each seven frames,
six matched colors, zero errors/overflows, and successful `capture-stopped`
then `window-destroyed`. Its source SHA is
`194C3CF761AF0411188726DA337E8428D40D8DC20757515C0BBA3AAC5B5B0FCE`
and helper SHA is
`E28A836100AA4D12E4B256C50D4F43B44BBD37B54341DA78E5073FE72F8A1DCA`.
Later comment/field-name-only edits do not change this executable's semantics.
The existing default `Measure-WgcClock.ps1 -SkipBuild` parser was also checked
successfully after stop-before-destroy, without promoting its samples to a
product baseline. Independent static review is in `TimestampOrderReview.md`;
DWM-HRESULT validity and close-vs-active-callback findings were both corrected.

## Concrete next measurement, and the endpoint we can actually claim

1. On a **disposable hosted interactive desktop**, prime DXGI Desktop
   Duplication before input, using the exact output/adapter. Record unconverted
   [LastPresentTime QPC and AccumulatedFrames](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/ns-dxgi1_2-dxgi_outdupl_frame_info),
   skip pointer-only frames, check clock ordering and acquire/release brackets.
   Do not run desktop capture on a normal local user desktop as an implicit
   fallback for this exact-HWND probe.
2. Correlate the first **observed** edited desktop ROI with the existing exact
   A → B → Undo A → Redo B source/file oracle, and exact foreground/geometry.
   Accumulation, access loss, missed acquisition or obscuration make first-frame
   attribution inconclusive; retain a late observation bound rather than a
   fabricated first event. Test a synthetic draw delay separately from an
   observer readback delay before applying the endpoint to mote.
3. Once the desktop endpoint passes calibration, interleave 1/100 MiB ordinary
   AOT cases using one executable SHA, plus observer-off input-ack controls.
   Only a reproducible product phase—not the observer's copy or scheduling—can
   justify optimization. No production renderer change follows from this note.

The defensible software endpoint is a **source-verified desktop-image update**,
not exact physical presentation. WGC remains useful for source-state capture
and an explicitly named opaque OS frame mark, but this host does not justify
reading its metadata as a completed rendering instant. Neither WGC, DWM global
timing, nor DXGI certifies physical key-to-light: the [peer-reviewed Schmid et
al. apparatus](https://epub.uni-regensburg.de/55003/1/schmid_halbhuber_latency_variation_2023.pdf)
separates end-to-end hardware measurement from application timing. Its gaming
user-study results do not establish an acceptable text-editor latency target.
