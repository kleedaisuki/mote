# Windows native editor adapter

`WindowsEditorShell` uses the Windows `RICHEDIT50W` control from the OS-provided
`Msftedit.dll`. No native UI dependency is distributed with mote. The controller's
`Document` remains the only persistent text and undo owner; RichEdit holds one bounded
editable page. The Windows shell reports full *display-page* text on user edits and
never saves its own buffer.

## Coordinates and selection

RichEdit's selection messages count each paragraph delimiter as one CR code unit,
while `EM_GETTEXTEX` with `GT_USECRLF` returns two code units per newline. The
`RichEditOffsetMap` converts both directions. The controller handles the separate
source-to-CRLF-display mapping, preserving mixed source line endings. Never pass a
controller display offset directly to `EM_EXSETSEL`.

`EN_SELCHANGE` is deferred until the current native input message has completed,
because RichEdit can collapse a selection before it sends `EN_CHANGE` for typed text.
If a prior user selection is still pending when a new edit, Copy or Cut begins, it is
flushed first. This ordering lets a whole-document selection survive a page-local
native replacement long enough for the controller to apply the global engine edit.
Programmatic `SetSelection` calls suppress notifications. RichEdit's `CHARRANGE`
reports ordered endpoints, not the active edge: reverse selection direction is not
recoverable through this adapter, and `SelectionChanged` reports `(min, max)`.

The shell intercepts Ctrl+A/C/X and native `WM_COPY`/`WM_CUT` so those commands
cannot silently act on just the visible page. Copy uses the controller's global
selection and places Unicode text on the Windows clipboard. Find and Go to Line use
a small OS-native modal prompt. The controller owns search state and navigation.
Clipboard API failures throw rather than showing an error and returning: the
controller must see failure before it commits a Cut deletion. Before a successful
`SetClipboardData`, the shell owns and frees the movable memory block; afterward,
Windows owns it.

## Semantic decoration

Repeated RichEdit selection-formatting calls scale poorly even with redraw disabled.
The shell builds one escaped RTF page and imports it with `EM_SETTEXTEX` only after
300 ms of input idle. It skips stale text and defers import during IME composition.
The import verifies an exact plain-text readback, restores caret and scroll, and
falls back to unstyled plain text if conversion changes content. Engine Undo/Redo
commands are bound to menu/accelerators; RichEdit's own undo stack is not canonical.
Measurements and alternatives are in `docs/native-richtext-performance.md`.

## Preview accessibility name

The preview remains a separate **read-only** RichEdit/UIA Document; it is not a
second editable source document. RichEdit otherwise exposes the generic UIA Name
`RichEdit Control`, which does not distinguish the preview from the editor.
`WindowsPreviewAccessibleName` uses the Windows
[`IAccPropServices::SetHwndPropStr`](https://learn.microsoft.com/en-us/windows/win32/api/oleacc/nf-oleacc-iaccpropservices-sethwndpropstr)
annotation for the preview HWND's `OBJID_CLIENT`/`CHILDID_SELF` Name property,
setting the constant `Mote preview`. The annotation is cleared on `WM_DESTROY`;
its failure does not prevent text editing. Neither the preview's text nor a user
path enters its accessible Name. This follows Microsoft's
[Win32 control-name guidance](https://learn.microsoft.com/en-us/accessibility-tools-docs/items/win32/control_name)
without hiding or demoting the useful read-only preview.

On 2026-09-30, Windows 10.0.26200.0, a strict one-file win-x64 Native AOT publish
(`.cache/preview-name-win-x64/mote.exe`, 6,175,232 bytes, SHA-256
`B532E5CEE1A1BB21FACFFC561479D0FA858F4D723F3DAF058CE7075165D70128`)
was queried from a separate PowerShell UIA client in both ordinary Continuous and
`--legacy-page` modes. The reproducible content-free probe and JSON reports are
under `.temp/preview-name/`. Raw, Control, and Content views each contained two
Documents. Continuous exposed one editable source `mote.source.document` named
`Mote editor` (`ValuePattern.IsReadOnly=false`) and one preview id `102` named
`Mote preview` (`ValuePattern.IsReadOnly=true`, `TextPattern.IsReadOnly=true`);
legacy exposed its editable id `101` plus the same named read-only preview. The
custom source TextPattern did not return a Boolean IsReadOnly attribute in this
probe, so its complete editable-text contract remains a separate gate. This proves
target-host UIA metadata for win-x64, **not** Narrator/NVDA speech, keyboard
focus behavior, or win-arm64 ABI parity. Those are separate acceptance gates.

## Focused local probes

Reproducible Windows-only probes live under repository `.temp/`:
`win_offset_probe`, `win_shell_map_probe`, `win_event_order_probe`,
`win_navigation_probe`, `win_prompt_probe`, `win_global_probe`, and
`win_controller_probe`. They validate multiline coordinate conversion, event order,
native command routing, Unicode clipboard/prompts, whole-document Select All then
typing, and a bounded long-line typing path. They are development probes, not a
replacement for actual CJK IME, accessibility, paint-latency, Windows Arm64 or
installer/association testing.
