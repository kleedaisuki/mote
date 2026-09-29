# Format-policy contracts and boundaries

`Mote.Formats` is a statically linked, reflection-free policy assembly. `DocumentPolicies.ForPath` selects a policy from the file extension, while `Analyze` returns an immutable snapshot whose semantic nodes, tokens, and diagnostics use **UTF-16 half-open offsets** into the exact `SourceText` reference. The engine should associate that snapshot with a document version and discard stale results after edits. `SourceText` does not itself clone the input string; the parsed semantic tree and token/diagnostic arrays are additional memory, so the engine should avoid retaining an unlimited history of analyses for large files.

Policies do not perform I/O, do not own document lifetime, and never execute markup or source text. `RenderHtml` escapes user-controlled text. `Format` is conservative and should leave invalid content untouched rather than silently repair or destroy data. These contracts matter more than clever parser internals: they allow a fast single-file engine to keep formatting, diagnostics, preview, and highlighting on one versioned source snapshot.

## Standards and deliberate scope

* JSON: [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259). `.jsonc` deliberately falls back to plain text because comments are not JSON.
* TOML: [Tomlyn](https://xoofx.github.io/Tomlyn/docs/) provides validated TOML 1.1 grammar and a lossless syntax tree with source spans. TOML 1.0-valid documents are normally a subset, but this policy explicitly targets 1.1 rather than claiming independent 1.0 conformance. `Format` normalizes only horizontal whitespace adjacent to parser-identified assignment tokens, then reparses and requires semantic-tree equivalence; it never rewrites comments, multiline-string interiors, or user values.
* YAML: [YAML 1.2.2 specification](https://yaml.org/spec/1.2.2/). [SharpYaml](https://xoofx.github.io/SharpYaml/docs/low-level/syntax-tree/) provides source spans/trivia and grammar parsing. Its syntax-tree change operation performs a full reparse, not an incremental parse; malformed input needs explicit diagnostics/fallback. mote's Core Schema resolver follows the specification rather than SharpYaml numeric extensions; mapping-key uniqueness compares canonical tagged scalars, ordered sequences, unordered mappings, and resolved aliases. Cyclic alias graphs (implementation-defined by YAML) and custom scalar tags without a canonicalizer produce explicit warnings. `Format` compresses only proven same-line scalar mapping-separator gaps, with semantic-tree revalidation; flow forms, comments, and block scalars are untouched.
* Markdown: [Markdig](https://github.com/xoofx/markdig) provides a CommonMark-tested AST with precise source locations and optional trivia tracking. Rendering disables raw HTML and unsafe URI schemes; parsing a document is not permission to execute it. `Format` normalizes extra spaces after nonempty ATX heading markers only when rendered HTML remains identical; hard line breaks and empty headings are untouched. An unterminated fenced block at end of file is valid and consumes the remainder of the document.
* CSV: [RFC 4180](https://www.rfc-editor.org/rfc/rfc4180/). CSV does not declare that its first row is a header; the policy therefore exposes rows/cells rather than inventing a header schema. Inconsistent row width is a warning because real CSV dialects can vary.

Native AOT builds preserve this assembly's static registry and require no runtime type discovery. A future richer parser can replace a policy's internals without changing document lifetime or offset conventions.

On 2026-09-29, the project-local `.temp/formats-aot` smoke project referenced this assembly and invoked `Analyze`, `Format`, and `RenderHtml` for all six policies plus `CreateSession`/versioned edits for Markdown, CSV, and plain text. `dotnet publish .temp/formats-aot/formats-aot.csproj -c Release -r win-x64 -p:PublishAot=true -p:DebugType=None -p:DebugSymbols=false -o .temp/formats-aot/publish` succeeded without AOT/trimming warnings; the native executable ran successfully and was 4,549,120 bytes (single file in that publish directory). This measures a **formats-only smoke host**, not the GUI executable size or startup time.

### YAML key-equality probe

YAML 1.2.2 [§3.2.1.3](https://yaml.org/spec/1.2.2/#3213-node-comparison) defines key equality by tag and canonical scalar form, positional sequence equality, and order-independent mapping key/value equality; equality of self-descendant alias graphs is implementation-defined. This is not equivalent to comparing source spellings. The project-local `.temp/yamlkeyprobe.py` probe (Python 3, `python -m pip install --target .temp/python-packages ruamel.yaml==0.19.1`, then import it from that directory, `YAML(typ='rt', pure=True)`, `allow_duplicate_keys=False`) provided an independent check:

| Pair of keys in one mapping | ruamel result | YAML 1.2.2 expectation |
|---|---|---|
| `0xB` / `11` | duplicate error | duplicate integer eleven |
| `0o13` / `0xB` | duplicate error | duplicate integer eleven |
| `"11"` / `11` | accepted | distinct string/integer tags |
| `[a, b]` / `[a, b]` | duplicate error | duplicate sequences |
| `[a, b]` / `[b, a]` | accepted | distinct sequences |
| `{a: 1, b: 2}` / `{b: 2, a: 1}` | accepted | **duplicate per specification** |

The last row exposes a reference-processor limitation/disagreement; mote should follow the YAML specification rather than copy that result. Safe-mode `ruamel.yaml` could not construct complex mapping keys, so the round-trip mode was used for this differential probe. This probe tests duplicate-key behavior, not all YAML conformance.

Parser selection evidence and rejected alternatives are tracked in [`../../docs/parser-evaluation.md`](../../docs/parser-evaluation.md). The legacy `Analyze(string)` contract remains full-reparse. An **additive** `IIncrementalDocumentPolicy` capability now exists for selected policies; it takes engine snapshots plus a contiguous versioned edit chain and returns absolute UTF-16 spans without retaining a full source string. Sessions are per-document, single-caller, and may safely rebuild on a chain gap. `AnalysisCompleteness` distinguishes a global semantic check from a provisional visible sample; the native shell must never present a provisional result as “0 problems.” JSON, TOML, and YAML still use legacy full reanalysis.

### Incremental-session status

| Policy | Reuse unit | Correctness boundary |
|---|---|---|
| Plain text | No parser state or source read; entire snapshot semantically valid by definition. | No diagnostics or hidden dependencies. |
| CSV | Logical record index in shared segments, lazy offset shifts, quote-state convergence after local edit; compact row boundaries/width/error summaries, visible rows decoded on demand. | Header/first-row width change recomputes width diagnostics; edits that alter quoting reparse until a safe record delimiter converges. |
| Markdown | Cached Markdig top-level blocks and run-level lazy shifts; isolated one-line heading/paragraph with self-contained inline markup reparsed locally. | References, fences, lists, multiline/structural edits, malformed inline ambiguity fall back to full parse. A large document without a complete cache returns a bounded `Provisional` visible result, not a false global validity claim. |

The full legacy parser is the differential oracle, not the implementation of each local edit. Sessions preserve the old public policy methods for established callers; native UI integration is separate and requires serialized session calls, edit-log retention, cancellation, and document/version publication guards (see [`../../docs/incremental-plan.md`](../../docs/incremental-plan.md)).

### CSV session evidence (2026-09-29)

The project-local `.temp/CsvProbe` generated an ASCII CSV file at `.temp/CsvProbe/data.csv` by streaming rows containing 1,022 `a` characters, a comma, and LF; it opened the file with `Document.OpenAsync`, created a CSV session, built the first index, applied a near-start edit, then compared that edit against fresh legacy full parsing. Runs were local Windows x64 .NET 10 non-AOT Debug probes, not startup/product latency claims. `GC.GetTotalMemory(true)` measured the **managed heap** after forced collection, including the document; process working set and native allocations are not represented. Timings are single-run observations.

| Streamed file | Opened heap | After CSV index | Initial index | Near-start session edit | Fresh full analysis |
|---|---:|---:|---:|---:|---:|
| 16 MiB | ~32 MiB | ~33 MiB | 787 ms | 2.28 ms | 132 ms (earlier probe) |
| 100 MiB | ~201 MiB | ~203 MiB | 6,830 ms | 3.41 ms | 1,293 ms (earlier probe) |

The initial 100 MiB scan is not instant, but it runs against bounded snapshot windows and need not block file opening. The compact index adds about 2 MiB managed heap in this fixture; quoted/irregular data may cost more. The first synthetic benchmark constructed the entire 100 MiB input as one managed string before measuring and showed ~1.14 GiB after analysis, but **~913 MiB existed before the session**; that result was a fixture artifact, not evidence of a 1 GiB CSV cache. Differential checks compared semantic nodes, diagnostics, and tokens against `CsvPolicy.Analyze(string)` for 10,000 random small-file edit transitions and 1,000 edits across 1,024-record segment boundaries. These checks are stronger than a happy-path speed test, but not proof for every malformed CSV dialect.

### Markdown session evidence (2026-09-29)

`.temp/MarkdownProbe/Program.cs` (project-local) compared the session against fresh `MarkdownPolicy.Analyze` after 81 directed edits over headings, lists, fences, references, inline links and malformed intermediate text, plus 600 random sequential edits. It compared the complete semantic tree (kind, span, name, value), diagnostic codes/spans, and token kinds/spans. On one Windows x64 non-AOT Release-like local probe, a **synthetic** ~16 MiB document built from repeated `alpha paragraph words words words\n\n` took ~1.86 s for the initial full Markdig parse; one near-start visible edit then took ~9 ms versus ~669 ms for a fresh full parse. A repeated `**bold** and [link](https://example.org)\n\n` workload took ~7 ms for the same safe local edit after its initial full parse. These are single-run timings, not p95. The initial full pass and its memory are still material; before a complete cache exists, large `Visible` requests are explicitly `Provisional` bounded samples rather than a concealed whole-file parse. Reference definitions, fences, lists, structural changes, and ambiguity trigger safe full-parser fallback.

## Measured whole-file Markdown cost (2026-09-29)

The reproducible benchmark in `tests/Mote.Benchmarks/Program.cs` writes a UTF-8 file by repeating `# Heading\nA short paragraph with **emphasis** and [link](https://example.com).\n\n` to the requested size, opens it through `Mote.Engine.Document`, applies one insertion near EOF, then times `MarkdownPolicy.Analyze(document.Snapshot.GetText())`. Each row below is one fresh Release-process run on Windows 10.0.26200 x64, .NET 10.0.11, 20 logical processors. `PeakWorkingSetBytes` is the **whole process** high-water mark, not isolated parser allocation; timings are observational, not CI gates. Raw JSONL rows are in `.cache/benchmarks/results.jsonl` (project-local, ignored).

| Implementation | File | Analyze time | Peak working set | Top-level nodes |
|---|---:|---:|---:|---:|
| Markdig AST projection, copying structural source substrings | 1 MiB | 310.448 ms | 96,735,232 B | 26,210 |
| Same baseline | 16 MiB | 2,491.950 ms | 876,990,464 B | 419,330 |
| No structural substring copies, lazy child-list allocation | 16 MiB | 2,730.412 ms | 805,818,368 B | 419,330 |
| Also omit uninformative literal-text leaf nodes from semantic IR | 16 MiB | 1,991.779 ms | 657,248,256 B | 419,330 |
| Restore parsed display text on heading/paragraph leaves for native preview | 16 MiB | 2,230.063 ms | 675,315,712 B | 419,330 |

Across these single runs, projection changes lowered observed 16 MiB peak by about 202 MB and analysis time by about 0.26 s, but uncontrolled run-to-run variation prevents a precise speedup claim. Restoring parsed display text for leaf blocks added about 18 MB in one run but fixes an observed blank native preview; it does not recreate a text-run node for every literal. The larger conclusion is stronger: **whole-file analysis of a 16 MiB Markdown document is not suitable for a per-keystroke path**. The engine/UI must defer or bound analysis in large-file mode and expose whether results are complete; a true incremental parser has not been implemented for Markdown. Literal text remains in `SourceText` and source spans.
