# End-to-end tracing independent review

Date: 2026-09-30. Status: targeted correction review completed; all reported P2 findings resolved. No unresolved substantive finding in the reviewed tracing scope. Findings refer to the working-tree implementation inspected on this date. This review does not modify production code.

## Scope

Reviewed `NativeDrawTrace`, `MoteTelemetry.Fork`, the new telemetry enum/name mappings, controller startup/open/edit/analysis lifecycle wiring, normal program disposal, legacy Windows RichEdit `WM_PAINT`, Windows canvas painting, legacy macOS NSTextView `drawRect:`, and macOS canvas drawing. Unrelated CSV Grid changes are excluded. Existing telemetry tests were inspected as contract evidence; target-native draw callbacks were not executed in this review. No physical-display or compositor-latency claim is supported by this review.

## Original findings — resolved after targeted re-review

### Resolved P2 — analysis exceptions are persisted as successful parse operations and cancelled presentation operations

Locations: `src/Mote.Native/NativeEditorController.cs`, `ScheduleAnalysis` and `ScheduleSessionAnalysis`.

The new `StartChild(AnalysisParse, editMark, ...)` scopes default to `TelemetryStatus.Success`. If `policy.Analyze`, `driver.AnalyzePresentationAsync`, or the following `ThrowIfCancellationRequested` throws, `using` disposes the scope before the outer catch, recording success. The exception catch publishes `Analysis failed` without completing `_presentationMark`; a later edit, replacement, or disposal calls `CancelAnalysis` / `FinishEditPresentation(Cancelled)`, misclassifying the actual failure and inflating the interval until that later action. The current `OperationCanceledException` and `ObjectDisposedException` paths similarly do not set the parse scope's terminal status. The native publication scopes also default to success if projecting or installing presentation throws in the posted callback.

Impact: enabled traces report failed/cancelled parse work as success, hide semantic failures behind cancellation, and cannot reliably distinguish failures from user supersession. This affects the intended diagnostic purpose rather than document contents. Confidence: high, established by the lexical `using` lifetime and default scope status; no OS timing assumption is needed.

Correction: catch within each operation scope and call `SetStatus(Failure/Cancelled)` before disposal; finalize the request's semantic parent promptly and exactly once. Finalization on the UI thread must be guarded by captured serial/identity so an old request cannot finish a newer edit's parent. Handle posted publication exceptions locally rather than expecting a worker-side catch to catch a later callback. Add tests with a throwing policy/driver, cancellation after scope creation, and an older failure racing a newer edit.

### Resolved P2 — closing during an asynchronous open leaves its causal parent without a terminal record

Locations: `src/Mote.Native/NativeEditorController.cs`, `StartOpen`, `Dispose`, `Post` / `TryPost`; `src/Mote.Native/Program.cs`, shutdown ordering.

`openMark` is retained only by `StartOpen`'s worker and posted callback. `Dispose` closes startup, semantic, and draw intervals, but has no reference to the pending open interval. When the user closes while `Document.OpenAsync` is pending, `app.Dispose()` is followed by telemetry shutdown. The eventual callback can be rejected by `TryPost`, or remain in a shell queue whose event loop no longer pumps. The callback's new `_disposed` cancellation branch therefore cannot ensure cancellation is recorded before draining the writer. This is a concrete normal close path, not an exceptional process termination.

Impact: traces omit the outcome of in-flight opens, biasing lifecycle diagnostics toward completed requests. The unrelated pre-existing ownership behavior of a successfully opened document whose callback is never pumped is outside this tracing-only review. Confidence: high from controller ownership and shutdown ordering.

Correction: retain request-owned open trace state at controller level, cancel/finalize it on supersession and `Dispose` before telemetry shutdown, and have the async completion consume that state exactly once. Do not simply record both in Dispose and the eventual callback with the same span ID. Use rejected-post handling for request-owned cleanup. Add a deterministic delayed-open/close test or an explicit injectable open seam; avoid timing-only tests.

## Targeted correction assessment

`AnalyzeTraced` and `AnalyzeSessionTraced` now set failure/cancellation before scope disposal and check cancellation inside the measured operation. Failure publication consumes the semantic parent as Failure; cancellation and failure callbacks guard the captured serial. `PostAnalysis` handles later UI-installation exceptions locally. These resolve the first finding. A cancellation arriving after successful analysis but before UI publication may legitimately leave analysis.parse successful while edit_to_presentation is cancelled.

The controller now owns `_openMark` / `_openTraceRequest`. `FinishOpen` consumes ownership before recording; StartOpen supersession, New/replacement, and Dispose cancel it, while late callbacks cannot consume another request. Dispose no longer depends on pumping the asynchronous completion. This resolves the second finding.

Inspected the new `NativePaintTraceTests`: parser failure/cancellation, failure-to-parent status, pending-open close without pumping callbacks, semantic supersession, stale/replaced tickets, uniqueness, privacy and disabled hot-path allocation assertions exercise the reported contracts. Owner reports 14/14 including existing telemetry tests; this reviewer did not repeat that completed run. The open-close test uses a non-pumped fake shell and does not rely on actual file-I/O duration; that is adequate for this particular ownership regression.

The corrected edit population now includes undo, redo, format application, and engine-driven cut, in addition to native typed edits. Those causal marks begin at command/application boundaries rather than measuring the entire formatter or clipboard transaction.

### Resolved P2 — newly added layout scope recorded installation exceptions as success

`ShowDocument` now opens `ViewLayout` with its default Success status. `ShowCanvasDocument`, snapshot projection, or `_shell.SetDocument` can throw; disposal then serializes `view.layout` success despite a failed installation. Set Failure at scope entry and Success on both normal completion paths (canvas return and legacy tail). This is the same diagnostic correctness contract as the resolved parser issue, now in a newly added scope. Confidence: high from scope lifetime. Owner notified promptly; no wrong-revision draw claim is involved. Targeted reinspection confirms Failure is now set at entry and Success only after both normal source installation paths; this finding is resolved.

## Positive observations

- `Fork` creates a new span ID, preserving the original monotonic start and using the parent mark's span as parent; draw and semantic endpoints do not intentionally serialize the same span ID twice.
- `NativeDrawTrace.CompleteDraw` clears the pending mark before enqueueing. A stale ticket, wrong generation/version, duplicate completion, or replacement arm cannot complete the new interval.
- Legacy shell installations clear the installed stamp before native operations. Draw completion is eligible only after installation returns, with generation/version matching the armed interval. macOS additionally excludes IME-deferred source installs.
- Canvas completion captures binding/frame identities at entry and rechecks references after drawing. macOS body-height publication can reenter the controller; these checks prevent a changed frame from completing the old ticket.
- Windows legacy paint checks a nonempty update rectangle only while tracing is pending. Windows canvas completes only after painter end, successful BitBlt, and EndPaint; macOS completes only after its native draw returns normally. These names describe CPU draw submission, not displayed pixels.
- Dimensions and operation names are allowlisted numeric/enumerated metadata. The new correlation generation is not serialized; no path/content/exception string was added to trace records.
- The macOS IMP uses a blittable by-value rectangle and a void return, consistent with the existing native draw callback patterns. This is a source-level ABI assessment only; four-RID Native AOT and actual macOS execution remain necessary acceptance evidence.

## Limits and follow-up

No new substantive wrong-revision draw-success path was demonstrated. Native installation success still relies on the adapter's existing native text installation contract; this review did not independently verify failed EM_SETTEXTEX handling or external native buffer readback. The inactive path avoids clocks, span IDs, queue records, and new paint-query calls, but each real shell creates one small `NativeDrawTrace` instance regardless of tracing; this is not an allocation-free application startup claim. Captured mutation boundaries are native typed edits, undo, redo, applied formatting, and engine-driven cut; measurements do not include every preceding user interaction or background transaction.

This is a focused code review, not evidence that all runtime, exception, cancellation, accessibility, or platform behavior passed.
