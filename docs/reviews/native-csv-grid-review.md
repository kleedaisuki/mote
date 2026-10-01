# Native CSV Grid independent production review

Status: completed failure-directed source review, 2026-10-01. No production
files were modified. Historical findings below are resolved; final disposition
and current scope limitations are recorded in the final section. This is not native
runtime, external assistive-technology, performance, or release acceptance.

## Scope and baseline

Read `csv-grid-architecture.md`, `native-csv-grid.md`, the shipped Formats Grid
contract/projection, driver evidence, and the shared controller/command/lookup,
Win32 owner-data ListView, AppKit table, shell-routing drafts and focused tests.
The repository changed concurrently; locations are named by method rather than
unstable line numbers. No unrelated accepted test suite was rerun. Review uses
the architecture's explicit immutable installed identity, Engine-only source and
Undo ownership, exact clipboard, bounded cache, and selection-lifetime contracts.

## Necessary corrections

### P1: Grid-focused Cut routes to the hidden source selection

Initial draft: `WindowsEditorShell.HandleCommand` special-cases only Grid Copy;
Cut still flushes source selection and raises `CutRequested`. `MacEditorShell.Cut`
always raises source Cut, unlike its Grid-aware Copy route. After a field has been
revealed in source and a read-only Grid cell is selected, Ctrl/Command-X or Edit >
Cut can publish and delete the previously selected source field. The shared
`CopyOrCut` path authorizes the stable source selection, not the focused Grid,
so its otherwise correct async version checks do not prevent this wrong target.

Impact: unintended source mutation from a read-only table. Confidence: high,
code-traced; no physical keyboard test was claimed. Refuse Cut when Grid owns
focus, or explicitly define a separate structured action. Check Select All and
Paste routing at the same boundary. Undo can remain an explicit Engine command.

### P2: Same-version installation silently loses table selection

Initial draft: `WindowsCsvGrid.Install` assigns both anchors to the active cell;
`MacCsvGrid.Install` resets the anchor and selects local row zero. Theme/status/
full-analysis refresh therefore collapses rectangles on Windows and changes the
active row on macOS despite unchanged source. `GridWindowRequested` immediately
reinstalls the old Grid with pending status, so this reset can happen before the
requested new window even arrives.

Impact: user selection/next Copy differs after non-editing refresh; violation of
architecture section 5's theme-only selection/scroll retention. Confidence: high,
code-traced. Preserve logical selection across same-document, still-delivered
coordinate refreshes; explicitly invalidate or retain domain selection when a
window changes rather than silently rebinding a slot. Source revisions and
lifetime changes must still retire old coordinates.

### P2: Windows context menu rebinds a queued action to the newest map

Initial draft: `WindowsCsvGrid.ContextMenu` runs `TrackPopupMenu`, then calls
`Emit(kind)`, which reads the current identity and coordinates. The popup's nested
message loop can install a new same-version presentation or different window
before the user chooses a command. The old menu action then carries the *new*
identity and passes controller admission against a different map.

Impact: Copy/Replace/Reveal may target a cell other than the one for which the
menu was opened. Confidence: high for the executable reentrancy path; native
reproduction pending. Capture the complete immutable intent (identity and
logical selection) before the modal menu loop, then dispatch that intent after
return. The controller should reject it if installation authority changed.

## Examined safety paths without a new blocking finding

- `CurrentGrid` rejects disposed/composing state, hidden Grid, mismatched installed
  sequence, generation and source version. Async Copy repeats identity, clipboard
  serial and document-reference checks before native publication. New selection,
  request and source reset advance the serial. Replacement checks again after the
  modal value editor, and uses one Engine `TextChange` rather than native table
  editing.
- Copy helper reads exact field/row syntax, not sanitized display. Missing/pending/
  malformed decoded cells are refused; explicit padding is separate. Empty values
  are always quoted in CSV/quoted TSV, eliminating EOF empty-record ambiguity.
  Source rows retain real delimiters. Bounds apply before output concatenation;
  quoted source decoding has a finite roughly twice-output source admission.
- Prepared payload rejects NUL before adapter mutation. The new global source
  Copy/Cut NUL check also precedes clipboard mutation and cut's Engine apply.
  Oversized refusal does not claim unread NUL validation. OS publication failure
  is distinct and does not falsely promise prior clipboard restoration.
- Ready native callbacks do not parse/decode Engine text or retain full documents.
  Windows labels honor capacity, append NUL and avoid splitting a surrogate pair.
  Its interop fields match the relevant native prefix; drawing does not require a
  whole-file owner-data count. AppKit callbacks have pointer-sized row return/
  argument signatures, registered Cdecl entry points and exception containment.
  Table/delegate ownership is detached before release on normal disposal.
- Driver uses one CSV session turn, preserves independent source coverage without
  a false hull, and checks source/Grid snapshot identities before its baseline
  update. Existing non-Grid Flow calls remain additive.

These are source assessments, not exhaustive proofs or new target-host results.

## Honest remaining architecture scope

Initial adapters provide bounded local table windows and keyboard/page/wheel
requests, not a complete logical whole-file scrollbar/jump control. Cross-window
rectangular selection/copy is not provided: commands resolve only the delivered
window. Windows supplies a row gutter; initial macOS table has no equivalent
whole-row gutter, context format menu, or explicit rectangle paint (native row
highlight is not a cell rectangle). Arrow/Tab and active-column parity require
acceptance against the architecture rather than generic table availability.
Windows has no dedicated multiline selected-cell detail panel in the draft.
Native replacement initially reused single-line search prompts; the owner is
being asked to provide a true multiline editor or truthful explicit refusal.

Do not call these adapters complete architecture/product acceptance merely because
managed tests or an AOT executable launches. Focused target probes can establish
actual table creation, bounded readbacks, identity, clipboard refusal, source
navigation/edit/Undo and rendering; they do not establish Narrator/VoiceOver or
physical IME behavior. No native edit/present percentile is established here.

## External platform contract cross-check

- [Microsoft NMLVCUSTOMDRAW](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/ns-commctrl-nmlvcustomdraw): relevant prefix is native custom draw,
  foreground/background colors and subitem index; do not assume a managed struct
  defines the full native allocation.
- [Apple selectionHighlightStyle](https://developer.apple.com/documentation/appkit/nstableview/selectionhighlightstyle-swift.property): native table highlighting
  does not by itself implement the architecture's independent active-cell and
  rectangular domain selection.

A small failure-directed hidden Windows EDIT probe is retained under
`.temp/native-csv-grid-review/PromptProbe.cs`: a single-line EDIT initialized with
`a\r\nb` returns both original CR/LF via GetWindowText. Therefore no initial-text
newline-loss defect is claimed solely from the single-line style. Its inability
to act as the intended multiline value-editing UI remains an honest scope issue.

## Failure-directed correction review (2026-09-30)

The three original findings are **source-resolved in the corrected draft**:

1. Both shells now refuse Grid-focused Cut and route Select All to bounded Grid
   selection; table key handlers refuse Cut/Paste. Windows focused native HWND
   evidence is reported as 5/5 by its owner, including the shell routing test.
2. Windows retains logical active/anchor coordinates on same-document installs
   and tracks pending keyboard coordinates through a window request. Mac captures
   old logical active/anchor ordinals before Clear and restores them when still
   delivered. The initial same-window selection-reset defect is corrected.
   Full cross-window selection remains limited by command delivery admission;
   scroll retention and macOS behavior need target evidence.
3. Windows captures both rectangle and active-cell intents before TrackPopupMenu;
   Mac's added menu captures both at menuWillOpen. Later actions carry frozen
   installation identity. Reveal/Replace/Copy value/source use the active cell;
   rectangular formats use the frozen rectangle. The previous rebinding hole and
   incidental anchor-versus-active ambiguity are corrected by source inspection.

Reviewed new Windows tests use actual hidden HWND notifications, callback buffer
sentinels, real hit-testing, key messages and shell command routing, not an
assertion that compilation implies native acceptance. The owner-reported 5/5
execution was not redundantly rerun. Mac source corrections add rectangle cell
colors and an explicit format menu; these supersede the corresponding initial
scope gaps above, but do not substitute for AppKit target execution.

Both platforms now provide dedicated multiline replacement inputs. Windows' real
production input readback test retains a 5,000-character-plus mixed-CR/LF/emoji
value. The final multiline editor admits input without a silent 65,536-unit cutoff;
acceptance visibly rejects a complete value over the Formats 8 Mi output limit.
Helper encoded-output admission remains authoritative. Mac uses a temporary
plain NSTextView, verifies initial exact text roundtrip, disables automatic text
substitutions, explicitly settles marked input before acceptance, and releases
normal-path owned alert/scroll/editor references. No actual Mac input-method
acceptance is claimed.

The integration owner identified a remaining policy boundary issue: CSV grammar
encoding/decoding was initially in Native. Moving it to an additive Formats
CsvGridCommands API is pending. Final review admission awaits that final helper
and the controller integration; the review is not a blanket approval of files
that are still changing.

## Final policy-boundary review

The frozen boundary correction is source-approved: `Mote.Formats.CsvGridCommands`
now owns field decoding, CSV/quoted-TSV encoding, exact row copying and replacement
construction. Its additive public logical selection/copy-kind/result types have
no Native dependencies. `NativeCsvGridCommands` only validates the native version,
maps intent kinds/selection, and adapts results. Controller generation/installation
identity admission remains Native's responsibility; the Formats API explicitly
documents that equal version/length cannot authenticate a caller-fabricated
projection or distinguish documents. This is an honest caller contract, not a
false security guarantee.

The move preserves NUL refusal, empty-record serialization, exact source/quote
preservation, hard output budget and provisional/missing refusal. Encoding now
checks cancellation at bounded iteration intervals. Both Formats and Native row
lookups use bounded binary search, accepting the ordered sparse-ordinal contract
rather than assuming `rows[ordinal - first]`. No new blocker was identified in
these corrections. Owner evidence: direct Formats/Native/controller 59/59 and
Native sparse lookup 2/2; these accepted runs were not redundantly repeated.
Platform logical navigation additions are still being finalized and are outside
this frozen helper-boundary approval.

## Navigation correction review: newly introduced recovery case

### P2: Follow source is rejected using an ordinal that it explicitly ignores

In the new `NativeEditorController.GridWindowRequested` draft, the exact-row-count
check precedes FollowSource handling and rejects `request.Row >= count`. A user
can detach to an unindexed distant ordinal while final extent is unknown. Full
analysis can subsequently certify a smaller exact row count and leave a ready
empty window whose requested start is still that distant ordinal. The new Follow
source menu copies this old requested row into its command; despite valid current
identity, the controller rejects it and cannot recover source-follow mode.

This is a concrete state-machine recovery defect, not a hypothetical huge-file
performance objection. Apply exact-ordinal admission only when FollowSource is
false; FollowSource should obtain the current source anchor independently of the
unused row. Add a focused controller test with an ignored out-of-extent row and
FollowSource=true. Actual ordinal navigation at exact zero rows should refuse.
Windows numeric navigation also needs a truthful known-extent refusal rather than
returning success and letting controller silently ignore the request. Reported to
the integration owner; pending correction.

## Final bounded-navigation source assessment (2026-10-01)

The FollowSource recovery finding is **source-resolved**: controller ignores the
unused Row for source-follow requests and applies exact extent rejection only to
ordinal requests, including exact zero rows. The independent focused controller
regression now passed **3/3**, zero failures/skips, 661 ms. Only the new
`FollowSource_` cases were run; accepted earlier tests were not repeated.
The cases use actual CSV policy facts with a valid blank far-row delivery:
FollowSource with Row=1000 and Row=-1 recovers actual row zero under the same
document stamp and a new installation sequence, without source mutation.
An actual empty CSV's ordinal row-zero request is refused without inventing a
row. See [controller evidence](native-csv-grid-controller.md) and the TRX under
`.temp/csv-grid-controller/csv-grid-controller-follow-source.trx`. Managed test
reflection reproduces only transient delivery state, not parser/source facts;
these tests are not native-host or physical-input acceptance.

Final bounded Go-to-coordinate and Follow-source paths preserve the menu/prompt
opening identity. Mac numeric parsing validates known exact extents and checked
row/column budgets; Windows posts bounded logical requests without a full-file
native count. Windows post-default scrollbar edge handling reads native scroll
state and only enqueues bounded interests. Native paint/data callbacks remain
ready-cache reads, not parsing or document loading. Windows lazy EnsureGrid creates
no table for ordinary Plain/Flow startup. No additional safety blocker was found
in this final navigation source scope.

The Windows numeric refusal gap is now corrected: `RequestCoordinate` checks
the frozen exact row/width extent before emitting a request. Its modal caller
reports invalid coordinates explicitly. The owner reports the new focused
boundary regression passed 1/1; this accepted result was not repeated.
Whole-file logical scrollbar, Mac whole-row gutter, Windows
selected-cell detail and complete cross-window rectangle command delivery remain
separate architecture completion/acceptance items, not established by these
bounded navigation callbacks. Earlier absence of Go-to-coordinate, explicit
Follow source and Mac rectangle/menu surfaces is superseded by these additions.

**Integration disposition:** this reviewed Grid command/boundary/navigation scope
has no unresolved data-safety finding after the above corrections. This is not
permission to ship. The separate Windows source-NUL four-file safety patch has
since been independently approved with 22 host and 6 clipboard cases, per the
integration owner. Its former code-level integration blocker is resolved; that
approval is not a claim of full native embedded-NUL editability. No macOS target
runtime, external accessibility,
physical IME, or performance claim is made. Normal native lifetime/ABI paths were
source-inspected; exceptional allocator failures and OS host behavior are not
exhaustively verified.

All Grid Native/Formats/test writer areas are frozen for root handoff. The root's
standalone extraction/build of the separate source-NUL patch remains its own
integration gate; this review does not substitute for it.


## Failure-directed full-integration regression (2026-10-01)

Review reopened only for the actual full-suite failure:
`WindowsFlowRtfTests.Windows_hidden_Hwnd_reentrant_layout_keeps_newer_presentation`
expected preview installation sequence 2 but observed 1 (one failure among 805
Mote.Tests). The test assertion is correct and must be preserved.

### P1: Preview visibility callback lets an obsolete outer analysis overwrite a newer map

The Grid integration moved preview `ShowWindow` later in `WindowsEditorShell.SetAnalysis`.
The last identity guard occurred before `_grid.Install`/visibility. A synchronous
`WM_SHOWWINDOW` callback in `ShowWindow(_preview, ...)` can call SetAnalysis with
newer sequence 2. On return, the outer sequence-1 call unconditionally calls
InstallPreview(view), replacing the newer preview text/origin identity while
`_analysis` still references the newer view. This is the concrete failing path,
not an assumed thread race. Source Engine/P0 NUL guards are unrelated and must
remain untouched.

Practical correction: check ReferenceEquals(_analysis, view) after each native
visibility call and before later preview/status publication; obsolete outer work
must stop after synchronous nested publication. Lazy EnsureGrid and ResizeControls
already have guards, but those do not protect the later ShowWindow boundary.
Windows owner retains exclusive production ownership and is reproducing/fixing
this failure. Feature integration remains held pending exact and affected-suite
passes. No accepted unrelated suite was rerun by this reviewer.

### Reentrancy correction disposition

The frozen fix is **approved for this failure-directed delta**. Source inspection
confirms identity guards now immediately follow both native ShowWindow calls and
preview installation before status publication. A nested newer SetAnalysis wins;
obsolete outer work returns before importing old Flow or clearing the new Grid.
The correction does not alter the reviewed source-NUL input/persistence guards.

Evidence from the exclusive Windows owner: the exact original Flow test failed
before the fix and passed **1/1 unchanged** afterward. The new Grid counterpart
uses a real hidden HWND WM_SHOWWINDOW callback to install sequence 2, then checks
actual owner-data row count/labels and retained installation identity, not only
an internal analysis variable. Affected Flow plus Windows Grid tests passed
**18/18**, 251 ms, Release `--no-restore -warnaserror`. Accepted executions were
not redundantly rerun. These results resolve the observed integration regression;
root may resume its held feature integration/full acceptance gate. No new
substantive issue was identified within this correction's inspected scope.
