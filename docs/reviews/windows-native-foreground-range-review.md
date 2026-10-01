# Windows diagnostic TOM foreground range review

Date: 2026-10-02. Independent source/ABI review; no native execution or performance certification.

## Scope and conclusion

Reviewed the working-tree `WindowsRichEditForegroundRange.cs` and the foreground
publication changes in `WindowsNativeSourceCapabilityProbe.cs`, their existing
RichEdit offset mapping and undo-scope precedent, and the retained hosted timeout
attribution in `docs/performance/native-source-capability-timeout.md`.

**No substantive defect identified in this scoped change.** The implementation
removes per-token live-selection movement rather than weakening the fixture or
dropping semantic spans. This is a reasonable next experiment, not proof that
publication is fast or that the default product surface is ready.

Reviewed source SHA-256 identities:

- Host: `078AE3FA876D1BFD1747FB073D987BF4376B8771BD3A0A01BBB7EC4861FCA0CD`.
- Helper: `37C38F989F01E0EEAC5131E28D55B65AB82E30835547AACAD75551F58A1682D6`.

The reviewer did not modify production code or tests, run builds/tests, start a
native window, dispatch CI, commit, or push. The implementation owner independently
reported readiness; any build/test qualification belongs to its own evidence.

## ABI verification

Primary local source: Windows SDK `10.0.26100.0/um/TOM.h`, SHA-256
`ED6CFD4C3B128D46A8E5AC412DB763905C5A52403527084CDC05CB3F8F1526F7`.
Its C vtable declarations were inspected and method indices enumerated directly,
including IUnknown and IDispatch rather than counting only TOM-specific methods.

| Interface | Method | Zero-based slot | Native parameter shape after `This` |
| --- | --- | ---: | --- |
| ITextDocument | Freeze / Unfreeze | 18 / 19 | LONG* |
| ITextDocument | Range | 24 | LONG, LONG, ITextRange** |
| ITextRange | GetFont / SetFont | 18 / 19 | ITextFont** / ITextFont* |
| ITextRange | SetRange | 28 | LONG, LONG |
| ITextFont | GetDuplicate | 7 | ITextFont** |
| ITextFont | Reset | 11 | LONG |
| ITextFont | GetForeColor / SetForeColor | 24 / 25 | LONG* / LONG |

The explicit document IID matches ITextDocument, not ITextDocument2. LONG and
HRESULT use C# `int`, pointers use `nint`, and IUnknown Release returns `uint`.
The unmanaged Stdcall function pointers follow the SDK STDMETHODCALLTYPE ABI;
Windows x64/ARM64 use their platform calling convention without 64-bit LONG
widening. No RCW, reflection activation, or generated-at-runtime COM marshaling
is introduced. Source inspection does not substitute for both hosted AOT runs.

## Formatting, ownership and cleanup contracts

- The range is obtained through document Range, not GetSelection. SetRange
  retargets this separate range; the per-span loop does not select, import source,
  clear native history, or invoke Engine edits.
- The attached GetFont result is cloned through GetDuplicate and then released.
  Only the duplicate receives Reset(tomUndefined). Official semantics make all
  properties undefined, so transferring that font changes only subsequently
  defined foreground. Font family, size, weight, background and paragraph
  formatting are not copied from the original sampled range.
- Explicit colors use the existing RGB COLORREF conversion, with high byte zero.
  Readback rejects auto-color, undefined, palette-index or out-of-domain values
  rather than substituting the expected theme color.
- Acquisition outputs, including unexpected nonnull failing outputs, are owned
  and released. Readback obtains a fresh attached font on each sampled range,
  with release in finally; the detached setter font is not treated as evidence.
- Mutations/getters require S_OK. This avoids interpreting Reset's documented
  S_FALSE protected/no-change result as successful mutation.
- One successful Freeze acquisition is balanced by one Unfreeze in Dispose.
  The returned count must decrement exactly once. Unfreeze's documented S_FALSE
  for a remaining nonzero count is not mistaken for an HRESULT failure, nor is
  another owner's freeze drained in a loop. References release in finally even
  if Unfreeze reports failure; idempotent disposal does not repeat transitions.
- Helper acquisition/use/release stay on the checked managed owner thread;
  factory creation separately requires STA. The host restores selection and
  viewport and re-enables redraw through nested finally blocks, even after
  acquisition, application, readback or unfreeze failure.
- Successful publication still checks complete native text equality, sorted
  selection bounds, the available viewport tuple and empty native undo/redo.
  Engine version/history checks remain the runner's separate contract.

Failure cleanup is attempted independently of formatting success. As with the
existing host, a cleanup exception can replace an earlier exception; the code
does not claim a complete multi-error causal record. No scoped executable failure
requiring a new aggregate-error mechanism was established here.

## Readback coverage and limits

At most 18 deduplicated offsets are sampled: document first/middle/last plus
first/middle/last style interiors and adjacent boundaries. Empty styles are
skipped. Sample normalization moves only diagnostic offsets to scalar/CRLF
starts; publication spans are not silently adjusted. The host now rejects span
or selection boundaries inside surrogate pairs as well as CRLF expansion before
native formatting. Each witness covers one complete scalar/paragraph unit.

Expected color follows reverse input order (last writer wins), matching the
actual full-span application order even for overlapping or unsorted styles.
Uncovered text is checked only when a selected sample happens to land there.
The source comments correctly state this is bounded, non-exhaustive verification.
It cannot certify all 79,433 dense-fixture spans, physical glyph appearance,
unchanged non-color properties at every character, real IME behavior, selection
direction, screen-reader speech, or absence of all native side effects.

The loop remains O(number of spans), with all spans published. Native attribute
run management could still dominate. The next authoritative decision requires
unchanged hosted fixture identities, exact source/save/history checks and actual
timed phase completion on both Windows RIDs. API success and portable tests alone
must not be presented as a speedup over the retained 74–79-second baseline.

## Primary references

- [ITextRange SetFont](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextrange-setfont): reusable font duplicates and character-attribute transfer.
- [ITextFont Reset](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextfont-reset): duplicate-only undefined reset and protected S_FALSE semantics.
- [ITextFont GetDuplicate](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextfont-getduplicate): independent font object acquisition.
- [ITextRange SetRange](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextrange-setrange): endpoint retargeting without a separate selection request.
- [ITextFont SetForeColor](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextfont-setforecolor) and [GetForeColor](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextfont-getforecolor): explicit COLORREF and special-color domains.
- [ITextDocument Freeze](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextdocument-freeze) and [Unfreeze](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextdocument-unfreeze): counted screen-update suppression and balancing.
