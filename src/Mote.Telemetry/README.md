# Mote.Telemetry

`Mote.Telemetry` is an optional, local-only tracing module. The editor does not
need a telemetry service, network exporter, SQLite library, or reflection-based
serializer. It uses .NET `ActivitySource` for causal spans and `Stopwatch` for
monotonic durations. A bounded channel hands fixed-schema records to one JSONL
writer; editor and UI callbacks never wait for disk.

## Opt in and integrate

At process startup, before the first operation:

```csharp
MoteTelemetry.ConfigureFromEnvironment(); // Active only for MOTE_TRACE=1.
using var startup = MoteTelemetry.Start(TelemetryOperation.Startup);
```

Set `MOTE_TRACE=1` to persist traces under `~/.mote/traces`.
`MOTE_TRACE_SUBDIR` may select a single ASCII directory name under `~/.mote`,
e.g. `MOTE_TRACE_SUBDIR=profile1`.
No arbitrary path is accepted through environment variables. The application
may pass its configuration-resolved absolute trace directory to
`ConfigureFromEnvironment(config.TraceDirectory)`; that explicit argument takes
precedence over the fallback path. A programmatic
`TelemetryOptions.OutputDirectory` exists for local benchmarks and tests; use a
repository `.temp` directory for project experiments.

Wrap synchronous or async operations with a scope. `SetStatus` must be called
for a failed or cancelled operation; disposal otherwise records success:

```csharp
using var open = MoteTelemetry.Start(
    TelemetryOperation.OpenToEditable,
    new TelemetryDimensions(Format: TelemetryFormat.Json, DocumentBytes: fileLength));
try
{
    await OpenDocumentAsync();
}
catch
{
    open?.SetStatus(TelemetryStatus.Failure);
    throw;
}
```

For a time interval crossing UI callbacks, retain a `TelemetryMark` instead of
keeping an `Activity` ambient across unrelated events:

```csharp
var editMark = MoteTelemetry.Mark();
// Complete an adapter-verified native source draw; not physical display paint.
MoteTelemetry.RecordElapsed(TelemetryOperation.EditToDrawSubmission, editMark,
    new TelemetryDimensions(Version: version));
```

`RecordElapsed` completes **one** named interval per mark; do not call it twice
with the same mark or its span ID would be reused. Child work may start before
the interval completes and still retain causal identity:

```csharp
var editMark = MoteTelemetry.Mark();
using var parse = MoteTelemetry.StartChild(TelemetryOperation.AnalysisParse, editMark);
// After semantic publication, complete this parent once; it does not claim
// that compositor output reached the display.
MoteTelemetry.RecordElapsed(TelemetryOperation.EditToPresentation, editMark);
```

For multiple endpoints, `MoteTelemetry.Fork(editMark)` makes a distinct child
span retaining the original timestamp. The native adapters use a bounded
`NativeDrawTrace` to match source generation/version, reject stale/reentrant
draw tickets and terminate replaced/closing intervals as cancelled. See
[end-to-end native tracing](../../docs/end-to-end-tracing.md) for precise endpoint
boundaries. `EditToPaint` is retained for compatibility but **is not emitted by
the native editor**: neither `WM_PAINT` nor AppKit `drawRect:` proves compositor
presentation or physical display visibility.

`Mark`, `Start`, `Record`, and `RecordElapsed` are cheap no-ops when disabled.
The native Save failure path additionally calls `RecordSaveFailure` for
filesystem exceptions. It is inert without explicit tracing and emits only a
fixed `save.failure.<phase>` operation and the original signed numeric
`hresult` attribute. Unknown or untrusted phase text maps to
`save.failure.unknown`; exception messages, paths, and other `Exception.Data`
values are never serialized. This is diagnostic evidence, not an automatic
retry or a claim that a localized error message identifies the Win32 cause.
The original `RecordSaveFailure(Exception)` public signature is preserved. Native
Save uses its explicit-parent overload to retain the schema-1 failure event
alongside the typed Engine phase, with the actual captured snapshot version when
available. Its parent belongs to the original Save session: reconfiguration
rejects and counts the event against that sink rather than falling back into the
new session. Successful Saves do not emit a legacy failure event.
At normal process exit call `await MoteTelemetry.ShutdownAsync()` after closing
controller-owned delayed intervals. Shutdown closes new producer admission,
allows already admitted scopes to enqueue their final records, then drains the
writer. Producer completion and writer flushing share the same default
two-second deadline; a stalled scope cannot extend it. Do not await shutdown
from inside a scope you still hold. Inspect
`MoteTelemetry.Health` for `SinkFaulted` and `DroppedRecords` and show a warning
in the diagnostics UI if the sink fails. A write failure disables the sink; the
exception text is never displayed or persisted because it may contain a path.

## Record contract and privacy

Every JSONL line has `schema_version`, `utc_time`, `session_id`, `trace_id`,
`span_id`, `parent_span_id`, `operation`, `duration_us`, `status`, and
`attributes`; an optional GUID `run_id` may be supplied by a benchmark harness.
The final `mote.session` record is the root of a session's spans. UTC is for
ordering; duration uses monotonic time. The only
attributes are normalized format enum, coarse size bucket, document version,
a numeric count, and a signed numeric `hresult` only on filesystem Save failure.
There is no API field for document content, path, filename,
extension, parse message, arbitrary tag, or command-line argument. The same
constraint applies to `ActivitySource` names: operation names are fixed enums.

The JSONL writer rotates by file size or age and retains at most
`MaxFilesPerSession` files from its own session. It does not delete files from
another process/session. Producers use non-blocking `TryWrite`; overflow drops
new records, increments `Health.DroppedRecords`, and emits a later
`telemetry.dropped` aggregate where possible. A normal shutdown drains the
queue and flushes to the OS durable path. The drop counter also accounts for
records attempted after the queue closes, including scopes that outlive the
shutdown budget and old delayed marks; it does not exclusively mean queue
overflow. A late loss after the final aggregate cannot be appended to an
already closed file. If shutdown exhausts its budget with admitted scopes
unfinished, the terminal `mote.session` has status `cancelled`, not success;
this signals incomplete evidence without inventing a dropped count.
Delayed marks do not hold admission indefinitely, and
must be completed/cancelled by their owner before process shutdown. An abrupt process or power failure
can leave an incomplete final physical line; JSONL readers may ignore only an
unterminated final row. A newline-terminated malformed record or any earlier
malformed row is corruption and must fail validation, not be silently discarded.
There is no claim of per-record `fsync` durability.

Producer admission is the shutdown acceptance boundary, not a prior read of
the process-wide sink reference. Fresh scopes/events that lose admission to
shutdown are inert (`Start`/`StartChild` return null), like disabled tracing;
they were never accepted. Existing scopes hold admission through their final
enqueue, while retained delayed marks that miss closure remain counted as
losses. Zero dropped records therefore describes accepted trace work, not an
audit of every concurrent instrumentation call or every unfinished mark.

The `OutputDirectory` override is an explicit local diagnostics destination,
not a network export. No retention policy spans distinct sessions; users or a
future consent-aware cleanup feature must manage old trace sessions. This
avoids deleting a concurrent editor's active trace file.

## Design references

- [.NET `ActivitySource` and tracing concepts](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts): activities are created only for listeners, allowing instrumentation without always-on tracing allocations.
- [.NET bounded channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels): bounded queue behavior and non-blocking `TryWrite` prevent disk stalls from reaching the editor hot path.
- [OpenTelemetry semantic convention guidance](https://opentelemetry.io/docs/specs/semconv/how-to-write-conventions/): use stable, low-cardinality names and opt-in for sensitive attributes. Mote deliberately exposes no free-form attributes.
- [Google's Dapper report](https://research.google/pubs/dapper-a-large-scale-distributed-systems-tracing-infrastructure/): production tracing emphasized low overhead, common instrumentation points, and sampling. Mote adopts common operation points and a bounded pipeline, but does not sample an explicitly enabled local diagnostic run by default: losing rare edit stalls would undercut the main use case. If profiling shows excessive telemetry-on overhead, measure it before introducing workload-aware sampling.

## Explicit native Save requests

`BeginRequest(CommandSave | CommandSaveAs)` returns an enabled-only
`TelemetryRequest`; disabled tracing returns null without allocating. The request
holds no ambient Activity, document, snapshot, path, or long producer lease.
Native owners carry `request.Mark` explicitly through worker and UI callbacks.

```csharp
var request = MoteTelemetry.BeginRequest(TelemetryOperation.CommandSave);
request?.Checkpoint(TelemetryEvent.SaveAdmitted);
var save = request?.BeginPhase(TelemetryOperation.Save) ?? default;
var phase = MoteTelemetry.BeginPhase(TelemetryOperation.SaveTargetCheck, save);
// Execute the real operation; recording a boundary never replaces it.
MoteTelemetry.EndPhase(TelemetryOperation.SaveTargetCheck, phase);
MoteTelemetry.EndPhase(TelemetryOperation.Save, save);
request?.EndOnce(TelemetryStatus.Success, TelemetryReason.Completed);
```

Receipt and phase entry are **persisted causal anchors**: `command.save.received` or
`command.save_as.received` uses the request mark identity and records command kind
even when no terminal exists, and each fixed `<phase>.entered` uses its phase
mark identity. Ordinary checkpoints use fresh child IDs. Terminal request and
phase durations also use fresh IDs, parented to their respective entry anchors,
and measure from the original mark timestamp. Therefore every row has a unique
span ID, and a readable held-phase prefix can reconstruct request ancestry even
before any terminal duration exists. Entry success means boundary execution,
not file commit success. A missing terminal is censored evidence, not success.

`EndOnce` atomically selects one terminal enqueue attempt. Its boolean result is
not a disk acknowledgment. An optional fixed `attributes.reason` is written only
for request terminals; schema version remains 1, and no-reason records retain the
existing field shape. Readers must include `reason` in their attribute allowlist.
Phase failures may carry only the adapter-supplied numeric filesystem `hresult`.

All new explicit methods reject old/shutdown contexts and count rejection on
that context's **original** sink. They never attach old work to a newly configured
sink. Legacy Activity-backed `StartChild` behavior is unchanged; use the new
explicit methods for request lifetimes. `MarkChild` alone is an unpersisted mark;
use `BeginPhase` for any work whose entry must survive in a recoverable prefix.
Callers end each phase once; only request terminal selection is internally atomic.

## Independent native menu observation

Healthy opt-in tracing enables an owned `NSMenu` subclass on macOS. Tracing off
keeps the stock menu with no new managed key callback. It observes only matching
key-down Command-S-family candidates; it neither binds commands nor initiates
Save. The exact event is sent once to the superclass and its exact Boolean
result is returned. Shift/Caps Lock do not classify Save versus Save As.

`RecordNativeMenuCheckpoint` accepts only five appended fixed events:
`native.menu.observation.ready`, `native.menu.observation.unavailable`,
`native.menu.save_family.entered`, `native.menu.save_family.returned_true`, and
`native.menu.save_family.returned_false`. Each is a unique, zero-duration success
checkpoint under the **current session**, ignoring ambient Activities and
accepting no caller dimensions. A false return is successful observation of
false, not failed Save. Readiness proves installation only.

These are independent positives, not a menu-entry/return pair or an input-to-Save
causal chain. Nested calls and reconfiguration do not require event retention or
cross-callback state. No characters, native pointers, event timestamps, modifiers
or key codes are serialized. A missing checkpoint never certifies nonexecution;
forced-exit evidence remains censored and bounded-queue loss still applies.

## Windows Grid adapter focus provenance

The opt-in `BeginNativeGridFocus(target, before)` API records only a real
`WindowsGridUiaBridge.Focus` adapter invocation attempt. It does **not** cover
provider `Node.SetFocus` early stale/unsupported returns, OS focus delivery, or
cross-process client/server causality. Missing records remain unobserved, not
proof that a provider was never called.

The fixed `native.grid.focus.adapter.received` receipt is a zero-duration successful
session child, independent of ambient `Activity`. Its terminal
`native.grid.focus.adapter` has a distinct span ID parented to that receipt and
measures elapsed time from the receipt timestamp. `NativeGridFocusRequest.EndOnce`
selects one attempt atomically without holding a producer lease across native work;
late attempts are rejected/accounted by their original sink and never redirected
to a newer session. A winning attempt does not certify persistence.

Receipt attributes are exactly `native_thread_relation`,
`managed_admission_relation`, `focus_before`, and `focus_target`. Terminals add
exactly `focus_after` and `focus_result`. Relationships are `unknown`, `owner`, or
`non_owner`; owner GUI-queue focus categories are `unavailable`, `none`, `source`,
`table`, `row_scroller`, `column_scroller`, `coordinate`, `owned_other`, or
`outside`; target is `table` or `cell`. `focus_before` and `focus_after`
classify `GetGUIThreadInfo(ownerThread).hwndFocus` in the `owner_gui_queue`
observation boundary. They do not observe global keyboard focus, foreground
ownership, or physical input delivery. Native thread relationship and managed
admission relationship are separate evidence; an unavailable query yields
unknown/unavailable evidence and is not an adapter action failure. `applied` and `no_change` terminals succeed;
`unsupported`, `stale`, `not_ready`, `invalid_coordinate`, `unavailable`,
`composition_blocked`, and `fault` fail. The first eight results are actual adapter
results before HRESULT conversion; `fault` is exceptional invocation failure,
not an invented adapter result. No HWND, PID, TID, cell coordinate, document
content, exception text, legacy dimensions, or free-form value is accepted.

Disabled receipt calls allocate nothing and return before inspecting evidence.
Enabled calls reject undefined enum values. Invalid terminal evidence does not
consume terminal ownership. The native caller remains responsible for containing
optional evidence failures and preserving the exact original action/result.

Portable validation: `NativeGridFocusTelemetryTests` covers all closed results,
all owner GUI-queue categories, exact attribute sets, nonambient parentage, concurrent
terminal ownership, disabled zero allocations, closed enum validation, and stale
session rejection. Release regression command (2026-10-01) passed **58/58**, zero
failed/skipped; retained TRX is
`.cache/validation/focus-telemetry/focus-telemetry-regression.trx`. This includes
13 focus tests plus existing request/menu/general telemetry coverage. An initial
fixture incorrectly expected `session.start`; the actual schema operation is
`mote.session`. The fixture was corrected, its failed TRX retained, and no
production behavior was changed to accommodate it. No local native GUI or hosted
runtime coverage is claimed by these tests.

## Product native source phases

Five append-only `TelemetryOperation` values distinguish product source work:

| Fixed operation | Measured boundary |
| --- | --- |
| `native.source.install` | Install source text in the native editing surface. |
| `native.source.readback` | Read the native source buffer for reconciliation. |
| `native.source.reconcile` | Reconcile native state with the engine-owned document and history. |
| `native.source.range_publish` | Publish engine-owned selection or source ranges. |
| `native.source.style_publish` | Publish semantic foreground styles to the native surface. |

Callers use the existing `Start`/`StartChild` duration API and explicitly set a
failure, cancellation, or skipped status when appropriate. These names add no
new schema fields, dynamic labels, content, paths, coordinates, native handles,
or exception text. Existing numeric operation identifiers are unchanged.
Dimensions remain the existing normalized format, byte-size bucket, version,
and numeric count; a caller must define its count locally rather than inventing
a free-form attribute. An explicit parent span identifies causality; temporal
proximity alone does not.

Instrumentation belongs to the product native-source profile, not the
capability experiment or a retroactive claim about other presentation profiles.
Installation, range publication, and style publication measure call boundaries,
not physical screen presentation. Readback is not proof of keyboard delivery;
reconciliation is not by itself proof of a saved document. Missing spans remain
unobserved under disabled tracing, rejected admission, queue loss, or forced
exit. The disabled path remains allocation-free after warm-up.

`NativeSourceTelemetryTests` checks append-only IDs, closed serialized names,
v1 root/attribute fields, explicit parentage, all four statuses, and the disabled
allocation/file boundary. These portable tests do not exercise a native GUI or
certify that every product source call site has been instrumented.

The strict native acceptance reader adds exactly these five operation names;
its schema, closed dimensions, and privacy checks are otherwise unchanged.
Windows Grid graph consumers reuse that reader; the causal Save reader already
accepts compatible future operation names and needs no graph-contract change.
On 2026-10-02, `python -B -m unittest discover -s benchmarks/NativeAcceptance
-p test_native_source_operations.py -v` passed **4/4** methods, no skips, against
the modified reader. The fixtures cover 20 accepted operation/status rows and
reject unknown source names, content/foreign attributes, unsupported root
fields, invalid numeric types, and non-vocabulary dimensions. Retained output:
`.temp/validation/native-source-telemetry/python-vocabulary.txt`. This result
does not qualify the separate C# producer tests or native product call sites.
