# Explicit-open encoding engine review

Date: 2026-10-01. Review type: independent source and contract review.
Base HEAD: `33858ff15b14c45a0551001117c53de8dbd06bf3`; encoding changes were
working-tree changes during inspection.

## Decision

**No substantive defect found within the engine change under review.** This is
not a claim that native encoding selection or four-platform Native AOT behavior
has been verified. Independent portable tests are owned by the validation agent;
this review did not rerun completed builds or probes.

Reviewed files and byte SHA-256 at inspection:

| File | SHA-256 |
| --- | --- |
| `src/Mote.Engine/Document.Open.cs` | `C15D107BFD2454F16BB158B8D8E57D63D35AA1BB0EB114875F632A2059AD4317` |
| `src/Mote.Engine/DocumentTextEncoding.cs` | `D67577B6F2323FBFF04DEA38FC5BC711FB38A03429D35C4F837C95F5C4748865` |
| `src/Mote.Engine/Document.cs` | `7395DCFBEBAB972BC9207D29C042B8405C07B833327EF4D34E3671E19862BC2E` |

Also inspected `docs/architecture/explicit-open-encoding.md`, the new portable
test source and validation notes, and the existing save implementation only for
the newly admitted codecs' integration paths.

## Evidence and executable paths

- **Default-open compatibility:** `Document.OpenAsync` forwards to
  `OpenCoreAsync(path, null, token)`. The former read loop, raw hash, stream
  sharing, BOM detector, chunk construction, file-stamp comparison and document
  publication are preserved. The nullable explicit choice does not allocate an
  inverse verifier on this route. UTF-32 BOM detection still precedes UTF-16's
  shared prefix. No legacy guessing or automatic retry enters default open.
- **BOM policy:** `OpenCoreAsync` rejects a recognized BOM whose code page differs
  from the selected codec before decoding or publishing. Matching Unicode BOMs
  are consumed and included in the raw hash; `markerSize > 0` is retained for
  Save. BOM-less explicitly selected Unicode does not acquire a BOM. Legacy
  choices cannot silently consume a Unicode BOM.
- **Strict provider isolation:** the closed enum rejects unknown values. Fixed
  provider lookups supply both exception fallbacks, do not register a global
  provider, and do not consult code page zero or the host locale. This is a
  deliberate deviation from Microsoft's recommended registered-provider usage,
  documented in the architecture note rather than hidden as ordinary guidance.
  No reflection or dynamically generated codecs appear in this change.
- **Bounded inverse verification:** one encoder and incremental hash persist
  across all decoded chunks; a fixed 64-KiB output buffer is drained until
  `completed`. Input advances by `usedChars`, not buffer length. Stateful handling
  therefore retains a trailing UTF-16 high surrogate and resumes on subsequent
  input; this matters for GB18030 astral characters. Final empty-input flush
  occurs before hash comparison. A second whole-document string or byte array
  is not allocated. The document itself remains subject to the existing UTF-16
  length limit; bounded verifier memory is not a claim of bounded document memory.
- **Admission and ownership:** source hash covers every raw byte, including the
  BOM. Legacy inverse identity is checked before a `Document` exists. Exceptions
  unwind the source stream and both hash lifetimes through `using` / `await using`.
  No recovery files or source writes occur during open. The fresh document retains
  the same initial clean state and empty history.
- **Cancellation:** the same cancellation token reaches initial marker and
  subsequent stream reads. No cancellation exception is swallowed or converted
  to a codec retry. CPU-side decode/hash/rope work is not instruction-level
  cancellable, just as in the prior default path; the API does not promise an
  atomic cancellation fence immediately before publication.
- **Save failure protection:** `SaveCoreAsync` captures the retained encoding;
  `ForWrite` passes legacy codec instances through, retaining strict encoder
  fallback. Encoding and flushing complete in an exclusively created staging
  file before `CommitTempAsync` can replace a target. A failed encoding takes the
  existing nonfatal cleanup/inspection path, not commit or saved-state
  bookkeeping. Cleanup failure retains recovery ownership rather than claiming
  the staging file vanished. The raw source hash still participates in final
  external-change verification, including same-size / restored-timestamp edits.

## Limits and required next evidence

The architecture note accurately separates engine implementation from native
chooser ownership. A successful codec choice is not proof the user chose the
right codec: wrong-but-valid text can round-trip. No heuristic correctness claim
is warranted.

The validator's fixed-byte tests are stronger than codec-generated expectations.
Its exhaustive fixed-width scan found no real-provider inverse mismatch; the
defensive rejection branch therefore must remain explicitly unexercised rather
than supported by an invented duplicate mapping. This review makes no new test
execution claim.

Actual Windows/macOS x64/arm64 Native AOT execution must establish code-page
resource inclusion and strict behavior, alongside existing one-binary inventory
checks. Analyzer compatibility and an embedded resource alone are insufficient.
Native retry must independently prove cancel/failure/stale/replacement safeguards
before the feature is described as shipped. Existing unrelated save races,
crash recovery and UI behavior were not re-audited here.

## Primary references checked

- [EncodingProvider.GetEncoding contract](https://learn.microsoft.com/en-us/dotnet/api/system.text.encodingprovider.getencoding?view=net-10.0): public fallback overload, exception-versus-replacement semantics, and the explicit recommendation not to invoke provider lookup directly from user code.
- [CodePagesEncodingProvider, .NET 10.0.0 source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Text.Encoding.CodePages/src/System/Text/CodePagesEncodingProvider.cs): fixed-code-page provider boundary used by the documented implementation.
