# Scalar-safe native page differences

## Contract and mechanism

`NativeTextProjection.Difference` returns one UTF-16 `TextChange` consumed by
`Document.Apply`. The engine rejects boundaries inside a surrogate pair and rejects
ill-formed inserted UTF-16. String splicing alone is not an adequate oracle: splitting
both strings at the same high surrogate can reconstruct correct-looking text while
still producing an illegal engine edit.

Before this change, replacing `left😀right` with `left😁right` yielded a common
prefix ending after the shared high surrogate. The proposed replacement therefore
started at the low surrogate. Both Preserve and CRLF projection modes failed at
`Document.Apply` with `ArgumentException: The edit bisects a UTF-16 surrogate pair`.
The pre-fix actual failing tests are retained under
`.cache/validation/native-unicode-edit-difference/before/before.trx` (2 failed, 0 passed).

The common-prefix/suffix calculation is now a single shared helper. It retreats a
prefix endpoint or advances both suffix endpoints by one equal code unit whenever
either string's endpoint lies inside a surrogate pair. Unchanged units can belong
to the replacement without changing the resulting text. The same helper is used
by the ordinary display difference and by the existing exceptional newline-atom
reconstruction path. No public API, source/display coordinate map, newline policy,
or engine admission rule changes.

The change is scalar-boundary preservation, not a grapheme segmentation or Unicode
normalization feature. It does not sanitize unpaired surrogates or strip NULs.
Native controls' ability to represent NUL is a separate capability contract; here
NUL is retained exactly in the source and edit. See the platform-neutral Unicode
scalar versus UTF-16 distinction in [Microsoft's encoding documentation](https://learn.microsoft.com/en-us/dotnet/standard/base-types/character-encoding-introduction)
and [`Rune` API documentation](https://learn.microsoft.com/dotnet/api/system.text.rune).

## Actual focused evidence

Environment: Windows host, .NET SDK 10.0.400, Release portable test execution,
no GUI, clipboard, input-source, activation, or global input operation.

The first shared test-project build was not a regression result: concurrent explicit
encoding tests referred to an API not yet written. A separate repository-local
`.temp/native-unicode-edit-difference/Focused.csproj` subsequently exercised the real
pre-fix projection and real engine, producing the two failures above. For post-fix
tests, this project links the production projection source directly and references
a frozen Engine DLL, avoiding shared native build-output races. Its local enum
fixture has exactly the two production `NativeLineEndingMode` members.
Frozen Engine DLL SHA-256:
`534E6B451C6D6B5D2A07979F0AD966A2DD4B4BD52C34A2C87C9A8BA042A0582E`.

`NativeUnicodeEditDifferenceTests` passed **15/15**, zero failures/skips:

- Shared high-surrogate and shared low-surrogate replacement in both projection modes.
- Emoji insertion, deletion, permutation, Chinese text and mixed untouched CR/LF/CRLF.
- Existing newline seam fallback, source NUL retention, and an edit at the 16-Ki-unit
  rope leaf seam.
- Actual `Document.Apply`, exact final source, reprojection, undo and redo for fixed cases.
- 5,000 deterministic independent selections (seed `0x5343414C`), choosing anchor and
  caret independently, then actual Apply and exact reprojection in both modes.
  Selection direction is normalized only to the text replacement extent; this is
  not a native selection-direction or IME integration test.
- Three malformed UTF-16 cases constructed at runtime, rejected without document mutation.

An initial malformed-input fixture used `InlineData` strings, but test-runner
serialization repaired their unpaired surrogates into replacement characters and
deduplicated two cases. That run (12 passed, 2 failed) is retained in `after.trx`
and is not a product failure or qualified malformed-input result. Numeric code-unit
parameters and runtime string construction eliminate that fixture corruption.
The qualified result is `after/after-qualified.trx`.

Seven pre-existing projection cases were extracted unchanged from
`NativeControllerTests` into the local focused project and passed **7/7**:
mixed newline coordinate preservation, nearby newline spelling, the CR/deleted-text/LF
seam, the existing 5,000-case randomized round trip, and exhaustive short round trips.
The extracted tests are an isolated execution of those methods, not a full controller
suite or cross-platform Native AOT certification. Result: `existing/existing-projection.trx`.

Reproduction from the repository root:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release -p:PublishAot=false `
  --filter FullyQualifiedName~NativeUnicodeEditDifferenceTests
```

The production suite needs all concurrently developed dependencies to be present.
No performance benchmark was run: the helper retains the existing linear code-unit
scan, adds constant-time endpoint checks, and performs no new allocation. This is a
small ordinary-file correctness fix, not a large-file or native latency claim.
