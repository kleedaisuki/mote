# Ordinary CSV native product readiness

Date: 2026-10-02. This is a runtime correctness investigation and correction,
not a latency benchmark or a new large-file requirement.

## Observation and discriminating evidence

The corrected local Windows release workflow completed its byte/task checks,
but `.temp/windows-release-corrected-suite-2/csv/native-product.png` showed
`CSV · Complete · v5` while the first two ordinary records displayed `[pending]`.
Complete semantic certification alone did not certify installed table delivery.

`NativeCsvReleaseReadinessTests` creates the actual `WindowsEditorShell` controls
and the actual `NativeEditorController` in an owned, hidden STA window. It omits
the shell's ShowWindow/SetFocus calls, uses isolated repository-local settings,
and never changes global input, clipboard, font, locale, or registry state.
Its three-record fixture has quoted Chinese text, a comma, and escaped quotes.
Native selection is placed in the last existing record. A controller viewport
analysis is requested explicitly because a hidden control's native geometry
does not imply that selection changes independently dispatch that analysis.

The pre-correction test observed the same real window's exact `Complete v0`
projection, nonpending ready navigation identity, retained native presentation
identity, and actual owner-data slots. Two of three slots were null; the ready
projection retained only record ordinal 2. This failed in 251 ms in
`.temp/csv-release-readiness/csv-before.trx`. The failure was an actual terminal
delivery mismatch, not evidence that an arbitrary screenshot delay was too short.

## Mechanism and correction

1. CSV Source anchor delivery correctly retained records starting at the source
   anchor: here only ordinal 2.
2. `MakeGridFrame` correctly normalized a three-record file with a larger native
   page to first ordinal 0.
3. Combining those independent facts incorrectly granted a ready frame for
   ordinals 0–2 without retained records 0 and 1. The native adapter truthfully
   rendered those absent records as pending. There was no hydration request.

The driver now accepts optional **captured native visible-row geometry** through
the existing dispatcher Work object. Under its existing serialized session gate,
a Source query whose certified exact extent would clamp its first record makes
one bounded ordinal query for the normalized first record. Only this actual
normalized payload is delivered to the controller. The second query uses the
same snapshot, source interests, column bounds, row limit, and scope, with an
empty edit chain because the first policy query already committed that version.

The controller remains in source-follow mode (`_gridAnchor == null`). There is
no UI retry loop, sleep, permanent detached Row anchor, forged ready identity,
or scrollbar first coordinate greater than its last coordinate. Public format
Source/Row anchor semantics and existing callers that omit the optional geometry
are unchanged. This correction applies to ordinary native CSV delivery, not a
new parser or optimization architecture.

Composite cancellation needs explicit handling: once the first query committed
its cache, canceling or faulting the second query must retire that session before
future edits use the old driver baseline. The implementation does so. A wrapper
around the actual CSV grammar cancels precisely on second-query admission;
the next revision rebuilds a fresh session and receives an empty edit chain.
No timer-based race is used to test this invariant.

## Post-correction runtime oracle

The actual hidden product check verifies:

- all three retained native slots are nonnull and have ordinals 0, 1, and 2;
- real `LVM_GETITEM` notifications return each of the nine decoded cell labels,
  including quoted Chinese/comma/escaped-quote values;
- installed native identity and frame equal the controller's current ready map;
- the actual native item count equals the retained slot count;
- a later UI-mailbox witness does not change that terminal ready identity;
- moving the same owned source control back to offset zero produces a new
  Source-anchored delivery and leaves controller source-follow mode intact.

Focused validation includes existing native Grid driver, dispatcher, and
controller contracts for edit baselines, cancellation, stale same-version
identity, version retirement, and navigation. Results are kept under
`.temp/csv-release-readiness/`. The final run,
`csv-final-native-labels.trx`, passed **37 / 37**, with zero failures, zero skips,
and zero build/analyzer warnings or errors. This includes both new regressions;
the native label assertions were present in that run.

This local managed Windows evidence is not fresh four-architecture Native AOT
qualification, actual macOS AppKit execution, physical keyboard/Pinyin evidence,
screen-reader certification, or display-to-photon measurement. The full release
workflow and fresh screenshot must be run on the integration commit separately.
