# CSV Grid independent differential validation

Date: 2026-09-30. Scope: format-session delivery, **not native table acceptance**.
Repository base HEAD at execution: `5dc0fcf`; Grid contracts/parser are concurrent
uncommitted changes. No production files were modified by this validator.

## Contract and independent expectation

The existing `CsvPolicy.Analyze(string)` is the separately implemented semantic
oracle for actual record count, maximum width, decoded values, field/record UTF-16
spans and invalid syntax diagnostics. It does not call the incremental/Grid parser.
The contract is `src/Mote.Formats/CsvGridContracts.cs`, with the intended source
and ordinal coordinate behavior described in `docs/csv-grid-architecture.md`.

Additional assertions do not use parser internals: a CR/LF/CRLF record delimiter
belongs to the preceding record; EOF belongs to the last existing record, without
inventing a trailing record. The next oracle record's start (or source EOF) derives
the exact delimiter span. Requested columns retain their actual ordinal; columns
beyond the actual width are Missing with no source origin, not an empty existing
field. Display maps TAB/CR/LF to `⇥`/`␍`/`↵` according to the native Grid display
contract while preserving raw syntax origins. These are not claims that a finite
test set proves CSV grammar equivalence.

## Reproduction

Environment: Windows `10.0.26200`, .NET SDK `10.0.400`, Release configuration.

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release `
  --filter FullyQualifiedName~CsvGridDifferentialTests --no-restore `
  --logger 'console;verbosity=minimal'
```

Final observation: **15 passed, 0 failed, 0 skipped; reported test duration 762 ms**.
Duration is test-run metadata, not a performance benchmark. Intermediate runs
were performed only after adding distinct coverage; no failure was rerun until
passing and no tested contract was weakened.

## Coverage and observed results

| Family | Independent checks | Result |
| --- | --- | --- |
| Short ordinary/ragged/empty/EOF cases | Every source offset, including each CRLF interior; every actual row and one past extent | Exact spans, owners, widths, missing fields, values and extents match |
| Valid randomized CSV | Seed `0x435356`, 120 sources, 1–19 records, 1–6 fields; 8 ordinal + 8 source requests per source; escaped quotes, commas, multiline CR/LF, empty, TAB, CJK/emoji | 1,920 requests match the legacy oracle |
| Edited snapshots | Four explicit boundary/multiline edits, supplied `VersionedEdit`; independently distant source interests and ordinal interest | Both analysis versions match snapshot; new coordinates match oracle |
| Adversarial edits | Seed `0x45444954`, 80 edits of commas, quotes, CR/LF, text and emoji; scalar-safe edits can intentionally create invalid CSV | Delivered rows/values/spans and exact whole-file diagnostic totals match reparse |
| Sparse index | 262,150 `a,b\r\n` records; checks segment/checkpoint-neighbor ordinals and source offsets, CRLF near EOF, EOF and past row extent | Actual `SparseFull` mode; coordinates match full legacy parse |
| Giant leading field | 70,000 quoted content units, ordinary column 1 tail; source-follow and column-1-only request | Oversized origin remains exact; tail remains column 1 with exact span/value |
| Narrow columns | Nine windows starting at 0–8, each width 3, across ragged rows | Actual ordinals and Missing states match |
| Invalid syntax | Unquoted quote, invalid closing suffix, unterminated quote, escaped quotes | Diagnostic code/severity/span sequence and field error flags match |
| Cold Visible | Request row 90,000 in unindexed 100,000-record source | No invented rows; Provisional, unknown exact extent, RowsTruncated |
| Cancellation | Precanceled new-version Full request after a prior committed cache | Throws cancellation; entire cache-statistics value including version and scan units unchanged |

Artifacts: `tests/Mote.Tests/CsvGridDifferentialTests.cs` (only newly owned test
file) and this document. No staging or commits were made by the validator.

## Verdict and limits

**Supported local pass for the above managed format-session contracts.** No
implementation defect was found in these discriminating checks. The cancellation
test proves rejection before work, not rollback after a deterministically controlled
mid-scan cancellation. It intentionally does not infer cancellation timing from
`CancelAfter(1)`, since normal completion before the timer fires is also legal.

Unverified here: native Windows/macOS table construction/selection, accessibility,
Copy/Replace/reveal, four-RID Native AOT, thread coordination, 100 MiB latency and
allocation budgets, malformed UTF-16 sanitization, large diagnostic truncation,
64-column/8,192-cell/64-KiB delivery limits, and strict single-binary deployment.
The dedicated performance owner and native integration validators must supply
those separate claims. Oracle equivalence inherits the explicit CSV dialect of
`CsvPolicy`; it is not cross-dialect or RFC-conformance certification.
