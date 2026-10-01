# Pure theme override composition

Implemented 2026-09-30. Scope: `Mote.Themes` only; this is not evidence of TOML
loading, settings reload, native painting or target-host custom-color acceptance.
It implements the domain contract from [theme override architecture](theme-override-architecture.md).
That design records the production-role precedents and contrast sources; no new
accessibility or performance claim is made here.

## Contracts and ownership

- Existing `IThemePolicy`, built-in IDs, every compiled palette, typography,
  spacing and semantic alias remain unchanged. Theme IDs keep their existing
  case-insensitive registry resolution. Override **role keys are case-sensitive**.
- `ThemeColorRole` contains exactly 28 documented roles: 20 palette roles and
  eight semantic families. `ThemeOverrideData.TryParseRole` uses an exact switch,
  not reflection, generic configuration binding or dynamic theme registration.
- `ThemeOverrideData.TryCreate` accepts raw string pairs so a caller need not
  discard duplicate logical keys before validation. It defensively copies into
  a private finite array. Invalid names, values, duplicate roles (including after
  an invalid first value) or more than 28 entries reject the whole map and return
  `Empty`. Issues follow input enumeration order. Enumeration stops at entry 29,
  yielding at most 29 diagnostics. Invalid role text is bounded to 64 characters.
  Non-string TOML values and duplicate logical TOML paths must be rejected by the
  configuration adapter before supplying the map; the domain does not parse TOML.
- `ThemeComposer.Compose(basePolicy, data)` inherits unspecified colors,
  preserves base metadata and validates every existing `ThemeContrastValidator`
  edge against the complete candidate. Rejection returns the original base
  policy and `CONFIG_THEME_CONTRAST` issues with ratio/minimum. No automatic
  recoloring and no partially installed palette. Contrast details are limited
  to 32 plus an omitted-count summary. Configuration decides startup/reload
  fallback; the domain does not know a previous installed theme.
- Semantic dispatch remains a finite switch. Heading aliases key; code and fenced
  code alias string; boolean/null/emphasis alias keyword; heading/list/sequence/
  fence markers alias marker; invalid aliases error. Unknown semantic kinds use
  the effective editor foreground. Palette diagnostic.error and semantic.error
  remain independently configurable, as in the existing compiled policy model.
- `ThemeEffectiveValues.Capture(policy)` captures `IsDark`, all palette values,
  typography/spacing and eight semantic families as immutable record values.
  Equality deliberately ignores map order, hex case, display name and base ID.
  `IsDark` is included because native chrome can depend on appearance independently
  of RGB values. Two same-ID themes with different colors compare unequal.
  The native coordinator owns a monotonic revision counter and increments it
  only when accepted effective values change; there is no global mutable revision.
- No filesystem, parser, asynchronous execution, UI dependency or platform call
  exists in this layer. Configuration/native integration must not treat a failed
  raw map as an intentionally empty map merely because the out parameter is Empty.
  Always inspect `TryCreate`/`IsValid` before publishing a candidate.

## Example

```csharp
var entries = new KeyValuePair<string, string>[]
{
    new("semantic.key", "#FFFFFF"),
    new("window.background", "#111111")
};
if (ThemeOverrideData.TryCreate(entries, out var requested, out var parseIssues))
{
    var candidate = ThemeComposer.Compose(ThemePolicies.Get("mote-dark"), requested);
    if (candidate.IsValid)
    {
        var identity = ThemeEffectiveValues.Capture(candidate.Theme);
        // Native installation and its revision are owned by the coordinator.
    }
}
```

## Strict color grammar compatibility

`ThemeColor.TryParse` is additive and accepts only opaque ASCII `#RRGGBB`.
`FromHex` keeps null throwing `ArgumentNullException`, invalid syntax throwing
`FormatException`, and unchanged results for every valid literal (upper/lower
hex digits both work). The previous use of `NumberStyles.HexNumber` accidentally
accepted whitespace within a two-character segment, e.g. `# FF000`; it did not
implement its documented exact-six-digit contract. Rejecting those malformed
strings is an intentional contract correction, not a change to supported valid
color inputs. There is no released binary API to migrate; no existing compiled
palette literal changed. No alpha, shorthand, Unicode digits, CSS expressions or
named colors are accepted.

## Verification

Windows local .NET 10, 2026-09-30:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore `
  --filter FullyQualifiedName~ThemeOverrideCompositionTests -warnaserror
dotnet build src/Mote.Themes/Mote.Themes.csproj -c Release --no-restore -warnaserror
```

Results: **22/22** focused tests passed; Themes Release build **0 warnings,
0 errors**. The focused test command compiled the real referenced Configuration,
Engine, Formats, Native and Telemetry projects successfully; no isolated substitute
project was needed. Tests cover all bundled effective-value inheritance, exact
28-role schema, invalid role/color/duplicate/entry cap, defensive ownership,
semantic aliases and fallback, complete contrast rejection including preview
surface, and equality across reordered/case-varied maps versus same-ID changed
colors. This does not constitute Native AOT publication, four-RID validation,
full repository test execution, screen-reader acceptance or custom native theme
installation evidence.
