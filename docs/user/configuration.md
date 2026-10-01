# Configuring mote

No configuration file is required. Conventions apply first; explicit valid user
settings override defaults. Configuration is not loaded from an opened document's
folder or a workspace.

## Default locations

```text
~/.mote/
  config.toml       # optional UTF-8 settings, edited by you
  cache/            # destination for replaceable derived data
  data/             # destination for durable application data
  traces/           # local diagnostic JSONL, only when enabled
```

On Windows `~` is your profile (for example `C:\Users\Ada`); on macOS it is
your home directory. Reading settings does not create these directories. A path
setting reserves a destination, not a promise that a cache or autosave writer
currently uses it. Recovery staging for failed Save is separately documented in
[the manual](manual.md#save-and-recovery).

## Example configuration

Create `~/.mote/config.toml` yourself when needed:

```toml
[appearance]
theme = "mote-dark"

[editor]
preview = "auto"

[paths]
cache = "cache"
data = "data"
traces = "traces"

[telemetry]
enabled = false
```

All keys are optional. The defaults above are the ordinary conventions. `preview`
accepts `auto`, `source`, or `split`: auto follows the format/profile convention;
source hides preview without disabling analysis; split requests both panes.

## Themes and color overrides

Built-in theme IDs are `mote-dark` (default), `mote-light`,
`mote-high-contrast-dark`, and `system` (follows OS light/dark preference).
Themes are presentation policies, not executable plugins. The palettes use
restrained familiar editor colors inspired by VS Code/JetBrains design patterns,
not copied proprietary theme assets.

Optional data overrides use known role names and opaque `#RRGGBB` values:

```toml
[appearance]
theme = "mote-dark"

[appearance.colors]
"preview.background" = "#202124"
"preview.foreground" = "#DADCE0"
```

Overrides are validated together, including contrast. Invalid roles, value types,
duplicates or an invalid combined palette do not produce a partially applied
unreadable theme. On reload, a rejected theme group retains the previous theme;
at startup the base policy remains available. See the
[full typed override contract](https://github.com/kleedaisuki/mote/blob/main/docs/theme-override-configuration.md) and
[theme policy documentation](https://github.com/kleedaisuki/mote/blob/main/docs/themes.md) for the role model. Contrast validation
alone is not a certification of every native control or assistive technology.


The accepted roles are:

| Group | Roles |
| --- | --- |
| Surfaces | `window.background`, `panel.background`, `editor.background`, `editor.foreground`, `preview.background`, `preview.foreground` |
| Navigation and selection | `text.muted`, `gutter.foreground`, `editor.activeLineBackground`, `selection.background`, `selection.foreground`, `editor.cursor`, `border`, `accent` |
| Diagnostics | `diagnostic.error`, `diagnostic.warning`, `diagnostic.info`, `diagnostic.success` |
| Controls | `control.background`, `control.foreground` |
| Semantics | `semantic.key`, `semantic.string`, `semantic.number`, `semantic.keyword`, `semantic.comment`, `semantic.marker`, `semantic.link`, `semantic.error` |

Role names are case-sensitive. The override map is limited to these 28 leaves;
unknown roles or duplicate logical roles reject the whole map.

## Move directories

Relative paths resolve against the selected mote home, not the current working
directory or document folder. `~/...` resolves against your user home; absolute
paths are accepted. TOML single-quoted strings are convenient for Windows paths.

```toml
[paths]
cache = 'D:\Caches\mote'
data = "~/Documents/mote-data"
traces = "../mote-traces"
```

To move the entire root, set `MOTE_HOME` before launch to an absolute path or a
path starting with `~/`. It changes where `config.toml` is read and the default
child destinations. It does not move existing files for you.

```powershell
$env:MOTE_HOME = 'D:\Settings\mote'
.\mote.exe
```

```sh
MOTE_HOME="$HOME/.config/mote" /Applications/mote.app/Contents/MacOS/mote
```

Ensure destinations are accessible. A failing trace/cache destination should not
prevent editing the document; a failed write is not evidence of successful
persistence or recovery.

## Reload and invalid settings

Choose File > Reload Settings on Windows or mote > Reload Settings on macOS.
Theme and preview groups can apply to the current window after composition has
settled. Cache/data/trace destinations and tracing enablement remain tied to
startup and require a fresh launch; the status notice says so. There is no
implicit file watcher or automatic migration of destination contents.

Unknown/invalid individual values produce diagnostics and retain defaults.
Malformed TOML or duplicate TOML keys reject the whole startup file; failed or
rejected reload retains the previous settings. The loader never rewrites your
file. Use UTF-8 (a UTF-8 BOM is allowed), and keep configuration below 1 MiB.

## Local diagnostic traces

Enable `[telemetry].enabled = true` and restart, or explicitly opt in for one
launch:

```powershell
$env:MOTE_TRACE = '1'
.\mote.exe .\notes.md
```

```sh
MOTE_TRACE=1 /Applications/mote.app/Contents/MacOS/mote ~/Documents/notes.md
```

The configured `[paths].traces` destination is authoritative in the application;
do not use environment variables as arbitrary trace-path overrides. To stop an
environment opt-in, unset `MOTE_TRACE`; configuration must also have tracing off.
Tracing is local and content-free, not uploaded analytics. Old sessions are not
deleted by a global automatic retention policy; inspect and remove unneeded
traces yourself. See [trace schema and limits](https://github.com/kleedaisuki/mote/blob/main/src/Mote.Telemetry/README.md).

