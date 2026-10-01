# Editor experience standards: HCI and production evidence audit

Date: 2026-10-02. Status: independent evidence audit and proposed standard changes,
not product-owner approval, implementation, benchmark results, or release certification.
Repository HEAD inspected: `6827cd1a19142c9ad766d296c18d0770e600e79c`.
Only this document is owned by this investigation. No build, GUI experiment, CI,
production code, or commit was performed. Sources below were opened on this date.

## Answer and decision boundary

**Keep demanding correctness and a usable ordinary editor; revise the grounds on
which performance targets become binding.** Neither 16 ms nor 100 ms is a universal
human threshold that can certify typing, scrolling, launch, navigation, and full
semantic analysis together. A frame budget is not a physical input-to-visible
latency budget. Conversely, lack of a validated numerical threshold does not make
multi-second stalls in ordinary editing acceptable: investigate the user's actual
failed interaction rather than wait for a literature-derived magic cutoff.

For this intended user, ordinary sub-1 MB files and occasional few-MiB continuous
reading belong in the primary acceptance story. 100 MiB and dense adversarial
fixtures belong in capacity, correctness, and regression protection, not a claim
of representative use. This changes priority and standard classification, **not**
six-format semantics, exact text ownership, reversibility, or existing capacity.

## Internal evidence reused before external research

- [Delivery frontier](delivery-research-frontier.md) separates delivery topology,
  execution model, and UI implementation; no packaging change proves better input.
- [Large-file demand](../product/large-file-demand-and-experience.md) records Klee's
  self-report: files rarely reach 1 MB; one completed novel is 3.54 MiB. This is
  direct evidence for one intended user, not a market-size estimate or inspected
  corpus. It already labels its 500/50/100 ms budgets as unapproved hypotheses.
- [Native latency acceptance](../native-latency-acceptance.md) separates six endpoint
  families, clocks, proxies, foreground capability, and uncertainty. It explicitly
  refuses invented product limits. Its 1/10/100 MiB synthetic matrix answers an
  engineering coverage question; it is not a target-user workload distribution.
- [Architecture](../architecture.md), section 4, calls its targets hypotheses but
  lists typing p95 input-to-paint <=16 ms and resident memory <=2x file size. These
  are not demonstrated requirements or literature results. A ratio alone is poorly
  suited to tiny files because runtime/UI fixed costs remain nonzero.
- [Delivery alternatives](delivery-constraint-alternatives.md) records ordinary-size
  native publication/import taking seconds in scoped hosted observations. These
  are actionable failures to explain, not distributions or runtime comparisons.

## Five primary sources: actual claims and transfer limits

### 1. Deber et al., CHI 2015: perceptibility is task/device dependent

**How Much Faster is Fast Enough? User Perception of Latency & Latency Improvements
in Direct and Indirect Touch**, peer-reviewed CHI proceedings,
[DOI](https://doi.org/10.1145/2702123.2702300),
[opened full text](https://cliftonforlines.com/papers/2015_FastEnough.pdf).

Experiment 1 used 14 right-handed participants over 24 tapping/dragging sessions,
a custom low-latency touch sensor/projector, and forced-choice adaptive comparisons
against a 0.98 ms reference. Mean just-noticeable differences were 11/55 ms for
direct/indirect dragging and 69/96 ms for direct/indirect tapping. These are
psychophysical discriminability results, **not** acceptable-wait ratings, task
completion targets, or desktop keyboard/Chinese IME measurements.

**Transfer:** high confidence that a single cross-action perceptual threshold is
unsound; low confidence in transferring any reported number to mote. Distinguish
continuous visual tracking from discrete command response, and perceptibility
from usefulness. The paper does not license a 100 ms universal release gate.

### 2. Liu and Heer, IEEE TVCG 2014: useful exploration differs from reported feel

**The Effects of Interactive Latency on Exploratory Visual Analysis**, peer-reviewed
journal / InfoVis paper, [author page](https://idl.uw.edu/papers/latency),
[opened full text](https://idl.cs.washington.edu/files/2014-Latency-InfoVis.pdf),
[DOI](https://doi.org/10.1109/TVCG.2014.2346452).

Sixteen participants experienced two exploratory visualization sessions with
counterbalanced datasets/order; one condition injected an additional 500 ms into
querying operations. Logs and think-aloud records showed reduced activity,
coverage, and observation/generalization/hypothesis rates. Six participants did
not report a responsiveness difference; 15 did not think delay changed their
interactions. Effects differed by operation.

**Transfer:** moderate confidence that repeated inspect/search/context loops
should measure correct discovery and exploration behavior, not only subjective
speed or first-window timing. This controlled visualization setting is closer to
inspection than a reflex-only task, but is still not a natural mote workflow.
It establishes neither a 500 ms absolute editor limit nor a 50 ms typing target.

### 3. VS Code 1.73, October 2022: put visible input ahead of secondary work

The official [release engineering account](https://code.visualstudio.com/updates/v1_73#_optimizing-for-input-latency)
describes inspecting the keystroke path and deferring work until after rendering.
The team estimated about 15% lower input latency without IntelliSense and greater
improvement while refiltering. This is a historical team estimate, not an
independently reproduced benchmark or a current VS Code latency distribution.

**Transfer:** high confidence in treating the entire keystroke-to-render path as
the optimization object and separating useful immediate feedback from auxiliary
analysis. This corroborates mote's existing scheduling invariant; it does not
justify weakening canonical edits, semantic correctness, or stale-result rejection.

### 4. Zed 0.121, February 2024: fast render duration does not certify delivery

The official [Metal pipeline investigation](https://zed.dev/blog/120fps) describes
scroll jank on hardware/display modes not reproduced on developers' initial
machines. Later, cursor frames dropped despite measured frame times below 4 ms;
display refresh behavior mattered. Relaxing GPU synchronization initially caused
buffer races and raster corruption, requiring lifecycle-safe buffering.

**Transfer:** high confidence in testing delivery/display behavior rather than
equating draw duration with smoothness, and in retaining correctness while
removing waits. This is a platform-specific historical incident, not proof that
GPU UI is always faster, all native controls are slow, or mote needs Zed's renderer.

### 5. JetBrains Rider help: text capacity and optional intelligence are separate

The opened official [Configure file size limits](https://www.jetbrains.com/help/rider/Configuring_File_Size_Limit.html)
page labels itself Rider 2026.2 Help (footer 09 October 2024). It documents default
open and coding-assistance limits of 20000 and 2500 kilobytes, respectively, with
design-time inspection disabled by default above 300 kilobytes and an explicit
OFF indication. Limits are configurable.

**Transfer:** high confidence that mature editors distinguish loading, editing,
and derived intelligence, and expose disabled work. These are product choices,
not human thresholds. Do not copy their cutoffs, claim all JetBrains products have
identical behavior, or use this as permission to silently drop mote's six-format
semantic contract. Partial status and eventual/on-demand complete work must remain
honest under mote's existing contract.

## Proposed audit dispositions

These are recommendations for owner review, not edits to the cited contracts.

| Standard or claim | Disposition | Replacement / rationale |
| --- | --- | --- |
| Exact canonical source, no lost/reordered input, undo/redo, safe Save/reopen | **KEEP** | Functional acceptance remains binding at every supported size. A fast incorrect edit fails. |
| Six-format semantic commitments and explicit Complete/Provisional status | **KEEP** | Full coverage may complete later; timing never grants permission to lie about completeness. |
| Existing capacity, long-line, dense/malformed/Unicode regression cases | **KEEP** | Preserve no-crash, source/history/Save, boundedness and semantic honesty; do not erase established protection. |
| 100 MiB/dense synthetic success as representative product excellence | **DEMOTE** | Classify as stress/capacity evidence until a valuable observed user task makes it primary. No new 100 MiB specialization solely to improve a trophy. |
| 1/10/100 MiB Cartesian matrix as ordinary experience workload | **REVISE** | Primary: consented actual sub-1 MB files/tasks plus occasional few-MiB reading. Engineering matrix remains separate and selective. |
| Typing p95 <=16 ms as universal perceptual or release truth | **REMOVE** the universal interpretation; **REVISE** the hypothesis | 1000/60 = 16.67 ms is one 60 Hz frame period; 120 Hz gives 8.33 ms. End-to-end response crosses input, dispatch, application, rendering, composition and refresh. Identify endpoint and hardware before approving a budget. |
| Navigation/cancellation <=100 ms; input p95 <=50 ms | **REVISE** | Existing proposed numbers can remain labeled trial budgets. Validate each action/task; neither cited HCI paper certifies them. |
| “Instant” / “smooth” / “native therefore responsive” without endpoint evidence | **REMOVE** | State observed endpoints, hardware, distributions, outcomes and unresolved gaps. Window shown or draw return alone is insufficient. |
| Resident memory <=2x bytes as one global rule | **REVISE** | Separate fixed app cost, size-dependent cost, peak, retained growth, paging and completeness stage. A slope/envelope on representative hardware is more meaningful than a tiny-file ratio. |
| Multi-second ordinary editing/import as acceptable because no universal threshold exists | **REMOVE** | Treat visible broken workflow as a concrete defect candidate; owner/task evidence determines urgency, profiling determines cause. |

## Smallest informative future protocol (not executed or commissioned here)

1. **Establish tasks before thresholds.** Ask the intended user for a few local,
   privacy-safe actual files and tasks: read Chinese, search and inspect context,
   modify one value with real CJK composition, undo, Save and reopen. Include all
   six formats' semantic correctness checks separately; do not require equal
   frequency merely because six formats exist. Use sanitized or generated
   substitutes only when their shape preserves the relevant task properties.
2. **Separate three evidence questions.** (a) Does the task complete correctly?
   (b) Does the user experience interruptions or lose context? (c) Which measured
   phase causes them? Retain perceived responsiveness and task outcome together.
   Asking only “did it feel fast?” can miss behavior changes; timing alone can
   miss confusing navigation or composition failures.
3. **Use the existing endpoint contract.** Record foreground, physical input vs
   automation, real IME, native surface, refresh/DPI, machine, launch/cache state,
   binary provenance and observation overhead. Keep launch-to-usable, first
   visible response, semantic completion, search first-result/full-scan, scroll
   delivery, cancellation, and exact Save completion separate. Do not subtract
   unmapped clocks or substitute draw submission for photons.
4. **Avoid learning and observer artifacts.** Rotate comparable tasks and
   before/after order; do not repeat the identical discovery problem and call
   learned answers a speed gain. Keep natural silent task trials apart from
   diagnostic think-aloud or latency-focused comparison, which changes attention.
   Compare with the user's familiar editor on the same host/task; vendor branding
   and a broad editor ranking are not the experiment.
5. **Approve a budget only with its scope.** Product owner records action,
   workload, hardware class, acceptable interruption/failure rate and numerical
   hypothesis. Retain raw observations, failures/censoring and clustered-process
   uncertainty per native-latency guidance. A few successful tasks can reject an
   obviously bad design but cannot certify stable p95/p99. Never pool a stress
   fixture's timings into ordinary-use percentiles.

## What would materially change this assessment

Independent actual tasks frequently requiring 100 MiB full-semantic editing would
promote that stratum; they would not retrospectively make the old synthetic corpus
user research. Reproducible same-task physical input/display evidence could turn
one candidate budget into a justified local contract. Until then, confidence is
high in the distinctions above, moderate in ordinary-task prioritization for this
one intended user, and low in any universal numerical promise. The decisive next
unknown is the ordinary user's completed task on representative hardware, not
another unconstrained literature search or a wider benchmark matrix.
