# Native TOML known-error publication

Status: implemented bounded production slice, local focused tests passed;
Windows/macOS real OS rendering and target CI acceptance remain separate.

## Contract

A Provisional idle Full TOML result may contain one proved `TOML_OWNERSHIP`
Error while its global diagnostic total remains unknown. The Native controller
merges that diagnostic only into the exact current document/driver/policy,
version, generation and viewport. Existing publication guards are unchanged.
The private visible frame pairs `NativeAnalysisView` with bounded
`NativeCanvasSemantics` and the viewport; it retains no source snapshot,
whole-source string or format tree. Every pre-existing visible-frame
invalidation site clears the pair atomically.

The merge preserves the existing projected highlight tokens, preview text,
source maps, Flow, source frame and layout. It deduplicates by code/span,
retains the 4,096 visible diagnostic cap, and refuses a new entry when the
existing batch is full rather than evicting another visible fact. Only
Provisional TOML ownership Errors qualify; offscreen errors, other codes,
warnings and unrelated policy partial results keep the previous behavior.
The summary reports observed errors alongside explicitly unknown global
counts. Reentrant source/viewport changes during native presentation prevent
old-version semantic overlay installation.

A separate single bounded `Diagnostic` witness, stamped with current
version/generation and document/driver/policy identity, survives page-only
navigation. It holds no `TextSnapshot`, rope, tree or source copy. The first
proved error is captured even when initially offscreen; normal Visible
publication reprojects it only when its exact source span intersects the
current viewport. Thus page-away/back does not lose a still-valid witness
or repeat Full scanning. Every source edit (including Undo/Redo), Open/New,
policy/driver replacement and disposal clears the witness. No offscreen
error is shown as a visible error, and no exact global count is invented.
This slice adds neither a diagnostic navigation panel nor a generalized
cross-page semantic cache.

## Local verification

`tests/Mote.Tests/TomlKnownErrorControllerTests.cs` uses the existing queued
fake native shell. Synthetic private publication seam tests challenge all
identity/version/viewport guards, code/span deduplication, narrow admission,
render payload reference identity, edit/Undo/New invalidation and a source
edit reentrant inside pane installation. Real format-session integration
uses >4 MiB TOML fixtures and the normal asynchronous Visible/idle-Full
pipeline: exact duplicate witness, repair, Undo, Redo, page-away/back,
an initially offscreen witness visited later, Open
with JSON policy replacement, and cancellation before idle publication.
These are controller integration tests, not actual RichEdit/AppKit input
or physical paint evidence.

Focused regression command (Release, warnings as errors):

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore `
  -p:TreatWarningsAsErrors=true `
  --filter 'FullyQualifiedName~Toml_known_error|FullyQualifiedName~Controller_idle_toml_provisional|FullyQualifiedName~Controller_idle_yaml_full'
```

The focused suite passed **22/22** locally (20 new controller cases plus
two existing regressions); its TRX is under
`.cache/toml-known-error-native/toml-known-error-native.trx`. After adding
an explicit serial/cancellation/frame recheck immediately after source
semantic installation and before the idle offer, a fresh Release
warnings-as-errors rebuild and the reentrant-edit case passed **1/1**.
The existing TOML partial-render preservation and YAML Complete idle
promotion tests guard the unchanged unrelated publication paths. Formats
proof obligations and independent differential evidence are maintained in
[semantic-policy-frontier.md](semantic-policy-frontier.md) and
[toml-large-semantics.md](toml-large-semantics.md).
