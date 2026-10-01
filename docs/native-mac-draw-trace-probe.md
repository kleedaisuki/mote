# Opt-in macOS source draw tracing probe

## Scope and invocation

This diagnostic validates production callback wiring in a published target binary,
not physical paint, compositor presentation, a latency SLA, keyboard input, or IME.
Run one profile per process from the repository root:

```sh
./mote --check-native-mac-draw-trace legacy
./mote --check-native-mac-draw-trace continuous
```

The only success lines are respectively:

```text
mote-native-mac-draw-trace-ready mode=legacy; endpoint=source-draw-return; physical-presentation=not-tested
mote-native-mac-draw-trace-ready mode=continuous; endpoint=source-draw-return; physical-presentation=not-tested
```

Exit 1 means failed evidence/setup; exit 2 means invalid profile; exit 3 means
non-macOS. An internal 20-second watchdog exits 124 if AppKit or teardown blocks.
The watchdog is failure-only and cannot manufacture callback evidence. Normal
execution closes the owned window and drains telemetry. A native deadlock cannot
be cleanly unwound by a posted close, so watchdog termination does not promise a
drained file. A disposable target runner should also impose an external deadline.

## Isolation and evidence

The early route executes before launch/configuration parsing. It creates an
unsaved Engine document and one AppKit shell, never a document controller. It
neither opens nor saves user text, reads user configuration, accesses clipboard,
changes input sources, synthesizes external events, requests TCC, captures pixels,
uses the network, or writes the user home. Standard OS/AppKit initialization is
unchanged. The probe's benign shell-stop wake event stays within its own AppKit
application; this is not external event injection.

Telemetry is explicitly configured under a fresh opaque directory in
`.temp/mac-draw-trace/` below the repository working directory. Repository markers
are required. Reparse/symlink ancestry is rejected. Existing directories and traces
are not deleted. This path check is a cooperative diagnostic boundary, not a
security boundary against a concurrent adversary swapping filesystem links.

The probe installs source version 0, arms an interval, accepts one canonical
mutation to version 1 and supersedes the previous interval. It requests drawing
with version 0 still installed, then installs version 1 and invokes AppKit
`displayIfNeeded` on the actual owned source surface twice. It does **not** invoke
`drawRect:` directly or fabricate a graphics context. Legacy exercises mote's
NSTextView subclass; Continuous exercises the production canvas and requires a
positive source body. Their existing hooks alone can emit draw success.

After telemetry shutdown, exactly five records must exist: one cancelled draw
interval at version 0, one successful draw interval at version 1 and their two
semantic parent marks, plus the writer's successful `mote.session` terminal record
with empty dimensions and a null parent. Span IDs must be distinct, draw/parent trace IDs identical,
and parent links exact. Only allowlisted top-level JSON keys and numeric version
attributes are accepted for the four endpoints. Sink health is checked before
drain, and detached telemetry health is checked afterward. The public health API
does not retain the old sink's post-detachment health; exact persisted records
and the terminal session record therefore supply the post-drain evidence, rather
than claiming a detached health snapshot proves durable flush success.
The version-0 draw request is a mismatched stimulus;
this final-record audit is not independently timestamped proof of its rejection.
The parent records are explicit synthetic causal anchors, not background parser
completion evidence. A second source draw must not create a second successful
interval. No return before the actual native hook can satisfy the record audit.

The probe yields 250 ms after Shown before the stimulus and 250 ms after the
stimulus before closing. It requests owned-window layout and source/window
`displayIfNeeded`, but permits normal AppKit drawing during that second event-loop
turn. The delay is not a fabricated latency measurement or proof that drawing
occurred: only the existing callback's persisted successful record can satisfy
the audit. Failed private assertions print a fixed in-code contract identifier;
arbitrary native exception messages, document text and filesystem paths remain
excluded. The hard deadline is unchanged.

## Verification state

Windows developer host: Native Release `--no-restore -warnaserror` build passed
with zero warnings/errors. This is compilation evidence only: AppKit callbacks
and published Native AOT execution remain unverified until target execution.
Independent static safety review is required before wiring disposable macOS CI.
No workflow was changed or dispatched by this assignment.

## First target failure and narrow corrective hypothesis

Run [36752189587](https://github.com/kleedaisuki/mote/actions/runs/36752189587),
osx-arm64, failed both non-gating diagnostic steps with exit 1. A successful job
with `continue-on-error` does not override these failures. Downloaded artifacts:
`.cache/ci-36752189587-mac-draw-arm/` and
`.cache/ci-36752189587-logs/osx-arm64.log`.

- Legacy emitted five records, but the accepted version-1 draw interval was
  **cancelled**, not successful. Its parent completed in approximately 762 µs and
  the draw was cancelled in finally at approximately 763 µs. This proves the probe
  closed without observing an eligible callback, not that production drew it.
- Continuous emitted only the terminal session record on both ARM and x64,
  proving failure before the first probe marks. Static inspection located a
  definite contract violation: the probe passed its complete multiline snapshot
  as `InputSourceText`, while `MacTextInputIsland.Bind` requires an exact
  single-line slice and rejects CR/LF. The correction binds only the first line;
  the full immutable snapshot remains the canvas's source. This is a probe bug,
  not evidence that production binding or drawing is broken.
- x64 Legacy passed with its exact success marker and five records including a
  successful version-1 draw. Preserve this as a target control, not evidence for
  ARM Legacy or either Continuous profile. It supports a race in the immediate
  closing stimulus rather than a universally broken Legacy hook.

The first implementation did all setup, direct display requests and closing
within one posted-action drain. The narrow correction above allows actual
AppKit layout/drawing turns before closing and provides fixed assertion IDs.
The Continuous binding is corrected to satisfy the existing single-line contract.
Neither production hook nor completion guards changed. Independent safety
re-review and target re-execution are required; no Mac success is claimed yet.

## Corrected hosted target result

[CI run 36754713708](https://github.com/kleedaisuki/mote/actions/runs/36754713708)
at `dea61e5` completed all seven strict jobs successfully. On both
`osx-arm64` and `osx-x64`, each of the separate Legacy and Continuous
published-Native-AOT diagnostic processes exited zero and printed its exact
`mote-native-mac-draw-trace-ready` mode marker. Root independently inspected
both job logs and downloaded all four JSONL artifacts beneath repository
`.cache/ci-36754713708-mac-draw-{arm,x64}/`. Each process emitted exactly five
records with one successful version-1 `edit_to_draw_submission`, a cancelled
version-0 draw, the corresponding causal parents and terminal session. This
closes the initial **probe** failure for these four synthetic target invocations.

These are source draw callback-return observations, not physically displayed
pixels, natural user input, a latency distribution, or proof that the earlier
Legacy ARM timing race cannot recur under a different load. The CI steps remain
non-gating: their logs, exact markers and artifacts—not the green parent icon—
are the acceptance evidence for this narrow run.

See [end-to-end tracing](end-to-end-tracing.md) for production endpoint,
cancellation, content privacy and interpretation contracts.
