# Large-file demand and experience: evidence before specialization

Date: 2026-10-01. Status: product evidence assessment and proposed validation
contract, not user-study results, a performance pass, or production changes.
Scope: the existing single-file structured-text editor goal. No new formats,
scripting runtime, database functions, benchmark suites, or GUI experiments are
authorized by this note.

## 1. Decision

**Do not define mote's value as opening a 100 MB file faster than Python.**
The likely useful job is to understand an unfamiliar file, find a relevant
record and its context, extract trustworthy evidence, and occasionally make a
small reversible repair. A competent script remains the better tool for a known,
repeatable whole-dataset transformation. The two workflows often complement one
another: inspect -> formulate a transformation -> run a script -> inspect its
output. This is a product hypothesis, not observed mote adoption.

Treat 100 MiB as an existing engineering capacity/stress envelope, not as a
validated dominant user need or a magical boundary. Retain useful architecture
that prevents unbounded rendering and copying, and preserve existing correctness
and compatibility work. Do not commission further 100 MiB format-specific
specialization merely because synthetic files are available. Prioritize complete
ordinary-file interaction, safe persistence, reliable input, and meaningful
runtime evidence. If a representative large-file task fails, use its endpoint
evidence to decide what to improve.

**Direct evidence obtained during this discussion:** Klee reports that their
actual text files rarely reach 1 MB, and that even a completed novel's TXT is
3.54 MiB. This is one intended user's self-report, not a representative market
statistic; no file was independently inspected. For this user's product, the
dominant experience should therefore center on ordinary sub-1 MB structured
files and occasional few-MiB continuous reading, not 100 MiB inspection. Startup,
first useful display, readable Chinese text, real CJK IME input, search/navigation,
safe editing/Save, and complete format semantics take priority. Large-file
capacity remains resilience and regression protection unless independent
valuable workflows establish stronger demand.

This changes prioritization and the claims we may make, **not** the user's
requirement for complete format-aware semantics or a complete native product.
Analysis that is partial must remain honestly partial; hidden omissions must not
be promoted to semantic completeness to meet a latency target.

## 2. What we knew before the user's challenge

The following relevant internal material was read first, selected by filename:

- [Architecture](../architecture.md): the stated dominant journey is opening one
  file, immediate editing, useful structure/diagnostics, and safe Save; there is
  no requirement to make the editor an analytics platform.
- [Historical large-file UI evaluation](../large-file-ui-evaluation.md): a
  synthetic 100 MiB many-line full-control representation incurred substantial
  duplication/layout costs, and a smaller single enormous line was a more severe
  counterexample. These findings justify rejecting that representation; they do
  not establish who regularly edits such files. The evaluated Avalonia surface
  is not the current ordinary native surface.
- [Native performance baseline](../native-performance-baseline.md): deterministic
  ASCII fixtures, headless storage timings, and scoped native edit/Save probes
  are explicitly synthetic. Microsecond engine edits are not physical-key-to-
  visible-glyph responsiveness; an exact large-file Save is not usability.
- [Native latency acceptance](../native-latency-acceptance.md): endpoint families,
  evidence boundaries, workload dimensions, observer overhead, and uncertainty
  are already specified. It explicitly declines to fabricate product limits
  without representative hardware and owner approval.
- [Release gaps](../release-gaps.md) and
  [native visual assessment](native-canvas-visual-acceptance.md): test/build
  success does not settle input, visual polish, actual assistive-technology use,
  semantic completeness, or latency tails.

**Prior evidence gap:** before the self-report above, no target-user interview
records, representative actual-file
task corpus, diary of large-file frequency, or comparative adoption study was
found in the inspected product and relevant engineering documents. Engineering
research and synthetic capacity testing have taken place; a completed mote
target-user demand study has not been demonstrated. Do not retrospectively call
those experiments user research.

100 MB means 100,000,000 bytes; the existing 100 MiB fixture means 104,857,600
bytes. Keep these units distinct in reports. Neither alone predicts difficulty.

## 3. External evidence, with limits

Sources were opened on 2026-10-01. Historical failure reports are not claims about
current versions. A report's existence proves an occurrence, not prevalence.

| Evidence | What it supports | What it does not establish |
| --- | --- | --- |
| [VS Code team's 2018 text-buffer account](https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation), linking [issue #13187](https://github.com/microsoft/vscode/issues/13187) | A real 35 MB / 13.7 million-line file triggered a reported out-of-memory failure. Line count and representation matter independently of bytes; the team's integrated profiling found hot paths differed from initial assumptions. | Present-day VS Code failure, typical mote workload, or the usefulness of another storage optimization without endpoint evidence. |
| [VS Code issue #197715](https://github.com/microsoft/vscode/issues/197715), filed 2023 | A user reported trying to open large JSON files, including roughly 35 MB and 1 GB, and difficulties with opening/wrapping on macOS. | A reproduced current bug, demand frequency, a representative sample, or willingness to switch editors. |
| [VS Code issue #160260](https://github.com/microsoft/vscode/issues/160260), filed 2022 | An explicit attempt to inspect a >40 MB log file with wrapping. | A current confirmed regression or how often this workflow occurs. The issue requests more information. |
| [GitHub CodeQL query documentation](https://docs.github.com/en/code-security/how-tos/find-and-fix-code-vulnerabilities/scan-from-vs-code/running-codeql-queries#troubleshooting) | Production documentation explicitly routes query logs that are too large for the extension to an external program. This is a concrete diagnostic handoff, not merely a vendor slogan about big files. | That the external program must be mote, must edit the log, or must parse all bytes before showing anything. |
| [He et al., An Empirical Study of Log Analysis at Microsoft, ESEC/FSE 2022](https://www.microsoft.com/en-us/research/publication/an-empirical-study-of-log-analysis-at-microsoft/) ([DOI](https://doi.org/10.1145/3540250.3558963)) | The primary abstract reports a 13-question survey of 105 employees and 12 individual interviews, addressing real log-analysis practices and industry/research gaps. Empirical practitioner study is possible and more informative than synthetic throughput alone. | Demand for mote, desktop inspection frequency, a 100 MB threshold, or particular tool shares. The author PDF URL returned 404; no table frequencies from search snippets are used here. |
| [EmEditor large-file operations documentation](https://www.emeditor.com/text-editor-features/large-file-support/optimized-sort/) | An established editor distinguishes early viewing/editing from completed loading and whole-file commands. Its CSV examples use a real CDC dataset. Mature products treat open, search, and whole-file processing as different endpoints. | Neutral comparative speed, independently verified vendor benchmarks, market size, or a mandate for mote to add sort/join/pivot analytics. |
| [pandas scaling guidance](https://pandas.pydata.org/docs/user_guide/scale.html) and [jq 1.8 streaming manual](https://jqlang.org/manual/v1.8/#streaming) | Established programmatic alternatives include selective columns, chunked processing, and streaming JSON paths/leaves. Scripts need not load or render a whole file, and chunking has workload-specific constraints. | That an editor should reproduce these analytics capabilities, or that every Python workflow is low-memory by default. |

**Conclusion:** large-file inspection is a real class of problem. The currently
available evidence does **not** show that 100 MB editing is mote's dominant job,
that the target audience encounters it frequently, or that our present experience
beats the tools people already use. Secondary demand evidence cannot replace
observing the intended user's actual job.

## 4. Choosing an editor, a script, or both

These are candidate jobs to validate, not fabricated user personas.

| User question/task | Natural first tool | Where mote could help | Avoided scope/error |
| --- | --- | --- | --- |
| "What did this unfamiliar export actually contain?" | Editor/structured viewer, or a few exploratory commands | Read schema/context without inventing a parser first; navigate source and rendered structure without losing location. | Do not silently assume a regular CSV or valid JSON. |
| "Find this request ID and the surrounding failure." | Existing log platform, search tool, or editor | Search and inspect adjacent source context, copy a bounded trustworthy excerpt. | A log file is plain text in existing scope; this does not add a log language or live-tail promise. |
| "Which field/record made my import fail?" | Diagnostic location plus editor; script for validation | Jump to the exact source location, understand structure, optionally repair one known defect and undo it. | Diagnostics must disclose incomplete coverage, not claim an unseen remainder is valid. |
| "Fix one export cell or value and preserve the rest." | Editor if the change is genuinely local | Source-backed cell/value location, reversible edit, exact preservation of unrelated bytes and encoding. | Preserve originals, warn about external changes; do not encourage ad-hoc editing of live production data. |
| "Group a million rows, join tables, remove duplicates, or apply a rule nightly." | Python, jq, SQL, or a dedicated data pipeline | Inspect samples before writing the rule and verify a result afterward. | No embedded Python/SQL engine, pivot/join UI, or agent runtime is implied. |
| "Read a huge generated document from beginning to end." | Depends on actual task; often search/sampling, not linear reading | A coherent continuous document and useful navigation, if needed. | Being able to scroll every byte does not make exhaustive manual reading valuable. |

A decision model, not a fitted quantitative result:

`T_editor = launch + locate + understand + local_action + verify`

`T_script = specify_rule + construct/verify_rule + execute + inspect_output`

Use the editor when the next question depends on seeing context and the scope of
action is still unknown. Use a script when the rule is explicit, the action is
global/repeated, or reproducibility is central. With agents, constructing a script
can become cheaper; this strengthens the need for mote to excel at trustworthy
inspection rather than competing at bulk processing. It does not remove the
human's need to verify the problem specification and the result.

## 5. Workload dimensions, not one byte-size trophy

Select real tasks and then describe their inputs along these dimensions. Do not
generate the Cartesian product as an automatic benchmarking obligation.

| Dimension | Why it changes experience |
| --- | --- |
| Bytes and encoding | Decode cost, memory expansion, storage/cache state; ASCII is not a Unicode workload. |
| Logical line count and maximum line length | Many tiny lines stress indexes; one enormous line stresses layout and horizontal navigation. |
| Record/field count and size skew | Wide CSV, quoted multiline fields, giant JSON scalars, and many tiny records create different costs. |
| Structure and cross-reference density | Nesting, Markdown references, YAML aliases, and TOML ownership affect meaningful analysis independently of bytes. |
| Validity and error location | Truncation or a late error changes recovery and whether a useful result can be trusted. |
| Required action and location | Inspecting a known line, searching an unknown identifier, selecting across distant ranges, or editing one field are not equivalent. |
| Interaction context | DPI/refresh, keyboard/IME, accessibility, free RAM, disk, focus, and concurrent CPU load affect the endpoint. |
| File lifecycle | Static exports differ from externally rewritten files; untrusted content and privacy limit what may be collected. |

Large TOML/Markdown inputs remain defensive semantic stress cases until actual
task evidence makes them a product-priority scenario. This is not permission to
drop support or break established behavior.

## 6. What "good" must mean

First establish functional acceptance, regardless of file size:

1. The user can reach a relevant place without guessing an internal page number.
   Source position, outline/grid selection, find, and copy refer to one canonical
   document. Search is not secretly restricted to the visible projection.
2. Opening and examining a file do not alter it. Unsupported encoding, binary
   data, ambiguous parsing, and incomplete analysis are explicit states with a
   safe recovery path, not silent data replacement or a false clean bill of health.
3. Input, undo, selection, navigation, and cancellation remain usable while
   expensive analysis/search/save runs. A local edit does not require a blocking
   full-file re-render. Keep established input and accessibility contracts.
4. A deliberate local repair is exactly reversible. Save either commits the
   intended snapshot while preserving unrelated bytes or reports failure and
   leaves recoverable content; a successful-looking title is insufficient.
5. The task produces a correct answer/excerpt or exact edited file, not just a
   quick first window. No-crash, source correctness, and task completion outrank
   a favorable median.

### Proposed latency hypotheses, not approved or achieved product promises

The numbers below are **starting hypotheses for owner review and task testing**.
They are not measured mote results, literature-derived universal thresholds,
current CI gates, or reasons to optimize before confirming value. They and the historical architecture budgets have no release-gate authority
without task/context approval. The [release acceptance contract](release-acceptance-contract.md)
separates ordinary tasks, capacity stress and mechanism experiments. Calibrate them
on a documented representative SSD laptop/hardware class rather than the fastest
development machine; test Windows and macOS independently.

| Task endpoint | Candidate experience budget | Necessary qualification |
| --- | --- | --- |
| Ordinary-file launch to first usable source | p95 <= 500 ms | External source readiness/action acknowledgement; physical first pixels separately. |
| Representative 100 MiB static-file launch to first usable source | p95 <= 1 s | Capacity stratum only after an actual task justifies it; includes launching/opening, excludes fixture generation. |
| Key/selection action to corresponding visible source response | p95 <= 50 ms; investigate any >100 ms interruption | Real input-to-visible endpoint where measurable; direct native messages and draw-return traces remain distinct proxies. No lost/reordered input. |
| Navigate to known distant record/line | p95 <= 100 ms once source is ready | Correct context visible and global selection intact, not merely changed scrollbar metadata. |
| Search a representative file | Useful first result/progress <= 1 s; cancel acknowledgement <= 100 ms | Full scan completion reported separately, including no-match; correctness must survive a change in query/document. |
| Whole-file semantic work or Save | Progress and usable UI; exact completion separately | No fixed throughput target without workload/hardware evidence; do not block typing or fake completion to meet a deadline. |

Memory acceptance should measure total process resident/private/peak memory as
available, retained growth through repeated workflows, and impact on the other
tools open on that laptop. Avoid paging-induced stalls, leaks, per-keystroke
whole-file copies, and uncontrolled growth. No universal "100 MB must use X MB"
limit is justified here; an owner-approved resource envelope needs representative
machines and task evidence first.

[Liu and Heer, IEEE TVCG 2014](https://idl.uw.edu/papers/latency) found that adding
500 ms to exploratory visualization reduced activity and dataset coverage and
changed exploration strategy. This supports protecting rapid investigation
loops; it does not establish an editor's 50 ms threshold. [Deber et al., CHI
2015](https://doi.org/10.1145/2702123.2702300) studied direct/indirect touch tapping
and dragging and found task/form-factor effects. Do not extrapolate touch latency
perception into a universal typing or 100 MB launch standard.

Use the existing [endpoint/evidence contract](../native-latency-acceptance.md):
report raw outcomes, observer overhead, trace-off primary timing, per-machine
strata, failures/censored observations, and clustered actions. Three successful
synthetic processes cannot estimate dependable p95/p99 or validate adoption.

## 7. Next discriminating user evidence

The first useful step is not another stress benchmark. Klee has now explicitly
reported no actual large-file workflow; do not keep asking for one or invent it.
Validate the ordinary files and few-MiB reading tasks they do encounter. To assess
a separate broader audience, reconstruct recent real work, including decisions
**not** to use an editor; add approximately 5-8 purposeful participants spanning
developer diagnostics and CSV/export inspection if recruitment is possible. This
is exploratory discovery, not a prevalence estimate or statistical sample-size
claim. A participant who always scripts is important contrary evidence.

Non-leading prompts:

- "Tell me about the last file that was inconvenient to open or inspect. What
  were you trying to decide or change?" Do not start by requiring 100 MB.
- "What did you do first, then next? What made you choose those tools?"
- "What was the concrete point of failure, waiting, uncertainty, or switching?"
- "Which parts did you need to inspect manually, and which were automated?"
- "When did you deliberately avoid an editor? What worked well instead?"
- "How many similar episodes can you recall in the last month?" Record recall
  limits; do not transform answers into market frequency.

Request a sanitized file or metadata/task reconstruction only with consent.
Record format, dimensions, task, current workaround, completion/abandonment,
setup time, active interaction time, correctness, and consequence. Do not collect
raw personal logs, file contents, paths, or document text through telemetry by
default. Public datasets can fill reproducibility gaps but must not be relabeled
as a participant's actual work.

Observe one real workflow with the participant's existing editor and existing
script/tool. Compare mote only when that workflow's necessary functions are
usable; let participants use familiar shortcuts and pipelines. Include script
setup time when the rule is new, but do not charge setup again when an existing
script already solves the task. Include outcome verification for **both** tools.
Avoid a predetermined winner or a synthetic race that excludes the user's
dominant operation.

### Decision after discovery

| Observation | Priority consequence |
| --- | --- |
| Repeated real inspection/local-repair episodes fail in existing tools | Promote that complete workflow; measure and repair its failing endpoint. |
| Large files occur, but existing scripts/search tools already solve the job | Keep safe capacity handling; do not make large-file speed the positioning. |
| The main pain is understanding/locating evidence rather than file opening | Prioritize coherent search, source context, navigation, and trustworthy diagnostic coverage over parser throughput alone. |
| Almost all encountered files are small; large cases are rare defensive inputs | Prioritize ordinary-file polish and reliable recovery, retaining stress tests without escalating specialized machinery. |
| Evidence is mixed or participants cannot recall concrete episodes | Keep the hypothesis open; collect an actual episode/short diary rather than manufacture certainty. |

Success means the user completes an authentic task correctly with less friction
and chooses the tool again when appropriate. A report saying "100 MB opened"
cannot establish that outcome. No network analytics infrastructure is required
to make this first decision; opt-in local runtime evidence explains *why* a task
failed, while observed user work explains *whether the task is worth prioritizing*.
