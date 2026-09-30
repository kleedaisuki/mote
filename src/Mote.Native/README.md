# mote native desktop shell

`Mote.Native` is the static composition root for a **single native executable** on
Windows and macOS. `Mote.Engine` owns the file and undo history; `Mote.Formats`
owns semantic analysis; `Mote.Themes` supplies UI-neutral palette policies;
`Mote.Configuration` resolves conventional `~/.mote` paths; and
`Mote.Telemetry` records local, opt-in JSONL traces. No workspace discovery,
language server, runtime plugin loading, web service, or bundled UI library is
used. Windows uses the OS RichEdit control. macOS uses AppKit `NSTextView` via
the Objective-C runtime. All files remain separate at build time and are
Native AOT-linked into one executable per architecture.

The macOS linker embeds `Info.plist` into the Mach-O `__TEXT,__info_plist`
section so Launch Services can open the naked executable, not a companion
`.app` bundle. The published directory must be inventoried: only `mote.exe`
on Windows or `mote` on macOS is a distributable artifact. The executable
still dynamically uses system Windows DLLs or Apple frameworks; strict
single-binary means no **shipped** companion library or resource, not a
statically linked operating system.

```
dotnet publish src/Mote.Native/Mote.Native.csproj -c Release -r win-x64 --self-contained true -o .cache/native-publish/win-x64
# On macOS, use osx-arm64 or osx-x64 instead.
.cache/native-publish/win-x64/mote.exe --check-runtime
.cache/native-publish/win-x64/mote.exe --smoke-gui
```

`--check-runtime` only exercises the headless entry point and prints
`mote-native-ready`. `--smoke-gui` creates and closes a real native window and
prints `mote-native-gui-ready`; neither proves IME, accessibility, editing
latency, file picker behavior, or full document workflow. Native AOT's
published executable is self-contained, but startup and OS-control behavior
must be measured separately on each target architecture ([Microsoft Native
AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)).

The 2026-09-30 profile-wiring checkpoint published with the command above to
`.cache/native-profile-win-x64/`: its inventory contained **one** `mote.exe`
(6,171,648 bytes; SHA-256
`374763A343471C8CCD8D729E479DB76BF44C3EBE1A3880AA8EC09D36266FD995`).
Separate, 30-second-bounded process runs of ordinary `--smoke-gui` and
`--legacy-page --smoke-gui` both exited 0 and printed exactly
`mote-native-gui-ready`; redirected output is retained under
`.temp/profile-smoke/`. This proves both new startup routes create and close a
native GUI in the local Windows x64 build, **not** real input, source AX tree,
physical paint, or the other three RIDs.
The same executable also passed
`pwsh -NoProfile -File tests/NativeWindowsWorkflow.ps1 -ExecutablePath .cache/native-profile-win-x64/mote.exe`:
the explicit `--legacy-page` process opened 11 UTF-16 units, accepted one
real RichEdit `WM_CHAR`, saved 12, and a fresh GUI process reopened all 12
(exit 0, `native-windows-open-edit-save-reopen-ok`). This checks the rollback
route's existing Windows workflow, not Continuous editing or another RID.

## Current behavior and explicit limits

On this development branch, ordinary `mote [path]` uses the source-backed
`Continuous` presentation and automatically enables the single-source Windows
UIA fragment route; `mote --legacy-page [path]` retains the established native
text-page workflow as an explicit rollback. Presentation is fixed for the
window lifetime. The historical `--canvas-experimental` and optional
`--uia-fragment-experimental` invocations remain diagnostic A/B routes, not
alternative product configuration. **This wiring is under validation, not a
release-readiness claim:** real IME, reader, four-RID editing and measured
input-to-screen gates in [the migration design](../../docs/virtual-editor-design.md)
remain conjunctive. If the source accessibility provider cannot attach or
later detaches, editing keeps the canonical document; the persistent status
explains how to save and restart with `--legacy-page`. Mote never moves a dirty
document or marked text between modes inside one process. On Windows, the
source canvas must expose exactly one **editable source** UIA Document; the
separate read-only RichEdit preview may correctly expose a second Document
([Microsoft's standard-control UIA mapping](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controlsupport)).
The input ribbon and hidden page must not expose duplicate source Documents.
Preview labeling, reader speech and focus behavior remain separate gates.

- Open, New, Save, Save As, Undo, Redo, and formatting are wired to the engine.
  Open/save run off the UI thread. Save As over an existing file requires an
  additional approval after a fingerprint is captured; a changed target is
  rejected. Invalid UTF-8 is not silently replaced.
- The `--legacy-page` editor control holds a nominal 64 KiB source page with
  8 KiB of edit slack before a focus-preserving rebase. This avoids the
  gigabyte-scale full-control mirror observed for large text and long lines,
  but page navigation is **not** a finished virtualized whole-file editor.
  Global Select All, Copy, Cut, Find Next and Go To Line use engine offsets;
  continuous scroll and off-page screen-reader text exposure are not yet
  implemented. A 64 KiB page within a single 50 MiB line still
  needs measured shaping and caret behavior.
- Format analysis is version-checked and canceled when superseded. All six
  shipped format policies offer `IIncrementalDocumentPolicy`: each open document
  owns one serialized session and a bounded versioned edit chain. Immediate
  analysis requests the visible source range above 2 Mi UTF-16 units; its
  `Provisional`, `CoveredRegion`, or `Complete` result is shown honestly. A
  separate idle lane may request `Full` for incomplete JSON, YAML, TOML, and
  Markdown. It waits 1/3/15 seconds for files <=4 Mi, <=32 Mi, or larger,
  respectively, and at most one completed pass is attempted per stable version.
  A new edit or page reanalysis cancels the pass; a retry after cancellation
  pays the full delay again. Large Markdown now uses a resource-admitted,
  cancellable certifier: restricted flat documents may become `Complete`, while
  structures outside that proved subset remain `Provisional`. Even a `Full`
  request can remain provisional: the status says so, visible diagnostics stay
  intact, and no global count is invented. Only certified `Complete` results
  promote the global count. The idle pass is deferred with an explicit status
  when the [GC's last physical-memory observation](https://learn.microsoft.com/en-us/dotnet/api/system.gcmemoryinfo.memoryloadbytes)
  suggests insufficient work
  headroom; this advisory guard cannot guarantee that another process will not
  exhaust memory later. Large JSON visible requests scan globally for accurate
  semantics, so the controller uses a 200/500 ms debounce above 8/32 Mi UTF-16
  units to coalesce typing, with immediate cancellation of stale work. Analysis
  cannot block the native input thread, but OS/format-specific peak memory and
  background CPU cost remain release gates.
- A separate `Viewport/` model represents continuous source-backed scrolling
  and bounded visible slices without a per-line object graph. The development
  branch's ordinary route now composes this with a visible, bounded OS input
  ribbon and one source-backed accessibility document. The older
  `--canvas-experimental [path]` route remains an evolving A/B diagnostic, not
  a parity claim. Read-only DirectWrite and
  CoreText geometry probes remain available through
  `--check-native-windows-canvas` and `--check-native-mac-canvas`.
- Native Flow preview receives immutable bounded ordered text/style/origin data
  from the serialized format session. Markdown uses its private parsed syntax
  for nested inline styles, reference-link labels, heading levels, list ordinals,
  quotes and code; existing structured trees remain source-backed. Windows
  imports only mote-generated escaped RTF; AppKit builds attributed text directly.
  Both retain native selection/Copy and install text/maps with a presentation
  sequence, rejecting stale actions even at the same document version. Heading
  activation retains its established marker target while literal provenance is
  independently exact. See [Flow integration](../../docs/native-flow-implementation-contract.md)
  and [navigation](../../docs/native-preview-navigation.md).
  CSV uses its separate bounded native Grid, not a whole-file virtual Table;
  image resources and external-link actions remain separate unfinished phases.
  Host tests and hidden Windows HWND evidence do not establish four-RID AOT,
  physical Copy, accessibility or real IME acceptance. The isolated Mac probe
  is `--check-native-mac-flow-rendering` and requires target execution.
- Native CSV Grid accessibility is experimental and disabled by default. Set
  `MOTE_NATIVE_GRID_ACCESSIBILITY=1` before process startup to register its
  bounded UIA/AX tree. Native Table indices are local; labels and headers carry
  absolute CSV ordinals. Windows F6 / Shift+F6 cycles source, Table, available
  logical scrollers and Go-to without changing source Tab behavior. Windows
  accessible navigation ranges are read-only; use native keys or Go-to to move.
  Grid source-command Invoke/press actions remain omitted, and external Windows
  cell SetFocus is deliberately refused pending a safe owner-thread boundary.
  This opt-in does not change the source editor provider or establish reader,
  real IME or cross-platform release acceptance. See the
  [implementation/evidence ledger](../../docs/csv-grid-accessibility-implementation.md).
- Ordinary Continuous plain text defaults to full-width source by the format
  presentation convention; `[editor] preview = "split"` explicitly retains its
  bounded preview, while `"source"` hides it for any format. `"auto"` is the
  default and preserves historical split behavior under `--legacy-page`.
- Configuration follows convention first, then `~/.mote/config.toml`
  overrides cache/data/trace destinations, theme, and trace opt-in. The
  `system` theme queries OS app appearance. Trace setup failure never blocks
  file editing; traces never contain text or document paths.

## Native viewport navigation

`NativeNavigationModel` holds the caret and selection in **global, canonical UTF-16 source offsets**, independent of the bounded native text page. The native control's selection is only a projection: `Project(pageStart, projection)` clips both endpoints to the visible page, preserves selection direction, and converts source offsets through `NativeTextProjection`. A null projection means the caret or selection is off-page, not that the global selection was lost.

The controller translates user selection changes back into source coordinates and suppresses notifications caused by its own projection. `GoToLine` takes a **one-based** line number and uses the snapshot's indexed lookup. `FindNext` performs ordinal, case-sensitive KMP search across immutable rope chunks, including matches crossing chunk boundaries, without a whole-file string. The pure model provides `WriteSelection` for streaming original source; the current OS clipboard adapters require a contiguous string, so Copy/Cut materialize the selected range off the UI thread before invoking the clipboard. This can consume substantial memory for a very large selection and needs a measured platform-specific clipboard design.

The model deliberately does not own page movement, platform prompts, or clipboard APIs. These remain controller/shell policy and mechanism respectively. Off-page selections remain in engine coordinates across page transitions; the native caret is parked at the new page until the user changes selection or explicitly edits there. The shell defers selection-collapse events from typing until after its text-change event, so typing over a global selection replaces the entire source range, not merely the visible page.

## Continuous canvas contract under validation

`--canvas-experimental` preserves the same engine-owned document, undo, I/O,
format sessions, global UTF-16 navigation, and preview. `CanvasFrame` paints
bounded source slices, while a visible RichEdit/NSTextView host receives OS text
input for at most one 16 Ki UTF-16 single-line, grapheme-safe source interval.
Every final edit carries document generation, base version, binding nonce, and
an **absolute** `TextChange`; a mismatch is rejected rather than applied to a
new document or stale selection. Composition text belongs to the OS until
committed. The canvas never constructs a whole-document text-control mirror.
The legacy contiguous analysis/preview bridge is separately capped to 64 Ki,
even if two visible slices lie across a huge source gap. Thus a later visible
slice may lack semantic overlay until a multi-range analysis path exists.

The ordinary-path profile wiring is a product experiment, not a default-switch
release gate pass.
The experimental Windows and macOS adapters are designed to route exact
plain-text clipboard payloads larger than the input host directly to the
controller as one global transaction; rich clipboard formatting is never
imported into source text. Windows has local real-HWND evidence; macOS still
requires hosted AppKit workflow evidence. The engine now retains the newest
oversized edit through shared persistent rope roots, rather than dropping its
Undo entry when its nominal cost exceeds the ordinary 32 MiB history budget.
The experimental canvas no longer imposes a duplicate 32 MiB hard cap;
50 MiB paste/delete still need target-host latency, peak-memory, and Undo
evidence before product parity can be claimed. Target-OS CJK IME composition,
screen-reader access to off-host text, rich Unicode hit-test/candidate geometry,
semantic colors on the native host row, background analysis latency, and
end-to-end 100 MiB editing remain independent acceptance gates. Keep
`--legacy-page` available until those are demonstrated rather than inferred
from a successful geometry or GUI smoke probe.

In particular, an input method can finalize a candidate whose bounded host
text is byte-for-byte unchanged while a much larger global selection is active.
That finalization must not be confused with canceled preedit. Windows records
only an explicit nonempty `GCS_RESULTSTR` as such evidence; the AppKit path
still needs an observed final `insertText:replacementRange:`/marked-text trace
before claiming this edge is safe. A synthetic `unmarkText` call or GUI smoke
is not evidence of real Chinese/Japanese candidate behavior. RichEdit can also
deliver Text Services Framework (TSF) composition notifications without the
legacy IMM result signal; a real TSF commit/cancel trace is required to ensure
that the conservative Windows cancel guard does not discard confirmed text.
