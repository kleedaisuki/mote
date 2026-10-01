# Windows preview access-violation diagnostic regression review

## Scope and verdict

Reviewed the working-tree diff in `tests/Mote.Tests/NativeThemeOverrideWindowsTests.cs` only, with surrounding `WindowsEditorShell.SetTheme`, `InstallPreview`, `ImportPreviewPayload`, and `WindowsRichEditUndoScope` read to establish the exercised contract. No production edits, commits, or additional execution were performed by this review.

**No substantive defect found in the scoped diagnostic change.** This is additional diagnostic/regression coverage, not a demonstrated correction of the access violation reported in CI run 36800944850. A passing run cannot establish its cause or prove that the intermittent failure is eliminated.

## Checks

- `AssertOwnedRichEdit` checks the current native thread ID against the HWND owner thread, the owner PID against this process, and the exact RichEdit class before entering the theme/import call. Invalid handles fail the thread/PID checks rather than silently authorizing import. These synchronous hidden-fixture checks neither acquire a lifetime lock nor claim to do so.
- The new P/Invoke declarations match the required ABI: thread/process IDs are 32-bit unsigned values, HWND is pointer-sized, `GetClassNameW` receives an explicitly Unicode `StringBuilder` and character capacity, and its count result is 32-bit signed. Default Windows calling conventions are appropriate here. Failure diagnostics do not depend on last-error values.
- The semantic fixture has matching document/analysis stamps and unchanged style/source identity, so the added calls reach `InstallPreview` and its literal `SetWindowTextW` import rather than only updating background colors. Eight alternating original/composed policies remain distinct consecutive changes, including the same-ID override case.
- Each iteration verifies exact source text, exact preview payload, and source selection. The existing assertions after the loop exercise the retained native undo unit, native redo branch, resumed recording after nested exceptional suspension, and unchanged engine snapshot/history before the explicit engine Undo command.
- Handles remain on their creating thread and in the creating process. The change adds no global input, foreground manipulation, clipboard access, external process operation, or operating-system configuration mutation. Existing finally cleanup remains in place.
- Explicit collection/finalizer waiting perturbs the test process and can modestly increase total test time, but this is bounded to eight semantic iterations. It does not introduce a new fixture-owned finalizer or cross-thread HWND operation.

## Evidence limits

Collection happens **before** each synchronous import, not while a newly marshalled string is inside the native call. This exercises repeated import and between-call lifetime pressure; it is not a direct payload-lifetime or native reference-count proof. Likewise, preserved history is functional evidence of suspension behavior, not independent instrumentation of each COM AddRef/Release. No comment in the change asserts that the root cause was found or fixed.

A fresh targeted/full Windows result and the parent's crash-dump investigation should determine the next step. Retain the unresolved-crash classification if those runs pass without a causal reproducer. If the crash recurs, the new preconditions can distinguish an invalid/foreign/wrong-class fixture from a failure after valid owner-thread checks, but cannot by themselves identify corruption inside RichEdit or preceding native operations.
