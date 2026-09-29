# Local configuration and data directories

`mote` keeps its own settings, traces, caches, and application data under the
user's home directory, not beside an edited document and not in a workspace.
On Windows, `~` means the current user's profile directory (for example,
`C:\Users\Ada`); on macOS it means the Unix home directory. The default layout is:

```text
~/.mote/
  config.toml       # optional, user-edited settings
  cache/            # replaceable derived data
  data/             # durable app data such as recovery state
  traces/           # opt-in local JSONL traces
```

Merely loading settings creates none of these directories. Components create
their own destination only when writing is necessary. Reading a document does
not require a configuration file, tracing remains off by default, and no
network lookup occurs.

## Precedence and paths

1. Built-in conventions establish all values.
2. `MOTE_HOME` optionally changes the *root* used to locate `config.toml` and
   the default directories. It must be an absolute path or start with `~/`.
3. Existing `config.toml` values override individual defaults. A relative
   directory in this file resolves against the selected mote root; `~/...`
   resolves against the user profile. Absolute paths are accepted, so users
   can relocate cache, data, or traces independently. The root config file
   itself remains inside the selected mote root.

For example:

```toml
[paths]
cache = "cache"                  # ~/.mote/cache by default
data = "~/Documents/mote-data"  # an explicit, durable-data location
traces = "../mote-traces"        # relative to ~/.mote, then normalized

[appearance]
theme = "mote-light"             # default: mote-dark

[telemetry]
enabled = false                  # default: false; JSONL stays local
```

Theme identifiers refer to statically shipped theme policies; the config
loader validates identifier syntax, and the desktop policy registry determines
whether a particular ID exists. `MOTE_TRACE=1`, if supported by the desktop
composition root, is an explicit environment opt-in to tracing and should not
silently override the selected trace directory. There is no runtime plugin
loading or reflection-based config binding.

## Failure behavior and implementation contract

`MoteConfigLoader.Load()` returns an immutable `MoteConfiguration` containing
absolute `HomeDirectory`, `ConfigPath`, `CacheDirectory`, `DataDirectory`, and
`TraceDirectory` paths, `ThemeId`, `TraceEnabled`, and non-fatal `Diagnostics`.
Callers can supply `MoteConfigLoadOptions` for a portable user home or selected
root; `UseEnvironmentOverride = false` is useful for deterministic tests.

The loader parses TOML via the same explicit syntax parser family used for
documents, without object reflection or runtime code generation. It accepts
only the documented keys and scalar types. Unknown or invalid individual
values produce diagnostics and retain their defaults; a syntactically invalid
or duplicate-key TOML file is ignored as a whole to avoid ambiguous partial
application. The file must be UTF-8 (a UTF-8 BOM is accepted); UTF-16 BOMs,
invalid UTF-8, or an unreadable file preserve the defaults.
A 1 MiB cap keeps configuration I/O bounded during startup. There is no
automatic rewrite: a malformed user-edited file is left untouched so the user
can repair it. The loader never creates paths and never writes a config file.

Consumers should treat config paths as explicit *destinations*, not trusted
content. A selected destination can be unavailable (permissions, disconnected
volume, disk full). Cache and telemetry writers must fail independently of
opening, editing, or saving the user's actual document; durable application
data must report write failures rather than pretending recovery succeeded.

## Windows native runtime audit (2026-09-29)

This is an observed Windows AOT behavior, **not** a claim about an unbuilt
future revision or macOS. The audited artifact was
`.cache/win_canvas_onscreen_aot/mote.exe`, 5,886,464 bytes, modified at
2026-09-29 18:35:30 Asia/Singapore, SHA-256
`0D01E7E87CCD113B69249AB1098AADE8CAD5549A5AF348DCC54BA2DFF3530D38`.
The workspace had no usable source commit identifier at audit time. The
artifact's provenance is therefore its path, timestamp, size, and hash, not a
verified source commit. Its directory contained exactly one file, `mote.exe`,
before and after the runs. These tests used a local Windows desktop session;
they do not establish macOS behavior.

The audit used distinct absolute `MOTE_HOME` directories below the repository's
`.temp/config-runtime-audit/`. A small JSON file from that same directory was
opened by the executable (not copied into the publish directory). For the
document-open cases, the window was launched hidden, allowed to run for 1.25 s,
then closed with Windows `WM_CLOSE`; every case exited with code 0. This ordinary
close matters: force-killing a process left an empty trace file in an earlier
probe, which would not test shutdown flushing. The local reproduction script
and raw results are under `.temp/config-runtime-audit/`; this directory is
intentionally ignored by Git. A portable reproduction is to create each
`config.toml` values below (using the table-and-newline syntax in the example
above), run `mote.exe <small-json-file>` with the listed environment,
close the window normally, and inspect files beneath `MOTE_HOME` and beside the
executable.

| Case | Inputs | Files observed after normal close |
| --- | --- | --- |
| Default/off smoke | Absent home; `MOTE_TRACE=0`; `mote.exe --smoke-gui` | Exit 0; home was not created; no new file beside executable. |
| Disabled config | In `[paths]`, `traces = "trace-custom"`; in `[telemetry]`, `enabled = false`; `MOTE_TRACE=0` | Only the pre-existing `config.toml`; no trace, cache, or data directory. |
| Environment opt-in | No config; `MOTE_TRACE=1` | One nonempty JSONL file beneath `MOTE_HOME/traces/` (1,720 bytes). |
| Config opt-in and relocation | In `[paths]`, `cache = "cache-custom"`, `data = "data-custom"`, and `traces = "trace-custom"`; in `[telemetry]`, `enabled = true`; `MOTE_TRACE=0` | One nonempty JSONL file beneath `MOTE_HOME/trace-custom/` (1,721 bytes), not beneath default `traces/`. No cache/data directory was created. |
| Malformed config | Duplicate `enabled` keys in `[telemetry]` plus `traces = "trace-custom"` in `[paths]`; `MOTE_TRACE=1` | Configuration override was discarded as a whole: one nonempty JSONL file beneath default `MOTE_HOME/traces/` (1,720 bytes), not `trace-custom/`. |

The sampled config-opt-in JSONL contained five records (document-open,
analysis, presentation, and session), but neither the opened file path nor its
`audit` content. `MoteConfigLoader` unit tests separately prove that cache and
data overrides resolve to normalized absolute paths. **Native runtime routing
of cache/data writes is not yet observable:** the audited editor does not
currently write either kind of file, so their absence shows lazy creation, not
that a future cache or recovery writer already honors those overrides. A
future writer needs a focused end-to-end destination test before claiming that
contract. The audit found no concrete current path-placement defect.
