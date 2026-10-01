# Delivery-first release scope — 2026-10-02

## Current user decision

The user superseded the paused objective: allow multiple delivery files, retain
Native AOT, publish a deliverable version, and maintain its product release page,
manual and changelog. This permits an application directory/macOS `.app` and
reviewed companions; it does **not** authorize switching to CoreCLR, adopting
Avalonia without qualification, or changing one editable document into a
workspace. Prior strict-single-binary descriptions are historical constraints,
not the current release rule.

## Product documentation ownership

- `README.md`: concise landing, downloads/navigation and product expectations.
- `docs/user/installation.md`: architecture selection, no-.NET deployment,
  complete-package updates, unsigned security limitations.
- `docs/user/manual.md`: actual native commands, formats/completeness, encodings,
  Save/recovery, privacy and limitations.
- `docs/user/configuration.md`: real keys, precedence, destinations, theme
  overrides and live-vs-next-launch behavior.
- `CHANGELOG.md`: user-visible version history, never invented releases.
- `docs/releases/v0.1.0.md`: planned-to-published transition, assets and exact
  qualification scope.
- `docs/releases/README.md`: repeatable artifact/document maintenance checklist.

## Source-traced claims

Documentation was checked against `src/Mote.Native/Program.cs`, native menu
creation in Windows/Mac `EditorShell`, `NativeEditorController` settings reload,
`Mote.Configuration/MoteConfigLoader`, `Mote.Formats/Contracts.cs` policy dispatch,
Engine codec definitions, and existing Save-recovery/telemetry contracts.
No new platform behavior is inferred from documentation work.

Writer destinations and trace opt-in stay at startup lifetime during reload.
Theme/preview can apply live after composition. Cache/data directory settings do
not prove an active cache or autosave writer. Save recovery content is a narrowly
justified same-directory exception to the `~/.mote` convention. A failed Save can
have unknown/changed target state; the manual does not promise universal original
retention. Unknown file extensions and `.jsonc` use plain text.

## Outstanding release decisions / evidence

The root and product/packaging teams own final default-profile promotion,
four-architecture extracted-package qualification, tag/source mapping, supported
OS statement, inventories, checksums and publication. Documentation uses draft
status until those facts exist. No unsigned Apple trust promise or latency/reader/
IME claim is manufactured to make the release sound finished.

The draft scope is an integrated, usable product release—not a permission to
ship silent loss, pretend incomplete analysis is global, or delete difficult
capacity tests. Conversely, 100-MB stress benchmarks and theoretical numeric
latency hypotheses do not become ordinary-user release gates by repetition.

## External source check

Accessed 2026-10-02: Apple's [Safe app launch guidance](https://support.apple.com/en-us/102445)
supports the manual's application-specific Open Anyway path and warning that
unidentified/unnotarized downloads may be blocked. The live [.NET 10 supported OS
matrix](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md),
updated 2026-09-28, lists supported macOS 15/26/27 and nuanced Windows versions/
editions. This rolling policy is not proof of mote minimum-OS execution; final
release requirements and actually tested runner versions are owned by release
qualification. Old macOS 12/14 or blanket Windows 10 claims must not be inferred
from historical targeting or a runtime identifier.
