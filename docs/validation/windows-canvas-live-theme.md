# Windows ordinary Canvas live-system-theme diagnostic

## Purpose and status

`tests/NativeWindowsCanvasThemeWorkflow.ps1` is a **new, test-only** external
Windows diagnostic, separate from `NativeThemeWorkflow-Windows.ps1`. The latter
now explicitly launches `--legacy-page`; its passing hosted result at run
[36794910486](https://github.com/kleedaisuki/mote/actions/runs/36794910486)
is not evidence for the ordinary Continuous Canvas route.

The new diagnostic launches the published executable with only its disposable
fixture filename. It does not select a legacy, experimental Canvas or test-only
product route. Its current status is **portable contracts passed; the corrected
owner reached both native targets, but the first dark-phase source-readiness
predicate failed on both RIDs; restoration and unchanged-file cleanup passed**.
No live palette or source-selection acceptance has yet been established. No
registry setting was changed on the local developer machine, and no product
implementation was modified to satisfy the checks.

## Acceptance basis

The user requires a live system theme strategy, unchanged source/editing state,
and default ordinary Canvas behavior. This diagnostic expects:

1. An exact launched-process `MoteNativeEditorWindow`, a direct owned
   `MoteInteractiveCanvas`, and its visible owned child ID 301 input island.
2. The Canvas HWND routes externally to a target-process UIA `Document` with
   automation ID `mote.source.document`; its bounded full source value is exactly
   the eleven UTF-16-unit synthetic fixture `alpha\nbeta\n`.
3. One source-provider selection attempt establishes absolute `[1,3)` (`lp`).
   Every theme phase must retain the same range endpoints, selected text and
   full source text. Native `EM_GETSEL` would only measure input-window-local
   state and is deliberately not used for this claim.
4. Dark -> light -> dark changes the Canvas source-body background and at least
   five first-row pixels near the expected ordinary foreground. Background is
   sampled at `(clientWidth-16,64)`, **above** the bottom input ribbon. A ribbon
   background must not substitute for the Canvas source background.
5. The target-only `WM_SETTINGCHANGE` notification uses `ImmersiveColorSet`.
   The exact theme fixture is disk-hash unchanged, the process closes normally,
   and registry state is verified exactly restored before a report can pass.

Expected builtin roles are derived from existing product theme contracts, not
from the observed raster: dark background `#1F2023`, foreground `#D8DADF`, panel
`#27292D`; light background `#FFFFFF`, foreground `#26282E`, panel `#F6F7F9`.
Canvas foreground/background have RGB-channel tolerance 24/20 respectively;
PrintWindow status/Win11 caption samples have tolerance 1. These are narrowly
scoped raster probes, not general screenshot or contrast acceptance.

## Safety and containment

- Mutation is rejected unless Windows, `GITHUB_ACTIONS=true`, `RUNNER_OS=Windows`
  and `RUNNER_ENVIRONMENT=github-hosted` all hold. Never execute the GUI workflow
  on a normal developer/user account by spoofing these guards.
- Only the disposable hosted runner's `HKCU/.../Themes/Personalize/AppsUseLightTheme`
  is touched. Existing key/value existence, registry kind, and raw unexpanded
  data are restored in `finally`; cleanup verifies structural equality. A newly
  created key is removed only if still empty. Unexpected unrelated data prevents
  deletion and produces failure.
- Notifications target only the owned editor HWND, not HWND_BROADCAST. Caption,
  children and source patterns are read only after exact process ownership.
  Cleanup closes only the owned editor; forced cleanup uses the launched Process
  object, never enumerated foreign processes.
- An independent owner process snapshots/mutates/restores HKCU and owns the exact
  launched editor Process. All potentially blocking UIA, GetPixel and PrintWindow
  work runs in a separate GUI-only worker (same account; not an OS privilege
  boundary). Each phase worker is waited for at most 30 seconds, killed on timeout,
  and given 3 seconds to terminate. The owner retains registry cleanup authority.
  Cleanup closes the exact editor with a 1-second bounded message, waits 3 seconds,
  and kills/waits another 3 seconds if necessary. An absent HWND never prevents
  exact-Process fallback. A worker timeout is failure, not a skipped success.
- Nested `finally` explicitly runs restoration even if process cleanup fails.
  File hashing, handle disposal and report writes occur only after restoration
  was already attempted; the outer owner retries/verifies restoration before
  those operations. Owner/runner death cannot execute cleanup; the hosted-only
  guard means this is a disposable-runner diagnostic, never a user-account tool.
- Fixture/config paths use nonce-specific repository `.temp`; worker logs,
  phase JSON and PNGs use repository `.cache`. Executable is restricted to the
  exact repository/RID Native AOT publish path and all artifact/executable
  ancestors reject reparse points. No arbitrary source/profile is accepted.
  Only the editor's `ProcessStartInfo.Environment` receives generated `MOTE_HOME`;
  the parent user's environment is never mutated.
- No global keyboard/mouse input, clipboard, IME selection, TCC or real user
  file/profile inspection occurs. PNGs contain only the owned synthetic window.

## Local validation

Command, run from `D:\Code\mote` with PowerShell 7:

```powershell
pwsh -NoProfile -File tests/Test-NativeWindowsCanvasThemeContract.ps1
```

Observed: exit 0, owner and GUI worker PowerShell AST valid; both embedded C#
declarations compile; role separation/ordinary-route/no-broadcast/hosted-restore
contracts pass. The old linear-finally structure is reproduced red: an injected
cleanup/hash exception prevents restoration. The new nested-finally owner passes
13 restoration/cleanup/fidelity contracts: body and cleanup exception combinations,
raw ExpandString/Binary/MultiString/QWord values, value absence, newly created key
removal/retry, refusal to remove unrelated data, corrupted restoration rejection,
and an actual sleeping child process killed by the same watchdog helper after a
150-ms test deadline, followed by fake-registry restoration. This child only sleeps;
it reads no source or profile and invokes no registry API. Seven independent
synthetic source-state cases cover exact success, wrong start, wrong end, wrong
selected value, changed full source, no selection and multiple selections. The
same copied production assertion accepts only the success case. These tests
invoke no Win32 DLL function, GUI workflow or registry API; only the sleeping-child
watchdog test launches a bounded subprocess.

This local check does **not** execute Windows UI Automation, validate the physical
Canvas raster, prove cleanup under a real target crash, or establish cross-RID
behavior. It is not a substitute for the following hosted observation.

## Hosted invocation and evidence

New **non-gating** CI step, separately from the legacy theme step, with an outer
step timeout (recommended 5 minutes, comfortably above three 30-second phase
deadlines plus cleanup; individual GUI polling loops are 15 seconds and native
target messages are 1 second):

```powershell
pwsh -NoProfile -File tests/NativeWindowsCanvasThemeWorkflow.ps1 `
  -ExecutablePath "src/Mote.Native/bin/Release/net10.0/$rid/publish/mote.exe" `
  -RuntimeIdentifier $rid
```

Supported RIDs: `win-x64`, `win-arm64`. Report:
`.cache/ci-inventory/<RID>/native-canvas-theme.json`; PNG directory:
`.cache/native-canvas-theme/<RID>/<nonce>/`. Retain owner report, all phase JSON
(including source HWND/Document launch observations), worker logs, PNGs and raw
step exit.
A `continue-on-error` green job/step conclusion is not a passing probe. Require
`status=passed`, three exact named cases, `registry_restored=true`,
`source_sha256_unchanged=true`, `normal_exit=true` and a zero raw exit. A missing
report or early preflight failure is unavailable evidence, not product success.
A timeout can leave an incomplete/missing worker JSON; the owner report must
retain the phase and non-success worker result while still restoring HKCU.

## Explicit limits

`source_version_status=unverified-no-public-external-version-contract` remains
true to its name: an exact unchanged disk/host/provider string does not expose
or prove the engine's immutable document version. No new production hook is
introduced for this diagnostic. A separate version-aware integration probe is
needed before making that stronger claim.

`draw_callback_status=not-observed`: this external probe does not instrument
DirectWrite/Direct2D callback completion. `GetDC/GetPixel` and `PrintWindow` sample
window/raster content; they are neither a callback endpoint measurement nor a
compositor/physical-display presentation or latency claim. Real IME candidate
placement, composition preservation, screen-reader behavior, full accessibility
acceptance and product-wide theme reload/edit/version guarantees remain separate.

## First hosted audit: run 36800944850

Run [36800944850](https://github.com/kleedaisuki/mote/actions/runs/36800944850),
head `99fbe39`, exercised the separate diagnostic on both Windows RIDs. The
reviewed-source pins passed, followed by the portable `13` restoration/fidelity
and `7` source-range contracts. Both outer Native AOT jobs had green conclusions,
and the non-gating diagnostic step conclusions were also masked `success`.
The actual nested command failed with **exit 1** on both RIDs:

| RID | Job ID | UTC error time | First failure | Evidence verdict |
| --- | --- | --- | --- | --- |
| win-x64 | 110174903104 | 2026-10-01 01:27:53.3733580 | `Cannot overwrite variable HOME because it is read-only or constant.` | harness setup failure; no Canvas evidence |
| win-arm64 | 110174903186 | 2026-10-01 01:27:30.9183472 | same error | harness setup failure; no Canvas evidence |

Raw job logs are preserved locally under repository `.cache`:
`canvas-theme-36800944850-win-x64.log` and
`canvas-theme-36800944850-win-arm64.log`. Reproduce retrieval after job completion:

```powershell
gh api repos/kleedaisuki/mote/actions/jobs/110174903104/logs `
  > .cache/canvas-theme-36800944850-win-x64.log
gh api repos/kleedaisuki/mote/actions/jobs/110174903186/logs `
  > .cache/canvas-theme-36800944850-win-arm64.log
```

Both artifact-upload steps explicitly warned `No files were found` for the owner
report/phase output paths. The run artifact inventory contained no
`native-canvas-theme-win-x64` or `native-canvas-theme-win-arm64`. There are **no**
owner reports, phase reports, PNGs or worker logs from which to infer colors,
source selection/version, source hash or an actual registry-restore result.

Mechanism: PowerShell variable names are case-insensitive. The owner assigned
`$home` during synthetic path setup, colliding with the built-in readonly `$HOME`.
This occurs before the fixture/config writes, source hash capture, registry
snapshot/mutation, editor launch and GUI worker. Only repository-local output
and scratch directories had been created. Static execution ordering therefore
establishes **no registry mutation or editor launch was reached**; it does not
manufacture a `registry_restored=true` report. This failure is neither a product
palette failure nor a blocked-foreground observation.

### Isolated correction and regression evidence

The owner now uses `$syntheticHome` consistently; no registry, product, theme,
GUI worker or launch-route behavior was changed. The portable contract adds:

- A case-insensitive AST rejection of writable `HOME`, `home` and scoped
  `script:Home` assignments.
- A real local red reproduction of the old `$home` assignment, which throws
  the readonly-HOME error without touching the environment or filesystem.
- Execution of only the actual corrected `Join-Path` setup assignment, proving
  the generated home equals `<synthetic scratch>/home` without running fixture,
  registry, editor or GUI code.

The fresh local command again exits **0**, retaining all `13 + 7` prior contracts
and adding the readonly-HOME red/green setup checks. This closes the specific
harness setup defect, not the hosted Canvas theme acceptance. A subsequent run
must still supply all three exact phase reports/rasters, source UIA assertions,
normal exit, unchanged input hash and verified registry restoration.

## Corrected hosted audit: run 36802378381

Run [36802378381](https://github.com/kleedaisuki/mote/actions/runs/36802378381),
head `c453506`, passed the reviewed harness byte pins and the portable
readonly-HOME red/green setup, `13` restoration/fidelity/watchdog and `7`
source-range contracts on both RIDs. The old setup failure did not recur.
Nevertheless, the nested native diagnostic and its first GUI worker both
returned **exit 1**. Green Native AOT/non-gating step conclusions do not override
these observed failures.

| RID | Job ID | Owner/worker status | Owner cleanup | Artifact ID |
| --- | --- | --- | --- | --- |
| win-x64 | 110179342793 | failed / dark-before / exit 1 | registry restored, input hash unchanged, normal exit all true | 11136462885 |
| win-arm64 | 110179342824 | failed / dark-before / exit 1 | same three checks true | 11135443306 |

Published executable SHA-256:

- win-x64: `526C45D6F6DF34861F4FE0B86D3C27D30C5D37BD8D7AC68D22FF25FB51408B15`.
- win-arm64: `1D1050838A13150671DD71C41095E55BD6FC37E99BA9A9DC0AE3EB9EC1E40EF7`.

Unlike the previous run, both artifacts contain an owner report, one
`dark-before.json`, its stderr and an empty stdout file. Both phase reports have
identical SHA-256
`C5130410032B003BB00C042AEEE0B99777B6FF1ECE913778697A5749653AAB37`,
`launch_observation=null`, `cases=[]`, and the precise first failure:

> Ordinary Canvas did not expose target-owned source Document and visible input island.

Raw command exit-1 records occur at `2026-10-01T01:45:44.9046983Z` (x64) and
`2026-10-01T01:45:37.0650339Z` (ARM64). Neither worker timed out at its independent
30-second owner deadline: `completed=true`, `exit_code=1` indicates a worker that
returned its own failed source-readiness polling result. Neither artifact has a
PNG, light phase, or dark-after phase.

### What is and is not established

The worker's control-flow guards found and owner-verified the launched editor,
direct Canvas, input child 301 and status child before entering the failing
source-readiness loop. This is bounded native target reachability, not the full
source contract. The failed loop conjunctively requires process/automation-ID/
Document control-type identity, exact bounded `alpha LF beta LF` source text,
and visible input/Canvas HWNDs. Its current report does not retain each conjunct's
last observed value. Consequently, this evidence **does not identify** which
condition failed, nor distinguish a provider contract defect from an observer
assumption such as text representation. It does not contain a foreground/occlusion
check, so it cannot be classified as a proven foreground blockage either.

The failure occurs **before** `sourceRange.Select()`, source-state acceptance,
target `WM_SETTINGCHANGE`, raster color sampling, and PrintWindow. Thus there is:

- no verified global `[1,3)` selection or theme-preserved source-provider text;
- no editing/undo/history acceptance (those operations are not part of this
  diagnostic even on a pass);
- no accepted dark palette, no dark -> light -> dark transition, and no observed
  product color failure;
- no draw-callback, compositor/physical-display or real IME evidence;
- no immutable engine-version evidence, as already declared by the reports.

The owner reports **do** establish their narrowly scoped cleanup checks on both
real native targets: original Personalize key and AppsUseLightTheme value existed
with kind DWord, exact raw kind/data restoration verification succeeded,
`source_sha256_unchanged=true`, and clean owned-editor shutdown returned
`normal_exit=true`. These are not merely inferred from a green job and must not
be extended into unexecuted theme/editing assertions.

### Reproducibility and next discriminating observation

All downloaded audit files stay under repository
`.cache/canvas-theme-36802378381/`, with raw logs `win-x64.log` and `win-arm64.log`
and separately named artifact extraction directories. Commands:

```powershell
gh run download 36802378381 --name native-canvas-theme-win-x64 `
  --dir .cache/canvas-theme-36802378381/win-x64
gh run download 36802378381 --name native-canvas-theme-win-arm64 `
  --dir .cache/canvas-theme-36802378381/win-arm64
gh api repos/kleedaisuki/mote/actions/jobs/110179342793/logs `
  > .cache/canvas-theme-36802378381/win-x64.log
gh api repos/kleedaisuki/mote/actions/jobs/110179342824/logs `
  > .cache/canvas-theme-36802378381/win-arm64.log
```

When inspecting inventory use pagination, e.g.
`gh api --paginate 'repos/kleedaisuki/mote/actions/runs/36802378381/artifacts?per_page=100'`;
the default first page did not include the ARM64 theme artifact. Absence from a
partial inventory is not artifact absence.

The smallest informative next diagnostic is content-free last-observation
metadata for each source-readiness conjunct (owner PID match, automation ID,
control type, exact synthetic newline-variant booleans/length, Canvas/input
visibility). Keep the acceptance predicate unchanged until the metadata
identifies whether its expectation or actual product contract is wrong; do not
select the hidden legacy control or weaken the source requirement to make the
palette check run. No product, harness or CI code was changed by this audit.

## Content-free readiness discriminator (awaiting hosted observation)

The next worker revision adds `readiness_observation` without weakening any
acceptance criterion or changing the owner/registry implementation. Each polling
attempt gets a fresh bounded metadata record; the final observed attempt remains
in the phase JSON even if the compound predicate times out. No retry history or
source/unknown identity string is accumulated.

| Metadata | Meaning and scope |
| --- | --- |
| `attempt_count`, `elapsed_ms` | polling count and time sampled at attempt start, not render latency |
| `canvas_initial_owner_verified`, `input_initial_owner_verified` | original target-owner guards passed before polling; **not current per-attempt ownership claims** |
| `canvas_visible`, `input_visible` | native visibility read in this attempt; does not prove foreground, occlusion or physical presentation |
| `provider_process_matches_target` | exact expected-process match; no foreign process identifier is serialized |
| `automation_id_class` | only `not-read`, `expected-source`, `empty`, `other`; never an arbitrary ID |
| `control_type`, `control_type_is_document` | standard UIA ControlType name/classification and the existing Document match |
| `text_pattern_available` | true when the existing GetCurrentPattern call succeeds, false on its exception, null if not read |
| `bounded_text_utf16_units` | length of one existing `GetText(256)` result, not the text |
| `exact_synthetic_lf`, `exact_synthetic_crlf`, `exact_synthetic_cr` | exact fixture representation comparisons, not a relaxed acceptance rule |
| `error_stage`, `error_hresult` | enumerated API-read stage and base exception HRESULT; provider exception messages are not serialized |

Provider identity, pattern and document-value reads remain gated by matching the
expected process. A wrong automation ID or non-Document type still prevents
pattern/text reads. **Only the exact LF fixture is accepted**, with the original
source identity, Document type and visible input/Canvas requirements. CRLF/CR
booleans discriminate representation; they do not make those variants pass.

Native visibility reads add facts before the short-circuited identity/text gates,
without input or mutation. Initial-owner naming was chosen after independent
review: the original owner checks run before polling, so assigning an unqualified
`owned_by_target=true` on every attempt would overstate current HWND ownership.
The new metadata does not imply a recheck, preserve an obsolete source value, or
promote a prior observation to a new attempt.

API exceptions still fail immediately rather than being retried. The error
record retains only safe stage/HRESULT; the worker's outer report suppresses the
potentially arbitrary provider exception message. Existing 15-second polling,
50-ms interval, independent 30-second GUI-worker deadline, hosted-only guards,
exact registry restoration, and absence of selection/theme notification before
source readiness remain unchanged. No product or CI code is modified.

The fresh portable command exits **0** with all previous readonly-HOME and
`13 + 7` contracts plus **11** readiness cases: exact success, foreign PID,
unknown/empty ID, wrong control type, CRLF, CR, wrong source text, invisible Canvas,
invisible input, and unavailable TextPattern. Tests serialize each observation
and reject raw synthetic/private source, foreign/unknown ID and exception-message
sentinels; they verify correct length/newline booleans, no early pattern reads,
structured error provenance, initial-only ownership names, fresh-attempt unknown
fields, unchanged polling deadline and readiness-before-input/theme ordering.
These are synthetic observer contracts, not newly observed native product results.
