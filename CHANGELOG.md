# Changelog

User-visible release history. Final release entries refer to published tags and
matching assets; engineering test counts and transient CI investigations belong
in validation documents, not here.

## Unreleased

No post-v0.1.0 changes recorded yet.

## 0.1.0 — 2026-10-02

Public download availability is determined by the
[versioned release page](docs/releases/v0.1.0.md) and its GitHub release assets.

### Added

- Single-document native desktop editing for Markdown, TOML, JSON, YAML, CSV and
  plain text, with canonical engine history, whole-document Find/Go to Line and explicit file operations.
- Parsed format diagnostics and source-backed native previews, including CSV
  cell replacement through source edits.
- Conservative formatting, strict Unicode round-tripping and explicit GBK,
  GB18030 and Big5 open choices.
- Compile-time theme policies, restrained built-in palettes and validated local
  color overrides.
- User-editable `~/.mote/config.toml`, independent directory overrides, reloadable
  appearance/preview settings, and opt-in local content-free JSONL tracing.
- Explicit retained Save snapshot recovery/export and external-target checks.
- Product landing page, installation guide, user manual and versioned release
  documentation.

### Presentation

- Ordinary launch uses the full-native source editor. `--native-source` remains
  an explicit equivalent; additive `--continuous` preserves the earlier canvas
  route, and `--legacy-page` preserves the historical native page route.
- Presentation is fixed for the window lifetime; file size does not switch a
  dirty document or active composition into another representation.

### Delivery

- Native AOT remains the execution model; users need no .NET installation.
- Application delivery may contain multiple files or a macOS `.app` bundle.
  This does not introduce workspaces or multi-document editing sessions.

### Known limitations

Unsigned platform trust, analysis resource limits, real IME/reader coverage, and
large-file capacity remain explicitly scoped in the
[versioned release page](docs/releases/v0.1.0.md). Hosted qualification does not
certify every composition, assistive-technology or physical display workflow.
