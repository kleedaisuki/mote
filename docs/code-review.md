# Code review: correctness and data preservation

Review date: 2026-09-29. Scope: `Mote.Engine` document/rope/I/O/history, desktop open/save/paging/analysis/render flow, format HTML export, and JSONL telemetry. This is an executable-path review, not a claim that all GUI platform behavior or crash consistency was tested.

## Findings and disposition

### P1 — An external same-size/same-mtime edit could be overwritten (engine; fixed in current working tree)

**Trigger and impact.** `Document.SaveAsync` originally used `FileStamp(Length, LastWriteTimeUtc)` as its sole conflict predicate (`src/Mote.Engine/Document.cs`, original `FileStamp` and two checks in `SaveCoreAsync`). An external writer could change bytes without changing file length and restore the previous modification time; Save then replaced that external content and reported success. This is real user data loss, not a theoretical timestamp-resolution concern.

**Reproduction.** `tests/Mote.Tests/ReviewRegressionTests.cs`: write `AAAA`, open, edit in memory to `CCCC`, externally write `BBBB`, restore original `LastWriteTimeUtc`, then save. The required result is an `IOException`, disk `BBBB`, and a dirty document. The original metadata-only code instead overwrote disk with `CCCC`.

**Current correction.** Engine owner added a raw-byte SHA-256 fingerprint during open and compares the current target against it immediately before replacement. The regression passed locally with `dotnet test tests/Mote.Tests/Mote.Tests.csproj --filter FullyQualifiedName~ReviewRegressionTests --no-restore --verbosity quiet` (1/1, Windows). This does not eliminate the narrow time-of-check/time-of-use window between hash verification and `File.Replace`; fully eliminating that window would require stronger platform-specific identity/locking or compare-and-swap semantics. Do not claim absolute atomic conflict detection.

### P1 — An in-flight open could discard edits without confirmation (desktop; fixed in current working tree)

**Trigger and impact.** `MainWindow.OpenPathAsync` previously awaited `Document.OpenAsync` and unconditionally called `ReplaceDocument` (`src/Mote.Desktop/MainWindow.axaml.cs`, original lines 90–115). `OpenClicked` checked dirty state *before* the picker and read, and startup command-line opening did not check it. While loading a large file, a user could type into the current editor; the eventual completion disposed that newly dirty document without a prompt. Multiple opens could also complete out of order.

**Current correction.** Desktop owner added a request sequence and captured document identity/version, disposes stale completions, and reconfirms if the current document changed before replacement. `ReplaceDocument` invalidates older opens. The race should receive a UI-level asynchronous test when such a harness exists; the logic was inspected but not end-to-end GUI-tested here.

### P2 — In-flight save could update the wrong document's presentation (desktop; fixed in current working tree)

**Trigger and impact.** `MainWindow.SaveAsync` originally chose a path, then awaited `_document.SaveAsync(path)` and unconditionally assigned `_policy = DocumentPolicies.ForPath(path)`, updated chrome, scheduled analysis, and reported `Saved`. If a document replacement happened while the picker or disk save was pending, the continuation could apply the old save target's policy/status to the new document. A Save As picker returning after replacement could even initiate a save of the *new* `_document` to the old selection, because `_document` was dereferenced only after the picker returned.

**Current correction.** Desktop owner now captures the `Document` before any await, checks `ReferenceEquals(captured, _document)` after picker and after I/O, and avoids updating replacement UI state. This was inspected in source but not exercised with a GUI race harness here.

### P1 — Save As overwrite contract and desktop integration (fixed in current working tree)

**Original trigger and impact.** `Document.SaveCoreAsync` previously performed external-change checks only when `path` equaled the document's original path, then unconditionally `File.Replace`d any existing destination. Thus `SaveAsync(otherExistingPath)` could silently destroy a different file through the public Engine API. The desktop now sets `ShowOverwritePrompt = true`; the [Avalonia API](https://api-docs.avaloniaui.net/docs/P_Avalonia_Platform_Storage_FilePickerSaveOptions_ShowOverwritePrompt) says this controls an existing-file warning.

**Current Engine correction.** `SaveAsync` now refuses an existing different target and uses no-overwrite `File.Move` for a new target. Explicit `FileOverwriteToken.CaptureAsync(path)` plus `SaveOverAsync(token)` validates a stamp and SHA-256 before replacing a pre-existing file. Tests in `EngineTests.cs` cover the plain Save As refusal and token-based overwrite/change refusal; the targeted `Save_as` test passed locally on Windows (1/1). The usual narrow verification-to-replacement race remains; the token does not lock the file.

**Current Desktop correction.** `MainWindow.SaveAsync` now distinguishes a different existing path, captures `FileOverwriteToken`, asks explicit replacement confirmation, then invokes `SaveOverAsync`; same-file Save and create-new Save As use `SaveAsync`. This integration was inspected in source after the Engine contract change, not exercised with a native picker harness. The picker and app currently both prompt for overwrite, which is redundant UX rather than a correctness defect.

### P2 — Subscriber exceptions could strand committed change notifications (engine; fixed in current working tree)

**Trigger and impact.** The original `DrainChanges` reset `_notifying` and rethrew when a `Changed` handler threw, leaving pending events queued. If v1's handler blocked, another thread could commit v2 (its drain returned because `_notifying` was true), then v1's handler threw. No thread remained to deliver v2 until some later edit. Text stayed correct, but subscribers such as views/caches missed a committed transition, violating the documented ordered-delivery contract.

**Current correction.** Engine owner now records the first handler exception, continues draining committed events, resets notification state when empty, then rethrows. This was inspected in current source; no dedicated concurrent regression was added here.

## Verified non-findings and review limits

- The rope uses persistent nodes; edits/undo create new snapshots and the existing randomized model test covers text and line-index agreement. No specific rope corruption path was identified in this review.
- `TextSnapshot` deliberately treats CR, LF, and CRLF as logical line breaks, including a CRLF across chunks. Existing `EngineTests` cover this boundary.
- Markdown HTML export reparses under a pipeline with raw HTML disabled, and sanitizes link destinations before `Markdown.ToHtml`; native preview uses text controls rather than executing HTML. No demonstrated HTML/script injection path was found.
- JSONL telemetry uses a fixed schema and bounded channel; the reviewed record fields contain only operation/status, enum format, bucketed size, version/count, and random IDs. No path/text leak or material writer defect was demonstrated. Rotation and shutdown durability are best-effort under deadline, as documented by the implementation.
- Windows/macOS GUI behavior, IME, file replacement semantics under crashes, and the remaining save-verification/replacement race were not reproduced with OS-specific harnesses here. The last is an explicit residual risk, not an assertion that current data has been lost.
