# macOS owned deferred menu popup review

Date: 2026-10-01. Scope: current uncommitted owned-popup candidate in
`MacCsvGrid.Accessibility.cs`, `MacCsvGrid.cs`,
`MacCsvGridMenuPresentation.cs`, `MacCsvGridAccessibilityProbe.cs` and the
portable presentation tests. No production edits, commits or repeated tests.
The external protocol capture wrapper is reviewed separately.

## Conditional lifetime concern — statically addressed

**Priority: P1 before native acceptance; confidence: medium, not a reproduced
crash.** `AccessibilityPresentMenu` passes borrowed `menu` and physical table
handles into synchronous `popUpMenuPositioningItem:atLocation:inView:` without
holding an owned reference across its nested menu tracking loop. A reentrant
owner disposal cancels tracking but then immediately detaches/releases the
table and its configured menu while the outer native popup call is still on
the stack. A managed reference to `g` does not preserve released Objective-C
objects. No documented guarantee was found that NSMenu retains all these
borrowed parameters until the call returns.

Trigger: owner/window teardown during tracking, for example another native
callback or external accessibility close. Impact is conditional native
use-after-release if AppKit touches a released receiver/view on unwinding.
The current cancellation-only probe does not dispose the attachment during
tracking and therefore does not discriminate this path.

Practical correction: explicitly retain the existing menu and view across the
popup call, balance them in `finally`, and preserve current owner-lookup
retirement/cancellation behavior. Add target-only disposal-during-tracking
coverage. This does not require a second menu, focus transfer or a changed
source-command policy. The concern is an ownership-contract risk rather than
evidence that either current hosted target crashes.

Follow-up inspection: the candidate now captures the physical table locally,
retains the executing delegate (`self`), menu and table before tracking, and
releases table/menu/delegate in reverse order in its native-call `finally`.
The enclosing callback ends presentation state in a separate managed `finally`.
Reentrant disposal can clear adapter fields without
invalidating the native popup arguments or executing receiver. Disposal also
clears target and action for each existing menu item aimed at this adapter's
delegate, and clears the menu delegate before releasing that delegate. This
prevents a separately retained old menu from sending an action to a released
target or routing a now-targetless action through the responder chain.

The retained-detached-menu probe checks both nil delegate and nil target/action
for the established coordinate item. These changes statically address the
ownership and stale-target concern; no outstanding correction is requested.
Native disposal-during-tracking stress acceptance remains unclaimed and is
distinct from this static resolution. The owner reports the same 24/24 focused
portable tests passed after the fix; this reviewer did not repeat them.

Final narrow refactor inspected: the callback consumes pending state using
primitive attachment/installation facts before invoking fallible native
preflight. Its enclosing `finally` always ends presenting state even if fresh
native preflight returns false or throws, preventing a consumed callback from
leaving the attachment permanently queued/presenting. Native participants are
released only after popup return in the instance helper; the subsequent outer
`finally` touches the managed `g` state only, not the potentially released
delegate handle. No new lifetime hazard or outstanding finding was identified
in this refactor; no completed tests were repeated.

## Remaining inspected contracts

- Queue and presenting state are main-thread confined and per attachment. A
  pending request is single-use; an installation serial mismatch consumes it
  without presenting. Duplicate and reentrant scheduling are refused.
- Admission and dispatch both inspect live native attachment/window/menu and
  finite, positive physical visible bounds. The anchor is in the supplied
  view's coordinate system and not the entire document center.
- The typed byte-return/Point-argument import matches the native scalar and
  aggregate register classes. The popup result means selected versus cancelled,
  not visibility or action scheduling success.
- The AX callback returns queue admission before deferred tracking. The queued
  selector is owned by the existing delegate and explicitly cancelled by exact
  target/selector/object before disposal. There is no global event or mutation
  of first responder, application activation or source contents.
- Existing `menuWillOpen:` remains the sole command-freeze seam. The added
  shown-menu relation is published/cleared by actual matching delegate
  callbacks, not inferred from BOOL results. Selection and source authority
  still reside in the existing controller.
- The cancellation probe schedules the existing menu's `cancelTracking` in
  Default and EventTracking modes, checks exact observed open/close increments,
  frozen field identity, nil relation and unchanged responder. Later enclosing
  command-count assertions still detect unexpected source-command dispatch.
  It does not prove a deferred selector executes after the external client has
  received its reply under every possible AppKit nested run-loop path.

## Evidence and validation limits

Apple's
[popUpMenuPositioningItem:atLocation:inView:](https://developer.apple.com/documentation/appkit/nsmenu/popup%28positioning%3Aat%3Ain%3A%29?language=objc)
declares BOOL/NSPoint and defines the return as selection versus cancellation.
Its location is in the provided view's coordinates. The
[delayed selector reference](https://developer.apple.com/documentation/objectivec/nsobject-swift.class/perform%28_%3Awith%3Aafterdelay%3A%29?language=objc)
defines default-mode queued execution and explicit cancellation, including
zero-delay scheduling rather than an immediate call.

The integration owner reports 24/24 focused portable tests passing. This
reviewer did not repeat them and cannot execute AppKit on the Windows host.
Both Mac native ABI/tracking targets and the unchanged external oracle remain
required. No first-responder diagnostic value is treated as the cause of the
prior external discovery failure.
