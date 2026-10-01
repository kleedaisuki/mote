# Editor standards audit: real tasks before optimization

Date: 2026-10-02 (Asia/Singapore).
Status: independent product/acceptance audit and replacement protocol proposal;
not a release pass, requirement amendment, native execution, or optimization.
Scope: whether the standards themselves are justified. Only this document is
owned by this audit. Production, tests, workflows and historical evidence are
unchanged. No GUI, build, benchmark, CI, commit or user-data collection was run.

## 1. Decision

**The present acceptance model mixes user contracts, engineering hypotheses,
diagnostic fixtures and product claims. That mixture is not reasonable. Fix the
classification and the task protocol before using it to demand more optimization.**

Klee reports that actual text files rarely reach 1 MB, that a completed novel's
TXT is 3.54 MiB, and no actual 100 MB job has been identified. This is direct
evidence about the intended user's context, not a market distribution and not
an independently inspected file. It overrides an inferred *dominant workload*,
not established source correctness, capacity or six-format requirements.

The smallest complete experience is opening one ordinary file, understanding or
finding a relevant place, editing there with real Chinese input, Undo/Redo,
trustworthy structure/diagnostics, and exact Save/reopen. Occasional few-MiB
reading is a second task family. A 100-MiB synthetic operation is not the
definition of product success. Nor is a small successful edit enough to certify
the six formats or the product.

Recommendations:

1. **Keep** one-document scope, full six-format semantics as the required end
   state, lossless source/encoding/history/persistence, honest incomplete
   analysis, continuous global commands, and real input/accessibility contracts.
2. **Revise** release acceptance into a task-first ordinary-file protocol with
   explicit evidence provenance and a separately frozen semantic conformance
   protocol. Do not convert a byte cutoff into a silent behavior switch.
3. **Demote** 100-MiB/50-MiB-line cases, dense semantic publication and mixed-script
   torture strings to capacity, mechanism or regression strata. Keep their
   existing correctness evidence; do not present them as common user demand.
4. **Remove** automatic promotion of unapproved numerical hypotheses into release
   gates, a universal memory/file-size ratio, a universal one-frame typing rule,
   and test-count/green-CI proxies for task completion.
5. **Do not begin another optimization or UI migration merely to pass the old
   matrix.** First determine which supported authentic task fails, or which
   retained safety/compatibility contract a mechanism demonstrably violates.

This does not approve relaxing the active single-binary delivery requirement.
Packaging, runtime and UI remain independent decisions in the delivery research.

## 2. Evidence and provenance: where standards actually came from

Internal retrieval was filename-led: product demand, goal checklist, native
latency acceptance, architecture, ordinary editing locus, visual acceptance,
native-source publication/import findings and UI migration. Only the directly
relevant fixture generator was then inspected. Current checkpoint references
are reused; no earlier validation was rerun.

| Item | Provenance established in inspected material | Classification / limitation |
| --- | --- | --- |
| Single-file editor, six-format support, lossless Save/history and current delivery shape | [Architecture](../architecture.md), [goal checklist](../product/goal-checklist.md) | Recorded authoritative contracts. This audit is not the original user transcript and cannot establish the wording of every historical request. |
| Sub-1-MB ordinary files, 3.54-MiB novel, no demonstrated 100-MB task | [Direct user evidence](../product/large-file-demand-and-experience.md#1-decision) plus the current audit instruction | Actual self-report. Frequency, encoding, layout and task details have not been independently measured. No need to keep asking Klee to invent a big-file task. |
| Warm shell 150 ms, 1 MB open 250 ms, 100 MB open 2 s, typing p95 16 ms, resident memory at most 2 times file size | [Architecture section 4](../architecture.md) | Explicitly labelled **acceptance hypotheses**, not measured claims. No owner-approved representative hardware/endpoint derivation was found in the inspected records. MB is not MiB. |
| Ordinary launch p95 500 ms; input/selection p95 50 ms; investigate interruptions over 100 ms; 100-MiB launch p95 1 s | [Demand document section 6](../product/large-file-demand-and-experience.md) | Later **proposed hypotheses**, explicitly unapproved. They conflict in endpoint and number with the older hypotheses and do not become gates by repetition. |
| Exactly 1/10/100 MiB, JSON/CSV/Markdown, short lines and unbroken lines | [Native latency matrix](../native-latency-acceptance.md) | Controlled synthetic workload for endpoint/mechanism research. It starts above the user's typical file sizes, excludes three required formats from that performance slice and is not a product-demand corpus. |
| Three native-source fixtures described as ordinary-file tasks | [Ordinary editing locus section 5](../architecture/ordinary-editing-locus.md), [generator](../../src/Mote.Native/NativeSourceDiagnosticFixtures.cs) | A generated mechanism reference informed by size/character concerns. The generator's comment does not turn controlled insertion into an observed user task. |
| Meet/improve existing candidate experience targets | [Ordinary editing locus pass/fail facts](../architecture/ordinary-editing-locus.md) | Useful intent, but no single approved target table is named. This can launder conflicting hypotheses into a gate unless the exact endpoint, host and approved version are frozen. |
| Twelve initial visual states; later 100-MiB JSON / 50-MiB-line extension | [Visual acceptance section 6](../product/native-canvas-visual-acceptance.md) | A scoped proposed visual/interaction method, not completed acceptance. Three documents, two themes and two scales identify twelve base states; extra editing/composition captures are additional states. |
| 3508 tests per platform and four inventories pass; native reference checks pass | [Goal checklist](../product/goal-checklist.md) | Strong component evidence, explicitly not a release certificate. Reference in-process Document reopen is not fresh-process user reopen; synthetic marked text is not real IME. |

**Origin finding:** the inspected evidence supports engineering capacity work
and numerical exploration, not the claim that Klee routinely needs 100-MB
editing or approved all these budgets. The right correction is not to deny the
old tests, nor to call every author-created number a user requirement.

There is a standards-versioning defect: successive documents introduce numbers
while retaining caveats, and later documents refer to "existing targets" without
identifying the exact authoritative table. Freeze one future acceptance record
with owner, approval status, context, endpoint, workload and evidence version.
Until then, historical hypotheses must remain visibly non-gating.

The demand document also says its new hypotheses "do not override any stricter
existing architecture or phase-specific budgets." Because the architecture's
numbers are themselves explicitly hypotheses, this creates a circular promotion
channel: a caution against weakening a contract can accidentally treat an older
unapproved number as an established stricter contract. The fix is to name the
approved requirement, not delete tests or claim these numbers are current hard
CI thresholds.

## 3. Are the current synthetic inputs representative?

The generator builds one fixture at a time, owns its content, differentiates
UTF-8 bytes and UTF-16 units, and preserves exact source outcomes. These are good
engineering choices. The concern is interpretation, not that synthetic data is
intrinsically invalid.

| Fixture | What the source and retained evidence establish | What it probes well | What it does not establish / replacement |
| --- | --- | --- | --- |
| `dense-json` | Exact 524,288 UTF-8 bytes, 484,573 initial UTF-16 units, 79,433 semantic tokens, zero initial diagnostics; generated by repeating a short object containing CJK/combining text, Boolean, number and small array. `EDIT_TARGET` is in the leading field. | A real demanding native style-publication mechanism; roughly 155 tokens/KiB shows why bytes alone are inadequate. Its 5.2–10.3-second Windows phase observations are not fabricated. | Not a measured common configuration/export distribution, not malformed recovery, not six-format semantics, not distant repair or physical key latency. Keep as dense-publication sentinel. Add an ordinary configuration task and an authentic export task *if supplied*; do not assume this density is rare enough to ignore. |
| `novel-text` | Exact 3,711,959 UTF-8 bytes, 1,278,983 UTF-16 units; one short original Chinese paragraph repeated, blank-line separators, trailing padding. `EDIT_TARGET` is at the start. | Few-MiB CJK paragraph import/layout/readback, Undo/Redo reimport and exact persistence. Size follows the self-report approximately; the byte target is not the user's exact novel. | Does not reproduce vocabulary/font diversity, chapters, punctuation variation, long paragraphs, find ambiguity, sustained reading, bookmarks or distant selection. Replace the *task claim* with reading/navigating a consented representative TXT or an explicitly surrogate varied-paragraph document; retain this deterministic import sentinel. |
| `mixed-text` | 36 repetitions of `iW中🧪éمرحبا\t` plus Chinese text and alternating CRLF/LF/CR. Marker is near the beginning. | Unicode scalar/surrogate, combining text, bidirectional text, tabs, newline mappings and cold font-fallback hazards. | Not ordinary novel composition, not natural multilingual paragraph order, not a real Pinyin session. Keep as adversarial mapping/shaping correctness coverage. Pair with a natural Chinese phrase and actual commit/cancel at the caret. |
| 100-MiB short lines and 50-MiB unbroken line | Existing reproducible capacity/layout counterexamples | Avoiding unbounded layout/copying, global commands, safe failure and capacity regression | Not primary user need, not mandatory ordinary typography campaign, not reason to keep an inferior input surface. Retain established support, and require new specialization only when a useful task or retained contract demonstrates failure. |

An input can be **ordinary in byte size and adversarial in semantic density**.
That does not invalidate its failure; it changes what conclusion the failure
supports. Conversely, a repetitive synthetic novel can *understate* some text
diversity costs while still exposing a significant import bottleneck. We cannot
deduce either "the product is unusable for all small files" or "the failure is
irrelevant" from these fixtures alone.

The prescribed insertion is seven UTF-8 bytes (`新🧪`) at a known leading marker.
It tests exact transaction plumbing, not finding a target. A real editor task
often includes locating context, verifying a diagnostic, composing, checking the
result and recovering from an error. That missing journey is the key gap.

## 4. Named standards disposition

Definitions: **KEEP** preserves a justified contract; **REVISE** replaces the
acceptance formulation; **DEMOTE** retains useful coverage at a lower claim or
priority; **REMOVE** rejects the specified gate/claim, not its historical data.

Several inspected documents already prohibit misleading certification and call
numbers hypotheses. The rows below audit both written formulations and their
possible promotion into gates; they do not assert that every rejected inference
is currently an enforced CI rule. The candidate-baseline repetition plan is
already scoped, and its statistical safeguards are not a defect.

| Named current standard | Decision | Concrete replacement / rationale |
| --- | --- | --- |
| One source document; no project/workspace/LSP dependency | **KEEP** | All acceptance tasks use one canonical document. Another file requires the established safe replacement/New/Open flow, not a new workspace feature. |
| Six formats with their real policy semantics, diagnostics, rendering and editing | **KEEP + REVISE evidence** | Markdown, TOML, JSON, YAML, CSV and Plain Text each need a frozen policy profile and a task oracle. The five structured formats additionally need their applicable grammar/semantic conformance oracles; Plain Text imposes no syntax or validity rules. A three-format timing slice cannot certify six-format delivery. Full semantic completion remains a release requirement; scopes/resources and outstanding unsupported cases must be explicit. `Provisional` is an honest interim state, not fulfillment. |
| Lossless encoding/newlines, one committed source/history, exact Save/reopen | **KEEP** | Unedited examination changes no bytes; explicit edits produce exact expected bytes/encoding; external conflicts and failed Save leave original protected and edits recoverable. No relaxed budget excuses truncation or replacement decoding. |
| One coherent editing locus and real CJK IME/accessibility | **KEEP** | Click/type/compose/select at one visible source location, one caret and one correct source text provider. Attended Pinyin commit/cancel and reader tasks remain separate from callback/tree probes. Avoid arbitrary aesthetic requirements that do not affect these outcomes. |
| Exactly 1/10/100-MiB primary workload matrix | **REVISE / DEMOTE stress** | Primary tasks use actual representative file sizes, including genuinely small configurations. Keep dimensional metadata and deterministic stress cases separately. Do not enumerate a size/shape/format Cartesian product before selecting a task. |
| 100 MB/MiB as flagship value or release-priority workload | **REMOVE claim / KEEP capacity** | Do not position the product around this without evidence. Retain global selection/find/copy/edit correctness, safe incomplete semantics and existing capacity contracts; no silent read-only degradation or page-local workaround. |
| 512-KiB / 79,433-token JSON as "ordinary editor acceptance" | **DEMOTE** | Retain a demanding semantic-style diagnostic and regression sentinel. Product acceptance adds a realistic JSON locate/fix/Save task. A representative dense task can later promote this stratum; byte size alone cannot. |
| Repeated 3.54-MiB TXT as representative novel workflow | **REVISE** | Retain import sentinel. Run a reading/navigation task with varied natural paragraphs and distant locations, labelling surrogate versus actual provenance. Do not demand another 100-MB file from Klee. |
| Mixed CJK/emoji/combining/Arabic string as normal reading/input distribution | **DEMOTE** | Correctness/adversarial suite; natural Chinese reading and actual IME are independent primary checks. No need to include every script in every screenshot. |
| Warm shell 150 ms / 1 MB editable 250 ms / 100 MB editable 2 s | **DEMOTE hypotheses** | Report separate shell/source-ready/task-response endpoints. Bind eventual budgets to a supported host and frozen authentic tasks, with owner approval; shell time is not task readiness. |
| Later ordinary 500-ms / action 50-ms budgets and 100-MiB 1-s launch | **REVISE status** | Keep as optional discussion hypotheses, not silently loosen/tighten earlier ones. Do not invent approval, and do not optimize against contradictory tables. Use current-task delays and paired reference evidence to decide whether a budget is meaningful. |
| Local typing p95 input-to-paint at most 16 ms on supported desktops | **REMOVE universal gate** | One 60-Hz frame interval is not a validated editor latency requirement and excludes physical input/display pipeline differences. Preserve responsive typing as a requirement; specify receipt-to-commit, draw submission and observed-visible endpoints separately, inspect missing/reordered input and visible stalls. Choose any eventual quantitative budget from the actual supported context. |
| Resident memory at most 2 times file size before full semantics | **REMOVE universal ratio** | Fixed application overhead makes the ratio arbitrarily large for tiny files. ASCII UTF-16 payload alone is roughly twice UTF-8 bytes; native replicas/indexes add more. Measure total memory and retained growth across authentic tasks, separately engine overhead and density stress. Approve a host/resource envelope rather than erase source semantics. |
| Native-source experiment must meet "existing candidate targets" | **REVISE** | Name a single approved endpoint/workload/host record, or explicitly call the experiment diagnostic/non-gating. Acceptance cannot depend on an unidentified inherited hypothesis. |
| Candidate baseline: three machines / 30 launches / 100 observations | **KEEP method / DEMOTE immediate priority** | Already a scoped quantitative plan, not a prerequisite for every decision. Keep sound clustering, raw failures and observer calibration. A small formative task can reject a broken interface; larger repetitions are required only for the numerical claim being made. Do not deploy a benchmark program before establishing task value. |
| Twelve visual states and broad later stress extension | **REVISE** | First observe the complete ordinary editing task in a default theme/scale, then use paired light/dark and high-DPI/high-contrast checks for specific visual risks. Extend to all formats for semantic presentations, not duplicated full task runs at every visual combination. Large-file visual stress stays separate. |
| Aggregate CI, test count, in-process reopen and synthetic marked-text evidence | **KEEP scoped evidence / REMOVE product-pass inference** | Existing checklist already rejects blanket certification; retain that rule. Product pass requires the observable user tasks, exact persistence, real input and supported reader checks. Unknown/skipped is not pass. |
| New UI toolkit or JIT runtime automatically improves UX | **REMOVE inference** | Compare the same frozen task/controller/engine behavior; distinguish packaging, runtime and surface. No migration is justified by a synthetic phase result alone. |

### Why the memory ratio is structurally wrong

Let file size be `n`, fixed process overhead `B > 0`, decoded source/metadata cost
`c*n`, and other derived work `D`. Then `M/n = B/n + c + D/n`. As `n` becomes
small, the ratio grows independently of implementation quality. For ASCII input,
UTF-16 characters already contribute about `2*n` bytes; a native full replica
can add another comparable payload before the process and indexes. This does
not bless inefficient copying: it shows why an unconditional `M <= 2*n` is the
wrong contract. Bounded overhead, no copy queue, no leak and reasonable total
memory on the user's machine are testable and relevant alternatives.

## 5. Replacement task protocol: smallest useful next evidence

This is a specification for a **later explicitly authorized** observation, not
an instruction to run GUI/benchmarks during this audit. No analytics service,
new benchmark harness, new runtime or participant recruitment campaign is
needed for the first decision.

### 5.1 Keep four evidence strata distinct

| Stratum | Purpose | Pass means / does not mean |
| --- | --- | --- |
| P: product tasks | Does the intended user finish useful ordinary work? | Correct task completion and understandable interaction. One session is formative evidence, not a population prevalence or p95 claim. |
| S: semantic conformance | Are all six required format semantics correct? | Named grammar/profile, full outcomes, rendering/source-map invariants and recovery oracles. Not UI speed. |
| C: capacity/robustness | Do existing supported commands survive limiting/adversarial shapes safely? | Exact source, global commands and honest resource/recovery outcomes. Not user demand. |
| M: mechanism diagnostics | Why does a measured operation cost time/memory? | Valid phase/observer evidence. Not automatically a product or migration gate. |

A fixture/task may participate in two strata, but the claims must stay separate.
Promoting a case requires evidence of a useful user task or an established
supported contract, not the fact that we already spent time optimizing it.

### 5.2 Minimum product task cards

First use one intended-user session and a small frozen corpus. Ask for the last
ordinary episode/files if useful and consented, **not for a 100-MB example**.
If files cannot be shared, reconstruct metadata and use a clearly labelled
nonprivate surrogate. Do not claim a hypothetical task is observed demand.

| Task card | File provenance / scope | Observable journey and completion oracle |
| --- | --- | --- |
| P1: inspect and repair a configuration | Recent ordinary JSON, YAML or TOML, typically below the reported 1-MB region; exact size follows the episode, not a minimum | Open; find a stated key/problem using source/diagnostic/structure; inspect context; alter one intended value or repair syntax; Undo/Redo; Save; close/reopen in a fresh product process. Expected semantic outcome and exact whole-file bytes must agree. |
| P2: edit Chinese prose at the source caret | Natural Chinese Markdown or TXT, with a heading/paragraph context | Find a phrase; select; enter/cancel actual Pinyin; commit intended phrase once; move/scroll away/back; Undo/Redo; Save/reopen. No duplicate destination, wrong-target edit, lost composition, unexpected newline/encoding change or selection drift. |
| P3: read/navigation in a few-MiB TXT | Consented representative reading file, or varied original surrogate around the reported 3.54 MiB; labels distinguish them | Read successive sections, search a repeated/unique phrase, navigate to a middle/end section, select/copy an excerpt and return. Opening/reading leaves disk unchanged. If an edit is actually part of the user's episode, append exact persistence; do not invent one solely to justify an editor benchmark. |
| P4: structured table capability | CSV; real task if available, otherwise explicitly a required-capability check rather than demand | Locate a named row/cell; verify source/derived Grid correspondence where supported; edit a field value; diagnostics/structure/rendering reflect the current version; Save/reopen exact. Quoted commas, escaped quotes and multiline fields must retain the defined CSV semantics. |

These cards cover the dominant interaction and locate/fix/read distinctions.
They do not claim Klee currently uses every format. Product demand and the
explicit six-format capability contract are different reasons for coverage.
For coverage, P1 variants cover JSON/YAML/TOML, P2 covers Markdown, P3 covers
Plain Text and P4 covers CSV. Give each policy at least one complete
analysis/edit/render/Save chain; add a deliberate local Plain Text edit/Save
capability check separately if the observed P3 reading task does not involve an
edit. This check is not evidence that novel readers want editing. Reuse shared input/history
checks instead of repeating an entire IME campaign for every format.

### 5.3 Semantic standard must not be shrunk to "these samples passed"

The active six policies are Markdown, TOML, JSON, YAML, CSV and Plain Text;
their source scopes are recorded in [format contracts](../../src/Mote.Formats/README.md)
and [the static registry](../../src/Mote.Formats/Contracts.cs).
For each structured format record its supported specification/profile, critical
semantic relations, recovery behavior and independent expected results. Include
representative valid source, one intentional error and the format's relevant
nonlocal relation (for example references/aliases/ownership where applicable).
Cross-check suitable existing conformance/differential evidence without treating
another parser as infallible. Freeze source spans, source/derived navigation,
current-generation/version publication, full completeness and exact persistence
oracles. Preserve existing exhaustive/model/regression tests rather than replace
them with one screenshot.

Plain Text is not a sixth structured grammar. Its
[policy](../../src/Mote.Formats/PlainTextPolicy.cs) imposes no syntax or semantic
validity rules: analysis exposes the whole document, produces no syntax
diagnostics/tokens, formatting preserves the text, and HTML rendering escapes
the source in a preformatted view. Its
[snapshot session](../../src/Mote.Formats/PlainIncrementalSession.cs) must preserve
the full-completeness, exact-source and version/coverage contract. Test literal
text preservation, safe escaped rendering, unknown-extension fallback,
selection/input/history and exact persistence rather than inventing malformed
Plain Text, element/attribute validation or markup escaping rules for it.

`Full` requested, `Complete` result and six-format *general semantic adequacy*
are different assertions. All must be scoped honestly. A small byte size cannot
authorize silently skipped semantics, but neither can a finite test corpus prove
every possible input. If the target grammar/profile is underspecified, list that
as a standards-definition gap; do not close it by accepting perpetual partial
results. Expensive global analysis can be asynchronous and cancellable while
editing stays available. That scheduling choice does not remove the promised
semantic capability.

### 5.4 Required record, states and outcomes

For each task freeze:

- Intended user and concrete goal; observed episode, explicit capability or
  hypothesis; file consent/provenance; format and task answer/edit oracle.
- Bytes, encoding, UTF-16 extent, line count/max line length, meaningful
  record/token density, validity and action locations when known. Avoid raw
  private document content in telemetry. Derived counts are not demand proof.
- Product binary/hash, active surface and configuration; OS/hardware/display;
  launch/cache/focus state and actual input method. Do not compare changed
  toolkit, runtime and corpus as one causal contrast.
- Start/end definitions: request-to-usable-source, task action-to-observed
  response, semantic publication and Save completion separately. Draw-return,
  external message, physical input and displayed response keep distinct labels.
- Completed/correct, completed with assistance, error, abandoned, timed out,
  unsupported and unknown. Preserve retries/failures; no non-idempotent edit
  resend on timeout and no first-success filtering.
- Task time, error/assistance count, correctness, waiting interruptions and
  user's explanation of the edit target. Satisfaction/preference is evidence
  about experience, not a substitute for byte safety.

The main state journey is:

```text
Open request -> usable exact source -> locate/understand -> compose/edit
            -> semantic/current-source check -> deliberate Save
            -> exact persisted snapshot -> fresh-process reopen
```

Necessary recovery paths remain: unsupported encoding requires explicit
choice; malformed but supported source stays inspectable/editable; incomplete
analysis is visibly incomplete; composition cancel commits nothing; Save
conflict/failure protects the original and leaves recoverable edits; abandoning
dirty work requires the established consent flow. Do not grow a list of invented
rare states beyond current contracts and discriminating regressions.

### 5.5 Decision rule before numerical gate-setting

1. First reject data loss, wrong edit target, lost/duplicate input, false semantic
   completeness, broken global commands and incomprehensible edit destination.
   A faster median never offsets these failures.
2. Observe whether the useful task completes and where waiting/interruption
   happens on the intended supported machine. Keep familiar tool comparison
   task-equivalent if useful; do not charge existing scripts setup twice or
   compare only the part where mote looks favorable.
3. If an ordinary task fails, attribute the failing endpoint before proposing a
   change. Existing multi-second dense/native-import observations are valid
   warning signals and profiling candidates, not permission to run a fix now.
4. Only then freeze owner-approved numerical budgets for named endpoints on a
   named supported hardware/context. Existing 16/50/150/250/500-ms values are
   hypotheses; this audit neither approves new values nor excuses measured
   regressions in supported ordinary tasks.
5. Use adequate independent repetitions only for the distribution/regression
   claim being made. One attended task is valuable formative evidence; three
   synthetic successes cannot substantiate dependable p95. Preserve calibrated
   observer overhead, raw outcomes and process clustering when timing is used.

Success of this first stage is one completed authentic ordinary journey,
coverage/correctness records for all six capabilities, and an explicit decision
on the actual remaining obstacle. It is not a finished release certificate.

## 6. Product/UI quality and migration: what evidence does and does not justify

The retained two-line `alpha`/`beta` theme captures establish selected palette
and a scoped typography issue/correction. They cannot establish readable
structured content, composition understanding, accessibility or general polish.
The closed typography fix should not be redone. The duplicate source/ribbon
appearance raises a real coherence question, but a screenshot alone cannot prove
how often the user makes a wrong-target error. [Visual assessment](../product/native-canvas-visual-acceptance.md)
already separates these facts correctly; task acceptance must retain that
discipline.

The reasonable UI standard is **one understandable edit destination, readable
content/states at supported scale, usable keyboard/IME/reader behavior, stable
source/derived navigation and safe recovery**. Pixel identity with VS Code or
JetBrains, always-dark native chrome, a mandatory decorative gutter, bundled
fonts and every-script-at-once screenshots are not demonstrated local needs.
Theme/scale/high-contrast checks still matter; they should answer concrete
legibility/hit/caret/diagnostic questions, not become unrelated polishing work.

The current [UI migration research](delivery-ui-migration.md) supports these
limited conclusions:

- Relaxed delivery can permit standard macOS application packaging without
  replacing the source surface. It does not itself authorize a delivery change.
- Avalonia/AvaloniaEdit supplies useful reusable UI/editor machinery and has
  production usage signals. Those signals do not establish mote's Chinese
  preedit, exact engine-history bridge, accessibility or task outcomes.
- Inspected pinned/current editor source and upstream issue/PR state identify
  a preedit capability gap. Toolkit-level IME support cannot override
  editor-specific evidence. Qualifying a fix/fork is actual migration work.
- Historical 100-MiB/long-line full-mirror costs reject unconditional full-mirror
  promotion at that capacity. They do **not** reject every ordinary-file use of
  the toolkit, nor justify retaining inferior ordinary interaction indefinitely.
- Native-source reference probes establish useful exactness/mechanism facts,
  not an accepted default. Current Windows import/style delays deserve attention
  if the supported task traverses them, but a phase result alone does not prove
  the replacement UI wins.

Therefore migration approval requires completing the same P tasks and S/C
contracts through the candidate, preserving CLI/home/config/theme identity,
encoding, history, Save, global selection and recovery. Compare ordinary benefit
and maintenance burden, not only the most pathological old fixture. Packaging
may be independently worthwhile; bundling JIT is not a demonstrated cure for
RichEdit/TextKit calls. Rollback must preserve dirty source and settled input.

## 7. External grounding: standards describe context, not magic file sizes

New sources were opened on 2026-10-02 to address a gap not settled by earlier
performance papers: how to specify/report usability requirements themselves.

**Industry/standards grounding.** NIST's [Common Industry Specification for
Usability -- Requirements](https://www.nist.gov/publications/common-industry-specification-usability-requirements)
links requirements to their test method and context. Its [CIF explanation](https://www.nist.gov/itl/iad/human-centered-technologies/what-cif)
requires participant/context/apparatus/design reporting and distinguishes
performance from satisfaction. CIF is a reporting form for summative tests, not
a rule that choosing a form makes a task realistic. The first Klee session here
is formative, not a claimed CIF-compliant certification. The applicability of
context-specific usability is also described by [ISO 9241-11:2018](https://www.iso.org/standard/63500.html);
the public abstract is not a full standards-conformance audit.

**Academic connection, reused rather than inflated.** [Liu and Heer, IEEE TVCG
2014](https://idl.uw.edu/papers/latency) experimentally found that an added
500-ms delay changed exploratory visual-analysis activity/coverage and strategy.
It supports protecting short exploration loops, not converting that number into
an editor launch or key-response gate. [DesignChecker, UIST 2024](https://doi.org/10.1145/3654777.3676369)
([author manuscript](https://arxiv.org/abs/2407.17681)) separates visually
readable design from screen-reader accessibility in web-development studies.
Its connection is to inspect both rendered tasks and assistive behavior; neither
the web domain nor a small study proves mote's native interface is confusing.

The existing latency research's hierarchical repetition and endpoint distinctions
remain technically sound. Its unresolved problem is *construct selection*: a
well-measured synthetic operation can answer the wrong product question. This
audit supplies the missing link from user goal -> task -> input provenance ->
endpoint -> oracle -> approved gate, while keeping established correctness.

## 8. Consequential unknowns and the next decision artifact

| Unknown | Smallest useful validation / decision |
| --- | --- |
| Which ordinary task Klee actually performs most often | Reconstruct one recent ordinary episode; consented file or metadata/surrogate. Not another general market study or a request for a huge file. |
| Whether the current edit locus is understandable in real use | One attended click/select/Pinyin commit/cancel/Undo journey; record target explanation and errors, not just a screenshot. |
| Which hardware/endpoint budgets the owner intends to promise | One frozen approval table after the task pilot. Keep unapproved numbers out of hard gates meanwhile. |
| Exact completeness boundary of each of all six formats | A profile/oracle/gap record using existing conformance evidence; keep unresolved semantic gaps open rather than lower the requirement. |
| Whether a replacement surface improves the actual experience | The same task and correctness protocol with side-by-side candidate/default identity, after input/safety capability qualification. No runtime/toolkit rewrite during this audit. |

**Next artifact:** a short task/standard approval record with P/S/C/M labels,
actual provenance, one authoritative budget table (or explicitly no numerical
gate yet), and open semantic/input/reliability failures. Update the owning
architecture/checklist/latency documents only after coordinating that decision;
this independent note does not silently supersede or edit their contracts.

**Bottom line:** the standards need correction, not blanket lowering. Keep the
strong source/semantic/product invariants; remove unsupported universal gates;
stop treating a synthetic capacity or publication fixture as the reason the
user wants an editor. That is a more demanding standard of product honesty,
while allowing engineering effort to serve the dominant real task.
