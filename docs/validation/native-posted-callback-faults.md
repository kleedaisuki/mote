# Native posted UI callback fault boundary

Date: 2026-10-01. Scope: queued `Action` callbacks only, not certification of
every native callback, selector, input route, or disk transaction.

## Problem and implemented contract

The source-backed exposure is documented in
[`native-causal-save-final-review.md`](../reviews/native-causal-save-final-review.md):
Windows startup and `WM_APP` drains invoked queued delegates directly; macOS
`DrainPosted` used `Notify`, whose primary catch could itself throw while
obtaining the exception message or displaying `ShowError`.
Save-specific completion already contains its own failures and is unchanged.

Both Windows drain sites now share `DrainPosted`. macOS uses `NotifyPosted`
only for its existing posted queue. Each invokes `NativePostedCallback.Invoke`
exactly once. Success returns null without clocks, telemetry calls, allocations,
or new queue envelopes. A nonfatal fault returns the **original exception** for
immediate optional presentation; the helper does not retain or serialize it.
Later queued items continue. `Report` attempts the platform reporter once and
contains secondary nonfatal exceptions. `OutOfMemoryException` is excluded
from primary, reporting, and observability containment. No retries, global
WndProc catch, synchronization changes, or rollback semantics were added.

Windows retains its existing path-free `ReportCallbackFailure("Posted", error)`
and preserves an existing actionable status notice. That reporter already
absorbs failures from its optional status update; these absorbed failures are
not retrospectively observable as `report_failed`. macOS retains its existing
error-message presentation, but message-getter and `ShowError` faults cannot
escape this posted boundary. Exception text is **not** read by telemetry.

## Evidence semantics and privacy

Schema remains 1; two event enum values are appended after existing values:

| Fixed operation | Meaning |
| --- | --- |
| `native.posted.callback.failed` | A dequeued callback threw a nonfatal managed exception. |
| `native.posted.callback.report_failed` | Its optional reporter threw a nonfatal exception that reached the helper. |

Both are independent current-session children with status `failure`, duration
zero, and empty attributes. They deliberately ignore ambient Activity context.
They do not identify a Save request, infer disk failure, create a duration, or
prove callback absence when missing. No path, exception Message/Data/HResult,
or source contents are persisted. A telemetry failure is optional and contained
under the same nonfatal policy. The private session-checkpoint writer is shared
with the existing five menu events; both public APIs keep separate closed
vocabularies and outcome semantics. The strict NativeAcceptance operation
allowlist includes only the two new fixed names.

## Retained local verification

Environment: repository Windows host, .NET 10, Release managed test execution.
No real windows, clipboard, native selector, or system configuration changes.

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release `
  --filter "FullyQualifiedName~NativePostedCallbackTests|FullyQualifiedName~NativeMenuObservationTests" `
  --logger "trx;LogFileName=posted-callbacks-final.trx" `
  --results-directory .cache/validation/posted-callbacks `
  -p:PublishAot=false -p:TreatWarningsAsErrors=true --no-restore
python -B -m unittest discover -s benchmarks/NativeAcceptance -p test_acceptance.py
```

Results: **20/20** C# cases passed, zero failed/skipped; TRX retained at
`.cache/validation/posted-callbacks/posted-callbacks-final.trx`.
NativeAcceptance **7/7** existing reader cases passed.

The six new helper cases verify ordered continuation after primary and reporter
faults, original exception identity, one invocation/no retry, exact persisted
bytes surviving a later UI fault, successful reporting once, enabled-tracing
success emitting no records and allocating zero warmed thread bytes across
10,000 invocations, hostile Message/Data accessors remaining unread, independent
session ancestry inside an ambient Save, empty payload/duration, fatal exclusion,
and appended event IDs/closed API vocabulary. Existing menu ancestry/default-off
tests ran once because their internal session writer changed.

These are portable executable helper tests and source-backed adapter wiring,
not actual fault-injected user32/AppKit runtime evidence. Four-RID AOT and hosted
native runs remain integration checks owned by the root agent.
