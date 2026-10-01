# Goal implementation checklist

Date: 2026-10-02 (Asia/Singapore). This is an implementation inventory, not a release certificate.
The user supersedes the earlier paused objective: **allow multi-file application
packages, retain Native AOT, publish the first deliverable, and maintain its
release page/manual/changelog**. Single-document behavior and integrity remain;
the old literal-binary constraint and unapproved size/latency promotion budgets
are retired. See the [release acceptance contract](release-acceptance-contract.md)
and [versioned release page](../releases/v0.1.0.md).

First release candidate `25b897e2bc375c0e36556ff569717843ff2bfa16` is undergoing
[CI 36915680535](https://github.com/kleedaisuki/mote/actions/runs/36915680535):
Windows/macOS complete solution tests and four native-architecture extracted
package product/codec tasks. No published release or default promotion is claimed
until those actual tasks and final assets are verified. Local affected product
selection is 85/85, package controls 15 methods, strict trace oracle 7 methods;
these independent counts are not added into a fabricated complete-suite total.
Old Canvas/capacity experiments remain explicitly dispatchable, not deleted.

## Evidence baseline and status rules

Historical component checkpoint (before the release candidate): `6827cd1a19142c9ad766d296c18d0770e600e79c`,
[CI 36899695843](https://github.com/kleedaisuki/mote/actions/runs/36899695843).
The component audit covers managed suites, four native inventories/codecs and
native source reference experiments. It is not an audit certifying every
inherited diagnostic or the complete product.

- Windows/macOS main suites each pass 3508/3508 with no skips; Themes 14/14
  and Configuration 9/9 also pass on both platforms.
- All four strict single-binary inventories and actual seven-check published
  codec runs pass with correct architecture and stable executable hashes.
- All four source reference experiments exit normally with three exact saved
  files and fresh-Document reopens, 120 paired timed phases, actual bounded
  foreground/state readback, and a normal trace session terminal. Reopening
  a Document in-process is not fresh-process product reopen acceptance.
- Windows dense-JSON publication still takes 5.2–10.3 seconds, and novel initial
  import/history reimports take 3.8–4.9 seconds. Mac post-edit dense publication
  is 228.7 ms (x64) / 81.0 ms (ARM64), down from the preceding observed
  4961.1 / 2731.8 ms. These are single phase observations, not latency
  distributions, physical input-to-pixel measurements or a default promotion.
- Aggregate CI is success, but the non-gating Windows Save diagnostic job
  `110495482950` failed before posting Save or acquiring its control holder.
  The original file is exact, the child exited normally, and its nine-row trace
  ended normally. The wrapped exception does not identify the failing API.
  Bounded operation-stage/deepest-type evidence is being added without changing
  the behavior or oracle. This is not an established product Save failure.

Historical `23f1c1a` macOS fixture cleanup/prelaunch supervisor failures are
retained in their validation records. The subsequent `065585ca` run actually
qualified those corrections and four-RID codec execution; the `6827cd1a` run
qualifies the later YAML closer and native foreground changes. Original Grid,
real IME/reader and ordinary workflow gaps remain open unless their own scoped
runtime acceptance supplies stronger evidence.

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
| C# Native AOT application packages | Release qualification pending | Four prior AOT architectures established; new Windows directories/macOS .app resources now allowed. Literal-one-executable inventory is historical, not the current delivery gate. |
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

## Current parallel delivery (qualified component baseline: 6827cd1a)

The subsequent local `--native-source` integration is implemented and reviewed
at `76c70fc`, with separately retained portable binding/controller/platform
qualification. It has no hosted native/AOT/input/IME/reader certificate and has
not changed ordinary launch. The diagnostic stream-import alternative is also
local and unmeasured on real controls. Both are held pending the user's decision,
not promoted on the preceding reference measurements.

Delivery alternatives and an independent standards audit are complete in
`docs/research/`. Their recommendations do not change the active strict-binary
contract, approve numerical experience budgets or authorize a UI migration.

| Stream | Actual progress | Remaining qualification |
| --- | --- | --- |
| Native editing locus | All four full-resident experiments complete at 6827cd1a with scoped foreground/state checks. A real NativeSource candidate is now being integrated into existing product shells/controller. | Windows full styling/import remains slow; candidate real input, marked text, range-based history, visible decoration and native geometry need qualification. Default Continuous/LegacyPage remain unchanged. |
| Ordinary YAML semantics | Graph-key identity, erroneous-key completeness and ordinary/streamed flow closer corrections are implemented/reviewed, with scoped differential evidence and passing 6827cd1a integrated suites. | Intended +1 span changes are documented; other recovery, resource and conformance limits remain. |
| Unicode edit difference | Shared scalar-safe difference implemented/reviewed; real pre-fix Apply failures, focused regression evidence and passing current Windows integrated suite retained. | Native ingress and real composition acceptance remain scoped; scalar safety is not full grapheme/IME acceptance. |
| Explicit source encoding | Eight-codec engine API, explicit chooser/controller and four-RID seven-check published-codec runs are qualified at 065585ca and 6827cd1a. | Real native chooser interaction remains open; seven fixed checks are not exhaustive encoding-standard conformance. Never guess the user's file encoding. |
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
- [Actual native source costs and next bounded investigation](../performance/native-source-capability-timeout.md).
- [YAML flow closer correction](../validation/yaml-flow-collection-spans.md),
  [Mac native publication costs and candidate](../performance/mac-native-source-style-publication.md).
- [Independent pre-Save diagnostic failure](../validation/windows-save-diagnostic-first-target.md).
- [Delivery alternatives](../research/delivery-constraint-alternatives.md),
  [independent standards audit](../research/editor-experience-standard-audit.md).

This checklist intentionally does not assign a completion percentage. The
remaining native-user requirements are not proportional to file/test counts.
Signing/notarization work is not being pursued under the user's current instruction.
