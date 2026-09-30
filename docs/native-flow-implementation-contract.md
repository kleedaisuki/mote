# Native Flow implementation contract

Status: first Flow subgraph implemented and independently reviewed; target CI pending, 2026-09-30. This is the shared interface note
for the first coherent Flow subgraph of `native-rendering-architecture.md`, not
a declaration that Grid, resources or product acceptance are complete.

## Ownership

- Native lead: `NativeShell.cs`, `NativeEditorController.cs`,
  `NativeFormatSessionDriver.cs`, `NativeIdleFullAnalysis.cs`,
  `NativePreviewBuilder.cs`, controller/driver integration tests.
- Format worker: new `RenderContracts.cs`, `MarkdownRenderProjection.cs`,
  `MarkdownIncrementalSession.cs`, additive Markdown policy helpers if needed;
  precise-source fix `920ced9` must remain intact.
- Windows worker: Windows shell/interop, generated RTF and focused tests.
- macOS worker: Mac shell/interop, attributed Flow and target probes.
- Configuration worker: configuration loader/model, new presentation capability,
  focused config tests and configuration documentation.

No worker stages, commits or changes another area's interface without coordination.

## Additive cross-assembly interfaces

Formats owns public additive immutable `FlowRenderProjection` with properties
`Version`, `Text`, `Runs`, `Paragraphs`, `Truncated`, `Completeness`.
`FlowRun` carries `DisplayRange`, `SourceRange`, `Role`, `Style`, `Precision`,
`Navigable`; `FlowInlineStyle` is flags `Strong`, `Emphasis`, `Code`, `Link`.
`RenderOriginPrecision` is `Item` or `ExactText`. `FlowParagraph` carries
`DisplayRange`, `SourceRange`, `Kind`, `Level`, `Depth`, `Marker`.
`IRenderFormatSession.Render(snapshot, analysis, request, cancellationToken)` is
optional and runs immediately after Analyze on the same serialized driver lane.
Rendering does not commit analysis state or retain snapshots in its output.

Initial display limits are 16 Ki UTF-16 units, 120 paragraphs and 4096 runs.
These are independent of semantic completeness. Projection owns defensive list
copies, rejects overlapping/out-of-buffer runs, and does not slice surrogate
pairs. Every absolute source origin belongs to the returned version.

Native owns `NativePresentationId(NativeDocumentStamp Document, long Sequence)`.
`NativeAnalysisView` retains legacy fields and appends `PresentationSequence`,
nullable `Flow`, and `ShowPreview` (default true). `Identity` combines its stamp
and sequence. `NativePreviewActivation` appends the retained sequence; default
zero preserves source compatibility for existing direct test adapters. Production
controller publications always receive a new positive sequence. A callback for
an earlier map at the same source version is rejected.

Shells install text/styles/maps together and retain identity only after successful
native text readback. Source-stamp and composition checks remain. Same-text
restyling preserves native preview selection/scroll; normal native Copy remains.
Only explicit activation navigates source; no implicit external-link launch.

## Layout convention

`PreviewLayoutPreference` is `Auto`, `SourceOnly`, `Split`; TOML `[editor]`
`preview` values are `auto`, `source`, `split`. Default is auto. Centralized
`DocumentPresentation.ForPolicy` supplies `SourceOnly` for plain text and
`SourceAndPreview` for structured formats. The ordinary Continuous product applies
this default. Legacy Page auto retains its established split; explicit preference
overrides either profile. Platform layout uses `ShowPreview`, not format checks.

## Admission and acceptance

Exact Markdown session paths may retain private parsed Markdig state to preserve
ordered inline structure and resolved references without whole-file reparsing on
scroll. Historical source arenas must be bounded; memory consequences require
tests and documentation. Large certified flat blocks use bounded policy-owned
parsing. Provisional context and display omissions remain visible and non-navigable.

Host builds and pure tests are not target-native acceptance. Windows actual
RichEdit readback/selection/style and macOS AppKit target tests remain separate
evidence; root wires non-gating target CI. Flow delivery does not satisfy CSV Grid,
safe local image loading, complete reader acceptance or full product completion.

## Integrated host evidence and corrected failures

Release `warnaserror` Native and test-project builds completed with zero warnings
and errors. The controller/driver/preview/idle affected suite passed 24/24 after
the initial-pending layout change, including synchronous source resize reentry,
same-version sequence rejection, original heading-marker navigation with exact
literal provenance retained, and surrogate-safe CSV/JSON fallback admission.
A subsequently added short nine-column CSV regression passed 1/1, proving that
render omission does not change `Complete` semantics and does set `Truncated`.
An additional actual same-version controller viewport refresh regression passed
1/1: source stamp is unchanged, the new sequence invalidates the old activation,
and the latest heading map still reveals the source marker.

The Windows production-install hidden HWND suite passed 9/9 (Windows 11
10.0.26200 x64, .NET SDK 10.0.400, managed Release). Actual control evidence
includes Chinese/emoji readback, heading font attributes, selected Copy payload,
same-text selection/pixel-scroll preservation, identity invalidation, literal
RTF-looking fallback text, and synchronous nested visibility publication.
It does not prove actual clipboard transfer, ordinary physical gestures, AOT
or Windows ARM64 behavior. No foreground/clipboard mutation was used by these
hidden tests.

Independent reviewers found and closed three actual implementation failures:
fallback truncation could split valid surrogate pairs; plain preview import
could enter RichEdit's RTF auto-detection; and AppKit reverse Flow-to-legacy
restyle could retain prior underline/paragraph attributes. Additional omission
state and reentrant-install/composition guards were corrected. Reviews live in
`docs/reviews/native-flow-integration-review.md` and
`docs/reviews/native-flow-platform-review.md`. The macOS synthetic target probe
has been admitted for disposable hosted non-gating CI, not executed on this
Windows host; require exit zero plus its exact success marker.

Format independent review is recorded in `docs/reviews/flow-format-review.md`.
It discovered empty/nested-only list items losing their outer marker; marker
ownership now belongs to the item rather than child zero, and independent probes
confirm ordered, unordered and quote-wrapped nested cases. A shared 8192 syntax
visit budget also counts hidden definition groups and marker-owner search, so
invisible syntax cannot bypass display caps. Focused additions passed; see the
format evidence document for exact runs rather than summing overlapping suites.
All three independent reviews have closed their concrete findings in current
source. Exact target results remain to be appended rather than inferred from
these host checks. Existing CSV Flow is explicitly a
compatibility surface pending real virtual Grid, not the final table renderer.
