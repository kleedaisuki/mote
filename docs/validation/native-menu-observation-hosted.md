# Hosted native menu observation audit

Date: 2026-10-01. Source `113df9cffaa7d9d3a3c973b1597cd69f69840ad5`, [CI 36827565407](https://github.com/kleedaisuki/mote/actions/runs/36827565407).

## Verdict

Both macOS Native AOT RIDs execute the new in-process menu ABI control successfully, and all four ordinary Mac JSON samples retain independent menu ready/entered/returned-true observations. All eight four-RID ordinary Save/reopen samples pass their separate exact-byte/version-consistent causal contracts with **explicit numeric editor/reopen exits 0**. This establishes the newly instrumented boundary, **not an event-to-request causal edge or a Save-routing fix**.

Overall CI is failure: Windows strict tests report 1353 passed / 1 failed of 1354. The failing existing queued-save observation test times out its five-second synchronous entry wait, before its version-capture assertions; no native ABI crash is observed. macOS strict tests pass 1354/1354. Four AOT jobs, three strict clipboard jobs, and the Windows Save strict diagnostic succeed. Any local test repair after this SHA is not hosted evidence here.

## Method and provenance

Reused `docs/validation/native-menu-observation.md` and `docs/reviews/native-menu-observation-review.md`; prior causal hosted audit remains complete and is not recreated. Retained completed-job logs, selected inventories/reports and raw traces under `.cache/ci-36827565407-menu-evidence/`. Independently reparsed all 16 ordinary JSON trace files and 16 synthetic recovery traces using `python -B`, the strict complete-prefix reader, and their distinct native/recovery Save contracts. Checked raw menu attributes, status, duration and parent identities. No new experiments, production edits, workflow changes or push were performed.

## Actual macOS ABI execution

Both completed AOT job logs emit `mote-native-mac-flow-rendering-ready`: ARM64 at 07:02:19 UTC, x64 at 07:03:14 UTC. At the audited SHA, `MacFlowRenderingProbe.Check` calls `MacMenuObservationProbe.VerifyForwarding` before the Flow assertions/ready marker; the command requires exit 0. Thus this is actual native control execution, not merely a compiled test name.

The control constructs owned synthetic NSEvents without posting them, exercises true/false exact byte returns, unchanged event pointer, one base invocation per call, a bounded nested call, and an inherited observation IMP on a descendant (lexical superclass avoids recursion). It uses the same production forwarding core. These successful controls establish the scoped Objective-C BOOL/object ABI behavior on both RIDs; they do not establish external keyboard delivery, every NSMenu topology or physical input.

## Ordinary menu inventory: independent retained positives

Each edited Mac trace has exactly the following successful zero-duration session-child records with **empty attributes**:

| RID / MiB | ready | unavailable | save-family entered | returned true | returned false |
| --- | --- | --- | --- | --- | --- |
| osx-x64 / 1 | 1 | 0 | 1 | 1 | 0 |
| osx-x64 / 100 | 1 | 0 | 1 | 1 | 0 |
| osx-arm64 / 1 | 1 | 0 | 1 | 1 | 0 |
| osx-arm64 / 100 | 1 | 0 | 1 | 1 | 0 |

Each fresh Mac reopen trace contains only one menu-ready record, no candidate entry/return. Windows inventories are unobserved with all menu counters zero, appropriate to this Mac-only boundary. Absence does not certify callback nonexecution; the reader records `absence_certified=false` and `request_correlation=none`. No returned-false candidate was observed in ordinary product samples; the synthetic ABI control does exercise false returns.

Raw vocabulary contains only the fixed documented menu operations, no serialized text, key codes, modifiers, event pointers or arbitrary content. All observed records parent directly to their own normal session root, not to a Save request or ambient Activity. No parse/integrity errors occurred. A returned-true observation means superclass handled the menu candidate; **it is not successful Save**. Entry/return pairs and event-to-request relations are not invented from counts or timing.

## Independent Save and regression evidence

| RID | Edited trace rows: 1 / 100 MiB | Reopen trace rows: 1 / 100 MiB | Explicit exits, editor / reopen |
| --- | --- | --- | --- |
| win-x64 | 56 / 61 | 13 / 18 | 0 / 0 for both sizes |
| win-arm64 | 56 / 64 | 13 / 18 | 0 / 0 for both sizes |
| osx-x64 | 64 / 72 | 12 / 17 | 0 / 0 for both sizes |
| osx-arm64 | 67 / 72 | 12 / 17 | 0 / 0 for both sizes |

All eight reports contain expected exact saved SHA256, unchanged original-fixture SHA256, complete diagnostics and fresh edited-spelling reopen; numeric exits are now separately retained. Raw reclassification finds one complete successful Save request per edited trace, captured snapshot version exactly 1, no identity errors, all route-aware Engine commit phases and local UI completion. Normal edited/reopen roots are retained. These eight observed passes are not a reliability estimate; historical intermittent Mac failures are not erased by this successful run.

All 16 synthetic recovery cases pass: held and receipt-only children exit 1 on Windows / -9 on Mac and remain censored; normal children exit 0 and have complete synthetic chains. The reader preserves typed receipt anchors without inventing Save, bytes, normal termination or absence certification. Recovery remains infrastructure-only, not document persistence.

Strict logs: macOS Mote.Tests 1354/1354, Windows 1353/1354 with one failure; both Themes 14/14 and Configuration 9/9, no skipped tests. The Windows failure is `DocumentSaveObservationTests.Queued_save_captures_later_version_after_gate`, Assert.True false at audited source line 57: `operations.Entered.Wait(TimeSpan.FromSeconds(5))`. This identifies the failed wait, not the unexecuted later snapshot assertions or a proven production regression/root cause.

## Non-gating truth remains visible

Uploaded summaries continue to expose both original Mac Grid probes as failed (40/41, Swift exit 1, normal editor exit, unchanged input), and C0/P0 as AX 0 / -25205 despite normal owner/client exits. Windows ARM continuous and many-100MiB outcomes remain inconclusive. These existing surfaces are not fixed by menu observability. Four single-binary AOT jobs succeed, but green AOT jobs never override nested diagnostic failures or the failed Windows strict suite.

## Repaired strict-suite and posted-guard followup — CI 36831903238

Source `a13a9b0b2051089eca051e650a1314a58207ab6d`, [completed CI 36831903238](https://github.com/kleedaisuki/mote/actions/runs/36831903238), 2026-10-01. This append does not replace the preceding initial-run audit or recreate the test-repair investigation. All ten jobs now conclude success. Actual strict logs on **both Windows and macOS report Mote.Tests 1362/1362, Themes 14/14 and Configuration 9/9**, zero failed/skipped tests. Four single-binary AOT jobs and all three strict native clipboard jobs succeed.

### Evidence retained and independently checked

Artifacts were enumerated with `per_page=100` (87 artifacts, no truncated default-30 listing). Completed strict/AOT job logs, all four uploaded diagnostic summaries, ordinary JSON reports/traces and recovery reports/traces are retained in `.cache/ci-36831903238-menu-evidence/`. `audit.py` and `independent-audit.json` record this followup: all 15 retained ordinary edited/reopen traces were reparsed with the complete-prefix reader and native route-aware Save contract, and all 16 recovery traces with their separate synthetic contract, using `python -B`. No production edits, experiments or push were performed.

**Ordinary JSON is 7/8, not 8/8.** The green non-gating macOS x64 AOT job contains a failed 100 MiB sample. The following numbers are actual retained observations, not inferred from job conclusion:

| RID / MiB | Sample result | Edited / reopen trace rows | Numeric editor / reopen exits | Independent menu ready / entered / true / false |
| --- | --- | --- | --- | --- |
| win-x64 / 1 | pass | 56 / 13 | 0 / 0 | 0 / 0 / 0 / 0 (Mac boundary unobserved) |
| win-x64 / 100 | pass | 61 / 18 | 0 / 0 | 0 / 0 / 0 / 0 (Mac boundary unobserved) |
| win-arm64 / 1 | pass | 56 / 13 | 0 / 0 | 0 / 0 / 0 / 0 (Mac boundary unobserved) |
| win-arm64 / 100 | pass | 64 / 18 | 0 / 0 | 0 / 0 / 0 / 0 (Mac boundary unobserved) |
| osx-x64 / 1 | pass | 63 / 12 | 0 / 0 | 1 / 1 / 1 / 0 |
| osx-x64 / 100 | **failed, censored** | **32 / absent** | **-9 / null** | **1 / 0 / 0 / 0** |
| osx-arm64 / 1 | pass | 69 / 12 | 0 / 0 | 1 / 1 / 1 / 0 |
| osx-arm64 / 100 | pass | 71 / 17 | 0 / 0 | 1 / 1 / 1 / 0 |

All seven passing samples have exact expected saved SHA256, unchanged original fixture, fresh reopen, one independently classified successful native Save chain with captured version 1, and normal session termination. The three successful Mac reopens retain only a menu-ready checkpoint. All menu observations have empty attributes, zero duration and their own session parent; unavailable count is zero throughout. No request/event causal correlation is asserted.

The failed x64 100 MiB sample times out at `save-exact-bytes` after one attempted `CGEvent.postToPid` key-down/up pair, with `execution_acknowledged=false`. The working file still has the original hash, original fixture is unchanged, cleanup terminates the editor with numeric exit -9, and no reopen is attempted. Its retained edited trace is **11,167 bytes / 32 valid complete rows**, with no discarded partial row, one menu-ready checkpoint, no observed menu candidate entry/return, **no recorded Save request**, and no normal session terminal. The witness is ready-only and censored. Active/frontmost/main and source-focused observations are positive before dispatch and at failure; those external observations do not acknowledge target execution. Zero retained entry counts and no retained drop record **do not certify callback nonexecution, loss-free transport, or the root cause**. This is renewed evidence of the historical intermittent Mac Save failure, not a new routing fix.

### Summary rendering and actual native regression

All four uploaded `evidence-summary.json` inventories agree with these raw sample results. Both Mac completed logs actually print the new fixed menu-count Markdown, including setup-ready/unavailable, entry/true/false in success/failure/cancelled/skipped order, explicit normal-exit-observed versus censored boundary, and the warning that counts are independent checkpoints, not Save routing or request edges. The x64 rendered row visibly reports 100 MiB failed, editor exit -9, reopen null, requests 0, censored boundary and ready-only counts. JSON-only storage or green-job inference is not substituted for this rendered evidence.

Both Mac Flow diagnostics execute successfully and print `mote-native-mac-flow-rendering-ready` (x64 07:47:22 UTC; ARM64 07:48:52 UTC), after the existing in-process menu ABI control. Ordinary successful native Save/reopen and the strict native workflows provide normal-path regression coverage at this SHA. **They do not prove native target fault injection for the new posted-exception guard.** Its current fault coverage is managed/helper and uncreated-shell tests; a real created native target throwing through its posted callback is not demonstrated by this run.

All **16/16 synthetic recovery cases pass** their actual report assertions and independent trace classifications: each RID has held / normal / receipt-save / receipt-save-as rows 5 / 12 / 1 / 1; normal exits 0 and classifies success; the other three classify censored and exit 1 on Windows or -9 on Mac. These are infrastructure recovery controls, not ordinary document persistence or physical input tests.

Existing non-gating failures remain visible in both Mac summaries: original Grid 40/41 with Swift exit 1, normal editor close and unchanged fixture; same-client C0/P0 AX replies 0 / -25205 with explicit owner/client exits 0. Windows ARM ordinary Continuous and many-100MiB summaries remain inconclusive. Strict-suite repair, normal posted-guard regressions and improved summary rendering do not remove those product gaps, establish Mac Save reliability, or establish physical-key/physical-pixel end-to-end coverage.
