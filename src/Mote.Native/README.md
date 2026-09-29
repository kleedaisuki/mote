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

## Current behavior and explicit limits

- Open, New, Save, Save As, Undo, Redo, and formatting are wired to the engine.
  Open/save run off the UI thread. Save As over an existing file requires an
  additional approval after a fingerprint is captured; a changed target is
  rejected. Invalid UTF-8 is not silently replaced.
- The editor control holds a nominal 64 KiB source page with 8 KiB of edit
  slack before a focus-preserving rebase. This avoids the
  gigabyte-scale full-control mirror observed for large text and long lines,
  but page navigation is **not** a finished virtualized whole-file editor.
  Global Select All, Copy, Cut, Find Next and Go To Line use engine offsets;
  continuous scroll and off-page screen-reader text exposure are not yet
  implemented. A 64 KiB page within a single 50 MiB line still
  needs measured shaping and caret behavior.
- Format analysis is version-checked and canceled when superseded. Policies
  offering `IIncrementalDocumentPolicy` get one serialized per-document session
  with a bounded versioned edit chain; Plain, CSV and Markdown currently offer
  this capability. CSV and plain text can report complete large-file facts;
  large Markdown may report a provisional viewport until a complete cache is
  available. The legacy fallback performs complete analysis only through
  2 Mi UTF-16 units and explicitly labels larger projections as partial or
  unavailable. An empty partial diagnostic list never means the whole file
  is valid. A future policy can adopt sessions without changing the shell.
- Native preview is a bounded, source-mapped semantic rendering: Markdown
  headings, paragraphs, lists, quotes and code; CSV rows and columns; and
  structured JSON/TOML/YAML trees. It is not yet a full CommonMark or
  HTML-equivalent preview, and preview click-to-source navigation is not
  implemented. Platform adapters apply theme-provided colors and font styles
  to preview runs without a WebView dependency.
- Configuration follows convention first, then `~/.mote/config.toml`
  overrides cache/data/trace destinations, theme, and trace opt-in. The
  `system` theme queries OS app appearance. Trace setup failure never blocks
  file editing; traces never contain text or document paths.

## Native viewport navigation

`NativeNavigationModel` holds the caret and selection in **global, canonical UTF-16 source offsets**, independent of the bounded native text page. The native control's selection is only a projection: `Project(pageStart, projection)` clips both endpoints to the visible page, preserves selection direction, and converts source offsets through `NativeTextProjection`. A null projection means the caret or selection is off-page, not that the global selection was lost.

The controller translates user selection changes back into source coordinates and suppresses notifications caused by its own projection. `GoToLine` takes a **one-based** line number and uses the snapshot's indexed lookup. `FindNext` performs ordinal, case-sensitive KMP search across immutable rope chunks, including matches crossing chunk boundaries, without a whole-file string. The pure model provides `WriteSelection` for streaming original source; the current OS clipboard adapters require a contiguous string, so Copy/Cut materialize the selected range off the UI thread before invoking the clipboard. This can consume substantial memory for a very large selection and needs a measured platform-specific clipboard design.

The model deliberately does not own page movement, platform prompts, or clipboard APIs. These remain controller/shell policy and mechanism respectively. Off-page selections remain in engine coordinates across page transitions; the native caret is parked at the new page until the user changes selection or explicitly edits there. The shell defers selection-collapse events from typing until after its text-change event, so typing over a global selection replaces the entire source range, not merely the visible page.
