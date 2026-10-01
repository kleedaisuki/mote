# TOML statement edit-reuse validation

## Contract and independent expectations

This retained validation exercises the actual `Document` rope snapshots and
`TomlIncrementalSession`, not a standalone cache model. The contract is documented
in [the architecture decision](../architecture/toml-statement-semantic-reuse.md):
syntax reuse requires certified logical seams and exact actual source agreement;
ownership reuse requires identical decoded namespace effects; otherwise even
off-screen dependent statements must be revisited. A Complete certificate must
describe the current snapshot, never an earlier version or unrelated document.

`tests/Mote.Tests/TomlStatementReuseTests.cs` supplies literal expected valid/invalid
cases in addition to differential checks. Every differential comparison starts a
fresh session without cached state or edit history and requests Full analysis of
the same actual snapshot and viewport. It compares version, coverage, completeness,
total diagnostic count, ordered diagnostics (including code, severity, message and
span), tokens, and recursive node kind/name/value/current absolute UTF-16 spans.
This isolates incremental behavior from cold behavior; it is **not** an independent
TOML grammar implementation. Normative corpus validation is separate.

The ballast is 1,025 valid 4,096-character comment lines (4,198,400 UTF-16 units),
shared as a static string across cases. Every large-file check therefore actually
crosses the 4 MiB dispatcher and remains within statement/line/binding budgets.
No setting or test seam lowers the production size threshold.

## Retained coverage

| Boundary | Checks |
| --- | --- |
| Statement syntax | Scalar, array, inline table, date/time, hexadecimal, infinity; inline duplicate rejection; malformed strings/arrays |
| Multiline repair | Interior insertions, removed closing delimiters, four/five quote closures, newline expansion, mapped suffix projection |
| Physical/EOF seam | Newline insertion/deletion and invalid statement joining; EOF with/without original newline; whole header deletion |
| Namespace dependencies | Off-screen duplicate assignment/header, escaped Unicode key aliases, dotted/header conflict, ordinary versus array header, latest array-element re-entry |
| Reuse work | Valid value/category/spelling changes parse only a bounded repaired range and replay zero ownership actions |
| Snapshot identity | Same version on unrelated documents cannot preserve the old certificate |
| Untrusted edits | Missing history, incorrect offset/payload, undeclared change outside the edited range, unsupported multi-edit chain all cold-fallback |
| Projection | Cold Visible is Provisional; certified same-snapshot Visible is Complete with zero syntax/boundary/ownership work |
| Engine history | Actual Changed events for edit/Undo/Redo produce fresh current-coordinate results |
| Publication/lifetime | Repair/full/projection cancellation and exceptions preserve previous cache; published syntax/ownership failure and large-to-small switch clear it; analysis and disposal reentry fail; disposed sessions reject calls |

The work counters measure standalone statement syntax-parser input, logical seam
scanner visits and external ownership actions. They **exclude** exact prefix/suffix
source verification, lexical viewport parsing, rope traversal, summary mapping,
projection and allocation. Bounded syntax counters are not a total sublinear-time
claim. Failed repair plus Full fallback can legitimately count both attempts.

## Reproduction and observed result

Windows local managed .NET SDK **10.0.400**, target `net10.0`; no GUI, input source,
clipboard or operating-system settings were changed. Run from repository root:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj --no-restore `
  --filter FullyQualifiedName~TomlStatementReuseTests `
  --logger 'trx;LogFileName=toml-reuse-final.trx' `
  --results-directory .cache/toml-reuse
```

Observed 2026-10-01: **57 executed / 57 passed / 0 failed / 0 not executed**;
approximately 10 seconds reported test duration, process exit 0. The retained TRX is
`.cache/toml-reuse/toml-reuse-final.trx`; its counters were independently inspected.
An earlier 44-case run also passed, before adding boundary/category coverage. The
57-case final run was repeated after the production disposal guard/projection
unification and corresponding reentrant-disposal assertion changed; final evidence
is this corrected-source run, not a sum of executions or a retry of a failing check.

Source was still being integrated on parent HEAD
`e1a8d3535c667d6fffaad6333ab3fad995fd9169`; these exact production worktree bytes
were the final focused-run inputs (SHA-256, including local line endings):

| File under `src/Mote.Formats/` | SHA-256 |
| --- | --- |
| `TomlIncrementalSession.cs` | `AD19270EA40BCD37E0D9ED0F9B8F2D6DE8C92C01580C5207F3EE5F082950F2E6` |
| `TomlStatementCache.cs` | `7F5F4FF4D3FAC0E847D122516EB8EA8A0FCA847FECC039AA30A8C696F65CFD4F` |
| `TomlStatementReader.cs` | `8ECDD524E4F027CC8BD87CE4A492E151E5D4A49A6E83298259ACDA750C10F590` |
| `TomlStatementSummary.cs` | `FEEBA7EC6F849C1CF10DD6E32B10C2BEE16045FE464983D40C318B5FD4EE21FA` |

## Verdict and limits

No incremental/cold disagreement or literal validity/ownership expectation failure
was observed in these 57 retained cases. This supports the scoped cache/source,
dependency and publication contracts. It does not certify all possible TOML input,
decoded values, parser resource refusals, general sublinear complexity, cancellation
latency, physical memory reclamation, concurrent callers (the caller must serialize),
Native AOT, macOS execution, editor scheduling/rendering, startup or UI latency.
Full hosted cross-platform checks and focused performance evidence remain separate.
