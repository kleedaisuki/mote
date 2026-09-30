# Theme policy and accessibility decision

Status: static policy module, nonfatal unknown-ID warning, and a committed
native live-`system` implementation under target-host validation. Local Release
build and 409 managed tests pass. Published Win x64 completed a three-state
synthetic OS-preference transition; published Mac x64/ARM64 completed a
three-state **window-local** AppKit transition in both default and Canvas
editor modes. Neither an actual OS-wide light/dark transition nor a theme
switch during a real IME
session has been certified. The Avalonia prototype is not the strict-single-
binary product shell. See [the exact CI artifacts and boundaries](../tests/VALIDATION.md).

## Why a theme policy

The document engine and six format analyzers must not know colors. They produce
source-mapped semantic categories such as `key`, `heading`, `link`, `number`,
and `error`. A bundled `IThemePolicy` converts a category into presentation
without mutating text or analysis. This is the same mechanism/policy split used
for document formats, but themes are **compile-time policies**: there is no
runtime code loading, assembly scanning, or reflection. The only user setting
needed for selection is a stable theme ID in `~/.mote` configuration.

The model separates editor surface, chrome, preview, controls, selection,
diagnostics, and semantic foregrounds rather than treating "dark mode" as one
background plus a collection of special-case token brushes. VS Code's [theme
color reference](https://code.visualstudio.com/api/references/theme-color)
distinguishes editor colors, selection foreground/background, workbench and
diagnostics. JetBrains [color scheme guidance](https://www.jetbrains.com/help/idea/configuring-colors-and-fonts.html)
also distinguishes editor color schemes from the surrounding UI theme and
supports common language defaults. Mote follows these *structural* lessons;
its colors are an original, restrained palette rather than copied proprietary
theme assets. Dark/light surfaces and low-saturation blue, amber and violet
semantic accents should feel familiar to VS Code and JetBrains users without
the earlier teal-heavy appearance.

## Static IDs and configuration

| Config value | Resolution | Intended use |
| --- | --- | --- |
| `system` | OS prefers dark → `mote-dark`; otherwise `mote-light` | Optional preference; the native shell supplies OS state, never the policy module. |
| `mote-dark` | Exact immutable policy | Built-in default: familiar restrained dark editor. |
| `mote-light` | Exact immutable policy | Light editor with explicit contrast checks. |
| `mote-high-contrast-dark` | Exact immutable policy | Stronger text and boundary contrast. |
| syntactically valid unknown ID | Same as `system` | Native composition adds `CONFIG_THEME` and shows one actionable warning after the window opens. |
| empty or invalid ID | Built-in `mote-dark` | Config loader retains its default and reports `CONFIG_VALUE`. |

The config layer owns `~/.mote` path conventions and any user-editable file.
`Mote.Themes` has no filesystem paths or side effects. Custom color overrides
would need a typed, validated *data* layer applied to a base policy; they must
not become runtime plugin DLLs or silently bypass the contrast contract.
`MoteConfigLoader` validates the syntax of a theme ID without depending on a
theme registry. `Mote.Native.Program.ValidateThemeId` then uses
`ThemePolicies.IsKnownId` at the composition boundary. For example,
`appearance.theme = 'mote-drak'` appends a nonfatal `CONFIG_THEME` diagnostic,
preserves any other config warnings, and leaves `Resolve`'s safe OS light/dark
fallback intact. The native status bar counts the diagnostic; because that
count alone cannot reveal its text, the composition root also posts the
actionable message once after the real GUI window is shown. The automatic
`--smoke-gui` path skips the modal so diagnostics cannot hang startup probes.
The warning names `config.toml` and the four supported IDs. This avoids
duplicating the registry in the configuration module. Focused Release tests
cover all known IDs (including `system` and case-insensitivity), a valid typo,
diagnostic preservation, and fallback (**7/7 passed**). The modal's native
visual presentation has not yet been independently exercised on both OSes.

## Contrast contract and important limits

`ThemeContrastValidator` uses the [WCAG 2.2 contrast ratio
formula](https://www.w3.org/TR/WCAG22/#dfn-contrast-ratio): normal text pairs
must reach 4.5:1; essential non-text boundaries and caret must reach 3:1
([text criterion](https://www.w3.org/TR/WCAG22/#contrast-minimum),
[non-text criterion](https://www.w3.org/TR/WCAG22/#non-text-contrast)). Checked
pairs include button and active-tab labels against their own surfaces, selected
text against selection, semantic colors against both ordinary and active-line
backgrounds, and preview/status/gutter text. In particular, **selected text
must use the selection foreground instead of retaining semantic foregrounds**.
That is a UI adapter invariant, not something palette contrast alone enforces.

The validator is a static safeguard, not a substitute for screenshots and
platform testing: inherited Avalonia styles, hover/disabled states, DPI, font
weight, anti-aliasing and macOS/Windows high-contrast OS settings can alter the
real presentation. A UI test should inspect rendered buttons, tabs and editor
selection in each bundled theme. Product accessibility also includes keyboard,
IME and screen reader behavior, which this module cannot establish.

## Integration and performance

The native composition root resolves the policy once per theme change; adapters
should create/reuse UI brushes for the 20 palette roles and known semantic
roles, and invalidate only paint/layout as needed. It must not parse files or
allocate a brush per token.
`SemanticColor(kind)` is a direct switch and unknown kinds return default
editor text, so new semantic roles remain legible even before a palette update.
Theme data is immutable and safe to share across document windows. Typography
lists platform fallbacks; it does not bundle Inter or any proprietary font into
the strict one-binary deployment.

Verification: `dotnet test tests/Mote.Themes.Tests/Mote.Themes.Tests.csproj`
tests all three policies, system resolution, malformed colors, contrast pairs,
synthetic dark-on-dark control failure, and a preview-surface semantic-color
failure. At the latest focused run, **14/14 tests passed**. This is a palette
contract test, not a native rendering or live-theme-switch test.

## Native live-appearance implementation and acceptance (source audit, 2026-09-30)

`Mote.Native.Program` validates the theme ID, samples `shell.PrefersDark`, and
passes the resolved policy to `NativeEditorController`. The controller now holds
a replaceable `_theme`, the configured ID, and a coalesced `_pendingTheme`.
`INativeEditorShell` has `AppearanceChanged`, `CompositionSettled`,
`IsTextComposing`, and `SetStatusNotice`. On an appearance event the controller
re-queries the OS and resolves **every** ID. This makes explicit IDs naturally
no-ops; `system` and a syntactically valid unknown-ID fallback track OS
light/dark. Repeated notifications with the same effective policy do not call
`SetTheme`. A nested appearance callback during `SetTheme` is re-resolved after
the current application using `_appearanceSerial`, rather than losing the
latest requested policy.

| Boundary | Implemented source behavior | Evidence and remaining acceptance |
| --- | --- | --- |
| Windows preference | `WindowsEditorShell.PrefersDark` reads per-user `AppsUseLightTheme`, documented by [Microsoft's settings reference](https://learn.microsoft.com/en-us/windows/apps/develop/settings/settings-common). The window procedure handles `WM_SETTINGCHANGE`, `WM_SYSCOLORCHANGE`, and `WM_THEMECHANGED`, then re-queries the preference and emits `AppearanceChanged` only when its effective Boolean changes. This avoids assuming a particular `lParam` string: [Microsoft's message contract](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-settingchange) permits a null/broad value; [`WM_SYSCOLORCHANGE`](https://learn.microsoft.com/en-us/windows/win32/gdi/wm-syscolorchange) covers system-color changes. | The published win-x64 HWND passed synthetic hosted HKCU dark→light→dark with exact editor background, matching glyph-pixel counts, stable selection/text and three PNGs; the original HKCU value/kind were restored. This is not a user operating Windows Settings, win-arm64 theme evidence, or an IME-in-progress test. |
| macOS preference | `MacEditorShell.PrefersDark` reads the visible editor's [`effectiveAppearance`](https://developer.apple.com/documentation/appkit/nsapplication/effectiveappearance) and uses [`bestMatchFromAppearancesWithNames:`](https://developer.apple.com/documentation/AppKit/NSAppearance?language=objc) for Aqua/DarkAqua classification. The default editor subclasses `NSTextView` to receive Apple's [`viewDidChangeEffectiveAppearance`](https://developer.apple.com/documentation/appkit/nsview/viewdidchangeeffectiveappearance%28%29?language=objc); Canvas samples its source-rendering `NSView` and also observes its input child callback, deduplicating both after startup. | Both published Mach-O RIDs passed process-local window DarkAqua→Aqua→DarkAqua in both editor modes with three callbacks, exact preview/default-editor heading RGB, stable G/V and selection, Undo/Redo and changed/returned PNG hashes. Actual macOS system Auto/Light/Dark changes, background-window changes and real IME remain separate acceptance work. |
| IME and palette transaction | `SetTheme` preflights `IsTextComposing`; a late native preedit can throw the typed `NativeThemeDeferredException` **before mutation**. Controller retains only the latest policy and retries on `CompositionSettled`. Windows updates its canvas island, surfaces and cached semantic colors with rollback attempts on failure. Mac applies the canvas island first, then the editor/preview theme and cached analysis. A failed update retains the canonical source and exposes a persistent, nonmodal “Theme update unavailable” status notice; successful retry clears it. | Fake-shell tests cover commit/cancel coalescing, late preedit, nested callbacks, rollback notice and source/selection/undo invariants. They cannot establish the real RichEdit/NSTextView/IME ordering, candidate position, or all-or-nothing paint on a target OS. Exercise Chinese and Japanese IME with an OS appearance change during marked text; verify one final palette, no preedit leak or extra edit, stable candidate/selection and no modal warning. |
| Typed document/analysis identity | `NativeDocumentStamp(Generation, Version)` accompanies bounded document and analysis views. The generation distinguishes a replaced document that reuses a numeric text version. Both shells reject analyses whose stamp does not match their current document/canvas binding. A theme change recolors only cached *matching* analysis; otherwise it paints base text rather than showing an old token palette. `SelectPolicy` explicitly clears `_visibleSessionAnalysis` and publishes an empty “Format analysis pending; global diagnostics unknown” projection before Save As to a different format, because format policy can change without changing G/V. | Fake-shell tests cover same-version Save As JSON→Markdown in default and canvas modes and a same-format Save As. They demonstrate stale semantic/cache clearance at the controller boundary; they do not prove visual absence of a stale frame in a published OS binary. |
| Selection, chrome and contrast mode | Windows/macOS canvas painters explicitly use `SelectionBackground` and `SelectionForeground`. Default Win32 RichEdit and AppKit `NSTextView` still rely on native selection presentation, not necessarily the policy pair. Win32 now themes its status `STATIC` via cached `WM_CTLCOLORSTATIC` brush and, on supported Windows 11, its title via documented DWM caption/text attributes; an active OS high-contrast setting takes system colors. The native Win32 **menu remains system-colored** rather than using undocumented theme hooks. AppKit [`selectedTextAttributes`](https://developer.apple.com/documentation/appkit/nstextview/selectedtextattributes) can control selected text/background if exact palette matching is required. Explicit `mote-high-contrast-dark` is static; `system` still resolves only dark/light. | Local real-HWND dark/light/high-contrast screenshots showed exact status/title pixel colors and a stable status brush; [CI 36675182046](https://github.com/kleedaisuki/mote/actions/runs/36675182046) then passed hosted Win11 x64 dark→light→dark status/caption pixels, source/selection and HKCU restoration. This does not certify native menu contrast, system high-contrast activation, selection colors or Mac cached status compositing. Static palette contrast tests do not replace those checks. |

The implementation deliberately does **not** reparse or replace canonical text
on a palette change. Fake-shell tests assert unchanged document page/canvas
binding, G/V stamp, source bytes, selection, viewport, horizontal position,
analysis publication count, and undo/redo. The latest local verification
reported by the root engineer is a Native Release warnings-as-errors build
with **0 warnings / 0 errors** and a full managed solution test run of
**Mote.Tests 386/386, Mote.Themes.Tests 14/14, Mote.Configuration.Tests 9/9**.
Those managed source-level tests (including fake-shell theme tests) and policy
tests alone could **not** prove published Win32 or Mach-O GUI appearance
transitions. In [CI run 36607770262](https://github.com/kleedaisuki/mote/actions/runs/36607770262),
all six strict build/test jobs passed, but the separate non-gating theme
reports failed. Follow-up [CI run 36671152190](https://github.com/kleedaisuki/mote/actions/runs/36671152190)
showed that the default Mac probe had sampled the leading Markdown marker:
both architectures read `#9CC6E8` on the heading's interior and final glyphs,
while its first marker remained base `#D8DADF`. Canvas reported a dark editor
appearance but an unchanged light policy, consistent with the canvas ancestor
callback preceding child-view appearance propagation. The Windows harness had
first hit a PowerShell `$HOME` name collision and then a runtime ValueTuple
`.Start` property error after launching mote; both hosted reports record exact
HKCU restoration but no color case. In [CI run 36671876359](https://github.com/kleedaisuki/mote/actions/runs/36671876359),
the Win x64 published HWND then passed dark→light→dark background/glyph-pixel,
selection, text, PNG and registry-restoration checks. The default Mac editor
passed the corresponding window-local three-state native-color, callback,
source/selection, Undo/Redo and raster checks on both RIDs. Mac Canvas initially
reached the native edit, but its probe incorrectly expected a character
appended to the *one-line input host* to appear at the end of a multiline file.
With an exact source-offset oracle, [CI run 36672506098](https://github.com/kleedaisuki/mote/actions/runs/36672506098)
then passed **both default and Canvas modes on both Mac RIDs**: each recorded
three appearance callbacks, exact preview accent transitions, unchanged G/V
and selection across palettes, exact Undo/Redo/source SHA and distinct light
versus dark PNG hashes. The Canvas probe does not read source-glyph pixels from
the Core Text raster, and none of these tests changes global macOS appearance,
drives a physical keyboard/IME or observes compositor presentation. The unknown-ID
`CONFIG_THEME` warning
has focused tests; its real native modal has not been visually exercised on
both OSes. A release claim for live `system` and IME safety still requires the
target-host workflow in the table, ideally with before/after screenshots and
exact source/version/selection checks rather than an “event fired” assertion.

Visual inspection of the osx-arm64 **content-view cache** PNGs in
`.cache/ci-run-36672506098-theme/osx-arm64/` initially suggested a black
bottom status strip. Direct PNG pixel inspection corrected that interpretation:
the sampled blank status pixels are **ARGB `00000000` (fully transparent)**,
while the editor and preview pixels are opaque policy colors. The image viewer
displayed transparency as black; this is **not evidence of a black on-screen
status strip**, because the actual `NSWindow` backing is omitted from this
view-only cache. A first attempt to set `NSWindow.backgroundColor` still left
the cached pixel transparent in [CI 36673812520](https://github.com/kleedaisuki/mote/actions/runs/36673812520).
A decorative policy-backed status view was then added, but [CI 36674427007](https://github.com/kleedaisuki/mote/actions/runs/36674427007)
sampled `#2B2C30` rather than the expected dark `#202124` in both Mac modes.
The later [CI 36675805214](https://github.com/kleedaisuki/mote/actions/runs/36675805214)
retained PNGs before the same assertion: **both** RIDs' raw bitmap pixels at
the blank bottom status region were opaque `FF202124` (the exact dark policy),
while `NSBitmapImageRep colorAtX:y` followed by `colorUsingColorSpace:sRGB`
reported `#2B2C30`. The decorative view's draw callback ran without fault;
the mismatch is in the probe's color-space measurement, **not** evidence that
the cached status surface painted the wrong color. After switching to Apple's
raw `getPixel:atX:y:` samples, [CI 36676668596](https://github.com/kleedaisuki/mote/actions/runs/36676668596)
passed **both Mac RIDs and both editor modes** again: cached status RGB was
opaque `#202124 → #F1F2F4 → #202124`, alongside the earlier exact
preview/editor colors, G/V, selection, Undo/Redo and fixture SHA checks. The
arm64 light PNG was visually inspected and its status text is legible within
the cached content view. An external on-screen window crop would still be
needed to claim compositor-visible chrome contrast, and a real IME/global OS
transition remains outside this probe.

### Repeated Windows live-theme probe precondition failure (2026-10-01)

Independent raw-log/artifact triage of [CI 36791254056](https://github.com/kleedaisuki/mote/actions/runs/36791254056) at `f657c003c8bb2d52fd94ea632335a356c2f3d445` found the win-x64 non-gating live-theme step (job step 25) exited **1**, although its `continue-on-error` conclusion and enclosing Native AOT job were `success`. The downloaded `native-theme-win-x64` report says `status=failed`, `stage=launch`, `error=launch: RuntimeException: Synthetic RichEdit selection was not established.`, and `cases=[]`. It separately records `registry_restored=true` and `source_sha256_unchanged=true`. No dark/light color case was reached, so this run provides neither a live-theme pass nor a color-transition defect reproduction.

The immediately preceding [CI 36789139005](https://github.com/kleedaisuki/mote/actions/runs/36789139005) has the **same** launch-stage selection failure, empty cases and successful restoration/hash flags, with a different published executable SHA. Thus this is not a newly introduced failure of the latest Mac Grid proxy/Windows identity-probe commit. It is a repeated unresolved Windows probe precondition failure; do not erase the historical successful scoped theme evidence or call the current probe a pass.

The harness waits only for a top-level title containing `theme.txt`, retrieves child ID 101, sends `EM_SETSEL(1,3)` once, and immediately asserts the returned selection. It does not record the actual returned pair, child class, source content at failure, or current native source mode. A title is not an independent content/selection-ready assertion. These missing observations prevent a supported distinction between a readiness/host-assumption problem and a product selection defect. The next useful probe should record those observations and establish the exact synthetic source/appropriate host before selecting; adding retries without that discriminator would hide rather than diagnose the failure.

#### Content-free launch discriminator (pending hosted evidence)

The Windows harness now records `launch_observation` after the **same single**
`EM_SETSEL` attempt and the existing immediate text/selection readback, before
the unchanged selection assertion. It records the actual/expected selection
pair, child ID/class/visibility, the presence of a direct
`MoteInteractiveCanvas` sibling, a readiness-phase label, and monotonic elapsed
milliseconds at title readiness and selection readback. The observed host mode
is `visible-legacy-editor`, `canvas-with-hidden-legacy-editor`, or
`unclassified`; it is an HWND-structure observation, not an inference that
the document is fully ready.

The existing 256-unit text read contributes only a bounded UTF-16 length,
a capacity-reached flag (255 units), and exact-equality booleans for the
known synthetic `alpha`/`beta` fixture with LF, CRLF, or CR line endings.
**No source string or arbitrary window caption is serialized.** A capacity-
reached result is not a document-length measurement. These fields can distinguish
an empty/not-yet-projected control or an inactive legacy control from a
fully populated visible control that rejected the expected selection. They
do not by themselves prove a readiness race or a product defect.

No retry, selection routing, additional readiness wait, registry operation,
palette assertion, or product code changed. The hosted-runner restriction and
exact HKCU restoration remain intact. The owned workflow step has no source
hash pin to update. A later hosted run must supply the discriminator result;
the earlier repeated failure remains unresolved until then.

Local non-mutating verification on Windows, PowerShell 7:

```powershell
pwsh -NoProfile -File tests/Test-NativeThemeWorkflowWindowsObservability.ps1
```

Result: **PASS** for PowerShell AST parsing, compilation of the embedded C#
native declarations, single-attempt/readback ordering and hosted-runner guard,
and three synthetic metadata contracts (visible legacy/full CRLF sentinel,
hidden legacy/Canvas/empty, and unclassified/capacity-reached). The test never
starts mote, invokes the Win32 declarations, or reads/writes HKCU. It verifies
the report contract, not the published target's live-theme behavior.

Reproduction and retained evidence (repository-local):

```powershell
gh api repos/kleedaisuki/mote/actions/jobs/110144423689/logs > .cache/ci-36791254056-triage/win-x64.log
gh run download 36791254056 --name native-theme-win-x64 --dir .cache/ci-36791254056-triage/theme
gh run download 36789139005 --name native-theme-win-x64 --dir .cache/ci-36791254056-triage/prior-theme
```

The same current job's other exit **1** (step 37, baseline external UIA canvas client) is the established duplicate-Document release blocker, not a new range-behavior failure: its artifact reports **19/19** checks passed, zero `Failures`, one `ReleaseBlockers`, zero `Inconclusive`, and Raw/Control/Content counts **2/2/2**. Source text has 153,020 UTF-16 units while the RichEdit input host exposes 16; global focus is the bounded RichEdit Document with mote foreground. This repeats the baseline/opt-in-fragment distinction already documented in [release-gaps.md](release-gaps.md), and does not invalidate the separate CSV Grid provider checks. Exact artifact: `.cache/ci-36791254056-triage/uia/report.json`. No product code or validation harness was changed by this audit.

#### Hosted discriminator result and explicit legacy correction

[CI 36792454502](https://github.com/kleedaisuki/mote/actions/runs/36792454502) at `0b85a0e` supplied the missing evidence. The win-x64 live-theme process again exited **1** at `stage=launch`, `cases=[]`, with `registry_restored=true` and `source_sha256_unchanged=true`. Its published binary SHA-256 was `6B18BED6897FF2EF6C91F1081D86133E903BB5089957C13DD85BF8B836D1FBE5`. The content-free metadata reports:

| Observation | Actual |
| --- | --- |
| Requested selection / readback | 1..3 / **0..0** |
| Child ID/class/visibility | 101 / `RICHEDIT50W` / **false** |
| Direct Canvas sibling / observed mode | true / `canvas-with-hidden-legacy-editor` |
| Bounded text length / capacity reached | **0** / false |
| Exact synthetic LF/CRLF/CR match | false / false / false |
| Title readiness / selection readback | 145 ms / 156 ms |

This identifies an inactive **hidden empty legacy control**, not a populated visible source host that refused selection. The program's documented default is Continuous; `WindowsEditorShell` hides `_editor` when the Canvas mode is installed and its Canvas branch does not project text into that legacy control. The historical RichEdit-specific theme harness omitted `--legacy-page`. Its repeated failure is therefore a **probe mode/host mismatch**, not evidence that the default Canvas selection or palette implementation is defective. The timing values are observations, not evidence of a readiness race.

The harness correction now explicitly launches `--legacy-page`, declares `source_mode=legacy-page` and legacy-only scope in its JSON, and accepts only a visible legacy host whose bounded text exactly matches the disposable fixture (permitted native newline forms). It retains the single selection attempt, original color/source/selection assertions, hosted-only guard, fresh repository-local synthetic home/fixture, unchanged HKCU try/finally restoration, and no arbitrary content serialization. It does **not** route selection to child 301 or claim default Canvas coverage.

Local regression command `pwsh -NoProfile -File tests/Test-NativeThemeWorkflowWindowsObservability.ps1` first failed on the original harness because explicit legacy launch was absent, then passed after the correction. It compiles the embedded native helper, checks AST/scope/ordering, and independently exercises acceptance of the visible populated legacy fixture versus rejection of hidden-empty Canvas and unclassified-capacity cases. This local test neither launches mote nor touches HKCU. **The corrected published legacy workflow still requires a subsequent hosted run**; CI 36792454502 predates the correction.

Default Windows Canvas live-theme coverage remains a separate open target-host gate. Existing Continuous open/edit/Save probes do not assert dark→light→dark palette transitions. A bounded future probe should launch the ordinary default mode, establish exact source/active input ownership, sample policy-backed Canvas/status pixels on each transition, and check source plus global selection/history preservation through the source-backed controller contract. The bounded RichEdit input island is not a full-source legacy editor and must not be substituted into the old pixel/selection assertions. Real IME and physical presentation/screen-reader claims remain separate.

Evidence: `.cache/ci-36792454502-triage/win-x64.log` and `theme/ci-inventory/win-x64/native-theme.json`. The baseline external UIA artifact in the same directory has unchanged **19/19** behavior checks, zero failures, one known duplicate-Document release blocker and zero inconclusive cases; no new UIA regression was found. Download with `gh run download 36792454502 --name native-theme-win-x64 --dir .cache/ci-36792454502-triage/theme` (baseline UIA artifact name `windows-ax-external-win-x64`).
