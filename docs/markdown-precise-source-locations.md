# Markdown inline source-coordinate correction

## Scope and mechanism

The shared `MarkdownPolicy.Pipeline` now enables Markdig 1.3.2's supported
`UsePreciseSourceLocation()` after the existing `DisableHtml()`. Upstream implements
this option by setting `pipeline.PreciseSourceLocation = true`; it does not register
a grammar extension. See the [pinned upstream implementation](https://github.com/xoofx/markdig/blob/1.3.2/src/Markdig/MarkdownExtensions.cs).

This fixes the independent coordinate defect found in
[markdown-reference-index-design.md](markdown-reference-index-design.md). It is
**not** an implementation of the proposed reference dependency index. Definitions
still use whole-document Markdig semantics wherever the existing session admits a
complete parse; unsupported large-file reference context remains provisional.

All public coordinates remain half-open UTF-16 ranges. The existing inclusive
Markdig-to-mote conversion, projection structure, URL sanitation, duplicate winner
rules, formatting policy and parser version are unchanged. Full parsing, bounded
parsing and HTML rendering share the same pipeline. No Engine, Native or session
code is modified by this fix.

## Before/after evidence and compatibility

New tests were run against the old pipeline before the fix: six failed, one passed.
A LF fixture with a heading containing a non-BMP emoji, 300-character preceding
paragraph, and definitions after consumers independently expects reference ranges
`(323,14)` and `(351,11)`. The old policy returned `(0,1)` for both. The precise
pipeline returns the independently located full source atoms. Other inline ranges
(emphasis, inline code, direct links and images) also receive correct delimiter-
inclusive positions; preserving their known-wrong positions is not compatibility.

The compatibility boundary is unchanged syntax, semantic values and rendering,
not incorrect coordinates. A safe mixed corpus (CRLF, emoji, quote, list, image,
autolink, fence, reference title and raw HTML) produces exactly the old HTML-disabled
pipeline's HTML. Unsafe reference targets remain sanitized, warning multiplicity
belongs to resolved consumers, and a safe first definition defeats an unsafe
duplicate. Formatting remains conservative, render-equivalent and idempotent.

The native Markdown preview currently maps a displayed paragraph to its physical
paragraph source span, not to individual inline link spans. A dedicated preview
test proves that `Lead use tail.` maps to `Lead [use][id] tail.` after a CRLF/emoji
heading. This correction therefore does not silently introduce inline link
activation or alter native preview navigation granularity. Synthetic definition
groups are still represented according to the existing whole-document projection;
their treatment is outside this fix.

## Verification

Environment: Windows x64, .NET SDK 10.0.400, `net10.0`, Release, Markdig 1.3.2.

`tests/Mote.Tests/MarkdownPreciseSourceTests.cs` contains independent ordinal
source-string oracles, not a comparison of two parses using the same wrong source
positions. Coverage includes forward/backward references, case-variant duplicate
winners, safe/unsafe destinations, two distant uses, nested emphasis inside a
reference, CRLF/LF, non-BMP UTF-16 offsets, direct links/images/code/emphasis,
complete-session absolute positions, native paragraph source maps, raw HTML and
formatting compatibility.

Commands actually run:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --filter FullyQualifiedName~Markdown --logger 'console;verbosity=minimal'
dotnet build src/Mote.Formats/Mote.Formats.csproj -c Release -warnaserror
dotnet build src/Mote.Native/Mote.Native.csproj -c Release -warnaserror
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release -warnaserror --filter FullyQualifiedName~MarkdownPreciseSourceTests --logger 'console;verbosity=minimal'
```

Results: all 44 Markdown-filtered tests passed (including existing versioned local
edit, cancellation, adjacency/fence certification and admission checks). After
adding the rendering-compatibility test, the focused suite passed 8/8 with warnings
treated as errors. Formats and Native builds each reported zero warnings/errors.
No target macOS or Native AOT performance claim follows from these managed tests.

## Bounded parser cost probe

Scratch artifacts live in `.temp/MarkdownPrecisePerformance/`: `Probe.csproj`,
`Program.cs` and `results.jsonl`. Reproduce with:

```powershell
dotnet run --project .temp/MarkdownPrecisePerformance/Probe.csproj -c Release
```

The probe compares old/precise HTML-disabled pipelines on the same immutable source,
asserts identical HTML, warms both for six iterations, and records 15 paired parses
with alternating order. Allocation uses `GC.GetAllocatedBytesForCurrentThread()`;
elapsed time excludes corpus construction and HTML rendering. The parser is
synchronous. Figures are exploratory local medians, not a performance SLA.

| Corpus | UTF-16 length | Old median parse | Precise median parse | Old allocated bytes | Precise allocated bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| Headings + 4,096-character plain paragraphs | 1,048,333 | 5.2109 ms | 5.2556 ms | 191,072 | 197,296 |
| Dense emoji/emphasis/code/reference/direct-link paragraphs | 1,048,557 | 107.1851 ms | 89.7733 ms | 40,134,144 | 40,656,656 |

The measurable allocation increase is 6,224 bytes (plain) and 522,512 bytes (dense,
approximately 1.30%). This is a correctness cost accepted for accurate diagnostics
and highlighting; neither changing to a second parser nor preserving wrong spans
is justified. Elapsed times do not demonstrate a material regression in this probe;
the apparent dense speedup is not claimed as an optimization because GC/tiering
and machine contention were not isolated. This is not startup, painting, input
latency, peak-resident-memory or 100 MiB retained-cache evidence. Existing large-file
admission and bounded parsing limits remain necessary and unchanged.
