# Mote.Themes

`Mote.Themes` is the statically linked, UI-neutral presentation policy module. It
does not depend on `Mote.Desktop`, Avalonia, platform APIs, file I/O, or runtime
plugin discovery. A format policy emits semantic token kinds; a theme policy
maps those kinds and application chrome roles to opaque RGB values. The desktop
composition root converts `ThemeColor` to native brushes.

```csharp
var theme = ThemePolicies.Resolve(config.ThemeId, osPrefersDark);
var editorBackground = theme.Palette.EditorBackground.ToHex();
var keyForeground = theme.SemanticColor("key").ToHex();
```

The immutable built-in IDs are `mote-dark` (the config default), `mote-light`, and
`mote-high-contrast-dark`. `system` is a *configuration preference*, not a
mutable fourth policy: `Resolve` chooses the dark/light policy using the OS
appearance value supplied by its caller. Unknown IDs fall back the same way,
so an invalid user setting cannot prevent the editor from opening; the config
layer should issue a diagnostic using `ThemePolicies.IsKnownId`.
`ThemePolicies.Get` uses dark as fallback when
the OS appearance is unavailable.

The adapter must apply `ControlForeground` to buttons and active tabs and
`SelectionForeground` to selected text even when syntax colors are present.
Inheriting the token foreground over `SelectionBackground` can make a formally
valid palette unreadable. The color contrast validator checks text against the
ordinary editor surface and active-line surface, preview, panel, controls,
selection, and essential non-text boundaries. Passing these checks is **not**
a claim of complete UI accessibility; platform focus, disabled states, dialogs,
screen reader semantics and applied styles still need UI-level validation.

The font family strings are ordered platform fallbacks, not bundled font assets.
No font is required to be installed for the policy module to work. Typography
and spacing are policy tokens rather than hard-coded view values.

Run `dotnet test tests/Mote.Themes.Tests/Mote.Themes.Tests.csproj` for registry,
resolution, color serialization and contrast contracts.
