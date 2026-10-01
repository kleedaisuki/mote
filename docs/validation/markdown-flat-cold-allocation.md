# Markdown flat cold-allocation independent validation

## Scope and expected behavior

The allocation change must preserve the previous large-file certification contract: exact bounded dependency-free paragraph/ATX-heading/closed-fence owners may be Complete above 16 Mi UTF-16 units, while unsupported syntax, adjacent paragraph lines, unclosed fences, CR-only separators, and over-budget lines remain Provisional. Reusing a parser certificate is permitted only for exact source identity; the adjacency relation must still be checked for each physical owner. Cancellation must not replace an already complete committed session with a partially built stage.

The semantic oracle is the public whole-document `MarkdownPolicy.Analyze(string)` Markdig-backed route, independent of the new sequential reader and stage-local certificate cache. Tests compare recursive node kinds, absolute spans, names, values, and token kinds/spans at first, middle, distant, and actual rope-seam viewports. No elapsed-time or allocation quantity is a test assertion.

## Artifacts and environment

- Permanent suite: `tests/Mote.Tests/MarkdownFlatColdAllocationValidationTests.cs`.
- Isolated Formats-only harness: `.temp/markdown-flat-independent/Validation.csproj`; no Native project dependency.
- Result: `.cache/markdown-flat-independent/flat-validation.trx`.
- Windows 10.0.26200, win-x64, .NET SDK 10.0.400, MSBuild 18.9.6.
- Examined production source SHA-256: `AF377F39007ED90788CCDEA9892D4288D1A7A29DF99E0866E42CB2380151EA73`.
- Permanent test source SHA-256: `65C55C17E14474C2DA93CE48EF8D44387EF1F707BE0B6AB65259D6C2A96D4D1D`.

## Exact command and results

```powershell
dotnet test .temp/markdown-flat-independent/Validation.csproj -c Release --no-restore -p:TreatWarningsAsErrors=true --logger 'trx;LogFileName=flat-validation.trx' --results-directory .cache/markdown-flat-independent
```

Final result: **15 passed, 0 failed, 0 skipped**, reported test duration 2 seconds. The earlier harness first used `DocumentAnalysis` rather than `FormatAnalysis` for the whole-string oracle, a compile-time harness defect corrected without touching production. The first reference coexistence fixture mixed heading/paragraph adjacency outside the established explicit-reference admission grammar; it was corrected to admitted blank-separated ASCII owners. Neither was a production defect.

| Check | Cases | Observation |
| --- | ---: | --- |
| >16 Mi UTF-16 mixed repeated/unique BMP Unicode owners, LF/CRLF | 2 | Complete and recursively identical to whole parse across five viewports per case |
| Exact 65,536 body limit, 65,537 refusal, unterminated final owner | 4 | Inclusive body limit preserved; first extra unit Provisional |
| Repeated source with adjacent physical paragraph owners | 2 | Provisional, unknown total |
| Opaque closed fence containing reference-like and hostile source | 1 | Complete, recursive nodes/tokens identical to whole parse |
| Emphasis, unclosed fence, CR-only intermediate, adjacent paragraphs offscreen | 4 | Provisional, unknown total |
| Explicit-reference certificate followed by real deletion back to flat route | 1 | Both versions Complete, actual reference nodes and flat projection match whole parser |
| Pre-canceled and active-call cancellation, old-state check, retry | 1 | Both throw cancellation; old committed version remains; old snapshot and new retry Complete |

The CRLF fixture places a 16,383-unit heading immediately before CRLF and asserts that an actual immutable rope chunk ends after CR and the next begins with LF. Long 32,600-unit ballast owners also exercise reads spanning multiple chunks and reusable-buffer growth. Repeated heading/paragraph sources alternate with unique paragraph owners; allowed Unicode includes Chinese letters, accented Latin, Greek letters, and full-width digits.

## Cancellation interpretation and limits

The active-call cancellation test observes the private `_analyzing` guard from a second thread, cancels without an elapsed-time threshold, and checks `_version` against the previously committed version. This establishes cancellation during an active Analyze request and transactional publication/retry behavior. It does **not** instrument an exact line number or prove that cancellation happened inside `SnapshotTextReader.Read` rather than an earlier cancellation gate. A deterministic exact mid-reader-phase check would require a dedicated internal test seam; no production hook was added solely for this verification.

No product defect was observed in the covered cases. This suite does not establish the size of the allocation improvement, AOT startup latency, native painting responsiveness, or every CommonMark construct; those remain separate measurements and admission contracts. It also does not convert intentionally refused Markdown into complete semantics.
