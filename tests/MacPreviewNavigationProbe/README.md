# Published macOS preview-navigation diagnostic

`NativeMacPreviewNavigation.ps1` launches a **published Native AOT Mach-O** with
an isolated `MOTE_HOME` and a synthetic two-heading Markdown file in the
repository's `.temp/mac-preview-navigation/`. It compiles `Probe.swift` in
that scratch directory. Reports must be written under `.cache/ci-inventory/`.

Run each profile/gesture as a separate process, for example:

```powershell
pwsh -NoProfile -File tests/NativeMacPreviewNavigation.ps1 `
  -ExecutablePath src/Mote.Native/bin/Release/net10.0/osx-arm64/publish/mote `
  -ReportPath .cache/ci-inventory/osx-arm64/preview-continuous-enter.json
pwsh -NoProfile -File tests/NativeMacPreviewNavigation.ps1 `
  -ExecutablePath src/Mote.Native/bin/Release/net10.0/osx-arm64/publish/mote `
  -ReportPath .cache/ci-inventory/osx-arm64/preview-legacy-pointer.json `
  -LegacyPage -Pointer
```

Add `-Space` for Space instead of Return; `-Pointer` takes precedence. The
external Swift process uses AX to find exactly one `Mote editor` and one
`Mote preview`, searches only the capped preview text for the
`Destination` UTF-16 range, and obtains its actual glyph bounds through
`AXBoundsForRange`. It uses a Quartz mouse event at that glyph or attempts to
set/focus the preview's `AXSelectedTextRange`. Before Return/Space, a harmless
Right Arrow must visibly advance the preview AX caret by one; the driver then
resets it to the target. This separates a keyboard-delivery failure from a
navigation failure. It then demands source proxy caret 9, collapsed selection, source
focus, exact unchanged disk SHA-256, and no mutation after Save and Undo.

The report contains offsets, dimensions, role counts, status, and hashes but
no document content. A failed AX setter, permission denial, missing bounds,
wrong frontmost process, unavailable clean-window observation, or absent source
focus is a **failed or unavailable diagnostic**, never converted to a product
pass. The driver retries initial app activation within a bounded readiness
window rather than assuming the process is already registered at launch.
On hosted macOS, `AXEditable` may be absent even for a read-only `NSTextView`;
the report preserves `preview_read_only: null` in that case. A navigation pass
does **not** establish external AX read-only status; the separate in-process
AppKit assertion checks `isEditable == false`. A positively observed
`AXEditable: true` still fails this diagnostic.
This is synthetic gesture
coverage, not real Chinese IME, VoiceOver speech, or physical-paint timing.
Quartz events are global and the frontmost-process guard cannot eliminate a
focus race; run this only on an isolated desktop or disposable hosted runner.
Every navigation key-down/up explicitly clears inherited CGEvent flags;
Save/Undo explicitly uses Command instead. Numeric created/posted flags are
retained for Right Arrow and activation keys. Fresh events on hosted macOS can
inherit modifiers: run 36712114598 moved Right Arrow to word end rather than
one character. With this harness-only correction,
[run 36718845829](https://github.com/kleedaisuki/mote/actions/runs/36718845829)
passed all eight Continuous Return/pointer and LegacyPage Space/pointer cases
across published `osx-x64`/`osx-arm64`. All keyboard controls moved `9 -> 10`;
their created Right-arrow flags were `0x20B00000` (including Command), whereas
posted flags were zero. Source caret/focus, unchanged SHA-256 and clean state
after Save/Undo were checked independently of job success. AXEditable remained
unavailable (`preview_read_only: null`); this result is not physical-keyboard,
IME, VoiceOver, or external read-only acceptance, nor proof the nonmutating
Save/Undo handlers executed. See `docs/mac-preview-keyboard-diagnostics.md`
for the retained negative observations and evidence limits.
The file is intentionally not attached to automatically mutating system-input
source state. The driver does not delete the scratch directory, so a failed
target can be inspected without destructive cleanup.
