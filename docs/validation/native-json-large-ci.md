# Independent ordinary Native AOT JSON pilot CI audit

## Identity and verdict

2026-10-01: independently audited [run 36799464145](https://github.com/kleedaisuki/mote/actions/runs/36799464145), source
`d7b24730b17b51d49dc0f384fff5dc2e27c99f98`. This is the first retained
four-native-RID execution of `benchmarks/NativeJsonLargeAcceptance/probe.py`.
The expectations come from its documented ordinary-product, exact-byte,
version/causal/normal-exit contract, not the green job color.

**The pilot is incomplete on all four RIDs.** Windows 1 MiB cases genuinely
pass the whole scoped contract; Windows 100 MiB cases fail at the Save byte
oracle. Both Mac sizes fail before source-binding acknowledgement. No 100 MiB
end-to-end acceptance, physical-paint observation, real IME result or tail SLA
was established. Do not silently promote successful earlier phases into a
successful workload.

| RID | 1 MiB | 100 MiB | Pilot report | Actual nested exit |
| --- | --- | --- | --- | --- |
| win-x64 | pass / complete, normal close and fresh GUI reopen | failed / save-exact-bytes / TimeoutError | incomplete | 1 |
| win-arm64 | pass / complete, normal close and fresh GUI reopen | failed / save-exact-bytes / TimeoutError | incomplete | 1 |
| osx-x64 | failed / launch-to-source-bound / RuntimeError | same | incomplete | 1 |
| osx-arm64 | failed / launch-to-source-bound / RuntimeError | same | incomplete | 1 |

All four portable protocol/artifact test executions passed **9/9**. Those tests
do not execute native OS input and cannot override these pilot failures. Each
non-gating nested step prints its `incomplete` summary, throws `Native JSON
source pilot incomplete (exit 1)` and ends with `Process completed with exit
code 1`. All four Single-binary AOT jobs nevertheless conclude success because
the nested step uses `continue-on-error`. The overall run is separately failed
by `Test / macos-latest`: `Native_recovery_export_then_explicit_save_keeps_versions_and_identity_distinct`
timed out awaiting a fake-shell controller state, `Plain text · Complete · v2`,
`shell-errors=2`, at `NativeSaveRecoveryTests.cs:88`. That managed-test failure
is **not** the Mac JSON pilot's source-binding error and is not used to diagnose it.

## Published payload and observer provenance

Python **3.14.7**, native x64/arm64 observer architecture matches each RID.
Both Mac Swift clients actually compiled successfully, with source SHA-256
`bd4f387ecf3c8009de9243cc14f77354330d601a52986a33b92573bba26fd72f`.
Driver and artifact-auditor hashes were independently recomputed from the run's
Git blobs, accounting for Windows CRLF versus Mac LF checkout. They match the
reports; line-ending differences are not different executable logic.

| RID | Published executable bytes | Pre/post pilot executable SHA-256 |
| --- | ---: | --- |
| win-x64 | 7,105,024 | `2df493b5e3d7402b636c2ca540e4377f00b48a78426ab88b0dcbab7ad16460d9` |
| win-arm64 | 7,244,288 | `520ca50236c180db45316853a0b3e465546d377ed0ba499ad31ce1b0957bad08` |
| osx-x64 | 16,842,440 | `ef98590aa12322ef0ca1f8d000fe27308beb59c77a29174c1c9c60ce6ded9305` |
| osx-arm64 | 16,505,608 | `0aa07f3fea66377e28582462b11611cd561b47e565a434bff5d909b364403f96` |

The pre/post hash agrees per report. The pilot inventory check requires exactly
one regular executable in the publish directory before launching. Separate
`native-inventory-*` artifacts for ARM Windows and both Macs corroborate one
payload, exact byte size, zero other payloads and zero bundled native libraries.
Windows x64's publish-inventory JSON is present in its raw job log with the
same single payload/byte count, but no separate x64 inventory artifact is listed.
No published executable was uploaded in this run; the audit therefore verifies
reported identity and corroborating inventories/logs, **not an independent local
rehash or execution of the downloaded product binary**.

## Exact file and trace evidence

Independent bounded-batch reconstruction of the documented ASCII JSON array
agrees with both declared input and single `r`→`X` saved hashes on every RID:

| Bytes | Original SHA-256 | One-byte edit SHA-256 |
| ---: | --- | --- |
| 1,048,576 | `9004bc8156e480461b02205536ee607414186c776078a8f35b20716c7e668355` | `7fb64708951aa7dc1337a43c841aa7a5e0f3807f53875da4e1ea965719248b3a` |
| 104,857,600 | `11c596afa32f508d22cf7704eb458200fd66c8ec05af3d9f4a384aeb0570c5db` | `f11fa45a8ff38a7fcf4c8cb925ed3775294cb98169e2aa75077b303d22459d09` |

Both Windows 1 MiB reports witness Complete/zero diagnostics at v0 and v1,
one edit attempt, disk unchanged before Save, saved/final working hash equal to
the edit oracle, immutable original hash unchanged, and two normal exits. Raw
edit-session traces contain 26 records each. Independently checked successful
single terminal roots, session/span uniqueness, no drops, exact endpoint counts,
v0 startup/open, pre-edit v0 action and v1 commit/presentation/draw. Open draw's
parent is open-to-editable; edit draw/action's parent is edit-to-presentation.
Fresh reopen traces contain no edit/commit/Save/draw-edit operation. Retained
file SHA-256 matches the report's trace hashes; causal and endpoint integrity
are pass. There is no observed false-positive trace acceptance in these cases.

The unversioned engine `document.open`, `document.save` and `save.completed`
records **do not themselves certify a revision**. Accepted native status and
independently checked exact saved bytes are separate witnesses; versions are
not fabricated for absent attributes. This preserves the prior successful-action
and conflicting-version rejection contract rather than relaxing it for CI.

Both Windows 100 MiB reports reached bound source, Complete v0, one acknowledged
edit, Complete v1 and unchanged working disk before Save. After the 60-second
Save oracle deadline the final working hash still equals the original, not the
edit oracle. Both processes were forcibly cleaned up; no normal exit or GUI
reopen is certified. Each retained 100 MiB trace is **0 bytes**. Buffered trace
loss prevents identifying whether Save dispatch, replacement, another failure
or a modal prevented completion. Do not infer a replacement HResult, modal
purpose, no Save attempt, or reliable original preservation from that absence.

Both Mac sizes have zero edit attempts, forced cleanup, unchanged immutable
and working hashes, and **no uploaded trace files**. `mac_client_compiled=true`
proves compilation, not source readiness. The reports classify RuntimeError,
not PermissionError/blocked, but do not preserve the bounded client's first
failed attribute or capability result. They cannot distinguish tree/ownership/
source-contract failure from other runtime observation failures. There is **no
evidence here establishing a TCC denial**, source focus, successful Quartz edit,
semantic certification or normal terminal session. No permission prompt or
TCC-state workaround was attempted.

## Timing endpoints, not latency claims

These are individual trace-on observations on just-written, non-evicted files.
The two fresh process sizes are not a latency distribution. Parent clocks include
observer overhead and 50 ms polling; child clocks begin after configuration and
must not be subtracted from parent clocks. Native draw callback return is not
compositor presentation, whole-window paint, physical key acknowledgement or
photons. Failed cases have no successful full-workflow latency.

| Windows 1 MiB endpoint (ms) | x64 | ARM64 |
| --- | ---: | ---: |
| Parent launch → bound source | 162.0263 | 154.2519 |
| Child startup → editable | 94.615 | 81.548 |
| Child open → editable | 18.086 | 13.410 |
| Child open → source draw return | 26.925 | 20.580 |
| Parent edit dispatch → source acknowledgement | 13.3568 | 13.9150 |
| Child edit → source draw return | 9.426 | 10.393 |
| Child edit → semantic presentation | 155.872 | 130.789 |
| Child Save | 17.014 | 34.454 |

The 100 MiB Windows parent source acknowledgement is 618.3253 ms (x64) /
457.8447 ms (ARM64), edit acknowledgement 12.6922 / 13.4274 ms. These earlier
phase observations cannot be promoted to full-workflow pass, cross-architecture
performance superiority or p95 acceptance. No child trace endpoints survive
for those samples, and no Mac source-binding timing was achieved.

## Reproduction and discriminating next investigation

Artifacts were downloaded only under repository `.cache/ci-36799464145-json-<RID>`.
Raw complete logs: `.cache/ci-36799464145-json-all.log`. Independent assertions
and structured output: `.cache/ci-36799464145-json-audit.py` and
`.cache/ci-36799464145-json-independent-summary.json`. No product/harness was
modified or native process rerun for this audit.

```powershell
gh run view 36799464145 --log > .cache/ci-36799464145-json-all.log
gh run download 36799464145 --name native-json-large-win-x64 --dir .cache/ci-36799464145-json-win-x64
# Repeat the artifact command for win-arm64, osx-x64 and osx-arm64.
python .cache/ci-36799464145-json-audit.py
```

The independent provenance/corpus/retained-trace assertions pass for all eight
samples; that means the reported failures and scoped successes are corroborated,
**not that all samples pass the product workflow**.

The next useful Windows probe is bounded read-only owned-window/modal metadata
at the first Save failure plus safe normal terminal evidence, without another
Save attempt or broad retry. The next useful Mac change is retaining the first
bounded client failure/capability classification without source text, foreign
metadata or changing TCC. These distinguish causes that the present reports
collapse; repeating the same opaque pilot would not resolve them.

## Follow-up: current-source title-only Save observation

Independently audited [run 36800944850](https://github.com/kleedaisuki/mote/actions/runs/36800944850),
source **`99fbe395308816e3d1d2381ace58c3d5800811ec`**, on the same date. This
supersedes the first pilot's Windows acceptance gap, **not its retained historical
failure evidence**. It executes freshly published current-source AOT binaries;
it is not the earlier local stale-binary harness check described in the pilot
README.

The updated driver waits for owned native chrome to acknowledge a clean document
before reading the Save target for its byte oracle. Ordinary Python reads on
Windows can deny DELETE sharing during atomic replacement; the earlier poll
opened/hashes the target while Save may still be committing. Removing that
observer interference is a harness correction, not relaxing the exact-byte
requirement or retrying Save. The new observed success is consistent with that
mechanism, but the previous empty traces did not capture its actual failure
HResult, and the source/binary changed between runs. This is not a controlled
causal proof of every previous failure or a production reliability distribution.

| RID | 1 MiB | 100 MiB | Report / actual nested outcome |
| --- | --- | --- | --- |
| win-x64 | complete scoped pass | complete scoped pass | pass / pass marker, no pilot exit-1 |
| win-arm64 | complete scoped pass | complete scoped pass | pass / pass marker, no pilot exit-1 |
| osx-x64 | failed at launch-to-source-bound | same | incomplete / exit 1 |
| osx-arm64 | failed at launch-to-source-bound | same | incomplete / exit 1 |

Portable protocol/artifact tests now pass **12/12 per RID**. The raw logs contain
the Windows `Ordinary Native AOT JSON source pilot passed` marker after the
two-case `status=pass` summary. Both Mac steps emit the incomplete summary and
explicit pilot exit-1 error. Their masked step/job colors remain irrelevant.
The overall run separately fails the strict `Test / windows-latest` solution-test
step; that result is not conflated with either successful Windows native pilot
or failed Mac pilot.

### Current payload and independent bytes/trace checks

Python remains 3.14.7 with native observer architecture matching each RID. All
four downloaded separate publish-inventory artifacts corroborate exactly one
payload with zero non-executable payloads and zero bundled native libraries.
The binary before/after identity matches per report:

| RID | Bytes | SHA-256 |
| --- | ---: | --- |
| win-x64 | 7,105,024 | `85d3b300cb1756d5c21a63bb2280a73a06b7d011c903aae7f515c29d93948634` |
| win-arm64 | 7,244,288 | `f8e6f84a376567b8c801e103b21b6de50797b973f518aaea5933cf7f9378d97f` |
| osx-x64 | 16,842,720 | `9e60aea84e588339cb9a7ce9d36afb2a9b27629c73463ebe2c4f9a593a3564a1` |
| osx-arm64 | 16,489,288 | `ec1e61195123ab387558873887cdd90819323ed74b5cf254e6e45f8d52351d5b` |

The independent audit recomputes driver/auditor/Swift source hashes from this
run's Git blobs and native checkout line endings, reconstructs both fixture
and edited hashes without trusting report constants, and hashes retained trace
files. All assertions agree. As before, reported executable hashes and inventories
are corroborated; no product binary was downloaded/rehashed or rerun locally.

**All four Windows size/architecture cases** have Complete zero-diagnostic native
status at v0 and v1, exactly one edit attempt, unchanged disk before Save, exact
saved/final working bytes, unchanged original, normal initial exit, fresh GUI
reopen with unchanged saved bytes, and normal reopen exit. The independent raw
trace check verifies single successful terminal roots, successful counted actions,
v0/v1 progression, unique sessions/spans, correct open/edit draw parents, matching
trace hashes, and no edit/Save in reopen sessions. Both report causal/endpoint
audits pass, with no drops. Main/reopen record counts are x64 **26/13** (1 MiB)
and **31/18** (100 MiB); ARM64 **26/13** and **34/18**. Extra bounded background
operations do not alter required endpoint counts. The unversioned engine I/O
limitation remains unchanged; exact bytes and native status remain separate
witnesses rather than invented I/O revisions.

### Mac capability and failure guard discrimination

Both Mac Swift clients compile and all four Mac samples retain a failure
observation with **`trusted=true`, `post_event_access=true`**, `guard_stage=window-count`,
and **`ax_error=-25204`** from `AXUIElementGetAttributeValueCount(..., "AXWindows", ...)`.
The samples do not establish source binding, full semantics or focus; no input
events are dispatched and no edit is attempted. Immutable and working hashes
remain the original. One bounded owned close is attempted, but it also produces
RuntimeError; forced cleanup is still necessary and no trace files are uploaded.

This is a **failed post-error read-only observation**, not a guaranteed capture
of the very first failing AX operation. `window_count=0` is the initialized
out-parameter on an unsuccessful API call and **does not prove there are zero
actual product windows**. The observed permission preflights are granted, so
calling this a TCC-denied run is unsupported. Granted preflights also do not prove
successful AX communication or imply product/source corruption. No TCC mutation,
activation workaround, foreign metadata read, Save retry or modifying transaction
occurred. The remaining question is whether the guarded initial AX read raced
window/server readiness or encountered a persistent communication issue; bounded
read-only readiness discrimination is appropriate before declaring a product
defect or broadening permissions.

### Instrumented descriptive endpoints

These single observations retain the same cache/clock/paint exclusions above.
They are not p95, trace-off baselines, cross-architecture superiority or screen
presentation measurements. No Mac source-binding duration was achieved.

| Windows case | Parent bound source ms | Parent edit ack ms | Child open→editable ms | Child open→draw return ms | Child edit→draw return ms | Child edit→semantic presentation ms | Child Save ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| x64 / 1 MiB | 169.4535 | 13.1626 | 19.311 | 30.831 | 9.612 | 145.644 | 18.278 |
| x64 / 100 MiB | 578.2432 | 13.8596 | 421.494 | 429.436 | 9.654 | 518.751 | 881.940 |
| ARM64 / 1 MiB | 175.2245 | 13.6819 | 16.376 | 25.094 | 9.207 | 132.266 | 39.642 |
| ARM64 / 100 MiB | 408.1422 | 13.4381 | 306.876 | 313.892 | 9.616 | 514.757 | 1078.867 |

Current run artifacts/logs: `.cache/ci-36800944850-json-<RID>/` and
`.cache/ci-36800944850-json-all.log`. Independent assertions and output:
`.cache/ci-36800944850-json-audit.py` and
`.cache/ci-36800944850-json-independent-summary.json`. The reused assertions are
bound to **this** run/commit, not the prior results; they pass across all eight
retained samples while preserving the four Mac failures. Only this document
was changed for this audit.

## Follow-up: bounded initial AX count readiness

Independently audited the completed [run 36802378381](https://github.com/kleedaisuki/mote/actions/runs/36802378381),
source **`c453506c84e249dbff7c73748140314f6760ff33`**. The initial Mac window-count
`cannotComplete` response is now an observed, read-only pending result under
the existing deadline; unsuccessful count output is unavailable rather than
fabricated as zero. Count and window-copy outcomes are recorded separately.
This advances readiness evidence without authorizing input until the owned
source/focus/range witnesses succeed. The prior two runs remain historical
evidence, not claims about this changed harness/source.

**Do not summarize this run as "both Macs pass 1 MiB and fail 100 MiB".** Its
actual size/architecture outcomes differ:

| RID | 1 MiB | 100 MiB | Report / actual pilot result |
| --- | --- | --- | --- |
| win-x64 | full scoped pass | full scoped pass | pass / actual pass marker |
| win-arm64 | full scoped pass | full scoped pass | pass / actual pass marker |
| osx-x64 | failed at save-exact-bytes / TimeoutError | full scoped pass | incomplete / exit 1 |
| osx-arm64 | full scoped pass | failed at initial-whole-document-semantics / RuntimeError | incomplete / exit 1 |

All four portable protocol/artifact suites pass **15/15**. The overall CI and
all four AOT jobs are green, but **both Mac nested pilots actually exit 1**;
their incomplete summaries and explicit pilot-error lines survive in raw logs.
Windows summaries and emitted pass markers corroborate their full two-size
results. The green overall run cannot close Mac pilot acceptance.

### Identity and independent pass evidence

Fresh current-source native payload identities are:

| RID | Bytes | Before/after SHA-256 |
| --- | ---: | --- |
| win-x64 | 7,105,024 | `526c45d6f6df34861f4fe0b86d3c27d30c5d37bd8d7ac68d22ff25fb51408b15` |
| win-arm64 | 7,244,288 | `1d1050838a13150671dd71c41095e55bd6fc37e99ba9a9dc0ae3eb9ec1e40ef7` |
| osx-x64 | 16,842,720 | `1b24d7357eccca349e73884221037ab3feaf2f19781e6a168cd85a6621126d21` |
| osx-arm64 | 16,489,288 | `3cc3683d0bb7f57920762ea08e131b37433fc09236f8680dd9ae563a3a180e91` |

Native observer architecture, exact Git source/checkout hashes, same corpus
and single-byte edit oracles, before/after binary hashes and all four separate
single-payload inventories match. Both Swift clients compiled. As in previous
audits, this is uploaded report/inventory provenance, not a locally downloaded
product executable rehash or execution.

The **six successful cases**, including Mac ARM64 1 MiB and Mac x64 100 MiB,
independently satisfy exact source/save/final hashes, immutable original,
Complete zero-diagnostic v0/v1 observations, one edit attempt, unchanged disk
before Save, normal initial exit, fresh GUI reopen with unchanged saved bytes,
normal reopen exit, and audited successful trace endpoints/versions/parents.
Their trace file hashes agree with reports. Raw successful terminal roots and
no-edit/no-Save reopen contracts pass; there is no evidence of trace false
positives. Mac ARM64 1 MiB main/reopen traces have **35/11** records; Mac x64
100 MiB **40/16**. I/O revisions remain unavailable when omitted; native status
and byte correctness remain separate witnesses.

### Readiness versus later failure

Every Mac sample now achieves a source observation with one owned window,
successful count/copy calls, one exact-length source, source focus, caret 0,
and permission preflights true. The count-readiness summary records first AX
error -25204 followed by last count error 0 and first ready acknowledgement:

| Mac sample | Source observation attempts | Driver attach → first ready ms | Parent launch → bound source ms |
| --- | ---: | ---: | ---: |
| x64 / 1 MiB | 2 | 399.180760 | 403.051354 |
| x64 / 100 MiB | 19 | 4008.251812 | 4011.828249 |
| ARM64 / 1 MiB | 4 | 1310.631958 | 1315.942459 |
| ARM64 / 100 MiB | 6 | 957.182875 | 959.094750 |

These counters and times concern the bounded observer, not product CPU work,
compositor presentation or a statistical startup SLA.

**Mac x64 1 MiB:** reaches Complete v0, one witnessed edit, Complete v1 and
unchanged disk before Save. The clean-title Save wait times out; final working
hash is still the original, not the edit oracle. Post-error observation remains
ready/Complete/focused, dirty, caret 10, count/copy success, exact 1,048,576 source
units. One owned close fails with RuntimeError and forced cleanup follows; the
retained trace is 0 bytes. It proves no accepted clean/exact Save result, **not
whether Command-S was lost, a product save failed, or another operation blocked**.
No Save resend was attempted. The 499 total observation attempts in the failure
summary include earlier stages and are not 499 modifying actions.

**Mac ARM64 100 MiB:** source readiness succeeds with exact 104,857,600 units,
then the external initial-whole-document Complete wait aborts. The post-error
observation has **count success/error 0/count 1**, but
**window-copy error -25204 / copy count unavailable**, `guard_stage=window-read`.
This is not the earlier window-count failure and not proof of absent windows.
Permission preflights remain true. No edit was attempted; exact original and
working bytes are unchanged.

The ARM64 failure's owned cleanup **does exit normally** (`failure_cleanup_normal_exit=true`),
retaining a 4,838-byte, **14-record** trace with one successful terminal root,
successful open/editable/open-draw and one successful initial **Visible**
parse/publish/presentation sequence for the 100 MiB document at v0,
`analysis.published.count=0`, and no edit or Save. Independent checks confirm
unique identities, valid parents, no drops and successful statuses. The source
size bucket describes the document, **not the extent parsed**; delivered count 0
does not establish an exact whole-document diagnostic total. The trace has no
analysis-scope or completeness attribute and does **not certify Full completion**.

This interpretation was independently checked against the run's `c453506`
sources, not the subsequently edited worktree. `NativeEditorController` uses
`FullAnalysisLimit=2 * 1024 * 1024`, so the initial 100 MiB request is Visible.
`NativeIdleFullAnalysis.DelayFor` waits **15 seconds** above its 32 Mi-unit
medium limit before opportunistic Full work, unless explicitly promoted. This
sample dispatches no demand/navigation/edit action. The failed observer summary
ends at **13.5937575 seconds** from driver attachment; the successful terminal
session lasts **13.723412 seconds**. Both are earlier than the idle delay itself,
which starts after the initial visible result is offered. The only 100 MiB
parse span lasts **2,216 microseconds** and its presentation span **450 microseconds**;
they occur near opening, consistent with the initial bounded Visible work, not
the later certified idle Full pass.

Therefore the trace establishes successful initial Visible work and cleanup,
**not successful global/full analysis**. It also does not establish a parser
failure: the external Complete wait was interrupted by AX window-copy
communication before the idle Full opportunity. `initial_complete_zero_diagnostics_version`
is absent, and whole-document Complete was never observed. Cleanup-only normal
exit still **does not** certify the unexecuted edit/Save/reopen workload or turn
its sample into pass. The correction changes interpretation, not the retained
raw trace facts or the sample's failed verdict.

### Endpoint boundaries and reproducibility

New Mac pass observations are descriptive only: ARM64 1 MiB parent edit ack
424.649292 ms, child edit→draw return 7.646 ms, semantic presentation 266.442 ms,
Save 8.649 ms; x64 100 MiB parent edit ack 464.950699 ms, child edit→draw return
10.068 ms, semantic presentation 529.717 ms, Save 2266.715 ms. Parent/child clocks
are not subtracted, physical paint/IME remain untested, and one case cannot
establish reliable small-file Save or cross-platform tail latency.

Artifacts and inventory downloads are under `.cache/ci-36802378381-json-<RID>/`;
raw logs `.cache/ci-36802378381-json-all.log`. Independent run/commit-bound
assertions and structured results are `.cache/ci-36802378381-json-audit.py` and
`.cache/ci-36802378381-json-independent-summary.json`; they corroborate all eight
sample outcomes, including the two distinct Mac failures. No native process
was rerun and no product/driver file was modified for this audit. The most
informative next checks discriminate bounded read-only AX copy readiness and
owned ordinary Save-command delivery separately; they should not retry writes
or hide a failed observed phase behind internal trace success.

## Follow-up: both Mac ARM sizes pass; x64 trace loss remains

Independently audited completed [run 36804628122](https://github.com/kleedaisuki/mote/actions/runs/36804628122),
current source **`42462d764fb5183d6ad8f03f30f9957bd54ebad5`**. This run preserves
the prior history while establishing a new scoped Mac ARM milestone. It is not
a rerun of the older product binary or an inference from a green CI job.

| RID | 1 MiB | 100 MiB | Actual pilot result |
| --- | --- | --- | --- |
| win-x64 | complete scoped pass | complete scoped pass | report pass + actual pass marker |
| win-arm64 | complete scoped pass | complete scoped pass | report pass + actual pass marker |
| osx-arm64 | complete scoped pass | complete scoped pass | report pass + actual pass marker |
| osx-x64 | failed / trace-audit / ValueError after exact Save and normal exit | complete scoped pass | incomplete / actual exit 1 |

All four protocol/artifact test executions pass **21/21**. The x64 Mac raw log
emits its incomplete summary and explicit pilot exit-1 error, despite the
non-gating step's masked success. The other three emit real post-execution
two-case pass summaries and markers. This is **not four-RID acceptance pass**.

### Provenance and independent contract checks

All four reports match source commit/native observer architecture, exact source
blob hashes with native checkout line endings, independently reconstructed
1/100 MiB input and edited-byte hashes, and pre/post binary identity. Separate
inventory artifacts corroborate one executable and no companion payload/native
libraries. Both Mac Swift clients actually compile. Reported payload identity:

| RID | Bytes | Before/after SHA-256 |
| --- | ---: | --- |
| win-x64 | 7,105,024 | `88bdc4c03de323bcaf91fbf2b37e107cbaba5646e6accf27b0e4cfea4dd5af89` |
| win-arm64 | 7,244,288 | `5ed3fb378a992d0e6c755e13828d0a37aa7146ccf98fe7a294c19ca928ed9e5c` |
| osx-x64 | 16,842,720 | `5af37718c82a3d6dcc7d4ce426c6a43d89550ab881f2f4cba3d30d1df305a424` |
| osx-arm64 | 16,489,288 | `fafaf2dab0369fa6dfc52e92e31a0ee8917003cda38ac045ccacbe6bb277c05b` |

The seven complete cases satisfy exact Save/final working bytes, immutable
original, Complete zero-diagnostic native v0/v1 observations, one edit attempt,
disk unchanged before Save, normal initial exit, fresh GUI reopen with exact
saved bytes, normal reopen exit, matching raw trace hashes, and successful
counted action/terminal/version/causal-parent contracts with zero trace drops.
Mac ARM main/reopen trace counts are **34/12** (1 MiB) and **37/16** (100 MiB);
Mac x64 100 MiB **40/16**. No-edit/no-Save reopen evidence is independently
checked. Unlike the prior ARM100 cleanup-only Visible trace, this ARM100 sample
has **actual observed whole-document Complete v0 and v1**, full workflow and
reopen evidence; its pass does not depend on interpreting generic parse spans
as certified Full. I/O records without revision attributes remain unversioned.

### Mac Save attempts are not delivery acknowledgements

All four Mac samples record `save_command_attempted=true` with method
`CGEvent.postToPid`, status **`attempted-posts-no-delivery-acknowledgement`**,
`attempted_events=2`, and **`execution_acknowledged=false`**. The action guard
rechecks the same target PID, trusted/post-access capabilities, focused dirty
source, exact full source length, caret 10 and empty selection. The observer
does **not** claim that Quartz acknowledged delivery or execution. Successful
clean-title observation, independent exact file bytes, successful Save records
and normal sessions are separate outcome witnesses; they do not turn the
posting API's void return into a delivery acknowledgement. No Save retry or
global key posting is used.

Initial source-readiness summaries show count pending observations **3/2** on
ARM 1/100 MiB, **5/2** on x64 1/100 MiB; first count error -25204 and last 0.
Each initial summary has copy-pending count 0 and successful first/last copy
errors 0. These counters describe bounded initial observation, not proof that
every future AX read is reliable or that a pending count authorized input.

### Mac x64 1 MiB: exact Save succeeds, zero-drop trace contract fails

The x64 1 MiB case is materially different from its previous Save timeout. It
has Complete v0/v1, acknowledged edit, unchanged disk before Save, **exact saved
and final working hash equal to the edit oracle**, unchanged original, and
normal initial exit. It then fails the trace audit before GUI reopen. The
retained raw trace hash matches the report and contains successful required
endpoint/action records with correct reported versions, including Save and
one successful terminal root. However, an explicit **`telemetry.dropped` record
with `attributes.count=1`** establishes one dropped record. The auditor reports
`issues=["trace-dropped-records"]`, `dropped_records=1`, endpoint issues empty,
but causal integrity `incomplete-or-invalid` and endpoint integrity `incomplete`.

The refusal is correct under the existing zero-drop acceptance contract. Do
not ignore the loss merely because all required endpoint records survived or
because saved bytes are correct. The lost record's identity/cause is not
recoverable here. There is no certified GUI reopen for this sample, no forced
cleanup, and no justification to label it a Save failure or complete workflow
pass. The actionable next investigation is bounded telemetry-drop attribution,
not a Save retry or weakened acceptance predicate.

### Reproducibility and timing limits

Artifacts/inventories are under `.cache/ci-36804628122-json-<RID>/`; raw logs
`.cache/ci-36804628122-json-all.log`; independent assertions and output
`.cache/ci-36804628122-json-audit.py` and
`.cache/ci-36804628122-json-independent-summary.json`. The script explicitly
checks `telemetry.dropped`'s **count** attribute, confirms the failed x64 sample's
exact Save and drop witness, and separately verifies all seven passes. It
corroborates all eight outcomes without promoting the incomplete one.

Descriptive ARM parent source binding is 1135.373958 ms (1 MiB) /
1190.728292 ms (100 MiB); edit acknowledgement 640.374916 / 503.878917 ms;
child Save 38.765 / 380.014 ms. Mac x64 100 MiB parent source binding is
7111.696544 ms and child Save 2725.992 ms. These noisy single trace-on observations
do not establish a startup/edit tail SLA or architecture superiority. Physical
paint, real keyboard/IME and screen-reader acceptance remain outside the pilot.
No native pilot was rerun locally, and only this document was edited for the audit.

## Follow-up: first complete four-RID ordinary JSON pilot

Independently audited [run 36806841387](https://github.com/kleedaisuki/mote/actions/runs/36806841387),
current source **`48a371108a67370bc46cea52299dc5c6561e9cc5`**. This source includes
the bounded admitted-producer telemetry shutdown change `ea3035f` and its
independent review. **All eight 1/100 MiB native cases now pass the existing
complete scoped contract**, including the previously refused Mac x64 1 MiB
case. Historical failures above remain valid evidence for their exact sources.

| RID | 1 MiB | 100 MiB | Main/reopen trace records, 1 MiB | Main/reopen trace records, 100 MiB |
| --- | --- | --- | ---: | ---: |
| win-x64 | complete pass | complete pass | 26 / 13 | 31 / 18 |
| win-arm64 | complete pass | complete pass | 26 / 13 | 34 / 18 |
| osx-x64 | complete pass | complete pass | 32 / 11 | 40 / 16 |
| osx-arm64 | complete pass | complete pass | 33 / 11 | 37 / 16 |

This verdict uses raw artifacts and nested-step execution, **not green jobs**:
every RID emits `status=pass,samples=2`, the actual post-execution pass marker,
and no pilot failure/exit-1 path. Portable protocol/artifact tests are **21/21**
per RID. The non-gating nature of the pilot does not change its evidence contract.

### Exact workload and session evidence

Each RID has exactly two distinct-sized samples: **1,048,576** and
**104,857,600** bytes. Independent bounded reconstruction verifies the original
and one-byte edited hashes recorded earlier. Every sample witnesses immutable
original, disk unchanged before Save, exact saved/final working bytes, native
Complete zero-diagnostic v0/v1, exactly one edit attempt, normal initial exit,
fresh GUI reopen with unchanged saved bytes, and normal reopen exit.

The run uses eight fresh ordinary GUI editing processes and eight fresh GUI
reopen processes, plus eight separate `--check-runtime` launch-control processes.
The independent retained-trace check finds **16 distinct GUI session identities**,
one successful terminal per session, expected open/edit/Save counts and versions,
proper open/edit draw parents, matching trace hashes, and no edits or Saves in
reopen sessions. **All 16 sessions have zero dropped records**, with no positive
`telemetry.dropped.count` witness. The previous x64 small case's saved-byte
success is now supplemented by a valid zero-drop trace and actual GUI reopen;
its earlier audit refusal has not been weakened or retroactively erased.

This is an observed hosted success after the producer-drain change, not a claim
that trace drops can never occur or that one run statistically proves repair
reliability under every admission/shutdown race. The specific admitted-producer
mechanism and directed tests are separately documented in
`docs/reviews/trace-drop-normal-exit.md`. No retries or predicates were relaxed
to produce this pilot pass. Engine I/O spans lacking revision attributes remain
unversioned; exact file and native version witnesses stay separate.

### Mac attempt versus observed outcome

For all four Mac size/architecture cases, Save records retain method
`CGEvent.postToPid`, **two attempted events**, status
`attempted-posts-no-delivery-acknowledgement` and
**`execution_acknowledged=false`**. Guards certify matching PID, granted preflights,
focused dirty source, full exact source length and caret 10 with empty selection
before the attempt. The posting API has no delivery acknowledgement; independent
clean-title observation, exact saved bytes and successful Save/session traces
certify the scoped resulting behavior. This remains synthetic process-specific
input, not physical-key, global foreground, real IME or screen-reader evidence.

### Published current-source identity and reproducibility

Native Python 3.14.7 architecture matches each RID. Independent hashes of the
run's driver/auditor/Swift Git blobs agree with native checkout hashes; both
Swift clients compiled. Four separate inventory artifacts confirm one executable,
zero other payloads and zero bundled native libraries. Report pre/post identities:

| RID | Bytes | SHA-256 |
| --- | ---: | --- |
| win-x64 | 7,107,072 | `7261ddd62cd4f34cb963c618b2a9732256bed4f7c70497939c1ab2a13eaeade0` |
| win-arm64 | 7,247,360 | `b6f7f3def82dd3ec3be3dc51076a316c48057ac4164670bf0b9a37246c8db6c2` |
| osx-x64 | 16,849,104 | `b124339fe32cbf0fd4c9c7ac5c648bd150eabf3aa75525eb84ea4a82ff83c20b` |
| osx-arm64 | 16,508,120 | `356dee6e2b531435ca12250f0dad6ea52543bb622803cd91d8a806b9614f9cc0` |

No downloadable product binary was independently executed or rehashed locally;
inventory and reported identity are corroborated without expanding that scope.
Artifacts/inventories: `.cache/ci-36806841387-json-<RID>/`; raw logs:
`.cache/ci-36806841387-json-all.log`; independent assertions and structured output:
`.cache/ci-36806841387-json-audit.py` and
`.cache/ci-36806841387-json-independent-summary.json`. Assertions bind this exact
commit/run and independently check all eight success outcomes and 16 session
identities. No workflow or native pilot was rerun for the audit.

The milestone closes the **single-run, trace-on, cache-resident ordinary JSON
root-array capability pilot across four native RIDs**. It does not close startup/
editing tail SLA, trace-off overhead, disk-cold I/O, physical paint, true IME,
general JSON workloads, repeated-run reliability or the overall mote release goal.

## Follow-up: Mac ARM100 Save observation fails on a later green run

Independently audited completed [run 36809964231](https://github.com/kleedaisuki/mote/actions/runs/36809964231),
current source **`8d5796542b63e54e630933de38a2c68d864ca085`**. The prior
`36806841387` eight-case pass remains a valid **single-run** result, not proof
that every later run or changed source will pass. This later run has **seven
complete cases and one incomplete Mac ARM64 100 MiB case**.

| RID | 1 MiB | 100 MiB | Actual pilot result |
| --- | --- | --- | --- |
| win-x64 | complete pass | complete pass | pass / actual pass marker |
| win-arm64 | complete pass | complete pass | pass / actual pass marker |
| osx-x64 | complete pass | complete pass | pass / actual pass marker |
| osx-arm64 | complete pass | failed / save-exact-bytes / TimeoutError | incomplete / actual exit 1 |

The ARM raw log emits `status=incomplete,samples=2` and an explicit pilot exit-1
error. The other three emit real two-case pass summaries/markers. All four
portable protocol suites pass **21/21**; the overall CI is green. Neither fact
overrides the incomplete non-gating ARM pilot. Do not average this failure away
with the earlier pass or call the new Engine/UIA changes its cause merely from
commit order: source/binary changed and no handler-level failure was captured.

### Independently corroborated successes and identity

Exact current Git/driver/auditor/Swift hashes, native architecture, one-file
inventories, pre/post binary identities, independently reconstructed corpus/edit
hashes and retained trace hashes agree. Reported payload identities:

| RID | Bytes | SHA-256 |
| --- | ---: | --- |
| win-x64 | 7,130,112 | `663311f0aa475c6913a250510967027fb38d20799bdadde1339150681f2ed971` |
| win-arm64 | 7,272,448 | `34d34f55f41385b37d1d1038d8085e45a7e919ac172aea8f61745695b57ee732` |
| osx-x64 | 16,849,256 | `4c498c2b2d28946e024e89fa6a1a641ef4faba28b60914c63fced8b9eff8fece` |
| osx-arm64 | 16,508,264 | `8e9cd26c258216712817ff4ca4d1392b3d29d135ba69484e55ff27af129305c5` |

The seven successful cases have exact 1,048,576/104,857,600-byte Save and final
working oracles, unchanged immutable original, Complete v0/v1, one edit attempt,
unchanged disk before Save, normal initial exit and normal fresh GUI reopen.
Their **14 distinct retained GUI session identities** satisfy terminal/action/
version/parent/trace-hash and no-edit/no-Save reopen contracts with **zero drops**.
Main/reopen trace counts: Windows x64 **26/13, 31/18**; Windows ARM64
**26/13, 34/18**; Mac x64 **33/11, 37/16**; Mac ARM64 1 MiB **32/11**.
No new zero-drop failure is observed among these normal terminal sessions.

### ARM64 100 MiB: one attempted Save, no accepted Save outcome

The failed sample achieves source readiness with full exact **104,857,600**
source units, Complete zero-diagnostic v0, one acknowledged edit, Complete
zero-diagnostic v1 and unchanged working disk before Save. It records exactly
one Save command attempt: `CGEvent.postToPid`, **two attempted events**, status
`attempted-posts-no-delivery-acknowledgement`, **execution_acknowledged=false**.
The guard certifies matching PID, granted AX/post preflights, focused dirty
source, caret 10 with empty selection, and successful window count/copy. The
client command returns after 58.585875 ms; this is not command execution or
successful Save duration.

The 60-second clean-title wait times out. Final read-only observation is still
ready, Complete, focused and **modified=true**, with one exact-length source,
caret 10, window-count/copy error 0 and one window. Final working hash remains
the **original** `11c596af…`, not expected edit hash `f11fa45a…`; the immutable
original also remains exact. The failed sample's observation summary contains
649 read-only observations across stages, not 649 Save attempts. No modifying
action is retried, no global key is posted and no permission workaround is used.

One owned close is attempted and fails with RuntimeError; forced cleanup
follows. `normal_exit=false`, `reopen_normal_exit=false`; no GUI reopen occurred.
The sole retained 100 MiB trace is **0 bytes**, so there is no terminal/Save/
drop record to audit. This cannot establish whether Command-S reached the
handler, whether product Save encountered an error, or why the dirty state
persisted. It is specifically **failed external Save-outcome acceptance with
unacknowledged event posting**, not a proven product replacement failure, TCC
denial or parser failure. The producer-drain fix cannot promise flush after a
forced kill; absence of a terminal trace is not evidence of a normal-shutdown
drop recurrence or successful no-drop acceptance.

### Reproducibility and next discriminating step

Artifacts/inventories are under `.cache/ci-36809964231-json-<RID>/`, raw logs
`.cache/ci-36809964231-json-all.log`, independent run-bound assertions and output
`.cache/ci-36809964231-json-audit.py` and
`.cache/ci-36809964231-json-independent-summary.json`. Assertions corroborate
all eight reported outcomes, checking seven full passes separately from the
failed ARM's exact unchanged bytes and empty trace. No workflow/native process
was rerun and no product/driver file was edited for this audit.

The informative next probe needs to distinguish owned ordinary Save-command
delivery/handler entry from subsequent product I/O outcome without resending
writes or broadening foreground/TCC authority. Another green overall CI or
silent automatic retry would not resolve that uncertainty. Physical paint,
real keyboard/IME, tail latency and reliable repeated-run acceptance remain
outside the established single-run capability results.

## Follow-up: four-RID scoped pass on d9ddda9

Independently audited completed [run 36811953139](https://github.com/kleedaisuki/mote/actions/runs/36811953139),
source **`d9ddda97cedf98245fd499c8bb6a64cf64919633`**. All **eight** exact
1/100 MiB ordinary-product cases pass, including Mac ARM100. This run establishes
another current-source scoped pass; the preceding ARM Save failure remains
unattributed historical evidence, not a failure to erase or a cause proved fixed.

| RID | 1 MiB | 100 MiB | Main/reopen trace records, 1 MiB | Main/reopen trace records, 100 MiB |
| --- | --- | --- | ---: | ---: |
| win-x64 | complete pass | complete pass | 26 / 13 | 31 / 18 |
| win-arm64 | complete pass | complete pass | 26 / 13 | 34 / 18 |
| osx-x64 | complete pass | complete pass | 34 / 11 | 39 / 16 |
| osx-arm64 | complete pass | complete pass | 32 / 11 | 36 / 16 |

Each raw nested log emits its actual `status=pass,samples=2` summary and
post-execution success marker without the pilot error/exit-1 path. All four
portable suites pass **22/22**. No non-gating false green is present **for this
JSON pilot**; this does not generalize to unrelated diagnostic steps or establish
release acceptance from the overall green run.

Independent checks corroborate exactly **1,048,576** and **104,857,600** bytes
per RID, independently reconstructed original/edited hashes, immutable original,
Complete zero-diagnostic v0/v1, one edit attempt, unchanged disk before Save,
exact Save/final working bytes, normal initial exit, fresh GUI reopen with
unchanged saved bytes and normal reopen exit. **16 unique GUI session identities**
have successful terminal roots, exact action/version/causal-parent contracts,
matching trace-file hashes and **zero dropped records**; reopened sessions have
no edit/Save. The unversioned I/O-span limitation and separate byte/version
witnesses remain unchanged. No successful phase is inferred merely from a parse
span or its document size bucket.

Mac Save continues to record **one attempt**, two `CGEvent.postToPid` attempted
events and **`execution_acknowledged=false`**, with matching PID, granted
capability preflights, focused dirty source, exact source length and caret
10/empty-selection guard. New read-only target-active/frontmost/main-window/
window-focus metadata does not change the attempt into a delivery acknowledgement
or authorize app activation/global input. Exact bytes, clean-title observations
and successful product trace records are independent outcome evidence. No Save
retry occurred, and this is not physical-key/IME or foreground responsiveness
acceptance.

Native observer architecture and Git/check-out source hashes match; both Mac
clients compiled. All separate inventories corroborate one executable with no
other payload/native libraries, and reported pre/post identities agree:

| RID | Bytes | SHA-256 |
| --- | ---: | --- |
| win-x64 | 7,130,112 | `c4124139d386725bf42cd2750698fb08910a17d285a4d8ccd806125cdbe49721` |
| win-arm64 | 7,272,448 | `6e5eaf91fa5249c8d2d613f98b9cb74a31e1532a1b87396e4934e5b1b24fc74b` |
| osx-x64 | 16,849,256 | `ddd9f9d712f8e494e07241b02e7fe6dc32eb26cec88cf382ac2600a6097ebf55` |
| osx-arm64 | 16,508,264 | `1b6155901b02884bedf1a75ed97c42a447927cef3004f3443126f11b35b6e4c5` |

Artifacts/inventories: `.cache/ci-36811953139-json-<RID>/`; raw complete logs:
`.cache/ci-36811953139-json-all.log`; run/commit-bound independent assertions and
results: `.cache/ci-36811953139-json-audit.py` and
`.cache/ci-36811953139-json-independent-summary.json`. Assertions pass all eight
cases and 16 distinct trace sessions. No native product was rerun or downloadable
binary rehashed locally, and no product/probe was edited for the audit. This
remains a trace-on, just-written/cache-resident root-array capability result,
not disk-cold/tail-latency/physical-paint/real-IME or repeated-run reliability
certification.
