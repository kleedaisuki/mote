# Relaxing mote's delivery constraint: packaging, runtime and editor surface

Date: 2026-10-02. Status: integrated research and local runtime mechanism study; **not approval to
change the active delivery contract, default UI or one-document product**.
Baseline source: `6827cd1a19142c9ad766d296c18d0770e600e79c`. Native product
integration is changing concurrently; a working-tree implementation is not a
qualified published alternative.

## Decision question and scope

The user requests a research group to assess (1) allowing multiple delivered
files and (2) allowing multiple files with a bundled runtime, including
performance, user experience, migration cost and feasibility. Here "files"
means application delivery artifacts, **not multiple source documents,
workspaces, project indexing or language servers**. That interpretation follows
the prior strict-binary discussion; it should be stated explicitly in a user
decision rather than silently broadening the editor's scope.

The central distinction is that these are three independent choices:

1. **Delivery shape:** bare executable, application directory, macOS `.app`,
   Windows package/installer, with optional native libraries and resources.
2. **Execution model:** Native AOT, CoreCLR JIT, or CoreCLR plus ReadyToRun.
3. **UI/text model:** retained Win32/AppKit adapters or a cross-platform toolkit
   and text surface such as Avalonia/Skia/AvaloniaEdit.

Native AOT already carries the runtime support required by its compiled app.
"Allow a bundled runtime" therefore does not logically require JIT. Removing
the file-count rule alone does not replace a text engine or make it faster.

## Current evidence, not an imagined starting point

Internal retrieval reused the architecture, strict-binary feasibility, macOS
bare-binary distribution, ordinary editing locus, large-file UI evaluation,
current native publication/import evidence and goal checklist. The main
authoritative checkpoint is [CI 36899695843](https://github.com/kleedaisuki/mote/actions/runs/36899695843):

- Four actual Native AOT products pass single-binary inventory and published
  encoding checks, without requiring a preinstalled .NET runtime.
- Windows/macOS main suites each pass 3508 tests at this checkpoint. This does
  not certify a replacement UI, real IME, screen-reader interaction or latency.
- All four native source **reference experiments** complete controlled editing,
  history, exact saves and fresh in-process Document reopens. This is not
  default-product/fresh-GUI-process acceptance.
- Windows 512 KiB JSON foreground publication still costs approximately
  **5.2–10.3 seconds**; the synthetic 3.54 MiB UTF-8 novel's whole-control import
  and history reimports cost **3.8–4.9 seconds**.
- Mac dense post-edit publication is **228.7 ms x64 / 81.0 ms ARM64**, after a
  semantics-equivalent native attribute update reduced the preceding observed
  **4961.1 / 2731.8 ms**. These are individual hosted phase observations, not
  latency distributions or architecture comparisons.

Sources: [goal checklist](../product/goal-checklist.md),
[Windows publication attribution](../performance/native-source-capability-timeout.md),
[Mac publication mechanism](../performance/mac-native-source-style-publication.md),
[Windows import investigation](../performance/windows-native-source-import.md).
Do not cite the older 74–79-second Windows observation as current performance.

This evidence locates real native text publication/import costs. The identical
RichEdit or TextKit calls do not disappear when CoreCLR is bundled. A claim that
JIT solves them needs stage-level evidence; it cannot be inferred from compiler
mode. Likewise the Mac improvement demonstrates that useful performance
progress is possible without changing either packaging or VM.

## Candidate matrix

| Candidate | Delivery and execution | What actually becomes possible | Dominant remaining work |
| --- | --- | --- | --- |
| Baseline | Strict one Native AOT binary, Win32/AppKit | Existing portability/no runtime installation | Native editing surface, publishing/layout cost, real IME/a11y and release acceptance |
| A: packaging-only relaxation | Native AOT in a directory / `.app`, optional resources or native companions | Standard app resources, bundle identity, conventional distribution; third-party native libraries allowed | Deployment inventory/update atomicity; same existing UI/runtime costs unless independently changed |
| B: cross-platform AOT surface | Native AOT + Avalonia/Skia/HarfBuzz native libraries, macOS backend in `.app` | Stock upstream dependency graph rather than maintaining custom static native builds; more shared UI code | Correct engine/view bridge, input/accessibility acceptance, theme parity, document duplication and long-line behavior |
| C: bundled CoreCLR | Self-contained JIT or ReadyToRun, retained native shell or Avalonia | Wider library compatibility, dynamic code where actually needed, mainstream managed diagnostic tools | Runtime startup/memory/payload/update trade-offs; macOS JIT entitlements and all unchanged UI acceptance |
| D: installed runtime (separate option) | Framework-dependent .NET | Smaller app-specific payload, system runtime servicing | User prerequisite and version/architecture policy; inconsistent with effortless portable opening unless deliberately chosen |

Packaging-only migration is materially smaller than UI migration. AOT with
native companions removes the **historical reason stock Avalonia was rejected**:
the observed Windows AOT graph had an executable plus three native libraries;
macOS adds its native backend. It does not require a maintained static Skia,
HarfBuzz, ANGLE and macOS backend build. Source:
[strict-binary feasibility](../single-binary-feasibility.md).

## Evidence ownership and next integration

The research group uses separate persistent writers:

- [Platform/deployment evidence](delivery-platform-evidence.md): official
  .NET/Apple platform mechanisms, signing, packaging and runtime servicing.
- [UI migration map](delivery-ui-migration.md): current mote boundaries,
  Avalonia/AvaloniaEdit upstream contracts and retained native alternatives.
- [Runtime performance probe](delivery-runtime-performance.md): isolated
  same-workload AOT/JIT/ReadyToRun process experiment and its limited scope.
- [Relevant research frontier](delivery-research-frontier.md): peer-reviewed
  evidence, transfer limits and discriminating experimental methods.

All experiments stay in repository-root `.temp`/`.cache`. No product edit,
shared build, local GUI, OS input/font/locale change, CI dispatch, goal rewrite,
commit or push is authorized by this research assignment.

## Necessary experiment before a product switch

Use two comparisons, not one confounded competition:

1. **Same native shell, same frozen engine/policies, different VM:** Native AOT,
   untrimmed self-contained JIT and ReadyToRun. Identical actual source-ready,
   edit reconciliation, semantic-publication, history, save and fresh-process
   reopen boundaries separate runtime startup from platform text costs.
2. **Same runtime, different text surface:** native adapter versus a corrected
   Avalonia/AvaloniaEdit candidate. Preserve engine ownership, exact bytes,
   selection/version/history, semantic analysis, theme policy and tracing.

The representative tasks are tiny mixed CJK/emoji text, small ordinary
configuration/Markdown files, 512 KiB dense JSON and a 3.54 MiB novel-like file.
Existing capacity/long-line regressions remain safety checks, not assumed
dominant user demand. Run first-start and paired warm runs separately; record
cache definition instead of labeling an unevicted run "cold". Report actual
artifact contents/bytes, sampled memory limits, process exit, source hash and
stage durations. Do not pool differing hosted CPUs, claim p95 from a handful of
runs, or relabel layout/draw-return as physical input-to-pixel latency.

Native/Avalonia real Pinyin composition, cancellation/selection/Undo and
screen-reader navigation need explicit interactive verification. A console
READY marker can test runtime startup, not certify those experiences.

## Migration work packages and cost model

Cost is expressed as concrete engineering/acceptance work, not unsupported
calendar estimates. Agent parallelism reduces serial execution but does not
remove shared contract decisions or native acceptance dependencies.

| Work package | Packaging-only AOT | CoreCLR with same native shell | Avalonia text/UI migration |
| --- | --- | --- | --- |
| Engine, six format policies and exact file I/O | Reuse | Reuse, qualify published mode | Reuse, never replace with toolkit text authority |
| `Mote.Configuration`, `Mote.Themes`, `Mote.Telemetry` | Reuse; keep writable state out of installed app | Reuse; add VM identity to evidence | Reuse policy semantics; rebuild visual bindings and lineage hooks |
| `Mote.Native` shell and source bindings | Same implementation | Same interop, reviewed runtime/delegate lifetime | Replace presentation/input adapters; extract only truly UI-neutral coordination |
| `Mote.Desktop` prototype | Not needed | Optional compatibility reference | Existing XAML/controls are reusable prior art, not release-ready command semantics |
| Publish/package scripts and inventories | New allowed-file manifest, `.app` layout/resources, package identity | Explicit runtimeconfig/deps/managed/native closure; runtime update policy | Toolkit/version/native asset closure for four RIDs and license/security review |
| Runtime tests | Same binary behavior plus new install/activation/dependency cases | Actual AOT/JIT/R2R equivalence and macOS hardened runtime checks | New composition/selection/navigation, screen-reader and theme/renderer acceptance |
| External contracts and migration | Preserve CLI/file associations/config/save | Same plus launch host/runtime independence | Preserve public behavior; existing OS-specific diagnostic routes require explicit retention/deprecation decisions |

Relative engineering scope: **packaging-only is low; switching the VM behind
the same UI is low-to-medium in code but medium in qualification; replacing the
text/UI surface is medium-to-high and carries the largest user-visible risk**.
This is a scope/risk judgment, not a promise of effort duration. Retaining an
old adapter as an experiment reduces discovery cost; maintaining two production
editors indefinitely adds duplication and multiplies acceptance work.

The old Desktop prototype uses full-buffer assignment, clears its local undo
stack, and exposes manual 256 Ki UTF-16 page navigation beyond an 8 Mi-unit
threshold. Its page-local analysis/commands do not meet the continuous global
document contract. Restoring that target as the default would change the end
state rather than complete it. A corrected bridge should apply actual deltas,
keep the engine's sole committed history/version, reject stale results and
preserve scalar/newline coordinates. Full-file duplication and line/layout
indexes still need measured bounds.

Multi-file deployment adds concrete failure modes: missing/wrong-architecture
libraries, mixed versions after partial updates, unsafe DLL lookup, new native
dependency vulnerabilities and macOS nested-code signing. Deliver the application
as a complete versioned unit; never search the user's opened-document directory
for product libraries. Keep mutable configuration/cache/traces under the existing
user-selected `~/.mote` conventions, not under a signed/read-only `.app`.
Sources: [platform evidence](delivery-platform-evidence.md),
[DLL security](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-security),
[Desktop prototype contract](../../src/Mote.Desktop/README.md).

CoreCLR has a legitimate diagnostic advantage. Official guidance documents
optional EventPipe and native CPU profiling for Native AOT but partial runtime
events and no current managed heap analysis in the documented support model.
That is not "AOT has no observability." Keeping a matching managed diagnostic
build may resolve managed behavior cheaply; OS profiles are still necessary for
RichEdit, TextKit and graphics costs. Preserve symbols as CI/private artifacts
even if they are not shipped. Source:
[Native AOT diagnostics](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/diagnostics).

## New measured runtime mechanism (not an editor benchmark)

An isolated .NET 10.0.11 JSON console workload was published three ways using
SDK 10.0.400 on the same Windows x64 host. Each executable reads identical
224,171-byte fixture bytes and validates all 4,096 numeric values, then flushes
the exact same READY milestone before memory instrumentation. The observer
times new-process launch through receipt of that marker, not process exit.
There are 36 processes per mode with six launch orders balanced across modes;
OS caches were not evicted and the host is shared with other project work.

| Publish mode | Runtime files | Uncompressed publish bytes | Launch → READY median | Observed range |
| --- | ---: | ---: | ---: | ---: |
| Native AOT | 1 | 1,675,264 | 10.73 ms | 9.25–13.91 ms |
| Self-contained JIT | 191 | 80,369,503 | 71.69 ms | 66.77–85.52 ms |
| Self-contained ReadyToRun | 191 | 80,384,351 | 58.74 ms | 56.20–64.88 ms |

This establishes a runtime startup/payload trade-off on one headless workload;
it does **not** predict mote's first editable frame, physical paint, toolkit
initialization, font fallback, IME, long-session throughput, memory peak or
macOS behavior. JIT/R2R publications are untrimmed, whereas AOT necessarily
trims; this is a supported default-mode comparison, not equal-dead-code
optimization. File bytes are installed publish payload, not compressed download
sizes. R2R is not JIT-free: it retains CoreCLR and can replace precompiled hot
methods with tiered JIT code.

The group lead independently inspected the 111 retained raw rows (three labelled
setup observations plus 108 balanced observations), recomputed each main median
and range, and enumerated actual final publish-root files/bytes. Those match the
table. JIT/R2R share an identical apphost executable; their managed application
DLL differs, so an apphost hash alone is not complete application provenance.
The full manifest is retained in the probe study.

These publish settings are not a Pareto-optimal search over trimming, composite
R2R, static profiles, GC modes or toolkit dependencies. They answer a cheap
runtime-mechanism question, not "the best achievable CoreCLR editor." No Engine
or six-format policy code is linked into the probe.

The first probe build discovered generated-source glob contamination from
other modes' nested artifacts. It was corrected by explicit `Compile` of the
one owned source; failed logs remain, and those failed builds were not measured.
Full reproducibility, hashes, retained raw samples and measurement limitations:
[runtime performance study](delivery-runtime-performance.md). No production
code or shared build output participates in this experiment.

Official mechanism expectations agree with the observed direction but do not
guarantee it for an editor: [Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/),
[ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
and [tiered compilation](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation).

## Toolkit benefits and the important counterexample

Avalonia provides shared controls/chrome/layout and supports Native AOT. Its
production adoption is substantive: ILSpy 11's shipped Avalonia port uses
AvaloniaEdit. That is evidence of a maintained useful framework/control, not a
guarantee of mote's editing workload. Source:
[ILSpy 11 release](https://github.com/icsharpcode/ILSpy/releases/tag/v11.0),
[Avalonia Native AOT deployment](https://docs.avaloniaui.net/docs/deployment/native-aot).

The research group verified the **pinned AvaloniaEdit 12.0.0 source** and live
master: `SupportsPreedit => false`, with an empty `SetPreeditText`. Issue #524
and proposed PR #592 remain open in the fetched evidence. This proves that
stock in-editor composition display is missing; it does not prove all committed
IME input is unusable. A mainland-China editor must resolve and verify visible
Pinyin composition before default promotion. A fork/upstream contribution is
possible, but its maintenance is part of migration cost, not a solved service
that we can assume. Source:
[pinned TextArea](https://raw.githubusercontent.com/AvaloniaUI/AvaloniaEdit/86fdebec4cff7affb0e14c7885df71d28edce777/src/AvaloniaEdit/Editing/TextArea.cs),
[issue #524](https://github.com/AvaloniaUI/AvaloniaEdit/issues/524),
[PR #592](https://github.com/AvaloniaUI/AvaloniaEdit/pull/592).

Toolkit automation support likewise does not establish complete editor text
range/selection or CSV table accessibility. A limited inspection of two editor
files did not locate a specialized text automation provider; that is not proof
that framework inheritance provides nothing. Real external range/selection
queries and attended reader workflows determine adequacy.

The prior Windows AOT AvaloniaEdit probe measured a 1 MiB full mirror at
18.6 ms assignment-to-visual-line layout and 0.7 ms synchronous insertion,
155 MiB working set. These show an ordinary-file candidate worth testing, but
are from a different workload/machine than current RichEdit phases and cannot
be used as a direct speedup ratio. The same probe's 100 MiB full mirror used
1444 MiB and its 16 MiB single-line counterexample exceeded 2.6 GiB before edit.
The control's internal rope does not remove per-line/height indexes or engine
replica duplication. Source: [prior UI measurements](../large-file-ui-evaluation.md).

## Integrated recommendation and decision gates

**Recommend relaxing the installed-file-count constraint to permit a complete
application package and reviewed companion dependencies, while keeping
"no user-installed .NET required" and treating Native AOT as the first execution
candidate.** This is a recommendation for a user decision, not an implemented
contract change. A macOS `.app` can initially wrap the current AOT executable
without adopting a different UI or CoreCLR. Keep CLI forwarding and existing
configuration/byte contracts explicit.

The major gain is engineering freedom and conventional product packaging, not
a demonstrated automatic rendering speedup. The strict rule formerly forced
stock Avalonia out or implied custom native static builds. Allowing companions
removes that artificial build-maintenance problem. It also permits a narrowly
typed native glue module if it demonstrably simplifies AppKit interop; do not
add one merely because it is allowed.

App-contained AOT and app-contained CoreCLR both make the publisher responsible
for rebuilding/redistributing runtime security fixes; a bundled CoreCLR is not
automatically serviced by the machine's installed runtime. Add dependency/runtime
update monitoring and whole-package qualification to release responsibilities.
The retained native implementation's existing progress is an available qualified
baseline, not a sunk-cost argument against a demonstrably better toolkit.

**Do not make bundled CoreCLR mandatory and do not immediately replace the
native surface with the retained Desktop prototype.** CoreCLR/R2R are feasible
because the core is ordinary managed C#; choose them for a concrete library,
managed diagnostic or measured interactive benefit. The headless experiment
shows a real startup/payload penalty here and R2R recovers only part of it.
It cannot settle steady-state editor throughput.

The next decision gates, with bounded parallel ownership, are:

1. Packaging owner: AOT `.app`/portable-directory inventory, activation/CLI,
   version-atomic replacement and dependency failure cases. Signing trust stays
   unverified while the publisher has no Apple account; do not request one again.
2. Runtime/performance owner: same-source native shell under AOT/JIT/R2R on
   four RIDs, identical ordinary workflows and stage/exit/source-byte evidence.
   No whole-product performance verdict from the console study.
3. UI/input owner: qualify a concrete AvaloniaEdit candidate's visible Pinyin
   composition and reader navigation first, then exact engine delta/history
   integration and semantics/preview/theme parity. Failure does not force a UI
   switch or justify a detached composition ribbon.
4. Integration/review owner: compare total product risk and ordinary task
   outcomes; retain current native progress while the candidate is evaluated.
   Select one production surface deliberately rather than maintain competing
   canonical controllers forever.

This preserves the whole structured-editor goal. It neither narrows success to
a passing console benchmark nor removes capacity semantics because ordinary
files dominate the user's demonstrated demand.

## Research contribution and remaining uncertainty

The useful finding is a **deconfounding model plus one real mechanism test**:
packaging freedom can buy maintenance/product-integration value without losing
AOT; bundling CoreCLR and changing a UI are separate trade-offs. The pinned
preedit counterexample prevents a seemingly low-cost toolkit migration from
being treated as native input acceptance. The next highest-information step is
the same-UI VM comparison and actual candidate input/reader workflow, not another
generic AOT microbenchmark or unbounded toolkit survey.

Peer-reviewed VM warmup/hybrid execution and accessibility research support
first-use-aware measurement and user-task acceptance, but do not evaluate this
.NET desktop or prove a multi-file speed advantage. Their scope and primary
sources are retained in [research frontier](delivery-research-frontier.md).

Completion check for this bounded research task: all five owned research
documents exist; local Markdown links resolve; targeted diff whitespace checks
are clean. The platform specialist independently reviewed this synthesis's
packaging/trust/claim boundaries and found no scoped defect; their review did
not certify GUI behavior or recompute the runtime samples. This research task
does not certify or complete the active mote product goal.
