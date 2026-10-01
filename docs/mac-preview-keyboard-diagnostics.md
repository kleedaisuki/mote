# macOS preview keyboard delivery investigation

## Resolved synthetic-gesture result

Published Native AOT run [36718845829](https://github.com/kleedaisuki/mote/actions/runs/36718845829)
at `f2b86862ba3e13e0029d2c3d4d830fff9ca5e49a` passed **8/8** scoped
preview-navigation reports: Continuous Return/pointer and LegacyPage
Space/pointer on each of `osx-x64` and `osx-arm64`. The reports were inspected,
not inferred from a green non-gating job. All eight observed source caret
`0 -> 9`, returned source focus, unchanged fixture SHA-256, and no observed
mutation/dirty state after Save and Undo shortcuts. The four keyboard reports
first observed Right Arrow move the preview caret `9 -> 10` while source
selection remained zero, then reset the preview caret before activation.

All four Right-arrow key-down/up events were created with flags `548405248`
(`0x20B00000`, including the Command bit), but posted with flags `0` after the
probe explicitly cleared inherited modifiers. The activation events were also
posted with flags `0`. This discriminating harness-only change corrected both
the prior word-end movement and the blocked activation, strongly supporting
inherited synthetic-event modifiers as the cause. The prior run did not record
its flags, so its exact modifier state is not directly proven. No production
keyboard handler was changed to obtain this result.

Local evidence: `.cache/mac-preview-navigation-run-36718845829/{osx-x64,osx-arm64}/mac-preview-navigation-*.json`.
Reproduction uses `tests/NativeMacPreviewNavigation.ps1` with the matching
published executable, once per profile/gesture as documented in the probe
README. This establishes bounded external AX/Quartz synthetic navigation only:
it does not establish physical-keyboard input, real IME composition, VoiceOver
speech, viewport/version state, or paint latency. `preview_read_only` is `null`
in all eight reports because AXEditable is unavailable; the separate in-process
AppKit `isEditable == false` assertion remains the read-only evidence. Save/Undo
nonmutation observations do not independently prove their command handlers ran.

The following sections retain the earlier negative evidence and escalation
plan for future delivery failures; they are not current release blockers for
this now-passing synthetic preview gesture.

## Evidence and scope

Hosted `osx-x64` and `osx-arm64` run [36710621980](https://github.com/kleedaisuki/mote/actions/runs/36710621980)
passed preview pointer navigation in both Continuous and LegacyPage profiles.
Return in Continuous and Space in LegacyPage timed out at activation. Reports
are retained under `.cache/mac-preview-navigation-run-36710621980-x64/` and
`.cache/mac-preview-navigation-run-36710621980-arm/`. Both RIDs have the same
four-case pass/fail pattern.
The target process remained frontmost, preview AX-focused, source selection at
zero, source AX-unfocused, and window clean. These observations do not prove
that a key-down event reached the preview's AppKit first responder.

In those reports `preview_range_length=11` is the heading marker's range length,
not the preview's `selectedRange`. It therefore does not prove either a collapsed
caret or an accidental selection. Current validator changes expose separate
preview selection fields and check a Right-arrow control before activation;
that new validation has not been assessed here.

## Static path assessment

`MacEditorShell.RegisterPreviewNavigationClass` installs a `keyDown:` IMP on the
same preview subclass whose `mouseDown:` path passed. `PreviewKeyDown` accepts
Space, CR, and LF without Command/Option/Control; other events go to NSTextView.
It invokes the same `ActivatePreview` as pointer activation. That method checks
the applied preview stamp/current analysis and requires a collapsed selection
inside the preview text. Controller mapping, version guards, and focus routing
are shared with the passing pointer path.

This narrows, but does not eliminate, production faults. Discriminators:

| Observation | Next interpretation |
| --- | --- |
| Right-arrow does not move preview caret | AX focus or Quartz delivery is not enough; do not blame activation mapping. |
| Right-arrow passes, Enter/Space does not | Inspect preview `keyDown:` entry, modifier mask/key classification, and selection/stamp guards. |
| Preview IMP entered, no activation event emitted | A guard rejected it or an exception was swallowed by the native callback boundary. |
| Activation emitted, source unchanged | Investigate controller rejection independently from input delivery. |

Both Space and Return failing makes a CR/LF-only mismatch an insufficient common
explanation. No observed event flags, native key classification, actual window
first responder, or guard-rejection reason is yet available. `AXFocused=true`
is not a direct measurement of `NSWindow.firstResponder`.

## Minimal safe diagnostic if the external control is insufficient

No production or validator edits were made for this investigation. A bounded
published-binary in-process probe can separate handler correctness from hosted
global event delivery:

1. On the AppKit UI thread, set the known fixture preview's collapsed
   `selectedRange`, call `makeFirstResponder:` with the preview, and assert the
   window's actual `firstResponder` identity and unchanged source selection.
2. Construct a real `NSEvent` using AppKit's
   `keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:characters:charactersIgnoringModifiers:isARepeat:keyCode:`
   with key-down type, zero modifiers, matching window number, CR/keycode 36 or
   Space/keycode 49. Use an accurately typed Objective-C interop declaration;
   do not approximate this mixed scalar/structure signature using pointer-only
   overloads. Keep the factory declaration confined to the diagnostic.
3. First send that event directly to the preview's `keyDown:`. This exercises
   the installed product IMP and controller, not a direct `ActivatePreview`
   call. Require exact source caret/focus and unchanged source bytes/dirty state.
4. Reset source and preview state, then send an equivalent event through
   `NSApplication.sendEvent:` or `NSWindow.sendEvent:`. Compare the result to
   direct dispatch, explicitly distinguishing responder routing from the IMP.
5. If necessary, add opt-in scalar diagnostic counters only: preview IMP entry,
   activation-key classification, modifier-blocked boolean, collapsed-selection
   boolean, stamp-valid boolean, activation-emitted count, callback-exception
   count, and first-responder-is-preview boolean. Never log arbitrary event
   characters or document text. Do not change normal event behavior or add
   fallback activation merely to make a hosted test pass.

An in-process event pass establishes the native handler/routing contract, not
real keyboard, VoiceOver, or IME acceptance. The existing legacy workflow uses
NSTextInputClient insertion/marked-text selectors, and the Canvas AX probe checks
preview identity/read-only state; neither presently sends preview key NSEvents.

## Platform references

- [Apple NSWindow.firstResponder](https://developer.apple.com/documentation/appkit/nswindow/firstresponder)
- [Apple NSEvent](https://developer.apple.com/documentation/appkit/nsevent)
- [Apple event architecture](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/EventOverview/EventArchitecture/EventArchitecture.html)
- [Apple NSTextView subclassing guidance](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/TextEditing/Tasks/Subclassing.html)
