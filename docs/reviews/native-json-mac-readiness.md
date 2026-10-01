# Native JSON Mac AX readiness: independent scoped review

Date: 2026-10-01. Reviewed the uncommitted diff in
`benchmarks/NativeJsonLargeAcceptance/{MacClient.swift,probe.py,test_probe.py}`.
Reused the existing harness review and
`docs/validation/native-json-large-ci.md`, including the retained four Mac
startup failures from run 36800944850. This reviewer owns only this document;
no client, driver, product or workflow code was changed.

## Verdict

**No substantive corrective finding in the inspected change.** The patch is a
bounded readiness-discrimination experiment, not proof that the prior AX failure
was transient, that TCC was denied, or that Mac product acceptance now passes.

## Reviewed behavior

- The special pending classification is exactly the application `AXWindows`
  **count** call returning Swift `.cannotComplete` (raw value -25204). It does
  not classify every failed AX operation as pending. Count failure now emits
  null/absent `window_count`, rather than interpreting the initialized zero as
  evidence that no native window exists.
- A pending count returns `ready=false` with no source/window handle. Every
  modifying operation explicitly fails on this error before the edit/Save/close
  branches. Existing trust, PID ownership, source length, physical focus,
  selection and close-button guards remain intact. No editing event is retried.
- Window count success still requires at most one window. A subsequent window
  **copy** error or wrong copied count is now fatal, with its own error/count
  metadata rather than being mislabeled as a successful count. The two API
  outcomes are not conflated.
- The existing initial `wait(..., 60)` and liveness check are unchanged; polling
  still uses the existing 50 ms interval and the client retains its 6-second
  process watchdog and 0.15-second AX timeout. No deadline enlargement, global
  event posting, activation, permission request or TCC mutation is introduced.
  As before, the outer wait checks its deadline between calls, not by preempting
  the current bounded client call; it is not an exact hard 60-second cutoff.
- The error is pending for read-only `observe` transactions at any phase, not
  only the very first transaction. Later source/semantic/Save-ack polling remains
  bounded by its preexisting endpoint deadline; this does not authorize a
  modifying retry or turn a persistent communication failure into acceptance.
- Parent-clock attempt count, first/last AX count error and first ready time are
  bounded metadata. The successful readiness summary is captured before later
  phases and states explicitly that it includes client launch/IPC overhead.
  Failed-run metadata remains separate from accepted timing. First/last errors
  refer to validated reports: if a later client itself fails before producing
  a valid report, `last_report` remains the previous validated report; the
  separate exception classification records that failure. These fields must
  not be interpreted as fresh child-side observations in that case.
- The metadata whitelist only adds integer error/count fields. No document
  text, arbitrary AX strings or unbounded polling history enters the report.

Apple's [AXError.cannotComplete definition](https://developer.apple.com/documentation/applicationservices/axerror/cannotcomplete)
confirms the exact numeric value. The specific
[AXUIElementGetAttributeValueCount documentation](https://developer.apple.com/documentation/applicationservices/1459066-axuielementgetattributevaluecoun?changes=_5&language=objc)
describes this result as messaging failure. A startup race
is a hypothesis to discriminate, not a guaranteed meaning of the enumeration.

## Verification

Command, executed inside the repository:

```powershell
python -m unittest discover -s benchmarks/NativeJsonLargeAcceptance -p test_probe.py -v
```

Result: **15/15 passed**, 0.223 seconds. The three new tests distinguish a pending
count followed by readiness, persistent pending count ending in timeout, and a
nontransient count failure remaining fatal. Existing exact-byte, causal-trace,
Save-observer and single-edit-attempt regressions remain green.
`git diff --check` reports no whitespace error (only normal Git line-ending
warnings for unrelated concurrently edited files).

## Limits and next evidence

The portable tests mock the Swift report; they do not execute its actual AX API
or compile Swift on this Windows host. The newly inspected Swift branch must
therefore compile and execute on both hosted Mac RIDs. No native editor was
launched by this review and no successful Mac readiness measurement is claimed.
The next hosted report should discriminate: pending count recovering within the
existing deadline versus persistent -25204 timeout, with copy errors kept
separate. Full edit/Save/reopen/trace acceptance is still required after readiness.

## Follow-up review: bounded window-copy pending observation

Date: 2026-10-01. Scope: subsequent uncommitted diff in the same three files.
This section supersedes the earlier count-only statement that every window-copy
error is fatal. The original review remains historical, not a description of
the expanded current contract.

**No substantive finding or commit blocker in the inspected follow-up.**

### Evidence and scope of the change

Reused the independent run **36802378381** audit in
`docs/validation/native-json-large-ci.md`: all Mac samples reached source
readiness. Mac ARM64 100 MiB later failed during the external initial semantic
wait with count success (0), one counted window, but copy error -25204 and no
valid copied count. Its normal-cleanup trace independently establishes successful
v0 analysis, not full edit/Save/reopen acceptance. This is evidence for
separately discriminating copy communication readiness; it does not establish
that every such error is transient. The Mac x64 1 MiB dirty-title Save timeout
in that run is a different unresolved issue and is not repaired or certified by
this patch.

### Inspected contracts

- Only `.cannotComplete` from the bounded `AXWindows` copy call becomes
  read-only pending at `window-read`. Count success remains recorded as 0/1;
  unsuccessful copy count remains unavailable. Unsupported attributes, other
  copy failures, malformed successful arrays and wrong copied counts remain
  fatal. The change does not reclassify arbitrary tree errors.
- The Swift modifying guard now rejects either count or copy -25204 before
  edit, Save or close dispatch. The Python client independently rejects even
  an `observed` pending modifying response. Existing PID, focus, exact source
  length, selection, permission and close-button guards remain unchanged.
  A pending copy returns no source or window handle and cannot authorize input.
- No per-phase wait call, liveness check, polling interval, client watchdog or
  AX timeout changes. Pending read-only observations use existing deadlines;
  modifying actions remain one-shot. No global input, activation or TCC change
  appears in this diff.
- Count and copy first/last errors and pending counts are separate. Copy attempts
  count only reports where the copy call was reached, not every observation.
  Fresh validated reports are detected by dictionary identity: `mac_report`
  always allocates a new dictionary. Thus a tool timeout, wrong PID or malformed
  output before report validation increments the overall observer attempt but
  not validated/copy counters and does not reuse a stale ready report as a new
  ready witness. Failed-but-valid guard reports still contribute their genuine
  error metadata. The last validated report remains explicitly available for
  failure diagnosis; this is not a fresh observation after a client timeout.
- Summary storage remains constant-size; the newly added fields contain only
  integer counts/errors. Existing report privacy and parent-clock labels remain
  intact.

### Focused verification and remaining limits

Re-ran only the portable harness suite because four new tests exercise the
changed contract:

```powershell
python -m unittest discover -s benchmarks/NativeJsonLargeAcceptance -p test_probe.py -v
```

**19/19 passed**, 0.214 seconds. New regressions cover copy pending→ready,
persistent copy pending→timeout, nontransient copy failure and pending
edit/Save/close rejection, and no stale copy recount after client timeout.
Scoped `git diff --check` is clean. No product source, driver, workflow or
unrelated review file was modified; this review is not committed.

These tests still mock client reports and do not compile/execute the new Swift
branch. Hosted Mac compile/execution and complete ordinary edit/Save/reopen
acceptance remain required. Pending copy timeout remains a failed sample;
internal successful analysis or cleanup-only normal exit cannot substitute
for the unobserved external endpoint.

## Subsequent scoped review: bounded window-copy pending state

Date: 2026-10-01. Inspected the subsequent uncommitted client/driver/four-test
change following hosted run 36802378381. The earlier copy-fatal description
above records the prior patch, not the new behavior. **No substantive finding
in this narrow follow-up.** No source edits, native launch, redundant portable
suite run or trace mutation recheck was performed; trace certification was
untouched.

- Only application `AXWindows` bounded copy returning `.cannotComplete` (-25204)
  joins count messaging failure as read-only pending. A fresh Swift report still
  has ready=false and no source/window handles; successful count1 does not imply
  a successful copied window. Other copy errors and incorrect successful copied
  counts remain fatal. This is not blanket AX retry behavior.
- Native `main` rejects unresolved count **or** copy before every non-observe
  transaction; Python independently rejects an observed pending report for
  edit/Save/close. No non-idempotent event is resent and no permission/global
  focus/input behavior changes. Parent wait deadline, child liveness, observer
  polling and client watchdog remain unchanged, including the prior bounded
  final-call overrun limitation.
- Summary distinguishes count-pending from copy-pending observations, first/last
  copy errors and copy-call count. Only a newly validated report is counted;
  client timeout retains the previous report for failure evidence without
  treating it as another AX call. Total attempts remains distinct from validated
  observations. All added fields are sanitized numeric metadata, not AX text.
- Added tests inspect pending-copy recovery, persistent copy timeout, other-copy
  failure plus modifying guard rejection, and no double-counting stale metadata
  after a tool failure. Owner reports 19/19 portable tests; this reviewer inspected
  their assertions without repeating the suite. Mocked reports do not verify
  Swift compilation or AppKit/AX runtime acceptance.

Evidence interpretation remains narrow: owner reports ARM64 100 MiB reached
source binding and then count success1/copy -25204 during the initial Full wait
at about13.6s, before any edit. That supports testing read-only copy recovery
under the same bound; it does not establish that recovery will occur. Reported
x64 1 MiB Save-ack timeout with dirty source and original disk bytes is separate
and **not fixed or diagnosed by this patch**. A future Save-action report change
requires its own review. Hosted current-source compilation and exact
edit/Save/reopen/terminal evidence remain necessary.
