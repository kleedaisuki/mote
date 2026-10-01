# Goal implementation checklist

Date: 2026-10-01. This is an implementation inventory, not a release certificate.
The authoritative updated objective asks for clear top-down modeling, good
implementation and native productization; it does not authorize shrinking the
six-format, performance, single-binary, or semantic requirements.

## Evidence baseline and status rules

Latest inspected completed hosted source: `f1b929843563254ff15fb6586041d0ea2a069038`,
[CI 36877121813](https://github.com/kleedaisuki/mote/actions/runs/36877121813).
Both main suites report 3271/3271, Themes 14/14 and Configuration 9/9; four
Native AOT inventories contain one executable. Non-gating original Grid probes
still fail, the new x64 client is unknown after GoTo, and ordinary JSON is 6/8.
These failures remain requirements/evidence gaps, not erased by green jobs.

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

## Current parallel delivery (not part of f1b9298 hosted evidence)

| Stream | Actual progress | Remaining qualification |
| --- | --- | --- |
| Native editing locus | One-source full-resident native adapter capability experiment is being implemented on both OSes, without changing ordinary default or LegacyPage. | Coordinated build/model qualification, then actual hosted run; controlled input is not real IME or a delivered default surface. |
| Ordinary YAML semantics | Graph-key structural identity implemented/reviewed, 153 affected checks and 21 frozen output comparisons; skipped erroneous-key completeness correction is in validation. | Integrated source/RID coverage; retained collection-span boundary and other policy limits remain. |
| Unicode edit difference | Shared scalar-safe difference implemented/reviewed; real pre-fix Apply failures and qualified focused regression evidence retained. | Full integrated suite and native ingress execution; scalar safety is not full grapheme/IME acceptance. |
| Explicit source encoding | Eight-codec engine API qualified; native explicit choice/menu/controller implemented with portable checks; tiny published-AOT codec check is being finalized. | Actual four-RID codec-table execution and native chooser behavior; never guess the user's file encoding. |
| Runtime evidence | Owned Windows cleanup controls now really pass 0/124; separate adapter graph exists on both RIDs. | Original Grid failure, new x64 post-GoTo unknown, and Mac native edit/Save failures remain open. |

## Primary internal evidence

- [Architecture](../architecture.md), [native contracts](../../src/Mote.Native/README.md),
  [format boundaries](../../src/Mote.Formats/README.md), [release gaps](../release-gaps.md).
- [Actual hosted followup](../validation/windows-grid-focus-provenance-workflow.md).
- [Direct workflow evidence/priorities](large-file-demand-and-experience.md),
  [ordinary editing-locus design](../architecture/ordinary-editing-locus.md).
- [YAML graph proof](../validation/yaml-key-graph-identity.md),
  [Unicode difference proof](../validation/native-unicode-edit-difference.md),
  [explicit encoding proof](../validation/explicit-open-encoding.md).

This checklist intentionally does not assign a completion percentage. The
remaining native-user requirements are not proportional to file/test counts.
Signing/notarization work is not being pursued under the user's current instruction.
