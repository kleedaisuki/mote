# Windows read-only natural-close trace variant

## Scope and implementation

The existing ordinary Windows file driver has a new opt-in
`-NaturalCloseTrace` mode. It requires a pinned repo-local external fixture and
`-ReadinessOnly`, and cannot combine with legacy `-DiagnosticTrace`. It performs
the same source-prefix, native selection and unchanged-byte oracles as the
[trace-off readiness pilot](WindowsReadinessPilot.md), then gives ordinary
queued drawing a bounded observation window and normally closes the exact child.
It never edits, Saves, mutates clipboard/input sources, changes TCC, forces
foreground or forces painting. Default Markdown and trace-off behavior remain
compatible. No Engine/Native, atomic-save diagnostic or CI change is made.

Each child gets a fresh `.temp/native-startup/<GUID>/home-<ordinal>` and
`MOTE_TRACE=1`, not the user's `.mote` or a shared home/cache/config. After the
source/selection oracle, the default observation is **1,000 ms**, configurable
via `-TraceObservationMilliseconds` within **250–5,000 ms**. The actual parent
wait interval is recorded separately; it is not native rendering CPU time or
part of a child open duration. There is no `UpdateWindow` or injected `WM_PAINT`.
A missing/cancelled draw remains an endpoint limitation, not fabricated success.

The title must not be dirty before close. The driver posts exactly one
`WM_CLOSE` to the identified PID/class window, waits at most **10 seconds** for
exit, and requires exit code zero. The production exit disposes pending intervals
before a two-second bounded telemetry drain. A terminal `mote.session` must be
present; successful flushing is not inferred from exit alone. Failed close uses
an exact-child kill/reap fallback and a failed row, never normal termination.
An unreapable child retains scratch instead of deleting live files.

## Reuse the bounded causal auditor

`benchmarks/NativeStartup/NativeTraceEvidence.ps1` is an artifact-only helper.
After normal exit it preserves raw rotation files in
`.cache/native-startup/traces/<GUID>/<ordinal>/`, writes a manifest with the
actual parent-verified binary digest, and invokes existing
[`acceptance.py`](README.md). Input must remain below `.temp`, output below
`.cache`; reparse ancestry/files and output overwrite are refused. Only one to
eight correctly named JSONL files, each at most 32 MiB, are admitted; existing
line/record/schema/causal bounds remain authoritative.

Required coverage is exactly one `mote.session`, one `document.open` and one
`document.open_to_editable`. Integrity requires one normal root session, no
duplicates, missing parents, cycles or reported drops, and explicit matching
open-draw parent revisions. Recorded canonical edit, edit-commit/draw, Save,
completed-Save or Save-failure operations reject read-only evidence. Independently,
original/copy SHA must remain unchanged **through normal exit and collection**.
Source bytes/prefix are never copied into parent JSONL; raw product trace is
fixed-schema, numeric/content-free and retained with hashes and causal summary.
The root session is serialized-shutdown evidence, not an exact storage-durability
certificate or proof that an uninstrumented event never occurred.

## Separate endpoints and clocks

| Parent field | Actual endpoint | Missing/censored representation |
| --- | --- | --- |
| Existing launch/window/source/selection fields | Parent launch/observer clock, now a trace-on **diagnostic** | Bounded failed stage row |
| `configuration_ms` | **Not instrumented**: tracing starts after config | Always null, explicit `configuration_status`; never subtract clocks to estimate it |
| `child_startup_to_editable_ms` | Child monotonic startup after config → initial source installation | Null unless exactly one success |
| `child_document_open_ms` | Child monotonic combined engine open/decode/rope phase | Null unless exactly one success |
| `child_open_to_editable_ms` | Child accepted open → replacement source installation return | Null unless exactly one success; not full semantics |
| `child_open_to_draw_submission_ms` | Child accepted open → first eligible version-matched source draw callback return | Explicit missing/cancelled/failed/skipped/multiple state; only one success yields a duration |
| `trace_observation_actual_ms` | Parent bounded draw opportunity | Not a first-frame or drawing-duration endpoint |
| `close_to_exit_ms` | Parent close request → observed exit, including drain | Not launch or source draw; failure cannot certify child endpoints |

No parent and child clocks are subtracted. Causal IDs/revisions, not UTC or JSONL
adjacency, join intervals. There is no edit endpoint in this experiment. Draw
return is not compositor presentation, physical pixels, a physical key or
input-to-light latency. CPU/RSS retain the existing readiness/selection boundary,
**before** observation and close; not whole-session peaks or phase allocations.

`child_trace_evidence.endpoints` carries per-operation outcome counts and a
`success`, `cancelled`, `failure`, `skipped`, `missing`, or
`ambiguous-multiple-records` status. Cancelled observations have null successful
duration. `child_endpoint_status` is `collected-successful-endpoints`,
`collected-with-missing-or-censored-endpoint`, or `requested-but-not-certified`.
The last accompanies failed close/collection. A passed source/normal-close
capability with missing draw is **not** a draw/performance acceptance pass; CI
must inspect endpoint evidence rather than only the parent's `status`.

Trace-on parent numbers are separate from the prior trace-off pilot: different
binaries/runner instances/regimes cannot quantify tracing overhead. Configuration
remains unavailable without a separately coordinated production phase contract.

## Fresh-hosted command proposal — target run pending

Generate `json-long-1` outside timing; use the root owner's fresh exact-checkout
strict Native AOT publish and preserve its binary/provenance evidence:

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
$manifestPath = (python benchmarks/NativeAcceptance/acceptance.py prepare `
    --sizes 1 --formats json --shapes long).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Synthetic JSON preparation failed.' }
$case = (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).cases[0]
& ./benchmarks/NativeStartup/Measure-WindowsOrdinary.ps1 `
    -ExecutablePath .cache/FRESH_STRICT_PUBLISH/mote.exe `
    -FixturePath (Join-Path (Get-Location) $case.fixture) `
    -FixtureSha256 $case.sha256 -SizeMiB 1 -Runs 1 -ReadinessOnly `
    -NaturalCloseTrace -TraceObservationMilliseconds 1000
```

`FRESH_STRICT_PUBLISH` is a wiring placeholder, not a verified local binary.
Use a bounded, **non-gating** first target step; `if: always()` upload corpus
manifest, `ordinary-windows.jsonl` and `.cache/native-startup/traces/**`. Inspect
logs, termination, byte hashes, normal session, causal summary and endpoint
outcomes independently of workflow success. Do not use an older local binary.
Only a later fair same-binary/same-host off/on pair can quantify overhead.

## Completed artifact/preflight checks, no native execution

Windows x64 developer host, PowerShell 7, Python 3.14.6:

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
& ./benchmarks/NativeStartup/Test-ExternalFixture.ps1
& ./benchmarks/NativeStartup/Test-NativeTraceEvidence.ps1
```

- **17/17** external preflight: old guards plus accepted inventory-only natural
  mode and rejected no-fixture/edit-route/mixed-legacy-trace combinations.
- **7/7** synthetic evidence: normal session/child duration, cancelled/null draw,
  missing draw, canonical edit/Save refusal, missing terminal and drop refusal.
  Fabricated test durations validate extraction, never product latency.
- Independent **4/4** PowerShell syntax checks and embedded C# compile-only
  validation; no Win32 function, GUI or native process called.
- Frozen hashes and independent review:
  [`NaturalCloseTraceReview.md`](../NativeStartup/NaturalCloseTraceReview.md).

Actual target normal close/flush/draw availability is not yet validated. No
configuration time, native timing/percentile, allocation/RSS improvement, Save
behavior or physical presentation is claimed. Missing/censored hosted results
must guide the next investigation instead of prompting speculative optimizations.
