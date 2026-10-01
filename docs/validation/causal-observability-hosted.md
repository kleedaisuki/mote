# Hosted causal observability audit

Date: 2026-10-01. Independently audited [CI 36823606282](https://github.com/kleedaisuki/mote/actions/runs/36823606282), source `3f3e59a816ec497e2605d4d417cb8b7339202601`.

## Verdict

The permanent request graph now has hosted evidence on all four Native AOT RIDs. Four synthetic recovery controls per RID pass, and six of eight ordinary native JSON Save/reopen cases have complete version-consistent target-callback-to-local-UI chains. This is an observability milestone, **not a Mac Save reliability fix**. Two ordinary Mac cases still time out without a retained Save receipt; their abnormal streams cannot certify callback nonexecution.

The overall run is **failure**: three clipboard jobs fail reviewed-source pins before launch, and the strict Windows Save positive control detects removal of the established schema-v1 `save.failure.replace` event. Four AOT jobs and two strict solution-test jobs pass. Local repairs discussed after this run are not hosted evidence.

## Method and retained provenance

Reused `docs/reviews/observability-end-to-end-audit.md`, `docs/validation/causal-trace-recovery.md`, and prior JSON pilot documents. Downloaded artifacts via the run artifacts API (`per_page=100`) and `gh run download`. Retained raw artifacts/logs under `.cache/ci-36823606282-causal-evidence/`, grouped by RID. Reparsed all 14 ordinary JSON trace files and all 16 recovery trace files with `python -B`, `read_prefix`, and the distinct `MOTE_SAVE_CONTRACT` / `RECOVERY_SAVE_CONTRACT`; ordinary edited-trace SHA256 values match their reports. No GUI experiments, production changes, or workflow changes were performed for this audit.

## Strict jobs: actual outcomes

| Evidence | Windows | macOS |
| --- | --- | --- |
| Complete solution tests | Mote.Tests 1333/1333; Themes 14/14; Configuration 9/9 | Same counts |
| Failed/skipped tests | 0 / 0 | 0 / 0 |
| Disposable Grid clipboard | Prelaunch exit 1: harness differs from reviewed source | x64 and ARM64 prelaunch exit 1: probe differs from independently reviewed bytes |

The three clipboard failures yield no runtime acceptance report: they are frozen-source hash drift, not observations of clipboard behavior. Strict suite counts come from numeric completed-job log lines, not job color.

Windows Save replacement diagnostic job `110244294133` exits 1 at its unchanged positive control. Its nested editor exits **0 normally**, remains responsive, retains dirty state, and leaves original bytes unchanged under the no-delete-share held handle. Its complete 52-row trace contains `save.commit_replace` failure with `version=1`, `hresult=-2147024864`, the same coarse `document.save` failure, successful failure inspection, and terminal `command.save` failure (`reason=save_failed`). However, established `save.failure.replace` is absent and the old reader's `trace.failures=[]`. This is a telemetry compatibility regression, not a persistence failure; restoring the additive legacy event is preferable to weakening the safety oracle. Only the positive control ran; later diagnostic cases are not certified.

## Four-RID recovery: 16 actual synthetic controls

| RID | held nested / receipt Save / receipt Save As exits | normal exit | Raw record counts: held / normal / Save / Save As |
| --- | --- | --- | --- |
| win-x64 | 1 / 1 / 1 | 0 | 5 / 12 / 1 / 1 |
| win-arm64 | 1 / 1 / 1 | 0 | 5 / 12 / 1 / 1 |
| osx-x64 | -9 / -9 / -9 | 0 | 5 / 12 / 1 / 1 |
| osx-arm64 | -9 / -9 / -9 | 0 | 5 / 12 / 1 / 1 |

Each held trace retains the graph `command.save.received -> document.save.entered -> save.temp_flush.entered`, with admitted/worker checkpoints, and no manufactured duration/request/session terminal. Reader classification changes from pending before the owned kill to censored afterward. Receipt-only cases retain exactly the correct typed Save or Save As anchor and are censored. Normal controls have a successful request terminal, complete **synthetic** contract, and normal session terminal. All 16 report `absence_certified=false`; none has observed dropped records, but abnormal transport remains `legacy_health_unknown`, not a zero-loss certificate. These controls intentionally do not execute document Save or certify byte correctness/power-loss durability.

## Ordinary product JSON: exact Save and fresh reopen

| RID / MiB | Actual result | Edited trace rows / bytes | Fresh reopen trace rows | Native causal chain |
| --- | --- | --- | --- | --- |
| win-x64 / 1 | pass | 56 / 18,662 | 13 | complete, saved version 1 |
| win-x64 / 100 | pass | 61 / 20,507 | 18 | complete, saved version 1 |
| win-arm64 / 1 | pass | 56 / 18,662 | 13 | complete, saved version 1 |
| win-arm64 / 100 | pass | 64 / 21,574 | 18 | complete, saved version 1 |
| osx-x64 / 1 | pass | 63 / 20,955 | 11 | complete, saved version 1 |
| osx-x64 / 100 | Save exact-byte timeout | 32 / 11,180 | absent | unobserved request; censored session |
| osx-arm64 / 1 | Save exact-byte timeout | 29 / 10,014 | absent | unobserved request; censored session |
| osx-arm64 / 100 | pass | 69 / 23,152 | 16 | complete, saved version 1 |

All six passing cases report expected exact saved SHA256, unchanged original fixture, normal edited/reopened exits, and fresh reopened edited spelling. The pilot enforces `child.wait()==0` for both exits, but **does not retain numeric editor exit fields in its JSON report**; numeric 0 is thus an enforced source-level predicate, not a separately inspectable numeric report field. No numeric exit is invented for the failed cases.

Reclassification verifies receipt, admission, worker, gate, exact captured snapshot, target checks, encode/write, flush, hash, final target check, successful replace, saved stamp, bookkeeping, coarse Save, local UI post-return/start, completion, and the request terminal, under one request/session graph. Captured version is exactly 1 with no identity errors or missing required stages. All six normal edited traces and six normal reopen traces contain normal session terminals and no dropped-record events.

Both failed traces contain valid retained open/edit/analysis/layout evidence and one successful generic `document.edit_to_draw_submission`, but **zero Save request receipts or Save stages**, no session terminal, and forced cleanup. Mac witness streams contain only ready, not selector/admission/completed, and are censored. One attempted Command-S key pair has no delivery acknowledgement; focus metadata is not dispatch evidence. The strongest conclusion is retained pre-Save progress with Save receipt unobserved, not 'the callback never ran' or 'OS delivery failed'. Generic draw evidence is independent of Save graph completeness and is not pixels/compositor presentation.

## Step Summary truthfulness and remaining Grid failures

The four uploaded evidence summaries preserve selected nested outcomes instead of laundering `continue-on-error` results into a green product verdict. Mac summaries explicitly expose the incomplete JSON report, censored failing sample, numeric recovery exits, and original Grid failures.

| Surface | osx-x64 | osx-arm64 |
| --- | --- | --- |
| Original external Grid AX | Swift exit 1; 40/41 checks; normal editor exit | Swift exit 1; **25/26** checks; downstream admission guard refused; forced editor cleanup |
| Failing check | context-menu-accessible, AX -25205 | Same |
| Same-client C0/P0 pair | AX 0 / -25205; both owner/client exit 0; normal owners | Same |

The ARM original probe is not the historical 40/41 normal result: later checks did not execute after guarded downstream refusal, and retirement/normal-close acceptance is censored. Pair harness/client exit 0 means the comparison completed, not that product AX succeeded; the summary correctly labels P0 failure observed. Input hashes remain unchanged. No new Grid hypothesis or runtime fix is established here.

## Consequential next validation

Revalidate the three reviewed-byte pin repairs and additive legacy failure event in the next hosted run without weakening existing controls. Preserve the new phase graph and causal reader. Ordinary Mac Save remains a separate open runtime problem: instrument the earlier target key/command-routing boundary if pursued, while retaining the rule that missing censored records never certify nonexecution. A fresh infrastructure performance comparison is still needed; the older trace overhead baseline does not measure this newly integrated request graph.
