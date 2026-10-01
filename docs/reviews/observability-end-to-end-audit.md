# Independent observability and end-to-end coverage audit

Date: 2026-10-01. Source inspected: `1171d0f9b3c02c14c0d6e57b6a7f6d8d2fd3246d` and current same-area production hooks. This is an independent review, not a production patch or a claim that unexecuted paths passed.

## Verdict

Mote has a real, privacy-constrained tracing implementation, not merely lexical log statements. Its strongest causal coverage is **accepted canonical mutation -> analysis/publication -> revision-matched native source draw return**, plus accepted open -> editable/draw and coarse Save. It is **not complete user-action-to-durable-save observability**, nor input-to-photon telemetry. The current hard Mac Save investigation has two material observability explanations: receipt/admission are outside built-in tracing, and a small abnormal session can lose all buffered trace records. Adding a narrowly scoped witness was reasonable, but that witness is a separate diagnostic transport, not evidence that the permanent telemetry covers these boundaries.

Do not equate any of the following:

- an external event-post attempt with target event receipt;
- source first-responder/focus metadata with successful command dispatch;
- no received witness stage in a censored stream with callback nonexecution;
- a green `continue-on-error` AOT job with successful nested acceptance;
- a normal zero-drop session with complete coverage of all user operations;
- source draw callback return with compositor presentation or visible pixels.

## Method and evidence provenance

First reused `docs/end-to-end-tracing.md`, `src/Mote.Telemetry/README.md`, the prior focused tracing review, normal-exit drain review, and JSON pilot validation documents. Then inspected actual production `Program`, `NativeEditorController`, `NativeDrawTrace`, `MoteTelemetry`, `JsonlTraceSink`, `Document.SaveCoreAsync/WriteTempAsync/CommitTempAsync`, and Mac Save selector/diagnostic hooks. A repository-wide source-only search found no production Engine telemetry hooks; instrumentation is principally controller/shell owned. Enum values such as `DocumentDecode` and `AnalysisSemantic` are not proof of an emitted production span.

Examined retained JSONL and reports from [CI 36818175897](https://github.com/kleedaisuki/mote/actions/runs/36818175897), not just job color. Local originals are under `.cache/ci-36818175897-save-witness/osx-{x64,arm64}`. Independently parsed raw files and grouped operation/status counts. Reused earlier four-RID audit evidence rather than repeating native tests; no native GUI or new benchmark was run for this review.

## Stage-by-stage coverage matrix

B = built-in opt-in JSONL; D = separate scoped diagnostic/observer; missing = no reviewed end-to-end certificate. Windows/macOS columns distinguish execution evidence from shared controller source.

| Stage | Built-in boundary / missing detail | Windows evidence | macOS evidence |
| --- | --- | --- | --- |
| Process launch/configuration | `startup_to_editable` begins **after** config/tracing setup, before shell construction; loader/native image/config costs excluded | Pilot parent clock separately observes launch; not B launch timing | Same limitation; latest ordinary pilot parent clock includes client/poll overhead |
| Accepted open -> editable | `open_to_editable` plus combined `document.open` (read/decode/hash/rope), UI delay included; no separate production decode/rope spans | Earlier four-RID ordinary pilot audit validates causal trace; no new rerun here | Latest three successful cases contain accepted open/editable records and fresh reopen |
| OS event posted -> receipt | **Missing B**; external `postToPid` reply records attempted two events, not delivery | External UIA/synthetic inputs are scoped D evidence, not universal native input receipt | Latest report explicitly `execution_acknowledged=false`; no product event-receipt span |
| Command selector -> controller admission | **Missing B**; Mac `NativeSaveDiagnostic` has fixed selector/admission stages only when separately enabled | Windows Save diagnostic is separate D, not common request tracing | Three successful cases have D stages; failed x64 case stream censored |
| Canonical edit/history | `document.edit` measures Apply, `edit.committed` records mutation; mark begins before accepted engine transition | Shared controller wiring and prior hosted edit traces; Undo/Redo lack internal engine-cost spans | Latest success cases each have one Apply/commit; no natural keyboard/IME/history coverage certificate |
| Incremental analysis/publication | parse scope + causal edit-to-analysis/presentation; cancelled/discarded work distinct; parse/semantic not separately timed | Prior trace evidence validates scoped JSON workload | Latest successful traces validate same scoped edit chain; do not generalize to all formats/idle/error paths |
| Layout/source installation | `view.layout` covers bounded projection/native binding installation, not compositor layout | Canvas + legacy source callbacks have scoped validation | Same controller boundary; latest traces contain 11-15 layouts per edited session |
| Source draw submission | installed generation/version and reentrancy guards; first eligible source callback returns | Legacy RichEdit update-region WM_PAINT and Canvas painter/BitBlt/EndPaint hooks; synthetic callback tests/hosted pilot, not photon timing | Legacy own NSTextView and Canvas hooks; prior two-RID synthetic diagnostic validates both, latest ordinary pilot is not every-route evidence |
| Physical presentation | **Missing**, intentionally not fabricated | No compositor/scan-out certificate | No compositor/scan-out certificate |
| Save scheduling/I/O/atomic commit | one `document.save` scope begins inside Task.Run; failure phase/HResult allowlisted; **no B queue delay, snapshot version, per-commit phase or request-linked completion** | Shared controller/engine source; byte oracle separately certifies scoped saves | Latest successful traces have coarse Save; exact bytes and reopen are separate D witnesses |
| Normal exit | explicit controller disposal then 2-second producer/writer drain; session root and drop aggregates | Prior hosted zero-drop successful sessions | Latest six successful sessions have success terminal roots, no drop records |
| Abnormal exit/hang | **Missing durable tail**: buffered writer and end-of-operation records may vanish; no terminal root means censored, not success | Historical 0-byte failed pilot traces | Latest x64 1 MiB failure is exactly 0 bytes; witness ready only and forced cleanup |

Canvas and legacy **draw wiring** have separate guards and scoped target evidence. Other shared controller rows do not mean both adapters executed every listed behavior. CSV Grid AX diagnostics and physical Pinyin remain separate acceptance surfaces; neither is certified by JSON trace success.

## Latest raw observations

Source/run: `1171d0f9`, CI 36818175897. Edited/reopen JSONL counts and lengths independently observed:

| RID / size | Edited trace | Reopen trace | Diagnostic Save stream | Correct interpretation |
| --- | --- | --- | --- | --- |
| osx-arm64 / 1 MiB | 36 records / 12,285 B | 11 / 3,756 B | ready, selector, admission, completed; no overflow | scoped normal Save/reopen succeeds |
| osx-arm64 / 100 MiB | 37 / 12,779 B | 16 / 5,551 B | same complete sequence | scoped normal Save/reopen succeeds |
| osx-x64 / 100 MiB | 37 / 12,815 B | 16 / 5,549 B | same complete sequence | scoped normal Save/reopen succeeds |
| osx-x64 / 1 MiB | **0 B** | absent | ready=1, selector=0, admission=0, completed=0; forced cleanup | **no callback absence proof and no zero-drop trace proof** |

Each of the three edited successful raw traces contains one successful `document.save`, one `save.completed`, one successful session root; all six successful traces contain no `telemetry.dropped`. Save durations respectively 38,568 us, 361,106 us, and 1,175,281 us are **individual enabled-diagnostic observations**, not performance estimates. Their `document.save.attributes` are empty and parent is the session root; `save.completed` is another session-root child with no revision. The byte oracle/fresh reopen supplies correctness information not present in those spans.

For the x64 failed case, pre-Save and failure observations say app active/frontmost true, first-responder focus true, owned window AXFocused false. Command report records two attempted posts with no delivery acknowledgement. D stream says healthy_completed_stream=false and absence_interpretation=not-proof-of-callback-nonexecution. This excludes neither OS routing nor lost diagnostic work. It does not locate a product Save/I/O failure.

## Material findings and narrow corrections

### P1 - failure traces can be wholly censored during the failures we need to diagnose

Location: `JsonlTraceSink.WriteLoopAsync` uses a 64 KiB FileStream and flushes on rotation/closure; scopes generally serialize only on disposal. Latest failing 1 MiB Mac session is 0 B despite completed edit/analysis being observed externally. Earlier failed cases also retained 0-byte files. This is demonstrated evidence loss, not a claim of a data-loss bug in editor Save.

Impact: a healthy-path tracing system cannot classify a hang/crash if the last operation never closes and prior records never become externally readable. The two-second normal drain fix improves **normal** closure but cannot repair forced termination.

Remedy: retain nonwaiting UI producers and add a writer-owned, bounded periodic flush-to-OS policy (time/bytes), explicitly not per-record fsync. For opt-in diagnostic sessions, add fixed **start/checkpoint** records for low-frequency command/Save boundaries so a blocked operation leaves an in-flight stage. Keep every stage allowlisted and acknowledge transport watermark separately from operation success. Periodic flushing alone does not expose the beginning of an unclosed scope. Do not turn each keystroke into synchronous file/pipe I/O.

Discriminating tests: controlled blocking operation followed by process kill must retain a valid prefix/entry checkpoint after the flush budget; trailing partial line may be ignored but earlier corruption must fail. Slow/broken writer must never block UI producers; queue losses and absent terminal root remain censored. Measure diagnostic-on overhead separately.

### P2 - command-to-Save chain lacks request-level causal identity and subphase admission

Locations: Mac Save selector; `NativeEditorController.StartSave` around lines 665-725; `Document.SaveCoreAsync/WriteTempAsync/CommitTempAsync`. Built-in Save begins only inside its worker, has no saved snapshot version, and SaveCompleted is a later unlinked session child. Separate Mac witness counts have no built-in request identity or shared target timestamps.

Impact: absence of `document.save` cannot separate failed OS receipt, composition guard, picker cancellation, already-saving rejection, unscheduled worker, or unflushed trace. A long Save span cannot separate queued worker, encode/write/hash, final target check, atomic move/replace, saved bookkeeping, or UI completion. Concurrent newer edits make UI current version distinct from captured saved version.

Remedy: one bounded operation-owned command mark from **target selector receipt**, with fixed admission/rejection outcomes, worker start, captured snapshot version at the engine boundary, low-frequency Save phases, and completion as children of that same request. Include fixed command enum and ephemeral opaque span identity only; no path/content/key text. Explicitly preserve distinction between command receipt and OS physical key receipt. The separate witness remains useful for dead-transport diagnosis but must not become permanent parallel business-state machinery.

Tests: blocked composition, already-saving, picker cancel, rejected overwrite, failure/cancellation before and after commit, edit-during-save, and old completion after document replacement must produce exactly one attributable terminal outcome or declared censoring; saved snapshot version must not be guessed from current UI version. Existing exact-byte oracle remains independently necessary.

### P2 - at least two outcome paths still default to misleading success

Locations: `NativeEditorController.StartSave` sets `cancelled=true` when overwrite confirmation is declined, but does not set the live Save scope to Cancelled before disposal. The later callback returns without SaveCompleted. `ApplyIdleAnalysis` around lines 2057-2068 opens AnalysisToPresentation with default Success and can throw from projection/native publication without locally marking Failure.

These are source-established paths, not failures demonstrated by the current hosted JSON workload. Impact is diagnostic misclassification: declined Save can appear successful, and exceptional idle native publication can dispose a success span. The prior focused review repaired similar main analysis/layout paths but does not certify all remaining scopes.

Remedy: initialize these scopes to non-success until normal completion, explicitly record cancellation on declined approval, and contain publication failure at its own scope; avoid relying on an outer catch after using disposal. Add targeted deterministic injected-throw/declined-confirmation tests, not duplicate full native acceptance.

## Health, privacy, clocks, and overhead

Sound foundation: default-off config `[telemetry] enabled` or `MOTE_TRACE=1`; default `~/.mote/traces`, config `[paths] traces` override; local-only fixed-schema JSONL; no network exporter, native sidecar, free-form tags, filename/path/content/IME text/exception message. Trace IDs and span IDs correlate accepted operations; monotonic Stopwatch durations avoid UTC subtraction. Draw generation identity is internal, not persisted. Queue capacity 4096, nonwaiting TryWrite, size/age rotation, per-session max eight files, sink-fault/drop health, and closure admission leases are substantive engineering rather than marketing.

Limits: session-only retention does not bound aggregate historical disk use. Sink fault means missing evidence even if drop counter stays zero; zero drops does not certify unfinished delayed marks or rejected admission. UI health appears via diagnostics command, not always-on loss alarm. Writer and shared work use Task.Run; if investigating thread-pool starvation, an independent dedicated witness can remain useful, but latest artifact does not prove starvation. Parent Python/Swift observation durations and target Stopwatch durations have no calibrated common clock; fixed witness receipt timestamps are not target execution timestamps. No UTC-based fusion is valid.

Disabled mark/fork/draw-path allocation tests establish zero managed allocation for their warmed loop, not zero startup cost or tracing-on latency. Existing Engine attribution experiments have overlapping on/off/control ranges and do not measure native typing stalls. A paired alternating tracing-off/on native edit workload across fresh process runs, reporting latency distribution, queue loss, allocations, and telemetry completeness, remains needed before an enabled-tracing performance claim. One successful sample per case cannot establish p95/p99.

## External grounding and scope limit

[.NET tracing concepts](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts) justify explicit trace/span parentage rather than timestamp adjacency. [FileStream.Flush](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-10.0) distinguishes stream flushing from flush-to-disk: a diagnostic readable-prefix policy need not fsync every record. [Google Dapper](https://research.google/pubs/dapper-a-large-scale-distributed-systems-tracing-infrastructure/) supports common instrumentation points and low overhead, but is a production technical report, not a proof that desktop input/compositor boundaries are captured. The relevant research lesson is **causal reconstruction plus explicit missing evidence**, not importing distributed-service infrastructure into a single-process editor.

No universal claim about crash durability, natural keyboard latency, physical presentation, all formats, IME, accessibility, or cross-RID overhead follows from this audit. The priority is recoverable low-frequency command evidence and a single request-owned causal chain, not more scattered debug prints or another telemetry database.
