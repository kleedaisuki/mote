# macOS bounded Grid Table-proxy design review

Date: 2026-10-01. Scope: static review of the proposed stable NSAccessibilityElement Table proxy, existing Mac Grid ownership/selector seams and official Apple documentation. No production edits. Parent reports external AX run 36787947202 at 5977250 failed both Mac RIDs because AXRows[0] was an unlabeled default native AXRow while AXColumns returned our labeled/identified wrapper. Reviewer has not independently executed or fetched that run. Earlier selector successes do not establish cross-process hierarchy merging.

## Decision (future route B; not implemented or currently authorized for delivery)

A stable bounded Table proxy would be a coherent, appropriately scoped response to the reported native row-synthesis failure. Keep NSTableView/NSScrollView as the unchanged rendering/input implementation, and replace only their Grid accessibility representation. Reuse the existing bounded immutable frame, epoch-bound rows/columns/headers/lazy cells, sole adapter selection owner, geometry conversion and native keyboard/menu/controller contracts. No parser, whole-file child array, source action pattern or replacement input responder is needed.

Apple documents NSAccessibilityElement as the representation for non-view accessible elements and requires role/label/parent plus inclusion in the parent's accessible children. Its custom-control guide separates this hierarchy from view ownership and explains explicit notification publication. [Custom controls](https://developer.apple.com/library/archive/documentation/Accessibility/Conceptual/AccessibilityMacOSX/ImplementingAccessibilityforCustomControls.html), [NSAccessibilityElement](https://developer.apple.com/documentation/appkit/nsaccessibilityelement-swift.class).

Recommended graph:

```text
Existing source accessibility (unchanged sibling)
CSV View / AXGroup
  stable CSV Table proxy / AXTable
    epoch column headers
    epoch rows / AXRow
      row ordinal header
      epoch cells / AXCell
    AXColumns / AXRows relationships reference these same nodes
  actual logical row scroller
  actual logical column scroller
  existing Go-to/detail/status surfaces

Physical firstResponder, drawing and keyboard/menu input: native NSTableView
```

Rows/header/column parent becomes proxy. Cells should keep their row as parent when row.children includes those cells; do not return proxy as cell.parent while claiming that cell as row.child. AXRows, AXChildren, selected/visible cells and parameterized lookup must return the same current node identities, not separate proxy copies or native row views.

## Why not only override native row views?

Apple's NSTableRowView is the native per-row view and conforms to NSAccessibilityRow, so a custom row-view label override is a legitimate small experiment. [NSTableRowView](https://developer.apple.com/documentation/appkit/nstablerowview?language=objc). It is not proven impossible.

However, this proposal already has complete bounded metadata wrappers, while the external result demonstrates that native Table translation substitutes rows despite in-process selectors returning our metadata. Merely labeling default row views would not establish that row.children, cell.parent, selectedCells and pending slots share one identity model. A native-row solution must also handle row recycling/rebinding: an externally retained row view must not acquire a new ordinal or another document. It would need coordinated row/cell wrapper parenting and epoch/lifetime logic, and still must externally prove the framework does not merge competing native children. Given that observed failure, the proxy removes this special case instead of adding another overlapping row mapping. Prefer it, but do not call it externally fixed until the exact previously failing client reacquires labeled rows/cells on both ABIs.

## Material safeguards before integration

### 1. Excluding a view is not suppressing its descendants

Apple explicitly states accessibilityElement=false skips the element and proceeds to its children. It therefore does not hide NSTableView's default row/cell subtree. [Standard controls](https://developer.apple.com/library/archive/documentation/Accessibility/Conceptual/AccessibilityMacOSX/EnhancingtheAccessibilityofStandardAppKitControls.html).

Provide an authoritative Grid View accessibilityChildren array containing proxy and the existing relevant logical controls/detail, excluding the native scroll subtree. Suppress duplicate native scroll/table children narrowly (empty accessible children or an externally verified hiding mechanism); preserve actual rendering, view visibility and firstResponder behavior. Do not hide View itself, unrelated window children, source input island or logical scrollers. Use the same intended accessible set for navigation/visible-child traversal and hit testing, so there is not a second entry path into hidden implementation rows. This is a concrete requirement, not a proof that one AppKit setter alone accomplishes it.

### 2. Stable Table identity must not be an epoch-bound cell node in disguise

The proxy is stable for one Grid attachment, whereas rows/columns/cells remain bound to frame.Id. Keep a separate proxy-owner lookup or explicit stable-table node semantics that captures CurrentFrame once per request. Do not create proxy with the first window Id and then require that stale Id after rebase. Live RetireAccessibility clears current frame and child maps before releasing epoch nodes but preserves attached proxy; disposal removes the proxy-owner mapping before native teardown/release. A retained detached proxy must never reach the old Grid/controller through a static dictionary or delegate.

Check native ownership too: clear parent-child property storage during disposal if it can form a retaining View -> proxy -> View cycle. Alternatively custom parent/children accessors can derive hierarchy from live lookup without permanently storing native parent graphs. Do not assume retention semantics not documented by Apple. Existing old-child entries must remain removed, so retained stale cells cannot root managed owners or return new-window facts.

### 3. Semantic focus must follow actual native focus without moving it

Keep makeFirstResponder/readback targeting NSTableView. Proxy/cell setFocused forwards to the existing guarded adapter operation; it is not a source action. Semantic focusedUIElement returns the current admitted cell, or proxy for table-only/Pending focus, only when the native table really is firstResponder. Source/scroller focus must not be masked by a retained Table frame.

Preserve a narrow focus projection hook on the native firstResponder path even if the Table selectors migrate to proxy. App/window focusedUIElement queries may otherwise return the ignored native table or no target; a correct proxy-local focus getter alone does not prove global focus lookup. Do not return hidden `_table` as the fallback semantic element or post Table events on it. Keep focused keyboard Reveal/Replace routing, explicit table-only focus, Pending guard, composition refusal and menu freezing unchanged.

### 4. Geometry, events and callback reentry stay coherent

Proxy frame is the actual clipped table/header footprint, not the entire CSV group/scrollers and not a source-interpolated rectangle. Continue native geometry conversion for rows/cells/headers; include the header in Table footprint if hit testing returns header nodes. Apple distinguishes parent-space frame from screen frame and documents parent-space tracking as the parent moves. [Frame in parent space](https://developer.apple.com/documentation/appkit/nsaccessibilityelement-swift.class/accessibilityframeinparentspace). Either use that mechanism correctly or preserve the existing explicit screen conversion; never mix coordinate systems.

Route layout/selection notifications to proxy and focus notifications to its current focus node. Group hit test forwards table/header points to proxy/current nodes, scroller points to actual controls, and outside-table points never to cells. Publish bounded metadata before notifications and stop superseded outer event delivery after reentry. All native creation/release, owner lookup and lazy cells remain main-thread owned; no source decoding or worker waits enter these callbacks.

## Small discriminating target matrix

| Check | Required observation |
| --- | --- |
| Exact failing external AXRows query | One Table proxy, AXRows[0] carries expected absolute Row 1001 label and our identifier; AXColumns/cells agree |
| Local table relationships | Row index 0; cell row/column ranges local {0,1}; cell.parent=row, row.parent=proxy; lookups and row.children identify same cell |
| Tree/point paths | Exactly one CSV semantic Table; no native row/NSTextField subtree resurfacing via children, point lookup or focus |
| Global native focus | Source -> native table/cell -> logical scrollers; global AX focus returns corresponding proxy/cell/control while firstResponder readback stays native |
| Existing input regression | Native arrows/Shift, menu freeze, Copy/Replace paths and source-island/IME guards unchanged; no new AX source action advertised |
| Rebase/clear/close | Stable attachment proxy, new epoch children, held old children unavailable; held detached proxy has no managed owner or source retention |
| Move/resize and both ABIs | Current screen frame/header hit, clipped visible cells and valid Rect/Range returns; target execution rather than managed layout inference |

Do not extend this patch into whole-file virtualization or a new command acknowledgment protocol. Existing contract's production cached-frame precedent and reader-orientation motivation remain the external rationale; this design specifically resolves a framework hierarchy substitution, not reader speech acceptance.

## Review limits and current conclusion

No substantive design blocker was found provided the hierarchy suppression, native-focus projection and stable-proxy/epoch-child lifetime safeguards above are implemented. They are implementation acceptance requirements, not defects asserted against nonexistent code. Native row-view override is possible but has no current evidence advantage and more identity/merge work than the proposed proxy. No production, test, workflow, README or release-gap files were modified. No external AX, VoiceOver or real IME pass is claimed. Final implementation should receive a focused diff review and the two-RID external failing-case test before freeze.


## Approved route A: AXRows-only legacy bridge experiment

Parent subsequently approved only the smaller route A as a discriminating opt-in experiment before considering the Table proxy. The route B proposal above is future design analysis, not implemented/delivered scope. Actual inspected delta adds one accessibilityAttributeValue: override to the existing custom NSTableView class when AccessibilityEnabled is true, plus a pointer-return objc_msgSendSuper import and targeted native probe assertions.

### Static verdict

No substantive correctness, ownership or ABI defect was found in this narrow experiment. The callback first checks main thread, then the exact live native table lookup. It specializes only the NSString attribute AXRows, returning the existing current bounded row-wrapper array only outside installation with a complete frame. It does not create another row cache, parser, projection, selection owner or source action. During retirement/install it returns nil; removal from Instances on disposal prevents a retained native table from reaching the old managed owner. Other supported attributes delegate to NSTableView rather than introducing a parallel legacy attribute model. Unexpected off-main requests return nil before mutable lookup access or superclass dispatch.

The registered Objective-C encoding `@@:@` matches object return/self/SEL/object argument. The fixed P/Invoke signature returns an object pointer and takes ref objc_super plus selector and attribute pointer; no aggregate return or stret path is involved. Existing Super consists of receiver and superclass pointers. Starting lookup at NSTableView skips the custom subclass override, avoiding direct recursion. Apple documents objc_msgSendSuper as dispatch beginning at the specified superclass and returning the target method's simple return value. [Objective-C runtime](https://developer.apple.com/documentation/objectivec/objc_msgsendsuper?language=objc). This is static ABI review, not a claim that both target executions have passed.

The callback has no worker wait or native focus/selection/source mutation. It serves current rows after the attribute comparison, so ordinary nested installation before that point cannot resurrect an old coordinate; old row maps are already epoch-retired by existing code. Returned array uses the existing autoreleased NSMutableArray helper, preserving normal object ownership. No new retained history or indirect managed owner field is introduced. The default build remains unchanged because the selector override is registered only beneath the established experimental environment gate.

### Probe discrimination and limits

The new native probe asks legacy AXRows and modern accessibilityRows, compares counts and the exact first row handle, checks absolute Row 1001 plus our window/row identifier, and invokes legacy lookup off-main to assert nil. The test-only synchronous Task.Run wait is safe for this specific discriminator because the callback refuses immediately and does not marshal to main; this wait is not added to production accessibility callbacks.

These checks establish only direct selector equivalence and refusal behavior on a target binary. The decisive experiment is still the same cross-process AXRows failure on both Mac ABIs: it must now return our labeled/identified current wrapper rather than default AXRow. A passing in-process legacy call is not sufficient to prove NSTableView's external bridge uses that override. Keep negative external evidence if it still fails; only then proceed to a separately approved coherent proxy (route B), not expanding the legacy override attribute by attribute.

No production/test files were changed by reviewer; no target tests were run or rerun. Full reader/IME, external selection/focus/hierarchy and release-promotion gates remain unchanged.


Final authorization boundary: route A is the currently root-approved cheap discriminator only. Route B remains a proposed conditional fallback if fresh cross-process two-RID falsification shows A cannot preserve the bounded wrappers; it is not current implementation approval. Coordinator reports local focused 10/10 and production/probe diff check passed. Reviewer does not repeat those tests or infer hosted success. Static review supports freezing the narrow A delta for the approved hosted experiment.
