# Theme override configuration contract and evidence

Implemented configuration-side contract, 2026-09-30. This extends
[the reviewed architecture](theme-override-architecture.md) without claiming
native application/reload or accessibility acceptance.

## Ownership and dependency

`Mote.Configuration` has a one-way project reference to `Mote.Themes`.
`MoteConfiguration.ThemeOverrides` is an additive, immutable requested-data
property defaulting to `ThemeOverrideData.Empty`; existing required-property
initializers remain source-compatible. Additive `ThemeOverridesAccepted` defaults
to true and becomes false for any map/schema rejection, so native transactions
do not depend on diagnostic-code strings. `ReadDisposition` defaults to Missing
for compatibility and distinguishes Missing / Loaded / Rejected file outcomes.
Configuration does not resolve OS
appearance, compose an effective policy, allocate native resources, or validate
contrast. The native composition owner must call `ThemeComposer.Compose` against
the currently resolved base and retain its composition diagnostics.

## Schema and failure units

```toml
# <selected mote home>/config.toml; every key is optional.
[appearance]
theme = "mote-dark"

[appearance.colors]
"preview.background" = "#202124"
"preview.foreground" = "#DADCE0"
"semantic.link" = "#ABCDEF"
```

These sample requests are not a promise that the final combined palette passes
contrast. Existing bundled-theme IDs and default selection are unchanged.

The 28 role keys and `#RRGGBB` grammar come exclusively from
`ThemeOverrideData`. Keys are case-sensitive; hexadecimal digits are not.
Unknown keys, overlong keys, wrong value types, invalid color strings,
duplicate logical roles, or more than 28 leaves reject **the whole map** and
publish `Empty`, never a partial palette. Other valid scalar groups still apply.
A TOML syntax/duplicate-key error continues to reject the entire file.

The loader preserves structural TOML key segments until it recognizes the
actual `appearance` / `colors` namespace. Below that namespace, joined leaf
segments identify the closed role. Thus these are equivalent requests:

| Representation | Example |
| --- | --- |
| Quoted dotted role | `[appearance.colors]` then `"preview.background" = "#202124"` |
| Structural dotted leaf | `[appearance.colors]` then `preview.background = "#202124"` |
| Nested table | `[appearance.colors.preview]` then `background = "#202124"` |
| Inline quoted role | `[appearance]` then `colors = { "preview.background" = "#202124" }` |
| Inline nested table | `[appearance]` then `colors = { preview = { background = "#202124" } }` |
| Root dotted assignment | `appearance.colors."preview.background" = "#202124"` |
| Root inline representation | `appearance = { colors = { preview.background = "#202124" } }` |

A top-level quoted `"appearance.colors"` is **not** the structural namespace.
Quoted `"preview.background"` and nested `preview.background` may coexist as
distinct TOML keys, but they duplicate a mote logical role and reject its map.
Empty maps and empty known family containers inherit everything. Empty unknown
containers and individual color roles assigned empty tables are rejected;
arrays of tables are not maps. Structural ancestry is tracked in a case-sensitive
segment trie, not by joined-string prefix tests: any array ancestor rejects the
map, including `[[appearance]]` followed by inline colors or ordinary
`[appearance.colors]`. Empty maps under an array ancestor also reject. An
unrelated sibling array, or quoted `"appearance.colors"` array, does not taint a
valid ordinary `appearance` / `colors` path. Traversal uses an explicit stack rather than
recursing through nested inline tables.

The distinction between quoted keys, structural dotted keys and true TOML
key duplication follows the [official TOML specification](https://toml.io/en/v1.1.0#keys).
The extra logical-role collision rule is mote's schema, not a TOML syntax rule.

## Bounded reads and diagnostics

- Open the selected file read-only, sharing read/write/delete with other users.
- Read at most **1,048,577 bytes** from that stream. Reject if more than
  **1,048,576 bytes** are observed. Metadata length is only an allocation hint;
  if the file grows, capped buffer expansion preserves the same read budget.
- Decode only strict UTF-8. Remove one optional leading UTF-8 byte-order mark;
  its three bytes still count against the byte budget. Do not autodetect
  UTF-16/32 or replace malformed byte sequences.
- Independently retain the decoded 1,048,576-character budget.
- Keep at most **32 diagnostic details plus one omitted-count summary**.
  Theme role names included in details are capped at 64 characters. Diagnostics
  are deterministic for unchanged file bytes and inputs.

The byte cap is not a filesystem timeout, file snapshot, or proof that a
concurrently rewritten file is consistent. A mid-read rewrite can still produce
a bounded syntax/read error. No racing-growth experiment was run; the hard bound
is established by the stream-read loop and exercised at static boundaries.

Diagnostics use existing `CONFIG_READ`, `CONFIG_SIZE`, `CONFIG_TOML`,
`CONFIG_UNKNOWN` and scalar codes, plus `CONFIG_THEME_ROLE`,
`CONFIG_THEME_COLOR`, `CONFIG_THEME_LIMIT`, and the general
`CONFIG_DIAGNOSTICS` omitted-count summary. Contrast issues remain Themes/native
composition responsibilities. No diagnostic is uploaded by this loader.

## Authoritative read disposition and reload contract

The loader opens the stream directly instead of gating on `File.Exists`, which
can return false for an inaccessible existing path. The actual open/read outcome
is authoritative:

| Disposition | Meaning | Explicit reload owner action |
| --- | --- | --- |
| `Missing` | Open reports FileNotFoundException or DirectoryNotFoundException | Deliberately return to conventional defaults |
| `Loaded` | Bounded strict UTF-8 read and syntax-valid TOML | Independently validate/apply setting groups |
| `Rejected` | Other open/read error, oversized content, invalid UTF-8, or TOML syntax failure | Retain previous whole active settings snapshot |

A wrong scalar or rejected theme map in an otherwise valid document is Loaded,
not a file-level rejection. `ThemeOverridesAccepted=false` provides the separate
map transaction outcome; explicit empty/absent maps are true. Contrast acceptance
remains a later composer result. A directory occupying `config.toml` is rejected
with `CONFIG_READ`, not treated as deletion. Startup still returns defaults with
nonfatal diagnostics on Rejected, preserving the prior startup behavior.

## Compatibility and side effects

Missing settings retain `~/.mote`, `mote-dark`, preview `auto`, tracing disabled,
and an empty override map. Explicit load options, permitted `MOTE_HOME`,
relative path resolution against mote home, `~/` user-profile resolution,
independent cache/data/trace overrides and existing validation defaults retain
their prior semantics. Config cannot relocate its own discovery root.
The read path creates no directories, rewrites no files, edits no document,
and performs no network request. Native `MOTE_TRACE=1` handling remains outside
this loader and was not changed.

## Actual validation

Windows local .NET 10 Release evidence:

```powershell
dotnet build src/Mote.Configuration/Mote.Configuration.csproj -c Release --no-restore -warnaserror

dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore `
  --filter 'FullyQualifiedName~ThemeOverrideConfigurationTests|FullyQualifiedName~ConfigurationThemeTests|FullyQualifiedName~PreviewConfigurationTests' `
  --logger 'trx;LogFileName=theme-config.trx' `
  --results-directory .temp/theme-config-test
```

- Configuration build: **0 warnings, 0 errors**.
- Focused suite: **81 passed, 0 failed, 0 skipped**: 60 new configuration-theme
  cases, 8 existing configuration/theme cases, 13 existing preview cases.
- New checks cover equivalent TOML forms, empty defaults, typed failures,
  logical versus TOML duplicates, structural quoted-key distinctions, empty
  invalid containers, exact 28/29 entry boundaries, bounded deterministic
  diagnostics, overlong role names, UTF-8 BOM, invalid UTF-8/UTF-16/UTF-32,
  exact byte-cap boundaries, multibyte overflow, independent destination paths,
  no directory creation, existing initializers, and low-contrast requested data
  being retained for the composer. Thirteen follow-up cases cover array ancestors
  (including empty/nested maps), unrelated arrays, direct-open inaccessible-path
  classification, and deliberate deletion; existing checks additionally assert
  read disposition and map acceptance where relevant.
- TRX: `.temp/theme-config-test/theme-config.trx` (transient local artifact).

An isolated project under `.temp/theme-config-isolated/` also ran the same 81
linked cases against real Configuration/Formats/Themes projects with warnings as
errors while concurrent Native edits temporarily prevented the full test project
from building. Once Native reached a compilable checkpoint, the actual
`Mote.Tests` focused suite above passed 81/81. The isolated project is not a
substitute for that successful integrated run.

No native UI, reload, Native AOT publication, macOS execution, live racing writer,
telemetry sink, or startup-latency benchmark was performed by this assignment.
