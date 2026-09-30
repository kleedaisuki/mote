# Mac non-gating diagnostic audit: CI 36752189587

Date: 2026-10-01. Scope: completed hosted macOS x64/ARM64 diagnostics, excluding the separately investigated new draw-trace probe. No implementation, test, CI, staging, or commit changes were made. Evidence was downloaded only beneath repository `.cache/ci-36752189587/` and `.cache/ci-36749281937/`.

## Conclusion

The three investigated acceptance failures are **pre-existing failures**, not newly established regressions introduced between the comparison runs. They must not be described as passing because their containing job/step is green. Theme/composition have a concrete source-level readiness-contract mismatch with the typed Flow representation; AX fault recovery remains unresolved, with stage variation on ARM64. Neither observation justifies labeling all failures harmless environment instability.

- Current run: [36752189587](https://github.com/kleedaisuki/mote/actions/runs/36752189587), started 2026-09-30 17:33:06 UTC, revision `8671c062cb9dc1c790220a1d3028acad737f7bd6`.
- Baseline: [36749281937](https://github.com/kleedaisuki/mote/actions/runs/36749281937), started 2026-09-30 17:08:37 UTC, revision `2498285d0b5d2e25edabba82fdabbf8965c91143`.
- Current Mac jobs: x64 `110013331236`, ARM64 `110013331949`; baseline x64 `110003417127`, ARM64 `110003417112`.

## Actual outcomes (process logs and uploaded reports)

| Exact workflow step | Current x64 | Current ARM64 | Baseline x64 | Baseline ARM64 |
| --- | --- | --- | --- | --- |
| Diagnose in-process macOS canvas AX selector (non-gating) | process exit 1, stage 5 | process exit 1, stage 3 | process exit 1, stage 5 | process exit 1, stage 5 |
| Diagnose macOS window-local live theme (non-gating) | both default/canvas fail stage 0 | both default/canvas fail stage 0 | both default/canvas fail stage 0 | both default/canvas fail stage 0 |
| Diagnose macOS marked-text theme transition (non-gating) | both default/canvas fail stage 0 | both default/canvas fail stage 0 | both default/canvas fail stage 0 | both default/canvas fail stage 0 |

The Jobs API reports these `continue-on-error` steps as `success`; raw logs contain `##[error]Process completed with exit code 1.` and explicit failed/unverified reports. Acceptance must use markers/report assertions, not job color.

Current executable SHA256: x64 `3E0E283B76400E3FC0D27E815031ED088533C88F9B252E5DB4F54DDAEFEB8409`; ARM64 `38C4218424E98B7D300BFCDE27575501DDF381B8936FA35A42D19D82349C3A09`.

### Theme / synthetic composition: failure before transitions

Current theme stderr for all four cases: `Mac theme readiness: stamp=True;analysis=True;analysis-ready=False;heading=True;body=True;` followed by `Mac theme stage 0 check deadline failed.` Native JSON has `LastStage=0`, zero callbacks, empty States. Baseline artifacts contain the same stderr and report state.

Current ARM64 composition stderr (both modes) and baseline x64 composition stderr: stage 0, no marked text, no appearance/settled callbacks, `analysis-ready=False`; deadline failure. Reports have empty Phases and `InputUnchanged=true`. These runs did **not** exercise appearance switching or marked-text commit/cancel; they cannot falsify or establish those downstream behaviors.

Source evidence (also present at baseline revision):

1. `MacCanvasThemeProbe.AnalysisReady` and `MacCompositionThemeProbe.AnalysisReady` require `PreviewSpans.Any(span.Kind == "heading")`.
2. `MarkdownRenderProjection.Add` stores block heading identity in `FlowParagraph.Kind="heading"`, while a plain heading's inline run starts with role `"text"`.
3. `NativePreviewBuilder.FromFlow` assigns legacy `NativePreviewSpan.Kind = run.Role`; it adjusts heading reveal origin but does not convert the role to heading.
4. Both probes also later locate a legacy heading span for native color reads. Merely removing the readiness check would leave another stale assumption.

**Inference:** the old probes' flat-span heading assumption is incompatible with the ordinary typed Flow heading representation. This is a specific, testable harness-contract explanation, not evidence that the actual native heading typography/color or composition is correct. `MacFlowAttributes` obtains heading level/font from FlowParagraph, but color from run.Role; the existing probe expects Accent. The intended heading color contract must be decided explicitly before adapting the color assertion, rather than weakening it to get green.

### AX fault recovery: unresolved, not data-loss evidence

Current x64 uploaded metrics show stages 0→1→2→3→4→5 in 996 ms, then failure around 89,991 ms. The retained source proxy passes initial selector checks. AX fault request initially returns with provider still attached; later stage 3 exits, insertion N is observed in stage 4, and New is invoked. Stage 5 waits for *both* empty canonical snapshot and the unavailable-status substring before insertion M; the report does not identify which condition remained false.

Current ARM64 fails stage 3, which requires provider detached plus matching status before checking editable/focused input. Its diagnostic artifact was absent from the available artifact listing; the log alone does not expose the last provider/status/source state. Prior ARM64 failed stage 5, so stage variation exists, but two runs cannot establish environmental flakiness or exclude a state-ordering defect.

`NativeMacCanvasAxWorkflow.ps1` initializes `input_sha256_unchanged=false` and `metrics_lines=0` and calculates them only *after* a successful process/marker. Thus failed wrapper JSON values are **unmeasured defaults**, not observed input mutation or proof that native metrics do not exist. The x64 uploaded native metrics in this run directly illustrate that distinction.

## Changes between the runs

`git diff 2498285 8671c06 --name-only` lists new draw probe/Program route, Windows clipboard test/CI, and documentation. It does not change the legacy theme/composition/AX probes, native shell/controller, or Markdown renderer. This supports pre-existence, not a claim that initialization/new-route effects are impossible.

## Decisive next experiments

1. **Theme/composition:** add a pure managed contract test constructing an ordinary Markdown heading through the actual format Flow→NativePreview path; record heading paragraphs and run/legacy-span roles. Update probe identity/range selection to typed heading paragraphs, preserving source/version/selection and native color assertions. Resolve whether the expected heading Accent color is still intended, then run only the affected published-Mach-O probes on both RIDs. Successful stage 0 alone is not acceptance; require complete callbacks/states and marked-text phases.
2. **AX:** add bounded privacy-safe state evidence at the stage-5 deadline (snapshot presence/length, provider attached, unavailable-status token, generation/version equality, editable/focused flags) and ensure stage-3 fault metrics are uploaded on either RID. Repeat only this failing probe against the same executable on a fresh hosted runner. This distinguishes missing status from failed New/canonical state and detach from status propagation without changing recovery semantics or weakening assertions.

## Reproduction / retained evidence

`gh run view <run> --job <job> --log` captures full logs at `.cache/ci-<run>/osx-{x64,arm64}.log`. Jobs API output is `.cache/ci-<run>-jobs.json`; revision is `.cache/ci-<run>/revision.json`.

Current downloaded artifacts: `native-theme-osx-x64`, `native-theme-osx-arm64`, `native-composition-theme-osx-arm64`, `mac-canvas-ax-osx-x64`. Baseline: both native-theme artifacts and `native-composition-theme-osx-x64`. All are under `.cache/ci-<run>/artifacts/`. The missing opposite composition/AX artifacts were not fabricated; both-RID failure status is supported by full process logs, while detailed report claims above are restricted to inspected artifacts.
