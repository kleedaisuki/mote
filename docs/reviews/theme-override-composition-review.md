# Theme override composition review

Date: 2026-09-30. Verdict: **no substantive issue found in the reviewed domain draft**.
This is a source review, not target-host custom-theme acceptance.

## Scope and evidence

Reviewed the frozen draft of:

- `src/Mote.Themes/ThemeColor.cs`
- `src/Mote.Themes/ThemeOverrides.cs`
- `tests/Mote.Tests/ThemeOverrideCompositionTests.cs`
- `docs/theme-override-composition.md`

Read the existing `ThemeContracts.cs`, `ThemePolicies.cs`,
`ThemeContrastValidator.cs`, `Mote.Themes.csproj`, `docs/themes.md` and
`docs/theme-override-architecture.md` as contracts and surrounding context.
Inspected repository references to the new types: at review time they existed
only in Themes and its focused tests, not Configuration or Native callers.
The existing contrast validator has no diff in this draft.

The implementation author's recorded 22/22 focused Release tests and zero-warning
Themes build were not rerun: there was no failure evidence pointing back to them.
No production/test file was modified and no changes were staged or committed.

## Findings

No necessary correction identified. Confidence is high for the finite mapping,
validation and value-identity logic under the existing immutable, compile-time
`IThemePolicy` contract; deployment and integration confidence is explicitly
outside this review.

| Concern | Source assessment |
| --- | --- |
| Closed vocabulary | Manually compared the 20 palette assignments and eight semantic families to the architecture and existing palette properties. The enum, exact case-sensitive key switch, array indices and 28-entry cap agree. Invalid typed enum values are checked before indexing. |
| Whole-map rejection | Any unknown/null role, invalid/null color, duplicate logical role or excessive entry produces `Empty` and false. A valid role is marked seen before color validation, so an invalid first color does not conceal a duplicate. Composition separately returns the original base policy after contrast rejection. |
| Bounded work and diagnostics | Input enumeration ends at the 29th entry; input role text retained in diagnostics is capped at 64 UTF-16 units. Only exact ASCII literals can be recognized as valid keys. The map retains at most 28 nullable color values, with no caller-owned mutable collection. Contrast enumeration is the existing fixed edge set; published issues are capped at 32 details plus an omitted-count summary. Enumeration cost does not promise a timeout for an arbitrary user-supplied iterator. |
| Contrast | The candidate is assembled completely before the existing validator runs. Semantic overrides are subject to editor, active-line and preview edges, not just their first usage. Existing selection, chrome and diagnostic edges are preserved. These finite checks do not establish that native adapters actually honor all paint invariants. |
| Semantic compatibility | Every existing known alias resolves to its original family. Unspecified families preserve the base policy's family values; unknown kinds fall back to the effective editor foreground. Diagnostic Error and semantic Error remain separate, as in bundled policies. |
| Immutability/value identity | Private arrays do not escape. Palette, typography and spacing are immutable record values. `ThemeEffectiveValues` includes every declared palette/metric value, all eight semantic families and `IsDark`; map order, hexadecimal case, base ID and display name do not accidentally affect paint equality. Delegation of base metadata assumes the existing static, immutable policy contract; mutable third-party policies are not a supported plugin mechanism. |
| Nullability | Explicit null argument checks cover the domain entry points that require non-null inputs. Nullable raw colors/keys fail validation without partial success. Public result/value records remain ordinary C# value carriers rather than enforcing non-null runtime construction for malicious callers; no current caller violates their declared contracts. |
| Native AOT | Direct enum/string switches and finite arrays use no reflection, dynamic code, runtime assembly loading or serialization binder. `IsAotCompatible` remains enabled. Source inspection does not substitute for actual Native AOT publication. |
| Existing FromHex contract | Valid ASCII six-digit input and exception categories are retained. Malformed strings formerly accidentally accepted through whitespace-tolerant numeric parsing are now rejected in line with the documented exact syntax. No bundled color changes. |

## Required integration boundaries, not findings against this draft

The implementation document correctly limits its claim to the pure Themes layer.
Future Configuration and Native work must retain these already documented
obligations:

1. Preserve raw logical duplicate detection and reject non-string TOML leaves
   before converting to the string-pair domain API. Inspect `TryCreate`'s boolean:
   rejected `Empty` must not be mistaken for a deliberate empty override map.
2. Compare `ThemeEffectiveValues`, not base IDs, when deciding whether a custom
   same-ID palette needs installation. Retain the previous installed theme on an
   invalid explicit reload, with the separate documented startup/system-appearance
   fallback rules.
3. Continue bounded config reads and origin-aware nonmodal diagnostics in the
   Configuration layer. Domain validation alone cannot bound TOML parsing or disk
   I/O.
4. Preserve source text, undo, composition, selection and viewport while applying
   native resources. Do not claim WCAG/native accessibility acceptance from
   successful finite palette validation.

No TOML parsing, settings reload, native resource lifecycle, screenshot, physical
input, screen-reader or four-RID AOT test was performed for this review. No new
external research was needed: the established architecture already records the
relevant primary contrast and platform-role references, and this review makes no
new academic or platform claim.
