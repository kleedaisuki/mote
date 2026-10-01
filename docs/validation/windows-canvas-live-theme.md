# Windows ordinary Canvas live-system-theme diagnostic

## Purpose and status

`tests/NativeWindowsCanvasThemeWorkflow.ps1` is a **new, test-only** external
Windows diagnostic, separate from `NativeThemeWorkflow-Windows.ps1`. The latter
now explicitly launches `--legacy-page`; its passing hosted result at run
[36794910486](https://github.com/kleedaisuki/mote/actions/runs/36794910486)
is not evidence for the ordinary Continuous Canvas route.

The new diagnostic launches the published executable with only its disposable
fixture filename. It does not select a legacy, experimental Canvas or test-only
product route. Its current status is **portable contracts passed; actual hosted
GUI execution pending**. No registry setting was changed on the local developer
machine, and no product implementation was modified to satisfy the checks.

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
