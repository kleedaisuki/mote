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

On 2026-09-29, the project-local `.temp/formats-aot` smoke project referenced this assembly and invoked `Analyze`, `Format`, and `RenderHtml` for all six policies plus `CreateSession`/versioned edits for **all six** sessions. `dotnet publish .temp/formats-aot/formats-aot.csproj -c Release -r win-x64 -p:PublishAot=true -p:DebugType=None -p:DebugSymbols=false -o .temp/formats-aot/publish` succeeded without AOT/trimming warnings; the native executable ran successfully and was 4,649,984 bytes after the JSON key-index and YAML projection changes (single file in that publish directory). This measures a **formats-only smoke host**, not the GUI executable size or startup time.

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

Parser selection evidence and rejected alternatives are tracked in [`../../docs/parser-evaluation.md`](../../docs/parser-evaluation.md). The legacy `Analyze(string)` contract remains full-reparse. An **additive** `IIncrementalDocumentPolicy` capability now exists for all built-in structured policies; it takes engine snapshots plus a contiguous versioned edit chain and returns absolute UTF-16 spans without retaining a full source string. Sessions are per-document, single-caller, and may safely rebuild on a chain gap. `AnalysisCompleteness` distinguishes a global semantic check from a provisional visible sample; the native shell must never present a provisional result as “0 problems.” A session is not necessarily locally incremental: JSON and YAML currently re-stream Full requests after edits, and TOML's restricted large-file certifier returns `Complete` only for proven bounded statements; all other large inputs remain `Provisional`.

### Incremental-session status

| Policy | Reuse unit | Correctness boundary |
|---|---|---|
| Plain text | No parser state or source read; entire snapshot semantically valid by definition. | No diagnostics or hidden dependencies. |
| CSV | Logical record index in shared segments, lazy offset shifts, quote-state convergence after local edit; compact row boundaries/width/error summaries, visible rows decoded on demand. | Header/first-row width change recomputes width diagnostics; edits that alter quoting reparse until a safe record delimiter converges. |
| Markdown | Cached Markdig top-level blocks and run-level lazy shifts; isolated one-line heading/paragraph with self-contained inline markup reparsed locally. | References, fences, lists, multiline/structural edits, malformed inline ambiguity fall back to full parse. A large document without a complete cache returns a bounded `Provisional` visible result, not a false global validity claim. |
| JSON | Full structural and duplicate-key scan directly over immutable snapshot chunks, with a ≤256 KiB viewport projection retained even if a caller requests a larger visible range. | Every edit currently re-streams the whole document; object key sets consume memory proportional to unique keys. Depth limits, skipped recovery, unparsed suffix, or undecodable object keys downgrade to `Provisional`. |
| TOML | Validated Tomlyn syntax tree for ≤4 MiB; larger Full requests stream bounded logical statements through Tomlyn and a key/table ownership trie, retaining only viewport nodes. | Small files with any Tomlyn diagnostic downgrade to `Provisional` because recovery may omit later cross-key checks. Large `Complete` is restricted to individually valid statements ≤256 KiB/64 lines, ≤120,000 statements and ≤200,000 bindings. Repeated **root** array-table headers with local assignments can be certified; nested array-table headers or ordinary headers crossing an array table still downgrade. Syntax/ownership errors and unproven constructs remain `Provisional`. |
| YAML | Validated small-document oracle for ≤256 KiB; larger Full requests use SharpYaml event stream plus bounded key/anchor canonical summaries and viewport nodes. | Every edit re-streams. Giant physical lines, folded plain scalars, block scalar bodies, excessive node density, key/anchor-summary budget exhaustion, unknown custom canonical forms, or syntax-recovery uncertainty downgrade to `Provisional`; large Visible requests are provisional by design. |

The full legacy parser is the differential oracle, not the implementation of each local edit. Sessions preserve the old public policy methods for established callers; native UI integration is separate and requires serialized session calls, edit-log retention, cancellation, and document/version publication guards (see [`../../docs/incremental-plan.md`](../../docs/incremental-plan.md)).

### Structured-format large-file probes (2026-09-29)

* **JSON:** `.temp/json-probe` used a file-backed 100 MiB numeric-array document opened through `Document.OpenAsync`. A complete chunk-backed scan took ~640 ms; a near-start edit followed by another complete scan took ~634 ms. The analyzer allocated under 1 MiB on this simple corpus. This is **not incremental edit reuse**. A separate `.temp/json-object-probe` Release Windows x64 run used a 100 MiB object of unique keys: ~3,629 ms, 1,421 MiB cumulative allocation, ~877 MiB peak process working set (about +632 MiB from pre-scan), while post-GC managed heap returned to ~201 MiB after the session; distinct-key checking requires memory proportional to key cardinality during the scan. A 100 MiB single key transiently allocated ~400 MiB and raised working set ~404 MiB; a 100 MiB single value required only ~0.01 MiB analyzer allocation. Cancellation requested during scans completed in 27–34 ms in these observations. These are workload-dependent, single-run numbers, not p95 bounds. Small exact-oracle differential checks covered 3,022 cases, including malformed Unicode surrogate escapes and off-screen duplicate counts. The streaming result is `Complete` only if all source syntax was inspected; depth-limit and skipped error recovery explicitly return `Provisional` with unknown total count.
* **YAML:** `.temp/formatprobe/dense100.yaml` was 104,792,250 bytes of repeated anchored flow maps and sequence mappings with `id`, 360-character quoted `name`, and alias `ref`. A file-backed Full scan took 2,616 ms with 1,008.2 MiB **cumulative transient allocation**. Managed heap was 201.3 MiB before, 204.7 MiB live immediately after, 201.4 MiB after forced GC; process working set rose from 237.2 to a measured peak of 256.5 MiB; GC gen0/1/2 counts were 84/1/0. It returned `Complete` and zero diagnostics on this valid corpus. A cancellation requested after 25 ms ended the scan at ~31 ms. The 1 GiB allocation volume is a real GC/energy concern even though retained heap stays bounded. The edit path re-streams, not reuses.
* **YAML density guard:** The legacy small-parser path allocated ~242 MiB for 120,000 `- x` items despite only 480,000 UTF-16 input characters. The session now routes documents above 256 KiB to event streaming; `.temp/formats-density-probe` measured the same file at 257 ms/~26.6 MiB cumulative allocation with `Complete`. A 750,000-item, 3 MiB file exceeds the conservative short-line budget and returns `Provisional` in 119 ms/~18.1 MiB allocation; 120,000 unique anchored items return `Provisional` in 59 ms/~10.8 MiB after a preflight anchor-count cap. The budget is a deliberate semantic-completeness gap for pathologically dense large YAML, not an assertion that those files are valid. A post-guard `.temp/yaml-session-bench` 100 MiB structured corpus still returned `Complete`: cold 2,308 ms, edit 2,288 ms (full re-stream), 1,008.1 MiB cumulative allocation per pass, peak process working set 259.2 MiB, post-GC managed heap ~201.4 MiB; cancellation observed ~32 ms after a 25 ms timer. These are single-run Windows x64 Release observations, not p95.
* **TOML:** Tomlyn 2.10.1's `TomlParser` event stream reported zero diagnostics for `a=1` followed by `a=2`, repeated `[a]`, scalar-prefix conflicts, and sealed-inline-table extension, while `SyntaxParser.Parse(validate:true)` reported errors. Actual `SnapshotTextReader`→`TomlParser.Create(TextReader)` allocated 64.2 MiB at creation for 16 MiB source and 400.9 MiB for 100 MiB; process working set reached about 234 MiB / 1.13 GiB respectively. The 100 MiB event parse took ~2,784 ms. Therefore event parsing alone is **neither a bounded-memory nor semantic-complete substitute** for the validated syntax parser. The replacement restricted certifier partitions top-level logical statements, validates each with Tomlyn, and tracks key/table ownership in a bounded trie. Two deterministic generated corpus sweeps (10,000 ownership cases, seed 1729; 10,000 statement-boundary cases, seed 3017) found zero false `Complete` against whole-file validated Tomlyn, but are not a general proof. Nested array-of-tables source-order counterexamples remain excluded. A 100 MiB, 104,544-line restricted sample took ~1.58 s, allocated ~1.15 GiB cumulatively, and reached ~571 MiB peak process working set; the allocation cost remains substantial even though no full syntax tree or source copy survives. Invalid/unsupported large documents remain `Provisional` with unknown total diagnostic count, not falsely error-free.

#### Semantic-session optimization rerun (2026-09-29)

The earlier figures above are **before** the following changes; they are retained to expose the baseline rather than silently replacing it. All measurements are single Windows x64 Release runs from project-local `.temp` probes, including the process and its open document in working-set figures.

| Corpus / analyzer | Before | After | Correctness boundary |
|---|---|---|---|
| 100 MiB JSON object, unique keys (`.temp/json-object-probe`) | 3,629 ms; 1,421 MiB cumulative analyzer allocation; ~877 MiB peak working set | 3,100 ms; 204.56 MiB allocation; 433.88 MiB peak working set | `Complete`, exact decoded-key equality; every edit still re-streams. |
| 16 MiB JSON object, 1,398,101 repeated short keys | 480 ms; 469.41 MiB allocation; 81.02 MiB peak working set | 280 ms; 0.30 MiB allocation; 69.29 MiB peak working set | Exact total of 1,398,101 duplicates; off-viewport diagnostics are counted, not materialized. |
| 100 MiB dense structured YAML (`.temp/yaml-session-bench`) | ~2,308 ms; 1,008.1 MiB cumulative allocation; ~259 MiB peak working set | 2,327 ms; 674.5 MiB cumulative allocation; 258.4 MiB peak working set | `Complete`; edit Full still re-streams (~2,315 ms); Visible is only `Provisional`. |

The JSON index stores paged source spans and seeded 64-bit hashes rather than decoded strings. Equal hashes are always checked by exact decoded UTF-16 comparison; a collision-chain or 10-million-key resource cap downgrades to `Provisional`. A short raw-key hot cache avoids per-duplicate temporary strings without weakening escape equivalence. Escaped-equivalent, astral literal/escaped, forced-equal-hash, invalid-key, depth/recovery, and 3,022 random differential cases were probed. A 100 MiB numeric array still took ~808 ms and only ~0.04 MiB analyzer allocation after the change; its timing difference from the older 640 ms run is within uncontrolled single-run variation, not an established regression. Cancellation on tested large JSON inputs was observed within 23–32 ms. JSON unique-key scanning now uses less peak memory, but exact duplicate detection still has a **linear cardinality cost**; this is not constant-memory parsing.

For YAML, parser-only SharpYaml events on the same 100 MiB file allocated 579.2 MiB across 2,082,466 events (1,301,538 scalar events), so ~579 MiB is presently below mote's projection layer. The projection layer's incremental ~429 MiB allocation dropped to ~95 MiB after a ≤4-key inline mapping fast path and a **64-entry per-analysis** short plain-key canonical cache. Complex-key/alias, >4-key spill, unsupported-key/budget, multiline quoted scalar, and apostrophe-in-plain-scalar probes retained the same semantics. The cache is neither global nor unbounded. Post-change heap returned from ~206.1 MiB live to ~201.4 MiB after forced GC; GC counts on the 100 MiB Full scan were 56/0/0. Cancellation after a 25 ms timer was observed at ~34 ms. This reduction does not remove the underlying event parser's substantial transient allocation.

Two TOML ownership counterexamples establish why **nested** array-of-tables headers remain outside the current certifier. The whole-file validated Tomlyn 2.10.1 parser reports `a.[2].b` already defined in each, while the earlier statement-by-statement trie accepted them:

```toml
q=[1,2]
[[a]]
[[a]]
[[a.b]]
[[a]]
x={a=1}
a=1
b=2
```

```toml
[[a]]
[[a]]
s="""x
y"""
[a.b]
[[a]]
b=2
a.b=3
[[a]]
s="""x
y"""
[a.b]
```

The [official TOML 1.0](https://toml.io/en/v1.0.0#array-of-tables) and [1.1](https://toml.io/en/v1.1.0#array-of-tables) rules resolve references to the most recently defined array element. Two independent implementations, Python `tomllib` 3.14.6 and Rust `toml` 1.1.6+spec-1.1.0, **accept both exact files** and place bindings in separate latest-element dictionaries (`.temp/TomlProbe/toml_oracle.py` and `.temp/TomlRust/src/main.rs`, with locked Rust dependencies and captured outputs). This is strong evidence of a Tomlyn validator disagreement, not evidence that those files violate the specification. The certifier nonetheless avoids the disputed nested-header path: it admits only repeated root `[[a]]` elements with independent local assignments. An AoT-biased generated sweep of 20,000 documents (seed 9173) found zero false `Complete` against Tomlyn: 4,541 valid certified, 2,097 valid conservatively downgraded, and 13,362 invalid downgraded. This finite sweep is evidence for the restricted path, not a proof of full TOML 1.1 streaming conformance. A 100 MiB root-AoT file (52,272 elements, 104,544 statements, 104,857,632 UTF-16 chars) was `Complete` in 1,477 ms, with 1,118 MiB cumulative allocation and 577 MiB peak process working set; cancellation requested at 10 ms was observed at ~25 ms. The edit path still re-streams the full file.

An exploratory nested-AoT corpus (`.temp/TomlNested/corpus.txt`, 14 hand-written cases plus 10,000 generated cases, seed 6451) made Python `tomllib` and Rust `toml` agree on every case while revealing 11 mismatches in the initial trie. The observed mechanism was source-order distinction between an implicit parent created by an ordinary table header and one created by an array-table header when later dotted assignments appear. A prototype with separate origin states matched the 10,000 independent-oracle cases, but **that state is not in the production certifier**: nested AoT still has no spec-level invariant strong enough to justify a global `Complete` claim, and shipping dormant state would add maintenance complexity without user-visible correctness benefit. The exact fixtures/scripts/outputs are retained in project-local `.temp/TomlNested` for a future focused proof or counterexample search.

### CSV session evidence (2026-09-29)

The project-local `.temp/CsvProbe` generated an ASCII CSV file at `.temp/CsvProbe/data.csv` by streaming rows containing 1,022 `a` characters, a comma, and LF; it opened the file with `Document.OpenAsync`, created a CSV session, built the first index, applied a near-start edit, then compared that edit against fresh legacy full parsing. Runs were local Windows x64 .NET 10 non-AOT Debug probes, not startup/product latency claims. `GC.GetTotalMemory(true)` measured the **managed heap** after forced collection, including the document; process working set and native allocations are not represented. Timings are single-run observations.

| Streamed file | Opened heap | After CSV index | Initial index | Near-start session edit | Fresh full analysis |
|---|---:|---:|---:|---:|---:|
| 16 MiB | ~32 MiB | ~33 MiB | 787 ms | 2.28 ms | 132 ms (earlier probe) |
| 100 MiB | ~201 MiB | ~203 MiB | 6,830 ms | 3.41 ms | 1,293 ms (earlier probe) |

The initial 100 MiB scan is not instant, but it runs against bounded snapshot windows and need not block file opening. The compact index adds about 2 MiB managed heap in this fixture; quoted/irregular data may cost more. The first synthetic benchmark constructed the entire 100 MiB input as one managed string before measuring and showed ~1.14 GiB after analysis, but **~913 MiB existed before the session**; that result was a fixture artifact, not evidence of a 1 GiB CSV cache. Differential checks compared semantic nodes, diagnostics, and tokens against `CsvPolicy.Analyze(string)` for 10,000 random small-file edit transitions and 1,000 edits across 1,024-record segment boundaries. These checks are stronger than a happy-path speed test, but not proof for every malformed CSV dialect.

### Markdown session evidence (2026-09-29)

`.temp/MarkdownProbe/Program.cs` (project-local) compared the session against fresh `MarkdownPolicy.Analyze` after 81 directed edits over headings, lists, fences, references, inline links and malformed intermediate text, plus 600 random sequential edits. It compared the complete semantic tree (kind, span, name, value), diagnostic codes/spans, and token kinds/spans. On one Windows x64 non-AOT Release-like local probe, a **synthetic** ~16 MiB document built from repeated `alpha paragraph words words words\n\n` took ~1.86 s for the initial full Markdig parse; one near-start visible edit then took ~9 ms versus ~669 ms for a fresh full parse. A repeated `**bold** and [link](https://example.org)\n\n` workload took ~7 ms for the same safe local edit after its initial full parse. These are single-run timings, not p95. The initial full pass and its memory are still material; before a complete cache exists, large `Visible` requests are explicitly `Provisional` bounded samples rather than a concealed whole-file parse. Reference definitions, fences, lists, structural changes, and ambiguity trigger safe full-parser fallback.

### Semantic-session design checkpoint and follow-ups

These state the design assumptions, discriminating tests, and remaining boundaries behind the optimization rerun above. Measured behavior, rather than the original hypothesis, is reported in the table.

* **TOML array-of-tables ownership:** The [TOML 1.1.0 array-of-tables rule](https://toml.io/en/v1.1.0#array-of-tables) points each nested reference at the most recently defined array element and forbids defining a child before its parent element exists. The current source-order trie retains an independent namespace per repeated root AoT element, but still cannot certify nested header transitions where Tomlyn and two independent parsers disagree. Next investigation: represent the latest element pointer at every array-table path; test invalid parent-before-child order, nested re-entry, dotted keys, inline tables, and element-boundary edits against the specification and independent processors. Finite fuzz is evidence, not a proof; unresolved nested paths stay `Provisional`.
* **JSON exact duplicate-key index:** The snapshot already owns the source, so a large object's key set need not retain a second decoded string for every unique key. The implemented per-object table keeps decoded-UTF-16 hashes plus source spans, verifying exact decoded equality on every hash collision; invalid/undecodable key identity skips equality and downgrades global completeness. A randomized per-session seed and a 128-link/10-million-key budget bound adversarial work with `Provisional` fallback. A further open question is whether source-span indexing can be made locally incremental across edits without invalidating key identities or retaining old snapshots.
* **YAML event allocation:** Profiling isolated ~579 MiB to SharpYaml's parser and ~429 MiB to mote projection on the original 100 MiB structured file. Bounded fast paths reduced mote overhead substantially, but the parser still creates many transient events. Compare cold Full, post-edit Full, and Visible separately, because the former two re-stream the whole document while Visible is only provisional. Future parser-level optimization requires preserving canonical key and alias semantics and cancellation responsiveness, not merely lowering allocation on one fixture.

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
