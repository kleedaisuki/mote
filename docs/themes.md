# Theme policy and accessibility decision

Status: implemented policy module; desktop application of every role is a
separate integration concern.

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
| unknown/empty | Same as `system` | Nonfatal config error; config loader should warn. |

The config layer owns `~/.mote` path conventions and any user-editable file.
`Mote.Themes` has no filesystem paths or side effects. Custom color overrides
would need a typed, validated *data* layer applied to a base policy; they must
not become runtime plugin DLLs or silently bypass the contrast contract.
The desktop can use `ThemePolicies.IsKnownId` to surface a configuration typo
while still continuing with the safe fallback.

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

The desktop should resolve the policy once per theme change, create/reuse UI
brushes for the 20 palette roles and known semantic roles, and invalidate only
paint/layout as needed. It must not parse files or allocate a brush per token.
`SemanticColor(kind)` is a direct switch and unknown kinds return default
editor text, so new semantic roles remain legible even before a palette update.
Theme data is immutable and safe to share across document windows. Typography
lists platform fallbacks; it does not bundle Inter or any proprietary font into
the strict one-binary deployment.

Verification: `dotnet test tests/Mote.Themes.Tests/Mote.Themes.Tests.csproj`
tests all three policies, system resolution, malformed colors, contrast pairs,
and a synthetic dark-on-dark control failure.
