# Goal implementation checklist

Date: 2026-10-02 (Asia/Singapore). This is an implementation inventory, not a release certificate.
The authoritative updated objective asks for clear top-down modeling, good
implementation and native productization; it does not authorize shrinking the
six-format, performance, single-binary, or semantic requirements.

## Evidence baseline and status rules

Last fully inspected completed hosted baseline: `f1b929843563254ff15fb6586041d0ea2a069038`,
[CI 36877121813](https://github.com/kleedaisuki/mote/actions/runs/36877121813).
Both main suites report 3271/3271, Themes 14/14 and Configuration 9/9; four
Native AOT inventories contain one executable. Non-gating original Grid probes
still fail, the new x64 client is unknown after GoTo, and ordinary JSON is 6/8.
These failures remain requirements/evidence gaps, not erased by green jobs.

Latest inspected hosted source is `23f1c1a9bc01f198add7e44df8b026fe1955f923`,
[CI 36887820181](https://github.com/kleedaisuki/mote/actions/runs/36887820181).
At the 2026-10-01 16:03 UTC inspection the run was still in progress. Windows managed tests had a
successful job conclusion, but the macOS managed test job failed. Both completed
macOS AOT jobs passed publish, inventory, system-library imports and the strict
single-binary gate, then failed the newly added embedded-codec verification step.
The run subsequently completed with a failed overall conclusion and successful
Windows AOT jobs. All four publish inventories passed the strict single-binary
gate. Completed-job/artifact inspection establishes these distinctions:

- Windows main suite: 3436/3436 passed, with Themes 14/14 and Configuration 9/9;
  all three report no failures or skips.
- macOS main suite: 3434 passed, 2 failed, 0 skipped. Both failures are dangling
  symlink fixture cleanup exceptions after admission assertions, not failed
  production link rejection. Themes 14/14 and Configuration 9/9 passed.
- Both macOS new probe routes failed before launch: PowerShell provider lookup of
  hidden `.cache` omitted `-Force`. No codec/source report was produced, so this
  is not evidence that the macOS codecs or native text adapters failed at runtime.
- Both Windows published-codec reports actually pass all seven checks, with
  process exit 0 and unchanged executable hashes. macOS codec acceptance remains
  unobserved until the supervisor repair executes there.
- Both Windows full-native source experiments reached dense JSON but were
  censored at the unchanged 120-second deadline. The last recorded phase entered
  the second, post-edit native semantic publication after 79,433 projected tokens.
  The initial publication completed in approximately 78.85 seconds on x64 and
  73.85 seconds on ARM64, already unacceptable for a 512 KiB ordinary document.
  The experiment is not a passing native editing surface; a non-gating step conclusion cannot erase
  this result. Its attribution is being investigated independently.

The two harness corrections are scoped separately from product requirements.
They cannot retroactively turn this source's failed jobs into a passing run.
They are now implemented as test-only Unix link cleanup and supervisor ancestry
inspection through native attributes, with 12/12 and 10/10 respective focused
Windows checks. Their repaired macOS execution remains pending a new source run.

- Established: implementation exists with relevant scoped evidence.
- Partial: implementation exists, but required behavior or acceptance remains open.
- Local integration: new source/model tests exist; current published four-RID
  runtime evidence does not cover them yet.
- Open: no delivered, qualified product implementation establishes the requirement.

## Goal-level inventory

| Requirement | Status | Actual implementation and remaining boundary |
| --- | --- | --- |
| Open a single document without projects/workspaces/LSP | Established | Native composition opens one document; no discovery/indexing/language-server dependency. |
| Mechanism/policy architecture | Established | Engine owns canonical text/lifetime/I/O/history; statically registered format/theme policies and UI adapters are separate modules. |
| C# Native AOT, strict one executable | Established at hosted baseline | win-x64, win-arm64, osx-x64, osx-arm64 actual inventories; system libraries allowed, no shipped companion libraries/resources. New source must retain this gate. |
| Text model, versioned snapshots and edit history | Established | Immutable chunked text, line indexing, undo/redo, ordered edits and stale-result rejection. |
| Safe file persistence and default encoding | Established with reliability gaps outside engine | Strict BOM/UTF-8 loading, guarded Save/Save As, original-file protection and recovery contracts; real Mac workflow failures remain open. |
| Six-format parsing/semantic results | Partial | All six policies exist and produce structural results, diagnostics and conservative formatting/rendering. General semantic completeness and recovery/large-domain boundaries remain format-specific. |
| Intermediate representation / source projections | Established foundation | Versioned semantic nodes, tokens and source spans; per-format caches/compact summaries. Not a claim of one universal compiler IR or general local incremental parsing. |
| Incremental analysis and caching | Partial | Actual reuse for CSV, restricted JSON/Markdown domains and TOML statements; YAML still re-streams. Session interface alone is not incrementality. |
| Native Windows/macOS editing | Partial | Basic commands and bounded native workflows implemented; coherent single editing locus, real IME and assistive-technology acceptance remain open. |
| Restrained strategy-based themes | Established foundation | Dark/light/high-contrast policies, overrides and system appearance notifications. Physical/high-DPI/all-state visual polish is not certified. |
| ~/.mote conventions and configurable directories | Established | Typed configuration, MOTE_HOME, relocatable cache/data/trace paths; explicit settings override conventions. |
| End-to-end tracing | Partial | Opt-in bounded local JSONL, explicit Save provenance, versioned analysis/draw boundaries and native observations. Physical presentation and missing cross-process edges are not fabricated. |
| Fast startup | Partial | AOT and source-ready measurements/attribution exist; representative ordinary-file current-binary cold/warm acceptance is not closed. |
| Smooth editing and bounded memory | Partial | Chunked engine and bounded visible work exist; actual input-to-visible tails, long-line/native composition and prolonged interaction require stronger evidence. |
| Large-file capability | Partial | Capacity tests, virtualized projections and correctness-preserving partial status exist; no universal full semantics/fluent large-file certificate. Retain resilience, prioritize actual ordinary workflows. |
| GitHub Actions cross-platform validation | Established | Managed/native/AOT/inventory/control/artifact jobs execute. Non-gating success is not product acceptance. |
| Native release-ready product | Open | Product interaction, reliability, performance and accessibility gaps above must be closed; implementation count is not release readiness. |

## Current parallel delivery (published at 23f1c1a; qualification remains open)

| Stream | Actual progress | Remaining qualification |
| --- | --- | --- |
| Native editing locus | One-source full-resident native adapter capability experiment implemented on both OSes; coordinated builds, 33 model checks and 12 admission checks qualified, without changing ordinary default or LegacyPage. | Both Windows runs have slow initial dense-JSON native styling and time out during post-edit publication; Mac routes did not launch. Controlled input is not real IME or a delivered default surface. |
| Ordinary YAML semantics | Graph-key structural identity implemented/reviewed, 153 affected checks and 21 frozen output comparisons; skipped erroneous-key completeness correction implemented with qualified scoped evidence. | Current macOS suite's two failures are separate fixture cleanup failures; retained collection-span boundary and other policy limits remain. |
| Unicode edit difference | Shared scalar-safe difference implemented/reviewed; real pre-fix Apply failures, focused regression evidence and passing current Windows integrated suite retained. | Native ingress and real composition acceptance remain scoped; scalar safety is not full grapheme/IME acceptance. |
| Explicit source encoding | Eight-codec engine API qualified; native explicit choice/menu/controller and published-AOT codec probe implemented with portable checks. Both Windows published codec checks passed. | Both Mac routes failed before launch in the supervisor; Mac codec runtime acceptance and real native chooser behavior remain open. Never guess the user's file encoding. |
| Runtime evidence | Owned Windows cleanup controls passed 0/124 at the previous baseline; separate adapter graph exists on both RIDs. Mac edit reports now preserve the rejected transaction independently of later cleanup. | Original Grid failure, new x64 post-GoTo unknown, and Mac native workflow/Save failures remain open. The previous 1 MiB Mac failure occurred in external AX preflight before any edit event, not a demonstrated editor edit fault. |

## Primary internal evidence

- [Architecture](../architecture.md), [native contracts](../../src/Mote.Native/README.md),
  [format boundaries](../../src/Mote.Formats/README.md), [release gaps](../release-gaps.md).
- [Actual hosted followup](../validation/windows-grid-focus-provenance-workflow.md).
- [Direct workflow evidence/priorities](large-file-demand-and-experience.md),
  [ordinary editing-locus design](../architecture/ordinary-editing-locus.md).
- [YAML graph proof](../validation/yaml-key-graph-identity.md),
  [Unicode difference proof](../validation/native-unicode-edit-difference.md),
  [explicit encoding proof](../validation/explicit-open-encoding.md).
- [Current macOS test failure and scoped cleanup repair](../validation/native-source-admission-macos-cleanup.md),
  [codec/source supervisor qualification](../validation/native-codec-source-workflow.md).

This checklist intentionally does not assign a completion percentage. The
remaining native-user requirements are not proportional to file/test counts.
Signing/notarization work is not being pursued under the user's current instruction.
