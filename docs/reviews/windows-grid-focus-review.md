# Windows Grid focus: independent causal review

Date: 2026-10-01. Review source checkpoint: `21f4c222bfe2e04bf4ea400331779c1f0134f7e7`.
Ownership: review document only; no production or external acceptance oracle edits.

## Evidence and limits

Inspected the actual failed x64 report in
`.cache/ci-36837499493-flow-gate/windows-grid-accessibility-ci-win-x64/report.json`,
the external client, shell traversal and analysis installation, Grid installation,
selection and focus owner, provider options, and the retained pane regression.
No GUI, global input, native probe or native tests were run for this review.

The original report remains `product-fail`, with seven recorded errors. That
classification is not erased by identifying a faulty premise in individual
assertions. This review does not yet establish the transition that moved initial
focus to the Table, or certify foreground/assistive-technology behavior.

## Necessary verification corrections

### 1. F6 oracle assumes a source identity it never establishes

Location: `tests/WindowsGridExternalProbe/Program.cs`, the `sourceFocus = info.Focus`
assignment and `cycle` construction (approximately lines 118-145).

The client samples owner-thread focus **after** selection operations and all
three UIA tree walks, then calls that arbitrary HWND `sourceFocus`. In the actual
report, the expected first and last handles are both `1048752` (Table). The
observed sequence is rows, columns, Go-to, actual source, Table. This is exactly
the production successor cycle from Table, not evidence of an off-by-one
`CyclePaneFocus` implementation. The production algorithm reads actual current
focus and moves to its next admitted pane.

Impact: four traversal errors and two scroller publication errors conflate an
unverified starting pane with traversal and property coherence. Each scroller
assertion queries the expected, unfocused pane while the actual semantic focus
correctly names the next observed pane. `PhysicalHwnd` inside the focus facts is
also the **expected target**, whereas `SyntheticF6Observed` is the real readback;
do not describe the former as measured physical focus.

Confidence: high, directly supported by report values and executable client code.
Correction requires independent identification and observation of the starting
source HWND, not a forced focus reset to make the existing oracle pass. Determine
whether passive initial publication steals source focus first; then derive
traversal expectations from the verified start while keeping the intended source
focus invariant independently asserted.

### 2. External cell focus refusal is an unmeasured thread-affinity premise

Location: the external client's `distant.SetFocus()` assertion and constant
`ExternalCellFocus` report; `WindowsCsvGrid.Focus`; provider `GetProviderOptions`.

The client requires `InvalidOperationException`, but the call returned normally.
Its text claiming an off-owner callback is a fixed literal, not a native or
managed callback-thread measurement. Grid advertises server-side provider,
ProviderOwnsSetFocus and UseComThreading; native entry/owner STA work has evolved.
The adapter explicitly refuses off-owner calls before `SetFocus`, but accepts
owner-thread, current, ready, composition-safe cell focus and verifies native
readback and installation identity. A safe admitted owner callback is compatible
with the implementation's contract.

Confidence: high that the asserted premise is unverified; unknown actual
callback affinity/result details. Preserve off-owner refusal. Verify a real
owner-thread callback/readback or an actual off-owner refusal rather than
changing production to refuse all external focus solely to satisfy this oracle.
STA initialization alone does not prove generated COM callback affinity.

## Product hypotheses, not established defects

| Transition | Source evidence | Discriminating observation |
| --- | --- | --- |
| Grid installation | `SetNativeFocus(false)` changes LVIS_FOCUSED item state; no direct HWND `SetFocus` | Source readback before/after Install, including reentrant native notifications |
| Navigation/rebase | Axis updates, item count, EnsureVisible and optional row-state update | Separate readback after Navigation and Resize |
| Grid visibility | `Show(true)` calls `ShowWindow(...,5)` on native children | Before/after Show and each child, with source initially focused and no later repair |
| Shell preview visibility | Analysis calls Grid.Show followed by hiding the separate preview | Separate before/after preview hide; do not fold into Show result |
| External UIA selection/read | Select/Add happen before the initial focus sample | Readback around each operation and first Table/provider acquisition |

Microsoft documents SW_SHOW (5) as activating and SW_SHOWNA (8) as nonactivating;
this is a concrete reason to investigate passive presentation calls, but does
**not**, by itself, prove what happened to these particular child HWNDs.
LVIS_FOCUSED is an item focus-rectangle state; it must not be confused with
measured owner-thread HWND keyboard focus. Grid Select intent handling in the
controller only cancels pending Copy and returns, so no explicit focus transfer
was found on that path. Native default processing and COM/UIA framework calls
remain possible until runtime transitions discriminate them.

The existing `WindowsGridPaneFocusTests` sets source focus **after** installation,
navigation, resize and Show. It proves traversal after a repaired start, not
preservation of initial source focus through those steps. An additional owned,
offscreen transition regression should set source before publication and assert
after each operation without interposed resets. Retain existing traversal and
composition checks; no global input or foreground activation is necessary for
that narrow discriminator.

## Primary sources

- [ShowWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow): activating and nonactivating show contracts; return value is previous visibility, not focus success.
- [ListView_SetItemState](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-listview_setitemstate): LVIS_FOCUSED is per-item state, distinct from LVIS_SELECTED and control focus.
- [ProviderOptions](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.provider.provideroptions): ProviderOwnsSetFocus and UseComThreading behavior, including owner-STA callback contract.
- [ISelectionItemProvider.Select](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-iselectionitemprovider-select): selection operation, not evidence of callback-thread identity or starting keyboard focus.

No new academic claim is needed to settle these concrete native contracts.
The repository's existing CSV accessibility research remains motivation, not a
substitute for callback affinity and actual focus-transition evidence.

## Scoped passive-visibility change review

Reviewed the pending diff after root approved the passive-presentation mechanism:
`WindowsCsvGrid.Show` now uses named `WindowsGridInterop.ShowWithoutActivation`
(SW_SHOWNA, 8) on all six visible child branches; hidden branches remain 0.
There is no production focus reset, retry, callback marshalling change, new ABI,
or changed COM focus admission. Explicit mouse/cell Focus and shell F6 still own
their existing `SetFocus` calls, and shell initial source focus is unchanged.
No substantive defect found in this narrowly scoped production delta. This is a
documented mechanism correction, **not a demonstrated fix for the historical
external-client failure**.

The pending `WindowsGridPaneFocusTests` extension establishes source focus before
Grid construction/publication, observes constructor, Install, Navigation,
Resize, Show and repeated installations without intervening resets, and tests
direct selection mutations and provider reads for noninterference. Bidirectional
traversal also checks cached Table/cell and both scroller HasKeyboardFocus facts
against the exact native pane. The original repaired-start traversal test remains;
new checks do not silently replace it. Source text readback remains asserted.

These source-level tests cover the owned adapter/cache paths, not a cross-process
UIA framework invoking Select or SetFocus. They do not install the shell's full
analysis/preview visibility sequence or certify foreign/foreground focus. Native
execution is pending hosted validation; retain the original unchanged external
probe and its truthful failure result. No test-pass or runtime fix claim is made
by this review.

### Separate publication/reentrancy boundary

`PublishAccessibility` samples Table focus before setting the native status text,
and samples scroller focus before setting their native text. Native calls can
be synchronous reentrancy boundaries. A hypothetical nested focus/installation
change could leave an outer sample stale; the final bridge publication guards
installation and frame reference, but the outer scroller writes do not have an
equivalent final guard. This is an identifiable structural concern, not evidence
that the retained failure traversed that path.

The actual owned status/scroller WM_SETTEXT subclass routes inspected here call
default processing; they do not explicitly transfer focus, publish a new
installation or dispatch a controller selection. Managed group/scroller Publish
methods are callback-free volatile writes. No executable triggering path for a
focus change inside these particular label calls has been demonstrated. Therefore
this concern does **not** block the passive Show delta and does not justify an
unrelated production reentrancy redesign now. If a transition test points here,
add a reproducible owned synchronous callback discriminator and then repair the
publication transaction coherently rather than patching one HasKeyboardFocus
property or forcing focus.

## Proposed Focus provenance contract review after CI 36858899063

Inspected the separately owned final-source CI audit and the proposed construction
in `docs/csv-grid-accessibility-implementation.md`. Both actual owned-HWND tests
passed on each RID; the unchanged published-AOT external client still reports
the original seven errors on each. This is material negative evidence against
calling passive visibility a demonstrated historical cure. No completed native
validation was rerun for this review.

The opt-in provider-boundary construction is appropriate, with the following
implementation constraints. These are review requirements, not newly demonstrated
production defects or permission to change the legacy oracle.

### Exact boundary and minimal types

1. Define whether `received` means every `WindowsGridUiaNode.SetFocus` entry,
   including stale/header early returns, or only the sole admitted attempt to
   call the adapter action. The proposal currently says actual provider invocation
   and sole adapter action. Choose one precise contract and test early returns;
   do not silently omit refused requests while describing complete provider
   coverage. Targets outside table/cell must not be mislabeled cell.
2. Keep a Windows-only observation interface returning a value-type sample:
   native callback-thread relation, managed admission relation, and physical
   pane. Use closed enums for each independently. Native owner-thread equality
   does not substitute for the existing managed owner check. Unknown is not
   non_owner, false or refusal. A sample capture failure should produce unknown/
   unavailable observations without changing the action's return.
3. Capture the exact eight-valued `GridAccessibilityResult` from the original
   adapter invocation before the existing HRESULT translation. Unsupported,
   NotReady and CompositionBlocked all map to the same HRESULT; Applied and
   NoChange both map to success. A translated HRESULT alone cannot reconstruct
   the admission result. If the original action throws, record a fixed fault
   terminal best-effort and preserve the original exception/COM conversion;
   never swallow it into a guessed success or new unsupported result.
4. A typed receipt should own its original sink and persisted receipt span; a
   fresh terminal span points to that anchor and completes at most once. Do not
   invent ambient Activity parents or fall back to the current newer session.
   Request-local state must contain no HWND, event or content retention. No
   cross-process client ID is supplied by the original UIA contract.

### Source identity and disabled path

The shell's actual source handle is stronger evidence than the external client's
arbitrary focus sample. `EnsureGrid` currently has access to legacy `_editor` or
Canvas `_canvasIsland.InputHandle`; the Canvas input is constructed once and
cleared at native destruction, not replaced by ordinary binding updates.
Therefore passing the actual identity at Grid construction is reasonable, but
capture must validate its current native liveness, PID and owner-thread relation
when enabled. Zero/missing Canvas input must remain unavailable; do not fall back
to a legacy source HWND solely to produce a source classification. A retained,
destroyed/reused numeric HWND is not identity evidence. No foreign text, class,
window title or UIA metadata is required to classify a focus handle outside the
known owned panes.

Disabled observation must branch before constructing observation payloads,
querying native threads/focus, acquiring an observation interface or creating a
capturing delegate. A wrapper lambda allocating on every default-off request
would violate the declared contract even if telemetry drops it internally. Keep
the original direct action path and exactly-once invocation. Enabled capture and
serialization faults must be isolated from original action failures; do not
catch both under one broad catch that masks which operation failed. No user-facing
error UI, focus setter or retry belongs to the observation path.

### Attribution and privacy

Server receipt/terminal edges establish one actual method-call lifetime. Client
begin/end labels establish sequential client observations. Timestamp overlap,
one visible successful COM return, or even one observed server pair does not
prove a client-to-server edge when the API carries no explicit correlation token
and the transport may drop unrelated requests. Keep both graphs independent and
report temporal/sequential compatibility, not certified correlation. Multiple
pairs, drops, missing terminal, unavailable identity or censored shutdown require
incomplete/ambiguous attribution, never proof of absence.

Operation-specific readers must reject unknown enum integers/strings, unexpected
fields, invalid receipt/terminal parent/status combinations and these attributes
on unrelated operations. Append event identifiers without renumbering existing
schema-1 operations. Serialize only the closed pane/thread/target/result facts;
do not serialize native handles, thread IDs, PID, coordinates, content or exception
messages. Explicitly freeze how unavailable/faulted observations map to terminal
status: observation uncertainty is not by itself proof that the focus action
failed. Portable serializer/action-transparency/default-off tests support these
contracts but do not prove native COM delivery.

### Review verdict and next evidence

No conceptual blocker to the proposed opt-in design. Resolve the exact boundary,
independent thread relations, identity validation and terminal status mapping
before implementation. The independently sequenced client must retain the old
client/source pins and negative result, avoid all repair/reset/resend behavior,
and capture native child identity before UIA inspection. One actual published-AOT
execution per RID with raw report/traces and numeric exits is the next required
evidence; no local GUI execution or production implementation was performed by
this review.

## Addendum: scoped dual-architecture hosted focus step

Independent workflow-only review on 2026-10-01 examined the additive 26-line
`.github/workflows/ci.yml` change for `WindowsGridPaneFocusTests`, with passive
Show implementation `4cc9c16` and retained source-before-publication/F6 fixtures
`3ec540a` as dependencies. **No substantive issue found in this scoped diff.**
Native source and the external AOT probe were not re-reviewed or edited; no
local HWND, GUI, input or full-suite experiment was executed for this review.

The existing Native AOT matrix maps win-x64 to `windows-latest` and win-arm64
to `windows-11-arm`. The Windows-only step derives x64/arm64 from that fixed RID,
passes it explicitly through `dotnet test --arch`, and permits the required
architecture-specific restore/build rather than reusing an x64-only `--no-build`
output. The test project uses VSTest/xUnit; [Microsoft's VSTest CLI contract](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-vstest)
documents `--arch` as selecting the architecture-specific RID. The global
`-p:PublishAot=false` override is appropriate for this managed user32 test seam
and its Native project reference; it does not republish or modify the already
inventoried `$rid/publish` payload.

The step is blocking, has one invocation and a five-minute bound, restricts
discovery to the existing Windows focus fixture class, and explicitly rejects
a nonzero test exit. TRX results are written under the repository's per-RID
`.cache/ci-inventory` area and uploaded with an `always()` Windows-only condition.
Missing results are warned, not fabricated as a pass; inspect actual test counts
and outcomes before claiming execution. The original publish-inventory JSON,
payload paths, strict single-binary/import gates, external accessibility probe,
existing safety/hash pins and all unrelated step contracts remain unchanged.

Hosted execution of the managed owned-offscreen HWND fixtures on both Windows
architectures remains pending. A future pass establishes the tested native seam,
not a published Native AOT/cross-process UIA certificate, a physical F6 event,
foreground/assistive-technology behavior, or a fix for the historical external
client's unverified starting-focus premise. Root's planned YAML semantic and
PowerShell AST checks are separate integration checks, not runtime evidence.
