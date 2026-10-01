# macOS menu selector-permission diagnostic review

Date: 2026-10-01. Independent static review of the current narrow delta in
`MacCsvGrid.Accessibility.cs`, `MacCsvGrid.cs`,
`MacCsvGridMenuDiagnostic.cs`, and `MacGridMenuDiagnosticTests.cs`.
The separately owned strict capture wrapper is outside this review.

## Result

No substantive issue found in this read-only observation change. No production
file was edited and no completed test was repeated. The integration owner
reports 11/11 focused portable diagnostic tests passed; these are not AppKit
execution or external action acceptance.

## Checked contracts

- The new `objc_msgSend` declaration uses a one-byte BOOL return with receiver,
  dispatch selector and exact subject SEL arguments. It does not interpret a
  pointer-width return as BOOL or marshal the SEL as source text.
- Both calls query the current semantic proxy, not a physical implementation
  view or a retained old root. Main-thread admission, dual opt-in gating and
  the existing process-wide 16-event budget precede the native reads.
- A disposed attachment has a zero root; Objective-C messaging nil produces
  zero facts rather than reaching released native state. Such false facts
  must not be interpreted as proof of an attached live root denying access.
- The calls do not override selector permission, invoke the show-menu action,
  open/close a menu, change focus or authorize a source operation. Existing
  diagnostic exception containment still prevents managed observation/output
  faults from changing menu behavior.
- `allowaction` and `allowshown` are independent fixed 0/1 fields. The formatter
  appends only fixed ASCII names and boolean digits, preserving bounded output
  and existing phase/result semantics. All seven admitted phases and four
  permission combinations are covered by the added portable assertions.
- Permission observations do not mutate lifecycle counters or infer that a
  menu was displayed, that an external action succeeded, or why an external
  call failed. The old native-return phase remains a valid diagnostic phase
  but its preservation does not synthesize native callback evidence.

## Evidence and limits

Apple's
[isAccessibilitySelectorAllowed: reference](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol/isaccessibilityselectorallowed%28_%3A%29?language=objc)
declares `BOOL` and `SEL` and defines the result as whether accessibility
clients may invoke that selector on the element. Reading this method is not
equivalent to overriding its result or proving the external transport path.

Both hosted targets must supply actual permission observations before any
permission-policy change is proposed. This review makes no modern-versus-Carbon
wire-key distinction claim; the owner reports that hypothesis was falsified by
both targets at `833ef48`. Native ABI execution, external action error codes,
real menu relation and child discovery remain separate evidence.
