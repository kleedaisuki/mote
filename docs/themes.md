# Theme policy and accessibility decision

Status: implemented static policy module and nonfatal unknown-ID warning. Both
native shells resolve the policy at startup, but live `system` appearance
updates and full native chrome/selection application are not yet demonstrated.
The Avalonia prototype is not the
strict-single-binary product shell.

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
| `system` | OS prefers dark → `mote-dark`; otherwise `mote-light` | Optional preference; OS state is supplied by desktop, never read inside policy. |
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

The native composition root should resolve the policy once per theme change, create/reuse UI
brushes for the 20 palette roles and known semantic roles, and invalidate only
paint/layout as needed. It must not parse files or allocate a brush per token.
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

## Native runtime integration audit and acceptance (2026-09-29)

This is source inspection of the current `Mote.Native` tree, not a target-host
run of operating-system appearance transitions. `Program` loads and validates
the user configuration, samples `shell.PrefersDark`, calls
`ThemePolicies.Resolve`, and
passes one immutable policy to `NativeEditorController`. The controller keeps
`_theme` readonly and calls `_shell.SetTheme(_theme)` when shown. Neither shell
currently publishes an appearance-change event back to the controller. Thus
`[appearance] theme = 'system'` chooses a correct palette **at startup only**;
flipping OS light/dark while mote remains open will not update the editor.
Explicit `mote-dark`, `mote-light`, and `mote-high-contrast-dark` must remain
stable across OS changes.

| Area | Observed implementation | Acceptance needed |
| --- | --- | --- |
| Windows preference | `WindowsEditorShell.PrefersDark` reads per-user `AppsUseLightTheme` once for initial resolution. This value is documented by [Microsoft's settings reference](https://learn.microsoft.com/en-us/windows/apps/develop/settings/settings-common). | On OS appearance changes, re-query on the UI thread and emit only a changed effective preference. Do not depend solely on an undocumented `WM_SETTINGCHANGE` `lParam` string: Microsoft's [message contract](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-settingchange) says it may be null or broad. Include [`WM_SYSCOLORCHANGE`](https://learn.microsoft.com/en-us/windows/win32/gdi/wm-syscolorchange) for system-color changes. |
| macOS preference | `MacEditorShell.PrefersDark` reads `NSApplication.effectiveAppearance.name` and checks for `Dark`. Apple documents [`effectiveAppearance`](https://developer.apple.com/documentation/appkit/nsapplication/effectiveappearance) as the actual drawing appearance. | Observe a view/window effective-appearance change, e.g. documented [`viewDidChangeEffectiveAppearance`](https://developer.apple.com/documentation/appkit/nsview/viewdidchangeeffectiveappearance%28%29?language=objc), and re-resolve on AppKit's main thread. For classification, [`bestMatchFromAppearancesWithNames:`](https://developer.apple.com/documentation/AppKit/NSAppearance?language=objc) is more robust than substring matching when custom/vibrant appearance names are present. |
| Composition safety | Windows canvas island throws if `SetTheme` arrives while RichEdit IME composition is active. macOS canvas shell defers a supplied theme while marked text exists, replaying it after composition settles; neither path is currently triggered by OS appearance changes. | Never rebind the input island, replace marked text, move the candidate window, or call a throwing theme setter synchronously during preedit. Coalesce the newest pending appearance, apply it once after commit/cancel on the UI thread, and preserve exact document bytes, version, undo history, source selection and viewport position. Exercise actual Chinese/Japanese IME, not only synthetic key messages. |
| Selection and chrome | Windows/macOS canvas painters explicitly use `SelectionBackground` and `SelectionForeground`. Default Win32 RichEdit and AppKit `NSTextView` rely on native selection presentation; they do not currently apply the policy pair. Native Win32 `SetTheme` applies editor/preview surfaces and font, not all `Control*`/panel colors. AppKit [`selectedTextAttributes`](https://developer.apple.com/documentation/appkit/nstextview/selectedtextattributes) can set selected text/background if product policy requires exact palette matching. | Verify selected syntax text remains readable in every palette and OS mode, including focused/unfocused selection and active IME. Either apply the policy pair correctly or deliberately rely on OS colors with a measured contrast check; do not claim the static pair is what RichEdit/NSTextView paints. Inspect native controls, menus, preview and status separately; palette unit tests cannot certify inherited native styles. |
| OS contrast mode | Explicit `mote-high-contrast-dark` is static. `system` currently resolves only dark/light and does not query the OS contrast preference. | Do not treat the bundled high-contrast preset as proof of respecting a user's Windows contrast theme. Microsoft directs Win32 apps to query [`SPI_GETHIGHCONTRAST`](https://learn.microsoft.com/en-us/windows/win32/winauto/high-contrast-parameter) at initialization and on `WM_SYSCOLORCHANGE`, then use actual system foreground/background colors. Decide whether `system` gains a validated OS-color overlay; test with a user-customized contrast palette, not just the built-in preset. |

Recommended small interface evolution: retain `IThemePolicy` as immutable data;
add a UI-thread shell appearance-change notification and let the controller keep
the configured preference plus a replaceable *resolved* policy. Only `system`
re-resolves after an OS transition. The shell must defer the visual transaction
until composition is safe, then update canvas painter/geometry, native input
host, preview and chrome together, without reanalysis or a document edit.
Make an unchanged effective policy a no-op. A real Windows and macOS acceptance
run should switch OS appearance twice during an open file, repeat once during
IME preedit, capture before/after screenshots and exact text/selection/version
checks, and verify explicit IDs ignore the OS transition. The unknown-ID
warning's real native dialog remains an independent startup acceptance case.
