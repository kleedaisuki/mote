# Ordinary Native AOT JSON pilot: second four-RID target evidence

Date: 2026-10-01. Source:
`99fbe395308816e3d1d2381ace58c3d5800811ec`,
[CI 36800944850](https://github.com/kleedaisuki/mote/actions/runs/36800944850).
This is the first hosted execution with UI-only Save polling and retained Mac
guard metadata. It supersedes neither the
[first run's failure evidence](native-json-large-first-hosted.md) nor the broader
native-latency release requirements. No product/native workflow was rerun for
this artifact audit.

## Actual four-RID outcome

All four portable batches passed **12/12** and native Python setup succeeded.
Windows x64 and ARM64 each printed the real two-case `status: pass` plus the
postcondition success marker; the driver returned zero. Mac x64 and ARM64 each
compiled the updated Swift client, printed `status: incomplete`, and the raw
step ended **exit 1**. `continue-on-error` still made all four API step/job
conclusions green; only the Windows pilot actually passed.

| RID | 1 MiB | 100 MiB | Native interaction evidence |
| --- | --- | --- | --- |
| win-x64 | pass | pass | Ordinary source binding, Complete v0/v1, one exact in-string replacement, exact Save, normal original/reopen exit and audited terminal traces |
| win-arm64 | pass | pass | Same contract, actual native ARM64 observer/ABI |
| osx-x64 | failed before source acknowledgement | same | `window-count`, AX error **-25204**, no modifying input |
| osx-arm64 | same | same | Same guard/error, no modifying input |

For all four Windows cases, the exact source byte count and saved SHA equal the
independent declared one-byte replacement oracle; immutable original SHA remains
unchanged. GUI reopen leaves saved bytes unchanged. The original **and** reopen
raw traces were independently re-audited: all eight processes have exactly one
successful terminal session, no drops, correct action counts and successful
native versions (open0, pre-edit0, commit/presentation/draw1). Unversioned engine
I/O remains explicitly unavailable, not an invented Save revision. Reopen has
no edit/Save actions. No forced cleanup occurred for these accepted cases.

This establishes the **scoped Windows Native AOT correctness/capability pilot**
on current source. It does not establish physical-key input, IME/VoiceOver,
latency tails, compositor presentation, all JSON shapes, burst typing or all
four-RID product acceptance. The change in hosted Save outcome supports the
noninterfering observer correction, but the two runs are not an isolated paired
experiment: product source and runner instances also differ. No performance
speedup is inferred by comparing their times.

## Provenance

All reports show one publish executable, native observer architecture, the
source commit above, unchanged before/after binary hash, and correct committed
tool bytes (Windows CRLF versus Mac LF). As in the first audit, executable bodies
were not downloaded; these identities are target-generated inventory/hash
observations, not local binary reinspection.

| RID | Executable bytes | SHA-256 |
| --- | ---: | --- |
| win-x64 | 7,105,024 | `85d3b300cb1756d5c21a63bb2280a73a06b7d011c903aae7f515c29d93948634` |
| win-arm64 | 7,244,288 | `f8e6f84a376567b8c801e103b21b6de50797b973f518aaea5933cf7f9378d97f` |
| osx-x64 | 16,842,720 | `9e60aea84e588339cb9a7ce9d36afb2a9b27629c73463ebe2c4f9a593a3564a1` |
| osx-arm64 | 16,489,288 | `ec1e61195123ab387558873887cdd90819323ed74b5cf254e6e45f8d52351d5b` |

Driver LF/CRLF hashes respectively:
`9269c33653166fa91140a8568370ee47b0096e2d5c9d2cff895d09250fe7bcea` /
`8a56bf566671eb026b341e52e4fed78b01a4acf58ae99a63ff993acc6dc6f7c3`.
Both Swift hashes:
`ffa1ec738b61cac976f8a790843950f85bc67b6db9667eef41da27ee4977ff0c`.
The reused artifact auditor remains the first run's committed LF/CRLF identity;
all were checked against Git blobs from **99fbe39**, not an evolving worktree.
Corpus original/saved hashes remain the exact table in the first-run document.

## Requested-file endpoints, not blank-shell startup

| Observed endpoint (ms) | x64 1 MiB | x64 100 MiB | ARM64 1 MiB | ARM64 100 MiB |
| --- | ---: | ---: | ---: | ---: |
| Parent process launch → bounded **requested-file** source acknowledgement | 169.4535 | 578.2432 | 175.2245 | 408.1422 |
| Child accepted file open → editable source v0 | 19.311 | 421.494 | 16.376 | 306.876 |
| Child accepted file open → source draw callback return v0 | 30.831 | 429.436 | 25.094 | 313.892 |
| Parent native edit dispatch → bounded source acknowledgement | 13.1626 | 13.8596 | 13.6819 | 13.4381 |
| Child edit → source draw callback return v1 | 9.612 | 9.654 | 9.207 | 9.616 |
| Child engine Save | 18.278 | 881.940 | 39.642 | 1,078.867 |

These are **one traced observation per case** on different machines/images,
just-written files and 50 ms polling. External native-message acknowledgement is
not physical keyboard latency. The child draw endpoint is callback return, not
compositor pixels. Parent and child durations are never subtracted. CPU time,
RSS/working-set and allocations were **not measured**. There is no p95, SLA,
trace-off baseline, disk-cold startup or cross-architecture speed comparison.

### Material attribution counterexample

`mote.startup_to_editable` must **not** be advertised as requested-file startup.
In the win-x64 100 MiB original trace:

| Raw operation | Duration | Dimensions |
| --- | ---: | --- |
| `mote.startup_to_editable` | **104.107 ms** | `size_bucket: <1KiB`, version0 |
| `document.open_to_editable` | **421.494 ms** | `size_bucket: 64-256MiB`, version0 |

Source explains why: `NativeEditorController.Shown()` first calls
`ShowDocument()` for the controller's initial empty document, records the startup
endpoint, then schedules analysis and calls `StartOpen(_startupPath)`. Program's
startup mark already excludes configuration. Therefore the first span means
**post-configuration → initial blank shell/source**, not the requested 100 MiB
file. Version0 alone does not disambiguate the two document identities. Its
success remains a useful shell witness, but requested-file latency must use
parent launch/source acknowledgement or the accepted-file open endpoints above.
No instrumentation/product contract was changed to conceal this distinction.

## Mac failure: exact guard and justified next discriminator

All four Mac case reports contain `guard_stage: window-count`, `ax_error: -25204`,
`trusted: true`, `post_event_access: true`, no source candidate, no edit attempt
or dispatched event. They were forcibly cleaned up, with no normal/reopen/trace
acceptance. Their saved/original bytes remain unchanged. The original
`window_count: 0` is only an initialized output after an **unsuccessful count
call**, not evidence that the app has no native window.

[Apple's count API contract](https://developer.apple.com/documentation/applicationservices/1459066-axuielementgetattributevaluecoun?language=objc)
describes `CannotComplete` as messaging failure. This does not establish a
specific startup race, TCC denial, memory failure or unsupported array. Exact
source behavior was the actionable harness error: the very first failed count
made the Swift report fatal; Python immediately raised, preventing the existing
60-second process-alive readiness loop from retrying this **read-only** request.
No count-versus-copy or later readiness observation existed in this run.

The authorized narrow correction classifies **only the application window-count
call's CannotComplete** as observed/not-ready inside that same deadline. A failed
count now has null window count. Other count errors, copy errors, TCC and PID
guards fail closed, and modifying commands fail before posting any event if
the count is unresolved. The bounded copy has separate error/count metadata.
Parent observations retain attempt count, first/last AX count error, first-ready
and last-observation elapsed times, explicitly including client launch/IPC.
No global input, activation, permission request, editing retry or deadline
increase is added. Persistent refusal must time out, not become success.

Updated portable tests **15/15** distinguish initial -25204→ready, persistent
-25204→timeout and other errors remaining fatal; the mock reports do not certify
actual Mac API recovery. [Independent scoped review](../reviews/native-json-mac-readiness.md)
found no substantive corrective issue. Updated Swift compilation and whether a
later read reaches source/focus/edit/Save are still a new hosted evidence gate.

## Retention and reproducibility

Each exact report, trace and job log is under
`.cache/ci-36800944850-native-json-{win-x64,win-arm64,osx-x64,osx-arm64}/`.
Content-free independently recomputed raw trace/provenance summary:
`.cache/native-json-large/ci-36800944850-audit.json`; procedure:
`.temp/native-json-share-discriminator/audit-second-hosted.py`.
Windows original/reopen trace byte counts are 8,927/4,456 (x64 1 MiB),
10,765/6,258 (x64 100 MiB), 8,925/4,453 (ARM64 1 MiB),
11,828/6,256 (ARM64 100 MiB). Mac has no accepted terminal trace here.
The broader independent matrix audit owns `native-json-large-ci.md`; this
document retains the harness-owner's exact endpoint/readiness reasoning.
