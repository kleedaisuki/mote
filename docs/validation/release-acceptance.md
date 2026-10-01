# Release acceptance: ordinary source tasks and independent artifacts

## Contract and current evidence

The release direction permits application packages with multiple files and keeps
Native AOT. This does not permit projects/workspaces, multiple simultaneously
editable documents, or weakening exact-text/history/Save contracts. Packaging
and native-source qualification are separate checks.

`tests/NativeReleaseProductWorkflow.ps1 -ExecutablePath <extracted executable>
-OutputDirectory <repository .cache/.temp child> [-RuntimeIdentifier <rid>]` is
the cross-platform release suite. Packaging should run it against the extracted
release archive on each of Windows x64/ARM64 and macOS x64/ARM64. This document
does **not** certify those hosted executions before artifacts are reviewed.

The suite is independent of the historical Canvas ancestry/input-island oracle.
Windows delegates real Win32 RichEdit edits/menu commands/fresh GUI reopen to
`Invoke-NativeWindowsReleaseProduct.ps1`. macOS uses the actual NativeSource
product shell/controller through additive in-process AppKit workflow and fresh
GUI-process reopen probes. Neither is a physical keyboard or real Pinyin test.

## Expected task outcomes

All samples are self-created, small representative task shapes, **not** authentic
user data or evidence of market frequency. They share exactly one editable
`mote-release-original` value; selection replaces it with
`mote-release-edited中`. The replacement stays valid in each structured format.
The marker is not an appended token that makes JSON invalid just to test Save.

| Format | Representative task shape | Independent invariant |
| --- | --- | --- |
| Markdown | Checklist owner, headings and Chinese note | Source surrounding owner unchanged |
| TOML | Nested profile/display settings | String value replaced; tables/scalars unchanged |
| JSON | Profile object and feature array | String value replaced; braces/array unchanged |
| YAML | Profile mapping and feature sequence | Value replaced; indentation/comments unchanged |
| CSV | CRLF records with comma/escaped-quote fields | Name changed; quoting and CRLF exact |
| Plain text | Short Unicode reading note | Ranged replacement; accents/emoji/newlines exact |

For each task the wrapper creates an original and computes the expected bytes
by an independent string replacement, before executing the product. It requires
native selection/edit, engine Undo/Redo, Save, and **a new GUI process** showing
the saved file. The original and saved outputs are checked byte-for-byte using
BOMless strict UTF-8; checking marker presence alone is insufficient. Future
encoding additions require separately specified bytes rather than assuming this
six-task set proves every codec.

The Windows task driver now exercises a real dirty-close dialog Cancel; the Mac
workflow calls the actual AppKit window close route, cancels the prompt, and
checks committed marked text/dirty state retained. This is a cancellation
witness, not acceptance of every Save/Discard/Cancel branch. Both drivers retain
`native-product.png` from their owned window/content view. The wrapper requires
a valid PNG signature/IHDR and dimensions at least 100 by 100, recording its
hash and dimensions. Image existence/format alone is not a pixel-quality oracle;
visual review is separately necessary.

The suite also requires deterministic CLI help/runtime readiness and invalid
multi-path/conflicting-profile/unknown-option exit 2 with stderr. Missing-file
GUI dialogs are not treated as CLI exit errors and are not silently included.

Configuration checks use child-specific `MOTE_HOME` and no global settings:
default tracing-off smoke creates no mutable home; a light-theme config enables
tracing to a relative custom directory; an environment opt-in overrides disabled
config. Config bytes must remain unchanged. This verifies loading/placement and
clean shutdown, **not** rendered theme pixels or nonexistent cache/data writers.

## Runtime evidence and limits

Every actual task opts into local JSONL and retains stdout/stderr, task originals,
outputs and result JSON. `release_trace_oracle.py` reuses the existing closed
schema and causal graph validator; it rejects unknown names/attributes, missing
parents, cycles, duplicate spans, drops, absent successful session shutdown,
unfinished records, and input/path/home sentinels. At least one individual
process trace must contain successful open/edit plus a complete native Save
request chain whose engine phases/completion agree with the captured version;
the existing causal Save reader supplies this oracle. Phase counts without
request ancestry or with mismatched versions do not pass. A clean
reopen-only process may have its own trace and is checked independently.

Successful telemetry means instrumented phases and callback return, not event
transport completeness, physical input, visible pixels, percentile UX or a
universal latency budget. The 90-second process bound is a hang watchdog only.
The existing 100 MiB/dense controls remain capacity/mechanism evidence, not the
ordinary-task demand or an automatic release experience gate.

Important remaining qualification limits: actual Pinyin composition/cancellation
via OS input methods, NVDA/VoiceOver reading behavior, remaining dirty-close
Save/Discard branches, physical rendering, broad codec cases and measured ordinary-task
responsiveness. Product documentation must identify unexercised paths rather
than converting a green aggregate into those claims. An actual data-loss or
incorrect Save failure is a blocker, not a benchmark caveat.

## Locally executed harness checks

On Windows, without starting a GUI or modifying OS/global settings:

```powershell
pwsh -NoProfile -File tests/NativeReleaseProductWorkflow.ps1 -SelfTest
python -B -m unittest discover -s tests -p test_release_trace_oracle.py -v
```

The fixture self-test exercises six independent expected-byte controls and a
negative comparison against every unedited original, plus an outside-artifact
path rejection. The Python tests exercise accepted separate edited/reopen
process traces and negative missing Save, unknown operation, content leak,
missing parent/terminal, unterminated row, cycle and duplicate identity cases.
These checks validate the harness, **not release binaries**. Hosted records and
their exact commit/archive identities are to be appended only after execution.

Executed on 2026-10-02: the PowerShell self-test passed all six positive/negative
byte controls and the outside-path rejection; its retained report is
`.temp/release-acceptance-selftest/release-acceptance.json`. The Python oracle
initial suite passed **6/6**, zero failures/skips, in 0.027 seconds; after adding
native Save request/version linkage controls it passed **7/7** in 0.033 seconds.
These are distinct harness revisions, not a fabricated combined count. A later
artifact-safety self-test at `.temp/release-acceptance-safe-selftest/` passed the
same fixture byte controls and rejected an actual Windows junction ancestor;
fresh output directories are required and existing evidence is not overwritten.
Python standard
library JSON/TOML/CSV readers additionally decoded the generated edited fixtures:
the JSON/TOML profile value equals the edited Unicode marker, and CSV yields
three exact records including the comma-containing Chinese cell and escaped
`He said "ready"` value. YAML and Markdown were not independently parsed by this
local harness check. No product GUI was launched.

## Prior knowledge reused

- [Independent standards audit](../research/editor-experience-standard-audit.md)
- [Task audit](../research/editor-standard-task-audit.md)
- [Oracle audit](../research/editor-standard-oracle-audit.md)
- [Product native-source binding](native-source-product-binding.md)
- [Product controller contracts](native-source-product-controller.md)
- [Native-source integration review](../reviews/native-source-product-integration-review.md)

## First hosted qualification and harness regression

Run `36919898449`, source `6778c4a`, built all four AOT targets and passed both
OS main test jobs. The macOS release product suite failed **before any task**:
PowerShell parameter/local `$Home` collided case-insensitively with read-only
automatic `$HOME`. This is an acceptance harness defect, not evidence of an
AppKit product failure; no six-task macOS verdict follows from that run.

The initial `-SelfTest` only exercised fixtures/oracles and never called the
real process helper, so it did not detect this defect. The correction renames
both helper parameters and all config/task locals to `$moteHome`, retaining the
existing assertions and process/byte/trace contracts.

An isolated copy of the **original actual helper**, with the production strict
mode/Stop setting, reproduced `Cannot overwrite variable Home` and exit 1 before
starting the harmless console child. Retained reproduction:
`.temp/release-home-regression-before/reproduce.ps1` and `reproduction.log`.

The updated self-test now runs the actual `Invoke-Child` helper against a
repository-local console script. It verifies isolated `MOTE_HOME`/`MOTE_TRACE`,
reading the intended config by hash, normal exit/stdout, expected exit 2/stderr,
unchanged config bytes, and the actual `Assert-Traces` helper through a valid
synthetic session record. An AST-based negative control rejects case-insensitive
automatic-variable parameters/assignments, including `$hOmE` and `$pId`. Existing
six byte controls and the real junction guard still run unchanged.

Executed correction command:

```powershell
pwsh -NoProfile -File tests/NativeReleaseProductWorkflow.ps1 -SelfTest `
  -OutputDirectory .temp/release-home-regression-fixed
```

Result: `self-test-passed`, console success exit 0/error exit 2, config/environment
and trace checks passed, six byte negative controls passed, junction guard passed.
Output is retained in `.temp/release-home-regression-before/fixed-selftest.log`
and `.temp/release-home-regression-fixed/release-acceptance.json`. These synthetic
console records are harness evidence, not product telemetry or GUI validation.
No local GUI/global setting changes, commit, push or CI dispatch were performed.

The final negative controls separately reject mixed-case HOME parameters, PID
assignments and PROFILE assignments; the final suite self-test passed at
`.temp/release-home-regression-final/`, with stdout retained in
`.temp/release-home-regression-before/final-selftest.log`. The individual Windows
task script was also searched for these parameter/assignment collisions; none
were found. All writes in the suite were inspected through the AST rule rather
than treating read-only automatic-variable reads (for example `$PSHOME`) as bugs.

## macOS actual product artifact audit: run 36921665382

Source: **`9fecef2d5ede8b0c593e709bdd6b39d7ad3e6986`**. Both macOS task suites
completed with `status: passed`, then the workflow step incorrectly threw on
`$LASTEXITCODE` after the successful PowerShell suite. The failed job conclusion
must not be replaced with an overall CI success claim; equally, the subsequent
wrapper failure is not evidence that these completed product tasks failed.
Windows tasks are not certified by this macOS-only audit.

Independent offline command (no product/test rerun):

```powershell
python -B .cache/release-ci-36921665382/mac-product-audit/audit.py
```

The audit reads the immutable downloaded original/output/trace/stdout/report
files directly, retaining the hosted paths inside those files unchanged. The
reader receives their **local artifact paths**; no rewritten original trace or
report is needed. Frozen suite and Mac probe source copies are retained alongside
the audit. The script derives expected original and edited bytes from separately
written full literal task samples, not merely trusting child result booleans.

| Native release target | Hosted OS | Exact original/output task pairs | Fresh GUI process reopens | Task trace records |
| --- | --- | ---: | ---: | ---: |
| osx-x64 | macOS 15.7.9, build 24G830 | 6/6 | 6/6 | 982 |
| osx-arm64 | macOS 26.6.2, build 25G83 | 6/6 | 6/6 | 991 |

Executable identities reported by the successful suites:

- x64: `9C1F9095FA9AF5AD3F171A79E793DD87618F62171753C0F119390265A744EA86`.
- ARM64: `8E6A377DCBF713689D90A63B19AE11910E62512C22BB98FD4DB74537E2452EB8`.

All 12 original inputs equal the independent full-byte expectations; all 12
saved outputs equal only the specified ranged Unicode value replacement. Input
and output lengths/hashes match the retained suite reports, including CSV CRLF
and quoted/escaped-quote fields. All workflow and separate fresh-process GUI
reopen stdout files contain their exact completion marker, with empty stderr.
Normal exit 0 is enforced by the frozen `Invoke-Child` implementation before
each marker is accepted and the suite reports pass; there is no independent
per-child exit-code sidecar, so this exit evidence is tied to that frozen helper.

Each of the **12 task traces / 1,973 records** independently passes the closed
schema, complete-record, graph, normal-terminal and no-observed-drop checks.
Private task markers, original/output filenames, hosted task paths and Chinese
content sentinels are absent. Each contains exactly one successful, complete
native Save request chain with **captured/saved version 4**, consistent engine
phase/completion versions, and correctly linked receipt/terminal ancestry.
Archived trace-oracle record counts and SHA-256 values match the raw downloaded
trace bytes, rather than only matching another generated summary.

Every task actually emitted successful source install/readback/reconcile/range
publication/style publication operations, plus analysis parse/publication. In
each task, the retained successful counts include 4 installs, 3 reconciliations
and 3 range publications; other analysis/readback/style counts differ with
asynchronous scheduling. This certifies those exercised callbacks and engine
phases, **not complete six-format semantic conformance or physical presentation**.

In particular, not every task publishes analysis at the saved version 4 before
the probe rapidly runs dirty-close/history/New/Open stages. For example x64 YAML
and plain text have successful analysis publication at versions 0 and 3, while
the saved version is 4. The traces include asynchronous discarded analysis;
mere parse/publication presence therefore must not be described as proof that
every final saved-version semantic view was rendered correctly. The exact saved
text is independently proven; final semantic/pixel state has a narrower oracle.

Configuration and CLI side evidence also matches the specified contract on both
targets: all five CLI cases have expected exits; all three GUI config cases have
the exact marker and empty stderr; default-off creates no home; both opt-in
config traces pass the strict graph/privacy oracle. These are runtime placement
and load/shutdown checks, not rendered-theme verification.

All 12 retained owned-view PNG signatures/dimensions/hashes match the reports:
x64 captures are 1120×760; ARM64 captures are 1024×642 for Markdown and 1024×645
for other tasks. The frozen probe captures at stage 6 **before** committing the
marked Chinese character, so a capture legitimately shows the edited prefix
without the final `中` suffix. Pixel review belongs to the independent visual
review, not this artifact audit.

Artifacts:

- `.cache/release-ci-36921665382/evidence/release-evidence-osx-x64/product/`
- `.cache/release-ci-36921665382/evidence/release-evidence-osx-arm64/product/`
- `.cache/release-ci-36921665382/mac-product-audit/audit.py`
- `.cache/release-ci-36921665382/mac-product-audit/result.json`
- `.cache/release-ci-36921665382/failed.log` (passed suite JSON followed by the
  workflow's `Packaged product workflow failed` throw).

Supported verdict: **both macOS native-source six-task suites pass the stated
text/history/Save/reopen/trace contracts for this frozen candidate**. The complete
release workflow remains failed/unqualified; Windows acceptance and unresolved
physical input/readers/full semantic view/performance boundaries are not hidden.

## Four-target actual candidate audit: run 36925090282

Frozen source: **`f2b2de77912b7bb57baa6c72947d392c9b1f9668`**. The authoritative
run completed successfully with all eight jobs, including all four package
product gates, embedded codec checks, post-run inventory checks and release-asset
collection. Unlike the preceding run, no post-success PowerShell status mistake
invalidated the job conclusion. This audit still checks actual evidence rather
than accepting the aggregate green status as an experience certificate.

```powershell
python -B .cache/release-ci-36925090282/product-audit/audit.py
```

The audit reuses the previous full-literal byte oracle and existing strict trace
readers, extending them to Windows edit/reopen process reports and the actual
collected package payloads. No product/GUI/test rerun or original artifact
rewrite was performed. Package executable bytes are read directly from ZIP/tar
archives and matched to source/RID manifest identity and suite executable hash.

| Target | Exact original/output task pairs | Fresh GUI process reopens | Task processes with traces | Trace rows | Save version |
| --- | ---: | ---: | ---: | ---: | ---: |
| win-x64 | 6/6 | 6/6 | 12 | 734 | 5 |
| win-arm64 | 6/6 | 6/6 | 12 | 711 | 5 |
| osx-x64 | 6/6 | 6/6 | 6 | 951 | 4 |
| osx-arm64 | 6/6 | 6/6 | 6 | 996 | 4 |

All **24 task pairs** preserve original bytes and save exactly the independently
specified ranged Unicode edit, including CSV CRLF/quoted values. All 24 fresh
GUI processes reopen the exact saved source. Windows drivers additionally retain
their passed/complete native-message reports and normal edit/reopen stdout/stderr;
Mac workflow/reopen markers and stderr remain exact. Normal exit is required by
the frozen helpers before reporting pass. These callbacks/messages are still not
physical keyboard, real Pinyin or screen-reader evidence.

The **36 task process traces / 3,392 rows** independently pass schema, complete
records, causal graph, successful terminal, no-observed-drop and sentinel privacy
checks. Privacy is additionally checked against decoded JSON string values, so
escaped Windows paths cannot evade that artifact-side sentinel inspection. Each
task has exactly one complete request-linked Save chain with the captured version
matching all required engine/commit/UI completion versions. Reopen-only process
traces are checked separately and are not incorrectly required to perform Save.

All task sets actually invoke source install/readback/reconcile/range/style
publication and analysis parse/publication. In this run both Windows targets
publish analysis at every task's saved version 5. Neither Mac target publishes
analysis at its saved version 4 before the deliberately rapid subsequent marked
input/history/New/Open stages. Thus the successful Save/text task does **not**
establish the final saved-version semantic presentation, on-screen token colors,
preview completeness or complete format conformance.

CLI/configuration checks match on all four targets: expected exits for all five
CLI cases; three GUI config markers and empty stderr; no home creation when off;
strict independent graph checks for both opt-in traces; and independently
specified unchanged light-theme/custom-path or dark-theme/disabled config bytes.
The frozen suite explicitly launches NativeSource here, so this candidate audit
does **not** certify an eventual bare/default profile change.

Suite/package executable SHA-256 identities:

- win-x64: `7099F31ABDC0198612FDD9FCC36403DC89FCE93906AF0796344F1AB64AECD533`.
- win-arm64: `B05823B66EFD1AC4AA5555AE544E11B7C42BDADA4E666DE66C72EDB4BABD809F`.
- osx-x64: `FBD29914F265142875D12DF08D8278B54F4724DE2671E1BF625939AC56AD4783`.
- osx-arm64: `E10F3E635BF4C88BBCC74F23CB04BD0D05298BF7D87DD42DC061456CA5A53CDD`.

Artifacts and reproducibility:

- `.cache/release-ci-36925090282/evidence/release-evidence-<rid>/product/`
- `.cache/release-ci-36925090282/assets/` (actual archives and manifests)
- `.cache/release-ci-36925090282/product-audit/audit.py`
- `.cache/release-ci-36925090282/product-audit/result-win-x64-win-arm64-osx-x64-osx-arm64.json`
- `.cache/release-ci-36925090282/product-audit/frozen-suite.ps1`
- `.cache/release-ci-36925090282/product-audit/frozen-windows-task.ps1`
- `.cache/release-ci-36925090282/product-audit/frozen-mac-probe.cs`

Supported verdict: all four frozen **NativeSource candidate source-text task
contracts** pass. The separately investigated **visible CSV preview pending**
issue is neither checked nor excused by exact CSV source bytes; it remains an
independent product/rendering issue and must not be masked by this green gate.
Default promotion and final release remain separate decisions/qualification.

### Default-profile qualification witness prepared after this candidate

After authorizing NativeSource as the normal default, the suite's configuration
GUI cases now launch **bare `--smoke-gui`**, without `--native-source`. Both opt-in
normal-entry traces must contain successful `native.source.install` and
`native.source.readback`; these fixed operations are emitted by the actual
NativeSource product surface. Merely showing a window or ending a session cannot
pass this witness. Each config report records `launch_route: bare-default` and
whether the traced source-surface witness was required and obtained. The
untraced default-off case does not fabricate a telemetry witness.

The Windows individual driver is owned by the product leader and now opens the
bare file path for both edit and fresh-process reopen. The Mac diagnostic is
also owned by that leader: it now obtains the normal parsed product route and
uses the production shell factory/controller profile, rather than constructing
a hard-coded NativeSource shell. These are **new qualification changes**, not
retroactive assertions about source `f2b2de7`; the next frozen hosted run must
exercise them.

The trace helper adds an opt-in `--require-native-source` contract. Its regression
test rejects a normal-session-only trace and an install-without-readback trace,
then accepts the complete successful surface witness. After this contract change
the narrow Python oracle suite passed **8/8**, zero failures/skips, in 0.037
seconds; PowerShell parsing passed. No GUI or full product suite was rerun locally.

### Post-Save macOS final-ready contract prepared for the next frozen run

The Mac product writer now waits after marked input is committed and Save
completes, then obtains `native-product-semantics.json` from the actual installed
source/analysis/style/Grid identity guard and captures the owned view. This
replaces the earlier prefix-only, pre-commit capture. The suite **requires** that
sidecar for every Mac task, not merely its existence or a `ready: true` flag.
No hosted success for this new contract is claimed here before execution.

The sidecar has exactly these 20 fields, with no source content or free-form path:

```text
schema_version generation version installation_nonce presentation_sequence
document_kind completeness coverage_start coverage_length source_units
token_count diagnostic_count style_ready geometry_known grid_required grid_ready
grid_rows grid_columns grid_cells grid_pending_cells
```

The independent artifact reader enforces:

- File size ≤16 KiB; exact field set; integer fields reject booleans, negatives
  and values outside signed 64-bit range; readiness fields must be actual booleans.
- Schema version 1, positive generation/installation nonce, nonnegative sequence,
  expected closed document-kind name, `Complete`, and full coverage beginning at 0.
- `source_units` and `coverage_length` equal the independently computed complete
  expected edited UTF-16 length, including CSV CRLF and the plain-text emoji.
- The version matches a complete successful Save chain, **in the same process**
  as successful parse, analysis publication and native style publication at that
  version. Counts without this linkage cannot certify readiness.
- These valid small fixtures have zero diagnostics. Plain text has zero format
  tokens; each structured sample has positive tokens. A bounded sanity check
  limits token counts to eight times this small sample's source units; this is
  an artifact count check, not an application performance or large-file budget.
- The known CSV task includes its ordinary header row: all **3 rows ×3 columns =9
  visible cells** must be admitted and non-pending. A partial viewport, inconsistent
  product count, pending cell or wrong Grid-required flag fails. Non-CSV samples
  must have false Grid flags and zero Grid counts.

The producer's tested identity guard checks source installation stamp/nonce,
current analysis stamp/presentation sequence, installed semantic revision/theme,
geometry-known state and the actual CSV ready-frame/analysis/projection identity.
The artifact reader does not pretend the existing privacy-preserving trace
independently transmits or attests every nonce/generation/sequence field. Neither
the guard nor exact numeric checks alone certify pixel quality, OS input methods,
accessibility reading, universal semantic conformance or experience latency.

Suite reports retain the checked sidecar hash/fields and Save/current-phase
linkage verdict as `final_semantics`. The Windows producer now supplies its own
distinct observable schema; no Mac identity/geometry fields are fabricated there.

After this bounded contract change, the portable artifact-oracle suite passed
**12/12**, zero failures/skips, in 0.137 seconds; raw output is retained at
`.temp/release-semantic-oracle-tests.log`. New negative controls exercise partial
coverage/wrong source length, wrong kind/version, unknown path field, invalid
integer/boolean types, zero nonce, incomplete analysis, excessive counts, stale
parse/publication/style versions, partial/pending CSV frames and invented plain
text tokens. Existing independent bytes/default-source/Save controls remain.
PowerShell parsing passed. No GUI, full product tests, commit or dispatch were
performed by this validator.

### Distinct Windows final-view contract and actual helper verification

The Windows driver also writes `native-product-semantics.json`, using **10**
closed fields (not the Mac 20-field schema):

```text
schema_version document_kind version trace_session_id source_units
analysis_status_current parse_publish_style_witness grid_cells grid_labels_exact endpoint
```

The required endpoint literal is:
`native status/preview or actual owner-data callbacks plus version-linked trace; not physical pixels`.
Free-form endpoint strings, paths, extra Mac identity fields and omitted fields
are rejected. The schema/kind/UTF-16 length and integer/boolean types are checked
independently. The session ID must be a valid 32-character causal identifier and
match **the same process** as the complete Save request/version and matching
parse/publication/style phases. A matching version in a different process does
not pass. CSV requires nine actual label-checked cells; non-CSV requires zero
cells and a false Grid-label flag.

The producing driver waits for current complete-analysis native status, actual
edited preview values for Markdown/TOML/JSON/YAML, and source text for plain text.
For CSV it reads all nine actual owner-data ListView cells from the owned process
and compares them against independently decoded expected records. The artifact
reader checks the fixed evidence and trace linkage; it does not claim to repeat
those native calls, prove pixel colors or certify Mac-style geometry identities.
Both platform schemas are now required by the suite, selected explicitly by
`--semantic-platform windows|macos`.

After adding the Windows schema/session-linkage controls, the narrow Python
oracle suite passed **13/13**, zero failures/skips, in 0.171 seconds. The separate
negative cases include a wrong process ID, wrong saved version/length, invalid
boolean/integer, false current status, unknown endpoint, extra field, and a
partial CSV cell count. PowerShell parsing passed.

Independent **artifact-only** validation also applied the new reader to all six
already-produced local managed Windows task artifacts in
`.temp/windows-release-default-semantic-suite-1/`: full original/output bytes,
saved session/version/current analysis/style and CSV label-count evidence passed.
This did not rerun the editor or upgrade managed evidence into Native AOT/hosted
evidence. Reproducer/results:
`.temp/release-semantic-artifact-audit/audit.py` and `result.json`.

Finally, a repository-local harness extracted the actual current `Invoke-Child`
and `Assert-Traces` functions, adjusting only the oracle entry-file location for
the relocated harness, and called the **real PowerShell→Python CLI** semantic
path against the existing CSV evidence. Expected UTF-16 length 89, platform
Windows, complete Save and nine cells all passed. This exercises the newly added
argument construction/binding, not only a Python function call. Retained:
`.temp/release-semantic-artifact-audit/helper.ps1`, `helper-result.log` and
`helper.stdout.txt`. No GUI/global setting/full-suite rerun or dispatch occurred.
