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

## Bounded correction review after first hosted failure

Baseline HEAD: `9fecef2d5ede8b0c593e709bdd6b39d7ad3e6986`.
Reviewed only the working changes in `release.yml`,
`Invoke-NativeWindowsReleaseProduct.ps1`, and
`WindowsGridAccessibilityTests.cs`, plus their relevant existing validation
records. **No substantive defect found in this correction scope.**

- The external `pwsh -NoProfile -File` gives the workflow an actual suite process
  exit status. Required JSON report identity, six ordered formats, executable
  hash, original protection, exact bytes and fresh GUI reopen are checked after
  exit success. Existing exact oracles and hang watchdogs are unchanged.
- Native package jobs and managed test jobs run independently after identity;
  `release-assets` retains `needs: [test, package]` and no unconditional success
  override. A failed test or package still blocks the complete download set.
- Failure-only payload artifacts use `release-diagnostic-payload-*`, not the
  `release-package-*` download pattern. They do not enter the release index or
  qualified assets. "Private reproduction" means CI-only/non-release evidence,
  not a confidentiality guarantee for a public repository's workflow artifacts.
- The Windows external observer no longer sends a client-local CHARRANGE pointer
  using EM_EXGETSEL to another process. Pointer-free EM_GETSEL reads packed bounds;
  both signed and unsigned -1 overflow forms are refused and fixtures are bounded
  below 65,535 UTF-16 units. This is an observer bound, not editor capacity.
  [Microsoft's EM_GETSEL contract](https://learn.microsoft.com/en-us/windows/win32/controls/em-getsel)
  and [SendMessage marshalling boundary](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagew)
  independently support the corrected transport.
- Prompt observation waits for both edit and accept controls, writes exactly one
  answer, checks successful text setting and exact readback, then posts exactly
  one acceptance. No edit/Save retry conceals missing acknowledgments.
- The Grid regression uses a dedicated caller thread, captures exceptions, joins
  before examining its result, releases the owner barrier in cleanup and joins
  both threads. Original Unsupported/Unavailable/no-late-mutation assertions and
  production timeout remain. This avoids reliance on test-runner pool scheduling
  without changing production behavior.

Source SHA-256 at this correction checkpoint:

| File | SHA-256 |
| --- | --- |
| `.github/workflows/release.yml` | `17753acad71f84cad8f3e1af4b00e8854812487a38838a05b0a8b8406a72aee0` |
| `tests/Invoke-NativeWindowsReleaseProduct.ps1` | `5c11a6ae2ff054a5b69ae0a7eea87f0902c23fcfb0f8051a936ea7e19a198a24` |
| `tests/Mote.Tests/WindowsGridAccessibilityTests.cs` | `ea3a5b88da575b88c9a2d6edcc0bb354352ffe4b474c7d8b0a45d0a2d6304cef` |

This source review does not relabel the failed hosted workflow as success.
Local managed six-format success is not hosted Native AOT or release-default
qualification. No tests/builds/GUI/full audit were rerun by this reviewer.

## Ordinary default, CSV terminal readiness and final Mac capture review

Checkpoint HEAD: `d77f53dfe6029a3c4544e57c0b2cc42834b44efb` (CSV correction
committed during review); default/Mac/task-oracle changes reviewed in the
working tree. Reused the earlier Grid driver/controller contracts,
`release-product-csv-readiness.md`, release acceptance record and Mac validation.
**No substantive defect established in this bounded correction scope.**

### Default and compatibility

Bare launch, a path and `--smoke-gui` now choose NativeSource; `--native-source`
remains its explicit equivalent. `--continuous` preserves the earlier immutable
window-lifetime product, and explicit LegacyPage/canvas/UIA diagnostics retain
routing. Existing Continuous-specific external clients now pass that explicit
flag instead of silently testing the new default with their old Canvas oracle.
Windows release edit/reopen and configuration smoke remove the candidate flag.
Mac tasks parse the ordinary path and use the production shell factory, rejecting
an unexpected profile. Opt-in config smoke additionally requires successful
native source install/readback trace evidence. The launch models test default
and preserved routes; no file-size fallback or in-window presentation swap was
introduced. Actual new-default hosted qualification remains necessary.

### CSV delivery and lifetime

The original small-file defect is retained with a failing native test: same-version
Complete source analysis plus a ready three-row navigation map did not hydrate
the first two native rows. The correction is not a screenshot delay or a false
readiness label. Controller captures visible-row geometry on its UI turn; the
immutable dispatcher Work transports it without background UI reads. Under the
existing driver semaphore, only a Source-anchored request beyond the certified
exact last viewport makes a second bounded Row query. That query uses the same
snapshot/interests/scope/column and row limits, with an empty edit chain because
the first query already applied the edits. Final baseline commits once.

If the second query fails/cancels, the session and committed baseline are retired;
next analysis rebuilds from the authoritative snapshot. No source text/Engine
history changes occur. Existing geometry changes cancel the analysis serial and
schedule the new interest, while UI delivery checks cancellation, serial,
document/driver identity and version; a captured old page cannot publish as the
new geometry. Source-follow mode is retained in the controller, not converted
into a persistent detached Row anchor or UI retry loop.

Independently read retained `csv-final-native-labels.trx`: **37 executed, 37
passed, zero failures/not-executed**. Inspected the tests' actual HWND slot
identities, all nine owner-data labels, item count, late-mailbox stability,
subsequent source-follow return and deterministic second-query cancellation.
This is local managed Windows evidence, not a new hosted AOT verdict.

### Mac final current-version witness

Post-Save capture now waits for actual current semantic/style/Grid facts rather
than the earlier precommit theme stage. The model requires exact installation
stamp/nonce, current analysis sequence, globally Complete full-source coverage,
installed semantic/style identity and known geometry. CSV additionally requires
current ready projection/frame and zero pending visible cells. Missing/Clipped/
Oversized cells are not renamed Complete merely because they are nonpending.

The reflection-free sidecar uses fixed content-free fields. Its separate Python
oracle verifies expected format and independently computed source units, valid
counts (including the supplied CSV task's three rows/three columns/nine cells),
and links its version to a complete Save request plus successful current-version
parse/publication/style operations. Nonce/sequence authority remains an admission
model witness, not separately encoded trace attestation; comments state this.
Capture is owned AppKit content, not physical screen/presentation certification.
Independently read retained `mac-release-semantic-guards-complete.trx`: **29
executed/passed, zero failures/not-executed**. Portable guards do not prove this
new AppKit/AOT capture path has run successfully.

Current docs say publication remains pending, do not promote earlier four-RID
candidate passes to new-default/CSV qualification, and retain actual Pinyin,
reader, pixel/performance and unsigned download limits. No 100-MB requirement
or new latency SLO was added. No code/tests/build/native GUI/CI reruns were
performed by this reviewer.

Selected source hashes for this checkpoint:

| File | SHA-256 |
| --- | --- |
| `src/Mote.Native/NativeFormatSessionDriver.cs` | `cc9b281b147c73ea4d8e34ca9ad917156c805194cd7fe2c38279eb16c20e254c` |
| `src/Mote.Native/NativeAnalysisDispatcher.cs` | `f8ffc948fe34cb85ce11d03a4500993d3a1ecff8d95d2a48d6ad331079974d1d` |
| `src/Mote.Native/EditorPresentationProfile.cs` | `a7fe7df37f8d40b876ea511a99f31a1d7d6accf2510d108396c1f6772c413fed` |
| `src/Mote.Native/Mac/MacReleaseSemanticModel.cs` | `59e71472b13580d99f318d462aa6adc655a3ded4982dd7813d9945fc92cbf245` |

### Windows final semantic/Grid observer addendum

Reviewed the additional working Windows observer after the product owner froze
Mac source. **No substantive defect found.** It now waits for the version from
the successful instrumented Save, then observes successful parse/publication/
style at that version and current native status/preview. CSV values are decoded
independently with TextFieldParser and compared to all real owner-data labels;
this is stronger than treating Complete status alone as table hydration proof.

The cross-process LVITEMW observer checks target HWND PID ownership and 64-bit
caller/coordinate bounds. It requests only VM_OPERATION/VM_READ/VM_WRITE, places
both naturally aligned structure and text storage in the owned child, checks
exact transfer lengths and a bounded NUL-terminated result, and frees local and
remote allocation plus process handle in `finally`. The inspected product
`WindowsCsvGrid.FillDisplay` copies into that provided buffer rather than changing
its address. [Microsoft LVM_GETITEM](https://learn.microsoft.com/en-us/windows/win32/controls/lvm-getitem)
and [LVITEMW](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/ns-commctrl-lvitemw)
were checked independently. No thread/code injection or desktop-global input
was introduced. Polling repeats observations, not edits or Save commands.

Successful new-default local managed tasks and portable guard tests are not
relabeled as hosted AOT. The final hosted source/package/visual evidence still
belongs to the release coordinator. Review ready for the next freeze.

## Mac release observer correction after run 36928548957

Baseline HEAD: `edef3379512332b0fe15b1136622d3ac82e54a40`; this review covers
only the working Mac renderer/failure observation corrections and tests/docs.
Read the updated `release-product-mac.md` and previous acceptance scopes first.
**No substantive defect found in the inspected correction.**

The prior successful source/style version-4 phases do not erase the hosted CSV
task refusal or the separate older version-3 presentation failure. The old
observer's dependency on optional experimental AX publication is replaced with
actual installed renderer facts, not an enabled provider or a success fallback.

`MacCsvGrid.ProbeReleaseRenderedGrid` reads the actual installed identity,
projection, native slot array, display/installed column ranges, rendered string
matrix shape, navigation and installation flags. `MacReleaseGridModel` refuses
missing slots, wrong absolute row ordinals, different row objects from the
installed projection, uninstalled columns, mismatched matrix shape and unfinished
installation. The shared `NativeGridAccessibility.Create` call only creates a
pure bounded value frame: it does not register or query experimental AX, and
it additionally removes readiness for pending or identity-mismatched navigation.
The semantic guard still requires exact current document/version/sequence,
full Complete coverage, installed style, known geometry and nonpending cells.
This observation certifies retained renderer data, not physical pixel delivery.

The one retained `NativeAnalysisFailure` record contains only the **attempted
analysis stamp and serial captured before background scheduling**, a closed
exception category and numeric HResult. It does not retain an exception object,
message, arbitrary type name, source or path. `PostAnalysis` keeps its prior
failure/containment flow; no retry or success conversion is added. The current
observation is qualified separately using installed/current stamp and analysis
serial. `analysis_failure_current_match` therefore cannot relabel a version-3
attempt as a current version-4 failure, or transfer the same numeric version
across document generations. The record describes a caught failure of the
attempted callback, not a fresh readback of the current document at catch time.

At the unchanged stage-8 deadline, refusal diagnostics write fixed refusal codes
and numeric attempted/current/installed semantic/style/Grid facts to a separate
failure sidecar and optionally capture the existing owned content. These are not
the success sidecar. Diagnostic-write failure does not turn the task green.
A historical caught failure is also preserved beside a later successful witness
with an explicit identity/serial match flag. The 40-second watchdog, exact Save,
current semantic checks and ordinary-task scope are unchanged.

Independently inspected existing tests and read
`.temp/mac-release-guards/mac-release-render-history-qualified.trx`: **37
executed/passed, zero failures/not-executed**. Guard cases cover AX-independent
renderer success, absent/different native slots, columns, shape, pending state,
stale semantic identities and historical failure qualification. The new
controller test captures version 3, queues its exception, changes canonical text
to version 4 and checks the retained attempted stamp without a canonical edit.
No tests or GUI were rerun by this reviewer; this is not corrected hosted AOT,
physical Pinyin/VoiceOver/pixel or release qualification.

Selected reviewed source hashes:

| File | SHA-256 |
| --- | --- |
| `src/Mote.Native/Mac/MacReleaseGridModel.cs` | `0d0760176e455cd74d3ea1d2fa8cab6ccaa1787751cb455c444d2a111d561f28` |
| `src/Mote.Native/NativeAnalysisFailure.cs` | `cbb278c28a8719a76fcfa97bb2efcb4896302319ac6ff3b81861dccd9a96ad67` |
| `src/Mote.Native/NativeEditorController.cs` | `b6d6f4490f14c80736d28f9b208cbe008cb9f258d206f94b09479bf6bd734906` |

Producer freeze confirmed after combined affected validation. Independently read
`.temp/release-controller/release-mac-observer-final.trx`: **71 executed/passed,
zero failures/not-executed**. This includes the queued attempted-v3/current-v4
regression. It overlaps the prior 37-guard result; counts are not added. Source
hashes above remain unchanged at freeze. Targeted review complete and ready for
integration; corrected four-RID qualification remains pending.

## Final narrow Windows Save-observer ordering correction

Baseline: `a86121ea4a1477796b3174332b921e3efb716a89`. Reviewed only the working
Save-observer change, new deterministic regression script and Windows validation
append after hosted run `36931198293`. **No substantive defect found.**

The changed task sends Save once, waits for clean native modified chrome and
successful `document.save`, `save.completed`, `command.save` with the same actual
version/session, then reads the target text exactly once. Final semantic checks
still require current parse/publication/style and actual view facts. The trace
helper ignores an unfinished live JSONL suffix; the final closed-schema/causal
oracle still checks the complete retained trace after shutdown. There is no
sharing-exception retry/catch, repeated edit/Save, weakened byte comparison or
increased watchdog. Final release aggregation still needs both test/package jobs.

The regression extracts the production observer function by PowerShell AST,
uses a real owned FileShare.None guard, demonstrates the old read's sharing
exception, rejects persistence-only and mismatched-version completion, and admits
one exact read only after guard release and matching completion. Independently
read its retained result: zero reads before completion, one after, derived version
7 and `gui_tested=false`. Independently read the affected local suite report:
`passed`, win-x64, six fixtures. These reports are managed local evidence, not
hosted AOT/ARM64 or a retroactive pass of the failed aggregate. The documentation
preserves the original failure and explicitly states this distinction.

Reviewed source SHA-256:

| File | SHA-256 |
| --- | --- |
| `tests/Invoke-NativeWindowsReleaseProduct.ps1` | `b57fb45eb2b1598ff1caa9234af332060b40ad0cabb736a306467ab9e9d5794f` |
| `tests/Test-NativeWindowsReleaseSaveObserver.ps1` | `505e75821555eb32c5b53d9d03b31c5621c40dd5867f0b32fa1f7e13059f42a7` |

No production behavior, timeout, test, GUI or CI rerun was changed/executed by
this reviewer. Narrow review ready for integration and new exact-source hosted
qualification; no public-release completion claim is made.
