# Selected Native CI diagnostic evidence summary

Date: 2026-10-01. Owner: causal observability delivery / CI evidence summary.

## Contract

`tests/summarize_ci_evidence.py` reads a fixed per-RID manifest without GitHub
network calls or third-party runtime dependencies. It writes schema
`mote-ci-evidence-summary-v1` JSON and appends an Actions Step Summary. The scope
is selected non-gating diagnostics, not complete release acceptance. Existing
strict checks and diagnostic oracles are unchanged.

The manifest covers the native JSON two-size pilot, causal trace recovery,
ordinary Continuous editing, Windows 100 MiB Continuous editing, Windows Canvas
live theme workers, Windows external source ranges, macOS Grid action
contract control, the original macOS external Grid AX report, and the same-client
C0/P0 ShowMenu pair. Platform-inapplicable entries are not expected. Additional
inventory report files are counted but their names and contents are not emitted.
This is not an exhaustive native diagnostic inventory: historical Windows canvas
UIA, separate Windows Grid, macOS external-workflow and other diagnostic reports
remain outside this fixed manifest. Their absence from this table is not a pass.

A report may be `missing`, `malformed`, `oversized`, `unrecognized`, or `present`.
This is separate from its normalized claimed result. Nested JSON pilot samples,
Canvas theme worker exits, and recovery cases remain visible even when the
parent reports pass. Missing numeric exit evidence stays unknown: zero is never
inferred from a pass claim or a green GitHub job. `control-completed` becomes
`experiment-completed`, never AX/product pass. Forced termination of JSON pilot
samples marks evidence censored without asserting that any event was absent.
Expected killed recovery cases can legitimately pass their synthetic test while
retaining censored request evidence and `absence_certified=false`.
Missing/empty request lists and pending or unfamiliar request classifications
remain `unverified`. Recovery `observed` requires explicit normal-session
termination and a nonempty list of typed terminal request classifications. It
does not independently rerun the causal reader or certify a successful Save.
For JSON samples, no forced-cleanup flag alone proves nothing: `observed`
requires a recognized pass/failure outcome and explicit normal exit; a pass
also requires explicit normal reopen exit. Incomplete or unknown outcomes remain
unverified even if the cleanup flag is false.

The original external Grid report exposes its recorded Swift numeric exit and
aggregate typed check counts without copying check names or details. The pair
exposes only fixed C0/P0 sessions, recorded numeric owner/client exits and the
original numeric AX reply. `completed-success-observed` and
`completed-failure-observed` normalize to corresponding **experiment** outcomes,
not a product pass. A zero owner/client exit can coexist with a failing AX reply.
No numeric editor exit is invented when a report contains only a normal-exit
Boolean. This summary reads retained report fields, not GitHub step conclusions.

Trace health is separate: an explicitly valid causal-integrity report with zero
observed drops is `no-observed-drops`, not proof of zero producer loss. Missing
or unrecognized trace evidence cannot be converted into certified absence.
Native JSON samples additionally retain the native Save reader's fixed
`native-save-causal-v1` claim (`complete`, `incomplete`, `censored`, `unobserved`,
or `invalid`), request-list count, and typed absence/normal-exit/terminated
Booleans. These are separate from the document's result and ordinary trace
health. Unknown contracts remain unverified. Request payloads, trace SHA values,
and discarded-file paths are not copied. The summary reports the reader's
claim; it does not replace its route/version completeness checks.
The Actions Step Summary also renders the fixed Save-chain status, recorded
request count, and normal-exit Boolean. Missing fields remain `unknown`, and
non-JSON diagnostics without this evidence display `not-recorded`.

## Privacy boundary

Outputs contain only fixed diagnostic IDs, fixed normalized statuses, RID,
typed numeric exits/counts, case size/mode allowlists, and typed Booleans.
Raw statuses outside the enum, source contents, filenames, paths, PIDs,
exception messages, screenshots, free-form stages, and raw traces are never
copied. Reports larger than 8 MiB are rejected before reading. Nested summaries
are bounded to 16 entries. The summary does not copy arbitrary metadata fields.

The recovery artifact remains the original synthetic evidence, not a privacy
filtered export of user documents. It is uploaded separately from the summary.

## CI integration

At the end of each of the four Native AOT RID jobs, an independently published
Native AOT recovery executable runs the existing four-case driver: normal,
nested-held termination, receipt-only Save, and receipt-only Save As. Publish
payload is under `.cache/causal-recovery-probe/<RID>`; evidence is under
`.cache/causal-recovery-<RID>`. The experiment is non-gating until hosted evidence
is independently audited. The final always-run summary and its artifact expose
actual nested evidence without promoting diagnostics into release gates.
If the whole job is forcibly cancelled, even `always()` steps may not finish;
a missing summary artifact is not a pass.

## Reproduction and validation

```powershell
python -B -m unittest discover -s tests -p test_summarize_ci_evidence.py -v
python -B tests/summarize_ci_evidence.py --rid win-x64 --output .cache/ci-inventory/win-x64/evidence-summary.json
```

Local validation: 25 deterministic fixtures passed. They cover all four RID
manifests; missing/malformed reports; unknown report privacy; fake typed exits;
control completion vs product pass; censored expected kills; forced cleanup;
JSON exact two-case coverage; observed trace drops; hidden worker exits; and
repeat-run self-summary exclusion. Added fixtures verify missing/empty/pending
request evidence, explicit normal outcomes, negative drop-count rejection,
Grid nested failures, C0/P0 reply retention, fake Boolean/string exit rejection,
native Save claim contract/typed-field/privacy boundaries, and visible Markdown
Save-chain evidence with missing-field and privacy checks.
A cached real hosted osx-arm64 JSON pilot
report was summarized: both 1/100 MiB claims and no-observed-drops retained,
other unavailable reports explicitly missing, no inferred numeric exits.
A cached real hosted x64 original Grid report from CI 36818175897 summarized
as failed with Swift exit 1 and 40/41 checks passing. Its same-client pair
summarized as experiment-completed-failure-observed: C0 and P0 each recorded
owner/client exit 0, while their original AX replies remained 0 and -25205.
The local reproduction output is `.cache/summary-tests/cached-grid-summary.json`.
PyYAML locally parsed the modified workflow (five jobs) and `git diff --check`
passed. No hosted execution of the new workflow steps is claimed yet.
