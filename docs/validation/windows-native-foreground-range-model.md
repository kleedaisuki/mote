# Windows native foreground witness model validation

Date: 2026-10-02. Verdict: **25 focused portable cases passed; native COM and
foreground readback remain unverified by this check**.

## Contract and scope

The full-native source host must apply attribute-only foreground changes in
input order after validating every span. A complete publication first resets
previous styling to the theme foreground. Spans are half-open display UTF-16
ranges; endpoints inside an astral scalar pair or CRLF expansion are invalid.
Zero-length spans at legal boundaries are admitted but do not paint anything.
Overlapping runs retain last-writer-wins behavior, not sorting or widest-span
priority. The small readback witness set must never select only half a scalar
or expanded paragraph. It is deliberately bounded, not exhaustive verification.

This derives from `INativeSourceDiagnosticHost.PublishStyles`, the existing
RichEdit paragraph/display offset contract, and the detached-range worker's
explicit foreground-only proposal in
`docs/performance/native-source-capability-timeout.md`. It is not derived solely
from the implementation returning particular colors.

`tests/Mote.Tests/WindowsNativeForegroundRangeTests.cs` invokes the production
pure helpers on `WindowsNativeSourceCapabilityProbe`. It invokes private
`Host.ValidateStyles` through reflection without constructing a Host. No native
constructor, HWND, `SendMessage`, COM call, GUI, global input, filesystem fixture,
or process launch occurs inside these tests. The file locally suppresses CA1416
because only pure methods on a Windows-annotated container are used; there is
no OS-dependent early return or skipped acceptance on macOS.

## Independent expectations

- Four document-only cases: empty, one character, two characters, and ordinary
  source; exact expected start/middle/end offsets and duplicate elimination.
- One run interior plus neighboring uncovered positions: exact samples and
  half-open start/end foreground expectations.
- Overlaps in both publication orders, with zero-length overwrite attempt:
  literal color arrays, not a copy of the reverse-search implementation.
- Empty and zero-length-only publications: default foreground expectations.
  This verifies the expected-color oracle, **not an actual native reset**.
- Four scalar/paragraph cases: emoji-only, CRLF-only, mixed `A😀\r\nB`, and
  `e` plus combining acute. The combining mark remains its own scalar; this is
  not a grapheme-cluster guarantee.
- Mixed emoji/paragraph mapping: emoji occupies two native UTF-16 units; CRLF
  becomes one RichEdit paragraph unit; no sampled trailing surrogate/LF.
- Eleven invalid-span cases: negative start/length, beyond-document start/end,
  integer-size excessive length, and start/end/zero-length boundaries inside
  either an emoji or CRLF. All throw `ArgumentOutOfRangeException` before any
  native call. Valid empty-source/endpoints/whole-unit runs also pass.
- One deterministic differential case: seed 73041, 100 trials of 43 arbitrary
  valid overlapping runs over 257 characters. An independent **forward paint
  array** supplies 25,700 expected foreground values. Every returned witness is
  in bounds and unique; each set has at most 18 entries.

## Reproduction and observed results

Environment: Windows development host; .NET SDK **10.0.400**, Release/net10.0.
Commands, from repository root:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore `
  --filter FullyQualifiedName~WindowsNativeForegroundRangeTests `
  --logger "trx;LogFileName=focused.trx" `
  --results-directory .cache/windows-foreground-model
```

Actual result: **25 executed, 25 passed, 0 failed, 0 skipped**, test duration
59 ms. Evidence: `.cache/windows-foreground-model/test.log` and `focused.trx`.
The first compile produced CA1416 warnings because pure helpers inherit their
container's Windows annotation. After adding a documented file-local annotation
suppression, a build-only check passed with **0 warnings, 0 errors**, 3.88 s:

```powershell
dotnet build tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore
```

Evidence: `.cache/windows-foreground-model/build-annotation.log`. No assertions
or production behavior changed, and completed tests were not repeated solely
for the warning-only edit.

Identities for the actual tested source/binaries (SHA-256):

| Artifact | SHA-256 |
| --- | --- |
| Production adapter | `078AE3FA876D1BFD1747FB073D987BF4376B8771BD3A0A01BBB7EC4861FCA0CD` |
| Detached TOM helper | `37C38F989F01E0EEAC5131E28D55B65AB82E30835547AACAD75551F58A1682D6` |
| Test source at execution | `0210A0CAD47F21BDF29B6D6A84C980094C37437C9255FC417E0B0297BEC6F303` |
| Test DLL at execution | `1A70490D913A450000012557E6313E68143B89DE2DC884923449A1429FD8FFF8` |
| Native managed DLL at execution | `FFFADDD05CFCA325CD473728751C56F922C9D6C734BCD62AEEE612C8D4DA4C84` |
| Test source after warning-only annotation | `6EA7CFDB17D58F181C3E58FAB852AE6F214A6E49DDB98AE9F50F8945FDF7270D` |

## Coverage limits and next evidence

No fake COM framework was introduced: a portable fake would not certify actual
TOM ABI, ownership, S_OK/S_FALSE behavior, Freeze/Unfreeze balance or RichEdit
color readback. Those require independent ABI review and the unchanged hosted
native capability workflow. This check also does not prove text/selection/scroll
preservation, native Undo state, font attributes, dense-publication latency,
AOT compatibility, physical presentation, real typing or IME behavior.

The next discriminating evidence is the actual hosted full fixture journey:
initial and post-edit dense foreground publication, native getter witnesses,
state preservation, engine Undo/Redo, exact Save and fresh Document reopening.
The 74–79 s retained baseline is not rerun or erased by these model tests.
