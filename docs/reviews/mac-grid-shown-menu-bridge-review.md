# macOS semantic Table shown-menu bridge review

Date: 2026-10-01. Independent static review of the current uncommitted delta in
`src/Mote.Native/Mac/MacCsvGrid.Accessibility.cs` and
`src/Mote.Native/Mac/MacCsvGridAccessibilityProbe.cs` only.

## Result

No substantive defect found in this bounded getter bridge and relation-marker
probe. This is not a claim that native menu presentation or the failing external
menu-discovery assertion is repaired. AppKit execution was unavailable on this
Windows review host; no tests were rerun by this reviewer.

## Contract and ownership checks

- The semantic Table registers `accessibilityShownMenu` with the existing
  object-return `@@:` ABI, and forwards that exact getter selector to the
  physical NSTableView. It does not return the installed `menu` property or
  synthesize a presented menu.
- Both main-thread admission and live owner lookup precede physical-view access.
  Unexpected off-main callbacks return nil without reading the mutable owner
  dictionary. `DetachAccessibilityTable` removes the root lookup before release;
  disposal detaches this root before releasing the physical table. A separately
  retained detached proxy therefore cannot access the old physical view.
- Forwarding introduces no owned menu field, extra retain, retained proxy/view
  cycle, menu action, or source-edit permission. The normal getter's borrowed
  object remains owned by the physical view under the platform property contract.
- The probe explicitly retains the original relation before overwriting it,
  retains its newly allocated marker through the test, restores the original
  relation in `finally`, then balances both owned references. Messaging nil for
  the optional original relation is valid Objective-C behavior.
- Probe assertions distinguish exact physical/proxy marker identity, off-main
  refusal, explicit nil relation, and retained detached-root nil. They exercise
  selector wiring and lifecycle guards, not actual menu tracking/presentation.

## External evidence and remaining gate

Apple defines
[accessibilityShownMenu](https://developer.apple.com/documentation/appkit/nsaccessibility-c.protocol/accessibilityshownmenu?language=objc)
as the currently displayed menu and declares the property `strong, nullable`.
Its separate
[shownMenu attribute](https://developer.apple.com/documentation/appkit/nsaccessibility-swift.struct/attribute/shownmenu)
has the same current-display semantics. This supports forwarding the physical
view's transient relation rather than its installed context-menu configuration.

Still required: target execution on both macOS architectures, enclosing probe
exit and readiness evidence, and the unchanged external menu-discovery assertion.
A passing synthetic relation marker does not establish that AppKit populates
the physical relation during actual tracking, exports it across processes, or
that a screen reader can discover/operate the menu.
