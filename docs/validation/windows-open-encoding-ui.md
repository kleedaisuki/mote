# Windows explicit open-encoding chooser

## Scope and contract

The optional `INativeOpenEncodingShell` is implemented by `WindowsEditorShell`.
File > **Open with Encoding…** uses unused command ID 218 and adds no accelerator;
ordinary Open and Ctrl+O remain unchanged. Existing controller admission is not
bypassed. The new command contains nonfatal callback errors with a fixed notice,
without serializing filenames, text, native handles, or exception messages.

`Win32EncodingPrompt` is an editor-owned modal window built with system STATIC,
COMBOBOX and BUTTON controls. It reads no files and guesses no encoding. Its
closed dropdown contains an initial **Select an encoding…** placeholder followed
by the eight stable shared `NativeOpenEncodingChoices.All` entries. Index zero
is never mapped to a codec. Open is initially disabled; selecting a valid codec
enables it but does not accept. Cancel has initial focus and is the default;
Escape, close and Cancel return null. A deliberate Open command rereads and
bounds-checks the selected index before returning the corresponding enum.

All control creation results, each exact CB_ADDSTRING insertion index, and the
placeholder selection result are checked. Failed creation/population cannot
silently produce a guessed or shifted codec. Nonfatal managed exceptions in the
unmanaged window procedure are captured; WM_CREATE returns failure or the
owned window is destroyed, then the captured exception is rethrown from the
managed modal loop. OutOfMemoryException retains the existing fatal-policy
exception. The owner is restored and modal callback state cleared in finally.
No new native library, resource file, reflection, clipboard operation, global
input injection, registry change or process activation beyond this user-owned
modal is introduced.

## Platform rationale

The custom window uses the existing Win32TextPrompt modal-loop mechanisms, not
its editable numeric/text field or composition subclass. Microsoft documents
that IsDialogMessage works with any control-containing window and can send
DM_GETDEFID / DM_SETDEFID. This chooser explicitly responds with Cancel as the
default; focusing and deliberately activating Open remains possible.

- [IsDialogMessageW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-isdialogmessagew)
- [DM_GETDEFID](https://learn.microsoft.com/en-us/windows/win32/dlgbox/dm-getdefid)
- [CB_ADDSTRING](https://learn.microsoft.com/en-us/windows/win32/controls/cb-addstring)
- [CB_SETCURSEL](https://learn.microsoft.com/en-us/windows/win32/controls/cb-setcursel)

## Validation and remaining evidence

Focused static review checked unique menu ID, unchanged accelerator table,
placeholder-to-codec offset, exact insertion order, disabled initial acceptance,
cancellation paths, contained callback failures, and cleanup ownership. The
shared native/controller build is coordinated by the encoding integration owner;
its actual qualification is recorded below when available. No local GUI test
was run and no native user-interaction behavior is claimed from compilation.

Hosted Windows native acceptance is still required for initial Cancel focus,
Enter cancellation, dropdown navigation, selection/acceptance of all eight
entries, reselecting the placeholder, Escape/X cancellation, and leaving the
current document unchanged on cancellation. Failure injection is required to
prove control/population failure containment at an actual unmanaged callback
boundary. High-DPI layout and assistive-technology behavior remain unverified.

### Coordinated managed compilation

The integration owner's final coordinated Release test build included the frozen
Windows shell and chooser and completed with zero warnings and zero errors;
`NativePaintTraceTests` passed 11/11 with no failures or skips. Actual log:
`.cache/validation/explicit-open-encoding/native-paint-open-wrapper.log`.
The earlier controller qualification passed 166/166. A subsequent intermediate
build initially failed CS0103 while a concurrently published Program runtime
probe helper was missing; the integration owner resolved that publication race
before the final qualification. That initial failure is not credited as success
or treated as a chooser defect. No tests were rerun by the Windows UI owner.
These managed checks prove compilation and the scoped controller/paint contracts,
not native dialog interaction, AOT delivery, or Windows accessibility acceptance.
