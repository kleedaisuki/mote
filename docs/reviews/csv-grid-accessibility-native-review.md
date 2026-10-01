# CSV Grid native accessibility implementation review

Date: 2026-10-01. Status: final targeted internal review of the opt-in experimental implementation. Earlier findings and their dispositions are retained below. This is not external UIA/AX or reader acceptance.

## Scope and evidence

Inspected `docs/csv-grid-accessibility-contract.md`, prior `docs/reviews/csv-grid-accessibility-contract-review.md`, shared frame/action declarations, WindowsGridUiaAbi/Provider, WindowsCsvGrid integration, MacCsvGrid.Accessibility and MacCsvGrid integration. No production or test files modified. External target workers own execution, so this review does not repeat their tests. Source command omission is correct: neither Grid exposes Invoke/press/Copy/Replace against the unacknowledged void seam. Local counts, absolute labels, bounded descriptors, SAFEARRAY/BSTR ownership transfer and old-projection-free wrapper fields are directionally consistent.

## Necessary corrections found in initial implementation

### P1: Windows admits obsolete actions while native installation is incomplete

Locations: WindowsCsvGrid.cs `Install`, `SetNavigation`, `MutateSelection`, `Focus`.

`Install` increments installation, clears ready identity and sets `_installing=true`, but keeps the old accessible frame/bridge live while sending synchronous native layout messages. An old-window request reentering during those messages still matches `_accessibleFrame.Id`. The actions do not check `_installing` or an installation revision. Selection can write old coordinates; `PublishAccessibility` returns early under `_installing`, yet mutation returns Applied. Focus similarly may claim success without a current coherent frame. `SetNavigation` also changes placement before retiring the published tree. Native transition must retire the old epoch before callbacks, block action admission during installation, and keep nested-finally state conditional on the winning installation. Confidence: high, executable control-flow inspection; no injected native reentry executed by this reviewer.

### P1: Windows reads/cache are not an atomic installation boundary

Locations: WindowsGridUiaProvider.cs `Frame`, `Node`, `Publish`, `Clear`; node `GetItem`, `Navigate`, `GetSelection`, header/parent/root methods.

`Frame(id)` reads `_frame` twice; replacement between comparison and returned value can give a retained old node a new installation. Methods capture a frame but then `Node` fetches the current root/map and stamps newly created nodes with `_frame.Id`, not the captured ID. Replacement between capture and lookup returns another window's cell; clearing can dereference null. Concurrent lookups/publication mutate the same ordinary Dictionary. These become concrete under external non-owner-thread callbacks or reentrant publication. Use one atomically captured installation state; every node/cache lookup must bind to that captured epoch and a safely synchronized bounded cache, with retirement rejecting stale completion. Geometry caching alone does not repair this identity/cache defect. Confidence: high conditional on concurrency/reentry; callback-thread realization is an external test gate.

### P2: Clearing Windows semantic selection leaves the rectangle painted

Location: WindowsCsvGrid.cs `Draw` (initial lines 496–499), `_selectionCleared` mutation/publication.

Removing the final cell sets `_selectionCleared=true` and publishes Selection=null, but leaves anchor/active coordinates, and Draw ignores the flag. UIA reports no selection while the same cell/rectangle remains visually selected. Membership must consume the same cleared/full retained selection invariant as the frame. Confidence: high; direct branch comparison.

### P2: Mac explicit focus cannot target Table and survives native cell movement

Locations: MacCsvGrid.Accessibility.cs `Focus`, `PublishAccessibility`, `AccessibilityFocusedElement`; MacCsvGrid.cs keyboard/pointer/selection callbacks.

`Focus(id,null)` clears `_accessibilityFocusCell`, but reads fall back to Selection.Active, so the table setter claims focus while selected cell remains the semantic target. After Focus(cell A), native selection movement to B does not clear the override, so A remains focused. Use a distinct table-only sentinel and reset explicit cell focus when real native cell navigation takes ownership. Normal first-responder enter/exit and active-cell movement must publish focus facts/events, not only the accessibility setter. Confidence: high.

### P2: Event publication is incomplete and can deliver superseded facts

Locations: WindowsGridUiaBridge.Publish; MacCsvGrid.PublishAccessibility/Focus.

Windows emits only Selection_Invalidated (20013) for every selection change, never required SelectionItem single-select/add/remove events, and does not raise relevant property/layout changes on geometry/status changes. Publication raises native events then uses the originally passed frame with mutable current `_root`/Node; reentrant replacement can produce a focus event for the wrong installation. Mac posts AXSelectedCellsChanged for every non-new publication, including unchanged selection/focus/geometry, and normal focus transitions have no dedicated event comparison. Diff immutable previous/current facts, publish before events, check captured installation/selection revision after each reentrant event, and stop a superseded outer delivery. Use actual SelectionItem events for bounded small changes and invalidation only when warranted. Confidence: high for missing events, conditional/high for reentrant mismatch.

## External gates and sources

- Microsoft [ProviderOptions](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-provideroptions): 0x10 is ProviderOwnsSetFocus, 0x20 is UseComThreading. Initial Grid returns 2|16, not UseComThreading. Adding a flag alone does not prove StrategyBasedComWrappers apartment affinity. Workers must record callback threads under actual external MTA clients, and prove synchronous Select/SetFocus outcomes. Owner-thread action rejection must not be mistaken for accepted SelectionItem support.
- Microsoft [SelectionItem events](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.selectionitempatternidentifiers.elementselectedevent): single-item selection requires ElementSelected; add/remove transitions use their corresponding item events. [Event identifiers](https://learn.microsoft.com/en-us/previous-versions/dd757490(v=vs.85)) distinguishes broad invalidation from per-item changes.
- Existing contract contains production Chromium cached-frame/publication precedent and CHI 2024/2026 reader-task research. Their useful implication here is that an internally consistent label is not proof of reader orientation: external point/parent/focus/selection/readback and attended speech remain distinct gates. No additional research claim or desktop pass is inferred.

Mac aggregate-return selectors, screen conversion/clipping, stale native retain/release and AppKit callback-thread assumptions require osx-x64/osx-arm64 execution. Windows host/default ListView/header proxy merging requires all three UIA views; cached geometry and old-wrapper retention require external tests. No such execution was performed by this reviewer. Neither platform's composition gate is proved by synthetic calls alone.

## Follow-up during implementation

Windows bridge now uses one Volatile frame read, locks the node map and requires the requested epoch in Node. This resolves the original cross-epoch rebinding/data-race mechanism; a superseded lookup creates an immediately unavailable old-key wrapper rather than a new-window cell. Cached geometry removes native geometry calls from external provider reads. External threading/action support remains unverified.

The geometry capture introduced a separate stale-outer-publication path: Publish calls native `_bounds` repeatedly before committing the passed frame. If a native geometry call reenters installation and a nested publisher wins, the outer unconditional commit can overwrite that winner. Validate the adapter installation plus selection/publication revision after geometry capture and before committing. Checking only epoch after event callbacks also misses same-epoch selection/focus supersession; compare exact frame/revision. This follow-up was sent to root immediately.

SDK clarification: [IRawElementProviderFragment.GetRuntimeId](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-irawelementproviderfragment-getruntimeid) requires an HWND-hosted top-level element to return NULL; append-runtime IDs belong to descendants. The initial table root returned an append array like its cells. Parent/group refactoring must keep the root distinction explicit and externally check stable relationships.

## Focus versus keyboard-active coordinate follow-up

P1 / high confidence: Focus(cell A) can make keyboard Replace affect cell B. Windows Focus stores only `_accessibleFocusedCell`; Enter/F2 dispatches Emit using `_row/_column` (B), and arrow Move begins from B before clearing the override. Mac Focus stores only its accessibility override; Return/Command-Return CaptureIntent reads native selectedRow/_column (B), and arrows likewise originate B. Native table focus readback is genuine, so this is not fabricated HWND/first-responder success; the defect is semantic keyboard focus identifying a cell that is not the target of the next focused-keyboard operation. Trigger: establish selection B, externally focus distinct admitted A, then Replace without a selection request. Impact: user can edit the wrong CSV cell.

Selection/focus separation does not imply two unrelated keyboard-active cells. Preserve the full selection for rectangle Copy. Route explicit focused-cell Reveal/Replace and the first arrow origin through the single semantic focus coordinate, then let keyboard selection operations deliberately take ownership. Do not repair it by silently replacing selection during Focus. Table-only focus policy may retain established selection-based keyboard behavior, but must be explicit. This was sent to coordinator before correction.

Recent fixes inspected: Windows retires `_accessibleFrame`/bridge before Install and SetNavigation native callbacks; actions reject `_installing`; nested-finally guards now test installation winner. Windows geometry publication receives installation/exact-frame validation before commit and stops event delivery after exact-frame supersession. Windows action completion additionally checks current selection/focus. These address the original stale-admission and geometry stale-outer-write paths by inspection; runtime reentry execution remains the target worker's gate. Mac now distinguishes table-only focus and publishes selection events only for actual change, with coherent frame checks and dedicated actual-focus events. Keyboard routing discrepancy above remains a separate necessary correction.

Follow-up clarification: latest Mac AccessibilityFocusedElement now consumes frame.FocusedCell directly, so the sparse Pending focus readback concern is resolved. Mac Focus still needs current semantic/native focus readback after Publish callbacks, rather than ID-only completion.

Windows explicit focus overrides must retire with document identity. Current Install/SetNavigation clears frame/bridge but leaves `_accessibleFocusedCell` and table-only override. A new document whose window contains the old coordinate can inherit that old focus while its actual selection/keyboard endpoints reset. This matters especially when single-cell keyboard actions are repaired to use semantic focus.

Exact-empty adapter edge: Windows Install for a new exact-empty projection resets `_selectionCleared=false` and anchors to row/column zero. The shared constructor removes invalid focused cells but preserves selection, so status describes nonexistent Row 1/Column 1 even with zero Table counts. Clear selection for proved exact-empty; do not use this rule to discard a legitimate retained off-window rectangle in a nonempty document. Coordinator was notified to avoid duplicate model/platform test work.

## Targeted routing recheck

Mac Reveal/Replace now routes through coherent frame.FocusedCell with origin/readiness validation. First physical arrow prepares native navigation from the explicitly focused cell while preserving the retained rectangle until the keyboard operation takes ownership. Focus completion now checks semantic focus and native firstResponder after publication. Original wrong-cell command and focus-readback concerns are resolved by inspection.

Windows Reveal/Replace and arrow origins now use explicit semantic focus, but blanket Dispatch reset after any handled Input also resets focus on Copy/context-menu operations. Focus A + selection B + Ctrl+C should copy B without silently moving focus to B; reset only on actual native selection/navigation operations (Select already does this). Document retirement must clear the old focus override.

P2 / high confidence: Windows selection-Copy after Clear still dispatches a CopyRows/CopyTsv/CopyValue intent from retained hidden endpoints because Copy ignores `_selectionCleared`. A cleared rectangle must not remain clipboard authority. Refuse selection-Copy without selection; any intentionally separate single-cell Copy source/value action may use the genuinely focused current ready cell, but must not revive the cleared rectangle. Reveal/Replace may remain valid with focused cell and no selection; do not disable all single-cell actions as a blanket repair. Parent's targeted test expectation correction was supported; this reviewer did not rerun tests.

Exact-empty normalization is now implemented in common Create with authoritative exact-zero extent clearing shape, selection and focused coordinate; coordinator reports dedicated regression passing. The original externally advertised exact-empty defect is resolved. Internal adapter cleared-state cleanup is still recommended so native commands cannot use an empty document's phantom endpoints.

## New Windows synchronous dispatch boundary review

Platform worker observed real external MTA Select returning invalid-operation under owner-thread rejection. This is valuable negative evidence, not a successful action gate. New code therefore introduces a 500 ms SendMessageTimeout request token in a concurrent managed dictionary, with serial-only native messages and retirement cancellation. This avoids dangling GCHandle/native pointers, but the first implementation has two concrete necessary corrections.

### P1: timeout cancellation is checked only before entering the action

AdmitAccessibilityRequest checks Expired once then calls public MutateSelection/Focus without passing the token. A composition callback can block/reenter past the deadline. Sender returns Unavailable and cancels/removes its token; receiver resumes and still mutates fields/focus. Timeout during later native publication/events similarly returns failure after a committed selection. Native SetFocus may itself finish after sender timeout. Require token-aware atomic commit versus cancellation (state machine/CAS or a narrow non-callback commit critical section), and an actual coherent completion receipt. A last-minute plain boolean check alone leaves a TOCTOU race. Do not claim failed focus remained unchanged after an in-flight native transfer. Deterministic test: block composition callback after initial Admit, let external caller time out, release callback, prove no late selection/focus mutation. Coordinator owns target tests; reviewer did not run them.

### P1: request exceptions fall through to success-shaped native result

AccessibilityRequestMessage is handled inside generic Dispatch try/catch. If Admit/action throws, catch reports then DefSubclassProc handles the unrecognized WM_APP and normally returns 0. Sender casts that zero to GridAccessibilityResult.Applied. Give request handling a separate exception boundary returning an explicit failure result; never use an unhandled/default LRESULT as a successful receipt. A throwing composition callback is a deterministic discriminator.

Other targeted repairs now inspected: Windows Install resets explicit focus overrides; Dispatch no longer blanket-resets override after Copy; Copy checks cleared selection; root runtime ID is NULL. Mac post-publication semantic/native readback and selection-cancellation notification match the approved controller Select-only cancellation seam. Original focused routing findings are closed by inspection; new timeout boundary remains blocking until repaired and executed.

## Stable Mac closure and Windows boundary gate

Mac targeted follow-up is closed by inspection: table-only sentinel, shared pending-aware focused coordinate readback, native keyboard takeover, focused Reveal/Replace routing, post-event focus readback and selection-only cancellation notification were inspected. This does not certify native AX ABI or attended VoiceOver.

Coordinator reports controlled STA experiment with UseComThreading plus STA initialization still failed to realize synchronous external actions; preserve that negative evidence. The temporary no-callers timeout code was an experiment snapshot, not the final boundary. A final callback-free selection commit/receipt design is being implemented and will receive targeted review when owner freezes it.

Focus safety recommendation: an in-flight reentrant native SetFocus cannot be cancelled by a timeout token. The simplest honest temporary boundary is to reject external off-owner focus before native transfer, record the external action gate as blocked/product-fail and keep the experimental feature gated. Empirical latency alone cannot prove absence of the late-focus race. Do not add a success-shaped deferred focus receipt or claim failed focus left native focus untouched.


## Final disposition (opt-in experimental scope only)

No unresolved critical correctness/security defect was found in the final inspected native selection transaction, epoch/lifetime handling and focused-keyboard routing fixes. Windows and Mac native registration remain opt-in, not default production acceptance. This distinction is essential: Windows off-owner external Focus deliberately returns Unsupported before any native call. The external STA experiment failed; that negative result must remain in the verification record.

Final Windows manual selection boundary has one admitted request at a time, a serial-only native token, composition hook before current frame/full-selection capture, and a private gate around callback-free adapter/frame/provider commit plus Completed receipt. Timeout takes the same gate and either cancels before commit or observes genuinely coherent completed facts; it cannot resurrect a removed token. Request exceptions return Unavailable explicitly rather than default Applied-shaped zero. Source Select-only cancellation is now delivered immediately after coherent commit and before native status labels/events, preventing old Copy completion from overtaking cancellation through notification reentry. Outside-gate callbacks recheck current selection/identity before later publication. Off-owner focus never enters this timeout path.

Coordinator reports owner execution of 21/21 focused cases including new timeout/concurrent-request/throw/no-late-effect test (512 ms), focused A versus selected B command/navigation regression and actual Draw clear regression. The exact timeout test had been completed before reviewer execution was authorized; per repository instructions it was not rerun. Reviewer independently inspected its discrimination: block after native request admission, allow caller timeout, release and synchronize with next no-change request; verify retained selection, throwing admission failure and off-owner Focus refusal. This is owner test evidence plus independent code/test review, not a second independent runtime execution.

### Remaining release gates (not disguised as implementation success)

1. Windows external SetFocus remains unsupported on actual COM callback threads. Native F6/pane focus and in-process owner-thread Focus do not prove external cell SetFocus acceptance.
2. Windows event completeness still requires SelectionItem selected/added/removed events for appropriate bounded transitions and relevant property/layout changes; broad Selection_Invalidated alone is not full Table event acceptance. Observer and reader confirmation are required.
3. Stable HWND-hosted Table root, epoch-bound children, native header override and DescribedBy status were inspected. Verify final all-three-view tree merging, point/parent/focus relations, distant window reacquisition, stale retention and detach externally against final binaries.
4. Mac ABI/AX aggregate returns, actual callback threads, screen geometry/multi-display clipping, retained native nodes and reader behavior require target evidence on both architectures; portable geometry-size checks are not native ABI execution.
5. Real IME composition, source-provider isolation, attended Narrator/VoiceOver speech/orientation and worst-case bounded memory/performance remain separate gates. Omitted source Invoke/press/Replace/Copy remain omitted until the controller acknowledgment protocol is implemented and externally validated.

Only this review document was modified by reviewer. No production/test edits, staging, commits or OS trust/reader settings were made.

## Approved read-only Windows scroller addendum

Scope: only new WindowsGridUiaScroller, its IGridRangeValueProviderAbi and the narrowed scroller publication/focus hooks. Parent explicitly approved read-only range facts after native host RangeValue fallback failed; writable RangeValue/native-reader action acceptance is not part of this addendum.

The new scope is coherent: cached immutable State publishes admitted navigation plus actual owner-thread focus together; external reads neither query native focus nor dispatch navigation/source commands. Value is First, minimum zero, maximum Last = Count - min(Count,Page), LargeChange=min(Page,Count), SmallChange=1. RangeValue is omitted for Unavailable; exact-empty zero range has no invented Row 1; prefix/known-column labels and Help explicitly state unknown whole-file totals and distinguish last legal origin from last data ordinal. IsReadOnly=true and SetValue unconditionally returns invalid-operation, including NaN, so this is not a fabricated writable host fallback.

ABI independently checked against installed Windows SDK 10.0.26100.0 `um/UIAutomationCore.h` lines 3009–3032: GUID 36dc7aef-33e6-4691-afe1-2be7274b3d33, separate IUnknown-derived vtable, SetValue/Value/IsReadOnly/Maximum/Minimum/LargeChange/SmallChange order and double/BOOL types match. Microsoft [IRangeValueProvider documentation](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nn-uiautomationcore-irangevalueprovider) confirms the read-only and range-members surface. Cached pure reads need no apartment-bound mutation promise; native keyboard focus remains owner-thread behavior.

One narrow P2 lifetime hole was sent to coordinator: document-null PublishAccessibility early return clears Table but initially left scroller State at the previous document's extent. Retained hidden scroller HWND providers can therefore expose an obsolete range after document/navigation authority is removed but before destruction. Clear both State publications to `(null,false)` on the document-null path; Detach already clears them on final destruction. This correction does not require a new range writer or protocol.

New targeted COM tests and final AOT are owned by the platform worker; no broad tests were run or rerun by reviewer. Overall opt-in/default-promotion and reader gates are unchanged.

Read-only scroller addendum closure: latest document-null early return now publishes `(null,false)` to both scrollers before returning. The obsolete-range lifetime hole is resolved by targeted inspection. No remaining substantive correctness/security issue was found in this approved read-only scope; no writable action or reader pass is inferred. Inspected scroller SHA-256: E2CFC94C40AB66879F4739C696958D0989FD08F0EE54199796607BE77B98AFD0. Review-document diff check passed.

## Final indirect-owner retention guard

Targeted scope: nullable Windows bridge action/geometry-owner fields, final Detach and null-safe accessors only. A retained stale COM child previously kept bridge -> readonly actions/instance bounds delegate -> disposed Grid -> shell/controller closures alive even after clearing the projection. Merely observing Frame=null did not sever that indirect owner graph.

Latest guard makes `_actions` and `_bounds` nullable; final Detach clears frame/geometry/node map/root, then nulls both owner references before native registration teardown. Live Clear intentionally preserves these references for the still-attached reusable Grid. Mutate/Focus null fallback maps to UIA_E_ELEMENTNOTAVAILABLE; geometry publication captures the nullable bounds once and refuses publication when absent. Cached Bounds after cleared frame returns default. The readonly status provider holds only HWND/status facts, not the disposed adapter/controller closure graph. No remaining issue was found in this final retention fix by inspection.

The new weak-reference test keeps both a managed stale node and an actual owned COM pointer alive, while a separate non-inlined setup scope drops the action/instance-delegate owner and forces GC. It asserts the owner is collectable and the held node returns unavailable. This is a meaningful owner-retention discriminator, not a 100 MiB/RSS measurement or complete closed-document memory benchmark. Worker owns execution; reviewer did not repeat tests.

The final Pending-cell Focus guard is present before any native SetFocus call. Coordinator reports two targeted cases passed; preceding AOT executable/hash evidence must not be claimed to match this subsequent guard delta. Integration remains experimental opt-in, not full reader/IME acceptance.
