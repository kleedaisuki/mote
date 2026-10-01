# CSV command policy/mechanism boundary

Date: 2026-09-30. Status: implemented; focused managed evidence below is not platform clipboard or accessibility acceptance.

## Ownership

CSV field decoding, CSV/quoted-TSV serialization, explicit empty-token handling,
source-row delimiter preservation, padded ragged export, bounded payload admission,
and exact field replacement belong to the compile-time Formats policy. The additive
public API is `CsvGridCommands`, `CsvGridSelection`, `CsvGridCopyKind`, and
`CsvGridCommandResult`. It references Engine and existing immutable Grid contracts
only: no Native dependency, new parser, format session, or mutable table model.

Native's existing `NativeCsvGridCommands` now maps intent/selection/result types
and admits the intent document version. Controller generation/installation nonce
admission and re-admission, source settling, scheduling, clipboard mechanisms,
modal editors, and Engine application remain unchanged. Native's result type,
entry points and resource constant alias preserve existing controller callers.

## Contracts and compatibility

- Callers must use the Grid produced from the exact snapshot. Version and source
  length checks are useful stale-data checks, not authentication of constructed
  projections or global document identity.
- Only delivered origins are read. Sparse logical row ordinals are looked up by
  bounded binary search, not interpreted as contiguous list positions. Missing
  rows refuse rectangle and contiguous-source-row export.
- Maximum output remains 8 Mi UTF-16 units; quoted input admission remains bounded
  before materialization. Refusal returns neither payload nor change.
- All text export modes refuse embedded NUL before native publication. This is an
  explicit text-export compatibility contract, not a statement that Engine cannot
  preserve NUL. Size refusal does not claim unread source was NUL-free.
- Value export decodes exact syntax, never clipped/sanitized display text. CSV and
  quoted TSV quote empty values explicitly. Source/Rows retain original syntax
  and mixed/final delimiters. Ragged padding is an explicit separate copy kind.
- Replacement prepares one exact `TextChange`, preserves existing quote style,
  validates UTF-16, and refuses incomplete/missing/malformed fields. Applying it
  and creating the Undo transaction remain Engine/controller responsibilities.
- Pre-cancelled operations throw without mutation. This change introduces no
  parser cache mutation, configuration migration, source rewrite, or native ABI.

Public XML documentation includes exact-snapshot trust constraints and copy/edit
usage examples. The direct policy tests retain the prior command semantic cases,
including cap/NUL/empty rows/TSV/source/replacement; additional cases cover null
arguments, unknown kinds and sparse delivered row gaps.

## Verification

Initial boundary integration: Release focused Formats command, Native command
and Native controller suites: **57 passed, 0 failed**. Further public-input and
sparse-gap coverage added afterward; final result recorded below.

Final focused Release command policy + Native commands + Native controller suites: **59 passed, 0 failed**. Formats Release build with warnings as errors: **0 warnings, 0 errors**.
