# Windows native source NUL fix: independent review

## Scope and state

Review of `WindowsEditorShell`, `WindowsRichEditIsland` and the shared
`WindowsNativeSourceSafety` helper against the confirmed source truncation in
`native-source-nul-review.md`. Grid rendering/commands are outside this review,
except focus routing must not expose hidden source mutation. Production code is
owned by the Windows adapter worker; this reviewer changes this artifact only.
This is an interim review of the working tree, not frozen approval.

## Confirmed design properties

- U+0000 becomes U+2400 only in a read-only bounded native source projection.
  There is deliberately no ambiguous full-string inverse. Literal U+2400 with
  no source NUL remains editable ordinary source.
- Source callbacks refuse read-only text edits; programmatic native mutation is
  restored from an already prepared safe display rather than committed.
- Both hosts verify imported native text against the exact intended display
  before installing editing authority. Readback uses the explicit returned
  UTF-16 count and a zero-initialized buffer, not NUL-terminated conversion.
- Continuous global-selection deletion and paste have explicit read-only guards.
  Shell Cut routes are guarded. Engine undo/redo and Save remain Engine commands.
- No whole-document mirror is added: established source has its bounded page;
  Continuous source has its existing exact bounded input interval.

## Findings sent to owner

### P1: exception during Continuous import verification retains editing authority

`WindowsRichEditIsland.Bind` assigns `_binding`, sets `_inputReadOnly` from source
NUL, then calls `ReadInputText()` inside import verification. If the helper throws
its buffer-validity exception for an otherwise non-NUL binding, the mismatch
branch does not execute. `finally` consequently sets native read-only false and
leaves the binding installed, although exact import was never certified. This
violates fail-closed authority at the explicitly defended native failure boundary.
Clear the binding and force read-only in a catch covering import/readback before
rethrowing, and report unavailable input honestly. This is a conditional failure
path established by code inspection; it is not a physical-device reproduction.

### P1: equal-display fast path ignores failed native-map certification

`WindowsEditorShell.SetDocumentCore` resets read-only and notice based solely on
new source NUL, then takes `_visibleText == nativeText` shortcut irrespective of
`_sourceMapInstalled`. After a readback failure this can unlock the native widget
and clear the failure notice while the map is still uncertified; edits are then
silently ignored by the callback. Require a certified map for the fast path, so
normal reinstallation verifies readback and recovers authority atomically.

## Verification limits

The independent validator owns hidden-HWND regression executions and exact file
Save/reopen evidence. This reviewer has not repeated accepted Grid or source
regression suites. Physical keyboard/IME, visible UI, Narrator and cross-RID AOT
are not inferred from code inspection or hidden-control tests. Final approval
requires the Windows writer's frozen file hashes and disposition of the findings.

## Final disposition (2026-10-01)

**Approved for the scoped Windows P0 safety fix; no outstanding material finding
in the reviewed frozen source hunks.** Both P1 draft findings above are resolved:
`Bind` now catches failed import/readback, retires the binding, forces read-only
and emits an unavailable-input notice before rethrow; the shell equal-display
fast path requires `_sourceMapInstalled` and otherwise recertifies import.

The validator independently reproduced the old shell equality bug (18 accepted
ordinary cases plus one failing recovery case) and then accepted three targeted
failure/recovery cases after the correction. Its final evidence is **21 distinct
passing cases**, not a newly rerun 21-test suite: see `native-source-nul-review.md`.
This includes both NUL-to-literal-marker same-display transitions, exact UTF-8
Engine Save/reopen, and Continuous binding retirement on failed import. A thrown
reader exception is covered by the corrected catch in code review, not a separate
injected exception execution. Existing Engine undo/redo command dispatch remains
unchanged; native undo/redo inertness is tested, not an independent Engine history
acceptance exercise.

The owner note is actually `docs/reviews/native-source-nul-native-import.md`.
Reviewed its concrete `SF_TEXT | SF_UNICODE` streaming negative result: successful
byte import/count does not retain source suffix after NUL, and zero-filled explicit
readback prevents uninitialized tails becoming source. This supports the scoped
read-only decision; it does not establish universal RichEdit behavior across
versions or an editable NUL solution.

### Frozen working-tree SHA-256 ledger

The shell hash includes separately reviewed concurrent Grid changes; approval
here covers source safety hunks only, not those unrelated Grid changes.

| File | SHA-256 |
| --- | --- |
| `src/Mote.Native/Windows/WindowsNativeSourceSafety.cs` | `6B2DE5452F5D3472F6728DCDC3BD9161E74E22D4D1465C15D9642CE9ACBC73FB` |
| `src/Mote.Native/Windows/WindowsEditorShell.cs` | `7981FE25891A289C4203EDD8639C99FB1E90A7F44ED399D2B0AC8B6859B16161` |
| `src/Mote.Native/Windows/Canvas/WindowsRichEditIsland.cs` | `59DDCBBCC27461069732DD497657D87B2AAC7A7945E0D29FBE5B45BF96564FFE` |
| `.temp/source-nul-p0.patch` | `F143D35FFECA1018ED178B59AE130C3D036D9AA77D7B963FC8FE6492CE7EDD22` |

Inspected the P0-only extracted patch against baseline HEAD: its shell Cut guard
retains source safety without introducing Grid-type dependencies, and its source
host notice wiring is retained. Parent/extraction owner owns cached-apply and
compile validation of that isolated commit; this review does not claim to have
built the extracted patch independently. No production edits or redundant test
runs were performed by this reviewer.

## Narrow delta review: standalone controller clipboard guard

The P0-only patch now includes four production files. The additional six-line
`NativeEditorController` guard rejects a selected string containing U+0000 before
`SetClipboardText` publication and before the Cut `ApplyTraced` deletion. It is
inside the shared Copy/Cut asynchronous completion lane, after existing stale
selection/document and composition checks. Neither refusal changes canonical
source nor attempts to publish a NUL-terminated partial clipboard string. Literal
U+2400 remains unaffected. The baseline-only controller variant contains no Grid
references introduced by this change. No material defect found in this delta;
standalone controller guard tests are owned by the validator.

The shell's later callback refinement also passes review: a read-only
`OnTextChanged` restores a display only when `_sourceMapInstalled` was certified.
A failed newer import cannot reinstall the preceding page and regain offset
certification on a late notification. The validator accepted one targeted new
late-notification case; accepted Windows host coverage is now **22 distinct cases**.

### Updated freeze ledger (supersedes earlier shell and patch hashes)

| File | SHA-256 |
| --- | --- |
| `src/Mote.Native/Windows/WindowsNativeSourceSafety.cs` | `6B2DE5452F5D3472F6728DCDC3BD9161E74E22D4D1465C15D9642CE9ACBC73FB` |
| `src/Mote.Native/Windows/WindowsEditorShell.cs` | `980A9284EF94D656985A01C8AEAD8CF589C60EC3C3E61754333F37D360B3FAA1` |
| `src/Mote.Native/Windows/Canvas/WindowsRichEditIsland.cs` | `59DDCBBCC27461069732DD497657D87B2AAC7A7945E0D29FBE5B45BF96564FFE` |
| `src/Mote.Native/NativeEditorController.cs` (working tree, includes separate Grid work) | `D9FE329A5C9250988F0524D86318B2E2D329AB822158DA87E301E744BE80B09B` |
| `.temp/source-nul-p0/NativeEditorController.cs` (baseline-only authoritative extracted variant) | `B07BB89BB2F303E92EF24D6A85B25CE912541A7AB5BA4447786835524116FDEF` |
| `.temp/source-nul-p0.patch` (four production files) | `53674AC7EEE4441F57C9049FD7B1774ADD8B7EFD3F95E94FB863792CDBB34EBA` |

The new Grid-independent controller guard validator tests are now accepted:
**6/6 passed**, Release `-warnaserror`, without repeating the prior 22 host cases.
Reviewed `NativeSourceClipboardNulTests.cs`: leading/middle/EOF NUL each exercises
Copy and Cut through the existing FakeShell with a clipboard-publication exception
trap. Exact NUL refusal (rather than clipboard-exception diagnostic) proves the
publication call was not reached; complete document/stamp/unmodified state and
original disk source remain unchanged. Test evidence is recorded in the source
validator note. The narrow four-file delta remains approved with the updated
hash ledger above; there are no new material findings.

## Final test-only instrumentation delta

Reviewed the two-hunk FakeShell helper patch: `ClipboardSetCalls` is documented
and increments at `SetClipboardText` entry, before the simulated rejection. The
six existing new controller cases snapshot its seeded-sentinel baseline and
assert unchanged attempted-publication count after refusal. This directly tests
no publication attempt, rather than only no clipboard overwrite. No production
change, new wrapper, or Grid dependency is introduced. No material issue found.
The validator reran only these six affected cases: **6/6 passed**, Release
`-warnaserror`; the previous 22 host cases remain accepted without repetition.

| Final test artifact | SHA-256 |
| --- | --- |
| `tests/Mote.Tests/NativeCsvGridSourceNulTests.cs` (22 distinct accepted host cases) | `649AF2773033A3C9DA002F6D9287013EDB48E91007B5D416BB58EF66BCF79CD1` |
| `tests/Mote.Tests/NativeSourceClipboardNulTests.cs` (6 controller cases) | `AE9DB1921BFAC5FEA0E730CBA5527F024C22084F07BB3DFD293F57FC19ABAA19` |
| `tests/Mote.Tests/NativeControllerTests.cs` (existing helper, test-only instrumentation) | `AF4F9D495D5BC25299048EF99F0BE6C093A7AD6D25EA9C51E309C66E926808C7` |
| `.temp/source-nul-p0-test-helper.patch` | `0FF4214B6EA5CCCCC84BC6F7EC376B3A495C877CC8A0E2F4DC1D68210F68A473` |

The four-file production patch hash remains
`53674AC7EEE4441F57C9049FD7B1774ADD8B7EFD3F95E94FB863792CDBB34EBA`.
**Final scoped approval remains unchanged.** Parent owns clean-HEAD patch
application/build and integration; reviewer performed no staging or production
edits.
