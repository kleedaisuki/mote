# Lean macOS Grid ShowMenu first pair

Date: 2026-10-01. Status: implementation and portable validation; **hosted native execution pending**.

## Question and scope

The original 41-check external product gate still reports AXShowMenu -25205,
while seven separate native controls report success. Those clients/preparations
differ, and the previous control also invokes the action twice during metadata
inspection without admitting a menu. The new C0/P0 experiment asks whether the
same fresh exact-PID native client reproduces that difference without the old
preparation or action warmup. It is not a product behavior fix.

The design and independent reviews are in
`../architecture/mac-grid-showmenu-discriminator.md`,
`../reviews/mac-grid-showmenu-discriminator-review.md`, and
`../reviews/mac-grid-showmenu-first-pair-implementation.md`.
Only C0 then P0 run in one invocation. Both targets/clients are new processes;
the compiled client hash is identical. Broad getter-selector tracing is deferred:
`dispatcher_observation_available=false`, `server_calls=[]` means unavailable,
not absence of framework getter traffic.

## Implementation contract

- `tests/MacGridShowMenuDiscriminator/{Client,Control}.m` implement the shared
  client and standalone runtime-B control. No Metadata/NSInvocation, direct
  action invocation, selector-permission warmup or 150-ms scheduled cancellation
  exists in this control.
- Client discovery is bounded generic Children BFS through the owned window,
  pruning Table/Row/Column descendants. Only fixed role/identifier classification
  is retained; no source values or menu contents are queried. Unsupported
  Children denotes a leaf; other discovery failures remain unavailable.
- The sole action result is atomically written **before** the post-reply audit.
  A retained Table receives one bounded rediscovery/CFEqual and parent/window
  audit; exhaustion produces unknown. No fallback action or input is permitted.
- Product observation is internal, disabled by default, gated by both Grid AX
  and `MOTE_NATIVE_GRID_SHOWMENU_DISCRIMINATOR=1`. The session is an existing
  absolute no-link repository `.cache` descendant. Ordinary launches have no
  timer, diagnostic file I/O, or extra registered diagnostic selectors.
- Action-entry/refusal facts are fixed preallocated fields, including unknown
  off-main owner fields. AX callbacks do not format JSON or perform I/O. Each
  action/lifetime row array is capped at 16 with separate total/overflow facts.
- Owner readiness checks actual visible attachment/frame/menu availability,
  without invoking the action or reserving the queue. Control frame availability
  refers to its initialized synthetic frame, not a product document frame.
- A fixed finish marker is emitted after the original reply and audit, except
  no-action unresolved cleanup (ordering flags null). Hung action emits none.
  Default/tracking-mode timers consume it, cancel only the owned menu, preserve
  its original did-close notification, and defer window closure to default mode.
  Pending admitted requests are cancelled without clearing the shown-menu
  relation. Timers and delayed cleanup callbacks are invalidated on disposal.
- Server `normal_shutdown` is an owned normal-close/stop fact, not independently
  proven process success. Driver actual owner exit, forced cleanup and unchanged
  fixture hash must be checked separately.

Ready/finish polling is at most 20 attempts separated by 150ms within the 12s
ceiling; the client has 55s overall, 1s per-element messaging timeout and 12,000
API admissions. Discovery caps are 256 nodes/depth12/Children128/action names16.
The single post-reply audit is additionally bounded by 3s/128 admissions. Outer
owned-process watchdog is 75s with at most 10s owner-exit wait. Neither cooperative
AX timeouts nor timer scheduling are hard native execution guarantees.

`Run.ps1` verifies the published product directory contains exactly one binary;
synthetic helpers/reports remain in repository `.cache`, not the payload. It
drains/discards target/client streams, validates fixed JSON keys/enums/types,
and preserves raw interrupted action snapshots separately from final reports.
Compiler-only failure logs are bounded and collected before any target launch.
The existing 41-check gate and seven-control diagnostic remain unchanged.

## Portable validation (Windows)

Commands run from `D:\Code\mote`, .NET SDK10.0.400:

```powershell
dotnet build src/Mote.Native/Mote.Native.csproj --no-restore -v:q
dotnet test tests/Mote.Tests/Mote.Tests.csproj --no-build --no-restore `
  --filter 'FullyQualifiedName~MacGridPairObservationTests|FullyQualifiedName~MacGridMenuDiagnosticTests|FullyQualifiedName~MacGridMenuPresentationTests' `
  --logger 'trx;LogFileName=focused.trx' --results-directory .cache/tests/mac-grid-pair -v:q
pwsh -NoProfile -File tests/MacGridShowMenuDiscriminator/Test-Guards.ps1
```

Build: zero warnings/errors. Focused tests: **24/24**, including five new
off-main/unknown/overflow/path/ABI facts tests. TRX:
`.cache/tests/mac-grid-pair/focused.trx`. Driver guards: **27/27**; fixture is
263,760 UTF-16 units, 1,100 records, ragged record2, with the same hash/oracles as
the original external fixture. Workflow YAML parses successfully.

These checks do not compile Objective-C, load AppKit, validate menu tracking,
establish either original AX reply, or measure observer overhead. Both Mac RIDs
must execute the non-gating first-pair step and have their raw reports audited.
`completed-failure-observed` is valid evidence collection of a failing original
reply, never product `passed`. No VoiceOver, physical input, IME, arbitrary-file,
release or causal-fix claim is made before those observations.

## External grounding

Apple documents that a timer can be registered in multiple modes and that
`invalidate` removes it from all installed modes:
[NSRunLoop addTimer:forMode:](https://developer.apple.com/documentation/foundation/runloop/add(_:formode:)-392ag?language=objc).
The [Threading Programming Guide run-loop chapter](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/Multithreading/RunLoopManagement/RunLoopManagement.html)
distinguishes default and event-tracking modes. The product resolves exported
framework mode constants instead of guessing NSString wire values. These
documented mechanisms justify the cleanup design, not an unexecuted guarantee
that hosted AX transport returns before a tracking loop starts. The design's
failure-preserving reduction rationale remains the prior IEEE TSE2002 evidence;
no speculative product bridge or getter change is introduced.
