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
