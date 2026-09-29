# External native canvas GUI diagnostic

These scripts exercise the **opt-in** `mote --canvas-experimental` process, not
the default native editor or the read-only canvas geometry probe. They generate
only synthetic data: exactly 1, 10, or 100 MiB of fixed-width CRLF lines and
50 MiB of one unbroken ASCII line. All fixtures and AppleScript intermediates stay under
repository `.temp/benchmarks/native-canvas-gui/`; compact JSONL observations
stay under `.cache/benchmarks/`. A successful run removes its scratch directory;
a failed run retains it for diagnosis. No real document body is logged.

## Local Windows behavior probe

```powershell
./benchmarks/NativeCanvasGui/Measure-WindowsCanvasGui.ps1 `
  -ExecutablePath .cache/canvas-large-undo/publish-win-x64/mote.exe

# Repeat one-character typing in one process without coalescing the 80 ms
# semantic debounce. Run several fresh processes for uncertainty estimates.
./benchmarks/NativeCanvasGui/Measure-WindowsCanvasGui.ps1 `
  -ExecutablePath .cache/benchmarks/native-canvas-range-win-x64/mote.exe `
  -Cases many -ManyMiB 100 -EditCount 20 -Trace
```

For each fresh process, it waits for the exact-PID native window, visible
`MoteInteractiveCanvas`, focused RichEdit input island, and nonempty bounded
input text. It sends one `WM_CHAR` through that real island, checks the dirty
title, requests native Save, then verifies a prefixed `X` and **all** original
bytes by streaming SHA-256. The many-line case dispatches `WM_VSCROLL/SB_BOTTOM`
and requires the canvas scrollbar to advance. The one-line case sends one wheel
message and requires the host to remain bounded; it does **not** claim horizontal
scrolling, which the interactive canvas does not currently implement.

`-ManyMiB` selects 1, 10, or 100 MiB; `-EditCount` inserts that many `X`
characters at source start and verifies their exact saved bytes. `-Trace`
enables the product's own numeric, text-free trace and leaves 300 ms between
edits plus a final 200 ms for the 80 ms semantic debounce. It retains raw
trace JSONL under `.cache/benchmarks/native-canvas-traces/` and reports the
count and nearest-rank p50/p95 of `document.edit_to_presentation` samples.
That phase ends at semantic publication, **not physical paint**; a complete
trace requires the count to equal `-EditCount`. The script does not measure GC
collections in the shipped Native AOT binary, where EventPipe is disabled.

`open_to_host_ready_ms` is parent process start to *externally observed,
focused host*, sampled at 20 ms intervals and including launcher/poll overhead.
`edit_to_dirty_ms` and `edit_to_save_ms` include OS message dispatch and external
observation; none measures first draw or physical screen presentation. The
positive Windows `PeakWorkingSet64` is a whole-process counter, not an isolated
canvas allocation. `Win32Probe.cs` uses `WM_GETTEXTLENGTH` to inspect the child
RichEdit; Windows [`GetWindowText` cannot read a control in another
process](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowtexta).

## Hosted macOS arm64 capability probe (non-gating)

```powershell
./benchmarks/NativeCanvasGui/Measure-MacCanvasGui.ps1 `
  -ExecutablePath src/Mote.Native/bin/Release/net10.0/osx-arm64/publish/mote
```

The script first runs a **1 MiB many-line control**, then the 100 MiB many-line
and 50 MiB one-line cases. A control failure identifies AX/TCC/input trouble
without conflating it with large-file load; every case still requires an
independent exact-byte save oracle. It drives the exact child PID through
`System Events` and a separately compiled, exact-PID ApplicationServices AX
observer. Before any keyboard input, it requires a unique source-backed
`AXTextArea` with the exact source length, its `AXFocused=true` (the native
provider derives this from the hidden `NSTextView` being the window's actual
first responder), and an exact-PID focused window whose title matches the
synthetic fixture. It does **not** gate solely on `System Events` `frontmost`.
The helper also records `NSWorkspace.frontmostApplication`'s numeric PID as a
diagnostic, not an uncalibrated hard gate on this hosted session.
It then sends **only non-mutating Shift+Right** and requires the source-backed
selection to change exactly `0/0→0/1`; Left must restore `0/0` before the
single `X`, Command-S, full streaming byte oracle, and fresh-process reopen
with the saved source length. Any ambiguity aborts **before the text key**;
the process is terminated without a global Command-W shortcut. The 100 MiB
case attempts View → Next Page and records a before/after
`AXVisibleCharacterRange` only as a *candidate* scroll marker. The custom
canvas AX element may expose the **global** document length while its hidden
`NSTextView` input island remains bounded; the script does **not** use AX text
length as host-length evidence and never reads `AXValue`. Existing in-process
AppKit clipboard tests establish a separate bounded-host property, not this
external keyboard workflow.

The [first hosted canvas attempt (`36576635105`)](https://github.com/kleedaisuki/mote/actions/runs/36576635105)
timed out **before any external key or Save** in one opaque 100 MiB AX-ready
AppleScript. It yielded no evidence that loading, focus, TCC, or edit was the
cause. The revised probe retains schema version 1 and the outer `AX-focus`
stage, but splits `readiness_step` into exact-PID AX process, expected synthetic
window title, foreground observation, and source-proxy first-responder gate.
Each stage records elapsed time, last safe observation, attempt count, and
distinct process-launch, compile, execution, timeout, and child-exit status.
Process visibility and window title have 12- and 20-second budgets; each
`osascript` attempt is bounded by 4 seconds. The Swift AX observer is compiled
once before the editor is launched; every
AppleScript is compiled before execution; source, compiled script, and compiler
and interpreter stdout/stderr for **each** attempt are kept under the failed
case's repository-local `.temp/` directory. Title observations record only a
match flag/count/length, not arbitrary window text, and no script reads
`AXValue`. This is still a **non-gating diagnostic** until hosted evidence
shows AX/TCC and the custom canvas hierarchy work end to end.

The [second hosted attempt (`36578393670`)](https://github.com/kleedaisuki/mote/actions/runs/36578393670)
localized the first **1 MiB control** failure: the exact-PID AX process was
visible after 883.5 ms, its expected window title matched after 1,462.9 ms,
but `set frontmost` was followed by `frontmost=false` on all 27 observations
within the former 10-second foreground gate. No focused role, key, or Save was
reached; this is not evidence of a 100 MiB load regression. The [read-only follow-up (`36579476337`)](https://github.com/kleedaisuki/mote/actions/runs/36579476337)
again matched the 1 MiB control's window (1,879.3 ms), then saw 23
`not-frontmost` attempts; the actual foreground process was **Finder PID 357**,
and mote's `AXFocusedUIElement` had role **`AXScrollArea`**, not `AXTextArea`.
The former gate correctly sent **no keyboard input** when routing was
unproven, but this did not distinguish a hosted activation/session limitation
from a canvas-specific first-responder defect.
A macOS arm64 Native AOT job uploaded this non-gating artifact; the overall
workflow's separate test jobs failed, so the run URL is not a release pass.
In [run `36591084601`](https://github.com/kleedaisuki/mote/actions/runs/36591084601),
the strict **default** editor completed external X → Save → reopen while its
read-only `System Events` foreground observation still said
`target-frontmost=false;global-pid=378`; its actual focused AX role was
`AXTextArea`. The separate Canvas 1 MiB control again matched process/window
but saw Finder as foreground and `AXScrollArea`. Thus `System Events`
frontmost/global PID is a **false-negative for a known successful keyboard
workflow** and cannot be the sole Canvas gate. Conversely, an AX scroll-area
role does not prove the hidden input island is first responder. The new
source-proxy focus + non-mutating selection challenge is designed to separate
those cases without risking a text key to an unverified target.

The [first hosted source-proxy-gated run (`36594818528`)](https://github.com/kleedaisuki/mote/actions/runs/36594818528)
compiled the Swift observer and executed all three cases on macOS arm64. The
**1 MiB control and 100 MiB many-line** cases passed the exact source-backed
selection challenge, one external `X`, full-byte Save, and fresh-process
reopen with source length +1. For 100 MiB, the externally observed source
proxy was ready after **3,007.5 ms**, X-to-dirty-title was **336.3 ms**, and
X-to-exact-Save was **1,055.7 ms**. These are **one-run automation-inclusive
intervals**, not first editable frame, paint, or a latency distribution. The
source AX visible range changed `0/2176→2113/2240` after the native Next Page
menu; that is a scroll-anchor clue, not rendered-pixel proof. Positive point
working sets were **291.7 MiB** at focus and **309.2 MiB** after Save; the
macOS peak counter remained unavailable. In all three cases the Swift
`NSWorkspace` frontmost PID matched mote while `System Events` still reported
Finder, directly confirming why the older foreground-property gate was false.
The **50 MiB single-line** case passed Shift+Right and Left selection routing,
but the combined edit script did not observe a dirty title within its bounded
poll. It did **not** reach Save or reopen. The current artifact cannot tell
whether the X was never committed, committed slowly, or only the title marker
was missed; no long-line edit/Save performance claim follows. The follow-up
harness sends **exactly one X** and separately polls the source-backed AX
length/selection and dirty title for at most 25 seconds. It never retries X;
if AX fails to confirm the exact length +1 and selection `1/0`, it stops before
Save. If AX confirms but the title does not, it records
`dirty_marker_status=missing-after-source-confirmed` and allows one Command-S
only after renewing the focused-source/window gate; the full streaming byte
oracle and fresh reopen still decide whether the edit actually persisted.
This diagnostic has **not yet been target-run**.

Mac timing is an **automation round-trip upper bound** including AppleScript
compilation, `osascript` startup, AX polling, and process launch. macOS
`PeakWorkingSet64` has been unavailable in prior Native AOT runs; a positive
`WorkingSet64` is a point-in-time resident-set observation, not peak RSS.
Neither OS probe measures IME, edit-to-draw, compositor presentation, or true
horizontal scrolling. Do not turn a single result into a CI latency threshold.
