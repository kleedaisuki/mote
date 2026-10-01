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
   Cache the actual owned window content view into sibling `native-product.png`
   using AppKit bitmap display caching. This contains the native source/chrome/
   preview content (not the OS titlebar), requires no global screen capture or
   Screen Recording permission, and certifies a view raster rather than physical
   presentation. PNG signature and bounded view dimensions are checked.
7. Stage `中` through `setMarkedText:selectedRange:replacementRange:`. Native
   text must contain the candidate while canonical text excludes it.
8. Invoke actual Save As. Its command barrier must unmark/admit that candidate;
   saved canonical source equals the independent replacement, with no remaining
   marked input or dirty state.
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
