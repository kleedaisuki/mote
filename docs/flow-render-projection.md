# Bounded format-owned Flow rendering

Status: implemented additive Formats capability, 2026-09-30. Native layout, accessibility,
image resources, explicit external-link actions and release acceptance remain separate.
See [native rendering architecture](native-rendering-architecture.md).

## Contract and ownership

`IRenderFormatSession` extends the established analysis session without changing existing
`IFormatSession.Analyze`, semantic nodes or HTML APIs. The serialized driver calls Analyze
then Render on the same snapshot/version; rendering neither commits semantic cache state nor
changes source, Undo, diagnostics or dirty state. Wrong versions, disposed sessions and
cancellation are rejected. A render result contains only copied display text and value metadata:
no snapshot, Markdig objects, native handles or canonical whole-file source string.

`FlowRenderProjection` copies run/paragraph arrays and validates ordered nonoverlapping display
ranges, nonnegative source coordinates without overflow, bounded role/kind/marker metadata,
well-formed UTF-16 and surrogate-safe display boundaries. Initial caps include notices:

| Resource | Cap |
| --- | ---: |
| Display UTF-16 units | 16,384 |
| Paragraphs | 120 |
| Runs | 4,096 |
| Nested block/inline depth | 64 |
| Shared syntax visits, including hidden nodes and marker-owner inspection | 8,192 |

Display truncation and `AnalysisCompleteness` are independent. A fully checked document can
have a truncated preview. A large provisional document stays provisional even when its
visible paragraph looks valid. Covered-region status is distinct from provisional status.
Synthetic markers/notices have `Navigable=false`; transformed entity/code/image display has
item origins, not guessed affine coordinates. `ExactText` is emitted only after bounded
source/text equality verification. Native themes interpret roles and style flags; Formats
does not select fonts or colors or open links.

## Markdown mechanism

For the existing exact-parse path, the session now retains policy-private parsed Markdig
blocks. The existing `DisableHtml().UsePreciseSourceLocation()` CommonMark pipeline is unchanged.
Flow walks authoritative ordered inlines directly, preserving distant reference resolution,
literal order, nested strong/emphasis flags, code, entities, links, image alt placeholders,
headings, list starting ordinals, quotes, code lines and hard/soft line breaks. List bullets
and ordinals are visible non-navigable synthetic runs as well as paragraph metadata.
Marker-only CommonMark items retain that marker and a source-backed empty-body paragraph;
an empty `ListItemBlock` is not an absent item, and subsequent ordinals do not shift.
Marker ownership is structural: a first descendant leaf may consume its item's marker,
but a nested list owns new markers. When the first visible descendant is a nested list
(also through a quote container), the owning item emits a marker-only paragraph first.
Reference definition groups have no visible paragraph. Images are alt placeholders, not
decoded resources. Unknown admitted nodes produce explicit unsupported notices; pipe-table
syntax is ordinary literal CommonMark text, not falsely advertised as a supported extension.

Markdig can retain unresolved bracket/emphasis delimiters as container objects with their
own literal delimiter outside child nodes. A simple container-only walk lost the middle
`[` in `[a][b][c][d]`; the projection explicitly renders these delimiters before children.
The repository-local `.temp/flow-parser-probe` reproduced the private AST shape and confirmed
Markdig HTML correctly kept all brackets. This is a renderer bug fixed here, not an upstream
parser defect or permission to weaken the ambiguity test.

Each lazy block run carries source displacement. Rendering seeks the first intersecting
block by binary search and stops beyond requested interest; it does not linearly walk every
exact block on each scroll. Rendering never reparses a whole document. Certified large-file
flat blocks are independently reparsed at the policy boundary only up to 64 Ki UTF-16 units.
Larger blocks use bounded source excerpts plus omission notices. Provisional local block
rendering remains explicitly provisional: distant definitions are not guessed or certified.

## Private syntax memory consequence

Retained Markdig `StringSlice` objects can retain their original exact-parser source arena.
The existing exact-parser admission limits remain: ordinary documents at most 4 Mi UTF-16
units and sparse documents at most 16 Mi units, subject to line/marker limits. This change
adds AST retention within those existing limits; it is not a zero-cost allocation claim.

Independent local fast-path reparses are limited to 64 Ki units per replacement. At most
16 such reused edits are admitted before a normal exact rebuild releases old syntax arenas.
Thus local replacement source arenas cannot grow without bound: at most about 1 Mi UTF-16
units in addition to one bounded full-parser arena, excluding object/AST overhead. Cancellation
cannot advance this reuse counter. Rebuilding trades occasional bounded parsing for a clear
retention limit; RSS and cross-platform latency still need measurement. The public projection
never retains these arenas. Large certified sessions retain no whole-file AST.

## Verification and remaining scope

Release `-warnaserror`, isolated `.temp/flow-render-validation` output: **62/62 passed**,
comprising **14 rendering cases plus 48 existing incremental-policy cases**. Coverage includes
literal order/precise origins, nested flags, reference resolution after scroll, unresolved
delimiter containers, list ordinals and marker navigation, quotes/code/line breaks, giant-line
provisional excerpts, distant reference rebinding ambiguity, surrogate-safe clipping, paragraph
and run limits including notices, immutable copies, overflow/boundary rejection, canceled and
stale rendering, and twenty local edits across forced arena rebuilds. The reuse-counter test
checks admission invariants, not measured process residency or garbage-collection timings.

Independent review additionally identified dropped empty list containers (`- ` and
`1. \n2. two`). The renderer now emits their marker and paragraph even with no child block.
Three added cases verify unordered/ordered/non-one-start markers, source origins and
non-navigable synthetic runs. A focused Release `-warnaserror` run of these three cases
plus the adjacent list/quote/code case passed **4/4**, without repeating the completed
62-case run. Follow-up review caught the same ownership issue for nested-only items:
`1. - child`, `- - child`, and `1. > - child`. Structural first-presentation selection now
preserves the outer marker before the nested list. Three added cases plus the previous
empty-item and adjacent list cases passed **7/7** in a focused Release `-warnaserror` run.
The rendering test class now contains 21 cases in total. A direct AST-budget test constructs
hidden reference-definition groups (which produce no display text), proves the 8,192-visit
cap is reached, and checks a non-navigable truncation notice without downgrading Complete
semantic evidence. This hidden-node case and six empty/nested marker cases passed **7/7**
in a focused Release `-warnaserror` run. Marker-owner inspection shares the global visit
budget as well as its local depth/inspection cap, so hidden descendants cannot multiply
otherwise independent traversal budgets.

Commands:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release `
  --artifacts-path .temp/flow-render-validation --no-restore `
  --filter 'FullyQualifiedName~RenderProjectionTests|FullyQualifiedName~IncrementalPolicyTests' `
  -warnaserror --logger 'console;verbosity=minimal'
```

No strict-one-binary target publication, Windows/macOS native rendering gesture, screen-reader
or rich image/table acceptance is claimed by these unit tests. Actual native layout and source
navigation are independently verified by the owning integration workstream.
