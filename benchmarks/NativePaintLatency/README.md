# Native editing: input, drawing, and captured-screen latency

The retained [same-binary trace off/on paired guard](TracePairs.md) reuses the
exact-source GDI fixture on a disposable hosted Windows desktop, stops at the
first qualification failure and produces no estimate for a rejected series.
It does not certify macOS instrumentation cost or physical input/presentation.

The newer exact-HWND Windows Graphics Capture edit probe and independent
synthetic-clock calibration are documented in
[`PresentMeasurementDesign.md`](PresentMeasurementDesign.md). Its local
source-state oracle works, but WGC frame metadata was observed later than
callback arrival and the target was not foreground. It therefore makes **no
edit-to-present latency or release-SLA claim**. The GDI probe below remains a
first sampled screen-change observation, not a compositor timestamp.

The follow-up [`WgcTimestampOrdering.md`](WgcTimestampOrdering.md) local clock
controls correlate the future metadata with DWM compose/vblank marks, including
single-CPU and delayed-readback controls. They resolve several false clock/GPU
explanations, **not** first desktop presentation or a product p95. The associated
`Measure-WgcTimestampOrder.ps1` captures only a self-painted synthetic HWND.

## Endpoint contract

The existing `document.edit_to_presentation` trace ends when the native shell
accepts semantic analysis after an 80 ms (or larger) debounce. It does **not**
end at `WM_PAINT`, AppKit `drawRect:`, the desktop compositor, or physical light
output. The external Windows probe in this folder adds a deliberately narrower
observation: the first **screen-DC capture of changed pixels** in a known
synthetic canvas row after one cross-process `WM_CHAR` message. Its clock also
records the synchronous `SendMessage` return. Neither endpoint is a physical
keyboard event; neither is a compositor-present callback or a photon timestamp.

```
external WM_CHAR dispatch → input callback/engine edit → native invalidation
                                      ↘ semantic publication (trace, debounced)
                       native WM_PAINT/drawRect → desktop composition
                                                     → sampled screen ROI → panel
```

Microsoft's [GDI capture example](https://learn.microsoft.com/en-us/windows/win32/gdi/capturing-an-image)
uses `GetDC(NULL)` and `BitBlt` for screen pixels. Unlike
[`PrintWindow`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow),
this probe does not ask mote to render into a caller-owned DC; however, a
captured screen image still does not certify the precise moment DWM presented
it, much less the panel's scan-out. [Desktop Duplication](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api)
provides frame-by-frame desktop surfaces and metadata for a more precise later
observer. Apple's [ScreenCaptureKit sample](https://developer.apple.com/documentation/screencapturekit/capturing-screen-content-in-macos)
requires complete frame status; [`displayTime`](https://developer.apple.com/documentation/screencapturekit/scstreamframeinfo/displaytime)
is WindowServer's frame-display timestamp. Both are external observers, not
the editor's own drawing callback. In research, [Schmid and Wimmer's hardware
latency device](https://epub.uni-regensburg.de/45570/1/yet-another-latency-measuring-device.pdf)
uses an electrically triggered input and a screen photodiode; a [2023
peer-reviewed follow-up](https://epub.uni-regensburg.de/55003/1/schmid_halbhuber_latency_variation_2023.pdf)
uses the same method to quantify actual input-to-light latency. That stronger
endpoint is outside this software-only probe and should not be implied by its
numbers.

## Reproduction and privacy

`Measure-WindowsScreen.ps1` accepts an existing **published Native AOT** mote
binary. It is restricted to a GitHub-hosted Windows runner unless a developer
explicitly supplies `-AllowLocal`. The default workload covers exact 1, 10,
and 100 MiB CRLF corpora and a 50 MiB single line, each in a fresh process.
The fixture replaces only the first few synthetic bytes with a distinctive
plain-text glyph sentinel, preserving exact file size. The child uses a fresh
`MOTE_HOME` under repository `.temp/`, with tracing disabled. Each generated
fixture directory is removed after its process is reaped. Cleanup rejects
reparse points in every path ancestor or descendant and verifies the resolved
absolute target remains under the workspace's resolved `.temp`; this bounds
retained fixture space even for 30 repetitions. All report JSONL stays under repository
`.cache/`; neither screenshots nor document bodies are persisted. The report
contains only synthetic size, hardware/display
description, executable SHA, numeric capture metadata, status and errors.

```powershell
# Hosted runner: exact foreground HWND is required.
./benchmarks/NativePaintLatency/Measure-WindowsScreen.ps1 `
  -ExecutablePath .cache/published-win-x64/mote.exe -Repetitions 3

# Local, explicitly permitted proof-of-method. A visible but non-foreground
# window is labeled passed-visible-background, never user-key latency.
./benchmarks/NativePaintLatency/Measure-WindowsScreen.ps1 `
  -ExecutablePath .cache/published-win-x64/mote.exe `
  -Cases many-1 -Repetitions 1 -AllowLocal
```

The script locates the exact-PID `MoteNativeEditorWindow`, visible
`MoteInteractiveCanvas`, and GUI-thread-focused bounded RichEdit island. It
attempts foreground activation and **requires** exact global foreground on
hosted Windows; local runs persist whether that succeeded. It sends `EM_SETSEL`
to source start, waits for initial paint to settle, then sends a `WM_NULL`
negative control. `EM_SETSEL`, `WM_GETTEXT[LENGTH]`, `WM_NULL`, and `WM_CHAR`
use finite 3-second `SendMessageTimeoutW` waits; dispatch timeout is a distinct
failure and the exact child is killed/reaped. [Microsoft documents why
ordinary `SendMessage` can block](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw).
Up to five initially unsettled controls are recorded, not
discarded; a still-changing control aborts before the edit. A dedicated
sampler thread is ready before the `WM_CHAR` dispatch. The probe samples only
the first-row **256×32** screen rectangle beginning at canvas-client **(42,4)**,
under per-monitor-v2 DPI awareness. It rejects an overlapping pointer or
obscured corners/center both before and during capture. **Five sampled
ownership points cannot prove that every interior pixel belongs to mote**;
the local opt-in can transiently read foreign pixels into process memory if a
small overlay evades those points. Automatic runs use a disposable hosted
desktop, and only numeric comparisons—not pixels—are persisted. At least 128
pixels must differ by 24 RGB units from baseline. A visual-change candidate
must match a later settled frame within 64 pixels and retain visible glyph
ink; this is still only *provisional*. The synthetic source must remain
byte-identical before explicit Save. After the timing window, the script
requires full-file exact **X Save → Undo → original Save → Redo → X Save**
oracles, a stable visible Undo glyph shape distinct from the timed candidate,
and three Redo screen captures matching that candidate. Only after this
reversible source-state check does `source_specific_verified=true` authorize
using the first-change timestamp as an edited-source observation.
Before dispatch, a mode-color baseline check also requires at least 100
high-contrast source-glyph pixels, so a blank never-painted window cannot be
mistaken for a successfully opened editor.

The first verified capture is a **sampling-late bound on the changed image
becoming available to this screen-capture API**. It is not an upper bound on
physical display time: compositor/scan-out order, screen-copy behavior and
hosted virtual displays can differ. The report includes each capture's median
cost and the *largest observed completion gap*; differences smaller than that
gap cannot be resolved confidently. The `WM_CHAR` input-ack time is a separate
interval, not subtracted from the screen number as if it were paint duration.
The probe itself consumes CPU and screen-copy bandwidth; compare observer-on
and observer-off edit acknowledgement before using its distribution as a
performance regression signal.

### Local proof-of-method, **not a current-source baseline**

The eight-case **pre-oracle visual-change pilot** at
`.cache/benchmarks/native-paint-latency/272c450285704e94a9fc122eae3267d5/screen-observations.jsonl`
used the **older cached** win-x64 one-binary Native AOT executable SHA-256
`13299F53F5D34F934DD7C5D9D896938071132D70D14215596B3E81DDB2F85B0E`.
Its source vintage is documented in [`docs/native-performance-baseline.md`](../../docs/native-performance-baseline.md):
it was published from the Native owner's frozen local source *before* commit
`e6add4e` and included contemporaneous default-off AX draft code. Its file
timestamp was 2026-09-29 13:16:07 UTC. This is older than the live-theme and
current-source work, so these figures validate the **observer workflow only**.
The pilot ran locally on Windows 10.0.26200, Intel i9-12900H, 20 logical
processors, approximately 32 GiB RAM. A later final-schema 50 MiB check at
`.cache/benchmarks/native-paint-latency/f74af1689b014c30b986c19ea2a21f31/screen-observations.jsonl`
recorded a 2560×1440 primary display, 96-DPI target, 1057×1014 canvas
client, exact target foreground, quiet negative control, untouched source
before Save and exact saved suffix. The original eight-row schema did not
record global foreground or pre-Save hash, so do **not** retroactively claim
those controls passed in the pilot.

| Fixture | Processes | `WM_CHAR` ack range | First visual-change desktop capture range, **not source-verified** | Maximum capture-completion gap across runs |
| --- | ---: | ---: | ---: | ---: |
| 1 MiB many-line | 2 | 3.82–3.97 ms | 16.26–16.44 ms | 27.22 ms |
| 10 MiB many-line | 2 | 4.08–4.32 ms | 16.95–20.19 ms | 18.75 ms |
| 100 MiB many-line | 2 | 3.71–3.94 ms | 16.84–28.10 ms | 18.10 ms |
| 50 MiB long line | 2 | 5.23–5.39 ms | 33.19–36.69 ms | 17.07 ms |

All eight pilot Save oracles passed, and the final quiet negative controls
passed. They lack the later reversible screen-state oracle, so their capture
times must **not** be read as proved edited-glyph latency. An earlier
24-pixel threshold repeatedly found a **31-pixel** change
during `WM_NULL`, including one false edit candidate. Its cause was not
established; raising the threshold to 128 for this large synthetic glyph shift
made the control discriminating without claiming general sensitivity to small
edits. The final-schema single long-line run observed `WM_CHAR` ack **4.36 ms**
and matching screen capture **24.72 ms**, with a **20.81 ms** maximum sample
gap and **3.33 ms** median capture cost. These few measurements neither prove
long-line slowdown nor support a p95/production SLA. The 80 ms semantic trace
would answer a different question even if collected concurrently.
The later **pre-reversal** ink-gated 1 MiB check at
`.cache/benchmarks/native-paint-latency/2f2a6ac9bcc14facafecca5b42deb469/screen-observations.jsonl`
confirmed **2,338** visible baseline glyph pixels, exact foreground and Save,
`WM_CHAR` ack **4.10 ms**, and first matched screen capture **12.07 ms** with
a **16.86 ms** maximum sample gap. It is one new observer self-check, not a
repeat estimate or evidence of a speed change.

The reviewer correctly identified that an immediate “settled” frame could
still be a transient blank. A proposed one-glyph horizontal translation
signature was tested and **rejected**, not threshold-tuned: on a real exact-X
Canvas edit its best foreground-mask Jaccard score was only **0.229**, despite
visible pixels and exact Save. A naive exact-baseline-after-Undo comparison
also failed (**2,683** differing pixels) even after the bounded input host
showed the original prefix; viewport/style state can evolve across edit/Undo.
The replacement oracle tests reversible *source states*, not assumed glyph
translations or a frozen pre-edit raster. Its first short local run at
`.cache/benchmarks/native-paint-latency/1fa7196bdf7c4b5f8f3fbcef8ead2097/screen-observations.jsonl`
passed exact foreground, quiet control, **15.62 ms** first visual capture,
full X/original/X Save hashes, a stable Undo foreground shape differing at
**1,904** pixels, and three Redo screen samples with **zero** pixel difference
from the timed X candidate. The generated source was removed after report.
The same corrected oracle passed one 100 MiB many-line case in
`.cache/benchmarks/native-paint-latency/07c22a074be8421a808fa3e08068a133/screen-observations.jsonl`:
`WM_CHAR` ack **3.40 ms**, first verified screen capture **17.80 ms**,
maximum capture gap **33.57 ms**, exact X/original/X suffix hashes and
zero-pixel Redo mismatch. It remains a single old-binary sample.
These are **old-binary proof-of-method samples**, not a current-source performance
baseline or evidence about p95. A foreign overlay covering the ROI without
touching any checked ownership point remains a privacy/measurement risk on
the explicit local mode; do not automate that mode on a normal user desktop.

### First hosted current-source observation: run `36680070533`

[GitHub Actions run `36680070533`](https://github.com/kleedaisuki/mote/actions/runs/36680070533)
at commit `3cb05484d79844173c0e907165feb92c91ce5185` completed all six strict
jobs; the separate **non-gating** win-x64 paint diagnostic and its JSONL upload
steps also succeeded. The downloaded **paint artifact only** is retained at
`.cache/ci-run-36680070533/native-paint-latency-win-x64/8fef001b9dea41e5859c024b5c551698/screen-observations.jsonl`.
Both rows used the **same published Native AOT** executable SHA-256
`2B7E96214C057419430EA42DC586D7956329C086A729C84B4B6AB58AF9D0C4A7`,
Windows 10.0.26100, hosted AMD EPYC 7763 with four exposed logical processors
and approximately 16 GiB RAM, 1024×768 primary display, 96-DPI target, and
668×659 canvas client. These are one process per size, not repeated latency
distributions.

| Hosted exact-size fixture | `WM_CHAR` acknowledgement | First source-state-verified changed screen capture | Median copy/readback cost | Largest capture-completion gap |
| --- | ---: | ---: | ---: | ---: |
| 1 MiB CRLF lines | 1.187 ms | 30.878 ms | 27.893 ms | 47.081 ms |
| 100 MiB CRLF lines | 1.113 ms | 30.976 ms | 17.170 ms | 32.143 ms |

In **both** cases, the exact target HWND was foreground at focus and just
before input, the `WM_NULL` control remained quiet with no unsettled attempt,
the initial ROI contained 2,196 visible-glyph pixels, and the first changed
capture had 1,002 changed pixels. The fixture stayed byte-identical before
Save; full-file hashes established **X Save → original Undo Save → X Redo
Save**. A stable Undo source shape differed from the candidate at 699 pixels,
three Redo screen samples matched the candidate with **zero** changed pixels,
`source_specific_verified=true`, and both case directories were removed after
completion. No screenshot or document body was uploaded. This is evidence
that an actual edited source state reached the sampled screen rectangle on
the hosted published binary—not a `WM_PAINT` timestamp, compositor-present
event, physical keyboard result, or photon latency.

The near-equal 30.9 ms screen observations do **not** show that 100 MiB edits
have the same user-perceived latency as 1 MiB edits: each has only one sample,
and this hosted observer spent **17–28 ms median** in screen copy/readback with
**32–47 ms maximum** capture-completion gaps. These measurement costs are on
the same scale as the reported screen interval. Inference: the immediate next
causal question is whether the gap lies in native draw/composition or in this
coarse observer; no product rendering bottleneck can be identified from these
rows alone. The earlier local 16–28 ms pilot used a different, older binary,
Intel host, display geometry, and weaker visual-change oracle, so it is **not
a paired before/after comparison** and supports no regression claim. A
version-tagged native draw-return hook plus DXGI frame metadata or a calibrated
external observer is needed to resolve the distinction.

### Observer-cost localization (instrumentation, not a product optimization)

The observer already retains one DIB for the whole timed phase, so allocating
a persistent bitmap again cannot explain or cure the hosted 17–28 ms cost.
New JSONL fields divide each timed copy into `median_owner_check_ms` (five
window-ownership points and pointer exclusion), `median_bitblt_ms` (screen DC
to persistent DIB), and `median_readback_ms` (`Marshal.Copy` into an
in-process array); `first_changed_capture_cost_ms` and
`first_changed_bitblt_ms` describe the candidate frame itself. A separate
**post-oracle** ABBA control alternates 40 full 256×32 and 40 small 64×16
screen copies on the same already-verified synthetic window. It reports
`copy_area_*` medians without writing pixel bytes. Running that control only
after X/Undo/Redo verification avoids prewarming the primary first-edit
measurement. The small ROI is a *cost control*, not yet an edit-latency
observer: its source-state detection threshold and reliability have not been
calibrated.

A five-process local pilot of the phase split and area control is retained in
`.cache/benchmarks/native-paint-latency/a228ccb34efc4331bc62087bf69745e4/screen-observations.jsonl`.
It used the **older** AOT SHA above, the local Intel host, explicit
`-AllowLocal -LocalTopmost`, and the same synthetic 1 MiB fixture. All five
passed the exact X/Undo/Redo source oracle, but were labeled
`passed-visible-background`; this does not simulate a physical key. Across
their per-process medians, ownership checks cost **0.11–0.29 ms**, managed
readback **0.007–0.008 ms**, and `BitBlt` **3.10–5.01 ms**. In the paired ABBA
control, full-area medians were **4.26–5.63 ms**, small-area medians
**4.03–4.32 ms**, and the five paired full-minus-small values had median
**0.77 ms** (range **0.14–1.60 ms**). This supports a *local* mechanism:
`BitBlt`, not `Marshal.Copy` or five-point checking, dominates the 4–6 ms
copy cost; shrinking area alone had only a modest benefit. That pilot ran
ABBA **before** the edit, whereas the corrected harness runs it **after** the
source oracle; its primary edit timings must not be compared with the earlier
or future first-edit workflow. The ABBA cost comparison is within each pilot
process, not a hosted result or a proven improvement to mote.

The discriminating next measurement is the same phase split and post-oracle
area control on one current-source hosted win-x64 binary. If hosted `BitBlt`
also dominates and reducing area barely helps, the 30.9 ms screen interval
remains too observer-limited to blame on mote rendering; replacing the renderer
would not be justified. If hosted `BitBlt` is fast but the
capture-to-capture gap stays large, investigate scheduler/virtual-display
cadence. If both are small while first changed pixels arrive late, add a
version-tagged native draw-return hook before touching renderer architecture.

#### Hosted phase-split observation: run `36684299172`

The separately uploaded [win-x64 paint artifact from CI `36684299172`](https://github.com/kleedaisuki/mote/actions/runs/36684299172)
at commit `0a90986a840fe00129a3fc027f34ffb0f93e5ce5` is retained under
`.cache/ci-run-36684299172/native-paint-latency-win-x64/acc61f85a05048468c57ff561b9adb66/screen-observations.jsonl`.
The non-gating diagnostic and artifact-upload steps both completed
successfully, and the win-x64 Native AOT job succeeded. The **overall CI run
later completed with failure in the separate `Test / windows-latest` job**;
these paint rows are not evidence that all six jobs were green.
Both cases used the same published Native AOT executable SHA-256
`07BD3A1CF8FFE41B0EC3A6CA8E4A2D8F7AF79CB0BB865952585F07AEF2E9E036`
on a hosted Windows 10.0.26100 AMD EPYC 7763 runner with four exposed
logical processors, a 1024×768 primary display, 96-DPI target and 668×659
canvas. Each size has **one** process/sample, not a distribution.

| Synthetic fixture | Input ack | First source-verified screen capture | First-change `BitBlt` / total copy | Timed-phase median owner / `BitBlt` / readback | Post-oracle ABBA full / small median |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 MiB CRLF lines | 1.280 ms | 30.841 ms | 30.512 / 30.841 ms | 0.195 / 30.512 / 0.014 ms | 16.652 / 15.843 ms |
| 100 MiB CRLF lines | 1.231 ms | 30.618 ms | 30.309 / 30.621 ms | 0.051 / 17.558 / 0.009 ms | 15.939 / 15.827 ms |

Both rows passed exact target foreground, quiet `WM_NULL`, full X/original/X
Save hashes, visible distinct Undo source geometry, three exact Redo screen
matches and fixture cleanup. The first changed frame had the same 1,002
changed pixels as the earlier hosted probe. Critically, **the first changed
capture spent nearly its entire observed interval inside `BitBlt` itself**;
five ownership checks and managed readback were comparatively negligible.
For 100 MiB, the timed-phase *median* `BitBlt` was only 17.558 ms while the
specific first-changed call took 30.309 ms; those are different statistics,
not a contradiction. Post-oracle same-process 64×16 area reduction saved
**0.810 ms** in the 1 MiB control and **0.112 ms** in the 100 MiB control;
the full/small `BitBlt` medians were 16.585/15.814 and 15.902/15.793 ms.
This supports an observer cost dominated by a mostly area-insensitive screen
copy/synchronization call on this hosted virtual display. It does **not** prove
that mote drew in <1 ms: the updated frame could become available *during*
the blocking `BitBlt`, and no versioned native draw-return mark exists.

The prior hosted run's first changed captures were 30.878/30.976 ms for
1/100 MiB, numerically close to these 30.841/30.618 ms observations, but
the binary SHA, VM instance and added instrumentation differ, and each cell
has just one process. No p95, cross-run regression or equality-of-size claim
follows. A smaller DIB alone is not a worthwhile new edit observer yet;
the next discriminating measurement is a version-tagged native draw-return
timestamp correlated with a frame-level external capture source such as DXGI
Desktop Duplication, with capture overhead and permission boundary stated
separately. Optimizing the document engine or Canvas painter from these
software-screen numbers would be premature.

## Instrumentation architecture before optimizing product code

1. Keep the current `EditToPresentation` operation and its privacy-safe
   version dimension intact; do not rename semantic publication “paint.” Add
   a separate *native draw complete* event only after a format-independent
   `NativeDocumentStamp` (generation/version) is sampled at draw entry and
   checked again at successful draw exit. A bounded pending-mark queue may
   coalesce superseded revisions, but must report skip/cancel rather than
   silently attribute an old edit to a new frame. A `TelemetryMark` can be
   completed **once**; one edit needs distinct marks for semantic publication
   and native drawing. Theme repaint and viewport-only drawing require a
   separate draw-reason counter, not a fake source edit.
2. For the interactive Windows Canvas, place the hook **after** successful
   `BitBlt` and `EndPaint` in its `WM_PAINT` path. For the default RichEdit,
   its existing subclass can observe `WM_PAINT` after `DefSubclassProc`, but
   that only proves OS control paint-return and must be separately validated
   against a pixel-changing fixture. For the Mac Canvas, sample after the
   visible `drawRect:` Core Text body has returned; `displayIfNeeded` forced by
   a probe is not representative. A default `NSTextView` hook likewise needs
   a verified Objective-C super-call ABI and visible-window condition. None
   of these callbacks alone proves compositor presentation.
3. Match each draw mark to an external source-specific pixel canary. On
   Windows, progress from this GDI screen-copy pilot to DXGI Desktop
   Duplication or WPR/WPA graphics ETW when the specific draw-to-screen gap
   matters. Microsoft [documents WPR/WPA for UI responsiveness](https://learn.microsoft.com/en-us/windows/apps/develop/performance/winui-perf)
   and [GPUView flip-queue correlation](https://learn.microsoft.com/en-us/windows/win32/direct2d/profiling-directx-applications).
   On macOS, a permissioned `SCStream` observer should retain only numeric
   complete-frame timestamps and a synthetic ROI match; hosted CI may lack
   screen-recording consent. Do not put capture permissions or a sidecar in
   the shipped one-binary editor.
4. Separate first editable, `WM_CHAR`/`insertText` acknowledgement, native
   draw-return, externally captured frame, and true input-to-photon tests.
   Repeat 1/10/100 MiB many-line plus long-line on fixed SHA/image/display
   settings with at least 30 independent-process observations per stratum and
   multiple hosted VMs. Preserve raw records, process-level median/MAD and
   bootstrap intervals. [Kalibera and Jones, ISMM 2013](https://kar.kent.ac.uk/33611/45/p63-kaliber.pdf)
   motivate nested repetitions and uncertainty; a two-process maximum is not
   p95. Measure observer-off acknowledgement and working set to quantify
   probe overhead before accepting or rejecting an optimization.

**Decision now:** the external capture path is feasible and falsifiable, but
the current pilot does not justify a product optimization. The consequential
next test is a fixed-current-source hosted Native AOT run with exact foreground
and draw-version hooks, not a microbenchmark of `TextSnapshot` alone.
