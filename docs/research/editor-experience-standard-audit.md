# Independent audit of mote's experience and acceptance standards

Date: 2026-10-02. Status: independent audit complete; **no performance optimization,
benchmark execution, GUI/build/CI action or product-contract change**.
Question: are the standards we are using reasonable for the intended editor,
rather than merely difficult standards we have become good at measuring?

## Scope and preliminary answer

The user explicitly asks to review standards before further optimization,
calling the earlier 100 MB emphasis unrealistic. The strongest target-user
evidence is one user's self-report: ordinary files rarely reach 1 MB, no recent
large-file editing need, completed novel TXT approximately 3.54 MiB. That is not
a population distribution and does not authorize deleting large-file support.

**The correctness foundations are defensible; the experience workload hierarchy
and numerical target promotion are not sufficiently grounded.** Existing docs
carefully distinguish many proxy endpoints, but the selection of experiments
and promotion decisions can still overgeneralize from them. A disclaimer does
not transform a repeated synthetic stress workload into a representative task.

Use four separately governed layers:

| Layer | Purpose | Required evidence | Can it choose the default UI? |
| --- | --- | --- | --- |
| Correctness/integrity | Lossless source, safe I/O, no silent loss of committed or pending native input, sole history, version/lifetime admission | Exact independent functional oracles across formats/encodings/ingress | Necessary, never sufficient |
| Ordinary task experience | Read/locate/understand/edit/verify a file with natural input and navigation | Named tasks, justified corpus/hardware, observed outcomes and endpoint distributions | Yes, together with correctness and capacity safety |
| Capacity/adversarial regression | No corruption, bounded failure and established behavior at difficult shapes/sizes | Explicit stress classification and honest analysis coverage/resource state | Blocks regressions; not a demand/preference estimate |
| Mechanism experiment | Resolve one platform/runtime/representation question | Frozen source/fixture, phase identity, exact transfer/state checks | Only narrows options; not product acceptance |

The six-format semantic goal remains. Complete semantics means accurate syntax,
resolution/validation and honest coverage, **not eagerly applying every token's
native foreground to offscreen text before interaction is possible**. Deferred
presentation does not permit deferred source correctness or false validity.

## Concrete standards retrieved first

- [Architecture](../architecture.md), particularly section 4's acceptance
  hypotheses: 150 ms warm shell, 250 ms 1 MB open, 2 s 100 MB open, 16 ms typing
  p95 and resident memory at most twice file bytes.
- [Native latency acceptance](../native-latency-acceptance.md): primary matrix
  1/10/100 MiB, JSON/CSV/Markdown and both line shapes; 18 generated cases total
  666 MiB, default six 1 MiB cases; capability/phase/tail protocol.
- [Demand/experience assessment](../product/large-file-demand-and-experience.md):
  direct user evidence and later 500 ms/50 ms target hypotheses.
- [Ordinary editing locus](../architecture/ordinary-editing-locus.md): one
  painter/input goal and requirement to meet existing candidate experience
  targets before promotion.
- [Native startup probe](../../benchmarks/NativeStartup/README.md): 1/100 MiB,
  exact source-island selection/Save, `MoteInteractiveCanvas`/16,384-unit checks.
- [Native source fixtures](../../src/Mote.Native/NativeSourceDiagnosticFixtures.cs)
  and runner: fixed mixed-text, 3.54 MiB novel-size and 512 KiB dense JSON;
  complete resident replica/all-native-foreground publication and fresh
  in-process Document reopening.
- [Source workflow](../validation/native-codec-source-workflow.md): 120-second
  source-probe safety supervisor and closed fixture/phase certificate.
- [Current checklist](../product/goal-checklist.md): explicitly scoped runtime
  proof; it does not claim a complete editor or experiential latency distribution.

## Initial disposition register

| Named rule/artifact | Disposition | Reason | Replacement / preserved boundary |
| --- | --- | --- | --- |
| Lossless bytes, original protected on failed Save, explicit encodings, stale-version refusal, sole engine history | **KEEP** | Directly protects user work; not a speed preference | Test independent exact output/unchanged original and all committed edit ingress; preserve platform fault evidence |
| One document/no workspace/LSP, six semantic policies, theme and configurable `~/.mote` | **KEEP** | Explicit product requirements, not benchmark inventions | Do not trade them for a faster smoke or fewer cases |
| 100 MiB as a primary ordinary-editor objective | **DEMOTE** | No supplied target-user task justifies priority; size alone says little | Capacity/regression layer with named shape and operation; retain already-established safety |
| 1/10/100 MiB Cartesian primary matrix in native-latency doc | **REVISE** | Synthetic size ladder drives work without task/value evidence | Ordinary task corpus first; selected stress dimensions separately; no automatic 666 MiB suite obligation |
| `ordinary-file tasks` label for fixed native source generator | **REVISE** | User supplied novel byte size, not its repeated paragraphs/tasks; dense/Unicode cases intentionally structural | Name mechanism/structural probes; never remove difficult input to claim improvement |
| Universal 16 ms p95 input-to-paint and byte-proportional `<=2x` memory | **REMOVE as hard release authority; retain historical hypotheses** | No approved reference hardware/endpoint or overhead model; fixed runtime memory makes tiny-file ratio ill-defined | Task-specific measured latency/resources with independent perceived/visible endpoint; fixed + workload-variable memory model |
| 150/250/2000 ms versus 500/50 ms target sets | **REVISE** | Hypotheses differ and can be promoted through later cross-reference without approval | One versioned task/endpoint/reference-machine target registry; targets explicit tentative until approved |
| 120 s subprocess deadline | **KEEP as safety; DEMOTE as UX evidence** | Own-process cleanup limit prevents hangs, says nothing about acceptable waiting | Normal completion/failure/censor classification separate from interaction duration/results |
| All offscreen foreground attributes during reference publication | **DEMOTE** | Measures one full-control mechanism, not complete semantics or mandatory eager rendering | Visible-interest/style freshness contract with exact semantic and source state retained |
| Fresh `Document.OpenAsync` in same GUI process | **KEEP scoped; REMOVE equivalence to fresh-process reopen** | Validates byte decoding/engine re-open, not startup/restore/activation | Explicit fresh-process exact-file check when making product lifecycle acceptance claims |
| Closed fixed fixture/phase inventory | **KEEP for replayable mechanism experiment; REVISE for product acceptance** | Protects reproducibility but embeds the experiment in its oracle | Product-neutral task outcomes plus mode-specific measurement metadata |
| CI job success as editor-experience pass | **REMOVE inference** | Non-gating diagnostics and scoped suites can coexist with failures | Per-task PASS/FAIL/UNKNOWN/NOT_ATTEMPTED with provenance and scope; no completion percentage |

Dispositions above concern authority and classification, not deleting evidence,
weakening exactness or silently altering tested runtime contracts. The completed
group findings below refine named examples and replacement procedures.

## Independent group and boundaries

Four specialists own separate persistent findings:

- [Task/requirement audit](editor-standard-task-audit.md), product_manager.
- [Measurement/workload audit](editor-standard-measurement-audit.md), perf.
- [Oracle/decision audit](editor-standard-oracle-audit.md), reviewer.
- [HCI/production evidence audit](editor-standard-hci-audit.md), explorer.

Only retained artifacts/source and inexpensive structural analysis are allowed.
No new measurements are needed to recognize unsupported standards. The audit
must criticize our own native/Avalonia decisions rather than rationalize them.

## Confirmed structural findings (no new benchmark)

The measurement specialist inspected retained exact fixture bytes:

| Fixture | Actual structure | Defensible use | Unsupported inference |
| --- | --- | --- | --- |
| 3,711,959-byte `novel-text` | 31,192 repetitions of the same 39-character paragraph; 62,386 LF; 31,193 blank lines; longest line 98 characters including final padding | UTF-8 transfer/residency/history preservation at one reported byte size, paragraph-heavy layout probe | Real novel layout, natural reading/Find task, general 3.54 MiB behavior |
| 524,288-byte `dense-json` | 7,943 identical objects; 79,433 semantic tokens, approximately 155 tokens/KiB | Dense token/range-publication regression and exact stable semantic baseline | Normal JSON configuration density, user prevalence, complete ordinary-format acceptance |
| 2,875-byte `mixed-text` | Same deliberately mixed-script/combining/emoji/tab/newline string repeated 36 times | Unicode/shaping/newline seams and exact edit projection | Common everyday text composition or real IME behavior |

None is bad because it is synthetic. The flaw is **reassigning a mechanism probe
the authority of a task distribution**. Keep exact frozen inputs/results so a
new implementation cannot hide a regression by substituting an easier file.
Add justified ordinary tasks separately rather than make these fixtures smaller
and call the same certificate representative.

The current retained source-reference run (CI 36899695843) normally completed all
four adapters, but Windows dense initial/post-edit publication was 7587.94 /
10256.40 ms x64 and 5223.90 / 7796.11 ms ARM64; novel import/history reimports
were approximately 3.8–4.9 seconds. These identify reference-path costs, not
current range-based product Undo latency. Normal completion is not fluent
editing, and a 120-second supervisor does not make those intervals acceptable.
These single observations do not provide percentiles or cross-CPU comparisons.

### Concrete oracle coupling

The Windows startup/ordinary JSON observers require a `MoteInteractiveCanvas` ancestor
and a native island of at most 16,384 characters. Those conditions correctly
identify the historical Continuous adapter; they are not universal editor
requirements. A sole full-body source surface can violate both while preserving
global selection/exact bytes. Keep the old lane and identity; compare candidates
through a common behavioral oracle with reviewed adapter-specific discovery,
not by removing the check and pretending the old lane passed.

The new Windows candidate's 8192-unit viewport-derived style interest and
128-calls-per-turn budget are scheduling mechanisms. The stated interest does
not guarantee complete visible coverage when a long paragraph wraps. Deferred
offscreen styling is permitted; indefinitely stale/wrong visible styling is not.
The standard must require actual visible range/version/theme consistency or
explicit neutral pending presentation, not treat a convenient constant as proof.

## Replacement protocol: tasks first, endpoints second, budgets third

This is an executable acceptance **specification**, not an instruction to start
new benchmarks before the owner accepts the revised standard.

### 1. Minimal ordinary task set

| Task ID / goal | Input selection | Actions and observable success |
| --- | --- | --- |
| R: read and locate | Consented real/public-licensed text with varied chapter/paragraph lengths; few-MiB novel-size stratum is supported by direct user evidence | Open normally, read/scroll, Find a chosen phrase, move to a distant known passage, select/copy a bounded excerpt. Continuous correct location and no hidden page boundary |
| C: inspect and repair configuration | Authentic task-sized TOML/JSON/YAML configurations or provenance-matched generated equivalents; include valid and one explicitly planted error | Locate diagnostic/structure, understand context, replace one value using natural input, Undo/Redo, Save and fresh-process reopen; correct diagnosis/expected exact bytes |
| M: structured prose | Task-sized Markdown with headings/reference links/fences from permitted source | Navigate source/outline/preview, edit a paragraph/link, validate relevant dependencies and preserve source location/bytes |
| T: inspect a CSV record | Legitimate small export with header/quoted fields and varied row widths, not only identical rows | Find/select record/cell, source jump, one reversible correction; exact quote/newline handling and consistent global coordinate |
| I: ordinary Chinese input | Small simple document first, then one justified structured task | Actual Pinyin marked composition, candidate change, commit/cancel, selection replacement and command during composition; visible in-place composition and exactly-once committed text |
| A: assistive reading/navigation | Same ordinary task, independently selected by reader user/tester | NVDA/VoiceOver reading, line/selection navigation and relevant diagnostic/context; correct source identity and usable spoken flow |

The table defines candidate tasks, not a fabricated observation that Klee edits
all formats or uses a reader. The six supported formats still require functional
conformance independently of their frequency. Do not invent exact ordinary byte
cutoffs or equal weights from one user's statement. Record source provenance,
format, byte/UTF-16 counts, encoding, line/record length distribution, structure
and task intent; do not upload personal documents/contents as telemetry.

The prior corpus remains a separate **capacity/stress** lane, selected by
dimension (long line, many records, dense tokens, alias/reference dependency,
encoding seams). Its success is capacity/scoped correctness, not normal UX.

### 2. Behavioral oracle and timing contracts

For each declared adapter, discover the exact owned source, global extent and
selection, read a bounded source range at the intended task location, execute
one natural/specified action, and validate canonical change/expected output.
Save validates independently prepared full bytes, followed by normal exit and
a distinct owned GUI process reopening the saved file with clean state/source
witness. Preserve destructive-command settlement, stale lifetime/version tests
and all unchanged-file/recovery safeguards.

Record separate endpoints:

1. New-process launch to actual task-interactive source (not title/window alone).
2. Input to canonical committed text; marked composition remains a separate state.
3. Input/scroll to corresponding independently observed visible response.
4. Current semantic result publication and honest completeness.
5. Current visible decoration, with source/version/theme/viewport stamp.
6. Save success/failure and fresh-process reopen; cancellation/progress separately.

Direct messages, in-process callbacks, draw return, attribute readback and
physical visible response keep separate labels. No unverified cross-clock
subtraction or retry of non-idempotent edits. Failed, cancelled, censored and
not-attempted observations remain alongside successful ones. CI aggregates do
not replace lane verdicts.

### 3. Numeric targets require explicit authority

Replace scattered candidate numbers with one small Markdown target registry:
**task + endpoint + justified workload/hardware + origin/owner approval + target
status + oracle/measurement limits**. Existing numbers are historical hypotheses
until reviewed, not automatic gates. Do not create a new telemetry/framework
bureaucracy. A first usable demo can expose multi-second blocked interactions
without needing a purported universal human threshold; absence of an approved
millisecond cutoff is not permission to call that experience good.

Measurement selection follows the uncertainty: capability pilot before repeated
timing, ordinary tasks before scaling, matched before/after on the same hardware,
first-use and repeated work separately, no tail claims from tiny/correlated
samples. Stop increasing repetitions when the decision is already identified;
do not turn 3 hosts x30 launches x100 actions into an unconditional Cartesian
qualification tax. Memory separates fixed shell/runtime cost, workload-dependent
residency, semantic stage and retained growth; preserve unavailable counters as
unknown, not zero.

## Evidence from objective external practice

Primary HCI research supports task-dependent measurement, not a replacement
magic number. [Deber et al., CHI 2015](https://doi.org/10.1145/2702123.2702300)
studied different touch actions/form factors, not desktop typing/Pinyin/startup;
its perceptibility results cannot approve one common latency threshold.
[Liu and Heer, IEEE TVCG 2014](https://idl.uw.edu/papers/latency) showed effects
on exploration despite many participants not attributing effects to delay.
Therefore neither a timing proxy nor "felt fine" alone proves task quality.

Production practice separates layers too:
[VS Code input-latency work](https://code.visualstudio.com/updates/v1_73#_optimizing-for-input-latency)
prioritizes input-to-render over secondary work;
[Zed's frame-delivery investigation](https://zed.dev/blog/120fps) demonstrates
that short work duration alone can coexist with dropped frames;
[Rider size-limit guidance](https://www.jetbrains.com/help/rider/Configuring_File_Size_Limit.html)
separates loading and assistance/inspection policy rather than one size trophy.
These support a layered standard, not direct numerical or functional imports.
Detailed source methods/limits are in the independent HCI report.

## Architecture decisions exceeding the evidence

- A single visible editing locus is defensible from direct user interaction;
  full residency, native-only controls or eager all-token attributes do not follow
  logically. NativeSource reference success narrows choices but does not approve
  real IME/reader/capacity or default promotion.
- A 100 MiB failure can reveal a representation limit; it cannot establish that
  a custom canvas/input island is worthwhile for a target user who has no such
  task. Conversely, small ordinary success cannot justify losing global editing.
- Allowing delivery companions does not prove Avalonia/CoreCLR UX superiority;
  the prior delivery report's console timings and upstream preedit limitation
  remain scoped mechanism/compatibility evidence, not task-standard acceptance.
- Same-process Save/reopen and bounded foreground witnesses are valuable tests
  but must not authorize fresh-product reliability or all visible glyphs.
- The current documentation largely states these limits already. The problem is
  **standards/priority drift between documents and decisions**, not evidence that
  every test or previous implementation is useless or dishonest.

## Final decision and adoption boundary

**Do not continue optimizing against the existing primary size ladder or promote
the native/UI candidate on its three reference fixtures. First revise the
standard hierarchy and approve task/context definitions.** This is an actionable
standards correction, not an instruction to scrap measured work or accept a
slower/corrupt editor. Multi-second blocked ordinary interaction remains a real
problem when it occurs; what is not justified is selecting the architecture or
entire backlog from an unvalidated workload proxy.

The minimum next repository changes after owner review are documentation and
protocol alignment, not faster code:

1. `docs/architecture.md`: retire unapproved numerical hypotheses as promotion
   authority; retain history, invariants and workload-shape lessons.
2. `docs/native-latency-acceptance.md`: replace the primary 1/10/100 MiB matrix
   with task-defined ordinary inputs, retain the old matrix as versioned stress
   coverage and preserve endpoint/uncertainty distinctions.
3. `docs/product/large-file-demand-and-experience.md`: remove the circular
   "stricter existing budgets" precedence for unapproved hypotheses; link one
   reviewed target registry rather than create a competing number set.
4. `docs/architecture/ordinary-editing-locus.md`: promotion gate names the actual
   task/endpoint/target status, not "existing candidate targets" generically;
   source-reference tests and real IME/reader/capacity evidence stay separate.
5. Fixture/validation prose: classify `mixed-text`, `novel-text`, `dense-json`
   as frozen Unicode/size/density mechanism probes. Keep the inputs, phase
   conservation and historical verdicts; do not weaken readers to turn prior
   failures into passes.
6. Future replacement observer: preserve old Continuous diagnostic names and
   add explicitly selected adapter-aware discovery sharing the same behavioral
   outcome oracle. Do not execute or change it during this standards audit.

All six explicit formats remain **Markdown, TOML, JSON, YAML, CSV and Plain
Text**. Structured grammar/semantic conformance and plain-text coverage,
identity formatting, safe rendering and lossless editing are different policy
contracts. Few natural tasks do not establish complete policy conformance or
make permanent `Provisional` a completed semantic goal.

Scope/confidence: high for source/fixture/oracle classification and unsupported
target authority; moderate for proposed task hierarchy from one intended user's
evidence; deliberately unproven for numeric experience budgets, market demand,
real Pinyin/reader outcomes and which alternative UI wins. Those uncertainties
become explicit task/approval gates, not reasons to add speculative optimization.

Completion evidence: four independent specialist documents and this synthesis
exist. The oracle reviewer independently checked preservation of scope,
historical hypothesis status, task evidence limits and representation-independent
protocol; no substantive scoped defect, two wording clarifications incorporated.
Local links and targeted whitespace checks were inspected. No historical
measurement was repeated and no product/test/workflow/goal contract was changed.
