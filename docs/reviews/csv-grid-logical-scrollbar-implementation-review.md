# Logical CSV Grid implementation review

Date: 2026-10-01. Static review against the uncommitted integration on
`codex/initial-product`. Contract: `docs/csv-grid-logical-scrollbar.md` and
`docs/reviews/csv-grid-logical-scrollbar-review.md`.

## Scope and evidence limits

Inspected shared navigation/planner/controller, CSV analysis dispatcher and idle
Full scheduler, Windows Grid/control interop/shell routing, and Mac Grid/scroller
interop/Grid shell integration. Production code was not changed by this reviewer.
Program, workflows, acceptance probes, Flow, Markdown, and external accessibility
are outside this review. Existing validation artifacts are reused rather than
rerunning completed tests: controller delta reports 23 passing cases; Mac pure
part mapping reports 8 passing cases. Those are author/validator results, not
independent target-runtime executions by this reviewer.

No actual desktop menus, AppKit runtime, clipboard, physical thumb/wheel input,
screen reader, compositor presentation, or latency percentile acceptance has been
performed here. Cross-compilation and pure mapping tests cannot establish these.

## Findings communicated during review

### P1 — Windows context menu abandons navigation by invalidating its own command

Initial inspected locations: `WindowsCsvGrid.ContextMenu` and
`NativeEditorController.GridGestureRequested`, Cancel branch.

When logical navigation is installed, every context menu calls `Begin()`. For a
normal Copy/Reveal/Replace command (or Follow source), the adapter subsequently
dispatches navigation Cancel **before** the frozen source command. Cancel calls
`CancelAnalysis()` and `ScheduleAnalysis()`, both clearing `_presentedPreview`.
Consequently the immediately following `CurrentGrid(frozenIdentity)` fails and
the requested action is silently discarded. No parser race is needed: this is
the synchronous event sequence for every ordinary context-menu command.

Impact: regressions in established native Grid source commands. Confidence:
high, executable call-path inspection; a real menu reproduction has not run.
Required correction: do not admit an unused navigation gesture for an ordinary
command, or consume that token without retiring the separately captured ready
command authority. Preserve stale frozen-command rejection across actual nested
replacement. Add a test matching Cancel/command ordering, not merely eventual
ready restoration after Cancel. Windows owner is correcting this; delta review
is pending.

Delta inspection: `ContextMenu` now captures its opening frame/page and admits a
gesture only for Go to cell, after `TrackPopupMenu` returns. Ordinary frozen
commands no longer have a preceding Begin/Cancel pair. This resolves the
demonstrated synchronous invalidation path statically; real native-menu behavior
and focused regression evidence remain separate acceptance gates.

### P2 — A dismissed Mac menu leaves a permanently live frozen denominator

Initial inspected locations: `MacCsvGrid.MenuWillOpen`, empty `MenuDidClose`,
`MenuCommand`, `MenuFollowSource`, and `MakeGridFrame` active-gesture branch.

Opening any menu admits `_menuGesture`. Dismissing the menu or choosing an
ordinary source command never consumes it. If same-version indexing subsequently
publishes larger prefix/exact extents, `MakeGridFrame` continues to use the menu's
frozen range/page although no gesture is actually active. The scroller therefore
remains on the old prefix until another gesture/edit retires the token.

Impact: file-wide navigation may stay visibly prefix-only after certification.
Confidence: high for retained token and frozen-range path; no AppKit execution.
Required correction: terminate menu gesture lifetime after action/dismissal in a
way compatible with AppKit's menu-close/action ordering. Do not erase frozen
ready command authority before ordinary menu dispatch. Delta review is pending.

Resolved by inspected delta: `MenuWillOpen` now captures a frame/page admission
request only. `MenuNavigation`/`MenuGoToCell` admit a token only when the user
actually selects a navigation action. Dismissal and ordinary source commands no
longer create a controller token; unchanged menu-close ordering cannot preserve
an unused denominator. Opening-frame admission still rejects an intervening
newer frame, and modal completion retains its admitted token.

### P2 — Asynchronous Full failure can have no visible retry outcome

Initial inspected location: `NativeEditorController.CreateIdleFullAnalysis`
`onError` callback.

The failure callback publishes a notice only when `_visibleSessionAnalysis` is
nonnull. A Grid/source viewport `ScheduleAnalysis()` clears that field before
the replacement delivery arrives. A demanded Full turn can fail or be refused
at dispatch during that interval; its stored scheduler outcome becomes Failed
or Deferred but the native frame remains "requested coordinates loading" with
no displayed failure/retry reason. Demand originally returned Scheduled, so its
immediate refusal branch cannot report this later outcome.

Impact: a terminal resource/parser outcome can look like indefinite indexing.
Confidence: high for conditional call path; controlled failure reproduction is
needed. Required correction: publish a document/version-validated status outcome
independently of whether a ready analysis is currently retained; do not grant
ready command authority merely to display a failure. Delta review is pending.

Resolved by inspected delta: the CSV error observer calls `SetGridIndexNotice`
without depending on `_visibleSessionAnalysis`. The dedicated notice is included
in `MakeGridFrame` and published through `SetGridNavigation`; deferred/failed
text explicitly offers Retry. Pending frames retain `Ready=null`, so reporting a
terminal outcome does not restore source authority. Notice lifetime is scoped to
the document navigation retirement and explicit retry.

## Integration concerns already under correction

Parent implementer independently identified Mac Pending installation clearing
all table slots, rather than supplying bounded pending ordinal slots, and missing
newer-install guards around reload/scroll/selection. These were communicated as
active corrections, not independently demonstrated defects by this reviewer.
Both platform installers require delta inspection before acceptance.

Delta inspection: Mac `SetNavigation(Pending)` now installs bounded null slots,
pending strings and requested columns without source identity. Mac installation
uses a monotonic local serial and checks ownership after reload/scroll/selection,
column mutations and axis mutations; an outer finally does not reset a newer
installation guard. Windows also adds local installation serial checks, and
resets the local first row on logical rebases. These address the identified
static gaps; target-runtime reentrant acceptance is not implied.

### P2 — Windows page keys use cache capacity rather than visible logical page

Initial inspected location: `WindowsCsvGrid.Input`, PageUp/PageDown cases.

With a 64-row delivered cache and 10 fully visible rows, PageDown uses
`grid.Rows.Count - 1` (63), not measured `navigation.Rows.Page` (10). It calls
selection `Move`, followed by local `EnsureVisible`; local table movement can
therefore disagree with the logical first-row axis while the local scrollbar is
hidden. Mac's corresponding route uses measured page requests. Required remedy:
use the measured logical-page admission path when navigation is active, retaining
legacy standalone behavior separately. Check both page keys with a cache larger
than the pane, including boundary and pending states. Confidence: high for the
cache-sized arithmetic; physical keyboard behavior has not been executed.

Resolved by inspected delta: the active-navigation key branch intercepts
PageUp/PageDown before legacy selection handling and calls `Navigate(Rows,
±navigation.Rows.Page)`. It therefore uses fresh gesture admission and measured
logical-page requests even while ready data is pending. The cache-sized legacy
path remains only for standalone installs without logical navigation.

## Contracts inspected without an additional finding so far

- Engine snapshot remains the source/Undo owner; Grid navigation stores ordinals
  and never commits text. Copy/Reveal/Replace still require current ready
  presentation authority in the controller.
- Per-gesture immutable Id distinguishes same-version A and B. Terminal
  controller authority is consumed before native installation; serial checks
  protect A's terminal tail against reentrant B.
- Pure axis arithmetic uses saturating wide-step comparisons, exact normalized
  endpoints, and nonfinite refusal. Bounded pages and sparse null row slots avoid
  file-sized native arrays and compacted unproved rows.
- Dispatcher retains one content, one viewport, and one Full slot plus one
  running turn. Mandatory content precedes reserved Full, which precedes later
  viewport service. Navigation cancellation is separate from content/Full.
- Full dispatch repeats memory admission; completion re-queries latest CSV
  interest instead of installing its captured source viewport. Numeric Cell
  outside certified extents is rejected; End is symbolic while totals are
  unknown. Prefix and observed-width lower bounds are not inferred from bytes.
- Windows uses full `SCROLLINFO.nTrackPos`, not the truncated message high word.
  Mac float setters/returns use distinct ABI signatures and NSScroller native
  part ordinals. Static correctness is not target-OS acceptance.

## Final shared-controller delta inspection

The final Cancel contract distinguishes an unused gesture from an uninstalled
Track target. Unused Cancel consumes its token and republishes the existing
ready identity without a parser roundtrip. Pending tracked Cancel retires the
coalescing mailbox and restores `_gridDeliveredView`/`_gridDeliveredFrame`, which
are captured only after an accepted native installation, not from a requested
coordinate. Terminal authority is consumed before restoration can reenter Begin B;
no trailing token clear follows the native callback. Late A phases remain inert.

`MakeGridFrame` now monotonically merges same-version certified prefix and paired
exact extent facts. Restoring an older bounded ready payload cannot downgrade
an already learned Exact extent. Document retirement clears the retained payload
and facts, preventing cross-version restoration. User status distinguishes shown
and requested one-based bounded windows, last ready rows, unknown totals, and
unproved numeric or symbolic End interests; labels use wide arithmetic.

CSV asynchronous Full refusal/failure notices are appended to the navigation
frame rather than using the shell's unrelated settings/accessibility notice
channel. Reporting an outcome does not imply ready source authority. Finally,
`CreateIdleFullAnalysis` disposes and clears the old dispatcher even when the new
policy has no session driver, closing the previously omitted null-driver lifetime
transition. No additional concrete defect was found in these targeted deltas.

The final additive `_gridDeliveredAnchor` is stored alongside the accepted view,
cleared on navigation retirement, and restored by Cancel/failure. Its nullable
value preserves source-follow mode rather than silently detaching on recovery.
Failure framing uses the last accepted Grid payload for proved placement while
keeping `Ready=null`. No new concrete defect was found in that delta.

Final evidence inspected without re-executing tests:

- `.cache/grid-logical-tests/grid-logical-final-accepted.trx`: counters show
  **184 total/executed/passed, 0 failed, 0 not executed**, Completed. Parent reports
  Release `/warnaserror`, approximately 13 seconds, covering navigation,
  dispatcher/idle, CSV controller, hidden Windows logical/legacy HWND, portable
  Mac mapping, general controller and theme override contracts.
- `.cache/grid-navigation-validation/navigation-cancel.trx`: counters show
  **3 total/executed/passed, 0 failed, 0 not executed**, Completed. Independent
  validator's accompanying note distinguishes accepted-placement Cancel,
  unchanged ready identity after unused Cancel, and deterministic monotonic
  extent restoration. The last test injects a snapshot-matching weaker retained
  payload; it does not demonstrate a natural large-file Full/Cancel runtime race.

The reviewer read these existing artifacts; no completed native validation was
rerun. Final additive source-follow field was statically inspected after the
reported suite; it is not claimed as independently executed runtime coverage.

## Final disposition

The four actionable triggering paths found during this review are resolved by
the inspected production deltas. The parent-reported bounded pending host and
installer ownership gaps are also addressed statically. No additional substantive
defect was found in the inspected scope. This is conditional static acceptance,
not release acceptance: native Mac runs, physical
interaction and external accessibility remain separate gates. The absence of a
further finding does not imply unexamined behavior was verified.
