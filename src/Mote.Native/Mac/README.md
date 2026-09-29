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
- Both text views enable attributed text because AppKit's range-specific color
  and font APIs do not work on plain-text controls. This is presentation only:
  the engine receives plain `NSString` characters, image import is disabled,
  and the standard Paste menu invokes `pasteAsPlainText:`. The preview consumes
  source-mapped spans from the controller to style Markdown headings/code,
  structured scalars, and table headers without touching the editor caret.
- `TextChanged` copies the entire *bounded page*, not the entire document. The
  controller must keep the page size bounded and reconcile page replacement
  against its immutable document snapshot.
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

## Validation status

Windows cross-compilation of the macOS source succeeds with
`dotnet build src/Mote.Native/Mote.Native.csproj -p:BuildProjectReferences=false
-p:AllowUnsafeBlocks=true -p:OutputType=Library`. This checks managed signatures,
not runtime AppKit ABI. macOS arm64/x64 GUI CI must exercise a launched Native AOT
binary, window creation, an editable frame, close veto, typing/IME, file panels,
and a smoke-run exit. Manual VoiceOver validation remains necessary; a standard
`NSTextView` is a strong platform mechanism but not proof of integration quality.

## Platform references

- [NSApplicationLoad](https://developer.apple.com/documentation/appkit/nsapplicationload)
- [NSApplication lifecycle](https://developer.apple.com/documentation/appkit/nsapplication)
- [NSTextView and native text editing](https://developer.apple.com/documentation/appkit/nstextview)
- [NSTextViewDelegate](https://developer.apple.com/documentation/appkit/nstextviewdelegate)
- [Range text colors](https://developer.apple.com/documentation/appkit/nstext/settextcolor%28_%3Arange%3A%29)
- [Range fonts](https://developer.apple.com/documentation/appkit/nstext/setfont%28_%3Arange%3A%29)
- [Plain-text paste](https://developer.apple.com/documentation/appkit/nstextview/pasteasplaintext%28_%3A%29)
- [Putting an NSTextView in an NSScrollView](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/TextUILayer/Tasks/TextInScrollView.html)
