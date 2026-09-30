# Native CSV Grid Windows implementation evidence

Date: 2026-09-30. Scope: `WindowsCsvGrid`, `WindowsGridInterop`, Windows shell
integration and the optional multiline `Win32TextPrompt` mode. This is bounded
native adapter implementation evidence, not product-wide CSV or accessibility
acceptance. No commit/stage/push was performed by this worker.

## Contracts implemented

- The platform `SysListView32` uses `LVS_REPORT | LVS_OWNERDATA`. Its item count
  is the delivered immutable `GridRenderProjection.Rows.Count`, never an
  estimated or exact whole-file extent. The projection admits at most 256 rows,
  64 data columns and 8,192 fields; one additional native ordinal gutter does not
  add source cells or a second document graph.
- `LVN_GETDISPINFOW` reads only the installed ready projection. It neither parses
  CSV nor accesses Engine, opens files, decodes source or synchronously performs
  a missing-data request. Labels distinguish missing/pending/oversized/clipped
  states. Caller-owned buffers are NUL-terminated with scalar-safe UTF-16 cutoff;
  no pointer is retained. Owner-data find requests return `-1`, not a fabricated
  match or an expensive synchronous search.
- Pointer hit testing uses actual native row/subitem coordinates. Arrows,
  Shift-arrows/Shift-pointer rectangles, row-gutter selection, Return/Space
  reveal, F2 Replace, and default Copy emit stamped logical coordinates.
  Reveal/Replace/CopyValue/CopySource use the active cell, not a shifted
  rectangle's anchor. Rectangular default Copy requests quoted TSV; selected
  gutter rows request exact source-row Copy.
- Context menu distinguishes decoded value, quoted TSV, CSV, padded CSV, exact
  cell syntax, exact rows, Reveal, Replace and Go to row:column. It captures the
  exact identity and selection **before** the popup's nested event loop. Commands
  are not rebound to newly installed same-version data after popup dismissal.
  The controller remains responsible for stale rejection and clipboard authority.
- Edge arrows/page keys, wheel-edge input and both native scrollbar edges
  coalesce bounded overlapping ordinal/column window requests. Go to row:column permits distant coordinates without a
  whole-file native mirror. Modal coordinate requests keep their pre-prompt
  identity; a stale prompt cannot select a new cell silently.
  The explicit Follow source selection context action also carries the identity
  and column interest captured before opening its menu.
- Same-document replacement keeps logical selection across same-version theme
  and viewport installs, including off-window anchors. Off-window Copy is sent
  with its actual coordinates for the controller to refuse, not clamped to a new
  neighboring row. A pending keyboard target selects only when delivered ready.
- Grid-focused shell Cut refuses without flushing or deleting the previous
  source selection; Copy and Select All stay table-local. Native Cut/Paste/Clear
  are inert. Source and Flow continue using their established controls.
- Grid theme surfaces use the current policy palette and UI font. Native header
  styling remains OS-owned; this evidence does not assert full dark-header
  polish or high-contrast/screen-reader acceptance.
- The shell creates the native table lazily on its first actual CSV Grid view.
  Plain/Flow startup creates no unused ListView HWND. Lazy creation inherits the
  current theme, UI font and layout and retains the nested-layout identity guard.

## Replacement prompt safety

The pre-existing single-line Find/Go-to-line modes remain single-line. The new
cell mode uses a multiline standard Windows EDIT, with Ctrl+Enter acceptance and
Escape cancellation. Initial mixed CRLF/LF/CR text is not normalized by this
adapter. Input is not silently clipped at a new 64 KiB limit: its native edit
limit is generous, and acceptance visibly rejects a complete value exceeding
`NativeCsvGridCommands.MaxPayloadLength` (8 Mi UTF-16 units), leaving the prompt
open for editing/cancellation. Embedded NUL initial values are rejected before
opening rather than silently truncated. Commands additionally validate encoded
output separately in the shared controller/helper.

The temporary EDIT tracks native composition start/end through a subclass.
Accept refuses while preedit is active, rather than swallowing composition Enter
or persisting uncommitted text. This safety invariant is implemented; actual
physical IME composition acceptance remains a separate platform acceptance task.

## Reproducible verification

Environment: Windows local machine, .NET 10, Release configuration. All tests use
hidden HWNDs and synthetic control messages only; they do **not** write the real
clipboard, steal foreground desktop input, or mutate an input source.

```powershell
dotnet build src/Mote.Native/Mote.Native.csproj -c Release --no-restore -warnaserror
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore `
  --filter FullyQualifiedName~NativeCsvGridWindowsTests -warnaserror
```

- Native Release build: **0 warnings / 0 errors** after adapter integration.
- First focused native suite: **5/5 passed**, 249 ms. Actual owner-data
  `LVM_GETITEMW` requests reached parent `WM_NOTIFY` callbacks; emoji capacity-2
  buffer returned an empty string rather than a surrogate half, with a sentinel
  proving no buffer overrun. Native hit-test selection, active shifted reveal,
  F2, read-only message admission, same-version maps, edge coalescing/rebase,
  off-window selection, focus-sensitive Cut/Copy/Select All and production
  multiline input mixed-line-ending + 5,000-character readback passed.
- Final six-test focused run after frozen-coordinate and scrollbar hook changes:
  **6/6 passed**, 236 ms, Release `-warnaserror`. Its first attempt was blocked
  at compilation by another writer's draft `CsvGridCommandPolicyTests` symbols;
  the subsequent coherent run passed without modifying those files here.
- Added native vertical/horizontal scrollbar-edge coverage separately:
  **1/1 passed**, 44 ms, Release `-warnaserror`. Actual `WM_VSCROLL/SB_BOTTOM`
  and `WM_HSCROLL/SB_RIGHT` requests produced coalesced overlapping logical
  windows while the actual owner-data control retained only two delivered items.
- After lazy shell creation, the focused Grid accelerator/source-safety test was
  rerun alone: **1/1 passed**, 46 ms, Release `-warnaserror`; unrelated accepted
  adapter tests were not repeated.
- Failure-directed known-extent navigation test (2026-10-01): **1/1 passed**,
  46 ms, Release `-warnaserror`. Known EOF/last-column requests are visibly refused
  before emission; a modal command uses its pre-prompt extent, never a larger
  replacement projection installed during the dialog's event loop.

No measured first-paint/input-to-present performance, visible custom-cell
selection screenshot, physical keyboard/IME, Narrator/UIA Grid semantics,
cross-architecture AOT or shipping executable acceptance is asserted here.

## Platform rationale / primary sources

### Failure-directed visibility reentrancy regression (2026-10-01)

Integration full-suite validation exposed one unchanged Flow regression:
`Windows_hidden_Hwnd_reentrant_layout_keeps_newer_presentation` expected
presentation sequence 2 but received sequence 1. An exact focused reproduction
failed before the fix. Grid integration had moved native `ShowWindow` calls
after the previously protected layout block. `ShowWindow` synchronously sends
`WM_SHOWWINDOW`; its callback published a newer same-version analysis, after
which the obsolete outer call still imported its old Flow text and identity.

The fix checks reference identity after **each** Grid/Flow visibility operation
and after preview installation before publishing status. A newer nested analysis
owns visibility, native content and presentation identity; the older outer call
returns without overwriting them. The already committed source-NUL P0 safety
logic was not changed by this fix.

- Original regression test, unchanged: **1/1 passed** after the production fix.
- Added hidden-HWND Grid counterpart: a real Grid `WM_SHOWWINDOW` callback
  installs a newer ready table; the old outer call cannot retire its map/content.
- Affected `WindowsFlowRtfTests | NativeCsvGridWindowsTests` only:
  **18/18 passed**, 251 ms, Release `--no-restore -warnaserror`.
- No unrelated full-suite rerun, real clipboard write, physical input, staging,
  commit or push was performed by this worker.

Microsoft documents owner-data ListView as maintaining little per-item state and
requesting display data through owner callbacks, with `LVM_GETITEMTEXT`
unsupported. Accordingly the hidden tests use actual `LVM_GETITEMW`, and the
native adapter supplies already bounded cached labels rather than scanning
source or claiming a whole-file row graph:

- [List-view controls and virtual list-view style](https://learn.microsoft.com/en-us/windows/win32/controls/list-view-controls-overview)
- [LVN_GETDISPINFO notification](https://learn.microsoft.com/en-us/windows/win32/controls/lvn-getdispinfo)
- [EM_SETLIMITTEXT semantics](https://learn.microsoft.com/en-us/windows/win32/controls/em-setlimittext)

Broader architecture, source mapping and incremental-coordinate assumptions are
shared in [CSV Grid architecture](../csv-grid-architecture.md); this worker did
not independently change those policy/Engine contracts.
