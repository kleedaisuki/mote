# Native Flow heading identity review

Date: 2026-10-01. Independent scoped review of the frozen heading adapter/color change. **No substantive defect found in this change.** Suitable for integration; successful hosted AppKit execution remains a separate acceptance obligation.

## Scope and evidence

Inspected the production diff and surrounding `RenderContracts`, Markdown projection, fallback projection, preview navigation, theme role mapping, Windows installation, Mac attributed-text implementation, existing RTF/fallback tests, new heading tests, and `docs/validation/native-flow-heading-identity.md`. The initiating diagnostic evidence is `docs/validation/ci-36752189587-mac-nongating-audit.md`.

Inspected existing `.cache/flow-heading-validation/flow-heading-final.trx`: 34 executed, 34 passed, zero failures. These are the owner's executed results, not a newly repeated reviewer run. No tests or native probes were rerun, and no production/test/workflow files were edited.

Reviewed raw-file SHA256 identities:

| File | SHA256 |
| --- | --- |
| `src/Mote.Native/NativePreviewBuilder.cs` | `C7B2D63176DE2A9BD83BA4192DCFFBC0FE4E27B0F30A636D89846C85F9E5CBBC` |
| `src/Mote.Native/NativeFlowPresentation.cs` | `5A05C50387776A40A6F6382ABC795E4657C2946D1AC12854CC58675B71C88C7C` |
| `src/Mote.Native/Windows/WindowsFlowRtf.cs` | `275A5522BFDA4D7A95CF614EFAAB7D9D0CCEFE94C1F9E3663E6E52A0A374D1D9` |
| `src/Mote.Native/Mac/MacFlowAttributes.cs` | `16FB2D2914354ED09B2E491CF43AFA3755BFBD7D10FAA84AB3483B4218CDA065` |
| `tests/Mote.Tests/NativeFlowHeadingIdentityTests.cs` | `A1D1463C2DE9713C93335FA75AA991B10870D804063DB938575E54C6E061FDDF` |

## Contract assessment

- **Ownership and containment:** the resolver relies on ordered, nonoverlapping paragraph maps, explicitly guaranteed and copied by `FlowRenderProjection`. It requires full run containment and stops once the next paragraph starts after the run. Replacing the former heading-only search does not change ownership of valid contained heading runs, since competing overlapping paragraph owners are disallowed.
- **Source identity:** ordinary and explicit-inline heading spans still reveal the heading paragraph's existing source origin; navigation flags are unchanged. Exact literal origins, precision, version, completeness, inline flags and source/display ranges remain in the original immutable Flow. Retaining the same Flow instance avoids accidentally replacing precise provenance with the compatibility reveal target.
- **Role precedence:** only `text` and `paragraph` roles under a heading become presentation `heading`. Code, link, marker, notice and image-alt roles retain their own color contracts. Paragraph bold inheritance changes legacy span emphasis as intended, without changing Flow inline flags. Inline font/link logic in both native renderers is untouched.
- **Color contract:** both platform adapters explicitly map resolved `heading` to `Palette.Accent`. This addresses the reported plain-heading failure instead of weakening the readiness/color assertions. Other role switch branches are unchanged. Existing platform differences for code, links, quotes and generic foreground remain outside this change and are not newly introduced defects.
- **Other formats and blocks:** non-heading paragraphs return their unchanged run role/origin/emphasis. JSON/TOML fallback does not create heading paragraphs in its tree path. Lists and fences preserve explicit roles, except the intended heading inside a list still receives heading block styling and preserves its marker role. Neither parser nor engine public contracts change.
- **AOT and resource behavior:** added code uses direct managed calls and existing record structs, with no reflection, runtime discovery, dynamic code, or added native dependencies. Reflection remains test-only. Paragraph lookup is linear per run, bounded by 120 paragraphs and 4,096 runs; this is not evidence of a measured performance regression, and does not justify introducing an index for this scoped fix. Actual RID publish/trim validation is still CI work.

## Test strength and limits

The policy/session cases exercise real Markdown parsing rather than merely synthesizing the desired flat heading role. The Windows hidden RichEdit test independently queries native character color and bold effects, so a color-table entry alone cannot certify success. The source-slice assertions and object-identity check provide useful safeguards against changing canonical origins while adjusting presentation.

The RTF color-table assertion in the six Markdown cases is weaker than a per-inline native color check: existence of Accent does not prove each run used it. The independent hidden-window test covers ordinary heading text; inline precedence is additionally supported by the shared resolver cases and unchanged renderer switches. This is a coverage limitation, not an observed defect or integration blocker.

Hosted macOS theme/composition probes must finish all phases and native color checks on both RIDs; reaching stage 0 is insufficient. This review does not establish AppKit runtime behavior, marked-text safety, physical presentation, real IME input, or full theme acceptance. A heading composed entirely of explicit code/link/image roles need not contain a legacy `Kind=heading` span: typed paragraph identity remains authoritative, as the implementation note correctly states.

Cross-boundary runs receive no promoted heading role. Existing platform typography handling of malformed/nonparagraph-aligned cross-boundary content was not redesigned by this color fix; the added crossing test establishes resolver/legacy role behavior, not a universal native typography rejection contract.

## Decision

No owner correction requested. Integrate the frozen scoped change and retain the explicit hosted macOS acceptance caveat. Do not summarize the parent CI job's green color as proof that non-gating theme/composition diagnostic assertions passed.
