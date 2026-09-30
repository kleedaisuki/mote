# Independent Markdown reference production validation

Date: 2026-10-01. Scope: the first frozen production reference integration in
`MarkdownReference*.cs` and `MarkdownIncrementalSession.cs`. This report is not
an authorization of general CommonMark completeness or product release.

## Verdict

**The exercised bounded domain passes. No implementation defect was found in
these checks.** The production integration preserves public source coordinates,
first-winning isolated declaration semantics, diagnostic totals and real native
rendering for the admitted large-file reference grammar. It refuses the tested
unsupported shapes and incomplete edit histories without stale whole-file
claims. Resource exhaustion and deterministic cancellation do not publish an
uncommitted certificate.

Only the independent test file and this report were written by this validator.
No production fixes, staging, commits, user clipboard/configuration changes, or
outside-repository artifacts were used.

## Independent expected behavior

The public `IFormatSession` contract requires exact snapshot versions, safe
rebuild or partial results after edit-history gaps, and no commit on cancellation.
`DocumentAnalysis.Complete` requires an exact whole-document diagnostic count,
independently of bounded visible materialization. `FlowRenderProjection` gives
explicit text/paragraph/run limits and source provenance.

The delegated admission contract permits isolated physical-line ASCII full
reference owners at most 4,096 UTF-16 units and isolated ASCII definitions. It
does not admit adjacent atoms, shortcut uses, Unicode labels, list containers,
multiline paragraphs or adjacent declarations. Unsupported is a refusal, not a
syntax error in the general Markdown language.

Large fixtures contain 5,600 repeated real paragraphs of the form `Alpha `,
3,000 `a` characters, and ` [use][id]`, separated by blank lines. They exceed
16 MiB and therefore cannot pass by taking the ordinary sparse whole-document
parser path. Small independent contexts contain the actual full owner source
and real declarations; `MarkdownPolicy.Analyze` provides the existing parser
oracle. Nested node fields and child arity, tokens and diagnostics are compared
after an independently calculated absolute source offset. No expected node or
span is derived from the new skeleton/index. Render comparison uses a separate
small-document session and checks display text, inline styles, paragraph and
run source origins. The complete 3,000-character ballast must remain visible.

## Cases and results

| Check | Expected | Observed |
|---|---|---|
| Cold Visible then idle Full, LF/CRLF, first/middle/end definitions | Cold Provisional and unknown total; Full whole-file Complete | Pass, 3 cases |
| Duplicate declaration with case-folded key | First actual source winner, including unsafe winner | Pass in cold/full cases |
| Missing target with present unsafe display-text key, present target plus display key, all missing | No accidental shortcut links or display-key warning | Pass, 3 cases |
| Distant owner with different ballast length sharing skeleton shape; definition-like content inside JSON fence | Actual distant source geometry and actual outside-fence winner | Pass, LF and CRLF |
| Unicode, touching atoms, shortcut use, list, multiline, adjacent definitions offscreen | Provisional, no global count | Pass, 6 cases |
| Destination retarget, one consumer changed to missing, first-winner removal, engine Undo/Redo | Exact values, first-winner promotion, warning totals and versions | Pass |
| Gapped real engine edit chain, Full | Safe exact rebuild or honest partial; no stale destination | Pass, exact rebuild |
| Warm Visible contained destination edit then missed-edit gap | Contained edit may remain Complete; gap is Provisional until Full | Pass |
| Unsupported Visible edit and subsequent repair | Immediate certificate revocation; Full repair can recertify | Pass |
| Whole-document requested viewport | Bounded node/token/diagnostic/source materialization and native display | Pass; omitted display marked truncated |
| Cancellation at private `before-commit` hook | Previous exact snapshot remains published; retry succeeds | Pass |
| Pre-canceled public edit and retry, then disposal | Old version still usable, successful retry, disposed Analyze/Render reject | Pass |
| Retained-accounting, allocation, deterministic work and parser budget exhaustion | Classified resource refusal with no certificate publication | Pass, 4 cases |

The initial independent run passed **23/23** tests. The existing broad Markdown
filter then passed **101/101**, including those 23 independent cases and the
implementer's certificate tests; these counts are overlapping, not additive.
Two subsequently added public Visible-boundary cases passed **2/2**. Thus all
**25 independent cases** in the final file have been executed successfully,
without repeating the completed initial checks.

## Reproduction and evidence

Environment: Windows `10.0.26200`, x64, .NET SDK `10.0.400`, `net10.0`, Release.
The working tree was based on `7420d0bf08612fe8f10207e123c84d1f446e7841`;
the integration was uncommitted, so source hashes below are the operative
implementation identity. Commands were run after the implementer declared the
five production files frozen.

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release `
  --filter FullyQualifiedName~MarkdownReferenceIntegrationValidationTests `
  --logger 'trx;LogFileName=markdown-reference-integration.trx' `
  --results-directory .cache/markdown-reference-validation `
  -p:TreatWarningsAsErrors=true

dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-build `
  --filter FullyQualifiedName~Markdown `
  --logger 'trx;LogFileName=markdown-regressions.trx' `
  --results-directory .cache/markdown-reference-validation

dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release `
  --filter 'FullyQualifiedName~Warm_visible_contained_destination_then_gap|FullyQualifiedName~Unsupported_visible_edit_and_repair' `
  --logger 'trx;LogFileName=markdown-reference-visible-boundary.trx' `
  --results-directory .cache/markdown-reference-validation `
  -p:TreatWarningsAsErrors=true
```

All three commands returned exit code 0. The first and last built warning-clean.
TRX evidence is retained under `.cache/markdown-reference-validation/`; the
initial independent run reported about 1 second of test duration, the broad
filter about 5 seconds, and the two added cases 406 ms. These are test-run
durations, **not editor latency benchmarks**.

| Frozen production file | SHA-256 |
|---|---|
| `MarkdownReferenceBudget.cs` | `F29497525543F9B1C3EED94B57D0BC3B75FCC0840A0A5194F1B004A262994B2B` |
| `MarkdownReferenceCertificate.cs` | `FAB7A8B728123A9C782A29765A79090FE3BC50465CAC2797D0412533399BC5BE` |
| `MarkdownReferenceIndex.cs` | `3898005FA6DF15E236846367DD4E222CA9E59A6CE0E0EDA61C5ECD8DA9C5FA9A` |
| `MarkdownReferenceSkeleton.cs` | `282AA6BA79BAFE09E5780FE67F40F4AB8CFDD6A6FCB042460AAF688BD4FD05DD` |
| `MarkdownIncrementalSession.cs` | `4CE9F2C9553A1A3241D9A0ECCF3DD3CEBDE9C7614A3AA5BE93D9D910F21CD408` |

## Coverage limits

- This validates the admitted domain through public engine/session/render
  workflows, not general cross-block Markdown, all possible reference key
  alphabets, native window interaction, physical paint, IME or accessibility.
- The oracle uses the established `MarkdownPolicy` and pinned parser; shared
  underlying parser defects remain possible. Raw fixture substring positions,
  warning multiplicities, source preservation and refusal expectations are
  independently specified to avoid blindly accepting index output.
- Publication-hook cancellation is deterministic and directly exercises the
  private index; public session cancellation is pre-canceled. No asynchronous
  cancellation timing or concurrently invoked sessions is claimed.
- Zero-budget cases establish fail-closed classifications, not a measured
  memory ceiling, heap bound or performance percentile. No large unique-key
  workload benchmark, startup improvement or target-GUI result is claimed.
- History gap Undo/Redo may rebuild rather than incrementally reuse; this
  report checks correctness, not reuse efficiency.

The exact integrated change at `24bf145` later passed all nine strict jobs in
[CI run 36758358616](https://github.com/kleedaisuki/mote/actions/runs/36758358616),
including Windows/macOS solution tests and four strict single-binary Native AOT
publishes. This is compatibility evidence, not the large-reference corpus
running in the native GUI on four RIDs. The file-backed repeated/unique-owner
cold and warm managed measurements are recorded in the implementation document;
target GUI first-paint/input/render tails and RID-specific live memory remain
the next independent measurements.
