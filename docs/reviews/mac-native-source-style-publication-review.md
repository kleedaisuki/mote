# Mac native source foreground publication review

Date: 2026-10-02. Scope: the diagnostic full-native AppKit source host only.
Status: source-and-test review complete; qualified portable evidence inspected. No native
execution or performance improvement is certified by this review.

## Objective and reused evidence

The proposed change removes a whole-document foreground reset and redundant
native attribute writes without changing final foreground semantics. The target
is the ordinary 524,288-byte dense JSON fixture, not a new huge-file subsystem.
The authoritative baseline is source `065585ca31db41ad0a0236cefc6a3ced33a58f49`,
CI [36890601478](https://github.com/kleedaisuki/mote/actions/runs/36890601478).
Read `docs/performance/mac-native-source-style-publication.md` and the latest
hosted section of `docs/validation/native-codec-source-workflow.md` first.
Post-edit verified publication took 4,961.0908 ms on x64 and 2,731.7920 ms on
ARM64. Those are single observations including preservation checks, not an
isolated native mutation profile or latency distribution.

## Source reasoning

Reviewed `MacNativeForegroundPublication` and the `PublishStyles`,
`ApplyForeground`, `SameColor` and `AttributeAt` host changes, plus the existing
common `NativeSourceCapabilityProbe.Publish` caller and Objective-C range bridge.

- Range validation precedes native mutation. Subtraction-first bounds reject
  negative, overflowing and out-of-document style spans. Empty styles remain
  valid and produce no native lookup or mutation.
- Coverage sorting is used only for union-complement gaps. Original style order
  remains unchanged for actual overlays; overlapping styles therefore retain
  last-style-wins semantics. Covered text does not need the old default reset.
- Effective ranges may start before the queried location and need not be
  maximal. The helper requires containment, nonempty progress and a range
  entirely within native text, then clips the forward end to the requested span.
  The checked order prevents unsigned subtraction/addition overflow.
- A missing or non-NSColor value is not treated as an equal color. Actual native
  NSColor `isEqual:` is used, not pointer identity or a managed remembered color.
  The borrowed current attribute is compared before mutation can release it.
- One key and a dictionary of desired colors are local to the synchronous
  publication. Factory colors remain in the existing owned autorelease pool;
  attributed storage retains assigned values. There is no persistent borrowed
  pointer cache or new lifetime protocol.
- `beginEditing` is balanced by `endEditing` in `finally`. Only foreground
  attributes are changed; text, other attributes, native selection and engine
  history are not rewritten. Native undo remains disabled.
- UTF-16 coordinates are preserved rather than normalized. Union endpoints
  come from the same approved style endpoints; no character edits, surrogate
  repair, CRLF conversion or offset-map substitution occurs. Native effective
  partitions and readback points may address UTF-16 units inside a scalar; that
  is an attribute lookup, not an admission of a scalar-splitting text edit.
- The bounded readback resolves expected color in reverse original overlay order
  and rejects nil or unequal actual attributes. At most 21 representative
  positions are sampled. This is explicitly not a complete native style
  certificate. Full text/selection readback and common viewport/snapshot/history
  checks remain in the original timed phase.

## Objective-C ABI evidence

Apple documents [attribute:atIndex:effectiveRange:](https://developer.apple.com/documentation/foundation/nsattributedstring/attribute%28_%3Aat%3Aeffectiverange%3A%29?language=objc)
as returning an object with a `NSUInteger` index and `NSRangePointer` output.
The private bridge returns `nint`, receives `nuint`, and passes a pointer to the
sequential `ObjC.Range(nuint Location, nuint Length)` aggregate. Both supported
macOS architectures are 64-bit: two pointer-sized unsigned fields, 16-byte range,
not a by-value range argument or structure-return call. A null output pointer is
used only for sample lookups where no range is requested.

Apple's [NSRange reference](https://developer.apple.com/documentation/foundation/nsrange-c.struct?language=objc)
identifies the range and range-pointer types; its
[location declaration](https://developer.apple.com/documentation/foundation/nsrange-c.struct/location)
is `NSUInteger`. Apple describes
[effective-range traversal](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/AttributedStrings/Tasks/AccessingAttrs.html)
as potentially returning nonmaximal ranges. The implementation does not assume
one query covers all characters with the same color.

Apple's [architecture guidance](https://developer.apple.com/documentation/apple-silicon/addressing-architectural-differences-in-your-macos-code)
specifies native `bool` on Apple silicon and signed `char` on Intel for `BOOL`.
The existing byte-return `SendNativeBool` bridge reads the one-byte result for
`isKindOfClass:` and `isEqual:`, rather than inspecting unspecified upper bits of
a pointer-sized return register. Apple's
[NSObject equality contract](https://developer.apple.com/documentation/ObjectiveC/NSObjectProtocol/isEqual%28_%3A%29)
and [runtime header](https://github.com/apple-oss-distributions/objc4/blob/main/runtime/objc.h)
provide the corresponding equality/BOOL definitions. These are source-level ABI
checks, not proof of execution on either architecture.

## Scope and remaining evidence

No local GUI process, native probe, CI dispatch, rerun, or duplicate test run was
performed. Default product profile, deadlines and fixtures are outside this
change. Performance benefit and actual Objective-C behavior require the next
hosted x64/ARM64 capability reports with the existing preservation predicates.
Portable test artifacts and frozen reviewed source hashes will be added when
provided by the implementation owner.

## Stable source and test inspection

Implementation owner confirmed the scoped API/source stable before this pass.
SHA-256 of inspected files:

| File | SHA-256 |
| --- | --- |
| `src/Mote.Native/Mac/MacNativeForegroundPublication.cs` | `177BDB8E62942054DC78CD021319DEC22C83548AF6BDA6E9A1674E9115E2530F` |
| `src/Mote.Native/Mac/MacNativeSourceCapabilityProbe.cs` | `5661D5D6401952574DAF13469716C460C271E1EC06B3ED258CEB252437FCE6B3` |
| `tests/Mote.Tests/MacNativeForegroundGapTests.cs` | `64931EF171BEDED833D7C3A4811E831920C1F3D2AD46D4BDC1F252569D3F9222` |

The randomized test uses 3,000 deterministic cases with arbitrary initial colors,
unsorted overlays, gaps, zero-width spans and overlaps. Its old-algorithm oracle
starts from default everywhere and paints the original order, independently of
the union planner. Candidate final colors and reverse-overlay expectation are
compared at every position. Directed tests cover empty/fully covered documents,
invalid spans, maximal lengths, short/nonmaximal effective ranges, missing
progress, unsigned overflow, surrogate/CRLF coordinates and bounded sampling.
These assertions test the intended foreground contract, not just a sorted-output
implementation assumption. They do not exercise AppKit mutation, native nil
returns, exception cleanup or native performance.

No substantive defect was found in this source-and-test inspection. This is not
approval of default-surface promotion or an assertion that the multi-second
publication stall is resolved. Qualified portable execution evidence is still
pending; it will be recorded without rerunning the owner's checks.

### Pre-build sampling refinement

The implementation owner replaced the temporary array of all nonempty styles in
`SampleLocations` with a count and original-order traversal. Inspected final
helper SHA-256:
`62040BB1DC3EC350E84D04BF54B44F6EF1AF514493EAD7A53C9B28D175C14122`.
For zero, one, two and larger nonempty-style counts, the selected ordinals remain
exactly first/middle/last without duplicate sample positions. This bounded
refinement does not change gap planning, overlay order, native bridge or test
scope. The earlier helper hash above denotes the inspected intermediate source,
not the final candidate.

## Qualified portable evidence and final assessment

Inspected the owner's retained `build.log`, `test.log`, TRX counters and
`qualified-hashes.json` under
`.cache/mac-native-foreground-publication-20261002/`, without rerunning them.
Release build succeeded with zero warnings/errors in 36.37 s. Focused portable
checks passed **18/18**, zero failed/skipped, reported duration 250 ms. These run
the pure range/overlay helpers, not AppKit.

Final host/helper hashes match the reviewed candidate above. Final test source
SHA-256 is `A4996510C806B20276E460E00D60B1B70F3C35E84F543D60B8000D582C01CED0`;
the test-source change after first inspection adds documentation comments for the three palette fields, not a weakened assertion. Loaded production assembly `mote.dll` SHA-256 is
`5680593A4C4EA6C0D672BC6D0FED464894C56574861870538D9F4525D94E9CBE`;
`Mote.Tests.dll` is
`EA04FD8E3D52E926D8EECF47B60074D8779C899DF1FE1923743FF15DB0265E75`.

**Final scoped assessment: no substantive defect found.** Source semantics,
range/ownership checks and independently inspected portable execution support
integration of this diagnostic implementation. Actual macOS x64/ARM64
foreground readback, state preservation and performance remain unverified for
this candidate until hosted execution. There is no claimed speedup, resolved
post-edit stall, complete style certificate or default-surface promotion.
