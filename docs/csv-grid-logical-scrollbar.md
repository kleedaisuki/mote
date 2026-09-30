# CSV Grid: logical-file scrollbars over bounded ready tables

Date: 2026-10-01. Status: **architecture proposal; no implementation or target-OS
acceptance**. Inspected production at `8671c06`. This is the next native Grid
slice, not a new CSV parser or spreadsheet subsystem. The existing
[native implementation](native-csv-grid.md),
[Grid architecture](csv-grid-architecture.md),
[format contracts and measured limits](csv-grid-format-implementation.md), and
[release gates](release-gaps.md) remain authoritative for delivered behavior.
The older format-only document's “native UI not installed” heading describes
its earlier checkpoint; the later native implementation document supersedes it.

## 1. Decision

**Keep native table storage bounded. Give each Grid container separate native
row/column navigation scrollers whose ranges are logical coordinates, not the
installed cache size. Use exact whole-file ranges only after Formats certifies
them. Before that, explicitly navigate the certified row prefix and observed
column lower bound; never estimate total rows from file bytes or physical lines.**

No public Formats API change is needed. `GridExtent`, `CsvGridAnchor`,
`CsvGridRequest`, `GridRenderProjection` and the single serialized `AnalyzeGrid`
lane already supply the required grammar and extent facts. New navigation state
and events are internal to Native. Engine remains the only source/Undo owner.

For example, a 100 Mi-unit CSV can have 50 million short records, two records
with one huge quoted field, or one record with millions of columns. File length
does not determine row count, and a CSV record is not a physical line. After a
successful Full scan, two extent scalars and sparse certified checkpoints are
enough to position a row thumb and query a bounded distant window. The native
control does not need millions of rows or a millions-of-pixels document view.

```text
CSV session: certified prefix or exact (rows, maximum columns)
                    + bounded ready projection
                                  |
                                  v
controller: document-scoped navigation epoch + latest viewport target
            /                               \
native logical scrollers                  bounded native table
integer row / column interests             <=256 local row slots
no parsing in callbacks                    <=64 local columns
            \                               /
             stamped/coalesced requests -> one serialized session
```

The ordinary workflow is row-aligned/column-aligned navigation, small wheel
movements inside ready data, page moves, drag-to-location and Go to cell. It
preserves current source-follow, source reveal, explicit Copy, Replace, Save and
Undo semantics. A new scrollbar does not authorize cross-window Copy or create
origins for pending cells.

## 2. Actual seams and the work that must not be hidden

| Current production responsibility | Concrete next change |
| --- | --- |
| `CsvGridContracts.cs`: paired optional exact counts plus `CertifiedPrefixRows`; exact extent is independent of clipped display | Reuse unchanged. Do not derive whole-file maximum width from a visible row. |
| `CsvGridProjection.cs`: sparse record/field checkpoints; 128 Ki-unit Grid replay admission with bounded record/field overshoot | Reuse unchanged. A certified count does not promise every requested cell is already decoded or every warm request meets a time budget. |
| `WindowsCsvGrid.Install`: ListView item count is `grid.Rows.Count`; built-in scrollbar is local | Add a bounded container and independent native `SCROLLBAR` controls; retain bounded owner-data ListView. |
| `MacCsvGrid`: NSTableView row count is the bounded ready array; NSScrollView owns local scrollers | Keep a bounded clip/table, hide its competing scrollers, and place owned `NSScroller` controls at the Grid-container edge. Do not repeatedly set NSScrollView's auto-owned scrollers to pretend global ranges. |
| Native controller Grid request carries `NativePresentationId` and detached row anchor | Keep command identity unchanged; add a different, narrow navigation authority that survives same-version ready installation. |
| `ScheduleAnalysis()` cancels idle Full on every call; CSV session work has an 80 ms debounce; >32 Mi-unit idle Full waits 15 seconds | Distinguish Grid navigation from source/content analysis. An explicit distant/End request needs a resource-admitted, demand Full path, not another restarted 15-second quiet period. |
| Native callbacks enumerate only delivered rows, so sparse gaps may be compacted | Map bounded requested ordinal slots to ready `GridRow` descriptors. Missing delivery is an explicit pending slot, not the next ready row moved into its place. |

The scheduler issue is causal, not an asserted measured GUI regression: repeated
navigation currently restarts the conditions needed to learn the range. No new
UI should advertise a usable file-wide thumb while its own event stream prevents
the required certification from finishing.

## 3. Extent model: exact, prefix, empty and temporarily unavailable

The shared internal axis model is `(Kind, Count, Page, First)`. `Count` counts
logical records/columns in the **named** navigable domain. It is not the number
of cached native rows. `Page` is the number of fully visible logical slots,
derived from the actual clipped geometry and admitted against the delivery cap.
`First` is the first requested visible ordinal, not the selected cell.

| State | Vertical axis | Horizontal axis | User/assistive description |
| --- | --- | --- | --- |
| Exact nonempty extent | `Count = ExactRowCount` | `Count = ExactMaxWidth` | “File rows”; “File columns”; exact totals available. |
| Unknown total, certified prefix | `Count = CertifiedPrefixRows` | Largest proved `GridRow.Width` observed in this document version; lower bound only | “Indexed prefix rows; file total unknown”; “Known columns; file maximum unknown.” |
| Exact empty extent | Zero rows | Zero columns | “Empty CSV”; no row/column selection and disabled scrollers. |
| No certified prefix / unresolved source anchor | Disabled row thumb; no invented ordinal | Disabled until any proved width exists | “Indexing; row coordinates unavailable.” Source remains independently editable. |
| Resource-deferred or failed Full | Keep certified prefix/lower-bound navigation | Same | Visible reason, explicit retry; never replace unknown totals with zero. |

The observed column maximum is one Native scalar, reset on every source version,
format change and document replacement. It is a lower bound on whole-file maximum
width, not a certified prefix-wide maximum: the ready rows may be only a small
subset. A row can have `Missing` cells inside the file's maximum width; this is
the existing ragged-row semantics, not an absent column in the document.

At the prefix end, “Next rows” can request the next unknown ordinal, and Go to
cell can retain an unproved numeric target. Such requests **do not stretch** the
prefix thumb range. An explicit End command stores an `End` target, rather than
guessing a row from source length. Both request resource-admitted Full immediately
after current content work settles. When exact counts arrive:

- Numeric Go to cell outside the final extent fails visibly without changing
  source, selecting a different cell, or claiming successful navigation.
- End resolves to the final legal viewport origin (or an empty-state result).
- An ordinary viewport offset that becomes too large after resize is clamped.
  This is viewport placement, not reinterpretation of a numeric cell command.
- Follow source uses the existing exact source anchor; a pending source anchor
  never acquires a row by interpolating a source-byte percentage.

There is no byte-position substitute called a row scrollbar. A later explicitly
named source-position navigator would be a different feature and is unnecessary
for this slice.

## 4. Shared arithmetic and geometry

For a nonempty navigable domain of `N` slots and measured visible page `P`:

```text
P = clamp(measured fully visible slots, 1, admitted capacity)
V = min(N, P)
L = max(0, N - V)                 // maximum legal first visible slot
t = clamp(requested first, 0, L)
thumb fraction = V / N
normalized position = (L == 0 ? 0 : t / L)
```

Use `long` intermediates for increments, one-based labels, `start + count`, page
clamping and products. The existing `int` coordinate domain is sufficient for
the existing `int`-length UTF-16 Engine; this design does not add unsupported
64-bit file-size claims. Never wrap a row/column ordinal at `int.MaxValue`.

`P` comes from actual viewport geometry, **not** the default 64-row/16-column
cache window. Vertical slots use native measured row pitch, excluding headers
and detail area. Horizontal slots count fully visible field columns, excluding
Windows' row gutter. A partially visible trailing slot may be requested as one
extra bounded delivery slot but is not counted as an entirely visible slot.
For a pane narrower than one column, page is one and the bounded native clip
still permits local inspection of that column.

Request some bounded ahead/behind data if it reduces rebases, but compute the
thumb from the visible origin and geometry. Cache origin and viewport origin
are different coordinates. Default cache capacities can remain 64 rows / 16
columns; a resized pane may enlarge them up to 256 rows, 64 columns and 8,192
cells. `rowCapacity * columnCapacity <= 8192` is checked once in the planner.
If a deliberately extreme pane requires more simultaneous visible slots, retain
the cap and expose delivery truncation; do not allocate a screen-sized unbounded
graph or say the entire pane is ready.

Column resizing, native row focus and local pixel inspection remain available.
The global horizontal control is explicitly in **logical column units**, not
a percentage of hypothetical unmaterialized variable-width pixels. There is no
file-wide retained column-width array. Existing window-local column resize
behavior need not acquire new cross-window persistence. Both scrollers are
row/column navigation controls; do not expose their ordinal percentage as a
whole-table pixel `ScrollPattern` until that stronger model is implemented and
externally validated.

### Windows

For a nonempty axis, set the independent scrollbar's `SCROLLINFO` to
`nMin=0, nMax=N-1, nPage=V, nPos=t`. The resulting maximum legal position is
`N-V`. Use `GetScrollInfo(SB_CTL, SIF_TRACKPOS)` during `SB_THUMBTRACK`, never the
16-bit high word of `WM_VSCROLL`/`WM_HSCROLL`; distant rows past 65,535 must remain
addressable. Microsoft documents this distinction and the clamping performed
by `SetScrollInfo` ([GetScrollInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getscrollinfo),
[SetScrollInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setscrollinfo)).

For empty/unavailable domains, publish a disabled control plus the named status,
not `nMax=-1`. Check native API failures before interpreting values. Hide the
ListView's competing local scrollbar, but keep its bounded programmatic local
scroll/focus mechanism. In particular, `SetScrollInfo` returns the resulting
position, not a Boolean: zero is a valid first position, not a failure test.
Read back range/page/position with `GetScrollInfo` when certifying installation.
Parent routing distinguishes global scrollbar HWNDs
from the table; old boundary handlers must not issue a second request for the
same gesture. Repeated auto-reveal (`EnsureVisible`) must not move the viewport
back to an offscreen retained selection after a user drags the global thumb.

### macOS

Use owned `NSScroller` instances, target/action and `hitPart` to route line/page
and knob actions. Set `knobProportion=V/N` and `doubleValue=t/L` (zero if `L=0`).
For a finite received value `u`, map to
`round(clamp(u,0,1) * L, MidpointRounding.AwayFromZero)` and clamp once more;
special-case exact endpoints to preserve first/last. Reject NaN/infinity instead
of converting them to an ordinal. Apple describes `NSScroller` as a viewport
position control and its proportional knob; its older combined float setter is
deprecated in favor of the separate property/value operations
([NSScroller](https://developer.apple.com/documentation/appkit/nsscroller),
[knobProportion](https://developer.apple.com/documentation/appkit/nsscroller/knobproportion),
[deprecated setter guidance](https://developer.apple.com/documentation/appkit/nsscroller/setfloatvalue%3Aknobproportion%3A)).

Use correctly typed `CGFloat`/double Objective-C message signatures on both Mac
RIDs. Obtain preferred scroller style/width from AppKit; verify standalone overlay
visibility and hit targets on a target host. Keep the table/clip bounded; do not
use `ExactRowCount * rowHeight` as an NSTableView frame. Trackpad fractional
deltas accumulate in a small per-axis remainder and reset on document/gesture
invalidation; line/page/thumb actions share the same logical planner.

## 5. Internal state and additive API seams

The following are proposed **internal** declarations, not new public Formats
types. Final names can follow the existing Native naming style. Constructor
factories validate values; consumers do not repeat domain checks in callbacks.

```csharp
/// <summary>Names the certified navigation domain; Prefix is not a file total.</summary>
internal enum NativeGridExtentKind { Unavailable, Prefix, Exact }

/// <summary>Only permits coordinate navigation, never a source-backed command.</summary>
internal readonly record struct NativeGridNavigationId(
    NativeDocumentStamp Document, long Epoch);

/// <summary>Identifies one controller-admitted gesture, not merely its document.</summary>
internal readonly record struct NativeGridGestureId(
    NativeGridNavigationId Navigation, long Sequence);

/// <summary>A bounded visible origin; capacities are separate from measured pages.</summary>
internal readonly record struct NativeGridViewport(
    int FirstRow, int FirstColumn, int VisibleRows, int VisibleColumns);

/// <summary>An axis range in logical slots, not a native cache or pixel extent.</summary>
internal readonly record struct NativeGridScrollAxis(
    NativeGridExtentKind Kind, int Count, int Page, int First);

/// <summary>Freezes one gesture's extent and geometry while delivery continues.</summary>
internal readonly record struct NativeGridScrollFrame(
    NativeGridNavigationId Navigation, NativePresentationId? Ready,
    NativeGridScrollAxis Rows, NativeGridScrollAxis Columns, long RequestSerial);

/// <summary>Distinguishes content work from coordinate delivery and explicit indexing.</summary>
internal enum NativeAnalysisReason { Content, SourceViewport, GridViewport }
```

Controller owns one navigation epoch, desired viewport, delivered viewport,
optional symbolic End/numeric cell target, extent facts and request serial.
It also owns a monotonically increasing gesture sequence and at most one active
gesture token, shared across both axes. Sequences are not reused after a
terminal phase or when the document changes.
Adapters own native geometry, gesture lifetime and ready local slot lookup.
Formats owns all parser/index proofs. A shared pure planner contains clamping,
normalization, slot/page/capacity arithmetic and row/column step semantics; no
platform-specific parser logic is admitted.

Add a narrow shell Begin event carrying the current navigation frame and,
when ready, its exact `NativePresentationId`. The controller admits that frame
and returns a fresh `NativeGridGestureId`. Subsequent events carry this token,
axis, logical target and phase (`Track`, `Commit`, `Cancel`); a token is not
minted or rebound while processing a later phase. When there is no ready table
because a target is pending, Begin can still use the current navigation frame,
but it obtains navigation-only authority, never an invented ready identity.
One-off keyboard/wheel actions use the same Begin/Commit admission rather than
bypassing it; a grouped trackpad event can update both axes under one token.
Existing
`NativeGridWindowRequest` stays available; its old callers keep exact-presentation
admission and existing contracts. An internal shell installation overload or
separate navigation-state installation adds scroller state without making
`GridRenderProjection` a native UI state holder.

Navigation authority deliberately differs from command authority:

- Begin B atomically retires active gesture A **before** callbacks, status or
  native installation can reenter. Every Track/Commit/Cancel requires both the
  current navigation epoch and equality with the controller's active gesture
  token. A late A event is rejected even if B has the same document, geometry,
  extent, axis and `NativeGridNavigationId`.
- Commit/Cancel consumes its token before dispatching work. Duplicate terminal
  events and a Track arriving after a terminal event are rejected. The adapter
  retains the token returned at Begin; queued/posted callbacks capture that
  token rather than consulting whatever token is current at execution time.
  If a new control/axis begins a gesture, the old control must not relabel its
  outstanding callbacks as that new gesture.
  A terminal A handler must not clear gesture state again in its tail/finally
  after a reentrant callback begins B; B's newer token and serial must survive.
- A palette-only refresh or successful same-version Grid delivery may change
  `NativePresentationId` without invalidating a coordinate gesture.
- Edits, document replacement, format changes, disposal, composition admission
  failure, or geometry changes retire the navigation epoch immediately. Late
  gesture actions cannot acquire a new epoch implicitly.
- A new extent discovered while dragging is held for the end of that gesture.
  Freeze its range/page denominator to avoid the target moving underneath the
  pointer. On commit, transfer the absolute ordinal into the new range and clamp
  as needed; never preserve a percentage and jump to a different record.
- Every Reveal/Copy/Replace continues to require the **current installed**
  `NativePresentationId`, version and exact delivered source origin. Navigation
  IDs cannot be passed to these commands.
- Retiring a navigation request cancels its outstanding Copy and increments the
  existing clipboard serial. Source/Undo remains untouched.

Beginning B also supersedes A's delivery serial and pending symbolic target, so
a late A result cannot install even if its worker completed successfully. A
final Commit creates the final delivery serial and retires only the gesture:
that final asynchronous result is admitted by navigation epoch + request serial,
**not by an active gesture token**, because valid delivery commonly finishes
after pointer release. Gesture phase admission and worker-result admission are
different contracts. Cancellation may leave a parser candidate committed for
the same snapshot; it cannot make an obsolete viewport authoritative.

An extent change is not an edit: same-version prefix-to-exact installation must
not reset source selection, active ready-cell selection or source-follow mode.
The current detached ordinal invalidation after an actual source mutation is
preserved; shifting old quote-sensitive row coordinates is out of scope.

## 6. Pending presentation and sparse delivery

A moved thumb must not label the old visible data as if it belonged to the new
target. Install a cheap **bounded pending slot view** for the accepted requested
window while ready data is unavailable. The slot host contains no fabricated
`GridRow` or `SourceRange`: ready slots reference real policy descriptors;
pending slots display the requested logical coordinate and “loading”.

In an exact extent, row existence is known but its field data may not be ready.
Inside a certified prefix, row existence is also known. Outside that prefix,
Go to/End remains an explicitly pending navigation command with no claimed
actionable row selection. Unknown-domain requests do not install a fake table
extending beyond the prefix. A visible old table can remain until certification,
but is labeled with its actual shown range and has no reused command authority.

The local table count is the bounded requested slot count, clipped to the named
known range, not `ExactRowCount`. Mapping local slot `i` to `first+i` preserves
sparse gaps. Use a prebuilt local descriptor/index map, at most 256 entries;
callbacks only consult that map and the immutable ready arena. A pending row or
cell has no actionable origin; clicking it may retry bounded delivery, never
Copy/Reveal/Replace. A `GridCell.Missing` in a **proved row** remains different
from a pending descriptor.

The delivered viewport origin is advanced only when the latest accepted frame
is installed; desired and delivered coordinates are separately named in status.
Pending slots and the scroller expose a pending/busy value description rather
than reporting ready data. A preparation/native-install failure leaves source
unchanged, retires command identities, shows retry status and returns the
navigation control to a known delivered range if available. There is no silent
“success with a clipped tail”. Native installation guards must recheck the
current epoch/serial after each reentrant visibility/reload/layout call, as the
existing Grid/Flow correction already requires.

## 7. Scheduling, fairness and lifetime

Two cancellation domains are necessary; two parser sessions are not:

1. **Delivery request:** latest viewport target only; changing target cancels
   obsolete bounded projection jobs. Source/policy/lifetime changes also cancel.
2. **Certification work:** one Full attempt per current document version, shared
   by idle and explicit demand. Grid-only movement cannot restart a demand Full
   pass for that same version. A source edit, format/document replacement, close
   or explicit Cancel indexing retires it promptly.

Extend the internal idle scheduler with a demand entry point using the same
memory admission, cancellation ownership, single driver and attempted-version
rules. Demand skips the 1/3/15-second quiet delay **after current source work**;
it does not bypass resource refusal or make a Full request equivalent to a
Complete result. Existing Full retries after cancellation keep their rules.
In a memory refusal or parser failure, surface a reason and allow explicit
retry; do not repeatedly launch the same failed attempt on every track event.
Concretely, retain one attempt outcome per version (`Pending`, `Complete`,
`Canceled`, `Deferred`, `Failed`), or equivalent scheduler-private state.
Ordinary track events cannot clear `Failed`/`Deferred`; an explicit Retry may
re-admit one attempt after rechecking resources. A `Complete` outcome never
re-runs Full for unchanged navigation. This refines the current attempted-version
sentinel without adding a public parser or configuration contract.

### Bounded service, not just separate cancellation

An admitted demand Full **reserves a service turn**. For a stable document
version V, the dispatch rule is:

1. Let the one currently dispatched turn finish or acknowledge cancellation.
2. If a current-version content pass is still mandatory, dispatch the single
   latest such pass once. This is new-version source/semantic work, not
   same-version caret motion, scrolling, drag tracking or a refreshed projection.
   Navigation cannot reset this pass's edit-derived debounce deadline.
3. Dispatch the reserved Full **before every remaining same-version source or
   Grid viewport request**, even if newer navigation continues to arrive.
4. After Full retires, dispatch the latest remaining interactive viewport
   interest. It observes the committed index/extent and latest selection, not
   the interests captured when Full was reserved.

Thus admitted demand Full starts after at most the current running turn and
one mandatory current-version content turn; it never waits for an empty
navigation queue or a quiet interval. This is a bounded **analysis-turn** service
guarantee, not a wall-clock deadline for arbitrary parser/native work. The
guarantee assumes V remains current, the document stays open, the user does not
cancel, and resource admission remains valid. A resource recheck that fails at
dispatch changes the outcome to `Deferred` with a visible reason; it does not
leave the reservation silently pending forever. A real content edit changes V,
retires the reservation and gets the next turn after obsolete work acknowledges
cancellation. No design can certify a version that is continuously replaced
while also giving new edits priority.

Idle certification that reaches its quiet deadline may use the same reservation
rule; explicit demand promotes an existing same-version idle attempt without
creating another Full. Once reserved, further same-version navigation cannot
demote it, refresh its age/deadline or spend an additional interactive turn.
Repeated nonterminal arrival is a mailbox update, not another admission ticket.

All native controller and idle/demand callers for one document must share a
single dispatch decision point **before** entering the driver's analysis gate.
Retain only a running turn, latest content/viewport interests and one Full
reservation. The idle scheduler's timer offers an intent to this dispatcher;
it must not independently enqueue `AnalyzeAsync` behind a stream of viewport
Tasks. A FIFO semaphore guarantees serialization, not this priority or bounded
service, so the contract cannot be implemented merely by changing cancellation
tokens around the existing independent `Task.Run` calls. Direct test callers of
the existing driver/session API remain compatible; this coordination is Native
internal and introduces no public Formats API.

Once Full is running it may delay a Grid request, but not UI event processing or
Engine Apply. While it owns the lane, replace the latest pending target instead
of creating a Task per drag event. Do not wait for cancellation or the analysis
gate on the UI thread. Existing session candidate commit/cancellation invariants
remain intact.

Coalesce navigation over a short UI dispatch/frame interval; **<=16 ms is a
measurement target, not a timer guarantee or established result**. Do not use
the content-analysis 80 ms debounce for warm Grid-only movement. Leave source
typing debounce and non-CSV behavior unchanged unless separate evidence supports
a change. Same-version source-viewport work should not gratuitously cancel an
explicit demand Full either; document/version admission still applies.

Every queued analysis captures `(driver, document, version, navigation epoch,
request serial, source decoration interest, requested Grid anchor/columns)`.
Install only if all still match. Full commits extent/index facts on the session
lane, but **never installs a captured Grid selection or old viewport**. Its UI
completion validates document/policy/version, merges facts, and schedules the
latest live bounded Grid interest. Existing `PublishIdleFullAnalysis` already
re-queries rather than substituting Flow; retain and strengthen this behavior.
The captured source viewport may have changed: that rejects its old decoration
or ready-table presentation, not its same-version certified index facts. Remove
obsolete-source-viewport equality as a gate **only for that facts handoff**;
never relax current viewport/presentation admission for source maps or commands.

Selection is a separate logical interest: drag scrolling does not automatically
reselect the first displayed row, focus source, or collapse a ready rectangle.
An offscreen selection may be remembered for the same version, but does not
become an actionable ready cell or cross-window Copy certificate. The Mac
adapter's current fallback to the first ready row must not masquerade as retention
of an unseen selected record. Source edits keep the existing reset-to-source
behavior, and active composition keeps existing command admission.

## 8. Accessibility: name the real domain, not a fictitious full table

The independent native navigation controls must be externally discoverable,
keyboard-operable and labeled with axis, units and extent kind. Provide a nearby
accessible status with exact totals or explicitly unknown totals, shown range,
and pending target. Do not narrate every drag sample: publish coalesced position
updates and one terminal result, without stealing keyboard focus.

The table remains an explicitly named **bounded CSV window**. Its realized
row/column counts describe that window; visible cells include global one-based
coordinate labels and state. No provider reports the file's count as the native
child count while returning only cache-local row indices. UIA distinguishes the
container Scroll pattern from scrollbar RangeValue, and virtual placeholders
require an actual realization contract if exposed as VirtualizedItem
([Scroll guidance](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingscroll),
[VirtualizedItem guidance](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingvirtualizeditem)).
For this slice expose global navigation through the native controls with truthful
RangeValue/AX value descriptions; do not invent whole-file table GetItem or
synchronously parse in an accessibility callback. A later full virtual-table
provider is a separate contract, not something native labels alone establish.

AppKit table accessibility returns actual table row elements; pending rows must
be named pending, with no fake selected source field
([Apple table accessibility](https://developer.apple.com/documentation/appkit/nsaccessibilitytable/accessibilityrows%28%29)).
Verify standalone NSScroller's actual AX role/value/action on both target RIDs;
an in-process delegate probe or custom label is not VoiceOver acceptance. On
Windows independently inspect native RangeValue bounds/value/steps beyond 65,535
and focus routing, using the existing UIA host boundary. If either platform's
native provider cannot express the global ordinal domain correctly, add only a
small provider for these two navigation controls, not a new whole-table AX/UIA
framework. That decision requires target evidence, not speculation.

Prefix navigation can have a percentage **of its named prefix domain**, never a
whole-file percentage. It must not appear as a full-file Scroll pattern that
claims vertical scrollability is false merely because total count is unknown.
Native fallback can retain its bounded-table-local scrolling semantics provided
the accessible domain is unambiguous and external inspection confirms it.

## 9. Resource/performance acceptance targets

These are proposed release budgets, not current measurements. Record distributions
on matched hosted Native AOT targets and keep parser time, install/draw callback
return and physical/compositor presentation distinct.

| Property | Budget / decisive observation |
| --- | --- |
| Navigation state | O(1) per-axis/gesture/target state; two native scrollers; <=256 local row-map entries; no allocations proportional to exact row or column count. |
| Ready delivery | Existing <=256 rows, <=64 columns, <=8,192 cells, <=64 Ki UTF-16 arena and policy index cap remain enforced. At most one live installed and one candidate bounded projection. |
| Callback work | No file I/O, GetText, parsing, analysis-gate waits or unbounded source loops in paint/hit/AX/UIA/scroll callbacks. Nav callback p95 target <=1 ms; record worst case. |
| UI install | p95 target <=8 ms for representative visible windows; measure column recreation, local slot clearing and reentrancy separately. Existing all-cell redraw loops must not silently become whole-file loops. |
| Warm coordinate request -> native install | p95 target <=50 ms for ordinary sparse short-row 100 Mi-unit files; coalescing target <=16 ms. No general claim from a fast pure planner or managed projection benchmark. |
| Input fairness | Current source edit is admitted while Full/drag runs. Target cancellation observation <=10 ms on ordinary/giant/wide corpora, measured on each RID, not assumed from a token. |
| Overload | One latest target survives 1,000 drag updates; stale work never installs. Pending status remains useful if replay/demand work exceeds the warm budget. |
| Full indexing | Resource-admitted O(file) work; exact count arrives or deferred/failure status. No universal “instant distant row” claim before certification. |

Budgets intentionally do not claim lower process RSS: native views, Engine text,
source canvas, transient parser candidates and runtime overhead also contribute.
Keep benchmark/test files and reports under repository `.cache/` or `.temp/`.
New tracing is opt-in, aggregate counts/times/state only, with configured
`~/.mote` trace paths; never record file names, field values or source fragments.

## 10. Discriminating tests and migration

### Tests that would reject a misleading implementation

| Test family | Required invariant |
| --- | --- |
| Empty; one row/one column; exact-fit; smaller than page; CR/LF/CRLF trailing delimiter | No synthetic final row; disabled/non-scrollable domains; exact endpoints. |
| 100 Mi-unit `a\n`, ordinary CRLF, quoted-newline records, huge first field + tail | Thumb count uses actual record proof, not physical lines/bytes; sparse seek never enters quotes; giant first field does not renumber tail. |
| One giant comma-run record and 500,000+ columns | Horizontal End and drag reach the last actual column with bounded controls/cells; integer overflow and one-based labels remain safe. |
| Values 65,535/65,536 and near `int.MaxValue` | Windows uses full track position, not high-word truncation; pure arithmetic/adapter boundary tests need no huge test file. Mac conversion is monotone and both endpoints exact. |
| Unknown prefix; unknown numeric row beyond final count; memory-refused Full; canceled/retried Full | No final count fabricated; no 15-second navigation-starvation loop; Go-to failure differs from viewport clamp; source stays editable. |
| Prefix -> exact while thumb held; resize/typography change; palette-only update | Frozen drag target retains absolute coordinate; geometry invalidates gesture; palette does not unexpectedly move it. |
| Sparse ready rows `[r,r+2]` | Slot `r+1` remains pending, not compacted into row `r+2`; pending actions have no origin. |
| A->B->C same-version drag with B finishing last; Full starts at A and finishes after C selection | Only C installs; Full re-queries live C, does not overwrite its selection with A or substitute Flow. |
| Begin A, queue A Track/Commit/Cancel, Begin B with the same NavigationId, then dispatch queued A events | Exact gesture-token admission rejects every A event; no fresh serial/target/selection/native state is installed by A. Posted events captured A's immutable token when queued. |
| Commit A reenters Begin B; duplicate A terminal/late Track; valid final A worker completes after release | A consumes authority before reentry and cannot clear B afterward. Duplicate/late gesture phases fail. Final worker delivery uses epoch + request serial, so pointer release alone does not reject a valid result; B supersession does. |
| Hold the running turn, reserve demand Full V, then send 1,000 alternating Grid/source viewport updates without advancing a quiet timer | After release and at most one mandatory V content pass, the next admitted turn is Full, regardless of latest viewport arrivals. Verify admission order at the shared dispatcher/driver boundary, not Task creation order. |
| Reserve demand Full V, then edit to V+1 before service; or force a resource refusal at dispatch | Old V reservation retires and V+1 content wins the next eligible turn; refusal becomes visible Deferred, not an indefinitely pending or repeatedly relaunched attempt. |
| Edit/Undo/Redo, format-changing Save As, document replacement, close, active IME | Old epoch/presentation/Copies retire; no stale extent/version/source map reused; one Engine owner and exact Save bytes preserved. |
| Reentrant native reload/visibility/scrollbar installation | A nested newer epoch/serial wins; old outer call cannot restore native authority/status. |
| External UIA/AX before/after exact count, at distant location and pending state | Controls expose correct named domain/values; native bounded child counts stay bounded; source focus/selection never changes from scroll alone. |
| Sustained wheel/thumb + source typing + demand Full | Queue stays bounded; current edit wins admission; cancellation/latency traces distinguish user responsiveness from background completion. |

Run shared arithmetic/property tests locally without clipboard/host mutations.
Windows hidden-HWND tests validate actual scrollbar notifications/32-bit readback
and bounded ListView callbacks. Mac published-binary probes validate actual
NSScroller/NSTableView ABI and callback state on both architectures. Hosted
external UIA/AX tests then verify global navigation controls and focused ready
cell labels; manual Narrator/VoiceOver/physical trackpad/IME remain separate
acceptance gates. Do not recast synthetic dispatch as physical-input evidence.

### Integration order and assigned boundaries

1. **Controller/shared Native:** implement pure axis/viewport planner, epoch and
   latest-target state, exact per-gesture tokens, frozen gestures, pending slot
   contract and one shared reason-specific admission point with Full reservation.
   Add fake-shell/dispatcher tests for stale identity, bounded service and Full-result
   selection preservation. Preserve old Grid events and all source/Flow tests.
2. **Windows adapter:** container + two native logical scrollers, bounded ordinal
   slots, global event routing and native range tests. Do not touch Mac or Formats.
3. **Mac adapter:** owned scrollers/geometry/fractional input plus ordinal slots
   using the frozen shared contract; target ABI/probe review before dispatch.
4. **Independent validation:** external UIA/AX range/focus and matched AOT
   large-file request/install/cancellation measurements; no source-bearing artifacts.
5. **Integration:** enable global scrollers together with truthful prefix status
   only after same-version/empty/stale tests pass. Keep existing Go to/Follow
   source/Copy/Replace contracts and legacy source rollback intact. Update the
   native/release evidence documents with observed scope, not inferred completion.

One writer per area is sufficient. Before changing the internal shell event or
installation signature, freeze it with the root/controller owner and both adapter
owners. No public Formats ABI/API, configuration schema, source encoding, on-disk
cache or user document migration is introduced. If an adapter gate fails, retain
the shipped bounded-window navigation with its existing truthful label; do not
relax stale-map or pending-origin admission to make the new scrollbar appear
successful.

## 11. Why not a different architecture?

| Alternative | Rejection / scope |
| --- | --- |
| Full file row count in ListView/NSTableView, loading synchronously on callback | Couples native virtualization/AX realization to parser availability, invites unbounded native geometry and hidden callback work. It is not needed for two explicit logical scrollers. |
| Giant fake pixel document plus scaled thumb coordinates | Adds precision/overflow/pixel-to-ordinal problems while the source truth is already logical. A billion-pixel AppKit frame is unnecessary. |
| Estimate rows from bytes, line count or first-record density | A quoted giant field, ragged widths and quoted newlines immediately falsify the estimate. Estimates cannot authorize row identities or full-file percentages. |
| A second CSV index/parser for navigation | Duplicates quote state, lifetime/version proofs, memory and cancellation; existing ordinal checkpoints already solve the relevant query. |
| Universal asynchronous virtual-table accessibility framework now | Much wider than truthful global navigation; Realize/GetItem semantics need separate scope and target evidence. Keep bounded table identity and narrow navigation controls coherent first. |
| Repeatedly canceled idle Full on drag, or a new Full per event | Prevents progress or wastes O(file) work. Shared certification plus one latest target makes cancellation ordinary instead of an event backlog. |

The production principle is to separate view extent from retained ready payload,
and use the operating system's navigation controls without pretending those
controls loaded the file. Microsoft's Windows Terminal also publishes buffer
range/page/position independently through `SCROLLINFO`; this supports the
mechanism choice, **not** mote's CSV semantics or latency targets
([production window code](https://github.com/microsoft/terminal/blob/main/src/interactivity/win32/window.cpp)).

The relevant academic connection is selective metadata rather than a full cell
graph. *NoDB: Efficient Query Execution on Raw Data Files* (SIGMOD 2012) uses
position metadata and selective raw-data access to avoid mandatory import; its
database workload is not interactive, mutable editor acceptance. The useful
inference here is to reuse the existing bounded certified positional index, not
import database operators, per-attribute maps or an LRU policy
([author-hosted paper](https://scholar.harvard.edu/files/NoDBsigmod2012.pdf)).
*Fast Incremental PEG Parsing* (SLE 2021) investigates incremental parser reuse
and selective memoization; it reinforces that reuse/resource trade-offs need
measurement, but does not establish CSV row counts, quote-safe seeks or a Native
AOT GUI percentile ([paper](https://people.seas.harvard.edu/~chong/pubs/gpeg_sle21.pdf)).
Neither is a new proof of this UI design. The concrete research-worthy next
question is narrower: **can certified sparse ordinal seeking plus a one-target
delivery scheduler keep edit admission and warm navigation bounded on adversarial
100 Mi-unit record shapes?** The tests and budgets above make that answer
falsifiable before claiming release readiness.
