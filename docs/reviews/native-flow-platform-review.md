# Native Flow platform adapter review

Date: 2026-09-30. Scope: current uncommitted Windows and AppKit typed Flow adapters, native interop, focused tests and macOS diagnostic. This is a static independent review, not target-native acceptance. No production code was edited; no native UI, input source, clipboard or other OS state was mutated by this reviewer.

## Initial findings (resolved by targeted code re-review below)

### P2: Keep literal fallback text outside the RTF parser boundary

Location: `src/Mote.Native/Windows/WindowsEditorShell.cs`, `InstallPreview` and `ImportPreviewPayload` (approximately lines 1290 and 1311).

The Flow-null path sends arbitrary `projection.Display` through the same `EM_SETTEXTEX` importer as generated RTF. Microsoft explicitly documents automatic RTF detection for a leading valid `{\rtf` or `{urtf` prefix. A literal fallback preview such as `{\rtf1\ansi hello}` is therefore not guaranteed to remain literal: RichEdit can display `hello` instead. Exact readback prevents authorizing a wrong source map, which is good, but occurs after interpreting the payload and does not restore the required visible text. The generated Flow serializer's inert escaping contract does not protect this alternate input path.

This API choice exists in HEAD too; it is not a newly introduced SetWindowText-to-RTF regression. However, the new temporary read-only removal makes programmatic imports functional and exposes this inherited trust-boundary issue. Do not claim arbitrary code execution or external resource access from this finding; neither was demonstrated.

Remedy: distinguish plain-text import from trusted generated RTF import, using an explicitly literal native operation for the former. Preserve the synchronous read-only restoration and exact readback checks. Add actual hidden-HWND coverage for RTF-looking literal fallback text, including braces/backslashes/Unicode, and verify selection and identity remain coherent.

Evidence: [Microsoft EM_SETTEXTEX documentation](https://learn.microsoft.com/en-us/windows/win32/controls/em-settextex). Confidence: high for API contract and reachable adapter input; native reproduction is delegated to the Windows implementation worker and pending.

### P2: Reset obsolete attributes on a same-text Flow-to-legacy transition

Location: `src/Mote.Native/Mac/MacEditorShell.cs`, `SetPreview`, Flow-null branch (approximately lines 1293-1349).

Typed Flow correctly clears the native attributed-string model before applying its attributes. The reverse transition does not: if a valid Flow-null analysis has the same `PreviewText` as the installed Flow model, `setString:` is skipped and the fallback branch resets only text color and optionally font. Old `NSUnderline` and `NSParagraphStyle` survive, so previously linked characters remain underlined and former list/quote paragraphs retain their hanging indentation/spacing even when the new model has none. This violates the contract that the installed visual attributes describe the installed presentation rather than its predecessor. Source ownership and identity checks do not repair visual attribute leakage.

Remedy: clear/reset the whole native attribute model for fallback installs as well, then apply fallback attributes, preserving native selection and scroll. Add a target-native same-text Flow -> legacy -> Flow transition to the synthetic probe; verify underline and paragraph-style removal, not just text and identity.

Confidence: high from the executable mutation sequence; macOS runtime verification remains pending.

## Positive assessment and limits

- Generated Windows Flow text escapes RTF metacharacters and signed UTF-16 surrogate units; no fields, objects, pictures or hyperlink actions are emitted by that serializer. Font data is similarly escaped.
- The read-only import transaction uses `finally` without message-pumping/yielding, and failed exact native readback does not authorize a prior source map.
- Windows selection offsets consistently pass through CRLF/RichEdit coordinate maps. Same-text restyle preserves selection and pixel scroll, subject to OS extent clamping.
- New AppKit range, point and CGFloat-plus-NSInteger interop signatures have the appropriate pointer/scalar argument shapes for x64/arm64. Font/color objects are autoreleased native factory results and retained by attributed storage; explicitly allocated dictionaries/paragraph styles are released after installation. Detached preview scroll view retains its original allocated ownership.
- AppKit links are appearance-only: no `NSLink` attribute or importer/resource access is introduced in the typed path.
- Installed sequence publication follows native text readback; explicit activation uses retained identity instead of pending analysis identity.
- The macOS diagnostic checks actual AppKit text/attributes, read-only state, identity, selection/scroll, and split layout. It does not assert physical Copy, reader usability, actual rendered pixels, real IME, parser correctness or ordinary Continuous coexistence. Its wrapper currently relies on the surrounding CI timeout rather than imposing a separate process deadline.
- Windows hiding the preview does not explicitly transfer a focused preview to source, unlike AppKit. This is an acceptance question, not a proven defect in this review: test focused-preview -> source-only under ordinary and legacy shells; ensure input remains on a visible control without stealing focus from an unrelated active window.
- No claim of successful macOS target execution or four-RID product acceptance is made. Host compilation alone is insufficient.


## Targeted re-review: fixes and CI probe admission

Reviewed current updated code on 2026-09-30; no unrelated completed validation was repeated.

Both initial P2 findings are **resolved in the current source**:

1. Windows `ImportPreviewPayload` now uses literal `SetWindowTextW` for Flow-null text and clear operations, and restricts `EM_SETTEXTEX` to generated Flow RTF. The existing synchronous read-only `finally` and exact readback remain. Two actual hidden-HWND tests cover ordinary and Unicode leading RTF-looking literal text, plus identity/clear assertions. The implementation leader reports the Windows suite passing 9/9; this reviewer inspected those tests but did not independently rerun that completed suite.
2. AppKit Flow-null installs reset the whole native attributed-storage range, then apply fallback foreground/fonts/spans. Same-text selection and scroll are restored. The target synthetic probe now verifies removal of old underline, paragraph indentation and font traits, selection preservation, and return to Flow underline. This corrects the stale-style data model rather than adding a content-specific guard. Actual macOS execution is still pending.

Additional revised safeguards inspected:

- Windows rechecks `_analysis` reference identity after synchronous visibility/layout changes; its hidden-HWND test explicitly publishes a newer analysis from `WM_SHOWWINDOW` and asserts newer text/map wins.
- AppKit defers analysis for composition in both shell profiles and replays matching deferred state after settling. Preview installation has a reentrancy guard; layout-induced newer analysis prevents old installation and is reposted only if still current and stamp-matching.
- No new material defect found in these targeted revisions. Real composition acceptance remains separate from static correctness of the deferral path.

### Safe to wire non-gating disposable-hosted macOS CI

**Yes: no static safety blocker to invoking `tests/NativeMacFlowRendering.ps1` against the published x64 and ARM64 Native AOT binaries on disposable hosted macOS runners.**

The exact one-argument diagnostic branch in `Program.Main` returns before normal configuration/telemetry/document startup. The synthetic probe constructs its own legacy AppKit shell and bounded in-memory source/Flow model, directly invokes native text/attribute/layout APIs, checks read-only/selectable state and retained identity, and closes its own shell in `finally`. It does not dispatch physical keys, modify input sources, call external-link handlers, request TCC permission, write the clipboard, load resources, open/save files, or modify user settings. The AppKit window can affect that disposable runner's foreground state; this is not a claim of zero UI activity and is not suitable for silently executing on a person's working desktop.

Conditions for CI admission:

- Keep the diagnostic non-gating with an explicit step/job timeout. The PowerShell wrapper does not have its own process deadline, so a surrounding timeout is required to bound a native hang.
- Retain exit/output evidence even on failure; do not reinterpret a green parent job or `continue-on-error` as a probe pass. Require both exit 0 and the exact `mote-native-mac-flow-rendering-ready` marker for diagnostic success.
- Do not label this synthetic legacy-shell probe parser, ordinary Continuous, actual rendered-pixel, external Copy, VoiceOver, real IME or full product acceptance.
- No macOS target-native success is claimed by this review. Its next authoritative evidence is execution on each published target binary.
