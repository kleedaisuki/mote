# CSV Grid accessibility contract: independent review

Date: 2026-10-01. Reviewed checkout: `4ea4a2ac21a3000ba17c7c812650ae0d91625914`.
Scope: proposed `docs/csv-grid-accessibility-contract.md`, current native Grid
adapters, controller command/navigation admission, and existing source UIA ABI
helpers. No production code, tests, OS state, stage, commit or push were changed.

## Decision

The bounded-window Table decision is coherent and materially safer than exposing
unproved file totals or absolute indices inside a small local Grid. No reason
was found to reverse it. **Both reported internal API gaps are resolved by the
targeted section-3 revision; no remaining substantive design blocker was found
within this review scope.** These were design-contract findings, not evidence
that the as-yet unimplemented Grid providers fail externally. Implementation and
external acceptance gates below remain mandatory.

## Findings

### Resolved P2 / high confidence: the action seam cannot express promised selection mutations

Location: contract section 3, `IGridAccessibilityActions.Select(id, cell)`;
section 5, rectangle/empty selection and UIA AddToSelection/RemoveFromSelection
and AX selected-cell setter requirements.

Counterexample: select `(r,c)`, then add `(r,c+1)`. The resulting two-cell rectangle
is explicitly supported, but the only proposed selection operation replaces the
selection with one cell. Removing the last selected cell or applying an AX array
containing a two-by-two rectangle also cannot pass through this interface. Using
`Command(Select)` does not solve it: current `GridIntentRequested` merely cancels
pending Copy and changes the clipboard serial; it does not mutate adapter-owned
selection. Direct provider field writes create the competing selection owner
that the contract correctly prohibits.

Required correction: provide an adapter-owned, synchronous atomic selection
mutation operation with a closed operation kind: replace with one cell,
add/remove exactly one item, replace with an admitted rectangle, and clear.
An alternative is a complete desired-selection transaction with a selection
revision, plus adapter-side validation. Preserve nullable empty selection and
anchor/active/WholeRows semantics. Return an explicit unsupported-selection
result mapped to an appropriate OS failure rather than silently filling holes.
Add/remove must calculate against the latest adapter selection during admission:
the child identity intentionally survives selection changes, so a provider's
older captured frame is not authority to overwrite a newer selection.

Define the cross-window case explicitly: validate add/remove against the full
retained rectangle, not just its visible intersection; define whether an AX
bounded-array setter replaces the whole selection (recommended explicit rule)
or modifies its intersection. For a rectangle spanning outside the window,
removing a locally apparent edge cell can still create a hole in the full set.
No source, Undo, dirty, clipboard or native focus changes should be implicit.

Evidence: [Microsoft AddToSelection](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-iselectionitemprovider-addtoselection)
defines addition to the selected collection, not rectangle expansion with
unrequested cells. Current owners are `WindowsCsvGrid` private
`_row/_column/_anchorRow/_anchorColumn/_wholeRows` and `MacCsvGrid` private
native row selection plus `_column/_anchorRow/_selection`.

### Resolved P2 / high confidence: command completion cannot be acknowledged through the existing void dispatch seam

Location: contract section 3 `Command(NativeGridIntent)` and result model;
section 7 asynchronous Reveal acknowledgement; section 2 controller seam;
production `NativeEditorController.Grid.cs::GridIntentRequested` and adapters'
`Action<NativeGridIntent>`/`IntentRequested` dispatch.

Counterexample: a Reveal intent passes a provider's earlier ready check, but
controller admission subsequently rejects due to composition or a stale
presentation. Existing dispatch returns void. Missing origin returns after
`ShowError`, and source focus is a void shell call. A provider cannot distinguish
rejection, admission or actual source focus from dispatch returning normally.
Replace similarly can be cancelled/rejected by a modal dialog after admission;
Copy may fail asynchronously. Returning `Applied` after dispatch or inventing
`AcceptedAsyncCommand` for every call would violate the proposed truthful
acknowledgement contract even without an exception.

Required correction: explicitly authorize a narrow internal controller command
admission/result seam, with identity-bound completion facts for asynchronous
commands, rather than specifying only adapter-local entry points. Separate
admitted from completed, cancelled, rejected and failed. For Reveal, admission
must bind the exact source range and completion must verify source selection
and actual focus on the UI thread after reentrancy; do not claim success merely
because `FocusSource()` was called. Synchronous Reveal may instead return a
verified terminal result if that is genuinely the execution path. Keep existing
physical command entry points compatible and use one shared admission mechanism.
Provider retirement must invalidate subscriptions without retaining documents
or delivering late events to retired nodes. No clipboard operation is necessary
to implement or validate the Reveal-only accessibility action.

This is a missing planned internal API change, not a demand to change public
Engine/Formats/configuration contracts. Existing controller rejection and
one-transaction Replace checks should remain authoritative.

## Targeted revision verification

The architect's frozen section-3 revision was independently reread without
rerunning unrelated code/tests:

- `MutateSelection` now expresses ReplaceRectangle/Clear/AddCell/RemoveCell and
  admits against the latest full retained rectangle. Selection-only identity
  reuse cannot authorize stale-frame unions/differences. Unsupported shapes are
  atomic no-ops; empty AX arrays clear the entire retained selection; nonempty
  arrays replace it, capped before allocation. Explicit whole-row semantics are
  not inferred from cell arrays. UIA unsupported operations and void AX setter
  refusal/readback are distinguished.
- Separate `IGridAccessibilityCommands` now requires controller admission and
  an identity-bound terminal acknowledgment. The existing void dispatch is
  explicitly insufficient. Accepted-token ordering precedes possible synchronous
  completion; only one outstanding accessibility command is retained. Reveal
  success verifies exact source selection plus actual native focus after
  reentrancy, partial selection/focus failure is disclosed, and teardown/supersession
  has terminal outcomes. No Invoke/AX command action is advertised until this
  seam and its external evidence exist.

These changes close the two specific gaps. Their greater precision does not
imply that command receipts, rectangle setters or platform providers have been
implemented or tested. A synchronous verified Reveal outcome remains a possible
internal simplification if compatible with the chosen asynchronous OS admission
and reentrancy contract; this is not an additional required redesign.

## Confirmed design strengths

- GetItem/local GridItem indices and AX cell ranges describe the bounded Table;
  absolute ordinals are labels/headers. The gutter is not a CSV data column.
- Sparse pending slots, Missing, real empty and oversized fields stay distinct;
  displayed values never become source or clipboard authority.
- Document/window/presentation retirement prevents retained wrappers from
  becoming another coordinate; same-version placement and readiness are covered.
- Selection is independent of source navigation; explicit origin-backed Reveal
  and composition refusal preserve existing source input ownership.
- Memory is bounded by admitted slots/display, not total rows or visited history.
- ItemContainer/full-file virtualization is excluded coherently rather than
  advertising incomplete realization. No full-file ScrollItem promise exists.
- External process assertions, all UIA views, Mac AX lookup, actual reader speech,
  real composition and both AOT architectures are distinguished from synthetic
  native clicks, labels and successful compilation.

## Mandatory implementation gates, not additional defect findings

1. **COM thread/apartment realization:** existing `UiaComInterface` uses ordinary
   `StrategyBasedComWrappers` with `CreateComInterfaceFlags.None`; existing
   provider flags are server-side, not evidence of an STA-affine Grid wrapper.
   The contract already correctly makes this a discriminating early gate.
   Resolve wrapper marshalling and UI-thread initialization before implementing
   synchronous action callbacks, then query from a separate MTA process on both
   Windows RIDs. [ProviderOptions](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-provideroptions)
   routes an actually STA-based provider to its STA; setting UseComThreading does
   not create that ownership. No claim that the existing wrapper is non-agile
   or that a particular replacement has been proven is made here.
2. **Tree composition:** the Table HWND and its header plus sibling scrollers
   require a concrete native-parent composition choice. Verify FromHandle,
   Raw/Control/Content views, focus and hit testing before default activation.
   The contract recognizes this risk; no proxy-merging success was reproduced.
3. **Mac retained element identity:** `CellView` currently reuses NSTextField
   objects through `makeViewWithIdentifier:owner:` and changes their labels.
   Merely adding labels/selectors to these recycled views does not prove the
   proposed retained-wrapper retirement rule. Retain an external cell across
   viewport replacement and exercise reads/actions; choose bounded semantic
   wrappers if native recycling cannot satisfy the rule. Native row selection
   is also not the custom rectangle, as the contract already states.
4. **ABI/resource ownership:** Grid/Table/Selection interfaces introduce owned
   interface arrays, provider references, runtime IDs and event variants beyond
   source TextPattern helpers. Specify exact SDK vtables and SAFEARRAY element
   ownership, verify empty arrays, failure cleanup, stale retained elements and
   detach on win-x64/win-arm64. Mac selectors returning NSRange/CGRect require
   exact architecture calling conventions and native retained-release checks
   on osx-x64/osx-arm64. Existing single-binary builds are not these new ABI tests.
5. **Platform/speech evidence:** API patterns and labels do not prove Narrator
   or VoiceOver navigation. In particular, reader-local table navigation may
   not cross the window boundary; named logical controls/Go-to must actually be
   reachable and orientation restored after replacement. This usability gate
   is explicitly present in the contract and must remain in release evidence.

## Source checks and limits

Verified primary Microsoft documentation on Table required patterns/header
relationships, Grid GetItem and selection addition:
[Table](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-supporttablecontroltype),
[GetItem](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-igridprovider-getitem),
[SelectionItem](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingselectionitem).
These support the local-table mapping and a real provider for ragged empty
layout cells; they do not make whole-file extent mandatory for a deliberately
scoped named Table.

Apple's linked `accessibilityRowIndexRange()` page was requested, but its
JavaScript page did not expose documentation content to this browsing tool and
the offered Markdown fetch failed. Search nevertheless returned primary Apple
[cell attributes](https://developer.apple.com/documentation/appkit/cell-attributes)
describing starting index and span in the containing table and
[selected cells](https://developer.apple.com/documentation/appkit/nsaccessibility-c.protocol/accessibilityselectedcells?changes=_9&language=objc)
describing the selected-cell array as required for cell-based tables. These
support the conceptual scoped mapping and selection-array obligation, not exact
selector encodings or VoiceOver behavior. Such ABI and reader evidence remains
a target-platform obligation, not a source-based claim.
No previously completed CI/tests were rerun. No external Grid provider, actual
assistive reader, UI-thread marshalling, selector ABI or performance was executed
in this architecture review.
