# Native CSV Grid: macOS adapter and evidence

Date: 2026-09-30. Implementation available in the working integration tree;
**AppKit target execution has not yet been performed by this Windows worker**.
A successful local managed build is not macOS ABI, GUI, VoiceOver, physical input,
or Native AOT acceptance.

## Delivered behavior

- `MacCsvGrid` uses a genuine programmatic view-based `NSTableView` inside the
  established native preview split. Source NSTextView/canvas and existing Flow
  NSTextView remain alive. The table is read-only and its reusable NSTextField
  views never own Engine text or Undo.
- Only immutable ready Grid IR is read by native data-source/paint callbacks.
  Installed display strings are bounded by at most 256 rows / 64 requested
  columns / 8,192 descriptors / 64 Ki display arena. AppKit reuses views with
  `makeViewWithIdentifier:owner:`. No parser, file I/O, source snapshot or whole
  document scan occurs in callbacks. Fixed-height rows and bounded detail avoid
  huge multiline field layout. Pending, Missing, Oversized, Clipped and errors
  remain explicit display states, never clipboard values.
- Row selection uses native NSTableView input; left/right chooses actual column;
  Shift input builds a bounded rectangle. Selected fields, not a misleading whole
  row, use the theme's selection background/foreground. Select All selects only
  the current certified bounded window. A native context menu exposes decoded
  value, CSV, quoted TSV, exact records, exact field syntax and explicit padded
  CSV copy modes. Command-C means decoded value for one field / quoted TSV for a
  rectangle. Return/double click reveals source; Command-Return requests Replace.
- Every command/window request carries installed `NativePresentationId`. Menu
  opening freezes identity and selection before its nested event loop; menu
  actions cannot rebound to a newly installed map. Reveal/Replace/CopyValue/
  CopySource use the frozen active field, other modes retain frozen rectangle.
- Page Up/Down requests adjacent bounded rows. Option-left/right requests adjacent
  bounded columns. Wheel/trackpad vertical movement at the table's current edge
  also requests adjacent rows; dominant horizontal swipe movement at a horizontal
  edge requests adjacent columns. Pending momentum requests are coalesced.
  Explicit Follow source and Go to row:column menu actions freeze opening identity;
  the numeric prompt validates positive one-based coordinates against frozen known
  exact extents and checked bounded ranges. Unknown distant coordinates request a
  bounded window, not an invented full table. Exact EOF
  facts prevent invented terminal row/column requests. Native callbacks never
  invent missing-field origins or renumber sparse cells.
- Same-document identity/theme updates preserve logical row/column/rectangle
  coordinates when the selected rows/columns remain in the delivered window.
  Same-stamp source page invalidation retains snapshot-free logical selection
  coordinates while disabling stale commands. A changed document stamp clears
  them. A nonoverlapping requested Grid window starts selection at its first ready
  record; selection gaps are not represented as actionable unseen cells.
- Grid Cut and Paste are explicit no-ops (keyboard and responder actions); Edit
  menu Cut cannot delete a prior source selection while the table owns focus.
  Grid-focused global Select All/Copy are routed to Grid, not hidden source.
- Replacement uses a temporary plain multiline NSTextView in NSAlert. Initial
  UTF-16 / CR / LF / CRLF roundtrip is checked before display. Smart quote/dash/text
  replacement and independent native Undo are disabled. Command-Return explicitly
  accepts, Escape cancels. Source transaction admission remains controller/policy
  owned and rechecks identity after the modal prompt.
- Table and reusable cells have non-content table AX identity and cell labels with
  actual logical row/column/value state. No external VoiceOver acceptance is claimed.

## Verification actually performed

`dotnet build src/Mote.Native/Mote.Native.csproj -c Release --no-restore
-warnaserror -v minimal`: **succeeded, 0 warnings / 0 errors** after final adapter,
replacement editor and disposable probe changes. Executed on Windows; native
AppKit imports and callbacks were compiled, not invoked.

`MacCsvGridProbe.Run()` is ready for opt-in target execution through the parent's
Program route. It creates a fresh shell and an in-memory certified fixture. It
checks genuine table counts, reusable read-only field/string/state/AX readback,
direct native NSEvent keyDown intent delivery, rectangle TSV intent, same-document
selection identity replacement, frozen context-menu identity/active-versus-rectangle semantics,
Edit menu Grid focus routing, frozen Follow source request, pure numeric navigation
validation, Cut/Paste no-op, exact bounds, temporary editor
CR/LF/supplementary-character roundtrip, immediate source invalidation and return
to Flow. It does **not** read/write NSPasteboard, mutate files, change input sources,
request TCC permissions, send external input, or access network.

Remaining acceptance: both macOS RIDs must run the probe after safety review.
The numeric navigation modal prompt is not opened by this safe probe.
Physical keyboard/mouse/trackpad, menu interaction across an actual nested event
loop, multiline modal editing/IME commit, scrolling boundary behavior and
VoiceOver require additional target/product evidence. Direct NSEvent dispatch
only proves in-process AppKit event routing, not physical input or input methods.

## ABI and lifecycle

NSInteger/NSUInteger and object arguments are pointer-sized on supported 64-bit
Macs; CGPoint/CGRect/CGFloat use typed aggregate/scalar signatures. Callback
encodings include `q` for rows and `@@:@@q` for the view-returning delegate.
Superclass key/mouse/wheel dispatch uses `objc_msgSendSuper`. All managed native
entrypoints catch errors rather than unwinding through AppKit. Only the UI thread
uses the instance map. Disposal detaches delegate/data source/target/menu delegate,
removes managed lookups, then releases owned objects. No library/helper sidecar is
introduced; AppKit and Objective-C runtime remain operating-system dependencies.

## Primary references

- Apple, [Populating a Table View Programmatically](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/TableView/PopulatingView-TablesProgrammatically/PopulatingView-TablesProgrammatically.html).
- Apple, [tableView:viewForTableColumn:row:](https://developer.apple.com/documentation/appkit/nstableviewdelegate/tableview(_:viewfor:row:)?language=objc).
- Apple, [makeView(withIdentifier:owner:)](https://developer.apple.com/documentation/appkit/nstableview/makeview(withidentifier:owner:)).

These production AppKit mechanisms implement the repository's typed/bounded IR
contract; this adapter does not introduce an independent academic framework or
claim a new parsing/incremental-computation result.
