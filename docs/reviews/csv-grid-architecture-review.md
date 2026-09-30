# CSV Grid architecture independent review

Date: 2026-09-30. Scope: target `docs/csv-grid-architecture.md` (not an implemented Grid). Repository inspected at `7388ec8`, including existing CSV incremental/windowed contracts, sparse/oversized-record certificates, serialized Native driver, installed Flow identity, Engine range transactions and Windows clipboard adapter. No production or design files modified. Independent probes are retained in `.temp/csv-grid-review/`.

## Disposition

The core design is coherent with current code and strict single-binary distribution. Two small but concrete clipboard contract corrections are needed before implementing the promised complete data commands. Neither requires changing the Grid architecture. No other material design blocker found in the inspected scope. Native scrolling/accessibility/ABI and performance remain explicitly unproved target gates, not established acceptance.

## Findings

### P2 — Specify empty final-row serialization to preserve selected row count

Location: design section 5, `Copy cells` / `Copy cells as CSV`, lines 320–325.

The listed quoting triggers cover delimiters, quotes and line breaks, but not a single selected empty field. Under the current CSV grammar, an empty document contains zero rows, and a terminal record delimiter closes the preceding record rather than creating another row. Thus a straightforward serializer following the stated triggers loses an empty one-column final row:

| Selected logical values | Naive serialized CSV | Reparsed rows | Required rows |
| --- | --- | --- | --- |
| `[[""]]` | empty string | 0 | 1 |
| `[["a"], [""]]` | `a\r\n` | 1 | 2 |

Independent managed Release probe using the actual `CsvPolicy` confirmed: empty string => 0 rows; `""` => 1 empty row; `a\r\n` => 1 row; `a\r\n""` => 2 rows. This is a serialization contract gap, not a current Grid implementation defect and not a reason to change empty-document semantics.

**Remedy:** explicitly quote empty values in structured CSV/TSV Copy (simplest uniform rule), or require an equivalent row-count-preserving final-empty-record rule. Keep `Copy cell value` distinct: its scalar empty string is legitimate. Add round-trip tests for one empty cell, several one-column empty rows, an empty final row after a nonempty row, and ordinary multi-column trailing empty fields. For TSV external-client interoperability remains a separate target gate; this finding already holds against mote's own CSV parser.

Confidence: high for parser counterexample; implementation impact conditional on the currently underspecified encoder choosing minimal quoting.

### P2 — Admit clipboard payloads against Windows NUL-terminated representation

Location: design section 5 complete decoded-value/source-row Copy promises and publication/admission rules, lines 317–350; existing `WindowsEditorShell.SetClipboardText` at approximately line 682.

The parser accepts embedded U+0000 as field data. The independent probe parsed `"a\0b",tail` with zero diagnostics and a three-code-unit decoded value. Windows ordinary Unicode clipboard format `CF_UNICODETEXT` is terminated by NUL. The existing adapter writes the complete managed string plus a final NUL to this format, so a standard consumer sees only `a`; successful `SetClipboardData` does not certify that the consumer can obtain `a\0b`. CSV/TSV quoting does not remove this representation limit. It also affects `Copy source rows` if the source interval contains NUL.

The design requires complete payload or explicit failure, not a silently truncated success. Size admission alone does not satisfy that contract. This is a target-design admission gap; no clipboard was mutated in this review.

**Remedy:** before clipboard clearing/publication, explicitly reject embedded NUL for standard text Copy with a clear message and preserve the previous clipboard. Source editing/Save remain available and unchanged. A future explicitly named escaped export or length-aware private data format is an alternative operation, not an excuse to advertise lossless ordinary text Copy. Include a no-clipboard-mutation rejection test and an ordinary non-NUL successful payload test. Do not normalize source content or silently strip NUL. If cross-platform Copy permits different capabilities, surface that explicitly rather than inventing a universal lossless claim.

Primary evidence: [Microsoft standard clipboard formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/standard-clipboard-formats), `CF_UNICODETEXT` definition.

Confidence: high for the Windows format limit and accepted source fixture; Grid command implementation is not yet present.

## Compatibility and design checks with no substantive finding

- Existing sparse checkpoints are source offsets, not ordinals. `SemanticNode.Children` omits oversized fields. The design correctly requires policy-certified row/column ordinals and descriptors rather than Native comma/newline counting or child-index renumbering.
- Full cache commit currently happens after projection/final cancellation; the additive Grid bundle preserves this. Driver integration must use its own combined session call, not call existing Analyze and commit edits before a later failing Grid projection. The design already states the required atomic candidate rule.
- Dense suffix reuse needs refreshed first-row width dependencies; global extent scalars must be derived from current records. The design preserves the distinction between global completeness and bounded delivery.
- Source UTF-16 spans versus decoded arena spans, zero-width existing fields versus Missing, and giant origin versus giant decode requests are distinguished correctly.
- Engine remains the single editable/Undo owner; stamped whole-field replacement rejects locally invalid syntax and preserves neighboring syntax/delimiters. Exact origin checks plus composition settlement are appropriate integration requirements.
- Same-version presentation sequence, stale selection/action rejection, close cancellation and source-version invalidation agree with current Flow/controller identity. Copy is expressly noncommitting and must not consume the driver's edit chain.
- Bounded native slots avoid both unknown extents and ListView's actual 100,000,000 virtual-item ceiling. The newline-record counterexample is compatible with `CsvPolicy`'s logical-record rules. Programmatic AppKit table construction does not inherently require an app/nib/helper sidecar.
- Domain-owned rectangular selection is necessary because row-selection tables do not supply complete spreadsheet semantics. Accessibility is correctly made a native probe and actual-reader gate; default native table behavior is not claimed to satisfy logical coordinates/offscreen/rectangle semantics automatically.
- Giant warm parser figures are not promoted to native paint/p95 claims. Cold Full allocation costs, index charging, bounded callback work, prepared model limits and target-platform performance remain separately measurable obligations.

## External primary-source verification

Opened on 2026-09-30:

- [Microsoft LVM_SETITEMCOUNT](https://learn.microsoft.com/en-us/windows/win32/controls/lvm-setitemcount): verifies 100,000,000 owner-data item ceiling.
- [Microsoft ListView overview](https://learn.microsoft.com/en-us/windows/win32/controls/list-view-controls-overview): verifies creation-time owner-data requirement, unsupported dynamic toggling, ready owner callbacks/cache hints and unsupported item-text storage messages. These support the adapter choice, not performance or accessibility acceptance.
- [Apple programmatic table guide](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/TableView/PopulatingView-TablesProgrammatically/PopulatingView-TablesProgrammatically.html): confirms reusable programmatic table/view mechanism. ABI/lifetimes still require real target execution.
- [Microsoft DataGrid control requirements](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-supportdatagridcontroltype): confirms a DataGrid assertion has control-pattern obligations beyond naming.
- [Garcia 2024 journal article](https://journals.sagepub.com/doi/abs/10.3233/DS-240062): independently confirms abstract's 93.38% average accuracy on 548 CSV files. The design correctly treats inference as tentative, not grammar certification.
- [FastLanes 2025 paper](https://www.vldb.org/pvldb/vol18/p4629-afroozeh.pdf): retrieved; transferable metadata-granularity argument does not establish editable CSV random-access bounds. Full benchmarking comparison was not independently replicated. Pollock PDF retrieval timed out; its detailed experimental claims were not reverified in this review.

## Reproduction and limits

`dotnet run --project .temp/csv-grid-review/Probe.csproj -c Release` on the Windows host, .NET 10. Probe sources and `results.txt` retained under that directory. This is a new discriminating boundary probe, not a rerun of completed CSV suites. No production native Grid exists to execute, no native clipboard mutation was performed, and no Mac/Narrator/VoiceOver/performance acceptance is inferred. Illustrative IR constructors are not present, so their invariant validation cannot be reviewed as implemented code.

## Targeted correction review — both findings closed in design

The author froze the revised design and the reviewer inspected only the affected Copy rules/counterexamples/acceptance gates, without rerunning the completed probe or reviewing unrelated changes. Section 5 now always quotes empty structured-copy fields, explicitly quotes CSV commas, CRLF-joins records without a synthetic terminal delimiter, and distinguishes scalar empty Copy and zero-selection no-op. It supplies the exact final-empty-row counterexample and requires rectangle row/width/value round-trips, with a separate matching TSV oracle. **Finding 1 resolved at design level.**

Section 5 now rejects embedded U+0000 for every native text Copy mode on both platforms before any clipboard mutation, including fully prepared offscreen values/source intervals. Source and previous clipboard remain unchanged on preparation refusal. It correctly does not claim AppKit strings have the Windows NUL representation limit, and distinguishes post-mutation OS publication failure from admission failure rather than promising unsupported clipboard rollback. Target pure/real native gates require actual nonmutation evidence. **Finding 2 resolved at design level.**

Final disposition: **no remaining substantive target-design blocker found in this review scope**. This closes document findings only; it does not certify nonexistent Grid implementation, serialization/clipboard tests not yet written, native table accessibility, strict AOT runtime behavior or measured performance.
