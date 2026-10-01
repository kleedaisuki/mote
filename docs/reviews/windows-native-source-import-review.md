# Windows native source stream import: independent ABI and ownership review

Date: 2026-10-02. Status: independent source and retained portable-evidence review complete; no substantive scoped defect found. Native acceptance and performance remain unproven.
Scope: the explicitly selected native-source capability adapter's Unicode import,
not the ordinary Windows shell or default product presentation. Reviewer owns
this document only; no production changes, native GUI execution, test replays,
CI dispatch or performance claims are authorized by this review.

## Evidence and intended purpose

The retained baseline in [native source timeout attribution](../performance/native-source-capability-timeout.md)
reports 3.54-MiB novel native import at 4,786.7398 ms (win-x64) and
3,989.0690 ms (win-arm64), with similarly costly history reimports. A
Unicode `EM_STREAMIN` importer is a discriminating candidate, not a demonstrated
speedup and not an incremental Undo architecture. Exact control readback remains
the authority; neither callback consumption nor the message's character count
proves installed content.

## Independently verified SDK ABI

Installed Windows SDK `10.0.26100.0`, file
`C:/Program Files (x86)/Windows Kits/10/Include/10.0.26100.0/um/Richedit.h`,
SHA-256 `E06C9B374B73F181E977D34AB953D4FB6DBB7582CD10ADBC701D151579391676`:

- Line 22 includes `pshpack4.h`; line 1613 restores with `poppack.h`.
- Lines 1028–1033 place `EDITSTREAM` inside that four-byte packing region.
- Line 1026 defines callback return `DWORD`, parameters `DWORD_PTR`, `LPBYTE`,
  `LONG`, `LONG*`, and `CALLBACK` calling convention.
- On the targeted 64-bit Windows platforms: cookie offset 0, DWORD error
  offset 8, callback offset 12, complete size 20. Default eight-byte packing
  would produce callback offset 16 and size 24: that is **not this SDK ABI**.
- `SF_TEXT | SF_UNICODE` is 0x0011; no `SFF_SELECTION` means full control
  replacement. Unicode bytes are little-endian 16-bit units; a supplementary
  scalar still consists of two such units.

Microsoft primary references:
[EDITSTREAM](https://learn.microsoft.com/en-us/windows/win32/api/richedit/ns-richedit-editstream),
[callback](https://learn.microsoft.com/en-us/windows/win32/api/richedit/nc-richedit-editstreamcallback),
and [EM_STREAMIN](https://learn.microsoft.com/en-us/windows/win32/controls/em-streamin).
The callback must set actual transferred byte count, return nonzero on error,
and report zero transferred bytes only at genuine EOF (or explicitly fail a
zero-capacity request before EOF). The control can stop because of its own
failure even when the callback was successful.

## Initial review boundary (superseded by completed assessment below)

Pending concrete source inspection: pinned-string and stack-cookie lifetimes,
callback error/EOF/odd-byte behavior, exception containment, import completion
checks, exact readback preservation, NUL rejection, and disabled native history.
No defect or acceptance conclusion about implementation is asserted yet.

## Implementation assessment (first source)

Inspected `WindowsNativeTextImporter.cs`, SHA-256
`748FC3A686BFD2469FFF83B6D6B3760A698590A3C8B0FD17C6538DCA1F057220`,
and the one-call diagnostic `Host.Install` change. **No substantive defect found
in this inspected source.** This is source review, not native acceptance.

- `EditStream` explicitly has `Pack = 4`; pointer-sized unsigned cookie,
  unsigned 32-bit error and callback pointer match the SDK. The unmanaged
  Stdcall entry has unsigned 32-bit return, pointer-sized cookie, byte pointer,
  signed 32-bit capacity and signed 32-bit output pointer. Existing P/Invoke
  `SendMessageW(nint, int, nuint, nint)` selects the pointer overload.
- The cursor and packed stream are stack locals, and `fixed` pins the exact
  source throughout synchronous same-owner-thread `SendMessageW`. No delegate,
  GCHandle, pointer registry, global transfer state or outstanding callback
  survives return. Caller `CheckOwner` precedes installation. Invalid HWND
  ownership beyond that contract is not independently authenticated by helper.
- The callback initializes a nonnull output count to zero before validation.
  Null cookie, negative capacity, negative/out-of-range/odd cursor counts and
  null live source/destination fail with nonzero error. A null output pointer
  also fails without dereference. Capacity zero or one before EOF fails rather
  than reporting a false successful EOF. Odd larger capacity leaves one byte
  unused; only complete 16-bit units transfer.
- EOF is successful zero output after exact consumption; empty source does not
  require a nonnull data buffer. A surrogate pair can cross byte chunks, but
  no bytes are substituted or reordered. Native stream decoding across these
  chunks still needs the unchanged complete-readback oracle in hosted runs.
- On admitted pointers, `MemoryCopy` length is bounded by nonnegative capacity
  and remaining bytes. The cursor increment cannot overflow its validated
  nonnegative long length. These guards rule out ordinary managed exceptions
  from the callback's operations; arbitrary invalid pointers remain outside
  the contract and are not made safe by a generic catch. No allocation or
  exception-producing logging occurs inside the unmanaged entry.
- Import rejects NUL before mutation, checks little-endian platform semantics,
  excludes string terminator/BOM and checks error plus complete consumption.
  It deliberately does not equate RichEdit paragraph character count with the
  source's UTF-16 length. A nonnegative message result is necessary evidence,
  not a certificate of installed text.
- The caller preserves its existing CRLF display admission and disabled/cleared
  native undo contract. It sets its intended replica only after helper success;
  the runner then reads complete native text and calls `CertifyInstalled` before
  draw/selection/semantic publication. A stream failure may leave partial native
  text, but the diagnostic terminates rather than committing source/history or
  falling back to another importer. No redraw suppression, freeze flags,
  source normalization, fixture, timeout or default-presentation change is part
  of this diff.

At first source inspection, portable test evidence and actual Windows AOT control behavior were pending; portable evidence is assessed below.
Potential performance gain is unknown; compare import **and following readback/
draw** phases so deferred work is not mistaken for eliminated work. Full-file
Undo/Redo reimport is still a representation limitation even if faster.

## Retained portable qualification inspected, not replayed

Read the actual new `WindowsNativeTextImporterTests.cs` (SHA-256
`B6226F3F12397D998C83DD6CD432C079451B47A0D946E5D2B3A4B7EB2A95A3FC`)
and retained `.cache/windows-native-import-callback/callback.trx` (SHA-256
`EBF5C601D794F757F8D46FED318F06423E9EBD894E2612DB615315AAC2869CA8`).
The TRX counters independently show **20 executed / 20 passed, zero failed,
not-executed, aborted or error cases**. No test was rerun by the reviewer.

Tests invoke the production unmanaged entry address through its declared ABI,
not an alternative managed copier. They independently build literal UTF-16LE
bytes, including Chinese, supplementary scalars, combining marks, bidi, CRLF,
a literal FEFF and malformed units. Capacity 2 explicitly splits supplementary
pairs across calls, while odd capacities exercise the unused byte. Guard bytes
check destination boundaries. Other cases cover actual EOF/empty text,
negative/zero/one capacity, null arguments, invalid cursor bounds/alignment,
metadata/packed offsets, necessary completion predicates above int byte length,
and pre-P/Invoke invalid-input refusal. The test expectations match the primary
SDK contract rather than assuming default pointer alignment.

Limits: isolated managed x64 qualification does not certify ARM64 callback
execution, Native AOT output, actual RichEdit parsing across chunk boundaries,
font/paragraph retention, selection, native history or speed. The deliberately
malformed-unit case tests transport only; Engine and control admission are not
claimed. No test attempts a huge actual allocation merely because completion
arithmetic covers a large length.

**Final disposition:** no necessary correction identified in this scoped
implementation or tests. Keep the existing hosted exactness oracles and compare
all import/readback/draw work at the frozen candidate before keeping this added
mechanism for performance. This review is not approval to promote the adapter,
change default presentation, weaken history/readback checks, or claim the full
ordinary-editing goal complete.
