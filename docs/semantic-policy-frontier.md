# Semantic policy frontier: preserve a proved TOML error without pretending completion

Status: completed focused investigation, 2026-10-01. This is an implementation-ready
production slice proposal, **not a shipped fix**. No production, tests, or CI source
was changed by this investigation. It follows the bounded Markdown reference and
CSV Grid work rather than reopening their completed proofs.

## Decision

The next high-value tractable semantic slice is **retain and display the first proved
large-TOML ownership conflict even when Full validation stops and stays Provisional**.
This repairs a concrete discontinuity: the same duplicate is diagnosed below 4 MiB,
but above 4 MiB the policy discards the diagnostic it already computed. The Native
idle publisher then independently discards every non-Complete Full diagnostic.
Fix both boundaries; changing only Formats will not make the error visible.

The question is deliberately narrower than general tolerant recovery or incremental
TOML reuse:

> After a completely certified prefix, can the next bounded, individually valid
> logical statement provide a permanent counterexample to document validity, while
> the unknown suffix keeps the global diagnostic total unknown?

For the existing certified ownership subset, yes. A repeated binding or traversal
through a sealed scalar/inline table already violates TOML at this document version;
no subsequently appended statement can undo it. No claim about the remaining suffix,
complete error count, or regional semantic validity is required. An edit can repair
the conflict, but then it creates a different authoritative snapshot/version.

## Why this slice, rather than another parser replacement

Selected current mechanisms were checked in `JsonIncrementalSession.cs`,
`TomlIncrementalSession.cs`, `TomlOwnershipIndex.cs`, `YamlIncrementalSession.cs`,
`NativeIdleFullAnalysis.cs`, and `NativeEditorController.cs`. The comparison below
is a decision comparison, not a claim that every outstanding failure was surveyed.

| Candidate | Current user consequence and evidence | Next mechanism and relative scope |
| --- | --- | --- |
| TOML known-error retention | Above 4 MiB, `ProcessStatement` turns a non-null `TOML_OWNERSHIP` into `false`; `TryAnalyzeLarge` returns null; `AnalyzeVisible` emits no diagnostics. Fresh probe reproduced seven distinct conflicts in the actual requested suffix. | Keep one existing exact error at the abort, preserve Provisional, and merge its same-version visible diagnostic into existing Native presentation. Small bounded contract change; no new grammar or parser dependency. **Selected.** |
| JSON enclosing-owner reuse | JSON streams the authoritative snapshot for **both** Visible and Full. The prior hosted 100 MiB / 7,489,828-unique-key object took 3.87 s on Windows x64 and 4.59 s on Mac ARM64, allocating 204.51 MiB, one sample per OS; this is not edit latency. | A real reduction needs safe container boundaries and object-local decoded-key identity invalidation. The adversarial corpus is one giant object, so a syntax-only subtree cache does not solve the semantic key dependency or memory budget. Worth a separate design, not this short production slice. |
| YAML tolerant continuation | The existing alias-key recovery now correctly preserves its located error and revokes completion. Existing 100 MiB structured Full/edit observations were 2,818/3,337 ms with 674.5 MiB cumulative allocation, one local sample. Syntax exceptions and noncanonical keys still stop or downgrade. | Continuing after an invalid header/key requires proved collection/anchor context; aliases read preceding anchors and recursive key equality may remain unknown. Retaining already-known diagnostics at Native is useful, but claiming newly recovered YAML semantics needs its own differential proof. |

Prior costs are reused from [architecture.md](architecture.md),
[TOML ownership](toml-large-semantics.md), and
[YAML alias-key recovery](yaml-alias-key-recovery.md); they are different corpora and
environments, **not a cross-format performance ranking**. TOML's earlier 100 MiB
nested-AoT observation (1,512 ms, 1,143.9 MiB cumulative allocation) also remains an
open optimization issue. The selected slice does not fix restream allocation.

## Fresh discriminating probe

Artifacts are entirely under `.temp/semantic-policy-frontier/`:

- `Frontier.csproj`, `Program.cs`: public production session reproduction plus an
  isolated first-witness mechanism; `ProbeOwnership.cs` is a name/namespace-renamed
  copy of the **existing** ownership index, not an independent implementation.
- `provenance.json`: source SHA-256 for the inspected session, ownership index, and
  Native publisher. The relevant source hashes remained unchanged throughout the
  probe even though other branches of work advanced repository HEAD.
- `fixtures/*.toml`, `report.json`, `run.log`: exact large fixtures, all measured
  production results, and the repair/Undo version marker.
- `oracle.py`, `python-oracle.json`: Python 3.14.6 `tomllib` classification of all
  exact large files, independent of Tomlyn and the ownership index.

Run from repository root:

```powershell
dotnet run --project .temp/semantic-policy-frontier/Frontier.csproj -c Release
python .temp/semantic-policy-frontier/oracle.py
```

The probe uses .NET SDK 10.0.400, Tomlyn 2.10.1, Windows 11 build 26200 x64,
Intel Core i9-12900H, 14 cores/20 logical processors. Generation and `Document`
construction are excluded from measured calls. Each production case has three
sequential Full calls in one process; the table reports their median, **not a
process-isolated cold benchmark or p95**. Prototype timing is intentionally not
compared: it uses reflection to reuse the production continuation predicate.

Each fixture contains 1,025 valid 4,096-character comment lines (4,198,400 UTF-16
units) between the prefix and target suffix. That crosses the 4 MiB threshold
without changing key ownership or exceeding 120,000 logical statements. The
requested viewport is precisely the offending suffix, so absence of diagnostics
cannot be explained by offscreen filtering. The maximum fixture is 4,438,410 units.

| Fixture mechanism | Actual large production result | First-witness experiment | Independent Python result |
| --- | --- | --- | --- |
| `a=1` then `a=2` | Provisional, 0 diagnostics, unknown total | Exact `a` conflict at 4,198,404, length 1 | Invalid |
| `a=1` then `"\\u0061"=2` | Provisional, 0 diagnostics, unknown total | Exact escaped key conflict, length 8 | Invalid |
| Scalar parent, inline-table parent, duplicate explicit table, dotted-defined table reopened | All four Provisional, 0 diagnostics | Four precise key/header witnesses | All invalid |
| Multiline value, then duplicate key | Provisional, 0 diagnostics | Exact duplicate witness after correctly delimited multiline statement | Invalid |
| Valid implicit parent `[a.x.y]` → `[a]` → `x.z=3` | Complete, 0 diagnostics, exact total 0 | No witness | Valid |
| Unsupported traversal before a later duplicate | Provisional, 0 diagnostics | Refused witness after certificate loss | Invalid |
| Unsupported traversal and conflict **in the same transition** | Provisional, 0 diagnostics | Refused witness after checking post-transition certificate | Invalid |
| Early malformed statement, or prior statement-count exhaustion | Both Provisional, 0 diagnostics | Refused unproved downstream witnesses | Both invalid |

All **12** fixtures completed; all **seven** emitted experimental witnesses were
rejected independently by Python, and the valid implicit control was accepted.
Tomlyn whole-source validation of the corresponding small prefix+suffix forms
also rejected all seven conflicts. This is finite differential evidence, not a
new proof of all TOML ownership rules. Python validates TOML 1.0; these particular
fixtures use its shared subset with the pinned TOML 1.1 parser.

Across the seven witness cases, the actual production Full call median was
10.82–16.21 ms with 8,462,936–8,469,216 allocated bytes (approximately 8.07–8.08 MiB).
The valid control was also scanned; unknown-prefix cases stop earlier. These values
describe this sparse-comment shape only. The experiment then changed the final `a`
to `b` and rebuilt the witness from the new snapshot (none), followed by Undo and
a further-new version (witness restored): `repair-undo-witness-version-ok`.

### The discriminating result

Three competing hypotheses were separated:

1. **The conflict cannot be known without parsing the complete file.** Rejected for
   these admitted prefixes: the existing index returns an exact error before abort.
2. **Returning Provisional requires erasing every known semantic error.** Rejected:
   `DocumentAnalysis` already permits diagnostics with Provisional and unknown total;
   the YAML recovery path uses this distinction. Whole-file validity is unknown, but
   a located counterexample may be known.
3. **Publishing every diagnostic after arbitrary recovery is safe.** Not supported.
   The same-transition negative control demonstrates why checking certification only
   before the call is insufficient. Stop before publishing if either the prefix or
   post-transition ownership state is unsupported/inexhaustive.

## Ready production slice and exact boundaries

### Formats: preserve one counterexample, do not recover the suffix

Primary writer area: `src/Mote.Formats/TomlIncrementalSession.cs`.

- Replace the overloaded success/failure-null result internally with a small private
  result that distinguishes complete success, uncertainty, and the first proved
  ownership conflict. A public general-purpose certificate framework is unnecessary.
- Validate logical statements and boundaries exactly as now. Capture the existing
  `AddAssignment`/`AddHeader` diagnostic **only if** the ownership index is exhaustive
  and certifiable both before and after the call; a resource/grammar refusal is not
  an ownership error. Do not change `TomlOwnershipIndex` rules in this slice.
- On that first conflict, stop. Return bounded lexical viewport tokens plus this
  current-snapshot located diagnostic. Keep `Completeness=Provisional`,
  `TotalDiagnosticCount=null`, and existing coverage semantics. Never invent an
  exact count of one or certify the remainder.
- Syntax-error retention could be a later additive slice, but is deliberately not
  required here: the minimal mechanism handles only individually accepted statements
  with known ownership transitions. No scan continuation after the first failure.
- Keep every existing statement/line/length/binding cap and cancellation check.
  Carry only one diagnostic, not a retained full tree/source or unbounded error list.

### Native: merge known errors without replacing better visible output

Coordinated writer area: `src/Mote.Native/NativeEditorController.cs`, especially
`PublishIdleFullAnalysis` and `SessionDiagnosticSummary`.

The current non-Complete branch merely publishes a status containing global unknown
and returns, erasing the Full diagnostic payload. Preserve that behavior for
unrelated outputs; add an explicit narrow merge for the TOML known-error slice:

1. Reuse **all** existing document/driver/policy/generation/version/viewport guards.
2. Preserve the currently published Visible tokens, preview, Flow, source maps, and
   layout. Do not replace them with an empty/less useful partial Full projection.
3. Deduplicate the known error by code/span and merge it into only the matching
   viewport's source diagnostics and diagnostic summary, within the current bound.
   Offscreen known errors must not create a fake exact global count; global remains
   unknown. This slice need not add a new navigation panel for offscreen errors.
4. Keep `Provisional` and explicitly state global diagnostics unknown alongside the
   observed error. Repair, Undo/Redo, Open/New, policy changes, and page changes must
   not preserve an old-version conflict.

Implementation detail: `_visibleSessionAnalysis` is currently only a
`NativeAnalysisView` (projected tokens and a diagnostic-summary string), not a
retained source-diagnostic payload. The writer therefore needs a private paired
current-visible frame containing the already-bounded `NativeCanvasSemantics`, or
equivalent bounded source token/diagnostic lists. Do **not** parse the summary string
back into diagnostics and do not retain an unrestricted whole format tree merely
to perform this merge. Invalidate this pair at every existing
`_visibleSessionAnalysis = null` site. A single paired record is less error-prone than
several independently stamped fields.

No public `IFormatSession`, `DocumentAnalysis`, `IDocumentPolicy`, Engine source,
Native ABI, theme/config, binary packaging, LSP, or workspace contract needs change.
`TomlOwnershipIndex.cs` need not change. There is no runtime reflection in the
recommended production path; reflection exists only in this experiment.

Tests can be a **new** TOML-specific test file under `tests/Mote.Tests/` to avoid
editing the shared `IncrementalPolicyTests.cs` writer area. Native controller
tests must challenge publication, not merely Formats output. Required acceptance:

- Above-4-MiB visible duplicate/escaped-equivalent/scalar-prefix/header/inline-table
  cases yield one exact located conflict with unknown total and unchanged source.
- A supported valid control remains Complete; unsupported-prefix, unsupported
  same-transition, syntax-before-conflict, and each existing cap do not manufacture
  downstream ownership errors.
- A non-Complete idle Full result with the known error augments the visible error
  layer/summary while tokens, preview and Flow remain unchanged.
- Wrong document, version, policy or viewport refuses the merge; repair and Undo
  publish only the appropriate new-version witness. Cancellation publishes nothing.
- Existing complete-result promotion and other-format Provisional preservation
  remain unchanged; a missing history chain rebuilds rather than reusing a witness.

**Readiness:** ready for a coordinated Formats + Native production slice and
independent review. Native UI integration is not yet experimentally verified by
this research executable. Do not call the fix shipped until those tests and target
CI pass. If a larger generic partial-result merge is preferred, first specify
which diagnostics are exact observations and avoid accidentally downgrading better
Visible rendering from YAML/TOML. The smaller TOML-specific route is sufficient.

## External reality: specifications, production tooling, and research

The normative [TOML 1.1 keys and tables](https://toml.io/en/v1.1.0#keys) distinguish
binding redefinition and sealed values; comments do not alter key semantics. This
justifies the sparse comment padding and why a proved conflict cannot be healed by
an appended suffix. By contrast, [JSON RFC 8259 §4](https://www.rfc-editor.org/rfc/rfc8259#section-4)
treats unique object names as an interoperability recommendation, not the same
unconditional invalidity rule. [YAML 1.2.2](https://yaml.org/spec/1.2.2/#3213-node-comparison)
defines mapping-key equality structurally, with aliases reading preceding anchors;
do not transfer the TOML prefix certificate to it unexamined.

For mature engineering practice, [Tomlyn's official low-level API](https://xoofx.github.io/Tomlyn/docs/low-level/)
already separates lossless syntax/diagnostics from object serialization; mote should
continue to use its bounded validated statements and original source, not serialize
a repaired model over the file. [Tree-sitter's official project](https://github.com/tree-sitter/tree-sitter)
similarly treats useful error-tolerant structure as a tooling goal. Neither an error
node nor incremental syntax reuse independently proves namespace ownership. The
practical lesson is **preserve useful known facts without upgrading their scope**,
not replace the parser merely because a framework advertises incrementality.

Recent peer-reviewed [AnyText, SLE 2025](https://doi.org/10.1145/3732771.3742716)
combines scannerless incremental packrat parsing, left recursion, reference
resolution, and grammar-directed formatting. Its [authors' implementation docs](https://nmfcode.github.io/anytext/index.html)
explicitly distinguish incremental semantic-model updates from syntax construction.
That is a relevant longer-term direction for policy-private dependency propagation;
its LSP-oriented integration and DSL evaluation are **not** evidence of mote's
single-binary AOT suitability or large-TOML budget. No dependency adoption is proposed.

The directly relevant peer-reviewed [Fast Incremental PEG Parsing, SLE 2021](https://people.seas.harvard.edu/~chong/pubs/gpeg_sle21.pdf)
uses shiftable interval-tree memoization and demonstrates typical fast reparses on
large inputs. This supports future boundary-index work, but does not remove TOML's
source-order namespace dependencies or establish an exact diagnostic total after
unknown recovery. The selected repair comes first: **make the already-computed
semantic counterexample survive policy and presentation boundaries**, then measure
incremental ownership reuse as a separate question with explicit retained-memory
and dependency-read/write budgets.
