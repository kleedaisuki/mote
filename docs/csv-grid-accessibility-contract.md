# CSV Grid external accessibility contract

Date: 2026-10-01. Status: **implementation contract; bounded model and experimental
native adapters implemented, not externally accepted**. See the evolving
[implementation/evidence ledger](csv-grid-accessibility-implementation.md).
Original design inspected native adapters at `f7d7e44`; the logical-scrollbar code is
already present in this checkout even though the older
[scrollbar design](csv-grid-logical-scrollbar.md) retains its proposal heading.
This document introduces no code, public API, configuration or release claim.
Root explicitly approved the bounded-window decision below during design.

## 1. Decision: a truthful accessible window, not a fictitious whole-file table

Expose one **CSV navigation group**, containing one **bounded-window Table**, two
logical navigation controls, a coordinate command and a concise status/detail
surface. The Table describes the installed window only. Its native pattern
indices are local zero-based indices; row/column headers and cell names carry
the actual one-based CSV ordinals. The group describes the independently proved
whole-file or prefix extent. No pattern count changes meaning when indexing
finishes: exact file counts remain group/status facts, not Table counts.

This distinction is deliberate, not permission to misnumber CSV cells. Example:

```text
Mote editor (existing source Document / AXTextArea; unchanged)
Mote CSV grid navigation (Group / AXGroup)
  CSV grid window (Table / AXTable)
    columns: Column 17, Column 18, ... Column 32
    rows:    Row 1001, Row 1002, ... Row 1064
      cell: Row 1001, Column 17; value/state; selected
  File rows / Indexed prefix rows (native navigation control)
  File columns / Known columns (native navigation control)
  Go to CSV cell (existing native coordinate dialog, reachable command)
  CSV grid status (extent, pending target, selected absolute coordinate)
```

For this window, `GetItem(0,0)` is CSV `(1000,16)` internally and is named
`Row 1001, Column 17`. UIA `GridItem.Row=0`, `Column=0`; AX row/column ranges are
`{0,1}`. They must **not** be `1000/16` inside a Table reporting `64/16`.
Row-number gutter is a row header, not CSV column 1 or an extra data column.
First CSV record is data unless an explicit future policy says otherwise; do
not guess that it contains semantic column names.

[Microsoft's Grid contract](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementinggrid)
uses a two-dimensional coordinate system; its
[GetItem contract](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-igridprovider-getitem)
uses zero-based positions and requires a provider even for ragged empty cells.
[Apple's cell index ranges](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol/accessibilityrowindexrange())
likewise describe cells in their containing table. Neither inspected contract
provides a portable unknown whole-file count sentinel. Therefore advertising
cached counts as full-file totals, or absolute item indices outside a local
count, is not an acceptable shortcut.

The first production contract supports semantic browsing of every **admitted
window** plus explicit logical navigation to another window. It does not claim
that an arbitrary external `GetItem(fileRow,fileColumn)` can retrieve any cell
in the file. Whole-file virtual patterns are a separate coherent migration
(section 10), not an incomplete pattern added for an inspector screenshot.

## 2. Existing ownership and concrete seams

Read the existing [native implementation](native-csv-grid.md),
[source provider design](accessibility-provider-design.md),
[Windows source tree design](windows-uia-tree-design.md), and
[reader protocol](screen-reader-acceptance.md) before changing their contracts.
Some older source-provider checkpoints intentionally preserve earlier negative
evidence; use the later target results for delivered scope, not their headings
as an excuse to repeat completed experiments.

| Actual code | Required internal change |
| --- | --- |
| `NativeGridScrollFrame`, `NativeGridNavigationId`, `NativePresentationId` | Reuse extent/navigation versus ready-command authority; do not conflate them. |
| `NativeGridPlanner.Slots` and adapter `_slots` | Reuse contiguous requested slots with explicit null gaps; never compact sparse ready rows. |
| `NativeCsvGrid.Row/Cell/Display` | Reuse coordinate lookup and bounded display; never decode source inside an accessibility callback. |
| `WindowsCsvGrid` owner-data `SysListView32` | Default ListView semantics cannot be assumed to express the custom rectangular cell selection. Add a narrow Grid-specific fragment provider and verify proxy merging externally. |
| `MacCsvGrid` cell-view `NSTableView`, `_column`, `_anchorRow`, `_selection` | Preserve native rendering/input. After external native-row bridge failures, one stable non-view semantic Table replaces its accessible subtree; reuse coherent cell selection/ordinal/state facts rather than native row selection or detail labels. |
| Adapter-private selection and native focus | Publish one immutable selection/frame from the adapter after each coherent mutation; both painting and accessibility consume the same coordinates. No new competing selection owner. |
| `NativeEditorController.Grid.cs` | Keep source Reveal, bounded off-thread Copy and one-transaction Replace semantics unchanged. Add an internal acknowledged command entry point if accessibility actions are exposed: existing `void GridIntentRequested` / `Action<NativeGridIntent>` cannot certify admission or completion. |
| Existing source UIA/AX bridge | Separate registration/lifetime. Grid detach must never invalidate `AccessibleDocument`, hide source or change the source input-island protocol. |

Engine owns text/history; Formats owns grammar, origins and value state; controller
owns document/navigation admission; adapter owns the already-established Grid
selection and platform focus; Grid provider owns only registration and bounded
node wrappers. None owns another CSV parser or another copy of the file.

## 3. Proposed internal data/state API

The following are **proposed internal Native types**, not existing declarations
or a public Formats change. Freeze names/signatures with root and both adapter
owners before implementation. Use XML documentation on every declaration.

```csharp
/// <summary>Identifies a bounded installed tree, not a reusable native row slot.</summary>
internal readonly record struct GridAccessibilityId(
    NativeDocumentStamp Document, long WindowSerial);

/// <summary>Absolute CSV coordinates; never local native indices or source offsets.</summary>
internal readonly record struct GridCoordinate(int Row, int Column);

/// <summary>One adapter-owned selection, independent of canonical source selection.</summary>
internal readonly record struct GridAccessibleSelection(
    GridCoordinate Anchor, GridCoordinate Active, bool WholeRows);

/// <summary>Closed selection mutations applied by the sole adapter selection owner.</summary>
internal abstract record GridSelectionMutation
{
    /// <summary>Replaces the entire Grid selection with an admitted rectangle.</summary>
    internal sealed record ReplaceRectangle(GridAccessibleSelection Selection) : GridSelectionMutation;
    /// <summary>Clears Grid selection only, including any retained off-window rectangle.</summary>
    internal sealed record Clear : GridSelectionMutation;
    /// <summary>Adds exactly one cell only if the exact union is representable.</summary>
    internal sealed record AddCell(GridCoordinate Cell) : GridSelectionMutation;
    /// <summary>Removes exactly one cell only if the exact difference is representable.</summary>
    internal sealed record RemoveCell(GridCoordinate Cell) : GridSelectionMutation;
}

/// <summary>Snapshot-free immutable facts for one complete accessible installation.</summary>
internal sealed record GridAccessibilityFrame(
    GridAccessibilityId Id,
    NativeGridScrollFrame Navigation,
    NativePresentationId? Ready,
    GridRenderProjection? Projection,
    GridRange Rows,
    GridRange Columns,
    GridAccessibleSelection? Selection,
    GridCoordinate? FocusedCell,
    bool HasTableFocus,
    GridAccessibilityGeometry Geometry);

/// <summary>Named UI-thread operations; no source decode or worker wait is permitted.</summary>
internal interface IGridAccessibilityActions
{
    /// <summary>Atomically applies a representable mutation; rejection changes no selection.</summary>
    GridAccessibilityResult MutateSelection(GridAccessibilityId id, GridSelectionMutation mutation);
    /// <summary>Focuses the admitted table/cell, not source, clipboard or Engine.</summary>
    GridAccessibilityResult Focus(GridAccessibilityId id, GridCoordinate? cell);
}

/// <summary>Controller-issued operation identity, never inferred from event dispatch.</summary>
internal readonly record struct GridCommandOperationId(
    NativeDocumentStamp Document, long Sequence);

/// <summary>Admits a command from one accessible window and one ready presentation.</summary>
internal readonly record struct GridCommandRequest(
    GridAccessibilityId Window, NativeGridIntent Intent);

/// <summary>Separates actual controller admission from eventual source-command outcome.</summary>
internal interface IGridAccessibilityCommands
{
    /// <summary>Returns Accepted with a token only after controller guards admit the request.</summary>
    GridCommandAdmission BeginCommand(GridCommandRequest request);
    /// <summary>One terminal acknowledgment for every accepted token, with observed outcome.</summary>
    event Action<GridCommandCompletion> Completed;
}
```

`GridAccessibilityGeometry` contains the clipped Table rectangle, row height,
bounded row/column rectangles, scale/coordinate conversion and layout serial.
It has no source snapshot. `GridAccessibilityResult` is a closed internal result:
`Applied`, `NoChange`, `Unsupported`, `Stale`, `NotReady`, `InvalidCoordinate`,
`Unavailable`, `CompositionBlocked`. Selection/focus succeeds only after actual
mutation and coherent frame publication (or verified `NoChange`); it never
returns success for a mutation merely queued. Source commands deliberately use
a **different** admission/completion protocol below, not `Applied`.

### Selection mutation admission

All mutations pass through the adapter's existing selection owner. There is no
provider-private set and no direct native selected-row setter bypass. At actual
UI-thread admission, capture the **latest** frame and adapter-owned full retained
selection, validate identity and all local-to-absolute conversions, compute the
desired selection without mutation, then atomically publish native/accessible
selection. `GridAccessibilityId` survives selection-only updates: it does not
authorize applying a union/difference to an earlier provider-read selection or
only its local intersection. Either perform the complete admission/mutation
without callbacks between them, or validate an adapter selection revision again
after any reentrant native call. Reentrant supersession returns `Stale`, never
writes an old result. Frame/selection revision is internal state, not a second
selection owner.

| OS request | Internal mutation and exact outcome |
| --- | --- |
| UIA cell `Select()` | `ReplaceRectangle` with identical anchor/active and `WholeRows=false`. |
| UIA cell `AddToSelection()` | `AddCell`; exact union with current rectangle must still be a rectangle. Adding an already selected cell is `NoChange`. Do not expand a hull if it would select any additional cell. |
| UIA cell `RemoveFromSelection()` | `RemoveCell`; exact difference must be a rectangle or empty. Removing an unselected cell is `NoChange`; a hole or split rectangle is `Unsupported`. |
| AX `setAccessibilitySelectedCells:` | Validate every node belongs to the current window, deduplicate bounded entries, and verify the set fills its bounding rectangle exactly. Empty array/nil means `Clear`; full rectangle means `ReplaceRectangle`; sparse set or old/mixed node means rejection. |
| Existing keyboard/pointer rectangle operation | Adapter-owned rectangle update followed by the same frame publication. It is not a new provider selection state. |

AX validation is O(window slots), capped at 8192 input entries before allocation;
oversized arrays are refused, not partially read/applied. Empty setter explicitly
clears the **whole Grid selection**, including retained off-window endpoints;
nonempty setter replaces it rather than retaining undisclosed off-window cells.
There is no whole-row inference from a full-width cell array: `WholeRows=false`
unless the existing explicit whole-row command requested it. Add/remove with an
existing whole-row selection is `Unsupported` unless the adapter can prove the
exact requested set under its established whole-row semantics. For an ordinary
retained off-window rectangle, check exact union/difference using its full
endpoints, not only the current visible intersection.

On `Unsupported` or any failed admission, Grid selection, native selected-row
state, canonical source selection, focus, document/history and clipboard remain
unchanged. Map unsupported UIA mutation to `UIA_E_INVALIDOPERATION`; expose no
successful AX mutation/selection event for a rejected setter. AX setters whose
ABI returns void cannot report success: retain old state, optionally update a
bounded non-source error/status, and prove unchanged selection via external
readback. Test actual reader recovery from a rejected hole operation; do not
pretend all arbitrary multi-selection requests are representable.

### Source command admission and completion acknowledgment

The existing `GridIntentRequested` and adapter `Action<NativeGridIntent>` seams
return void. They can silently refuse stale/composing/missing-origin commands,
open/cancel a modal Replace dialog, and fail native focus or clipboard publication.
An adapter event invocation is therefore **neither admission nor completion**.
No provider may return `Applied`, declare Reveal successful or synthesize a
completion event from this fire-and-forget seam.

`GridCommandAdmission` has exactly two alternatives: `Rejected(reason)` or
`Accepted(GridCommandOperationId)`. Reasons include stale window/presentation,
not-ready/missing origin, composition blocked, unsupported intent, busy,
unavailable and policy refusal. The proposed acknowledged controller entry
reuses existing command guards and implementations; it does not add a second
command dispatcher with weaker validation. Only explicit source/data commands
are eligible; `Select` must use the selection mutation seam above.

Admission validates both current accessible-window identity and the **captured
exact ready presentation**. It reserves a controller-issued monotonic operation
token only after those guards pass and before async/modal work can reenter. At
most one accessibility-issued source command is outstanding per attached Grid;
a concurrent request is `Rejected(Busy)`, not an unbounded operation queue.
Existing non-accessibility keyboard/menu paths remain compatible, but a command
or presentation that supersedes the accepted operation must terminate its token
with the accurate cancellation/stale outcome rather than leave it pending.

`GridCommandCompletion` contains the original operation token, command kind,
terminal kind (`Completed`, `Cancelled`, `Stale`, `Refused`, `Failed`), bounded
reason code and observed completion facts. Define a discriminated outcome, not
optional facts that permit a successful-looking empty receipt:

| Command/outcome | Minimum observed terminal facts |
| --- | --- |
| Reveal completed | Same command document/version, exact proved source range, canonical source selection at that range and **actual native source focus readback** after the focus operation. A void `FocusSource()` call alone is not proof. |
| Reveal refused/failed | Guard/policy/focus failure recorded; do not emit completion-success or claim the source was untouched if selection actually changed before focus failed. Receipt reports that partial outcome honestly. |
| Replace completed | Original document admitted after modal return, one committed Engine change with resulting document/version and the existing one-transaction semantics. Source coordinates afterward are the result, not an expected reused Grid identity. |
| Replace cancelled/stale/refused/failed | Dialog cancel versus post-modal identity failure versus preparation/apply failure distinguished; no claimed commit. A stale callback cannot edit another document. |
| Copy completed, if exposed later | Actual native publisher return succeeds after final identity/cancellation admission; report publish failure separately. Prepared payload alone is not publication success. |

Reserve/publish the Accepted receipt **before** dispatching work that could
finish synchronously: post the operation only after admission returns, or buffer
its terminal acknowledgment until the receiver can associate the token. Exactly
one terminal ack per accepted operation; stale duplicates/late callbacks are
ignored. Final disposal cancels remaining accepted tokens and clears all
listeners without keeping old source snapshots alive. Observer delivery is
outside internal locks and cannot resurrect a retired operation during reentry.

The provider maps `Accepted` to OS asynchronous action acceptance where permitted,
not to task completion. On terminal completion it posts the **actual current**
focus/selection/status event only when its attachment and token are still live;
a stale cell does not become valid again because the command finished. UIA
Invoke is asynchronous-compatible, while synchronous selection/focus retains
the stricter contract above. For Mac actions whose API reports only a boolean,
true means admitted action, never a fabricated successful source transfer;
subsequent facts/events must corroborate completion.

**Implementation gate:** until this controller acknowledgment seam, token
ordering and native focus/result readback are implemented and externally tested,
do not advertise cell Invoke/AX press/custom Replace or Copy actions through
the Grid provider. Existing keyboard/menu commands still work and remain
discoverable in help, but that is not an accepted accessibility command pattern.
No public Engine/Formats or source-provider API change is necessary; the
internal controller seam is a coordinated additive implementation change, not
an assertion that the current void API already returns these outcomes.

Frame construction invariants:

1. Rows/columns name contiguous bounded **window slots**, not delivered array
   positions: <=256 rows, <=64 columns, <=8192 slot products. Reduce rows if a
   rectangular pending-slot matrix would exceed the cap. Projection delivery
   omissions do not authorize larger accessible windows.
2. `Ready` may be non-null only when its document stamp, projection version and
   navigation ready installation agree. Pending placement has no command
   authority, even if an older same-version projection remains retained.
3. Each slot `(i,j)` maps by checked wide addition to
   `(Rows.Start+i, Columns.Start+j)`. A delivered row with another ordinal can
   never fill it. Null row/field descriptor is an explicit unproved/pending slot,
   not a guessed empty record/field.
4. A cell's state, display slice, syntax flag and source-command eligibility
   are derived from that captured frame once. Retained wrappers do not read
   a new projection midway through a method. The display arena remains <=64 Ki
   UTF-16 units and a delivered cell <=1024 units. Names/status suffixes have
   fixed bounded overhead; do not construct an entire-table text value.
5. Frame publication occurs only after coherent native install/selection/geometry.
   Existing installation serial checks remain after every reentrant native call.
   Publish before raising events; after event reentry, never write the old frame.
6. The provider captures the frame atomically per request. A node key includes
   `GridAccessibilityId`, role and absolute coordinate. Slot recycling cannot
   rebind a retained object to another row or document.

The root/group object survives viewport changes for one adapter attachment.
Window children are invalidated on window-origin/shape, projection identity,
document or command-readiness changes. Selection/focus-only changes update the
same child set and raise property/events; pure palette changes need not replace
nodes, but **command admission still uses the latest presentation identity**.
If implementation cannot prove palette-only equivalence, retire children safely
and measure the reader disruption; do not silently reuse stale command authority.
Geometry changes update captured bounds without interpreting old screen pixels
as new source coordinates. Old child methods fail unavailable after retirement,
including labels, parent navigation and actions; they never retain closed text.

## 4. Cell state semantics: value is not clipboard authority

The operations below describe existing native keyboard/menu capabilities plus
the intended acknowledged accessibility command surface. They do not waive the
section 3 gate: providers omit source-command actions until controller admission
and terminal acknowledgment are implemented and externally accepted.

| Captured state | Accessible value/name/help | Allowed operations |
| --- | --- | --- |
| Complete, including empty field | Absolute coordinates plus bounded presentation value; empty has explicit “empty value”, distinct from Missing. | Grid select/focus; Reveal only with proved origin; existing Copy/Replace admission. |
| Clipped | Displayed prefix explicitly described as “display clipped; use Copy or reveal source for full value”. | Select/focus; origin-backed Reveal; existing policy may prepare full bounded Copy; no direct value setter. |
| Oversized | “Oversized value; value not materialized; reveal source”. Never a successful empty/full value. | Select/focus; Reveal only if `SourceRange` exists; Copy subject to existing independent source budget; Replace refused. |
| Missing | “Missing field in this record”, not empty string data. Row width is proved; no source origin. | Select/focus; no Reveal, decoded Copy or Replace; explicit padded-CSV command remains a distinct menu action. |
| Pending descriptor or null slot | “Pending; row/field not ready”. If only requested ordinal is known, say “requested row”, not “proved record”. | Orientation/focus within installed window only; no source/data action. Navigation may request another window. |
| Syntax error flag | Append “CSV syntax error” independent of value completeness; any detail is from bounded delivered diagnostics. | Preserve current policy refusals. Never announce “valid” just because a diagnostic list was truncated. |
| Sanitized display | Explain “display contains visible substitutions”; value is presentation, not exact decoded/source text. | Do not synthesize clipboard from the accessibility value. |

For Pending/Missing/Oversized expose state descriptions rather than an invented
data ValuePattern. For Complete/Clipped, any Windows read-only ValuePattern
represents **bounded presentation text**, explicitly labeled as such; no editable
ValuePattern on any Grid cell. UIA Name/HelpText and AX label/help carry state,
including actual complete empty values. Embedded NUL display substitutions must
not enable native clipboard publication. Existing Copy independently refuses
NUL; source Reveal does not interpolate display offsets.

Installed pending and Missing **layout slots** are legitimate bounded cell nodes
because the UI actually displays them, not fabricated offscreen CSV records.
No node is created beyond the admitted window, and no source origin is invented
for any node. No attempt to read a cell causes parsing or I/O.

## 5. Extent, selection, focus and keyboard behavior

### Extent is separate from Table dimensions

| Navigation state | Group/status and logical controls | Table |
| --- | --- | --- |
| Unavailable | “Indexing; CSV row coordinates unavailable”; disabled unproved axes. | Empty or bounded requested pending window only when controller provides real requested coordinates; no guessed anchor row. |
| Prefix | “Indexed prefix: N rows; file total unknown”; “known columns: at least M; file maximum unknown”. | Local slot counts and absolute labels only; neither N nor M is a whole-file Table count. |
| Exact | “File: N records; maximum M columns”; ragged Missing remains distinct. | Same bounded-window contract; exact-empty is zero-row/zero-column table, no selection/focused cell. |
| Demand/deferred/failure | Current domain plus pending target/reason/retry command. | Old content is not silently exposed under the new target; pending slots have no Ready authority. |

UIA Scroll/RangeValue and AX scroller value describe the **named** navigable
domain. Prefix percentage is percentage of indexed prefix, never estimated
percentage of the file. Keep the two actual native scrollers, suppress competing
cache-local scrollers and verify external labels/values; code annotations alone
are insufficient. Go-to input and symbolic End use existing navigation tokens
and certification; Table property reads do not start demand Full.

### Selection and focus

- Grid selection is independent from canonical source selection. Selecting a
  cell, reading its value, scrolling or reader exploration never edits source,
  changes source caret, creates Undo, changes dirty state or publishes clipboard.
- One native table focus owner, one semantic active cell. Physical focus stays
  on ListView/NSTableView; UIA fragment focus/AX focused element identifies the
  active cell when available. At pending/unavailable cell coordinates report
  the table/container with explicit pending status, not a false ready focus.
  Merely reading or moving the reader's browse cursor does not force keyboard
  focus or selection. Logical scroller focus is that scroller, not the active
  cell or source.
- Rectangle selection preserves anchor/active and reports selected cells only
  inside the current admitted window. Selection of a proved Missing layout cell
  is legal. The bounded Table's Selection pattern/AXSelectedCells describes the
  intersection; status gives full anchor/active absolute coordinates and says
  if selection extends outside this window. Never advertise a whole-file
  selection array or silently truncate a claimed whole-file selection.
- UIA Select replaces rectangle with one cell. AddToSelection/RemoveFromSelection
  cannot create arbitrary disjoint sets because the product supports rectangles:
  admit only an exactly representable rectangle (or empty selection) and reject
  unsupported holes without changing state. `CanSelectMultiple=true` is not
  permission to claim arbitrary set semantics. AX selected-cell setter applies
  the same validation to a bounded array; reject mixed-window/stale objects.
- Pending navigation retains the pending target and native focus without
  selecting a different nearby ready row. After delivery, install/select/focus
  the exact target only if current request authority survives. Edits retire
  document-scoped coordinates rather than shifting quote-sensitive records.
- Reveal is an explicit named action: it selects the **proved whole-field source
  syntax**, then focuses the existing source editor. The cell's primary Windows
  Invoke / Mac press action may be “Reveal source” only when that action is
  advertised **and the acknowledged command seam has passed its gate**.
  Select/SetFocus is not Reveal. Missing/Pending omit the action.
- Replace remains an explicit native modal editor, not a direct AX/UIA value
  setter; repeat ready identity after the dialog and preserve one Engine Undo.
  Active source IME composition blocks unsafe transfer/commands. Grid provider
  never commits or cancels preedit merely to satisfy an inspector call.

Keep existing platform keyboard vocabulary: Windows arrows/Shift arrows,
PageUp/PageDown, Enter/Space Reveal, F2 Replace, Ctrl+C; Mac arrows/Shift,
Return Reveal, Command-Return Replace, Command-C, Page and Option-column
navigation. Go-to/source/Grid/scroll controls are keyboard reachable and escape
paths remain available. Do not reuse reader modifiers (Narrator/VO) for custom
shortcuts. Expose current command shortcuts in help, not inaccessible prose
alone. Any keyboard parity changes are a separately coordinated product change,
not silently added as an accessibility implementation detail.

## 6. Windows UIA provider and macOS AX mapping

### Windows

Use Native AOT source-generated COM interfaces (separate SDK vtables), following
the existing bridge's ABI tooling, **not** runtime reflection or a UI framework
with packaged sidecar binaries. At Grid HWND `WM_GETOBJECT/UiaRootObjectId`,
return the Grid fragment root; preserve unrelated object IDs/default handling.
Default HWND/window/non-client providers remain, but must not leave a second
semantic row-select ListView beside the custom cell Table. Header HWND merging,
ControlView/ContentView/RawView and `FromHandle` are explicit external gates.
Do not suppress every `WM_GETOBJECT` request as a supposed hide mechanism.

| Node | Required behavior/pattern |
| --- | --- |
| Table | `IRawElementProviderSimple`, Fragment/FragmentRoot, Grid, Table, Selection; bounded local RowCount/ColumnCount, correct row-major layout and headers. Native parent composition handled by HWND host. |
| Row/column headers | Native-compatible Header/HeaderItem or appropriate row-header text elements, exact absolute labels, associated through Table/TableItem. Not selectable CSV cells. |
| Cell | Fragment, GridItem/TableItem (local indices, span 1), SelectionItem; current Name/HelpText/state, clipped geometry; read-only presentation Value only when semantically applicable; Invoke only if Reveal admitted. |
| Group/status/scrollers | One reachable named domain; native logical controls retain their own focus/value/actions; table `DescribedBy` references status. No Table-wide TextPattern duplicating source. |

[Microsoft Table requirements](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-supporttablecontroltype)
require Grid/Table and child GridItem/TableItem plus focus/structure/property
events. Implement them completely before claiming Table acceptance; an
advertised pattern with `E_NOTIMPL` members is not production productization.
Valid in-window empty/Missing cells return real layout providers; out-of-range
coordinates fail invalid-argument. Retired wrappers return
`UIA_E_ELEMENTNOTAVAILABLE`, not old text or silently rebound objects.

Hit testing returns the actually clipped cell/header/scroller at screen point;
outside the Table never returns a cell. Delivered but currently clipped local
cells can have empty visible geometry / `IsOffscreen=true`, not invented bounds.
ScrollItem can reveal such a retained in-window cell through existing native
geometry; it cannot realize arbitrary file cells outside this Table.

### macOS

Prefer adding the narrow selectors to the existing native Table/row/cell views
when they can express this contract. If default AppKit row selection/focus or
cell recycling cannot do so, attach bounded `NSAccessibilityElement` children
for the Grid only and hide **only duplicate Grid implementation views**. Do not
alter the source proxy/input island, window child list outside Grid, or first
responder behavior. Choose native-backed versus proxy-backed mapping from the
first external negative result, not by assumption.

Expose AXTable with local row/column counts; AXRows/AXColumns and headers;
AXVisibleRows/Columns/Cells consistent with clipping; AXCellForColumnAndRow
returns the exact local slot; AXRowIndexRange/AXColumnIndexRange `{local,1}`;
AXSelectedCells and validated setter; row/column AXIndex local; absolute
CSV labels/identifiers; state/help and bounded presentation values. Default
row-level AXSelectedRows must not masquerade as the custom rectangle. If rows
are also marked selected, define that as complete-row selection only, not an
accidental native selectedRow highlight. Do not expose a cell as editable text.

Apple documents the
[table rows/selection/visibility surface](https://developer.apple.com/documentation/appkit/nsaccessibilitytable/accessibilityrows())
and [table attributes](https://developer.apple.com/documentation/appkit/table-view-and-outline-view-attributes).
Use a native cell press/custom named Reveal action only when origin-backed;
return nil/no supported action for inapplicable operations. Unknown extent is
status, not NSNotFound incorrectly repurposed as a file count. Check the exact
Objective-C selector encodings, pointer-sized NSRange/NSInteger and aggregate
returns on published osx-x64 and osx-arm64; Windows compilation is not an ABI
test. Parent-space versus global geometry conversion must follow AppKit screen
coordinates, including Retina and multi-display placement.

## 7. Threading, events, lifetime and limits

Windows first choice is a correctly apartment-bound UI-thread COM provider with
`ServerSideProvider | UseComThreading`, **after** verifying source-generated COM
registration/marshalling on both RIDs. Microsoft documents that
[UseComThreading routes STA providers to their STA](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-provideroptions).
The flag alone does not establish that the generated wrapper is apartment-bound.
The initial discriminating test records actual callback thread IDs under an
external MTA client. If callbacks arrive on arbitrary threads, immutable reads
remain safe, but UI mutations must not be advertised until an appropriate
marshalling boundary is implemented and externally verified. Do not return
success for a queued Select/SetFocus, block on a format worker, or use a nested
message pump to simulate synchronous completion. AppKit Grid mutation/objects
remain main-thread owned; verify external callbacks and retained element release
against that ownership. Exceptions must never cross either unmanaged callback.

UI never synchronously waits for an accessibility client or analysis; providers
never lock the analysis lane or decode fields. Read calls use only one frame;
action calls re-admit exact identity on UI immediately before mutation. Async
Invoke may acknowledge controller admission with a tracked operation token and
complete via the terminal acknowledgment in section 3; merely invoking the
existing void event is not admission. It cannot announce successful Reveal
until exact source selection and actual native focus have been read back after
reentrancy. Deferred/failed command remains visible; if a failed Reveal had
already changed source selection before focus failed, report that partial
outcome rather than claiming no state changed.

| Transition | Event after publication; no stale outer write |
| --- | --- |
| Cell selection | UIA SelectionItem/Selection changes; AX selected-cells change; current absolute detail once. |
| Native focus enters/leaves/moves | Correct single semantic focused element and focus event; no artificial focus on every parser result. |
| Window/pending/ready replacement | One bounded structure/layout notification, selected/focused target update if actually changed; parent status/extent update. |
| Prefix becomes exact | Domain/status and scroller properties changed; do not renumber Table indices or focus source. |
| Value/state change in surviving node | Value/Name/help/property notification as appropriate; pending does not falsely become empty. |
| Resize/window moves | Current clipped geometry/offscreen change; no cell/selection data change. |
| AX-only fault/detach | Retire provider-local token before UI teardown; editing and source accessibility continue. |

Use UIA structure/focus/selection/property event APIs and AppKit
`NSAccessibilityPostNotification`, not `NSNotificationCenter` for AX. Apple's
[layoutChanged contract](https://developer.apple.com/documentation/appkit/nsaccessibility-swift.struct/notification/layoutchanged)
identifies affected elements; send only the current bounded set. Coalesce
noncritical status updates, never drop a final selection/focus transition.
Do not issue a full speech announcement on every cell property request/paint.
Keep native automatic events and custom events from duplicating the same change;
verify with an external event observer and then the actual reader.

Lifetime: registration -> complete frame -> published/events -> retire -> detach
before HWND/view destruction. Children hold only a provider-local token and key,
not controller/document snapshots or permanently captured old projections. A
retained client wrapper cannot grow a provider-owned history cache. Native
wrappers are created lazily for current bounded slots; discard their map on
retirement, and clear frame/projection at detach. No allocations proportional to
file row count, field count, or visited-window history. Target memory overhead
is O(window slots + bounded display + headers), not O(file). Measure worst-case
8192-slot tree/enumeration, retaining stale wrappers, and teardown with a 100 Mi
fixture. Limits are resource contracts, not measured RSS improvements.

Tracing remains opt-in local `~/.mote` convention/configured trace path. Record
method/category, state, requested count, duration, result and identity epoch only;
never source values, file paths or clipboard data in product tracing. Synthetic
acceptance fixtures may record expected marker strings in explicit repo-local
test reports, not general telemetry.

## 8. Discriminating external acceptance

Use separate cross-process clients against freshly published strict one-binary
AOT for win-x64/win-arm64/osx-x64/osx-arm64. Keep synthetic fixtures and reports
under root `.temp/` and `.cache/`; do not inspect unrelated apps or enable OS
trust/input sources/reader without the existing reviewed test protocol. Record
commit, binary SHA-256, OS, architecture, scaling, exact PID/window, fixture hash,
current navigation/ready identities and four-way pass/product-fail/blocked/
inconclusive classification. No clipboard mutation is needed for these tests.

| Falsifier | External expected result |
| --- | --- |
| Window beginning at row 1001, column 17 | One bounded Table; local GetItem/AX ranges 0-based, absolute labels/headers exact; not row 1 or file RowCount. |
| Quoted CRLF/newline values; final empty record; ragged rows | Real logical ordinals differ from physical lines; empty value distinct from Missing; no synthetic final record. |
| Sparse delivered rows r and r+2 | Window slot r+1 remains Pending, r+2 stays local index 2; no compacted/renumbered record. |
| Complete empty, Pending, Missing, Clipped, Oversized, syntax error, sanitized NUL | Distinct state/help/value; no fabricated full value/origin; commands obey current policy. |
| Extent unavailable -> prefix -> exact; exact empty; Full deferred/refused | Group/scroller domain truthful; Table counts retain local meaning; pending target never becomes a guessed selection. |
| Read tree/properties repeatedly, focus/select Grid, move logical scroller | Source bytes/version/selection/dirty/history unchanged; no clipboard publication or parser call from reads. |
| Rectangle, whole-row, cross-window retained rectangle; AX selected-cell setters | Same painted/accessible selection; local intersection explicitly scoped; impossible holes/mixed old nodes rejected without mutation. |
| Hold Add/Remove request; change selection without replacing window identity; admit request | Exact mutation uses latest full retained rectangle, not the earlier frame/intersection; impossible union/difference is Unsupported and source selection stays unchanged. |
| AX empty/full/sparse array with retained off-window selection | Empty clears entire Grid selection; full rectangle replaces entire selection; sparse/oversized/mixed-stale arrays fail atomically, no partial setter or successful event. |
| Focus table/cell/scroller; browse without focus; explicit Reveal | One correct semantic focused element; native focus on expected control; Reveal selects exact whole syntax and transfers to existing source once. |
| Controller command refused, accepted then modal cancel/edit/rebase, focus call reenters or fails, duplicate terminal callback | No success inferred from void dispatch; one correctly associated terminal ack per admitted token; cancelled/refused/stale/failed distinct; Reveal success requires post-reentry selection/focus readback, partial failure reported. |
| Retain cell; same-version rebase/theme; edit/Undo/New/close; provider-only fault | Old cell cannot become a new coordinate/document; appropriate unavailable/error; source provider remains alive through Grid fault. |
| External UIA all three views, FromHandle/point/focus; Mac AX focused/parent/point | No duplicate default row-selection Grid alongside custom table; headers/gutter not counted as CSV data; source/preview identities unchanged. |
| Move/resize/Retina/multi-monitor; clipped cell | Actual screen geometry/offscreen facts; no false hit outside table; no source-offset interpolation. |
| 1000 bounded queries during pending Full/navigation/close | No hangs, parser work in callback, per-file/per-history wrapper growth or late successful mutation. Preserve negative timing records. |
| Physical source composition active; reader requests Grid action | No silent preedit commit/cancel/focus theft; explicit blocked command and recovery. Synthetic IME dispatch is not proof. |

A client requesting a cell outside the bounded Table must receive a local-range
failure, not data from another viewport. Navigate via the named Go-to/control
then reacquire the new window. Large-file tests assert bounded tree size even
when exact totals are millions, and verify a distant known marker after
navigation. They do not enumerate all file cells.

After API gates, add an attended Narrator/VoiceOver task extension to
[screen-reader-acceptance.md](screen-reader-acceptance.md): announce source versus
Grid; interact with Row 1001/Column 17; hear complete empty versus Missing and
Pending; select a rectangle; Go to a distant proved cell; hear correct absolute
coordinate/state; Reveal to source, read exact marker; return without lost
orientation; open/cancel Replace; confirm no unintended edit. Capture actual
Speech Recap/Caption Panel plus listener judgment. A UIA/AX pass does not count
as speech or keyboard/menu usability, nor does a successful native arrow key
rescue failed reader table navigation. Repeat with active real Pinyin only
after the small task works. No Narrator/VoiceOver pass is claimed here.

## 9. Production precedent and research implications

Chromium's current
[accessibility architecture](https://chromium.googlesource.com/chromium/src/+/main/docs/accessibility/overview.md)
serves synchronous platform requests from a consistent cached representation,
then publishes coherent updates/events. The applicable mechanism is one immutable
ready frame and bounded geometry, not copying Chromium's whole-tree IPC system
into a single-process editor. Its Mac
[external-facing table tests](https://chromium.googlesource.com/chromium/src/+/HEAD/content/browser/accessibility/browser_accessibility_cocoa_browsertest.mm)
check cell lookup and matching row/column ranges; mote should similarly test
relationships, not only a label. A production Chromium
[VoiceOver row-index defect](https://issues.chromium.org/issues/40711234)
shows that superficially correct roles can still yield silence. This is relevant
negative precedent, not proof that mote needs Chromium's TreeGrid structure.

Recent peer-reviewed research makes feedback/navigation evidence consequential:
Huq et al., **CHI 2026**, compare 31 mobile intervention papers with 20 study
sessions on four Android apps, finding feedback-related and more specific
user-experienced issues beyond the initial automated categories
([paper](https://doi.org/10.1145/3772318.3791293),
[author-hosted PDF](https://ics.uci.edu/~seal/publications/2026_CHI.pdf)).
The inference for mote is to test orientation after pending-to-ready, selection,
and Reveal, rather than treating role/label inspection as usability. These
mobile results do not certify desktop UIA/AX patterns or predict failure rates.

**AXNav, CHI 2024**, explores executing accessibility instructions through actual
assistive features and producing reviewable video; its study used ten
professional testers and mobile/iOS workflows
([paper](https://doi.org/10.1145/3613904.3642777)). It is a promising future
automation direction, not a deterministic desktop acceptance oracle. Mote's
first slice should use fixed fixtures, external identity assertions and actual
reader speech; a future agent-driven replay must preserve those oracles and
report inability/uncertainty, not generate a plausible narration. Earlier
**A11yPuppetry, CHI 2023** similarly motivates recording actual TalkBack actions
and failures rather than testing only touch/API hierarchy
([author project](https://seal.ics.uci.edu/projects/a11ypuppetry/index.html),
[paper DOI](https://doi.org/10.1145/3544548.3580679)).

The useful research question is narrow: **Does a truthful bounded table with
absolute ordinal headers and explicit logical navigation preserve reader
orientation as well as a fully virtual logical table, while keeping callbacks
and memory bounded on adversarial CSV?** Test task success, wrong-coordinate
errors, pending-state comprehension, navigation loops, command-to-first-speech
and memory across repeated distant windows. First establish task correctness;
do not run an expensive user comparison against a nonconformant fake full grid.

## 10. Integration and eventual full-file virtualization

### Implementable order, no userspace/data migration

1. Freeze internal frame/actions; add pure coordinate/state/selection invariants
   and adapter frame publication. Include complete selection mutations and the
   coordinated additive controller admission/completion seam; omit advertised
   source actions until it is ready. No public Engine/Formats/theme/config changes.
2. Implement Windows narrow Table fragment, complete bounded patterns, events
   and lifetime; externally falsify default proxy duplication and COM threading
   before default registration. Leave source provider untouched.
3. Implement Mac native-backed selectors; external tests decide if a bounded
   proxy is necessary. Execute selector ABI/lifetime on both AOT architectures.
4. Validate identical synthetic external matrix per OS; then actual reader task
   and source/IME regression matrix. Enable the coherent feature, not a partial
   pattern whose required members return placeholders/errors.
5. Record exact target evidence/scope in existing native/release docs. Grid-only
   detach/registration switch is rollback, never a claimed accessible fallback.

Native visual behavior, source editing, Copy payload/refusals, Replace one-Undo,
file encodings and `~/.mote` configuration remain established contracts. Standard
ListView/AX clients may have depended on incidental row-only structure; do not
retain wrong duplicate semantics for them. Document the new scoped Table
AutomationIds, verify native headers/focus/menu clients and test source provider
identity before adoption. Strict single-binary remains: OS libraries are platform
dependencies, no extra shipped accessibility helper/service/native sidecar.

### Separate future full logical-table stage

[UIA virtualization guidance](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-workingwithvirtualizeditems)
uses ItemContainer and VirtualizedItem, with non-realized items absent from the
normal tree and placeholders realized on demand. Such a token is not a decoded
cell, but it creates a new realization/lifetime contract. Do **not** add tokens
to the first bounded-window stage just to return something for every ordinal.

A full logical Table stage must separately establish:

- A defined non-guessing extent policy, including unknown-prefix operation and
  stable change of total; exact counts alone do not prove decoded cell readiness.
- Coordinate discovery and realisation through one resource-admitted serialized
  policy lane; synchronous OS APIs cannot wait for an unbounded scan.
- One coherent identity spanning realization without an old placeholder acquiring
  another document's cell; stale/cancel/retry semantics and outstanding-client
  lifetime/memory caps.
- UIA ItemContainer/VirtualizedItem/GetItem/ScrollItem behavior as a complete
  pattern; a matching AX design tested against actual VoiceOver rather than
  assuming the Windows virtualization protocol maps directly to AppKit.
- No synthetic offscreen values/origins and no file-sized child array; explain
  unready coordinate tokens as such, if admitted at all.
- Reader task evidence demonstrating that full virtual table navigation improves
  access over the bounded contract without source/IME regressions.

That migration changes Table scope and item indexing, so it is a versioned
accessibility interface decision with a stable separate AutomationId or explicit
replacement, not switching local indices to absolute indices mid-session under
the same retained object. Preserve the bounded navigator as a usable explicit
mode if needed. No grammar parser, on-disk cache or document-data migration is
required by either stage.
