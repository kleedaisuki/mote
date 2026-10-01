# Hosted Windows Grid focus provenance supervision

This additive diagnostic does not replace the original external Grid probe,
assertions, pins, or blocking native pane-focus tests. It is not a focus fix or
product acceptance. Two Windows Native AOT matrix jobs run the same independent
client once, after the existing published executable has been inventoried.

## Ownership and bounds

`tests/Invoke-WindowsGridFocusProvenanceWorkflow.ps1` requires a disposable
GitHub-hosted Windows runner. There is no local override. It checks exact reviewed
LF/CRLF source/project hashes for corrected client `2d96897` and independent
review `08b1df3`, checks the original strict root `mote.exe` inventory, records the
current binary SHA before/after, and builds the architecture-specific .NET 10
Windows WPF client once. It does not republish the editor.

Repository inspection found no existing implemented native job supervisor: old
wrappers use `Process.Kill(true)`, while the paint measurement design only proposes
job objects. This wrapper therefore introduces a narrow embedded C# supervisor,
not a claimed reuse of an untested helper. An unnamed, non-inheritable kill-on-close
job is passed through `STARTUPINFOEX` `PROC_THREAD_ATTRIBUTE_JOB_LIST` during
`CreateProcessW`. Assignment precedes the first child instruction. Unlike
create-suspended/assign/resume, there is no crash window leaving an unassigned
suspended child. No PID enumeration, foreign process opening, breakaway fallback,
activation, input repair, source reset, clipboard, registry or input-source change
is performed. The client retains the existing one F6, one GoTo and one cell-focus
attempt; synchronous UIA calls are bounded by the outer supervisor, not by claims
of internal cancellation.

The client deadline is **120 seconds**, followed by at most **10 seconds** of
owned-job termination observation. A process exit is read only from the original
retained kernel process handle after its signaled state. A successful job query
with `ActiveProcesses == 0` is the only empty-tree witness. Forced cleanup,
timeout, unavailable exit, failed query, or unproven empty job cannot become a
normal exit. Closing the last job handle remains a crash-safety fallback, not an
invented observation that cleanup completed. An independently exited client
leaving a descendant causes forced job cleanup and cannot qualify as healthy.

Only explicitly inherited stdout/stderr/CreateNew file handles and NUL stdin
enter `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`; the job/process/thread handles never
enter that list. `bInheritHandles=true` is required by the API. Native attribute
value buffers remain allocated through `DeleteProcThreadAttributeList`. Helper
creation uses `CREATE_NO_WINDOW`, `EXTENDED_STARTUPINFO_PRESENT`, and `SW_HIDE`.
The editor itself is the ordinary graphical product, not a hidden product mode.

On modern Windows, nested job assignment can still fail under incompatible outer
job policy. It fails closed before executing the client; there is no retry,
breakaway, global kill or looser supervision. No UI restriction is installed.
`JOB_LIST` requires Windows 10 / Server 2016 or later; nested jobs are supported
since Windows 8. Both supported hosted Windows architectures meet the OS minimum.

Primary platform evidence:
- [Microsoft attribute list contract](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute)
- [Microsoft atomic job creation explanation](https://devblogs.microsoft.com/oldnewthing/20230209-00/?p=107812)
- [Microsoft nested-job constraints](https://learn.microsoft.com/en-us/windows/win32/procthread/nested-jobs)
- [Microsoft job lifetime and kill-on-close semantics](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)

Two console-only hosted controls precede the sole UIA attempt: a normal parent
waits for its owned descendant to exit; a timeout parent starts a sleeping
descendant and is terminated through its job. Both require the fixed descendant
startup marker, numeric exits, queried empty job, and exact expected error class.
Failure stops before the UIA client. Controls are not retries or GUI probes.

## Retained evidence and independent graphs

All paths remain inside the fresh, reparse-checked directory
`.cache/ci-inventory/<rid>/grid-focus-provenance/`. Nothing is deleted:
supervisor JSON, build log/output, both console controls and logs, actual client
stdout/stderr, raw client report, synthetic scratch, complete isolated `MOTE_HOME`
including trace rotations, and the typed summary. Actions always uploads this tree
including hidden files for 14 days, even after a failed/censored attempt.

`tests/summarize_windows_grid_focus_provenance.py` validates the closed client DTO
and explicit client receipt→terminal-parent pairs. UTC remains only in the raw
report, never used to join processes. `ActionAttempted=false` / `not_attempted`
before-query failure is distinct from actual returned/thrown actions; after-query
failure preserves action outcome and unknown query health. An observed client
sequence is never relabeled product pass. Identity, normal-exit boundary,
fixture-unchanged hashes and cleanup facts must be internally consistent.

The server graph is independently read through the existing strict schema and
`read_focus_paths`. All eight adapter results, separate fault, native/managed
thread relations, query availability and censored coverage remain categorical.
No client receipt is joined to a server receipt by time, count or outcome:
`cross_process_edge=unjoined` is unconditional. Missing server receipts remain
`unobserved`, not callback absence; refusals before adapter entry are outside the
instrumented boundary. No foreground, physical keyboard, complete provider-entry,
complete transport or absence certificate is emitted. Classifier errors produce
fixed unknown/error classes without exporting exception messages or arbitrary
paths/content; original raw evidence is preserved. Summary outputs use CreateNew.

The diagnostic is non-gating; its actual nonzero result remains visible. Actions
job success is not a positive runtime or completeness certificate.

## Verification before first hosted execution

On 2026-10-01, the embedded C# compiled with `Add-Type`. Managed-only checks
verified 64-bit sizes `STARTUPINFO=104`, `STARTUPINFOEX=112`, process information
`24`, extended job limits `144`, accounting `48`, stdin offset `80`, active-process
offset `40`. Windows CRT quoting was checked for empty/space/quote/trailing-slash
arguments. **No native function or GUI executable was invoked locally.** These
layout checks are repeated on the actual x64/ARM64 hosted process before controls.

Portable summary fixtures cover result/fault independence, unavailable queries,
not-attempted actions, timeout numeric exits, unknown cleanup, privacy/schema and
receipt contradictions, client binary/architecture identity, censored trace
prefixes, malformed retained records, links, contradictory observed reports,
CreateNew output, and fixed CLI error classification. Exact logs and static
workflow validation are retained in
`.cache/validation/windows-grid-focus-workflow/`.

The first hosted controls and product/client evidence are still pending; compile,
schema fixtures and static review do not certify actual Windows process ownership,
UIA routing or product focus transfer.

## First hosted execution — CI 36874262096 (control failure, no UIA client)

Date: 2026-10-01. Exact source `3a0552a691e46470ac88ef65ce3c621276ec852c`, [CI 36874262096](https://github.com/kleedaisuki/mote/actions/runs/36874262096). All ten jobs conclude success, **but the new provenance diagnostic fails its ownership control on both Windows RIDs before executing the UIA client**. The actual diagnostic and classifier scripts emit exit 1; `continue-on-error` normalizes API step/job conclusions and is not successful runtime evidence. The summaries correctly retain `status=unknown`, no client report and no server observations.

Both architecture-specific clients actually build with zero warnings/errors and `build_exit_code=0`. Both normal controls retain `owned-descendant-started`, numeric **ExitCode=0**, TimedOut=false, JobEmpty=true, CleanupForced=false, ErrorClass=null. Both timeout controls retain the same startup marker and exactly **ExitCode=null, TimedOut=true, JobEmpty=true, CleanupForced=true, ErrorClass=timeout**. Their identical raw JSON SHA256 is `4EDE233FC1BF0C1BDB1B0F41EAE5D9845268E5D30AA94B8AAD5DBC439DF4E30D`. Wrapper line 259 throws `Atomic job ownership control failed.` because the unchanged predicate requires numeric timeout exit **124**; it is not weakened or accepted as null. The original retained-handle signaled/numeric-exit witness is missing even though the queried job is empty. A scheduling/process-signal race is a plausible mechanism, not independently established by the serialized control facts.

The wrapper's supervisor phase stays `error_class=build` after compilation, misleadingly naming this later control failure. Actual raw log line/error and control JSON locate it after build and before UIA. `client_exit_code` and `binary_sha256_after` remain null; supervisor `job_empty=false` describes the never-started UIA attempt, **not a contradictory query of the control job**. No client `report.json`, client stdout/stderr, scratch target trace or native-thread/managed-thread relation exists. Therefore provider adapters, owner-queue pane observations, eight fixed result/fault operations and client/server graph are **unobserved**, not zero/nonexecuted; absence certification is false, cross-process edge unjoined. This run establishes actual atomic console ownership-control behavior only, not product focus provenance coverage or a focus fix.

Evidence is retained in `.cache/ci-36874262096-focus-provenance/`: x64 control/build/supervisor/summary files under `artifacts/windows-grid-focus-provenance-win-x64/`, ARM64 under `final-artifacts/windows-grid-focus-provenance-win-arm64/`, completed job logs (x64 110409529982, ARM64 110409529582), run metadata and artifact enumeration (`per_page=100`). The original separate external AOT Grid reports remain **product-fail / numeric exit 1 on both RIDs**, source pin `C6894B4EDCF931BAA322B878AF8965759AC7E44EFC10291200737A18C1AD5509`; no new control failure is reinterpreted as that existing product oracle's root cause.

Scoped integration regression: Windows/macOS main suites both **3253/3253**, Themes14/14, Configuration9/9, zero failed/skipped; all four delivery inventories retain one executable. Both blocking Mac callback/Flow controls actually succeed with both fixed markers and ready (ARM64 14:15:54 UTC, x64 14:17:53 UTC). Ordinary JSON is **7/8, not 8/8**: x64 Mac 100 MiB times out at `save-exact-bytes`, editor numeric exit **-9**, no reopen (null exit), original working/fixture hashes unchanged. Its independently parsed retained **12,502-byte / 36-complete-row prefix** has menu-ready1/input-ready1, no input candidate/menu entry/return/Save request/normal terminal, no discarded partial row. Witness is ready-only and censored; one external two-event Command-S attempt has `execution_acknowledged=false`. These missing records do not certify callback absence, loss-free coverage or a Save root cause; candidate-to-menu/menu-to-request edges remain unknown. The other seven summaries retain pass with exits0/0. No old successful raw-chain/recovery audit or local GUI experiment was repeated.
