# Windows UIA STA initialization review

## Scope and decision

Independent bounded review of pending `WindowsUiaApartment.cs`, its scoped use in
`WindowsEditorShell.Run`, `WindowsUiaApartmentTests.cs`, and
`tests/WindowsUiaRangeExternal.ps1`, on Windows / .NET SDK 10.0.400,
2026-10-01. **No substantive defect was established in this change.** This is a
review of safe apartment initialization and diagnostic scope, not a declaration
that external Select now works or that Native AOT wrappers are non-agile.
The reviewer changed no production files or shared tests.

## COM lifetime and compatibility

- Only S_OK and S_FALSE acquire an initialization obligation. Dispose balances
  that obligation exactly once on the owning managed thread. Wrong-thread disposal
  throws without clearing ownership, allowing later correct-thread disposal.
- RPC_E_CHANGED_MODE does not acquire ownership and is never balanced by this
  scope. The shell logs initialization/apartment status but continues launch with
  the host's existing apartment and unchanged owner-thread selection checks.
- The scope starts before HWND/provider creation. In the ordinary successful Run
  path the native message loop exits, canvas input/providers are disposed, and then
  the using scope ends. The using scope also balances initialization if later Run
  setup or a callback throws; this review does not independently certify the
  shell's pre-existing exceptional native-window teardown behavior.
- CoGetApartmentType is observational. Query failure produces explicit sentinel
  values; it does not change initialization ownership.
- The signatures use Windows LibraryImport with the correct scalar native argument
  shapes. Both successful results must be balanced, including nested S_FALSE.

These choices match the official [CoInitializeEx contract](https://learn.microsoft.com/en-us/windows/win32/api/combaseapi/nf-combaseapi-coinitializeex).

## Generated-wrapper limitation

STA initialization is not evidence that source-generated COM callable wrappers
marshal calls back to the HWND-owning thread. The test queries IAgileObject and
IMarshal and records their actual availability; its assertion intentionally accepts
both interface support and E_NOINTERFACE. Consequently a passing test is not a
non-agility assertion. Likewise CLR test-host behavior is not an external Native
AOT result. The existing controller WrongThread rejection remains necessary.
Native AOT external Select must pass independently before this experiment can be
reported as fixing owner-thread dispatch.

## External diagnostic safety

- The script creates fixture/config/client/report artifacts only under repository
  `.temp/windows-uia-range-external` and `.cache/windows-uia-range-external`.
  MOTE_HOME is overridden for the launched synthetic target process.
- It launches one explicit executable and retains that Process object. The client
  searches top-level windows by its target PID, then the exact source AutomationId;
  source duplicates fail explicitly. It does not read document contents from
  unrelated desktop windows.
- Selection and endpoint calls operate on the target's source TextPattern only.
  Normal close posts WM_CLOSE to the target process's main window. Finally cleanup
  acts only on the retained launched target/client process objects.
- No registry mutation, global keyboard/mouse injection, foreground acquisition,
  or accessibility SetFocus is present. The application's existing normal launch
  focus behavior is unchanged by the diagnostic.
- Potentially blocking UIA client work is isolated in a separately killable process
  with a 1–120 second script timeout. Target cleanup distinguishes forced kill from
  normal exit. Partial reports are written before Select/close/stale-range calls,
  and a previous report is deleted before a new attempt.
- The client demands exact source text and endpoint distances, canonical selection
  roundtrip, independence of document/selection clones, normal exit, and closed
  target range invalidation. It fails on Select HRESULT errors rather than turning
  a partial endpoint-only success into overall acceptance.

Fixed scratch paths imply this diagnostic is intended for serialized invocation;
this review does not certify concurrent independent invocations in the same checkout.

## Verification and limits

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj --no-restore --filter FullyQualifiedName~WindowsUiaApartmentTests --logger 'console;verbosity=normal'
```

Independent local result: **3 passed, 0 failed**, approximately one second total.
Cases exercise nested STA/idempotent disposal, preservation of a host-selected MTA,
and observation/reference balancing of generated-wrapper marshaling interfaces.
Tests use disposable dedicated threads, not apartment changes to the runner thread.

No external script or full native application session was rerun by this reviewer:
the parent/experiment owner owns those potentially GUI-sensitive checks. No claim
is made of Windows ARM64, a Native AOT interface agility result, successful external
Select, every initialization error mode, or complete screen-reader support.
