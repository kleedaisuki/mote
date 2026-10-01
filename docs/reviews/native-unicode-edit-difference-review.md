# Native Unicode edit difference: independent source review

Date: 2026-10-01. Projection implementation pinned at
`6db3f1994ba24e1a7509355954858143e0b6389f`; dedicated test/doc EOF cleanup at
`5f0dcdcf8debb37c6fa729e04f466df9415fef9f` changes no behavior.
Scope: shared `NativeTextProjection` direct difference and existing newline-atom
fallback, dedicated regressions, and retained isolated execution artifacts.
Concurrent encoding/source-capability APIs are outside this review.

## Verdict

**No substantive defect found in the scoped scalar-boundary correction.**
The source removes a demonstrated engine-rejected edit without weakening engine
validation or changing the source/display mapping contract. No production/test
files were edited, tests rerun, build invoked, native GUI launched or push made
for this review. This is not native input-method, platform ingress or AOT evidence.

## Mechanism and executable contract

The former code-unit prefix could end between the high and low surrogate shared
by `😀` and `😁`; reconstruction looked correct, but the edit itself violated
`Document.Apply`'s endpoint contract. A suffix could likewise retain only a shared
low surrogate. The correction uses **one** `ReplacementBounds` mechanism in both
the direct display path and the fallback's final canonical-source replacement.

* Prefix scanning is unchanged until the mismatch. If either input boundary
  splits a valid high/low pair, retreating one unit includes the equal high half
  in both replacement extents. This cannot underflow because `SplitsScalar`
  requires a positive interior offset.
* Suffix scanning stops at the already-safe start. If either endpoint splits a
  pair, advancing both endpoints includes the equal low half. A split endpoint
  implies a suffix unit was removed; therefore both endpoints have room to
  advance and cannot exceed their respective lengths. The replacement still
  reconstructs exactly the requested string.
* Equal input remains a null change. Complete replacement/deletion and insertion
  at source boundaries retain the existing range semantics. Ordinary surrogate
  pairs do not alter newline-coordinate mapping, so widening a display scalar
  boundary also preserves the corresponding canonical scalar boundary.
* The CRLF path retains floor/ceiling mapping and preferred-newline normalization
  inside the explicit edit; unchanged source outside it is not rewritten. The
  exceptional CR/deleted-text/LF seam still verifies exact reprojection before
  returning its canonical change. Its final difference now shares the same
  scalar-safe endpoint calculation rather than an independent code-unit loop.

No Unicode normalization, replacement-character repair, grapheme segmentation,
NUL stripping or new scalar admission rule is added. Inspected current engine
`Apply`: boundary and inserted UTF-16 validation precede rope/history mutation.
Unpaired surrogate insertions remain rejected without document changes. **NUL is
valid engine text and is preserved**, not falsely described as an engine error;
native controls' representation/capability refusal is a separate contract.
Microsoft's [UTF-16 and scalar documentation](https://learn.microsoft.com/en-us/dotnet/standard/base-types/character-encoding-introduction)
supports the distinction between code units, paired supplementary scalars and
user-perceived grapheme clusters. This fix concerns the scalar endpoint contract
only, not whole-grapheme editing behavior.

## Independently inspected retained validation

Parsed actual TRX counters under
`.cache/validation/native-unicode-edit-difference/`, without rerunning them:

| Artifact | Executed / passed / failed / unexecuted | Scope |
| --- | ---: | --- |
| `before/before.trx` | 2 / 0 / 2 / 0 | Both projection modes fail at Apply with surrogate-bisection ArgumentException |
| `after/after.trx` | 14 / 12 / 2 / 0 | Retained malformed-string test serialization problem, not qualified malformed-input evidence |
| `after/after-qualified.trx` | 15 / 15 / 0 / 0 | Runtime-constructed malformed UTF-16, fixed cases and 5,000 deterministic Apply/reprojection selections |
| `existing/existing-projection.trx` | 7 / 7 / 0 / 0 | Existing newline/mapping/fallback/randomized/exhaustive methods extracted unchanged |

The tests exercise actual engine Apply, exact fixed-case source, reprojection and
Undo/Redo, rather than accepting successful string splicing alone. Randomized
anchor/caret choices are independent and include both projection modes, surrogate
sharing, mixed newlines and NUL. The generator deliberately chooses scalar and
CRLF boundaries; it is not evidence for arbitrary native caret/IME states.
Malformed numeric code-unit parameters avoid test-runner repair of invalid UTF-16.

The repository-local focused project links the production projection source and
references a frozen Engine DLL; it does not substitute a mock splice engine.
Independently checked current worktree byte identities:

* Projection: `1187E829D2D06E1026CF5E2CEB634055EEDA6A8966388E8464EDF126F4098FFF`.
* Dedicated tests: `4743A84860E089C99174F96B060980AE239CFF0E20D5EFFDF9E494BB4DB59421`.
* Frozen Engine DLL: `534E6B451C6D6B5D2A07979F0AD966A2DD4B4BD52C34A2C87C9A8BA042A0582E`.

These source hashes identify observed checkout bytes, not a universal LF/CRLF
checkout hash. Scoped sources are unchanged from their pinned commits despite
unrelated concurrent work. See [validation methodology](../validation/native-unicode-edit-difference.md)
for the qualified isolated harness and retained initial failures.

## Limits

The helper preserves the existing linear scan and adds constant-time boundary
checks, with no additional allocation in the helper itself. This is a source
complexity assessment, not a latency/allocation benchmark. Shared full-suite,
controller ingress, native selection, IME, macOS/Windows GUI and published Native
AOT behavior require separate integration evidence. Existing platform text
capability and explicit file-encoding changes are neither verified nor implicitly
approved by this narrowly scoped review.
