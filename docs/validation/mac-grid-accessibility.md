# macOS bounded CSV Grid accessibility implementation and evidence

Date: 2026-10-01. **Experimental implementation, not external AX, native ABI,
VoiceOver or release acceptance.** Production default remains unchanged.

## Installation and scope

Set `MOTE_NATIVE_GRID_ACCESSIBILITY=1` before starting the process to register the
Grid-specific selectors. The environment gate is process-wide and evaluated
before Objective-C class registration. Without it, the established native Table
and source provider retain their previous behavior.

The native-first implementation preserves `NSTableView` rendering, native focus,
keyboard and context-menu vocabulary. Its narrow selectors return a bounded
window-specific set of `NSAccessibilityElement` rows, columns, ordinal headers
and cells. These are accessibility metadata wrappers, not replacement rendering
views or source text proxies. This is the first candidate to test externally;
there is **no external negative result proving a replacement proxy Table is
necessary**, and no claim that native/default AX merging has passed.

Files:

- `src/Mote.Native/Mac/MacCsvGrid.Accessibility.cs`: bounded selectors,
  registration, geometry, selection admission, focus and wrapper lifetime.
- `src/Mote.Native/Mac/MacCsvGrid.cs`: coherent installation/selection hooks and
  focus-aware keyboard routing, with the existing adapter as selection owner.
- `src/Mote.Native/Mac/MacEditorShell.cs`: supplies existing `IsTextComposing` to
  the optional accessibility focus admission guard.
- `src/Mote.Native/Mac/MacCsvGridAccessibilityProbe.cs`: native in-process
  regression checks reached through the existing Mac CSV diagnostic.
- `tests/Mote.Tests/MacGridAccessibility*Tests.cs`: portable fixture construction,
  native aggregate layout and pure clipped geometry checks.

No source accessibility files, Engine/Formats public interfaces, source input
island ownership, source tree registration or clipboard implementation changed.
No Grid AX press, editable value, Reveal, Copy or Replace action is advertised;
those await the acknowledged controller command gate. Existing physical keyboard
and menu commands remain available and carry established ready identities.

## Implemented semantics

| Surface | Implemented behavior |
| --- | --- |
| Table | Native `AXTable`; bounded local row/column counts; exact local cell lookup and out-of-range nil. Exact-empty is zero rows/columns; unknown extent is not treated as empty. |
| Nodes | Window-specific rows/columns/cells and absolute one-based CSV labels. Cell ranges are pointer-sized `{local,1}`, not absolute file ordinals. |
| Values | Shared immutable frame supplies Complete/empty, Missing, Pending, Clipped, Oversized, syntax-error and sanitized-display facts. Only bounded Complete/Clipped presentation has a value. No parser, worker wait or source decoding in a callback. |
| Sparse delivery | Missing ready row retains its requested Pending slot; later rows do not move earlier in the Table. |
| Selection | `AXSelectedCells` enumerates only the window intersection. The full retained rectangle stays adapter-owned. Selected native row is not falsely advertised as whole-row selection. Native row/column setters are refused. |
| Setter | Count checked before allocation, maximum 8192 entries; every node validated against the current owner/window; duplicate entries deduplicated by shared validation; sparse/mixed-retired sets rejected atomically. Nil/empty clears all retained selection, including off-window endpoints. |
| Selection notification | Published selection change notifies `AXSelectedCellsChanged`; subsequent controller `Select` notification only cancels an old asynchronous Grid Copy, never mutates source. |
| Focus | Physical composition blocks source-to-Grid transfer. Native `makeFirstResponder:` and readback must succeed after reentrancy. Explicit Table focus differs from retained cell selection. Pending cells refuse cell focus; Table remains focusable. |
| Keyboard coherence | Explicit cell focus does not change the retained rectangle. Return/Command-Return use the focused ready cell, while Copy remains selection-based. Physical arrows originate at the focused cell, then become normal adapter-owned selection. Pointer/native selection clears obsolete focus overrides. |
| Geometry | Native cell/row/column layout clipped to actual visible bounds; native header geometry from `NSTableHeaderView`. Window-to-screen conversion preserves AppKit coordinates. A row ordinal header without a painted gutter has empty geometry rather than a fabricated hit area. |
| Hit test | Native row/column lookup yields one cell candidate; actual clipped bounds verify it. Column-header fallback is capped at 64. No per-file enumeration. |
| Events | Tree publication uses `AXLayoutChanged`; selection/focus events are sent only for relevant transitions after publication. Reentrant supersession does not produce a late successful mutation. |
| Lifetime | Installation, clear, navigation replacement and theme retirement remove all node lookups before releasing owned handles. Externally retained old cells return nil/empty ranges, cannot acquire a new coordinate, and retain no managed projection. |
| Threading | Callback entry checks `NSThread.isMainThread` before touching AppKit objects or mutable managed lookups. Unexpected off-main dispatch refuses; actual external dispatch remains an acceptance gate. |

Cells are created lazily by local lookup, row/selection enumeration, visible-cell
enumeration or focused/point queries; merely installing a large admitted window
does not create every cell. Visibility enumeration skips clipped rows and creates
only intersecting cells. Rows/columns and ordinal metadata headers are bounded
and created at installation. Wrapper count is bounded by admitted cells plus two nodes per row/column, never
file row count, previous window history or ready delivery compaction. Layout
queries may call bounded native AppKit methods but never source analysis.

## Portable evidence actually obtained

Environment: repository `D:\Code\mote`, Windows development host, .NET 10 project.
Native Mac runtime cannot be loaded here. Artifacts remain in the repository.

```powershell
dotnet build src/Mote.Native/Mote.Native.csproj --no-restore -v:q
dotnet test tests/Mote.Tests/Mote.Tests.csproj --no-restore `
  --filter 'FullyQualifiedName~MacGridAccessibility' -v:q
```

- Native managed build succeeded with **0 warnings / 0 errors** after Mac
  integration (2026-10-01).
- Focused portable tests: **8 passed, 0 failed, 0 skipped**. This includes
  constructor-valid ordered source origins for the sparse target fixture,
  Missing outside proved row width, Oversized with an actual field origin,
  NSRange/CGPoint/CGSize size 16 bytes and CGRect size 32 bytes, clipped
  intersections/negative coordinates/edge touching/nonfinite refusal.
- `git diff --check` for the owned Mac area passed (line-ending notices are not
  whitespace errors).

These facts certify neither callback calling convention nor AX framework
behavior. In particular, correct managed struct size is **not** a native ABI test.

## Native target probe, prepared but not executed here

Run on each freshly published strict single-binary AOT Mac RID:

```sh
MOTE_NATIVE_GRID_ACCESSIBILITY=1 ./mote --check-native-mac-csv-grid
```

Required additional marker:

```text
Mac CSV Grid AX selector probe passed; external AX/VoiceOver/geometry gates remain untested.
```

The probe uses an in-memory constructor-valid descriptor fixture, no source
document mutation or clipboard publication. It verifies local counts at
Row 1001 / Column 17, actual NSRange selector returns, sparse Pending slot,
Complete empty versus Missing/Oversized, bounded selection setter, sparse and
8193-entry refusal, native row-setter bypass refusal, nil clear, composition
focus refusal, native focus/readback and Table sentinel, focused-cell keyboard
command/arrow routing, retained-cell retirement and mixed-old-array rejection.
Its command callback only records intents; it does not execute source Reveal or
Replace. Its native key events target the probe table, not global input.

Classification now: **blocked / not run on this Windows host**, not product-pass
and not product-fail. Target results must record commit, binary SHA-256, OS/RID,
PID/window, scale and logs under `.temp/` or `.cache/` before closing this gate.

## Remaining external acceptance gates

1. Execute the native probe on **osx-x64 and osx-arm64** published AOT binaries.
   Test actual NSInteger/NSRange/CGRect/CGPoint method encodings, stret versus
   direct aggregate returns, BOOL callbacks and retained-client release.
2. Cross-process AX client: verify exactly one native semantic Table, parent,
   rows/columns/headers, selected cells/setters, focused element and point lookup.
   Confirm no recycled `NSTextField` or default native row-selection tree is
   duplicated alongside the bounded semantics. A duplicate/merge failure is the
   evidence needed to choose a replacement bounded Table proxy; do not infer it.
3. Actual resize/Retina/multi-monitor/scroll clipping and native header point
   lookup, including absence of false hits outside the Table. Portable rectangle
   arithmetic alone does not close this gate.
4. Retain cells across ready/pending navigation, theme, document edit/Undo/New
   and close; confirm unavailable old nodes and live unchanged source provider.
5. External 1000-query timing/memory evidence during indexing/navigation/close;
   main-thread callback evidence and no source decode/worker waits.
6. Real Pinyin composition and source regression protocol, followed by attended
   VoiceOver speech/navigation tasks. Synthetic refusal and native key events are
   not real IME or screen-reader acceptance.

Until these gates pass, keep the opt-in gate. The implementation does not claim
production Grid accessibility, arbitrary whole-file virtual lookup, acknowledged
source-command actions or reader speech usability.

## Primary-source rationale

Apple documents the [required Table surface](https://developer.apple.com/documentation/appkit/nsaccessibilitytable),
[selected cells](https://developer.apple.com/documentation/appkit/nsaccessibility-c.protocol/accessibilityselectedcells?language=objc)
and [visible cells](https://developer.apple.com/documentation/appkit/nsaccessibility-c.protocol/accessibilityvisiblecells?language=objc).
Overriding information getters provides read-only access unless the setter is
also exposed; only the validated selected-cell and focus setters are added here.
The repository [bounded Grid contract](../csv-grid-accessibility-contract.md)
supplies the extent/state/command and independent source-provider requirements.
Its cached-frame industry precedent and reader-feedback research motivate the
external orientation/notification tests, but are not substituted for target
evidence.
