# Mac AX persistent-warning lifetime review

Date: 2026-10-01. Independent source/evidence review; no product or test edits,
build/test rerun, target AX execution, staging, commit, or push. Working-tree
baseline HEAD at inspection: `4ec81bcba4ab84b232654d86914174d4cc141f63`.

## Verdict

**No substantive defect or integration blocker found in the frozen warning-lifetime
delta. Approve the repair for target validation, not as already accepted on macOS.**
The repair addresses the demonstrated ownership error: a session capability
failure previously lived in replaceable document/analysis status. Moving that
failure to the existing persistent notice channel makes ordinary analysis updates
normal, rather than adding a special New retry or modifying parser policies.

## Evidence and contracts checked

- `NativeEditorController.UpdateStatusNotice` is the single controller writer of
  `SetStatusNotice`. It recomposes from latched AX, theme, reload, preview and
  settings facts, never from an already-enriched rendered string. Both attachment
  failure and runtime failure latch the same session state and refresh that
  composer. Document replacement does not clear it. The ordinary AX suffix and
  Continuous prefix are removed, avoiding a duplicate warning.
- Experimental/null-profile wording stays exactly `Accessibility provider
  unavailable`; Continuous wording stays exactly `AX unavailable: save, restart
  --legacy-page`. The latter's actionable restart instruction is not weakened.
  This inspection does not confuse the null-profile experimental Canvas probe
  with the distinct LegacyPage product profile, which has no Canvas provider.
- New/Open pending and ready analysis, retained analysis theme replay, theme
  failure, reload failure/success, edits and Undo can replace ordinary status but
  do not replace the persistent notice. The existing preedit guard defers notice
  publication until `CompositionSettled`; no new input commit was introduced.
- The changed fake now replaces ordinary status on `SetDocument`,
  `SetCanvasChrome` and `SetAnalysis`, including diagnostic-summary composition,
  and observes the notice separately. This corrects the specific AppKit fidelity
  gap that let the old regression pass. Changing `StartsWith` to `Contains` is
  justified by migrating placement into persistent chrome, and is accompanied by
  exact-message and single-occurrence assertions. Removing ordinary canvas-label
  assertions does not remove the effective health-warning contract: analysis
  already replaces those ordinary labels in production. Privacy, editing/history
  and alternate-profile warning checks remain.
- Deferred/error publications in the new test directly exercise the fake shell
  contract, not an actual parser-failure path. The implementation note correctly
  states this limitation. The four profile/fault combinations and composition
  settlement test cover the warning lifetime without changing product threading.
- Settings writer/path lifetime and telemetry code are unchanged. Trace-health
  suffix composition is unchanged; this review does not certify that pre-existing
  ordinary trace warnings survive all analysis publications. Grid hooks,
  scheduler, navigation and fault/probe stage transitions are unchanged by this
  scoped delta. Other working-tree Grid work was not reviewed here.

## AppKit observation and failure behavior

`EffectiveStatus` is now the shared rendering value. Once the native status field
exists, `ProbeCanvasStatus` reads its actual `NSTextField.stringValue` instead of
returning a raw model field that omits persistent notices. Before creation it
uses the composed fallback. `objc_msgSend` returns an object pointer through the
existing pointer-return overload, and `ObjC.ManagedString` copies NSString UTF-16
using the established range-based ABI on both architectures. No new callback
signature or native structure layout was added.

The accessor's callers are the probe's `Tick`/fault-state observation callbacks,
posted through the AppKit shell UI queue. They do not query the native field from
the timer worker. Probe exception guards retain failure rather than fabricating
success; null NSString becomes an empty string, causing the warning predicate to
fail closed. Native Objective-C exceptions are not newly handled, but the field
is the shell-created live NSTextField with a standard selector. The accessor is
not an external AXUIElement or screen-reader verification and is not a physical
paint/compositor measurement.

## Retained validation, without rerun

Inspected `.cache/ax-warning-fix/ax-warning-focused.trx`: 109 individual result
records, all `Passed`; counters total/executed/passed 109, zero failed/skipped.
The four new matrix cases, preedit case and existing product attach/runtime
regressions are present and passed. This is Windows managed evidence, not AppKit
execution. The producer records Release warnings-as-errors; TRX alone does not
independently prove the command-line property or cryptographically bind binaries
to current source.

Inspected both retained RID reports/metrics under
`.cache/ci-36756839425-ax/<RID>/.cache/ci-inventory/<RID>/` against
[`mac-ax-stage5-status-investigation.md`](../validation/mac-ax-stage5-status-investigation.md).
Both are `unverified`, child exit 1, stage 5 failure, empty success marker, unchanged
fixture hash and 11 metrics records. At second New return and deadline each has
detached provider, empty generation-4/version-0 binding, editable/focused input,
but `status_match=False`. This supports the fix's intended cause; it is **not**
evidence that this patch passed, and stage 6 insertion M was not executed there.

## Exact reviewed raw SHA-256

Hashes include local checkout line endings; normalization changes raw hashes.

| File | SHA-256 |
| --- | --- |
| `src/Mote.Native/NativeEditorController.cs` | `077F268F5875B550284A9B2AE7D645C53C161C9A872F27D94156697457CA52F7` |
| `src/Mote.Native/Mac/MacEditorShell.cs` | `4262332CB5F1FCDB0CB5BC2FD215DF8F74BF61D5979828F6F96C958D86EF83D9` |
| `tests/Mote.Tests/NativeControllerTests.cs` | `2F271FBC72EF201413D52F34631FF3897B3B6E2EBDE8D20DDBE4E203D25C9342` |
| `tests/Mote.Tests/NativeProductAccessibilityTests.cs` | `C32200EAF19181DB3286F4730549A3F08109FFBE459FDB12F1617E8020B18CEE` |
| `tests/Mote.Tests/NativeAccessibilityNoticeLifetimeTests.cs` | `8F48DB35B871954CDA4AA9B38EC80DA801BC5395EF906BCA4F6EF74B10324AAC` |
| Unchanged `src/Mote.Native/Mac/Canvas/MacCanvasAccessibilityProbe.cs` | `A1D73E0FA6E10215BEE3AF25E491FE18FDA4AE62B21C1819D8AB974AC8CC950F` |
| Unchanged `tests/NativeMacCanvasAxWorkflow.ps1` | `8E4572045DCC01443247C6554E0C05AFCD1007E2A378A6525E4C5B622A027BAE` |
| Unchanged `src/Mote.Native/Program.cs` | `D725F5C3D8DD650DA4920C93D550DF9E9C2AAD3D112D120B8D42AB933F6F90BA` |
| Focused TRX | `138F2CA3F8C6B546F70112CAD2B409EED47C516FB9618EE871B02B2C2660C99A` |

## Required target acceptance

Run the unchanged published Native AOT probe/wrapper on **both osx-x64 and
osx-arm64** at the repair revision. Retain each exact stdout marker
`mote-native-mac-canvas-ax-ready`, zero child exit, wrapper
`status=mac-canvas-ax-workflow-ok`, unchanged input SHA, fresh metrics, all stage
transitions through stage 6 and final success/detached-held-element checks.
Confirm stage 5's effective `status_match=True` with empty replacement binding,
and subsequent M insertion. `status_exact_suffix` is diagnostic, not an
independent acceptance obligation when multiple notices coexist.

The existing CI diagnostic step has `continue-on-error: true`; a green parent run
is insufficient. Inspect the step outcome and uploaded RID JSON/metrics. The
unchanged target probe checks experimental wording; Continuous wording currently
has managed regression evidence, not a new native Continuous fault acceptance.
External AXUIElement/VoiceOver, real IME, window clipping/readability, and Windows
adapter-local notice writers remain outside this review.
