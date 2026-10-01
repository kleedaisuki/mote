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

Windows uses the shared path-free `SetCallbackFailureNotice("Editor")`
installer and preserves an existing actionable status notice. Posted reporting
calls that installer directly through `NativePostedCallback.Report`, so a
secondary nonfatal notice fault reaches the shared guard and emits
`report_failed`. The compatibility `ReportCallbackFailure` wrapper still
absorbs optional notice faults for nonposted callbacks, preserving their
existing behavior. This closes the initial implementation's hidden secondary
posted-fault gap without duplicating notice policy. macOS retains its existing
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

## Windows reporting follow-up

The shared notice-installer extraction has two additional portable cases using
the actual Windows shell before any window is created. They enqueue a throwing
callback and later successful callback, then invoke the real drain. They verify
exactly-once continuation, the fixed generic Editor notice, preservation of an
existing actionable notice, and primary-only failure telemetry on successful
reporting. Zero handles ensure `Post` and `RenderStatus` return without native
calls. The existing enabled-tracing helper test injects a throwing reporter and
certifies the secondary failure event; no artificial production fault seam was
added to force `SetStatusNotice` to fail.

Follow-up verification: Release `NativePostedCallbackTests` with
`PublishAot=false`, `TreatWarningsAsErrors=true`, and `--no-restore` passed
**8/8**, zero failed/skipped. Retained TRX:
`.cache/validation/posted-callbacks/posted-callbacks-windows-notice.trx`.
The shared session writer and menu implementation are unchanged in this
follow-up, so the already-passing menu cases were not repeated.

## Owned AppKit queue fault control (hosted execution pending)

`MacPostedCallbackProbe` extends the existing non-gating in-process Mac Flow
diagnostic, without a new CLI mode, workflow, production fault hook, or tracing
configuration. Once the actual shell is shown, the diagnostic queues one
throwing callback through `MacEditorShell.Post`, followed by its ordinary
successful Flow check. The first callback throws a nonfatal custom exception;
its `Message` getter increments a separate counter and throws a second
nonfatal exception. The actual AppKit `moteDrainPosted:` selector must contain
both faults and continue to the later queued item.

The later item requires **one** primary callback invocation and **one** message
getter access before running its existing checks. Duplicate queueing fails
instead of retrying. A successful counter check prints the fixed marker
`Mac posted callback primary/report fault containment passed.` The getter
throws before `ShowError` can construct an `NSAlert`, so there is no modal
dialog, key posting, clipboard access, input-source mutation, or injected
exception text in telemetry. Production guard code is unchanged.

This control distinguishes actual created-AppKit queue continuation from the
portable helper and uncreated Windows-shell checks above. Native AOT
compilation alone does **not** establish that continuation occurred. Actual
execution on both macOS RIDs remains pending in the next hosted non-gating Flow
run; record the fixed marker and original process exit before upgrading this
claim. The control does not certify telemetry emission (tracing stays at its
existing setting), native Objective-C exception containment, fatal recovery,
Windows dispatch, wake completeness, external input, or performance.

Local verification on the repository Windows host: `dotnet build
src/Mote.Native/Mote.Native.csproj -c Release -p:PublishAot=false
-p:TreatWarningsAsErrors=true --no-restore` succeeded with zero warnings and
zero errors. This verifies managed compilation of the new helper, not native
Mac execution or the subsequently integrated Flow call sites. Source checks
confirm that the hostile getter throws during argument evaluation, before
`ShowError`, and that the existing posted guard excludes out-of-memory faults;
the injected exceptions are ordinary `Exception`/`InvalidOperationException`.

## Actual created-AppKit continuation — CI 36836309613

The previously pending created-shell control now executes on **both** Mac Native AOT RIDs at exact source `b4093b84e8e242a00d99fb3e3c8ef0249d24a467`, [CI 36836309613](https://github.com/kleedaisuki/mote/actions/runs/36836309613). Completed x64 job 110284447321 at 08:30:27.620 UTC and ARM64 job 110284447436 at 08:31:40.150 UTC each print `Mac posted callback primary/report fault containment passed.`, then the local-monitor control marker and `mote-native-mac-flow-rendering-ready`. Thus the real created AppKit queue contains the primary callback exception and the separately throwing Message getter, checks exactly one invocation of each, and continues to the subsequent ordinary Flow work.

The actual Flow diagnostic step concludes success for both RIDs. At this audited source it is **non-gating** (`continue-on-error: true`); a green AOT job is not the proof. The pinned PowerShell script directly invokes the binary, captures `$LASTEXITCODE`, and requires exit 0 plus exact Flow-ready output. This enforces original process exit 0 but does not separately serialize a numeric-exit report. Any later blocking-step promotion is not retroactive evidence here. Raw logs, step metadata and pinned script are retained in `.cache/ci-36836309613-input-evidence/`; [the accompanying hosted input audit](native-local-input-monitor.md#hosted-first-runtime-verification--ci-36836309613) records the independent ordinary 8/8 Save/reopen and 16/16 recovery regressions.

This closes the specific **created-AppKit primary/report exception containment and later queue continuation** evidence gap, not Windows created-target injection, native Objective-C exception handling, fatal recovery, wake completeness, telemetry emission, physical input or performance. Tracing remains disabled in this in-process control. Historical ordinary Mac Save failures and other non-gating product gaps remain open.
