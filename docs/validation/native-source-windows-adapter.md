# Windows full-resident native source capability adapter

## Scope and ownership

`WindowsNativeSourceCapabilityProbe.Create(IThemePolicy)` creates a diagnostic-only
host on the current STA. The common hosted runner owns execution authorization,
canonical Engine source/history, fixture construction and all reports. This
adapter is not a product/default profile or another implementation of the bounded
`NativeDocumentView`. It does not reuse the product shell, Canvas, input ribbon or
controller.

The adapter owns one nonactivating fixed-size top-level system `STATIC` window,
one full-resident `RICHEDIT50W` child and its `msftedit.dll` reference. RichEdit is
the sole source glyph painter. No activation, keyboard focus, global input,
clipboard, registry changes or font installation is requested. `WS_EX_NOACTIVATE`
and `SW_SHOWNA` are explicit; no canvas fallback exists. Operations and cleanup
must run synchronously on the creator thread. Destroying the owned parent destroys
its child before the library reference is released. Destruction failure does not
unload a module whose window may still be alive.

## Data and native contracts

- Import uses one complete `WM_SETTEXT` operation, not a bounded page. NUL and
  non-CRLF line endings are rejected rather than silently normalized. Import
  performs no hidden full readback: the common runner times and verifies the
  subsequent complete readback separately.
- Readback reuses `WindowsNativeSourceSafety.Read`: explicit-length Unicode
  `EM_GETTEXTEX` with `GT_USECRLF`, checked capacity and rejection of invalid counts
  or embedded NUL. Equality must be established against the complete intended
  display, not a prefix or viewport.
- Selection and styling use `RichEditOffsetMap` because native paragraph offsets
  count CR once while display UTF-16 counts CRLF twice. Expanded-CRLF interior
  boundaries are rejected. Sorted range equality is not a physical selection
  direction witness.
- Controlled insertion uses `EM_REPLACESEL` with undo disabled. This is not proof
  of ordinary keyboard input, IME composition, accessibility input or an edit
  journal. Its adapter duration currently includes full readback to refresh the
  replica map and checks for competing native undo/redo.
- `EM_SETUNDOLIMIT(0)` disables native undo; import additionally empties undo.
  Attribute publication verifies that both undo and redo remain unavailable.
  Engine history is the only canonical history.
- Complete semantic foreground publication first resets all foregrounds and then
  uses `EM_SETCHARFORMAT` on each mapped range. Only `CFM_COLOR` is changed; no
  RTF stream or text replacement is used. All spans are validated before native
  mutation. Redraw is batched and restored even if viewport restoration fails.
  Text, sorted native selection and the observed viewport tuple are checked
  afterward. Publication duration includes these preservation checks, not just
  native formatting message latency.
- The SDK's `SCF_NOKBUPDATE` is **0x0020**, not **0x0080** (`SCF_SMARTFONT`). It is
  applied to formatting messages to suppress RichEdit automatic keyboard-layout
  switching. Initial font size uses the existing DIP-to-twips factor of 15;
  fonts are local control preferences and system fallback, not installed assets.

## Scroll and presentation limits

Microsoft documents `EM_GETSCROLLPOS` coordinates as **16-bit values even in
32-bit POINT members**. The adapter therefore captures both available pixels and
`EM_GETFIRSTVISIBLELINE`. Restore first applies available pixels and then corrects
the independent first-visible visual-line witness with `EM_LINESCROLL`. The runner
must compare the actual returned tuple. A matching tuple does **not** certify
full distant pixel coordinates, fractional line geometry or pixel-perfect
restoration beyond 64K. Failure to match is a capability failure, not permission
to weaken assertions or replace the whole document.

`FlushDraw` calls `UpdateWindow` on owned controls. This is synchronous native
layout/draw submission, **not** compositor presentation, physical paint, frame
pacing, real-user startup latency, font fallback quality or DPI acceptance.
The reported backend identifies the instantiated RichEdit control class, not a
claim that this experiment uses DirectWrite or a particular typography engine.

## Verification status

Source implementation and exact SDK message/flag review are complete. No local
GUI, HWND creation, physical input or AppKit execution was performed. The parent
coordinates the single integration build and hosted native execution; this note
does not claim those pending results.

## Primary references

- [EM_SETCHARFORMAT](https://learn.microsoft.com/en-us/windows/win32/controls/em-setcharformat):
  explicit masks, native return value and keyboard-update suppression.
- [Microsoft SDK Richedit.h](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/Richedit.h):
  exact flag and message definitions, notably `SCF_NOKBUPDATE`.
- [EM_SETUNDOLIMIT](https://learn.microsoft.com/en-us/windows/win32/controls/em-setundolimit):
  zero disables native undo.
- [EM_GETSCROLLPOS](https://learn.microsoft.com/en-us/windows/win32/controls/em-getscrollpos):
  virtual pixel-space position and documented 16-bit coordinate limitation.
- [EM_EXSETSEL](https://learn.microsoft.com/en-us/windows/win32/controls/em-exsetsel):
  native `CHARRANGE` selection surface.
