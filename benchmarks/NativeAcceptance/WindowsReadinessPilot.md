# Windows ordinary-file external JSON readiness pilot

## Implemented adapter and non-gating CI pilot; one hosted capability pass

The existing reviewed
[`Measure-WindowsOrdinary.ps1`](../NativeStartup/Measure-WindowsOrdinary.ps1)
now accepts an **opt-in** `-FixturePath` / `-FixtureSha256` pair. The default
Markdown fixture generator, prefix, CLI, edit/Save branch and result path remain
compatible. This is not a second GUI driver. The new adapter is limited to
`-ReadinessOnly`; Save, mutation, clipboard and diagnostic tracing are refused.
Neither an older local binary nor the uncommitted working tree was used as
current-product performance evidence.

External input must be a real non-reparse file below repository `.temp`, with
exact requested 1 or 100 MiB size, `.json`/`.csv`/`.md` extension and an independently
pinned SHA-256. Twenty initial printable ASCII bytes must precede any line break;
they are derived before timing and compared against the native input island
using ordinal equality. The actual prefix is never written to the JSONL report.
Every measured process receives a fresh **copied** pathname with the same format
extension; the copy's hash is checked before launch. Both original and copy
hashes must remain unchanged after readiness/selection. No original fixture is
edited, renamed or Saved. A changed input or copy is a failed capability, not a
latency sample to discard silently.

The first pilot uses generated `json-long-1`: valid JSON with one 1 MiB ASCII
string-bearing object, no newline, SHA-256
`bae792983708d005e143e90cfcb3a2110554dd590dd299059fb0d842dc20b5dd`.
The many-line JSON corpus begins with `[` followed by a newline; it deliberately
fails the twenty-byte oracle preflight rather than weakening source binding to
a one-character prefix. Supporting that corpus requires a separately reviewed
oracle/fixture contract, not an accidental broad “JSON ready” pass.

## Safe hosted command and expected evidence

The `win-x64` Native AOT CI job runs this only after publishing the **fresh
exact-checkout** binary and asserting its strict one-file inventory. Python
3.11+ is used only for corpus preparation, outside the launch timer.

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
$manifestPath = (python benchmarks/NativeAcceptance/acceptance.py prepare `
    --sizes 1 --formats json --shapes long).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Synthetic JSON preparation failed.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.cases.Count -ne 1 -or $manifest.cases[0].case -cne 'json-long-1') {
    throw 'Unexpected readiness corpus.'
}
$case = $manifest.cases[0]
& ./benchmarks/NativeStartup/Measure-WindowsOrdinary.ps1 `
    -ExecutablePath src/Mote.Native/bin/Release/net10.0/win-x64/publish/mote.exe `
    -FixturePath (Join-Path (Get-Location) $case.fixture) `
    -FixtureSha256 $case.sha256 -SizeMiB 1 -Runs 1 -ReadinessOnly
```

The CI step additionally checks the manifest against the independently pinned
SHA-256 above, requires exactly one fresh JSONL row, and compares the recorded
binary hash and Git commit to the current publish/checkout. Its result remains
non-gating until the target report is inspected; do not substitute an old local
binary simply because it can launch successfully.

The existing driver appends to
`.cache/native-startup/ordinary-windows.jsonl`; a clean hosted checkout should
produce exactly one new external-pilot row. Inspect the row itself and artifacts
even if its CI step is non-gating. Required capability fields:

| Evidence | Required value / interpretation |
| --- | --- |
| `status` | `passed` for the one requested external row |
| `fixture_kind`, `fixture_format` | `external-pinned-readiness-only`, `json` |
| SHA fields | original, copy-after-probe and input-after-probe all equal the independently pinned corpus digest |
| `executable_sha256` | Matches the fresh published binary inventory evidence; script does not independently certify AOT |
| `child_process_id` | Actual child, allowing foreground PID comparison |
| Window/source | Exact child PID plus `MoteNativeEditorWindow`, visible canvas, RichEdit child, exact twenty-byte prefix, 1–16,384 native characters |
| Selection | `EM_SETSEL(1,1)` / `EM_GETSEL` matches exactly; selection restored to 0 without a text mutation |
| Mutation/Save | dirty/Save latency, saved SHA and after-Save counters remain null |
| Child phases | configuration, child open→editable, child open→draw-return remain null with explicit `not-collected` status |
| `diagnostic_trace` | false; no child trace is collected or inferred |
| Focus | Foreground equality is reported, not forced; foreign foreground excludes user-responsiveness inference |

The driver keeps the existing finite three-second native-message timeouts and
5 ms polling cadence. It forcibly reaps its exact child during cleanup; because
the new route cannot mutate/Save and traces are off, no successful shutdown/trace
flush claim is made. Scratch is under `.temp/native-startup/<GUID>` and results
under `.cache/native-startup`. Cleanup is the existing confined scratch cleanup;
the external source remains outside that scratch and is never removed. The new
preflight test uses exact-leaf cleanup and rejects reparse ancestors before its
first write. Concurrent hostile filesystem mutation is outside this contract.

## Timing boundaries, deliberately not flattened

- `process_create_return_ms`: parent clock from just before `Process.Start` to
  its return. Fixture copy, SHA, prefix extraction and metadata are outside it.
- `window_visible_ms`: same launch clock to external visible native HWND.
- `source_bound_ms`: same clock to filename title **and** exact native source
  prefix. This is observed source binding, not semantic readiness or editable
  content proof; selection acknowledgement also cannot prove actual editability.
- `selection_ack_ms`: same clock through native selection acknowledgement.
  It includes observer/message overhead, not a physical keyboard event.
- `configuration_ms`, `child_open_to_editable_ms`,
  `child_open_to_draw_submission_ms`: **null, not collected**. Parent intervals
  include configuration/open work but do not separately time them. No child and
  parent clocks are subtracted; no visible HWND is relabeled first paint.
- CPU and RSS are point/process counters through the existing observation
  boundary, not allocations or a measured isolated parser/view phase. Positive
  peak working set remains a Windows process counter; zero stays null.

One hosted success proves this adapter's capability for this exact JSON-long
case, not its reliability, a 1/10/100 MiB matrix, a full semantic analysis,
input/scroll smoothness, p50/p95, allocation improvement or physical presentation.
There is no timing threshold. A failure must distinguish preparation, launch,
window, source binding, selection and unchanged-byte evidence before changing
production code. Only after a capability pass should a coordinated trace-on
natural-close variant or existing safe draw trace adapter be added to obtain
child phase evidence; this pilot does not fabricate those missing phases.

## Completed non-GUI checks

Windows x64 developer host, PowerShell 7, no editor process launched:

```powershell
& ./benchmarks/NativeStartup/Test-ExternalFixture.ps1
```

**13/13 preflight checks passed:** owned exact JSON accepted; mutation/Save route,
missing path/digest, wrong digest, unsupported trace, outside-scratch path,
unsupported extension, wrong size, weak prefix and junction rejected; default
Markdown preflight retained; source bytes unchanged. The test uses a malformed
sole `mote.exe` for **inventory-only** checks and must never execute that file.
PowerShell syntax parse passed. See
[`ExternalFixtureReview.md`](../NativeStartup/ExternalFixtureReview.md) for the
independent safety review, original finding and frozen script hashes.

## First hosted target checkpoint

The separately opted-in [natural-close trace variant](WindowsNaturalCloseTrace.md)
now provides a bounded trace-on/normal-exit path. Its target run is pending and
does not retroactively add child phases to this trace-off checkpoint.

[CI 36770328576](https://github.com/kleedaisuki/mote/actions/runs/36770328576)
at checkout `7bc42a6a0e995a15c3dfe81cf7a198951dd52de8` completed the win-x64
fresh-publish AOT job successfully. Its separately non-gating Python setup,
readiness probe and report upload steps each succeeded. The downloaded artifact
is retained locally under `.cache/ci-36770328576-readiness/`; the uploaded
`native-readiness-win-x64` artifact contains the corpus manifest and **exactly
one** JSONL readiness row.

The row reports `status=passed`, child PID 6380, real canvas/RichEdit discovery,
a 2,048-character bounded input island, matching pinned 20-byte source prefix,
and native selection acknowledgment. The fixture's original, copied-after and
input-after SHA-256 all equal
`bae792983708d005e143e90cfcb3a2110554dd590dd299059fb0d842dc20b5dd`.
The binary SHA-256 recorded and compared within the same hosted job is
`55d8ad7a795040b7151ae8c3eab2dbe07d696cc8c7dbf8b9ae331c79fb7c9eb2`.
No edit, Save or trace occurred; configuration, child open→editable and child
draw-submission endpoints remain null/not-collected. The observer reported the
target as foreground but did not assert native first-responder or physical key
delivery. Its single source-bound observation was 269.2647 ms and selection
acknowledgment 304.5401 ms from parent launch; these are **one automation-
inclusive observation each, not p50/p95 or an input-to-paint latency result**.

This establishes the adapter's narrow target capability on one published
Windows x64 binary and one 1 MiB long-string JSON fixture. Windows ARM, macOS,
10/100 MiB, repeated reliability, editability, whole-file JSON semantics and
physical paint still require separate evidence.
