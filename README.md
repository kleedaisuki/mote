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

**Current release status:** the integrated OS-native editor—not the Avalonia prototype—combines the engine, format policies, and native editing/rendering. At `5d0fcb6`, [CI 36863766458](https://github.com/kleedaisuki/mote/actions/runs/36863766458) passes all ten strict jobs: main Windows/macOS suites each **3218/3218**, Themes **14/14**, Configuration **9/9**, and **one Native AOT executable with zero sidecars** on win-x64, win-arm64, osx-x64 and osx-arm64. Hosted tests establish bounded native open/edit/save/reopen workflows; ordinary 1/100 MiB JSON inventories retain eight successful Save/reopen samples. TOML's valid-tree certification is integrated without weakening the invalid-input recovery contract.

This is **not release readiness**. Both Windows owned-HWND Grid focus tests pass, while the unchanged external Grid probe still fails on both architectures; one does not certify the other. New focus-boundary provenance is being integrated and is not covered by the pushed checkpoint above. Arbitrary large-file semantic completeness, real IME/attended screen-reader coexistence, physical presentation and native tail latency remain open. macOS builds have no publisher Developer ID signature or notarization; there is no Apple Developer account, and CI launch success does not establish Gatekeeper acceptance for a quarantined download. See [release gaps](docs/release-gaps.md), [latest scoped integration evidence](docs/validation/toml-tree-certification.md#hosted-clean-tree-integration--ci-36863766458), and [single-binary feasibility](docs/single-binary-feasibility.md).

## License

GPL-3.0; see [LICENSE](LICENSE).
