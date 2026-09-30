# Hosted Windows CSV Grid accessibility subset diagnostic

Date: 2026-10-01. Status: first hosted attempt rejected project pin before client
execution; corrected exact line-ending pins await hosted validation. This is a non-gating diagnostic, not a release gate.

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
