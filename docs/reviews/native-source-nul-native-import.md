# Native RichEdit NUL import probe and safe source-host decision

Date: 2026-10-01. Failure-directed Windows source-host investigation. Ownership:
Windows shell, Continuous RichEdit input island, and the shared Windows native
readback safety helper. Grid adapter logic is not part of this source-host fix.

## Probe before representation choice

The self-contained probe is `.temp/source-nul-stream-probe/Probe.csproj` and
`Program.cs`. It creates hidden `RICHEDIT50W` HWNDs using the system msftedit.dll,
imports length-delimited bytes using `EM_STREAMIN` with
`SF_TEXT | SF_UNICODE` (`0x11`), reads explicit `EM_GETTEXTEX` returned counts,
and exports length-delimited `EM_STREAMOUT` bytes. It then performs a native
prefix `EM_REPLACESEL` with `X`, without clipboard writes or desktop input.

```powershell
dotnet run --project .temp/source-nul-stream-probe/Probe.csproj -c Release
```

**ABI constraint:** RichEdit `EDITSTREAM` requires `Pack=4`. The initial probe's
default-packed 64-bit structure failed with process exit `-1073740791`; correcting
the callback pointer layout made the scoped probe run normally. No production
streaming import was introduced.

| Input UTF-16 units | Stream-in result | Explicit stream-out before edit | After prefix X |
| --- | --- | --- | --- |
| `0061,0000,0062` | 6 bytes, error 0 | `0061,0000,0000` | `0058,0061,0000,0000` |
| `0061,2400,0062` | 6 bytes, error 0 | exact input | `0058,0061,2400,0062` |
| `0000` | 2 bytes, error 0 | `0000` | `0058,0000` |
| `0061,0062,0063` | 6 bytes, error 0 | exact input | `0058,0061,0062,0063` |
| `0061,0000,0062,000D,000A,0063` | 12 bytes, error 0 | `0061` followed by five zero units | `0058,0061` followed by five zero units |

`EM_GETTEXTEX` returned the apparent full character count for NUL-containing
text but did not populate the complete suffix. An uninitialized unmanaged
readback buffer consequently exposed unrelated units after its terminator.
These counts therefore do not prove complete source delivery. Neither a
successful streaming return nor explicit-count retrieval establishes a lossless
NUL source host on this machine. U+2400 and ordinary text controls succeeded.

## Decision: explicit read-only bounded NUL source host

Editable global NUL-to-U+2400 substitution is **not** sound under the existing
complete-string edit notification: original `NUL,U+2400` becomes two identical
markers and deleting/inserting one cannot be uniquely inverted. The production
fix therefore does not change `NativeTextProjection`, globally decode U+2400 to
NUL, or pretend that complete-string differences carry exact edit provenance.

- A NUL-bearing bounded source page/island displays one U+2400 per source NUL,
  preserving UTF-16 coordinate width, **and is read-only**. Literal U+2400 source
  remains literal. An explicit nonmodal notice explains the limitation and that
  Engine text and Save remain authoritative.
- Native Cut/Paste/Clear/character/IME-start mutation paths and the global source
  Cut command are blocked for this host. Continuous global selection deletion,
  paste and `CommitFinalText` cannot emit an Engine transaction while blocked.
- Injected programmatic native replacement is never accepted as canonical text;
  its next commit notification restores the safe display from the ready page or
  exact binding. Read-only selection still maps one-unit display coordinates to
  source offsets; it is not an editing inverse.
- Every source import, including ordinary non-NUL text, verifies exact native
  readback before installing offset authority. An unexpected transformation
  disables native input rather than updating the baseline from transformed text.
- `WindowsNativeSourceSafety.Read` uses explicit counts and a zero-initialized
  pinned managed UTF-16 buffer. Invalid counts or embedded zero units fail closed;
  no null-terminated prefix or uninitialized unmanaged suffix becomes Engine
  input. The same helper is used by both Windows source hosts.
- Native read-only/no-op settlement leaves Save available. Save uses canonical
  Engine content, never the visible U+2400 marker text. Subsequent NUL-free
  bindings restore normal editability; protection is bounded-host capability,
  not a fabricated whole-file format diagnostic.

## Verification state

The native stream-in/out probe completed with the concrete results above.
After safe-host implementation and helper extraction, Native Release build
with `--no-restore -warnaserror`: **0 warnings / 0 errors**.
The independent source-NUL validator owns `NativeCsvGridSourceNulTests.cs`:
**21 distinct source-safety cases accepted**, including actual native mutation
restoration, NUL/literal-U+2400 same-display transitions, exact Engine Save/UTF-8
bytes/reopen, invalid-handle map retirement/re-certification and Continuous
mismatch binding retirement. Its final failure-directed guard run passed
**3/3**, Release `-warnaserror`; these are validator results, not inferred from
this build. Physical pointer/IME and forced thrown-reader injection were not
claimed by that validator.
No staging, commit, push, real clipboard writes, or physical input occurred.

The limitation remains visible and intentional: editing a NUL-bearing interval
is unavailable in these RichEdit-backed source hosts until exact native edit
range provenance or another lossless input representation is implemented.
