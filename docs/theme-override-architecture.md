# Theme overrides and user-owned data locations

Status: **target design, not implementation or acceptance evidence**, 2026-09-30.
Source checkpoint: `b675e0f` plus the in-progress Native Flow/configuration changes.
This document extends [themes](themes.md), [configuration](configuration.md),
[native rendering section 9](native-rendering-architecture.md#9-accessibility-and-themes-are-contracts-not-decorations),
and [the Flow integration contract](native-flow-implementation-contract.md).
It does not change those files or the active workers' APIs.

## 1. Decision and existing obligations

Keep bundled themes as immutable, compile-time `IThemePolicy` strategies. Add a
small data-only override map to `config.toml`, compose it with the selected base,
validate the complete result, and publish one immutable effective theme. Formats
continue emitting semantic roles; Engine continues owning text and no colors.
No plugin assembly, CSS interpreter, embedded browser, downloaded theme, sidecar
font, expression evaluation or reflection-based binding is needed.

The inspected source already has three concrete palettes, `system` selection,
opaque `ThemeColor`, twenty `ThemePalette` properties, UTF-8 TOML loading with a
declared size cap,
path overrides, and opt-in content-free local JSONL tracing. It does **not** yet
parse arbitrary user color overrides or offer a settings reload command. The
controller's current theme-change deduplication compares base policy IDs; that
cannot distinguish two user palettes based on the same `mote-dark` policy.
Configured cache/data paths describe destinations; their presence does not prove
that all future disk cache/recovery consumers or migrations already exist.

Preserve these external contracts:

- Missing config means `mote-dark`, preview `auto`, tracing disabled, no directory
  creation, and no network request. Explicit `system` remains optional, not the
  new default.
- Existing valid theme IDs remain case-insensitively resolved by Themes; invalid
  ID syntax retains loader defaults. A syntactically valid unknown ID retains
  the present warning and OS light/dark fallback.
- `MOTE_HOME`, `paths.cache`, `paths.data`, `paths.traces`, `[editor] preview`,
  and `MOTE_TRACE=1` retain their current semantics.
- Theme/reload/cache operations never mutate Engine text, version, dirty state,
  undo history, document encoding, or the document's Save destination.
- Native composition deferral, native source/preview selection and existing
  ordinary/LegacyPage compatibility stay intact.

**Product judgment:** familiar means restrained surfaces, readable hierarchy and
coherent controls, not copying every VS Code token or importing JetBrains theme
assets. The current original palettes are the baseline. VS Code separates
workbench/editor/selection roles and exposes user color overrides; JetBrains
separates UI theme from editor color schemes. Transfer those structural practices,
not either application's workspace/plugin machinery. [VS Code theme roles](https://code.visualstudio.com/api/references/theme-color),
[JetBrains colors and fonts](https://www.jetbrains.com/help/idea/configuring-colors-and-fonts.html).

## 2. Concrete config schema and precedence

Use the existing optional root file; do not introduce recursive theme discovery
or a second mandatory file.

```toml
# ~/.mote/config.toml; every table/key is optional.
[appearance]
theme = "mote-dark"                   # original default; "system" follows OS

[appearance.colors]
"preview.background" = "#202124"      # quoted dotted role; opaque sRGB only
"preview.foreground" = "#DADCE0"

[paths]
cache = "cache"                       # relative to selected mote home
data = "~/Documents/mote-data"         # explicit durable data override
traces = "../mote-traces"              # explicit trace destination override

[telemetry]
enabled = false                       # local only, never upload

[editor]
preview = "auto"                      # policy convention; source/split override
```

The two example colors are a candidate, not a promise that changing unrelated
roles will pass contrast. Quoting the role makes it one TOML key. Support ordinary
TOML table/inline-table representations with the same normalized leaf semantics;
recognize quoted `"preview.background"` as a role identifier, not an include or
path. Do not reject a syntactically equivalent TOML representation merely because
the sample uses a table. Tests must disambiguate quoted dots, actual table nesting
and duplicate logical roles; an ambiguous duplicate rejects the override set.

### Resolution order

| Layer | Rule | Can create/write data? |
| --- | --- | --- |
| Root convention | User profile + `.mote`; never current working directory or document parent | No |
| Explicit load inputs | `MoteHomeOverride`, otherwise permitted `MOTE_HOME`; existing deterministic options remain | No |
| Config discovery | Exactly `<selected root>/config.toml`; config cannot relocate its own root recursively | No |
| Built-in values | Existing paths, static theme default, preview auto, telemetry off | No |
| Config scalar overrides | Independently validated known path/theme/preview/telemetry keys | No |
| Base policy resolution | Selected static ID + current platform appearance | No |
| User palette composition | Inherit unspecified roles; validate all candidate colors together | No |
| Accessibility presentation | Platform high-contrast/user accessibility state overrides incompatible paint roles | No |
| Explicit trace environment opt-in | Existing `MOTE_TRACE=1` OR config enabled; never changes trace destination | Writer only |

Do not add generic `MOTE_*` binding or silently change to platform cache defaults
outside `~/.mote`. Root override is the existing intentional escape hatch for
portable setups; independent path overrides are intentional destinations, not
workspace configuration. Relative paths resolve against mote home, `~/` against
user profile, absolute paths remain allowed. Do not expand arbitrary environment
variables, shell commands, URLs or drive-relative paths.

### Parsing and failure units

Retain strict UTF-8, optional UTF-8 BOM, the 1 MiB config cap, nonfatal diagnostics,
and no automatic rewrite. Make the read itself bounded: read at most the cap plus
one byte from the opened stream, reject excess, then decode strictly. The current
metadata-length check followed by `ReadToEnd` does not provide that guarantee if
the file grows between those operations; no racing-growth experiment was run here.
The configured cap is a byte budget, with a separate decoded-character budget,
not a timeout guarantee for a slow filesystem. A syntax error or TOML duplicate key rejects the whole
file on startup as today. During reload, keep the previous effective settings
instead of resetting an active editor because the file was temporarily half
written. A deliberate deletion followed by explicit reload means return to
defaults; an unreadable existing file does not mean deletion.

Known non-theme scalar failures retain their conventional defaults in a valid
new config snapshot, preserving current load behavior. The **theme override map
is one failure unit**: unknown role, wrong scalar type, invalid color grammar,
duplicate logical role, entry limit, or composed-contrast failure rejects the
entire map. Other valid tables still apply. Startup falls back to the newly
resolved base policy. Reload retains the last successfully installed effective
theme; if there is none, it uses the base. Never silently drop one bad color and
install an untested mixture. Existing `CONFIG_UNKNOWN` remains appropriate for
unknown non-theme keys; additive theme diagnostics are `CONFIG_THEME_ROLE`,
`CONFIG_THEME_COLOR`, `CONFIG_THEME_LIMIT`, and `CONFIG_THEME_CONTRAST`.

Each rejected map reports offending role, ratio/required minimum when applicable,
and retained fallback in a persistent nonmodal settings diagnostic. Keep file
origin and role names in UI diagnostics; do not record paths or color text in
telemetry. Bound diagnostics to a summary plus at most 32 details, with an omitted
count, so a 1 MiB invalid config cannot create an unbounded warning collection.

## 3. Typed roles, immutable composition, contrast

### Initial closed color vocabulary

The following keys map directly to existing `ThemePalette` properties. This
keeps the first implementation concrete and avoids inventing unused controls.

| External keys | Existing internal roles |
| --- | --- |
| `window.background`, `panel.background` | WindowBackground, PanelBackground |
| `editor.background`, `editor.foreground` | EditorBackground, EditorForeground |
| `preview.background`, `preview.foreground` | PreviewBackground, PreviewForeground |
| `text.muted`, `gutter.foreground`, `editor.activeLineBackground` | MutedForeground, GutterForeground, ActiveLineBackground |
| `selection.background`, `selection.foreground`, `editor.cursor` | SelectionBackground, SelectionForeground, Cursor |
| `border`, `accent` | Border, Accent |
| `diagnostic.error`, `diagnostic.warning`, `diagnostic.info`, `diagnostic.success` | Error, Warning, Info, Success |
| `control.background`, `control.foreground` | ControlBackground, ControlForeground |

Add eight closed semantic family keys: `semantic.key`, `semantic.string`,
`semantic.number`, `semantic.keyword`, `semantic.comment`, `semantic.marker`,
`semantic.link`, `semantic.error`. Preserve existing aliases in
`ThemePolicies.SemanticColor`: heading→key; code/fenced-code→string;
boolean/null/emphasis→keyword; heading/list/sequence/fence markers→marker;
invalid→error. Do not expose arbitrary dictionary lookups in a hot glyph loop.
Unknown format roles still fall back to the effective editor foreground.

The exact initial map is 28 keys. Set the parser entry cap to the supported
schema count (28), with a separately bounded key length (64 ASCII characters).
Keys are case-sensitive, unlike existing theme-ID resolution; colors accept
case-insensitive hex digits but only `#RRGGBB`. Reject alpha, color names,
gradients, three-digit forms, CSS functions and expressions. These constraints
make compositing and contrast deterministic and match existing `ThemeColor`.

Flow's roles/styles remain unchanged. Native maps them to this vocabulary and
font flags, not to independently configurable per-format themes. When real
inline-code backgrounds, quotes/rules or Grid cells are implemented, add their
typed role, compiled defaults, config key and all contrast edges in one coherent
change. Do not accept unused speculative keys today, or hard-code colors inside
those future renderers. Heading level affects typography/hierarchy, not six new
color keys by default. Selection always uses its foreground over syntax/Flow
foreground; focused/disabled native states need explicit adapter rules.

### Ownership and APIs

Recommended additive contracts, names indicative rather than frozen APIs:

```text
Mote.Themes
  ThemeColorRole / SemanticColorRole          closed enum vocabularies
  ThemeOverrideData                          defensive immutable sparse values
  ThemeCompositionResult                    candidate policy or bounded issues
  ThemeComposer.Compose(basePolicy, data)    pure, no filesystem/platform calls

Mote.Configuration
  MoteConfiguration.ThemeOverrides           additive empty default
  ConfigLoadResult                           read disposition + parsed snapshot
  load bounded TOML -> typed overrides -> origin/diagnostics

Mote.Native composition root
  AppearanceState                           light/dark + accessibility samples
  EffectiveThemeSnapshot                    policy + runtime monotonic revision
  SettingsCoordinator.RequestReload()        latest request wins
  existing shell.SetTheme(IThemePolicy)      preserve this surface where possible
```

A small one-way Configuration→Themes reference is acceptable: Themes remains
UI/filesystem/parser-neutral and Configuration may name its closed color types.
Avoid a new generic schema/DI abstraction merely to remove this straightforward
dependency. Construct a private immutable `ComposedThemePolicy` implementing the
existing interface, delegating unchanged typography/spacing/name/base ID and
switch-based semantic resolution to typed data. Keep the base `Id` stable; do
not encode hashes into IDs or add dynamic registered themes.

Configuration owns requested data, not accepted native resources. The coordinator
compares effective paint/metric values and increments revision only for a real
change. Same base ID with different colors must apply; same colors with reordered
TOML must not. Use value equality over the finite palette/semantic fields, not
process-random string hashes or `Id` equality. A base-system appearance transition
recomposes the *same* requested data against the new base and revalidates it.
If that candidate fails contrast, use the newly selected valid base (do not keep
a dark old palette while claiming to follow light mode); retain the requested
override data and diagnostic so a later applicable base may use it again.

### Contrast and accessibility boundaries

Reuse `ThemeContrastValidator`, extending its pairs only for actual new surfaces.
Normal text/semantic colors use 4.5:1 against every surface they can be drawn on;
essential caret/focus/control boundaries use 3:1 against adjacent surfaces.
Selection checks its own pair; active-line/preview/panel/control/gutter checks
remain distinct. A separator which is purely decorative need not be invented
into a critical boundary, but current stricter existing checks must not be
weakened accidentally. Color is never the sole indicator of an error, link
action, focus or incomplete analysis. [WCAG 2.2](https://www.w3.org/TR/WCAG22/#contrast-minimum),
[non-text contrast](https://www.w3.org/TR/WCAG22/#non-text-contrast),
[use of color](https://www.w3.org/TR/WCAG22/#use-of-color).

Do not silently optimize or recolor an invalid user's requested palette. Suggest
the failing roles and let the user make a deliberate change. WCAG ratios are a
bounded product safeguard, not proof of universal comfort or desktop accessibility:
W3C itself describes diverse visual needs, including some users preferring lower
contrast. A supported alternative comfort contract would require an explicit
product decision; it is not an accidental bypass in this schema.
[W3C rationale and limits](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html).

For Windows high contrast, sample `SPI_GETHIGHCONTRAST` initially and on system
color changes; use the user-selected `GetSysColor` text/background/selection
pairs rather than substituting `mote-high-contrast-dark` for every OS palette.
Semantic families may collapse to readable system text while retaining glyphs,
underlines and names. Do not change global OS settings. [Win32 high-contrast guidance](https://learn.microsoft.com/en-us/windows/win32/winauto/high-contrast-parameter).
AppKit uses effective appearance and the accessibility display settings; Increase
Contrast needs stronger boundaries/palette, and Differentiate Without Color
needs non-color status information. [AppKit Increase Contrast](https://developer.apple.com/documentation/appkit/nsworkspace/accessibilitydisplayshouldincreasecontrast),
[Apple accessibility guidance](https://developer.apple.com/design/human-interface-guidelines/accessibility).
These platform adaptations are target work, not already certified by existing
light/dark tests. Never reject the user's OS-selected colors merely to restore
our dark theme; record measured deficits as accessibility diagnostics and avoid
claiming full conformance. User accessibility preference is authoritative.

Menus/dialogs owned by the OS may follow OS appearance. Mote-owned source,
preview, panels, splitter/status and owner-drawn menu states must use one
effective snapshot. Supported DWM title-bar APIs do not themselves theme the
whole client area. No undocumented uxtheme ordinal or global color mutation.
[Microsoft Win32 dark/light guidance](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-windows-themes).

## 4. Reload and native installation state machine

Provide native `Reload Settings` and `Open Settings` commands. Opening settings
creates an editable documented template only on explicit user action, using the
same safe Save mechanism; loading alone still writes nothing. Explicit reload
is the baseline hot-reload contract. A watcher may later *request* the same
reload, never mutate resources directly. Watcher events can duplicate or be lost,
so they cannot be truth or require periodic scanning on the startup path.
[FileSystemWatcher event/buffer contract](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher).

```text
Reload request(serial N)
    -> bounded read/parse on background lane
    -> validate configuration + compose complete theme
    -> discard if N is not latest or window is disposed
    -> retain diagnostics / stage valid setting groups
    -> if native source composing: retain latest pending visual candidate
    -> composition settled -> apply on UI thread
    -> native installation succeeds -> commit effective theme/revision
                               fails -> retain old model + best-effort rollback
```

Do not hold a snapshot of source text or Engine lock during configuration I/O.
Cancellation is an optimization; serial checking establishes correctness. Bound
to one active read plus one latest requested rerun. Sample platform appearance
again on UI publication, or compare its observation serial and recompose if it
changed while parsing. Do not let an older OS callback overwrite a newer config.

During marked text/preedit, keep the current resources and coalesce all changes.
After settle, apply the latest candidate once. Color-only installation reuses
typography and does not reparse documents or advance source version. Existing
bounded Flow may be restyled/reinstalled; if Native reissues its display/action
map, it receives the normal new PresentationId sequence, and old callbacks are
rejected. Preserve current source/preview selection and viewport as the adapter
contract allows. A future font/density override increments a separate layout
generation and requires measured layout/input invariants; it is not part of
this color-only schema.

Validation is transactional; a native paint operation is not magically atomic
under allocation/platform failure. Prepare reusable resources before swapping
where possible, suppress reentrant paints/callbacks according to each platform,
and commit `_theme` only after success. Current best-effort rollback plus a
nonmodal notice remains the honest failure model. Source stays usable even if
rollback also fails. Test source/undo separately from control-native undo.

Hot application groups are explicit, not an elaborate settings transaction:

| Setting group | Reload behavior |
| --- | --- |
| Palette/selection policy | Latest valid candidate; defer native installation during composition |
| Preview preference | Same composition-safe layout path; source/analysis truth unchanged |
| Cache destination | New background cache operations use new leased destination; old operations may finish safely |
| Durable data/recovery destination | Store as pending; requires closed-session handoff, normally next launch |
| Trace enable/destination | Single-writer coordinator drains/swaps off UI; disabling stops new producer admission promptly |
| Mote root/config discovery | Fixed for process lifetime; new environment root is a next-launch change |

Failure of one writer's new destination does not roll back a successfully valid
palette or affect actual document Save. Show effective/pending/failed states in
settings diagnostics; never say a destination changed while secretly writing new
records elsewhere. Existing `MOTE_TRACE=1` remains an explicit process opt-in
even when config says false; expose that origin in the settings UI.

## 5. Cache/data/traces: relocation without destructive migration

Default locations remain `~/.mote/cache`, `~/.mote/data`, `~/.mote/traces`.
Relocation does not mean moving user files. A replaceable cache is recreated in
the new destination; leave the old cache untouched. No startup copy, recursive
delete, directory merge or automatic migration is justified. Reads from the new
cache validate format/version/length/checksum/bounds before use; cache data is
not authority for text or semantic completeness. Stale/corrupt cache means a
cache miss, not malformed source or a loading failure.

Disk cache writers, when introduced, use an app-owned versioned child namespace
such as `<cache>/mote-cache-v1/`; atomic temporary-write/replace only within that
namespace. Every operation leases its resolved destination and generation so a
reload cannot redirect an in-flight temp rename or cleanup. Multiple processes
use unique temporary names; no shared mutable global parser state. Memory caches
need no filesystem migration and must remain separately bounded.

Paths are *explicit user destinations*, not security sandboxes. A configured
external/shared/removable path or deliberate symlink is not silently rewritten
under home. Opening an arbitrary document must never supply or modify these
destinations. Mote runs without elevated privileges. Cache and trace writing
errors (unavailable volume, denied permission, disk full, slow remote destination)
degrade those optional facilities independently of editing. A user-selected
remote storage location may involve OS network I/O; no preview fetch or automatic
remote config discovery is authorized by this exception. All writer I/O is off
the input lane, with bounded queue/CPU work and observable disabled/faulted state.

Never recursively clean the user-selected cache root: it may be home, a shared
directory, the data directory, or contain symlink/reparse redirects. Cleanup
must prove app-owned child namespace/leaf identity and no-follow ancestry before
deletion; if that proof is unavailable, leave files and warn. A lexical
`Path.GetFullPath` prefix is not such a proof. Use per-entry native handles or
equivalent validated no-follow operations for cleanup, and refuse traversal of
linked children. Detect lexical overlapping paths early to disable unsafe
cleanup; aliases/links make that check insufficient on its own. Trace retention
continues deleting only this session's known files, never a broad glob or root.
Do not expand cleanup scope just because a setting says `cache = "~"`.

Durable recovery data has a different contract. Do not copy/move it on a live
reload; persist its pending destination only on explicit config edit and report
next-launch applicability. Until an explicit validated recovery migration exists,
old recovery state remains at the old location. Any future migration requires
closed-session quiescence, manifest/schema validation, copy then durable
verification, conflict handling and user-approved old-data cleanup; not a cache
helper reused on irreplaceable data. A recovery write failure must say recovery
is unavailable rather than claiming it succeeded. Original-document Save never
uses `paths.data` as a fallback.

Runtime user config/cache/traces are not shipped dependencies. Native AOT still
ships exactly one executable with allowed OS imports. The data-only finite
switches avoid assembly discovery and runtime code generation, which Native AOT
does not support. Release symbol artifacts remain developer/CI artifacts, not
end-user companions. [Microsoft Native AOT limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/).

## 6. Research connection and deliberately rejected alternatives

A 2025 peer-reviewed PACM HCI study interviewed 29 people about mobile alternative
color modes and reports heterogeneous usability/accessibility experiences,
including ineffective modes and switching barriers. Its qualitative mobile
sample is **not** a benchmark proving our desktop palette or contrast thresholds.
The useful inference is that users need deliberate editable modes, predictable
switching and testing of actual rendered interfaces, rather than a developer's
single supposedly universal dark theme. [Understanding the Experiences of People
With and Without Vision Impairments When Using Mobile User Interface Alternative
Color Modes](https://doi.org/10.1145/3743704).

Production role-based themes and accessibility standards are ready mechanisms.
Adaptive/personalized color research makes a useful future evaluation question:
do user-chosen validated overrides improve long-session readability without
breaking status recognition? Test with representative users/tasks and actual
screens, not palette ratios alone. Do not add an automatic optimizer, ambient
sensor policy or ML recoloring to this editor's startup path without demonstrated
benefit. Defaults plus typed overrides answer the present request directly.

| Alternative | Why not chosen |
| --- | --- |
| Runtime theme DLLs/scripts | Conflicts with AOT/single binary; unnecessary execution/security/lifetime surface |
| Complete VS Code theme parser or CSS engine | Huge role/selector/alpha semantics unrelated to mote's bounded native UI |
| One global foreground/background | Cannot model selection, preview, diagnostics or coherent native chrome |
| Partial acceptance/autofix of bad colors | Unclear user intent and untested mixed palette; hides actual settings mistakes |
| New theme ID for every override hash | Breaks stable policy identity; use effective value/revision instead |
| Mandatory watcher and recursive theme discovery | Adds startup/lifetime work; explicit reload already satisfies editable settings |
| Automatically moving old cache/data trees | Startup/performance/destructive risk; cache disposable, recovery not disposable |
| All path changes require editor restart | Unnecessarily blocks harmless cache/trace relocation; use per-consumer lifetimes |

## 7. Coherent implementation order and acceptance matrix

1. **Theme data/composition:** implement finite roles and immutable override data
   in Themes, full-palette validator and pure tests. Preserve `IThemePolicy` and
   all shipped palettes/IDs. Do not change production colors without evidence.
2. **Configuration:** after the preview-config worker releases its files, add
   bounded TOML override extraction/default-empty property and diagnostics.
   Preserve existing path/config tests; document supported external roles.
3. **Native settings coordinator:** add explicit native commands, request serials,
   value/revision comparison, latest-candidate composition deferral and status.
   Coordinate controller ownership with Flow lead; same-version display-map
   identity and native source invariants are non-negotiable.
4. **Native surfaces/writers:** wire all twenty roles, actual menu/selection/focus
   states and platform accessibility; implement destination handoffs in actual
   cache/recovery/trace owners only as those consumers require them. Do not build
   a general migration framework for nonexistent disk caches.
5. **Four-RID publish/host acceptance:** GitHub Actions performs managed and AOT
   checks; hosted target probes cover actual native controls. Local GUI checks
   are reserved for physical IME/readers/visual scenarios CI cannot prove.

| Gate | Required discriminating cases and assertions |
| --- | --- |
| Defaults/compatibility | No config, old valid config, all existing IDs/case behavior/unknown fallback; no writes or workspace lookup; old preview auto behavior retained |
| Parser/schema | UTF-8/BOM/invalid UTF-8/size cap; wrong scalar; quoted dots vs equivalent nested/inline tables; exact 28-key cap; unknown/duplicate roles; alpha/CSS/name rejection |
| Composition | Omitted roles inherit; aliases preserved; changed foreground or background violates connected contrast pairs; reject entire map; bounded diagnostics; immutable defensive copies |
| Reload ordering | Slow A then fast B; dispose; same base ID/different colors applies; reordered equivalent data no-ops; half-written config retains old state; explicit deletion restores defaults |
| OS interaction | `system` dark→light recomposes; override valid only on old base falls back truthfully; Windows user high-contrast pairs/macOS Increase Contrast and Differentiate Without Color; no OS mutation |
| Native state | Actual RichEdit/AppKit/Canvas color readback/screenshots; source/preview selection and scroll unchanged; source text/version/dirty/undo/saved SHA identical; native Flow callbacks tied to installed map |
| Real input/accessibility | Reload/theme transitions during physical Chinese IME commit **and cancel**, actual Narrator/VoiceOver; candidate/focus/caret stability; synthetic marked text is separate evidence |
| Chrome | Dark/light/high-contrast menus/popups/focus/hover/disabled/selection/status/splitter/empty document; 100/150/200% Windows and standard/Retina Mac; OS-owned vs mote-owned surfaces distinguished |
| Writer relocation | Cache lease race, denied/disconnected/slow path, shared root, overlap, symlink/reparse/path swap, concurrent processes; never deletes arbitrary root or blocks source; trace swap/drain/disable preserves content-free records |
| Recovery safety | Pending vs effective destination shown; old recovery untouched; unavailable writer reports failure; original Save never rerouted |
| Performance/delivery | Matched default/override cold startup and hot reload CPU/allocations/native memory; zero per-token brushes; measure native install separate from physical present; four publish directories contain one executable |

Useful first probe: publish a valid dark override, edit only `preview.background`
to a second valid value while leaving `appearance.theme` unchanged, reload during
an input composition, then settle. Assert exactly one final palette installation,
unchanged source/undo/selection, correct visible preview background, and successful
same-version Flow source activation. This directly falsifies ID-only deduplication
and unsafe composition timing; a palette screenshot by itself cannot do either.

No new target-host or performance test was run for this design document. Existing
theme/ABI/runtime evidence remains in the linked repository notes; implementation
and acceptance must record fresh scope-matched results, not relabel those older
light/dark results as custom-override acceptance.
