# macOS opt-in Grid frozen-menu regression review

Date: 2026-10-01. Scope: static, failure-directed review of MacCsvGrid CaptureIntent/menu freeze, MacCsvGridProbe sequence, accessibility focused coordinate publication and controller guards. No production/test/workflow/README/release-gap edits. Parent reports hosted CI run 36780934192 at f76c56e failed both OSX ABIs only in opt-in probe after AX selector checks succeeded; this reviewer did not fetch or execute that run. Current checkout HEAD b285203834b28d963659da2e2ece5b32f97a8ff2 retains the same CaptureIntent branch as f76c56e (checked with git show).

## P2 / high confidence: focused intent capture adds an incompatible Missing/origin filter

Location: `src/Mote.Native/Mac/MacCsvGrid.cs:505-511`, `CaptureIntent` accessibility-focused Reveal/Replace branch. Related frozen consumers: `MenuWillOpen:859-860`, `MenuCommand:936-941`. Failing probe: `MacCsvGridProbe.cs:85-99`.

The new focused branch accepts only a descriptor with SourceRange and state neither Pending nor Missing. The established native keyboard/menu capture path accepts every delivered non-Pending descriptor and leaves source-origin and command-specific refusal to the controller. Enabling AX therefore changes existing native menu/keyboard behavior even without an AX action request.

### Exact failure chain

1. Probe synthetic rows explicitly include column 2 Missing with null SourceRange; requested columns include 0..2.
2. Native right moves to column 1; Shift-right selects rectangle 1..2, active column 2. Copy dispatches CopyTsv ending at 2.
3. Presentation sequence 2 keeps the same document and restores active/native selected cell at row 0, column 2. Accessibility publication falls back to retained selection.Active when the native Table is firstResponder.
4. Shared Create removes Pending focused cells, not Missing. This is intentional: Missing is a valid bounded layout cell, not an unready slot.
5. MenuWillOpen captures selection successfully but `_menuCell = CaptureIntent(Reveal)` becomes null solely because the focused descriptor is Missing/null-origin.
6. Sequence 3 installs before invoking the frozen Reveal item. MenuCommand sees null `_menuCell` and emits no callback. The last observed intent remains sequence 2 CopyTsv, not the expected sequence 2 single-cell Reveal at column 2. This exactly explains the reported assertion, rather than an ABI or selector-return failure.

`_menuCell` also supplies single-cell Replace/CopyValue/CopySource actions. One Reveal-specific prefilter therefore suppresses other frozen menu dispatches and their existing controller feedback. Fresh native Return on Missing becomes silent under opt-in instead of delivering a refusal through the existing controller path.

## Minimal compatibility-preserving correction

For existing native focused keyboard/menu intent capture, retain the focused-coordinate routing but require only an existing delivered descriptor with `State: not GridValueState.Pending`. Remove SourceRange and Missing exclusions from this branch. Do not fall back to selection B when focused cell A is unavailable; continue refusing null/Pending focused descriptors.

This preserves the same admitted capture domain as the legacy path without changing the controller or exposing source commands through AX. `NativeEditorController.Grid.cs:81-85` already refuses Reveal with no proved SourceRange and displays an error. Replace requires Complete/syntax-valid state at lines 128-134; Copy preparation has its own bounded value/origin policy. `CurrentGrid(intent.Identity)` rejects the sequence-2 menu intent after sequence 3, so frozen dispatch is not successful source-command admission. The probe assertion tests intent identity/coordinate freezing, not a completed Reveal.

The existing CSV accessibility contract explicitly retains established keyboard/menu behavior and omits AX press/Invoke until controller acknowledgment exists. A captured native intent is not an advertised AX source action. Keeping rejection at the established controller boundary preserves user feedback and avoids a second, inconsistent command-policy owner.

## Discriminating target checks

- Run the existing native CSV probe with opt-in=1 and without it on both OSX architectures: menu capture at Missing active column 2, install sequence 3, invoke frozen item, require exactly one sequence-2 Reveal callback at column 2 with EndColumn=null. Assert callback count as well as last intent so absence cannot masquerade as success.
- Add focused Missing A versus unrelated Complete selected B: native Return/Command-Return and frozen single-cell menu actions must address A under the established non-Pending capture policy, not B. AX cell source actions remain absent.
- Keep a controller-level Missing Reveal control: same-current identity produces refusal/error and no source selection/focus/edit/history/clipboard change; stale frozen identity is refused before document access. Do not count raw callback emission as source action success.
- Keep delivered Pending/null descriptor refusal and valid Complete focused-A routing as contrasting controls. No full reader/IME task or new AX action protocol is required to isolate this regression.

No runtime acceptance is claimed. The production cached-frame precedent and reader-orientation motivation already documented in the accessibility contract are unchanged; this failure is a concrete compatibility-policy mismatch, not evidence against bounded table architecture.

## Targeted fix review

Inspected final working-tree delta: CaptureFocusedIntent now performs bounded descriptor lookup and admits exactly non-Pending descriptors, preserving the frozen identity/kind/absolute coordinate without claiming origin or source-command completion. CaptureIntent invokes it only for focused native Reveal/Replace after the existing installation/ready guards. ProbeMenuCell is diagnostic readback of the menu-opening token, not a source-action receipt. The original P2 compatibility defect is resolved by static inspection.

The native probe now asserts Missing semantic focus before opening, exactly two sequence-2 native Return/Command-Return callbacks, the menu snapshot before replacement and exactly one frozen Reveal callback after sequence 3. These assertions discriminate the original null-snapshot/no-callback failure and do not reinterpret stale intent emission as successful Reveal. The managed theory contrasts Complete/Missing/Oversized with sparse/absent descriptor refusal for both kinds; it directly fails the original Missing prefilter.

Moving the nested AX probe after the complete frozen-menu section improves isolation: its separate temporary grid and responder transfers cannot explain the menu admission failure. The nested grid owns its own command list and navigation and does not mutate the outer shell Grid projection or callbacks. Restoring the original native table as firstResponder immediately afterward preserves the later Edit menu SelectAll/Copy routing, read-only Cut/Paste checks and navigation checks. No concrete isolation defect was found in the reordered sequence. The original probe later resets the document and verifies source invalidation, so the fix does not skip its post-menu native flows.

No remaining substantive issue was found in this bounded fix. Runtime/hosted success is not claimed: this reviewer performed static code/test review only; both OSX opt-in target probes still require fresh execution on the corrected binary. Unrelated AX external reader/IME gates and source acknowledgment remain unchanged. Relevant working-tree diff check passed; no production or test files were edited by reviewer.
