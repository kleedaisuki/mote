# Native local input monitor: ABI, ownership and evidence contract

Date: 2026-10-01. Implementation is locally built and portable contracts tested;
actual macOS x64/ARM64 AOT control and ordinary external-route positives remain
pending. No local UI/input mutation experiment or new workflow was added.

## Why this second permanent boundary exists

The audited menu-only [CI 36831903238](https://github.com/kleedaisuki/mote/actions/runs/36831903238),
source `a13a9b0`, retains a macOS x64/100 MiB forced-exit prefix of 32 complete
rows / 11,167 bytes, menu-ready 1, menu entry/return 0 and Save requests 0.
[The retained audit](native-menu-observation-hosted.md) does not infer callback
absence. A passive earlier target-local boundary can provide an independent
positive checkpoint without changing Save dispatch, input, activation or retries.

## Verified primary-source basis

* [Clang Apple Block ABI](https://clang.llvm.org/docs/Block-ABI-Apple.html): a
  no-capture global Block uses the **address** of `_NSConcreteGlobalBlock`, a
  leading isa/flags/reserved/invoke/descriptor literal and optional signature
  descriptor field. Captured state would require different lifetime machinery.
* [Apple event-monitor guide](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/EventOverview/MonitoringEvents/MonitoringEvents.html):
  local handlers return an event, run on the main thread, and require explicit
  removal. Returned tokens are not owned. Mote deliberately takes one additional
  retain, then releases **only its own reference**, never the borrowed return.
* [Apple local-monitor API](https://developer.apple.com/documentation/appkit/nsevent/addlocalmonitorforevents(matching:handler:)):
  the local application boundary excludes events consumed by tracking loops;
  absence of a row cannot certify global input absence.
* [dotnet/macios production BlockLiteral](https://github.com/dotnet/macios/blob/main/src/ObjCRuntime/Blocks.cs):
  the native prefix has the same field order, native symbol lookup supplies the
  isa address, and typed native trampolines accept the Block as the first argument.
  Its general captured-delegate/GCHandle machinery is **not copied** into mote.
  Mote's no-capture global uses an explicit Native AOT unmanaged function pointer.

Source inspection was performed against these primary sources on 2026-10-01.
Mote adds no packaged native library, copied runtime, plugin, dynamic managed
assembly, app subclass, swizzle, event tap or global monitor.

## Shipped storage and lifecycle

| Item | Contract on both supported 64-bit macOS RIDs |
| --- | --- |
| Literal | 32 bytes: isa@0, flags@8, reserved@12, invoke@16, descriptor@24 |
| Descriptor | 24 bytes: reserved@0, literal-size@8, signature@16 |
| Flags | `(1 << 28) | (1 << 30)`; global plus signature, no capture helpers/stret |
| Signature | NUL-terminated `@16@?0@8`: object return, Block argument, event object |
| Invoke | `nint invoke(nint block, nint event)`, C ABI; `UnmanagedCallersOnly` |
| Memory | Stable unmanaged literal/descriptor/signature, intentionally process-lifetime |
| Runtime | System `/usr/lib/libSystem.B.dylib` symbol address; no sidecar |
| Owner | One nullable owner per admission slot; production slot lazy and process-local |

Default-off returns before allocating an owner, native API, admission slot or
Block storage; no monitor is installed and no per-key managed callback is added.
The native API has an explicit static constructor, preventing beforefieldinit
from weakening that contract. Enabled setup occurs after the owned main-menu
installation. A key-down-only mask is installed. Event classification shares the
menu's native filter: metadata first, one UTF-16 unit only for fixed s/S candidates,
no managed string, no event identity/content persisted.

The owner reserves admission before Add, takes one retain on its opaque token,
and emits ready only after setup completes. Nil/Add/retain failures remain
optional instrumentation failures and do not block editor construction. Cleanup
of a known token happens once. Observation is passive before remove/release and
before shell views/autorelease pool/telemetry are torn down. Remove or release
failure retains the passive owner and forbids all later reinstallation. No retry
can create a duplicate observer. Objective-C exceptions are not assumed to be
catchable as managed exceptions; documented native APIs are used with valid types.

Portable tests use a private admission holder, **not** the production singleton.
They exercise the exact owner algorithm without poisoning a shared testhost or
adding a production reset API. Nonfatal classify/queue failures never change the
borrowed event's return pointer. No event, ambient Activity or producer lease
survives the callback.

## Evidence and retained validation

`MacLocalInputMonitorTests` checks admission, default-off, nil/Add/retain failure,
exact event return, observation containment, passivation before removal,
exact retain/remove/release counts, no double cleanup, and passive permanent
admission after teardown failure. It independently checks literal/descriptor
sizes and field offsets. These portable checks **do not execute AppKit**.

The existing strict Mac Flow command now runs `MacLocalInputMonitorProbe.Verify`:
actual system `_Block_copy`/`_Block_release`, literal/descriptor/flags/isa/signature
inspection, typed nil and nonnil synthetic s/S/x pointer identity before/during/
after real local monitor Add/Remove, and an explicit `RemovedSuccessfully`
readback before its fixed success marker. No event is posted. This is an ABI and
API control, **not ordinary input delivery**. The existing diagnostic CLI returns
before configuration/telemetry initialization. The control explicitly rejects an
already enabled sink; no synthetic checkpoint is classified as external receipt,
and a traced in-process Flow control is not a supported route.

The ordinary JSON benchmark remains a separate process with one existing
external Command-S attempt per sample. New input inventory and CI Markdown
rendering retain six operations in success/failure/cancelled/skipped order,
separate from menu inventory and Save request chain. All checkpoints have empty
attributes and session parents. Candidate-to-menu and menu-to-request edges stay
unknown; normal no-observed-drop sessions are not coverage certificates. Censored
prefixes can preserve positives but never certify negative callback execution.

Hosted checks on **both** Mac RIDs must demonstrate the ABI marker and retained
ordinary candidate positives before coverage can be claimed. Startup/editing
overhead on the new Mac binary remains an explicit measurement gap; previous
Windows telemetry measurements do not certify this new AppKit per-key callback.

### Local retained contract results

* Native Release managed build: `dotnet build src/Mote.Native/Mote.Native.csproj
  -c Release --no-restore -warnaserror`, zero warnings/errors.
* Portable monitor tests: `dotnet test tests/Mote.Tests/Mote.Tests.csproj --filter
  FullyQualifiedName~MacLocalInputMonitorTests --no-restore --verbosity minimal`,
  **4/4 passed** after admission isolation (`498f88f`); warmed production-disabled
  `TryInstall` loop of 2,000 calls records **0 managed allocation bytes**.
* Telemetry/input/menu/posted focused contracts **25/25**, generic acceptance
  **8/8** (`4d3f03c`); Native JSON inventory **43/43** (`a5e93ac`); CI summary
  **39/39** (`717bd8b`). These are portable retained fixtures, not Mac runtime
  measurements or ordinary-route positives.
* Source integration additionally ran a 24-case input/menu/posted/monitor subset
  before holder isolation; that older run is not substituted for the final
  isolated monitor tests. `git diff --check` reports no whitespace errors.
