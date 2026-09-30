# Theme override configuration integration review

Date: 2026-09-30. Scope: frozen working-tree changes in
`src/Mote.Configuration/Mote.Configuration.csproj`, `MoteConfiguration.cs`,
`MoteConfigLoader.cs`, `tests/Mote.Tests/ThemeOverrideConfigurationTests.cs`, and
`docs/theme-override-configuration.md`, against
`docs/theme-override-architecture.md` and the existing `ThemeOverrideData` API.
No production/test files were edited and no changes were staged or committed.

## Finding: P2 — array ancestry is erased before theme schema validation

**Location:** `src/Mote.Configuration/MoteConfigLoader.cs:189-200`, particularly
its `IsThemePath(prefix) && table is TableArraySyntax` rejection.
**Confidence:** high; both examples below were executed against the frozen code.

```toml
[[appearance]]
colors = { accent = '#ABCDEF' }
```

```toml
[[appearance]]
[appearance.colors]
accent = '#ABCDEF'
```

Both load with `ThemeOverrides.TryGetColor(ThemeColorRole.Accent, out _) == true`
and an empty diagnostic collection. Yet `appearance` is an array, not the
configuration table holding a single map. The second form places the colors
subtable inside the latest array element. This is valid TOML, not a parser error;
its structure is described by the [official TOML arrays-of-tables
specification](https://toml.io/en/v1.1.0#array-of-tables).

The new guard rejects arrays only when the array header itself has the
`appearance/colors` prefix. An `[[appearance]]` header bypasses it; flattening
its inline `colors` value produces the same path as an ordinary table.
Separately declared descendants also lose their array ancestor provenance.
Consequently structurally invalid theme data is silently installed, violating
the documented arrays-not-maps rule and whole-map failure contract. Multiple
appearance array elements can also merge different roles into one effective map.
This is not a text corruption/security finding, but it is a release-blocking
schema correctness gap in this integration.

**Correction guidance:** retain array ancestry when recognizing the actual
appearance/colors namespace, or explicitly detect an array `appearance`
ancestor and reject its theme group before extracting any descendants. Reject
the complete theme map with an appropriate bounded theme diagnostic, including
when valid leaves exist in other array elements. Preserve established non-theme
scalar behavior independently rather than incidentally changing legacy path,
preview, or telemetry parsing. Add discriminating tests for inline colors under
`[[appearance]]`, explicit `[appearance.colors]` below it, and multiple array
elements contributing distinct role leaves. Do not merely broaden the direct
array-header check: descendant plain-table headers need the same ancestry rule.

## Reviewed contracts with no additional substantive finding

- Structural key segments preserve the quoted top-level `"appearance.colors"`
  distinction; normalized role leaves collide through `ThemeOverrideData`.
- Invalid known role values and unknown leaves reject the complete requested
  map, while valid unrelated scalar groups apply. The cap counts actual leaves
  including invalid ones; collecting typed string entries remains at most 28.
- The read loop bounds actual stream reads to 1,048,577 bytes, regardless of
  metadata growth. Initial zero length allocates one byte and can expand;
  short nonzero reads continue; a zero read terminates. Buffer growth is capped
  and the final excess byte rejects before decoding. Strict UTF-8/BOM handling
  does not autodetect another encoding. Static cap cases are already covered by
  the owner's completed tests; they were not rerun here.
- Diagnostic retention is capped at 32 details plus an omitted-count summary;
  theme role detail strings are bounded. The collector is finalized once by
  `Load`, so its mutable finalization is not an observed defect.
- The added requested-data property has an empty default and is not required;
  existing initializers remain valid. Existing directory resolution helpers,
  theme-ID validation, preview scalar validation and local trace flag logic
  were inspected, without evidence of a new regression in ordinary supported
  forms. No filesystem writes are introduced on the load path.
- The Configuration-to-Themes project reference is one-way; the new extraction
  uses closed enums/switches and TOML syntax objects, not reflection binding,
  dynamic code or assembly discovery. This inspection does not substitute for
  four-RID Native AOT publication.

## Targeted probe and limits

Artifacts: `.temp/theme-config-review/probe.csproj`, `Program.cs`, and the local
fixture home. Execute from repository root:

```powershell
dotnet run --project .temp/theme-config-review/probe.csproj -c Release
```

Four additional cases were executed. Two demonstrated the finding; the other
cases exercised an empty known family alongside a quoted role, and a root
ordinary array value, the latter correctly remaining outside the accepted
map. No completed 68-case suite was rerun. No racing file growth experiment,
custom short-read stream, macOS execution, Native AOT publish, native theme
application/reload, accessibility verification, telemetry sink, or startup
benchmark was performed. The existing `File.Exists` read-disposition issue for
future reload and syntax-parser limits are outside the new contract's claimed
acceptance; no speculative blocker is asserted for them.

The documented split between requested data and effective theme composition is
appropriate. Native composition must still preserve contrast diagnostics and
reload retention; this configuration review does not certify those separately
owned features.

## Follow-up: finding resolved; explicit load disposition reviewed

The owner froze a corrected draft after this finding. `TableAncestry.Observe`
now records case-sensitive structural segments in a trie, carries array ancestry
through inline flattening, and rejects descendant theme tables even when their
own header is an ordinary table. The original two bypass fixtures now produce
`Loaded`, `ThemeOverridesAccepted=false`, an empty map and `CONFIG_THEME_COLOR`.
The additional multi-element fixture contributing different roles is also
rejected as one map. A quoted unrelated `[['appearance.colors']]` array does
not poison the separate structural `[appearance.colors]` map: Accent is
accepted and only its unrelated key yields `CONFIG_UNKNOWN`. The regression
tests for empty maps, nested arrays and unrelated sibling arrays were inspected.
**No substantive unresolved finding remains within this configuration scope.**

The additive `ReadDisposition` is based on direct open/read rather than
`File.Exists`: missing file/directory yields Missing; UTF-8/size/read/TOML
failure yields Rejected; a syntax-valid file with group-level validation failure
is Loaded. `ThemeOverridesAccepted` distinguishes a deliberately empty map from
a rejected one without scraping bounded diagnostics. Both properties have
source-compatible defaults. Consumers must check file-level Rejected before
interpreting group-level acceptance; this loader does not itself retain a live
editor's previous settings.

A targeted independent Windows probe additionally held an existing config open
with `FileShare.None`: the load returned Rejected plus `CONFIG_READ`. After
closing/deleting the file, a load returned Missing. This directly exercises an
unreadable existing file instead of inferring it from `File.Exists`. No whole
suite was rerun. The amended `.temp/theme-config-review/Program.cs` contains the
six follow-up probes; execution used the same command documented above.

The owner's 81/81 integrated focused suite and zero-warning Configuration build
are recorded in its implementation document, not claimed as independent reruns.
The shared read's original cap/short-read reasoning remains unchanged. Native
reload ordering/resource retention and four-RID AOT acceptance remain separately
owned and were not certified by this review.
