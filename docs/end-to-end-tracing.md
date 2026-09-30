# Native end-to-end tracing: causal endpoints and evidence

## Decision and implementation

Reuse `Mote.Telemetry`'s opt-in, local-only, bounded JSONL pipeline; do not add a
SQLite/native dependency or a generic UI event bus. Configuration keeps traces
under the resolved `~/.mote` trace directory by convention, with the existing
configuration path override. `MOTE_TRACE=1` or the existing trace configuration
enables persistence. No network exporter exists. The schema remains version 1;
only fixed operation names are added. The old `EditToPaint` enum/name remains
compatible, but native editor code does **not** emit it.

This answers a narrower, reproducible question: **where does CPU time go between
an accepted canonical mutation and return from the first matching native source
draw callback?** It does not answer input-to-photon latency.

```text
edit_to_presentation (one cross-callback mark, target version)
  ├─ document.edit             canonical apply (base version)
  ├─ view.layout               bounded source/native binding installation
  ├─ analysis.parse            background policy turn
  ├─ edit_to_analysis          original edit timestamp → analysis result
  ├─ analysis.to_presentation   UI semantic handoff
  └─ edit_to_draw_submission    original edit timestamp → source draw return

open_to_editable
  ├─ document.open             engine open/decode I/O as one combined phase
  └─ open_to_draw_submission
       └─ view.layout
```

Children may finish before their parent is serialized. Link with `trace_id`,
`span_id`, and `parent_span_id`, not JSONL adjacency or UTC subtraction.
`MoteTelemetry.Fork` gives each endpoint a unique span ID while retaining the
original monotonic timestamp. Parse and semantic handoff scopes explicitly use
the scheduling edit's causal context, rather than whatever ambient Activity
happens to be current in a later UI callback.

## Endpoint contracts

| Fixed operation | Start → end | Important boundary |
| --- | --- | --- |
| `mote.startup_to_editable` | After config/tracing setup, before shell construction → initial source view installed during `Shown` | Not process-launch time, configuration loading, first visible frame, or startup-file open completion |
| `document.open_to_editable` | Accepted open request → replacement source view installation returns | Includes background I/O and UI queue delay; does not wait for semantic analysis |
| `document.open` | Engine `Document.OpenAsync` call → return/error | Combined read/decode/rope creation, not separately measured subphases |
| `document.edit` | Canonical `Apply` call → return/error | Version dimension is the base version; native text differencing and earlier OS input/preedit work are outside it |
| `document.edit_to_analysis` | Accepted mutation mark → policy result and cancellation check | Includes intentional debounce and session serialization; result can later be discarded |
| `document.edit_to_presentation` | Accepted mutation mark → successful semantic handoff | Adapter can defer native style installation during IME; not a style-paint certificate |
| `view.layout` | Source projection/binding operation → normal return/error | Source installation and bounded viewport layout, not compositor layout |
| `document.edit_to_draw_submission` | Before accepted Apply/Undo/Redo/format/cut mutation → native source drawing returns | Excludes OS input and preedit before the canonical transaction; not physical paint |
| `document.open_to_draw_submission` | Open request → matching replacement source drawing returns | Not first pixel visibility |

`document.edit` currently instruments Apply mutations, not the internal cost of
Undo/Redo; their end-to-end draw intervals still begin before the command's
engine transition. Formatting intervals begin at accepting the returned
format transaction, not at the earlier background formatter invocation.
Unchanged/no-op commands do not produce a draw interval.

### Native endpoints and installed identity

- **Windows legacy page:** capture a ticket only for the source RichEdit
  `WM_PAINT` with a nonempty update region and an installed source stamp. Complete
  after `DefSubclassProc` returns and the stamp remains unchanged. Internal
  `WM_PAINT` without invalidated pixels is not an endpoint. Preview, status,
  editor ribbon and non-client paint do not complete it.
- **Windows continuous source canvas:** capture the current immutable binding
  and frame, require matching source versions and a positive source body height,
  complete only after normal painter/BitBlt/EndPaint return with the same binding
  and frame objects. The separate RichEdit input ribbon is not the endpoint.
- **macOS legacy page:** subclass only mote's own NSTextView (not global method
  swizzling), preserve superclass `drawRect:` and complete after it returns with
  a nonempty dirty rect and an unchanged installed stamp. Partial/deferred
  source installations clear eligibility.
- **macOS continuous source canvas:** complete only after actual source canvas
  drawing, matching immutable binding/frame/snapshot versions and unchanged
  binding/frame identities. Missing graphics context, zero body height,
  incomplete source frame, or an exception cannot produce success.

Generation/version guards stop a reopened document with reused version numbers
from borrowing an old interval. A monotonically advancing ticket also rejects
reentrant replacements **at the same version**. Adapter installed stamps become
eligible only after source installation returns. These are UI-thread-confined
trackers; this project does not opt into concurrent AppKit view drawing.

Neither endpoint verifies the complete visible glyph image. It records return
from a version-matched source draw callback. It does **not** assert GPU completion,
compositor presentation, display scan-out, or physical keyboard input latency.
The [WGC timestamp investigation](../benchmarks/NativePaintLatency/WgcTimestampOrdering.md)
found compositor timestamps later than CPU observation on the investigation
host. No offset correction, screenshot clock, vblank tag, or fitted value is
used to invent a physical paint endpoint.

## Boundedness, cancellation, faults and privacy

`NativeDrawTrace` stores exactly one pending mark per shell, not a per-version
dictionary or an unbounded history. A newer accepted mutation, different
installed document revision, or controller disposal terminates the previous
interval as `cancelled`. Multiple draws complete only the first eligible ticket.
An occluded or never-drawn revision may remain pending until replacement/close;
there is no fabricated timeout success. Cancelled durations are censored
observations: exclude them from successful-latency percentiles and report their
count separately.

The controller similarly owns one pending semantic parent and one request-owned
open parent. Superseded/closing requests end once even when their background UI
callback can never be pumped. Parser failures are `failure`, cancellation is
`cancelled`, and unsupported large-file semantic work is `skipped`; exceptions
cannot silently inherit a successful scope status. A successful parse followed
by a discarded result is legitimate: parse completion is not publication.

The existing writer's bounded `TryWrite` channel, file rotation/retention, drop
counter and sink-fault health remain authoritative. Callback instrumentation
performs no synchronous file I/O and takes no writer lock. The disabled mark
and fork paths are inert; inactive draw bookkeeping does not even query
`GetUpdateRect`. Only normalized format, coarse size bucket, numeric version,
count and the existing allowlisted save HResult can be serialized. No document
generation, binding nonce, source text, file path/name, exception message, IME
text, window title, pointer coordinate or arbitrary tag is emitted.

## Verification completed in this workspace

Environment: Windows x64 developer host, .NET 10 Release JIT test harness; this
is **not** a Native AOT GUI latency benchmark.

| Check | Observed evidence | Limit |
| --- | --- | --- |
| Initial tracing-focused tests | 14/14 (9 new deterministic cases + 5 existing telemetry tests) | Not native Mac execution |
| Actual Windows source callbacks | 2/2, legacy RichEdit and continuous canvas, accepted synthetic version 1; exactly one successful draw record each | Owned synthetic in-memory source/window, forced invalidation/UpdateWindow, not natural keystroke or physical presentation |
| Existing controller compatibility tests | 98/98 | UI-free orchestration contracts |
| Native + tests Release `-warnaserror` build | 0 warnings, 0 errors | No four-RID AOT acceptance claim |
| Disabled mark/fork/arm/observe/begin/complete loop | **0 managed bytes over 10,000 iterations**, after 100 warmup iterations | Allocation regression test, not nanosecond latency or tracing-on overhead measurement |
| Independent adversarial review | All reported P2s resolved; [review artifact](reviews/end-to-end-tracing-review.md) | Static review + test-contract inspection, not redundant target execution |

Reproduction:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore `
  --filter 'FullyQualifiedName~NativePaintTraceTests|FullyQualifiedName~TelemetryTests'
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-build `
  --filter 'FullyQualifiedName~NativeControllerTests'
dotnet build tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore -warnaserror
```

The two Windows callback tests return without running on other OSes; therefore a
green cross-platform test count is **not** Mac draw evidence. Tests use the
established repository `.temp/tests` scratch directories. They do not save a
user document, touch clipboard/input sources, capture the screen, call external
URLs or require accessibility permissions. The synthetic window closes in its
Shown callback's `finally`, without an unsaved-document controller prompt.

### Target-safe Mac/AOT draw probe (target callback acceptance established)

An opt-in [AppKit diagnostic](native-mac-draw-trace-probe.md) now owns a small
in-memory source, installs version 0, applies a tiny edit into version 1, and
observes the native source draw hook after `displayIfNeeded` without claiming
a compositor timestamp. Legacy and Continuous run in separate processes with
bounded deadlines and repository-local content-free JSONL. The probe checks one
cancelled prior interval, one successful installed-version interval, causal
parentage and the terminal session record. Independent static safety review
found no remaining dispatch blocker. The first hosted attempt exposed a probe
binding error and an early-close timing race, rather than proving callback
completion; both were corrected without changing production hooks. In
[CI run 36754713708](https://github.com/kleedaisuki/mote/actions/runs/36754713708),
both macOS RIDs independently emitted the exact Legacy and Continuous success
markers. All four uploaded JSONL sessions contain exactly five records, one
successful version-1 `edit_to_draw_submission`, one cancelled version-0 draw,
the two causal parents, and a terminal session. The result validates callback
wiring at **source draw return**, not natural input, compositor presentation,
pixel visibility or a physical-presentation SLA.

## External basis and remaining useful experiments

The integrated [CI run 36736928170](https://github.com/kleedaisuki/mote/actions/runs/36736928170)
at `6f990ed` passed all six strict jobs: Windows/macOS solution tests and
single-binary Native AOT on win-x64, win-arm64, osx-x64 and osx-arm64. This
confirms cross-platform compilation/test compatibility of the new hooks and
published binary inventory; that earlier run alone did not trigger the macOS
draw callback. The later Mac target result above adds synthetic target runtime
callback evidence. Windows managed hidden-window tests use synthetic
`UpdateWindow`; neither platform result measures physical screen presentation.

Microsoft's [WM_PAINT contract](https://learn.microsoft.com/en-us/windows/win32/gdi/wm-paint)
allows internal paint messages without an update region, motivating the explicit
source-region check. Apple's [displayIfNeeded contract](https://developer.apple.com/documentation/appkit/nsview/displayifneeded%28%29)
describes view drawing invocation, not physical display visibility. .NET
[distributed tracing concepts](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts)
support explicit causal parents. Google's production [Dapper report](https://research.google/pubs/dapper-a-large-scale-distributed-systems-tracing-infrastructure/)
motivates common instrumentation points and low overhead; it is a technical
report, not a claim that distributed-request timing transfers unchanged to
desktop rendering.

The next consequential measurements are an alternating tracing-off/on **real native editing workload** with
enough process repetitions to distinguish queue/profiling overhead from runner
noise. Existing startup-smoke timing cannot establish typing overhead. SQLite
indexing or trace sampling should be considered only if real JSONL analysis or
enabled tracing becomes a measured bottleneck, not because another storage
abstraction looks sophisticated.
