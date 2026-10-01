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
