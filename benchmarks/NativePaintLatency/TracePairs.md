# Same-binary opt-in trace overhead: hosted Windows paired guard

This is a retained **measurement harness**, not a performance result or release
SLA. It measures no AppKit/local-input-monitor cost. Previous local current-code
attempts produced **zero equivalent pairs**; failures remain in the
[causal-trace experiment record](../../docs/performance/causal-trace-overhead.md).
Do not retry a user's desktop until favorable samples appear.

## Invocation and unchanged default driver

Run once on an otherwise idle disposable GitHub-hosted Windows desktop, using
the single executable already published in that job. No local wrapper override
exists, and no additional publish is performed:

```powershell
./benchmarks/NativePaintLatency/Measure-WindowsTracePairs.ps1 `
  -ExecutablePath .cache/published-win-x64/mote.exe -Pairs 20
```

`Pairs` is predeclared, 1–30 (default 20). Each sample is a new interpreter and
one fresh target process. Odd pairs run off/on, even pairs on/off: ABBA across
adjacent pairs. There is **no preexcluded qualification sample**, background
fallback, adaptive filtering, retry, focus repair, topmost modification or
cross-machine pooling. The wrapper uses the existing driver's two target
`SetForegroundWindow` attempts unchanged; this is **not a no-activation protocol**.
Both recorded global-foreground observations must be true. Five-point ROI
ownership is not whole-image ownership or continuous focus observation.

`Measure-WindowsScreen.ps1` adds `-TraceMode off|on` (default `off`) and optional
`-ArtifactDirectory`. Existing default cases/repetitions, output location, fresh
`MOTE_HOME`, trace-off behavior, hosted foreground gate, local opt-in and source/
screen oracles remain intact. An explicit artifact directory must be new and
under repository `.cache`; existing ancestors are reparse-checked. The paired
wrapper never passes local switches. Generated files/homes stay in repository
`.temp` and are removed only after the exact launched target has exited or
bounded forced cleanup has reaped it.

## Workload and retained observations

Each sample opens the fixed 1 MiB CRLF sentinel plain-text source in experimental
Canvas. The driver preserves quiet `WM_NULL`, bounded `WM_CHAR`, unchanged-before-
Save source, **exact X Save → original Undo Save → X Redo Save**, stable distinct
Undo glyph shape and matching Redo screen-state oracles. No screenshot or
document body is persisted. Fresh default profile, source hash, ROI, OS, CPU
identity/count, physical RAM, display/client dimensions and DPI must be present
and equal across the **whole** declared series. Source size/hash/profile/ROI are
also fixed, not merely equal. Binary SHA-256 is frozen at entry, in every target
report and after every sample; manifest retains harness/reader hashes, PowerShell
version and runner image.

| Field | Endpoint and limitation |
| --- | --- |
| `input_ack_ms` | Synthetic cross-process `WM_CHAR` through synchronous acknowledgement; not physical keyboard input |
| `launch_to_source_ready_ms` | Monotonic launch through bounded nonempty input-island availability (20 ms polling), not first complete editable frame |
| `first_changed_capture_ms` | First sampled software ROI later qualified by source/Undo/Redo state; not compositor presentation or photons |
| `process_cpu_ms` | Target user+privileged CPU across open/edit/three Saves/Undo/Redo/draw/shutdown, excluding observer CPU; null if unavailable |
| `process_lifetime_ms` | Monotonic launch through observed exit including deliberate waits, not interactive latency |
| `exit_code` | Actual target numeric exit after reaping/before `Dispose`, never synthesized from booleans |
| `driver_exit_code` | Actual PowerShell driver exit, distinct from target exit |

CPU is retrieved while the retained Windows process handle exists, as
[Microsoft documents](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.totalprocessortime?view=net-10.0).
Coarse accounting and observer CPU/BitBlt cost limit sensitivity. No affinity,
priority, power profile, cache eviction, input-source or production change is
made. This is warmed-file-cache whole-workload measurement, not allocations,
memory, sustained typing, real IME, large semantic files or cold startup.

On-mode traces are copied after target reap/before home cleanup into the owned
sample artifact directory. The normal-exit reader rejects even a final partial
row, malformed complete records, missing/cyclic parents, ambiguous session root,
observed drops or incomplete Save chains. It uses the shared strict schema/
operation/privacy/dimension loader (not the future-tolerant prefix reader), graph audit and
`MOTE_SAVE_CONTRACT`, requiring exactly three complete real Save requests with
captured versions 1/2/3. Off-mode must retain zero trace files. This is
instrumented-chain evidence only: neither external key delivery, photons,
complete telemetry transport, absence nor durability is certified.

## Stop protocol, inference and artifacts

`.cache/benchmarks/native-trace-pairs/<series-id>/` retains:

- Immutable `manifest.json`: count/protocol/binary and harness hashes.
- Append-only `index.jsonl`: scheduling-order attempts including driver exit.
- `pair-<n>-<mode>/screen-observations.jsonl`: actual one-row target result.
- `pair-<n>-<mode>/many-1-1/*.jsonl`: enabled trace rotations.
- `pair-<n>-<mode>-driver.log`: driver output, including failures.
- `summary.json`: updated bounded evidence classification after every attempt.

The wrapper appends an index entry **only after its driver returns**.
`attempted_samples` therefore means **indexed driver-return samples**, not all
launched processes or a launch/completeness watermark. A hosted step timeout can
leave an in-flight launched sample unindexed; its output directory and driver log
remain retained by the workflow's always-upload step. Do not invent counts from
directories or normal-exit predicates. Such incomplete evidence is unknown, not
proof that the target did not launch or execute an unobserved operation.

First failed sample, unavailable required control, binary drift, trace integrity
failure or environment mismatch stops scheduling immediately. Rejected attempts
stay retained; no replacement sample is allowed. A half-pair is `incomplete`, not
a comparison. Even if earlier pairs qualified, an incomplete/rejected **whole
series** has `paired_estimate=null`; it cannot be promoted after seeing outcomes.

Only every predeclared pair qualifying yields on-minus-adjacent-off deltas for
common endpoints. The median and narrowest finite order-statistic median interval
with conditional coverage ≥95% use `1 - 2*sum(C(n,j),j=0..k-1)/2^n`, ranks `k`
and `n-k+1`. At n=20, ranks 6/15 yield 95.8611%. At n<6 no finite 95% distribution-
free interval exists: null interval and maximum finite coverage are shown. CPU-
null samples never become zero; that endpoint's estimate stays unavailable. No
p95/SLA is inferred from tiny samples. Independent stationary paired sampling
is an **assumption**, not a proven hosted-desktop property; the interval excludes
between-machine variation and correlated load. The
[Kalibera/Jones ISMM study](https://kar.kent.ac.uk/33611/45/p63-kaliber.pdf) motivates
explicit variation/uncertainty rather than one favorable timing.

Artifact-only checks (Python 3.12+), without opening windows or posting input:

```powershell
python -B -m unittest discover -s benchmarks/NativePaintLatency -p test_trace_pairs.py -v
python -B benchmarks/NativePaintLatency/summarize_trace_pairs.py `
  --series .cache/benchmarks/native-trace-pairs/<series-id>
```

The first actual hosted run, Benchmarks 36841148501 at c19f4c6, qualifies all 20 predeclared pairs; see docs/performance/causal-trace-overhead.md for inspected raw evidence and conditional estimates. Parser/fixture success alone remains insufficient, and this Windows study does not establish AppKit cost.

Initial artifact-only validation: 22/22 independent fixtures plus both
PowerShell AST parses, Windows/Python 3.14.6; retained output lives at
`.cache/validation/trace-pairs/{portable-tests,powershell-ast}.log`. These checks
include malformed reports, strict privacy vocabulary and actual retained-file
inventory, not a native/desktop launch.
