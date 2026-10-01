# Delivery/runtime alternatives: what research can actually establish

Date: 2026-10-02. Scope: bounded literature synthesis for the delivery-constraint
decision; no production changes, new benchmarks, CI, or release decision.
Repository context inspected: HEAD `6827cd1a19142c9ad766d296c18d0770e600e79c`;
the worktree is concurrent, so the cited documents identify the reused evidence.
External primary sources were opened on this date. This is a deliberately small
selection, not a comprehensive literature review.

## Answer

Relaxing **one physical delivery binary** can expand the set of supported GUI
dependencies and packaging mechanisms. It does not, by itself, require abandoning
.NET Native AOT, nor demonstrate faster startup, better input, or lower memory.
Runtime compilation strategy, UI implementation, and deployment topology are
different variables. The literature below supplies useful mechanisms and
measurement discipline, not a verdict that mote should change its delivery
contract. Any relaxation remains a user/product decision.

An informative next experiment changes one axis first: retain the same native UI
and Engine while comparing Native AOT with a specified CoreCLR publish mode.
Only separately compare a framework UI, if its actual interaction advantage
could justify migration. A wholesale runtime/UI/packaging replacement cannot
identify why an endpoint improved or regressed.

## Reused internal evidence: the problem to explain

- [Large-file demand](../product/large-file-demand-and-experience.md) records
  Klee's self-report: files rarely reach 1 MB, and a novel's TXT is 3.54 MiB.
  This is one intended user's evidence, not market prevalence. Ordinary startup,
  readable Chinese, CJK composition, search, reversible editing, and safe Save
  are the dominant hypothesis; 100 MiB remains resilience/regression scope.
- [Single-binary feasibility](../single-binary-feasibility.md) already reports
  process-private Avalonia native assets and a working OS-framework-only native
  route. Its historical package observations do not prove current-version
  compatibility. Reusing a mature UI remains an engineering possibility, not
  an assurance of mote's IME/accessibility correctness.
- [Requested-file Open attribution](../performance/native-json-open-attribution.md)
  separates blank-shell startup, requested-file editing, draw callback return,
  and later semantic certification. In the cited hosted pilot, Windows x64
  100 MiB Engine Open was 410.667 ms of a 421.494 ms editable interval; ARM64 was
  297.695 ms of 306.876 ms. These are single cache-warm observations on different
  machines, not percentiles, a cross-architecture ranking, or physical paint.
  For that particular large-file path, moving a runtime/UI boundary without
  changing ingestion cannot explain away the dominant Engine cost.
- [Native latency acceptance](../native-latency-acceptance.md) already provides
  the right endpoint and provenance contract. Reuse it; do not manufacture an
  independent benchmark program because literature discusses benchmarking.
- [Real Engine fast-path experiment](../performance/native-json-open-fastpath.md)
  provides a narrow locally controlled ingestion experiment with explicit small
  dense-LF trade-offs. It is not evidence for a packaging change.

## Three relevant peer-reviewed sources and their transfer boundary

### 1. VM warmup is not a fixed number of discarded iterations

Barrett et al., **Virtual Machine Warmup Blows Hot and Cold**, OOPSLA / PACMPL,
2017. [Author full text and artifacts](https://soft-dev.org/pubs/html/barrett_bolz-tereick_killick_mount_tratt__virtual_machine_warmup_blows_hot_and_cold_v6/).

The authors run small deterministic microbenchmarks for 2,000 in-process
iterations and 30 process executions on three machine/OS configurations, using
changepoint analysis to distinguish warmup, flat behavior, slowdown, and failure
to reach steady state. Only 30.0–43.5% of VM/benchmark pairs consistently reached
peak steady state. Tested implementations include historical HotSpot, Graal,
V8, PyPy and LuaJIT; **CoreCLR/.NET is not tested**. This is not an AOT-versus-JIT
desktop trial. The numerical outcome must not become a predicted .NET penalty.

**Inference for mote:** keep the first user task and subsequent tasks in reports;
do not silently drop the first few edits until a JIT variant looks good. Fresh
process, first interaction, and repeated same-process interaction answer
different questions. Advanced steady-state modeling is unnecessary for a short
interactive pilot unless iteration history actually exposes a regime change.

### 2. Hybrid execution changes the trade-off, but in a different runtime

Pichler et al., **On Automating Hybrid Execution of Ahead-of-Time and Just-in-Time
Compiled Code**, VMIL 2024, DOI
[10.1145/3689490.3690398](https://doi.org/10.1145/3689490.3690398).
[Author full text](https://ssw.jku.at/General/Staff/Pichler/VMIL2024.pdf).

This work selects C functions for native or managed execution using managed-data
constraints, code/loop characteristics, and call-graph overhead. It evaluates
GraalVM/Sulong native-extension workloads, including Python LXML, on one
x86-64 Ubuntu host using GraalVM 24. LXML comparisons use fully managed Sulong
as baseline, with hybrid and mostly-native alternatives; this is **not** GraalVM
Native Image versus stock HotSpot, still less .NET Native AOT versus CoreCLR.
Reported setup, warmup, and peak gains concern those workloads, not editor
launch. Interoperation and data placement are part of the mechanism.

**Inference for mote:** AOT/JIT is not a universal fast-startup/fast-throughput
binary law. Boundary costs and data ownership can matter as much as compiler
choice. This is research-level mechanism evidence, not an off-the-shelf .NET
hybridization recipe and not a reason to add another runtime to mote.

### 3. Accessibility quality is a reading/navigation workflow, not an API flag

Pandey, Oney and Begel, **Towards Inclusive Source Code Readability Based on the
Preferences of Programmers with Visual Impairments**, CHI 2024, DOI
[10.1145/3613904.3642512](https://doi.org/10.1145/3613904.3642512).
[Author full text](https://andrewbegel.com/papers/pandey-chi24.pdf).

The remote exploratory qualitative study involves 16 blind/visually impaired
developers choosing between differently formatted functionally equivalent
Python snippets and explaining preferences. Navigation granularity,
punctuation announcement, whitespace, and indentation affect comprehensibility;
visible readability is not sufficient. Limitations include Python-only
preferences, sample composition, and no observed Braille-display use. It does
not benchmark UI frameworks, IME, latency thresholds, or structured-file demand.

**Inference for mote:** preserve full-document line/word/character navigation,
selection and diagnostic context when substituting a UI. Test actual NVDA or
VoiceOver reading and navigation, not only provider registration. The source
supports these evaluation questions, not a claim that Avalonia or a native
control is automatically superior.

## .NET and production-practice boundary

[Microsoft's Native AOT deployment documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
states that publication compiles IL ahead of execution, avoids a runtime JIT,
and yields a self-contained app with faster startup/smaller footprints as its
general benefit. This is official mechanism/product guidance, **not** mote's
measured effect size. It does not promise one physical file with arbitrary
native dependencies or show that every sustained workload outperforms CoreCLR.

The production text-buffer lesson already sourced in
[large-file demand](../product/large-file-demand-and-experience.md),
[VS Code's 2018 implementation account](https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation),
is compatible with the academic lesson: profile integrated workloads and choose
representation from actual costs, rather than inferring responsiveness from
cheap isolated edits. Neither source makes a GUI migration necessary.

## Minimal discriminating experiment proposal (not executed)

| Question | Comparison and endpoints | Controls and rejection condition |
| --- | --- | --- |
| Does compilation mode matter for the dominant task? | Same native UI/Engine source; Native AOT vs a pinned self-contained CoreCLR configuration, explicitly recording ReadyToRun/tiered compilation settings. Fresh-process launch to requested-file editable; first edit; then repeated editing/search. | Same host, source/config/fixtures, focus, observer and cache policy; randomized paired order; binary/runtime hashes. Include sub-1 MB representative Unicode/CJK files and occasional few-MiB reading. Do not call fresh-process cache-warm execution disk-cold. Reject if functional parity fails before timing. |
| Does a framework UI remove enough implementation risk to justify migration? | Separate UI comparison with Engine semantics unchanged. Real Pinyin composition/commit/cancel, selection, undo, search, source/structured navigation, Save/reopen, NVDA/VoiceOver. | One canonical document and identical task results. Keep runtime mode fixed when possible; disclose incompatibility if it cannot be fixed. Visual or API smoke alone is insufficient. |
| Is there a large-file regression? | Reuse existing 100 MiB and long-line controls after ordinary-task acceptance; separate ingestion, source installation, semantic-ready and draw endpoints. | Do not optimize a throughput trophy first. No incomplete semantics mislabeled as complete. Existing exact-byte persistence and encoding/undo contracts remain mandatory. |
| Does packaging help the user? | Clean-machine transfer/install/launch/update/uninstall and macOS document association/quarantine behavior for each candidate deliverable. | Count downloaded artifacts, installed files, extraction/cache files and prerequisites separately. This tests delivery friction; compiler papers cannot settle it. |

Begin with a bounded paired pilot sufficient to locate a meaningful difference;
retain all observations, failures and timeouts. Report medians and individual
samples, with uncertainty appropriate to the sample size. A handful of samples
cannot certify rare-tail p95/p99. Separate process-launch, edit acknowledgement,
matching draw-return, and physical display latency: do not rename one as another.

## What can and cannot be concluded now

**Supported:** treat delivery topology, runtime, UI and canonical storage as
separate design axes; prioritize ordinary-user endpoints; measure first-use
behavior; protect input/accessibility and document contracts.

**Not established:** multiple files are inherently faster; JIT necessarily wins
steady state; Native AOT necessarily wins mote's first useful display; a mature
framework automatically fixes current IME/accessibility; or 100 MiB ingestion
results predict sub-1 MB perceived quality. No located paper directly tests
mote's .NET 10 desktop configurations or authorizes relaxing the single-binary
contract.

**Decisive next unknown:** whether a same-native-UI runtime comparison changes
ordinary launch/first-task costs enough to matter, and separately whether a
candidate framework passes a better complete interaction workflow at an
acceptable migration cost. If neither changes, delivery relaxation may still
reduce packaging/maintenance friction, but should be justified by that benefit
rather than invented performance gains.
