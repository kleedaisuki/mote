# Mac ordinary JSON Save: recurring failure and read-only routing discriminator

Date: 2026-10-01. Current evidence:
[CI 36809964231](https://github.com/kleedaisuki/mote/actions/runs/36809964231),
source `8d5796542b63e54e630933de38a2c68d864ca085`. This harness-owner record owns
only the Save-routing question; the independent four-RID matrix remains in
`native-json-large-ci.md`. No product, CI, input retry or native local rerun is
part of this investigation.

## Exact current outcomes and prior controls

| Case | Actual result | Material witnesses |
| --- | --- | --- |
| Current ARM64 1 MiB | complete pass | One attempted Command-S pair, exact Save, normal exit, GUI reopen and terminal traces |
| Current ARM64 100 MiB | **Save acknowledgement timeout** | Complete v0/v1, acknowledged one-byte edit, focused source/caret10, one returned two-post report, no clean acknowledgement, final disk original, forced cleanup |
| Current x64 1 MiB | complete pass | Exact Save, normal exit/reopen/terminal traces |
| Current x64 100 MiB | complete pass | Same scoped contract |
| Prior run 36806841387/source 48a3711 | all eight four-RID 1/100 MiB cases passed | Independent byte/normal-exit/reopen and zero-drop trace audit, not a reliability distribution |
| Earlier run 36802378381/x64 1 MiB | Save acknowledgement timeout | Similar dirty source/original working bytes; posting report had not yet been retained |

The previous all-eight success is valid for its exact run; the new failure shows
why one successful pilot cannot establish repeated-run reliability. It does not
retroactively invalidate exact byte/trace success or justify relaxing the current
failure contract. This recurrence is not exclusively a 100 MiB/ARM64 issue.

Independent artifact-only checks of the three current passing Mac cases confirm
exact saved hashes and successful causal/endpoint audits for all six original/
reopen sessions. The failed ARM64 large trace is zero bytes and has no reopen
trace. Structured recomputation is retained at
`.cache/native-json-large/ci-36809964231-mac-independent-audit.json`; no native
workflow was repeated to obtain these assertions.

Both current Mac tools match committed source identities. Driver LF SHA is
`6f1489c1fe3b4691bf3ce055d63534ffa7642a1c16c406ef58075ad6491a6f06`;
Swift LF SHA is
`1e78f1db707b0f11d2b349ad54d8dba786bfc9bd8ecd2acd846b4098d91172ad`.
Reported executable hashes (unchanged before/after): ARM64
`8e9cd26c258216712817ff4ca4d1392b3d29d135ba69484e55ff27af129305c5`,
16,508,264 bytes; x64
`4c498c2b2d28946e024e89fa6a1a641ef4faba28b60914c63fced8b9eff8fece`,
16,849,256 bytes. These are target inventory/hash observations, not locally
downloaded executable reinspection. Raw reports/logs reside below
`.cache/ci-36809964231-native-json-{osx-arm64,osx-x64}/`.

### ARM64 large failure, not an inferred delivery failure

The retained Save transaction report says:

```text
method = CGEvent.postToPid
status = attempted-posts-no-delivery-acknowledgement
attempted_events = 2
execution_acknowledged = false
guard_stage = save-dispatch
trusted = true, post_event_access = true
source units = 104857600, selection = 10:0
source focused = true, modified = true, Complete v1 = true
window count/copy errors = 0, counts = 1
```

The transaction returned in 58.585875 ms. Save acknowledgement then exhausted its
unchanged 60-second deadline. The last observer summary has 649 valid observations
and 648 bounded-copy calls over 79,096.982084 ms since driver attachment (this
includes initial source/idle semantics/edit work; it is **not** a 79-second Save
interval). One early count refusal recovered; no copy-pending refusal is recorded.
The first and final accepted Save-related snapshots show focused dirty source and
Complete semantics. Full per-poll focus/activity history was **not retained**;
do not claim every intermediate focus sample has been independently inspected.

The final working SHA is the original 100 MiB
`11c596afa32f508d22cf7704eb458200fd66c8ec05af3d9f4a384aeb0570c5db`,
not the expected edited
`f11fa45a8ff38a7fcf4c8cb925ed3775294cb98169e2aa75077b303d22459d09`.
The immutable fixture is unchanged. Forced cleanup leaves an unflushed trace,
not evidence of no Save-handler entry or a specific engine failure. The observed
fact is **no certified Save result**. The posting API reports no target delivery
or command-execution acknowledgement; no retry is safe merely because the
independent outcome was absent.

## Why source AX focus is insufficient for routing attribution

`MacAccessibilityElementPrototype.IsFocused` implements the source proxy by
checking only `window.firstResponder == inputEditor`. It does **not** inspect
`NSApplication.isActive` or `NSWindow.isKeyWindow`. Thus existing `focused=true`
is a first-responder witness, not proof that the application is frontmost or its
window is key. `MacEditorShell.Run` initially makes its window key and activates
the app, but that startup action does not certify the state at a later Save.

The ordinary Save shortcut is the File menu key equivalent `s` bound to
`moteSave:`; its selector calls `NotifyAfterComposition(SaveRequested)`. Native
text editing and menu-equivalent routing are not interchangeable. Apple's
[event architecture](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/EventOverview/EventArchitecture/EventArchitecture.html)
describes key equivalents and responder-chain dispatch through the key window.
[NSWorkspace.frontmostApplication](https://developer.apple.com/documentation/appkit/nsworkspace/frontmostapplication)
identifies the app receiving ordinary key events, while
[NSRunningApplication.isActive](https://developer.apple.com/documentation/appkit/nsrunningapplication/isactive)
reports frontmost status. These contracts motivate the hypothesis; they do not
prove how a particular synthetic PID-posted event was processed.

**Leading hypothesis to discriminate, not an established cause:** a retained
source first responder can differ from the application/window routing state
needed by this one-shot Command-S path. Alternatives remain lost/unprocessed
synthetic delivery, menu/handler routing, composition/Save admission or actual
I/O failure. Existing buffered telemetry cannot distinguish them after forced
kill. We do not patch product focus, activate the target, press Save again, or
call a posted report successful delivery to make the pilot green.

## Approved first discriminator: optional read-only Boolean facts

The same bounded Mac client now samples these before its one-shot Save and in
existing observe/failure polling:

| Field | Read contract | Interpretation limit |
| --- | --- | --- |
| `target_app_active` | Target-PID `NSRunningApplication.isActive`; nullable if lookup unavailable | Target activity, not an event receipt or Save-handler witness |
| `frontmost_is_target` | Compare current frontmost PID to requested PID, persist **only Boolean**; nullable if unavailable | Read PID transiently only for equality; never persist foreign identity or read app names/paths/titles; not global input |
| `window_main` | Owned window's `AXMain` Boolean; null on unavailable/error | A main window is not necessarily a key window |
| `window_focused` | Owned window's `AXFocused` Boolean; null on unavailable/error | Attribute observation, not an invented direct `isKeyWindow` result |
| Existing `focused` | Unchanged source proxy first-responder implementation | Deliberately not relabeled as application-active/window focus |

Queries are sequential within a client invocation, not an atomic snapshot. A
before-post activity observation can change before actual processing. The two
application activity facts are related OS metadata, not independent duplicate
proof. A correlation between failure and inactive state would support the
hypothesis, **not prove delivery loss or a product defect**. Active states with
failure would weaken that explanation and leave later routing/admission/I/O open.
Unavailable values remain null, never false permission/activation conclusions.

These fields are **diagnostic only**: no change to acceptance predicates, posting,
source/PID/trust/focus guards, per-phase deadlines, poll intervals, target reads,
normal/forced cleanup or one-action/no-retry contract. No activation, event tap,
system-wide AX object, foreign app metadata, global input, clipboard or TCC change
is added. The parser whitelists nullable Booleans and ignores foreign identity
fields; the retained Save guard snapshot preserves the new facts even if polling
later overwrites observer state. External timing can include added query overhead;
this is not a performance optimization or a controlled reliability fix.

Portable **22/22** tests pass: strict true/false/null/omitted facts, rejection of
non-Boolean substitutes, foreign identity redaction and retained Save-report facts,
with all prior byte/trace/no-retry/deadline tests intact. Tests mock reports, not
AppKit runtime behavior. Updated Swift compile and real activity correlations
require a future target-host run and independent review before promotion.

## If this is nondiscriminating

Do not install a global keyboard tap or request new permissions as a default
next step. Existing target `MOTE_NATIVE_MAC_STAGE_TRACE` has generic NSTextView
key-down breadcrumbs but **no Save menu-handler entry marker**; absence of such a
breadcrumb cannot prove non-delivery because a menu equivalent may be consumed
before text-view keyDown. A future separately reviewed opt-in, target-owned
content-free Save-selector/admission/worker marker, or a fresh-process ordinary
AX menu-action control (one Save per process, not a retry), could discriminate
later boundaries. Neither is implemented/approved by this readonly diagnostic.
Until a safe independent handler/result witness exists, keep the failure censored
and its causal attribution explicitly open.
