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
