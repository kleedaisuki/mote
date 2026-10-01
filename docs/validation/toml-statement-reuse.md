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

## Follow-up: uniform public-policy validation and formatting

The separate retained `TomlUniformValidationTests.cs` exercises the public
`TomlPolicy.Analyze` and `Format` string APIs after they were moved to the shared
normative ownership mechanism. This is distinct from the preceding 57 incremental
cache tests; their file and assertions were not changed for this follow-up.

All **712** pinned corpus entries are retained as individual cases. Exactly **218
valid and 485 invalid** sources decode strictly and reach public analysis; the
remaining **9 invalid UTF-8** cases exercise only the separate strict-decoder
boundary, not TOML policy acceptance/rejection. All 703 decoded sources match the
published validity label. All 485 decoded invalid sources return their original
string from `Format`. Every valid source is formatted, reanalyzed without errors,
formatted again for idempotence, and checked for equivalent recursive semantic
kind/name/value structure and unchanged source-anchored comment text. This compares
actual projected values before/after; it does not independently compare every
decoded value to upstream JSON oracles.

The two historical independently minimized array-element re-entry sources from
`.temp/toml-conformance/small-policy-minimal-{1,2}.toml` are embedded exactly in the
tests, so reproduction needs no temporary files. Both now validate and actually
normalize assignment gaps while preserving the projected semantic structure.
Their prior false-rejection/Python evidence remains in the original experiment
artifacts. Additional retained checks cover exact public `TOML_PARSE` UTF-16 key
spans (including an astral-character comment and CRLF), escaped aliases, internal
inline duplicates, forbidden trivia, Unicode/tab/comment formatting, cancellation
requested before analysis, and valid sources crossing each large-cache budget:
one >256 KiB value, one >64-line array, >120,000 comment statements, and 201,000
namespace bindings. All four directed resource sources remain below 4 MiB.

### Failed initial expectation and correction

The initial 730-case run had **729 passes and one failed test assertion**: it assumed
`a=9223372036854775808` must be rejected. The pinned normative specification at
`.cache/toml-test/specs/v1.1.0.md`, integer section, requires signed 64-bit values
to be accepted losslessly and requires an error when an integer cannot be represented
losslessly; it does not forbid a larger losslessly represented value.
([TOML 1.1 integer specification](https://toml.io/en/v1.1.0#integer).)
The editor retains this larger integer's exact decimal text in a number node.
Thus this was a **test expectation defect**, not evidence of a production defect.
The corrected tests explicitly assert exact node value preservation for both signed
64-bit endpoints and this larger value, including after formatting. The initial
failed TRX remains `.cache/toml-uniform/toml-uniform.trx`; it was not overwritten.

### Final reproduction and result

Same Windows SDK **10.0.400** environment:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj --no-restore `
  --filter FullyQualifiedName~TomlUniformValidationTests `
  --logger 'trx;LogFileName=toml-uniform-final.trx' `
  --results-directory .cache/toml-uniform
```

Observed 2026-10-01: **732 executed / 732 passed / 0 failed / 0 not executed**,
process exit 0, approximately 2 seconds reported test duration. The final TRX
counters were inspected independently. This includes the nine decoder-boundary
cases and must not be reported as 732 public grammar checks.

Source worktree based on parent HEAD `43b303a47804e1c347b02486c25d3da619fd9992`;
exact production inputs at final validation (SHA-256, local line endings):

| File under `src/Mote.Formats/` | SHA-256 |
| --- | --- |
| `TomlPolicy.cs` | `C8D67384F8567F6C24BFFC3A0F4CFC48D5D7C3399D349C7515A738792AF42E64` |
| `TomlDocumentValidation.cs` | `A8528D8F64C6367B1D22A055A53FB0B121C4DE80DA35D27686156FCF7217844F` |
| `TomlStatementReader.cs` | `11033182A07EC5C010011174C2F612266AFA1AD18D92F6E883A47AAD95CDCC09` |
| `TomlOwnershipIndex.cs` | `5617F49A6AC81EB0329EC7C915D3ACC3212BA9034FFA16ACE09D18EC7B55DD2F` |

Verdict: the public-policy finite corpus and conservative formatter contracts pass
these checks. No claim is made about exhaustive invalid-diagnostic enumeration,
cancellation latency during the synchronous whole-source grammar parse, all
possible values, memory/latency distributions, AOT/macOS or GUI behavior. The
resource checks establish absence of those four cache caps in this public path,
not that every arbitrarily large source can be processed within finite resources.

## Follow-up: public multi-witness diagnostic recovery

The public validation implementation subsequently gained recoverable independent
diagnostics rather than stopping after its first local/global witness. The bounded
large-file cache still stops at its first proved ownership witness; these are
different recovery contracts and must not be conflated.

Eight additional public-policy cases retain these independent expectations:

- Two different valid tables each contribute their duplicate-assignment key span.
- A conflicting header contributes its own key span and quarantines following
  assignments; after a valid new header, a new duplicate is diagnosed, without
  falsely attributing quarantined assignments to the preceding table.
- A failed dotted assignment rolls back an implicit parent's ownership origin,
  allowing the later valid parent header; its own failure and a later duplicate
  remain visible, without a manufactured parent-header error.
- A malformed one-line header permits valid-header recovery; local inline ownership
  failure does not suppress a later independent global duplicate.
- Malformed plain/collection/multiline values retain every independent whole-parser
  grammar diagnostic, including exact severity, message and clamped absolute span.
  Exact duplicate diagnostic records are not emitted twice.

For grammar-valid ownership examples, literal expected span sets establish the
entire expected diagnostic set. For grammar-invalid examples, the independently
invoked lossless `SyntaxParser.Parse(validate:false)` establishes grammar witnesses
that must be retained; additional namespace claims are checked separately.
The first run (**796/797**) exposed a test expectation bug: the malformed-header
test banned any error at a quarantined `x` key, but the whole grammar parser
legitimately emitted an unexpected-token error at exactly that span. The assertion
now subtracts exact reference grammar records before checking for a false namespace
witness; all grammar records and the later real duplicate must still be present.
The original failed `.cache/toml-uniform/toml-recovery-final.trx` is retained.

Both existing suites were rerun because the actual reader/index algorithms changed,
not to accumulate test counts:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj --no-restore `
  --filter 'FullyQualifiedName~TomlUniformValidationTests|FullyQualifiedName~TomlStatementReuseTests' `
  --logger 'trx;LogFileName=toml-recovery-corrected.trx' `
  --results-directory .cache/toml-uniform
```

Observed 2026-10-01: **797/797 executed and passed**, zero failed/skipped/not executed,
process exit 0, approximately 15 seconds test duration; inspected TRX counters.
This is **740 public-policy/decoder cases plus the existing 57 bounded cache cases**.
The preceding source snapshots/results are historical checkpoints, not current
source fingerprints. Final recovery validation used parent HEAD
`35823c4b1c045e80a3a0858dc35bb3dad9d08c54` plus these production worktree bytes:

| File under `src/Mote.Formats/` | SHA-256 |
| --- | --- |
| `TomlPolicy.cs` | `9CE5E57F9F23ADC8143EC36EAEE50EE40E3332D077CD590477EC2ED01ABD200D` |
| `TomlDocumentValidation.cs` | `57FE854D23E14FA0767A0CFF3D9888DE8B9D85A2EC4F750F92B24707C16A5867` |
| `TomlStatementReader.cs` | `B4B2D4A02A5CC26D363AC302B50D743D6C00442307F48ADB86C8FBE501AA12A8` |
| `TomlOwnershipIndex.cs` | `FE6270DB3EA022BA092518A15961A37D394805151B0B6DBA3CBB640EC7374CB4` |

Verdict: the retained examples preserve independent public witnesses and avoid the
tested false scope/origin errors; the existing bounded cache behavior still passes.
This is not exhaustive recovery of every malformed multiline suffix or a promise
of diagnostic completeness for arbitrary invalid input. Source/token highlighting
behavior beyond these existing comparisons, native execution and performance are
separate investigations. Integer checks compare the editor's exact textual number
node, not Tomlyn's runtime numeric model or an upstream decoded-value oracle.

### Diagnostic-anchor regression exposed by expanded assertions

The subsequent recovery coverage adds four illegal-trivia header prefixes
(FF, VT, NBSP, NUL) and asserts that **every** public diagnostic and token span
lies inside the current UTF-16 source for all 703 decoded corpus examples and
all formatted valid outputs. The same checks cover directed malformed recovery.
This exposes a real implementation defect, unlike the earlier expectation fixes:
`.cache/toml-uniform/toml-recovery-anchors.trx` reports **773 passed / 28 failed /
801 executed**, zero skipped, process exit 1. All 28 failing cases are malformed
published fixtures, including `invalid/array/no-close-01.toml`,
`invalid/key/single-open-bracket.toml` and `invalid/string/no-close-10.toml`.
The failure is a length-one parser-recovery diagnostic at `Start == source.Length`,
outside the editor's half-open source bounds. The new statement-local diagnostic
conversion did not preserve the original public span-clamping contract. The four
illegal-trivia quarantine cases and all existing 57 cache cases pass this run.
Assertions are not relaxed; the production owner has been notified to fix the
conversion and the identical failing cases must subsequently pass.

The production owner corrected `TomlStatementSummary`'s local diagnostic conversion
to clamp both start and length before shifting to absolute coordinates, preserving
zero-width EOF witnesses. The **same 801 assertions now pass**, including the exact
28 previously failing fixtures; no bounds assertion or fixture was removed.
Final run command is the combined filter above with
`--logger 'trx;LogFileName=toml-recovery-anchors-fixed.trx'`. Observed final result:
**801 executed / 801 passed / 0 failed / 0 not executed**, process exit 0, about
14 seconds test duration. Final independently inspected result is
`.cache/toml-uniform/toml-recovery-anchors-fixed.trx`; the failed anchors TRX remains
available to demonstrate detection of the original regression.

The final set is **744 public-policy/decoder cases + 57 bounded reuse cases**;
nine public-suite entries still concern only strict byte decoding. Final production
`TomlStatementSummary.cs` SHA-256 is
`E957DD69D9A49ABEA789C18C201B51C0ABCB0B928C7D9F08A7608A5162918145`
on worktree parent HEAD `f226a28848735e159597074f20571c57fbd2a546`.
This closes the demonstrated local recovery-span regression. It does not claim
all untested parser recovery shapes, GUI rendering or native execution are verified.
