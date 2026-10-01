# Explicit source encoding without implicit conversion

Date: 2026-10-01. Scope: ordinary file I/O, not encoding detection research.
Status: implemented engine contract, independently reviewed and portable-tested;
Native chooser integration and platform evidence are tracked separately below. The intended user's novel encoding is unknown.

## Existing behavior and compatibility

`Document.OpenAsync(path, CancellationToken)` recognizes UTF-8, UTF-16 LE/BE,
and UTF-32 LE/BE byte-order marks (BOMs), otherwise strictly decodes UTF-8.
Its raw-byte SHA-256 fingerprint includes the BOM. Save retains the chosen
encoding, BOM, mixed line endings, and snapshot-based dirty/undo semantics.
No invalid input is silently replaced. These default behaviors must remain
unchanged. `ForWrite` already preserves a non-Unicode encoding instance in its
default branch; the current open path cannot supply one.

## Bounded explicit choice

The implementation adds a distinctly named `OpenWithEncodingAsync` rather than an ambiguous overload
of `OpenAsync`. A closed `DocumentTextEncoding` enum admits UTF-8, UTF-16 LE/BE,
UTF-32 LE/BE, GBK (Windows code page 936), GB18030 (54936), and Big5 (950).
These labels identify the .NET codec mappings, not a promise that every historical
revision or vendor mapping is interchangeable. Do not accept arbitrary code page
integers, guess by locale, or automatically retry every codec.

An explicit choice must agree with a recognized Unicode BOM. A conflict rejects
the open before a document is published; a matching BOM is consumed and retained
for Save. Explicit BOM-less Unicode remains BOM-less. Legacy encodings have no
output BOM. Both decoding and encoding use exception fallbacks, never best-fit
or replacement fallbacks.

Legacy byte sequences can decode successfully yet re-encode to different bytes
because mappings need not be one-to-one. The editable open path therefore
incrementally re-encodes decoded chunks and compares its SHA-256 with the raw
input fingerprint before publication. Non-roundtrippable bytes are rejected,
not silently canonicalized during a later no-op Save. This extra linear work is
limited to explicit legacy opens and uses bounded buffers. Wrong-but-valid
encoding choices can still produce wrong text and round-trip; no heuristic can
replace the user's choice.

The new document remains a fresh lifetime: version 0, clean state, no undo
history. Save, Save As, external-change verification, exclusive recovery staging,
and failed-encode cleanup use the existing engine contract without bypasses.
Unsupported edits must fail encoding before target replacement and remain dirty.
Encoding conversion / lossy import is not part of this contract.

## Provider and Native AOT boundary

The .NET 10 provider implements 936/950 using DBCS tables and 54936 using its
GB18030 implementation. Its mapping data is an embedded `codepages.nlp` resource,
not an external plugin or generated runtime assembly.
[Provider source, v10.0.0](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Text.Encoding.CodePages/src/System/Text/CodePagesEncodingProvider.cs),
[library project, v10.0.0](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Text.Encoding.CodePages/src/System.Text.Encoding.CodePages.csproj).

Microsoft's usual application guidance registers the provider process-wide and
then calls `Encoding.GetEncoding`. Registration changes code-page-zero behavior
on Windows. The implementation deliberately calls the provider's public
fixed-code-page lookup directly, avoiding global registration and code page zero.
This is a deviation from the documented usage recommendation, justified by the
explicit isolated policy and checked against the shipped source; no private
reflection is involved.
[Provider guidance](https://learn.microsoft.com/en-us/dotnet/api/system.text.codepagesencodingprovider?view=net-10.0),
[fallback contract](https://learn.microsoft.com/en-us/dotnet/api/system.text.encodingprovider.getencoding?view=net-10.0).

Local Release/AOT-analyzer builds are not four-RID Native AOT runtime proof.
Actual Windows/macOS x64/arm64 execution and one-binary inventories remain CI
acceptance requirements. No encoding support claim should bypass those checks.

## Native user interaction

The default file chooser retains default decoding. A decoding error offers an
explicit encoding choice, with cancellation as the safe default. A successful
retry constructs a fresh document before replacing the existing document; a
cancelled, failed, stale, or refused retry must preserve current text, dirty state,
selection/history, and recovery ownership. Concurrent edits while opening must
still trigger the existing replacement confirmation. BOM conflicts and
non-roundtrip failures are not automatic retry signals.

Root approved an optional `INativeOpenEncodingShell` capability, including a File
> Open with Encoding command (no shortcut change) and a chooser after default
strict decode failure. Existing shell implementations need not implement it.
Controller and both native dialog implementations are integrated. A codec retry
retains the original approved document and version through the modal choice;
open serial changes invalidate the choice, and the existing Save / composition /
later-edit confirmation guards remain. The original single-argument private
default-open entry is retained for diagnostic callers. Actual OS dialog /
encoded-file workflows are not yet verified.

## Initial inexpensive probe

Repo-local `.temp/encoding-provider/Probe.csproj`, net10.0 Release with
`IsAotCompatible=true` and warnings as errors, executed successfully on the local
Windows host. Direct strict provider lookups verified fixed bytes:

| Mapping | Source bytes | Decoded text | Re-encoded bytes |
| --- | --- | --- | --- |
| 936 | D6 D0 CE C4 | 中文 | D6 D0 CE C4 |
| 54936 | D6 D0 CE C4 | 中文 | D6 D0 CE C4 |
| 950 | A4 A4 A4 E5 | 中文 | A4 A4 A4 E5 |
| 54936 | 94 39 FC 36 | 😀 | 94 39 FC 36 |

Code page 936's A2 E3 maps to U+E76C in this provider, whereas 54936's A2 E3 maps
to U+20AC. Do not invent a duplicate-Euro fixture based on encoding names.
This probe establishes lookup/strict codec behavior only, not production
document safety or Native AOT GUI behavior.

## Qualified engine delivery

Independent portable validation passed 47/47 cases; a later change only to five
BOM-property assertions passed those same 5/5 affected cases, not 52 unique tests.
The qualified DLL hash, commands, fixed raw bytes, exhaustive provider scans, and
coverage limits are retained in [validation](../validation/explicit-open-encoding.md).
The [independent review](../reviews/explicit-open-encoding-engine.md) found no
substantive defect in the bounded engine patch. Native AOT runtime, native dialogs,
and actual native modal lifetime outcomes remain separate acceptance requirements.

The optional-capability controller has 13 new portable cases plus 153 existing
controller regressions, all 166 passing. A mechanical private-wrapper compatibility
correction was subsequently checked by the 11 affected paint-trace tests. Both
native shells compile in the coordinated Release build; the Mac helper additionally
has isolated typed-ABI/layout compilation evidence. Neither compile proves actual
dialog operation. See [Windows chooser](../validation/windows-open-encoding-ui.md),
[macOS chooser](../validation/mac-open-encoding-ui.md), and the
[native integration review](../reviews/explicit-open-encoding-native.md).

A root-approved tiny hosted-only published-image diagnostic exercises the actual
codecs and safe-save paths through `--check-native-encoding`; its fixed-file body
passed one portable test without a simulated hosted identity. The bounded
admission, exclusive fixtures, closed report, and marker contract are described
in validation. Four-RID actual Native AOT execution remains pending CI, not an
inferred consequence of a portable green test or a successful publish.
