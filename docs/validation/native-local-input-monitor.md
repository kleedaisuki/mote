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

The existing non-gating Mac Flow diagnostic command now runs `MacLocalInputMonitorProbe.Verify`:
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

## Hosted first runtime verification — CI 36836309613

Date: 2026-10-01. Exact source `b4093b84e8e242a00d99fb3e3c8ef0249d24a467`, [completed CI 36836309613](https://github.com/kleedaisuki/mote/actions/runs/36836309613). This section upgrades only the pending native control and ordinary-route positive evidence above; it does not erase earlier Mac Save failures or certify reliability.

All ten jobs conclude success. Actual Windows and macOS strict logs each report **Mote.Tests 1369/1369, Themes 14/14, Configuration 9/9**, zero failed/skipped tests. All four single-binary AOT delivery checks and three strict native clipboard jobs succeed. Evidence is retained under `.cache/ci-36836309613-input-evidence/`: completed run/step metadata, strict and four AOT logs, artifact listing (`per_page=100`, all 87 artifacts), four ordinary reports and summaries, all 16 ordinary traces, all 16 recovery traces, and independent reparse/count outputs. The checks use `python -B`; no experiment, production edit or push was performed.

### Actual native control, not a retroactive CI gate

At this source, `Diagnose native macOS Flow rendering (non-gating)` has `continue-on-error: true`. Its **actual step conclusion is success on both Mac RIDs**, and each completed log contains exactly one of all three required observations:

| RID / completed job | Runtime marker time (UTC) | Observations |
| --- | --- | --- |
| osx-x64 / 110284447321 | 08:30:27.620 | `Mac posted callback primary/report fault containment passed.`; `Mac local input monitor global Block ABI/install/remove control passed.`; `mote-native-mac-flow-rendering-ready` |
| osx-arm64 / 110284447436 | 08:31:40.150 | Same three markers |

The pinned `tests/NativeMacFlowRendering.ps1` runs the executable directly with `&`, captures `$LASTEXITCODE`, and throws unless exit is 0 **and** the exact ready marker is present. The successful step thus enforces original process exit 0; it is **not a separately retained numeric-exit JSON field**, and no explicit `WaitForExit` implementation is claimed. Green AOT job conclusion alone would not establish this control. This first actual two-RID success proves the scoped system Block ABI/copy/release, install/remove and synthetic pointer-return controls described above, not physical/external input delivery or every AppKit tracking-loop route. Any later promotion to a blocking step is a different workflow/source.

### Independent ordinary-route evidence

All eight ordinary 1/100 MiB JSON samples pass exact saved-byte/hash, unchanged original fixture, complete diagnostic/version, fresh reopen and explicit numeric editor/reopen exit **0/0** contracts. Independent complete-prefix parsing classifies exactly one successful native Save request with captured snapshot version **1** per edited trace; reopen traces contain no Save request. Row counts:

| RID | Edited trace rows, 1 / 100 MiB | Reopen rows, 1 / 100 MiB |
| --- | --- | --- |
| win-x64 | 56 / 61 | 13 / 18 |
| win-arm64 | 56 / 64 | 13 / 18 |
| osx-x64 | 68 / 74 | 14 / 19 |
| osx-arm64 | 71 / 74 | 14 / 19 |

Each of the **four Mac edited traces** has the following six-operation input inventory; vectors are **success / failure / cancelled / skipped**. Both fresh reopen sizes/RIDs have the same ready/removed vectors but candidate `[0/0/0/0]`.

| Fixed operation | Each edited Mac trace |
| --- | --- |
| `native.input.monitor.ready` | `[1/0/0/0]` |
| `native.input.monitor.unavailable` | `[0/0/0/0]` |
| `native.input.monitor.callback_failed` | `[0/0/0/0]` |
| `native.input.monitor.removed` | `[1/0/0/0]` |
| `native.input.monitor.removal_failed` | `[0/0/0/0]` |
| `native.input.save_family_candidate` | `[1/0/0/0]` |

`check_input.py` independently verifies all eight Mac traces' raw input/menu rows: empty attributes, success status, zero duration and parent identity exactly their own normal session root; all six-by-four edited counters exactly match each raw report. Each edited Mac menu inventory separately has ready 1, entry 1, returned-true 1, unavailable/returned-false 0. Windows input/menu inventories remain unobserved, appropriate to Mac-only instrumentation. The new candidate rows are **actual ordinary external-route target-local positives**, distinct from the synthetic ABI control. They do not establish event identity, pairing, physical key receipt, candidate-to-menu or menu-to-request causal edges. Those edges remain explicitly `unknown`, request correlation `none`, absence certification false.

Both Mac completed logs actually render all six input counter vectors, normal-exit-observed boundary, explicit exits and Save chain, plus the unknown-edge/no-absence warnings in their Markdown summaries; this is not JSON-only storage. The summaries still expose original Grid **40/41 / Swift exit 1**, and C0/P0 **AX 0 / -25205** despite owner/client exits 0. Windows ARM Continuous and many-100MiB remain inconclusive. No ordinary sample is censored in this run; prior censored prefixes retain their interpretation, and normal zero/no-observed-drop counters cannot certify negative callback execution or loss-free coverage.

All **16 synthetic recovery controls pass** their reports and independent existing-contract reclassification: each RID has held/normal/receipt-save/receipt-save-as rows **5/12/1/1**; normal classifies success and exits 0; the other three classify censored and exit 1 on Windows / -9 on Mac. Recovery is infrastructure evidence, not ordinary persistence. This successful ordinary run is not a Mac reliability estimate or root-cause fix for CI 36831903238. New Mac per-key monitoring startup/edit overhead, physical IME and physical display endpoints remain unmeasured here.

### Blocking-gate execution followup — CI 36837499493

[CI 36837499493](https://github.com/kleedaisuki/mote/actions/runs/36837499493), exact source `a33c5ca8451081aa9a21a8589d832c1a12ea0637`, completes with all ten job conclusions success. The newly promoted **blocking** `Verify native macOS Flow and callback ABI` step actually runs and concludes success on **both** Mac RIDs: x64 job 110288371050 prints both fixed posted-fault and local-monitor ABI markers plus Flow-ready at 08:45:42 UTC; ARM64 job 110288371096 does so at 08:44:20 UTC. This source has no `continue-on-error` on that step; the unchanged direct-invocation script still enforces original process exit 0 plus ready. This is actual execution of the new gate, not retroactive promotion of the preceding non-gating run. Actual Windows/macOS strict logs remain **1369/1369, Themes 14/14, Configuration 9/9**, no failed/skipped tests; all four AOT jobs succeed.

Selected new evidence is retained in `.cache/ci-36837499493-flow-gate/` (completed metadata, strict/AOT logs, four ordinary reports and inventories, artifact listing with `per_page=100`). New ordinary reports retain **8/8 pass**, numeric editor/reopen exits **0/0** for both sizes/all RIDs. Each of four Mac edited samples reports input ready/candidate/removed success counts **1/1/1**, all other input outcome counts zero, normal-exit-observed boundary, absence certification false and both causal edges unknown. Windows input remains unobserved. No ordinary failure or report contradiction calls for deep trace reanalysis; the preceding b4093b8 raw ancestry/byte/version and recovery audit is reused, not repeated or claimed anew. Both Mac summaries still show Grid failure and the C0/P0 failure observation; Windows ARM Continuous/many100 remain inconclusive. The win-x64 log additionally retains two actual exit-1 diagnostics: historical external UIA canvas and external Grid subset; these are `continue-on-error` diagnostics, not strict-suite or promoted-Flow failures, even though job/step API conclusions normalize to success. Node runtime warnings are orthogonal, not evidence of a product failure or reason for an unassigned dependency upgrade. Historical Mac Save uncertainty and unmeasured monitoring cost remain open.
