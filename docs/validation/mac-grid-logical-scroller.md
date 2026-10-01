# macOS owned logical CSV Grid scrollers

Date: 2026-10-01. Status: implemented; portable part-mapping tests and Release
compilation passed on Windows. AppKit runtime, both macOS Native AOT targets,
preferred overlay visibility, physical trackpad/knob interaction and external
accessibility acceptance remain pending. This does not claim compositor paint
or user-input latency measurements.

## Adapter contract

`MacCsvGrid` owns two `NSScroller` objects outside its bounded `NSScrollView`.
The scroll view's competing local scrollers are disabled. AppKit supplies the
preferred scroller style and the correctly typed CGFloat width. Separate
`setKnobProportion:` and `setDoubleValue:` calls install the certified logical
axis. The native table never acquires a file-sized height or row array.

A scroller subclass freezes controller admission before calling AppKit's
synchronous `mouseDown:` tracking loop. Every target/action Track and final
Commit uses the captured gesture Id and denominator. Epoch invalidation clears
the captured gesture before cancellation dispatch and keeps a separate tracking-
active guard: remaining native callbacks cannot borrow a newly admitted token.
Terminal dispatch clears the token before reentrant controller installation.

Page geometry is measured from the native clip and row pitch, with resized
column widths and intercell spacing. A container resize override and table
column-resize notification publish deduplicated admitted page pairs only outside
partial installations. The shared planner enforces row/column/cell caps.

Pending installations create the bounded requested ordinal slots with null
`GridRow` descriptors and pending text, plus requested-column headers; they do
not empty the table or invent source origins. Ready, pending, navigation-only
and Clear entry points advance a monotonic installation serial before any native
or controller callback. Every native column mutation, reload, clip movement,
selection and detail/selection-color publication checks ownership afterward;
a superseded outer finally cannot reset the newer installation guard.

Installed column objects and user-resized widths are reused when the requested
column start/count is unchanged. A changed column window rebuilds only the
bounded native columns. No file-wide column-width state is retained.

Sparse delivered records occupy their requested ordinal slot, not the next
available compacted row. Null slots show pending text and cannot produce a
source command. A rectangle containing a pending record is refused. Selection
restoration requires both the retained row and retained column to be in the
installed window; no off-window selection can select a different column or
force a native auto-reveal. The optional navigation arguments preserve existing
standalone clipboard-probe constructor and Install call shapes, including fresh
legacy initial selection where no navigation frame exists.

Wheel deltas accumulate fractional logical units; nonfinite values are refused.
Page keys, Home, symbolic End, numeric Cell and Retry enter the same admission
path. Context-menu opening freezes the navigation frame and measured pages, not an
admitted token. Actual numeric navigation admits against that frozen opening
frame before modal input; End/Retry admit only when invoked. Dismissing a menu
or invoking a source-backed Copy creates no unused navigation authority.
Vertical arrow selection rebases the logical viewport when it leaves the
measured visible page instead of forwarding to native hidden-cache auto-scroll. Source-follow continues using the existing source-anchor request.

## Verification performed

- `dotnet build src/Mote.Native/Mote.Native.csproj -c Release --no-restore -warnaserror`:
  succeeded, zero warnings/errors after integration with current shared edits.
- `dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore --filter
  FullyQualifiedName~MacGridScrollInteropTests --logger
  "trx;LogFileName=mac-grid-scroll-interop.trx" --results-directory
  .cache/mac-grid-scroll-tests`: 11/11 portable tests passed. Covers actual AppKit
  part ordinals, finite normalized endpoints, invalid parts, repeated page
  accumulation, integer-limit saturation, and bounded origin-free pending ordinal slots. No AppKit calls are made by these
  tests.

## Sources and remaining acceptance

Apple's [NSScroller](https://developer.apple.com/documentation/appkit/nsscroller)
and [Part documentation](https://developer.apple.com/documentation/appkit/nsscroller/part)
name the control semantics; the generated upstream
[objc2 AppKit binding](https://docs.rs/objc2-app-kit/latest/i686-unknown-linux-gnu/src/objc2_app_kit/generated/NSScroller.rs.html)
records the native part ordinals (NoPart=0, DecrementPage=1, Knob=2,
IncrementPage=3, DecrementLine=4, IncrementLine=5, KnobSlot=6). These are not
Windows scroll-command constants.

Target-host acceptance must exercise both preferred scroller styles, line/page
and knob actions, resize during tracking, pending-to-ready same-epoch install,
source/version replacement during tracking, sparse row gaps, distant >65,535
coordinates, symbolic End certification and resource-deferred Retry. Neither
portable arithmetic tests nor cross-compilation substitute for those checks.
