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

## Verification state

Windows developer host: Native Release `--no-restore -warnaserror` build passed
with zero warnings/errors. This is compilation evidence only: AppKit callbacks
and published Native AOT execution remain unverified until target execution.
Independent static safety review is required before wiring disposable macOS CI.
No workflow was changed or dispatched by this assignment.

See [end-to-end tracing](end-to-end-tracing.md) for production endpoint,
cancellation, content privacy and interpretation contracts.
