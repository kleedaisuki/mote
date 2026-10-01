# Windows complete-source import: Unicode stream candidate

Date: 2026-10-02. Status: **candidate implemented and portable callback/ABI
qualified; no candidate native execution or performance result yet**. Scope is the diagnostic adapter's whole-control
initial/history import, not ordinary per-key product editing or default promotion.

## Evidence and competing explanations

Reuse [native source timeout attribution](native-source-capability-timeout.md)
and [one editing locus](../architecture/ordinary-editing-locus.md). The newer
retained baseline is CI
[36899695843](https://github.com/kleedaisuki/mote/actions/runs/36899695843), under
`.cache/ci-36899695843-codec-source/artifacts/`. Its existing three fixtures,
order, readback, style publication, history and Save oracles remain unchanged.

The synthetic novel has 3,711,959 UTF-8 bytes, not 3.54 million UTF-16 units.
These are individual hosted observations, not repeated-sample distributions:

| Novel whole-control phase | win-x64 ms | win-arm64 ms |
| --- | ---: | ---: |
| Initial `native-import` | 4898.8882 | 3875.6692 |
| Undo `native-import` | 4667.2545 | 3818.7237 |
| Redo `native-import` | 4621.8596 | 3810.1724 |

Read retained JSONL rows in order: the three imports correspond to initial,
Undo and Redo. All have terminal completed rows. The x64 supervisor identifies
the executable as SHA-256
`478CF5FD891100D92CA5900A520A6ECD299BBE391B4BD9BA62C4F1D1F08F835D`
before and after, with actual exit 0. Do not merge these timings with other
commits or infer a CPU-independent speed guarantee.

The current import is `SetWindowTextW` with the complete admitted string. There
are at least three competing explanations: whole-document native reformatting,
screen updates during import, and first-use font/script initialization. The
mixed-text initial x64 import is 1844.1462 ms but its later imports are 6.5783
and 6.1854 ms; that cold/warm distinction must remain visible. Streaming is not
expected to eliminate font discovery or mandatory native text layout.

## One discriminating candidate, not combined knobs

Use one `EM_STREAMIN` with `SF_TEXT | SF_UNICODE` to replace the complete control.
Do not add `SFF_SELECTION`, RTF conversion, code-page selection, draft-mode
flags, redraw suppression, TOM freezing, retries or a fallback import path.
This isolates the transfer mechanism while keeping independent redraw/readback
phases observable. A whole import is permitted only at this installation
boundary; it is not a proposed per-keystroke operation.

| Alternative | Reason not combined in this candidate |
| --- | --- |
| Owned `WM_SETREDRAW` false/true around `SetWindowTextW` | Simpler, but changes redraw/visibility state and tests screen-update cost rather than the import mechanism. A negative stream result can justify this as the next separate experiment. |
| Balanced TOM `Freeze`/`Unfreeze` around `SetWindowTextW` | Already valuable for many attribute updates, but an extra COM lease is unnecessary for one stream and would confound the mechanism comparison. |
| Unicode `EM_STREAMIN` | A mature RichEdit bulk-transfer path, with explicit byte callback and control error channel; chosen first. |

Microsoft documents stream replacement and Unicode flags in
[EM_STREAMIN](https://learn.microsoft.com/en-us/windows/win32/controls/em-streamin).
It documents screen-update suppression separately in
[WM_SETREDRAW](https://learn.microsoft.com/en-us/windows/win32/gdi/wm-setredraw)
and [TOM Freeze](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextdocument-freeze).
None establishes that streaming is faster for mote's workload. No generic
paper benchmark substitutes for the native control evidence here; this is a
bounded platform-mechanism comparison, not a new text-buffer research claim.

## ABI, lifetime and failure contract before implementation

The Microsoft [Richedit.h source](https://raw.githubusercontent.com/microsoft/win32metadata/main/generation/WinSDK/RecompiledIdlHeaders/um/Richedit.h)
uses four-byte packing. `EDITSTREAM` therefore has `DWORD_PTR` cookie,
32-bit `DWORD` error, and pointer-sized callback: on the two target 64-bit
architectures the offsets are 0, 8, 12 and the size is 20, **not 24**.
[Callback documentation](https://learn.microsoft.com/en-us/windows/win32/api/richedit/nc-richedit-editstreamcallback)
defines pointer-sized cookie, buffer pointer, signed 32-bit requested bytes,
signed 32-bit transferred-byte output and 32-bit unsigned error result.

The installed SDK was independently inspected at
`C:/Program Files (x86)/Windows Kits/10/Include/10.0.26100.0/um/Richedit.h`
(SHA-256 `E06C9B374B73F181E977D34AB953D4FB6DBB7582CD10ADBC701D151579391676`):
`pshpack4.h` at line 22 encloses callback line 1026 and structure lines
1028–1033 until `poppack.h` at line 1613. Line 1042 defines the Unicode flag.
This verifies the ABI independently of a C# declaration or test expectation.

Plan: one static `UnmanagedCallersOnly` Stdcall callback and a stack-resident
cursor containing a pinned string pointer, total byte length and consumed-byte
position. `fixed` keeps the UTF-16 string alive through synchronous
`SendMessageW`; no callback, cursor or source pointer survives that call.
There is no delegate thunk, GCHandle, secondary encoded full-file buffer,
managed callback allocation, global state or exception crossing the ABI.
The cursor uses 64-bit byte counts so a string length times two cannot wrap an
`int`; each transfer remains bounded by RichEdit's signed 32-bit request.

The stream transfers literal little-endian UTF-16 code units: no BOM, terminator,
escaping, Unicode replacement or newline rewriting. Existing caller admission
continues to reject NUL/non-CRLF display text before native mutation. Chunk
boundaries are transport boundaries, not edits; the callback preserves every
byte even if a scalar spans chunks. Zero output signals EOF only after the
source is consumed. Invalid callback arguments return an error, not false EOF.
Odd requested capacities round down to an even transfer; requests of zero or
one byte before EOF fail with `ERROR_INVALID_PARAMETER` and zero output.

After the message returns, require a zero `dwError`, nonnegative character
result and complete source-byte consumption. Empty source legitimately has a
zero character result. The character count is not equated with display length:
RichEdit paragraph normalization uses a different counting domain. The existing
separately timed complete readback remains the exactness authority. Native Undo
is still disabled/emptied by the existing caller and the engine remains the
sole history owner. A failed stream may have mutated the diagnostic replica;
the caller must fail the experiment, not publish an Engine edit or repair it.

## Qualification and next decision

Portable tests must exercise the actual unmanaged callback address, packed
structure offsets, exact mixed Unicode bytes, chunked transfers, empty/EOF,
invalid input rejection and completion checks without creating an HWND.
Such tests certify transport/ABI contracts, not RichEdit behavior or speed.
Hosted execution must retain unchanged full readback, selection, native Undo,
style, exact Save and reopen witnesses. Compare initial and warm imports
separately, along with the following readback/draw phases to detect shifted
cost. Reject the candidate if exactness fails, or revert it if no worthwhile
system-level benefit justifies the callback complexity. No timeout relaxation
or fixture reduction may turn a negative result into success.

## Portable qualification performed

The production change is only diagnostic `Host.Install` calling
`WindowsNativeTextImporter.Install` instead of `SetWindowTextW`. All existing
owner/admission checks, native Undo clearing, `_display` publication, fixtures,
style publication, deadlines and separate readback are unchanged. The new helper
uses actual existing `Win32.SendMessageW`; there is no stub interop and no
product-shell adoption.

To avoid compiling another writer's half-integrated product APIs, an isolated
project at `.temp/windows-native-import-callback/CallbackTests.csproj` links the
exact production helper, `Win32Interop.cs`, and new repository test file. It
uses .NET 10 and the repository's Microsoft.NET.Test.Sdk 17.14.1, xUnit 2.9.3,
and xUnit runner 3.1.4, with unsafe compilation and AOT/trim analyzers enabled.
No production Native AOT executable was created or run by this qualification.

```powershell
dotnet test .temp/windows-native-import-callback/CallbackTests.csproj -c Release `
  --logger 'trx;LogFileName=callback.trx' `
  --results-directory .cache/windows-native-import-callback
```

Observed local Windows x64 environment: .NET SDK 10.0.400, MSBuild
18.9.6, OS version 10.0.26200. The first build/test invocation succeeded with
**20/20 passing, zero failures/skips, 57 ms test duration**, no emitted build
warnings/errors. No failed candidate run was discarded. The tests call the
actual `UnmanagedCallersOnly` entry-point address through the declared
unmanaged callback ABI; they do not call a different managed copy routine.

Coverage: actual packed offsets and callback metadata; literal Chinese, emoji,
combining marks, bidi, CRLF, a literal U+FEFF and malformed UTF-16 code units;
six chunk capacities including odd capacities and surrogate-pair splits;
guarded destination allocations; empty source, real EOF, non-progress requests,
null arguments, invalid cursor alignment/bounds; necessary completion predicates
including a byte length above `int.MaxValue`; null/control/NUL refusal before
P/Invoke. Preserving malformed units in transport is not Engine admission or a
claim that RichEdit accepts them. Native import behavior, font retention,
paragraph handling, exact readback and performance remain hosted obligations.

| Qualified artifact | SHA-256 |
| --- | --- |
| `WindowsNativeTextImporter.cs` | `748FC3A686BFD2469FFF83B6D6B3760A698590A3C8B0FD17C6538DCA1F057220` |
| `WindowsNativeTextImporterTests.cs` | `B6226F3F12397D998C83DD6CD432C079451B47A0D946E5D2B3A4B7EB2A95A3FC` |
| Isolated `CallbackTests.dll` | `445269F6633AAE630A88A13594AD8D2D6532C09F1CE0970E4DB49511A39E875E` |
| `.cache/windows-native-import-callback/callback.trx` | `EBF5C601D794F757F8D46FED318F06423E9EBD894E2612DB615315AAC2869CA8` |
| Baseline win-x64 `report.jsonl` | `CC1632E02AD7C23EFD819533D766AA0D8DFD65D6A66EAE75EA302F46C880A9D4` |
| Baseline win-arm64 `report.jsonl` | `B9FDD61B40CAA2C15885BE0862BED4C8F88BE1E243E78F869B19742D929707F0` |

This is transport correctness evidence, not a speedup. The next discriminating
result is unchanged hosted three-fixture capability output at the frozen
candidate binary; initial/warm import times and subsequent draw/readback must
be compared separately. No local GUI/global input, CI dispatch/replay, commit
or push was performed by this task.
