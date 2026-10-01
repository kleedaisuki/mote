# macOS explicit-open encoding chooser

## Scope and contract

`MacEditorShell` implements the optional `INativeOpenEncodingShell` capability.
The existing Open menu item and Command-O remain unchanged. The new File > Open
with Encoding… item has no shortcut and dispatches `OpenWithEncodingRequested`
through the existing `NotifyAfterComposition` settlement path. A dedicated outer
guard also contains a secondary error-dialog failure at the unmanaged selector.

`MacOpenEncodingPrompt.Choose()` owns only codec selection. It performs no file
I/O, encoding detection, document replacement, conversion, or telemetry. The
controller remains responsible for decoding and preserving the current document
until actual open succeeds.

The native NSAlert accessory is an NSPopUpButton containing a non-codec placeholder
at index zero followed by exactly the eight shared `NativeOpenEncodingChoices`.
No codec is initially selected. Cancel is the first/default button, and Open is
the second button (response 1001). Only a valid explicit index maps to a codec.
Accepting the placeholder raises a visible, actionable refusal through the
controller/shell error path; it never silently selects UTF-8. Cancellation returns
null. Native alert, accessory, or button allocation failure throws an explicit
error rather than being represented as healthy cancellation.

Both the alert's ownership and the accessory's independent allocation ownership
are released in `finally`. AppKit's accessory retain is independent of the local
allocation reference. An Objective-C initializer consumes its allocated receiver
even when initialization returns nil; the helper does not double-release that
failure path.

## ABI and primary references

- Apple's [NSPopUpButton initializer](https://developer.apple.com/documentation/appkit/nspopupbutton/init%28frame%3Apullsdown%3A%29?language=objc)
  accepts `NSRect` followed by Objective-C `BOOL`, and can return nil.
- Apple's [NSPopUpButton documentation](https://developer.apple.com/documentation/appkit/nspopupbutton?language=objc)
  documents pop-up selection and item-index access.
- Apple's [NSAlert buttons](https://developer.apple.com/documentation/appkit/nsalert/addbutton%28withtitle%3A%29?language=objc)
  document positional response codes and the first button's default Return key.
- Apple's [NSAlert accessory view](https://developer.apple.com/documentation/appkit/nsalert/accessoryview)
  documents placing a native view between explanatory text and buttons.

The private `objc_msgSend` declaration for `initWithFrame:pullsDown:` uses the
existing sequential `ObjC.Rect` (four CGFloat doubles, 32 bytes on both supported
64-bit architectures) and a one-byte `byte` for Objective-C BOOL. It does not use
CLR bool's default interop marshalling or substitute an NSInteger/pointer for the
BOOL argument. The object return and `indexOfSelectedItem` NSInteger use `nint`.
The declaration stays local to the helper; no shared interop overload was changed.

## Verification performed

On 2026-10-01, an isolated linked-source net10.0 Release build compiled the actual
helper, actual ObjC bridge, shared choices/interface, and actual engine encoding
definitions: zero warnings and zero errors. Artifacts are under
`.temp/mac-open-encoding-ui/`, including the project and `build.log`.

Reflection of that compiled assembly checked the exact initializer signature
(`IntPtr`, `IntPtr`, rectangle, `Byte` -> `IntPtr`), the rectangle's 32-byte managed
interop layout, and the eight shared choices. `abi-check.json` records these
checks explicitly with `nativeInvoked: false`. No GUI or Objective-C library was
invoked locally.

The coordinator's later shared build must compile the main shell/controller
integration. Both macOS Native AOT architectures and actual native chooser input,
Cancel/default Return behavior, placeholder refusal, VoiceOver, and native
allocation/lifetime behavior remain unverified at this checkpoint. A linked
managed compile or a layout check is not evidence of native UI execution.
