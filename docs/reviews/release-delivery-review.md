# Release delivery and security review

Date: 2026-10-02. Owner: release_review. Status: static review complete; native qualification/publication pending.

## Objective and scope

The authorized first deliverable is v0.1.0, allowing a multi-file application
package while retaining Native AOT and no user-installed .NET prerequisite.
Review the release workflow, packaging, source/asset identity, checksums,
dependency notices, download instructions, and declared native product scope.
Implementation belongs to other writers. This reviewer owns this document only.

## Baseline evidence and constraints

- The prior checkpoint, `docs/product/decision-handoff-2026-10-02.md`, explicitly
  separates local NativeSource integration from native product acceptance.
- `docs/research/delivery-platform-evidence.md` separates package shape, runtime,
  and UI. Multi-file permission alone neither proves GUI improvement nor supplies
  Apple Developer ID trust. The user has no Apple publisher credentials.
- `docs/reviews/native-source-product-integration-review.md` records corrected
  static data-admission defects and remaining actual input/reader boundaries;
  they must not be mistaken for new release-profile runtime evidence.
- Historical `Pack-Windows.ps1`/`Pack-MacOS.ps1` target the Avalonia diagnostic
  program and are expressly non-release packages. Those scripts are being
  superseded; their known old purpose is not reported as a new defect.
- Current native production dependency closure includes Markdig 1.3.2,
  SharpYaml 3.13.1, Tomlyn 2.10.1 and app-contained Native AOT runtime material.
  Release packages must include the project license and applicable exact
  dependency notices, even when code is statically linked.

## Review method and limits

Inspect final workflow and packer source after their writers provide a freeze.
Trace failure paths rather than infer acceptance from aggregate CI success.
Require that uploaded release assets are the tested packaged executable, with
source commit and archive checksums recorded. Check that instructions never
recommend globally disabling platform security, and never invent publisher
signing or actual human IME/screen-reader verification.

No local GUI, native input, CI dispatch, tests, builds, or credentials changes
have been performed by this reviewer. Existing completed model/CI evidence is
not rerun. No final substantive finding has yet been established for the new
release implementation because that implementation is still being written.

## Final static checkpoint

Reviewed candidate commit: `8e494c33a06e8a11a58e373c8f412b17c3c804f8`.
The final source review found **no unresolved substantive delivery/security
implementation defect in the inspected release pipeline**. This is permission
for qualification, not confirmation that the release binaries or native
experience have passed. No release has been published by this reviewer.

### Resolved integration issues

1. The first Mac workflow validator accepted only a direct `.temp` child, while
   the cross-platform wrapper passed nested per-format output paths. This would
   reject every edit task before GUI initialization. The Mac writer corrected
   `MacReleaseWorkflowProbe.ValidateOutput` to accept bounded `.temp`/`.cache`
   descendants with existing real ancestor directories and no overwrite/links.
   Reinspection confirms the wrapper's intended path is now admitted.
2. During parallel documentation edits, the copied configuration manual linked
   to an uncopied theme contract. The documentation owner replaced engineering
   references with HTTPS source links. The final package verifier also validates
   local Markdown link targets in the packaged documentation. This issue is
   resolved, not an open release blocker.

### Verified design properties from source

| Area | Source evidence | Qualification boundary |
| --- | --- | --- |
| Workflow authority | `release.yml` accepts a full lowercase 40-digit SHA and canonical numeric version; verifies checkout HEAD and project Version; contents permission is read-only and checkout credentials are not persisted | This workflow creates candidates, not public GitHub Releases; root's separate publication must use its verified artifacts |
| Fail-closed dependency graph | Identity precedes both OS solution test jobs; all tests precede four native-architecture package jobs; all package jobs precede consolidated release assets; required subprocess exit/markers/oracles throw on failure | Actual hosted outcomes must still be audited; aggregate status alone is insufficient |
| Payload identity | `release_package.py` inventories all regular payload/resource files, rejects links/extra or missing files, validates architecture and native PE/CLR distinction, records source/version/RID/SDK, hashes bytes and validates again after product probes | Header fixtures do not establish runnable AOT; actual package execution is required |
| Archive delivery | Windows ZIP and macOS `.app` tar preserve a coherent package; extracted payload, not build-tree executable, is used for qualification; macOS metadata and executable bit checked | Finder association and quarantined first launch remain unverified; no Developer ID identity is invented |
| Consolidation | `release_index.py` requires exactly one archive/manifest for each RID, extracts and verifies all four, compares embedded/external manifests and workflow URL, checks source HEAD and creates `git archive` from that exact commit | Root must not substitute rebuilt/downloaded assets after this qualification; immutable tag and checksums must identify the same source |
| Licenses | Project GPL license, Markdig/SharpYaml/Tomlyn notices and pinned runtime license/notices copied; verifier requires documentation/license files; corresponding exact source archive accompanies consolidated binaries | Future dependency/runtime upgrades require matching notices; current runtime pin is SDK 10.0.400 |
| Independent task oracle | Wrapper generates expected output separately, checks original and saved bytes, invokes actual native edit/history/Save paths and fresh GUI processes, retains captures and causal traces | Six small synthetic cases are not universal parser/codec conformance or performance evidence |
| Save provenance | Trace oracle requires one complete version-linked request chain through the existing strict causal Save classifier, rather than loose operation counts | Trace evidence is instrumented callback/engine behavior, not physical key transport or pixel presentation |
| User safety | Installation warns packages are unsigned/unnotarized, checksum integrity is not publisher identity, and trust exceptions are per application; no global Gatekeeper disablement or recursive unverified quarantine removal is advised | Platform security may prevent launch; CI cannot certify publisher trust |
| Product claims | Contract/manual distinguish NativeSource product probes from reference/canvas probes and label Pinyin/readers/physical rendering unverified; old size/latency hypotheses are not release SLOs | Do not remove those limits merely because a hosted matrix turns green |

## Conditions before public publication (not new implementation findings)

- Inspect the actual four extracted-package task reports, original/output bytes,
  complete Save traces, normal child exits, codec reports, manifests/checksums,
  and retained captures at the exact intended release commit.
- The current six-format task driver explicitly selects NativeSource. If it is
  promoted to the release default, requalify that actual default and update
  help/manual consistently. A Continuous smoke result is not ordinary editing
  qualification of NativeSource, nor the reverse. Preserve established explicit
  historical routes.
- Any final code/default change after this checkpoint needs targeted review and
  exact-source qualification. A documentation-only review follow-up is not a new
  native runtime claim.
- The root coordinator must create/tag/upload the final immutable set and verify
  published download links before changing the draft release status. This source
  review does not attest to a tag or remote assets that do not yet exist.

## Exact reviewed file hashes

Hashes below describe source files, not a built executable:

| File | SHA-256 |
| --- | --- |
| `.github/workflows/release.yml` | `e9b15c4afaf3fafab500989dea62878361643537b69bbf9b50504f497a4573d6` |
| `packaging/release_package.py` | `de918c5cc81eeb4762c292bcd43b25b748e719a1503a5ee66d79630a05ab1f1c` |
| `packaging/release_index.py` | `512350820c3e663676c39e74863b9ea70629ea8bdfe2ebbb19b94a0a326ea6bd` |
| `tests/NativeReleaseProductWorkflow.ps1` | `101942bcc0a98dd52fad0c3ad020c0cb9e88d02a4515e449340649b9de37169a` |
| `tests/release_trace_oracle.py` | `85ceb2cba94df8247add8458e1df6503856fe7ac2184dca5e2c76dfacc276d4b` |

Review status: **static release-delivery review complete; native qualification
and actual publication pending**. No production/test/workflow changes, GUI,
build, test rerun, CI dispatch, commit, push, or credential mutation performed.
