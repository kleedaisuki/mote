# Windows full-native source: dense semantic publication timeout

Date: 2026-10-02. Status: **hosted baseline attribution and bounded next-step
proposal; no optimization implemented or speedup claimed**.

## Decision

Keep the ordinary single-native-source direction under investigation, but do not
promote the current diagnostic adapter. Its dense structured-text attribute
publication demonstrably blocks for approximately 74–79 seconds on a 512 KiB
JSON fixture. This is an ordinary structured-document problem, not justification
for another 100 MiB parser specialization.

The simplest next discriminating implementation is **attribute-only publication
through a non-selection TOM `ITextRange`**, with balanced document freezing and
checked foreground-only formatting. It needs separate implementation ownership,
ABI review and hosted measurements. Merely increasing the 120-second timeout,
reducing the fixture, dropping tokens or silently importing whole-source RTF
would not answer the current experiment.

## 1. Frozen source and artifact identities

- Commit: `23f1c1a9bc01f198add7e44df8b026fe1955f923`.
- Workflow: [CI 36887820181](https://github.com/kleedaisuki/mote/actions/runs/36887820181).
- Reused local downloaded evidence root:
  `.cache/ci-36887820181-codec-supervisor/`.
- Relevant frozen source:
  `src/Mote.Native/Windows/WindowsNativeSourceCapabilityProbe.cs`,
  `src/Mote.Native/NativeSourceCapabilityProbe.cs`, and the workflow supervisor.
  Concurrent checkout edits are not the source attributed to this run.
- No GUI, native control, input event, benchmark replay, CI dispatch or production
  edit was performed during this investigation. Only retained artifacts and
  source were inspected. This note is the sole authored project file.

The executable identity in the first report row equals the supervisor's
`binary_sha256_before` on each RID:

| RID | Native AOT executable SHA-256 |
| --- | --- |
| win-x64 | `12235120D6257EAE4BD4D3B8BCC54CA2F7819F17B29E925D51E15D0133DDC181` |
| win-arm64 | `4FAF083267D6B66C7D4A66782F01DC86905A08F127350652ECCBBCE46CF0D27A` |

Each `source-capability/probe/report.jsonl` has **247 valid rows and 49,805
bytes**. Its per-fixture row counts are probe 2, mixed-text 99, novel-text 99,
dense-json 47.

| RID | `report.jsonl` SHA-256 | `source-capability/supervisor.json` SHA-256 |
| --- | --- | --- |
| win-x64 | `77B69C991E11B47DAF4722D7E974661A91F9C68A298FF6711070B8F3E4A814C9` | `2E12C42E365129C2D2DB631CEEEA6B9ED268568418F348BB6226A14DA97C81E8` |
| win-arm64 | `EAB1D1B23E6040D5544E8EC3FE9D937F2798C8746C435061AF3188E201C702B8` | `2090621DCADA30BCD6D3804001EF5A24C3537E0A2E959BB86E07DD8E3ABF10F5` |

Both supervisors record actual `timed_out=true`, `cleanup_forced=true`,
`terminated=true`, observed process exit `-1`, `process_boundary=censored`,
status unknown, error class timeout and complete stdout/stderr drains. Those two
streams are empty. `binary_sha256_after=null`,
`capability_report_complete=false` and `trace_drain_witness=unobserved` do not
constitute success. Descendant ownership is explicitly not certified.

The workflow's 120,000 ms wait is a **whole probe process limit**, not a
120-second measurement of the last semantic phase.

## 2. Completed ordinary journeys must not be erased by the timeout

Both RIDs completed mixed-text and novel-text through initial publication,
one controlled insertion, exact one-version/one-mutation engine reconciliation,
post-edit publication, engine Undo and Redo with reimport/publication, exact
Save to a new path and fresh `Document.OpenAsync` reopening.

| Fixture | Input UTF-8 bytes | Source UTF-16 units | Semantic tokens | Saved UTF-8 bytes |
| --- | ---: | ---: | ---: | ---: |
| mixed-text | 2,875 | 1,219 | 0 | 2,882 |
| novel-text | 3,711,959 | 1,278,983 | 0 | 3,711,966 |
| dense-json | 524,288 | 484,573 | 79,433 | Not reached |

All input identities and the two completed saved-file identities are identical
across architectures:

| File | SHA-256 |
| --- | --- |
| `mixed-text.input` | `2EA48A1A0E8EBB3064C236083657A29857586268E654DABE719F1E995C8CEBD6` |
| `mixed-text.saved` | `DEC8D3B46CA37A972BC57B95DB877FC37B195768E00B9022EC2234C035F248C7` |
| `novel-text.input` | `CDFF59D22B1AF5595A049D9324212389ED2E534A173CB47268349DA0BCD8AE19` |
| `novel-text.saved` | `FD482B8375A39034BA92A5E3E3898E97CDC9BDA06ACDE470A61899FEE3FD55CF` |
| `dense-json.input` | `B16CAA47A0D80AC14D873A2011E2360ABA7C877DD56FB90C9C99C3CE67BE4257` |

These are controlled, synthetic capability journeys. They do not certify real
typing, CJK composition, physical paint, screen-reader speech, general editing
ingress or the default product surface. Reopening means a fresh Document object,
not a fresh GUI process.

## 3. Dense JSON: first publication succeeds very slowly; second is censored

Do not describe the initial dense publication as an entered-only timeout. The
raw report contains its completed row on **both** architectures, followed by
successful controlled insertion and engine reconciliation. The final row is the
**second, post-edit** semantic publication's entered row, without a terminal row.

| Dense JSON phase | win-x64 ms | win-arm64 ms |
| --- | ---: | ---: |
| Engine open | 1.7995 | 1.7950 |
| Initial projection | 3.9177 | 3.0275 |
| Native initial import | 636.3036 | 502.8205 |
| Full native readback | 3.4847 | 2.6587 |
| Scroll/draw/restore | 559.7634 | 469.4863 |
| Initial whole-policy analysis | 30.7468 | 30.6739 |
| Initial all-token projection | 4.7301 | 4.1383 |
| **Initial verified attribute publication** | **78,852.2383** | **73,852.4120** |
| Subsequent draw return | 17.5673 | 15.7851 |
| Controlled insertion, including adapter readback | 7.8883 | 5.3791 |
| Post-edit complete readback | 4.4426 | 2.8331 |
| Engine reconciliation/map/diff | 10.0934 | 7.7649 |
| Post-edit whole-policy analysis | 32.1211 | 43.1022 |
| Post-edit all-token projection | 5.5208 | 3.0819 |
| **Second verified attribute publication** | **Entered; censored** | **Entered; censored** |

Both completed analyses report 79,433 tokens and zero diagnostics. Initial
publication includes full text/selection/viewport/engine-history preservation
checks; subsequent `semantic-counts=complete` is also present. The controlled
edit records exactly one engine mutation and one version advance.

Initial style publication's approximate process-wide managed allocation deltas
are 8,901,368 B (x64) and 8,915,232 B (ARM64), not peak native memory. Its ending
working-set samples are 247,156,736 B and 146,833,408 B, not comparable controlled
memory distributions or peak values.

Dense JSON Undo, Redo, Save and fresh reopening are **not reached**. The final
phase has no duration, so no numeric second-pass latency, CPU attribution,
deadlock conclusion or precise completion progress is warranted. The sum of all
completed phase timers before censoring is 96,804.3876 ms (x64) and 88,919.0819 ms
(ARM64), excluding entered/terminal reporting, verification outside timers,
initial process work and the incomplete phase. Subtracting these sums from 120 s
would not produce a valid last-phase duration.

This is one observed run per RID, not a repeated latency baseline, percentile,
confidence interval, cross-architecture speed ranking or before/after comparison.
The severity and the completed phase attribution are nevertheless sufficient to
reject this publication loop as a product implementation.

## 4. Mechanism: presentation, not slow JSON parsing

The frozen Windows `PublishStyles` implementation:

1. Validates every style span, snapshots native selection/viewport and builds one
   `RichEditOffsetMap`.
2. Disables redraw with `WM_SETREDRAW` and resets the whole document foreground.
3. For every nonempty style, maps its endpoints, sends `EM_EXSETSEL`, then sends
   `EM_SETCHARFORMAT(SCF_SELECTION | SCF_NOKBUPDATE)` with only `CFM_COLOR`.
4. Restores native selection/viewport, reenables redraw, and verifies complete
   text, selection, viewport and absence of native undo/redo. The common caller
   performs additional engine/native preservation checks.

Thus the dense fixture requests roughly 158,866 selection/format messages per
publication, besides reset/restoration/checks. It repeatedly uses the **visible
selection** as a formatting cursor despite painting being suppressed. Managed
endpoint mapping uses one newline index and binary searches, not a whole-source
scan for each token. The wrapper has no timing inside this loop, so this run
alone cannot assign its 74–79 s exclusively to `EM_EXSETSEL`, native style-run
splitting, keyboard-layout work, native layout, COM/marshalling or another
specific internal RichEdit activity.

Relevant existing evidence was reused rather than rediscovered:
[native RichEdit performance](../native-richtext-performance.md) records a
128 KiB selection-only experiment at approximately 460 ms for 100 spans. A
TOM Freeze **around the same selection loop** still took approximately 2.9–3.2 s
for 4,096 spans. Those are older Release-JIT/local fixtures, not measurements of
this AOT binary, but strongly support repeated selection as a bottleneck
hypothesis. The old source confirms that Freeze experiment still called
`EM_EXSETSEL` for every span; it did not test a non-selection TOM range.

The current artifact directly establishes that all-token presentation is orders
of magnitude slower than its approximately 31 ms initial whole-policy analysis.
Parser optimization cannot remove those already-computed token publications.
[Yedidia and Chong's SLE 2021 incremental PEG research](https://people.seas.harvard.edu/~chong/abstracts/YedidiaC2021.html)
provides a useful complementary model for incremental analysis through reusable
intervals. It does not establish a fast native rendering backend or eliminate
this presentation-side work. This is a scope distinction, not a claim that the
paper is the latest editor research or that its parser should replace JSON's.

## 5. Separate ordinary bottleneck: full native novel import/history reimport

Plain text's zero semantic tokens are not a free whole-source native import.

| Novel phase | win-x64 ms | win-arm64 ms |
| --- | ---: | ---: |
| Initial native import | 4,786.7398 | 3,989.0690 |
| Undo native reimport | 4,678.1756 | 3,770.8675 |
| Redo native reimport | 4,682.4698 | 3,772.9407 |
| Initial full readback | 13.6934 | 10.8709 |
| Controlled insertion including internal readback | 16.0885 | 13.8985 |
| Post-edit engine reconciliation/map/diff | 16.3622 | 13.3488 |
| All four zero-token style publications | 41.4–59.7 | 31.9–34.9 |

Initial projection allocates approximately 57.0 MB and post-edit reconciliation
approximately 54.5 MB of process-wide managed bytes in this full-replica
experiment. Exactness succeeds, but the representation has material allocation
and import costs. The initial **mixed-text** import also takes 1,729.6214 ms
(x64) / 1,684.6210 ms (ARM64), whereas its history reimports take approximately
5–6 ms. Cold native/font/layout setup is a plausible hypothesis, not something
this phase certificate separately measures.

Do not call these startup-to-present or per-keystroke latency. Do not attribute
them to text-file parsing: plain policy analysis is effectively empty. The
multi-second whole-source history reimport remains unacceptable as the intended
ordinary product Undo path even if the dense style experiment becomes fast.
It motivates later exact native delta application for Engine Undo/Redo, with
composition/selection/ingress contracts, rather than lengthening this timeout or
quietly switching back to manual pages.

## 6. Next bounded implementation, subject to separate approval

Change only this diagnostic Windows adapter's attribute backend first:

1. Obtain `ITextDocument` from this owned control's OLE interface. Use an actual
   `ITextRange` created by `ITextDocument::Range`, **not** `GetSelection`, and
   never call `Select` while applying styles. Retain current mapped offsets,
   all-span validation, source identity and input order/overlap semantics.
2. Reuse a non-selection range with `SetRange`. Apply foreground only; a reusable
   detached font duplicate reset to undefined and assigned a COLORREF before
   `SetFont` can avoid font-object churn without overwriting face/size/bold.
   A simpler attached range-font `SetForeColor` path is also API-correct, but
   its object lifetime/retargeting behavior must be explicit rather than guessed.
3. Balance successful `Freeze` with exactly one `Unfreeze`, even after formatting
   or preservation failure. Check HRESULTs; release each acquired interface on
   the owner thread before destroying the control/module. Use the repository's
   existing AOT-safe checked direct COM ownership pattern, not dynamic COM or a
   generic capability framework. Validate SDK slots/signatures independently.
4. Preserve foreground reset, exact complete text, native selection, viewport,
   engine version/history and disabled native undo assertions. Keep the same
   fixtures, four-state publication journey, owned-process limit and artifacts.
   No replacement text, RTF fallback, dropped tokens, viewport-only substitute,
   silent exception recovery or default profile change.
5. Separate preparation, native attribute application and verification intervals
   if necessary to discriminate costs; keep the existing aggregate verified
   publication interval comparable. Foreground attribute readback for fixed
   representative styled/unstyled ranges would additionally qualify the new
   API route; API success plus unchanged text alone is not visual color proof.

Microsoft documents [document Range](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextdocument-range)
as a content range object and [SetRange](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextrange-setrange)
as endpoint adjustment. [Freeze](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextdocument-freeze)
disables screen updating; it is not a promise of constant-time formatting.
[SetFont](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextrange-setfont)
documents duplicate font reuse, and [SetForeColor](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextfont-setforecolor)
accepts COLORREF. These are mature OS mechanisms, not evidence that 79,433
non-selection range publications will meet a user responsiveness goal.

If measured detached-range publication remains expensive, do not accumulate
Freeze/flag patches. Then compare exact coalesced color runs and changed-run
publication with a deliberately specified source-surface alternative. Incremental
publication alone cannot cure this baseline's first 79-second publication.
Whole-document RTF was fast for old bounded fixtures but changes text installation
and composition/state semantics; it is not an attribute-only implementation and
requires a different reviewed experiment before consideration here.

## 7. Trace and reproducibility boundaries

Both retained traces contain **98 valid complete JSON rows**, ending after the
post-edit `analysis.semantic` completion. Neither has a normal session terminal
or a second `analysis.publish` completion; the completed initial publication
does have its span. Root trace/session completeness is not certified after
forced termination. Missing last terminal does not mean trace files are absent.

| RID | Trace filename under `probe/mote-home/traces` | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| win-x64 | `mote-trace-8167b98424154bd5916cbcacef971eef-000001.jsonl` | 30,230 | `DC2F1BACA6AC25F390F01FA2497CC757DBDE749A9B5B31F50B9E0E378ED1E38E` |
| win-arm64 | `mote-trace-4a39b90f780744e7bc8967d9b695a734-000001.jsonl` | 30,301 | `EB98E1A6528E859B8B1A9A965302F9993ED05365903B577ACB900C46A82B78BD` |

Reproduce this **artifact analysis**, not native execution:

```powershell
$base = '.cache/ci-36887820181-codec-supervisor'
foreach ($rid in 'win-x64', 'win-arm64') {
    $path = "$base/$rid/source-capability/probe/report.jsonl"
    Get-FileHash -LiteralPath $path -Algorithm SHA256
    $rows = @(Get-Content -LiteralPath $path | ConvertFrom-Json)
    $rows | Group-Object fixture | Select-Object Name, Count
    $rows | Where-Object {
        $_.fixture -eq 'dense-json' -and
        $_.phase -eq 'semantic-publication-verified'
    } | Format-List
    $rows | Select-Object -Last 5 | ConvertTo-Json -Depth 5
}
git show 23f1c1a:src/Mote.Native/Windows/WindowsNativeSourceCapabilityProbe.cs
```

This investigation settles the relevant decision: the first full-resident
experiment preserves the two plain-text journeys, but its per-token selection
publication cannot serve ordinary structured editing. The next useful question
is whether removing visible selection from attribute mutation solves the dominant
measured cost while preserving the exact contract—not whether a longer wait can
turn an unknown timeout into a green workflow.
