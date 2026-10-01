# Release product readiness: independent review

Date: 2026-10-02. Reviewer: release_product_review.
Initial reviewed HEAD: `011e5102fd79b61f81ae9507437ecbb981b13afd`.

## Purpose and boundary

The new objective permits multiple delivery artifacts, retains Native AOT, and
requests a usable published editor with release page, manual and release notes.
This review assesses release-critical product behavior, not the former 100 MiB
performance program. Packaging permission does not by itself select a text
representation or certify either existing presentation.

Read the decision handoff, standards audit, Native README, previous integration
review, launch parser, source contracts/binding, source controller, surrounding
Find/Goto/history/Save paths, Windows and Mac source adapters, portable candidate
tests, and existing native workflow entrypoints. No production/test writes,
native UI, clipboard/input-source/global setting changes, CI dispatch, builds or
repeated validation were performed. This is static review and retained-evidence
assessment; neither real composition nor native rendering was exercised here.

## Finding: P2, a completed Find can replace a newer user selection

Location: `src/Mote.Native/NativeEditorController.Source.cs:146-158`,
`SourceViewChanged`; completion path:
`src/Mote.Native/NativeEditorController.cs:1268-1310`, `StartFind`.

Triggering sequence:

1. Start Find in NativeSource. `StartFind` captures `startAnchor`, `startActive`,
   document snapshot, cancellation token and `_findSerial`, then searches a worker.
2. Before its posted completion is delivered, the user moves the native caret or
   selects another range. The adapter emits a current identity-qualified view.
3. `SourceViewChanged` updates `_navigation` without cancelling the outstanding
   Find or advancing `_findSerial`. Selection is not a document-version change.
4. Find completion checks lifetime, serial, cancellation and document version,
   but not the newer navigation state. All checks pass. It installs and reveals
   the match computed from the old selection, overriding the user's newer action.

This differs from both established `SelectionChanged` (lines 1008-1014) and
`CanvasSelected` (lines 1125-1127), which invalidate Find on a changed selection.
Impact is a confusing caret/selection jump, not demonstrated source corruption.
Confidence: high for the executable control flow; native event timing was not
reproduced. It is a bounded necessary correction before candidate promotion, not
a reason to redesign the search engine.

Remedy: compare incoming anchor/active with current navigation and call
`InvalidateFind()` only when selection actually changes, before committing it.
Viewport-only observations must not cancel a search. If a no-change source
candidate can independently change selection, apply the same rule there. Add a
deterministic test that holds the queued worker completion, moves selection, then
delivers it; add the complementary same-selection/viewport-only case.

The candidate fake currently disables `FindRequested` and `FindNextRequested`
subscriptions and returns null from `PromptFind`
(`tests/Mote.Tests/NativeSourceControllerTests.cs:451-455,488`). Its passing suite
cannot discover this failure or establish candidate Find correctness.

## Integrity assessment

No additional unresolved source-loss defect was established in the inspected
source. This is not a claim that all failure paths were executed.

- Engine owns canonical text, file I/O and history. `NativeSourceBinding.Prepare`
  checks snapshot identity, generation/version/nonce, scalar boundaries, exact
  projection round trip and selection bounds before producing a change.
- Real marked text is not admitted; Windows settlement requires actual exact
  acknowledgment before authorizing a command. The earlier false-success and
  blanket-settling-guard defects are corrected in inspected source.
- Guarded range publication is used for canonical Undo/Redo instead of rebuilding
  the native document. Adapter failure does not roll back canonical text/history.
- Unadmitted input is distinct from retained canonical data; Save/replacement
  cannot silently discard it. Explicit recovery consent is required, with
  canonical dirty changes retained and failed recovery still barred.
- Global Goto/SelectAll/Copy use full-source coordinates. Existing portable
  offscreen navigation/history tests exercise these paths, not real native
  selection direction or accessibility geometry.
- Embedded NUL is deliberately noneditable in NativeSource while canonical text
  remains retained/saveable. State this accurately in the manual rather than
  claiming arbitrary plain-text native editing.

## Actual acceptance gaps and misleading substitutions to avoid

| Existing evidence | What it establishes | What it does not establish |
| --- | --- | --- |
| `NativeWindowsWorkflow.ps1` | Real RichEdit WM_CHAR, Save and fresh GUI reopen for explicitly launched `--legacy-page` | NativeSource or ordinary Continuous acceptance; both launches hardcode LegacyPage |
| `NativeMacWorkflow.ps1` | Published diagnostic `--check-native-mac-workflow`, in-process AppKit seam and independently expected output bytes | Ordinary NativeSource launch, external input/IME, or a fresh GUI process reopen |
| `--check-native-source-capability` CI lane | Reference replica experiment, readback/history/Save under its own fixture/runtime scope | Real product shell/menu/focus/recovery/theme workflow |
| `NativeSourceControllerTests` | Independent fake replica and canonical source/history/Save admission models | Find, Format, applied themes/config reload or OS execution; fake Format events and SetTheme are no-ops |
| Four-RID publish / smoke | AOT linking, executable inventory and window create/close | Input, composition, source-reader range correctness or save/reopen workflow |

The README already acknowledges most limitations. Release notes must not erase
them merely because overall CI is green. Last retained published CI included a
non-gating pre-Save diagnostic failure; that is neither successful Save evidence
nor proof that product Save itself failed.

## Default recommendation

**Do not promote NativeSource on the current evidence alone.** Preserve Continuous
as the established default until the release owner obtains candidate-specific
ordinary product results. This is preservation of an existing external route,
not a certificate that Continuous is already release-ready.

For the actual release qualification, NativeSource is a sensible preferred
candidate: one OS text surface owns source input/layout/selection, removing the
custom canvas/input-island coordination burden for ordinary files. Its remaining
release obligation is much smaller and more concrete than proving 100 MiB eager
styling. No demonstrated ordinary-file result in the retained evidence justifies
claiming that it is already faster, better at IME or more accessible. Multi-file
packaging does not resolve those unknowns.

If its candidate-specific ordinary workflows pass after the Find fix, making it
the release default is a reasonable explicit product decision. Preserve named
compatibility/diagnostic routes; do not silently select profile by file size.
Neither a wholesale toolkit migration nor an unapproved numeric latency gate is
necessary to close the identified release gap.

## Bounded release qualification requested

1. Execute the **actual candidate product** on Windows and Mac AOT packages,
   starting with a naturally sized six-format corpus. Prove open, local edit,
   canonical Undo/Redo, whole-document Find/Goto, exact Save, normal process exit
   and fresh GUI reopen. Record which commands use real external/native input
   and which use a seam. Do not reuse a Reference/Legacy marker as qualification.
2. Exercise theme change/config reload without losing selection/text/history,
   using an isolated `MOTE_HOME` under repository `.temp`; confirm default and
   overridden mutable paths. Do not modify the reviewer's real `~/.mote`.
3. Retain focused composition/pending-input tests: committed Chinese input,
   composition cancellation, a command during marked text, vetoed settlement,
   cancelled discard consent, successful recovery and failed recovery. Actual
   IME is a distinct observation, not a manually emitted WM_CHAR claim.
4. Keep source/encoding/original-protection negative tests and useful capacity
   regressions. Do not turn 100 MiB, dense repeated JSON, or unknown physical
   presentation latency into an ordinary-user release gate without task evidence.
5. Publish a manual/release known-limit table distinguishing platform/RID proof,
   real input proof, unsigned macOS installation and unavailable geometry. Avoid
   presenting human review or automation limitations as functionality proven.

These are task endpoints, not a mandatory combinatorial performance experiment.
Use existing passing model evidence where unchanged; rerun only affected models
and native workflows needed to establish the release artifact identity.

## External grounding already retained

The [standards audit](../research/editor-experience-standard-audit.md) preserves
task-specific HCI research and the reason frame intervals cannot become universal
input budgets. The [delivery research](../research/delivery-constraint-alternatives.md)
separates production packaging/runtime practices from UI/IME claims. Microsoft's
[Native AOT documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
supports self-contained deployment, not native UI correctness. Reuse those
established findings; this review does not remeasure or extrapolate them.
