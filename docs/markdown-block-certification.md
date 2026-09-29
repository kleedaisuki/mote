# Certified Markdown block summaries

Status: restricted fence extension implemented, 2026-09-29. This is **not** general incremental CommonMark parsing. Implementation: `src/Mote.Formats/MarkdownIncrementalSession.cs`.

## Motivation and proof boundary

Markdig's public `Markdown.Parse` takes a full string and constructs an AST ([upstream](https://github.com/xoofx/markdig/blob/main/src/Markdig/Markdown.cs)). A previous 32 MiB dense mixed fixture cost about 9 seconds and 2.14 GiB process RSS; cancellation could not interrupt its whole-document parse. Arbitrary chunking is unsound: [CommonMark fences](https://spec.commonmark.org/0.31.2/#fenced-code-blocks) may consume later lines, while [reference definitions](https://spec.commonmark.org/0.31.2/#link-reference-definitions) may affect earlier links. [Tree-sitter's incremental tree reuse](https://tree-sitter.github.io/tree-sitter/using-parsers/3-advanced-parsing.html) is useful production practice for source-range maintenance, but a changed syntax range does not prove absence of nonlocal semantic dependencies. Research on [incremental scannerless GLR parsing](https://doi.org/10.1145/3359061.3361085) likewise reports a locality/grammar-ambiguity tradeoff; it does not make Markdown references independent.

The session stores compact `(start, length, kind, heading level)` source-coordinate summaries, not a second full source string. It certifies *every* bounded candidate with the same Markdig pipeline, discards each temporary AST, and projects only requested viewport blocks. Run-level displacements shift later coordinates lazily after an edit. `Complete` means the whole file met the restricted grammar; node/token projection can still be bounded. Diagnostics are exactly zero in this grammar. Cancellation and budgets are checked before committing a new version.

## Restricted grammar

- Blocks are separated by at least one truly empty LF or CRLF line. All positions are absolute UTF-16. At most 100,000 blocks are admitted; each block and physical line is bounded by 64 Ki UTF-16 units.
- Existing flat headings/paragraphs reject reference/link/escape/entity/container/list/thematic syntax and most inline markup.
- Added fence: a column-zero opener of 3–16 matching backticks or tildes, optionally followed by an ASCII alphanumeric, hyphen or underscore info word; a later line must contain the *exact same marker* and no other characters. Interior lines, including blank lines and reference-like text, are opaque. Markdig must parse the entire candidate as exactly one source-spanned `FencedCodeBlock` before it is certified. Early, indented or longer closers, an unclosed fence, or a budget overflow trigger `Provisional` rather than a guessed result.
- A local edit inside a certified block reparses only the changed bounded slice. If it ceases to be independent, the session recertifies or returns bounded `Provisional`. A canceled call does not publish state.
- An uncached large-file `Visible` request does **not** run whole-file certification. It returns a bounded `Provisional` projection promptly; a later idle `Full` request may certify the same version, after which visible requests reuse the `Complete` index. This separates edit/open responsiveness from global validation.
- Projection covers at most 256 Ki UTF-16 units of block starts and 2,048 blocks; one intersecting whole block can extend up to 64 Ki beyond that window. Whole-file `Complete` coverage and exact diagnostic count are independent of projection size.

This subset is deliberately stricter than valid CommonMark: adjacent blocks without blanks, many valid fence variations, lists and cross-block reference links remain `Provisional` on large files. This is a coverage limit, not a validity judgment.

## Reproducible evidence

All probes are under repository-local `.temp`:

| Probe | Observation |
| --- | --- |
| `dotnet run --project .temp/MarkdownFenceProbe/MarkdownFenceProbe.csproj -c Release` | 10,000 seeded LF/CRLF multi-block cases with randomized fence interiors. 7,367 locally parseable combinations matched legacy Markdig node kind/span/name/value, diagnostics and tokens. Of those, 3,236 passed the actual certifier and its viewport projection also matched the legacy IR. Rejected cases include early closers. |
| `dotnet run --project .temp/MarkdownFenceEditProbe/MarkdownFenceEditProbe.csproj -c Release` | About 17 MiB of headings and fences: safe content edits and reference-like text inside a fence stayed `Complete` and matched legacy visible IR; an early closer or opener mutation became `Provisional`; repair restored `Complete`. |
| `dotnet run --project .temp/MarkdownFenceScaleProbe/MarkdownFenceScaleProbe.csproj -c Release -- 100 full` | File-backed 104,863,680-byte corpus: cold Full `Complete` in about 1,453 ms at 262 MiB process RSS; a near-start fence-content edit stayed `Complete` in about 1.05 ms. Single Windows x64 .NET 10 Release observation, not a p95 or GUI-frame claim. |
| `dotnet run --project .temp/MarkdownFenceScaleProbe/MarkdownFenceScaleProbe.csproj -c Release -- 100 cancel` | Cancellation requested after 5 ms surfaced after about 33 ms at about 236 MiB RSS. |
| `dotnet run --project .temp/MarkdownFenceScaleProbe/MarkdownFenceScaleProbe.csproj -c Release -- 100 visible-first` | Same file-backed corpus: cold `Visible` returned bounded `Provisional` in about 32.8 ms (8,212 UTF-16 units covered); later same-version `Full` returned `Complete` in about 1,162.5 ms. This is a single local run, not a startup p95. |

Formal regressions: `tests/Mote.Tests/MarkdownFenceAdmissionTests.cs`. The isolated `Mote.Formats` build passed with zero warnings/errors. A coordinated fresh full-reference Release run of the Markdown-fence test filter passed **5/5**, zero failures, after the Native team released its build slot; this is targeted format evidence, not a whole-solution test claim.

The current Native idle scheduler can request `Full` above 16 MiB, but these are **format-session** measurements only. A 100 MiB native UI's end-to-end status presentation, stale-result suppression and cancel/retry behavior have not been observed for this corpus. The session avoids an unbounded whole-file Markdig parse; individual bounded Markdig calls (up to 64 Ki for a certified block or 512 Ki for a fallback viewport) are not internally preemptible, so uniform cancellation latency on hostile syntax is not claimed.

## Unresolved: reference dependency index

A useful next extension would map normalized reference definitions to consumers, including links *before* their definitions. Duplicate-definition precedence, escaped and Unicode labels, shortcut/collapsed links, multiline destinations/titles, and malformed-edit recovery all matter. A tractable research slice is ASCII one-line definitions plus explicit full reference uses, with a reverse consumer index and Markdig differential tests under moves, duplicates and deletions. Until that passes, cross-block references remain `Provisional`; mere bracket scanning must not be promoted to `Complete`.

Negative probe: `dotnet run --project .temp/MarkdownReferenceProbe/MarkdownReferenceProbe.csproj -c Release`. A use `[use][id]` has **no** link node when parsed in isolation, but resolves to an earlier or later definition's destination in a whole-document parse. Duplicate definitions choose the first; an unsafe `javascript:` definition also creates a global diagnostic on the earlier use. This is direct evidence that a fence-style independent-block parse cannot simply be extended to reference links.
