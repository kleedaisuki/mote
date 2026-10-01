# macOS external Grid AX probe review

## Scope and status

Independent review of `tests/MacGridAxExternalProbe/Probe.swift`; no production changes. Windows host has no Swift/macOS SDK, so syntax/type analysis is not a target compilation result. Wrapper review will follow when supplied. Target execution on both macOS RIDs remains mandatory.

## Necessary corrections identified

1. **Throwing short-circuit expression scope (high confidence).** In `matches`, `name == nil || (try label(node)) == name` leaves the `rethrows` operator outside the `try` expression. The navigation predicate has the same pattern. Precompute throwing labels after bounds checks or put `try` around the entire operator expression. Outer `try require(...)` covers nested argument expressions elsewhere and is not this issue. [Swift expression specification](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/expressions/) describes the scope of `try` and infix operands.
2. **Selection equality can falsely pass (demonstrated logically).** `same([a,a], [a,b])` returns true for distinct a/b: equal counts and one-directional membership do not establish set/multiset equality. A broken selected-cell provider may omit b and duplicate a while rectangle and normalization tests pass. Require unique elements and bidirectional membership, or consume matches one-to-one.
3. **Per-node timeout is not inherited (high confidence).** Setting messaging timeout only on the application handle does not bound descendant-handle calls to one second. Configure/check the timeout on every admitted exact-PID handle, and separately on the retained cell queried after close. Keep a wrapper process deadline. The SDK `AXUIElementSetMessagingTimeout` contract states timeout belongs to the specified AXUIElementRef instance, not equal references; no system-wide root is needed.

## Positive boundaries inspected

- Application root is constructed from the passed PID; all traversed/action handles are PID-checked.
- No system-wide tree, simulated global keys, TCC prompting/granting, or pasteboard API.
- AX arrays count before bounded copies; traversal does not expand table rows/cells.
- Source offscreen-string reads use synthetic fixed-range sentinels; report does not print fixture text.
- Selection actions read back state and explicitly distinguish local indexes from absolute labels.
- Untrusted AX status is separate from passed, not a product verdict.

## Remaining limits

A synthetic external API subset does not certify VoiceOver, geometry/Retina, input methods, performance or production default enablement. AXShowMenu and modal prompt bridging must be observed on macOS; unsupported action or inaccessible modal is an honest failed gate, never a key-injection fallback. Wrapper must prove the PID comes from its fresh exact synthetic launch, preserve binary/source provenance, bound child lifetime and verify source bytes unchanged and actual normal exit before claiming normal-close acceptance.

## Follow-up review (current working tree)

The initial three findings are corrected: throwing labels are precomputed, both arrays reject duplicate AX identities before membership comparison, and each admitted handle installs/checks a one-second messaging timeout. The retained-handle direct-read exception does not traverse or admit replacement objects and retains the already-installed timeout.

**Additional necessary correction:** nil alone on an AX read is not retirement/refusal evidence. `retained-old-cell-cannot-be-recycled` and `out-of-window-cell-refused` currently also pass `.cannotComplete`/`.apiDisabled` transport failures because the output is nil. Reject transient/access-disabled failures; allow explicit no-value/unsupported/invalid-object outcomes as appropriate to each operation. The post-close wrapper separately verifies editor exit, but preclose retired-node validation needs this distinction.

`Run.ps1` reviewed: source and helper hash, strict publish inventory, fresh child-owned PID, isolated MOTE_HOME, normal-exit observation, and fixture byte-integrity check are present. PowerShell parser returned no syntax errors. Portable preflight executed successfully with 1,100 records, 24 maximum columns, 263,760 UTF-16 units, SHA-256 `8BACC6F97A374853C5F04EE176384738D8BFF7F71B1C7E768D088310C98DD8C2`; unchanged input true. Evidence: `.cache/ci-inventory/review-mac-grid-ax-fixture.json`. This is not Swift typecheck or AX acceptance.

The 12,000 admission budget and 55-second internal deadline plus 75-second external-client watchdog are finite. `queryCount` counts admissions, not every low-level API invocation; interpret as such. AXShowMenu may be unsupported or a timeout on the target; retain failed-gate reporting rather than introducing global input or treating timeout as successful menu open. The close action may complete asynchronously; a single immediate retained-value read can conservatively fail before disposal. A bounded retry accepting only explicit no-value/invalid-object outcomes is optional robustness, not evidence already obtained.

## Final assessment

All four necessary corrections above are resolved in the reviewed working tree. The client now rejects transient transport/access failures as preclose retirement or invalid-lookup evidence. It also checks unique Table presence, header ordinal metadata, cell parent identity, and the exact synthetic coordinate prompt message before setting a numeric value. Close uses a bounded retained-handle poll and is expressly conditional on the wrapper observing normal editor exit. Wrapper disables trace/stage-trace inheritance.

No remaining substantive blocker found within this static and portable-review scope. This does **not** certify successful Swift compilation or external AX behavior: macOS typecheck/build and actual separate-client target execution remain required. Unsupported `AXShowMenu`, modal visibility, or timeout must remain a failed gate; absence of AX trust remains environment-blocked, never passed.

Independent `Test-Fixture.ps1` run passed both RID portable fixtures, deterministic CSV oracle, parser checks, and report-root refusal. Marker: `mac-grid-ax-portable-fixture-and-path-checks-passed`.

Reviewed SHA-256:

- Probe.swift: `51460E8511960789B6C28856F8D0CEAC0342E6B577DCA02168B236DC31456A70`
- Run.ps1: `DF68428D57701586CD99675A37F8955294990B929089143696D2EDF58B3D435A`
- Test-Fixture.ps1: `54CF150FF270DF6EF27CBFAD86307B0DA360130C2DADA5CFDB0DDC5747EF930C`

No production file was changed and no file was staged/committed by this reviewer.

### Frozen helper delta review

Reviewed the additional `AXCell` role/ordinal-prefix assertion and per-cell `AXSelected` true/false readback, plus semantic helper documentation. The throwing expression is covered by the outer `try` and left-hand `try`; selection flags require non-nil NSNumber values, so transport failure cannot pass the flag gate. No new substantive blocker. Prefix comparison is intentionally partial label evidence, not exact full-label validation; absolute coordinate correctness is independently checked by ranges/header labels/value.

Final frozen Probe.swift SHA-256 superseding the earlier value: `B74214F281DEE05235C9D272A64C1EC1EE86FE8BB0FBA6F342A05540C377A164`. Run.ps1 and Test-Fixture.ps1 hashes above remain unchanged. This small delta was statically reviewed; the already-completed fixture validation need not be rerun because fixture and driver are unchanged.

### Final cleanup and refusal delta (supersedes hashes above)

Reviewed bounded owned-child cleanup: watchdog expiration now enters shared cleanup; HasExited/Kill/wait races are caught, a ten-second cleanup wait replaces the unbounded wait, cleanup failure is recorded as probe-error, and fixture integrity/report writing continue. Process disposal remains explicit. Updated Run.ps1 parsed with no PowerShell syntax errors. This is static failure-path verification, not a reproduced macOS kill race.

Sparse, oversized, native-row-bypass and mixed-retired setters now require both unchanged selected-cell readback and a terminal/server-declared refusal-error whitelist. Indeterminate communication timeout and disabled API are not accepted as successful refusal. No new substantive blocker.

Final frozen reviewed hashes:

- Probe.swift: `0CB3E56BF68EBB22DA13316FE9AF1520D055DF0E41433F699359BCAC5E8FF36D`
- Run.ps1: `0A867687F62795790989B20A347A70EBEB93888923A666157DB45F5F5F816CEA`
- Test-Fixture.ps1 unchanged: `54CF150FF270DF6EF27CBFAD86307B0DA360130C2DADA5CFDB0DDC5747EF930C`

All prior target-evidence limitations remain. No repeat of already-completed fixture validation was needed for these process-cleanup/refusal-only changes.

### Hosted ordinal-transport diagnostic follow-up

Reviewed the exact Probe.swift delta adding `OrdinalObservation` after the hosted first-record label falsifier. The first-record assertion and all acceptance predicates remain unchanged. The observation surface is fixed at two already-admitted exact-PID axis nodes and eight named attributes each (16 entries), immediately after count validation; each read uses the existing admission/deadline/per-node timeout path.

Output retains only fixed node/attribute labels, AX error codes, value type class, UTF-16 length, fixed allowlisted equality/topology classifications, and bounded numeric ordinals. It never serializes raw attribute strings, fixture values, identifier serials, paths, global-tree content, or a source dump. `known-role-` concatenation is restricted to six literal role strings. Numeric suffix parsing requires ASCII digits and at most ten characters; NSNumber output is limited to 0...256. Wrapper-identifier classification reports structural facts only, not proof of provider identity.

Swift static type review: all Report initializers supply the new observations field; the Codable observation fields are concrete/optional primitives, AXError rawValue is Int32, and String/Substring equality and Character ASCII comparisons are consistent with the existing Foundation/Swift surface. No new throwing short-circuit scope issue found. No remaining substantive blocker in this diagnostic-only delta; target Swift typecheck/execution remains required. No fixture/driver validation was repeated because they are unchanged.

Reviewed Probe.swift SHA-256 for this follow-up: `A65E460046E0AA72B88F5E4289CAA0915A4EE15E1C24984B9C40DC6FD1227B8C`.
