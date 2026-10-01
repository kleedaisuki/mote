# Native local input monitor: independent source review

Date: 2026-10-01. Reviewer: independent `input_monitor_review` agent.
Native implementation reviewed at `3ec5c2ef01e7a1d0be969a48f3e8aca809dff4a8`;
portable admission fixtures at `498f88f`, telemetry contracts at `4d3f03c`,
JSON inventory at `a5e93ac`, and CI summary at `717bd8b`.

## Verdict and scope

**No remaining substantive source defect found in this scoped change.** The
implementation is suitable for the next hosted validation, not yet a certificate
of macOS ABI/runtime success, external shortcut delivery, or acceptable enabled
monitor overhead. Production and test sources were not modified by this reviewer.
Completed portable validations were not rerun merely to duplicate their results.

Reviewed the architecture and validation documents, `MacLocalInputMonitor`,
`MacLocalInputMonitorProbe`, `MacNativeSaveCandidate`, menu integration, shell
setup/finally teardown, Flow and posted-fault probes, appended telemetry vocabulary
and session API, strict NativeAcceptance parser, JSON independent input inventory,
CI summary filters, and associated portable fixture contracts.

## Material contracts checked

| Boundary | Evidence and assessment |
| --- | --- |
| Default off | `TryInstall` returns before native API/admission allocation; explicit `NativeApi` static constructor prevents early `beforefieldinit` construction. No Block construction or monitor installation occurs on the ordinary disabled path. The explicit ABI diagnostic is a deliberate separate opt-in control. |
| Global Block | Sequential 32-byte literal and 24-byte signature descriptor on the two supported 64-bit RIDs; global/signature flags only, native system symbol **address**, no captured managed object or GCHandle. Stable unmanaged storage and the loaded runtime remain process-lifetime. Invoke uses the exact Block/event-to-event C ABI. |
| Ownership | Admission is reserved before Add. A borrowed nonzero token receives one explicit retain; cleanup releases only that reference. Nil/Add/retain failures preserve optional-instrumentation semantics. Passivation precedes remove/release and shell views/pool teardown. Remove/release failure permanently retains a passive owner and denies reinstallation, without retry or dangling callback storage. |
| Callback transparency | Classification and telemetry nonfatal failures are contained; every path returns the original borrowed event. No candidate pointer, input string, ambient Activity, or cross-callback producer lease is retained. Existing fatal out-of-memory policy is deliberately unchanged. Managed containment does not certify Objective-C exception or native crash containment. |
| Shared candidate filter | Event metadata is checked before the native one-character UTF-16 read. Only the fixed Command-S family is considered; no competing keybinding interpretation, managed input text, or Save dispatch is introduced. The menu still forwards exactly once through its lexical superclass and preserves the exact BOOL result. |
| Telemetry | Six appended enum values preserve old IDs/schema v1. The typed API admits only these fixed events, with zero duration, fixed success/failure outcome and empty attributes. The common recorder takes a short current-sink producer lease and uses session identity, never ambient Save identity. |
| Interpretation | Strict reader retains the closed six-event/status/privacy contract. JSON and CI inventories keep candidate, menu, and Save request evidence separate; candidate-to-menu and menu-to-request edges remain unknown. A missing row, even in a normal session, is not absence certification; Save success criteria are not weakened or made dependent on monitor positives. |

## Probe quality and resolved pre-finalization risks

The final probe checks actual system `_Block_copy` identity and typed invoke
before/during/after actual Add/Remove, using both nil and **nonnil** synthetic
s/S/x events. `CreateKey` rejects a nil construction. `RemovedSuccessfully` is
required before the success marker, so swallowed removal failure cannot pass the
control. No event is posted or routed. The CLI enters Flow before telemetry
configuration; the ABI control explicitly rejects an enabled session rather than
silently mixing synthetic candidates with ordinary workload observations.

Earlier shared-testhost admission poisoning is resolved: fake tests use private
single-owner admission holders, with no production reset API. Final tests cover
failed removal and failed release, passivation during removal, exact ownership
counts, no double cleanup, no reinstallation, and the production disabled guard.
The beforefieldinit, nil-only control, and removal-failure false-positive concerns
were resolved before this verdict; they are not reopened findings.

The independent posted-fault probe queues one hostile callback before a later
successful Flow item. It requires exactly one primary invocation and exactly one
hostile `Message` access through the real platform reporter. That getter throws
before NSAlert construction, so the probe tests primary/secondary fault containment
and queue continuation without presenting a modal dialog. Normal Flow rendering
assertions still run afterward. This control does not certify all AppKit selectors
or every failure-reporting path.

## Validation limits and next evidence

The implementation owner reports warning-free Release build and portable monitor
4/4, telemetry/input/menu/posted 25/25, generic parser 8/8, JSON inventory 43/43 and
CI summary 39/39. This review inspected those test contracts but did not repeat
their completed execution. See [retained implementation validation](../validation/native-local-input-monitor.md).

Before claiming native coverage, both osx-x64 and osx-arm64 hosted Native AOT runs
must retain the actual ABI/install/remove and posted-fault continuation markers.
Ordinary one-attempt external Command-S workload traces must independently retain
monitor candidates alongside menu/request inventories, exact disk bytes, actual
exit codes and fresh reopen evidence. A successful direct ABI control is not an
external delivery witness. Failure prefixes remain censored, not negative proof.
Enabled per-key latency/CPU/native allocation on the new Mac binary remains
unmeasured; no performance nonregression claim follows from Windows measurements.

## External grounding checked

* [Clang Apple Block ABI](https://clang.llvm.org/docs/Block-ABI-Apple.html)
  supplies the layout, conditional descriptor signature, global/signature flags,
  global symbol-address and no-capture storage semantics used here.
* [Apple event-monitor guide](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/EventOverview/MonitoringEvents/MonitoringEvents.html)
  documents local event return, main-thread callbacks, non-owned tokens and
  explicit early removal. Mote's extra retain is distinct from ownership of the
  original returned token.
* [dotnet/macios production Block implementation](https://github.com/dotnet/macios/blob/main/src/ObjCRuntime/Blocks.cs)
  corroborates prefix field order, data-symbol lookup and typed native trampoline
  conventions; its captured-delegate machinery is not required for this global
  no-capture design.

These primary sources were checked during this review. Their contracts support
the narrow design, not runtime evidence for mote's new bridge on either RID.
