# macOS external menu-action failure discriminator review

Date: 2026-10-01. Scope: `9a723b0`'s test-only
`tests/MacGridAxExternalProbe/Probe.swift` delta. Product menu/source behavior,
the external action assertion, existing bounds and wrapper cleanup remain
unchanged. Windows static review; no native Swift typecheck or new execution
was performed by this reviewer.

## Result

No substantive defect found in the bounded read-only discriminator. It is
appropriate to pin/run this exact candidate, not to claim acceptance from it.
The observed action error and newly observed metadata must remain independent.

## Checked behavior

- The actual `AXUIElementPerformAction(table, "AXShowMenu")` call is unchanged.
  Its original error is retained in `menuError`. A non-success result admits
  only the diagnostic branch, then still records/fails the original
  `context-menu-accessible` assertion even if followup metadata looks correct.
- The failure branch neither retries an action nor presses a menu item, edits
  a value, waits for readiness, changes source selection or moves to logical
  navigation. It makes one bounded tree pass. Diagnostic exceptions are
  classified separately, restore the original phase, and cannot convert the
  failed action into success or erase its falsifier.
- `AXUIElementCopyActionNames` follows exact-PID and per-call-timeout admission.
  This API has no count-before-copy form; the code explicitly acknowledges
  that limitation, validates the returned array count at 32 before bridging
  or examining strings, and retains only error/count/fixed constant membership.
  It does not dump names or claim the framework copy itself is hard-sized.
- The helper obtains the modern shown-menu key from
  `NSAccessibility.Attribute.shownMenu.rawValue`, labels the actual key in each
  observation, and preserves the older Carbon relationship under separate
  fixed root categories. It does not treat an unsupported old attribute as
  proof that the modern getter is absent.
- A relation value is interpreted as an AX element only after CF type-tag
  validation. Its PID must equal the already launched editor PID before role
  or child data is queried. Child enumeration is count-checked at 128 and
  independently applies the existing per-node ownership/admission checks.
  Only the exact existing coordinate-command title's count is persisted;
  no arbitrary menu title, source value, identifier or desktop data is logged.
- Arrays remain diagnostic count/type observations bounded at eight; they are
  not treated as a verified owned single menu. The existing tree still prunes
  Table/Row/Column children and enforces the 256-node cap. Its exact role/name
  comparison is unchanged.
- Global 12,000 admissions, 55-second lifetime, timeout and wrapper forced
  cleanup are not increased. On a followup gate failure, the preserved original
  assertion still fails without another API query.

## Primary contracts and pending target validation

Apple documents the modern
[shownMenu](https://developer.apple.com/documentation/appkit/nsaccessibility-swift.struct/attribute/shownmenu)
as the current single menu, and the separate Carbon
[shown-menu UI element relationship](https://developer.apple.com/documentation/applicationservices/kaxshownmenuuielementattribute)
as contextual/Dock-menu objects. The
[modern showMenu action constant](https://developer.apple.com/documentation/appkit/nsaccessibility-swift.struct/action/showmenu)
and
[AXUIElementCopyActionNames](https://developer.apple.com/documentation/applicationservices/axuielementcopyactionnames(_:_:))
provide authoritative identifiers and read-only action discovery; they do not
establish that a callback or popup proves successful external acknowledgement.

Native Swift/AppKit typecheck and actual two-RID execution remain required.
Expected useful output is action-name membership, modern/legacy relationship
type/error/ownership, and exact coordinate-item counts while preserving the
original failed action verdict. No legacy product action-name override or
proxy-backed-menu setter change is justified before these observations.
