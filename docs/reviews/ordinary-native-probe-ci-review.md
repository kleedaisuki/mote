# Ordinary native probes: independent CI integration review

Date: 2026-10-01. Integration pinned at
`0510385728c25f5ef3dd068cb3adaf1bd00c1137`. Scope: three additive CI steps and
their shared PowerShell execution/classification block. Codec labels were checked
against `c20d3e1`; capability report shape remains unchanged by the private
admission-root seam `7b32321`. Source admission/model/native implementations have
separate owners and are not certified by this workflow review.

## Verdict

**No substantive defect found in this scoped integration.** It is suitable for
the next four-RID hosted run, not evidence that either new published-image probe
has already executed. No classification suite, AST check, build, process launch
or local GUI experiment was rerun for this review. Production sources and the
workflow were not edited by this reviewer.

## Owned execution and evidence lifetime

| Contract | Checked behavior |
| --- | --- |
| Admission | Existing native matrix and strict publish inventory select the exact per-RID executable. Declared disposable Windows/macOS hosted guards precede launch. Fresh repository `.cache` output and existing ancestors reject redirection/reuse; isolated MOTE_HOME is passed to the child. These mutable identifiers are scope guards, not host security attestation. |
| Exact process | ProcessStartInfo uses explicit executable, ArgumentList, no shell execution and redirected stdout/stderr. Both current probes create no child processes in their source contract. Cleanup owns only this returned Process, not a global PID, process-name match or descendant tree; metadata explicitly says descendants are not certified. |
| Concurrent stream drain | Two BaseStream CopyToAsync tasks start before waiting for exit, writing to separate CreateNew retained files. This avoids blocking the producer on sequential pipe reads. No PowerShell scriptblock is scheduled as a task-thread callback without a runspace. |
| Execution/cleanup waits | Initial waits are 60 seconds for codecs and 120 for capability. A single cleanup stopwatch then supplies the remaining portion of ten seconds to process exit and both copy tasks, not independent ten-second extensions. Two-/three-minute CI step limits are unchanged by classification. |
| Unknown handling | Only proven exit exposes the actual numeric code; forced cleanup, timeout, unproven exit or incomplete/faulted copies deny qualification. Cancellation and disposal close owned stream/process resources without an indefinite wait for an incomplete task. Already-faulted copy exceptions are observed. Raw prefixes are retained, not deleted or replaced with another attempt. |
| Persistence | Fresh stdout/stderr files and supervisor output use exclusive creation; report/trace framing and size checks precede selected-field classification. Final metadata uses fixed outward error classes rather than exception text. Prefix retention is best-effort diagnostic evidence, not per-byte durable flushing or a hard kernel-call deadline certificate. |

Microsoft's [finite process wait contract](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexit?view=net-10.0)
and [asynchronous stream-copy contract](https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.copytoasync?view=net-10.0)
support separate process and stream completion observations. The wrapper does not
use an unbounded parameterless process wait to manufacture stream completeness.
Unknown completion remains unknown even after cancellation/disposal. In-progress
copy cancellation is not itself claimed to prove the entire prefix was captured.

## Qualification boundaries

**Codec is blocking:** zero process exit, both complete streams, unchanged image
hash, exact runtime architecture, bounded report with the exact five DTO keys,
integer schema 1, success/none result, seven fixed labels in order and the exact
trimmed stdout marker are required. The seven labels cover the embedded
GBK/GB18030/Big5 and Unicode/default-UTF8 checks; absent, reordered or incorrectly
typed labels cannot be accepted. This is a published-runtime codec contract,
not an encoding-picker GUI certificate.

**Source capability is non-gating:** selected report witnesses require one final
probe completion, matching binary identity/runtime architecture and healthy
telemetry, no reported failure, paired entered/completed phases, and exact-save
plus fresh-Document reopen witnesses for each of the three fixed fixtures.
Bounded newline-terminated trace files must contain one successful session and
no observed dropped-record row. Successful result remains `observed`, not a
product pass, physical input/paint certificate or demonstrated editable-size
threshold.

These capability and trace checks are **not a closed full-schema/privacy audit,
complete causal graph check, lossless transport certificate or generic semantic
validation of every retained row**. They select positive witnesses from raw
owned producer reports; native source-level contracts and the strict trace
auditors remain separate evidence. In particular, no observed drop does not
prove no event was lost, and fresh Document reopen is not another native GUI
launch. This limitation must accompany any later hosted summary.

During review, coarse error attribution was clarified without changing the
acceptance predicates: an unproven completion is cleanup, a nonzero exit or
incomplete stream is process_evidence if no earlier error exists, and failed
image readback/hash equality is image_identity. These outcomes no longer leave
a null outward error class for an unknown result; original raw evidence remains.

## Retained static/portable evidence

Inspected `.cache/validation/native-codec-source-workflow/` completed artifacts,
without invoking their tests again:

* `classification-results.json`: 28 validation-only cases; eight four-RID/probe
  positives and twenty marker/schema/order/architecture/exit/timeout/stream/
  phase/fixture/image/trace negatives have the expected classifications.
* `error-classification-results.json`: four affected error-category follow-ups
  retain unknown status and fixed cleanup/process_evidence/image_identity classes.
* `workflow-static.json`: three new steps, zero new jobs; removing those steps
  restores the prior workflow definition and the anchor/alias literals match.
* The owner retained in-memory CopyToAsync/WaitAll overload and drain arithmetic
  checks, not a native child or pipe experiment. PowerShell AST/static results
  do not substitute for process execution.

The shared script's LF-normalized UTF-8 SHA-256 independently matches
`ad5dfbc9c4e3c2f26e32fc63069181b12b6c716c3ebdd78efa84eb558c59d540`.
Retained script raw CRLF file SHA differs by newline representation; it is not
mistaken for a source-identity mismatch. GitHub's current
[anchor/alias contract](https://docs.github.com/en/actions/reference/workflows-and-actions/reusing-workflow-configurations#yaml-anchors-and-aliases)
supports this reused scalar run block; no workflow helper/framework/job was added.

## Next runtime evidence

All existing gate/probe/pin contracts remain intact. Always-attempted 14-day
artifact retention includes hidden files, raw streams, reports, generated
fixtures, isolated homes and traces for both areas. A green job cannot qualify
the non-gating capability step. Actual four-RID numeric exits, complete copies,
codec reports and source witnesses remain pending the reviewed coordinator push.
This verdict does not resolve pending source-admission qualification, native
platform behavior or GUI/IME performance, and does not authorize a local retry,
global input, activation change, PID/tree kill or deletion of earlier evidence.
