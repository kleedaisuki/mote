# Native CSV logical navigation implementation

Date: 2026-10-01. Status: integrated implementation, scoped static review and
local affected tests accepted; not target-platform or release acceptance. Architecture:
[logical scrollbar contract](csv-grid-logical-scrollbar.md). No public Formats
API, user configuration schema, source encoding, cache format or migration is
introduced. Engine remains the sole text/I/O/Undo owner.

## Delivered data model and ownership

`NativeGridNavigation.cs` distinguishes an unavailable/prefix/exact logical axis
from retained native rows. Count is certified extent (or the explicitly named
prefix/lower bound), Page is measured fully visible geometry, and First is a
clamped viewport placement. Integer steps saturate with wide comparisons;
AppKit normalized values reject NaN/infinity and preserve exact endpoints.
Pages/capacities remain bounded to 256 rows, 64 columns and 8,192 cells.

Ready command identity remains `NativePresentationId`. Navigation uses a
different document/epoch identity and a nonreused per-gesture sequence. Begin
retires previous phase/delivery authority before reentrant native installation.
Terminal phases consume the token before dispatch. Same-version ready or palette
installations do not implicitly acquire or replace a gesture. Source edits,
replacement, format changes, composition rejection and changed geometry retire
old tokens. A final delivery is admitted by current controller analysis serial
and cancellation/lifetime rather than requiring an active pointer token.

`NativeGridPlanner.Slots` maps requested ordinal `start+i` to actual immutable
policy rows. A missing descriptor remains null; it is not compacted away and has
no source origin. Pending navigation has Ready=null and cannot authorize
Copy/Reveal/Replace. Ragged-row Missing fields retain their distinct policy
semantics. Unknown numeric/End targets request certification without extending
the prefix range; numeric cells outside a final extent fail rather than aliasing
another cell. Source-follow anchors that cannot yet resolve a row expose no
invented row ordinal.

## Scheduling and cancellation

The CSV controller and idle/demand lane share `NativeAnalysisDispatcher` before
the driver gate. Its retained state is one running turn, latest mandatory
content, one reserved Full and latest viewport. Mandatory content registers
synchronously with its edit-derived debounce deadline; same-version navigation
cannot reset or cancel it. Demand Full reserves synchronously, promotes an idle
timer, and precedes remaining viewport traffic after the running turn and at most
one mandatory current-version content pass. Full rechecks existing advisory
memory admission at dispatch and retains Deferred/Failed until explicit retry.

The controller coalesces accepted Grid delivery interests through one 8 ms UI
mailbox timer. This is a scheduling interval, not a <=16 ms measured latency
guarantee. Native callbacks never parse or wait for the analysis gate. Changing
interest cancels obsolete projection/Copy authority immediately. Full facts
publication validates document/driver/policy/version but does not require its
obsolete captured source viewport; it re-queries live interests and never
installs captured Grid selection or source decoration. Non-CSV paths retain
their existing behavior.

The last accepted same-version bounded placement is retained for cancellation
and failure recovery, including its source-follow mode. Unused Begin/Cancel
retains existing ready command identity without scheduling analysis. A tracked
but uninstalled target is not committed by Cancel: the last accepted payload is
restored instead. Same-version extent facts merge monotonically, so restoring an
older bounded prefix payload cannot downgrade already certified exact totals.
Failed installs expose a pending, navigation-only frame at
that placement, with no ready command identity; they do not claim successful
navigation to an uninstalled target. Grid indexing notices are separate from
persistent settings/accessibility notices.

## Native controls

Windows owns independent row/column `SCROLLBAR` HWNDs beside the bounded
owner-data ListView. Range/page/position use 32-bit `SCROLLINFO`; thumb positions
are read through `GetScrollInfo(SB_CTL,SIF_TRACKPOS)`, never the scroll-message
16-bit high word. The installed range is read back; zero `SetScrollInfo` position
is valid. Local competing scrollers are hidden. Existing adapter constructors
and methods remain available to frozen clipboard acceptance tests.

macOS owns two standalone `NSScroller` instances beside a bounded table/clip,
uses typed floating-point message signatures and AppKit style/width, and freezes
gesture authority across AppKit's synchronous tracking loop. NSScrollView's
competing cache scrollers are hidden. Existing optional constructor/Install
arguments preserve the independently reviewed clipboard probe source hash.

## Evidence and scope

Observed portable/native results and reproducible commands live in:

- [controller navigation validation](validation/native-grid-navigation-controller.md)
- [dispatcher validation](native-analysis-dispatcher.md)
- [Mac portable interop validation](validation/mac-grid-logical-scroller.md)
- [Windows real-HWND validation](validation/windows-grid-logical-scrollbars.md)
- [independent implementation review](reviews/csv-grid-logical-scrollbar-implementation-review.md)

The final affected integrated Release `/warnaserror` run passed **184/184**,
zero failed/skipped, after the reviewed deltas. It includes CSV controller,
dispatcher/idle, gesture/planner, Windows logical/legacy HWND, Mac portable
interop, general controller and theme-override controller suites. TRX:
`.cache/grid-logical-tests/grid-logical-final-accepted.trx`; duration 13 seconds.
An earlier final attempt had 68/69 pass because its Cancel assertion still
required committing an uninstalled Track target. Independent validation agreed
with the last-delivered Cancel contract and replaced that assertion; source
authority and monotonic extent restoration gained focused tests. The failing
TRX is retained, not erased or relabeled. Tests and experiments stay under
repository `.cache`/`.temp`.

Windows hidden-HWND stable 16-row/8-column range update/readback, 10 warmups
and 100 samples: p50 **0.2501 ms**, p95 **1.0284 ms**, Windows 10.0.26200 x64,
.NET 10.0.11 Release. This is a synchronous native API observation, not a GUI
latency percentile or proof of the <=1 ms callback target. No request-to-ready,
physical-paint, cancellation-tail or startup claim is made.

Remaining target gates: published macOS AOT AppKit behavior on both RIDs,
standalone overlay visibility/hit area, external UIA/AX values/focus, real
mouse/trackpad/IME and source typing under Full load, callback/install/warm
request-to-install/cancellation percentiles on adversarial large CSV shapes.
Portable arithmetic and hidden HWND dispatch do not establish physical-input,
screen-reader, composited paint or launch performance. Cross-window rectangle
Copy and a global variable-width pixel extent are intentionally not claimed.
