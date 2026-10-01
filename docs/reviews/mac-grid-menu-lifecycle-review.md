# macOS menu lifecycle diagnostic and BOOL bridge review

Date: 2026-10-01. Scope: the uncommitted menu lifecycle delta in
`MacCsvGridMenuDiagnostic.cs`, `MacCsvGrid.cs`, `MacCsvGrid.Accessibility.cs`,
and `MacGridMenuDiagnosticTests.cs`. The external capture wrapper and unrelated
working changes are excluded. No production edits or test reruns by this reviewer.

## Result

No demonstrated substantive defect found in the bounded diagnostic path or
one-byte native result bridge. Target AppKit execution remains pending; portable
tests do not establish menu display or external accessibility behavior.

## Checked contracts

- The two opt-ins gate one process-shared collector. Every production
  `TryNext` call follows the main-thread check, so the unsynchronized counter is
  main-thread confined. Unknown phases and an exhausted 16-event budget return
  before mutating counters or inspecting native state. All admitted counter
  values are at most 16.
- `TraceMenu` catches its own managed diagnostic exceptions, including native
  fact conversion, formatting and output faults. Its call precedes existing
  menu capture but a diagnostic exception cannot skip that capture or the
  physical menu action. It does not install, open, close, activate, focus or
  alter a menu/window. Fatal native faults are not claimed recoverable by a
  managed catch.
- Native reads are main-thread bounded to the existing table/menu/window/app;
  at most 16 configured menu items are scanned. The collector retains no native
  handle. The menu delegate resolves the existing owner entry; detached owners
  have no lookup. The proxy show action still admits only live, non-installing
  owners with an installed semantic frame.
- Protocol output contains fixed ASCII names, invariant bounded numeric fields
  and booleans, not the inspected title or source/path/identity. The ordinal
  title comparison only supplies the fixed coordinate-command boolean.
- Request, delegate-open and delegate-close counts remain independent. Neither
  a successful nor unsuccessful native return changes the last observed
  delegate transition. The `open` field is explicitly a process-wide last
  observation, not a per-menu live visibility guarantee.
- The direct `objc_msgSend` import returns `byte`, capturing only the native
  BOOL-sized result before nonzero normalization. Apple documents Intel macOS
  BOOL as signed char and Apple silicon BOOL as native bool; both are one-byte
  return values for these platform APIs. This is an ABI correction, not evidence
  that the former pointer-width interpretation caused the observed external
  discovery failure.
- The six reported portable cases cover non-inferred transitions for both
  native results, asynchronous/synchronous callback order, rejected phases and
  budget exhaustion, invariant protocol output/format refusal, and byte-return
  declaration metadata. Reflection checks do not execute the native ABI.

## Minor robustness note — closed

The initial candidate placed the collector initializer in another partial
declaration from `AccessibilityEnabled`. Current evaluated MSBuild Compile
items already used the needed order, so this was not a demonstrated failure.
The integration owner subsequently moved `MenuDiagnostic` immediately after
`AccessibilityEnabled` in `MacCsvGrid.Accessibility.cs`. Inspection confirms
the dependent initializers now share an explicit declaration order and the
old declaration is absent from `MacCsvGrid.cs`. The suggestion is closed;
this field relocation changes neither protocol nor menu behavior and does
not warrant repeating completed tests.

## Evidence and limits

Official Apple reference:
[Addressing architectural differences in your macOS code](https://developer.apple.com/documentation/apple-silicon/addressing-architectural-differences-in-your-macos-code),
section “Treat BOOL Variables as Binary Values”; and
[Writing ARM64 code for Apple platforms](https://developer.apple.com/documentation/xcode/writing-arm64-code-for-apple-platforms),
the BOOL/bool one-byte size entry.

Review host: Windows. Native action/delegate ordering and values must be
observed on both macOS RIDs using the unchanged external oracle. The strict
capture whitelist is separately reviewed by its owner. No test result here
supersedes the enclosing native probe exit, external assertion, or actual
screen-reader acceptance.
