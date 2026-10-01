# macOS external ShowMenu acknowledgment: independent AppKit control

Date: 2026-10-01. Status: **seven controls compile and execute on both native
targets with successful AXShowMenu replies; the product failure remains and
the tested generic metadata/getter/legacy-bridge explanations are unsupported**.
No mote production code, advertised role or established accessibility oracle
changed in this workstream. Root separately integrated the non-gating workflow.

## Preserved observation and rejected rediscovery

The latest [proxy evidence ledger](mac-grid-table-proxy.md) records both Mac RIDs
in CI 36800944850 and 36802378381: the product's modern show-menu callback admits
one next-turn native popup, native delegates record opening/closing, and the
guarded independent external coordinate navigation/selection/retirement/normal
close workflow completes. The original external AXShowMenu returns -25205;
the whole external report therefore still fails, despite 40 other checks.

The modern AppKit action constant equals `AXShowMenu`, its shown-menu attribute
constant equals `AXShownMenuUIElement`, the action is advertised, both exact
modern selectors are allowed during native will-open, and the Table's owned
native menu relation is accessible. Those observations reject the earlier
different-wire-key and live selector-permission refusal hypotheses. More
timeout, traversal budget, title relaxation or synthetic global input cannot
repair a failing original acknowledgment and are not proposed.

Apple's [accessibilityPerformShowMenu contract](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol/accessibilityperformshowmenu())
defines BOOL true as successfully **triggering** the action, not completing its
effect. The current queued acknowledgment is therefore not intrinsically an
incorrect return semantic. Error -25205 is
[attributeUnsupported](https://developer.apple.com/documentation/applicationservices/axerror/attributeunsupported),
not actionUnsupported or cannotComplete. No inspected evidence identifies the
private AppKit bridge that produces that reply after actually invoking the
modern callback.

## Discriminating hypotheses, not fixes

1. **Compiler/runtime metadata:** mote registers the show-menu IMP with `B@:`;
   Apple's [Objective-C type encoding guide](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/ObjCRuntimeGuide/Articles/ocrtTypeEncodings.html)
   distinguishes C char and C bool type metadata. Calling a direct one-byte IMP
   correctly does not alone prove every framework introspection path interprets
   its metadata identically. Native compiler-provided, `B@:` and `c@:` controls
   test this without assuming that Intel and ARM use the same BOOL typedef.
2. **Getter spelling:** mote's proxy registers `accessibilityEnabled`, while
   Apple's [modern protocol](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol)
   uses `isAccessibilityEnabled`. An inherited default may still provide the
   actual attribute; the spelling defect is an inspectable mismatch, not yet a
   cause of AXShowMenu's reply. Both direct getter response/value facts and
   external AXEnabled facts are collected, with no product correction yet.
3. **Residual legacy action bridge:** current
   [Chromium AXPlatformNodeCocoa source](https://github.com/chromium/chromium/blob/4a27de5bfd26225308f44e393bf2c560d31e8043/ui/accessibility/platform/ax_platform_node_cocoa.mm)
   explicitly comments that deprecated accessibilityActionNames and
   accessibilityPerformAction are still called internally and are not
   implemented by NSAccessibilityElement. It also implements modern action
   methods; its migration feature filters modern actions from legacy names.
   This is production precedent for investigating an interoperability boundary,
   not evidence that adding old methods to mote is necessarily correct.

The downloaded Chromium file is preserved locally at
`.cache/mac-grid-showmenu-contract/ax_platform_node_cocoa-pinned.mm`; relevant action
implementation is around lines 1471-1567 in the inspected snapshot. Its repository
commit is `4a27de5bfd26225308f44e393bf2c560d31e8043`, file SHA-256 is
`FFA417CD60D5DDAB12752BFFE9C2861CA8805F0B8A3F8ABBE388B3B7366840B9`,
and the frozen file matches the original moving-main download byte-for-byte.

## Independent synthetic control

`tests/MacGridAxActionContractProbe/Contract.m` creates an AppKit owner and seven
empty AXTable elements, with no document and no mote process:

| Fixed variant | Action implementation/metadata | Enabled getter | Legacy action bridge |
| --- | --- | --- | --- |
| compiled-modern | Objective-C compiler | correct | absent |
| runtime-native | runtime IMP, compiler-derived encoding | correct | absent |
| runtime-B | same runtime IMP, B@ metadata | correct | absent |
| runtime-c | same runtime IMP, c@ metadata | correct | absent |
| runtime-wrong-enabled | runtime IMP, compiler-derived encoding | misspelled only | absent |
| compiled-legacy-bridge | compiler | correct | exact ShowMenu only |
| runtime-legacy-bridge | runtime IMP, compiler-derived encoding | correct | exact ShowMenu only |

All admission callbacks use the same next-turn NSMenu popup and its owned
tracking-mode cancellation. Dry-run direct objc_msgSend/NSInvocation inspection
has no queued effect. The external exact-PID child performs exactly one
advertised ShowMenu per variant and reads only fixed counter states from AXHelp.
Actual native modern/legacy dispatch counters and menu will-open/did-close
counters independently retain any duplicate external dispatch or missing effect.
`Metadata()` invokes the modern action twice before external execution, once
through direct objc_msgSend and once through NSInvocation. The `inspecting`
flag suppresses both dispatch counters and menu admission/scheduling for those
dry runs. Consequently modernCalls=1 records one external dispatch, not the
total number of IMP invocations; these controls are metadata-prewarmed.

Only the synthetic owned window is ordered front; the probe never calls app
activation, system-wide AX roots, global mouse/key injection or TCC mutation/prompt. It
never opens, saves or links the mote binary or any user file. The owner process
and its exact child shut down normally after collection; watchdog fallback is
explicitly classified as forced cleanup and cannot pass.

Client bounds: one window, at most 32 discovery nodes, each child array <=16,
action-name array <=16 after unavoidable API copy, 20 discovery attempts,
20 state polls per variant, 0.5-second per-node AX messaging timeout and a
20-second checked client deadline. A 45-second independent process-tree
watchdog bounds stalled native calls. None changes the existing product gate's
deadline or query budget.

Artifacts are confined by the owner script to repository
`.cache/mac-grid-action-contract/<rid>/` and `.cache/ci-inventory/*.json`.
`control-completed` requires all seven advertised controls and one independently
observed menu open/close each, **not** AX return code zero. It is an experiment
completion classification, never whole product acceptance.

## Procedure and portable evidence before native execution

```powershell
./tests/MacGridAxActionContractProbe/Test-Guards.ps1
./tests/MacGridAxActionContractProbe/Run.ps1 -RuntimeIdentifier osx-x64
./tests/MacGridAxActionContractProbe/Run.ps1 -RuntimeIdentifier osx-arm64
```

On this Windows host, the portable test passes driver syntax, two RID
preflights, and three invalid report path refusals. It confirms no invented
native typecheck/build/process/effect results. Before the first native run,
root integrated the separately non-gating Mac step after independent review,
preserving the whole original product AX gate and strict single-binary
inventory independently. The native evidence is recorded separately below.

Interpretation after target execution:

- Native compiled baseline succeeds but runtime-native fails: runtime registration
  or introspection difference is supported, not a general AppKit defect.
- Only B or c differs: metadata is causally discriminated within this control;
  reproduce the corresponding narrow product fix before claiming closure.
- Correct getter differs from misspelled getter: investigate native/external
  enabled facts and reproduce in product; do not infer from response presence.
- Only legacy bridge changes the reply, with exactly one actual menu and known
  dispatcher: residual bridging becomes a supported causal candidate. A product
  fix still needs real two-RID oracle success and must share one admission path.
- All controls succeed: the product-specific graph/lifetime/reentrant bridge
  remains the relevant difference, and this metadata experiment did not resolve it.
- Baseline fails or native effects are missing: retain negative evidence and
  investigate the control itself before assigning a product cause.

## Research perspective and scope

Recent peer-reviewed
[A11yNavigator work (ASE 2025)](https://ieeexplore.ieee.org/document/11334511/)
tests whether screen-reader users can locate **and activate** elements, beyond
static labeling checks. This is web/NVDA research, not an AppKit mechanism
source. Its relevant implication is methodological: correct tree metadata,
successful transport acknowledgment, actual effect and reader navigation are
distinct validation layers. This control probes only the first three synthetic
layers. VoiceOver, real input composition, arbitrary-file behavior, geometry
and interaction performance remain independent unclaimed gates.

## First native execution and independent audit: CI 36811953139

[Run 36811953139](https://github.com/kleedaisuki/mote/actions/runs/36811953139)
at commit `d9ddda97cedf98245fd499c8bb6a64cf64919633` runs the reviewed control
without changing its source bytes. Both native jobs actually print portable
guard success and the full completed JSON report; this audit downloaded the
two artifacts and raw target job logs rather than interpreting containing
non-gating job colors as evidence.

| Exact target fact | osx-x64 | osx-arm64 |
| --- | --- | --- |
| Native job ID | 110208733159 | 110208733733 |
| Host version | macOS 15.7.9, 24G830 | macOS 26.6.2, 25G83 |
| Runner image | macos-15, 20260824.0482.1 | macos-26-arm64, 20260907.0351.1 |
| Artifact ID / ZIP bytes | 11140081144 / 2035 | 11140441333 / 2055 |
| Native typecheck / compilation | true / true | true / true |
| Wrapper / client status | control-completed / control-completed | control-completed / control-completed |
| Actual owner / child exits | 0 / 0 | 0 / 0 |
| Normal owner shutdown / forced cleanup | true / false | true / false |
| External trust / distinct target and client PIDs | true / true | true / true |
| Source unchanged / wrapper error | true / empty | true / empty |

Artifact names are `mac-grid-action-contract-osx-x64` and
`mac-grid-action-contract-osx-arm64`. Both exist; the default 30-item GitHub
artifact API page does not include x64, so this audit used `--paginate` before
classifying availability. Each downloaded artifact contains the wrapper report,
standalone `server.json`, and standalone `client.json`. JSON-normalized server
and client objects match the corresponding embedded wrapper objects on both
targets; no missing report or contradictory exit fact was found.

The actual source SHA-256 is
`60ECD9D078DC997F98D3EB7D265C916C847D9FA15F2A909B0AA1A9C6B2EB046A`
on both targets, matching the original frozen source, not merely one accepted
CI line-ending hash. Native compiler-provided action encodings are `c16@0:8`
on Intel and `B16@0:8` on ARM64. Every variant has one-byte method return length
and returns true both through direct objc_msgSend and dry-run NSInvocation.

### Exact seven-variant reply and dispatch results

In **every cell below**, the external action-name call returns error 0 with
exactly one advertised action; the actual AXShowMenu returns error **0** and
the independent external lifecycle predicate sees one opened and closed menu.
Final native counters are requests=1, opens=1, closes=1, modernCalls=1,
legacyCalls=0. Here modernCalls=1 is the one admitted external action route,
not total IMP invocations: the two earlier `Metadata()` dry-run invocations
are deliberately uncounted and schedule no menu effects. The legacy override
is present in the two bridge variants but is
**not invoked** in the observed external action route.

| Variant | x64 action encoding / AX reply | ARM64 action encoding / AX reply | External AXEnabled, both targets |
| --- | --- | --- | --- |
| compiled-modern | c16@0:8 / 0 | B16@0:8 / 0 | true, error 0 |
| runtime-native | c16@0:8 / 0 | B16@0:8 / 0 | true, error 0 |
| runtime-B | B@: / 0 | B@: / 0 | true, error 0 |
| runtime-c | c@: / 0 | c@: / 0 | true, error 0 |
| runtime-wrong-enabled | c16@0:8 / 0 | B16@0:8 / 0 | **false**, error 0 |
| compiled-legacy-bridge | c16@0:8 / 0 | B16@0:8 / 0 | true, error 0 |
| runtime-legacy-bridge | c16@0:8 / 0 | B16@0:8 / 0 | true, error 0 |

Both exact modern action and enabled-getter selector permissions are true for
all variants. The wrong-enabled control responds to the misspelled getter and
returns true through it, but its inherited real `isAccessibilityEnabled`
responds and returns **false**; external AXEnabled faithfully returns boolean
false with error 0. Despite that disabled metadata, the explicit advertised
ShowMenu request still dispatches through the modern callback once and returns
success. This is a real getter-spelling defect in the control, but it does not
produce the product's -25205 reply in this isolated experiment.

Recorded AXHelp poll counts, in table order, are x64 `[4,5,5,5,4,5,5]` and
ARM64 `[4,5,5,6,5,4,4]`, below the 20-per-variant ceiling. They are observation
effort, not editor latency measurements. No hard-watchdog cleanup occurred;
the preserved 20-second deadline remains cooperative, not independently hard.

### Same-run product comparison and supported falsification

This audit additionally downloaded both original `mac-grid-ax-external` product
artifacts from the same run. They still report **failed / Swift exit 1** with
the sole false check `context-menu-accessible`, AX=-25205; the action is
advertised once. Both guarded independent downstream workflows complete,
wrapper-observed editor exit is normal, forced cleanup is false, input hashes
remain unchanged, and wrapper errors are empty. Both native product traces
contain one show-enter, schedule-return(result=1), popup-begin, will-open,
did-close and popup-return(result=1), with one request/open/close. Thus the
product modern action callback was actually entered; this is **not** a
no-callback observation or an unavailable-instrumentation inference.

Supported conclusions are deliberately scoped:

1. Runtime class registration alone, abbreviated B/c metadata alone, and
   generic one-byte BOOL introspection failure do **not** reproduce this error
   in the independent native controls on either tested target. A product-specific
   Native AOT callback/bridge difference is not eliminated by native C IMPs.
2. The enabled-getter misspelling changes actual advertised state, but is not
   sufficient by itself to explain -25205 in this control. Correcting it would
   require a separate truthful-state contract change and product validation,
   not a claim that this experiment fixed ShowMenu.
3. No observed control needs legacy dispatch to acknowledge this modern action;
   adding a product legacy bridge based solely on Chromium precedent is not
   supported. This does not claim that all AppKit legacy interoperability paths
   are unnecessary, only that they did not cause this discriminated behavior.
4. The experiment completes and the product gate still fails on the same hosts.
   The remaining difference is product-specific; its exact mechanism is unknown.
   Native control success is **not** product acceptance or VoiceOver acceptance.

### Next smallest product-versus-control discriminator

Use one frozen external AX client/action function for both a single native
`runtime-B` control and the product's current semantic Table, each in a fresh
exact-PID process with **one** action attempt and the original error preserved.
The fresh minimal control must omit the `Metadata()` action prewarm; a fresh
process that still runs its two dry-run actions is not an uninvoked baseline.
The current control uses native C while the original product probe uses Swift;
both call AXUIElementPerformAction, but this audit has not isolated client
language/preparatory-query history. Sharing a client removes that difference
before changing any action bridge or introducing another mechanism.

The smallest additional instrumentation is content-free and opt-in:

- Immediately before the action, record exact-PID ownership, expected-role and
  expected-parent classifications, advertised fixed action membership, and
  whether re-discovery yields CFEqual to the retained Table. Record only equality
  and bounded count/error facts, not native pointers or identifier/title text.
- At the product action entry, record a bounded request serial and booleans for
  owner lookup, current-root equality, live attachment, installation in progress,
  current epoch equality and admitted queue result. Record rejection **before**
  returning from a stale/off-thread path so an absent ordinary lifecycle trace
  cannot be confused with lack of instrumentation.
- At action return and a bounded read-only followup, recheck the retained versus
  current Table, role/parent identity and retirement state, with unchanged query
  bounds, no ShowMenu retry and no navigation fallback. If comparing minimal
  startup versus the original 25-check preparation, use separate target processes
  and identical action code, never a second action on the same attachment.

Interpretation must distinguish **observed rejection/no admitted callback**,
**callback entered and admitted but reply failed**, and **unknown because entry
instrumentation was absent, exhausted or unavailable**. Existing product traces
already support the second classification for this run, but do not establish
the exact retained-element epoch, framework wrapper identity or reentrant read
sequence during the AX transport reply. If those identity/lifetime facts agree,
the next targeted question is which fixed informational selectors AppKit reads
during action dispatch versus the control, not a speculative legacy patch.
No product mutation, behavior correction or new probe implementation is made
by this document-only audit.

### Reproducibility artifacts

Downloaded raw logs/artifacts stay under
`.cache/ci-36811953139-mac-action-control/`, with `x64/`, `arm64/`,
`product-x64/`, `product-arm64/` and sibling `*-job.log` files.
Wrapper report SHA-256 values are:

- x64: `862E871FE5FE32762650DB980D84E646A301FC89216877BC25939840A7069443`.
- ARM64: `ED56AB42662FEDE439243A3EE03A68FFA6378C2107C807D1871FAF19D91D7D0C`.

The remote artifacts expire with their configured retention; fixed source,
run/commit/job/artifact identifiers, host versions and the full bounded results
above preserve the conclusion even after download URLs expire.
