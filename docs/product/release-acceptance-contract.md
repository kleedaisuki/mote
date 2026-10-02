# Release acceptance and maintenance contract

Decision date: 2026-10-02. Owner: root release coordinator, following the user's
updated goal. The user permits multi-file application delivery, retains Native
AOT, and requests the first usable release with a maintained product page,
manual and changelog. This supersedes the earlier packaging constraint and the
pause; it does not waive data integrity or turn research hypotheses into SLOs.

## Scope and authority

- One editable document per window/process; no project/workspace discovery,
  language server or dynamically discovered policy assemblies.
- Selected product: `Mote.Native` with statically registered Engine, six format
  policies, configuration, themes and opt-in local telemetry.
- Windows x64/ARM64 and macOS x64/ARM64 Native AOT packages. Users need no .NET
  installation. Reviewed resources/companions are permitted, not required.
- Package application resources are immutable; mutable state defaults to
  `~/.mote` with typed configuration/environment overrides.
- First version: `0.1.0`. A version number is not an unsupported claim of complete
  parser conformance, every assistive technology, or a universal capacity SLO.

Scope clarification from the user, 2026-10-02: dedicated screen-reader/accessibility
adaptation is not an assigned product goal or release prerequisite. Do not create
NVDA/VoiceOver work streams from an absence of certification. Preserve native
control capabilities; ordinary keyboard, focus, selection and Chinese input
correctness still matter. Automation observation is infrastructure, not a license
to promote its interface requirements into user-facing features. Historical
accessibility investigations below remain evidence records, not new assignments.

The [standards audit](../research/editor-experience-standard-audit.md) supports
separating correctness, ordinary task experience, capacity stress and mechanism
experiments. Historical 16/50/100 ms targets and memory-to-file-size ratios are
unapproved hypotheses, not promotion authority. The intended user has no actual
100 MB task; this is evidence about one user's needs, not the market. Retain
existing capacity regressions and historical failures without automatically
running the full stress matrix on each release change.

## Blocking acceptance

| Layer | Required outcome | Evidence and limits |
| --- | --- | --- |
| Source correctness | Exact Unicode/newline/encoding preservation; one canonical document and Engine history; stale results rejected; pending native input cannot silently bypass Save/replacement | Full solution tests and source-specific regressions; portable tests alone do not certify native controls |
| Ordinary product task | Actual default source control opens each small Markdown/TOML/JSON/YAML/CSV/plain-text sample; a located selection is replaced; Undo/Redo are exact; Save produces independently expected bytes; a new GUI process reopens them | Native release task workflow for each RID; synthetic samples are behavioral controls, not user/market observations |
| Product usability | Bare launch/help/version and error handling agree with the documented default; global source Find/Goto/select/history do not expose internal page numbers; built-in themes/config preserve semantics | Route/model tests, real native task witness and public manual; direct messages/AppKit selectors are labelled as such |
| Runtime | Published image executes on its native architecture; embedded codecs and GUI create/close successfully; no separately installed .NET prerequisite | Execute actual package payload on native hosted runner; console marker is not GUI readiness |
| Package integrity | Immutable full source SHA; version/architecture/build identity; complete per-file manifest and asset checksums; executable permission and macOS bundle metadata; license and corresponding source accompany release | Package round-trip validation, actual packaged-binary qualification and independent archive audit |
| Runtime servicing | Release-time supported security servicing patch, with matching selected SDK and actual resolved Native AOT/runtime packs and notices | Check official servicing policy; changed embedded runtime requires rebuilt/requalified native packages, not a claim that every upstream CVE applies to this editor |
| Public honesty | Accurate downloads, install/run/update instructions, configuration keys, shortcuts, Save safeguards, privacy and known limitations; no unsupported instant/p95/IME/reader/notarization claim | Source-traced manual/release page/changelog and independent delivery review |

Release automation must fail closed for required gates. Do not publish when a
required workflow, binary, report or exact oracle is absent/failed. Preserve
unknown/failure classifications and logs; a timeout is not a successful task and
an aggregate green does not promote a failed non-gating diagnostic. Process
watchdogs protect CI infrastructure, not human response budgets. Do not retry a
non-idempotent edit to conceal a missing acknowledgement.

Promoting a new presentation default requires evidence from that actual product
route, not `--check-native-source-capability` or a legacy/canvas substitute.
Preserve explicit historical routes and never swap a dirty/marked source to a
different presentation inside a running window. An additive explicit route may
retain an older presentation without making its old probe a release-default test.

## Explicit verification boundaries

The initial release task samples are modest, semantically valid self-created
examples with independent expected outputs. They do not replace parser
conformance/resource tests, nor prove all syntax receives complete analysis.
Incomplete/provisional analysis must remain explicit; absence of diagnostics in
an incomplete result is not a clean global verdict.

Text-byte correctness must not excuse a proven ordinary rendering defect. In
particular, a Complete CSV parse is not authority to label native visible rows
Ready when their source-backed payload is absent. The ordinary last-record
source-follow case needs matching same-version navigation, projection and native
row slots. Final macOS task capture should observe the saved document's current
analysis before rapid subsequent lifecycle edits, rather than photographing an
earlier version and interpreting it as the final semantic view. These are
correctness/readiness conditions, not additional numerical performance budgets
or exhaustive parser/browser/pixel certification.

Win32 messages and in-process AppKit mutations verify distinct native paths;
neither proves physical keyboard delivery, real Pinyin candidate/marked-text
behavior, every NVDA/VoiceOver workflow, or physical/compositor presentation.
Retain composition safety and native-control tests, document unverified attended
behavior, and do not claim certification. If ordinary native composition actually
fails, it is a defect to fix, not a performance trade-off.

macOS has no publisher Developer ID/notarization credentials by the user's
decision. Provide a correctly structured application package and clearly state
this trust limitation. CI launch or an ad-hoc linker signature does not prove a
quarantined download passes Gatekeeper. User instructions may describe Apple's
per-application trust exception, never global security disablement.

Capacity probes remain retained under their own explicit diagnostic/benchmark
workflows. They test resilience, not a demand promise or ordinary-task latency.
Do not advertise unlimited file sizes, bounded resident memory, full semantic
completion on every input, or verified tail latency without evidence.

## Publication and continuing maintenance

1. Commit coherent changes and independently review the release-critical diff.
2. Qualify the immutable source through Windows/macOS solution tests and all four
   published Native AOT package workflows. Record exact run/commit/artifact IDs.
3. Inspect archives and manifests, validate checksums and corresponding source,
   and verify public instructions against the actual packaged executable.
4. Merge verified release changes to the default branch, then qualify the exact
   release commit when its identity differs from the tested candidate.
5. Create an immutable version tag and a GitHub Release only from that qualified
   commit. Upload all platform packages, source, SHA256SUMS and provenance.
6. Publish versioned notes with verified boundaries; keep README, manual and
   CHANGELOG synchronized. Mark a release as published only after assets exist.

For later versions, update version/notes/manual and this contract only where the
actual behavior or approved scope changes. Do not overwrite an existing version
tag/asset to disguise a rebuild: a changed binary requires a new release version.
Upgrade by replacing the complete application package, preserving `~/.mote` and
the user's external documents. No background update or remote telemetry is
introduced by this release effort.

## Coordination

Root owns contract/CI integration, commits, pushes, main-branch merge and public
release. Product team owns native source/CLI and focused product corrections;
packaging owns package construction/workflow/notices; independent validation owns
task fixtures/oracles; documentation owns README/manual/CHANGELOG/release page;
review owns its findings only. One writer per file area, not a repository-wide
lock. Experimental artifacts remain under repository `.cache/` and `.temp/`.

## Publication record: v0.1.0

Published [v0.1.0](https://github.com/kleedaisuki/mote/releases/tag/v0.1.0) on
2026-10-02 07:10:43 Asia/Singapore (2026-10-01T23:10:43Z), release ID401422384.
The annotated tag targets exact main `ed96fe4f29278e633e10421c89e2ce1f5b0536ae`;
[run36938529836](https://github.com/kleedaisuki/mote/actions/runs/36938529836)
passes all eight required jobs. Independent task audit: 24 tasks, 36 processes,
3,479 records, protected originals/exact Save/fresh GUI reopens/current views.
Each OS managed suite passes3695 main,14 Themes and9 Configuration tests.

The publisher inspected all ten SHA256SUMS entries, native image architectures,
complete payload inventories, matching source's888 Git blobs, and resolved
SDK401/runtime/compiler12 provenance. Resolved known framework-pack entries do
not establish every recorded framework is linked or shipped. The old successful
SDK400/runtime11 main and serviced candidate are not substituted for this release.

After upload to a new draft, all eleven GitHub asset lengths/digests and the
remote tag target were checked before publication. A separate no-token/no-account
script then read the public release/tag API and downloaded all eleven public
assets, byte-compared them to the qualified set and checked GitHub's digests.
This establishes public availability and exact identity, not publisher signing.
Evidence: `.cache/release-ci-36938529836/{asset-audit.json,product-audit/}` and
`.cache/release-public-v0.1.0/public-audit.json`. Reproducer:
`.temp/release-coordination/verify-public-release.py`.

Post-publication documentation may record the outcome/screenshots without
rebuilding binaries, replacing immutable archives or moving `v0.1.0`. Original
vision gaps remain in the goal checklist; the delivery-first objective, not
universal performance/semantics/IME certification, is the completed scope.
