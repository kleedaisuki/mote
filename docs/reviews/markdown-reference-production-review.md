# Production review: bounded Markdown references

Reviewed 2026-10-01. Scope: `MarkdownReferenceBudget`, `MarkdownReferenceCertificate`,
`MarkdownReferenceSkeleton`, `MarkdownReferenceIndex`, and their integration in
`MarkdownIncrementalSession`; focused certificate tests and independent integration
test source. No production files were modified by this reviewer.

## Initial judgment

One concrete P2 document-lifetime defect was reproduced and then corrected. The remaining examined
certificate, winner-resolution, source-map, cancellation and bounded-output paths
showed no additional substantive defect at source review. **Qualified approval**
of the frozen implementation plus reviewed lifetime correction. This is not general CommonMark,
Native AOT, GUI latency or hard-heap-cap approval.

## P2: obsolete rejected snapshot remains retained after a successful exact commit

Locations: `MarkdownIncrementalSession.AnalyzeCore` sets
`_referenceRejectedSnapshot` after an unsuccessful Full; successful exact commit
and `CommitFlat` initially did not clear it. A changed-snapshot Visible fallback
also retained the obsolete memo.

Trigger: analyze a 17 Mi UTF-16-unit unsupported document with Full, then replace
its contents with `small` and analyze the real contiguous edit chain with Full.
The new analysis is Complete, but the session retains the rejected old snapshot
and hence its whole old rope. This is an independent extra retention root even
when Engine history or another caller legitimately retains the same old text.
A single-file format session should not keep obsolete source merely to avoid
repeating an admission attempt for a different immutable snapshot.

Reproduction uses already-built Release Engine/Formats DLLs, avoiding shared
production builds:

```powershell
dotnet run --project .temp/MarkdownReferenceProductionReview/Review.csproj -c Release
```

Recorded `.temp/MarkdownReferenceProductionReview/result.json`:

```json
{"initialLength":17825792,"before":"Provisional","memoBeforeMatches":true,"currentLength":5,"after":"Complete","retainedRejectedLength":17825792,"retainedRejectedIdentity":true}
```

Recommended correction: clear the refusal memo on exact/flat/reference successful
commits and when a fallback changes source identity. Only retain an intrinsic
fresh-build refusal for the exact current immutable source object. A transition
resource refusal includes inherited cache history and should not suppress a later
fresh Full rebuild of the same text; that distinction is a retry requirement,
not a claim that every resource refusal must automatically retry forever.

## Examined invariants

- Admission happens in a private stage. Cancellation and budget exceptions leave
  the previous committed index unchanged. Frozen-array allocations are followed
  by a poll before publication. Session integration projects privately before
  replacing its committed certificate.
- Complete reference projection/rendering requires the exact snapshot object,
  not an equal numeric version. Same-document serialized calls are assumed;
  this is not a concurrently callable general index.
- The closed ASCII full-reference owner grammar includes both display and target
  candidate keys, at most four distinct keys and 32 atoms. Every whole-owner
  presence mask is checked, including definition identity, source geometry,
  unexpected links, image/shortcut exclusion and owner boundaries.
- Skeleton ballast reduction preserves all punctuation, spaces, heading prefix
  and complete reference atoms. Actual owner source is reparsed for output;
  per-template original atom offsets must match the real parser. Hashes only
  narrow exact equality candidates; scratch tails are not cache keys.
- Declarations are parser-admitted and retained as source spans in physical order.
  First declarations win; losing declarations remain available for promotion.
  Winner identities distinguish unchanged declarations from edits/renames, and
  decoded URLs plus unsafe classification come from the production policy.
- One-owner transitions validate the contiguous version chain, length equation,
  original owner replacement and untouched newline boundaries; structural edits
  and unsupported owners fail closed or rebuild under Full.
- Retained accounting, observed thread allocation, deterministic work, all parser
  calls and parser source units share admission gates. Ordered output owners
  share node, token, diagnostic, displayed-value and reparse-source allowances;
  fences, independent blocks and real declarations are not separate uncapped
  output streams. Admission accounting is not measured resident memory, and a
  running opaque parser call can overshoot the observed allocation threshold.
- The new path follows existing exact/flat opportunities. Cold Visible still
  avoids a whole-file reference build. Real definition owners deliberately do
  not reproduce the synthetic whole-document definition-group wrapper; research
  documents explicitly prescribe that source-owner projection contract.

## Evidence and readiness boundary

The upstream contract was checked against
[CommonMark reference definitions](https://spec.commonmark.org/0.31.2/#link-reference-definitions)
and [pinned Markdig 1.3.2 LinkInlineParser](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/Parsers/Inlines/LinkInlineParser.cs),
as well as existing repository research/review notes. First-winner order and
whole-owner presence reasoning must not be generalized to arbitrary Markdown
syntax or pipeline extensions.

The parent reports focused tests 25/25 and warning-as-error Formats build;
independent integration execution belongs to the validator. This reviewer ran
only the new lifetime reproduction, not a duplicate focused suite. Hosted
four-RID Native AOT validation, production-scale measured retention/allocation,
and native end-to-end responsiveness remain separate acceptance gates. No
production edit, staging or commit was performed by this reviewer.


## Correction verification

The owner cleared the refusal memo on exact and flat successful commits, and
changed-snapshot Provisional outcomes now drop obsolete source identity. The
fallback tracks whether a previous reference state existed, preventing a
history-dependent transition refusal from becoming a fresh-build memo. The
private `_oracle` to `_policy` rename is semantics-neutral.

After the owner's warning-as-error Release Formats rebuild, the same independent
reproduction produced `.temp/MarkdownReferenceProductionReview/result-after.json`:

```json
{"initialLength":17825792,"before":"Provisional","memoBeforeMatches":true,"currentLength":5,"after":"Complete","retainedRejectedLength":null,"retainedRejectedIdentity":false}
```

**P2 resolved.** The exact-commit lifetime correction is directly reproduced;
flat/changed-Visible cleanup and retry distinction were checked in the source
delta. No outstanding substantive finding remains in the reviewed scope. This
verification is not a measurement of total process heap, because Engine undo
history may intentionally preserve the old document.
