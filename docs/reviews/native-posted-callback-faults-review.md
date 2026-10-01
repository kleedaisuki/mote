# Native posted callback fault boundary: independent review

Date: 2026-10-01. Reviewed production commits `601dc59` and `d601aef`,
the existing Save completion review, surrounding posted adapters, and retained
local verification. This review changes documentation only.

## Assessment

**No substantive unresolved defect found in the inspected change.** The
Windows secondary-reporting visibility limitation in the initial implementation
is resolved by `d601aef`: posted reporting calls the shared notice installer
directly inside `NativePostedCallback.Report`, rather than the compatibility
wrapper that swallows notice faults. A nonfatal installer failure can now reach
the guard and select the fixed `native.posted.callback.report_failed` event.
Existing nonposted reporting retains its previous containment behavior.

## Inspected contracts

- Both Windows posted drain sites (startup and `WM_APP`) call the same drain;
  the macOS `DrainPosted` selector uses `NotifyPosted` rather than changing
  the behavior of the existing nonposted `Notify` method.
- Each dequeued Action is invoked once. Primary nonfatal failure returns the
  original exception object; reporting runs once and contains secondary
  nonfatal failure. Neither path retries a callback, retries a disk write,
  changes queue ownership, or labels a committed disk result as failed.
- The success path returns before telemetry, clocks, reporter construction,
  or queue-envelope allocation. Reporter closures occur only on fault paths.
  `OutOfMemoryException` remains outside all three new containment catches
  (callback, reporter, and optional telemetry), including the Windows direct
  posted notice path. The old nonposted catch-all wrapper is unchanged in
  policy and is not claimed to adopt this new fatal policy.
- Windows preserves an existing actionable notice; otherwise it installs the
  fixed path-free Editor notice. The extracted installer contains the same
  notice policy as the former wrapper. macOS still presents the original
  exception message, but both its message getter and `ShowError` execute
  inside the secondary guard. This does not claim to redact platform UI.
- Fault telemetry accepts a closed pair of enum values, writes independent
  current-session children, and deliberately ignores ambient Save ancestry.
  Records have failure status, zero duration and empty attributes. No
  exception metadata, path, source contents, request ID or native pointer is
  serialized. Events diagnose a dequeued callback/reporting fault, not Save
  failure, input delivery, or absence of an unobserved callback.
- The shared private checkpoint writer preserves the existing five menu
  events' closed public vocabulary, success status, short producer lease,
  session ancestry and default-off behavior. New enum values are appended;
  existing numeric assignments are not shifted. The strict acceptance reader
  gains only the two fixed operation names.

## Retained execution evidence

The following TRX counters and test names were inspected directly; completed
tests were not rerun.

| Retained artifact | Counters | Scope |
| --- | --- | --- |
| `.cache/validation/posted-callbacks/posted-callbacks-final.trx` | 20 executed, 20 passed; zero failed, aborted or not-executed | Six helper cases and existing menu cases after the shared-writer change |
| `.cache/validation/posted-callbacks/posted-callbacks-windows-notice.trx` | 8 executed, 8 passed; zero failed, aborted or not-executed | Same six helper cases plus two actual uncreated Windows shell queue/drain cases |

These are overlapping runs, **not 28 distinct cases**. Helper tests establish
original exception identity, exactly-once invocation and reporting, ordered
continuation after primary/secondary failures, persisted bytes surviving a
later callback fault, fatal exclusion, content-free independent session
records, and warmed zero allocation/no records on enabled-tracing success.
The file-write helper case is not a real Engine Save transaction; that contract
remains supported separately by the existing Save completion tests/review.

The two additional Windows tests call the actual shell's `Post` and private
drain on an uncreated shell, checking continuation and generic/existing notice
behavior. Source inspection confirms zero `_window` skips `PostMessageW` and
zero `_status` makes `RenderStatus` return before native calls. They therefore
exercise actual managed adapter wiring, **not user32 message dispatch**.
Reporter-fault emission is injected through the common helper; no test
artificially forces the actual Windows status installer to fail.

The validation document reports 7/7 Python reader cases; this review inspected
the narrow reader allowlist diff but did not independently reread a retained
Python execution log or rerun that suite.

## Limits and remaining integration evidence

This closes the inspected posted-only managed failure boundary, not every
reverse native callback. No target-native fault injection into AppKit,
startup/`WM_APP` dispatch, or platform status rendering was observed here.
Four-RID Native AOT compilation and ordinary hosted native workflows remain
integration evidence owned by the root agent. Neither this source review nor
the portable helper tests certify wake delivery, physical keyboard/IME input,
visible pixels, fatal-process recovery, telemetry durability, or current-binary
performance under fault load.
