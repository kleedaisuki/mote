# TOML single-grammar validation investigation

## Decision and result

A supported **read-only syntax traversal plus explicit normative ownership** is a
credible way to remove the public-policy second grammar construction on sources
whose whole lossless parse has no grammar errors. There is **no public Tomlyn
2.10.1 API for selectively validating an existing syntax node/tree**. This is not a
recommendation to call the internal validator through reflection, reparent nodes,
mutate a document, or skip semantic checks.

The bounded construction probe classified all **703 decoded pinned corpus files**
(218 valid, 485 invalid), both historical valid latest-AoT minimals, and four directed
controls correctly: **709 observations, zero validity mismatches**. This establishes
feasibility on the retained domain, not general correctness or diagnostic equivalence.
Three corpus cases had different local diagnostic spans. Malformed-source recovery
remains an independent proof obligation. No timing, AOT, GUI or native experiment
was performed; the already completed cost measurements were not repeated.

The safest implementation boundary for a future worker is:

```
whole lossless parse(validate: false)
  grammar errors present -> retain today's validated-statement recovery path
  grammar clean          -> read existing nodes once:
                            local structural/value ownership checks
                            normative global namespace transitions
```

This is a semantic-certification boundary, not a fixture exception. Malformed inputs
continue to receive the established grammar plus recoverable local/global witnesses.
Do not attempt to eliminate that fallback until recovery equivalence is demonstrated.

## Provenance and method

Investigation date: 2026-10-01. Production source checkpoint `70def6d`; pushed product
checkpoint `c856816`; current documentation-only parent HEAD `af45be5`. No production
files were modified. Experiment artifacts are under `.temp/toml-single-grammar/`.
The probe references existing Release assemblies rather than rebuilding production.
Its assembly name is `Mote.Tests` solely to access the repository's existing friend
assembly normative index; it uses public Tomlyn node APIs. Reflection inventories
public API/type visibility only: no internal validator is instantiated or invoked.

| Evidence | Identity |
| --- | --- |
| Actually runtime-loaded Tomlyn net10.0 DLL SHA-256 | `2366B14AE842BF9D4EADF206C8B20C7C2217FF75B5F4567999C5C7A9CAAE6DEA` |
| Actually loaded Mote.Formats DLL SHA-256 | `C8F3A5FFA6A0BC42B58F2A5D91EBC3AF35074B1F16191A9FB16A0C6125F296E2` |
| Retained fixture JSONL SHA-256 | `9B23EAF58BD14C31F84A33F10648A905B2FF1F3E2E9C4AA328C6D7454F8F88EA` |
| NuGet 2.10.1 nuspec repository commit | `6cc82dd23a2c0aefb858f92a51c1b184e2812103` |
| Existing cached upstream checkout HEAD, **not matching package revision** | `353e75df51929dc20df616cac2ecabe39297a72a` |
| Fixture upstream revision | `ff49d109861c1ad25af53f687f2aef19ab650600` |
| Runtime | .NET 10.0.11, Windows x64; SDK 10.0.400 |

The package's repository metadata identifies the matching published source revision;
it is not a source-built reproduction of the NuGet binary. Four relevant files were
fetched from that exact revision, not from cached HEAD. Runtime behavior/API inventory
comes from the hashed installed DLL. `evidence.json` records downloaded source hashes
and the result artifact hash; `results.jsonl` records loaded locations/hashes and all
semantic-only invalid cases plus controls. Nine invalid UTF-8 files are separate
strict decoder failures, never credited to tree validation.

Reproduce from repository root:

```
dotnet run --project .temp/toml-single-grammar/Probe.csproj -c Release
python -B .temp/toml-single-grammar/audit.py
```

The project contains direct local HintPaths including the installed package path;
adjust that path if the package cache belongs to another user. Do not treat a build
log or source revision alone as proof of the runtime-loaded dependency: verify the
identity row. No new package or production dependency was introduced.

## What `validate:false` actually omits

The installed assembly exposes internal `Tomlyn.Syntax.SyntaxValidator` and internal
`Tomlyn.Helpers.TomlKeyValidation`; exported syntax/parse types have no declared public
method whose name contains `Validat`. The matching
[SyntaxParser source](https://github.com/xoofx/Tomlyn/blob/6cc82dd23a2c0aefb858f92a51c1b184e2812103/src/Tomlyn/Parsing/SyntaxParser.cs)
constructs the same grammar tree first, then conditionally invokes the internal
validator only when `validate` is true and the document has no errors.

The complete matching
[SyntaxValidator source](https://github.com/xoofx/Tomlyn/blob/6cc82dd23a2c0aefb858f92a51c1b184e2812103/src/Tomlyn/Syntax/SyntaxValidator.cs)
was inspected. Its additional work consists of namespace ownership, supported value
kind selection, missing-token/key/value/bracket/newline structural checks, and array
item comma/value checks. Primitive visitors check token presence; they do not
reparse numbers, Unicode escapes or dates. Inline tables are traversed through the
same namespace machinery, so removing global ownership without replacing inline
ownership would be wrong.

[Lexer source](https://github.com/xoofx/Tomlyn/blob/6cc82dd23a2c0aefb858f92a51c1b184e2812103/src/Tomlyn/Parsing/Lexer.cs)
already checks numeric spellings/conversion, string escapes, scalar/control characters
and date/time token decoding before `validate` is considered. The matching
[Parser source](https://github.com/xoofx/Tomlyn/blob/6cc82dd23a2c0aefb858f92a51c1b184e2812103/src/Tomlyn/Parsing/Parser.cs)
constructs the typed primitive/container nodes. Consequently `validate:false` must
not be described as lexing-only or as inherently losing every scalar check.

| Corpus population | Whole grammar result | Additional requirement |
| --- | --- | --- |
| 218 valid | No grammar errors | Correct global and inline ownership must still accept |
| 422 decoded invalid | Grammar errors already present | Preserve grammar diagnostics and current recovery |
| 63 decoded invalid | No grammar errors | Global/inline ownership must reject |
| 9 invalid byte sequences | Strict UTF-8 decode fails | I/O encoding boundary, not syntax visitor coverage |

The probe's fresh-inline-scope plus global-index traversal rejected all 63 semantic-only
invalid corpus files. The source/API analysis and runtime corpus are complementary:
source explains why scalar validation remains; finite corpus cannot prove every scalar
or structural invariant. An optimization should explicitly cover every structural
check rather than rely on a corpus-only assertion that parser-produced trees are sound.

Important range distinction: current production intentionally preserves optional TOML
integers beyond signed 64-bit range losslessly. Do not add an invented Int64 rejection
while claiming to restore local validation. Preserve the existing integer tests and
source text. Numeric/date invalidity must continue to follow the existing parser and
normative supported TOML version, not an independently guessed host-type constraint.

## Concrete construction

The prototype is `Program.cs`; it performs no AST changes. The product construction
should be a typed, cancellable semantic walker, not a reflective generic framework.

1. Keep the existing whole lossless tree. Tomlyn nodes expose setters and parent
   relationships: they are **not intrinsically immutable** like Roslyn nodes. Treat
   the tree as read-only for the full call; never adopt a node into a new document.
2. Traverse root key/value nodes, then tables in source order, then each table's
   items. Parser output groups assignments under their owning table; these typed
   sequences preserve the required namespace order. A worker should assert monotonic
   source spans instead of relying on names alone.
3. Check keys, typed values, required token/container structure and supported date
   kinds. Scalar grammar/decoding diagnostics are already attached to the whole tree.
   Do not convert a missing or unsupported value into an accepted sealed binding.
4. Before adding a global assignment, recursively validate its value's **local**
   namespaces. Each inline table gets a fresh normative ownership index with only
   assignment operations. Dotted assignments create traversable implicit parents;
   assigned primitives, arrays and nested inline values seal their path. Validate
   nested values first; a failed local unit does not insert its outer global binding.
5. Each array element recursively validates its value independently; inline-table
   scopes in separate elements must never share bindings. Arrays have no global
   externally traversable namespace. All validated assignment values therefore share
   the existing sealed-value external effect irrespective of primitive category.
6. Replay successful assignments/table/AoT headers through the existing unbounded
   recovery-enabled normative index. Preserve its journal rollback and failed-header
   quarantine. Fresh latest AoT scopes remove the old single-index false rejection.
7. Emit current absolute UTF-16 spans directly from nodes, with existing public
   `TOML_PARSE` IDs. This removes statement copying/reparsing, not validation itself.
   Use a bounded explicit traversal stack/cancellation checks rather than assuming
   recursive depth will be harmless under all parser options.

This needs no AST mutation/reparenting, private reflection, dynamic code generation,
additional parser, or unsupported Native AOT mechanism. That is a source-level
compatibility argument, **not** an executed Native AOT validation claim.

## Negative findings and compatibility obligations

### Diagnostic equivalence is not yet delivered

The experiment intentionally records this failed stronger claim. Three semantic-only
invalid corpus files differ in local span sets:

- `invalid/inline-table/duplicate-key-01.toml`
- `invalid/inline-table/duplicate-key-02.toml`
- `invalid/inline-table/overwrite-10.toml`

The prototype reports key-only spans through `TomlOwnershipIndex`; current statement
validation anchors inline ownership on the whole offending key/value node. The
independent `bad={x=1,x=2}` control similarly gives `(9,1)` versus current `(9,3)`.
Global quarantine and rollback controls match exact current spans, but that does not
justify changing inline diagnostic anchors. A worker must preserve existing anchors
and decide explicit message compatibility before landing. No filtering by diagnostic
message or per-case whitelist is admissible.

### Grammar recovery cannot simply be ignored

The malformed `[broken` control preserved a later independent duplicate in this
prototype, but four cases do not prove recovered-node ownership. Whole grammar
recovery can omit malformed units, consume later tokens or expose partial nodes.
Unknown header context must not bind assignments to the previous valid scope.
The existing statement boundary/recovery path is therefore the authoritative fallback
for **any** whole-grammar error initially. It also retains grammar witnesses independently
of later namespace witnesses. A source being invalid is not a license to drop additional
independent errors or silently claim a complete error census.

### Reusing Tomlyn's internal visitor is not a supported route

Calling `SyntaxValidator` by private reflection is rejected. Wrapping/reparenting
existing nodes into synthetic documents changes parser ownership and cannot be
advertised as immutable reuse. A deliberate upstream public local-validation API
could be a future alternative, but current installed 2.10.1 has none. Statically
vendoring a narrow validator would introduce a maintained licensed code copy; a small
normative inline-scope walker is preferable if the structural/diagnostic obligations
can be fulfilled without copying the faulty global algorithm.

## Decisive future validation, not a new benchmark program

Before production adoption, one worker should implement the clean-grammar walker and
an independent validator should compare it with the existing validated-statement
path on:

- All 703 decoded exact corpus sources plus both historical minimals, with validity,
  exact UTF-16 anchors and retained public IDs; nine decoder boundaries separately.
- Existing multi-error, rollback, conflicting/malformed-header quarantine, illegal
  trivia, EOF, unsupported/recovered node, integer-losslessness and formatting controls.
- Nested inline/dotted/sealed conflicts, escaped-equivalent quoted keys, distinct
  array-element inline scopes, mixed arrays and deeply nested containers; no sharing
  of namespace between independent values.
- Grammar-error branch identity: retain **all** whole-parser diagnostics and current
  recoverable statement witnesses; no count loss or weakened assertions.
- Read-only invariants: serialized original lossless tree, parent references, token
  roles and recursive projected semantics unchanged across validation; cancellation
  cannot publish partial state.

Only after correctness passes, reuse the already retained tiny/mixed/dense public
Analyze fixtures for one new matched comparison against the final hashed baseline.
Measure whole-call elapsed/allocation/retained memory and distinguish clean-source
savings from malformed fallback cost. The current 8 KiB doubling and 69–78% allocation
increase are debt to reduce, not justification for a benchmark rerun before a candidate
exists. This investigation makes no claim of a measured speedup.

## Production practice and research transfer

Roslyn's [production syntax model](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/syntax-analysis)
separates full-fidelity syntax from later analysis; its tree is immutable, unlike
Tomlyn's mutable public API. The transferable decision is one read-only lossless
representation shared by formatting/rendering/semantics, not importing Roslyn or
assuming its parent and concurrency guarantees hold here. Its
[incremental parser design](https://github.com/dotnet/roslyn/blob/main/docs/compilers/Design/Incremental%20Parser.md)
also treats reuse as conditional on parsing context, reinforcing our certified
clean-tree boundary rather than blind reuse of recovered nodes.

Yedidia and Chong's peer-reviewed [Fast Incremental PEG Parsing, SLE 2021](https://people.seas.harvard.edu/~chong/pubs/gpeg_sle21.pdf)
shows that interval shifting and repetition-aware memo structures can improve typical
localized reparse scaling. It concerns PEG parsing, not TOML namespace semantics and
not a drop-in guarantee for this dependency. Its useful connection is architectural:
first stop duplicating syntax construction, then optimize position mapping/dependency
replay separately. A general PEG engine would be disproportionate for this measured
public-path issue. The current small-node semantic walker is a narrower, testable
intervention with no extra delivery binary.

### Follow-up: structural local-anchor mapping

A second discriminating probe retained the original result and program as
`results.jsonl` / `Program-initial.cs`, then tested a structural anchor rule, not
message filtering. It tracks successful decoded inline assignments per fresh scope:

- If the failing path has an existing terminal binding (including a previously
  implicit parent with descendant assignments), anchor the whole offending pair.
- If traversal crosses an already sealed ancestor, anchor the dotted key.

The prototype asks whether any previously accepted decoded path has the new path
as a prefix to distinguish those cases; no diagnostic string or fixture name is used.
`results-structural-spans.jsonl` retains this second 709-row run. It again has zero
validity mismatches. **All 68 retained grammar-clean nonordinary-valid cases**
(63 semantic-only invalid corpus files, two valid historical minimals, three directed
semantic controls) now match current public diagnostic **span sequences and ID
sequences exactly**, including the independent local-inline/global duplicate control.
The malformed-header control is outside this clean-grammar equivalence claim.

This answers the specific anchor-feasibility question, not complete diagnostic
compatibility: messages were not compared, and arbitrary multi-conflict recovery can
expose different mutation behavior. The probe's accepted-path list is deliberately
small-case exploratory code, potentially quadratic; **do not ship it**. A product
inline trie should return a typed conflict witness (terminal redefinition versus
sealed-ancestor traversal, current/prior node anchor) while constructing the
existing Diagnostic. That preserves structural meaning without message parsing or
extra per-namespace scans. Test rollback of failed inline operations and independent
errors within the same inline table before adoption. The original three anchor
failures remain historical negative evidence, not deleted observations.

## Recommended first production construction: boolean valid-tree certificate

Root integration refined the first implementation boundary to avoid introducing a
new diagnostic-producing validator. Prefer a read-only
`TomlTreeCertification.TryCertify(DocumentSyntax, CancellationToken)` returning only
`bool`. It must return true only after certifying **the entire** grammar-clean tree,
including structural/value support, every nested inline-table scope, independent
array-element values and all source-ordered normative global namespace transitions.

```
whole lossless parse(validate: false)
  TryCertify(tree, cancellation) == true -> existing projection, empty diagnostics
  TryCertify(tree, cancellation) == false -> current uniform statement validation
                                           and diagnostic recovery, unchanged
```

Any grammar error, missing/unsupported structure, local-inline conflict or global
ownership conflict returns false. The existing complete validation/recovery path then
supplies **all existing diagnostic IDs, spans, messages and counts**, without trying
to synthesize a partially equivalent diagnostic set. Cancellation must propagate,
not become a false result that triggers additional work. No partial certificate or
mutable namespace state escapes the call.

The global index and each fresh inline index can use the existing unbounded
`TomlOwnershipIndex(int.MaxValue, recover: false)` because certification stops on the
first conflict and discards private state. Local values remain recursively checked
before their outer assignment is admitted; independent arrays never share inline
namespace state. Read-only structural checks and cancellation still apply. There is
no need for a new typed conflict-witness API, message matching or diagnostic-anchor
mapping in this first production construction.

This is a uniform semantic-confidence boundary, **not** a per-fixture workaround or
an invalid-source correctness shortcut. Valid sources avoid the second grammar
construction. Invalid sources retain today's double-parse cost and may incur an
additional failed certification traversal before recovery; measure that adverse path
explicitly when comparing a candidate. Format must preserve its existing candidate
semantic-equivalence/idempotence checks, using certification where applicable rather
than treating a syntax tree alone as sufficient.

The earlier 709-row classification and 68-case anchor experiment remain evidence that
public-node semantic checking is feasible; they are **not** executed evidence for
this planned bool-only production API. Its worker and independent validator must
check the existing corpus, nested scopes, structures, cancellation, formatting and
fallback identity. This simpler option is recommended because it recovers the
measured dominant valid-input cost while preserving established error behavior by
construction, rather than making diagnostic compatibility a prerequisite for the
first optimization.
