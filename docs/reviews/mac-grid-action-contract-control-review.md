# Independent macOS AppKit action-contract control review

Date: 2026-10-01. Scope: newly added `tests/MacGridAxActionContractProbe/Contract.m`, `Run.ps1`, `Test-Guards.ps1`, and `README.md`. Review performed on Windows. No production or helper implementation was modified. Native SDK compilation and AppKit/AX execution are **not** verified by this review.

## Assessment

No confirmed substantive blocker in the control design was found by static inspection. The control is suitable for target-host compilation and evidence collection. Both initial diagnostic findings below are now resolved by follow-up static review; no open finding remains. Its result cannot establish product AXShowMenu acceptance or justify a product legacy bridge by itself.

## Resolved diagnostic corrections

### Resolved P3 — Describe the 20-second client deadline as cooperative, not a hard lifetime

Location: `README.md` lifetime paragraph; `Contract.m:225-245`, `FindElements`, and `Run.ps1` owner watchdog.

The client checks its deadline before discovery attempts, before each variant, and between state polls. `FindElements` can perform a complete bounded traversal without checking this deadline, and the action-name/enabled/action phase also proceeds without another deadline check. Individual AX calls have a 0.5-second messaging timeout, but a sequence of those calls can exceed the stated 20-second client lifetime. A timeout-crossing discovery can finish considerably later than 20 seconds before the next loop check rejects further work.

Impact: the documented client safety bound is stronger than the implementation. This does not remove the independent 45-second owner process-tree watchdog and is not evidence of unbounded normal execution.

Correction: describe a “20-second cooperative client deadline, bounded AX calls and a 45-second owner process-tree watchdog.” If a true 20-second client cutoff is required, add an independent client watchdog or propagate deadline admission into every traversal/action query. Do not claim such a hard cutoff from the existing loop alone. Confidence: high, direct control-flow evidence. Follow-up: README now explicitly describes phase/poll-boundary admission, warns that admitted calls can outlive the client deadline, and identifies the owner watchdog as the hard bound. This resolves the documentation finding without claiming a new hard client cutoff.

### Resolved P3 — The recorded poll count is one too high on exhaustion

Location: `Contract.m:247-262`.

The loop executes at most 20 AXHelp reads. On ordinary exhaustion, `polls` becomes 20 and the persisted `polls + 1` value is 21. If the deadline has already expired when the loop starts, it performs zero reads but records one. Successful early breaks have the intended count.

Impact: diagnostic counters misrepresent actual observation effort; the action error and lifecycle booleans are unaffected.

Correction: maintain an explicit attempted-query counter incremented immediately before the AXHelp read, and persist that count. Confidence: high, direct loop arithmetic. Follow-up: the loop now increments `polls` immediately before the attempted Attribute query and records `polls` directly. Zero attempts remain zero and full exhaustion records 20. This resolves the counter finding.

## Checked contracts

- All seven synthetic variants share the same ActionElement admission/menu implementation. Native action encoding is extracted from the compiled baseline; B/c variants change the runtime metadata while retaining the same one-byte BOOL IMP. Direct dispatch and NSInvocation return observations are isolated by `inspecting` and do not increment dispatch/admission/menu counters.
- Runtime classes inherit ActionElement, not CompiledModern, so the compiled modern override is not accidentally inherited by runtime variants. The wrong-getter control adds only `accessibilityEnabled`; metadata separately records whether `isAccessibilityEnabled` is inherited/responding. Consequently it must be interpreted as a getter-spelling experiment, not an assertion that the inherited correct getter is absent.
- Legacy controls advertise and dispatch only NSAccessibilityShowMenuAction. Both modern and legacy call counts remain separately recorded; completion never implies which entry point executed.
- An action is attempted at most once per variant and only after advertisement. Completion is deliberately independent of the external AXError. A reported popup does not convert a non-success AX result into successful external acknowledgement.
- Discovery starts at an application AX object for the owner's exact PID, not a desktop/system-wide object. Windows and child arrays have count-before-copy bounds, copied elements have AX type/PID checks, discovery is capped at 32 queued nodes, and children are capped at 16. AXUIElementCopyActionNames has no count-before-copy API: its returned count is checked after copy, so this array is not framework-copy hard-sized.
- No global keyboard/mouse event, app activation request, TCC prompt/reset, mote launch/link/patch, source access, or user menu action appears in the helper. Window ordering and application-defined event-loop wakeup are not global input synthesis.
- The scheduled cancellation targets the exact owned NSMenu and includes tracking run-loop mode. The owner waits for its exact NSTask child, closes its window and records separate final counters. The PowerShell wrapper applies a 45-second watchdog and process-tree cleanup if the owner remains running.
- The script lexically restricts reports to repository `.cache/ci-inventory` JSON paths, uses fixed scratch/artifact filenames under repository `.cache`, removes only fixed stale server/client reports, and does not persist discarded native stdout/stderr as desktop/source dumps. As with ordinary lexical path guards, this is not a symlink-resolution guarantee.
- Portable validation does not set compilation/typecheck/normal-exit flags and does not invent native server/client data. Native typecheck and compilation failure remain probe errors, not successful preflight.

## Validation performed

`./tests/MacGridAxActionContractProbe/Test-Guards.ps1` printed `mac-grid-action-contract-portable-guards-passed`. The driver parsed successfully, both RID preflight schemas passed, and the three invalid report paths were refused. Generated preflight artifacts remain in repository `.cache/ci-inventory/preflight/`.

Follow-up static review also checked the wrapper exit-evidence correction: `owner_exit_code` comes from the process, `owner_normal_exit` comes only from `server.normalShutdown`, and `child_exit_code` comes from `server.childExit`. The unavailable classification now requires owner exit zero, normal shutdown and child exit zero; mere WaitForExit completion cannot invent normal shutdown. The updated portable guard asserts both exit-code fields remain null in preflight. The integration owner reports the revised guards passed; this reviewer did not rerun completed guards. No additional finding arose from these changes.

This reviewer performed one portable validation run; it was not native Objective-C compilation or external AX invocation. Actual SDK/ARC/Werror acceptance, architecture-specific BOOL signatures, accessibility trust, background-window popup behavior, menu delegate delivery and both target RID action results remain target-host checks. Static inspection found no demonstrated incompatible IMP cast or BOOL return-buffer sizing error, but cannot certify SDK-dependent compilation.

## Primary external evidence and interpretation

Apple documents [accessibilityPerformShowMenu](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol/accessibilityperformshowmenu%28%29?language=objc) as returning whether the action was triggered, not whether its final effect succeeded. Apple's [NSAccessibility protocol guidance](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol) identifies `isAccessibilityEnabled`, custom NSAccessibilityElement subclasses, and selector-permission customization. These support the isolated metadata/getter experiment but do not explain a product transport failure by themselves.

[Chromium's native macOS accessibility implementation](https://chromium.googlesource.com/chromium/src/+/4a27de5bfd26225308f44e393bf2c560d31e8043/ui/accessibility/platform/ax_platform_node_mac.mm) supplies a real production precedent for legacy action-name/perform-action dispatch. That is a comparison control, not proof that a narrow synthetic bridge is the correct product architecture. No academic claim or frontier conclusion is being made from this engineering control.

Reference provenance: the integration owner reports Chromium source frozen at commit `4a27de5bfd26225308f44e393bf2c560d31e8043`, downloaded-source SHA256 `FFA417CD60D5DDAB12752BFFE9C2861CA8805F0B8A3F8ABBE388B3B7366840B9`. This follow-up did not independently redownload or hash that external artifact.
