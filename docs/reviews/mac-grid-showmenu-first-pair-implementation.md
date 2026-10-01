# Review: lean macOS Grid ShowMenu first-pair implementation

Date: 2026-10-01. Status: integration recheck complete; both identified issues corrected.

## Scope and evidence

Reviewed the lean C0/P0 design and its independent design review, the current
`MacGridPairObservation.cs`, `MacCsvGrid.ShowMenuDiscriminator.cs`, product
accessibility/lifetime hooks, and new `Client.m`, `Control.m`, `Run.ps1` and
`Test-Guards.ps1`. No production files or workflow were edited by this reviewer.
Native AppKit compilation and execution are not available on this Windows host.

Portable driver guard suite ran successfully: **27 checks**. This validates
fixture/containment and selected report schema rejection paths, not Objective-C
compilation, timer-mode behavior, external AX replies or product acceptance.

## Necessary correction identified during integration

### Resolved P2 — Product cleanup must not erase its own menu-close evidence

Location: `MacCsvGrid.ShowMenuDiscriminator.cs::PollPairDiagnostic`,
`MacCsvGrid.Accessibility.cs::CancelAccessibilityMenu` and
`AccessibilityMenuTransition`, `MacCsvGrid.cs::Dispose`.

The diagnostic timer runs in both default and event-tracking modes. After the
client's reply/audit, a finish marker may therefore be consumed while
`PresentAccessibilityMenu` is still inside the native popup's tracking loop.
The initial implementation synchronously calls `CancelAccessibilityMenu`, then
`performClose:`. Cancellation clears `accessibilityShownMenu` immediately;
`AccessibilityMenuTransition` records close only when that relation still equals
the menu. Disposal subsequently clears the menu delegate and managed instance
lookup. Thus the actual ensuing menu-close notification can be omitted from P0's
ledger even though the menu genuinely closes. C0 instead cancels tracking and
queues its final shutdown in the default mode, preserving its delegate until
tracking unwinds.

This is a material evidence-completeness and matched-cleanup defect, not an
asserted use-after-free: the existing popup implementation explicitly retains
its native participants across reentrant disposal. It does not establish the
cause of the original external AX error.

Practical correction: finish consumption should cancel only the owned tracked
menu without destroying the current shown-menu relation, then defer final
window close/export/disposal until tracking has unwound. Preserve close
accounting at the original delegate callback. Cancel an admitted-but-not-yet-
dispatched request explicitly; do not permit cleanup to create another action.
Driver normal exit and forced cleanup remain independent facts.

### Resolved P2 — Align control lifetime ledger with accepted report bound

Location: `Control.m::lives/Lifetime/Facts` versus
`Run.ps1::Assert-ServerReport`.

The initial native control stores 32 lifetime rows, whereas the product and
validator admit at most 16. If unexpected transitions produce 17-32 rows, the
control reports zero lifetime overflow yet the driver rejects the complete
report, concealing the distinguishing lifecycle facts from its aggregate
session report (the raw file is still uploaded). Use the same 16-row bound and
preserve total/overflow accounting, or explicitly coordinate a larger shared
schema. Expected five-row normal sessions do not trigger this mismatch.

## Preserved positive contracts and limits

- The product retains original action return/refusal conditions and observes
  off-main refusal without consulting the mutable owner dictionary.
- Product entry accounting begins before main-thread/owner admission; overflow
  totals are retained. Its callbacks populate fixed fields, not JSON or I/O.
- Client performs one exact-PID action with no selection setter, fallback,
  activation, event injection or action retry; initial and post-reply action
  facts are persisted before identity audit.
- Control does not copy the old hidden direct/NSInvocation warmup.
- The stage-1 schema explicitly reports unavailable inherited dispatcher
  observation and leaves `server_calls` empty; selector-absence conclusions are
  not supported.
- Repository-cache session confinement, fixed marker tokens, bounded discovery,
  action-name type/count checks, and post-audit unknown states are present.
- Neither completed-success-observed nor completed-failure-observed replaces
  the original product AX gate or implies VoiceOver, IME or release acceptance.

This review is not final implementation approval. Recheck the corrected cleanup,
shared schema bounds, frozen CI hashes and executed Mac artifacts after integration.

## Integration recheck and current verdict

The product now invalidates its timer on finish, cancels its admitted pending
ShowMenu selector without clearing the shown-menu relation, cancels tracking
only for the exact owned shown menu, and schedules `moteGridPairClose:` in the
default mode. Final close therefore follows tracking unwind; `Dispose` also
cancels delayed diagnostic close before native delegate release and exports the
post-detachment ledger. The close-notification loss identified above is
corrected without changing the original AX action return.

The native control now uses the shared 16-row lifetime limit and records false
current/attachment/epoch flags after detachment. Product dispatch observations
cover refusal attempts before `TryBegin`, and its detach observation follows
root removal. These are more accurate sampled lifetime facts, not a promise
that native control and document frame semantics are identical.

Client revision `949578f` preserves bounded traversal through generic AX
containers, including split groups, while pruning Table/Row/Column subtrees.
Only `kAXErrorAttributeUnsupported` on count denotes a leaf; ownership, timeout,
budget, invalid types and oversized child counts remain unavailable. The
expected Group is the Table's discovered direct Group parent rather than an
unrelated global Group. No additional graph attributes are requested before
the primary action beyond this discovery contract.

All four workflow LF/CRLF SHA-256 pairs match the reviewed source/driver/guard
bytes. The workflow has a five-minute step timeout, two owner-bounded sessions,
non-gating experimental classification, and uploads fixed raw client/server
reports even if aggregate admission fails. Neither a nonzero original AX reply
nor green workflow completion is relabeled a product pass. Partial primary
reply files survive client/post-audit/watchdog failure as raw evidence.

Validation evidence: reviewer independently ran 27/27 portable driver guards
before these native-only revisions; driver/guard hashes remain unchanged. The
implementation owner reported targeted managed tests **5/5** and a zero-warning
build; those are owner-provided evidence, not a duplicate reviewer execution.
`git diff --check` passed on the final visible edits. Mac native compilation,
tracking/close ordering and exact-PID external action outcomes remain unexecuted
on this Windows host and require raw CI artifact audit on both Mac RIDs.

**No unresolved material implementation finding was identified in the corrected
visible first-pair slice.** Approval is for running this bounded experiment,
not for the unresolved product AX contract, selector-history absence, Native AOT
causality, VoiceOver, physical input or release readiness. The original two
findings above are retained as resolved review history, not current blockers.
