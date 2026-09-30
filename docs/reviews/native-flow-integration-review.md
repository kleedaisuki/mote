# Native Flow integration review

Date: 2026-09-30. Scope: uncommitted core Flow integration, configuration,
controller/driver/idle publication, compatibility preview adaptation and associated
focused tests. Platform renderer internals are independently owned/reviewed; this
review does not assert target-native acceptance.

## Finding requiring correction

**P1: legacy clipping can make valid structured input fail presentation.**
`NativePreviewBuilder.Builder.Csv` clips values with `value[..23] + "…"`;
`TreeNode` clips labels with `label[..119] + "…"`. The new `BuildFlow` sends
this text to `FlowRenderProjection`, whose `ValidateUnicode` correctly rejects
unpaired surrogates. A CSV value of 22 ASCII characters followed by an emoji and
one further character, or a tree scalar label of 118 ASCII characters followed by
an emoji and another character, leaves a high surrogate immediately before the
ellipsis. PowerShell UTF-16 reproduction yields code units 55357, 8230 at both
cut boundaries. The exception propagates through `AnalyzePresentationAsync` to
the controller's `Analysis failed` publication, removing otherwise valid preview
and diagnostics. This is a new integration failure, not a request to relax Unicode
validation. Fix the compatibility producer's clipping boundaries, bound text
before expensive normalization/concatenation, and test the actual driver path.

**Resolved by targeted re-review:** `Clip` backs off a high-surrogate boundary;
CSV clips before normalization and Tree clips before bounded concatenation.
`NativeFallbackFlowTests` now tests both original triggers through the actual
`AnalyzePresentationAsync` path. The lead reports focused Release
warnings-as-errors 24/24, with zero warnings/errors. Those completed checks were
not repeated. Strict Flow Unicode validation remains intact.

## Follow-up correctness checks

- `NativePreview.Truncated` is now explicit producer-owned omission state;
  `BuildFlow` no longer searches rendered text for a literal notice. A new actual
  driver-path regression proves a user's notice spelling is ordinary text.
  Cell/label clipping and tree depth omissions are recorded. **P2 resolved by final
  targeted source re-review:** the CSV column-omission branch now sets
  `_truncated = true` when appending the column ellipsis. The actual-driver
  regression `Csv_fallback_flow_reports_omitted_ninth_column` uses a short
  nine-column row, asserts Flow truncation and the omission decoration, and retains
  semantic completeness. The lead reports its focused Release warnings-as-errors
  run passed 1/1; it was not repeated here.
- Initial pending publication in `ScheduleAnalysis` now triggers layout before
  launching work. The serial/cancellation recheck appropriately avoids starting a
  superseded request, but existing FakeShell `SetAnalysis` has no reentrant layout
  callback in the original tests. **Core coverage resolved:** the added
  `Flow_initial_pending_layout_reentrant_canvas_resize_keeps_latest_request`
  synchronously resizes during the initial source-only publication and verifies
  latest sequence and successful Flow publication. Platform installation
  must likewise not overwrite a newer nested identity with the outer view.
- Mac `SetAnalysis` currently defers composition only in legacy mode. Continuous
  `ApplyAnalysis` immediately invokes the new split-layout path, which may resize
  source and call `FocusSource`. This differs from Windows all-mode deferral.
  The lead reports the platform worker added all-profile defer/replay and a
  reentrant installer guard; their targeted platform review is separate. Target
  owner must establish effective native handling before claiming candidate-safe Flow.
  No actual candidate disruption is asserted from source inspection alone.

## Positive conclusions from inspection

- Driver semaphore ownership extends across both Analyze and Render; no second
  version can interleave between them. Legacy `AnalyzeAsync` remains analysis-only.
  Rendering is explicitly non-committing by capability contract.
- Production publications receive a monotonic sequence; controller rejects an
  old same-version activation and a hidden-preview action. Publishing the retained
  map before the shell call allows nested callbacks to see the latest identity;
  exception rollback does not overwrite a newer nested map.
- Policy-driven Auto, explicit SourceOnly/Split overrides, and legacy Auto split
  are coherently centralized. No source mutation is introduced by layout.
- Flow retains precise inline source origins; the adapter deliberately preserves
  the established heading-marker reveal action without rewriting Flow provenance.
- Idle Full promotion retains document/policy/version and exact viewport checks;
  provisional Full cannot manufacture global diagnostics or erase visible facts.

## Scope and product limits

Previously reported focused controller 20/20 and Native warnings-as-errors build
0/0 are lead-provided evidence, not re-run here. The subsequent reported focused
24/24 covers the added pending-layout regression and corrected fallback admission.
New Flow tests also explicitly prove literal origin starts at source offset 3
while established heading reveal targets offset 0. These core checks do not
establish native styling/readback, actual IME or selection/Copy preservation.

Flow integration is a coherent incremental phase of the full rendering design,
not completion of mote: CSV Grid, safe resource/image loading, target accessibility
and real input acceptance, cross-platform performance, and the other documented
release gates remain required. No production changes or commits were made by this
reviewer.
