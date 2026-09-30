# Mac Canvas AX warning lifetime repair

Date: 2026-10-01. Production base: `a604ae9`. Investigation:
[`mac-ax-stage5-status-investigation.md`](mac-ax-stage5-status-investigation.md).

## Ownership and invariant

AX attachment/runtime failure is a session capability failure, not document
analysis state. The controller latches that failure until disposal. Exactly one
controller method, `UpdateStatusNotice`, composes the full persistent notice from
AX health, theme failure, reload failure, preview failure and settings state. It
uses the existing `INativeEditorShell.SetStatusNotice` contract: ordinary document
chrome and semantic analysis updates must not replace this notice. There is no
new public API, string stripping, special New retry or analysis-policy duplication.

Both attachment and runtime fault paths refresh the composer. The old ordinary
document-status AX suffix and Continuous prefix are removed to prevent duplication.
Experimental wording remains `Accessibility provider unavailable`; Continuous
wording remains `AX unavailable: save, restart --legacy-page`. Its position moves
to the shared persistent-notice section; the warning itself, restart action and
nonmodal editing behavior are preserved. Theme/reload updates recompose from
latched facts and never erase AX health. Existing composition guards remain:
preedit does not force an input commit, and settlement publishes a deferred notice.

Mac `RenderStatus` uses one `EffectiveStatus` composition value. The existing
in-process `ProbeCanvasStatus` reads the actual status NSTextField `stringValue`
after creation (using the established Objective-C interop), with a composed-value
fallback before creation. This lets the unchanged AX stage transitions inspect
native chrome, not stale raw `_statusText`. It adds no clipboard, accessibility
permission, input-source or user-configuration operation, and logs no status text.
It remains an in-process observation, not external AXUIElement/VoiceOver proof.

## Regression coverage

The shared controller fake now matches native status lifetime: document/chrome
and `SetAnalysis` replace ordinary status; `CanvasStatus` combines ordinary status
with the persistent notice. Existing AX tests retain warning/privacy/edit-history
assertions, but replace obsolete raw-prefix/canvas-label assumptions with effective
warning and persistent-notice assertions. Analysis is allowed to replace the
ordinary canvas label, as it already does in AppKit.

`NativeAccessibilityNoticeLifetimeTests.cs` covers four combinations of
experimental/Continuous and attachment/runtime fault. It checks exactly one
visible warning at synchronous pending New, matching ready analysis, Open, retained
analysis theme replay, theme failure, failed/successful settings reload, subsequent
edit and Undo. Source, input file and content-free status contracts are checked.
Deferred/error analysis publications are exercised directly through the faithful
fake shell contract; they are not claimed as injected parser-failure integration.
A separate test covers fault during preedit and settlement without input commit.

The matrix uses synchronous fake UI pumping so appearance callbacks execute on
the controller's original UI thread. An initial async test version could resume
on a different xUnit worker and endlessly requeue thread-affine appearance; that
harness mistake was corrected, not a product threading relaxation. An initial
reference-identity assertion also failed correctly because theme replay advances
presentation sequence; final assertions preserve status and document stamp instead.

## Verification and remaining target acceptance

Focused command (Windows local, repository-local artifacts):

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release `
  -p:TreatWarningsAsErrors=true --filter FullyQualifiedName~NativeControllerTests `
  --logger 'trx;LogFileName=ax-warning-focused.trx' `
  --results-directory .cache/ax-warning-fix
```

Final focused result: **109/109 passed**, zero skipped, Release warnings-as-errors;
TRX: `.cache/ax-warning-fix/ax-warning-focused.trx`. Builds compile the Mac
adapter but cannot execute AppKit on Windows. At that local verification point,
hosted AX acceptance was still pending; no CI/Program/probe stage or fault
transition changed. The later target result below requires its own exact marker,
all stages and unchanged fixture hash—not the managed regression or a non-gating
job's overall color. Grid scheduling/navigation, Markdown/Flow/clipboard and external
screen-reader claims remain outside this repair.

## Hosted target result (2026-10-01)

At the repair revision `5a25483`, the published Native AOT macOS probes in
[CI run 36763097147](https://github.com/kleedaisuki/mote/actions/runs/36763097147)
passed **on both osx-arm64 and osx-x64**. Root independently downloaded each
`mac-canvas-ax-<RID>` artifact beneath repository
`.cache/ci-36763097147-ax-{arm,x64}/`: each wrapper reports
`status=mac-canvas-ax-workflow-ok`, exact
`mote-native-mac-canvas-ax-ready` marker, unchanged fixture SHA, present fresh
metrics and empty error. The in-process native metrics reach stage 6; stage 5
shows detached provider, present empty generation-4/version-0 binding,
editable/focused input and `status_match=True`. The formerly blocked final M
insertion and held-element teardown therefore completed in these two scoped
target runs. The steps are non-gating, so the reports and metrics—not the
parent job color—establish this result.

This remains an in-process AppKit selector/lifetime probe, not an external
AXUIElement client, VoiceOver session, real CJK IME or repeated stress result.
