# Theme policy and accessibility decision

Status: static policy module, nonfatal unknown-ID warning, and a **new,
uncommitted native live-`system` implementation**. Controller/fake-shell tests
and local managed build pass; a published Windows EXE or macOS Mach-O has **not**
yet been observed through a real OS light/dark transition or real IME session.
Full native chrome/selection application remains a separate visual question.
The Avalonia prototype is not the strict-single-binary product shell.

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
| Windows preference | `WindowsEditorShell.PrefersDark` reads per-user `AppsUseLightTheme`, documented by [Microsoft's settings reference](https://learn.microsoft.com/en-us/windows/apps/develop/settings/settings-common). The window procedure handles `WM_SETTINGCHANGE`, `WM_SYSCOLORCHANGE`, and `WM_THEMECHANGED`, then re-queries the preference and emits `AppearanceChanged` only when its effective Boolean changes. This avoids assuming a particular `lParam` string: [Microsoft's message contract](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-settingchange) permits a null/broad value; [`WM_SYSCOLORCHANGE`](https://learn.microsoft.com/en-us/windows/win32/gdi/wm-syscolorchange) covers system-color changes. | Source and fake-shell tests cover event/coalescing logic; a published Win32 EXE has not been driven through an actual Windows Settings light→dark→light switch in this change. Capture screenshots and verify each effective palette, selection, status, open text and undo history on a target host. |
| macOS preference | `MacEditorShell.PrefersDark` reads the visible editor's [`effectiveAppearance`](https://developer.apple.com/documentation/appkit/nsapplication/effectiveappearance) and uses [`bestMatchFromAppearancesWithNames:`](https://developer.apple.com/documentation/AppKit/NSAppearance?language=objc) for Aqua/DarkAqua classification. The default editor subclasses `NSTextView` to receive Apple's [`viewDidChangeEffectiveAppearance`](https://developer.apple.com/documentation/appkit/nsview/viewdidchangeeffectiveappearance%28%29?language=objc); the canvas view forwards its effective-appearance callback. The shell emits only changed preferences after startup. | Source inspection confirms callback wiring, not that a published Mach-O receives the expected callbacks under macOS's actual Auto/Light/Dark transitions. Test both editor modes on arm64 and x64 target hosts, including appearance changes while the window is active and inactive. |
| IME and palette transaction | `SetTheme` preflights `IsTextComposing`; a late native preedit can throw the typed `NativeThemeDeferredException` **before mutation**. Controller retains only the latest policy and retries on `CompositionSettled`. Windows updates its canvas island, surfaces and cached semantic colors with rollback attempts on failure. Mac applies the canvas island first, then the editor/preview theme and cached analysis. A failed update retains the canonical source and exposes a persistent, nonmodal “Theme update unavailable” status notice; successful retry clears it. | Fake-shell tests cover commit/cancel coalescing, late preedit, nested callbacks, rollback notice and source/selection/undo invariants. They cannot establish the real RichEdit/NSTextView/IME ordering, candidate position, or all-or-nothing paint on a target OS. Exercise Chinese and Japanese IME with an OS appearance change during marked text; verify one final palette, no preedit leak or extra edit, stable candidate/selection and no modal warning. |
| Typed document/analysis identity | `NativeDocumentStamp(Generation, Version)` accompanies bounded document and analysis views. The generation distinguishes a replaced document that reuses a numeric text version. Both shells reject analyses whose stamp does not match their current document/canvas binding. A theme change recolors only cached *matching* analysis; otherwise it paints base text rather than showing an old token palette. `SelectPolicy` explicitly clears `_visibleSessionAnalysis` and publishes an empty “Format analysis pending; global diagnostics unknown” projection before Save As to a different format, because format policy can change without changing G/V. | Fake-shell tests cover same-version Save As JSON→Markdown in default and canvas modes and a same-format Save As. They demonstrate stale semantic/cache clearance at the controller boundary; they do not prove visual absence of a stale frame in a published OS binary. |
| Selection, chrome and contrast mode | Windows/macOS canvas painters explicitly use `SelectionBackground` and `SelectionForeground`. Default Win32 RichEdit and AppKit `NSTextView` still rely on native selection presentation, not necessarily the policy pair. Native Win32 applies editor/preview surfaces and fonts but not every `Control*`/panel color. AppKit [`selectedTextAttributes`](https://developer.apple.com/documentation/appkit/nstextview/selectedtextattributes) can control selected text/background if exact palette matching is required. Explicit `mote-high-contrast-dark` is static; `system` still resolves only dark/light. | Inspect selected semantic text, focused/unfocused controls, menu, preview and status in all three palettes. Static contrast tests do not certify native controls. A Windows user-customized contrast theme requires a separate decision: Microsoft recommends [`SPI_GETHIGHCONTRAST`](https://learn.microsoft.com/en-us/windows/win32/winauto/high-contrast-parameter) and the user's actual system colors, not merely a bundled preset. |

The implementation deliberately does **not** reparse or replace canonical text
on a palette change. Fake-shell tests assert unchanged document page/canvas
binding, G/V stamp, source bytes, selection, viewport, horizontal position,
analysis publication count, and undo/redo. The latest local verification
reported by the root engineer is a Native Release warnings-as-errors build
with **0 warnings / 0 errors** and a full managed solution test run of
**Mote.Tests 386/386, Mote.Themes.Tests 14/14, Mote.Configuration.Tests 9/9**.
Those are managed source-level tests (including fake-shell theme tests) and
policy tests, **not** published Win32 or Mach-O GUI appearance-transition
tests. The unknown-ID `CONFIG_THEME` warning
has focused tests; its real native modal has not been visually exercised on
both OSes. A release claim for live `system` and IME safety still requires the
target-host workflow in the table, ideally with before/after screenshots and
exact source/version/selection checks rather than an “event fired” assertion.
