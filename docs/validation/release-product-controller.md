# First-release native product controller qualification

Date: 2026-10-02. This record covers current implementation and portable/native
control checks, not a declaration that the release is published.

## Product choice and external contracts

The first-release work retains Native AOT and the existing Engine/Formats stacks.
Multi-file delivery means application packaging, not multiple editable files,
projects, workspaces, language servers, CoreCLR or a toolkit replacement.
The full native source product is the qualification candidate because ordinary
editing has one native source locus. Neither this rationale nor existing
reference-probe timings certify physical input, real Pinyin, readers or latency.

The default remains Continuous while the explicit `--native-source` candidate
is qualified. Legacy and historical diagnostic routes remain unchanged. A
default promotion must be a separate reviewed change after all supported
Native AOT architectures pass the actual release workflows.

## Necessary corrections

1. **Stale Find selection.** Source-native selection updates omitted the Find
   cancellation used by Legacy and Continuous. A queued asynchronous result
   could override a newer user caret. Real selection changes now invalidate the
   search generation and clear searching chrome; pure viewport updates do not.
   The regression exercises subscribed Find events and withholds UI completion
   delivery before a new selection. Its initial test incorrectly asserted the
   fake adapter's installed selection rather than the controller's actual source
   binding; that assertion was corrected, not disguised as a product failure.
2. **Literal RichEdit import.** Windows `EM_SETTEXTEX` can recognize a leading
   RTF header in ordinary text. Source installation now uses checked literal
   `SetWindowTextW` with complete readback certification. This is a correctness
   correction, not adoption of the held diagnostic stream optimization.
3. **Deferred native selection.** Windows source observations wait for deferred
   candidate admission instead of applying the old text's newline map to a new
   buffer. No clamped/invented coordinates are admitted.
4. **Layout convention.** NativeSource Auto respects format policy defaults,
   including full-width plain text. Only historical/Legacy routes retain the
   old forced split. Explicit configuration still overrides conventions.
5. **External application open.** Optional `INativeExternalOpenShell` delivers
   OS paths through canonical pending-input, Save and dirty-discard admission.
   The controller captures version after composition settlement but before
   potentially reentrant dialogs. A newer edit or open invalidates stale consent.
   True acknowledges admission for asynchronous I/O, not successful persistence
   or successful load. AppKit owns single-file delegate delivery and explicitly
   rejects multi-file requests; this does not create a workspace.
6. **CLI identity.** Unknown options refuse before shell creation. `--` escapes
   option-looking file names. `--version` derives the assembly version and
   `Mote.Native.csproj` declares `0.1.0`. Additive release AppKit diagnostics use
   the production NativeSource shell/controller and opt-in telemetry lifecycle.

## Evidence

The selected current controller/parser/portable AppKit guard command was:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release `
  --filter 'FullyQualifiedName~NativeSourceControllerTests|FullyQualifiedName~NativePresentationProfileTests|FullyQualifiedName~MacReleaseWorkflowTests' `
  --logger 'trx;LogFileName=release-controller-2.trx' `
  --results-directory .temp/release-controller -v minimal
```

Result: **76 passed, 0 failed, 0 skipped**. This run surfaced one new platform
annotation warning on the pure AppKit multi-file count helper; the platform owner
corrected that annotation, rather than treating a warning as clean evidence. It
does not execute AppKit on this Windows machine. Earlier overlapping selected
controller/parser run: **52/52**, zero warnings/errors; do not add these counts.

Final combined affected selection added `WindowsReleaseSourceTests` to the filter:
**85 passed, 0 failed, 0 skipped**, **zero build/analyzer warnings and errors**.
TRX: `.temp/release-controller/release-product-final.trx`. It includes the current
AppKit external-open pure model, Source controller, launch parser and Windows
hidden native controls; it still does not execute AppKit or a visible AOT product.

Windows actual hidden RichEdit/TOM qualification and AppKit guard records are
separate in [Windows](release-product-windows.md) and [macOS](release-product-mac.md).
These are not substitute proofs for visible published product workflows.

## Actual release workflow contract

`NativeReleaseProductWorkflow.ps1` owns six valid representative task fixtures
and independent expected byte oracles. Their single source marker changes from
`mote-release-original` to `mote-release-edited中` inside a value/field/text span;
the transformation does not deliberately invalidate structured formats.

Windows `Invoke-NativeWindowsReleaseProduct.ps1` copies the protected input to a
new output, launches actual `--native-source`, answers native Find and Go to Line
prompts, performs one selected RichEdit Unicode replacement without using the OS
clipboard, checks menu and native canonical Undo/Redo, cancels an actual dirty
close prompt, saves and waits for clean chrome, then starts a fresh GUI process
and reads the complete native source. It records fixed stage/outcome evidence and
window-owned `PrintWindow` PNG. Automation is native message delivery, not
physical keyboard, real input-method composition or a screen-reader session.

AppKit `--check-native-mac-release-workflow <input> <output>` uses actual source
protocols, responder history, navigation, theme reload, marked-text Save barrier,
dirty-close Cancel, New and delegate document-open. The separate
`--check-native-mac-release-reopen <saved>` creates another product GUI process.
AppKit captures the actual content view into a native bitmap; neither capture
method is a claim about photons, display latency or physical keyboard delivery.

Both suites retain artifacts under repository `.cache`/`.temp`. Process watchdogs
bound runner resources; they are not user-approved responsiveness targets. CI
and release owners must attach exact executable/source identities and hosted
results before describing the candidate as promoted or a release as delivered.
