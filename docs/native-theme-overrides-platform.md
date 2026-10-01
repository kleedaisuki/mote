# Native theme overrides: platform integration evidence

## Menu contract

- Windows adds **File > Reload Settings** (command ID 217).
- macOS adds **mote > Reload Settings** (`moteReloadSettings:`).
- Both publish `INativeEditorShell.ReloadSettingsRequested`. Neither adds an accelerator or changes established shortcuts.
- macOS uses exception-safe `Notify`, deliberately not `NotifyAfterComposition`: reload must not force marked IME text to commit. The controller owns coalescing and natural composition settlement.

## Windows formatting-history correction

An initial actual-HWND test established a concrete defect: `SetAllEditorColor(SCF_ALL)` appended a RichEdit native formatting undo unit during a theme update. A single `EM_UNDO` then undid palette formatting rather than the prior user text edit. Although mote routes normal undo through engine history, this native assertion was retained and the defect was fixed, not waived.

`WindowsRichEditUndoScope` now obtains `IRichEditOle` with documented `EM_GETOLEINTERFACE`, queries `ITextDocument`, and directly calls `Undo(tomSuspend)` / `Undo(tomResume)` using the native vtable. It uses no reflection-based COM wrappers and no shipped library. A per-input-control nesting depth ensures only the outer formatting transaction suspends and only the final lease resumes; idempotent lease disposal and `finally` release balance exceptions. Interface acquisition fails before palette, font, or brush mutation if TOM is unavailable. No history-clearing `tomFalse`, `EM_EMPTYUNDOBUFFER`, or undo-limit reset is used.

Palette updates, plain source recoloring, and semantic recoloring are scoped. Semantic RTF reimport also uses `ST_KEEPUNDO`: its former `ST_DEFAULT` flag explicitly deleted native history, independently of undo-recording suspension. Source document replacement remains unchanged; this helper is confined to formatting transactions, not source editing.

### ABI evidence

The installed Windows SDK `10.0.26100.0`, `um/TOM.h`, confirms:

- `IID_ITextDocument = 8CC497C0-A1DF-11CE-8098-00AA0047BE5D`.
- `ITextDocument` inherits `IDispatch`: slots 0–6 precede its methods; `Undo` is slot **22**.
- Signature: `HRESULT Undo(long Count, long *pCount)` (Windows long is 32-bit).
- `tomSuspend = -9999995`; `tomResume = -9999994`.
- `um/Richedit.h`: `EM_GETOLEINTERFACE = WM_USER + 60`, `ST_KEEPUNDO = 1`.

The same definitions are in [Microsoft's win32metadata TOM.h](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/TOM.h). The initially provided source.dot.net generated-file URL returned 404; the SDK and upstream header were used instead. [EM_GETOLEINTERFACE documentation](https://learn.microsoft.com/en-us/windows/win32/controls/em-getoleinterface) specifies ownership of the returned reference. [SETTEXTEX documentation](https://learn.microsoft.com/en-us/windows/win32/api/richedit/ns-richedit-settextex) distinguishes history-deleting `ST_DEFAULT` from `ST_KEEPUNDO`. [ITextDocument::Undo documentation](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextdocument-undo) provides the method signature; the suspend/resume constants are checked against the actual header rather than inferred from its shorter web description.

## Verification (Windows, 2026-09-30)

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --filter 'FullyQualifiedName~NativeThemeOverrideWindowsTests|FullyQualifiedName~WindowsFlowRtfTests' --no-restore
dotnet build src/Mote.Native/Mote.Native.csproj -c Release --no-restore -warnaserror
```

Result: **12/12 passed** (129 ms reported test duration), comprising **3** new platform cases and **9** existing Flow compatibility cases. Native build: **0 warnings, 0 errors**.

New tests create actual hidden HWNDs using OS `msftedit.dll`, without showing windows, changing focus, writing the clipboard, injecting keyboard events, or modifying OS settings. They verify:

1. Real installed menu label through `GetMenuStringW`; shell command dispatch publishes exactly one reload request.
2. Validated composition retains `mote-dark` ID while changing `preview.background` to `#101820`; actual RichEdit background readback matches. `EM_SETBKGNDCOLOR` returns the previous color; writing the same expected value avoids a temporary probe color.
3. UTF-16 source text (Chinese and a surrogate-pair emoji) and native selection remain unchanged in both plain and matching-semantic RTF palette paths.
4. Native **one-step EM_UNDO restores the preceding text edit**, not formatting. A second palette transaction preserves the existing redo branch, and **EM_REDO restores the text edit**.
5. Nested exceptional formatting leases resume recording: a subsequent native text replacement can still be undone in one step.
6. Engine snapshot identity and engine-owned undo/redo history remain intact through the established `UndoRequested` route.
7. A non-RichEdit HWND with no TOM interface rejects palette application before theme identity, fonts, or brush change.

The hidden fixture uses no real controller/native notification loop. Before testing a palette change on the native redo branch, it explicitly invalidates the previous semantic analysis, as a real text edit does; it does not authorize a stale same-text projection.

## Limits

macOS menu registration and exception-safe dispatch compile in the shared Native project on Windows. This is **not** runtime AppKit or macOS AOT evidence. Four-RID AOT verification remains for CI. No OS theme/high-contrast settings were changed. No claim is made about arbitrary externally initiated TOM suspension; the helper's nested leases own mote's formatting scope only.
