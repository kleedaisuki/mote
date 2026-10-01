# Uniform public TOML validation review

Date: 2026-10-01. Scope: pending `TomlPolicy.cs`, new
`TomlDocumentValidation.cs`, unbounded `TomlStatementReader.Validate`, local
diagnostic extraction, and configurable ownership binding budget. No production
or test files were changed by this review.

## Verdict and integration condition

No demonstrated new production correctness defect found in the uniform-validation
mechanism. One retained test is currently failing and must be resolved explicitly
before claiming a green validation result. It concerns positive signed-integer
overflow, whose relationship to the previous actual Tomlyn package behavior needs
verification; do not classify it as a new regression solely from this test.

## Semantic and compatibility assessment

* The whole-document lossless parse still determines grammar diagnostics and
  projection. `validate:false` disables Tomlyn's global semantic visitor, not its
  grammar parser. Grammar-clean input then undergoes independently validated local
  statements plus the shared normative source-order ownership index. No disputed
  array-reentry fixture is special-cased.
* Local syntax validation remains enabled, including inline-table ownership. All
  valid assignments seal their external namespace. Headers and dotted traversal
  retain the previously reviewed latest-element/implicit-parent distinctions.
  Whole grammar failures do not become globally valid through statement splitting.
* Public validation uses `StringReader`, no second Document/rope copy, and releases
  per-statement summaries rather than accumulating an IR cache. It explicitly opts
  out of the four large-session budgets: statement length, physical lines, statement
  count, and binding count. The bounded cache reader and default 200,000-binding
  index keep their existing limits. Tomlyn's own depth/representation limitations
  are not removed by the word "unbounded".
* Syntax/local diagnostic coordinates are shifted by the actual statement start;
  global ownership witnesses already carry absolute key coordinates. Public codes
  remain `TOML_PARSE`. A grammar-clean unexplained statement refusal produces an
  explicit diagnostic rather than a false valid result. First-witness behavior is
  not advertised as an exhaustive invalid-document diagnostic enumeration.
* Empty input, trivia, a newline-terminated last unit, and final EOF segments all
  pass through the same scanner. Unbounded EOF still invokes the parser even if
  the boundary tracker says continuation, allowing actual syntax diagnostics
  rather than silently suppressing the reason for refusal.
* Format validates both original and candidate with uniform ownership, then keeps
  the existing lossless projection-equivalence check. Only horizontal assignment
  gaps change; comments, scalar text and multiline contents are not rewritten.
  Invalid original or invalid candidate returns the original source.
* Cancellation checks exist before grammar work, during streaming/statement
  acceptance, diagnostic/token projection and before returning validation. A single
  Tomlyn whole-tree/local parser call itself is not interruptible; that limitation
  already existed and is not a new prompt-cancellation guarantee.

External contracts: official [TOML 1.1 tables](https://toml.io/en/v1.1.0#table),
[inline tables](https://toml.io/en/v1.1.0#inline-table), and
[arrays of tables](https://toml.io/en/v1.1.0#array-of-tables) support this ownership
model. The [integer section](https://toml.io/en/v1.1.0#integer) permits sizes beyond
signed 64-bit only when supported losslessly; simply exceeding `long.MaxValue`
does not by itself establish an invalid TOML grammar production.

## Evidence inspected

Independently parsed `.cache/toml-uniform/toml-uniform.trx`: **730 executed, 729
passed, 1 failed, zero unexecuted**. The sole failed case is
`Local_semantic_or_trivia_errors_are_not_lost("a=9223372036854775808\n")`:
the observed diagnostic collection is empty. This is not a 730/730 result.

The retained tests exercise all 712 pinned sources through the public Analyze and
Format APIs, distinguish nine decoder refusals from policy errors, check valid
format idempotence/value-tree equivalence/comment retention, reject invalid
rewrites, cover the independently minimized array-reentry cases, verify exact
UTF-16 witnesses including CRLF/non-BMP prefixes, and opt out of each large-cache
budget using valid public inputs. These tests are substantially better evidence
than testing the internal ownership trie alone.

The failed overflow assertion needs an actual-package old/new comparison. The
cached upstream lexer source contains an overflow guard, but its behavior cannot
be assumed identical to the installed NuGet 2.10.1 runtime. The review did not
establish a dirty local source modification. The editor's projected numeric value
retains original text; a wrapped dependency decoded scalar and a lossless textual
IR are separate questions. Preserve that distinction when deciding the test's
expected behavior or recording parser debt.

## Limits and next verification

The public path now builds the lossless tree and performs a separate local
validation pass. It retains the existing tree/value projection contract but may
cost additional CPU and allocation. This review produces no performance estimate;
the separately planned matched tiny/8-KiB/128-KiB comparison must determine actual
overhead rather than treating correctness as proof of performance neutrality.

This review does not certify decoded values against all upstream JSON oracles,
engine UTF-8 policy, native GUI/AOT execution, macOS behavior, arbitrary nesting,
or budget-free large-session certification. Resolve the sole test expectation
with runtime evidence, retain its original failure artifact, and validate the
focused correction without repeating unrelated completed work.
