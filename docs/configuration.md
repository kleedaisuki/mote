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
