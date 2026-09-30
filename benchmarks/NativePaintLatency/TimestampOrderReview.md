# Synthetic WGC timestamp-order review

Date: 2026-09-30. Scope: benchmark-only changes in `WgcClockProbe.cpp` and
`Measure-WgcTimestampOrder.ps1`. Static review plus PowerShell AST parsing and
synthetic arithmetic only; no GUI experiment, desktop capture, production edit,
build, staging, or commit performed.

## Findings

### Closed P2: Invalid DWM output became successful numeric measurements

Location: `Measure-WgcTimestampOrder.ps1:107-111`; producer:
`WgcClockProbe.cpp`, optional `DwmGetCompositionTimingInfo` call and exit code.
Confidence: high for the conditional failure path; no actual DWM failure induced.

The optional DWM call stores its HRESULT without incrementing capture errors.
This is appropriate for optional availability, but the parser then computes
compose/vblank differences and refresh period regardless of that HRESULT.
Microsoft only guarantees populated timing information on successful return.
A failed call can therefore produce zero or otherwise invalid timing fields
while the helper and wrapper still succeed. With a synthetic metadata time of
10,010,000 ticks (100 ns units) and a zero compose field, the reported delta is
1001 ms: an invalid field looks like a large measured clock disagreement.

Correction: preserve the raw HRESULT and bracket duration, expose an explicit
DWM availability flag, and return null for DWM timestamp/refresh derived values
unless the call succeeded and the individual timestamp/period is usable. Do
not reject otherwise useful WGC-only clock records merely because DWM is
unavailable. Downstream inference must filter unavailable records.

Follow-up disposition: **closed**. The current parser sets `dwm_available` from
the helper's exact S_OK encoding and emits null compose/vblank/refresh derived
values on failure, while preserving the HRESULT, query bracket, and WGC-only
measurements. Re-parsing the changed script found no AST errors. No DWM failure
was induced and the earlier GUI-independent arithmetic need not be repeated.

### Closed conditional P2: pool closure preceded callback exclusion

Location: `clock_capture::stop()` and `on_frame()` in `WgcClockProbe.cpp`.
Confidence: medium; the race is visible statically, but its exact manifestation
depends on Windows' close/revocation synchronization and was not reproduced by
this reviewer.

`stop()` closes the session and pool before acquiring `callback_mutex_`.
An already executing handler can consequently overlap pool closure, and its
next bounded-drain iteration can call `TryGetNextFrame()` on the closed pool.
If that call fails with closed-object status, the catch increments `errors_`.
The newly explicit `stop()` precedes the return-code test, so a shutdown-only
error can reject otherwise complete numeric data with exit 5. The final META
was printed before stop and can still say zero capture errors.

The three-frame bound usefully removes a perpetual refill loop, but does not
itself synchronize shutdown with callbacks. Recommended correction: mark the
observer stopping, revoke events, establish callback quiescence under the
callback mutex with callbacks checking stopping, release that mutex, and then
close the OS objects. Avoid holding the callback mutex across an OS close that
could wait for a queued handler. Also preserve shutdown-vs-capture diagnostic
distinction. This is not a claim that every OS version exhibits the race.
Microsoft documents pool Close as releasing pool resources, not as a substitute
for application callback-state synchronization:
[frame-pool Close contract](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.close).

The parent reports an intermittent teardown watchdog after all six marks-read
and one passing same-CPU control after the changes. That evidence motivates
this lifecycle follow-up, but neither outcome establishes which Close operation
blocked, and a single passing run does not prove shutdown synchronization.

Final follow-up disposition: **closed for the identified close-vs-active-work
race**. The current `stop()` stores atomic `stopping_=true`, revokes the event,
briefly acquires/releases `callback_mutex_` to let any active frame work finish,
and only then closes the session and pool without holding that mutex.
`on_frame()` checks stopping after acquiring the same mutex and before every
dequeue iteration. A handler already past its check completes before stop can
pass the lock; a handler admitted afterwards performs no pool work. Thus the
identified path cannot process the pool while Close runs. This reasoning is
for the single UI-thread stop owner, not concurrent public stop callers.

Existing numeric evidence was inspected, not rerun:
`.cache/benchmarks/native-paint-latency/wgc/timestamp-order-6a9c7ba73a8749c4899750676b484a9e/summary.json`
contains successful same-CPU and +20 ms readback-delay controls with zero
capture errors/overflow, and their stderr records reach capture-stopped and
window-destroyed. These are targeted teardown contract checks, not a blanket
claim of OS/GPU liveness. The wrapper watchdog remains appropriate.

## Checks that passed and interpretation boundaries

- PowerShell 7.6.5 AST parsing reported no errors.
- `ORDER` has 12 columns matching the parser. Indices 2/3 bracket the WGC
  metadata property read; 4/5 bracket DWM; 6 is HRESULT; 7/8 are compose/vblank;
  9/10 are complete/displayed; 11 is refresh period. `META`, `TOGGLE`, and
  `FRAME` retain their original column layout; `ORDER` is emitted only with
  `--timestamp-order`. Existing default output consumers remain parse-compatible.
- Conversion correctly uses the reported QPC frequency and decimal arithmetic,
  not a fixed 10 MHz assumption. Synthetic QPC frequency 3,579,545 Hz, arrival
  3,579,545, metadata 10,010,000 (100 ns units), and readback 3,651,136 yielded
  metadata-minus-arrival +1 ms and metadata-minus-readback -19.0000279365 ms.
  No practical precision issue was found for boot-scoped QPC values.
- `arrival_qpc` is sampled immediately after `TryGetNextFrame`, before property
  access. It is a **frame-dequeue observation**, not the actual event-handler
  entry time: later frames drained in the same callback can include prior
  readback and intentional-delay time. The old code sampled it after metadata
  access; this semantic refinement does not change the wire schema. Do not
  infer callback dispatch overhead from this mark.
- The exact center-pixel copy uses a 1x1 staging texture and a 1x1 source box.
  Only an internally created, self-painted HWND reaches `CreateForWindow`;
  there is no monitor/desktop capture path. Persisted records contain numeric
  color classes and clock values, not pixel buffers. The DWM API exposes global
  timing only and does not expand pixel capture scope.
- Each helper process is subject to a 10-second wrapper watchdog, with both
  output pipes drained asynchronously and a bounded post-kill wait. Repetitions
  are capped at five; the default three conditions give at most 15 helper runs.
  The new caller-selected Conditions array is finite but not limited to three
  entries (duplicates are possible). This is a wrapper bound,
  not proof that arbitrary GPU/OS calls in the standalone helper cannot stall.
- The revised normal path calls the idempotent `capture.stop()` before
  `DestroyWindow`, keeping the synthetic HWND alive while shutting down its
  capture. After a successful stop, the destructor sees a null pool and does
  not stop twice. Per-callback draining is bounded at the configured pool size
  of three frames; a GPU readback can still block, so retain the watchdog.
- Numeric stdout is explicitly flushed before teardown. Stage records are
  extended-mode-only stderr, not extra CSV records. The wrapper now persists
  stdout/stderr after successful reap even for timeout/nonzero exit and writes
  a failure JSON; it throws instead of promoting failed-helper output to a
  successful summary. These changes improve diagnosis without changing default
  numeric record schemas. The review did not run the parent's GUI controls.
- Same-CPU mode pins this process, including observer callbacks and paint
  marks, to one available processor. It does not pin the DWM process. Delayed
  readback records metadata before the 20 ms sleep, so it probes the observer
  delay explanation without rewriting that frame's saved metadata.
- DWM timing is queried **after** readback (and the optional sleep), and its
  latest global compose/vblank marks have no source-frame identifier matching
  the WGC item. The three conditions can support cadence/phase correlation and
  reject simple observer-readback explanations. They cannot prove that a WGC
  timestamp is a specific DWM frame, identify a Windows internal timestamp
  assignment mechanism, or validate first desktop presentation or photons.
  Compare per-condition WGC ordering first; treat DWM differences as supporting
  diagnostics, particularly because delayed observation moves the DWM sample.
- The current explicit name `metadata_minus_property_after_ms` uses ORDER
  column 3: the **WGC property-query** after mark, not the DWM after mark
  (column 5). Historical schema-1 pilot summaries used
  `metadata_minus_query_after_ms` for exactly the same quantity. The rename and
  corrected dequeue comment clarify semantics without changing executable
  timestamp collection or the default META/TOGGLE/FRAME wire layout; historical
  numeric artifacts retain their original field names.

Parser robustness boundary: counts are checked, but duplicate frame/order IDs,
matching ID sets, positive frequency, and META frame-count agreement are not
fully validated. The trusted, just-launched helper generates these invariants;
this is not a demonstrated execution defect. An eventual offline/imported
record parser should validate them rather than silently overwrite IDs.

## External contract and production cross-check

- [Microsoft: DwmGetCompositionTimingInfo](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetcompositiontiminginfo)
  specifies null HWND on Windows 8.1 onward and output validity upon success.
- [Microsoft: DWM_TIMING_INFO](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ns-dwmapi-dwm_timing_info)
  defines QPC-valued compose, vblank, and refresh timing; its frame counters do
  not supply captured-window revision identity.
- [Microsoft: SystemRelativeTime](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframe.systemrelativetime)
  describes the frame's compositor QPC timestamp, not callback or readback time.
- [Chromium's Windows VSync provider](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/ui/gl/vsync_provider_win.cc)
  is a production cross-check for treating DWM values as cadence diagnostics,
  not captured source-state or photon evidence. No research claim is being
  promoted to an implementation contract in this bounded review.

Disposition: both the DWM failure-field P2 and the conditional close-before-
callback-exclusion concern are closed in the latest inspected implementation.
No remaining substantive privacy, default record-format, unit-conversion, or
active-frame-vs-Close defect was found within this review's scope. Standalone
OS/GPU liveness and exhaustive callback-lifetime behavior are not verified.

## Findings-note consistency pass

The current `WgcTimestampOrdering.md` keeps exact WGC/DWM equality as local
cadence/phase correlation and expressly rejects same-frame identity, internal
timestamp-assignment proof, first desktop visibility, and physical scan-out.
It separates the nine-process pre-quiescence numeric experiment from the final
two-process shutdown validation, and does not promote either into a product
latency distribution or SLA. No materially overstated conclusion requiring a
new finding was identified in this prose pass. Its opening phrase that metadata
"denotes" a DWM phase could optionally be made strictly observational ("often
equals global DWM compose/vblank marks") to match its later careful causal
limits; that is precision of wording, not another demonstrated defect. No
completed execution checks were rerun for this consistency pass.
