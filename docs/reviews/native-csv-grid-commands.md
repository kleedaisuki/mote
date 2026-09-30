# Native CSV Grid command preparation

## Scope and contract

Implemented in `src/Mote.Native/NativeCsvGridCommands.cs`, with focused tests in
`tests/Mote.Tests/NativeCsvGridCommandsTests.cs`. This is pure preparation, not a
clipboard adapter, parser-session update, document mutation, or native value
dialog. It follows section 5 of `docs/csv-grid-architecture.md` and consumes only
an immutable `TextSnapshot`, matching `GridRenderProjection`, and logical
`NativeGridIntent`.

The controller must admit the complete `NativePresentationId` before scheduling
and again before native publication/Engine commit. The helper checks document
version, source length, bounded delivered row/column coordinates and actual
source origins; it cannot check a generation or presentation sequence that the
format projection does not carry. Preparation failure returns neither payload
nor edit. Cancellation throws `OperationCanceledException` before publication.

- `CopyValue` decodes the entire exact field syntax, never `DisplayText`.
- `CopySource` reads one field's exact syntax; `WholeRows` selects the same
  contiguous source behavior as `CopyRows`.
- `CopyRows` includes all actual delimiters, including the final selected row's
  delimiter. It preserves mixed CR/LF/CRLF and ragged widths.
- `CopyCsv`/`CopyTsv` serialize actual selected columns, double quotes and join
  records with CRLF, without adding a synthetic trailing delimiter. Empty values
  always have explicit `""` tokens, including consecutive trailing empty records.
- Missing fields refuse ordinary rectangle Copy. Only `CopyCsvPadded` converts
  explicit Missing descriptors to empty field tokens. Absent/Pending cells refuse.
- Syntax-error fields refuse decoded Copy but allow bounded source Copy.
- Every successful native text Copy mode rejects embedded U+0000. Oversized
  unread-origin refusal does **not** imply that a NUL scan took place.
- Output is capped at 8 Mi UTF-16 units, inclusive. Source reads are capped before
  materialization. Quoted decoding can read at most twice this cap plus two quote
  tokens, because doubled quotes may decode to an admitted output. Serialization
  checks escaped output size before appending. No path silently clips data.
- Replacement admits exactly one Complete, locally syntax-valid field, verifies
  its exact syntax, and returns one `TextChange`. Existing quoted style survives;
  special characters and empty values force quotes. Empty quoting preserves an
  explicit field at EOF rather than turning a one-cell record into an empty file.
  Surrounding delimiters/fields are untouched. Malformed replacement UTF-16 is
  rejected before Engine mutation. NUL remains representable in the Engine;
  native value-dialog admission belongs to the controller/platform contract.

Clipped/Oversized cells can still be copied if their exact origins decode within
the admitted output cap; replacement deliberately rejects those states. The
helper never scans an unavailable selection beyond the delivered bounded window.
Window extension remains a separately scheduled controller/session operation.

## Verification

Environment: Windows, .NET 10, Release configuration, repository working tree on
2026-09-30. Focused command:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~NativeCsvGridCommandsTests --logger 'console;verbosity=minimal'
dotnet build src/Mote.Native/Mote.Native.csproj -c Release --no-restore -warnaserror
```

Results: **19/19 focused tests passed**, 618 ms test duration; Native build
**0 warnings / 0 errors**. Tests use the production CSV Grid session for source
ownership, not a fabricated display-only fixture. Covered multiline/tab/quote/
formula-leading preservation, sparse actual columns, clipped full-value decode,
ragged missing refusal/padding, exact mixed-delimiter rows, malformed syntax,
all six Copy modes containing NUL, obsolete version/cancellation refusal,
replacement preservation plus a single Engine Undo, exact inclusive 8 Mi output
admission, escaped-output overflow, and a giant unquoted pre-read refusal with
less than 16 KiB allocated during preparation.

The independent small test grammar parses both CSV and quoted TSV serialization
of all 36 two-column combinations from six values: empty, plain, comma, tab,
quote, and CRLF. All rows, widths and decoded values roundtrip. This finite test
is evidence for the contract, not a claim of exhaustive grammar proof.

No native clipboard was cleared or written by these tests. No native value
dialog, real IME, AOT binary, platform accessibility callback or cross-platform
clipboard interoperability was exercised. Those acceptance checks remain owned
by native integration and CI.
