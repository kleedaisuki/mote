# Native controller telemetry outcome corrections

Date: 2026-10-01. Scope: two source-established misclassifications identified in
`docs/reviews/observability-end-to-end-audit.md`. This is not a claim of complete
end-to-end Save coverage or durable crash traces.

## Contracts and implementation

- `StartSave` retains the existing Save As picker, target identity check, overwrite
  token capture, UI confirmation, worker scheduling and completion callback. When
  explicit overwrite approval is declined, the existing `cancelled` branch now
  also marks its still-live `document.save` scope `Cancelled`. The target remains
  unchanged, the buffer stays dirty, no `save.completed` event is emitted, and
  the queued callback still clears the busy state. No retry or additional I/O is
  introduced.
- The current method named `PublishIdleFullAnalysis` (called `ApplyIdleAnalysis`
  in the audit prose) starts its complete-result `analysis.to_presentation` scope
  with `Failure`. Only after token projection, native pane installation, optional
  canvas semantics installation and the publication event does it mark `Success`.
  Scope disposal therefore cannot precede the outer exception handler while
  leaving an exceptional publication classified as success. The original exception
  continues to propagate unchanged; no fallback or observer behavior is changed.
- Existing guards and non-complete/CSV branches remain outside this scope. This
  change does not assert new coverage for them.
- No public API, serialized schema, operation names, dimensions, user paths,
  content, exception messages or arbitrary tags are added.

## Deterministic evidence

`tests/Mote.Tests/NativeTelemetryOutcomeTests.cs` is in the existing nonparallel
Telemetry collection. It reuses `NativeControllerTests.FakeShell` via test-only
reflection, runs the actual private production methods, drains tracing before
parsing JSONL, and keeps all fixture files under repository `.temp/tests`.

| Case | Trace assertion | Independent behavior assertion |
| --- | --- | --- |
| Save As overwrite declined | exactly one Save span, `cancelled`; no SaveCompleted | exact original bytes, dirty buffer, one prompt, busy callback finished, no shell error |
| Save As overwrite approved | exactly one Save span, `success`; one SaveCompleted | exact new bytes, clean buffer, one prompt, busy callback finished, no shell error |
| Complete idle publication | one presentation span, `success`; one AnalysisPublished | actual projection/native install completes |
| Idle projection exception | one presentation span, `failure`; no AnalysisPublished | test removes projection after valid viewport setup; original NullReferenceException propagates |
| Idle native installation exception | one presentation span, `failure`; no AnalysisPublished | fake SetAnalysis callback throws a sentinel; exact same exception object propagates |

All cases include sentinel `SECRET` source/path/preview/exception details and
assert that no trace record contains that sentinel. The projection fault is a
controlled internal invariant break, not a claim that this fault occurred in a
hosted native session. The native callback fault proves the scope handles an
actual publication throw without masking it.

Executed locally on Windows, .NET 10, Release:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release -warnaserror --no-restore --filter FullyQualifiedName~NativeTelemetryOutcomeTests
# Passed 5/5; 0 failed; 0 skipped; test duration 302 ms.

dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release -warnaserror --no-build --no-restore --filter 'FullyQualifiedName~NativePaintTraceTests|FullyQualifiedName~Controller_untitled_save_to_existing_target_honors_overwrite_decision'
# Passed 13/13; 0 failed; 0 skipped; test duration 934 ms.
```

The first command built all referenced projects with warnings treated as errors.
The second reused those outputs for existing trace and overwrite behavior
regressions. These are deterministic controller tests, not macOS/native AOT
acceptance, physical input-to-photon measurement, or diagnostic-on performance
benchmarks. Request identity, command receipt, Save phase evidence and abnormal
termination readable-prefix guarantees remain separate infrastructure work.

## Follow-up: fail-closed idle publication under native reentrancy

Independent review of the initial outcome repair identified a pre-existing
correctness hole: the entry identity check was not repeated after native
`SetAnalysis`, which can synchronously mutate source, viewport, policy or the
installed presentation. A stale complete result could then overwrite newer
canvas semantics and emit a misleading successful publication.

The complete idle path now captures its canonical document/driver/policy,
source version, generation, page start/length, projection object, analysis serial
and exact next presentation identity. After **each** external installation
(`SetAnalysis`, then `SetCanvasSemantics`) it verifies that all these facts still
match. Supersession emits one `analysis.discarded` and closes this old
presentation scope `Cancelled`, with no `analysis.published`; a native exception
still remains `Failure` and propagates unchanged. After a SetAnalysis reentry,
old semantics are never submitted. A reentry inside SetCanvasSemantics cannot
undo the native call that is already executing, but the old callback performs
no further overwrite/publication after the replacement and never claims Success.

The existing fake gained one adjacent documented `DuringCanvasSemanticsApply`
callback seam (no production observer/API added). Six additional deterministic
Canvas cases cover: ordinary success; source edit, viewport change and
same-version frame replacement during SetAnalysis; and source edit or
same-version frame replacement during SetCanvasSemantics. Tests assert one
terminal scope, exact success/cancelled status, publication/discard counts,
privacy and preservation of the newer semantic overlay. The viewport-only case
asserts that the pre-existing overlay is not replaced by the stale full result.

Executed locally after this repair:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release -warnaserror --no-restore --filter FullyQualifiedName~NativeTelemetryOutcomeTests
# Passed 11/11; 0 failed; 0 skipped; test duration 296 ms.
```

This closes the reviewed complete-result reentrancy hole; it does not certify
all other controller publication paths or native platform callback behaviors.

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release -warnaserror --no-build --no-restore --filter 'FullyQualifiedName~NativePaintTraceTests|FullyQualifiedName~NativeControllerTests'
# Passed 164/164; 0 failed; 0 skipped; test duration 41 s.
```

The regression command reused the warning-clean Release outputs; the only
subsequent source change was an XML documentation comment on the fixture helper.
