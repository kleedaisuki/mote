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
`System Events`, observes an AX focused text element, sends one `X`, uses
Command-S, and applies the same full byte oracle. It attempts View → Next Page
for the 100 MiB case and records a before/after
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
window title, foreground activation, and focused element role. Each stage has
a finite budget and separately records elapsed time, last safe observation,
attempt count, and distinct process-launch, compile, execution, timeout, and
child-exit status. Process visibility, window title, frontmost and focused-role
budgets are 12, 20, 10 and 16 seconds respectively, with a 4-second bound on
each `osascript` attempt and 10 seconds on compilation. Every
AppleScript is compiled before execution; source, compiled script, and compiler
and interpreter stdout/stderr for **each** attempt are kept under the failed
case's repository-local `.temp/` directory. Title observations record only a
match flag/count/length, not arbitrary window text, and no script reads
`AXValue`. This is still a **non-gating diagnostic** until hosted evidence
shows AX/TCC and the custom canvas hierarchy work end to end.

Mac timing is an **automation round-trip upper bound** including AppleScript
compilation, `osascript` startup, AX polling, and process launch. macOS
`PeakWorkingSet64` has been unavailable in prior Native AOT runs; a positive
`WorkingSet64` is a point-in-time resident-set observation, not peak RSS.
Neither OS probe measures IME, edit-to-draw, compositor presentation, or true
horizontal scrolling. Do not turn a single result into a CI latency threshold.
