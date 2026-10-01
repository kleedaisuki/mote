# YAML alias-key recovery certificate

## Contract and failure

`YamlIncrementalSession` streams files above 256 KiB and normally certifies a Full
analysis as `Complete` after consuming all events. YAML 1.2.2 requires an alias to
refer to an earlier anchor and defines mapping-key uniqueness through recursive
node equality, not spelling ([YAML 1.2.2 §§3.2.1.3, 3.3.1, 7.1](https://yaml.org/spec/1.2.2/)).
An unbound alias in a key is therefore both an error and an unavailable canonical
key. The previous stream checker reported `yaml.undefined-alias` but left its
completeness certificate intact; `? *missing\n: first\n? *missing\n: second\n`
could report `Complete` without deciding whether the two keys collide. The same
problem occurred when an alias was nested inside a sequence or mapping key.

## Mechanism

`ParseNode` already receives `needsCanonical`: true for mapping keys and nodes
anchored for possible later alias use. When it finds an unbound alias in that
context, it retains the existing `yaml.undefined-alias` error and also emits
`yaml.key-equality-unsupported`, downgrading the whole result to `Provisional`
with unknown `TotalDiagnosticCount`. It does **not** invent a duplicate-key
error. An unbound alias in an ordinary value still receives the existing error;
there is no new assertion that it affects key uniqueness. This is a recovery
certificate, not a YAML grammar extension or a promise to continue after parser
syntax exceptions.

## Evidence and limits

Release `YamlStreamDifferentialTests` passed 14/14: valid canonical scalar,
sequence, mapping and anchored-alias keys still match the whole-source oracle;
unbound direct, sequence-nested and mapping-nested keys downgrade. An anchored
ordinary value containing an unbound child alias also downgrades before a later
alias to that anchor is used as a key. One offscreen alias key after >2 MiB of
comments produces both diagnostics at the exact absolute `*missing` span.
The valid input path stayed `Complete`. A single Windows x64 Release run using
`.temp/yaml-session-bench` observed the existing 16 MiB and 100 MiB valid
structured files as `Complete`: cold 701 ms / 2,818 ms, cumulative allocation
107.7 / 674.5 MiB, 100 MiB peak working set 259.4 MiB. The 100 MiB full-edit
re-stream took 3,337 ms and allocated 674.5 MiB; Visible took <1 ms and remained
`Provisional`; cancellation requested after 25 ms was observed at 27 ms. These
are single samples, **not** a before/after speedup or p95 latency claim.

The existing structural-density, scalar-size, anchor-summary and diagnostic
budgets still force bounded provisional results; edits still re-stream rather
than reuse parsed subtrees. Undefined aliases inside ordinary values are errors
but are not independently treated as an incomplete uniqueness proof unless a
canonical form is requested. Further recovery work should compare supported
malformed key graphs against the small-document oracle, retaining provisional
status whenever a key comparison is skipped.

## Ordinary-file semantic-error key certificate (2026-10-01)

This follow-up extends the earlier streaming certificate to the public small-file
route; it is not a giant-file optimization. YAML 1.2.2 key uniqueness remains
unverified when mote deliberately skips key canonicalization after a semantic
error in that key. The presence of an error is not itself proof that all key
comparisons were performed. Conversely, an erroneous ordinary value need not
prevent a complete mapping-key/error scan when its graph is never used as a key.

### Reproduced baseline, independent expectation

Frozen baseline source HEAD `afb7b30268f7719a07019439d1572d3f606a7991`,
actually loaded Release formats DLL SHA-256
`82D496E65D5DAC812E626301A04340DF32D061C381C09C3DC93F63E96FAE13F1`,
was copied before source edits into
`.cache/validation/yaml-small-recovery/baseline/`.
A separate console harness recorded twelve discriminating public/session cases.
Direct undefined alias keys, sequence-/mapping-nested undefined aliases,
invalid explicit integer keys, and invalid collection-kind keys all incorrectly
reported small-session `Complete` with one error and no unavailable-equality
warning. Ordinary undefined alias, invalid scalar, and invalid collection values
reported `Complete` with one error; those are intentionally preserved negative
controls, not evidence of an incomplete key scan. An anchored value with an
undefined child later referenced as a key **already** reported `Provisional`;
that path was not falsely claimed as a new fix. Malformed flow-key syntax
reported `Provisional` after parser termination.

`YamlSmallRecoveryCertificateTests` derives expectations independently of the
implementation's existing blanket skip. The frozen eighteen-case pre-correction expectation checkpoint
ran against the frozen DLL: **7 passed / 11 failed / 0 skipped**. Failures include
missing uncertainty warnings, offscreen Visible certificate, and the initial
erroneous snapshot before a genuine repair. Raw TRX:
`.cache/validation/yaml-small-recovery/baseline-recovery-final-fixtures.trx`.
An earlier sixteen-case probe (7 passed / 9 failed) is also retained. The initial
isolated harness inadvertently compiled the console Program alongside tests,
producing an entry-point warning; excluding that unrelated harness file removed
the warning without changing assertions. This was a harness setup issue, not a
product failure.

The suite covers original undefined-alias message/severity/span, original error
order when later independent duplicate/undefined-alias failures occur, no guessed
same-bad-key duplicate, ordinary-value Complete, existing anchored uncertainty,
syntax termination without pretending later diagnostics were observed, one
257 KiB comment-padded alias-key case, offscreen uncertainty despite empty
viewport diagnostics, and a real repaired alias snapshot returning to Complete.
Only one threshold-crossing fixture is used; there is no 100 MiB benchmark or
new parser dependency. Candidate evidence is recorded below after actual execution.

### Candidate, compatibility, and acceptance boundaries

Production source commit `030e569`, source-file SHA-256
`E913D834EF59B55367D9AE4339CB82AFA2FD27356FC5B230EE5477D8799E5A67`.
The candidate DLL actually loaded by the independent console/test harness is
`C48F882B589674CE12003F0E6A556514E612D59EB00A2FE39128F65E10E1AA9A`;
this is recorded separately from the worker's differently built DLL, not inferred
from its build report. Baseline and candidate twelve-case serialized rows match
**12/12** after removing **only** newly added Warning records with exact code
`yaml.key-equality-unsupported` and exact message
`Cannot verify this mapping key's uniqueness: key contains a semantic error.`,
and excluding the intended session certificate/count changes from compatibility
comparison. Six rows gain this warning. All prior errors **and prior warnings**,
severity/code/message/span/order, SourceText, tokens, flat preorder semantic tree,
and Format output remain exact. In particular the previously provisional
anchored case is unchanged. Raw outputs `baseline-full.json`,
`candidate-full.json` and comparison script are in the artifact directory.

The first normal affected `Mote.Tests` build was unavailable because concurrent
Native implementation referenced an unfinished `NativeEncodingRuntimeProbe`
(CS0103, Program.cs line 15). This is an integration/environment boundary, not a
YAML failure; no production code was changed to hide it. A project-local isolated
portable test project references the frozen candidate Formats/Engine DLLs plus
existing SharpYaml/Markdig/Tomlyn and existing xUnit/Test SDK versions. It links
unchanged `FormatPolicyTests`, `YamlStreamDifferentialTests`, and
`YamlResourceIsolationTests`, plus the dedicated recovery test. No Native project
or native runtime is needed for these checks.

Initial candidate regression: **96 passed / 1 failed / 0 skipped**, 97 cases.
The one failure was an over-broad test expectation: after the streamed checker
loses certainty it suppresses later key comparisons, so its Provisional output
need not contain the later duplicate already proven by the small checker. This
is existing streaming behavior, not a regression introduced by this source
change. The informative threshold fixture with the later duplicate is retained:
the final test asserts the small duplicate is present, the large duplicate is
absent, both disclose undefined-alias/unsupported-key uncertainty and both have
unknown diagnostic totals. This characterization is **not** streaming recovery
acceptance. An intermediate alias-only narrowed control passed 1/1 and is retained
as historical evidence, not the final test. The final corrected scope-discriminator
then passed **1/1**, using the same actual candidate DLL. The completed 96 cases
were not rerun; these results are not mislabeled as one all-green 97-case run.

Thus all **18 new recovery cases** are covered by the 17 initial passes and one
final corrected pass. Existing 64 FormatPolicy, 14 YAML stream, and one resource
case passed in the initial regression run. Original failure artifacts remain:
`candidate-recovery-regression-isolated.trx`; final targeted artifact:
`candidate-recovery-threshold-scope-qualified.trx`.

Commands for the isolated, reproducible checks (harness files under root .cache):

```powershell
dotnet test .cache/validation/yaml-small-recovery/harness/CandidateTests.csproj `
  -c Release -v q --logger 'trx;LogFileName=candidate-recovery-regression-isolated.trx' `
  --results-directory .cache/validation/yaml-small-recovery

dotnet test .cache/validation/yaml-small-recovery/harness/CandidateTests.csproj `
  -c Release -v q `
  --filter FullyQualifiedName~Small_and_streamed_alias_key_both_disclose_uncertainty `
  --logger 'trx;LogFileName=candidate-recovery-threshold-scope-qualified.trx' `
  --results-directory .cache/validation/yaml-small-recovery
```

Environment: Windows x64 10.0.26200, .NET SDK 10.0.400, portable Release.
**Supported verdict:** ordinary semantic-error key uncertainty is now explicit,
prior diagnostic/public projection behavior is preserved, later independent
small-file errors are retained, ordinary erroneous values remain Complete when
key equality is decided, offscreen filtering does not certify unavailable work,
and a repaired authoritative snapshot recovers Complete. Syntax termination,
large streaming recovery, full-format completion, original Unicode flow-key span
coverage, native GUI/AOT behavior and mid-analysis cancellation are not claimed
by this validation slice.
