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

The initial review required a cleanup/schema integration recheck; that recheck follows below.

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

Validation evidence: reviewer independently ran 27/27 portable driver guards twice,
including the final driver snapshot and pins. The
implementation owner reported focused managed tests **24/24**, including the five new recorder/path/ABI tests and a zero-warning
build; those are owner-provided evidence, not a duplicate reviewer execution.
`git diff --check` passed on the final visible edits. Mac native compilation,
tracking/close ordering and exact-PID external action outcomes remain unexecuted
on this Windows host and require raw CI artifact audit on both Mac RIDs.

**No unresolved material implementation finding was identified in the corrected
visible first-pair slice.** Approval is for running this bounded experiment,
not for the unresolved product AX contract, selector-history absence, Native AOT
causality, VoiceOver, physical input or release readiness. The original two
findings above are retained as resolved review history, not current blockers.

## Scalar schema repair review: 597317a / 0159a6b

The second hosted first-pair execution,
[CI 36816778414](https://github.com/kleedaisuki/mote/actions/runs/36816778414),
compiled and ran both fresh sessions on both Mac RIDs, but its strict client
report parser rejected all four final client JSON files. This does not revise
the earlier implementation review into an executed acceptance claim: raw
artifact audit exposed a concrete observation-schema defect missed by portable
source checks.

The review independently ran the repository-cache reproduction
`.cache/ci-36816778414-grid-pair/schema-repro.ps1`: all four unmodified client
reports are rejected; fresh parsed in-memory copies pass the same parser after
converting only `owned_target` and the four nullable CFEqual-derived identity
fields to Boolean scalars. All four raw server reports already pass. No raw
file was rewritten, and these copies are not acceptance artifacts.

`597317a` adds `PairBoolean(BOOL value)`, returning the canonical `@YES` or
`@NO` object, and uses it only for owned-target classification and `EqualObject`.
Failed/unknown identity still remains `NSNull`; original AX calls, actions,
returns, discovery, 128-call audit bound, timing, cleanup and product code are
unchanged. This is a coherent serialization fix, not numeric coercion in the
strict parser. Clang's [Objective-C literal documentation](https://clang.llvm.org/docs/ObjectiveCLiterals.html)
distinguishes Boolean literals from type-dependent boxed scalar expressions;
Google's [Objective-C production style guide](https://google.github.io/styleguide/objcguide.html)
explicitly warns about boxing general integral/conditional expressions as
Boolean values. These support the repair rationale; only another native
execution can certify emitted JSON on both hosted toolchains.

`0159a6b` adds rejection tests for numeric owned-target and equality fields,
positive false/null identity cases, and source-contract checks for the canonical
helper/call sites. Reviewer independently ran the final guard suite: **37/37**.
The source-contract regex checks are intentionally structural, not a Foundation
serialization experiment. No unresolved material defect was found in this
scalar-only repair. Workflow pin updates remain with the integration owner;
this reviewer did not modify the concurrently edited workflow.

### Separate cleanup-timing proposal: design only

The same raw server ledger records C0 ARM as one entry/request/dispatch, zero
opens/closes, normal shutdown and one detach. C0 x64 records one open/close;
both P0 sessions record one open/close and the original external error -25205.
C0 ARM's raw external error is zero. These observed facts are sufficient to
identify incomplete control popup lifecycle, not to infer the exact instant of
finish consumption or prove a particular scheduling cause. The control's
`present` refusal after `finishConsumed` makes early cleanup a concrete possible
mechanism. AX success remains the original triggered-action reply, not a claim
that the complete visible popup lifecycle passed.

Keep the next execution **scalar-only**, so report repair is not confounded
with a changed cleanup intervention. If a separate paired timing discriminator
is later authorized, prefer one fixed common 250 ms observation hold after the
primary reply/identity audit and before publishing finish, subject to the same
55-second client deadline. Preserve the original reply on disk first. Apply
the identical hold to C0 and P0; no readiness retry, action retry, event injection
or global input is needed. If insufficient deadline remains, record unresolved
rather than exceed the budget or fabricate lifecycle completion.

This is leaner than adding a new owner event marker and polling it up to one
second: event-conditioned cleanup changes the intervention time according to
the outcome and expands the observer protocol. Such a handshake can be useful
later if the fixed hold leaves lifecycle unresolved, but is not required for
the next narrow discriminator. A 250 ms hold is a deliberately tested timing
condition, **not a guaranteed scheduling bound**. Continue reporting exact
entry/request/dispatch/open/close/detach counts, and do not label a zero-open
control as full lifecycle acceptance merely because its external AX reply or
aggregate diagnostic completion is successful. No timing code was changed by
this review.
