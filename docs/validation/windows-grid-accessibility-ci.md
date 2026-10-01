# Hosted Windows CSV Grid accessibility subset diagnostic

Date: 2026-10-01. Current checkpoint: native-architecture owned-HWND focus tests pass on both Windows RIDs; the unchanged external AOT Grid oracle still reports product-fail/exit 1. Earlier pin-rejection and successful subset evidence below remain historical. The external diagnostic remains non-gating, not release acceptance.

## Contract and provenance

The `native-aot` job runs `tests/WindowsGridExternalProbe` only for `win-x64`,
next to the existing external source-canvas UIA diagnostics. It uses the exact
`src/Mote.Native/bin/Release/net10.0/win-x64/publish/mote.exe` already published
and checked by that job, not a JIT editor or a separately rebuilt host. The
separate managed MTA UIA client is built with warnings-as-errors through its
project contract. Client build/restore output stays in
`.cache/windows-grid-accessibility-ci/win-x64/build/`.

Reviewed source pins:

- `Program.cs`: `1AF72C383BB49ADD171AD8E6DADA7EE2BD012506262D5FBC292DA06B4453F6C7`.
- `WindowsGridExternalProbe.csproj`, exact LF encoding: `5A442CDB96A250C26556165CABD5C58224378CD4573D6ECC13318DFCFF95F5E2`.
- The same project, exact CRLF encoding: `6A99955812E8EA1631792E3EF0B47798B42A1A9F916FE86AF6D992F2E32FA3D4`.

The two project pins admit only these two reviewed raw byte streams through
ordinal hash equality. They do not normalize arbitrary input or accept changed
content. The Program.cs source pin is unchanged. Local deterministic conversion
from the reviewed LF bytes to CRLF reproduces the second hash exactly; a
changed-byte negative case is not accepted.

Hosted run `36782853022` at commit `6fada3e2f880d2d564cf597b6ac9ded849d81a19`
recorded the expected Program.cs SHA and the CRLF project SHA in
`.cache/ci-36782853022-grid-windows/inventory.json`. The old LF-only pin rejected
that checkout before build/client execution (`process_exit_code: null`,
`timed_out: false`); **this run provides no Grid UIA execution result**. Git's
checkout line-ending conversion explains the exact verified byte difference,
not a source-content change. The narrow two-pin fix remains fail-closed.

`inventory.json` records the commit/run ID, exact published binary SHA256,
source/project SHA256, timeout and actual client exit status. `report.json`
independently hashes the tested binary and synthetic fixture. The workflow
requires binary-hash equality, an actual zero exit status and exact `pass`
classification. A build failure, timeout, missing/mismatched report,
`product-fail` or `inconclusive` fails this diagnostic step only.

## Safety and limits

The client creates only an 1100-by-32 synthetic CSV under
`.temp/windows-grid-accessibility-ci/win-x64/`, isolates `MOTE_HOME` there,
removes child `MOTE_TRACE`, and enables the child-only
`MOTE_NATIVE_GRID_ACCESSIBILITY=1` opt-in. It does not touch the clipboard, send
global key input, switch input sources or exercise a screen reader. Synthetic
F6 messages target the launched editor's own focus HWND; the coordinate prompt
must belong to the exact launched PID before interaction. No unrelated process
is terminated. The workflow allows 120 seconds for the client, kills only that
owned process tree on timeout, and bounds termination wait to another 10 seconds.
The entire diagnostic step has a five-minute Actions timeout.

Foreign global semantic focus remains explicitly **blocked**, not a failed or
passed foreground-focus test. The client inspects ownership before any foreign
semantic name/type. Its aggregate `pass` concerns only bounded reads, admitted
rectangular selection, read-only range facts, owned synthetic F6, coordinate
navigation and retained-cell retirement. It is not evidence of external cell
SetFocus, RangeValue writes, physical IME, reader speech, win-arm64, large-file
memory teardown or multi-monitor acceptance. See
[the implementation validation](windows-grid-accessibility.md) for those gates.

## Artifacts and local verification

The unconditional win-x64 artifact upload retains `report.json`, `inventory.json`
and `build.log` for 14 days; it excludes fixture/home files and build binaries.
Both the diagnostic and its artifact upload use `continue-on-error: true`, so
upload-service failure cannot fail the strict Native AOT job. Missing evidence is
warned about rather than fabricated. The probe prints only
its synthetic-fixture report to the runner log.

Local preparation validation checks YAML structure, both new step conditions,
non-gating/timeout/upload boundaries, exact source pins, and the embedded
PowerShell parser AST. The exact local command
`dotnet build tests/WindowsGridExternalProbe/WindowsGridExternalProbe.csproj --configuration Release --artifacts-path .cache/windows-grid-accessibility-ci-build-probe`
succeeded with zero warnings and zero errors. The expected relative output
`bin/WindowsGridExternalProbe/release/WindowsGridExternalProbe.dll` exists under
that artifacts root, confirming the workflow build-path/case contract. No hosted
UIA execution or new editor performance result is claimed. Before accepting hosted evidence, independently audit the downloaded
artifact binary/source hashes, classification, Errors/Inconclusive arrays and
blocked global-focus status; do not promote the opt-in registration by default.

## Independent hosted result: 2026-10-01, run 36783978146

The first actual hosted client execution at commit
`f8810b28d2dc2ce71676f05789ad36ba6508a8af` passes the **declared diagnostic
subset**, not accessibility release acceptance. This result supersedes the
pending-hosted status above only for that subset. The independently audited
artifacts are under `.cache/ci-36783978146-grid-windows/` (`inventory.json`,
`report.json`, `build.log`); the separately downloaded
`native-inventory-win-x64` artifact is under its `publish-inventory/` subdirectory.

### Attribution and independent checks

- Both the driver inventory and the client report identify binary SHA256
  `EF49E969C34E5D72E5A2B202EFB62F73F16EB5A940B369DB8B10949EF169BAA5`.
  The workflow uses the published win-x64 AOT editor, and the client hashes the
  executable it launches. The independent publish inventory records one
  `mote.exe`, 7,068,672 bytes, no companion payload or bundled native library,
  and no unexpected static imports. **That publish inventory has no SHA256**;
  its size/path cannot provide a third independent binary-hash comparison.
  The executable itself was not downloaded or rehashed by this audit.
- Hosted Program.cs SHA256 matches the reviewed source pin
  `1AF72C383BB49ADD171AD8E6DADA7EE2BD012506262D5FBC292DA06B4453F6C7`.
  Hosted project SHA256 is the admitted CRLF value
  `6A99955812E8EA1631792E3EF0B47798B42A1A9F916FE86AF6D992F2E32FA3D4`.
  Independent Python byte conversion of the reviewed local LF project
  reproduces both LF and CRLF pins exactly; this is not arbitrary normalization
  of an untrusted hosted file.
- Independently reconstructing all 1100 records and 32 columns, with values
  `R{one-based row}C{one-based column}` and CRLF record endings, reproduces
  fixture SHA256 `86796C9AF5EADC2DB5A0B8FBE3F14245DF7AB2F0456E6EE5189ED6987819E0B4`.
- Actual client exit is 0, timeout is false, Classification is exactly `pass`,
  and both Errors and Inconclusive arrays are empty. Build log reports zero
  warnings/errors. Environment is Windows NT 10.0.26100.0, X64, target PID 8448.
  Audit commands included `Get-FileHash` for the client source/project,
  `gh run download 36783978146 -n native-inventory-win-x64 -D
  .cache/ci-36783978146-grid-windows/publish-inventory`, and Python assertions
  over raw-byte pins, fixture reconstruction and downloaded JSON fields.

### Observed behavior and what the pinned client actually tests

| Claim | Evidence and boundary |
| --- | --- |
| Bounded initial Grid | Reported local Grid counts are 64 rows by 16 columns. Raw, Control and Content views each contain 1104 children, consistent with 1024 cells plus 64 row headers and 16 column headers. Each UnexpectedChildren array is empty. The client rejects non-Grid HeaderItem/DataItem prefixes but does **not** assert unique AutomationIds or exact child count, so this is not a duplicate-node uniqueness proof. |
| Read-only cell and selection | Pinned executed checks require first presentation value `R1C1` to be read-only and omit source Invoke. Select on (0,0), then AddToSelection on (0,1), yields selection count 2; adding (1,1) must throw InvalidOperationException and leave count 2. These checks passed; the report does not serialize selected identities and the client does not independently compare their coordinates. Thus it proves the admitted operation/count and atomic rejection subset, not arbitrary rectangle membership correctness. |
| Logical ranges | Initial row range: min 0, max 1076, value 0, read-only, small change 1, large change 24. Initial column range: min 0, max 28, value 0, read-only, small change 1, large change 4. After Go to: row max 1074/value 1000, column max 31/value 16, both min 0/read-only. Maxima are observed geometry-dependent snapshots, not a stable cross-host constant; the client specifically asserts distant origin values 1000/16. Writes are not exercised. |
| Coordinate navigation | Prompt ownership check passes for the launched PID; readback is `1001:17`. New first cell name/value is `Row 1001, Column 17; presentation value: R1001C17`; status reports absolute anchor and active coordinates 1001:17. Executed GridItem check requires this cell's local indices to remain (0,0). |
| Retained-node retirement | Pinned check reads the original first cell after rebase and requires ElementNotAvailableException. Empty Errors/Inconclusive and pass classification demonstrate the check completed. There is no separate serialized stale-node boolean. |
| Owned synthetic F6 | All five expected HWNDs exactly match observed HWNDs: table, row navigation, column navigation, Go to button, source. Unlike earlier local evidence, all five actual global semantic focus readings belong to the launched PID and have semantic HasKeyboardFocus true. On table entry the HWND-derived table element has HasKeyboardFocus **false**, while global semantic focus is the selected DataItem `Row 1, Column 2; presentation value: R1C2` with HasKeyboardFocus **true**. Do not flatten this into a claim that every HWND element advertises keyboard focus. Native row/column focus assertions pass, with previous element unfocused. This is owned message-driven F6, not physical keyboard or reader acceptance. |
| External cell SetFocus | Distant cell SetFocus is explicitly refused with InvalidOperationException on the off-owner generated COM callback. The client treats this safe refusal as expected and records it as blocked; it is **not** successful external cell focus delivery. |

The 1000-query sequence took 360.7573 ms and its maximum single query was
11.2121 ms; overall client elapsed time was 15,999.5709 ms. These are one hosted
synthetic run with waits and UIA/client overhead, not startup, editing latency,
physical paint, a percentile estimate, or a regression benchmark.

### Supported verdict and remaining gates

No implementation failure is demonstrated by these artifacts within the stated
subset. The initial rejected checkout still provides no execution evidence.
The present pass does not justify enabling Grid accessibility by default.
No screen reader speech, physical IME, win-arm64 Grid behavior, 100 MiB memory
teardown, multi-monitor scaling, RangeValue writes or successful off-owner cell
focus was tested. The table-HWND versus selected-DataItem focus distinction is
an observed fact that needs reader/contract-specific acceptance, not evidence
by itself of a product defect. Probe coverage also lacks unique-child-ID and
selected-identity assertions; avoid stronger claims than the executed checks.

## Windows ARM64 diagnostic extension: prepared, hosted execution pending

The current workflow now runs the same non-gating external Grid client on both
`win-x64` (`windows-latest`) and `win-arm64` (`windows-11-arm`). This extends
execution scope only: the successful x64 result above remains historical evidence
for its original commit and source pin, not evidence for the new ARM64 run.
No ARM64 UIA execution result or cross-RID parity is claimed by this change.

Each matrix child launches its own exact previously published Native AOT binary
at `src/Mote.Native/bin/Release/net10.0/<rid>/publish/mote.exe`; the inventory
records that RID and executable hash, and requires the client report to identify
the same hash. Client build output and reports stay under
`.cache/windows-grid-accessibility-ci/<rid>/`; isolated synthetic fixture/home
files stay under `.temp/windows-grid-accessibility-ci/<rid>/`. Artifacts are
`windows-grid-accessibility-ci-win-x64` and
`windows-grid-accessibility-ci-win-arm64`, each containing only its own
`report.json`, `inventory.json`, and `build.log`. Both diagnostic and always-run
upload remain non-gating, with the existing 120-second client timeout,
10-second owned-tree termination bound, five-minute step timeout, and 14-day
artifact retention. No global input or clipboard access was added.

The client's only change is truthful remaining-gate metadata: the unconditional
`win-arm64` label becomes `cross-RID parity beyond this one run`. A passing ARM64
run must not report its own architecture as untested; neither one passing RID
nor the two separate reports by themselves prove broader release acceptance.
Foreign global focus remains an ownership-reported blocked fact rather than a
product failure, and external cell SetFocus remains safely refused, not passed.
Selection, navigation, bounded child enumeration, and all other executed UIA
checks are unchanged.

Current exact reviewed Program.cs byte pins supersede the historical source pin
only for the new workflow revision:

- LF: `4347749886467393FC82BC95A97EE8EBB4E9F2DA600DCF784EC6A88EE4E10E95`.
- CRLF: `5D6E7D538B53685C3C592D1174A00C49F2DF116F13A28E58E47183768233B0ED`.

The project bytes are unchanged; its exact LF/CRLF pins listed above still apply.
The workflow admits only these four exact source/project hashes with ordinal
comparison. It does not normalize unknown hosted bytes. Git checkout may select
LF or CRLF; deterministic local conversion reproduced each admitted pin, and an
extra-byte source negative case was rejected by the pin-presence check.

Preparation validation on the local x64 machine:

- PyYAML 6.0.3 parsed the workflow; assertions passed for both exact RID conditions,
  per-RID artifact names and paths, both non-gating flags, timeouts, retention,
  four reviewed pins, and the altered-byte negative case.
- PowerShell's parser accepted both expanded per-RID diagnostic scripts with no
  AST errors. Scripts are retained at
  `.cache/grid-arm-ci-validation/win-x64.ps1` and `win-arm64.ps1`.
- `dotnet build tests/WindowsGridExternalProbe/WindowsGridExternalProbe.csproj
  --configuration Release --artifacts-path .cache/grid-arm-ci-validation/build`
  passed with zero warnings/errors; output DLL exists at
  `build/bin/WindowsGridExternalProbe/release/WindowsGridExternalProbe.dll`.
  Build log is `.cache/grid-arm-ci-validation/build.log`.
- `git diff --check` passed. No hosted child was launched locally, and no local
  x64 build is counted as hosted ARM execution or a performance measurement.

After hosted execution, independently audit each RID's binary/source attribution,
actual client exit and timeout, classification, Errors/Inconclusive arrays,
owned-focus facts, and blocked external-cell-focus status. Any observed ARM
failure needs its own bounded diagnosis; do not infer cross-RID success from the
existing x64 evidence or enable accessibility registration by default.

## Independent comparative hosted result: 2026-10-01, run 36785565685

Commit `04b5a8ef93ae739fec1c81d2cebf8ce21b316efd` supplies two separate
successful one-run diagnostic subsets, for win-x64 and win-arm64. This extends
actual external Grid execution evidence to ARM64; it does not establish broad
cross-RID parity, reliability or accessibility release acceptance. Artifacts
are under `.cache/ci-36785565685-grid-win-x64/` and
`.cache/ci-36785565685-grid-win-arm64/`; separately downloaded per-RID
`native-inventory` artifacts are in each directory's `publish-inventory/`.

### Exact attribution and outcome

Both inventories identify run 36785565685 and the above full commit. Both use
Program.cs SHA256
`5D6E7D538B53685C3C592D1174A00C49F2DF116F13A28E58E47183768233B0ED`
and project CRLF SHA256
`6A99955812E8EA1631792E3EF0B47798B42A1A9F916FE86AF6D992F2E32FA3D4`.
Independent local source hashing matches the source pin; deterministic LF to
CRLF project conversion reproduces the project pin. The new source retains the
same tested operations and replaces its old untested-win-arm64 remainder with
`cross-RID parity beyond this one run`; no coverage weakening was found in
that source change.

| Fact | win-x64 | win-arm64 |
| --- | --- | --- |
| Binary SHA256, inventory and report equal | `5451367148E6F035DB43222CC2EE003F4D41B36AD663E5140CB7C3356497C2BA` | `879305BD9AC43333C64D7768B80CBEA87B351E4C9710AA88B7C588012B18CE9A` |
| Reported process architecture | X64 | Arm64 |
| OS | Windows NT 10.0.26100.0 | Windows NT 10.0.26200.0 |
| Target PID | 5384 | 10308 |
| Actual exit / timeout / classification | 0 / false / pass | 0 / false / pass |
| Errors / Inconclusive | empty / empty | empty / empty |
| Publish payload | one mote.exe, 7,068,672 bytes | one mote.exe, 7,207,424 bytes |
| Build | 0 warnings, 0 errors | 0 warnings, 0 errors |

The publish inventories list no companion payload, bundled native library or
unexpected static import, but again contain **no binary hash**. Attribution is
the workflow's exact per-RID published executable plus its matching driver/client
hashes, not an independent rehash of downloaded executables. Architecture is the
client's reported process architecture; it does not inspect a downloaded editor
PE header. The two binary hashes differ as expected for separate RIDs and must
not be interchanged.

Independent Python assertions checked commit/run/RID, source/project hashes,
matching per-RID binary hashes, Architecture, exit/timeout/classification,
Errors/Inconclusive, view counts, navigation/read-only facts and ownership-aware
focus serialization. `gh run download 36785565685 -n native-inventory-win-x64
-D .cache/ci-36785565685-grid-win-x64/publish-inventory` and the corresponding
win-arm64 command supplied the additional publication inventories. These
checks passed without executing a new editor or rerunning hosted tests.

### Common bounded observations and important focus difference

Both reports use fixture SHA256
`86796C9AF5EADC2DB5A0B8FBE3F14245DF7AB2F0456E6EE5189ED6987819E0B4`
and initially report a 64-by-16 Grid, first cell `R1C1`, and exactly 1104
children in each Raw/Control/Content view with empty UnexpectedChildren arrays.
The same executed selection-count/illegal-union and retained-node retirement
checks pass on both RIDs. Their limits remain as described in the previous
result: no child-ID uniqueness or returned selection-coordinate assertions.

On both RIDs, Go to readback is `1001:17`, the new first cell identifies absolute
Row 1001/Column 17/value `R1001C17`, and the executed GridItem check requires
local indices (0,0). Row and column range origins are 1000 and 16. All four
initial/after RangeValue snapshots are read-only. Range bounds match between
these particular hosts, but that is a snapshot observation rather than a
universal geometry contract. The five owned synthetic F6 expected and observed
HWNDs match exactly on each RID, and both native row/column focus assertions
pass.

- **x64:** all five global semantic focus samples are target-owned and report
  semantic HasKeyboardFocus true. Table HWND-derived focus remains false while
  the selected DataItem `Row 1, Column 2; presentation value: R1C2` has semantic
  focus true, preserving the earlier distinction.
- **ARM64:** all five global semantic samples belong outside the target PID.
  Classification is explicitly `blocked: foreground focus not owned by exact
  target process; no foreign semantic metadata inspected`. Every semantic name
  and semantic HasKeyboardFocus is null; the type is only the fixed blocked
  placeholder. Inspection of the pinned source confirms ownership is checked
  before conditionally reading semantic type, name or focus. Owned-thread HWND
  transitions and native scroller focus remain tested; **global foreground
  semantic-focus acceptance does not pass on ARM64**. The aggregate subset pass
  intentionally does not claim otherwise. ARM button/source HWND focus flags
  are false in these snapshots and must not be reported as all panes focused.

Off-owner distant cell SetFocus is still refused safely on both RIDs and
serialized as blocked. Neither run demonstrates successful external cell focus
or RangeValue writes. No foreign input, clipboard, screen reader or physical IME
was exercised.

The x64 1000-query sequence is 158.8999 ms (maximum query 0.2469 ms), versus
257.9308 ms (maximum 0.4162 ms) on ARM64. Overall elapsed values are 13,931.0722
and 15,444.4914 ms. Different OS builds, runner hardware and UIA overhead make
these isolated observations **unsuitable for an architecture performance ratio,
regression or percentile claim**.

No product failure is demonstrated within the declared bounded subset.
ARM64 execution is no longer entirely untested, but foreground/reader behavior,
physical IME, successful off-owner focus, RangeValue writes, repeated cross-RID
reliability, 100 MiB memory teardown and multi-monitor scaling remain open.
Default enablement and release acceptance are not justified by these two passes.

## Strengthened child identity and exact selection assertions: local x64 verification

Date: 2026-10-01. This additive result closes two **client assertion** gaps,
not the remaining accessibility release gates. Earlier hosted results above
remain evidence for their original source pin; they cannot retroactively prove
these new assertions. New hosted x64/ARM64 execution is still pending.

The client now walks only the table's direct siblings in each Raw, Control and
Content view. It stops on child 1105, recording a product error before aborting;
no recursive or unbounded descendant traversal was added. Each view must expose
exactly 1104 children for this synthetic initial 64-by-16 window (1024 cells,
64 row headers, 16 column headers). AutomationIds and UIA runtime identities
must each be nonempty and unique. Control/Content identity sets must equal Raw's
sets without relying on enumeration order. These checks do not claim a full
cross-view identity-to-coordinate mapping proof.

Selection references are captured **before** their mutations. After `Select`,
the exact one-cell set must be returned. After rectangular `AddToSelection`,
and again after the rejected sparse addition, the exact two-cell set must be
returned: local coordinates (0,0)/(0,1), absolute headers Row 1 and Columns 1/2,
fixture values `R1C1`/`R1C2`, and both AutomationId and runtime identity. Record
set equality plus exact cardinality rejects duplicates, substitutions and wrong
coordinates while accepting reordering. The report now serializes these three
selection snapshots. Distant navigation remains independently tested as local
(0,0) versus absolute Row 1001/Column 17/value `R1001C17`; runtime identities
are compared only within a live initial window, not across retired frames.

Reviewed raw Program.cs SHA256 pins (no BOM):

- LF: `A1B87971E4975786D67909209E1B2133432C6894710D623F2CD24ECD948940D4`.
- CRLF: `C6894B4EDCF931BAA322B878AF8965759AC7E44EFC10291200737A18C1AD5509`.

Only `tests/WindowsGridExternalProbe/Program.cs` and this additive section were
changed in this task. Production code and workflow pins are unchanged here;
the workflow owner must update the reviewed source pins before hosted execution.
An independent reviewer examined the assertions and the final narrow delta;
its record is `.cache/windows-grid-identity-probe/assertion-review.md`.

### Commands, observed results and attribution limits

```powershell
dotnet build tests/WindowsGridExternalProbe/WindowsGridExternalProbe.csproj --configuration Release --artifacts-path .cache/windows-grid-identity-probe/build
dotnet .cache/windows-grid-identity-probe/build/bin/WindowsGridExternalProbe/release/WindowsGridExternalProbe.dll .cache/windows-grid-accessibility/aot/mote.exe .temp/windows-grid-identity-probe .cache/windows-grid-identity-probe/report.json
```

The final client build completed with zero warnings/errors. Local Windows NT
10.0.26200.0/X64 execution exited 0 with Classification `pass` and empty Errors
and Inconclusive arrays. Each view reported 1104 children, 1104 unique
AutomationIds and 1104 unique runtime identities, with no duplicates. All three
selection snapshots matched the expected exact sets. The report is retained at
`.cache/windows-grid-identity-probe/report.json`.

The launched **previously published** AOT binary SHA256 is
`BE2D17B41853A586F080F4DA3094203C2E9DD0A2AF57FF6B653DDB5B8708D0A0`,
not a new build from current HEAD; its earlier production-source coverage limits
remain documented in `windows-grid-accessibility.md`. Fixture SHA256 is
`86796C9AF5EADC2DB5A0B8FBE3F14245DF7AB2F0456E6EE5189ED6987819E0B4`.
This verifies the strengthened client against that binary only. No product
defect was observed in this run, and no broad solution suite was repeated.

Existing isolation/privacy bounds are unchanged: synthetic repo-local fixture,
child-only opt-in and MOTE_HOME, no clipboard/global keys/input-source changes,
exact-PID prompt checks, and owned-process cleanup. In particular the ARM64
foreign-global-focus classification remains blocked when ownership is absent;
this local x64 pass does not replace that hosted ARM64 observation or prove
reader speech, physical IME, off-owner SetFocus, RangeValue writes, large-file
memory teardown, or release/default enablement readiness.

## Independent strengthened hosted result: 2026-10-01, run 36791254056

The strengthened client actually executed on both Windows RIDs at commit
`f657c003c8bb2d52fd94ea632335a356c2f3d445`. Both reports pass the declared
bounded diagnostic subset, closing the hosted child-identity and exact-selection
assertion gaps above. This is **not** a default-enablement or release verdict.
The workflow step is non-gating; this finding uses downloaded JSON and actual
client exit, not its green Actions conclusion.

Artifacts are `.cache/ci-36791254056-grid-win-x64/` and
`.cache/ci-36791254056-grid-win-arm64/`; each contains `inventory.json`,
`report.json`, `build.log`, and the separately downloaded `native-inventory`
artifact in `publish-inventory/`. The independent audit script is
`.cache/ci-36791254056-grid-win-x64/audit.py`.

### Attribution and independent verification

Both driver inventories identify the exact commit/run/RID, strengthened client
CRLF SHA256 `C6894B4EDCF931BAA322B878AF8965759AC7E44EFC10291200737A18C1AD5509`,
and project CRLF SHA256
`6A99955812E8EA1631792E3EF0B47798B42A1A9F916FE86AF6D992F2E32FA3D4`.
Hashing the current reviewed LF source yields
`A1B87971E4975786D67909209E1B2133432C6894710D623F2CD24ECD948940D4`;
deterministic LF-to-CRLF conversion reproduces the hosted source pin. Independent
1100-by-32 fixture reconstruction again yields
`86796C9AF5EADC2DB5A0B8FBE3F14245DF7AB2F0456E6EE5189ED6987819E0B4`.

| Observed fact | win-x64 | win-arm64 |
| --- | --- | --- |
| Binary SHA256, driver/report equal | `2F951114F588E274BE76DD1220C9529D3E4ECCF30E0AFA328CA24484D6584E31` | `0F52705A9B8F312C81ECAC67D6898B338D55414445459084AC297C41328F9C94` |
| Client architecture / Windows build | X64 / 10.0.26100.0 | Arm64 / 10.0.26200.0 |
| Actual client exit / timeout / classification | 0 / false / pass | 0 / false / pass |
| Errors / Inconclusive | empty / empty | empty / empty |
| Published single mote.exe bytes | 7,070,720 | 7,208,960 |
| Client build warnings / errors | 0 / 0 | 0 / 0 |
| Raw, Control, Content child counts, each | 1104 | 1104 |
| Unique AutomationIds / runtime IDs, each view | 1104 / 1104 | 1104 / 1104 |
| Duplicate IDs and unexpected children | all empty | all empty |

The publish inventories contain one executable, no companion payload or bundled
native library, and no unexpected static imports. They do not contain executable
SHA256 and the executable was not downloaded/rehashed by this audit; matching
driver/client hashes plus the workflow's exact published path are the attribution
basis. Architecture is the client-reported process architecture, not independent
inspection of an editor PE header.

Independent assertions checked the above fields, raw-source pins, reconstructed
fixture, exact selected snapshots, range/navigation facts, and ownership-aware
focus facts. Reproduction commands are:

```powershell
gh run download 36791254056 -n windows-grid-accessibility-ci-win-x64 -D .cache/ci-36791254056-grid-win-x64
gh run download 36791254056 -n windows-grid-accessibility-ci-win-arm64 -D .cache/ci-36791254056-grid-win-arm64
gh run download 36791254056 -n native-inventory-win-x64 -D .cache/ci-36791254056-grid-win-x64/publish-inventory
gh run download 36791254056 -n native-inventory-win-arm64 -D .cache/ci-36791254056-grid-win-arm64/publish-inventory
python .cache/ci-36791254056-grid-win-x64/audit.py
```

Both RID audits passed. No new editor execution or previously completed suite was
repeated locally.

### Stronger tested semantics and remaining boundaries

All three view traversals executed exact-count, nonempty unique identity, and
cross-view identity-set equality checks in the pinned source. The JSON serializes
counts and duplicate lists, but not all 1104 identities; cross-view set equality
is supported by the executed pinned assertion and empty Errors, rather than an
independent reconstruction of those unrecorded sets. No full cross-view
identity-to-coordinate mapping claim follows.

Both serialized selection sequences independently confirm one exact cell after
Select, then the same exact two-cell set after rectangular Add and after rejected
sparse union. Cells have local coordinates (0,0)/(0,1), absolute headers Row 1 and
Columns 1/2, values R1C1/R1C2, IDs `Mote.CsvGrid.1.0.0` / `.1.0.1`, and distinct
nonempty runtime IDs stable across these three live-window snapshots. This closes
the earlier count-only weakness; it does not prove arbitrary rectangle geometry.

Both runs retain Go-to readback `1001:17`, absolute first cell R1001C17,
row/column origins 1000/16, four read-only range snapshots, and all five exact
owned synthetic F6 HWND transitions. The pinned distant local (0,0) and original
retained-cell ElementNotAvailable checks completed with empty Errors; JSON does
not independently serialize those two assertion outcomes.

The foreground distinction persists: x64's five global semantic samples are
target-owned and focused; ARM64's five are explicitly **blocked**, with no foreign
semantic name or focus metadata inspected. ARM64 owned HWND transitions and
row/column native focus are tested, but foreground semantic-focus acceptance is
not passed. On x64 the table HWND-derived element is not focused while its
selected DataItem is; do not conflate physical and semantic focus.

Off-owner cell SetFocus is safely refused/blocked on both RIDs. No RangeValue
write, screen-reader speech, physical IME, repeated cross-RID reliability,
100 MiB memory teardown, or multi-monitor acceptance was exercised. The one-run
1000-query totals (278.3314 ms x64, 212.0145 ms ARM64) include different runners
and UIA overhead and are not an architecture comparison, startup/edit benchmark,
percentile, or regression conclusion. No product failure is demonstrated within
the strengthened bounded subset; the remaining release gates are unchanged.

## Final-source integration checkpoint — CI 36858899063

Date: 2026-10-01. Exact source `c85681628552ea73f84d3fa8b42a0baf5710627d`, [CI 36858899063](https://github.com/kleedaisuki/mote/actions/runs/36858899063). All ten jobs complete success, but the **unchanged pinned external AOT Grid acceptance remains product-fail on both Windows RIDs**. This checkpoint is not a historical external-focus fix.

The new **blocking owned-HWND pane/source-focus tests actually execute 2/2 on each RID**, zero failed/skipped; retained TRX lists `Pane_cycle_reads_back_focus_prunes_unavailable_and_preserves_source_text` and `Source_focus_survives_initial_install_reinstall_selection_and_provider_reads`. Logs show `dotnet test --arch x64/arm64`, corresponding test assembly paths `win-x64`/`win-arm64`, and native host Architecture x64/arm64/RID. These are managed test hosts invoking actual owned Windows HWND APIs, **not tests against the published AOT editor**. They establish the scoped source-first install/reinstall/provider-read and bidirectional pane contracts; no physical input or arbitrary external UIA interleaving follows.

Original external probes retain source pin `C6894B4EDCF931BAA322B878AF8965759AC7E44EFC10291200737A18C1AD5509` and CRLF project pin `6A99955812E8EA1631792E3EF0B47798B42A1A9F916FE86AF6D992F2E32FA3D4`, with actual process exits **1**, `timed_out=false`, report `Classification=product-fail` and identical error categories on both RIDs: four `synthetic owned F6 cycle target <HWND>` errors, two `coherent scroller focus publication`, and `off-owner cell Focus fails safely pending acceptance gate`. Exact tested binary SHA is **C1DBED9E1399E5515C490CC552DEB0462F8F0B331EC43ABB42E7BF3CF9FEBC06** (x64), **18CF78493D8C94D68BFB1E7112569810F693365383CDF89B4A8EC5DC346B9FD2** (ARM64), consistent between each report and inventory. Each of five physical `HasKeyboardFocus` values is false. x64 global semantic focus belongs to the target and is true; ARM64 all five semantic samples are foreign-blocked with nullable focus, never read as false. This difference is not a cause attribution.

The unchanged pinned reports do **not** serialize an initial-source-focus/thread-ID witness equivalent to the proposed discriminator. No initial Source→Table focus mapping or off-owner thread sequence is inferred from later F6 facts. Initial source-focus preservation is supported by the executed new owned-HWND test assertion, not an unrecorded external-report field. The existing `ExternalCellFocus` is explicitly `blocked: generated COM callback is off owner thread; no native focus attempt`; it does not provide the missing initial-install witness. Future sequencing investigation must preserve this external oracle and capture the actual missing boundary rather than relax the errors.

Evidence is retained under `.cache/ci-36858899063-grid-final/`: actual pane TRX at `artifacts/native-grid-pane-focus-{win-x64,win-arm64}/grid-pane-focus.trx`, original reports/inventories at `artifacts/windows-grid-accessibility-ci-{win-x64,win-arm64}/`, strict/AOT logs, four single-binary inventories and final summaries. Main managed suites on Windows/macOS both report **3132/3132**, Themes **14/14**, Configuration **9/9**, zero failed/skipped. Four delivery checks retain one executable per RID; both blocking Mac Flow steps actually succeed with posted-fault/local-monitor ABI/ready markers (ARM64 12:05:26 UTC, x64 12:06:10 UTC). Four ordinary JSON summaries report **8/8 pass with numeric editor/reopen exits 0/0**. Those are scoped integration observations; prior schema/chain/recovery audits are reused, not repeated. Mac Grid nested failures and Windows ARM inconclusive diagnostics remain visible and separate. No local native experiment, production/workflow edit or push occurred during this audit.
