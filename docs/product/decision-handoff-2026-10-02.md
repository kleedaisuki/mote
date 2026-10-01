# Decision handoff: implementation and research checkpoint

Date: 2026-10-02. The user requests finishing the current implementation and
research streams, then pausing the goal for their decision. The full goal is
not achieved and its scope is not reduced.

## Implementation stream

- Local commit `76c70fc` integrates the explicit, window-lifetime
  `--native-source` candidate into the existing Windows/macOS product shells.
  Canonical Engine text/history/I/O, stamped native admission, exact
  acknowledgments, range-based history synchronization, marked-input barriers,
  explicit pending-input recovery, existing chrome and visible decorations are
  implemented. Ordinary Continuous, LegacyPage and historical routes remain.
- `1c06418` adds five closed native-source telemetry phases without changing
  established numeric operation IDs. Strict-reader names are explicitly extended.
- Qualification is separate: frozen binding 34/34 including 1,000 seam cases;
  selected product suite 78/78; affected compatibility regression 258/258;
  Python vocabulary 4/4; final Release build zero warnings/errors. Final Windows
  modal/focus refinements have compilation/static review, not native input proof.
  [Independent review](../reviews/native-source-product-integration-review.md)
  and per-component validation records retain failures and scope.
- Local `657fa29` retains the already-reviewed diagnostic-only Unicode stream
  import alternative, with 20/20 isolated callback/ABI tests. Product shells do
  not adopt it. Native callback/control behavior and speedup remain unverified.
- Local `766a93d` adds bounded pre-Save diagnostic stages/deepest error codes.
  It does not change Save behavior or reinterpret the earlier pre-Save failure.

This is a coherent, reviewable implementation checkpoint, **not completion of
native product acceptance**. Native AOT, real input/IME, reader, capacity,
new-process reopen and experience qualification remain outstanding for the new
candidate. No new GUI, performance run or CI dispatch was started after the
standards-review hold. Local commits are not pushed; the remote baseline remains
`6827cd1a19142c9ad766d296c18d0770e600e79c`.

## Research stream

1. [Delivery alternatives](../research/delivery-constraint-alternatives.md),
   retained in `9187b28`, separates packaging, AOT/CoreCLR and native/toolkit UI.
   Its recommendation is to consider application packages/selected companions
   without requiring a CoreCLR migration. The isolated console measurements are
   not mote GUI performance, and AvaloniaEdit's verified preedit limitation is
   not evidence that every committed IME input is broken.
2. [Independent standards audit](../research/editor-experience-standard-audit.md),
   retained in `b097994`, finds unvalidated ordinary-workload labels, numerical
   authority drift and representation-coupled observers. It proposes keeping
   integrity/six-format/global-operation safeguards while separating ordinary
   tasks, capacity stress and mechanism experiments. Existing hypotheses are
   not approved SLOs; no tests, failures or capacity coverage were deleted.

Both studies contain source references, reproducible retained evidence,
specialist reports and independent consistency reviews. No recommendations were
silently adopted as a delivery/default/acceptance contract change.

## Decisions reserved for the user

| Decision | Alternatives and boundary |
| --- | --- |
| Delivery | Keep a literal binary, or permit an application directory/`.app` and reviewed companions; either can retain AOT. |
| Execution model | AOT remains possible; self-contained CoreCLR/R2R is a separate compatibility/diagnostics trade-off requiring matched real-app evidence. |
| UI direction | Continue qualifying the held native candidate, investigate a corrected toolkit surface, or choose another representation after task-based comparison. No default switch has occurred. |
| Experience standard | Review the proposed task/endpoints hierarchy before assigning numerical approval or commissioning more optimization. Do not use the old size ladder as demand evidence. |

On explicit resume, first consume the user's decisions and this checkpoint.
Do not automatically dispatch old benchmarks, rerun passed model suites, promote
the candidate or treat the studies as approval. Preserve `~/.mote`, lossless
editing/Save, six formats, no-workspace behavior and established external routes.
