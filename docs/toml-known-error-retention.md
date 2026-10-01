# TOML known-error retention across policy and Native presentation

Status: implementation and focused local validation complete, 2026-10-01.
Independent review found no unresolved substantive defect. Target-OS CI and native
GUI acceptance are pending. This note implements the frozen proposal in
[semantic-policy-frontier.md](semantic-policy-frontier.md), not general TOML recovery.

## Contract

Above the existing 4 MiB whole-parser threshold, Full previously found a precise
ownership conflict and discarded it while returning Provisional. The Native idle
publisher also discarded all non-Complete Full diagnostics independently.

The policy now separates accepted statements, uncertainty, and one proved ownership
counterexample using private outcomes. It checks `IsExhaustive` and `IsCertifiable`
before and after `AddAssignment`/`AddHeader`. Unsupported traversal can return a
conflict in the same transition; its post-state refuses the witness. On the first
proved error, scanning stops. The policy returns the unchanged bounded lexical
viewport plus one absolute UTF-16 `TOML_OWNERSHIP` diagnostic, Provisional coverage,
and `TotalDiagnosticCount = null`. Even an offscreen error remains a known fact,
not an invented exact count of one. No syntax-error retention or suffix recovery
is added. No retained AST or source copy survives the analysis call.

Native retains one current-version proved witness independently of the disposable
visible frame. Its identity consists of document/driver/policy references and the
existing generation/version stamp; it holds only the bounded diagnostic, not an
AST, TextSnapshot, rope or source copy. A same-version page change clears the visible
frame but not this fact, so returning to the conflicting page reprojects it without
rerunning Full. An initially offscreen witness is retained without an offscreen
overlay or invented total. Edit, Undo/Redo, Open/New, disposal, and policy/driver
replacement revoke it. This is a root-approved refinement of the initial frame-only
proposal: the once-per-version idle scheduler otherwise lost the error on ordinary
page travel. Scheduling and public contracts remain unchanged.

The existing statement length (256 Ki UTF-16 units), physical lines (64), statement
count (120,000), binding count (200,000), cancellation, statement parser, ownership
rules, and whole-file threshold remain unchanged. Every new snapshot rebuilds from
authority; omitted or malformed edit chains never permit witness reuse.

## Local implementation evidence

A cheap pre-change run of the repository-local research fixture reproduced the
visible duplicate at absolute offset 4,198,404 as Provisional / 0 diagnostics /
unknown total; escaped-equivalent key had the same failure. After the change, both
return one exact ownership error with unchanged completeness and null total. The
original baseline is recorded in the frontier investigation; rerunning its executable
updates the research report, so it must not be mistaken for untouched baseline data.

`dotnet build src/Mote.Formats/Mote.Formats.csproj -c Release --no-restore -warnaserror`
passed. `TomlKnownErrorPolicyTests` passed 9/9 in Release warnaserror, with TRX under
`.cache/toml-known-error/policy.trx`: seven exact conflict forms including decoded
escaped keys and multiline prefix, lexical token/coverage invariance, first-witness
stop before malformed suffix, offscreen fact retention, repair/Undo/Redo with missing
history, and unchanged authoritative source/modified state. A first test incorrectly
assumed a new nonempty unsaved Document starts unmodified; it was corrected to compare
before/after state instead. No production defect was hidden by that correction.

The existing `IncrementalPolicyTests.Toml` regression subset also passed 19/19
against the fresh Release build (`.cache/toml-known-error/existing-toml.trx`).

[Independent differential validation](validation/toml-known-error-differential.md)
passed 24/24 public-session tests in Release warnaserror. Python 3.14.6 `tomllib`
classified all 21 exact >4 MiB files as expected (20 invalid, one valid), including
admitted and one-beyond resource boundaries. An isolated pre-change source harness
reproduced Provisional / 0 diagnostics for the same duplicate. The tests do not copy
or reflect over ownership logic; this is finite evidence, not general grammar proof.
[Native publisher integration](native-toml-known-error.md) passed 22/22 focused
Release warnaserror cases (20 new, two existing publication regressions), followed
by a fresh final-guard rebuild and the reentrant case passing 1/1. The actual
>4 MiB public-session/controller pipeline covers repair/Undo/Redo, initially
offscreen witness discovery, page-away/back without repeating Full, Open to JSON
policy replacement, and cancellation. Synthetic publication tests challenge all
identity/viewport mismatches and preserve render payload references. The shell is
queued and fake, not RichEdit/AppKit; hosted target-OS CI remains pending.

[Independent production review](reviews/toml-known-error-production-review.md)
found no unresolved substantive defect in the frozen Formats + Native changes.
The final Native post-install guard was rebuilt and exercised by the reentrant
case; the earlier 22-case run is not represented as a full rerun of that final guard.

This slice does not reduce Full restream cost or certify unsupported TOML grammar, establish physical UI latency, or claim target-OS
acceptance before CI.
