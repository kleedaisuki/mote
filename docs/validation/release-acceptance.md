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

## Scoped final-view audit: failed run 36928548957

Frozen source: **`832dae60ed5361ee45f652d39ef61dd2ae208d19`**. The run is a
**terminal failure**: both Windows six-task final-view suites passed, but both
Mac suites stopped at CSV stage 8 after their first four successful formats.
Do not promote partial Mac tasks or green managed tests into a complete release.

```powershell
python -B .cache/release-ci-36928548957/product-audit/audit.py
```

This artifact-only audit reused the independent full-byte fixtures and applied
the distinct Windows/Mac sidecar schema readers to the actual retained data.
No GUI/product/test rerun, original-report rewrite or production change occurred.

| Target | Completed source/Save/fresh-reopen/final-view tasks | Completed-task trace rows | Save version | Not certified |
| --- | ---: | ---: | ---: | --- |
| win-x64 | 6/6 | 729 | 5 | Physical input/pixels/reader and broad conformance |
| win-arm64 | 6/6 | 722 | 5 | Same boundaries |
| osx-x64 | 4/6 | 618 | 4 | CSV completion; plain-text task not started |
| osx-arm64 | 4/6 | 652 | 4 | CSV completion; plain-text task not started |

All **20 completed tasks** match independent original and saved bytes, protect
their originals, and have successful fresh GUI process reopen evidence. Their
**32 process traces / 2,721 rows** pass the existing strict session/causal/privacy
checks. Every completed final-view sidecar matches the suite's retained checked
sidecar result and is linked to a complete Save chain plus successful same-version
parse/publication/style evidence. This is new evidence, not a reinterpretation
of the earlier runs that captured only prefix text or lacked saved-version
analysis publication.

Both Windows targets actually use bare/default opening for edit and fresh reopen.
Their sidecars bind the exact saved version/session, expected source units,
current native status/preview and—on CSV—all nine actual label-checked cells.
Both Mac targets completed Markdown/TOML/JSON/YAML with complete current coverage,
zero diagnostics, admitted style/geometry identities, and token counts
**10 /15 /10 /50**, respectively. All four targets' CLI/config cases pass their
recorded contracts, including the actual bare/default source-surface install
and readback witnesses in opt-in config traces.

The incomplete Mac CSV evidence is deliberately preserved separately:

- Both original and saved CSV bytes are independently exact, and each has a
  complete successful Save chain at **version 4**.
- Both have successful parse, publication and style phases at version 4.
- Both stderr files say `Mac release workflow stage 8 did not complete.`; the
  child returned 1, with no success marker.
- Neither has a final semantic sidecar, final PNG or fresh GUI reopen record.
  The plain-text directory/task was never reached.
- Each CSV trace passes structural graph integrity (x64 **129 rows**, ARM64
  **127 rows**), but graph/Save success is **not** CSV task completion.
- Each retained trace also has an `analysis.to_presentation` failure at **version
  3**. That older-version failure must not be attributed to the successful saved
  version 4 or described as proof that current version-4 presentation failed.

The frozen observer reveals a concrete architecture bias: `ProbeReleaseSemantics`
passed `_csvGrid.AccessibilityFrame` to the final-ready model, while publication
of that frame requires the **disabled-by-default experimental**
`MOTE_NATIVE_GRID_ACCESSIBILITY` registration. A normal product run therefore
could never satisfy a guard requiring that optional frame. This establishes an
observer dependency defect; it does **not** establish actual visible CSV cells
were correct. The product owner is changing the witness to inspect the actual
installed render state without enabling experimental accessibility. Corrected
runtime CSV/final-view evidence remains required in the next frozen run.

The six retained TRX collections were independently enumerated: on each OS the
main suite has **3,684/3,684** passed results, Themes **14/14**, Configuration
**9/9**, zero failures or unexecuted results. These managed correctness checks
remain distinct from the Mac AOT task failures.

Suite executable identities (reported frozen binaries):

- win-x64: `3E435AF7C3E35FDFA78C4ADACC035054CDBEB2B3FE8DDD0BC59D6D623CC51DE6`.
- win-arm64: `AB9A72073A8925CEFFA299D736B57F6120C45AF5C9094CFECF5748C0F414300E`.
- osx-x64: `4E323EF5A06944FBE0C0C20EE8A5281F1A945B68117D8D7996ED70FBF8396DD3`.
- osx-arm64: `31198239A9195D6BF8AE0338ED0C2DD007CAD1875C6EA3B384714CF739F13814`.

Reproduction/evidence:

- `.cache/release-ci-36928548957/product-audit/audit.py` and `result.json`
- `.cache/release-ci-36928548957/product-audit/test-counters.json`
- `.cache/release-ci-36928548957/product-audit/frozen-mac-semantic-model.cs`
- `.cache/release-ci-36928548957/product-audit/frozen-mac-semantic-observer.cs`
- `.cache/release-ci-36928548957/product-audit/frozen-mac-grid-accessibility.cs`
- `.cache/release-ci-36928548957/evidence/release-evidence-<rid>/product/`

Supported verdict: Windows's new six-task default/final-view contracts pass;
Mac's first four do too, but **neither Mac CSV nor Mac plain-text completion is
certified, and the release remains failed**. No gate was waived or hidden.

## Corrected Mac render witness, remaining Windows observer failure: 36931198293

Source **`a86121ea4a1477796b3174332b921e3efb716a89`**; run conclusion remains
**failure**. Artifact-only audit:

```powershell
python -B .cache/release-ci-36931198293/product-audit/audit.py
```

| Target | Completed tasks | Completed-task trace rows | Remaining scope failure |
| --- | ---: | ---: | --- |
| win-x64 | 6/6 | 727 | None within this task contract |
| win-arm64 | 4/6 | 472 | CSV premature file read; plain text not started |
| osx-x64 | 6/6 | 1,009 | None within this task contract |
| osx-arm64 | 6/6 | 1,030 | None within this task contract |

All **22 completed tasks /32 process traces /3,238 rows** satisfy the previously
specified independent bytes, protected-original, fresh GUI reopen and strict
Save/current-semantic-sidecar contracts. All CLI/config cases match their
recorded bare/default source-surface witness requirements. These findings do not
certify physical input, reader behavior, pixel colors or broad format conformance.

Both Mac CSV tasks now have actual final sidecars and post-Save captures, complete
source coverage of **89 UTF-16 units**, Save/current version **4**, 3 rows ×3
columns, **9 ready cells /0 pending**, complete analysis with **2 tokens /0
diagnostics**, and current admitted style/geometry identities. Each has a
successful separate GUI reopen. Frozen code now reads the installed render
observation (`ProbeReleaseRenderedGrid.Frame`) rather than the optionally
registered AX frame; the experimental AX flag remains off. Thus this is actual
runtime evidence for the corrected ordinary-product observer, not a gate bypass
by enabling an experimental provider. Optional historical refusal JSON is absent
on success and is **not** a required product artifact.

Windows ARM64's incomplete CSV is not silently counted as a completed task:

- Original input bytes are exact; the retained output eventually equals the
  independent expected **95-byte** edited CSV.
- The native driver report is `failed`, stage `save`; stderr reports a
  `ReadAllText` sharing violation while the save operation still owns the file.
- The retained **93-row** trace has a captured version **5** and committed bytes,
  but the Save request is **censored**: last positive stage is
  `save.ui_post_returned`, required UI-start/completion acknowledgements and
  request/session terminal evidence are absent. Graph integrity is incomplete
  (`missing-causal-parent`, `normal-session-shutdown-not-certified`).
- No final sidecar/PNG or fresh reopen exists; the plain-text task was not reached.
  Eventual correct file bytes do not repair the missing lifecycle evidence.

The frozen external driver read the file before waiting for the native Save
completion acknowledgement; its failure cleanup terminates the child. The
product owner is correcting this observation order without adding retries,
changing timeouts or reducing byte/semantic checks. This evidence does not
establish a product Save corruption failure, nor does it certify the incomplete
task. Corrected Windows ARM64 runtime completion is still required.

The six TRX collections were enumerated without rerun: both OS main suites
**3,695/3,695**, Themes **14/14**, Configuration **9/9**, no failed/unexecuted
results. Complete artifact audit and counters are retained in
`.cache/release-ci-36931198293/product-audit/{audit.py,result.json,test-counters.json}`;
frozen Mac render/semantic observer and Windows driver copies are stored there.

Supported verdict: both Mac six-task final-view contracts and Windows x64 pass;
Windows ARM64's first four pass, but **CSV completion and plain text remain
unqualified, and the overall release is still failed**. No GUI/test rerun,
original artifact rewrite or production change was performed by this audit.

## Successful chosen-default candidate: run 36933575580

Frozen candidate source **`f3da110534e9c9c234c4d9a4deae6b54d1e4fdae`** completed
all eight hosted jobs successfully. This is the chosen-default **candidate**, not
a certificate for a future main/merge/tag commit or a claim that a GitHub release
has already been published. The final shipping identity must remain explicit.

```powershell
python -B .cache/release-ci-36933575580/product-audit/audit.py
```

Independent artifact audit reuses all prior byte/trace/final-view/default-entry
contracts, with no GUI or test rerun:

| Target | Exact task pairs and fresh GUI reopens | Task process traces | Trace rows | Save/current semantic version |
| --- | ---: | ---: | ---: | ---: |
| win-x64 | 6/6 | 12 | 727 | 5 |
| win-arm64 | 6/6 | 12 | 715 | 5 |
| osx-x64 | 6/6 | 6 | 974 | 4 |
| osx-arm64 | 6/6 | 6 | 1,023 | 4 |

All **24 tasks /36 processes /3,439 task trace rows** pass exact independent
original/output bytes, original protection, normal completion/fresh GUI reopen,
closed schema/privacy, complete causal graph, successful session terminal and
no-observed-drop checks. Each final semantic sidecar equals the suite's retained
checked result and is linked to its complete Save chain plus matching current
parse/publication/style phases. Both Mac CSV tasks have actual-render 3×3 ready
frames, zero pending cells, full 89-unit source coverage and version-4 identities;
both Windows CSV tasks have nine actual label-checked cells and version-5 evidence.
No task is skipped or replaced with a successful file-only observation.

The Windows completion-before-read correction is now supported by **12 actual
runtime witnesses**. Each native report records a completed Save version and
session matching its final-view sidecar/strict Save chain, plus true
`exact_saved_read_after_completion`. Inspection of the frozen driver confirms
the acknowledgement predicate requires successful `document.save`,
`save.completed` and `command.save` in the same session/version before its one
saved-target `ReadAllText` call. There is no target-file polling/retry or relaxed
watchdog. These witnesses and the normal complete traces replace—not excuse—the
previous ARM64 sharing-violation/censored lifecycle result.

All four targets also pass independently inspected runtime/help/invalid-argument
output, three bare/default config startup cases, unchanged independently
specified config bytes, default-off home absence, and strict native-source
install/readback witnesses in both opt-in traces. The six retained TRX
collections contain, per OS, **3,695/3,695** main results, Themes **14/14** and
Configuration **9/9**, without failed or unexecuted results.

Reported candidate executable SHA-256 identities:

- win-x64: `6DAB00C8ABCC075B99327DD14C3A49DA5B926A87EFF1AC6790464226F7F6892E`.
- win-arm64: `DFDD62FD0108F12C32CF980B389F565342F2F35E4FFDFE95D82D8A25104D05A4`.
- osx-x64: `2D5B11466B1C8EB36BEEEB425DBA0E70D70C8E9ADBED34C2E0F025AA0E3B7402`.
- osx-arm64: `331DE91EEEE3FF71429AE778838C0C555DE8E887E9244CDD10BE5FA9E5328657`.

The separate root-owned asset/source audit at
`.cache/release-ci-36933575580/asset-audit.json` checks ten checksummed collected
assets, architecture/payload identities and **886** corresponding source blobs
against this exact candidate. This task audit does not substitute a new main/tag
source identity for that verified one.

Persistent evidence:

- `.cache/release-ci-36933575580/product-audit/{audit.py,result.json,test-counters.json}`
- `.cache/release-ci-36933575580/product-audit/frozen-windows-task.ps1`
- `.cache/release-ci-36933575580/product-audit/frozen-suite.ps1`
- `.cache/release-ci-36933575580/evidence/release-evidence-<rid>/product/`

Supported verdict: **all four chosen-default Native AOT candidate task/final-view
contracts pass**, with the previously failed Mac render observer and Windows ARM
Save observation now actually exercised successfully. This does not turn green
CI into physical keyboard/Pinyin, screen-reader, pixel-color, broad semantic
conformance or latency-percentile certification. Final merge/tag publication
and its artifact identity remain separate release steps.

## Final main identity audit: run 36935354135

After PR 2 merged, the exact main source
**`3c2209cb8728120c5f2b94f9155b0c57b13ee7c4`** completed all eight jobs in
**run 36935354135**. This source differs from the previously qualified candidate,
so its actual binaries and tasks were independently audited rather than inheriting
the candidate verdict or executable hashes.

```powershell
python -B .cache/release-ci-36935354135/product-audit/audit.py
```

| Final main target | Exact original/output pairs and fresh GUI reopens | Process traces | Trace rows |
| --- | ---: | ---: | ---: |
| win-x64 | 6/6 | 12 | 727 |
| win-arm64 | 6/6 | 12 | 719 |
| osx-x64 | 6/6 | 6 | 990 |
| osx-arm64 | 6/6 | 6 | 1,024 |

All **24 tasks /36 processes /3,460 task rows** pass the unchanged independent
byte/protected-original/fresh-reopen/strict Save/current final-sidecar contracts.
All 12 Windows completion-before-one-read witnesses match the final-view
session/version and complete Save chain. Both Mac CSV observations have their
actual-render complete 3×3 ready frames with zero pending cells. CLI/config output,
independently specified unchanged config bytes, and bare/default source-surface
startup witnesses also pass on all targets. No physical input, reader, pixel-color,
universal format-conformance or latency-percentile claim is added.

The audit now additionally reads each final archive directly, verifies its
collected `SHA256SUMS` entry, and binds the actual executable bytes/size to the
manifest's **exact main source/RID** and suite executable identity. Final executable
SHA-256 values are:

- win-x64: `8AE70248A2E3CD30993AD9A67FC3DD30E5005736DEA1FBBA072A9DEC617C6929`.
- win-arm64: `D67FAA64835D55F635D1E89292B0320EE0E8A928A8F2A378064B8057ED4E5B25`.
- osx-x64: `262D782D2900F8170BBA9A9E6EE733341BD47906B23CF9D44FA5A78E12368656`.
- osx-arm64: `A9A2F4C31C3B930758E35754EF1DDAA0A627C9F6B6A2F0F884A648B62478C2FE`.

The root-owned separate asset/source audit also passes for this same main commit:
ten checksummed assets and **886** corresponding Git source blobs. The six TRX
collections were enumerated without rerun: each OS main suite **3,695/3,695**,
Themes **14/14**, Configuration **9/9**, zero failures/unexecuted results.

Evidence:

- `.cache/release-ci-36935354135/product-audit/{audit.py,result.json,test-counters.json}`
- `.cache/release-ci-36935354135/product-audit/frozen-windows-task.ps1`
- `.cache/release-ci-36935354135/product-audit/frozen-suite.ps1`
- `.cache/release-ci-36935354135/evidence/release-evidence-<rid>/product/`
- `.cache/release-ci-36935354135/assets/` and `asset-audit.json`

Supported verdict: **the exact final-main Native AOT payloads pass all four
default task/final-view contracts**. Publication must use these verified archives
and a tag pointing to this source; an older candidate or later documentation-only
commit must not silently replace their identity. This audit does not claim that
publication has already occurred. No GUI/test rerun, original artifact rewrite,
production/oracle change or commit was performed by this final audit.

## Serviced toolchain candidate: run 36937827713

Publication of the preceding `3c2209c` binaries was held for toolchain servicing:
their successful functional audit did not make the older embedded runtime an
appropriate release payload after a security update was identified. This new
qualification supersedes their **release eligibility**, not their historical
functional evidence.

Exact serviced candidate:
**`fcac11d0f1f2cfdc8de8f8777bf2c5478d8f581a`**, run **36937827713**, all eight
jobs successful. The independent task audit reused the unchanged behavioral
oracles; no local GUI/test rerun or production/oracle change occurred.

```powershell
python -B .cache/release-ci-36937827713/product-audit/audit.py
```

| Serviced target | Exact tasks and fresh GUI reopens | Process traces | Task rows |
| --- | ---: | ---: | ---: |
| win-x64 | 6/6 | 12 | 726 |
| win-arm64 | 6/6 | 12 | 709 |
| osx-x64 | 6/6 | 6 | 987 |
| osx-arm64 | 6/6 | 6 | 1,020 |

All **24 tasks /36 processes /3,442 rows** pass independent original/output bytes,
original protection, fresh GUI reopen, strict Save/current semantic sidecars and
bare/default CLI/config witnesses. All 12 Windows completion-before-read witnesses
match their actual successful Save session/version. All four archive checksums and
actual executable payload sizes/hashes match the serviced source/RID manifests
and suite identities.

The manifest's expanded `build.toolchain` summary is supported rather than rejected
as an unexpected old-build-field shape. Each target reports pinned **SDK 10.0.401,
runtime 10.0.12 and ILCompiler 10.0.12**, with matching RID/schema. This artifact
audit checks that sanitized summary and source/payload binding; the separate root
asset/toolchain audit verifies the retained resolved raw metadata against that
summary. Its `asset-audit.json` passes ten checksummed assets and **888** matching
Git source blobs for this exact commit. No claim that AOT automatically inherits
machine-installed runtime security updates is made.

Serviced candidate executable SHA-256 values:

- win-x64: `6D4E62AF2213DC06614663F56D1B6D36490E07827583AE6765630F9B0ADF0AC3`.
- win-arm64: `B8FC9B6EE853E62D852258ECBC9E40BA565687687F1C53AF0F0FE5AD544979F6`.
- osx-x64: `A535833FC3922D18B372FC18A75C32144CEEF54B1522DB836193E11C20798055`.
- osx-arm64: `1DC0B425EAB3F7A329DA81C6774F0D6EB825CFD984D12460579D1F285B7CF7B2`.

The six actual TRX collections still enumerate, per OS, **3,695/3,695** main
results, Themes **14/14**, Configuration **9/9**, zero failures/unexecuted results.
Evidence and frozen helper copies:
`.cache/release-ci-36937827713/product-audit/{audit.py,result.json,test-counters.json}`,
the adjacent `frozen-windows-task.ps1`/`frozen-suite.ps1`, and the downloaded
`assets/`/`evidence/` trees plus root `asset-audit.json`.

Supported verdict: the **serviced exact candidate** passes all four task/final-view
contracts and its version/source/payload checks. Future merge/main/tag identity
must still be qualified explicitly; this is not publication or physical input,
reader, pixel-color, universal semantic conformance or latency certification.

## Final serviced main qualification: run 36938529836

After the servicing change merged, exact main
**`ed96fe4f29278e633e10421c89e2ce1f5b0536ae`**, run **36938529836**, completed
all eight jobs successfully. This is independently checked final-main evidence,
not the `fcac11d` candidate result relabeled after merging.

```powershell
python -B .cache/release-ci-36938529836/product-audit/audit.py
```

| Final serviced target | Exact tasks and fresh GUI reopens | Process traces | Task rows |
| --- | ---: | ---: | ---: |
| win-x64 | 6/6 | 12 | 731 |
| win-arm64 | 6/6 | 12 | 727 |
| osx-x64 | 6/6 | 6 | 994 |
| osx-arm64 | 6/6 | 6 | 1,027 |

All **24 tasks /36 processes /3,479 task records** pass unchanged independent
text bytes, original protection, normal fresh GUI reopen, strict Save graph,
current final-sidecar and default CLI/config contracts. All 12 Windows reports
bind completion-before-one-read to the matching successful Save session/version.
Both Mac CSV tasks have complete actual-render 3×3 ready frames with zero pending
cells and current admitted analysis/style identities. These successful witnesses
do not expand into physical Pinyin, reader, pixel-color or performance claims.

The collected archive checksums, actual executable payload bytes/sizes, exact
main/RID manifests and suite identities all agree. The expanded toolchain summary
remains **SDK 10.0.401 /runtime 10.0.12 /ILCompiler 10.0.12**. Root's independent
archive/source/raw-toolchain audit also passes ten checksummed assets and **888**
corresponding Git blobs for this same main commit. Known framework-resolution
pack records are toolchain evidence, not a claim that every recorded ASP.NET or
WindowsDesktop pack is linked or shipped in the editor.

Final serviced-main executable SHA-256 values:

- win-x64: `BD01A0E5E642A6227FE5E53147891C9B95F2938B553B9142C54C1F5E9EC1622B`.
- win-arm64: `A33457F96EF4862059F17689E23B73F3C9D93E2C3C6CC83F80A987E441E81A38`.
- osx-x64: `0C2B9CBEF7EFF87C52641D032BA0CEEC3FC0B5373E5361C8802617C17CAD22C3`.
- osx-arm64: `32CA794B25EEBED62B55D06B9CA65E3E38ECFD83FBEBD8DFBEAE15EB6BAAA3F7`.

Enumerated actual TRX results, per OS: **3,695/3,695** main, Themes **14/14**,
Configuration **9/9**, with no failed or unexecuted results. Reproducible audit,
counters and frozen helper copies are retained in
`.cache/release-ci-36938529836/product-audit/`; raw tasks/manifests/archives remain
under its run's `evidence/` and `assets/`, with root `asset-audit.json`.

Supported verdict: **the exact serviced final-main four-target release payloads
pass the task/final-view and toolchain/source identity contracts**. Shipping must
use these verified archives and a tag pointing to `ed96fe4`; neither the old
runtime-11 main nor the servicing candidate may silently replace this identity.
This entry records qualification, not completed publication. No GUI/test rerun,
production/oracle change, original artifact rewrite or commit occurred in this
audit; the release owner records publication separately.
