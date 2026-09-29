# Published-binary macOS AX contract probe

`Run.ps1` compiles `Probe.swift` with the host Xcode toolchain and starts the
**published** `mote` executable as a separate process in opt-in canvas mode.
It first runs `xcrun swiftc -typecheck`; compile errors are classified as
`probe-error` and persisted before any editor process is launched.
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

## Hosted evidence

GitHub Actions run `36571266856` exposed a **test-client compile defect** on
both RIDs: Swift rejected conditional `as?` casts to CoreFoundation types.
The client now checks `CFGetTypeID` before converting those values. Run
`36572346343` then passed the full external probe on both `osx-x64` and
`osx-arm64` with `swift_typecheck_passed=true` and client exit code 0.
Each report observed two `AXTextArea` nodes in the application tree (including
the preview), exactly **one** source-backed editor, `Mote editor` via
`AXDescription`, 70,773 UTF-16 source units, and correct CR/LF/CRLF, emoji,
70,001-unit line, and offscreen tail mappings. The 65,537-unit request failed
with AX error `-25212` and no returned string; a subsequent offscreen read
succeeded. The uploaded reports are named `mac-ax-external-osx-x64` and
`mac-ax-external-osx-arm64`. This evidence is specific to the published
experimental canvas binary and hosted runner's granted AX permission.

After that first successful run, the client gained a graceful **Close** phase:
it presses the same window's `AXCloseButton`, keeps the original editor
`AXUIElement`, and demands that it stop exposing the old source before the
wrapper accepts the editor's normal exit. Hosted run `36576635105` passed
this phase on both RIDs: `AXPress` returned success, a subsequent request on
the retained element returned AX error `-25204` rather than old text, and
the wrapper observed normal editor exit without fixture-byte changes. The
earlier `36572346343` result proves only the preceding read-only checks.
