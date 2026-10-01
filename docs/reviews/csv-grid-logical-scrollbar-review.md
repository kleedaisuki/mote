# CSV Grid logical-scrollbar independent architecture review

Date: 2026-10-01. Scope: `docs/csv-grid-logical-scrollbar.md` proposal, inspected alongside production at `dea61e5`. This is a static design review, not implementation acceptance. No production/design files changed; no tests, native UI, clipboard, or hosted workflows executed.

## Disposition

The separation between logical navigation extent and bounded native table payload is sound. Existing Formats contracts can represent the required exact/prefix facts and ready/pending/missing distinctions. Two narrow contract gaps were identified in the initial draft: certification admission fairness and gesture-specific admission of delayed events. Both are now resolved in the frozen proposal reviewed below. No outstanding substantive design finding remains in this inspected scope; this is not implementation acceptance.

## Resolved findings and targeted re-review

### Resolved P2 — Strict latest-delivery priority permits demand certification starvation before it starts

Location: proposal section 7, especially “Dispatch order is current content/source-visible work, latest bounded Grid delivery, then admitted certification” (approximately lines 320–327), together with the same-version demand entry point (lines 306–317).

Counterexample admitted by that ordering:

1. A large partial CSV has an unknown total; End/Next explicitly admits demand Full.
2. Before the current delivery finishes, a wheel/track event replaces the latest pending Grid target.
3. At each serialized-lane handoff, another bounded Grid target exists, so the dispatcher selects that target before certification.
4. The user keeps navigating; the Full task never starts, although its cancellation token was never canceled and the queue contains only one pending target.

Separating delivery and certification cancellation ownership removes the existing restarted-quiet-timer problem, but does not alone establish progress under this schedule. Coalescing limits queue size, not how many consecutive delivery turns can win admission. This matters specifically for the proposed unknown-domain End/Next promise: a file-wide extent remains unavailable indefinitely during continued interaction.

Production evidence: `NativeEditorController.ScheduleAnalysis` cancels `_idleFullAnalysis` unconditionally; CSV session delivery has an 80 ms delay. `NativeIdleFullAnalysis` owns an independent Full timer/task, while `NativeFormatSessionDriver` serializes actual calls through `_analysisGate`. The proposal correctly changes these seams, but does not yet specify which bounded scheduler must reserve the certification turn. This finding does not claim a measured GUI regression.

**Required remedy:** once a same-version demand attempt is resource-admitted, reserve its next eligible lane turn after current content work and at most a finite bounded delivery burst. A simple rule such as one latest delivery followed by the reserved Full is sufficient; no general task-priority framework is necessary. New content edits still cancel obsolete certification and win admission. Idle-only certification may remain opportunistic. If demand Full is enqueued immediately into the existing serialized driver instead of dispatched by strict priority, state the finite admission ordering explicitly and ensure later viewport work cannot repeatedly overtake it.

**Discriminating test:** use a controlled driver and submit a new same-version navigation target at every delivery completion. Demand Full must start after the documented finite burst without requiring navigation to become quiet; pending target storage remains one. Separately verify a content edit cancels Full and is the next admitted content turn.

Confidence: high that strict priority allows the schedule; impact conditional on implementation adopting that ordering without an additional reservation/fairness rule.

### Resolved P2 — Document-scoped navigation identity does not distinguish late actions from superseded gestures

Location: proposal section 5, `NativeGridNavigationId`, `NativeGridScrollFrame.RequestSerial`, shell event fields and gesture lifetime (approximately lines 198–254).

The proposed shell event carries NavigationId, axis, target, and phase, while its NavigationId contains only Document and Epoch. The frame has RequestSerial, but that serial is not included in the event. Epoch retirement is explicit for document/content/geometry changes, not for beginning a newer gesture at unchanged document/geometry.

Counterexample under those stated fields:

1. Begin A captures navigation epoch E, then queues/coalesces a Track or Commit action.
2. A is canceled/superseded; Begin B starts at the same version and geometry, also using E.
3. A's delayed action is delivered after B begins, with the same NavigationId and axis as B.
4. The controller cannot distinguish A's old target/terminal phase from a valid B action using the advertised event fields. A can overwrite B's latest target or terminate its frozen-range lifetime.

The design expressly supports coalesced dispatch and nested native layout/install reentrancy, so delayed event admission is a real interface obligation. Per-delivery request serial validation only rejects obsolete analysis results; it cannot identify an old incoming gesture event unless that event carries corresponding immutable authority.

**Required remedy:** include a per-gesture immutable token in all Begin/Track/Commit/Cancel events and admit later phases only against the active token; alternatively increment/retire the navigation epoch on every Begin and explicitly retire it at Cancel/Commit, with a defined one-shot event path. Do not increment the gesture token on each successful same-version ready installation. Extent/page freezing and palette-only refresh continue to preserve the active gesture. The token must be captured when the adapter creates/queues an event, not synthesized from the controller's current frame when dispatch finally occurs. Define terminal idempotence: a late Cancel/Commit from A cannot cancel B or its outstanding latest delivery.

**Discriminating test:** Begin A, queue A Track/Commit, retire A, Begin B, then dispatch A's queued actions. B's target/frozen frame remain unchanged. Repeat with same-version palette refresh, extent update, and reentrant installation between queue and dispatch.

Confidence: high for insufficient event fields if epoch is document-scoped as currently described; resolved by explicitly choosing either token strategy above.

## Inspected contracts with no additional substantive finding

- `CsvGridContracts`: paired exact row/max-width counts, independent certified prefix, checked `GridRange.End`, capped rows/columns/cells, actual row ordinals, and Pending without source origin support the proposed model. Native observed width is correctly described as a lower bound rather than prefix-wide maximum.
- `CsvGridProjection.GridRows` and `ProjectGrid`: ordinal seeking uses certified record checkpoints; unknown coordinates yield no fabricated row. Shared replay admission and possible omissions justify explicit pending slots rather than compacted row indices. Exact extent is not a guarantee of complete field delivery or latency.
- Unknown numeric targets, End, exact empty state, prefix percentages and viewport clamping are distinguished coherently. The design does not infer rows from bytes/physical lines or silently reinterpret an out-of-range Go-to command as successful clamping.
- Windows production `WindowsCsvGrid.Install`, display/custom-draw/hit callbacks currently use `grid.Rows` local array positions; all must migrate together to the bounded ordinal slot map. `EnsureVisible` and old boundary/wheel handlers need the stated removal of duplicate request/selection-driven viewport reset. These implementation changes are already explicitly required, not additional findings.
- Mac production `MacCsvGrid.Install` restores native row selection or falls back to local row zero. Its cell/detail/intent lookup must use ready descriptors after the slot migration; the proposal correctly refuses treating that fallback as retained offscreen selection. Native reload/visibility calls require the stated post-call epoch/serial rechecks before restoring authority.
- Full publication currently checks captured source viewport equality in `PublishIdleFullAnalysis`; the proposal correctly requires merging same-version certification facts and re-querying current interests rather than installing the captured viewport. Implementation must relax obsolete viewport admission only for facts, not for source-decorations or commands.
- Existing idle memory admission is advisory and cannot guarantee OOM freedom. Reusing it preserves resource policy; a demand request must not bypass it. No additional allocations proportional to whole-file row/column count are introduced by the proposed controls.
- Logical column navigation deliberately does not claim global variable-width pixel extent or retained per-column width state. The explicitly bounded/truncated extreme-pane behavior is honest rather than an invisible enlargement of cache limits.
- External UIA/AX inspection and Narrator/VoiceOver/manual-input gates are correctly separate from synthetic native delegate probes. Truthful native range exposure remains unverified, including standalone NSScroller style, hit area and AX behavior on both Mac targets.

## Native arithmetic evidence

Opened Microsoft primary documentation on 2026-10-01:

- [GetScrollInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getscrollinfo): `SB_CTL` uses the separate scrollbar HWND; `SIF_TRACKPOS` retrieves a 32-bit thumb position, unlike the 16-bit position inside scroll messages. The proposed >65,535 handling is correct.
- [SetScrollInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setscrollinfo): native position bounds are `nMin .. nMax - max(nPage-1,0)`, giving `0 .. N-V` for the proposed nonempty model. **Implementation note, not a finding:** its return value is the resulting thumb position, not Boolean success. Zero is a valid first position; do not reject a legitimate installation using `if (SetScrollInfo(...) == 0)`. Read back the requested certified range/page/position where appropriate.

The NoDB/selective metadata and incremental parsing references in the proposal explain design motivation, not UI progress, native accessibility, mutable-file correctness, or percentile acceptance. This review does not reproduce their experiments or derive new performance claims from them.

## Evidence limits / acceptance state

The proposed <=1 ms callback, <=8 ms install, <=50 ms warm request-to-install, <=16 ms coalescing and <=10 ms cancellation observations are explicitly **targets**, not results. This review establishes no native ABI, physical input, rendering, accessibility, memory percentile, startup, or end-to-end performance acceptance. Existing production remains bounded-window Grid navigation until implementation and its stated gates pass. No additional speculative findings are raised.

## Frozen revision disposition

Targeted re-review on 2026-10-01 confirmed proposal SHA256 `C0CAA46A5F95F6099BC2CF7F9114A61C8C7E9F27B8250062BAEC839A54E5871A`.

- **Certification finding resolved:** section 7 now reserves demand Full after the current running turn and at most one mandatory current-version content pass, ahead of all remaining same-version viewport traffic. All callers share a dispatch admission point before the driver gate; Task creation/semaphore ordering is not used as a fairness claim. Navigation cannot spend another turn or reset the content deadline, and failed resource re-admission produces Deferred instead of silent indefinite reservation. New edits still retire obsolete work.
- **Gesture finding resolved:** section 5 adds `NativeGridGestureId` with non-reused sequence, controller Begin admission, immutable adapter capture and exact active-token checks. Begin B retires A before reentrant callbacks; terminal phases consume authority before dispatch and cannot clear a nested B in a stale handler tail. Delivery completion remains navigation-epoch/request-serial scoped, so Commit does not accidentally invalidate the final accepted viewport result.
- The revision also correctly distinguishes valid zero `SetScrollInfo` position from Boolean failure and preserves source-viewport validation for decoration/commands while allowing same-version certification facts handoff.

No tests were repeated. These resolutions are supported by the revised written contract, not proof that a future implementation obeys it. The target-platform/performance/accessibility gates above remain open.
