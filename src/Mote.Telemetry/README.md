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
At normal process exit call `await MoteTelemetry.ShutdownAsync()`; its default
two-second deadline is intentionally shorter than a user save. Inspect
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
queue and flushes to the OS durable path. An abrupt process or power failure
can leave an incomplete final line; JSONL readers should ignore only a malformed
trailing line, not silently discard earlier valid lines. There is no claim of
per-record `fsync` durability.

The `OutputDirectory` override is an explicit local diagnostics destination,
not a network export. No retention policy spans distinct sessions; users or a
future consent-aware cleanup feature must manage old trace sessions. This
avoids deleting a concurrent editor's active trace file.

## Design references

- [.NET `ActivitySource` and tracing concepts](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts): activities are created only for listeners, allowing instrumentation without always-on tracing allocations.
- [.NET bounded channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels): bounded queue behavior and non-blocking `TryWrite` prevent disk stalls from reaching the editor hot path.
- [OpenTelemetry semantic convention guidance](https://opentelemetry.io/docs/specs/semconv/how-to-write-conventions/): use stable, low-cardinality names and opt-in for sensitive attributes. Mote deliberately exposes no free-form attributes.
- [Google's Dapper report](https://research.google/pubs/dapper-a-large-scale-distributed-systems-tracing-infrastructure/): production tracing emphasized low overhead, common instrumentation points, and sampling. Mote adopts common operation points and a bounded pipeline, but does not sample an explicitly enabled local diagnostic run by default: losing rare edit stalls would undercut the main use case. If profiling shows excessive telemetry-on overhead, measure it before introducing workload-aware sampling.
