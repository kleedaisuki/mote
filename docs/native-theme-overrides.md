# Native theme overrides and explicit reload

Status: implemented and independently reviewed; Windows managed evidence only, 2026-09-30.

## Contract

`NativeEditorController` composes the startup base policy with immutable
`MoteConfiguration.ThemeOverrides`. Startup config remains the existing synchronous,
byte/character-bounded load: it is not a timeout guarantee on a slow filesystem.
No read or reload creates directories, rewrites config, downloads resources, or
reads the source document. The config root is pinned to the initial HomeDirectory
for the process; changing MOTE_HOME in another process cannot redirect reload.

File > Reload Settings on Windows and mote > Reload Settings on macOS are explicit
commands with no additional shortcut. They do not force native composition commit.
`NativeSettingsReload` runs one background bounded read, coalesces repeated requests
into one latest rerun, and suppresses stale/disposed publication by serial identity.
A newer request also invalidates a previously staged composition-deferred snapshot.
Actual UI callbacks remain on the shell's existing UI dispatch lane.

Missing files restore conventions on deliberate reload. Read/size/UTF-8/TOML rejection
retains the entire previous snapshot. A schema-invalid or contrast-invalid override
map retains the previous requested appearance and installed palette; independent
preview preferences may still apply. Native installation errors keep the canonical
old effective policy and attempt best-effort native rollback, with a persistent
nonmodal failure notice. Rendering/rollback failure is not reported as success.

Appearance callbacks recompose the same requested overrides against the current base.
A map invalid on the new base uses that actual new base and exposes contrast details;
it remains requested so returning to an applicable appearance can restore it. Effective
value comparison includes palette, semantic families, typography, spacing and dark mode,
not the stable theme ID. Empty/equivalent compositions reuse static policy identity.

Live settings are appearance and preview layout. Existing cache/data/trace paths and
trace enablement have process/writer lifetime; reload reports changes as next-launch,
keeps the running writer untouched, and never moves/deletes old data. There is no
invented cache/recovery migration. Open Settings/template creation is separate work.

Persistent settings notices contain origin and bounded diagnostic details (32 entries,
256 characters per detail), not document content. They are native status text, not
telemetry payloads. Existing Windows system high-contrast status/caption handling is preserved; full
source/preview OS accessibility adaptation remains target work. These custom
reload tests do not certify physical IME, screen readers, or OS appearance automation.

## Verification checkpoint

Commands:

```powershell
dotnet build src/Mote.Native/Mote.Native.csproj -c Release --no-restore -warnaserror
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~NativeThemeOverride|FullyQualifiedName~Theme_override|FullyQualifiedName~Runtime_'
```

Native build: 0 warnings/errors. Focused tests: 21/21 on Windows, including unchanged
source/Engine Undo, same-ID different values, value-equal no-op, composition deferral,
rejected file/map, explicit deletion, dark-only override fallback/recovery, coalesced
100 reload requests and disposed publication, existing appearance runtime cases,
and the first real HWND readback. A HWND negative result showed native RichEdit
formatting adds an internal undo action; OS-supported suspension mitigation and its
native undo regression are being investigated before freeze. See
[native platform evidence](native-theme-overrides-platform.md).

No macOS custom override/reload runtime or Native AOT acceptance has run yet.

## Broad affected-controller run and environment limitation

The affected controller subset returned 53/57. The four failures were:
`Controller_large_markdown_session_labels_visible_result_provisional`,
`Controller_idle_yaml_full_pass_promotes_exact_offscreen_diagnostic`,
`Controller_idle_toml_provisional_full_pass_preserves_visible_facts`, and
`Controller_large_csv_session_shows_exact_global_diagnostic_count`. Each timed out
with status `Full pass deferred: memory pressure; global diagnostics unknown`.
No theme assertion failed. The idle guard/tests were not weakened; this concurrent
local-host run does not establish a theme regression, nor is it a clean broad pass.
Later isolated/hosted CI must recheck the affected native assembly after integration.

## Final focused checkpoint

After review fixes and the Windows native-history mitigation, the scope-matched
union returned **37/37** in 242 ms; Native Release warn-as-error returned 0 warnings
and 0 errors. Filter includes NativeThemeOverride, Theme_override, Runtime_,
WindowsFlowRtfTests and two independent Review_* reload regressions. The third
latest-request/deferred-preedit regression separately passed in independent review. The
review fixes retain rejected read/layout diagnostics across OS appearance callbacks
and actually reinstall the previous native preview after a partial layout failure.
Windows TOM suspension plus ST_KEEPUNDO now preserve native undo/redo, including
pending redo; no clear-history workaround or weakened native assertion is used.

Independent review: [reload review](reviews/native-theme-overrides-review.md).
The in-memory Mac probe is registered as `--check-native-mac-theme-overrides` and
passed independent safety inspection. Its actual Mac AOT run remains pending CI;
see [Mac probe safety design](native-theme-overrides-mac-probe.md).
