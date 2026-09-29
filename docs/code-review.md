# Code review: correctness and data preservation

Review date: 2026-09-29. Scope: `Mote.Engine` document/rope/I/O/history, desktop open/save/paging/analysis/render flow, format HTML export, and JSONL telemetry. This is an executable-path review, not a claim that all GUI platform behavior or crash consistency was tested.

## Findings and disposition

### P2 — Markdown cached obstruction could prevent recertification after an adjacent separator edit (fixed in `b12608c`)

**Trigger and impact.** In the >16 MiB flat-document path, `TryCertifyFlat` records one failing line as `_uncertifiedLine`. `TryMapUncertifiedLine` then assumes any edit strictly before that source span cannot remove the obstruction and skips recertification. That assumption is false when the failure is *missing blank-line context* rather than the text on the failing line: inserting a newline just before the preceding delimiter makes the document certifiable without touching the bad-line span. The session keeps returning `Provisional` after the user fixes the syntax, while a fresh session returns `Complete`. This is a persistent missed upgrade, not a false validity or data-loss claim.

**Reproduction.** `.temp/code-review-markdown/Program.cs` built an 18,000,312-character file beginning `Alpha\nBeta\n\n` followed by 300 independent flat blocks. Initial Full result was `Provisional`. Insert `\n` at old offset 5, yielding `Alpha\n\nBeta\n\n`, then analyze the new snapshot with the correct single `VersionedEdit`: same session `Provisional`, fresh session `Complete`. The edit is before the cached `Beta` span, so the cache shifts it instead of rechecking separation.

**Current correction.** A missing-separator obstruction now records a dependency span from the preceding block through the failing line, so an edit to the adjacent line ending triggers recertification. Count-budget failures are not cached as intrinsic line failures. Re-running the exact probe after the fix returned initial `Provisional`, edited-session `Complete`, and fresh-session `Complete`.

### P2 — Markdown flat projection trusted an unbounded caller viewport (fixed in `b12608c`)

**Trigger and impact.** `MarkdownIncrementalSession.ProjectFlat` says it projects only a viewport even for Full requests, but directly uses `request.VisibleRange` without an internal bound, and checks cancellation only once per `FlatRun` rather than per block. A direct caller can pass the whole 100 MiB file as its visible range; the method will reparse and retain every certified block's semantic node, defeating the large-file bounded-projection contract. This is a resource/latency hazard, not evidence of incorrect semantics.

**Reproduction.** The `.temp/code-review-markdown` probe cached a 30,005,000-character flat Markdown file with 5,000 blocks. A same-version Full request with range `[0,100)` projected one node and allocated 25,472 managed bytes. A same-version Full request with range `[0,source.Length)` projected all 5,000 nodes and allocated 125,350,584 managed bytes. Measurements are per-call `GC.GetTotalAllocatedBytes` deltas on local Release/.NET 10 Windows, not peak RSS. A separate 30 MiB wide-range cancellation probe took about 14 ms to honor a 1 ms cancellation request; this scale alone does not justify a latency severity claim, but the only-per-run check is visible in source.

**Current correction.** `ProjectFlat` now caps projected block starts to a 256 Ki UTF-16 window and 2,048 blocks independently of caller range/scope; an intersecting certified block may extend at most 64 Ki beyond the window so a caret inside that block is not left without a node. Whole-document `Complete`/diagnostic count remains grounded in prior certification. The inner block loop checks cancellation. Re-running the exact 30,005,000-character probe after the fix projected 44 nodes and allocated 1,103,272 managed bytes for the wide request, versus 5,000 nodes and 125,350,584 bytes before; the narrow request still projected one node and allocated 25,472 bytes. These are single-run allocation measurements, not peak RSS or a cross-platform benchmark.

### P1 conditional — YAML key-budget accounting stopped after any semantic downgrade (fixed in current working tree)

**Trigger and impact.** In `src/Mote.Formats/YamlIncrementalSession.cs`, `StreamProjector.ParseMapping` continues adding every canonical mapping key to a `HashSet<string>` while `_budgetExceeded` is false, but increments `_keyChars` only under `if (_complete)`. An unsupported/custom-tag or oversized key can set `_complete = false` without setting `_budgetExceeded`; all later distinct keys in that mapping are then retained without contributing to the advertised 2 MiB `MaxKeyChars` budget. A sufficiently large YAML file with an unsupported key followed by many ordinary unique keys can therefore consume memory proportional to all keys, despite downgrading its result to `Provisional`. The failure path is visible directly in `ParseNode`/`Unsupported`/`Retain` and the conditional accounting in `ParseMapping`; no timing claim is needed.

**Current correction.** `StreamProjector.ParseMapping` now switches `_budgetExceeded` on any global semantic downgrade, clears its current key set, and stops accumulating later keys. The source invariant was inspected; an adversarial memory test remains desirable before claiming a strict process-memory bound.

### P2 — YAML full-stream giant-scalar guard missed folded plain scalars (fixed in current working tree)

**Trigger and impact.** `YamlIncrementalSession.RequiresBoundedFallback` checks single-line length, open multiline quotes, and `|`/`>` block-scalar bodies before invoking SharpYaml's full event parser. YAML also allows a plain scalar to fold across arbitrarily many short indented lines. Such input passes this pre-scan yet SharpYaml assembles a single large `Scalar.Value`; the 1 MiB scalar-body guard does not prevent that allocation. With a 2,700,011-character source (`"key: first\n"` plus 300,000 repetitions of `"  second\n"`), the `.temp/code-review-yaml` probe confirmed one folded scalar, a `Complete` full-session result, and 8,478,720 managed bytes allocated during analysis on local .NET 10 Release/Windows. This is an allocation measurement, not peak RSS or a 100 MiB extrapolation; it suffices to disprove a fixed 1 MiB scalar materialization bound.

**Current correction.** The pre-scan now tracks potential folded plain-scalar continuations and routes an oversized body to bounded provisional analysis before SharpYaml's event parser. The prior counterexample was used by the Formats owner in a local probe; the implementation is conservative and was source-inspected here. It is not a proof of an absolute SharpYaml allocation ceiling for every YAML grammar form.

**Independent recheck.** Re-running the 2,700,011-character counterexample after the fix produced `Provisional` with null global diagnostic count. The probe allocated 10,327,296 managed bytes in this run; that figure is not evidence of a memory reduction versus the prior 8,478,720-byte run, only of honest completeness. Larger-file peak RSS remains a separate benchmark question.

**Additional budget review.** The first streaming implementation also retained an unbounded `_anchors` dictionary after semantic downgrade. The Formats owner added a 16,384-name/2 MiB name-char cap, clears the anchor index on downgrade, and suppresses unsupported undefined-alias claims when that index is incomplete. A source-density preflight now routes excessive short semantic lines or structural markers to provisional analysis; the small exact-parser threshold was lowered to 256 KiB. These changes were source-inspected. A separate >threshold probe with 140,000 compact sequence items had previously allocated 33,652,616 managed bytes for 1.12 million UTF-16 chars while returning Complete; it demonstrated why scalar size alone was not a sufficient event-density guard. The earlier 242 MB allocation probe was **below** the old small-parser threshold and must not be attributed to the streaming path.

### P2 — JSON skipped recovery regions previously claimed exact Complete (fixed in current working tree)

**Trigger and impact.** `JsonIncrementalSession.Parser.Value` returns without inspecting a subtree beyond its depth limit, and malformed trailing content is diagnosed as one suffix rather than structurally inspected. The first implementation still returned `AnalysisCompleteness.Complete` with a global diagnostic count. Under the session contract, this was a false claim about whole-document syntax/semantic inspection; a deeply nested object could hide additional duplicate-key or malformed-token facts inside the skipped region.

**Current correction.** The parser now marks such depth/recovery skips uncertain, returns `Provisional` with viewport coverage, and leaves `TotalDiagnosticCount` null. This was source-inspected after the Formats owner fixed it; no independent deep-nesting regression was added in this review.

### P2 — Small YAML/TOML syntax failures were counted as globally Complete (fixed in current working tree)

**Trigger and impact.** The small-document session paths invoke whole-string parsers, then previously labeled their outputs `Complete` solely because the resulting diagnostic list fit a cap. A parser can stop semantic traversal or lose ownership context after a syntax error. This makes `TotalDiagnosticCount` appear exact although later semantic checks did not run. An independent `.temp/code-review-yaml` probe showed `bad: [\nmore: *missing\n` returning YAML `Complete`, count 1, and only `yaml.syntax`; the later alias was never checked. A TOML probe showed `b = 1\nb = 2\n` reporting a duplicate key, but `x = ???\nb = 1\nb = 2\n` returning only three syntax diagnostics and **no** duplicate, while `TomlIncrementalSession.AnalyzeComplete` still returned `Complete`, count 3. These results demonstrate incomplete checking rather than merely a different diagnostic policy.

**Current correction.** Formats owner changed YAML small-session `yaml.syntax` results to `Provisional` with null total. The TOML small path now conservatively claims `Complete` only when the validated whole-string parse has **zero diagnostics**; any diagnostic returns `Provisional` and null total. The two counterexamples were independently re-run after the fix and both returned `Provisional` with null total. This sacrifices some potentially knowable global counts but avoids a false completeness assertion. JSON's streaming recovery now marks skipped structure uncertain, but this review did not exhaustively prove every malformed JSON case.

### P2 — Undecodable JSON keys could create a false duplicate while claiming Complete (fixed in current working tree)

**Trigger and impact.** `JsonIncrementalSession.Parser.StringValue("key")` returns null after an invalid escaped surrogate or other undecodable string, but `Property` substitutes `string.Empty` for the missing decoded name and inserts it into the object-name set. Two *different* invalid names can therefore be reported as a duplicate even though equality was never established. The `.temp/code-review-yaml` probe used `{"\\uD800":1,"\\uD801":2}` and observed `Complete`, `TotalDiagnosticCount = 3`, and diagnostics `JSON_STRING`, `JSON_STRING`, **`JSON_DUPLICATE_KEY`**. The syntax errors are real; the duplicate semantic diagnostic is not supported. This contrasts with the parser's new Provisional behavior when recovery skips structure.

**Current correction.** The streaming parser now excludes undecodable names from duplicate-key checking and marks the result uncertain. Re-running the exact counterexample after the fix produced `Provisional`, null global count, and only two `JSON_STRING` diagnostics.

**Legacy-policy correction.** `JsonPolicy.Analyze(string)` initially used `key.Value ?? string.Empty` before duplicate detection and produced the same spurious result. Formats owner changed it to skip duplicate comparison when decoded key identity is unavailable. Re-running the exact counterexample against the compatibility API now yields only the two supported `JSON_STRING` diagnostics. The reviewer did not edit that production file.

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
- The new restricted large-TOML path was reviewed after freeze: it validates bounded standalone statements with Tomlyn, tracks global ownership in a trie, excludes array-of-tables, and downgrades on malformed/oversized/unrecognized statements. No concrete false-`Complete` TOML counterexample was found in this review. This is not a proof of full TOML 1.1 equivalence; the owner's differential fuzz and future adversarial tests are the relevant evidence.
- Windows/macOS GUI behavior, IME, file replacement semantics under crashes, and the remaining save-verification/replacement race were not reproduced with OS-specific harnesses here. The last is an explicit residual risk, not an assertion that current data has been lost.
