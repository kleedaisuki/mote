# Full-resident AppKit source capability adapter

## Scope and current evidence

`MacNativeSourceCapabilityProbe.Create(IThemePolicy)` constructs a diagnostic-only
`INativeSourceDiagnosticHost`. It is not a product/default mode, does not reuse
`MacEditorShell`, Canvas or a ribbon, and performs no file I/O. The common runner
owns the canonical document, history, fixtures, hosted guard and reporting.

The owned visible NSWindow contains one NSScrollView and one full-resident
NSTextView as its sole source painter. It uses `orderFront:`, not activation,
`makeKeyAndOrderFront:`, first-responder rescue, synthetic input, pasteboard,
input-source changes or font installation. Darwin `pthread_main_np` and the
managed construction thread are checked before operations. The adapter retains
its objects, closes its own window, releases its view references and finally
drains its own autorelease pool. Constructor failure releases partial ownership.

Source inspection and managed compilation are not AppKit runtime acceptance.
No local native/GUI execution was performed during implementation. Exact native
newline preservation, drawing, end scrolling, attribute behavior and both native
ABIs require the common hosted runner. No claims about IME, undo notifications,
physical selection direction, compositor presentation or user-perceived latency
follow from this adapter.

## Contracts and deliberate costs

* Import and readback use the existing explicit-length UTF-16 NSString bridge;
  embedded NUL, CR, LF and CRLF are neither normalized nor guessed. A changed
  replica fails the caller's separate exact-readback certification phase. Import
  itself performs no readback so those two timed phases remain distinct. No
  successful offset map is synthesized.
* Native selection is a sorted global UTF-16 NSRange. Controlled insertion uses
  one `insertText:replacementRange:` with that range and checks the exact final
  text. `allowsUndo` is disabled because canonical engine history owns undo.
* Scrolling to the global end uses `scrollRangeToVisible:` and checks that actual
  selection did not move. Clip bounds origins capture/restore viewport state.
  Scrolling is not a focus, input-delivery or whole-document layout witness.
* Style publication validates all spans first, batches actual NSTextStorage
  `addAttribute:value:range:` edits with `beginEditing`/`endEditing`, first resets
  the whole foreground and then applies supplied foreground spans. It does not
  replace characters or complete attribute dictionaries. Text and selection are
  checked and the captured viewport restored. Native font attributes remain.
* Exact checks copy the complete native NSString. Their allocation/time is part
  of guarded insertion and publication methods, together with native selection
  and viewport preservation checks. Do not relabel insertion as physical input
  latency or guarded publication timing as
  pure attribute mutation or native paint cost. Autoreleased colors and strings
  live until host disposal; this first bounded capability experiment is not a
  steady-state memory/performance certification.
* The theme supplies editor foreground/background and the system monospaced
  font size. Native defaults govern other paragraph layout; this does not certify
  matching Canvas line-height/spacing, glyphs, font fallback or visual parity.
* `FlushDraw` calls owned native layout/submission (`layoutSubtreeIfNeeded`,
  `displayIfNeeded`); its return is not a compositor/presentation timestamp.

## Backend and ABI

The backend getter first checks selector availability and the actual non-nil
`textLayoutManager`. Only when that manager is absent does it query
`layoutManager`; an actual non-nil legacy manager reports `textkit1`, otherwise
`unknown`. This avoids probing the legacy accessor first and causing a TextKit 2
compatibility fallback merely to identify the backend. It does not infer the
backend from the OS version or presume what `initWithFrame:` selected. Re-query
after style/edit operations when reporting their current backend.

Private bridges supplement existing ObjC messaging only in this owned file:
BOOL setters/results use byte ABI, `pthread_main_np` returns int, and CGRect
(four doubles, 32 bytes) returns through `objc_msgSend_stret` on x64 and ordinary
`objc_msgSend` on arm64. Other architectures fail before creating native objects.
Existing ObjC NSRange and CGFloat argument bridges are reused. Both target
executions remain necessary to establish runtime correctness.

### Frozen BOOL bridge review checkpoint

The committed adapter (`a86e049`, retained through import-phase correction
`9331107`) already routes `Responds` through private `SendNativeBool`, whose
`objc_msgSend` declaration returns `byte` and takes three `nint` arguments.
It does not reinterpret `respondsToSelector:` through the shared native-word
return bridge. This closes the concrete upper-register-bit ambiguity before
commit, not through a subsequent change to shared ObjC interop. The frozen
source SHA-256 is
`735BE81D281F2178043200AF1411966D9374EFE74EA06391D26C64DC97B21ED8`.
This is source/ABI evidence only, not native target execution.

## Primary documentation

* [Apple TextKit overview](https://developer.apple.com/documentation/appkit/textkit)
  describes the native text engine and NSTextView manager/container/storage.
* [Apple WWDC22: What's new in TextKit and text views](https://developer.apple.com/videos/play/wwdc2022/10090/)
  explains TextKit 1 compatibility and why legacy manager queries must be audited.
* [Apple NSMutableAttributedString beginEditing](https://developer.apple.com/documentation/foundation/nsmutableattributedstring/beginediting())
  documents batching edits rather than character replacement.
* [Apple NSTextView](https://developer.apple.com/documentation/appkit/nstextview)
  is the native editing, selection, insertion and scroll contract; the capability
  experiment still needs actual target evidence rather than documentation alone.
