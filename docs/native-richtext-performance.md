# Native Windows RichEdit semantic styling performance (2026-09-29)

## Decision

**Replace per-token `EM_EXSETSEL` / `EM_SETCHARFORMAT` styling with a one-pass RTF projection loaded through `EM_SETTEXTEX` for the bounded Windows RichEdit page.** The direct selection/format loop blocks the UI thread for hundreds of milliseconds at 100 spans and seconds at thousands. `ITextDocument::Freeze` is a useful batching facility but does not change that order of magnitude. A complete RTF document with semantic runs, passed once through RichEdit's documented RTF reader, was the only measured approach that kept 4,096 spans to tens of milliseconds on a 128 KiB page while preserving the exact displayed text and colors.

This is a **presentation replacement**, not a text edit: `Mote.Engine` remains canonical. The RTF must be built from an immutable, version-tagged page and published only if document version, page and theme still match. It must not run during active IME composition or on every keystroke; coalesce analyses and publish when input is idle. Even the best measured RTF path synchronously occupied **~16 ms** on ordinary ASCII multiline text, **~50 ms** on a single long line, and **~58 ms** on mixed Chinese/emoji text *with selection and scroll restored* for 4,096 spans. Those latter stalls are not acceptable as a guaranteed per-key hot path. Windows/macOS native acceptance testing and release-package Native AOT timing remain required.

## Mechanism and source evidence

`src/Mote.Native/Windows/WindowsEditorShell.cs` originally disables redraw, selects the full 128 KiB page for its base style, then sends `EM_EXSETSEL` and `EM_SETCHARFORMAT(SCF_SELECTION)` for every semantic span. The probe isolated those calls from parsing and the controller. For a 128 KiB *single line*, **selection alone** cost 460 ms for 100 spans; reducing `CHARFORMAT` to a color-only mask still cost ~704 ms versus ~766 ms with the current color/bold/size/face mask. Repeated selection is therefore a major bottleneck; avoiding font fields alone cannot repair it.

[Microsoft documents `ITextDocument::Freeze`](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextdocument-freeze) as disabling screen updates across multiple operations. In this experiment it improved 100-span one-line styling to ~82 ms but still took ~2.9 s for 4,096 spans. Existing `WM_SETREDRAW=0` was not equivalent: TOM freezing gave a real gain, yet still left a per-span cost. [Microsoft's `EM_SETTEXTEX` contract](https://learn.microsoft.com/en-us/windows/win32/controls/em-settextex) explicitly routes input beginning with a valid `{\rtf` sequence through RichEdit's RTF reader. [Its `SETTEXTEX` flags](https://learn.microsoft.com/en-us/windows/win32/api/richedit/ns-richedit-settextex) say `ST_DEFAULT` replaces all text and clears RichEdit's undo stack. That is compatible with the *engine-owned* undo model, provided the shell never mistakes this style publication for a user edit and Ctrl+Z continues to dispatch to the engine. It also means the control's selection/scroll and IME state cannot be assumed stable. The probe observed selection `[2000,2010] → [0,0]` and first visible line `50 → 0` on import; explicit restoration under redraw suppression recovered both exactly.

Academic work on [fast incremental PEG parsing and highlighting (Yedidia and Chong, SLE 2021)](https://people.seas.harvard.edu/~chong/abstracts/YedidiaC2021.html) improves the *analysis* side of the editor pipeline. It does not eliminate thousands of synchronous Win32 selection messages in the *presentation* side. This distinction matters here: the measured formatter cost occurred with tokens already computed, so changing the parser alone cannot fix the UI stall. A future custom viewport renderer could avoid whole-page RTF replacement, but it would have to preserve RichEdit's mature native text input and accessibility behavior; this experiment does not establish that such a renderer is ready.

## Reproduction and controls

The isolated source is `.temp/native-richtext-probe/` and raw JSONL is `.cache/native-richtext-performance/` (both intentionally kept inside the repository and ignored by Git). It uses the real `WindowsEditorShell` to create an `RICHEDIT50W` HWND, then invokes RichEdit directly; no `src/**` or `tests/**` files were edited for the probe. Because the native shell was concurrently under development, the probe was compiled with `-p:BuildProjectReferences=false` against the last built `Mote.Native` Release DLL. This evaluates the RichEdit API strategy, **not** a final integrated product build.

Environment: Windows 10.0.26200 x64, Intel Core i9-12900H, 32 GiB RAM, .NET 10.0.11, Release JIT probe. Each row uses a fresh process. The page is either one line of 131,072 `a` characters (`long`), 1,638 lines of 79 `a` characters (`lines`), or the same line count/approximate length with Chinese characters, emoji, braces, backslash and `é` (`unicode`). RichEdit exposes CRLF on retrieval; both multiline display fixtures therefore contain 132,678 UTF-16 units. Token ranges are 3 UTF-16 units every ~20 units, adjusted away from newline and surrogate boundaries. Formatting time excludes token generation, disk I/O and RTF construction; RTF build time is listed separately. The `EM_SETTEXTEX` path uses `SETTEXTEX { Flags = ST_DEFAULT, CodePage = 1200 }`, ASCII RTF with signed `\uN?` escapes for non-ASCII UTF-16 units, and color table entries for base and token colors.

```powershell
dotnet build .temp/native-richtext-probe/Probe.csproj -c Release `
  -p:BuildProjectReferences=false
dotnet .temp/native-richtext-probe/bin/Release/net10.0/Mote.Tests.dll lines full 100
dotnet .temp/native-richtext-probe/bin/Release/net10.0/Mote.Tests.dll lines freeze 4096
$env:MOTE_PROBE_RESTORE_STATE = '1'
dotnet .temp/native-richtext-probe/bin/Release/net10.0/Mote.Tests.dll unicode rtf-settext 4096
Remove-Item Env:\MOTE_PROBE_RESTORE_STATE
```

The primary numbers below are **medians of three fresh-process runs**. No confidence interval or p99 is claimed from three observations. Other development workloads were present on the machine; the order-of-magnitude differences are robust, while small differences are not a tuning target. `post-edit p95` in the final-state run is the 19th of 20 synthetic `WM_CHAR` timings at a restored caret, not a product end-to-end latency distribution. Earlier exploratory JSONL rows mislabeled the maximum of 20 timings as p95; do not use that field for a release gate. The corrected final-state runs are in `results-final.jsonl`.

### Synchronous styling cost, redraw suppressed; no state restoration

| Fixture | Spans | Current full-mask loop | TOM Freeze + loop | RTF build | RTF `EM_SETTEXTEX` |
| --- | ---: | ---: | ---: | ---: | ---: |
| 128 KiB one line | 100 | ~766 ms¹ | 81.8 ms | 1.5 ms | 6.3 ms |
| 128 KiB one line | 1,024 | not run | 811.7 ms | 2.0 ms | 10.4 ms |
| 128 KiB one line | 4,096 | >30 s in earlier shell probe | 2,925.9 ms | 2.6 ms | 29.4 ms |
| 128 KiB ASCII lines | 100 | 196.1 ms | 92.9 ms | 1.9 ms | 7.2 ms |
| 128 KiB ASCII lines | 1,024 | 1,788.1 ms | 861.4 ms | 2.4 ms | 9.0 ms |
| 128 KiB ASCII lines | 4,096 | not run | 3,215.2 ms | 3.4 ms | 13.0 ms |
| 128 KiB mixed Unicode lines | 100 | 239.0 ms | 141.7 ms | 5.0 ms | 40.8 ms |
| 128 KiB mixed Unicode lines | 4,096 | not run | not run | 7.4 ms | 52.9 ms |

¹ One direct Release-JIT run of the current selection/full-format sequence. The earlier integrated shell's 100-span result was 720.9 ms on the same one-line shape; the close agreement is useful, but these are not identical harnesses.

With **mandatory** selection/scroll restoration included in the timed RTF pass (4,096 spans), three-run medians were: one line **50.0 ms** plus **3.3 ms** RTF build; ASCII lines **16.7 ms** plus **4.1 ms** build; mixed Unicode lines **57.6 ms** plus **6.8 ms** build. Initial/last token and unstyled colors were checked via `EM_GETCHARFORMAT`, mapping post-newline display offsets to RichEdit native offsets; full `WM_GETTEXT` round-trips matched every UTF-16 code unit. `EN_CHANGE` remained suppressed during styling. In all restored-state runs, selection and first visible line matched their pre-import values. This proves those specific invariants for the fixtures, not safety during composition, external clipboard operations, or arbitrary overlapping semantic spans.

The one-pass RTF path's 20 synthetic post-style `WM_CHAR` calls at a restored caret had within-run p95 medians of approximately **17.3 ms** (one long line), **2.8 ms** (ASCII lines), and **14.9 ms** (mixed Unicode lines). The one-long-line case remains expensive for typing even after eliminating the styling loop. These samples are too few to infer a product p95; retain separate user-input-to-present tracing.

### Live-theme and Undo HWND follow-up (2026-09-30)

The current default Windows shell was exercised in a disposable, real `RICHEDIT50W` HWND by `.temp/WindowsThemeUndoProbe/` on Windows 11 10.0.26200 x64, .NET SDK 10.0.400, source checkout `2670ba2` plus the uncommitted live-theme Windows patch. Build and run, without rebuilding project references:

```powershell
dotnet build .temp/WindowsThemeUndoProbe/Probe.csproj -c Release -p:BuildProjectReferences=false -v:q
dotnet .temp/WindowsThemeUndoProbe/bin/Release/net10.0-windows/Mote.Tests.dll
```

Raw output is in `.cache/native-richtext-performance/theme-undo-20260930.txt`. The probe uses an in-memory `Mote.Engine.Document`, routes native committed edits and both native shell Undo commands through that engine, publishes version-matched semantic tokens, then invokes `SetTheme(light)` and `SetTheme(dark)` so the one-pass RTF path is actually eligible. It uses `TranslateAcceleratorW` with a temporary, restored Ctrl keyboard state for the Ctrl+Z route, and sends the Edit-menu command ID for the menu route. This is an actual HWND and engine transaction test, not a physical-keyboard or full `NativeEditorController`/disk-save test.

Two edits followed by the two distinct engine Undo routes restored the **exact original source** in both the engine snapshot and RichEdit's CRLF-projected text. Before/after each pair of live palette switches, `EM_EXGETSEL` remained `[800,805]`, `EM_GETFIRSTVISIBLELINE` remained `53`, and all **3,838 UTF-16 bytes** of the displayed page matched. The 120-line fixture reported `EM_GETLINECOUNT=120` and allowed `EM_GETFIRSTVISIBLELINE=119` after an explicit large scroll, so line 53 was not a scroll clamp. The selection was placed **before** sampling the scroll baseline: an earlier draft incorrectly sampled first line `76` and then moved the selection offscreen, which itself changed first line to `14`; that was a probe error, not a theme regression.

`EM_CANUNDO` nevertheless returned `1` after semantic RTF publication. A deliberately direct `EM_UNDO` on a disposable third edit returned `1` and changed `EM_CANUNDO` to `0`, but did **not** change either engine or native text and did not change the sampled token color (`0xDFDAD8`). Thus `EM_CANUNDO` alone cannot establish that the user can undo a text edit outside the engine. Synthetic keyboard-position `WM_CONTEXTMENU` and right-button messages produced neither a popup-menu window nor `GUI_INMENUMODE` in this probe; the shell's Edit menu and Ctrl+Z are wired to engine events. A physical mouse, assistive technology, and other native-command ingress were not exercised, so do not generalize this to every conceivable context-menu route. If a native Undo action ever becomes user-reachable, route it to engine Undo rather than relying on native stack state; do not replace the measured one-pass RTF styling with per-token formatting on the basis of `EM_CANUNDO` alone.

## Integration choice and failure boundaries

| Approach | Measured result | Judgment |
| --- | --- | --- |
| Repeated `EM_EXSETSEL` + full `CHARFORMAT` | Hundreds of ms at 100 spans; seconds at 1,024+ | **Remove from semantic-publish path.** Color-only format does not solve selection cost. |
| TOM `Freeze` / `Unfreeze` around repeated selections | 2–3× improvement for some fixtures, but ~3 s at 4,096 spans | Useful for small incidental batches, **not** the primary highlighter. |
| `EM_SETTEXTEX` with one RTF document | Fastest measured; exact text/colors/state after explicit restoration; 16–58 ms for 4,096 spans including restoration | **Implement as coalesced, versioned page publication**, not synchronous per-key work. |
| `EM_STREAMIN` | The isolated managed callback probe crashed before its callback, even on a tiny RTF sample | Inconclusive interop failure; **do not claim the Windows API itself is slow or unsafe**. No reason to prefer it over working `EM_SETTEXTEX` here. |
| Viewport owner-draw overlay / windowless RichEdit host | No measured implementation | Defer for this release. [Windowless RichEdit](https://learn.microsoft.com/en-us/windows/win32/controls/about-windowless-rich-edit-controls) is a supported host model, but text metrics, scroll, selection, IME and accessibility synchronization make it a distinct surface project. [`EM_FORMATRANGE`](https://learn.microsoft.com/en-us/windows/win32/controls/em-formatrange) is a print/device-formatting API, not a drop-in interactive syntax-overlay API. |

The implementation contract should be:

1. Build RTF off the UI thread from one immutable displayed-page snapshot, its semantic tokens and theme. Escape `\`, `{`, `}`, newlines and every non-ASCII UTF-16 unit; use an explicit color/font table. Flatten overlapping tokens by policy-defined priority into disjoint styled runs, rather than assuming the parser yields disjoint spans. Never insert document text into RTF control syntax unescaped.
2. Before applying, check document version, page start/length, format policy and theme generation. If any changed, discard the prepared RTF. Coalesce bursts and **defer while IME composition is active**; replacing the entire RichEdit buffer mid-composition remains untested and could destroy uncommitted input.
3. On the UI thread, save native selection and first visible line, suppress `EN_CHANGE` and redraw, call `EM_SETTEXTEX`, verify its success return, restore selection/scroll, then re-enable notifications/redraw in `finally`. Product tests must verify `WM_GETTEXT`/`EM_GETTEXTEX` still equals the engine's displayed CRLF projection and ensure no phantom engine edit. `ST_DEFAULT` discards RichEdit's undo stack; engine undo is the only authoritative history.
4. Never promise a zero-stall 128 KiB page based on the ASCII result: mixed Unicode already took ~58 ms and a long line ~50 ms in the same API path. If this remains visible in user tracing, reduce work per publication or page size, and measure the trade-off against navigation/selection semantics. A custom surface or viewport overlay is a later architecture choice only if the bounded RichEdit bridge cannot meet both responsiveness and native-input correctness.
