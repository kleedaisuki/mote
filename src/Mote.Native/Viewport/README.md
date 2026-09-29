# Continuous viewport model

`ContinuousViewport` is a pure, source-backed view state wired to an **opt-in experimental** Win32/AppKit canvas. The default native text-control workflow remains unchanged; opt-in input, composition, accessibility, and semantic-paint parity are separate acceptance gates rather than implied by this model.

## Coordinate and ownership contract

- The immutable `TextSnapshot` remains the only text source. Persistent coordinates are global UTF-16 offsets. `ViewportAnchor` retains an interior source offset and a fractional intra-row pixel offset; horizontal pixels are separate state.
- No-wrap gives each logical line one implicit row. Only deviations from the base row height have nodes in `SparseHeightIndex`. A theme color repaint need not call `Reflow`; a font, DPI, tab, or wrap change does, invalidating all measurements while keeping the source anchor.
- An edit transforms the anchor with right affinity and clears measured heights. This conservative invalidation prevents stale line identities or geometry. A later layout implementation may retain unaffected measurements using versioned dependency checks.
- `GetVisibleSlices` returns at most `maxSlices` source intervals, each at most `maxSliceLength` UTF-16 units, excluding line delimiters. A 50 MiB line yields a focused window, **not** a complete line or exact global horizontal geometry. The platform shaper may read only these bounded intervals using `Snapshot.GetText(start, length)` and must explicitly request adjacent context/windows to validate shaping seams.
- These are *logical-row* slices. Glyph shaping, grapheme-aware caret stops, bidi hit-testing, soft wrapping, and precise horizontal scroll metrics remain platform-layout work. Do not use the current slices as a claim of complete Unicode geometry or exact wrapped-line positions.

## Bounded input island

`CanvasInputWindowSelector` chooses one original-source logical-line interval around a global UTF-16 caret. The interval is at most 16 Ki UTF-16 units, excludes CR/LF delimiters, and requires both edges and the caret to be extended grapheme-cluster boundaries according to .NET `StringInfo`. It examines only bounded context and conservatively throws `CanvasInputWindowBoundaryException` when a cluster or its preceding context cannot be certified inside the limit. The controller must then invalidate the previous native host binding and keep the **new** immutable snapshot visible on the canvas; silently retaining an old host would risk editing the wrong document. This is source-integrity protection, not proof of font-shaping, bidirectional hit-test, or IME parity. Global selection, document edits, undo, and persistence remain engine/controller-owned; the native host is never a whole-file mirror.

## Cost model

With `n` logical lines and `k` measured exceptions, no-wrap viewport state is `O(k)` memory, independent of `n` and logical-line length. The augmented AVL height index guarantees `O(log k)` correction prefix and pixel-to-line lookup, plus indexed rope line lookup. A visible request performs at most `maxSlices` rope line/slice queries and allocates only the returned visible list and bounded delimiter probes. A single long line does not cause work proportional to its length.

The height index's implicit baseline makes unmeasured rows exact in no-wrap mode and approximate in future variable-height mode. When a row is measured, the source anchor remains fixed, so correction above the viewport changes the global scroll number rather than jumping the glyph. The far-off scrollbar fraction can drift until measurements converge; visible local geometry must be refined before paint.

Temporary reproducible probes live in repository `.temp/viewport-probe/`: `dotnet run --project .temp/viewport-probe/Probe.csproj -c Release` checks 5,000 random height updates and 20,000 source/pixel round trips against a flat-array oracle over 20,001 lines, then requests a focused 4 Ki-unit slice in a 50 MiB unbroken line. With `-- --many-lines`, it constructs a 10,000,001-line snapshot, scrolls near EOF, and requests 900 px; the viewport operations allocated 7,816 B on the development Windows host with zero sparse nodes. These probes establish bounded model allocation, not engine peak memory or target-OS rendering. Target-OS shaping, CJK composition, accessibility, and actual painted-frame performance are **not** established by this pure model.
