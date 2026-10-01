# Theme override architecture independent review

Date: 2026-09-30. Scope: `docs/theme-override-architecture.md`, a **target design**,
not implemented custom-theme or writer-relocation acceptance. Reviewed alongside
the current working-tree configuration, theme, native-controller, telemetry and
Flow contracts. Other agents were editing Native Flow concurrently; this review
does not approve those production changes.

## Verdict and prioritized findings

**No substantive design defect found in the reviewed scope.** The design can
proceed to implementation. This is not a claim that custom colors, live settings
reload, OS accessibility adaptation, destination handoff or safe disk-cache
cleanup currently work. No new build, platform execution or performance test
was performed for this document review.

No mandatory correction or speculative balance objection is recorded. The
finite data-only composition, independent settings failure units and explicit
consumer lifetime rules address the user's requirements without introducing a
runtime plugin system or a general-purpose migration framework.

## Evidence and contract checks

| Concern | Inspected evidence | Assessment |
| --- | --- | --- |
| Theme strategy and separation | `IThemePolicy`, `ThemePalette`, `ThemePolicies.SemanticColor`; design sections 1 and 3 | Bundled policies stay compiled, Formats emits semantic roles, Engine owns no colors. A private immutable composed policy preserves the existing interface while permitting user-owned data. |
| Default precedence and locations | `MoteConfigLoader.Load`, `TryResolvePath`, `MoteConfiguration`; design section 2 | `.mote` conventions, root override, per-destination overrides, default dark theme and tracing opt-in are preserved. Config cannot recursively relocate its discovery root or derive destinations from edited documents. |
| Existing ID behavior | Loader `IsThemeId` and registry `Resolve`; design section 1 | The document accurately distinguishes lower-case loader syntax validation from case-insensitive registry resolution. It does not promise that upper-case IDs newly become valid config syntax. |
| Immutable effective state | Controller `AppearanceChanged` / `ApplyPendingTheme`; design sections 3 and 4 | The existing ID-only deduplication cannot identify custom colors. The proposed finite value comparison and revision fix this without changing stable external IDs or using unstable hashes. |
| Composition and stale work | Controller composition deferral; Flow `NativePresentationId` contract; design section 4 | Latest-request serial checks, re-sampling OS state on publication and IME deferral are coherent. Source-version identity remains separate from native presentation identity. |
| Invalid settings | Loader read/parser behavior; design section 2 | Startup fallback and reload retention are explicitly different. Invalid color maps reject as a unit, without rolling back independent valid settings. Missing versus unreadable is a required future load disposition, not behavior certified by the existing loader. |
| Input/startup bounds | Current `ReadConfig` metadata check followed by `ReadToEnd`; design section 2 | The design explicitly remedies the currently unbounded growth window with cap-plus-one stream reading and bounded diagnostics. It does not falsely equate a byte cap with a filesystem latency timeout. |
| Contrast and semantic colors | `ThemeContrastValidator.Validate`; design section 3 | Existing validator checks source, active-line and preview semantic foregrounds, plus selection and chrome. The proposed full-result validation and surface-dependent pairs preserve rather than weaken that contract. |
| OS user preferences | Design section 3 and official guidance below | Windows high contrast uses user system colors rather than a hard-coded dark approximation. OS-selected colors are authoritative even if an application ratio check would reject them. The design does not present ordinary light/dark tests as high-contrast acceptance. |
| Trace relocation | `JsonlTraceSink` bounded channel, immutable `_directory`, asynchronous writer; design sections 4 and 5 | A coordinating writer handoff is necessary; mutating the existing sink's destination would violate its lifetime. The design specifies producer admission, draining and observable effective/faulted state rather than silently redirecting queued records. |
| Destructive/path safety | Existing explicit absolute destinations; design section 5 | Arbitrary user roots are not deletion sandboxes. App-owned namespace, leased destinations, no-follow identity checks and refusal to clean without proof are appropriate. Lexical overlap detection is explicitly not a security proof. |
| Durable versus replaceable data | `MoteConfiguration.DataDirectory` contract; design section 5 | Cache relocation is disposable; recovery relocation is deferred and requires separate validated migration. Document Save never falls back into app data. This avoids conflating derived cache with user recovery authority. |
| Single binary and AOT | Design sections 1 and 5; official .NET limitations | Closed typed values and switches need no dynamic assemblies or code generation. Runtime user config/data are not shipped companion libraries; release symbol artifacts remain separate developer evidence. |

## External verification

- [Microsoft Native AOT documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
  confirms the restrictions on dynamic assembly loading/runtime code generation,
  platform-specific publishing and separate debug artifacts. These support the
  design's mechanisms, but do not by themselves certify a particular publish directory.
- [Microsoft high-contrast guidance](https://learn.microsoft.com/en-us/windows/win32/winauto/high-contrast-parameter)
  calls for initial and `WM_SYSCOLORCHANGE` sampling with `SPI_GETHIGHCONTRAST`
  and user-selected `GetSysColor` foreground/background colors. The document's
  target Windows behavior matches that guidance.
- [FileSystemWatcher documentation](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher?view=net-10.0)
  documents buffer overflow/lost events and duplicate notifications. Treating a
  watcher as an optional reload requester, rather than configuration truth,
  is grounded in the real API contract.
- The Apple Increase Contrast reference was reachable, but its web extraction
  exposed only a minimal documentation shell. This review does not independently
  certify AppKit accessibility callbacks or ABI signatures from that page.

## Implementation gates, not additional findings

The document already contains a useful discriminating acceptance matrix. The
first implementation should preserve its hard cases: same-base-ID changed
colors during physical IME commit/cancel; bounded stream reading; quoted versus
nested/inline TOML role equivalence; unknown fallback semantic foreground contrast;
same-version Flow replacement; actual native color readback; and writer lease
races. These are requirements to test against implementation, not deficiencies
in the target document.

Recovery-path discovery and migration must be revisited when an actual durable
recovery consumer exists. Keeping old files untouched alone is not proof that a
future recovery UI can discover them after a destination change; no such
consumer or cross-launch recovery behavior was certified here. The design
appropriately does not claim otherwise.

No production/design file was modified, and no files were staged or committed
by this review. The durable output is this review artifact only.
