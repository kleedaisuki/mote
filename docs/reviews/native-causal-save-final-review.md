# Native causal Save: final integration review

Date: 2026-10-01. Reviewed committed production `b2f4661` / `d551bad`,
preceding independent review `be56c4a`, and dedicated follow-up tests `370de8a`.
This document owns no production changes. Scope is the native Save request,
worker and completion lifecycle, not a repeated Engine or transport audit.

## Completion-time callback failure finding: closed

**Initial P2 — `CompleteSave` rethrew a nonfatal failure into the native dispatch pump.**
Location: `src/Mote.Native/NativeEditorController.Save.cs:148–151`.

Trigger: after the worker posts its completion, a synchronous UI operation
(`ShowError`, pending-text settlement, or `ShowDocument` and its shell callbacks)
throws a nonfatal managed exception. The new catch correctly selects a
`failure / callback_failed` terminal but then rethrows. On Windows the
`WM_APP` branch in `WindowsEditorShell.WindowMessage` directly invokes
`action()` (`WindowsEditorShell.cs:1033–1035`), reached from native WndProc
`Dispatch`; there is no intervening containment. Receipt-time
`DispatchContained` is no longer on this asynchronous stack. An otherwise
recoverable post-save view error can therefore escape the reverse native
boundary and terminate the process, losing unrelated unsaved edits. On macOS
`DrainPosted` calls `Notify`, which catches the primary failure but calls
`ShowError` without containing a secondary reporting failure.

Confidence: high for the executable exception path; this is source-backed,
not an observation that a hosted native process has already crashed on this
specific path. The shared pump exposure predates this patch, but the newly
factored Save completion explicitly owns failure disposition and must not
claim callback containment while deliberately propagating this exception.

Narrow remedy: finish the failure terminal in `CompleteSave` and contain the
nonfatal exception there. If reporting is desired, use a fixed, path-free
notice with its own nonfatal containment. Preserve the worker's actual disk
result, release the matching busy owner as now, do not retry the Save, and do
not broaden this fix into a shared UI-pump redesign. Preserve the existing
fatal exception policy.

Discriminating test: save exact known bytes; after the worker has posted, set
the fake shell's existing `DuringDocumentSet` hook to throw a nonfatal
exception. Pump must return without throwing, busy must be false, saved bytes
must remain exact, the request must have exactly one `callback_failed`
terminal, and `save.completed` must be absent. Also inject a failure in error
reporting after a real persistence failure; secondary reporting must not
escape or replace the actual persistence evidence. This is a new fault case,
not a request to rerun all completed regression tests.

## Previously raised findings: closed in committed source

- **R1, admission-time ownership:** document and generation are captured before
  controller settlement, revalidated after settlement and modal picker, with
  a second busy check after picker return. Dedicated tests assert absent
  target bytes for replacement/disposal and distinguish an admitted nested
  request from the rejected outer request. The earlier finding is resolved.
- **R2, modal overwrite approval:** the posted confirmation validates both
  before and after the modal call, and returns false for retired ownership.
  Nonfatal confirmation exceptions settle the TCS with the original exception
  instead of stranding the awaiting worker. Dedicated tests check exact
  unchanged target bytes and exception identity. The earlier finding is resolved.

## Preserved causal and persistence contracts

- A single typed event distinguishes Save and Save As; missing handlers and
  receipt-time nonfatal failures have honest terminal dispositions.
- Mac receipt begins before its composition guard; selector dispatch contains
  managed failures. Windows receipt-time dispatch is contained separately.
- Explicit request and phase marks use original-sink APIs, not legacy
  `StartChild` fallback. The adapter retains no source text, path or native
  pointer in telemetry. Existing independent transport review owns the
  reconfiguration/race verification; it is not duplicated here.
- `NativeSaveObserver` reports the Engine's captured snapshot version. The
  exact-byte test edits after capture and checks that newer buffer changes
  remain modified; UI versions are not substituted for the saved version.
- Request terminal selection is atomic `EndOnce`. Retirement ends UI intent,
  not an already admitted disk commit. Late callbacks validate reference,
  generation and disposal before policy/view publication and after reentrant
  boundaries. `save.completed` is not emitted for stale completion.
- `save.ui_post_returned` means the local posting call returned, while
  `save.ui_started` means callback entry. Neither certifies OS input receipt
  or visible pixels. View deferral is explicitly distinguished.

## Review limits

No previously completed tests were rerun. Reviewed test bodies are not a new
execution certificate. Retained local results were inspected in the closure
below. Hosted
four-RID Native AOT, real macOS intermittent Save timeout attribution,
physical key/IME delivery, native wake acknowledgement, compositor output,
crash/power-loss durability and current-binary overhead remain integration
acceptance, not conclusions of this source review.

## Narrow closure: `5ebbdaf`

The follow-up changes only the completion catch's propagation behavior and
its contract documentation. Nonfatal exceptions still select exactly one
`failure / callback_failed` terminal, but do not rethrow or recursively invoke
another error dialog. The existing pre-catch busy release and captured-version
handling are preserved; Engine disk results are not relabeled or retried.
`OutOfMemoryException` remains outside the catch. This resolves the initial
P2 for the inspected Save completion path. It does not certify all unrelated
callbacks in the shared native pumps.

Inspected `NativeSaveCompletionContainmentTests` exercises the actual typed
request, worker and queued completion Action directly, avoiding reflection's
exception wrapper. Its four cases cover successful disk commit followed by
presentation failure, and failed commit followed by error-reporting failure,
with tracing enabled and disabled. Assertions verify no callback escape,
released busy/request ownership, exact committed bytes or absent target,
correct modified state, no recursive error reporting, truthful Engine and
commit phases, one version-1 receipt-linked callback-failure terminal, no
`save.completed`, and no secret text in records.

Retained execution evidence was read, not rerun:

| Artifact | Actual TRX counters | Scope |
| --- | --- | --- |
| `.cache/save-completion-containment/save-completion-containment.trx` | 4 executed, 4 passed; zero failed, aborted or not-executed | New completion containment fault cases |
| `.cache/causal-integration-tests/causal-integration.trx` | 1329 executed, 1329 passed; zero failed, aborted or not-executed | Root integration baseline, before the new containment slice |

These runs are not summed into an independently verified final full-suite
count. The focused follow-up closes the source-backed finding; hosted
platform-native acceptance and full current-HEAD integration remain separate.