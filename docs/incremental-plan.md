# Incremental semantics and single-owner text: concrete evolution plan

Status: design proposal with partial implementation, 2026-09-29. `IncrementalContracts.cs` and per-document sessions now exist for Plain text, CSV and Markdown; the other three structured formats and native product integration remain. This is **not** a claim that every parser or the delivered native editor is incremental. The document describes a compatible path from `IDocumentPolicy.Analyze(string, CancellationToken)` and engine snapshots toward bounded-work semantics. **Delivery update:** strict one-binary output makes Avalonia `Mote.Desktop` noncompliant; Section 4's Avalonia comparison is historical evidence about duplicate buffers/page semantics. Apply its single-owner/continuous-file conclusions to `Mote.Native` Win32/AppKit shells, whose page target is now 64 Ki UTF-16 units with 8 Ki of edit slack. See [architecture.md](architecture.md) and [release-gaps.md](release-gaps.md) for current product gates.

## 1. Starting facts and the actual bottleneck

Current `Mote.Engine.Document` owns a persistent 16 KiB-chunk AVL rope, monotonic snapshot versions, a UTF-16 `TextChange`, and **ordered** `Changed(Before, After, Change)` delivery. `TextSnapshot` supports chunk/range/line reads and CR/LF/CRLF line lookup. `Document.GetOrCompute<T>` is a **same-version cache** cleared on every edit, not an incremental parser state holder. Current policies in `Mote.Formats` are singleton, stateless instances; `Analyze(string)` reparses supplied text. `FormatAnalysis.SourceText` retains that string and all source spans are relative to it. `Mote.Desktop` debounces/cancels analyses and rejects stale version results; for documents above 8 Mi UTF-16 code units it copies a roughly 256 Ki-code-unit page into AvaloniaEdit instead of the whole file. The user can explicitly request full-document inspection **up to 32 Mi UTF-16 code units**, which still materializes the entire snapshot; beyond that budget, inspection remains page-local. These facts were checked against the source on this date.

That arrangement has two separate costs:

* **Computation:** passing a `TextChange` in an event does not make `Analyze(string)` incremental. A 100 MB JSON/TOML/YAML edit can still copy and reparse 100 MB, even when the UI remains responsive because the work is backgrounded.
* **Ownership:** for nonpaged files the rope, AvaloniaEdit's own `TextDocument`, and the whole-file `GetText()` analysis string can coexist; `FormatAnalysis.SourceText` retains the last of these rather than necessarily making a fourth copy. The page path bounds UI/analysis copies but makes cross-page selection, search, navigation, undo viewport restoration and full-file preview nontrivial. It is a pragmatic bridge, not the final seamless large-file model.

## 2. Contract: additive, per-document, acyclic

Do **not** change or remove today's public `IDocumentPolicy.Analyze(string)`, `Format(string)` or `RenderHtml(FormatAnalysis)` while callers/tests rely on them. A separate optional capability now exists in `Mote.Formats`; static policy instances remain stateless. `Mote.Formats` references `Mote.Engine` for `TextSnapshot` and `TextChange`; `Mote.Engine` never references `Mote.Formats`. `Mote.Native` is the release composition root that must create one analysis session per `(Document instance, policy kind)`, own cancellation/disposal, and translate result coordinates into view coordinates. This wiring is not yet present; the old Avalonia shell is only a development reference.

Representative *new* API (names may be adjusted once implemented, but the ownership and semantics should not):

```csharp
/// <summary>Optional capability beyond the stable string-based policy API.</summary>
public interface IIncrementalDocumentPolicy : IDocumentPolicy
{
    /// <summary>Creates isolated parser/index state for exactly one document.</summary>
    IFormatSession CreateSession();
}

/// <summary>One committed edit; text coordinates belong to BeforeVersion.</summary>
public readonly record struct VersionedEdit(
    long BeforeVersion, long AfterVersion, TextChange Change);

/// <summary>Full semantics or a window whose limitations are explicit.</summary>
public readonly record struct AnalysisRequest(
    TextSpan VisibleRange, AnalysisScope Scope);

public enum AnalysisScope { Visible, Full }
public enum AnalysisCompleteness { Provisional, CoveredRegion, Complete }

/// <summary>Projection with absolute UTF-16 spans for exactly one snapshot.</summary>
public sealed record DocumentAnalysis(
    long Version,
    TextSpan Coverage,
    AnalysisCompleteness Completeness,
    SemanticNode Root,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<SemanticToken> Tokens,
    int? TotalDiagnosticCount);

/// <summary>Policy-private cache lifetime; no mutation escapes to the engine.</summary>
public interface IFormatSession : IDisposable
{
    DocumentAnalysis Analyze(
        TextSnapshot snapshot,
        IReadOnlyList<VersionedEdit> changesSinceCommittedState,
        AnalysisRequest request,
        CancellationToken cancellationToken);
}
```

`Coverage` is a half-open range in the **full** snapshot. Every `SemanticNode`, `Diagnostic`, and `SemanticToken` in `DocumentAnalysis` also uses absolute UTF-16 offsets, even for a viewport result; this eliminates desktop's page-origin translation and its associated coordinate-error risk. `Complete` means whole-document syntax **and** policy-defined semantic checks are complete at that version, **not** that every syntax node or token is copied into this UI projection. `CoveredRegion` means local semantic facts are valid in `Coverage`, but absence of a diagnostic elsewhere says nothing. `Provisional` means syntax/style hints only; do not display “0 problems” as a validity statement. `TotalDiagnosticCount` is null unless complete; the diagnostic and node lists may be bounded to the requested viewport or an outline summary, with paging for more results. A request for `Full` may legitimately return `CoveredRegion` if the caller's work budget cancels it; it must not fabricate completion.

The session owns its private AST/CST, dependency summaries and parser-specific checkpoints; `DocumentAnalysis` owns only immutable, source-mapped projections needed by consumers and **does not retain a full source string**. Rendering evolves additively to `Render(TextSnapshot, DocumentAnalysis, range, sink)` or policy-produced bounded render blocks. Keep the old `RenderHtml(FormatAnalysis)` for legacy callers, but do not invoke it on giant full-document snapshots. `Format(string)` likewise stays a compatibility API; future formatting returns a version-preconditioned engine edit rather than mutating analysis state.

Version chain contract: the first `VersionedEdit.BeforeVersion` equals the session's last successfully committed version; adjacent entries satisfy `AfterVersion == next.BeforeVersion`, and the last `AfterVersion == snapshot.Version`. On a gap, new policy kind, parser crash or unusable checkpoint, the session discards private state and performs a safe full reparse (small input) or a clearly partial bounded analysis (large input). Undo/redo are ordinary new-version edits, not a rewind of the parser's version number. A no-change request for a different viewport can reuse the same-version private state. Memory budget may evict dormant sessions; eviction changes performance, not semantics.

### Scheduling and publication

One worker at a time invokes each `IFormatSession`; current desktop fire-and-forget tasks must not concurrently mutate one session. The coordinator retains a bounded edit log of `VersionedEdit` values, debounces foreground typing, and schedules visible work before idle full work. It may cancel an obsolete task, but **only commit new session cache state after successful analysis**. If an underlying mutable parser cannot roll back canceled work, mark the session invalid and rebuild at the next call. The UI publishes a result only if `(document identity, policy identity, result.Version) == current`; cancellation alone is insufficient because a task may finish after an edit. A partial result must never replace a newer complete result for the same version unless it has genuinely more useful coverage. Dispose a session on file close, policy switch and window close; clear edit log when state is rebuilt. The engine's immutable snapshots make background reads safe, but old snapshots must not be retained indefinitely.

```
Document.Changed(v, edit, v+1)
  → coordinator appends VersionedEdit; cancels obsolete request
  → serialized FormatSession analyzes immutable snapshot(v+1)
  → session commits private cache only on success
  → Desktop checks document + policy + version
  → UI receives absolute-span DocumentAnalysis; source stays in Document
```

## 3. Incremental strategy is per format, not a universal parser switch

| Format/current parser | First compatible step | Reuse unit and semantic invalidation | Safe fallback |
| --- | --- | --- | --- |
| Plain text/custom | Read visible lines directly from `TextSnapshot`; no full string needed. | Changed lines plus line-index delta. | Always cheap bounded analysis. |
| CSV/custom RFC-style record parser | Introduce quoted-field state checkpoints and a record-offset index, then reparse from preceding safe checkpoint until quote state/record boundaries converge. Header/expected-width changes invalidate row-width checks globally. | Record range; potentially suffix if edit changes quoting. | Full streamed scan, not `GetText()`; provisional visible display until complete. |
| JSON/custom recursive descent | Make parser consume snapshot chunks; retain immutable container nodes and per-object key summaries. Reparse smallest enclosing value/container touched by edit, expanding to parent/root if delimiters or recovery context change. Duplicate-name diagnostics invalidate the enclosing object. | Enclosing array/object plus dependent ancestors; shift later locations through edit mapping or regenerate visible projections. | Full streamed parse; deep nesting/cancellation bounded, never claim local validity under unmatched braces. |
| TOML/Tomlyn `SyntaxParser` | Initially keep exact current full-string parse for ordinary files through a legacy adapter. Add a prepass for safe table sections and multiline state; only then parse independent sections and update a dotted-path/table-definition index. | Table section plus keys/tables whose resolution depends on it; multiline strings can merge sections. | Full parse when section boundary cannot be proven; on giant file, bounded partial projection while full parse is explicit and asynchronous. |
| Markdown/Markdig AST | A session now conservatively reuses source-spanned simple isolated headings/paragraphs without reference syntax; other edits rebuild with Markdig. Extend dependency summaries only when equivalence tests justify them. | Proven fast path is one independent simple block; reference definitions, fences and lists force safe fallback. | Whole-document parse where context is ambiguous; giant file displays provisional viewport semantics rather than falsely resolving links. |
| YAML/parser-library AST | Initially full-string parse for ordinary files. Incrementalize only after defining indentation/flow-state checkpoints and anchor/alias/tag dependency summaries. Do not treat arbitrary indented line as independent; alias cycles and duplicate-key equality have global implications. | Safest enclosing collection/document plus referenced aliases. | Full parse; on giant files make incompleteness explicit. |

The full parser is the **correctness oracle** for every incremental implementation: after every edit in a test corpus, compare canonical semantic facts, diagnostic codes/spans, token roles and render structure between incremental and fresh full parse. Include malformed intermediate states and edits that alter delimiters/quotes/fences/anchors; otherwise a “fast” parser can be silently wrong. A syntax tree's changed ranges do not automatically invalidate semantic dependents: Markdown reference definitions can appear after uses [CommonMark](https://spec.commonmark.org/spec), YAML aliases refer to anchors [YAML 1.2.2](https://yaml.org/spec/1.2.2/), and TOML dotted paths interact with table declarations [TOML 1.0](https://toml.io/en/v1.0.0). Tree-sitter demonstrates subtree reuse after edits, but requires precise edit coordinates and separate semantic invalidation [Tree-sitter advanced parsing](https://tree-sitter.github.io/tree-sitter/using-parsers/3-advanced-parsing.html). Production rust-analyzer's derived-query boundary reinforces the value of stable semantic summaries, but a generic query engine is not required for this six-format app [rust-analyzer architecture](https://rust-analyzer.github.io/book/contributing/architecture.html). Incremental scannerless GLR research reports strong reuse for small edits while also reporting full-parse overhead and poorer locality under grammar nondeterminism; borrow measurement discipline, not an unexamined parser rewrite [Sijm, 2019](https://doi.org/10.1145/3359061.3361085), [TU Delft evaluation](https://repository.tudelft.nl/record/uuid%3A6ddf9fbd-c39e-4aae-b6ce-13389def6a9f).

## 4. Avoiding duplicate full text in Desktop

Today's bounded AvaloniaEdit page is the only established way in this codebase to avoid feeding a second **full** text buffer to the control. It should remain a temporary safety valve while measuring real workloads; it is not equivalent to a seamless entire-file editor. In particular, a page-local AvaloniaEdit selection cannot represent a drag/select-all spanning unloaded pages; page navigation preserves a clamped global caret but not a cross-page selection. Full-document Inspect still materializes a string through `snapshot.GetText()` for files within its 32 Mi-unit budget; larger files have only page-local inspection. This cap prevents a 100 MB whole-string escape hatch but also makes semantic incompleteness explicit.

| Option | Text ownership/memory | Product semantics and maintenance | Decision |
| --- | --- | --- | --- |
| Keep whole-file AvaloniaEdit plus engine rope | Duplicated full text, extra analysis string; simple UI | Highest large-file memory/latency risk; a reported AvaloniaEdit issue describes hangs above ~10 MB on Linux, not proof of Windows/macOS failure [upstream issue](https://github.com/AvaloniaUI/AvaloniaEdit/issues/547). | Keep for measured ordinary-size files only. |
| Windowed AvaloniaEdit over engine rope | Bounded view copy (today ~256 Ki units) | Must implement global scroll, selections, search/copy, page-crossing edits, caret anchoring and source mappings outside the control; complexity grows with every editor command. | Transitional, not a silent “large-file mode” product contract. |
| Fork/replace AvaloniaEdit's document backend | Potentially one rope | Upstream control uses its concrete `TextDocument` model; fork couples mote to control internals and increases update/AOT burden [AvaloniaEdit upstream](https://github.com/AvaloniaUI/AvaloniaEdit). | Reject unless a narrow upstream-supported adapter emerges. |
| One virtualized `TextView` backed by `TextSnapshot` for **all** sizes | Single canonical full text, bounded glyph/layout caches | Requires IME, accessibility, bidi/grapheme movement, selections, clipboard and input parity; substantial work but eliminates divergent small/large semantics. | Target if release gates or user workflows expose page limitations. |

The recommendation is to measure the current control first, but **not** solve its limits by adding more hidden page-specific conditions. If seamless 100 MB editing is an acceptance requirement (including select/copy/search across the entire file), use one snapshot-backed virtualized surface for both small and large files, and let AvaloniaEdit serve only as the interim adapter. The engine remains authoritative throughout; avoid switching canonical ownership by file size. Avalonia's custom input surface must implement `ITextInputMethodClient` for CJK IME [Avalonia text input](https://docs.avaloniaui.net/docs/input-interaction/text-input). A custom surface is productized only after Windows/macOS input, accessibility and rendering conformance tests, not merely an FPS demonstration. VS Code's piece-tree work and Zed's snapshot-backed rope show the production value of separating canonical text from viewport rendering [VS Code](https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation), [Zed](https://zed.dev/blog/zed-decoded-rope-sumtree).

## 5. Migration order and discriminating gates

Milestone status at this audit: additive contract is present; Plain text/CSV sessions and a conservative Markdown session are present; `Mote.Native` still calls the legacy string parser, so session work is not yet product behavior. The numbered order below records dependency order, not a claim that every step remains unstarted.

1. **Record baseline.** Capture 1/10/100 MB and one-50-MB-line open-to-editable, p95 input-to-paint, p95 analysis, peak RSS/managed allocations, page navigation, and full Inspect behavior on Windows/macOS Native AOT. Trace IDs must correlate edit→analysis→paint; log only normalized format/size, never text/path. Keep this benchmark outside fast CI but run on release candidates.
2. **Land additive contract.** Add session/result/request types in Formats, reference Engine one-way, retain the old API and corpus tests. Use a legacy adapter only when its whole-string allocation fits the budget; expose `Provisional` otherwise. Add version-chain and stale-publication tests before optimizing parsers.
3. **Prove the cheap cases.** Plain text and CSV consume snapshot chunks/lines; use their full parsers as oracles. Benchmark quote-state convergence and row-index memory. This validates session lifecycle and no-copy UI projection before harder grammars.
4. **Bring structural formats one by one.** Markdown now has a narrow safe fast path; JSON, TOML and YAML remain to be done, and broader Markdown dependencies need evidence. Each policy may remain full-reparse until its incremental implementation passes equivalence tests. A local edit p95 speedup matters only if initial full parse, memory and malformed-input behavior do not regress.
5. **Resolve view ownership.** Benchmark current AvaloniaEdit at ordinary sizes; test large-file global navigation/selection/copy and IME. If page semantics fail, implement a single virtualized snapshot view for all sizes rather than a growing collection of page exceptions. Remove full-string `FormatAnalysis.SourceText` from the new large-file path and make full Inspect streaming/cancelable.

Suggested pass/fail criteria, **not measured results**: after an edit, incremental and fresh full analyses are semantically equivalent on at least 10,000 generated and corpus transitions per format; no stale result is visible; a 100 MB file remains editable without retaining a second 100 MB text string; p95 input-to-paint stays within one 60 Hz frame under ordinary typing; 100 MB edit analysis does not monopolize the UI thread; and all large-file commands either work across the whole file or are explicitly disabled with a reason. Track worst-case fallback frequency, not only median speedup. Any parser optimization that increases unrecoverable data loss or incorrect diagnostics is a regression.
