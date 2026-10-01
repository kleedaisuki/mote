# Native Flow heading identity and color contract

Date: 2026-10-01. Scope: shared Flow-to-preview adaptation and native Windows/macOS Flow color selection. No diagnostic probe, Program route, CI configuration, clipboard, input-source, or global appearance changes.

## Diagnosis

The actual Markdown policy places block identity in `FlowParagraph.Kind` and plain heading content in `FlowRun.Role="text"`. This is intentional block/inline separation, consistent with [CommonMark 0.31.2 blocks and inlines](https://spec.commonmark.org/0.31.2/#blocks-and-inlines); heading contents may include independent code/link/emphasis inlines. Parser roles should not be changed to satisfy a native probe.

The native adapter previously copied only `run.Role` into legacy preview spans. It already recognized the enclosing heading when selecting the established source-reveal origin, but dropped heading identity and ordinary heading emphasis. Both platform Flow renderers retained heading typography but selected plain-text colors rather than the heading Accent expected by the established native heading preview contract. Thus this is a real appearance/compatibility defect, not merely a readiness assertion to remove.

## Chosen contract

`NativeFlowPresentation` resolves a completely enclosing paragraph and promotes only ordinary `text`/`paragraph` roles within a heading to presentation role `heading`. Explicit inline roles (code, link, marker, notices, image alt) remain unchanged. Both Windows RTF and macOS attributed-text adapters use that resolution for color; heading ordinary text is `Palette.Accent`. Existing inline font and link style behavior is unchanged. Cross-boundary runs inherit no partial heading identity.

Legacy spans use that resolved role and heading bold emphasis. Their established heading reveal action still targets the heading block marker (full paragraph source range), including inline runs as before. All original Flow roles, style flags, display text/ranges, exact source slices, precision, navigation flags, version and completeness remain immutable. The `Flow` object is retained rather than reconstructed. No external format/engine APIs change.

This is deliberately not a general block-style redesign: body/list/fence roles are differential controls and retain prior behavior. Platform-specific font scaling and existing non-heading color differences are outside this change.

## Verification

Command:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release -warnaserror --no-restore --filter 'FullyQualifiedName~NativeFlowHeadingIdentityTests|FullyQualifiedName~WindowsFlowRtfTests|FullyQualifiedName~NativeFallbackFlowTests|FullyQualifiedName~NativePreviewNavigationTests' --logger 'trx;LogFileName=flow-heading-final.trx' --results-directory .cache/flow-heading-validation
```

Result: **34 passed, 0 failed, 0 skipped**, Release build with warnings as errors. Evidence: `.cache/flow-heading-validation/flow-heading-final.trx`.

- Six actual Markdown policy/session-to-native cases: plain ATX, strong/emphasis, code/link, Unicode Setext, quoted heading, list-contained heading.
- Exact Flow run provenance, immutable object identity, legacy heading-origin compatibility and inline-role precedence checked for every run.
- Body, list and fenced-code differential cases retain every run role/origin/emphasis.
- Six direct shared-role precedence cases and a crossing-run rejection case.
- Real hidden Windows RichEdit installation checks Accent color and native bold for ordinary `Role=text` heading content in both default and light themes. No foreground, focus, clipboard or physical input side effects.
- Existing Windows RTF, fallback Flow and preview-navigation tests included.

macOS native color application consumes the same pure role resolver. The target
AppKit result is recorded below; it does **not** establish a real IME, physical
paint or complete theme/composition acceptance. Full headings made only of
explicit inline roles intentionally do not have a flat `Kind=heading` run:
block identity remains authoritative in `Flow.Paragraphs`, so general probes
should use typed paragraphs rather than assume all headings contain ordinary text.

## Hosted AppKit result (2026-10-01)

[CI run 36756839425](https://github.com/kleedaisuki/mote/actions/runs/36756839425)
at `7420d0b` passed all nine strict jobs. Root inspected the uploaded
`native-theme-osx-{arm64,x64}` and `native-composition-theme-osx-{arm64,x64}`
reports beneath repository `.cache/ci-36756839425-theme/`. Each report is
`passed` for both default and canvas modes, for **eight mode/RID workflows**
in all. The window-local dark→light→dark reports show exact heading RGBs,
three appearance callbacks and unchanged generation/version/selection; the
synthetic marked-text reports show deferred palette application during marked
text, commit/cancel isolation, source hashes and Undo/Redo selection. This
closes the prior stage-0 typed-Flow heading readiness failure on these hosted
targets without bypassing the original probe checks.

These probes use process-local AppKit appearance and synthetic marked text,
not a global macOS Settings change or real Chinese IME candidate workflow.
They do not establish VoiceOver, compositor presentation or latency percentiles.
