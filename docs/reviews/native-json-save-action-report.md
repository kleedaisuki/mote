# Native JSON Save action-report retention: independent review

Date: 2026-10-01.
Scope: the current uncommitted diff in
`benchmarks/NativeJsonLargeAcceptance/probe.py` and `test_probe.py`, limited to
retaining the one Save dispatch report. Reused the filename-selected
`docs/reviews/native-json-large-acceptance-review.md` and
`docs/validation/native-json-large-third-hosted.md`, and inspected the relevant
Mac client Save dispatch and sample failure/cleanup paths. This review does not
modify product, driver, tests or CI, and performs no native workflow.

## Findings

**No substantive finding in the inspected change.**

- `save_exact` still first requires observed dirty source, calls `driver.save()`
  exactly once, and preserves the returned report in the sample dictionary
  before beginning the existing clean-chrome wait. Later `MacDriver.observe`
  calls replace `last_report`, not the retained dictionary object, so the Save
  transaction report remains independently available after timeout.
- `save_command_attempted` means the wrapper invoked the action, not that any
  input reached the editor. Windows returns a report only after the existing
  owned-HWND check and successful one `PostMessageW(WM_COMMAND, 203)` call.
  Its `queued-not-execution-acknowledged` label and
  `execution_acknowledged=false` do not claim execution. Microsoft's
  [PostMessageW contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagew)
  explicitly separates queueing from target processing.
- Mac retains only the dictionary already validated and whitelisted by
  `mac_report`, including typed counts, booleans, enums and PID. It adds no AX
  strings, source content, paths, arbitrary exception text or foreign identity.
  The original client guards and PID-specific key posting remain unchanged.
  The `attempted-posts-no-delivery-acknowledgement` label describes the existing
  two calls to `CGEvent.postToPid`, not delivery, execution or Save success.
  The [Apple API reference](https://developer.apple.com/documentation/coregraphics/cgevent/posttopid(_:))
  identifies the posting API; the inspected Swift invocation consumes no return
  acknowledgement and performs no post-dispatch Save-handler certification.
- The additional parent-clock `save_command_return_elapsed_ms` measures wrapper
  return time, including the client overhead, not disk Save latency or execution
  acknowledgement. It does not change the existing 60-second endpoint wait,
  bounded client call, polling interval or cleanup. No modifying retry is added.
- Clean-state polling still reads owned UI only. Neither this diff nor the
  timeout path opens the working Save target during the replacement transaction.
  Full-file size/hash reads remain after clean acknowledgement, or after owned
  cleanup/termination in the final failure record. The independent saved-byte
  oracle, normal exit, trace integrity and fresh GUI reopen remain necessary for
  sample success. A stored command report cannot make a failed sample pass.

A thrown Save call may leave `save_command_attempted=true` without a returned
`save_command_report`; that is an honest absence of transaction-return evidence,
not a successful queue/post claim. The existing content-free failure observation
remains available for client guard errors. No fallback report fabricates a
successful return.

## Verification

- `python -B -m unittest discover -s benchmarks/NativeJsonLargeAcceptance -p test_probe.py -v`:
  **21/21 pass**, 0.224 seconds. This includes the two new tests for report
  retention after Save acknowledgement timeout with one dispatch/no target
  digest, and Mac posting-versus-delivery labels, plus existing clean-only byte
  rejection, no target read before clean, privacy and causal trace regressions.
- Additional in-memory Windows-driver check used a mocked API and ownership
  guard: successful queue called `(owned HWND, WM_COMMAND, 203, 0)` once and
  returned exactly the documented metadata; failed queue raised without a
  success report and without another dispatch. **Pass**, no native input.
- Scoped `git diff --check`: pass.

## Evidence boundary

This is diagnostic retention, **not a root-cause correction or proof for the
third-hosted macOS x64 1 MiB Save timeout**. An attempted post report cannot tell
whether the process received Command-S, invoked Save, suppressed it or failed
inside Save. That distinction still needs a valid flushed target trace or an
independent owned product witness. The prior zero-byte trace and later
`dispatched_events=0` observation cannot resolve it. No new target execution,
reliability distribution, four-RID acceptance or physical-input claim is made.
