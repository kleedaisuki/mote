# External Windows Native AOT UIA range validation

## Contract and method

`tests/WindowsUiaRangeExternal.ps1` launches the ordinary product route with a
synthetic `abcdef\nsecond line\n` file and isolated `MOTE_HOME`, entirely under
repository `.temp/`. It generates a managed .NET 10 WPF UI Automation client in
repository `.cache/`. The client independently discovers only the launched PID's
windows, requires exactly one `mote.source.document` and Document control type,
and exercises the actual Native AOT COM boundary. No theme workflow, registry,
global keyboard, clipboard, or foreground/focus mutation is involved.

Expected range behavior is derived from the fixture: clone DocumentRange, collapse
End to Start, move End three Characters and Start one Character. Endpoint
comparisons against the unchanged original document Start must be exactly 1 and
3; selected text must be `bc`. Select must publish that global selection without
changing the source or original clone. Further range mutation must not mutate the
selection clone. Target-owned WM_CLOSE must exit normally, and the held range
must reject access after close with ElementNotAvailable.

The owner process independently limits the UIA client to 60 seconds (configurable
1–120), retaining stage reports before potentially blocking calls. Cleanup closes
only the process it launched; any forced kill is explicitly recorded. The script
returns the client exit code and never turns a Select failure into a pass.

## Reproduction and observed result (2026-10-01)

```powershell
pwsh -NoProfile -File tests/WindowsUiaRangeExternal.ps1
```

Environment: Windows x64, PowerShell 7, .NET 10 external managed STA client;
fresh parent-published `.cache/uia-range-aot/mote.exe`, 7,122,432 bytes,
SHA-256 `A23E12F28BB977A5EA045BC9F5580E43A350B6A73EB4C7D17AB2A55846BD4C92`.
Preserved baseline artifact: `.cache/windows-uia-range-external/baseline-report.json`.

Three attempts, including the final explicit Document-control-type check, consistently
reached Select with endpoint distances **1/3** and text **bc**. Both calls threw
`System.InvalidOperationException`, HRESULT **0x80131509**, from the external
UIA marshaling boundary. Client managed thread ID was 2, apartment STA. The
target's controller thread identity was not observed: this HRESULT is consistent
with the known WrongThread mapping but does not by itself prove which internal
rejection branch fired. It is a genuine external Select integration failure,
not a missing UIA dependency or a fixture expectation mismatch.

The target then closed normally with exit 0, **no forced cleanup**; the fixture
file remained byte-equivalent UTF-8 source. Thus exact endpoint navigation and
clone-read behavior before Select are verified, but global GetSelection after
Select, further clone independence, and stale-range-after-close are **not
verified**, because the harness stops on the first consequential failure. The
full external workflow remains **FAIL**, independently of managed unit tests.

## Next discriminating check

The provider owner should locate the actual Select callback thread and rejection
outcome without weakening thread/lifetime guarantees. After a causal fix,
republish the same ordinary Native AOT binary and rerun this exact harness; a
green run must include all downstream assertions, not merely a successful Select
HRESULT. No production files were changed by this validation assignment.

## Isolated STA/provider-options experiment

The parent supplied a staged product build, without changing the production
entry point, adding `[STAThread]` to staged Main and OR-ing provider options with
32, alongside the reviewed COM scope. Its source hashes and modifications are
recorded in `.cache/uia-sta-product-probe/manifest.json` (commit context
`ef91b4a9ef4c6526d6cc76ca59501ec236c0afb7`).

```powershell
pwsh -NoProfile -File tests/WindowsUiaRangeExternal.ps1 `
    -ExecutablePath .cache/uia-sta-product-probe/publish/mote.exe
```

Staged binary SHA-256:
`FBD0DCB2FA63724D611AD04623786C6878E6FE2EFCD864F0AE78BC88CE268F85`.
The command returned **0** and the full external workflow **passed**:

- Select returned HRESULT `0x00000000`.
- Global GetSelection returned exactly `bc` at offsets 1/3.
- Original DocumentRange and selected-range clone remained independent of the
  subsequently extended working range; document text remained unchanged.
- Target-owned WM_CLOSE completed with exit 0, no forced cleanup.
- The held range rejected GetText after normal close with
  ElementNotAvailable HRESULT `0x80040201`.
- Fixture source remained unchanged; the client was still external managed STA
  thread 2. No thread-safety or lifetime guard was bypassed.

The first staged attempt had already passed Select and selection assertions,
but exposed a harness lifecycle defect: `Process.GetProcessById` had not acquired
its native process handle before exit, so .NET could not provide ExitCode after
PID retirement. The harness now acquires `target.Handle` before WM_CLOSE;
the following attempt passed all assertions. This is a harness correction, not a
relaxed product requirement. The current report is
`.cache/windows-uia-range-external/report.json`; the failing original-binary
report remains separately preserved.

This is evidence for the **combined staged changes**, not proof that any single
change alone suffices, and not yet verification of the final production build.
Republish and rerun after the parent integrates the causal fix.

## Integrated original-source Native AOT retest

The parent then published the authorized original-source entry-point STA and
three source-provider option changes, with the reviewed Run COM scope, to
`.cache/uia-range-aot/mote.exe`. This is not the staged-copy executable.
Its SHA-256 is
`20B5BD467899EB7618AF6737DCD10E875F90FA5B1E71A5B384A4F1052B9D8CED`.
The staged report was preserved separately as
`.cache/windows-uia-range-external/staged-report.json` before the retest.

```powershell
pwsh -NoProfile -File tests/WindowsUiaRangeExternal.ps1 `
    -ExecutablePath .cache/uia-range-aot/mote.exe
pwsh -NoProfile -File tests/NativeWindowsContinuousWorkflow.ps1 `
    -ExecutablePath .cache/uia-range-aot/mote.exe `
    -ReportPath .cache/ci-inventory/uia-sta-continuous-local.json
```

Both commands returned **0**. The original-source external range test passed
all the same assertions: unique source Document, exact endpoint distances 1/3,
Select HRESULT 0, global selection `bc`, independent original and selection
clones, normal target exit 0, stale HRESULT `0x80040201`, no forced cleanup,
and unchanged source file. Its report remains
`.cache/windows-uia-range-external/report.json`.

The existing ordinary Continuous workflow separately passed actual GUI open,
target-owned WM_CHAR edit and WM_COMMAND Save, exact BOMless UTF-8 byte oracle,
normal close, actual GUI reopen, unchanged saved bytes, and second normal close.
Fixture size was 81,941 source UTF-16 characters, 81,942 after editing; the bounded
native input island was 5 characters initially and 6 after reopen. Both process
exits were checked against 0 by the workflow. No registry, theme, global keyboard,
clipboard, or explicit focus mutation was performed by the validation assignment.

Coverage remains local Windows x64. This does not verify Windows ARM64, physical
IME input, screen-reader speech, compositor presentation, or unrelated format
workflows. The previously observed external Select failure is now closed for
this independently tested original-source Native AOT build.


## Affected managed regression suite

The implementation owner ran:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj --no-restore --filter 'FullyQualifiedName~WindowsUia|FullyQualifiedName~AccessibleSelectionController|FullyQualifiedName~NativeControllerTests|FullyQualifiedName~CanvasInputWindowTests|FullyQualifiedName~CanvasInteractionTests' --verbosity quiet
```

Result: **225 passed, 0 failed**, 42 seconds. This covers source COM cores,
canonical selection, controller editing/composition-compatible transactions,
bounded native input window selection, and Canvas interaction. Independent final
range/declaration/option tests additionally pass **38/38**, 534 ms. These managed
checks complement, not replace, the exact original-source Native AOT workflow
above. Physical IME candidate/composition testing remains a separate gate.

## Hosted Windows x64 / ARM64 audit: CI 36809964231

Independent raw-artifact and job-log audit of
[CI 36809964231](https://github.com/kleedaisuki/mote/actions/runs/36809964231),
commit `8d5796542b63e54e630933de38a2c68d864ca085`, completed 2026-10-01.
The non-gating diagnostic steps use `continue-on-error`; their green metadata is
**not** the acceptance criterion. Reports, pinned client assertions, nested theme
workers, and actual error/exit log intervals were inspected separately.

| Observation | win-x64 | win-arm64 |
| --- | --- | --- |
| Source-range step 47 actual exit | 0 | 0 |
| Range report stage / passed | `complete` / true | `complete` / true |
| Select HRESULT / exact source offsets | `0x00000000` / `[1,3)` | `0x00000000` / `[1,3)` |
| Normal close / forced cleanup | exit 0 / false | exit 0 / false |
| Retained range after close | `0x80040201` | `0x80040201` |
| Fixture source unchanged | true | true |
| Ordinary source UIA step 45 actual exit | 0 | **1: foreground focus inconclusive** |
| Ordinary source semantic/tree checks | 19/19; Raw/Control/Content Documents 1/1/1 | 19/19; Raw/Control/Content Documents 1/1/1 |
| Unchanged Canvas theme step 27 actual exit | 0 | 0 |
| Theme owner and three nested workers | passed; all workers exit 0 | passed; all workers exit 0 |
| Theme selection over dark/light/dark | `[1,3)` in all three phases | `[1,3)` in all three phases |
| Theme cleanup and source | registry restored; normal exit; source hash unchanged | registry restored; normal exit; source hash unchanged |

### Exact range evidence and scope

Artifacts `windows-source-range-win-x64` (ID `11138912717`) and
`windows-source-range-win-arm64` (ID `11139626446`) both report client STA thread 2,
Select success, exact endpoint distances 1 and 3, complete stage, normal target
exit, stale-range failure, no forced cleanup, and unchanged synthetic source.
They exercise the ordinary `mote <fixture>` route, not a special canvas flag.
Target discovery is scoped to the process launched by the owner, with exactly
one source Document. The reviewed client contains no global keyboard/clipboard,
registry, input-source, or foreground/focus mutation.

The compact report does not separately serialize every successful assertion.
In the pinned client at this commit, `passed=true` / `stage=complete` is reached
**only after** global GetSelection returns one `bc` range at `[1,3)`, the original
DocumentRange remains unchanged, later working-range mutation leaves the original
and selection clone independent, normal target close completes, and the retained
range raises ElementNotAvailable. Those results are established by the completed
assertion path, not invented report fields. The wrapper verifies source unchanged
and records actual forced-cleanup state after the client exits. The workflow
throws on nonzero child exit; both source-range step intervals complete without
that error or a nonzero process-exit line.

### Binary and architecture provenance

| RID | Exact binary SHA-256 | Strict publish payload |
| --- | --- | --- |
| win-x64 | `663311F0AA475C6913A250510967027FB38D20799BDADDE1339150681F2ED971` | `mote.exe`, 7,130,112 bytes; no other payload/native library |
| win-arm64 | `34D34F55F41385B37D1D1038D8085E45A7E919AC172AEA8F61745695B57EE732` | `mote.exe`, 7,272,448 bytes; no other payload/native library |

For each RID, the binary hash agrees **across the independent range, ordinary
source-tree, and theme-owner reports**. Job logs show checkout of the exact commit
above and `dotnet publish ... --runtime <RID> --self-contained true
-p:PublishAot=true`; the range invocation points to that RID's `publish/mote.exe`,
not the staged experiment or a local binary. ARM used hosted image
`windows-11-vs2026-arm64`, version `20260920.164.1`; its separate source-tree client
reports `ClientArchitecture=Arm64`, `.NET 10.0.12`, Windows `10.0.26200`. The x64
client reports X64, the same runtime, Windows `10.0.26100`. Strict inventory and
system-DLL import artifacts agree with the respective RID and have no unexpected
imports. These are build/runner/client/hash provenance checks; the downloaded
reports do not contain a separate PE machine-header inspection, and the range
client schema itself has no architecture field.

### Adjacent UIA and theme findings: do not erase the remaining focus gate

Ordinary source-tree reports have no `Failures` or `ReleaseBlockers`, pass all
19 source/tree/lifetime checks, reject old ranges after New and close, and record
native exit 0 on both RIDs. x64 foreground/focus is target-consistent. ARM retains
`focus-inconclusive-external-foreground`: a foreign process owned foreground
through the observation interval; the source/host focus properties were false
and foreign focused identity was deliberately not inspected. Its diagnostic
really exits **1**, recorded at `2026-10-01T03:22:40.2172770Z`, despite successful
step/job metadata. Therefore ARM target-owned selection is proved by the separate
range workflow, but ARM global foreground/focus consistency is **not** accepted
or reclassified as a product defect from this run.

Both unchanged theme owners and all six nested phase workers pass. Each phase
verifies target ownership of canvas/input/provider, exactly one source candidate,
exact synthetic LF source, and selection `[1,3)`. Source background is
`#1F2023 -> #FFFFFF -> #1F2023`; foreground sample counts are 101 -> 125 -> 101.
All six downloaded PNG SHA-256 values match their recorded phase hashes. Owners
report `registry_restored=true`, `source_sha256_unchanged=true`, and normal exit.
They explicitly retain `source_version_status=unverified-no-public-external-version-contract`,
`draw_callback_status=not-observed`, and `physical_presentation_status=not-tested`.
Thus this closes the previous E_NOTIMPL selection obstacle and proves the bounded
source UIA/raster theme pilot on both hosted Windows RIDs, not immutable-engine
version, physical compositor presentation, real IME, or screen-reader speech.

### Reproduction and retained audit artifacts

Raw downloads, full logs, job/artifact metadata, and an executable consistency
check are retained under `.cache/uia-hosted-36809964231/`:

```powershell
gh run download 36809964231 -D .cache/uia-hosted-36809964231/artifacts `
  -n windows-source-range-win-x64 -n windows-source-range-win-arm64 `
  -n windows-ax-product-win-x64 -n windows-ax-product-win-arm64 `
  -n native-canvas-theme-win-x64 -n native-canvas-theme-win-arm64 `
  -n native-inventory-win-x64 -n native-inventory-win-arm64
gh api repos/kleedaisuki/mote/actions/jobs/110202605779/logs
gh api repos/kleedaisuki/mote/actions/jobs/110202605878/logs
python .cache/uia-hosted-36809964231/audit.py
```

The audit checks ranges, 19/19 tree assertions and 1/1/1 counts, cross-report binary
hash equality, all six nested theme workers and PNG hashes, and actual step-log
error intervals. Its summary is `audit-summary.json`. GitHub step timestamp
metadata is second-granularity; the audit includes the full completion second,
so the ARM focus-step failure at fractional second `.2172770` is not accidentally
excluded. No product/CI code was changed or validation workflow rerun by this audit.
