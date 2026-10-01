# macOS embedded-NUL source bridge audit

Date: 2026-10-01. Status: **probe implemented and managed compilation passed;
AppKit execution pending independent safety review and target CI**.

## Scope and motivation

The separate Windows source audit found that RichEdit can lose the text tail after
an embedded NUL even with a length-aware import attempt. This is a release-blocking
source-integrity issue on that path, not evidence that AppKit shares the defect.
The AppKit bridge already constructs NSString using `stringWithCharacters:length:`
and reads it using `length` plus `getCharacters:range:`; managed readback explicitly
passes the UTF-16 length to `Marshal.PtrToStringUni`. The new audit exercises these
actual bridge and legacy NSTextView mechanisms rather than inferring correctness
from the existence of length parameters.

`MacSourceNulProbe.Run()` uses one fresh `MacEditorShell(experimentalCanvas: false)`
and one native AppKit event loop. It does not repeatedly restart NSApplication.
The parent's opt-in route is `--check-native-mac-source-nul`. Only a successful
exit may publish the corresponding ready marker.

## Discriminating fixtures and contracts

| In-memory fixture | Distinction exercised |
| --- | --- |
| `a\0b` | Embedded NUL followed by ordinary text |
| `"a\0b",tail\r\n` | Embedded quoted CSV field, following field and CRLF tail |
| `\0␀\0` | Actual NUL next to literal U+2400, including terminal NUL |
| `␀\0` | Literal control-picture glyph must not replace actual EOF NUL |
| `\0` | Single NUL document |
| `a\0b😀\r\n␀\0` | NUL, supplementary Unicode, exact CRLF and terminal NUL |

For each fixture the probe asserts:

1. NSString reports the complete source UTF-16 length and the production bridge
   reads back the exact source (actual U+0000 remains distinct from literal U+2400).
2. The legacy source control imported through `SetDocument` reads back that exact
   string and length, and unchanged native text produces no projected Engine edit.
3. Existing `ProbeInsertAtStart("x")` calls NSTextView's actual
   `insertText:replacementRange:`. Its native text and delivered `TextChanged`
   payload must equal `"x" + source`, including the complete tail after NUL.
4. Production `NativeTextProjection.Difference` yields only insertion at zero,
   with no deletion. Applying that observed change to an unsaved `Document`
   preserves exact text; Engine Undo/Redo recover the original/edited fixture.

The emitted coverage line explicitly says `mode=legacy` and
`save-reopen=not-tested`. No success claim is made before target execution.

## Safety boundary

- Source strings and Engine documents are entirely in-memory and small.
- No file picker, file I/O, config loader, project/workspace, Save, Save As, reload
  or network path is invoked.
- No NSPasteboard access/publication; source Copy with NUL is **not** certified by
  this audit and remains subject to the existing publication refusal policy.
- No TCC request, input-source preference change, external key/mouse events,
  process injection, reflection, private controller access or helper sidecars.
- Insertion is an in-process native text-input method on the probe's own fresh
  control. Closing its own probe window does not ask to save any Engine document.
- Existing Grid probe and frozen Mac production adapter files are unchanged.

## Evidence actually obtained

On the Windows development host:

`dotnet build src/Mote.Native/Mote.Native.csproj -c Release --no-restore
-warnaserror -v minimal` — **passed, 0 warnings and 0 errors**.

This only confirms managed compilation. `MacSourceNulProbe.cs` is ready for an
independent safety hash and explicit macOS x64/arm64 published-binary execution.
There is no local AppKit, Native AOT target, physical keyboard, IME, clipboard,
full controller or Save/reopen result here.

## Remaining work

- Target CI must execute this narrow legacy bridge/native-input/projection/Engine
  audit. A failure is source-integrity evidence and must not be bypassed by
  weakening tail-preservation checks.
- Root owns separate controlled target Save/reopen evidence. This probe cannot
  certify persistent bytes because it deliberately performs no filesystem I/O.
- Continuous canvas input binding is not exercised: it requires its actual typed
  binding/frame/lifetime contracts, not copying legacy `SetDocument` into a canvas
  shell and claiming equivalent coverage.
