# AppKit typed Flow rendering

Implementation: `MacFlowAttributes.cs`, `MacEditorShell.cs`, and
`MacFlowRenderingProbe.cs`. This is bounded native Flow delivery, not Grid,
images, external-link launch or complete product acceptance.

## Installation and interaction

The format-owned immutable Flow text is installed into the existing read-only,
selectable `NSTextView`. `NSTextStorage` receives native attributes directly,
inside paired `beginEditing`/`endEditing`; no HTML importer, browser, parser,
file access or resource loading exists in this path. Base attributes are cleared
before applying the new model, preventing an old italic/link/paragraph style
from leaking into a later projection. Fonts and colors are cached within one
bounded install. Paragraph depth is visually capped at 16 indentation steps
while the projection retains its full semantic depth.

Heading levels use decreasing theme UI-font scale, lists use native hanging
indents, quotes indent, and paragraphs use theme spacing/line height. Inline
strong/emphasis compose font traits; code uses the platform monospaced font.
Links receive accent and underline, **never `NSLink`**, so AppKit cannot bypass
the controller's explicit action/security model. Logical list markers come from
the projection text; Native does not reconstruct Markdown syntax.

Unchanged text is not replaced. Native selection and clip-view scroll origin
are restored after attribute changes. Normal native Copy and the `Mote preview`
accessibility label remain. Source ownership, IME guard, Save and Undo are
unchanged. An installed `NativePresentationId` is retained only after native
string readback matches the immutable view; source navigation carries that
retained sequence, rejecting a different same-version pending model.

`ShowPreview=false` detaches only the preview scroll view from `NSSplitView`,
expanding the source control. Its allocated native lifetime and prior split
fraction remain; showing it reattaches the same view and restores the fraction.
The shell consumes only this policy decision, never format names. Explicit
split compatibility remains the controller/configuration owner's responsibility.

## Reproducible target acceptance

From repository root on a disposable macOS host, using a published Native AOT
executable:

```powershell
./tests/NativeMacFlowRendering.ps1 -Executable ./.cache/publish/osx-arm64/mote
```

The one-argument `--check-native-mac-flow-rendering` probe opens an isolated
legacy AppKit shell with synthetic bounded text. It reads real native text,
font sizes/traits, paragraph indents, underline/no-NSLink, read-only/selectable
state, AX label and installed identity. It checks selection/scroll preservation
across a live theme change and same-version sequence replacement; source-only
expansion and split restoration; unchanged source. No controller parser result
is being verified by this synthetic probe: integration tests separately cover
format/controller delivery. No global settings, input sources, clipboard writes,
TCC grants or files are changed. A successful result prints
`mote-native-mac-flow-rendering-ready`.

The target probe also transitions the exact same text Flow → legacy Flow-null
→ Flow and checks that underline, paragraph indentation and font traits do not
leak into the legacy view, while native selection remains unchanged.

## Composition and synchronous layout reentrancy

All source profiles defer analysis installation during native marked-text
composition, not just Legacy Page. Deferred presentation is replayed only after
composition settlement and only if its document stamp still matches; no pane
resize or source focus change is allowed to disrupt the current candidate.
This extends the existing composition polling/settlement path and performs no
input-source mutations. The synthetic Flow target probe does not establish
Continuous real-IME acceptance; the existing composition/theme target probes
remain required evidence.

An installation guard prevents synchronous split/viewport callbacks from
recursively mutating the layout. Reentrant requests replace the pending model;
after layout the superseded outer install is abandoned and the newest matching
request is posted to the UI queue. Status and identity are not published for
the abandoned model. This keeps native text and its source map coherent even
when a split resize causes a same-version viewport request.

Host Windows Release warn-as-error compilation succeeded (zero warnings/errors).
**The macOS target probe has not been executed by the implementation worker.**
Both x64/ARM64 published target results, actual rendered pixels, external
selection/Copy, VoiceOver and ordinary Continuous canvas coexistence remain
distinct acceptance evidence; a build does not establish them.

## References

- [Apple text attributes](https://developer.apple.com/library/archive/documentation/TextFonts/Conceptual/CocoaTextArchitecture/TextAttributes/AboutTextAttributes.html)
- [Apple attributed-string mutation](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/AttributedStrings/Tasks/ChangingAttrStrings.html)
- [Apple beginEditing](https://developer.apple.com/documentation/foundation/nsmutableattributedstring/beginediting%28%29)
- [Native rendering architecture](native-rendering-architecture.md)
