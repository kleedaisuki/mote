# Uniform public TOML validation review

Date: 2026-10-01. Scope: pending `TomlPolicy.cs`, new
`TomlDocumentValidation.cs`, unbounded `TomlStatementReader.Validate`, local
diagnostic extraction, and configurable ownership binding budget. No production
or test files were changed by this review.

## Verdict and integration condition

Updated review: the original first-witness-only public validation **did lose
independent public diagnostics**, a material issue identified by root after this
review's initial assessment. That earlier clean assessment was too broad. The
current recovery revision fixes the underlying state model rather than adding
per-fixture exceptions: local units can recover, invalid header contexts quarantine
assignments, and failed ownership operations roll back mutations. No substantive
unresolved production defect found in the revised recovery source. Final evidence
for the newly added illegal-leading-trivia header cases remains pending below.

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
  explicit diagnostic rather than a false valid result. Recoverable local/global
  witnesses are retained alongside the whole grammar parser's witnesses, exact
  duplicate records are collapsed, and the final list is source-ordered. No fully
  exhaustive recovery claim is made for unrecoverable malformed multiline owners.
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

## Recovery correction and evidence

The public recovery journal is opt-in; normal bounded cache analysis allocates no
journal and keeps first-error refusal. A failed assignment reverses newly added
bindings and implicit-parent origin conversions in reverse order. A failed header
also clears the current assignment scope; a subsequent valid header restores an
owned scope. Invalid syntax never enters the namespace index. Header-only syntax
cannot legitimately continue onto another physical line, permitting a malformed
header to be diagnosed/quarantined without swallowing later independent headers.
Unclosed multiline assignment values remain conservative rather than inventing
statement seams.

During review, a conditional gap was reported: raw space/tab-only header detection
could miss a malformed header with leading FF/VT/NBSP/NUL and falsely attribute
following assignments to the previous scope. The owner proactively corrected this
before landing. Recovery now uses parser-recovered table identity or a conservative
header prefix that skips Unicode whitespace/controls **only for quarantine**. The
syntax parser still rejects non-TOML trivia. Broader recovery classification cannot
make invalid source valid. Retained directed illegal-prefix tests are being added;
source inspection alone is not their execution certificate.

Independently parsed retained TRXs, without rerunning suites:

| Artifact | Executed / passed | Interpretation |
| --- | ---: | --- |
| `toml-uniform.trx` | 730 / 729 | Original integer-overflow expectation failed; retained, not hidden |
| `toml-uniform-final.trx` | 732 / 732 | Corrected textual numeric preservation and full public corpus checks |
| `toml-recovery-final.trx` | 797 / 796 | A malformed-header test initially also excluded a legitimate whole-parser grammar witness |
| `toml-recovery-corrected.trx` | 797 / 797 | Test distinguishes unchanged grammar evidence from unsupported added namespace evidence |

All four have zero unexecuted results. These are checkpoints, not an execution
certificate for subsequent source changes.

The numeric expectation was corrected: TOML permits larger integers when supported
losslessly, and mote's numeric IR retains the exact original token text. New tests
check min/max signed 64-bit and the larger positive literal before and after
formatting. This does not certify the dependency's decoded scalar representation;
actual package behavior and cached upstream source are distinct evidence.

The retained public tests classify all 712 pinned sources, separately identify nine
encoding-boundary refusals, check valid formatting idempotence/value-tree/comment
retention, reject invalid rewrites, test two independently minimized array-reentry
cases, verify UTF-16 witnesses, and opt out of each large-cache budget. Recovery
controls cover independent duplicate scopes, conflicting/malformed headers,
rollback of failed dotted implicit-parent conversion, local inline errors followed
by global errors, and preservation of whole grammar witnesses when multiline
recovery is incomplete.

## Limits and next verification

The public path now builds the lossless tree and performs a separate local
validation pass. It retains the existing tree/value projection contract but may
cost additional CPU and allocation. This review produces no performance estimate;
the separately planned matched tiny/8-KiB/128-KiB comparison must determine actual
overhead rather than treating correctness as proof of performance neutrality.

This review does not certify decoded values against all upstream JSON oracles,
engine UTF-8 policy, native GUI/AOT execution, macOS behavior, arbitrary nesting,
or budget-free large-session certification. Retain all original failure artifacts and validate newly changed recovery cases
before treating this revision as integration-ready; do not repeat unrelated
completed work.
