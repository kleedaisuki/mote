# Independent native causal Save-chain review

Date: 2026-10-01. Reviewer owns this document only; no production changes.
Initial scope: working tree based on `ae03a42`, including pending
`NativeEditorController.Save.cs`, the typed `NativeSaveRequest` migration in
`NativeShell.cs`, Mac/Windows dispatch, and corresponding fake/probe migrations.
This is a source-and-contract review, not a claim of hosted Native AOT success.

## Required corrections found in the initial working tree

### R1 — Revalidate receipt ownership after admission-time native calls

Location: `NativeEditorController.Save.cs`, `StartSave`, from its initial
`_disposed` guard through `CommitPendingText` and `PickSaveFile`.

The controller checks disposal only before settlement and captures `_document`
and `_canvasGeneration` only after the picker returns. A native callback that
replaces the document during settlement or the modal picker can consequently
admit the original request against the replacement document. Disposal during
the picker can likewise proceed to worker scheduling. This conflicts with the
request's document-lifetime contract and can write bytes from an unintended
buffer to the path chosen for the original request. Completion-time checks do
not undo an admitted disk commit.

Capture document identity and generation before the first reentrant native
boundary. Revalidate after composition and after the picker, before busy
ownership or worker admission. Also recheck busy state after a modal picker:
another command may have acquired it while the original picker was active.
Test replacement and disposal at settlement and picker boundaries with an
exact-byte or absent-target oracle, not terminal telemetry alone.

Confidence: high from executable control flow. The validator is adding direct
reproductions using fake-shell callbacks; the initial completion-reentry tests
covered later settlement/presentation, not these admission boundaries.

### R2 — Validate overwrite approval after the native dialog returns

Location: `NativeEditorController.Save.cs`, `ConfirmSaveOverwriteAsync`.

The boolean expression checks controller/document/generation before invoking
`_shell.ConfirmOverwrite(path)`. A native modal dialog may reenter and replace
or dispose the controller before returning true. The resulting approval then
allows the worker to invoke Save on the old document even though the UI intent
was revoked. An already-admitted commit must not be cancelled merely because
UI intent ends, but overwrite approval has not been granted until this dialog
returns; these are different boundaries.

Separate pre-dialog validation, dialog call, and post-dialog validation, using
the same captured document/generation. A replaced or disposed request must not
receive approval. Add a reentrant confirmation test asserting the existing
target's exact bytes remain unchanged.

Confidence: high from executable control flow; practical reachability depends
on native modal event dispatch, which the shell abstraction explicitly permits.

## Positive findings and preserved contracts

- One typed event carries Save versus Save As with a retained explicit request
  identity. Missing handlers receive an honest skipped terminal disposition.
- Mac selector dispatch contains managed failures at the unmanaged ABI;
  secondary error-UI failure is also contained. No source/path strings are
  retained in request context.
- The worker receives document, generation, attempt, and request explicitly.
  The request terminal is independent of actual engine persistence; ending UI
  intent does not pass a cancellation token to an admitted file commit.
- The observer's exact Engine snapshot version, rather than a later UI version,
  reaches Save phase and completion records. The existing exact-byte test edits
  during a blocked commit and proves the distinction.
- Completion releases busy ownership only for its matching attempt, and checks
  document/generation/disposal before policy selection and after potentially
  reentrant settlement or presentation. A stale completion does not publish
  `save.completed` into the replacement view.
- `save.ui_post_returned` certifies return from the local posting call, not native
  callback execution; `save.ui_started` records callback entry separately.
- Composition deferral is explicitly reported as `view_deferred` rather than
  silently claiming the current view was refreshed.
- No per-key synchronous file I/O is introduced by the changed dispatch path;
  diagnostic recording retains the existing nonblocking fixed-enum path.

## Scope and limitations

The engine Save phase implementation, telemetry writer prefix durability,
privacy serialization, and hosted helper classification have separate owners
and are not independently revalidated here. Synthetic shell tests cannot prove
physical key delivery, operating-system activation, AppKit/UIA behavior,
compositor presentation, crash durability, or full format coverage. The old
Mac large-file timeout remains an observation to retest with the new target
chain; it is not explained merely by adding instrumentation.

Useful contracts: [runtime provenance design](../architecture/observability-provenance.md)
and [existing end-to-end endpoint definition](../end-to-end-tracing.md).
