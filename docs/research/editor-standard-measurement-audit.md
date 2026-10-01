# Editor experience standards: independent measurement audit

Date: 2026-10-02. Status: **standards review completed; no GUI execution,
build, test replay, benchmark timing, CI dispatch, production edit, commit or
push performed**. This document is a recommended disposition, not an implicit
change to current contracts or an approved release gate.

## 1. Decision

The existing measurement documents are usually careful about endpoint limits.
The main problem is not a missing benchmark framework. It is that a large and
growing collection of *capability, correctness, stress and diagnostic* results
can be mistaken for an ordinary editor experience standard. Several green
capability journeys contain multi-second synchronous phases. Conversely,
excellent engine-edit or process-control numbers do not measure usable source.

Keep exactness, owned-process cleanup, causal/revision checks and the retained
negative evidence. Revise the primary workload around ordinary files and the
user's few-MiB reading task. Demote huge synthetic coverage and all-document
attribute publication to the questions they actually answer. Remove the
inferences `workflow green => fluent editor`, `source installed => visible`,
`draw returned => physically presented`, and `120 s completion => acceptable`.
Do **not** delete the difficult fixture, reduce token counts or lengthen a wait
to manufacture acceptance.

The replacement is a small **layered acceptance contract**, not another
Cartesian-product benchmark campaign:

```text
hard correctness and compatibility
              |
ordinary task capability + exact outcome
              |
user-facing latency and resource envelope on representative hardware
              |
diagnostic attribution / capacity stress (separate results, not substitutes)
```

This audit reuses the filename-led repository knowledge in
[latency acceptance](../native-latency-acceptance.md),
[large-file demand and experience](../product/large-file-demand-and-experience.md),
[goal checklist](../product/goal-checklist.md),
[native baseline](../native-performance-baseline.md), benchmark READMEs and
measurement source. It cheaply decoded already retained generated input bytes
and existing JSONL reports; no user file was opened and no timing was rerun.

## 2. Keep the endpoints distinct

| Existing signal / path | What it actually measures | Disposition and replacement label |
| --- | --- | --- |
| `tests/Measure-NativeStartup.ps1`, `--smoke-gui`, console `mote-native-gui-ready` | Parent launch through smoke process exit. `Program.cs` prints at `shell.Shown`, queues close and drains telemetry. No requested-file task is certified. | **DEMOTE** to GUI lifecycle / packaging diagnostic. **REMOVE** any first-usable, first-pixel or cold-disk interpretation. Preserve the existing metric/schema for compatibility. |
| Headless JSON console-ready control in `delivery-runtime-performance.md` | Launch plus the frozen control's parse-ready work, output observation and runtime regime. | **KEEP** as a runtime mechanism control; **DEMOTE** from editor startup evidence. A marker's name cannot upgrade its endpoint. |
| `NativeStartup/Measure-WindowsOrdinary.ps1` source prefix + selection acknowledgement | Exact bounded source-island binding and direct native-message selection. Parent polling is included; selected local runs had foreign foreground. | **KEEP** as binding/capability. **REVISE** ordinary UX startup to require requested-file source identity, focus/first-responder, one externally delivered reversible action and a separately qualified visible-source response. |
| `mote.startup_to_editable` | Child interval starts after configuration; initial source installation can be empty-buffer startup rather than requested-file readiness. | **KEEP** attribution, **REMOVE** its use as whole process/config/requested-file launch time. Retain separate parent launch and `document.open_to_editable`. |
| `document.open_to_editable` | Accepted open to canonical-source installation return, not physical paint or full semantics. | **KEEP** internal install endpoint. UI input acknowledgement and source visibility remain separate evidence. |
| `document.edit_to_presentation` | Semantic acceptance after debounce, not native drawing despite the historical name. | **REVISE** prose/report alias to `edit-to-semantic-publication`; **KEEP** trace wire name to avoid breaking consumers. Do not apply a 50 ms visual-response hypothesis to a deliberately 80 ms-debounced semantic endpoint. |
| `document.*_to_draw_submission`, in-process `FlushDraw` phases | Version-matched native drawing callback return / forced diagnostic draw, respectively. | **KEEP** causal correctness and rendering attribution. **DEMOTE** forced drawing from natural interactive timing; neither certifies compositor present or photons. |
| `NativePaintLatency` GDI screen change | First source-specific changed image observed through sampled capture, with capture/scheduling costs. | **KEEP** software-visible response proxy, with provenance and observer bounds. **REMOVE** an exact physical-presentation interpretation. WGC clock anomalies already prohibit a present-latency claim. |
| Exact Save SHA + fresh `Document.OpenAsync` in source-capability | Controlled save to a new path and a fresh engine object. | **KEEP** hard byte/history contract. **REMOVE** inference of native Save-command routing, overwrite reliability or fresh GUI-process reopen. Use the separate reviewed product-route Save/reopen driver for those. |
| `NativeAcceptance/acceptance.py` integrity pass | Bounded schema/causal/count/loss/session audit and synthetic corpus preparation. | **KEEP** artifact correctness gate; **REMOVE** any performance-pass meaning. The tool does not execute a GUI. |

These distinctions are valuable, not pedantry: a first source draw can precede
semantic publication, and a source-control message can succeed while the user
cannot type into the foreground window. There is no single interchangeable
`ready` state. Parent and child clocks remain separate; do not derive missing
phases by subtracting unrelated clocks or summing probes with overlapping work.

## 3. Actual retained fixtures: useful but not representative by byte size alone

Source: `src/Mote.Native/NativeSourceDiagnosticFixtures.cs`. The following counts
were computed from the retained x64 baseline's generated `.input` bytes under
`.cache/ci-36887820181-codec-supervisor/win-x64/source-capability/probe/`.
UTF-16 counts match the report. Line counts below use Python `str.splitlines()`;
LF/CR are literal code-point counts, so mixed-newline meanings remain explicit.

| Fixture | Exact UTF-8 bytes / UTF-16 units | Observed structure | Standards disposition |
| --- | --- | --- | --- |
| `mixed-text` | 2,875 / 1,219 | 38 split lines; 36 repetitions of the mixed-script line; LF 26, CR 25; supplementary scalar, combining mark, Arabic, tab and CR/LF/CRLF. | **KEEP** Unicode/newline fidelity and cold native setup control. **DEMOTE** from real IME/bidi interaction, grapheme navigation or screen-reader acceptance: including characters is not executing those workflows. |
| `novel-text` | 3,711,959 / 1,278,983 | 31,192 identical 39-code-point paragraphs; 62,386 LF; 31,193 empty lines; 4 unique line strings; 98-space final padding line; 1,216,488 non-ASCII code points. Plain policy, zero semantic tokens. | **KEEP** exact few-MiB CJK capacity/import regression. **REVISE** representative-reading evidence with varied paragraph lengths, dialogue/punctuation, chapter structure, occasional long paragraphs and real reading/navigation tasks. This is a size surrogate, not a sanitized real novel. |
| `dense-json` | 524,288 / 484,573 | Valid JSON with 7,943 identical row objects; 7,945 LF; 5 unique line strings; longest line 60 UTF-16 units; 79,433 semantic tokens, approximately 155.14 tokens/KiB, 6.60 bytes/token. | **KEEP** dense all-token publication regression and exact preservation. **DEMOTE** its prevalence/typicality claim pending user evidence. **REMOVE** byte-size-only reasoning that calls every 512 KiB file equivalent. |

The JSON row has multiple keys, scalars and a three-element array, so the high
token density is not malformed padding or an impossible adversarial construct.
Repetition simplifies semantic diversity but still creates real style-run work.
The correct conclusion is **a plausible, deliberately dense structured-text
probe exposes a severe representation cost**, not “all ordinary JSON files
take this long” and not “synthetic means irrelevant.” A single huge string has
the same bytes but far fewer style spans; replacing dense JSON with that control
would answer a different question.

Retained baseline input SHA-256 identities (the same three identities are
reported by the newer four-RID capability journeys):

| Input | SHA-256 |
| --- | --- |
| `mixed-text.input` | `2EA48A1A0E8EBB3064C236083657A29857586268E654DABE719F1E995C8CEBD6` |
| `novel-text.input` | `CDFF59D22B1AF5595A049D9324212389ED2E534A173CB47268349DA0BCD8AE19` |
| `dense-json.input` | `B16CAA47A0D80AC14D873A2011E2360ABA7C877DD56FB90C9C99C3CE67BE4257` |

The novel is similarly not a realistic reading distribution. Its ~39-character
paragraphs and alternating blank lines favor one particular wrap/line-index
shape. Identical CJK characters repeatedly exercise the same glyph/font set.
It does not reproduce varied novel typography, long dialogue or search-result
distribution. Because fixtures run in fixed mixed-text -> novel -> dense order
in one process, the novel is not native-first-use and the dense case can inherit
prior resident allocations. Independent fixture processes/rotated order are
needed for a later startup or memory distribution, not for the completed
capability result.

### All offscreen styles: capability contract versus product requirement

The current common `Publish` analyzes the complete source, projects every token
and calls `PublishStyles` for all spans at initial/edit/Undo/Redo states. Its
verified interval includes preservation oracles. This is an intentional
**full-resident backend capability stress**, not evidence that every invisible
span must be synchronously styled before the user can read or type.

For an unchanged-backend before/after experiment, preserve all spans, source,
order, overlap semantics and oracles; dropping them would invalidate comparison.
For a separately approved product architecture, distinguish full *semantic*
coverage from native *attribute materialization*. Exact current-revision styles
must be correct when a region becomes visible; style state must not corrupt
text/history/selection or expose stale colors as current. Lazy/bounded style
materialization is only an alternative if those invariants, native editing and
accessibility survive and distant navigation has its own bounded response.
It cannot silently convert `Full` semantics into an incomplete analysis, and
the old all-token probe must remain identified rather than relabeled as passed.

## 4. Green capability is not experience acceptance: actual stage evidence

The earlier [timeout attribution](../performance/native-source-capability-timeout.md)
correctly retains completed first publication and the censored second one.
In run `36887820181`, Windows initial dense verified publication completed in
78,852.2383 ms (x64) / 73,852.4120 ms (ARM64); the post-edit publication entered
but had no terminal row before the whole-process 120 s deadline. Neither the
missing terminal nor `120 s - sum(completed phases)` is a valid final duration.

This audit also inspected the already downloaded newer run
[36899695843](https://github.com/kleedaisuki/mote/actions/runs/36899695843), under
`.cache/ci-36899695843-codec-source/artifacts/`:

| RID | Dense verified publication: initial / post-edit / Undo / Redo, ms | Novel native import: initial / Undo / Redo, ms |
| --- | --- | --- |
| win-x64 | 7,587.9403 / 10,256.3977 / 7,610.0333 / 7,776.7348 | 4,898.8882 / 4,667.2545 / 4,621.8596 |
| win-arm64 | 5,223.9037 / 7,796.1089 / 5,166.1181 / 5,380.0477 | 3,875.6692 / 3,818.7237 / 3,810.1724 |
| osx-x64 | 480.7010 / 228.6858 / 485.7956 / 576.1201 | 5.9238 / 3.8722 / 3.9611 |
| osx-arm64 | 142.4718 / 80.9661 / 157.1731 / 170.1695 | 1.9959 / 2.1165 / 1.8053 |

All four newer reports have 301 valid JSON rows and end with
`phase=probe,state=complete`. Their supervisors record observed exit 0, no
timeout, `capability_report_complete=true`, `trace_drain_witness=observed`, and
matching before/after executable hashes. These are meaningful complete
diagnostic journeys. **Windows still has multi-second synchronous costs after
successful completion.** The Mac phases also do not establish fluent physical
input or a dependable p95 merely because the reported milliseconds are lower.

Each cell is one phase observation, not a percentile, independent replicate,
before/after experiment or architecture speed ranking. Initial/post-edit/history
are different state transitions and must not be pooled as four repeated samples.
Different hosted machines/commits do not isolate the cause of the apparent
earlier/newer improvement. Also do not extrapolate diagnostic whole-control
history reimport to the current default product's ordinary Undo path.

The newer mixed-text x64 initial import is 1,844.1462 ms, whereas its two history
imports are 6.5783 / 6.1854 ms. This first-use difference is a real observation
requiring qualification, not a reason to discard the slow first sample. The
novel's multi-second history imports show that one-time warmup cannot explain
away its whole-source import cost.

Report identity for reproduction:

| RID | Newer `source-capability/probe/report.jsonl` SHA-256 |
| --- | --- |
| win-x64 | `CC1632E02AD7C23EFD819533D766AA0D8DFD65D6A66EAE75EA302F46C880A9D4` |
| win-arm64 | `B9FDD61B40CAA2C15885BE0862BED4C8F88BE1E243E78F869B19742D929707F0` |
| osx-x64 | `86C87BA03F65C4AA2DCD1330934E47CF4B81B6D384B7A9FDDD2905E2DDCA6BC0` |
| osx-arm64 | `C68AA69C182E606D45BA220990EAE737E04DD747EB9B718C861F073CE7998771` |

The source audit additionally found that entered/terminal JSON serialization
and durable flush are outside each `PhaseReport.Measure` stopwatch, but their
inter-phase scheduling/cache effects are not removed. Trace-enabled aggregate
publication includes preservation/readback and is not a pure native setter
microbenchmark. Preserve that aggregate when comparing the same probe; a
separate application-only timer may help attribution but cannot replace it.

Setup/cleanup opacity also remains explicit: fixture generation, configuration,
native first-use setup, report flushes, checks outside individual timers,
controller/host disposal and telemetry shutdown do not become a measured
launch-to-usable interval by summing phase durations. Some are separately timed;
some affect following phases without belonging to those phases' stopwatches.
Normal-exit/drain witnesses certify only their documented lifecycle facts, not
zero overhead or a complete causal graph. Bounded foreground readback samples
qualify observed native attributes, not every token's color or presented ink.
The newest complete all-resident diagnostic reports do not measure a product
range-publication path, new streaming-import candidate, natural input or default
profile performance. Already careful endpoint documentation should be preserved;
the criticism here is priority and proxy propagation, not an assertion that
those documents claimed photons.

## 5. Standards/path disposition

| Standard or path | Action | Concrete replacement / limitation |
| --- | --- | --- |
| Exact source, generation/revision/nonce, one intended mutation, history preservation, unchanged input before explicit Save, exact saved bytes, encoding/newline fidelity | **KEEP** hard correctness | Zero tolerated corruption/lost/reordered input. Failures remain failures even if latency is fast. Preserve existing external contracts and binary compatibility. |
| Focus/permission/owned-process/finite native calls/normal exit and trace drain | **KEEP** safety and capability | Unsupported observer is `unverified` or `unsupported`, not a product pass/fail guessed from absence. Actual numeric process exit and supervisor timeout remain separate. |
| 120 s whole-process capability deadline and 30/120 s startup-oracle waits | **KEEP** safety, **DEMOTE** UX meaning | They prevent hangs/runaway automation. A later result records `completed but budget exceeded`; never equate waiting within a safety deadline with interactive acceptance. Do not enlarge these limits to repair a design. |
| Ordinary 1/10/100 MiB, 18-case/666-MiB matrix in `native-latency-acceptance.md` and `NativeAcceptance` | **REVISE** primary selection, **KEEP** corpus | Small ordinary files + a varied few-MiB reading fixture are primary. 10/100 MiB and giant single lines become separately labeled capacity/defensive coverage until task evidence promotes them. No automatic full matrix after every change. |
| `NativeJsonLargeAcceptance` 1/100 MiB pair | **KEEP** product-route exact workflow; **DEMOTE** positioning/tails | Keep outcomes per size/RID/phase. A full 100 MiB Save/reopen pass is capacity evidence, not proof of ordinary user value or four-RID general reliability. |
| `NativeCanvasGui` experimental route and repeated 64-byte rows / 50 MiB long line | **KEEP** bounded-host/history regression; **DEMOTE** primary UX | Record surface/profile identity. No promotion from experimental route to default source, and no inference of long-line horizontal reachability from bounded memory. |
| `tests/Mote.Benchmarks`, `JsonArrayPages`, Markdown allocation and retained-memory tools | **KEEP** subsystem diagnosis; **DEMOTE** end-to-end rank | Engine Apply, full synchronous analysis, allocation and retained roots answer different questions. Keep costly negative allocation/RSS findings. |
| “Cold” first run / “warm” fresh processes in smoke/startup | **REVISE** labels | `first fresh process after publish, cache uncontrolled`, `subsequent fresh process`, `reused home`, `in-process warmed native path`. Only name disk-cold after a separately calibrated storage-state procedure; just-written fixtures are not disk-cold. Keep legacy fields and add explanatory aliases rather than breaking parsers. |
| p95/p99 from one to five processes, or many edits from one process | **REMOVE** SLA interpretation | n=3 nearest-rank p95 is the maximum. Report raw n, median/range and outcome count. Cluster repeated actions by process/host; do not manufacture independent sample size. |
| Current product latency hypotheses in `large-file-demand-and-experience.md` | **KEEP** explicit hypotheses, **REVISE** scope/approval | Ordinary usable startup p95 <=500 ms; visible interaction p95 <=50 ms and investigate >100 ms; distant navigation p95 <=100 ms remain proposed, not achieved/approved/universal. No fixed semantic/save completion throughput without a real task/resource class. 100 MiB <=1 s remains conditional capacity hypothesis. |
| Point RSS, private/peak counters and allocation | **KEEP** separate dimensions, **REVISE** resource envelope | Record point resident/private and calibrated peak where available, allocation for diagnostic phases, retained growth and user-impact stalls. Unsupported fields remain null. Do not invent one universal “X MiB file <= Y MiB memory” quota. |
| Green multi-job/continue-on-error workflow | **REMOVE** release/experience inference | Produce separate correctness, observer-capability, task-outcome, latency-budget and capacity summaries. Inspect nested artifacts and actual phase outcomes; an artifact-upload job may be green while its editor process times out. |

No test/script/CI gate was deleted or changed by this review. The action words
above specify proposed *classification and contract* changes; implementation
ownership and owner-approved product budgets are still required.

## 6. Exact replacement measurement protocol (future, not executed)

### A. Freeze a small primary task set before timing

1. Establish a documented representative laptop class for each OS independently:
   CPU, physical RAM, SSD, display resolution/DPI/refresh, power state and
   background-load policy. The local i9/31.75-GiB machine is an identified host,
   not automatically a typical user device. Keep shared-runner capability and
   timing separate from that experience baseline.
2. Primary ordinary cases: one small mixed-script plain-text source; ordinary
   structured JSON/config and Markdown documents with inspect/edit tasks; a
   few-MiB varied CJK reading document. Preserve the current novel/dense fixtures
   as named regression controls alongside these, not as replacements for them.
   Use consented sanitized data or reproducible public/generated examples with
   explicit provenance; never infer population representativeness from bytes.
3. Freeze each case's SHA, UTF-8 bytes, UTF-16 extent, lines/newlines, line-length
   quantiles/max, paragraph distribution, structure/token density and chosen
   task/action location. Hash/generate/publish/configure outside timing.
4. Freeze exact executable SHA, commit + dirty status, inventory/runtime mode,
   SDK/RID, observer source/version and surface/profile. Compare binaries on
   the same host/fixture/control route with randomized or alternating order.

### B. Separate timing and correctness without skipping either

5. In the **trace-off primary** fresh process, start the parent's monotonic clock
   immediately before spawn. Retain spawn return, visible window, requested-file
   source identity, foreground/first-responder and source usability milestones.
   Verify a single externally delivered reversible action at the defined source
   location; record dispatch-to-ack and dispatch-to-qualified-source-visible
   separately. No repeated non-idempotent input after timeout.
6. Natural UI drawing is the visual timing path. If a permitted source-specific
   capture is available, retain its frame interval/capture cost/observation gaps
   and label the value a software-visible proxy. Otherwise visual response is
   **unverified**, and native action acknowledgement is still reported under its
   narrower name. Physical input-to-light requires hardware instrumentation;
   it is not a prerequisite for every software regression test or an inference
   from drawing/capture.
7. After the timed response, certify exact engine/source version, selection,
   expected action count, intended result, Undo/Redo, unchanged unrelated bytes,
   exact Save and fresh product-route reopen where that task requires them.
   Full-file hash/readback belongs outside a user-action timer. For the existing
   diagnostic verified-publication timer, **keep** its inclusive oracles for
   comparability; do not silently redefine the baseline.
8. Reading task: open requested file, find or navigate to a predetermined distant
   chapter/phrase, scroll several screens, select/copy a bounded excerpt through
   the existing safe route and return to a known location. Certify actual text
   context and navigation result, not only changed scroll metadata. Editing task:
   one local edit, Undo/Redo and exact persisted outcome; no whole-source history
   reimport hidden outside the user-visible endpoint.
9. During a separate analysis-load interaction, retain input/selection/navigation
   response, cancellation acknowledgement, stale-result rejection and semantic
   completeness classification. Full analysis can finish later; it cannot block
   typing or be falsely reported as complete. Scroll requires its own timer;
   edit-to-draw instrumentation does not cover it.

### C. Diagnose only a consequential failing endpoint

10. Keep one paired trace-on/off diagnostic block for the slow case. Retain child
    open/install/semantic/draw phases, exact causal IDs/revisions and observer
    intervals independently. Map clocks only with a documented verified mapping;
    do not subtract UTC/parent/child values to invent decode or configuration.
    Profile the dominant installed-source/layout/style phase if warranted;
    engine/parser tuning is not the default answer to an attributed native cost.
11. Keep first-use and later-in-process paths visible. Rotate fixture order or
    isolate processes for representative startup/resource comparison. Do not
    discard cold native font/script initialization as an outlier. Separate fresh
    and reused home/configuration with both storage cache states explicitly
    uncontrolled unless calibrated.
12. Record successful observations **and** failed/cancelled/skipped/unverified/
    censored ones. The supervisor safety deadline is separate from each product
    hypothesis. If the observer remains alive after a budget violation, record
    both violation and eventual completion; forced termination leaves a censored
    endpoint, never an invented completed duration. A success-only p95 cannot
    conceal failed or never-drawn requests. Completion reliability is a separate
    acceptance dimension.

### D. Collect only enough evidence to settle the decision

13. One case/host capability pilot checks the protocol, not the tail. Once usable,
    adopt the existing baseline proposal of at least 3 host instances per RID and
    30 fresh-process launches per selected ordinary stratum, plus at least 100
    endpoint observations for *descriptive* tails. Expand according to measured
    variance and the effect/budget being resolved, not to fill a grid. Report
    per-host n, median/MAD/range, nearest-rank p95, raw outcomes and clustered
    process/host uncertainty. No p99 promise from 100 correlated actions.
14. A prospective strict p95 pass means a one-sided uncertainty bound and failure
    accounting meet an owner-approved budget on the defined hardware class,
    not merely sample p95 below it. As a simple limited check, if independent
    binary budget-violation trials have **zero** violations, the exact 95%
    one-sided upper bound is `1 - 0.05^(1/n)`; n=59 only brings it below 5%.
    Correlated actions/heterogeneous machines violate that model. This does not
    authorize 59 repeated keystrokes in one process as an SLA certification.
15. Memory: retain baseline and point resident/private samples at usable source,
    semantic completion and task/history completion, calibrated lifetime peak
    where available, and a fixed repeated-workflow retention diagnostic. Sampling
    reports its interval and only a lower bound on unsampled peaks. Forced GC is
    allowed only in a separately labeled retention diagnosis, not the primary UX
    run. Record allocation and total native/process memory separately; favorable
    allocation does not excuse increased paging, stalls or resident growth.

This protocol does not require a new telemetry framework, global cache eviction,
clipboard/input-source mutation, permission changes, a 666-MiB corpus or a
release timing gate on shared hosted VMs. Coordinate the few missing natural
input/visible-source/scroll endpoints with their current owners instead of
duplicating the drivers. Where no safe observer exists, keep the gap explicit.

## 7. External grounding and research limits

Production platform mechanisms support using the native text backend without
assuming a particular API is fast: Microsoft's
[TOM Freeze documentation](https://learn.microsoft.com/en-us/windows/win32/api/tom/nf-tom-itextdocument-freeze)
specifies counted screen-update suppression, not constant-time token formatting.
This directly explains why a frozen all-span transaction can still be too slow.
Source correctness and view/state preservation must be measured separately from
its cost. Mature platform API use is necessary evidence, not a speed guarantee.

[Liu and Heer, IEEE TVCG 2014](https://idl.uw.edu/papers/latency) experimentally
found that an additional 500 ms changed exploratory behavior and reduced
activity/coverage. The important connection is the rapid observe-hypothesize-
navigate loop of reading/diagnostic work. The study does **not** justify mote's
exact 50/100/500 ms proposals or classify a synthetic parser operation as a user
task. The research-informed decision is to measure the meaningful interaction
loop and retain task outcomes, not to import a universal human threshold.

[Henning et al., FSE 2025 Industry Track](https://arxiv.org/abs/2504.11826)
studied end-to-end cloud stream-processing variability over many deployments.
It shows that workload-specific variation can be quantified rather than assuming
all cloud measurements are unusable. Its measured variability is not a GUI
VM jitter bound. Use hierarchical/paired experiments calibrated to mote's
endpoint, and do not borrow its repetition count or stability for hosted input.
This is an applicable frontier in **measurement methodology**, not a ready-made
editor optimization or a new benchmark suite mandate.

## 8. Cheap reproduction and final recommendation

This is artifact inspection only. From repository root, without launching mote:

```powershell
# Inspect one retained current capability report and its owned-process outcome.
$base = '.cache/ci-36899695843-codec-source/artifacts'
$rid = 'win-x64'
$dir = "$base/native-codec-source-evidence-$rid/source-capability"
Get-FileHash -LiteralPath "$dir/probe/report.jsonl" -Algorithm SHA256
$rows = @(Get-Content -LiteralPath "$dir/probe/report.jsonl" | ConvertFrom-Json)
$rows | Where-Object {
    $_.state -eq 'completed' -and
    $_.phase -in 'native-import', 'semantic-publication-verified'
} | Select-Object fixture, phase, duration_ms
Get-Content -LiteralPath "$dir/supervisor.json"
```

Fixture counts can be independently checked by reading the baseline `.input`
bytes once with Python, decoding UTF-8, counting `splitlines()`/literal LF/CR,
UTF-16-LE length /2, and `len(json.loads(text)['rows'])`. The exact generator,
artifact path and hash are the reproducibility identity, not a new generated
corpus. The Unicode paragraph itself must not be modified when reproducing
counts.

**Recommendation:** pause speculative optimization selection, not preserve a
slow architecture out of habit. Adopt the classifications above; preserve the
hard compatibility/precision contracts and current negative regressions. Before
promoting a backend or promising experience, qualify the small ordinary task set
and its true source-visible response on a specified hardware class. The existing
multi-second Windows diagnostic phases already prevent calling that candidate
fluent; no additional broad stress campaign is needed to reach that decision.
After task/endpoint approval, the smallest next investigation is the dominant
ordinary native publication/import/history cost—not a smaller dense fixture,
a longer deadline, a parser microbenchmark victory or another green aggregate.
