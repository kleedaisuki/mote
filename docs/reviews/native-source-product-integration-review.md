# Native source product integration review

Date: 2026-10-02. Review owner: source_product_review.

## Scope and evidence boundary

Independent inspection of the explicit `--native-source` product candidate against
section 8 of `docs/architecture/ordinary-editing-locus.md`. Inspected launch routing,
source contracts/binding, controller admission and range synchronization, Windows
RichEdit and Mac NSTextView integration, viewport foreground models, and surrounding
composition/history/selection/Save dispatch. The working tree was actively being
refined; HEAD at initial capture was `6827cd1a19142c9ad766d296c18d0770e600e79c`.
Untracked source files are part of the reviewed work, not necessarily that commit.

No production or test changes, local GUI, native input, build, or repeated tests were
performed by this reviewer. Static paths establish the defects below; they do not
establish actual AppKit presentation latency, successful IME input, accessibility
ranges, exact native undo, or four-RID acceptance. This is not default-promotion or
whole-product approval.

## Findings communicated to the implementation owner

### P1: Windows settlement can report success without admitted native text

Location: `WindowsEditorShell.Source.cs`, `ReadSourceCandidate`, and
`SetSourceUnavailable`; command gate: `WindowsEditorShell.cs`, `CommitPendingText`.

The original implementation invokes `SourceCandidate` and then returns only
`!_sourceCandidateFailed`. A controller rejection of a malformed candidate, or failed
acknowledgment, calls `SetSourceUnavailable`, which clears the installed identity and
makes the control read-only but does not set that flag. No exception need escape.
Thus a command can receive `true` although final native text never entered the
canonical document, and Save/Open/Undo can proceed with stale canonical source.

Confidence: high, direct synchronous call path. Necessary remedy: certify final
candidate acknowledgment (generation/nonce, final display, and replica availability)
before returning settlement success. Do not blindly disallow all recovery saves of
already-committed canonical data merely because an adapter is unavailable.

Status: sent to owner; correction review pending.

### P1: Windows post-IME settlement is rejected by the controller composition guard

Locations: `WindowsEditorShell.cs`, `FinishDefaultComposition`;
`NativeEditorController.Source.cs`, `SourceEdited`.

`FinishDefaultComposition` calls `ReadSourceCandidate` before clearing
`_imeSettling`. `IsTextComposing` includes `_imeSettling`, so the original controller
immediately ignores the final candidate. The shell nevertheless proceeds to clear
settling and announce `CompositionSettled`. A later queued readback is not a valid
admission guarantee, particularly with queued commands and failure paths.

Confidence: high, the flags and guards directly contradict the intended settlement
sequence. Necessary remedy: distinguish actual marked text from certified settled
candidate admission. Preserve guards for native mutations, attributes, document
replacement and commands during real marked text. Final settled display must be
acknowledged before successful command settlement.

Status: owner reports removal of the controller's blanket composing guard because
`SourceCandidate` is a settled-ingress contract. Windows acknowledgment and ingress
checks must be reviewed together after its writer finishes.

### P2: Mac draw observations unconditionally republish unchanged attributes

Locations: `MacEditorShell.cs`, `DrawEditorSource`;
`MacEditorShell.Source.cs`, `QueueSourceView`, `PublishSourceView`,
`PublishSourceForeground`.

Each native source draw queues a selection/view observation. Delivery calls
`PublishSourceView`, which unconditionally invokes `PublishSourceForeground`.
Foreground publication always enters an NSTextStorage editing transaction and writes
NSColor attributes, even when viewport, semantic identity and theme are unchanged.
Attribute invalidation may cause another draw, returning to the same path. There is
no last-installed viewport/palette/semantic identity cache in the inspected version.

Confidence: medium for sustained native redraw behavior (not run here), high for
redundant attribute writes on every draw/view delivery. This can create continuous
layout/draw churn and makes per-turn run limits insufficient. Necessary remedy:
retain an installed decoration identity or an equivalent equality check; unchanged
draw observations should not mutate text storage. Invalidate it on genuine text,
semantic, theme, and viewport changes. Runtime evidence must qualify idle stability.

Status: sent to owner; correction review pending.

## Initial exact-file provenance

SHA-256 of working files at initial capture (not a build/binary identity):

| File under `src/Mote.Native/` | SHA-256 |
| --- | --- |
| `NativeEditorController.Source.cs` | `E83299C6B371559DB21F268DD9722103F5A5EE97A53B8DBFC913DD39F9ED45A5` |
| `NativeSourceBinding.cs` | `2BD50614B47361B8AAF2C9944AF7E5AC2D6B533A44C59C033D6D91D3EA108674` |
| `NativeSourceContracts.cs` | `A2F44DC21BB268BB0D259C971246A9318BC5FB7F3B96AF07EA6593B74009D9AD` |
| `EditorPresentationProfile.cs` | `2AC6BB9D3602D4AB6060060B9D0F453A431D6C60AE5116622349E43EDBDAC745` |
| `Windows/WindowsEditorShell.Source.cs` | `E4704D73C422C78DE252D49AC15151A052CAE2B729BBF07EAC69A2653182F9AD` |
| `Windows/WindowsEditorShell.cs` | `0969467FA57939C6D26028747FBCE34D01BFAF44C21CB445C6408D78ED6195F8` |
| `Mac/MacEditorShell.Source.cs` | `695046CF7DAE14B7FDB2EE5975CCA2AD3E5519EB56FC182B45FF61F52C048208` |
| `Mac/MacEditorShell.cs` | `4CC4075BEEFDB8B89F909FA8F9AF91889A18C227582D787A75BD450A55DE24EA` |
| `Mac/MacProductSourceModel.cs` | `E6942B09735AC74041CAF46FD321035911E342C1E8DDC2716B0C17DACE3A2727` |

## Remaining acceptance boundaries, not extra findings

- This opt-in route deliberately does not change ordinary Continuous or explicit
  LegacyPage launch. No transparent file-size fallback was introduced in inspected
  routing. Full-resident O(n) readback/import remains a declared candidate limit.
- The pure binding refuses NUL and malformed UTF-16 candidates, checks original
  snapshot/nonce, uses the shared scalar-safe projection difference, and advances
  only after a real one-version engine commit. These are useful model contracts,
  not native UI execution proof.
- Native ordered ranges do not certify an active endpoint. DirectionKnown is
  retained by the binding, but caret-dependent Grid follow-source consumes the
  navigation Active value; endpoint-sensitive acceptance must not infer direction
  from a sorted native selection. This needs explicit qualification rather than
  claiming complete directional-selection support.
- Native UIA/AX source ranges, physical presentation, real marked text,
  cross-platform long-line layout, and new-process exact reopen remain outside this
  review's evidence. Existing capability-probe results do not substitute for the
  interactive product route.

## Follow-up during active refinement

The Windows writer added `WindowsSourceSettlement.Acknowledged`, which requires
same generation/nonce, advanced canonical version, certified editable map, and exact
accepted/installed final display. `ReadSourceCandidate` now vetoes an unacknowledged
emission. This addresses the first finding's direct false-success path in inspected
source; no runtime result is asserted.

Controller admission now consults the capability's `HasSourceMarkedText`, not the
broader shell command barrier `IsTextComposing`. The Windows property is the actual
`_imeComposing` flag and AppKit uses a BOOL-returning `hasMarkedText` accessor. This
allows Windows synchronous settlement while keeping real marked text rejected.
Together with final acknowledgment it addresses the second finding's static path.

The Mac writer added installed and pending viewport/style identities and deduplicated
view observations. This addresses redundant writes on unchanged draw observations.
The implementation must additionally distinguish same-presentation-sequence semantic
publications: `ScheduleAnalysis` emits empty provisional facts before advancing
`_presentationSequence`. A cache keyed only by that sequence would keep the previous
colored attributes instead of installing neutral pending presentation. This was
communicated promptly as a correction to the new cache, not a claim that the original
loop is already runtime-qualified.

Apple's [Changing an Attributed String](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/AttributedStrings/Tasks/ChangingAttrStrings.html)
explains that NSTextStorage editing completion notifies associated layout managers
for layout/display. This supports the invalidation mechanism, not proof of the exact
runtime loop or its duration. Microsoft's [selection documentation](https://learn.microsoft.com/en-us/windows/win32/controls/interact-with-the-current-selection)
describes ordered RichEdit selection bounds; an ordered-range read is not by itself
an observed active endpoint.

## Corrected-source checkpoint

At the final static checkpoint, the direct Windows false-success and settling paths
above are corrected. Failure classification distinguishes unadmitted native data
from recoverable canonical-retained data. The triggering failed candidate is vetoed;
a later recovery command is not automatically prohibited for already committed data.

Mac now increments `_sourceSemanticRevision` for every accepted semantic publication,
including neutral facts reusing a presentation sequence; installed/pending style
keys use that revision. This closes the newly introduced neutral-cache collision.
Unchanged native view observations and completed style identities are deduplicated.
Runtime idle stability is still required, not inferred from this source inspection.

CSV follow-source now uses an explicit ordered-range start when native direction is
unknown, through `SourceFollowOffset`, rather than representing that start or end as
an observed caret. This removes the inspected Grid consumer concern without claiming
the native source adapter knows direction in all noncollapsed selections.

No additional substantive defect was established in this scoped final inspection.
The acceptance boundaries above remain open. Final checkpoint hashes cover the
source files named below only; other writers may still refine integration files.

| File under `src/Mote.Native/` | SHA-256 |
| --- | --- |
| `NativeEditorController.Source.cs` | `D868202C14ABE3B159D08F44346408B4FCD3C2667EE3B0C61434DF1E172B0EE6` |
| `NativeSourceContracts.cs` | `50FC7FE3A6630BF78FBC76880B5F180D9A2FD4E8726DA45D34ED86F4C4156680` |
| `NativeSourceBinding.cs` | `2BD50614B47361B8AAF2C9944AF7E5AC2D6B533A44C59C033D6D91D3EA108674` |
| `Windows/WindowsEditorShell.Source.cs` | `5FA24065E7EBA9222996081AA01DDA025D72E768689ADB23E0708106FD218927` |
| `Windows/WindowsSourceSettlement.cs` | `4DB60D3BBFF87714080F437BEC8FFB5FE38B337D502C6280CA907C703B72FCC6` |
| `Windows/WindowsSourceForegroundPlan.cs` | `D344641A7CE5FC217B337E14002DD2F209C590393B00BCEC8686E63BC537AC0F` |
| `Mac/MacEditorShell.Source.cs` | `CD2B1AC437C3FC7BF0C97B33ADEB2FC8F189865ECB73A53B6F2521E3480561D3` |
| `Mac/MacProductSourceModel.cs` | `F9C6C9E4A56AEAC68BBA040A18D512BB2BDB9853238F7C148792CB0851FB1E90` |
| `EditorPresentationProfile.cs` | `2AC6BB9D3602D4AB6060060B9D0F453A431D6C60AE5116622349E43EDBDAC745` |

## Recovery and trace delta review

Reviewed the later closed `NativeSourceFailure` classification, explicit platform
consent and `SourceRecoveryRequested`, controller `RecoverSource`, Mac Undo/Redo
availability, five appended telemetry operation IDs/names, and the corresponding
closed vocabulary addition in `benchmarks/NativeAcceptance/acceptance.py`. No tests
were executed or replayed for this follow-up.

Recovery reinstalls the same canonical document/history/file identity using a fresh
nonce; editable authority follows exact import certification. Failed recovery
preserves the unadmitted barrier rather than silently treating it as recoverable
canonical-only data. Mac consent defaults to Cancel, blocks nested consent, and
checks the original installation again after its modal alert. Native source history
availability consumes controller-provided CanUndo/CanRedo instead of NSTextStorage
undo state. Telemetry IDs are appended without renumbering prior values, fixed
operation names match both emitter and strict reader, and no privacy attributes or
schema gates are relaxed. Readback means buffer-copy success; its enclosing install
or reconcile owns exact-match success, rather than calling copied text an engine
commit or physical presentation.

### P2: Failed native input cannot be salvaged with standard Copy

The read-only unadmitted Windows buffer remains visible, but `EditorSubclass`
intercepts every WM_COPY and routes it to canonical CopyRequested. Installed source
identity is already cleared, so FlushSelection cannot publish its selected range;
controller CopyOrCut calls CommitPendingText, which asks to discard that native input
and reinstall canonical text. Thus the proposed practical native-copy escape hatch
is not present. Mac Copy similarly routes through NotifyAfterComposition and the
recovery gate. Cancelling recovery preserves pixels/text but does not let standard
Copy salvage the failed native characters.

Confidence: high, direct dispatch path; native clipboard behavior was not run.
Remedy: in the read-only *unadmitted* state only, allow a selected native-buffer copy
without engine mutations or discard consent, or provide an explicit copy-pending-text
command. Keep Cut/Save/history and ordinary canonical copy semantics unchanged.
Finding sent to implementation owner; correction inspection pending.

Windows owned MessageBox pumps messages but lacked Mac's nested-prompt and original
installation checks in this checkpoint. This is a conditional modal/reentrancy
concern, not an established extra defect: qualify queued asynchronous document
replacement or add an original failure-epoch witness before applying consent.

Windows salvage-Copy correction: reinspection of the current main shell establishes
that this part of the finding is stale for the final working tree. WM_COPY now
returns DefSubclassProc specifically for the NativeSource unadmitted state; the menu
Copy dispatch sends that same message. This permits native selection copying without
canonical mutation or discard consent. Mac's analogous path remained open at this
checkpoint. The initial Windows finding is not a remaining correction request.

Final recovery refinement checkpoint: Mac now handles salvage Copy through an
NSTextView superclass copy call only for the exact read-only, unadmitted source and
source first responder. Both delegate copy and main-menu copy use that helper; native
copy failure does not fall through to canonical copy. This closes the Mac salvage
finding's inspected path, without asserting clipboard runtime acceptance. Windows
added a nested-consent guard plus original failure epoch, state, installation and
owner checks after MessageBox; the prior conditional modal concern is addressed.

A final concrete P2 appeared in Windows menu focus routing: the new unadmitted-source
Copy branch precedes existing CSV grid-focused Copy. If the grid has focus after
source failure, Ctrl+C/Edit Copy copies the native source selection instead of the
grid selection. Preserve grid routing first, or require actual source focus for the
salvage branch. The direct WM_COPY path on the source control is not affected.
Finding communicated immediately; final correction check pending.

## Frozen recovery/trace correctness checkpoint

The Windows menu now retains grid Copy priority and requires actual GetFocus()==source
for its unadmitted salvage branch. The helper uses the pointer-returning user32
GetFocus ABI. This closes the final focus-routing regression. Rechecked original
failure epoch and modal guard, Mac source-only superclass Copy, canonical history
availability, controller fresh-nonce recovery, fixed telemetry names and strict
reader allowlist. No remaining substantive issue was established in these scoped
deltas. This conclusion is code correctness review, not performance/default promotion
or native runtime acceptance. The reviewer ran no tests, builds, clipboard operations,
GetFocus/native calls, or GUI. Owner-reported build/test results are not recounted as
independently executed validation here.

The following SHA-256 values identify the frozen-source checkpoint:

| File | SHA-256 |
| --- | --- |
| `src/Mote.Native/NativeEditorController.Source.cs` | `6E7BE9AE8BFFE47168B2207315AFFE698E408BA27A85CCC7C3EB2B5E5F5B8BF5` |
| `src/Mote.Native/NativeSourceContracts.cs` | `C54888397999097C98C61ED228F1C1AD990DDF0525D1AD5373D3F44CC9FE4657` |
| `src/Mote.Native/Windows/WindowsEditorShell.Source.cs` | `55926B0396C7FA4D7B7A12A32CDEDD2F4D77BCD5BB93DEE252E0E48C777CF88E` |
| `src/Mote.Native/Windows/WindowsEditorShell.cs` | `B1EFC44A3FB36BC5A78FA6980C2848F344A18E9D57942D7270B69088380AE98A` |
| `src/Mote.Native/Mac/MacEditorShell.Source.cs` | `75E73B4BFE4B3FB1F3EF2B080E2BBCA023CF9930B37B38B37591138B4F9010AC` |
| `src/Mote.Native/Mac/MacEditorShell.cs` | `B6A79504E5DF5E8C3662575A46FB7DFF29DDE59CEC0D7155E376860E5139C851` |
| `src/Mote.Native/Mac/MacProductSourceModel.cs` | `BD91E487791DC6D98BC839DF523F5A895842DD81478085FCB6A9C115C4CB2410` |
| `src/Mote.Telemetry/TelemetryTypes.cs` | `959B5C927C095F5CF26F97F40CFD8D0A20E7F745CC6C0A91888E6E1DDB8210EE` |
| `src/Mote.Telemetry/MoteTelemetry.cs` | `52B46B62930614EE9CFC9B189CBBF422CF8A662639FE298E83BFC05D0EB2FA8D` |
| `benchmarks/NativeAcceptance/acceptance.py` | `A4DA27792FCB456EF3B47C806BCAA1BB669FF60EF21CBC4FA08404C73173ED1B` |
