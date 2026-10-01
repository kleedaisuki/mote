# Native CSV Grid probe: static target-execution safety review

Reviewed: 2026-09-30. Scope is the current working-tree
`Program --check-native-mac-csv-grid` route, `MacCsvGridProbe`, and its reachable
shell/table/replacement-editor operations. This is independent static review on
Windows, **not macOS execution, ABI acceptance, accessibility acceptance, or a
general approval of every probe in this executable**. No production files or
workflows were changed and no native Mac code was executed.

## Decision

**No substantive host-mutation blocker found. Approve bounded execution of this
exact route on disposable macOS hosted runners, with the existing CI process
timeout.** Run each architecture in a fresh process and keep the executable's
exit status plus stdout/stderr. Approval does not extend to the separate canvas
clipboard route, interactive replacement dialog, or a developer's active desktop.

The review is code-level: ordinary AppKit/window-server housekeeping is outside
the application's control, so this does not promise literally zero filesystem
activity by the OS. The shell deliberately shows and activates its own window;
that transient focus change is acceptable on a disposable desktop, not a
persistent system preference change.

## Reachability and host effects

| Area | Evidence and conclusion |
| --- | --- |
| Composition | The exact single-argument route returns from `Program.Main` before ordinary configuration/controller/document opening. Non-macOS returns 3. `Run` constructs only a plain `MacEditorShell`, not an Engine controller or experimental canvas. |
| Text and snapshots | The source is the eight-character in-memory fixture `a,b\nc,d\n`; the table is two rows by three columns with a four-character display arena. No Engine snapshot is opened, copied, or serialized. The native source receives this fixture through `SetDocument`, not a full file. |
| Clipboard | Copy and Replace events have only `List.Add` observers. The grid emits identity-carrying intents and never publishes decoded values. Its `cut:`, `paste:`, and `pasteAsPlainText:` implementations are explicit no-ops; Command-X/V are consumed. No reachable general-pasteboard read/write exists in this route. Shell clipboard methods exist but are not invoked. |
| Disk/config/telemetry | No file, directory, configuration loader, persistence sink, or network operation is called by the probe. The shell's draw trace is never armed, so document/draw observation does not publish telemetry. Diagnostics go only to stdout/stderr for the caller to capture. |
| Input sources and permissions | No TIS input-source enable/select/disable, `AXIsProcessTrusted` request, TCC grant request, event tap, screen capture, or process-external Accessibility operation is called. Reading and setting accessibility labels on owned native objects is not a cross-process Accessibility request. |
| Input events | `Key` constructs an `NSEvent` with explicit flags (0, Shift bit 17, Option bit 19, Command bit 20), timestamp/window/context/keycode zero and sends `keyDown:` directly to its own table. It does not inherit global modifier state or use `CGEventPost`, `SendInput`, or a global event queue. Shutdown posts only an application-defined wake event to this process's `NSApplication`. |
| Native lifetime | The probe creates an autorelease pool through the shell, installs rooted class callbacks with the table/delegate instance dictionary, and tears down grid callback targets before dictionary removal and native releases. The temporary replacement `NSTextView` is explicitly released in `finally`; `CreateEditor` also releases on its own failure. Objective-C class registration lasts only for this process. |
| Modal UI | Command-Return records a Replace intent only. `MacGridReplacement.CreateEditor` creates and reads a detached multiline editor, disables rich text/Undo/automatic substitutions, and checks exact CR/LF/emoji roundtrip. `Prompt` and `NSAlert.runModal` are not reached by this route. |
| Shutdown | The posted check closes the shell in `finally` for managed non-OOM failures, after which `run` returns and cleanup runs. Unmanaged abort, OOM or an AppKit loop stall can evade that managed path; enforce an external bounded process timeout rather than interpreting a hang as success. |

The table callback registration uses static unmanaged callbacks and catches managed
exceptions before returning to AppKit. This supports process-local containment;
it does not prove the Objective-C ABI is correct on both target architectures.
The target run is required precisely to establish that narrower runtime evidence.

## Actual acceptance coverage

The assertions cover a real `NSTableView` row/column count, ready-cell readback,
Missing marker, noneditable native cell flag, owned-object accessibility labels,
direct responder keyboard intents, same-document selection and installation
sequence, refusal to mutate source on Cut/Paste, no negative/phantom window
requests at known boundaries, temporary decoded-editor exact line-ending
roundtrip, immediate stale-command invalidation, and Grid-to-Flow control reuse.

They **do not** exercise the Engine/controller command pipeline, actual clipboard
publication, CSV payload correctness, a user-edited modal Replace transaction,
Undo, native pointer hit-testing, global keyboard routing/menu validation, real
IME composition, external AX traversal, VoiceOver, performance percentiles, or
large-file indexing. The in-memory certified fixture does not validate Formats'
construction of a projection. Those contracts belong to their distinct managed
and target tests and must not be inferred from this probe's success string.

The inspected hidden Windows grid tests use owned HWNDs and direct
`SendMessageW`/callbacks with in-memory fixtures; their `WM_CUT`/`WM_PASTE` calls
test no-op table handling, not OS clipboard writes. No new explicit-disposable
real-clipboard test is present in these grid tests. The existing separate
`--check-native-mac-canvas-clipboard` route **does** write the OS clipboard and
test output files; this approval expressly excludes it.

## External contract checked

Apple documents the constructed event's explicit type, modifier flags and other
arguments in [`NSEvent.keyEvent`](https://developer.apple.com/documentation/appkit/nsevent/keyevent(with:location:modifierflags:timestamp:windownumber:context:characters:charactersignoringmodifiers:isarepeat:keycode:)).
[`NSEvent`](https://developer.apple.com/documentation/appkit/nsevent)
describes application responder/event handling. The decisive safety distinction
here comes from inspected code: an owned responder receives the object directly,
not a system-wide injected event. No claim about physical keyboard handling is
made.

## Failure-directed delta: frozen menu and Edit focus routing

The subsequent `Check` extension was reviewed separately without repeating the
approved core: `makeFirstResponder:` assigns the owned table, `menuWillOpen:` is
called directly on its existing delegate, a same-version sequence is installed,
then the owned Reveal/Copy CSV menu items' targets/actions are called directly.
`MenuWillOpen` freezes only two identity/coordinate intents; `MenuCommand` reads
the owned item's enum tag and emits the frozen intent to the probe's `List.Add`.
It does not prepare/publish a clipboard payload, open a dialog or edit source.
No native menu tracking loop is opened by this direct-delegate test.

The new direct `moteCut:`, `moteSelectAll:` and `moteCopy:` calls on the window
delegate inspect the owned window's first responder. With grid focus, Cut returns,
Select All changes only bounded table selection, and Copy emits an intent. Their
fallbacks invoke source events only; this event-only probe has no controller and
its attached source Cut/Select All observers merely increment counters. Therefore
even a focus-routing assertion failure does not reach OS clipboard publication.

**Approval remains valid for this expanded exact route**, with the same disposable
desktop/process-timeout restrictions. Coverage now additionally includes direct
menu-opening delegate freezing across a same-version reinstall and direct Edit
delegate focus routing. This is not actual context-menu popup/tracking, system
menu validation or physical shortcut dispatch. Follow Source / Go To Row modal
commands under development are not invoked by the reviewed probe; adding their
invocation requires another narrow reachability review.

## Final frozen navigation delta and route admission (2026-10-01)

Owner freeze was explicitly received before this final review. The probe now
directly invokes the owned Follow Source menu item: `MenuFollowSource` emits the
frozen `_menuWindow` with `FollowSource = true` into the shell's
`GridWindowRequested` event. Its sole probe observer is `windows.Add`. The probe
then clears that list. No controller is constructed, so no subscriber can open
a document, mutate source or publish clipboard data on that path.

`MacGridNavigation.TryCreateRequest` assertions use only in-memory parsing and
bounded integer arithmetic. Neither the Go To Cell menu item nor its `Prompt`
method is invoked. The added production horizontal-wheel handler is not driven
by the probe. Thus neither addition expands the executed route into modal UI,
external events or persistent host mutation.

**Final approval:** exact `--check-native-mac-csv-grid` at the following frozen
source hashes is safe for bounded disposable-runner execution. The event-only
shell subscribes only the probe's Shown callback, intent/list observers and source
counter observers; ordinary `Program` controller/config composition is bypassed.
This approval remains static, not evidence that the target assertions pass.

### Final immutable source ledger

SHA-256 hashes below bind the reviewed source bytes; any change to these files
requires a delta review before reusing this approval. The NUL probe is included
because it shares the entry point, but its separate execution approval lives in
`native-mac-source-nul-safety.md`.

| Source | SHA-256 |
| --- | --- |
| `src/Mote.Native/Program.cs` | `9DE1750454E973B0FD58EA7F23471E1BB3FA99043E4E75CF7E9CF46F375C8D8A` |
| `src/Mote.Native/Mac/MacEditorShell.cs` | `68D95BFED79AB7A218A7B0EFCAC29ED183DE17F3CD2FCF724C482430D6A27F7F` |
| `src/Mote.Native/Mac/MacCsvGridProbe.cs` | `BA5236413C8C52D68A1489EBCB0C7CF344BBF0C7F762B59433E9B546BD643CBA` |
| `src/Mote.Native/Mac/MacCsvGrid.cs` | `F4BCC415719CB9D3744F0B363AEA419B48DA23D666135C3867A9BF80C7065ABC` |
| `src/Mote.Native/Mac/MacGridReplacement.cs` | `ACB4ABF0CE6EB38770EC836F03473C08054AFAB462405603360C7608BF796138` |
| `src/Mote.Native/Mac/MacGridNavigation.cs` | `C3B77B359B1B28D38E4E3327A95BCB91C0714688225BE7AC108525280AE45FAE` |
| `src/Mote.Native/Mac/ObjC.cs` | `ECE56453C8756D81AB5E27ECAA40C79E6DEAB06B27536C7C48D8AF1A28006982` |
| `src/Mote.Native/Mac/MacSourceNulProbe.cs` | `1E7E8A4A624F9A5FBF14C386361AD553789D2AE579F12EBED38F61E0B109FBC8` |
