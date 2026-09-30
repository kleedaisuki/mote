# CSV Grid: certified coordinates, bounded native tables

Status: **target design, not production implementation or native acceptance**,
2026-09-30. Inspected repository HEAD: `a9770a5`, plus the concurrently integrated
Native Flow contract. This document owns only the CSV Grid subgraph of
[native rendering](native-rendering-architecture.md). Existing
[CSV sparse projection](csv-sparse-projection.md),
[bounded index](csv-index-budget-design.md),
[oversized fields](csv-oversized-projection.md), and
[Flow installation identity](native-flow-implementation-contract.md) are the
starting point, not work to repeat. Historical statements in other design notes
that the retained-index change is unimplemented do not supersede its current
implementation/evidence document.

## 1. Decision and dominant workflow

**Add a CSV-specific, immutable bounded Grid projection on the serialized CSV
session lane. Render it with a Win32 owner-data report ListView and a programmatic
AppKit NSTableView. Keep Engine as the only text/Undo owner. Default Grid is a
read-only source-ordered table with cell/row selection, exact Copy commands and
explicit source reveal; it is not a spreadsheet or another editable document.**

The representative workflow is:

1. Open a CSV; source becomes editable without waiting for CSV validation or
   creation of a table-wide cell graph.
2. See bounded actual cells near the source position, logical record/column
   coordinates when proved, and honest indexing/diagnostic status.
3. Scroll the Grid independently, select a cell or rectangle, Copy decoded data,
   or reveal the exact field in source. Source decoration and detached Grid now
   have two genuine interests, serviced by one session update.
4. Edit source, or invoke a separately validated **Replace cell** command. The
   Engine transaction invalidates the old Grid map immediately; background work
   supplies a new immutable presentation. Save persists the Engine text exactly.

Fixed-height rows and bounded column windows serve ordinary CSV well and prevent
one multiline/giant field from laying out a million-pixel native row. A focused
cell detail panel shows bounded multiline content and exact state; oversized
content opens in source. No automatic dialect change, header assumption, type
coercion, sorting, filtering, formula evaluation, external-link opening, browser,
database import, runtime plugins, or generic widget hierarchy is introduced.
These are deliberate product semantics, not a temporary mock table.

```text
Engine snapshot v + committed edit chain
                |
                v
one serialized CsvIncrementalSession update
  private certified records / sparse ordinal checkpoints / giant fields
       |                        |
       v                        v
source-window tokens/errors    bounded Grid rows/cells/evidence
       +------------------------+
                |
                v
Native controller: request sequence + document stamp + selection
                |
                v
one installed NativePresentationId + ready immutable table cache
      /                                         \
Win32 owner-data ListView                 AppKit NSTableView
      \                                         /
  callbacks read ready cells only; misses enqueue bounded requests
                |
explicit stamped reveal / Copy / Replace cell command
                |
                v
controller validates current identity -> Engine source operation
```

## 2. The concrete missing information

Current `CsvIncrementalSession` implements `IWindowedFormatSession`, not a Grid
renderer. `WindowedAnalysis` truthfully supplies interval coverage, an exact
diagnostic total only for `Complete`, per-window indexing/truncation, and sparse
`SemanticNode("row")`/`SemanticNode("cell")` output. It caps delivery at 4,096 rows
and 8,192 cells; eight windows/512 Ki UTF-16 requested source width are accepted.
Its `SparseFull` checkpoints are **source offsets only**. Oversized-record
certificates retain sparse field-start offsets and giant-field spans, but no
public row/column ordinals. Fields longer than 64 Ki source units may be omitted
from decoded node children. `Complete` does not imply a full reusable row array,
a row count, a maximum width, or even that every cell in a returned row exists
in `Children`.

Therefore **do not build Grid by enumerating `row.Children` and using its list
index as the column number**. A giant first field followed by `,tail` can emit
only the tail cell; it remains column 1, not column 0. Neither subtracting source
offsets nor counting commas/newlines in Native fixes this: quotes permit embedded
commas and newlines. The policy must emit explicit coordinates and a descriptor
for an interested giant field even when its decoded value is unavailable.

The smallest useful private-index extension is:

- Dense segments carry/derive cumulative logical record counts, using existing
  shared row blocks rather than retaining a second per-row graph.
- Sparse certified record checkpoints become `(SourceStart, RowOrdinal)` pairs.
  They point only to grammar-proved record starts. Full validation also computes
  exact `RowCount` and `MaxWidth`; these two scalars do not require retained rows.
  A prefix supplies a certified count/lower bound, not an estimated final count.
- Large-record field checkpoints become `(SourceStart, ColumnOrdinal)` pairs;
  giant summaries include their column ordinal. They remain field starts, not
  arbitrary positions inside quotes. Count skipped fields during the existing
  authoritative scan and certify their ordinals at commit.
- Existing row widths, first-row expected width, error summaries and giant facts
  remain policy-private. Coordinate metadata participates in the same combined
  retained-index budget. Never add an `int[]` per source comma.

This makes bounded source-anchor and bounded ordinal requests equivalent ways to
find the same certified owners. It does **not** make a cold distant ordinal
request cheap: before the relevant current-version certificate exists, return
pending and let Full indexing continue off the UI thread. After an oversized or
sparse-cache edit, current production correctly drops old certificates and may
return a bounded provisional prefix; Grid must not resurrect them by shifting
old coordinates. Dense ordinary-record reuse remains available under its existing
boundary proof. Preserve the public `Analyze`, `AnalyzeWindows`, `IDocumentPolicy`
and existing HTML compatibility APIs unchanged.

## 3. Minimal typed Grid IR and request boundary

The following types are **illustrative additive contracts**, not declarations
already present in `RenderContracts.cs`. Formats owns them; Native types and
handles never cross into the policy assembly. Use dedicated enum/record types,
defensive copies and construction validation like `FlowRenderProjection`.

```csharp
/// <summary>A bounded coordinate range; End is exclusive and checked.</summary>
public readonly record struct GridRange(int Start, int Count);

/// <summary>How a requested field's displayed value was delivered.</summary>
public enum GridValueState { Complete, Clipped, Oversized, Pending, Missing }

/// <summary>One exact logical field; Missing/Pending have no invented origin.</summary>
public readonly record struct GridCell(
    int Column, TextSpan? SourceRange, TextSpan DisplayRange,
    GridValueState State, bool HasSyntaxError);

/// <summary>A grammar-proved logical record, excluding its line delimiter.</summary>
public sealed record GridRow(
    int Ordinal, TextSpan SourceRange, TextSpan RecordDelimiter,
    int Width, IReadOnlyList<GridCell> Cells, bool DiagnosticsTruncated);

/// <summary>Whole-file coordinate facts, distinct from semantic certification.</summary>
public readonly record struct GridExtent(
    int CertifiedPrefixRows, int? ExactRowCount, int? ExactMaxWidth);

/// <summary>A source-follow or explicitly indexed ordinal query, never both.</summary>
public abstract record CsvGridAnchor
{
    public sealed record Source(int Offset) : CsvGridAnchor;
    public sealed record Row(int Ordinal) : CsvGridAnchor;
}

/// <summary>One coalesced request on the document's existing CSV lane.</summary>
public sealed record CsvGridRequest(
    IReadOnlyList<TextSpan> SourceInterests, CsvGridAnchor Anchor,
    int RowLimit, GridRange Columns, AnalysisScope Scope);
```

The closed two-case anchor is not a plugin/type hierarchy. A tagged immutable
struct with validated factories is equally acceptable. `GridRenderProjection`
contains `Version`, `GridExtent`, semantic `Completeness`, certified source
coverage, nullable exact total diagnostics, the requested/delivered coordinate
ranges, a single bounded `DisplayText` arena, ordered rows/cells, exact source
diagnostics, and explicit `RowsTruncated`, `ColumnsTruncated`,
`DiagnosticsTruncated` and `ValuesTruncated` flags. These flags answer different
questions; do not collapse missing fields, unavailable rows and clipped text into
an ellipsis-only boolean. Pending extent/rows are status outside the data rows,
not invented logical records. `Column 1`, `Column 2`, ... are presentation labels;
the first record remains data unless the user explicitly enables a header view.

`SourceRange` is the **whole field syntax**, including surrounding quotes and
escaped quotes, even when display is clipped or decoded differently. Cell
navigation is item precision. `DisplayRange` indexes rendered text only and must
never be interpolated back to source. A true empty field has an exact zero-length
origin; an absent field has `Missing` and no origin. `Pending` means delivery is
not known yet, never an empty string. `Oversized` has a proved full field span but
no materialized decoded value. A clipped value carries its true origin and
explicit clipping state, not a false complete cell. Diagnostic ranges stay
exact source UTF-16 spans; gutter badges are not language diagnostics.

Construction invariants:

1. Every origin, extent, token and diagnostic belongs to one snapshot version;
   all source spans are in bounds and display spans lie in the bounded arena.
2. Rows increase by exact logical ordinal and source order. Cells increase by
   **actual** column ordinal; sparse delivery never renumbers them. Row width is
   exact when the record boundary was proved.
3. `Complete` means whole-document checks and exact diagnostic total, independent
   of all delivery flags. Only exact `GridExtent` permits final ordinal totals.
   No convex hull of disjoint certified ranges is advertised as checked.
4. `Missing` is permitted only when a proved row width excludes that column.
   Provisional recovery cannot invent row boundaries, widths or missing fields.
5. Decoded/visual text may replace source control characters with visibly labeled
   glyphs; it does not become clipboard data. No cutoff splits a surrogate pair;
   use grapheme boundaries for user-facing clipping where practical. Invalid
   source surrogate sequences get an explicit replacement-display state, not an
   `ExactText` claim or a source edit.
6. No projection retains a snapshot, CSV cache, parser cursor, whole-file string,
   Engine transaction, or platform handle. Only ready bounded immutable data can
   be read during native paint/accessibility callbacks.

### Session integration: one update, two different delivery interests

Add an optional CSV-specific `ICsvGridFormatSession.AnalyzeGrid(snapshot, edits,
request, cancellationToken)` returning a bundle of source `WindowedAnalysis` and
`GridRenderProjection`. Both derive from **one candidate cache state** and commit
only after projection succeeds and the final cancellation check. Internally reuse
the existing normalization/update/parser routines; do not call public Analyze
twice, create a second CSV session, or make Native reconstruct omitted fields.
The bundle is produced on `NativeFormatSessionDriver`'s existing serialized lane.

Source interests retain their existing eight-window/512 Ki limit. The Grid query
has separate row/column/payload/work budgets: **a 100 Mi field's origin is not a
request to read its entire 100 Mi span**. Adding that span to source interests
would defeat the oversized fast path. An ordinal query translates through
current-version certified checkpoints; a source query finds its containing
record. Project only selected columns and bounded neighbor records. Source
decorations can still use a disjoint source window. Deduplicate validation and
diagnostic ownership rather than summing window totals.

## 4. Native adapter and bounded scrolling

The same-version `NativePresentationId(Document, Sequence)` from Flow identifies
an installed Grid/cache/geometry/selection bundle. Source edit, format change,
viewport change, source-follow toggle, idle analysis, theme layout or DPI change
gets a new sequence. An old action cannot become valid just because the new view
has the same cell text. Native Flow remains intact; add a nullable Grid body or a
closed internal Flow/Grid discriminant rather than turning `IRenderFormatSession`
into a breaking universal renderer.

Suggested internal operations, owned by the native adapter/controller:

| Operation | Semantics |
| --- | --- |
| `InstallGrid(view, identity)` | UI-thread install of model, native row/column slots and maps; acknowledge identity only after successful install/readback. |
| `GridInterestChanged(anchor, columns, rowLimit)` | Coalesced intent; no parsing. Maintains source-follow or explicit detached position. |
| `GridSelectionChanged(identity, selection)` | One active cell plus a single rectangular anchor/active range, or a contiguous whole-row range. Native slot indices are translated to proved logical coordinates. |
| `GridCommand(identity, command)` | Explicit Reveal source, Copy value/cells/rows or Replace cell; controller resolves origins against the installed model. |
| `ReadReadyCell(localRow, localColumn)` | O(1) bounded cache lookup; returns ready text/state or Pending. It cannot block or scan. |

**Always bound native slots**, not just managed payload. Use a small table window
with overscan, not one native object per row/column. A logical navigator controls
source/row anchor and column range; native rows/columns map to this installed
window. While cold, display proved prefix/source positions and Previous/Next/
Follow source actions. Once exact counts are published, enable the logical row
scrollbar and Go to row/column. Preserve a source-backed row anchor and within-row
pixel displacement when rebasing a ready window; wheel/trackpad navigation is
continuous, not a modal page command. Pending rebase does not clear readable cells
or freeze source input. Status clearly labels the currently installed range.

This bounded-window rule avoids an actual limit counterexample: Microsoft's
`LVM_SETITEMCOUNT` caps virtual items at 100,000,000, while a 100 Mi UTF-16 file of
single LF records can contain 104,857,600 logical records. Unknown cold totals and
millions of source columns also cannot be safely handed to a native table as
complete extents. Logical navigation uses checked integer ordinals; native slot
counts stay small. An external native logical scrollbar, when enabled, reports
its extent honestly; never relabel a local table-window scrollbar as the whole
file. Its thumb-to-row mapping uses wide intermediate arithmetic. Windows can use
an OS scrollbar adjacent to the report control; macOS can use an NSScroller next
to the bounded table/clip view. Exact scroll plumbing and accessibility need target
probes, not an assumed cross-platform pixel equivalence.

### Windows

Create `SysListView32` in report/`LVS_OWNERDATA` mode from the beginning; Microsoft
does not support toggling owner-data mode dynamically. Supply `LVN_GETDISPINFO`
from the ready cache, honor cache hints by enqueueing **bounded** interests, and
keep header/focused-cell semantics independent of CSV parsing. Native column
slots map to `FirstColumn + localColumn`; no native column is created for every
comma. No automatic sorting style or source reordering. Avoid supported-message
assumptions: `LVM_GETITEMTEXT` and `LVM_SETITEMTEXT` are not owner-data storage.
Marshal callback strings with native buffer lengths, scalar-safe cutoffs and
lifetimes valid until callback return. Keep selection/paint suppression in bounded
UI transactions; restore state on failure. Use system comctl32, not an application
framework or shipped helper DLL. [Microsoft owner-data contract](https://learn.microsoft.com/en-us/windows/win32/controls/list-view-controls-overview),
[item-count limit](https://learn.microsoft.com/en-us/windows/win32/controls/lvm-setitemcount).

### macOS

Construct NSTableView, NSTableColumn, data source/delegate and reusable cell views
programmatically through the existing typed Objective-C bridge. Use bounded ready
data for row count/cell lookup, `makeViewWithIdentifier:owner:` reuse and targeted
row/column reloads. Objects/delegates stay alive for the installed view and are
released when retired; AppKit mutation/layout remains on the UI thread. Use
pointer-sized NSInteger/NSRange and correctly typed return signatures on both
macOS RIDs. No nib, app bundle, embedded web engine or native helper sidecar is
required by this design. [Apple NSTableView](https://developer.apple.com/documentation/appkit/nstableview),
[programmatic view-based table guide](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/TableView/PopulatingView-TablesProgrammatically/PopulatingView-TablesProgrammatically.html).

Neither control natively supplies a complete spreadsheet cell-selection model.
Keep one domain-owned active-cell/rectangle state with a small native paint adapter
for its visible intersection. Preserve native row-header selection as a distinct
whole-row mode. Arrow/Tab moves active cell; Shift extends the rectangle; clicking
row gutter selects rows. Enter/context Reveal source is explicit, while ordinary
click selects instead of unexpectedly taking focus away. Preserve the existing
Flow preview gestures; document the Grid-specific table interaction rather than
changing every preview's click behavior. There is no arbitrary disconnected
selection set or general selection framework.

## 5. Selection, Copy and source-backed editing

### Selection lifetime

Within one source version, a rectangle may outlive a window rebase: selection is
logical data, not a retained cell object. Refresh and re-resolve the active cell's
origin before an action. A source edit invalidates all old-version actions and
ends the old coordinate selection; recenter on a mapped source anchor or current
source caret after reanalysis. Do not preserve a row ordinal through a quote edit
that merges records. Theme-only reinstallation retains selection coordinates and
scroll if the semantic model is unchanged, with new identity. Closing/switching
documents retires selection, cache, pending requests and native children together.

### Copy is a data command, not a screenshot command

Commands have visible names and precise contracts:

- **Copy cell value** returns the complete decoded field, preserving actual tabs/
  CR/LF and leading `=` characters, subject to the explicit native text admission
  rules below. An existing empty cell deliberately publishes an empty text value;
  having no cell selection is instead a no-op. It never copies a displayed
  ellipsis, arrow-shaped newline glyph, Pending label or Missing placeholder.
- **Copy cells** emits decoded rectangular data as **quoted TSV**: tab delimiter,
  CRLF record separator, doubled `"` inside quoted fields; **always quote empty
  values**, and quote any value containing tab, `"`, CR or LF. This is a documented
  serialization convention, not a promise that every external spreadsheet
  implements the same TSV grammar.
  **Copy cells as CSV** uses the current comma/doubled-quote grammar and the same
  empty-value rule, additionally quoting commas. Join encoded records with CRLF
  and emit no synthetic trailing record delimiter. Thus a one-cell empty row is
  the two-character token `""`, and selecting rows `["a"], [""]` produces
  `a\r\n""`, not `a\r\n`. Consecutive empty rows each have an explicit quoted
  field token. A nonempty rectangular selection always has at least one field
  token per row; a zero-row selection is a no-op, not a zero-row serialized file
  published as if it were one empty row. No formula evaluation or silent apostrophe
  injection changes values. Explain spreadsheet formula risk in command help;
  an opt-in safe-export mode would be a distinct transformation, not default Copy.
- A rectangle containing `Missing` is not silently padded into real empty fields.
  Require the explicit **Copy with missing fields padded empty** variant or report
  that ordinary rectangular Copy cannot preserve ragged structure. Whole-row Copy
  is the lossless option.
- **Copy source rows** reads the complete contiguous Engine source interval,
  including exact record delimiters. It does not serialize decoded cells or
  borrow strings from the rendered cache. It preserves original text rather
  than re-encoding empty records; native text admission can still refuse it.

The explicit empty-token rule removes an EOF ambiguity, rather than adding a
special case only for the last row. The independent reviewer reproduced with
the current `CsvPolicy` that empty source has zero rows, `a\r\n` has one row,
and `a\r\n""` has two. The failure-directed probe is preserved in
`.temp/csv-grid-review/Program.cs`, `Probe.csproj` and `results.txt`; see the
[independent design review](reviews/csv-grid-architecture-review.md). The same
probe confirmed a quoted `a\0b` field is accepted as three UTF-16 units with zero
diagnostics, so grammar validation does not substitute for clipboard admission.
It did not mutate a native clipboard. The serializer's proof obligation is
stronger than this example: for any admitted finite rectangle with at least one column,
parsing its CSV serialization returns exactly the selected row count, width and
decoded values. Induct on the encoded field and CRLF-joined record grammar;
quoting every empty value ensures every record has an explicit token, including
the final empty one. The quoted TSV contract uses the same proof with tab as
delimiter and a matching test parser; do not use the comma-only `CsvPolicy` as a
TSV oracle.

**Native plain-text Copy rejects embedded U+0000 in every mode on both platforms.**
Microsoft specifies that a null character ends `CF_UNICODETEXT`; a length-tagged
managed string or a bigger allocated buffer does not make `a\0b` a complete
portable clipboard value. Validate the fully prepared decoded/serialized/source
payload before any clipboard clear, ownership change or write. If any NUL is
present, report that native text Copy cannot represent this selection losslessly,
leave the clipboard and Engine source unchanged, and offer Reveal source/normal
Save. Do not copy the prefix, drop NUL, replace it with a visible glyph, turn it
into literal backslash-escape text, or call that successful lossless Copy. Quoting
a CSV field does not solve NUL termination. Applying the same refusal on macOS
keeps the cross-platform command contract simple; it is not a claim that every
AppKit string representation is NUL-terminated. No private binary clipboard
format or generic export subsystem is required to preserve the dominant text
Copy use case. [Microsoft standard clipboard formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/standard-clipboard-formats).

Selections can exceed displayed rows or include Clipped/Oversized cells. Schedule
full Copy decoding from an immutable snapshot on the serialized policy lane;
freshly validate the relevant owners and identity. Keep source editing responsive,
show progress/cancel for large work, and never complete with a silently truncated
payload. Copy decoding is a non-committing read operation: it cannot replace the
session's current analysis cache with an older snapshot or consume the driver's
edit chain. It uses matching current-version certificates or a locally validated
bounded/explicitly admitted read, and checks identity again before publication.
Clipboard APIs need a materialized payload: streaming parser work does
not imply constant clipboard memory. Default Copy admission budget: 8 Mi UTF-16
output units; reject with a clear size notice and leave the clipboard unchanged
if exceeded. A future documented `~/.mote` limit override may deliberately allow
larger copies, with explicit memory-cost confirmation and hard checked platform
limits; this is proposed configuration, not an existing key. At final clipboard
publication reject a superseded source version/selection. NUL, budget, stale
identity, canceled work and other preparation failures occur **before native
mutation**, so they preserve the old clipboard. Once a native publication begins,
an OS failure must be reported distinctly; do not promise transactional restoration
of every prior clipboard format. Windows `EmptyClipboard` clears prior data and
`SetClipboardData` can subsequently fail. Prepare/allocate first, minimize the
mutation interval, and never label a failed publication successful Copy.
[EmptyClipboard](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-emptyclipboard),
[SetClipboardData](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setclipboarddata).
Snapshot retained for the job is released on finish/cancel. No parser/clipboard
operation runs inside native display or accessibility callbacks.

### Editing without two sources of truth

Reveal source selects the whole exact field syntax, focuses the source editor,
and uses Engine selection/viewport mechanics. A zero-length existing field is a
valid insertion caret; a Missing/Pending placeholder is not an editable origin.
This provides format-aware editing now without guessing decoded glyph offsets.

For an explicit **Replace cell value** product command, the policy supplies a
versioned field replacement, not a mutable Grid cell. Admit only a proved field
boundary with no local syntax errors and a complete bounded decoded value;
`CSV004` width warnings elsewhere do not automatically forbid it. Settle source
IME before opening/committing the value editor. Use a temporary native text field
for the entered value; on accept the CSV policy encodes it by preserving existing
quote style when possible, quoting when comma/quote/newline requires it, and
doubling quotes. Replace only the field span in one Engine transaction, retaining
all other fields, whitespace and original record delimiter. Entering an empty
value replaces a field; it does not delete the column. Cancel has no Engine
effect. Revalidate stamp/span on accept; stale edits fail clearly instead of
applying to a shifted neighbor. Undo/Redo operates through Engine exactly once.
Malformed, giant or unavailable fields direct editing to source. Do not silently
expand a short row with commas, perform multi-cell paste, or normalize the entire
CSV as a side effect of one cell edit. Larger structured edit operations require
their own explicit transactional contract, not conditional branches in cell paint.

## 6. Accessibility is part of the table model

Keep the source document and rendered read-only Grid as **distinct labeled
surfaces**, not duplicate editable documents. Cell accessible names expose
logical row/column, full bounded value or explicit clipped/oversized/pending/
missing state, syntax/width warning state, and available Reveal/Copy action.
Decorative glyphs and omission notices must not masquerade as data cells. The
focused cell, row-selection mode and rectangle must agree with painted selection.

Windows must inspect the actual owner-data ListView proxy before claiming Grid
support. Standard report rows alone do not prove individual cells, GridItem
coordinates, rectangular selection or offscreen access. If the proxy cannot
represent the chosen semantics, expose a narrow table-specific UIA fragment from
the Grid model using the project's AOT-compatible COM/provider approach. Suppress
duplicate native fragments just as the source input host avoids duplicate
documents; do not layer two conflicting tables. A DataGrid claim requires Grid
plus applicable Selection/Scroll/Table patterns, cell GridItem and corresponding
events, not just an AutomationId. A virtualized full logical table must support
ItemContainer/VirtualizedItem semantics; a bounded-window table must instead
clearly label its range and accessible navigation controls. Do not invent an
unknown row count, silently return cell 0 for an offscreen request, or block a COM
call on CSV Full. Stale elements return explicit element-unavailable outcomes.
[Microsoft DataGrid requirements](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-supportdatagridcontroltype),
[virtualized item contract](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-workingwithvirtualizeditems).

macOS uses NSTableView's accessibility first, but verifies cell labels,
`accessibilitySelectedCells`, visible rows/columns, selected rows, logical versus
local coordinate announcements, and source-reveal actions. Add a narrow table
accessibility adapter only where the native behavior cannot expose the installed
window/selection truth. Send appropriate selected-rows/cells and layout updates
after atomic installation; retained objects must not expose another version's
values. [Apple table selection/accessibility surface](https://developer.apple.com/documentation/appkit/nsaccessibilitytable/accessibilityselectedrows%28%29?language=objc).

An automated tree/readback pass is not Narrator or VoiceOver acceptance. Both
readers must navigate ordinary cells, multiline/clipped/giant states, a detached
window and source reveal; hear useful coordinates; copy the intended value; and
return to the source without losing focus or a live input composition.

## 7. Budgets, scheduling and failure behavior

These are initial **acceptance budgets to measure**, not measured Grid results.
Current parser evidence is separate: the existing 100 Mi quoted warm two-window
session median is 0.0257 ms/35,328 allocated bytes/zero scanned source units; cold
Full remains approximately 292 ms/~420.7 MB cumulative allocations in one managed
Windows experiment. None establishes native paint, memory peak or input latency.

| Resource | Grid target | Why / failure |
| --- | --- | --- |
| Native/managed delivered rows | Default viewport + modest overscan; maximum 256 per prepared Grid | Fixed-height bounded layout; reduce delivered rows with visible pending state when payload caps bind. |
| Native column slots | Maximum 64 including overscan, plus separate row gutter | Wide comma runs cannot allocate millions of headers; explicit ordinal navigation. |
| Delivered cells | Maximum 8,192 per prepared Grid; enforce row × column admission before work | Reuse existing policy cell cap, not unbounded Cartesian expansion. |
| Display text arena | Maximum 64 Ki UTF-16 total, at most 1,024 units per displayed field | Detail/source action for omitted value; scalar-safe clipping, explicit state. |
| Source windows/work | Existing 8/512 Ki union; checkpoint lookbehind charged separately under current certified replay rules | Never add giant cell span as raw delivery interest. Bound ordinary-field overshoot and poll cancellation. |
| Retained policy index | Existing combined 32 MiB structural target, charging new ordinal scalars/checkpoints | Dense-to-sparse switch during scan; no post-hoc allocation cap. Validate reachable state/transient costs separately. |
| Prepared UI versions | Installed + at most one latest prepared model | Coalesce obsolete interests; no queued viewport backlog. |
| UI callback | Cache lookup/copy only; target p95 <1 ms, zero parser scans | A miss is Pending + one coalesced request, not synchronous I/O. |
| UI install | Target p95 <8 ms under ordinary viewport/selection fixtures on target machines | Report slow target case; tune bounded slots/payload rather than claim parser time proves smoothness. |
| Clipboard | 8 Mi UTF-16 admission default; async prepare with explicit failure/cancel | No silently partial success; materialization/RSS measured separately. |

Do not continuously rerun whole-file Full on every wheel event. Source input has
priority; CSV analysis and rendering share one serialized lane and latest-intent
coalescing. Cooperative cancellation must interrupt obsolete Full jobs at parser
polls. Idle Full remains useful for exact global diagnostics and coordinates, but
its cold allocation cost is unresolved; reduce it with failure-directed profiling,
not a speculative background thread pool. Themes provide data roles for table
foreground/background, borders, selection, pending and warnings through the
existing policy composition; this design creates no new theme mechanism.

Recoverable limits/cancellation yield truthful unavailable/truncated states,
not language errors and not dirty source. On native install failure keep the
previous bounded readable presentation only if clearly stale, invalidate its
actions and offer selectable source or the existing bounded textual preview.
Do not falsely label that fallback a successfully installed Grid. A format change
recreates the appropriate surface when owner-data mode requires it, preserving
source/file lifetime. OOM/ABI/control failure must not discard Engine text or
clear dirty state. No diagnostics, clipboard text or cell contents enter telemetry;
content-free timings/counts, if enabled, follow the existing `~/.mote` conventions
and user directory overrides.

## 8. Counterexamples that drive the representation

| Input/interaction | Incorrect shortcut | Required outcome |
| --- | --- | --- |
| `"first\r\nsecond",x` | Physical line = table row | One logical row; newline is field data; bounded display/detail and exact field source. |
| 100 Mi quoted field followed by `,tail` | Enumerate emitted children to number columns | Giant descriptor at column 0, tail at column 1, no giant-body decode on warm render. |
| `a,b,c\r\n1,2\r\n` | Missing third field = existing empty field | Width warning; Missing has no source origin; no implicit comma insertion/ordinary padded Copy. |
| `a,` at EOF | Zero-length span means no field | Existing second empty field with exact EOF insertion origin. |
| First row edited from width 2 to 3 | Reused row boundaries mean reused validation | Offscreen width diagnostics/count change against current first-row width. |
| Closing quote removed above Grid | Shift old row/column map by edit length | Old actions disabled; current provisional/pending grid until grammar proof is renewed. |
| Quoted `"a""b"` or `"😀"` | Display offset = source offset | Item-origin selection; escaped quoting and UTF-16 seams remain correct. |
| 100 Mi newline records / comma-only row | One native item/header for every record/field | Bounded local slots; exact logical navigation only with separately proved extent. |
| Same source version, scroll then activate old cell | Version alone identifies view | Reject old PresentationId sequence; no neighbor navigation. |
| Copy selected clipped/oversized value | Clipboard = rendered arena | Full bounded admitted decode or explicit preparation refusal; old clipboard unchanged on refusal, native publication failures reported separately. |
| Single-column selection ends in an empty row | Empty value can be an absent final token | Always encode empty fields as `""`; `["a"], [""]` serializes as `a\r\n""` and round-trips as two rows. |
| Source or decoded field contains `a\0b` | Allocated length preserves native text after NUL | Reject all native text Copy modes before mutation on both platforms; preserve old clipboard and full source, with explicit notice. |
| Provisional tail is not reached yet | Empty Grid means valid empty file | Pending/status; no invented row count or zero-diagnostic assertion. |

## 9. External evidence and what transfers

Native owner-data/data-source tables are established production mechanisms for
retaining only necessary display data. Microsoft's cache hints and Apple's reuse
API justify a ready bounded cache and platform adapter, **not** synchronous CSV
fetch in callbacks, full spreadsheet semantics, automatic accessibility fidelity,
or a measured AOT latency claim. They reduce mechanics that mote must own; the
format policy still owns grammar, origins and evidence.

[Pollock (PVLDB 2023)](https://www.vldb.org/pvldb/vol16/p1870-vitagliano.pdf)
formalizes structural pollution and evaluates loading robustness. Use its grammar-
perturbation approach to construct renderer/parser differential fixtures, including
quote/newline/delimiter corruption, while preserving a separate invalid-source
oracle. An editor must display and save malformed source instead of silently
cleaning it to match a loader's repaired table. The
[2025 DuckDB author evaluation](https://duckdb.org/2025/04/16/duckdb-csv-pollock-benchmark)
is useful current production follow-through, not an independent editor correctness
benchmark; do not import its scores as mote claims.

[García, Data Science 2024](https://journals.sagepub.com/doi/abs/10.3233/DS-240062)
investigates dialect detection using table uniformity and field type evidence.
The reported average 93.38% accuracy over 548 research files is evidence for useful
**suggestions**, not an exact parser certificate. Keep any future dialect/header/
type inference visibly tentative and user-overridable; it must not change the
current comma grammar or Save bytes automatically. The preprint's smaller-dataset
100% result is not the final journal result. Production
[DuckDB detection](https://duckdb.org/docs/current/data/csv/auto_detection) likewise
uses sampling and explicit option overrides; databases' desired typed imports
are not this editor's default source-preservation contract.

[The FastLanes File Format (PVLDB 2025)](https://www.vldb.org/pvldb/vol18/p4629-afroozeh.pdf)
connects random-access costs to the granularity of retained metadata and decoded
blocks. The transferable design question is: what is the smallest certified unit
needed to fetch a visible value without decoding a giant predecessor? Here that
unit is a CSV record/field-start certificate, not a row group or compressed
database table. Its random-access comparison includes first-row queries and
block-decoding assumptions; its impressive ratios are not bounds for arbitrary
editable CSV seeks. Do not add a compressed sidecar or analytical engine simply
because a storage-format paper demonstrates fast retrieval.

The worthwhile next research question is concrete: **Can current-version ordinal
metadata turn every warm source/row/column viewport into bounded grammar-proved
delivery without increasing reachable index state beyond its existing budget?**
Test source-anchor versus ordinal differential equivalence, field-checkpoint
spacing, wide-row/giant-field distributions and edit invalidation. The claim is
not O(1) arbitrary access or sublinear cold validation; it is bounded warm delivery
after a separately measured proof-building scan.

## 10. Implementation order and acceptance gates

This is an integration sequence for the complete subgraph, not an MVP reduction.
Each slice has a discriminating completion gate; do not repeatedly re-run already
cleared slices unless a later failure points back to them.

1. **Formats coordinates and Grid contracts:** add privately charged ordinal
   summaries/checkpoints and bounded typed projection; preserve old session APIs.
   Differentially compare emitted ordinals, decoded complete values, exact spans,
   missing/empty state, diagnostics/totals and completion to a fresh CsvPolicy
   oracle for the same snapshot. Add giant-field placeholder, omitted-first-field,
   first-row dependency, malformed in-progress quotes, CR/LF/CRLF, escaped quotes,
   zero-width EOF, sparse windows, cancellation and missing-edit-chain families.
   Random source-anchor and ordinal requests must identify identical owners.
2. **Controller/driver/state:** one serialized candidate update for source+Grid;
   same-version sequence rejection, close/switch race, stale Copy/edit refusal,
   source-follow/detached-interest fairness, source composition settlement,
   viewport rebase and budget status. Assert unchanged source/dirty state/Save hash
   after all read-only operations. Cell replacement is one Engine Undo transaction
   and preserves neighboring syntax/newline bytes.
   Copy serialization round-trips zero-selection/no-op, a single empty row, empty
   final/consecutive rows, one/two-column rectangles, embedded tabs/CR/LF/quotes,
   and Missing padding only through its explicit command. Use fresh CsvPolicy for
   CSV and a matching quoted-TSV test parser. NUL at start/middle/end, including
   offscreen NUL in a Clipped/Oversized value, must refuse cell/rectangle/CSV/source
   Copy before any adapter clipboard mutation; verify unchanged sentinel clipboard,
   unchanged Engine text and a visible failure notice. A size-rejected or canceled
   job cannot claim NUL validation or successful Copy merely from its visible prefix.
3. **Windows real HWND adapter:** test report creation/owner-data notifications,
   O(1) cache miss behavior, callback buffer lengths/lifetimes, Chinese/emoji/bidi,
   fixed-height multiline display, row/cell/rectangle selection, selected Copy
   payload, column-window mapping/rebase, exact source reveal, same-version stale
   event, theme/DPI/font/high contrast and resize. Read back actual displayed
   contents/selection; screenshot/style-only evidence is insufficient. Keep all
   harness artifacts inside root `.cache/` or `.temp/`.
   Read back admitted empty-row CSV payloads through actual CF_UNICODETEXT and
   reparse them to verify final row preservation. Exercise embedded-NUL refusal
   through the production command path, not only a managed-string unit test;
   instrument that no EmptyClipboard/SetClipboardData call occurs on refusal.
   Inject native publication failure separately and require an honest error,
   rather than an unsupported whole-clipboard atomic rollback assertion.
4. **macOS real AppKit adapter:** identical semantic tests on x64 and arm64 through
   actual NSTableView/data-source callbacks, reused-view reset, typed ABI, object
   lifetimes, copy/pasteboard data, trackpad/keyboard navigation and focused source
   reveal. Verify command modifier flags in synthetic events; prior Mac preview
   tests found inherited Command state, so do not treat default CGEvent flags as
   physical keyboard truth. No persistent input-source mutation is part of Grid
   acceptance; real Pinyin remains its separately controlled acceptance task.
   Repeat empty-row serialization/pasteboard readback and the shared NUL refusal
   contract through the production command path; refusal must not clear/write
   NSPasteboard or modify source, even if AppKit could store the original NUL.
5. **Independent external accessibility:** inspect one labeled read-only table and
   one logical editable source, logical coordinates and cell states, stale handles,
   visible/offscreen navigation, selection events, no duplicate fragments and
   bounded provider requests. Then execute actual Narrator/VoiceOver reading/
   copying/reveal workflows. Automatable tree success and reader speech are
   separate evidence.
6. **Target Native AOT performance/distribution:** GitHub Actions tests/builds for
   win-x64, win-arm64, osx-x64, osx-arm64; actual native runtime probes on available
   corresponding OS/architecture, marking unavailable execution honestly rather
   than treating cross-publish as runtime acceptance. Enforce one delivered mote
   binary with only OS-provided dynamic dependencies, no app/nib/helper/library
   sidecar. Record startup/source-ready, warm row/column scroll and Apply→Visible
   p50/p95, UI callback/install p95, scans, allocated bytes, retained index,
   native slots, peak process memory, executable size, and idle Full time/allocation
   separately for 1/10/100 Mi ordinary, giant quoted and comma-run fixtures.
   Include cold distant requests and >100-million-record extent fixtures without
   allocating that many UI nodes. No proven existing one-window or input-host
   regression is accepted without a measured benefit/explicit rationale.

**Open implementation decisions are testable:** native table selection/accessibility
may require a narrow adapter; continuous window rebasing/NSScroller geometry must
be probed; ordinal checkpoint charge and giant-field bounded seek must be measured;
clipboard interoperability with quoted multiline TSV needs target clients.
Resolve these with small production-path probes, then integrate. They do not
justify a speculative cross-platform widget framework or a claim that Grid is
already delivered.
