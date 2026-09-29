# Published-binary macOS AX contract probe

`Run.ps1` compiles `Probe.swift` with the host Xcode toolchain and starts the
**published** `mote` executable as a separate process in opt-in canvas mode.
The Swift client connects only through `AXUIElementCreateApplication(pid)` and
macOS Accessibility APIs. It does not call mote internals or use an in-process
selector hook. The deterministic fixture and compiler binary stay under the
repository's `.temp/mac-ax-external/`; the JSON report stays under
`.cache/ci-inventory/`.

Run on macOS with PowerShell 7 and Xcode Command Line Tools:

```powershell
pwsh -NoProfile -File tests/MacAxExternalProbe/Run.ps1 `
  -ExecutablePath src/Mote.Native/bin/Release/net10.0/osx-arm64/publish/mote `
  -ReportPath .cache/ci-inventory/osx-arm64/mac-ax-external.json
```

Use the matching `osx-x64` path for an Intel runner. The hosted job should
initially set `continue-on-error: true` and upload the JSON even on failure.
The script throws on a product assertion or harness failure, but treats a
confirmed `AXIsProcessTrusted == false` as
`external-accessibility-unavailable`: a TCC/runner limitation, not a product
pass. The raw `AXWindows` error is retained in the Swift report note.

The fixed fixture contains CR, LF, CRLF, supplementary emoji, 80 initial
lines, a 70,000 UTF-16-code-unit line, and a distant tail marker. The external
client verifies: exact editor PID and AX parent chain; one labeled, logical
`AXTextArea` rather than a second input-island editor; full UTF-16 character
count; selected and visible source ranges; line-index/range mapping; exact
parameterized strings including offscreen source and line endings; rejection
of a 65,537-unit request without returning a truncated value; and a successful
bounded request after that rejection. The wrapper checks the fixture SHA-256
again after the read-only requests.

Other `AXTextArea` elements (for example a read-only preview) are recorded but
do not fail the test. Only one candidate may expose both the full source count
and exact offscreen tail. Its `Mote editor` label is then checked separately.

This API test does **not** establish VoiceOver speech quality, keyboard or
mouse editing, IME correctness, or accessibility permission on other machines.
An old element's behavior after **New** is not yet exercised: unlike
`AccessibleRange`, an external `AXUIElement`/`CFRange` carries no exposed
generation token, and this first probe deliberately avoids a non-idempotent
file-switch action. Add a separate, controlled New/close phase only after the
basic provider and TCC route have passed on both macOS architectures.
