# macOS native shell contract and verification

`MacEditorShell` is an AppKit adapter, not an owner of the document. The controller
passes one bounded page of canonical UTF-16 text into `SetDocument`, and receives
the bounded visible page back after user edits. `NSTextView` supplies macOS-native
text input, IME, selection, clipboard, text accessibility, and platform focus
behavior. The engine owns versioned text, file identity, save, and undo/redo.
AppKit and libobjc are macOS system libraries; no helper executable, nib, font,
or application bundle is needed for this adapter.

## Invariants and trade-offs

- `SetDocument` skips `setString:` when the text is unchanged. This prevents the
  normal controller echo after a keystroke from resetting selection or marked
  text. A changed page intentionally replaces the native page.
- Semantic colors use `NSTextView`'s source-range foreground attributes. An
  analysis pass is deferred while `hasMarkedText` is true, to avoid interfering
  with an active IME composition. `SetAnalysis` does not replace source text.
- `textDidChange:` does not forward marked-text preedit to the engine. It
  forwards the final visible page once composition ends; a short AppKit runloop
  check catches an unmark without another text notification. If a canonical
  `SetDocument` differs during marked text, replacement is deferred until
  unmark. This avoids programmatically tearing a CJK composition but has not
  yet been verified with a real input method on macOS.
- `CommitPendingText` is a synchronous identity-boundary gate. It asks AppKit's
  `NSTextInputClient.unmarkText` to accept visible marked text, forwards that
  final page to the current controller document exactly once, then returns
  success only after marked text is gone and the controller has not replaced
  the visible text with a rejection rollback. Native New/Open/Save/Save As,
  close/quit, and navigation command callbacks invoke the same gate before
  raising their events. The controller must also call it immediately before
  an *asynchronous* open completion swaps document identity: a composition can
  start after the original Open command. If the gate fails, that identity swap
  or save must not proceed.
- Both text views enable attributed text because AppKit's range-specific color
  and font APIs do not work on plain-text controls. This is presentation only:
  the engine receives plain `NSString` characters, image import is disabled,
  and the standard Paste menu invokes `pasteAsPlainText:`. The preview consumes
  source-mapped spans from the controller to style Markdown headings/code,
  structured scalars, and table headers without touching the editor caret.
- The read-only preview remains a selectable `NSTextView` for native copy and
  accessibility. Its subclass lets AppKit place the click selection first, then
  emits that displayed UTF-16 offset with the stamp of the *applied* semantic
  analysis; Enter/Space on a focused preview emits the current selection start.
  The shell never interprets preview links or changes the source itself. The
  controller resolves source spans, validates version/composition, and only
  then calls `FocusSource`, which returns first responder to the existing source
  view without rewriting text or selection. Command/Option/Control shortcuts
  continue through AppKit. A stale or cleared preview cannot navigate.
- `TextChanged` copies the entire *bounded page*, not the entire document. The
  controller must keep the page size bounded and reconcile page replacement
  against its immutable document snapshot.
- The native selection delegate reports the text view's ordered `NSRange` in
  displayed UTF-16 coordinates. AppKit does not expose the drag anchor
  orientation through `selectedRange`; the adapter preserves a known prior
  anchor when it remains at a boundary (for keyboard extension), otherwise
  reports the ordered endpoints. Reverse mouse drags can therefore lose their
  direction, although the selected range itself remains correct.
  Programmatic `SetSelection` suppresses this notification. Selection events
  are queued to the next runloop turn and discarded if `textDidChange:` arrives
  first, so a transient caret collapse cannot erase a global selection before
  the controller processes the edit. The Find, Find Next,
  Go To Line, Select All and Copy menu shortcuts route to the controller rather
  than using the bounded text view's page-local actions. Global clipboard copy
  writes `public.utf8-plain-text` to `NSPasteboard`.
- Native `NSString` conversion copies UTF-16 code units, not UTF-8, so embedded
  NUL and source line endings are not changed by the bridge. This does not yet
  constitute a GUI-tested guarantee that every IME preserves CR-only text.
- Text views use AppKit's documented scroll-view geometry: vertically resizable,
  width-tracked text containers, and bounded initial frames. This avoids using
  a document view with the parent split-pane origin (which would render the
  preview offscreen).
- `NSOpenPanel` and `NSSavePanel` supply OS file selection. The controller
  captures the Save As destination fingerprint and asks `ConfirmOverwrite` for
  explicit path-specific approval before the engine replaces it. It must still
  preserve engine save-conflict guarantees.
- `PrefersDark` reads `NSApplication.effectiveAppearance`, not an unsupported
  global defaults key. Theme selection is made by the controller at startup.
- `NSApplicationLoad` is called before Objective-C class lookup. A prior AOT
  probe found `objc_getClass("NSTextView") == nil` without loading AppKit.
- Window close and the app-menu Quit action use `NSApplication.stop:` from the
  close event rather than `terminate:`. Apple documents that `stop:` returns
  from `run`; `terminate:` exits the process before managed post-run checks or
  telemetry shutdown. A benign `NSEventTypeApplicationDefined` event is posted
  after `stop:` so a timer-driven close also wakes a blocked event loop.
  Dock/system Quit is redirected through the same checked window-close path
  and returns `NSTerminateCancel` to prevent AppKit's direct process exit.
  The hosted workflow initially exposed the original gap: exit code 0 with
  empty stdout because post-run assertions never executed.

## Validation status

Windows cross-compilation of the macOS source succeeds with
`dotnet build src/Mote.Native/Mote.Native.csproj -p:BuildProjectReferences=false
-p:AllowUnsafeBlocks=true -p:OutputType=Library`. This checks managed signatures,
not runtime AppKit ABI. macOS arm64/x64 GUI CI must exercise a launched Native AOT
binary, window creation, an editable frame, close veto, typing/IME, file panels,
and a smoke-run exit. Manual VoiceOver validation remains necessary; a standard
`NSTextView` is a strong platform mechanism but not proof of integration quality.

`--check-native-mac-workflow <input> <output>` is a **published-binary,
in-process AppKit workflow probe**. It requires a small input, a nonexistent
output directly inside the repository's real `.temp/` directory, and leaves
the input untouched. After the real native window has opened the input, it
inserts through `NSTextInputClient.insertText:replacementRange:`, invokes the
real `NSTextInputClient.setMarkedText:selectedRange:replacementRange:` to
stage a Unicode candidate, asserts that preedit is visible but *not* in the
controller's canonical page, then invokes the native Save As action while
marked text is active. The command gate must unmark and commit it before Save
As captures a snapshot. A one-shot probe picker supplies the output path;
the probe then stages a second marked candidate, invokes New while it remains
marked, and approves exactly one dirty-document discard. It asserts that the
dirty prompt was reached *after* the pending candidate entered the canonical
document, then
reopens the saved file through the native Open action. It checks the actual
`NSTextView` text after reopen and independently decodes the saved file with
`Document.OpenAsync`; the published executable returns a specific success
marker only when all checks pass. This does **not** synthesize external
keyboard events, exercise the real file picker, grant Accessibility permission,
or validate Chinese/Japanese IME composition. Those require a TCC-authorized
interactive runner/manual test.

## Platform references

- [NSApplicationLoad](https://developer.apple.com/documentation/appkit/nsapplicationload)
- [NSApplication lifecycle](https://developer.apple.com/documentation/appkit/nsapplication)
- [NSApplication.stop:](https://developer.apple.com/documentation/appkit/nsapplication/stop%28_%3A%29)
- [NSEvent custom event creation](https://developer.apple.com/documentation/appkit/nsevent/otherevent%28with%3Alocation%3Amodifierflags%3Atimestamp%3Awindownumber%3Acontext%3Asubtype%3Adata1%3Adata2%3A%29)
- [NSApplication.postEvent:atStart:](https://developer.apple.com/documentation/appkit/nsapplication/postevent%28_%3Aatstart%3A%29)
- [NSTextView and native text editing](https://developer.apple.com/documentation/appkit/nstextview)
- [NSTextViewDelegate](https://developer.apple.com/documentation/appkit/nstextviewdelegate)
- [NSTextView selection notification](https://developer.apple.com/documentation/appkit/nstextview/didchangeselectionnotification)
- [NSTextView command delegation](https://developer.apple.com/documentation/appkit/nstextviewdelegate/textview%28_%3Adocommandby%3A%29)
- [NSPasteboard string type](https://developer.apple.com/documentation/appkit/nspasteboard/pasteboardtype/string)
- [NSTextInputClient.unmarkText](https://developer.apple.com/documentation/appkit/nstextinputclient/unmarktext%28%29)
- [Range text colors](https://developer.apple.com/documentation/appkit/nstext/settextcolor%28_%3Arange%3A%29)
- [Range fonts](https://developer.apple.com/documentation/appkit/nstext/setfont%28_%3Arange%3A%29)
- [Plain-text paste](https://developer.apple.com/documentation/appkit/nstextview/pasteasplaintext%28_%3A%29)
- [Putting an NSTextView in an NSScrollView](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/TextUILayer/Tasks/TextInScrollView.html)
