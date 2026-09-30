# Review: TOML first-known-error production retention

Status: final coordinated review complete; no unresolved substantive finding.
Date: 2026-10-01.
Reviewer scope: the coordinated Formats and Native change against
`docs/semantic-policy-frontier.md`, not general TOML grammar or native editor acceptance.

## Findings

No substantive defect was found in the inspected production diff. This is approval of
the narrow known-error retention design with the focused validation evidence below; it is not a claim that all supported TOML source-order semantics were verified.

## Inspected boundaries and reasoning

- `TomlIncrementalSession.AnalyzeLarge`, `TryAnalyzeLarge`, `ProcessStatement`, and
  `OwnershipOutcome`: the private outcomes distinguish certified completion,
  uncertainty, and one observed conflict without changing public contracts. Every
  noncomment statement must pass standalone validated syntax and the pre-transition
  exhaustive/certifiable ownership gate. The post-transition gate rejects a conflict
  discovered while traversing an unsupported path or exhausting ownership memory.
  The first accepted witness stops scanning rather than recovering an unknown suffix.
- Existing 256 Ki statement, 64 physical-line, 120,000 statement, and 200,000 binding
  limits remain in force. Reader/loop/final cancellation checks remain; a canceled
  lexical projection or final cancellation cannot return or commit the witness.
  Each snapshot rebuilds the trie rather than reusing an old-version certificate.
- A partial result uses the existing bounded lexical viewport, Provisional coverage,
  and null total. It carries one absolute key span even when offscreen, while a
  successful admitted stream preserves Complete and an exact zero total. Neither
  source nor parser tree is retained by the private outcomes beyond the call.
- `NativeEditorController.VisibleSessionFrame` pairs the existing presentation with
  bounded source tokens/diagnostics and its viewport. All original invalidation
  sites still invalidate the pair; no separately stamped diagnostic cache was added.
- `PublishIdleFullAnalysis` retains the original document/driver/policy/version and
  viewport rejection guards. `TryMergeTomlKnownError` additionally requires the exact
  generation/version stamp, paired viewport, TOML policy, Provisional result, and
  visible error-severity `TOML_OWNERSHIP` witness. Offscreen witnesses remain globally
  unknown rather than becoming a false count or visible overlay.
- Merge preserves visible token, preview, Flow/Grid, map, and stamp payloads. It
  deduplicates by code/span and does not evict existing diagnostics at the 4,096 cap.
  The summary explicitly states global uncertainty. The guard after native
  presentation refuses a stale overlay if synchronous installation caused a new
  frame, disposal, or an edit. Complete promotion and unrelated partial publication
  remain on their existing paths.

## Evidence and limits

The reviewer inspected the frozen Formats diff, the final frozen coordinated Native diff,
`TomlOwnershipIndex`, surrounding scheduling/publication/invalidation paths, and the
new policy, differential, and controller test sources. `git diff --check` passed
apart from informational line-ending conversion warnings. No full suite was rerun
by this reviewer: the assigned independent validator and Native writer own focused
execution, avoiding duplicate validation.

The controller tests challenge the private idle publication boundary after ordinary
asynchronous publication, including identity mismatches and rendering preservation.
Synthetic seam cases alone do not certify the large-file idle pipeline, OS drawing, native accessibility, or physical presentation. Actual large-file fake-shell integration is identified below.
Independent large-policy and TOML-oracle evidence must remain separately identified.
Target-OS CI and GUI acceptance remain outside this local static review.


## Page-navigation correction and final validation

The initial frame-only implementation lost a known same-version error after page
navigation because the idle lane attempts Full once per version. The agreed scope
extension resolves that functional limitation: `RememberTomlKnownError` retains
exactly one valid positive-length diagnostic before viewport filtering, with
current document/driver/policy and generation/version identities. `MergeCachedTomlKnownError`
reprojects it into each newly accepted Visible frame only on span intersection.
DocumentChanged, replacement, policy replacement and disposal clear it; page-only
scheduling intentionally does not. It retains no tree or snapshot, and does not
restart Full. The active document/driver references are identity witnesses already
owned by the controller, not a cache of old document lifetimes.

Final code review also checked the added post-SetCanvasSemantics serial/cancellation/
frame guard, so synchronous shell mutation cannot offer stale idle work after
visible overlay installation. No new unresolved defect was found in this extension.

Evidence inspected without rerunning completed validation:

- `.cache/toml-known-error/policy.trx`: dedicated author policy suite 9/9 passed.
- `.temp/toml-known-error-differential/results/toml-known-error-differential.trx`:
  independent public-policy suite 24/24 passed. The independent validator records
  21/21 exact Python tomllib fixture classifications and a baseline reproduction of
  the lost witness in `docs/validation/toml-known-error-differential.md`. Shared TOML
  1.0/1.1 subset evidence is not universal grammar certification.
- `.cache/toml-known-error-native/toml-known-error-native.trx`: 22/22 focused Native
  tests passed before the final additive post-install guard (20 new cases plus two
  existing regression cases). The writer reports the final guarded source compiled
  with Release warnings as errors and its reentrant test passed 1/1 afterward; the
  reviewer did not mistake the earlier 22-case TRX for a full rerun of the final hash.
- Final test source includes the actual >4 MiB normal Visible-to-idle-Full pipeline,
  precise witness, repair/Undo/Redo, page-away/back, initially offscreen witness then
  navigation to its exact span, Open/JSON policy replacement, and disposal before
  idle publication. Rendering reference identities and disk preservation are checked.
  These execute the real format session and controller through the fake shell,
  not RichEdit/AppKit input or physical OS drawing.

Reviewed raw source SHA-256:

| Source | SHA-256 |
| --- | --- |
| `src/Mote.Formats/TomlIncrementalSession.cs` | `A4AB0C96584DA1400629669B50D0AC85F1EADEB46813BC15774813FF58E8E43D` |
| `src/Mote.Native/NativeEditorController.cs` | `AEEEECAD7F56F33013638F69B4E15C802C06F97B1FDC9E890552C7D5383236E4` |
| `tests/Mote.Tests/TomlKnownErrorControllerTests.cs` | `AD134656C6B6B1D0CE2CCCFE6FB4D5A6B50E183D80444B25BC8BDB872603335B` |

**Verdict:** no necessary correction remains in the reviewed bounded retention
slice. Target-platform CI, actual native rendering/input, arbitrary TOML grammar,
and mid-scan cancellation timing remain unverified by this review. No public API,
Engine, ABI, packaging, config/theme or parser ownership rule was changed.
