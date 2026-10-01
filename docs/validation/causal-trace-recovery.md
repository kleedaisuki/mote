# Independent causal trace prefix validation

Date: 2026-10-01. Scope: synthetic target-owned telemetry requests, not real
native Save routing or document byte correctness.

## Contract and useful negative result

`command.received` is the persisted request anchor. Its parent is the session.
The distinct terminal `command.save`/`command.save_as` is a child of that anchor.
Each phase entry is the persisted phase anchor; phase duration terminals use
fresh IDs parented to the entry. This permits reconstructing a held nested
phase before any duration terminal exists.

The validator caught a consequential initial design defect before integration:
an entry checkpoint parented to an unpublished duration ID could not be linked
to its request after kill. Transport and orchestration owners adopted the
anchor graph above. The fixture `test_dangling_phase_entry_remains_positive_but_unlinked`
preserves the negative case; the native held-anchor fixture verifies its remedy.

The reader never treats a missing callback, stage, or terminal as proof of
nonexecution. A terminated received request without a terminal is `censored`.
Live incomplete lines are retained; only a terminated final incomplete line is
discarded. Malformed earlier complete lines and duplicate terminal identities
are integrity failures. Compatible old schema-v1 records remain readable as
`legacy_health_unknown`. No watermark/session-start has been added in this slice.
Optional future sequence continuity cannot establish zero producer loss.

## Reproducible checks

Environment: Windows local x64, .NET 10, Python standard library. Experiments
and output are confined to repository `.temp/` and `.cache/`.

```powershell
python -B -m unittest discover -s tests -p test_causal_save_trace_reader.py
dotnet build tests/CausalTraceRecoveryProbe/CausalTraceRecoveryProbe.csproj -c Release --nologo
python -B tests/Invoke-CausalTraceRecovery.py --binary tests/CausalTraceRecoveryProbe/bin/Release/net10.0/CausalTraceRecoveryProbe.dll --output .temp/causal-recovery-managed-20261001-b
dotnet publish tests/CausalTraceRecoveryProbe/CausalTraceRecoveryProbe.csproj -c Release -r win-x64 -o .cache/causal-recovery-probe/win-x64 --nologo
python -B tests/Invoke-CausalTraceRecovery.py --binary .cache/causal-recovery-probe/win-x64/CausalTraceRecoveryProbe.exe --output .temp/causal-recovery-aot-win-x64-20261001
```

Choose fresh output names when reproducing: the driver deliberately refuses an
existing output directory. Published executables can be supplied to the same
driver on other platforms. It terminates only the process it created; no global
input, clipboard, accessibility permissions, application focus, or profiles are
modified. The probe performs no document Save I/O.

## Observed results

| Check | Result | Interpretation |
| --- | --- | --- |
| Independent Python fixtures | 19/19 pass | Schema/framing, independent requests, unique terminals, positive stage status, prefix linkage and censorship |
| Managed owned-child experiment | held and normal pass | Received + coarse + nested entry readable while alive; retained after kill; normal control terminal and orderly session drain present |
| Local win-x64 Native AOT experiment | held and normal pass | Same scoped prefix and censorship behavior in an actual compiled native child |

The held case waits until **linked** `command.received`,
`document.save.entered`, and `save.temp_flush.entered` are actually readable,
then kills the owned process. The retained request is censored, has no invented
duration/command/session terminal, and ends at the positively observed nested
phase. Normal control emits phase terminals, a request terminal, and a normal
session terminal. Every row retains unique identity.

The initial managed driver run failed because the reader rejected
`parent_span_id: null` on the existing session terminal. This was a harness
compatibility error, not a product defect; the parser now accepts absent/null
parent IDs and the legacy fixture covers it. Failed evidence remains in
`.temp/causal-recovery-managed-20261001/report.json`; the corrected managed and
AOT reports are in the paths above.

## Coverage limits

This is evidence of OS-readable buffered prefix recovery after owned process
termination, **not** power-loss durability, a hard 250 ms scheduling guarantee,
or proof that the held phase remained blocked at death. No complete transport
health certificate exists yet. It is not a real native Save byte oracle or
permission/input-delivery test. Hosted macOS/Windows ARM execution, forced
writer failure/rotation controls, and ordinary GUI integration remain separate
checks. The reader's successful chain flag certifies listed instrumented
boundaries, not filesystem correctness or compositor presentation.
