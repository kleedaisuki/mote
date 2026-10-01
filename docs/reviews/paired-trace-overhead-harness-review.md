# Hosted Windows paired tracing overhead harness: independent review

Date: 2026-10-01.

## Scope and verdict

Reviewed the current worktree additions in
`benchmarks/NativePaintLatency/Measure-WindowsTracePairs.ps1`,
`summarize_trace_pairs.py`, and `TracePairs.md`, and the additive diff in
`Measure-WindowsScreen.ps1`. Inspected the portable tests introduced by
`d327073`, the native Save evidence reader, graph audit, target trace naming and
configuration, and document version progression. This review does not repeat
the Telemetry transport or Engine Save implementation reviews.

**No unresolved substantive defect identified in the reviewed source.** The
retained harness meets the intended bounded, hosted-only, same-binary paired
protocol without converting rejected or incomplete series into overhead
estimates. This is source and artifact validation, not evidence that any hosted
GUI series has qualified, and not an overhead measurement result.

## Contracts checked

| Area | Reviewed behavior |
| --- | --- |
| Existing driver compatibility | Default trace mode remains off; original cases, repetitions, default output root, local opt-in, topmost restrictions, two activation attempts, and hosted foreground gates remain unchanged. |
| Scheduling | Wrapper requires all three hosted Windows environment predicates, has no local/topmost switches, runs one fresh interpreter per sample, and alternates odd off/on and even on/off pairs. No replacement or filtered sample is scheduled. |
| Frozen workload | Every report must match the fixed 1 MiB sentinel hash, fresh default profile, ROI, case and repetition. Binary hashes are checked at declaration, in the report, and after each sample. Environment identity is checked against the first sample across the whole series, not only within each pair. |
| Owned lifecycle | Target numeric exit and CPU are read after bounded reap and before `Dispose`; forced cleanup is explicit. CPU retrieval failure remains null/unavailable, never zero. Trace copy occurs before generated home removal. |
| Evidence retention | Driver output, actual driver exit, target report, and copied rotations remain separate artifacts. Index append precedes classification. Failure, binary drift, identity mismatch, or trace rejection stops the series. |
| Path safety | Wrapper binary/output ancestry and driver scratch/output/trace ancestry are reparse-checked. Scratch removal checks containment and descendants before recursive deletion. Artifact reader rejects traversal, symlinks, junctions, and paths outside `.cache`, and constrains report/trace ownership to the series/sample. |
| Trace qualification | Enabled records use `MOTE_SAVE_CONTRACT` plus the existing causal graph audit: one normal session, exactly three genuine Save requests, complete instrumented chains, captured versions 1/2/3, no observed drops or unlinked positive stages. Final partial JSONL rows fail normal-exit qualification. Off-mode requires zero retained files/bytes. |
| Inference | Every predeclared pair must qualify before any estimate is emitted. Earlier successful pairs do not salvage a later failure. Deltas are on-minus-off regardless of scheduling order. Missing CPU suppresses that endpoint rather than imputing zero. |

The trace filename pattern matches `JsonlTraceSink`'s actual session GUID and
six-digit rotation naming. Desktop startup passes its configured trace
directory explicitly, so the retained `mote-home/traces` location is consistent
with the fresh `MOTE_HOME` contract. Document starts at version zero; edit, Undo,
and Redo each advance the version, explaining the required captured versions
1/2/3 rather than treating Undo as a return to version zero.

## Runtime and statistical interpretation

- Wrapper explicitly sets `$PSNativeCommandUseErrorActionPreference = $false`.
  This is important when its caller enables native nonzero-to-error conversion:
  driver/Python nonzero statuses must reach the index/classification path, not
  throw before the driver exit is recorded. This concern is resolved in the
  reviewed final source. Command-not-found and infrastructure exceptions still
  stop rather than schedule additional samples.
- Fresh `pwsh -NoProfile -File` isolates `Add-Type` definitions across samples.
  Target exit is not synthesized from `normal_exit_observed`; the actual
  `Process.ExitCode` remains independently required.
- Windows supports retrieving process CPU after exit when its handle remains
  available; the driver uses that supported lifetime and handles unavailable
  accounting conservatively. See [Microsoft Process.TotalProcessorTime]
  (https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.totalprocessortime?view=net-10.0)
  and [PowerShell native preference behavior]
  (https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_preference_variables?view=powershell-7.5).
- The rank interval implementation correctly chooses the largest admissible
  symmetric rank, with coverage
  `1 - 2 * sum(comb(n, j), j=0..k-1) / 2**n`. At n=20 this gives ranks 6/15 and
  95.8606% coverage. For n<6 it emits no finite 95% interval. Its validity is
  conditional on appropriate independent/stationary paired sampling; neither
  ABBA ordering nor a fixed host proves those assumptions. Documentation makes
  this limitation explicit rather than implying an unconditional hosted-fleet
  confidence statement. The motivation to report variation and uncertainty
  agrees with [Kalibera and Jones, ISMM 2013]
  (https://kar.kent.ac.uk/33611/45/p63-kaliber.pdf).
- Existing bounded unsettled-control warm-up remains inside each original
  driver attempt. It is not an added excluded qualification run or replacement
  sample. The endpoint remains software screen-DC capture, not photons,
  compositor presentation, physical-keyboard latency, or continuous focus.
- Normal session and complete observed Save chains certify only the stated
  instrumented-chain evidence. They do not certify absence, full telemetry
  transport, or storage durability. The summary explicitly leaves
  `absence_certified` and `appkit_cost_measured` false.

## Validation and limits

Independent artifact-only run:

```text
python -B -m unittest discover -s benchmarks/NativePaintLatency -p test_trace_pairs.py -v
Ran 16 tests; OK.
```

Both PowerShell files parsed with the PowerShell AST parser without errors.
Final implementation-owner validation logs were also inspected at
`.cache/validation/trace-pairs/portable-tests.log` (16/16) and
`powershell-ast.log` (both syntax-ok); completed validation was not needlessly
re-run after the small native-error/inventory-type hardening.

No native target, GUI driver, local publish, global input, or input injection
experiment was executed for this review. Hosted foreground acquisition,
actual driver/target process timing, real trace retention, and complete
predeclared-series qualification remain to be established by the first real
hosted run. That limitation is stated in the harness documentation and is not
a reason to invent a local favorable sample or weaken the gates.
