# Mac full-native source foreground publication

Date: 2026-10-02. Status: scoped diagnostic implementation, portable checks and
independent source/ABI review complete; actual hosted native qualification required.
No native timing improvement is claimed and the default product is unchanged.

## Representative workload and authoritative baseline

The relevant workload is ordinary dense structured text, not a new huge-file
specialization: the fixed 524,288-byte JSON fixture, 484,573 initial UTF-16 units,
79,433 semantic tokens and zero diagnostics, followed by one controlled native
insertion, engine Undo, engine Redo, exact Save and fresh Document reopening.
This diagnostic surface is not the default product surface or real IME ingress.

Frozen source: `065585ca31db41ad0a0236cefc6a3ced33a58f49`.
Hosted run: [CI 36890601478](https://github.com/kleedaisuki/mote/actions/runs/36890601478).
Retained evidence root: `.cache/ci-36890601478-codec-source/artifacts/`.

For each RID, evidence is
`native-codec-source-evidence-<rid>/source-capability/probe/report.jsonl`.

| RID | Report SHA-256 |
| --- | --- |
| osx-x64 | `3EA7BB3E8EEE0CE8323DF74A0D2C9CE713B4FAD716D6756314D1D17A5283963F` |
| osx-arm64 | `D892D7B438C97FA5FEDF629E997DEA8B41FCB920C8681488748A534834345AA2` |

Supervisors observe exit 0 and identical before/after binary identities:
`F18627A93F946E9B38446C67BA8939F514AEE420B0BBB358352B3C18B60ADB00`
(osx-x64) and
`0A15660AC0C4DDFECE9AF19EF7DF49CCB05839C3151AF6E6307601A3D4C24CF3`
(osx-arm64). The source adapter's report identities agree with those retained
executables. This is actual hosted baseline evidence, not a local managed test.

Both actual native hosts report `textkit2`. All three fixture journeys complete;
the dense JSON journey is nevertheless not a fluent editing result.

| Verified foreground publication | osx-x64 ms | osx-arm64 ms |
| --- | ---: | ---: |
| Initial | 432.8346 | 96.3230 |
| After controlled insertion | 4,961.0908 | 2,731.7920 |
| After engine Undo and reimport | 529.7896 | 88.9028 |
| After engine Redo and reimport | 871.3132 | 98.1715 |

These are one observation per architecture, not distributions, controlled
architecture comparisons, percentiles or before/after evidence. The common
publication phase includes attributes and text/selection/viewport/history
preservation checks; it does not isolate attribute mutation, native layout or
readback. Post-edit policy analysis takes 56.9804 ms (x64) and 39.9391 ms
(ARM64), far less than the publication phase, but these observations do
not identify one internal TextKit routine as the cause.

## Source-level mechanism and competing explanations

The frozen Mac host already balances `beginEditing` / `endEditing`. It resets
the entire text-storage foreground and then calls `addAttribute:value:range:`
once per semantic span. Every call creates an NSString key and an NSColor. It
preserves characters and other attributes; it does not replace RTF or text.

The whole reset destroys the existing foreground partition before rebuilding
it, even where native editing already preserved the correct colors. Repeated
object construction is another avoidable source-level cost. Neither proves the
cause of the observed post-edit asymmetry. Native run maintenance, attribute
fixing/layout notifications, and readback are competing explanations; a CPU
profile is not available in this baseline.

Apple documents batching as consolidation of storage changes and observer
notifications, not a guarantee that repeated mutations are cheap:
[beginEditing](https://developer.apple.com/documentation/foundation/nsmutableattributedstring/beginediting%28%29),
[changing attributed strings](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/AttributedStrings/Tasks/ChangingAttrStrings.html).
The production-grade platform mechanism used here is native attributed storage,
not a second text/layout engine. Apple documents that ordinary
[attribute:atIndex:effectiveRange:](https://developer.apple.com/documentation/foundation/nsattributedstring/attribute%28_%3Aat%3Aeffectiverange%3A%29?language=objc)
returns a valid, not necessarily maximal, range. A correct traversal must accept
short runs, missing attributes and clipping, and must reject non-progressing or
out-of-bounds native results.

The academic alternative is not automatically the relevant optimization:
Palma et al., [IEEE TSE 2025, On-the-Fly Syntax Highlighting: Generalisation and
Speed-Ups](https://doi.org/10.1109/TSE.2024.3506040), investigates learned
highlighting resolution across six programming languages, including CNN/GPU
prediction. Its resolver accuracy/throughput experiments do not measure this
AppKit publication path. Here exact semantic tokens already exist and the
observed bottleneck is later publication. Replacing semantics with a learned
approximation would neither address the measured stage nor meet mote's exact
analysis contract; there is no model/GPU adoption in this change.

## Implemented semantics-equivalent change

1. Validate all ranges before mutation, including unsorted/overlapping input.
2. Compute the union of nonempty style spans. Apply default foreground only to
   uncovered gaps, including prefix, suffix and a style-free entire document.
3. Apply original styles in original order, preserving last-style-wins overlap
   semantics. No token or unstyled gap disappears.
4. For each requested range, traverse current native foreground effective runs;
   mutate only clipped portions whose actual native color is not equal to the
   desired color. Do not infer current attributes from a managed style cache.
5. Reuse one key and distinct NSColor values only within one publication and the
   existing owned autorelease pool. No persistent cache or lifetime protocol.
6. Keep balanced editing, foreground-only changes and all original correctness
   checks. Add bounded actual native foreground readback after publication.

Why this is equivalent: the old algorithm first paints default everywhere and
then overlays every span in order. The union's complement has no overlay and
must end at default. Every point in the union receives at least one original
overlay, with its final color determined by the last one. Default reset inside
that union is therefore unnecessary. Skipping a mutation only after native
value equality does not change its final value.

The implementation must not change text, other attributes, input/history
ownership, deadlines, fixtures, style projection, profiles or default product
selection. Portable model tests can prove gap/overlap coverage and traversal
contracts, but not native performance or Objective-C runtime behavior.

Implementation ownership is limited to
`src/Mote.Native/Mac/MacNativeSourceCapabilityProbe.cs`, new pure Mac-only
`MacNativeForegroundPublication.cs`, and
`tests/Mote.Tests/MacNativeForegroundGapTests.cs`. There is no common API,
controller, policy, default launch, workflow or supervisor change.

The actual-readback locations include first/middle/last entries from styled and
unstyled ranges plus document boundaries, at most 21 distinct indices. Expected
values resolve original input order, including overlaps. Readback queries real
native storage after `endEditing`, rejects missing/non-NSColor values, and uses
native value equality rather than pointer equality. This is a bounded color
witness, not a claim to have read back every foreground character or rendered
pixel. All semantic spans and all unstyled gaps are still published.

The Objective-C object-attribute bridge returns `id`, passes `NSUInteger` and
an optional `NSRangePointer`. Native effective bounds are checked by subtraction
before addition, and traversal requires positive progress even for missing
attributes. Both `isKindOfClass:` and `isEqual:` use the existing one-byte BOOL
return bridge, not a pointer-sized result. Borrowed attribute values are compared
before any mutation can release or replace them.

## Qualified portable checks

Windows development host, .NET SDK `10.0.400`, Release configuration. Root
coordinated the shared build slot. Exactly one build and one focused test run:

```powershell
dotnet build tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-build --no-restore `
  --filter FullyQualifiedName~MacNativeForegroundGapTests `
  --logger 'trx;LogFileName=mac-native-foreground.trx' `
  --results-directory .cache/mac-native-foreground-publication-20261002
```

Build exit 0, **0 warnings / 0 errors**, 36.37 s. Focused tests **18/18**,
0 failed / 0 skipped, 250 ms. Retained raw logs, TRX and hashes:
`.cache/mac-native-foreground-publication-20261002/`.

The tests cover union complement, unsorted/nested/crossing/touching/zero-width
overlays, invalid range refusal, maximum signed and unsigned arithmetic,
nonmaximal native effective ranges, strict traversal progress, unchanged
UTF-16/CRLF coordinates, and bounded sampling. A deterministic 3,000-case model
starts with arbitrary old colors, applies union gaps and all original overlays,
and compares every position against the old full-default-reset algorithm.
The model also checks expected readback colors at every position. It does not
invoke AppKit, a native control, an input event or the diagnostic entry route.

| Qualified file | SHA-256 |
| --- | --- |
| Mac host source | `5661D5D6401952574DAF13469716C460C271E1EC06B3ED258CEB252437FCE6B3` |
| Pure helper source | `62040BB1DC3EC350E84D04BF54B44F6EF1AF514493EAD7A53C9B28D175C14122` |
| Focused test source | `A4996510C806B20276E460E00D60B1B70F3C35E84F543D60B8000D582C01CED0` |
| Loaded `mote.dll` | `5680593A4C4EA6C0D672BC6D0FED464894C56574861870538D9F4525D94E9CBE` |
| Loaded `Mote.Tests.dll` | `EA04FD8E3D52E926D8EECF47B60074D8779C899DF1FE1923743FF15DB0265E75` |

These hashes qualify the portable build, not a Mac Native AOT executable.
Independent review:
[`mac-native-source-style-publication-review.md`](../reviews/mac-native-source-style-publication-review.md),
no substantive scoped defect found. The reviewer inspected retained results
without duplicating the test run and did not certify a speedup or native execution.

## Next discriminating evidence

After source/ABI review and portable checks, use the existing hosted capability
journey with unchanged fixtures and phase boundaries. Compare initial and
post-edit publication, Undo/Redo, exact source bytes, selection/viewport/history
and bounded native foreground readback. Report allocations as the existing
process-wide approximation, not native peak memory. No local GUI, CI dispatch,
replay, speedup claim or default-surface promotion occurs in this investigation.
