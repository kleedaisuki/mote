# macOS bounded Grid AXRows bridge experiment

Date: 2026-10-01. **Rejected opt-in experiment.** The two-RID external
falsification below triggered its stopping rule; the legacy override was removed
and replaced with the [coherent Table proxy candidate](mac-grid-table-proxy.md).
The earlier design and local evidence are preserved as history, not current code.
Production defaults remain unchanged.

## Evidence and the next question

CI run [36787947202](https://github.com/kleedaisuki/mote/actions/runs/36787947202)
at `5977250` executed the unchanged separate-process AX client against both
Native AOT Mac RIDs. Both passed Swift typechecking, AX trust, exactly one
semantic Table and full-source coexistence, then failed `first-record-is-data`.
Reports are preserved under
`.cache/ci-36787947202-mac-grid-ax/{x64,arm}-artifact/mac-grid-ax-external.json`.
The integration owner and separate harness owner supplied and inspected these
reports; this Windows host does not execute AppKit.

The first `AXRows` element has role `AXRow` and local index 0, but Description,
Title, Value and Help all return unsupported (`-25205`); Identifier has no value
(`-25212`). The first `AXColumns` element exposes our exact `Column 1`
description, current-window wrapper identifier and Help. Both row label
attributes are absent, so Description-before-Title extraction precedence is
not the cause. The native in-process modern selector probes had already passed.

This strongly supports a native NSTableView row bridge/default replacement
path, but does not identify its private internal class or prove which selector
the external bridge calls. We must not infer that record 1 was consumed as a
CSV header, nor relax its required absolute `Row 1` label.

The narrow next question is: **does NSTableView's legacy `AXRows` attribute route
bypass the installed modern `accessibilityRows` getter, and can returning the
same wrappers from that route resolve the external mismatch?**

## Route A: bounded experimental discriminator

Under the existing `MOTE_NATIVE_GRID_ACCESSIBILITY=1` registration gate only,
the native Table subclass overrides `accessibilityAttributeValue:`. It handles
only `AXRows`, returning the current bounded row wrappers already owned by the
modern provider. It neither synthesizes different nodes nor adds a second
selection model. All other attributes delegate to `NSTableView` via
object-return `objc_msgSendSuper`.

The callback refuses off-main dispatch and absent owner lookup before reading
managed mutable state. `AXRows` additionally requires a coherent installed
frame and no installation in progress. Disposal removes the Table lookup, so
an externally retained native object cannot find a detached owner. No callback
waits for workers, accesses source, or decodes the file. Exceptions cannot
unwind into Objective-C.

The native target probe now checks:

- legacy and modern row arrays have equal counts and the exact same first
  wrapper object;
- that wrapper retains exact `Row 1001` and the current-window identifier;
- direct off-main legacy dispatch returns nil without inspecting owner state.

The background-task wait occurs only in the disposable diagnostic, never in a
production accessibility callback. Native execution of these new checks still
requires both Mac hosts. The external Swift client, its oracle, and CI workflow
remain unchanged by this workstream.

### Explicit falsification and stopping rule

Run the existing external harness from freshly published matching-RID binaries.
If it still observes an unlabeled/default row, or if passing requires expanding
legacy per-attribute machinery, reject route A rather than accumulating
exceptions. Existing in-process selector success cannot answer this question.
If it passes the row check, inspect the subsequent cell/range/parent/selection/
navigation/retirement/normal-close gates rather than promoting a partial marker.
Even a full external harness pass would not close VoiceOver or real IME gates.

## Route B: coherent fallback, not implemented in this experiment

Use one stable custom `NSAccessibilityElement` Table root under the existing
Grid AXGroup. Reuse the bounded frame and current epoch row/column/cell wrappers;
rows parent to the proxy, cells parent to their rows. Keep the visual
NSTableView as the actual first responder, rendering/keyboard/menu owner, not
as a second semantic Table.

The containing Group must provide explicit accessible children: proxy Table
plus real logical scrollers/detail/navigation controls. Merely setting
`isAccessibilityElement=false` on the native table is not subtree suppression;
ignored children can be promoted. Focus and hit-test routes must resolve to the
same current proxy/cell, while source/window/input island behavior is unchanged.
The stable proxy owner mapping must be removed before native release; epoch
nodes still become unavailable on replacement. Geometry continues to derive
from real AppKit rendering bounds, not invented proxy bounds.

This removes the native Table bridge special case and is likely simpler to
maintain than an expanding legacy attribute matrix. It also entails explicit
focus/menu/hierarchy/lifetime contracts, which must be verified externally on
both ABIs. No public API, Engine/Formats change, native library, runtime dylib,
or additional product payload is required for either route.

## Primary-source rationale and maintenance comparison

Apple's [custom-controls guide](https://developer.apple.com/library/archive/documentation/Accessibility/Conceptual/AccessibilityMacOSX/ImplementingAccessibilityforCustomControls.html)
documents role-specific protocols and custom NSAccessibilityElement nodes,
including explicit parent/children wiring and frame-in-parent-space. Its
[modern NSAccessibility protocol](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol)
supports overridden informational getters and separate mutation access.
The [older hierarchy guide](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/Accessibility/cocoaAXManipulateHierarchy/cocoaAXManipulateHier.html)
explains ignored-node promotion and explicitly discourages the old key-based
API in favor of the method-based API.

Thus route A is useful as a cheap discriminator, not proof that deprecated
bridging is a good long-term architecture. Production-practice grounding here
is using platform accessibility mechanisms and preserving real native input
ownership, rather than replacing rendering or shipping another runtime. The
bounded-contract research and cached-frame rationale remain in
[`csv-grid-accessibility-contract.md`](../csv-grid-accessibility-contract.md);
no new academic claim can substitute for the observed cross-process failure.

## Validation ledger

### External falsification: 36789139005 / 3aae8d5

Fresh hosted [CI run 36789139005](https://github.com/kleedaisuki/mote/actions/runs/36789139005)
executed the unchanged external Swift client on both published Mac RIDs.
Both reports remain `failed`, with eight prior checks passed and
`first-record-is-data` failing at origin-window. Typecheck/trust passed, Swift
exit was 1, cleanup was forced, and fixture bytes remained unchanged. The
helper source hash remained `A65E460046E0AA72B88F5E4289CAA0915A4EE15E1C24984B9C40DC6FD1227B8C`.
This falsifies route A as a fix for the external contract.

Preserved reports:
`.cache/ci-36789139005-mac-grid-ax/{x64,arm}-artifact/mac-grid-ax-external.json`.
Raw report SHA-256: x64
`7BD182F1C110161B58919BEC5359B73FAFB562AB7C8BB67168CB0A778AF8C618`;
ARM `1B846F3694E6949DDE22ECEE7F9BA6A1BEA71C5B4245D99048F2B25F4BB1115E`.
All nine strict CI jobs were green, but that summary cannot override the
non-gating diagnostic's failed report. The in-process selector source pin was
stale and its diagnostic did not execute; no native selector result is inferred.

### Historical local compilation

Windows/.NET SDK 10.0.400: `dotnet test tests/Mote.Tests/Mote.Tests.csproj --filter
'FullyQualifiedName~MacGridAccessibility' --verbosity minimal` passed **10/10**.
The tests compile the changed callback and probe, but cannot test AppKit or
external AX. The TRX is preserved in
`.cache/mac-grid-rows-bridge/legacy-rows-experiment.trx`. Independent review is
recorded in [the Table bridge review](../reviews/mac-grid-table-proxy-review.md).
Native execution of the three new probe checks and unchanged external harness
remains untested at this delivery. Preserve the old two-RID
negative evidence regardless of the new result. No release-default change,
VoiceOver speech claim, or repaired native acceptance is implied by this file.
