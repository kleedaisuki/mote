# Engine retained-memory investigation

Status: 2026-10-01. **No production optimization shipped.** The reported 158.89 MiB
Engine-only increase after 5,200 small edits is not evidence that 5,200 Undo roots
survived pruning. An awaited caller running synchronously inside the producer's
completion stack keeps the file-opening staging state alive. An independent
post-open continuation-unwind control reduces the observed edited heap by about
141.27 MiB, with identical current/history identity graphs and exact Undo/Redo.
A proposed staging-list clear did not improve the matched heap metric and was
reverted. This document distinguishes that negative result from the useful
lifetime diagnosis.

Related: [large-edit history contract](large-edit-undo.md),
[JSON page benchmark](json-array-pages-performance.md), and
[`benchmarks/EngineRetainedMemory`](../benchmarks/EngineRetainedMemory/).

## Workload, environment, and competing explanations

Windows 10.0.26200 x64; .NET SDK 10.0.400 / runtime 10.0.11; one managed Release
process per sample. Engine source baseline `d9aadd34c36ac896440f7147deee168a44e9f8a1`;
there are no Engine source changes in the final experiment. The process measures
Engine only: no format session, Analyze call, diagnostics, or per-edit result
objects. These are not Native AOT or UI paint/input measurements.

The 100 MiB ASCII UTF-8 LF fixture is
`.temp/JsonArrayPages/corpora/ordinary-lf-100.json`, SHA256
`D4FF1A6FB4AFE5F2CEEA0A772D9A3A8B885B84380B6F3A295D488239D5D96CC9`.
It contains 104,857,600 UTF-16 code units: its leaf strings alone occupy roughly
**200 MiB**, not 100 MiB. There are 204,003 records, source header length 2,
record stride 514, and edit offset 140 in string padding.

Edit `i` replaces one code unit with `X` / `Y` alternately. The first 200 edits
alternate records 1, 102001, and 204001; later edits use
`((i - 200) * 104729) % 204003`. No caller-held old snapshots remain after the
non-inlined edit method returns. Hypotheses were:

1. History pruning retained all 5,200 states (test root counts and Undo suffix).
2. Persistent rope path/leaf copies account for all measured growth (count unique
   nodes and backing strings reachable from current + Undo + Redo roots).
3. Producer async-frame lifetime retains obsolete opening data (heap roots and an
   independent post-open scheduling control).
4. Uncollected garbage/fragmentation explains the gap (compare `GetTotalMemory`
   with explicit blocking compacting generation-2 collections and SOS liveness).

Reflection graph inspection, full GC, hashing, and Undo/Redo verification run
outside the timed Open/Edit intervals. Live-heap samples are taken **before**
graph inspection allocates its temporary sets. The detached control waits 100 ms
immediately after OpenAsync; this is diagnostic scheduling, not a proposed product
startup delay. It lets the producer's callback return before the edit workload;
a bare `Task.Yield` can in principle race its completion thread. A separate JSON
benchmark's observed `Task.Yield` control corroborated the result, but its parser
and report storage are different from this Engine-only workload.

## Repeated equivalent-source controls

Five fresh-process pairs alternate order (inline/detached, detached/inline, ...).
Raw artifacts: `.cache/engine-retained-memory/inline-detached/`. Every process
verified exactly **512 Undo actions**, 512 successful Redo actions, each reapplied
character, no remaining Redo, and equal final UTF-16 streaming SHA256. Thus the
scheduling discriminator did not buy memory by throwing away history.

| Metric | Inline continuation median [min, max] | Detached control median [min, max] |
| --- | ---: | ---: |
| Open heap after explicit compacting GC, B | 211,212,248 [211,212,216, 211,215,320] | 210,889,488 [210,887,344, 210,890,416] |
| Edited heap after explicit compacting GC, B | 377,829,752 [377,826,680, 377,829,784] | 229,694,096 [229,691,952, 229,695,024] |
| Edited heap, MiB | 360.33 | 219.05 |
| Edited minus open heap, MiB | 158.90 | 17.93 |
| Heap after Dispose, retaining only final snapshot, B | 377,097,440 [377,094,344, 377,097,472] | 212,122,208 [212,120,040, 212,123,136] |
| 5,200 Apply total, ms | 288.59 [283.58, 293.31] | 291.89 [276.37, 303.83] |
| Apply same-thread allocation, B | 266,360,552 [266,360,552, 266,360,568] | 266,360,552 [266,360,552, 266,360,552] |

The edited-heap difference is 148,135,656 B (141.27 MiB). Five pairs and overlapping
timing ranges do **not** demonstrate an edit-speed improvement. Allocation is
unchanged: this is a lifetime discriminator, not a rope-allocation optimization.
Open timings were 311.53 ms [301.00, 337.43] inline and 317.45 ms [302.59, 336.65]
detached; the explicit 100 ms control is excluded. These uncontrolled local I/O
numbers are not a cold-start acceptance threshold.

The final unchanged-source Engine assembly SHA256 was
`35A093B7DCF28F4F7F6B48FC9DA44A89F3D66AC59C433C9492AC48AA38EBAA1D`.
Binary hashes from source archives can differ because source/PDB paths differ;
results record actual binaries and fixture identity rather than assuming them.

## What the retained graph actually contains

Both scheduling modes have **identical** graphs after 5,200 edits:

| Roots inspected | Unique rope nodes | Unique backing strings | Unique UTF-16 code units |
| --- | ---: | ---: | ---: |
| Initial current snapshot | 12,799 | 6,400 | 104,857,600 |
| Edited current snapshot only | 32,415 | 11,403 | 104,852,795 |
| Edited current + 512 history entries | 45,881 | 11,915 | 113,241,403 |

Counts are by object identity. A string such as the two reused one-character edit
payloads can back many leaves; unique backing units need not equal document
length. The retained history adds **8,388,608 UTF-16 units (16 MiB)** relative to
current-only, plus 13,466 nodes (roughly 0.72 MiB at the observed 56-byte node size)
and small entry/list overhead. Current rope metadata also grows as dispersed
edits split leaves. This reconciles the detached control's roughly 17.93 MiB
edited-minus-open heap; it cannot reconcile the inline control's 158.90 MiB.

The source-backed history contract remains:

- Roots share untouched subtrees; Undo/Redo switch roots rather than retaining
  inverse/redo full strings. External immutable snapshots remain valid independently
  of document lifetime and can deliberately retain old source.
- At most 512 ordinary actions survive. Pruning removes an oldest contiguous prefix,
  never a middle gap. A new Apply invalidates Redo.
- The 32 MiB budget is **nominal inserted/deleted UTF-16 edit bytes**, not a quota on
  unique physical rope allocations. Here 512 one-character replacements nominally
  cost just 2,048 B, while their root history costs roughly 16.7 MiB.
- The newest oversized action is protected as documented in `large-edit-undo.md`;
  later suffix cost/count can evict it. No bounded-total-process-RAM claim follows.

One additional detached 10,400-edit probe (not a repeated performance result)
retained 512 actions, 51,410 nodes, 16,921 strings, and 109,307,182 unique code units;
current-only was 37,247 nodes / 16,382 strings / 104,855,358 units. Edited heap was
222,264,304 B (211.97 MiB), **lower**, not linearly increasing with the edit count.
Later edits split smaller existing leaves, so the newest 512 copied leaf fragments
retain less text. This rejects an all-edits-history leak for this workload, not an
arbitrary-workload total memory bound.

## Heap-root evidence and the failed production candidate

Tools were installed under `.cache/tools` (`dotnet-dump` 10.0.745401); dumps and logs
remain in `.cache`, not outside the repository. The first diagnostic ran 10,400
edits, disposed the document, and retained its current snapshot while paused.
`.cache/engine-memory.dmp` contained 406,043,846 B of strings and 2,778,664 B of
RopeNodes. `gcroot` on its 65,560-byte original chunk array showed:

```text
ThreadPool AsyncStateMachineBox<Document.<OpenAsync>>.ExecutionContextCallback
  -> AsyncStateMachineBox<Document.<OpenAsync>>
  -> List<string> chunks
  -> String[8192]
```

The awaited program's long synchronous continuation was still nested inside
OpenAsync's completion callback. The original chunk staging list remained live
until that callback returned, although most original chunks were no longer in the
current/Undo/Redo graph. The independent scheduling control removes that lifetime
boundary without changing any Engine or Undo algorithm.

Candidate: call `chunks.Clear()` immediately after `RopeNode.FromChunks(chunks)`
hands the immutable strings to the rope. The source change preserved ownership and
text semantics, but **did not improve the matched target metric**: after 5,200
inline edits the explicitly collected heap was 377,825,064 B and a repeat was
377,829,752 B. A paused candidate dump (`.cache/engine-memory-candidate.dmp`) proved
`List<string>._size == 0` and sampled staging array slots were null. An obsolete
first source string had no SOS `gcroot`; after disposal SOS `dumpheap -live -stat`
reported 212,539,016 B (32,415 reachable RopeNodes), while unrestricted `dumpheap
-stat` reported 377,509,761 B (43,553 nodes). Thus merely clearing the list changed
root reachability but did **not** establish reclaimed-heap benefit at this active
async completion boundary. The remaining discrepancy between SOS reachable-object
counts and the runtime heap estimate was not fully attributed; do not invent a
second hidden root or claim fragmentation alone explains it.

The candidate was **reverted**. No production history limit, rope representation,
scheduler, or startup delay was changed. We do not ship an optimization because
its ownership argument sounds right when the target measurement is unchanged.

## Production/academic signals and decision

- [Microsoft's TaskCompletionSource discussion](https://devblogs.microsoft.com/premier-developer/the-danger-of-taskcompletionsourcet-class/)
  explains the difference between await continuations and ContinueWith scheduling.
  The [runtime async-builder source](https://source.dot.net/system.private.corelib/src/runtime/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncTaskMethodBuilderT.cs.html)
  shows completion-state cleanup. The heap stack is the evidence for this specific
  program; these sources are not proof that every application retains opening data.
- [GC.GetTotalMemory documentation](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalmemory)
  identifies a heap-size estimate excluding fragmentation; it is not an
  application ownership census. [dotnet-dump documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dump)
  supports inspecting object/root paths. Keep metrics and graph attribution separate.
- [VS Code's production text-buffer account](https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation)
  illustrates why edit and line-access workloads, not an attractive data structure
  alone, should drive representation choice. It does not justify replacing mote's
  rope with a piece tree on this evidence.
- [Boehm, Atkinson, and Plass, *Ropes: An Alternative to Strings*, SPE 1995](https://doi.org/10.1002/spe.4380251203)
  establishes sharing as a useful immutable-text mechanism. The more recent
  [Pietron et al., SoSyM 2025](https://doi.org/10.1007/s10270-024-01214-9), already
  evaluated in the large-edit history note, explores memory/restore-time tradeoffs
  in snapshot placement for a different model-editor workload. Neither predicts
  the exact .NET async-frame lifetime or justifies a disk-history subsystem here.

**Decision:** retain current Engine semantics. Correct the JSON benchmark's
steady-state memory methodology instead of blaming its ~32 KiB page certificate
or reducing Undo to manufacture a win. Keep the inline result as a real bounded
producer-continuation stress case, not as a measured steady-state history leak.
The useful next product experiment is native post-open idle versus immediate
in-continuation work, with owned-source graph/heap measurements on Windows and
macOS; only a persistent material product cost would justify revisiting the
producer completion architecture. Current leaf fragmentation and nominal-vs-
physical history cost remain explicit representation tradeoffs, not hidden promises.

## Reproduction

Generate the matching corpus with the JSON page benchmark, or reuse its existing
fixture after checking the SHA256 above. From repository root:

```powershell
dotnet run --project benchmarks/JsonArrayPages/JsonArrayPages.csproj -c Release -- generate
dotnet build benchmarks/EngineRetainedMemory/EngineRetainedMemory.csproj -c Release -o .temp/EngineRetainedMemory/control-bin
pwsh -File benchmarks/EngineRetainedMemory/Run.ps1 -RunName inline-detached -Pairs 5
dotnet .temp/EngineRetainedMemory/control-bin/EngineRetainedMemory.dll detached .temp/JsonArrayPages/corpora/ordinary-lf-100.json .cache/engine-retained-memory/detached-10400.json 10400
```

For independent source replay use `git archive` of the declared baseline under
`.temp/EngineRetainedMemory/baseline`, then pass
`-p:EngineProject=<absolute baseline>/src/Mote.Engine/Mote.Engine.csproj` at build.
For root diagnostics append `pause` after the explicit edit count; use the emitted
PID and collect while paused:

```powershell
dotnet tool install dotnet-dump --version 10.0.745401 --tool-path .cache/tools
.cache/tools/dotnet-dump collect -p <PID> --type Heap -o .cache/engine-memory.dmp
.cache/tools/dotnet-dump analyze .cache/engine-memory.dmp -c 'dumpheap -stat' -c 'dumpheap -live -stat' -c exit
```

Then locate the chunk array with `dumpheap -type System.String[] -min 60000` and
inspect its `gcroot`; also compare a superseded source string, not just the array
object itself. Instrumented dump runs are excluded from timing comparisons.
