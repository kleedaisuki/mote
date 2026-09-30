# Native preview-to-source navigation

## Decision and invariants

The preview is a bounded (16 Ki UTF-16 units / 120 lines), read-only semantic
projection, not a second document. The platform shell owns input gesture and
focus mechanism; the controller owns source-coordinate meaning. A preview
activation carries `(NativeDocumentStamp, preview UTF-16 offset)`. The shell
must attach the stamp of **actually installed** preview text, not merely the
most recent queued analysis. The controller accepts only the current document
generation/version and its own matching presented analysis, then finds an
explicit `NativePreviewSpan`. It does not derive source offsets by subtracting
indent or line numbers from arbitrary displayed text.

`NativePreviewSpan.Navigable` distinguishes semantic source items from
decorative context banners and CSV row ordinals/delimiters. A successful
activation collapses the canonical global source selection at the item's
start, reveals it in the LegacyPage or Continuous viewport, and returns focus
to the source editor. It performs no document transaction, Undo operation,
IME commit, or file write. Active source composition vetoes navigation
*without* forcing a commit. A stale stamp, missing/invalid source span,
nonboundary target, truncated-away text, or preview whitespace is inert.

The map is deliberately at **item granularity**, not an invented per-glyph
origin: Markdown lines point to their semantic node start, CSV displayed cell
text points to the exact parsed cell start (not its containing row), plain
text lines point to their own source-line start across CR, LF, and CRLF, and
structured entries point to semantic-node starts. Formatting and display
truncation may remove visible characters; their absent suffix has no hit
target. Clicking displayed link-looking text navigates to source; it does
**not** open a URL. Image and link activation, security policy, and richer
rendering remain separate work.

This one-way choice is intentional. Research on
[bidirectional structured-document editors](https://takeichi.ipl-lab.org/~scm/pub/pepm04.pdf)
and more recent [effectful lenses](https://doi.org/10.1145/3747523)
models a much stronger view-update operation: edits to a rendered view are
translated back to source while preserving round-trip laws. Mote's bounded,
lossy preview (clipped values, formatting, omitted syntax) does not have a
unique such inverse. Promising that a preview edit could rewrite Markdown,
CSV or YAML safely would hide ambiguity rather than solve it. An explicit
item-origin map gives useful navigation without duplicating engine ownership
or weakening source fidelity; direct preview editing would need its own
format-specific inverse and ambiguity contract.

The stamp identifies a document version, not a unique presentation of that
version. Today each shell installs non-composition analysis synchronously;
composition-deferred analysis cannot activate because the controller vetoes
active preedit. If a future shell asynchronously paints two different
same-version viewport previews, this contract must add a presentation nonce
or retain an installed-map acknowledgement. A version stamp alone would then
be insufficient to pair a click with the map of the bytes actually shown.

Windows RichEdit converts preview CRLF display positions through its native
character-position map and then to the UTF-16 preview offset before emitting
an activation. A collapsed pointer click or unmodified Enter/Space activates;
drag selection retains copy behavior. macOS uses a read-only NSTextView
subclass: `mouseDown:` completes native selection tracking first, then only a
collapsed selection activates; Enter/Space activates the selected caret while
modified shortcuts continue through AppKit. Both adapters retain the painted
stamp separately from pending/deferred analysis.

## Evidence and negative probe

On 2026-09-30, focused Release tests passed **8/8**:
`NativePreviewNavigationTests` checks mixed-newline plain text, CSV cell
mapping/decorative rejection and invalid spans; controller tests check
version rejection, composition veto, nonmutation, both presentation modes,
and an off-page TOML item beyond the 64 Ki-unit LegacyPage boundary. The
Windows current-source Release **managed apphost** then passed the bounded
real-HWND `tests/NativeWindowsPreviewNavigation.ps1` in four separate runs:
LegacyPage/Continuous × pointer/keyboard. LegacyPage moved the source caret
from 0 to the exact global offset 9. Continuous rebound its bounded input
island to `## Destination` with local caret 0; global-frame correctness is
covered by the controller test, not directly measured by that HWND probe.
Each run sent Save and Undo afterward and observed unchanged on-disk source
and no dirty-title marker. Reports are under `.cache/ci-inventory/`.

An initial probe caused process error `0xC0000005` inside
`WindowsEditorShell.PreviewSubclass → Win32.DefSubclassProc` by sending
pointer-bearing `EM_EXSETSEL` (`WM_USER+55`) **from another process**.
Windows does not marshal the caller's pointer for application-defined
messages at or above `WM_USER`; this was a harness fault, not evidence of a
product subclass defect. The reproducible probe now uses scalar system
messages `EM_SETSEL`/`EM_GETSEL` below `WM_USER`, and all four modes passed.
No product guard was added for invalid external pointer injection.
This boundary is documented by Microsoft's
[SendMessage contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessage).
Apple's [NSTextView subclassing guidance](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/TextEditing/Tasks/Subclassing.html)
also warns that raw `keyDown:` overrides should be narrow; this one applies
only to a read-only preview activation key and delegates all other keys to
AppKit rather than trying to replace normal text input handling.

## Remaining target evidence

The Windows result is a local managed apphost check, **not** a four-RID
published Native AOT acceptance. The new external, non-gating
`tests/NativeMacPreviewNavigation.ps1` and
`tests/MacPreviewNavigationProbe/Probe.swift` implement a first target probe.
On both hosted `osx-x64` and `osx-arm64`, they use the published one-Mach-O binary
and an isolated `MOTE_HOME` under repository `.temp/`. For each ordinary
Continuous and `--legacy-page` profile, open a two-heading Markdown fixture,
wait for the native preview to contain the second heading, then use AX and
Quartz to (1) click its actual glyph bounds and
(2) focus preview, move its caret, and press Return/Space. Capture the
source-backed selection/caret through the existing AX source provider, not by
guessing from preview text. The current probe does **not** expose viewport or
generation/version state. Verify exact source bytes, an observable clean-window
state, no mutation after Save/Undo shortcuts, and source focus after activation;
the shortcuts alone do not prove their handlers ran. A **later** real marked-text test must
require no preview navigation or composition loss. Set hard per-stage timeouts
and upload only content-free offsets/status; do not claim success merely from
green `continue-on-error` jobs. A Mac real IME gate remains independent of
this synthetic preview gesture probe.

In [run 36707470968](https://github.com/kleedaisuki/mote/actions/runs/36707470968),
Swift type checking exposed four `CGFloat?` → `Double?` report-field errors;
they were fixed without changing product code. In
[run 36708119143](https://github.com/kleedaisuki/mote/actions/runs/36708119143),
the Swift probe compiled, but `AXEditable` was absent on the standard macOS
preview and LegacyPage lacked an externally identifiable source label. The
navigation diagnostic now preserves `preview_read_only: null` rather than
calling it false or true; a separate in-process AppKit check establishes
`isEditable == false`. LegacyPage's source `NSTextView` gained the fixed
`Mote editor` accessibility label. Neither change asserts VoiceOver speech.

The published one-Mach-O binaries on both `osx-x64` and `osx-arm64` in
[run 36710621980](https://github.com/kleedaisuki/mote/actions/runs/36710621980)
passed the **pointer** case in ordinary Continuous and LegacyPage (four
cases): the source caret moved from 0 to UTF-16 offset 9 and focus returned;
file bytes remained unchanged. The **keyboard** cases (Continuous Return and
LegacyPage Space, two per RID) failed after AX focus: source caret stayed at 0.
These are non-gating failures despite six green strict jobs. A Right-arrow
delivery control is now required before attributing the failure to product
`keyDown:` rather than global-input routing. The exact observations,
ambiguities, and next discriminators are in
[the macOS keyboard diagnostic](mac-preview-keyboard-diagnostics.md). This
probe does not measure viewport, document version, real IME, VoiceOver speech,
or paint latency.

In [run 36712114598](https://github.com/kleedaisuki/mote/actions/runs/36712114598),
the new Right-arrow control moved the preview caret from 9 to **20** (the
end of `Destination`) rather than 10, in both profiles on both Mac RIDs.
Thus keyboard delivery occurred, but not with the intended one-character
semantics; the Enter/Space product verdict remains untested. A Command-like
modifier is one hypothesis, not an established cause. The probe now records
only numeric created/posted key flags and explicitly clears modifiers for
Right Arrow, Return, and Space before posting; the next target run must
confirm its effect before changing the product handler.
