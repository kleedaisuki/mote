# TOML normative ownership correctness review

Date: 2026-10-01. Scope: the first atomic correctness patch to
`TomlOwnershipIndex.cs`, `TomlIncrementalSession.ProcessStatement`, the pinned
TOML 1.1 conformance corpus, and corresponding directed regressions. The proposed
statement-summary/incremental-reuse architecture is not implemented by this patch
and is not certified by this review.

## Verdict

No substantive production defect found in the reviewed change. The three changes
replace incorrect certification/refusal behavior with uniform normative rules,
without adding fixture-specific exceptions. Integration still needs the existing
controller fixture to be corrected to use a genuinely provisional input: its
array-table parent re-entry is now correctly Complete, so its old expectation
cannot remain a release gate. This is a stale test assumption, not evidence of a
new analyzer failure.

## Source and external contract assessment

* `TomlOwnershipIndex.AddHeader` replaces the latest array-element scope on each
  append. Headers traverse each array binding's latest scope independently; earlier
  scopes are no longer reachable. Nested headers therefore do not prevent appending
  another parent element, and duplicate detection remains local to the new element.
  This directly follows the official [TOML 1.1 arrays-of-tables rules](https://toml.io/en/v1.1.0#array-of-tables),
  including the fruits/varieties parent re-entry example.
* `ResolveParent` rejects dotted traversal of explicitly declared ordinary tables
  and array tables. Implicit header parents can become dotted-defined; later dotted
  siblings can traverse dotted-defined parents. These are distinct states, not a
  blanket ban on nested keys. The official [table rules](https://toml.io/en/v1.1.0#table)
  prohibit redefining explicit tables via dotted keys while allowing header-defined
  subtables below dotted-defined parents.
* `ProcessStatement` now requires syntax validation before accepting a zero-key,
  zero-header trivia statement. The removed C# whitespace/comment shortcut admitted
  invalid bare CR, form feed, and vertical tab. Official [preliminaries](https://toml.io/en/v1.1.0#preliminaries)
  and [comments](https://toml.io/en/v1.1.0#comment) define narrower whitespace/newline
  and comment-control rules. Invalid or unsupported syntax remains Provisional,
  rather than being mislabeled globally valid.
* Existing statement, line, statement-count, and binding budgets are unchanged.
  Exhaustion still refuses Complete. A proved ownership violation stops analysis
  with a first witness and an unknown total; an uncertified syntax prefix does not
  manufacture a later ownership witness. No public policy/session contract changed.

## Independently inspected evidence

The review did not rerun the completed full test invocation. It independently
parsed the retained TRX, checked corpus bytes against the pinned local upstream
checkout, and inspected executable ownership transitions and directed test diffs.

| Evidence | Observed result | What it establishes |
| --- | --- | --- |
| Upstream checkout revision | `ff49d109861c1ad25af53f687f2aef19ab650600` | Matches the declared corpus pin |
| Embedded JSONL versus upstream manifest/files | All 712 names, order, expected classes, and decoded bytes match | No silent filtering or rewritten fixture source |
| `.temp/toml-conformance/normative.tsv` | 218 valid Complete; 485 invalid Provisional; 9 invalid byte-encoding refusals | Zero false Complete in decoded published validity corpus |
| `.cache/toml-normative/toml-normative.trx` | 796 executed; 795 passed; 1 failed; zero unexecuted | Actual focused run is not all green |
| Sole failed result | `Controller_idle_toml_provisional_full_pass_preserves_visible_facts`; observed `TOML · Complete · v0`, no shell errors | Old provisional fixture expectation conflicts with newly correct AoT validity |
| Directed differential retained summary | 10,000 random + 30,000 adversarial; Python/Rust disagreement 0; Complete counts 3,301 + 5,706; false Complete 0 | Additional finite cross-parser validity evidence, not general proof |

The upstream MIT license and byte corpus are retained in
`tests/Mote.Tests/Fixtures/Toml/`. The conformance test invokes the actual production
large-statement algorithm directly, avoiding padding hundreds of short fixtures;
separate ballast tests exercise the real greater-than-4-MiB dispatcher.

## Limits and subsequent work

* Published expected JSON values are not compared. This is syntax/ownership
  validity evidence, not decoded-value equivalence or complete semantic rendering.
* Nine ill-formed UTF-8 fixtures are classified by the strict test decoder before
  creating a UTF-16 snapshot. They are not credited as analyzer rejections, and do
  not verify the engine's actual encoding policy.
* Small files still use Tomlyn whole-document validation. This patch fixes the
  large statement ownership algorithm, not disputed small-file Tomlyn behavior or
  all size-independent policy semantics. The architecture document explicitly
  distinguishes the normative ownership index from that whole-file oracle.
* Parsing every trivia statement may add CPU cost relative to the unsafe shortcut.
  No new performance result was produced here. Preserve syntax correctness while
  measuring the planned statement-summary reuse; do not restore the bypass.
* Finite corpus success does not remove resource-completeness limits, certify
  incremental reuse, prove cancellation races, or establish native GUI performance.

Practical next step: repair the controller test's provisional fixture with a real
resource/syntax refusal, retain its actual idle-publication behavior assertions,
then validate that changed fixture and proceed to the independently reviewed
statement-summary reuse change. No unrelated redesign is required for this patch.
