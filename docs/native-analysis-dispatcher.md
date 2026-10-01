# Native analysis dispatcher and demand Full

## Contract and ownership

`NativeAnalysisDispatcher` is internal and belongs to one document lifetime. It
borrows (does not dispose) the existing `NativeFormatSessionDriver`. The controller
must route interactive source/Grid analyses and the optional idle lane through
the same dispatcher. Direct driver users retain the existing API and behavior.

```csharp
using var dispatcher = new NativeAnalysisDispatcher(driver);
var content = dispatcher.AnalyzeAsync(snapshot, request, editToken,
    gridRequest, content: true, delayMilliseconds: 80);
var viewport = dispatcher.AnalyzeAsync(snapshot, request, deliveryToken, gridRequest);
var full = dispatcher.ReserveFullAsync(snapshot, visibleRange, indexingToken);
```

Content is registered immediately after an edit, not after an independently
restartable navigation debounce timer. Its monotonic due time is captured once.
A viewport replacement replaces only the viewport slot and never resets that
content deadline. NativeIdleFullAnalysis accepts an optional trailing dispatcher;
without one its previous direct-driver behavior remains compatible.

## Bounded service and retained state

One worker pump owns one dispatched driver turn. Queued state consists only of
latest mandatory content, one Full reservation, and latest viewport. Replaced
queued interests finish as canceled without constructing a driver Task. Each
caller receives a completion Task, but there is no worker Task/semaphore waiter
per input event and no retained input-event queue.

At a stable version: current turn retires, latest mandatory content runs (waiting
only its already captured edit deadline), reserved Full runs, then latest viewport.
This is a turn-count guarantee, not a wall-clock parser latency guarantee. Caller
must classify only real content changes as `content:true`; navigation must not
manufacture more mandatory content turns. Newer versions cancel the running turn
asynchronously, retire queued old-version work and invalidate old Full reservation.
Old-version submissions are rejected. Dispose retires the same lifetime domains
without blocking native dispatch, and does not dispose the borrowed driver.

Full admission is repeated immediately before its driver turn. The default check
uses NativeIdleFullAnalysis.HasMemoryBudget and current GC load. A refusal throws
NativeFullAnalysisDeferredException instead of leaving a silent permanent pending
reservation. This is advisory memory admission, not an OOM guarantee.

## Idle/demand outcomes

`Demand(snapshot, visibleRange, retry:false)` shares the idle attempt/version.
It synchronously reserves Full when a dispatcher is present and wakes an existing
quiet timer, eliminating a reservation gap caused by async timer continuation.
Demand does not cancel/restart same-version certification. Full completion retains
policy completeness: a provisional policy result is not relabeled Complete.

Deferred dispatch and parser failure retain explicit Deferred/Failed outcomes,
and invoke the existing error observer so the controller can show the refusal.
Normal navigation cannot clear those outcomes. Explicit Retry clears only a
refused/failed attempt and rechecks admission; actual Complete or already-attempted
provisional Full is not re-run. Cancel is reserved for real version/lifetime changes
or explicit cancel indexing; a canceled attempt may be offered again.

## Verification (2026-10-01)

Repository-local Release tests:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --filter 'FullyQualifiedName~NativeIdleAnalysisTests|FullyQualifiedName~NativeAnalysisDispatcherTests' --no-restore --nologo -v quiet
dotnet build src/Mote.Native/Mote.Native.csproj -c Release --no-restore --nologo -warnaserror -v quiet
```

Result: 12/12 tests passed; Native build 0 warnings/0 errors. New tests cover
current/content/Full/latest viewport ordering, independent latest viewport
coalescing, single active driver call, edit cancellation of reserved Full,
dispatch-time memory refusal, quiet timer promotion, terminal complete outcome,
and explicit retry after Deferred/Failed. Existing idle tests remained unchanged and
passed. The 80ms debounce is an implementation input, not a measured latency
claim; tests use parser turn barriers and timeout waits only as deadlock guards.
Native scrollbar delivery and target-platform GUI acceptance remain separate.
