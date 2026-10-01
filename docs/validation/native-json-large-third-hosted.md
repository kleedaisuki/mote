# Ordinary JSON pilot: third-hosted Mac evidence and distinct failures

Date: 2026-10-01. Source `c453506c84e249dbff7c73748140314f6760ff33`,
[CI 36802378381](https://github.com/kleedaisuki/mote/actions/runs/36802378381).
This is the first target execution of count-only pending readiness; the two
different failing cases below must not be merged into one cause or a four-RID
acceptance claim. It follows the
[second-hosted attribution record](native-json-large-second-hosted.md).

## Observed Mac results

Both actual Mac pilot steps printed `status: incomplete` and raw exit1 despite
masked green Native AOT jobs. Both Swift clients compiled; all target tool hashes
match the exact source commit. Binary before/after hashes are unchanged:

| RID | Executable bytes | SHA-256 |
| --- | ---: | --- |
| osx-arm64 | 16,489,288 | `3cc3683d0bb7f57920762ea08e131b37433fc09236f8680dd9ae563a3a180e91` |
| osx-x64 | 16,842,720 | `1b24d7357eccca349e73884221037ab3feaf2f19781e6a168cd85a6621126d21` |

Driver LF SHA:
`1f0e8142441594cad0f0778324b6e063aa87541accb1397448dc57fa7b5d51e3`;
Swift LF SHA:
`aa6711477f91319039ddfb356b24b717aa57875672882dc1e6b049b68e0bbea1`.
The reused auditor matches its unchanged committed LF identity.

| RID/case | Result | Decisive observable boundary |
| --- | --- | --- |
| ARM64 1 MiB | **pass** | Initial count -25204 recovered after 4 observations; ordinary source/Complete0/edit/Complete1/exact Save/normal exit/GUI reopen/terminal traces |
| ARM64 100 MiB | **failed, read-only observation** | Source ready after 6 observations, then count success/count1 followed by bounded-copy -25204 during initial semantic wait, no edit attempted |
| x64 1 MiB | **failed, Save acknowledgement** | Source ready, Complete0, one edit, Complete1, focused source caret10; single Command-S transaction returned, but dirty source and unchanged working disk persisted to 60-second timeout |
| x64 100 MiB | **pass** | Count startup recovered after 19 observations; full ordinary exact Save/normal exit/GUI reopen/terminal traces |

All immutable fixture hashes remain unchanged. Passing saved hashes equal the
independent 1/100 MiB replacement oracles from the first-hosted record, and saved
bytes remain exact after GUI reopen. Independent raw original/reopen audits of
both passing cases establish correct action counts, success, causal parents,
open0/pre-edit0/commit+presentation+draw1, one normal terminal session and no drops.
No native workflow was repeated for this artifact audit. These two cases prove
actual target-specific capability, not acceptance of both sizes on both Mac RIDs,
physical input, VoiceOver/IME or a reliability distribution.

## ARM64 large case: observer copy, not failed Full parsing

At **13,593.7575 ms** from driver attachment, after 107 validated observations:

```text
phase = initial-whole-document-semantics
guard_stage = window-read
trusted = true, post_event_access = true
AXWindows count: error 0, count 1
AXWindows bounded copy: error -25204, count unavailable
edit_attempts = 0, dispatched_events = 0
```

The source had already become ready at 957.182875 ms, with six observations and
initial count -25204→0. A later count success does not guarantee the following
copy can complete. [Apple's copy API](https://developer.apple.com/documentation/applicationservices/1462060-axuielementcopyattributevalues)
defines CannotComplete as messaging failure; it is not proof of a malformed
document, empty array, unsupported target or denied permission.

The existing owned cleanup **exited normally** and flushed a 4,838-byte terminal
trace. That trace passes causal integrity, has successful file open/editable/draw
v0, two successful initial/Visible parse records, no edit/Save operations, and one
normal terminal session. It does not prove Complete global semantics. In fact,
`NativeIdleFullAnalysis.DelayFor(>32 MiB)` is **15 seconds**: the observer failed
around 13.6 seconds, before the planned idle Full certification started. Calling
this a slow/failed Full parser result would misattribute the evidence.

The approved correction makes **only this application's bounded AXWindows copy
CannotComplete** pending for read-only `observe`, under each unchanged phase's
process-alive check, timeout and poll interval. Count and copy error/count fields
remain distinct. Any unresolved count/copy fails before edit/Save/close; all other
errors and ownership/trust/source/focus guards remain fail-closed. It is a bounded
readiness discriminator, not proof that the next copy will recover. Persistent
refusal times out. No global input, activation, TCC request/mutation, deadline
increase or modifying retry is added.

Metadata separates fresh validated responses, actual copied-response attempts,
first/last copy errors and count/copy pending observations. A client exception
that preserves `last_report` cannot increment copy-attempt counters from that
stale report. Portable tests now **19/19** pass: copy recovery, persistent timeout,
fatal other copy errors/unresolved modifying calls and stale-report accounting,
along with prior count/deadline/byte/trace tests. Independent scoped review found
no substantive issue; updated Swift compilation/recovery remains hosted work.

## x64 small Save: separate unresolved delivery/product boundary

The last accepted observation after 499 polls /63,334.969407 ms from attachment
still has one source with 1,048,576 characters, `Complete v1`, physical responder
proxy focused, selection10:0, `modified=true`, both window count/copy success.
The working disk hash is still the original 1 MiB source; no clean UI or exact
saved result was observed. Cleanup's close transaction failed; owned forced
termination left a **zero-byte unflushed trace**. This cannot establish whether
the target processed Command-S, invoked its Save handler, suppressed an overlapping
request, or encountered product Save failure.

The existing `CGEvent.postToPid` call returns no delivery acknowledgement. The
driver's Save response was discarded, and later read-only observations naturally
report `dispatched_events=0`; that zero is **not** evidence that no earlier key
post was attempted. A separate diagnostic will retain a fixed, content-free
`save_command_report` immediately after the one modifying transaction and before
Save polling, labeling posts as attempted/posted, **not delivered/accepted**.
No automatic Save retry or target read during Save is allowed. Actual product
Save attribution still requires a valid flushed trace or another independent
owned UI witness; a posted report alone cannot supply it. This proposal/change
is separate from the copy-readiness fix and is not claimed to solve the failure.

## Passing-case timing, not tails or initial-shell confusion

| Endpoint (ms) | ARM64 1 MiB | x64 100 MiB |
| --- | ---: | ---: |
| Parent launch → requested-source acknowledgement | 1,315.942459 | 4,011.828249 |
| Child accepted file open → editable source | 114.903 | 3,654.175 |
| Child file open → source draw callback return | 136.048 | 3,674.580 |
| Parent edit transaction → acknowledged source | 424.649292 | 464.950699 |
| Child edit → source draw callback return | 7.646 | 10.068 |
| Child engine Save | 8.649 | 2,266.715 |

Parent intervals contain Swift launch, AX polling and navigation; child values
have their own monotonic clock. They are never subtracted. The known startup
operation ends on the initial blank shell, not the requested file, and is not
advertised here as file startup. These are single traced, just-written samples
on different hardware, not OS/architecture comparisons, p95, disk-cold startup,
physical-key latency or compositor presentation. CPU/RSS/allocations were not
measured.

## Retention

Exact Mac reports, raw traces and job logs are under
`.cache/ci-36802378381-native-json-{osx-x64,osx-arm64}/`.
Independent identity/raw-trace audit:
`.cache/native-json-large/ci-36802378381-mac-audit.json`; procedure:
`.temp/native-json-share-discriminator/audit-third-hosted-mac.py`.
The two failures, normal-versus-forced cleanup and narrow next interventions
remain separate in all interpretation.
