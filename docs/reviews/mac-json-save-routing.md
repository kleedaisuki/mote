# Mac JSON Save-routing metadata review

Date: 2026-10-01. Independent review of the uncommitted changes in
`benchmarks/NativeJsonLargeAcceptance/{MacClient.swift,probe.py,test_probe.py}`.
This review does not change the driver or product, rerun native acceptance, or
authorize any workflow dispatch. Also reviewed the completed owner interpretation
document `docs/validation/mac-json-save-routing-discriminator.md`.

## Findings

**No substantive defect found in the reviewed code diff.** The change adds
read-only diagnostic facts; it does not repair or establish the cause of the
hosted Save timeout. Windows-only portable validation cannot establish Swift
compilation or live AppKit/AX observations.

The owner document correctly separates the 60-second Save acknowledgement wait
from the approximately 79-second driver lifetime, the pre-dispatch report from
the final timeout observation, and historical all-eight success from current
seven-case success/one failure. It explicitly treats activity correlation as a
hypothesis rather than delivery, handler-entry, or I/O causality. One minor
wording correction was requested: the client necessarily reads the transient
frontmost PID for equality comparison, while never persisting that foreign
identity or inspecting its name/path/title. The owner document now states this
precisely; this was not a code/privacy defect.

## Verified contracts

- `MacClient.swift:observe` obtains the requested process's optional
  `NSRunningApplication.isActive` and reduces the workspace's optional frontmost
  process immediately to a PID-equality Boolean. It emits no foreign process
  identity, name, path, or accessibility value. Neither API call activates an
  application. No system-wide AX object, global key posting, new permission
  request, or target activation is introduced.
- `window_main` and `window_focused` are optional AX metadata reads only after
  the existing exact-PID traversal ownership check and bounded window-title
  guard. Reads reuse the 0.15-second element messaging timeout and existing
  6-second client watchdog. Unsupported/unavailable attributes stay unavailable.
- Existing `focused` remains source-proxy AXFocused. The current product
  `MacAccessibilityElementPrototype.IsFocused` checks the owned input editor
  against its window's `firstResponder`, not foreground application activity.
  The new fields neither relabel nor strengthen this existing input guard.
- `probe.py:mac_report` explicitly whitelists all four new fields, permits only
  exact Python `bool` or `None`, and rejects integers or strings masquerading
  as Booleans. Missing Swift optional fields become `None`. Unknown fields are
  dropped, including a deliberately injected foreign PID/name in the test.
- `save_exact` retains `save_command_report` before later polling replaces the
  driver's last report. New activity/window facts in that transaction are
  **pre-dispatch observations**, not handler-entry acknowledgements. The timeout
  `failure_observation` is the separately retained latest validated observation.
  These sequential API reads are not an atomic foreground/window snapshot, and
  are not proof of delivery even if every Boolean is true.
- No Save retry, edit retry, input admission condition, exact-byte oracle,
  accepted outcome, endpoint deadline, or liveness predicate is changed. Extra
  metadata reads add observation overhead under the existing bounds; no latency
  improvement is claimed.

## Prior hosted evidence and limits of the discriminator

Read `docs/validation/native-json-large-ci.md`'s run 36809964231 follow-up and
the retained ARM report:
`.cache/ci-36809964231-json-osx-arm64/.cache/ci-inventory/osx-arm64/native-json-large.json`.
At source `8d5796542b63e54e630933de38a2c68d864ca085`, ARM64 100 MiB reached
Complete v0/v1 and exact source readiness, then made one Command-S attempt with
two process-specific posts and `execution_acknowledged=false`. It timed out
waiting for clean chrome; its latest observation remained modified, focused,
Complete, and exactly 104,857,600 source units. Working bytes remained the
original. Forced cleanup left the only retained 100 MiB trace empty.

That establishes a failed external Save-outcome observation, not handler entry,
replacement failure, TCC refusal, or foreground routing as a cause. The earlier
source-proxy focus witness does not answer foreground/window activity. The new
facts can discriminate a non-active target observation from an active target
observation in a future run; they cannot independently distinguish undelivered
Command-S from an entered handler with failed/cancelled I/O. A passing follow-up
does not retroactively explain this failure or establish repeated-run reliability.

## Validation

Executed from `D:\Code\mote`:

```text
python -m unittest discover -s benchmarks/NativeJsonLargeAcceptance -p test_probe.py -v
Ran 22 tests in 0.242s: OK
git diff --check -- benchmarks/NativeJsonLargeAcceptance
No whitespace errors
```

New cases cover true/false/null/missing values, rejection of numeric/string
values, distinction from source focus, privacy whitelisting, and retention in
the Save transaction. Existing tests cover the timeout-retained transaction,
single Save attempt, and refusal to read target bytes before clean chrome.
Swift compilation and actual target routing remain for hosted macOS validation.

## API semantics references

- [Apple NSRunningApplication.isActive](https://developer.apple.com/documentation/appkit/nsrunningapplication/isactive):
  describes current frontmost activity, distinct from activation methods.
- [Apple NSWorkspace.frontmostApplication](https://developer.apple.com/documentation/appkit/nsworkspace/frontmostapplication):
  nullable read-only property for the application receiving key events.
- [Apple NSRunningApplication](https://developer.apple.com/documentation/appkit/nsrunningapplication):
  properties are individually atomic, and repeated state updates depend on run
  loop turns. Each existing driver observation launches a fresh short-lived
  client; the new fields are nevertheless sampled facts, not a transaction-wide
  routing guarantee.
