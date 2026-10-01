# macOS external ShowMenu acknowledgment: independent AppKit control

Date: 2026-10-01. Status: **portable guard checks passed; Apple SDK compilation
and exact-PID external execution pending**. No mote production code, workflow,
advertised role or established accessibility oracle changed in this workstream.

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
counters independently retain any duplicate dispatch or missing effect.

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

## Procedure and current evidence

```powershell
./tests/MacGridAxActionContractProbe/Test-Guards.ps1
./tests/MacGridAxActionContractProbe/Run.ps1 -RuntimeIdentifier osx-x64
./tests/MacGridAxActionContractProbe/Run.ps1 -RuntimeIdentifier osx-arm64
```

On this Windows host, the portable test passes driver syntax, two RID
preflights, and three invalid report path refusals. It confirms no invented
native typecheck/build/process/effect results. Root must add a separately
non-gating Mac target step after independent review, preserving the whole
original product AX gate and strict single-binary inventory independently.

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
