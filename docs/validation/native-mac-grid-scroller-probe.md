# Published Native AOT macOS logical Grid scroller probe

Date: 2026-10-01. Status: implementation compiled on Windows; portable mapping
suite passed. Published macOS x64/ARM64 execution is **not yet performed**.
Owner: `src/Mote.Native/Mac/MacGridScrollerProbe.cs`; route and CI integration
belong to the integrating agent, after independent safety review.

## Contract and containment

`MacGridScrollerProbe.Run()` initializes system AppKit and an autorelease pool,
constructs one hidden production `MacCsvGrid`, and disposes every owned control
before draining the pool. It creates no NSWindow, starts no application run loop,
enters no mouse tracking loop, and does not activate the application. No files,
user configuration, clipboard, input sources, TCC permissions, network or desktop
event injection are used. Console stdout/stderr are the only output channels.
The process is intended to run once per published executable invocation, on the
entry-point/main thread.

The probe discovers owned scroller instances via native `subviews` and verifies
real NSTableView/NSScrollView state. For synthetic actions only, it registers a
**new probe-only subclass** of the existing production scroller class and changes
the class of these two probe-owned instances. Its sole override is `hitPart`,
returning a bounded caller-selected AppKit part ordinal. It does not modify an
existing class/method, swizzle shared selectors, or touch any other instance.
Original instance classes are restored in `finally` before control disposal.
Production delegate target/action is then invoked directly. This is not a real
mouse drag, NSScroller hit-test, or NSApplication event dispatch acceptance.

## Assertions

- Two actual standalone scrollers and disabled competing local scroll-view axes.
- Finite positive native control geometry; getter ABI uses architecture-aware
  CGRect and typed floating-point CGFloat/double signatures already used by the
  adapter. Geometry does not establish visual overlay opacity or physical hit testing.
- Logical row/column normalized positions and knob proportions read back from
  real NSScroller objects at distant coordinates above 65,535.
- Actual exact-domain and prefix-domain labels remain distinct; these internal
  native getters do not establish external AX/screen-reader behavior.
- Three pending requested rows and two columns remain bounded despite the
  200,000-row/90,000-column logical range; pending cells are origin-free and cannot
  emit Copy, Reveal or Replace source commands.
- Synthetic line/page/knob/slot parts enter the production target/action callback;
  admitted per-action tokens remain unique, row/column coordinates are correct,
  endpoint mapping is exact, and fully-visible geometry stays within delivery caps.
- A delayed synthetic action after `SetNavigation(null)` cannot reacquire retired
  navigation authority. **Controller stale-token rejection and invalidation
  during AppKit's synchronous mouse tracking remain untested by this probe.**
- Sparse delivered row 2 stays in slot 2; gaps 0/1 read `[pending]`, rather than
  compacting the row into slot 0. Selecting a gap cannot authorize source commands.

The probe has no engine/controller/file instance and therefore makes no source
version, Undo, on-disk integrity or controller admission acceptance claim.
No physical mouse/trackpad/IME, composited paint, startup timing, clipboard,
external AX, overlay visibility or callback latency acceptance is implied.

## Reproducibility and handoff

Local Windows commands performed:

```powershell
dotnet build src/Mote.Native/Mote.Native.csproj -c Release --no-restore -warnaserror
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore `
  --filter FullyQualifiedName~MacGridScrollInteropTests `
  --logger 'trx;LogFileName=mac-grid-probe-portable.trx' `
  --results-directory .cache/mac-grid-scroll-tests
```

Build: zero warnings/errors. Portable tests: **11 passed, zero failed/skipped**;
TRX `.cache/mac-grid-scroll-tests/mac-grid-probe-portable.trx`. These tests are
existing part-mapping tests, not runtime execution of the new AppKit probe.

Integrating agent must add an early single-purpose opt-in route before ordinary
settings/document startup, retain exit status, and run each published Native AOT
macOS RID separately on disposable GitHub-hosted runners with an external timeout.
Exact success marker:

```text
mote-native-mac-grid-scroller-ready; native-action=synthetic; physical-input=not-tested; controller-stale-token=not-tested
```

The Native AOT compiler must retain the `UnmanagedCallersOnly` callback through
its direct function-pointer use; no reflection/dynamic managed code is required.
The probe class name is process-local and expects a fresh process per invocation.
No staging, commit, push or target execution is performed by this assignment.

Existing design and authoritative AppKit reference trail:
[implementation](../csv-grid-logical-scrollbar-implementation.md),
[adapter validation and Apple sources](mac-grid-logical-scroller.md).

## Safety-review hardening

Independent safety review requested two narrow changes. Failure output now uses
only the fixed content-free identifier
`mote-native-mac-grid-scroller-failed; contract=acceptance`; exception messages
are not emitted because runtime failures may contain host paths. Original-class
slots are preallocated before mutation, and every instance-class replacement
is inside its restoration `try/finally`. A failure while replacing a later
instance still restores every earlier nonzero saved class. No assertion,
production behavior, target execution or probe scope changes were made.
Release Native `/warnaserror` was rebuilt successfully after this delta.

Frozen source SHA-256 (UTF-8 without BOM):

- LF: `E0E0CCDA60C1AC2DC144CE01CE65B420457F1B9BDDB85899225762AB9F5B2938`
- CRLF: `63A3CFF32138CBA344D8A8CA91347B666AF37DB9FC9825ACB3527400EDCF97C5`

Current working-tree source uses LF. These supersede the earlier frozen hash;
the safety reviewer must approve this delta before route/CI target dispatch.
