# External macOS CSV Grid AX client

Run on matching macOS x64/ARM64 with PowerShell 7 and Xcode Command Line Tools:

```powershell
& ./tests/MacGridAxExternalProbe/Run.ps1 `
  -ExecutablePath src/Mote.Native/bin/Release/net10.0/osx-arm64/publish/mote `
  -RuntimeIdentifier osx-arm64 `
  -ReportPath .cache/ci-inventory/osx-arm64/mac-grid-ax-external.json
```

This uses the ordinary editor file route with **opt-in Grid AX registration**, a
synthetic isolated CSV and a separately compiled Swift client. The helper is test
only; the product inventory must still contain exactly one native executable.
No global keys, clipboard/source edits, input-source changes, system-wide AX tree,
TCC prompts/grants or VoiceOver commands are used. All artifacts stay in repository
`.temp`/`.cache`. Missing Accessibility trust is unavailable, not a passing test.

Portable fixture/path preflight (not native evidence):

```powershell
pwsh -NoProfile -File tests/MacGridAxExternalProbe/Test-Fixture.ps1
```

See [contract, bounded gates, schema and evidence](../../docs/validation/mac-grid-external-ax.md).
Swift typecheck and both native targets remain required; Windows fixture checks do
not compile ApplicationServices. A failed AXShowMenu route is retained, not bypassed
by synthetic key injection. Source/selection/navigational API success does not
certify VoiceOver speech, geometry, real IME or release readiness.
