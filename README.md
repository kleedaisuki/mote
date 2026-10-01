# mote

**Open a file, not a workspace.**

mote is a single-document desktop editor for Markdown, TOML, JSON, YAML, CSV,
and plain text. Read, inspect structure, fix a local mistake, and save—without
creating a project or starting a language server.

- **Text first:** parsed structure, diagnostics, and previews refer to the same
  document; a parse error does not erase your edits.
- **Local by default:** no account, service, or telemetry upload. Settings belong
  in `~/.mote`, not in the directory of the document you opened.
- **Native AOT:** platform-specific builds contain their runtime; users do not
  need to install .NET. Application packages may contain multiple files.
- **Restrained themes:** dark, light, system-following, and high-contrast-dark
  policies, with validated user color overrides.
- **One editing document:** multiple delivery files do not mean projects,
  workspaces, or multi-file editing sessions.

## Download and start

[Releases and downloads](https://github.com/kleedaisuki/mote/releases) ·
[Installation](docs/user/installation.md) · [User manual](docs/user/manual.md) ·
[Configuration](docs/user/configuration.md) · [Release notes](CHANGELOG.md)

**Release preparation:** v0.1.0 is being qualified. The versioned
[release page](docs/releases/v0.1.0.md) explicitly separates planned artifacts
from published downloads. A build or green unit-test suite is not itself a
published release. macOS builds are unsigned and not notarized; read the
installation instructions before downloading.

```powershell
# Windows, after extracting a release package
.\mote.exe "C:\Users\Ada\Documents\notes.md"
```

```sh
# macOS: see Installation for the selected release package's entry point
mote ~/Documents/notes.md
```

No argument opens an untitled document. File > Open replaces the active document
only through the application's unsaved-change workflow.

## What to expect

Format policies provide source-aware analysis rather than lexical coloring
alone. Markdown has a native text preview; structured formats expose structure;
CSV has a source-backed table. Format Document is deliberately conservative:
it is not a general beautifier and does not guess how to repair invalid input.
See the [format and editing guide](docs/user/manual.md#formats-and-previews).

The immediate product focus is ordinary structured files and few-MiB text
reading. Large-file stress tests describe capacity limits, not a promise that
100 MB is a normal task or that every document opens instantly. Composition,
assistive technology, and performance claims are limited to the workflows
qualified for each release; see [known limitations](docs/user/manual.md#known-limitations).

## Development

Install the .NET 10 SDK. From the repository root:

```sh
dotnet build mote.sln
dotnet test mote.sln
```

Publish Native AOT on the matching Windows or macOS host. Production tests and
release packaging run through GitHub Actions; no .NET SDK is needed by users.

| Area | Responsibility |
| --- | --- |
| `src/Mote.Engine` | Canonical text, document lifetime, edit history, file I/O |
| `src/Mote.Formats` | Compile-time format policies and semantic projections |
| `src/Mote.Native` | OS-native Windows/macOS application shell |
| `src/Mote.Desktop` | Avalonia interaction prototype; not the release application |
| `src/Mote.Configuration`, `src/Mote.Themes` | Typed local settings and presentation policies |
| `src/Mote.Telemetry` | Opt-in, content-free local traces |

[Architecture](https://github.com/kleedaisuki/mote/blob/main/docs/architecture.md) · [Product release scope](https://github.com/kleedaisuki/mote/blob/main/docs/product/release-scope-2026-10-02.md) ·
[Engineering validation records](https://github.com/kleedaisuki/mote/tree/main/docs/validation) · [Release maintenance](https://github.com/kleedaisuki/mote/blob/main/docs/releases/README.md)

## License

GPL-3.0; see [LICENSE](LICENSE). Release packages and their corresponding source
must remain traceable to the same version.

