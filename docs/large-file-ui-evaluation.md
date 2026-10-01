# Large-file editor surface evaluation (Windows, 2026-09-29)

## Decision

**Do not replace the current 8 Mi UTF-16-unit threshold with a full AvaloniaEdit mirror.** Its internal rope makes isolated text edits cheap, but attaching the complete document to a real editor still builds full-file line/height indexes and duplicates the engine's text representation. On this machine, a 100 MiB ASCII, line-oriented file took a median **1.24 s from view assignment to first visual-line layout**, occupied **1,444 MiB working set**, and took **42.4 ms for one off-screen middle insertion** in a Native AOT probe. A 256 KiB editor page took **9.6 ms**, **359 MiB**, and **0.2 ms** respectively. One 16 MiB line is a more severe counterexample: **3.58 s** to first visual-line layout, **2,646 MiB** working set before a single edit and **4,480 MiB** after it. These are *not* product first-paint or IME measurements.

The current page bridge protects latency and memory but does **not** satisfy one-file editing semantics: selection, Ctrl+A, clipboard, keyboard traversal and find are local to the 256 KiB AvaloniaEdit buffer. Keep bounded projection as a performance mechanism, not a user-visible page model. The product should expose a single global coordinate/selection model backed by `Mote.Engine.TextSnapshot`. For a near-term AvaloniaEdit bridge, globally implement and test the commands above and split very long logical lines into bounded display segments; manual Prev/Next is not release-quality. A custom snapshot-backed text surface is justified as the **target architecture** if the bridge cannot preserve continuous selection and navigation without proliferating edge-case glue, but should not replace AvaloniaEdit until Windows/macOS IME, grapheme selection, clipboard and accessibility are verified. Long-line layout makes a simple full-control fallback unsafe even for files below 8 MiB.

## Why this is not paradoxical

The project's `src/Mote.Desktop/MainWindow.axaml.cs` currently does `snapshot.GetText(start, length)` then `Editor.Document.Text = ...`, sets the caret, and clears the editor undo stack. Above 8 Mi UTF-16 units it projects at most 256 Ki units. This path *materializes another string* and sends it into AvaloniaEdit's own mutable document; there is no structural sharing between the engine rope and the control's rope. The [AvaloniaEdit 12.0.0 `TextDocument`](https://github.com/AvaloniaUI/AvaloniaEdit/blob/86fdebec4cff7affb0e14c7885df71d28edce777/src/AvaloniaEdit/Document/TextDocument.cs) has `Rope<char>`, a line tree, and a `Text` setter that replaces the full rope and rebuilds the line manager. Its [line tree](https://github.com/AvaloniaUI/AvaloniaEdit/blob/86fdebec4cff7affb0e14c7885df71d28edce777/src/AvaloniaEdit/Document/DocumentLineTree.cs) is an augmented red-black tree with a node per logical line. The [view's height tree](https://github.com/AvaloniaUI/AvaloniaEdit/blob/86fdebec4cff7affb0e14c7885df71d28edce777/src/AvaloniaEdit/Rendering/HeightTree.cs) also rebuilds nodes for all lines. [TextView](https://github.com/AvaloniaUI/AvaloniaEdit/blob/86fdebec4cff7affb0e14c7885df71d28edce777/src/AvaloniaEdit/Rendering/TextView.cs) constructs **visual lines for the viewport**, which is valuable, but viewport-limited visual-line count does not imply bounded document indexes or bounded shaping work for a *single enormous logical line*. Its `VisualLinesChanged` event is raised after measure, not after compositor presentation.

This aligns with industry experience that **text storage, line indexing and rendering are separate costs**: [VS Code's piece-tree work](https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation) treats large-file buffer representation and line lookup as coupled design concerns, while [Zed's rope/sum-tree account](https://zed.dev/blog/zed-decoded-rope-sumtree) uses indexed summaries and cheap snapshots. The academic frontier improves dynamic-string update/query bounds—e.g. [Lipták, Masillo and Navarro, ESA 2024](https://drops.dagstuhl.de/storage/00lipics/lipics-vol308-esa2024/LIPIcs.ESA.2024.86/LIPIcs.ESA.2024.86.pdf)—but fast string mutation alone says nothing about glyph shaping, per-line view indexes, or native input behavior. The experiment demonstrates that separation directly: at 100 MiB, isolated Native AOT `TextDocument.Insert` took about **0.01–0.03 ms**, whereas the attached GUI editor's same off-screen insertion took **42–53 ms**. It would be an unsupported inference to attribute the entire difference to one specific AvaloniaEdit callback without a CPU profile; disabling line numbers did **not** remove it (median 44 ms at 100 MiB).

## Reproducible probe

The isolated source is `.temp/large-file-ui-probe/` (intentionally excluded from the product and ignored by Git); machine-readable runs are `.cache/large-file-ui-evaluation/results-aot.jsonl`. The pre-`UndoStack.ClearAll()` control runs and earlier JIT runs are retained in adjacent `*-pre-undo-clear.jsonl` files but **are not mixed into the primary table**. The probe's 12.0.0 AvaloniaEdit package advertises source commit `86fdebec4cff7affb0e14c7885df71d28edce777` in its NuGet metadata. No `src/**` or `tests/**` file was changed for this evaluation.

Environment: Windows 10.0.26200, Intel Core i9-12900H, 32 GiB RAM, x64, .NET SDK 10.0.400 / runtime 10.0.11; Avalonia 12.1.3 and AvaloniaEdit 12.0.0. Native AOT published `win-x64`, self-contained, Release. A separate JIT run gave the same qualitative ordering, but Native AOT is the decision baseline. These tests ran on a shared development machine with other agent workloads; do not interpret small differences or disk-open times as controlled effects.

Each process creates/reuses a deterministic ASCII file under `.temp/large-file-ui-probe/workloads/`. `lines` repeats a short line-bearing pattern to the exact requested file byte count; `long` repeats `a` without newline. ASCII makes one file byte equal one UTF-16 code unit, so `100 MiB` means 104,857,600 document characters. The probe opens via `Document.OpenAsync`, materializes either the entire `TextSnapshot` or the Desktop-equivalent initial 256 KiB page, assigns `TextEditor.Document.Text`, sets caret offset zero, and calls `UndoStack.ClearAll`. `headless` substitutes a standalone `TextDocument` with no visual tree. `gui` uses a real 1000×700 Avalonia window, Fluent/AvaloniaEdit styles and line numbers. It times `OpenAsync`, `Snapshot.GetText`, synchronous assignment, the first `TextView.VisualLinesChanged` after assignment, and a one-character insertion at the *middle of the projected buffer*. It records working set, private bytes and managed heap without inducing a full GC. An off-screen edit need not produce a `VisualLinesChanged` event, so edit-to-paint is **not** reported.

```powershell
dotnet publish .temp/large-file-ui-probe/LargeFileUiProbe.csproj `
  -c Release -r win-x64 -p:PublishAot=true --self-contained true `
  -o .temp/large-file-ui-probe/publish-win-x64
.temp/large-file-ui-probe/publish-win-x64/LargeFileUiProbe.exe 100 lines full gui
.temp/large-file-ui-probe/publish-win-x64/LargeFileUiProbe.exe 100 lines page gui
.temp/large-file-ui-probe/publish-win-x64/LargeFileUiProbe.exe 16 long full gui
```

The `gui` process auto-closes after recording. Run each combination in a fresh process; the table uses **three repetitions** and their median, except the 16 MiB single-line full-control case, which was run once because it reached 4.48 GiB after one edit. `layout` is assignment-start → first *measured* visual-line set (includes assignment); it is **not a first painted frame**. `edit` is synchronous model/control callback latency, not edit-to-paint. `RSS` is process working set after initial layout and before edit, not a steady-state value after exhaustive GC. Values are rounded and reflect this host, not service-level objectives or cross-platform guarantees.

### Native AOT results: line-oriented ASCII, GUI

| File | Projection | n | `Snapshot.GetText` ms | Assignment → visual lines ms | Middle edit ms | RSS MiB |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 1 MiB | full | 3 | 1.1 | 18.6 | 0.7 | 155 |
| 1 MiB | 256 KiB page | 3 | 0.3 | 3.3 | 0.4 | 147 |
| 8 MiB | full | 3 | 10.3 | 89.2 | 4.2 | 251 |
| 8 MiB | 256 KiB page | 3 | 0.3 | 3.2 | 0.2 | 161 |
| 16 MiB | full | 3 | 17.3 | 231.5 | 8.3 | 353 |
| 16 MiB | 256 KiB page | 3 | 0.3 | 4.9 | 0.2 | 171 |
| 32 MiB | full | 3 | 32.9 | 397.0 | 19.6 | 574 |
| 32 MiB | 256 KiB page | 3 | 0.3 | 3.2 | 0.3 | 211 |
| 100 MiB | full | 3 | 93.0 | 1,241.2 | 42.4 | 1,444 |
| 100 MiB | 256 KiB page | 3 | 0.3 | 9.6 | 0.2 | 359 |

For scale of run-to-run uncertainty, the 100 MiB full-control visual-line intervals were **1,203–1,285 ms**, middle edits **42–53 ms**, and RSS **1,440–1,447 MiB**; page intervals were **8–10 ms**, **0.18–0.20 ms**, and **352–360 MiB**. Three observations do not estimate p95/p99, but the gaps are much larger than observed variability. At 100 MiB, standalone headless `TextDocument` assignment took a median **794 ms**, used **1,205 MiB**, and the same insertion took **0.01–0.03 ms**; the corresponding page values were **1.2 ms**, **222 MiB**, and about **0.02 ms**. This rules out a claim that the rope *alone* provides a low-memory full editor, while not pinpointing the GUI callback responsible for edit scaling.

### Native AOT results: one unbroken line, GUI

| File | Projection | n | Assignment → visual lines ms | Middle edit ms | RSS before / after edit MiB |
| --- | --- | ---: | ---: | ---: | ---: |
| 1 MiB | full | 3 | 252.4 | 0.6 | 284 / 453 |
| 1 MiB | 256 KiB page | 3 | 63.3 | 0.2 | 176 / 229 |
| 16 MiB | full | 1 | 3,583.1 | 12.0 | 2,646 / 4,480 |
| 16 MiB | 256 KiB page | 3 | 67.8 | 0.3 | 209 / 258 |

The long-line edit is at offset halfway through the line, far to the right of the visible horizontal viewport; the large post-edit memory rise must **not** be described as a fully painted second line. The result is still material because those allocations are already present in the real editor process after the synchronous edit. The 16 MiB full case has no repetition-based uncertainty estimate. The 256 KiB page also exceeds a 16 ms frame budget on this workload, so bounding *document size* alone is insufficient; bound the amount of a single logical line submitted to text layout as well.

## Options and release implications

| Option | Evidence-backed benefit | Semantic/performance debt | Judgment |
| --- | --- | --- | --- |
| Full `TextEditor.Document.Text` mirror | Mature AvaloniaEdit IME, clipboard and selection within one control; no page translation | Second full text/index representation; GUI assignment and edit cost grow with file size; enormous lines blow up layout/memory | **Reject as large-file default**; retain for ordinary small files only. Threshold must also consider longest line. |
| Bounded AvaloniaEdit bridge | Excellent measured buffer load/edit/memory for normal large files; keeps mature native input control | Current manual paging breaks global selection, find, Ctrl+A and keyboard continuity; 256 KiB long-line layout still costly | **Viable compatibility bridge only with a global selection/command model and bounded display-line slices.** Do not market present buttons as continuous editing. |
| Custom snapshot-backed text surface | Can request only viewport spans from the engine, avoid second full rope and bound both vertical *and horizontal* layout | IME composition, CJK, grapheme motion, accessibility, scroll anchoring and hit-testing are nontrivial; no current measured implementation | **Target architecture**, conditional on platform acceptance tests. Reuse OS/Avalonia text services; do not write a bespoke IME. |

The narrow next discriminating test is a real packaged Windows/macOS GUI workflow with global selection across a 256 KiB boundary, CJK IME composition, emoji caret/clipboard, a 1 MiB no-newline line, and first *presented* frame and edit-to-present p50/p95 traces. If a bounded AvaloniaEdit bridge can satisfy those semantics and tail targets without an expanding matrix of special cases, a custom surface can remain unnecessary. If not, the custom surface should replace page semantics rather than accumulate patches around them. A separate CPU trace of the 100 MiB attached middle insertion could locate the exact GUI callback, but would not change the decision against a full mirror at that size.
