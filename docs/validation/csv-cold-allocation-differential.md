# CSV cold borrowed-cursor independent differential validation

## Scope and basis

The expected contract is the existing `CsvPolicy` semantics and `docs/csv-cold-analysis.md`: quoted CRLF belongs to a logical cell, doubled quotes decode to one quote, source coordinates are UTF-16 units, Full has exact global diagnostic counts even when projected payload is bounded, and canceled calls do not replace committed cache state. The whole-string policy is the compatibility oracle for small cases; independently specified values and diagnostic coordinates avoid relying exclusively on agreement between two implementations. Large cases use directly constructed row counts and malformed tail coordinates rather than materializing a giant oracle tree.

New isolated artifact: `tests/Mote.Tests/CsvColdAllocationDifferentialTests.cs`. No production file was edited, staged, or committed by this validator.

## Checks

- Six quoted-pair cases place CRLF, escaped quotes, or a surrogate pair across the 16,384-unit rope boundary. Three use legal same-content edits to fragment leaves. A Visible prefix precedes resumed Full; exact values, provenance, diagnostics, tokens, and tree structure match the whole-string policy.
- One malformed source with a surrogate pair asserts CSV003 at `(7,1)`, CSV002 at `(17,3)`, CSV001 and CSV004 at `(24,5)`. These are hand-counted UTF-16 coordinates and are also checked against the policy oracle.
- Two 8 Mi-unit payloads, quoted/unquoted, assert malformed suffix/quote errors and exact giant record/token extents. Full-call cumulative current-thread allocation is bounded at 2 MiB, excluding document/fixture construction.
- A 104,858,006-unit fixture contains 262,145 two-column records and one one-column tail: exact total one CSV004 at `(104858000,4)`, SparseFull mode, at most 256 dense segments, bounded sparse checkpoints, and Full-call cumulative current-thread allocation below 64 MiB.
- One changed-document case verifies deterministic pre-cancellation leaves cache statistics unchanged, then Visible followed by idle-style Full matches both a fresh session and the whole-string policy.

## Draft evidence (2026-10-01)

Windows x64; .NET 10.0.11; Release; `-warnaserror`.

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --filter FullyQualifiedName~CsvColdAllocationDifferentialTests -warnaserror --logger 'console;verbosity=normal'
```

First execution: 9/11 passed; two fixture edits were rejected because they illegally bisected UTF-16 surrogate pairs. This was correct Engine behavior, not a parser defect. The harness now fragments adjacent legal boundaries or replaces the entire pair. Failure-directed seam rerun: 6/6 passed. The independently typed malformed-coordinate assertion initially undercounted offsets after the second row by one; manual source recount confirmed `(17,3)` and `(24,5)`, and corrected assertion passed 1/1. Logs are `.temp/csv-cold-differential.log`, `.temp/csv-cold-differential-repaired.log`, and `.temp/csv-cold-differential-spans.log`.

Draft source SHA256 at the last targeted run: `FB145C77F4D7402D74BABC1F0DBAAB5F52430CEBB40F0BFC6EB26D9084416E9F` (`src/Mote.Formats/CsvIncrementalSession.cs`). Owner re-freeze confirmed this identical hash after the small-file hot-path adjustment. All 11 unique cases have successful evidence on this source, across the initial run and failure-directed harness repairs; this is not a claim that the originally failing uncorrected harness passed. A final six-case seam run additionally asserts Prefix mode and null global count before Full, proving the first scan did not already complete the document. The committed prefix ends at offset 5 inside the first rope chunk; Full must resume there and cross the 16384 boundary. This final run passed 6/6 with zero compiler warnings/errors; log `.temp/csv-cold-differential-prefix.log`. No baseline suites were rerun.

## Limits

No controlled mid-scan cancellation was established; pre-cancellation checks transaction invariants deterministically. The test does not set a timer and assume a faster scan must lose a race. Cumulative allocations are not retained heap, peak RSS, or Native AOT GUI latency. macOS, ARM, actual input-to-paint, and async controller scheduling are not exercised. The compatibility oracle itself is not an independent CSV specification proof. No completed baseline suite was rerun.

## Verdict

Pass for the scoped managed Release semantic, source-provenance, cache-transaction, and cumulative-allocation contracts at the stated frozen hash. No production defect was found. Large mixed records still require authoritative O(source length) scanning; these bounds do not establish latency or eliminate legitimate index allocations. The earlier timing regression belongs to the performance owner's separate benchmark evidence, not this suite.
