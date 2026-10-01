# Windows UIA apartment experiment

## Question and safety boundary

Can the ordinary Windows Native AOT owner thread acquire COM STA before creating
HWNDs and source-generated accessibility providers, without relaxing canonical
selection owner-thread checks? This experiment does not prove external UIA range
selection, and does not alter providers' options or marshal UI work with a
blocking posted callback.

`WindowsUiaApartment` requests `COINIT_APARTMENTTHREADED`, records both the
initialization HRESULT and `CoGetApartmentType` before providers exist, and owns
exactly one `CoUninitialize` obligation only for `S_OK` or `S_FALSE`. Disposal is
idempotent and successful scopes must end on their owner thread. `Run` holds the
scope until its native message loop and normal provider cleanup end, including
exceptional exits. An incompatible existing apartment is preserved, launch
continues with explicit stderr diagnostic, and selection guards remain unchanged.

## Actual Native AOT discrimination (2026-10-01)

Environment: local Windows x64, .NET 10.0.11. Standalone console project under
`.cache/uia-sta-affinity-probe/`, referencing the real `Mote.Native.csproj` and
using assembly name `Mote.Tests` (the existing friend assembly). It creates the
same generated editor, fragment root, document child, and range classes, without
an HWND, focus changes, UIA client, or manual thread/apartment manipulation.

| AOT entry | STA request | Recorded apartment | IAgileObject / IMarshal |
|---|---|---|---|
| Default `Main` | `0x80010106` RPC_E_CHANGED_MODE | 1 (MTA), qualifier 0 | Both `0x80004002` E_NOINTERFACE, all four classes |
| `[STAThread] Main` | `0x00000001` S_FALSE | 3 (MAINSTA), qualifier 0 | Both `0x80004002` E_NOINTERFACE, all four classes |

The default entry's **pre-initialization** query returned success, type 1,
qualifier 0. The annotated entry's pre-initialization query returned success,
type 3, qualifier 0. Consequently, adding `CoInitializeEx` inside
`WindowsEditorShell.Run` alone is demonstrably insufficient: the Native AOT
runtime has already selected MTA for the default entry. `[STAThread]` selects
MAINSTA before managed Main; the new scope then acquires a balanced nested
reference. No production `Program` edit belongs to this experiment's ownership.

Absence of `IAgileObject` and `IMarshal` is direct wrapper-interface evidence,
not a proof of callback affinity through UIAutomationCore. The next discriminating
test is the real external AOT range workflow with annotated entry and
`ProviderOptions_UseComThreading` where appropriate, retaining wrong-thread
rejection. Do not claim Select works from these interface observations alone.

## Isolated product acceptance binary

An untracked staged copy under `.cache/uia-sta-product-probe/src/Mote.Native`
was published successfully for `win-x64`, excluding original `bin`/`obj` trees.
References to engine/format/configuration/theme/telemetry projects point to the
original project files. The only staged experiment changes beyond the Run/helper
work are `[STAThread]` on Main and `| 32` in the editor, fragment root, and
document child's GetProviderOptions. Original production Main and provider
options remain unchanged until external acceptance supports them.

Binary: `.cache/uia-sta-product-probe/publish/mote.exe`, SHA256
`FBD0DCB2FA63724D611AD04623786C6878E6FE2EFCD864F0AE78BC88CE268F85`.
The manifest at `.cache/uia-sta-product-probe/manifest.json` records original
source hashes, experiment changes, and HEAD `ef91b4a9ef4c6526d6cc76ca59501ec236c0afb7`.
External range Select acceptance is owned by the parent validator; this document
does not treat a successful publish as a UIA selection result.

## Reproduction and focused tests

Commands:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release `
  --filter FullyQualifiedName~WindowsUiaApartmentTests `
  --logger 'console;verbosity=detailed' --no-restore
dotnet publish .cache/uia-sta-affinity-probe/Probe.csproj -c Release -r win-x64 `
  -o .cache/uia-sta-affinity-probe/publish-default
# Repeat after annotating only the harness Main with [STAThread].
```

Focused tests passed **3/3**, with no new analyzer warnings: nested real STA
initialization (`S_FALSE`) remains valid after inner double-disposal; real MTA
conflict preserves its apartment after failed scope disposal; generated editor
wrapper QI reports both interfaces absent. Tests use dedicated threads rather
than mutate the runner's apartment. An initial default-Thread probe failed STA
initialization because CoreCLR had already chosen MTA; explicitly choosing STA
before the test thread starts is required and is not evidence about default Main.

## Primary sources

- [CoInitializeEx contract](https://learn.microsoft.com/en-us/windows/win32/api/combaseapi/nf-combaseapi-coinitializeex): every successful call, including S_FALSE, must be balanced; changed mode is not successful.
- [ProviderOptions contract](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-provideroptions): UseComThreading requests COM-model callbacks; STA callbacks should return to their own STA, whereas MTA callbacks can occur on another MTA thread.
- [UIA client threading](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading): UIA client work belongs on a separate non-window-owning MTA thread, not the product's owner GUI thread.

The known issue concerns production COM/OS contracts, not a research hypothesis
requiring a new concurrency algorithm. No speculative dispatcher, owner-thread
identity spoof, or hidden thread-safety bypass is introduced.
