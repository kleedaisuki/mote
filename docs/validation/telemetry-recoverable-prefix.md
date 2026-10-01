# Recoverable telemetry prefixes under abnormal process exit

Date: 2026-10-01. Scope: `JsonlTraceSink` transport only; no controller, command
admission, operation-entry instrumentation, or schema change.

## Problem and contract

The independent [observability audit](../reviews/observability-end-to-end-audit.md)
found a real forced-cleanup session with a zero-byte trace despite externally
observed completed edit/analysis work. Normal shutdown draining cannot recover
records still in managed buffers when the process is killed.

The writer now drains managed buffering to the OS on a **250 ms periodic turn
when dirty**, or after **64 KiB accumulated written bytes** under sustained load.
`AppendAsync` also checks elapsed monotonic time after each completed row so a
continuously nonempty queue does not starve the periodic flush. These are policy
thresholds under healthy scheduling/I/O, **not a hard deadline**: blocked writes,
blocked flushes, scheduling starvation, queue loss, or a kill before the next
flush may still censor evidence.

Producers retain the existing nonwaiting bounded `TryWrite` path. There is no
producer file I/O, per-record fsync, synchronous UI flushing, new public API, new
operation name, or schema-version change. Normal rotation/closure still uses
`Flush(flushToDisk: true)`. Periodic `FlushAsync` is intentionally weaker: it
makes already written rows available to external readers without claiming
power-loss durability or flushing the OS's own caches to storage media.

## Resource lifetime and test seam

One writer owns one `PeriodicTimer`, one cancellation source, at most one
retained channel-readiness task, and at most one retained timer-readiness task.
The losing `WhenAny` task is retained rather than recreated each tick or record.
On writer exit, including I/O failure, both retained waits are cancelled and
observed before disposal. The timer is disposed on leaving its writer scope.
No independent timer callback touches the file. A caller's shutdown timeout
still does not cancel ongoing file I/O or promise the writer has terminated;
it only bounds the caller's wait, as before.

An optional **internal-only** `Func<string, FileStream>` constructor parameter
allows a real `FileStream` subclass to gate `FlushAsync` deterministically. The
factory is called only by the writer at file creation; production uses the
original constructor/options. Normal durable flush/rotation behavior is not
abstracted away or weakened.

No unconditional session-start record was added: existing native probes and
schema-v1 consumers rely on their fixed operation sets/counts. Low-frequency
command entry/checkpoint evidence belongs to the separate causal-command work.
An unclosed scope still has no serialized operation row; this change preserves
completed earlier evidence, not a fabricated success for the unfinished scope.

## Verification actually performed

Environment: Windows, .NET SDK `10.0.400`, Release, warnings treated as errors.

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release `
  -p:TreatWarningsAsErrors=true --filter 'FullyQualifiedName~Telemetry' `
  --logger 'console;verbosity=minimal' `
  --results-directory .cache/telemetry-prefix-tests
```

Result: **24/24 passed**, including five new recoverable-prefix tests and the
existing disabled/privacy/schema/parentage/drop/admission/normal-terminal tests.
An initial parser fixture used xUnit exact `Throws<JsonException>` for the
runtime's derived `JsonReaderException`; changed to `ThrowsAny<JsonException>`
without weakening the malformed-data requirement, then the full focused suite
passed.

| Test | Observed assertion |
| --- | --- |
| Idle small trace with unfinished Save | An externally opened shared handle reads one valid completed edit row before shutdown; no session terminal row exists yet |
| Controlled forced kill | Owned `dotnet vstest` child runs only the selected test, holds Save unfinished, emits one edit row; parent observes that exact valid row, kills only its created process tree, and observes identical prefix afterwards; no Save or session terminal is invented |
| Broken destination | A real file occupies the intended directory; producer flood completes within its broad safety deadline, writer reports fault, no JSONL exists |
| Controlled slow flush | Real FileStream subclass blocks its consumer FlushAsync; 100,000 producer calls return, bounded queue drops are counted, caller shutdown budget returns while flush remains blocked; after release, drop aggregate and final session row are retained |
| Trailing-line interpretation | Only the non-newline-terminated last physical row is ignored; malformed complete earlier or final rows throw rather than hiding corruption |

All subprocess/trace/fixture artifacts stay under repository `.temp/tests` and
results under `.cache`. The child-only environment switch is set solely on the
owned subprocess. The test supplies broad scheduling safety deadlines (5-30 s),
not a measured 250 ms latency assertion or p95/p99 benchmark.

## Interpretation limits and integration

A retained valid prefix proves those rows reached an externally readable OS
file state before the owned process was killed in this controlled local test.
It does **not** prove callback absence after its last row, complete operation
coverage, no event loss, power-loss durability, or that a blocked native Save
callback has been instrumented. Missing terminal root means censored evidence,
not clean success. A trailing incomplete row is likewise evidence truncation.

This local run does not certify macOS/ARM behavior, Native AOT performance,
physical UI latency, or the existing Mac Save routing failure's cause. Hosted
cross-platform execution remains an integration check for the parent workflow.
There are no distribution or enabled-tracing overhead claims here.

## Primary references

- [FileStream.FlushAsync](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flushasync?view=net-10.0): drains .NET buffers, not OS intermediate caches; durable flush requires `Flush(true)`.
- [PeriodicTimer.WaitForNextTickAsync](https://learn.microsoft.com/en-us/dotnet/api/system.threading.periodictimer.waitfornexttickasync?view=net-10.0): one consumer at a time; ticks coalesce, and cancellation affects the current wait rather than disposing the underlying timer.
