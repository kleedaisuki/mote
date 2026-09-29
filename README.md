# mote

**Open a file, not a workspace.** mote is a single-document desktop editor for Markdown, TOML, JSON, YAML, CSV, and plain text. Its C# engine owns text, document lifetime, file I/O, and editing; statically registered format policies own interpretation. The release target is one Native AOT binary per Windows or macOS architecture.

## Product contract

- One editable document at a time. Opening another file does not create a project, workspace, or language-server session.
- Format-aware behavior is based on parsed structure and diagnostics rather than coloring words alone. Policy modules compile into the application; they are not dynamically loaded plug-ins.
- Text is authoritative. A failed parse must never discard an edit or corrupt a save.
- The UI thread must remain responsive while expensive analysis runs. Versioned analysis results must not overwrite a newer edit.
- File content and encoding must round-trip without silent loss. Save must not destroy the previous file on failure.
- The shipped application must be one on-disk executable with no bundled native companion libraries. System OS libraries are permitted.
- Configuration, traces, and caches default under `~/.mote`; typed user configuration overrides conventions and may relocate data directories.
- Themes are statically registered presentation policies independent of format semantics.

## Repository layout

| Path | Responsibility |
| --- | --- |
| `src/Mote.Engine` | Text model, edit history, line access, and file I/O |
| `src/Mote.Formats` | Statically registered format policies and semantic projections |
| `src/Mote.Native` | Primary OS-native Windows/macOS shell targeting one AOT binary |
| `src/Mote.Desktop` | Avalonia interaction prototype; not release-compliant because of native sidecars |
| `src/Mote.Telemetry` | Opt-in, privacy-preserving performance tracing and JSONL persistence |
| `src/Mote.Configuration` | `~/.mote` defaults, typed overrides, and path resolution |
| `src/Mote.Themes` | UI-neutral, statically registered presentation palettes |
| `tests` | Behavioral and performance regression tests |
| `docs` | Architecture, evidence, and design decisions |

See [the architecture document](docs/architecture.md) for contracts, invariants, and performance trade-offs.

## Building

Install the .NET 10 SDK. Run `dotnet build mote.sln` and `dotnet test mote.sln` from the repository root. Native AOT output is platform-specific: publish `src/Mote.Native/Mote.Native.csproj` on a Windows host for Windows and a macOS host for macOS. GitHub Actions exercises both platforms.

Performance tracing is opt-in. See [telemetry documentation](src/Mote.Telemetry/README.md) for configuration and the JSONL event schema; document text and full paths are not recorded.

**Current release status:** the Avalonia prototype publishes additional native DLLs on Windows and dylibs on macOS, so it is not the release executable. The OS-native shell is under construction. A small macOS Native AOT/AppKit probe has produced one Mach-O and opened a text window on both arm64 and x64, but this does not yet prove the integrated editor, IME, accessibility, Finder integration, or notarized delivery. See [the feasibility record](docs/single-binary-feasibility.md). Passing a headless `--check-runtime` probe is not GUI validation.

## License

GPL-3.0; see [LICENSE](LICENSE).
