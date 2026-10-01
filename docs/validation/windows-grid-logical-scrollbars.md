# Windows Grid logical scrollbar adapter

Date: 2026-10-01. Adapter implementation checkpoint; controller integration and
target CI are separate gates. No clipboard or desktop input mutation.

## Delivered contracts

- Owned sibling SCROLLBAR HWNDs publish logical row/column ranges independently
  of the bounded owner-data ListView. 32-bit SB_CTL/SIF_TRACKPOS is read for
  thumb actions; high-word message coordinates are never used.
- Range/page/position installation is read back. Zero position is valid.
  Empty/unavailable domains disable the axis without negative maximums.
- Begin captures controller authority and measured fully-visible geometry.
  Track/Commit/Cancel retain that token and frozen frame. Capture cancellation
  retires a token; SB_ENDSCROLL cannot manufacture a second terminal action.
- NativeGridPlanner.Slots preserves missing ordinal delivery as pending slots.
  Pending frames have no command identity. Exact certified column width clips
  materialized headers even when the format cache requested more columns.
- Fully visible rows use LVM_GETCOUNTPERPAGE. Horizontal geometry sums actual
  local column widths; pages are capped by the shared planner. Resize/header
  end-track notifications deduplicate before forwarding and tolerate synchronous
  controller range reinstallation.
- Competing local scrollbars and wheel/boundary requests are suppressed only
  when logical navigation is installed. Legacy no-navigation methods remain
  additive and pass their previous real-HWND tests.
- Page keys use logical measured pages. Arrow movement past the visible
  logical page becomes controller-certified navigation, not hidden local scroll.
  Range placement reveals local row zero, never an offscreen retained selection.
- End/F5 retry remain symbolic. Go-to captures authority before the text
  modal loop. Context menus do not begin/cancel navigation for ordinary
  source-backed Copy/Reveal/Replace commands.
- Generation guards abandon obsolete installation continuations after native
  callback reentrancy. Shell status includes the navigation status once, through
  existing persistent notice composition.

## Local evidence

Windows 10.0.26200, x64 process, .NET 10.0.11, Release.

Command:

    dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore -warnaserror --filter "FullyQualifiedName~NativeGridLogicalWindowsTests|FullyQualifiedName~NativeCsvGridWindowsTests"

Result: **16/16 passed** (7 new logical-range tests plus 9 existing native Grid
tests). New coverage: million-row native range/position readback, pending command
retirement, exact empty range, frozen token reuse through installation,
cancellation/no duplicate terminal, symbolic End/retry, column/page stepping,
sparse ordinal slots, exact-width header clipping, deduplicated reentrant
geometry, and nested newer native range installation.

The retained timing observation is synchronous hidden-HWND range installation
plus 32-bit range readback with 16 pending rows and 8 columns, stable geometry
and column definitions; 10 warmups and 100 measured repetitions:
**p50 0.2501 ms, p95 1.0284 ms**. Stopwatch measures callback/API completion.
This is not AOT startup, CSV parsing, end-to-end GUI response, physical input,
compositor paint, or a comparative performance improvement claim.

Actual desktop thumb dragging, externally observed accessibility, native popup
menu activation, and hosted target acceptance were not performed by this slice.
The menu cancellation fix has source review and retained identity/command tests;
there is no claim of automated actual TrackPopupMenu selection.

## Platform evidence reused

Microsoft documents the 32-bit versus 16-bit distinction and track-position
query in [GetScrollInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getscrollinfo).
Its [LVM_GETCOUNTPERPAGE](https://learn.microsoft.com/en-us/windows/win32/controls/lvm-getcountperpage)
contract counts only fully visible report-view items. These platform mechanisms,
rather than fabricated byte percentages or whole-file native row mirrors, implement
the architecture in [logical scrollbar design](../csv-grid-logical-scrollbar.md).
