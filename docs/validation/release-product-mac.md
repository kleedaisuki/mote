# macOS release product workflow

Date: 2026-10-02. Scope: real product `NativeSource` shell/controller wiring,
release diagnostics and portable diagnostic guards. Hosted AppKit evidence is
pending; a Windows build cannot qualify native macOS input.

## Assignment and assessment

The accepted release direction permits multiple delivery files while retaining
Native AOT. Packaging is owned separately. This change neither switches UI
frameworks nor adopts stress-derived timing requirements. Existing canonical
Engine I/O/history and AppKit source input remain authoritative in their domains.

Review reused the decision handoff, native-source component validation and
[independent standards audit](../research/editor-experience-standard-audit.md).
No new speculative optimization or font/input-source/clipboard/global preference
change was made. The reviewed barriers preserve unadmitted text, freeze marked
input admission and reject stale source identities. No additional product defect
was established by static inspection; actual AppKit qualification is necessary.

The legacy `--check-native-mac-workflow` hardcodes a legacy source shell. Its
successful run is therefore insufficient for the new release default. Added
`MacReleaseWorkflowProbe`, using the actual NativeSource shell and production
controller, fills that specific evidence gap without treating GUI smoke as
editing acceptance.

## Hosted task contract

The composition root owns these additive entrypoints and their stdout markers:

- `--check-native-mac-release-workflow <input> <output>`:
  `mote-native-mac-release-workflow-ready` after success.
- `--check-native-mac-release-reopen <saved-file>`:
  `mote-native-mac-release-reopen-ready` after success.

The acceptance owner provides independently constructed small six-format UTF-8
fixtures. Each editing fixture contains exactly one ordinal
`mote-release-original`. The native workflow replaces it with
`mote-release-edited中`, preserving enclosing structured syntax. Inputs are
bounded to 1 MiB solely to bound diagnostic activity, not because 1 MiB defines
the product's capacity or an approved experience SLO. Outputs must be new files
beneath repository `.temp/` or `.cache/`; every existing ancestor through the
selected root must be a real directory without a symbolic link. Overwrite,
missing ancestors and escaping output paths are rejected before entering AppKit.

Actual stage witnesses:

1. Native view and controller projection equal the complete decoded input;
   AppKit source accessibility label is `Mote editor`.
2. Replace the marker via `NSTextView insertText:replacementRange:` with the
   non-CJK prefix. Wait for canonical admission and dirty status.
3. Invoke the actual text responder `undo:` then `redo:`. Whole native and
   controller readback must equal the predecessor/successor, respectively.
4. Invoke the product Find menu with one injected prompt answer. Verify its
   actual ordered native selection exactly selects the replacement prefix.
5. Invoke product Go To Line with one injected line-1 answer. Verify `(0,0)`.
6. Invoke Reload Settings. An in-memory diagnostic loader switches the existing
   theme policy from `mote-dark` to `mote-light`; source text must remain exact.
7. Stage `中` through `setMarkedText:selectedRange:replacementRange:`. Native
   text must contain the candidate while canonical text excludes it.
8. Invoke actual Save As. Its command barrier must unmark/admit that candidate;
   saved canonical source equals the independent replacement, with no remaining
   marked input or dirty state.
   Before any subsequent edit, wait under the unchanged watchdog for final
   complete source semantics: exact installation stamp/nonce, complete `[0,
   source-length)` coverage and the same current analysis presentation sequence.
   Require the installed source palette's actual stamp/nonce/semantic revision/
   theme and qualified geometry. For CSV, require the actual visible native Grid
   accessibility frame's ready identity to match that analysis; navigation must
   not be pending and every enumerated visible cell must be nonpending.
   Then cache the actual owned window content view into sibling
   `native-product.png` using AppKit bitmap display caching. The image now depicts
   the final committed `中`, not the precommit prefix. It contains native source/
   chrome/preview content (not the OS titlebar), requires no global screen capture
   or Screen Recording permission, and certifies a view raster rather than
   physical presentation. PNG signature and bounded view dimensions are checked.
   Emit fixed-field `native-product-semantics.json` with generation, version,
   installation nonce, presentation sequence, closed format kind, completeness,
   UTF-16 coverage/length, token/diagnostic counts and actual visible Grid counts.
   No source contents, values, filenames or exception messages enter this file.
9. Stage a second marked candidate `弃` and invoke real window `performClose:`.
   A one-shot diagnostic Cancel answer must be consumed by actual controller
   discard confirmation. The window remains visible, and the committed candidate
   remains exact and dirty rather than being lost on close. Native responder Undo
   restores the saved source before subsequent New/Open.
10. While the cancelled dirty document remains intact, invoke the registered
    `application:openFile:` delegate selector with the saved file. A one-shot
    Cancel answer must reject its synchronous admission and preserve canonical
    input. Native Undo restores the saved source. Product New followed by a real
    `application:openFile:` delegate callback restores that file in the existing
    window; this is not a file-picker answer or direct event invocation.
11. The external wrapper launches a second process with the reopen entrypoint;
    its newly created GUI/controller must equal the complete disk-decoded source.

The original file SHA-256 is checked unchanged. The external wrapper separately
compares saved bytes with its independent UTF-8 replacement oracle. The reopen
entrypoint is not merely `Document.Open` in the original process.

The composition root/acceptance owner manages opt-in trace initialization,
shutdown and strict-reader checks. This helper does not silently reconfigure a
second telemetry writer. Fixed diagnostic failures expose stage/type only, not
file contents or exception messages.

## Evidence and limitations

Local Release build of `Mote.Native` succeeded with **0 warnings, 0 errors**.
Final portable guard suite: **12 passed, 0 failed, 0 skipped**, retained in
`.temp/mac-release-guards/mac-release-guards-root.trx`. Guards cover missing,
duplicate and case-mismatched markers; Unicode/JSON/TOML/CSV marker acceptance;
external/missing-parent output rejection; nested `.temp`/`.cache` acceptance;
and existing-output rejection. The final run rebuilt both native and test
projects without compiler/analyzer warnings. An intermediate concurrent test
build failed on a separately owned Windows test's incorrect color property
names; that owner fixed it, and the final focused run succeeded.
The later owned-view capture addition separately rebuilt `Mote.Native` with
**0 warnings, 0 errors**; it still requires hosted AppKit execution.

Tests explicitly locate the repository root and pass it to the portable path
validator; they do not mutate process-wide current-directory state. Artifacts
from an initial diagnostic-guard iteration used test-output-relative artifact
directories before this was corrected; they are untracked build artifacts, not
source or release inputs.

### Finder/Open With delegate integration

Added `INativeExternalOpenShell` implementation and registered AppKit delegate
selectors `application:openFile:` (native BOOL) and `application:openFiles:`.
Actual native NSString paths are forwarded to the controller, which owns pending
input settlement, dirty-consent and stale-consent fences. The synchronous BOOL
means command admission; actual asynchronous I/O still reports UI failure through
the existing controller path. Callback exceptions and secondary error UI are
contained, with fixed error text rather than filenames/exception messages.

The multiple-files selector admits only an exactly one-member array. Multiple
documents produce an explicit single-document-product warning and a failure
reply; it never silently chooses the first or last file. Every plural callback
attempts exactly one `replyToOpenOrPrint:` response, success for admitted input
and failure otherwise. No extra window or workspace is introduced.

After this addition, portable guards are **16 passed / 0 failed / 0 skipped**,
including counts 0/1/2/100; rebuilt native and test projects produced **0 compiler
or analyzer warnings**. TRX:
`.temp/mac-release-guards/mac-release-external-guards.trx`. These tests qualify
portable guards only; hosted AOT workflow now exercises registered single-file
callbacks for both rejected dirty replacement and admitted clean replacement.
Actual Finder/Launch Services delivery and plural reply execution remain distinct
from that controlled callback evidence.

### Final edited semantic capture

The probe now preserves the root's ordinary bare-path parser/factory composition,
asserting the promoted NativeSource route rather than hardcoding a shell. Capture
moved from the precommit theme stage to the post-Save final-semantic stage. It
waits on actual state rather than adding an arbitrary delay, repeating edits or
increasing the 40-second watchdog.

Portable final-semantic guard tests reject stale version/nonce/analysis identity,
wrong presentation sequence, provisional/incomplete coverage, unfinished style,
unknown geometry, absent/stale CSV frames and pending cells. The combined suite
passes **29/29, zero skipped or failed**, with rebuilt native/test projects and
no compiler/analyzer warnings:
`.temp/mac-release-guards/mac-release-semantic-guards-complete.trx`. Earlier
iterations exposed invalid test projection fixtures (completeness/diagnostic-total
pairing and invented pending-cell syntax origins); those were corrected to valid
bounded fixtures without weakening the production evidence guards. Retained
failed TRX files are not counted as native product failures.

The sidecar uses a reflection-free fixed JSON writer for Native AOT. It does not
claim CSV display values are all complete merely because they are nonpending:
Complete/Clipped/Oversized/Missing remain distinguishable in the actual frame.
The supplied small rectangular CSV task independently checks its expected
three-row, three-column window and zero pending cells. Header is an ordinary
record in this model. Actual hosted success is still required for the new
post-edit capture/witness path; prior precommit screenshots do not establish it.

### Hosted stage-8 failure: observer depended on disabled experimental AX

The final-candidate hosted run `36928548957` failed the macOS x64 CSV task at
stage 8. Retained CSV artifacts are under
`.cache/release-ci-36928548957/evidence/release-evidence-osx-x64/product/csv/`.
The output file exists with the intended edit; trace includes successful Save v4
and subsequent current parse/publish/style v4. The old refusal path only reported
the eventual stage number, which was inadequate runtime evidence.

Source inspection identifies a definite observer mismatch:
`MacCsvGrid.AccessibilityFrame` is populated only when experimental
`MOTE_NATIVE_GRID_ACCESSIBILITY=1`; ordinary release tasks do not enable that
provider. The final-semantic guard therefore always saw a null Grid frame on
default macOS, irrespective of the actual installed table. This establishes the
observer's impossible precondition, not a claim that every actual cell was ready
or that an earlier `analysis.to_presentation` failure in the same trace was benign.

The replacement `ProbeReleaseRenderedGrid` observes the real renderer's installed
identity/projection, actual row slots, display/installed columns, rendered matrix
shape and pending-install flag. It does not register experimental AX or fabricate
its frame. Every native slot must be nonnull, have the correct absolute ordinal,
and be the exact row object from the installed projection. Missing/stale actual
slots, uninstalled columns, matrix-shape mismatch or pending installation refuse
certification. The existing source completeness, style, document/presentation
identity and nonpending-cell requirements remain unchanged.

At the unchanged stage-8 deadline, failure now freezes a separate
`native-product-refusal.json` with closed refusal codes and numeric installed/
semantic/analysis/style identities, coverage, revision, geometry, native Grid
identity/projection, row-slot counts, missing-slot count, column/shape state,
navigation pending and whether the experimental AX frame exists. It also attempts
`native-product-failure.png` of the existing owned content view. Neither output is
the success sidecar, nor can capture mark a failed workflow successful. No source
or cell values, paths or exception messages appear in the diagnostic JSON.

Portable source/Grid/refusal guards now include actual-renderer observations with
AX absent, exact installed row objects, missing/different slots, uninstalled
columns, wrong matrix shape and pending installation. The first expanded run
passed **35/35**. Hosted AppKit rerun of the instrumented corrected observer is
still necessary; do not count this source-level mechanism diagnosis as that run.

The controller now exposes a bounded last analysis-presentation failure record.
Refusal diagnostics include its closed category, HResult, generation/version,
analysis serial, current analysis serial and an explicit identity/serial match.
If final semantics succeeds but such a historical record exists, a separate
`native-product-analysis-failure.json` preserves it instead of erasing it or
labeling the newer successful version as failed. No exception/message object is
retained. Tests verify that a version-3 failure reports `current_match=false`
against current version 4, while the same current identity/serial reports true.
The final expanded portable run passes **37/37**, zero compiler/analyzer warnings:
`.temp/mac-release-guards/mac-release-render-history-qualified.trx`.

No local AppKit runtime execution occurred. The new workflow must run on the
actual hosted macOS AOT artifacts before it can supply release evidence. Even a
successful hosted run proves in-process native protocol/menu/window behavior,
not external physical keyboard delivery, a real Pinyin input method, VoiceOver
interaction, physical pixel presentation or user-rated responsiveness. Injected
prompt/discard answers exercise the command barrier and callback wiring, not
the visual usability of modal dialogs. No 100 MiB synthetic work is promoted to
ordinary-task demand, and no arbitrary 16 ms limit is added.

## References and rationale

- [Apple NSTextView](https://developer.apple.com/documentation/appkit/nstextview)
  and [NSTextInputClient](https://developer.apple.com/documentation/appkit/nstextinputclient):
  production platform text input, selection and marked-text protocol instead of
  a bespoke imitation of native input semantics.
- [AppKit openFile delegate](https://developer.apple.com/documentation/appkit/nsapplicationdelegate/application(_:openfile:)),
  [openFiles delegate](https://developer.apple.com/documentation/appkit/nsapplicationdelegate/application(_:openfiles:))
  and [replyToOpenOrPrint](https://developer.apple.com/documentation/appkit/nsapplication/reply(toopenorprint:)):
  OS delivery protocol and explicit single-document rejection semantics.
- [Standards audit](../research/editor-experience-standard-audit.md), including
  its primary HCI research synthesis: task/device-specific latency evidence does
  not authorize a universal editing threshold. Exact workflow witnesses and
  integrity are distinct from later subjective/physical experience validation.
- [Previous source component validation](mac-product-native-source.md): typed
  stamp/nonce admission, canonical history and pending-input safety remain intact.
