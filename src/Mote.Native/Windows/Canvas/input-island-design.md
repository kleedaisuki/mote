# Windows caret-local RichEdit island: integration constraints

Status: design contract under implementation; **not a statement that interactive
canvas editing is complete**.

The released RichEdit shell currently owns a bounded 64 KiB page projection.
The opt-in canvas path should keep the main HWND, menus, file dialogs and
controller commands, but substitute a source-backed continuous DirectWrite
paint surface plus a **visible** 4–16 KiB caret-local `RICHEDIT50W` input
control. The engine snapshot and global source selection remain authoritative;
no hidden full-document native text mirror is permitted. The default shell must
retain its current behavior when opt-in is false.

## State and edit contract

1. Every island projection is tied to a document generation, immutable base
   snapshot version, source start and length, and an explicit CRLF display
   projection. RichEdit internal selection offsets count one CR per line ending;
   the existing `RichEditOffsetMap` is needed in addition to the engine-source
   `NativeTextProjection`.
2. Ordinary committed text changes produce one source-coordinate replacement
   tagged with the island's generation and base version. A controller accepts
   it only against the matching current document; a new/open transition changes
   generation, making in-flight native notifications stale by construction.
3. Preedit text belongs to the OS control. While composing, do not publish
   text edits to the engine, rebase the island, import RTF, change theme/font,
   replace its text, save, format, or close the document. After commit/cancel,
   compare final native text with the base projection and submit at most one
   edit; cancellation produces no edit.
4. The island is visible at the caret so the OS candidate UI follows a real
   caret rectangle. Text outside it is drawn from bounded visible engine
   snapshot slices. Global copy/cut/search/selection use controller source
   coordinates, not the island's local text range.

RichEdit exposes both IMM window messages (`WM_IME_STARTCOMPOSITION` /
`WM_IME_ENDCOMPOSITION`) and TSF-aware notifications on recent systems:
`EN_STARTCOMPOSITION = 0x0713`, `EN_ENDCOMPOSITION = 0x0714`, with event masks
`0x10000000` and `0x20000000` in Windows SDK `Richedit.h`. Their ordering with
`EN_CHANGE` must be measured with real Pinyin/Kana/Hangul input; a synthetic
message smoke cannot certify candidate handling. Microsoft documents
[RichEdit composition notifications](https://learn.microsoft.com/en-us/windows/win32/controls/en-startcomposition)
and [Windows IME composition messages](https://learn.microsoft.com/en-us/windows/win32/intl/wm-ime-startcomposition).
For same-text replacement of a document-global selection, the Windows island
requires an actual printable `WM_CHAR` attempt or a nonempty IMM result string
advertised by `WM_IME_COMPOSITION` with `GCS_RESULTSTR`; `WM_IME_ENDCOMPOSITION`
alone is not proof of a commit because cancellation also ends composition.
On a synthetic cancel, RichEdit itself removed the bounded selected text even
though no result string existed. The island must restore that local host from
the immutable binding and publish no engine edit; `EN_CHANGE` or a differing
post-composition host is therefore also insufficient commit evidence.
This source-level distinction is necessary but does not certify TSF-only Pinyin
candidate behavior without a target-host manual test.

## Compatibility and performance gates

- Default mode regression tests must continue to pass unchanged. Experimental
  mode must not alter Save, Undo/Redo, encoding, newline, or conflict behavior.
- A 100 MiB file and a 50 MiB line still paint only `ViewportSlice` intervals
  (at most 16 Ki UTF-16 each); island rebase is caret-local and must not copy a
  whole logical line.
- Candidate rectangle, source/caret round trip, pointer selection across the
  former 64 KiB seam, and document generation races need direct probes.
- Actual Chinese Pinyin conversion/cancel, UIA `TextPattern` beyond the island,
  screen-reader behavior, Windows Arm64, and physical paint latency remain
  separate release gates even after synthetic tests pass.
