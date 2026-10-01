# macOS source-NUL probe: frozen static execution safety

Reviewed: 2026-10-01 after explicit owner freeze. No Mac code was executed; no
production code or workflow was changed. This review covers only
`--check-native-mac-source-nul` and its reachable operations.

## Decision and execution envelope

**No substantive host-mutation blocker found. Approve this exact route on a
disposable macOS hosted runner with an external three-minute process timeout.**
Use a fresh process per RID and record stdout/stderr plus exit status. Its own
AppKit window is shown and activated, so this is not approval to run on an active
personal desktop. Ordinary OS/framework housekeeping is not a promise of zero
OS filesystem activity. Native ABI failures/hangs still require target evidence
and external timeout containment.

## Inspected route and effects

- `Program.Main` recognizes one exact argument, checks macOS, invokes the probe
  and returns before configuration, telemetry sink, document opening and the
  ordinary controller composition. Non-macOS returns 3.
- `MacSourceNulProbe.Run` constructs a legacy `MacEditorShell(false)`, never a
  `NativeEditorController` or canvas. Only Shown and TextChanged observers are
  attached; TextChanged appends to an in-memory list. No hidden subscriber saves
  text or publishes clipboard payloads.
- Six small in-memory strings cover embedded NUL, a CSV-like string, visible
  U+2400 symbols, emoji and mixed endings. `ObjC.String`/`ManagedString` use
  explicit UTF-16 lengths and temporary native allocations, not file storage.
- `SetDocument` imports the fixture into the owned `NSTextView` under the shell's
  installation guard. `ProbeInsertAtStart("x")` sets its local selected range and
  calls `insertText:replacementRange:` directly. No `NSEvent`, global injection,
  clipboard, TIS input-source manipulation, permission request or external AX
  action is involved.
- `NativeTextProjection` runs in Preserve mode and computes a local difference.
  `new Document(source)` has a null file path; Apply/Undo/Redo change only Engine
  rope/history and snapshots. No save/open APIs are called. Its `Dispose` clears
  memory/history/events without saving. The tiny `GetText` assertions intentionally
  materialize only fixture content, not a full user-file snapshot.
- The shell's draw trace is not armed and no telemetry sink is initialized.
  Console diagnostics contain fixture index/contract and a static summary, not
  user source or filesystem paths. Framework observation reads appearance only.
- The posted check closes the owned window in `finally` on normal or managed
  non-OOM failures. The shell releases its autorelease pool and process-local
  callback state; external timeout remains necessary for native aborts/hangs.

## What success would establish, and what it would not

The route asserts length-aware NSString bridging, real legacy NSTextView exact
import/readback, native prefix insertion and notifications retaining NUL/tail,
production projection Difference, and unsaved Engine Apply/Undo/Redo. It checks
fixture-only content, not arbitrary documents.

It does **not** run the full controller reconciliation path, Save/reopen, real IME
composition, Continuous canvas mode, clipboard publication, physical keyboard
routing, VoiceOver, performance or all native text behavior. The probe explicitly
prints `save-reopen=not-tested`; its success marker must not imply those features
were validated. Shared AppKit lifetime/activation safety follows the companion
Grid review; this independent route introduces no modal dialog.

## Final immutable source ledger

Approval is bound to the source bytes below after the owner freeze. Additional
shared source hashes (`MacCsvGrid`, replacement/navigation helpers) are recorded
in `native-csv-grid-probe-safety.md`; those families are not invoked by this NUL
route. Native source notification, bridge, projection and Engine files are also
included here to make its actual transitive assertions reproducible.

| Source | SHA-256 |
| --- | --- |
| `src/Mote.Native/Program.cs` | `9DE1750454E973B0FD58EA7F23471E1BB3FA99043E4E75CF7E9CF46F375C8D8A` |
| `src/Mote.Native/Mac/MacSourceNulProbe.cs` | `1E7E8A4A624F9A5FBF14C386361AD553789D2AE579F12EBED38F61E0B109FBC8` |
| `src/Mote.Native/Mac/MacEditorShell.cs` | `68D95BFED79AB7A218A7B0EFCAC29ED183DE17F3CD2FCF724C482430D6A27F7F` |
| `src/Mote.Native/Mac/ObjC.cs` | `ECE56453C8756D81AB5E27ECAA40C79E6DEAB06B27536C7C48D8AF1A28006982` |
| `src/Mote.Native/NativeTextProjection.cs` | `D138EE95CA04E661830B4B81C857F68A9F82048FAF3DC36AC7C84C7C13BA6213` |
| `src/Mote.Engine/Document.cs` | `F85FD585E5342ADDA7DFD8380E83B3BC3234BFAAC682FA3D8DAA014AF3FDF074` |
