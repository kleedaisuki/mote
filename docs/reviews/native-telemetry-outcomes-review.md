# Independent review: native telemetry outcome truth

Date: 2026-10-01. Reviewed commit: `316d7c93f6ae3b8e7f12eb32efa703e5d1c5b0e3`.
Scope: `StartSave`, complete idle publication, their new outcome tests, the
underlying scope implementation, and synchronous publication reentrancy contracts.
This review does not certify complete command-to-durable-save tracing.

## Verdict

The two narrow corrections are sound: explicit overwrite rejection now records
Cancelled while the Save scope is live, and a throwing complete idle publication
cannot dispose a Success span before the caller handles the exception. No new
Save operation, byte transformation, approval rule, or retry was introduced.

At the initial review, one material **pre-existing** complete-idle publication ownership gap remained. The follow-up below records its resolution in `4f0ad17`.
It is not a regression introduced by this commit, but prevents interpreting every
Success/AnalysisPublished record as a still-current, completely installed result.
Repair it while strengthening causal provenance, not by adding more log statements.

## P2 (resolved in 4f0ad17): idle publication may install superseded semantics and certify success

Location at reviewed commit: `src/Mote.Native/NativeEditorController.cs:2024-2036`
(entry ownership checks), `2067-2078` (external installation and terminal status),
and `2299` (synchronous `_shell.SetAnalysis` callback).

Trigger and executable path:

1. A Complete result for version V and the current viewport passes the entry
   document/driver/policy/version/page checks.
2. `PresentAnalysis` synchronously calls the native shell's `SetAnalysis`.
   That installation may reenter controller source edit, viewport resize,
   document replacement, or disposal. The new edit can advance the document to
   V+1 and install a fresh binding/semantic state before returning.
3. `PublishIdleFullAnalysis` performs no ownership check after this callback.
   It projects old tokens/diagnostics using the now-current page fields and calls
   `SetCanvasSemantics` with version V. This can overwrite fresh semantics with
   stale coordinates (or call a disposed shell).
4. If callbacks return normally, it emits `analysis.published` for V and marks
   the presentation Success even though the accepted publication was superseded.
   The semantics callback itself is another external reentrancy boundary; a
   mutation there similarly precedes an unguarded terminal success.

Evidence that reentrancy is supported, not a speculative concurrency model:

- `tests/Mote.Tests/NativeControllerTests.cs:1601-1604` explicitly models source
  resize inside pane installation with `DuringAnalysisApply`.
- `NativeFlowIntegrationTests.Flow_initial_pending_layout_reentrant_canvas_resize_keeps_latest_request`
  invokes a real controller resize inside that callback.
- `TomlKnownErrorControllerTests.Toml_known_error_reentrant_edit_refuses_old_source_overlay`
  commits a real versioned canvas edit inside it.
- Main session publication, controller lines `1828-1834`, rechecks request
  serial/cancellation after pane installation and again after semantics
  installation. The TOML merge path at `2091-2095` checks frame identity,
  disposal and source version after its pane callback.

Impact: stale visible semantic ownership and misleading publication provenance;
not a demonstrated file Save/data-loss bug or evidence of a hosted occurrence.
Confidence: high in the source-established path; no new native execution or
focused reproduction was performed in this review.

Minimal fail-closed remedy:

- Capture the accepted document/driver/policy, source version, viewport range,
  and publication/request identity before external callbacks. Use a request or
  presentation identity to reject same-version supersession, not version alone.
- After `PresentAnalysis`, revalidate before installing old semantics; revalidate
  again after `SetCanvasSemantics` before recording AnalysisPublished/Success.
- Supersession is Cancelled (or another explicitly documented non-success
  outcome), not an exception Failure. A real projection/installation exception
  must still retain Failure and propagate unchanged.
- A reentrant callback cannot necessarily be rolled back. The contract is that
  obsolete work cannot resume and certify/overwrite newer state.

Focused missing tests: inject a one-shot pane callback that commits a newer edit;
assert no later old-version semantics and no successful old publication. Repeat
with same-version viewport/request supersession, disposal/document replacement,
and mutation from the semantics callback. Assert only the stale operation is
cancelled and the newer publication remains attributable. Reuse the existing fake
shell/Telemetry collection; do not require native OS input for this contract.

## Narrow-change verification

| Surface | Assessment |
| --- | --- |
| Declined overwrite | `cancelled = true` and `SetStatus(Cancelled)` occur before scope disposal; queued callback clears busy and returns without SaveCompleted. Confirmation rejection on disposal/post rejection follows the same path. |
| Approved overwrite / ordinary Save | Existing Engine calls, token, path, and completion ordering are unchanged. No snapshot is recaptured by the telemetry patch. |
| Save failure | Existing caught exceptions retain Failure and failure-event classification. The change does not introduce a cancellation token or claim to classify every OperationCanceledException/abnormal exit. |
| Scope lifetime | Save span covers worker execution and posting the UI completion, not the later UI callback. Preserve this distinction until request-owned causal integration. A newer edit does not change this span's existing coarse meaning. |
| Idle exception | Failure is assigned before projection/native callbacks; Success only follows their normal return and the publication record. The same exception propagates; no catch or fallback was added. |
| Privacy / disabled behavior | Only status mutations were added; no content/path/message/dimension/tag/schema additions. Null scopes remain no-ops. |

The new five-case suite calls the shipped private methods, uses repository-local
`RepoTemp`, and independently asserts Save bytes/dirty state plus serialized
outcomes. Native callback exception identity is asserted; the projection null
fixture is an intentional fault injection, not a claimed production occurrence.
Trace parsing follows shutdown and sentinel SECRET data is checked absent.
Telemetry collection serialization protects the shared global sink. State-based
queue pumping has a bounded deadlock guard; timing is not a performance oracle.
The tests do **not** cover publication supersession or edit-during-Save, so those
claims must not be inferred from their pass count.

Reused already recorded local Release results in
`docs/validation/native-telemetry-outcomes.md`: new outcomes **5/5**, existing
paint/overwrite regressions **13/13**. No completed suite was rerun; no production,
test, CI, or native acceptance artifact was modified by this reviewer.

## Scope and remaining infrastructure

This commit resolves the audit's two specified outcome-defaulting errors, not its
separate recoverable-prefix, command-admission, snapshot-version, Save-subphase,
request-parentage, physical-presentation or crash-durability gaps. Source evidence
and local injected tests are distinct from hosted execution certificates.
Relevant contract guidance: [.NET distributed tracing concepts](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts)
explains explicit parentage; causal truth must follow operation ownership rather
than timestamp proximity or merely a normal callback return.


## Follow-up review: 4f0ad17 resolves the identified ownership gap

Reviewed `4f0ad173b7fda5b574ea003549a128912d32ed53` production and test diff
against the original finding and actual `PresentAnalysis`/presentation identity
semantics. **No substantive new defect was found in this narrow repair.** The
previous P2 is resolved for the complete idle publication path.

Both post-callback checks are present: the first precedes semantic submission;
the second precedes AnalysisPublished/Success. The predicate checks disposal,
reference identity of document/driver/policy, source version, document generation,
page start/length, projection reference, analysis serial, and exact installed
presentation identity. `PresentAnalysis` assigns exactly the checked next
presentation sequence before calling the native shell; nested installation
advances/replaces that identity, so even a same-version replacement is rejected.
Generation also distinguishes replacement documents whose versions happen to
match. No callback or await separates the captured facts from the initial
projection/install invocation.

Supersession calls one Discard helper, assigns Cancelled, records one
AnalysisDiscarded and returns. It cannot subsequently emit AnalysisPublished or
Success. A projection or either native installation exception still unwinds the
Failure-initialized scope and preserves exception propagation. The old operation
cannot alter the outcome of a newer scope. No Save, byte, telemetry schema or
privacy contract changes occur in this follow-up.

The six added cases distinguish pane callback from semantic callback and check
actual overlay ownership, not just status strings: newer edits preserve the newer
version; same-version replacement preserves its distinct coverage; viewport-only
supersession preserves the prior overlay. They also require one scope, exact
Cancelled/Success, exact discarded/published counts, single hook invocation, and
absence of SECRET payloads. The semantic hook stores its overlay before invoking
the nested transition, meaning assertions genuinely test whether stale work
resumes and overwrites/certifies a replacement. One-shot hook replacement prevents
fixture recursion without weakening the shipped callback path.

Limits remain explicit: document replacement/disposal and every individual guard
are source-reviewed rather than separately injected here. Viewport mutation uses
a controlled private-field change; real reentrant resize/edit behavior is already
established by the adjacent tests cited above. The repair cannot undo an external
installation call already in progress; its guarantee is that stale controller
work does not resume after reentry to overwrite newer semantics or certify
success. It does not claim rollback of arbitrary native side effects.

Reused recorded local results in `docs/validation/native-telemetry-outcomes.md`:
expanded outcomes **11/11** and paint/controller regressions **164/164**. These
completed checks were not rerun; no specific uncovered executable defect required
another experiment. No production, test or CI file was edited for this review,
and no new hosted/native-AOT or performance certification is implied.
