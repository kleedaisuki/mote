# mote user manual

This manual describes the integrated OS-native editor. Install the versioned
package using [Installation](installation.md); choose settings using
[Configuration](configuration.md). Release-specific qualifications and known
issues belong to the [v0.1.0 release page](../releases/v0.1.0.md).

## Open and edit a document

Start mote with no argument to create an untitled plain-text document, or pass
one file path. Use File > Open to choose another file. There is one editable
document per application process: no project discovery, folder workspace,
language server, or directory-wide indexing.

New, Open, and closing a dirty document follow the unsaved-change confirmation
workflow. Cancellation should leave the current document in place. Preview text
is not another independently editable document. Do not treat a status message
about background analysis as confirmation that a Save succeeded.

Source editing and Undo/Redo operate on the canonical document history. A parse
error is a diagnostic, not permission to rewrite or discard text. Use Find for a
literal whole-document text query, Find Next to advance, and Go to Line for whole-document
navigation. Search is not a regular-expression or workspace search facility.

## Keyboard commands

These shortcuts are taken from the native menu definitions. Menu commands remain
available when a shortcut is not assigned. Standard native source navigation and
paste behavior apply; composition and assistive-technology behavior are subject
to the release qualifications, not inferred from menu labels.

| Command | Windows | macOS |
| --- | --- | --- |
| New | Ctrl+N | Command+N |
| Open | Ctrl+O | Command+O |
| Save | Ctrl+S | Command+S |
| Save As | Ctrl+Shift+S | Command+Shift+S |
| Undo | Ctrl+Z | Command+Z |
| Redo | Ctrl+Y | Command+Shift+Z |
| Select All | Ctrl+A | Command+A |
| Copy / Cut / Paste | Ctrl+C / Ctrl+X / Ctrl+V | Command+C / Command+X / Command+V |
| Find | Ctrl+F | Command+F |
| Find Next | F3 | Command+G |
| Go to Line | Ctrl+G | Command+L |
| Format Document | Ctrl+Shift+F | Edit menu |
| Reload Settings | File menu | mote menu |

Windows uses F6/Shift+F6 to move between available panes; Tab remains an editing
key in source. Previous/Next Page commands are retained for the legacy page
profile, not a requirement to navigate an ordinary full-source document.

## Formats and previews

Policy selection uses the filename extension. Unknown extensions, including
`.jsonc`, use plain text. Saving under a supported extension can change the
selected policy; source text remains authoritative.

| Format and extensions | Analysis / view | Format Document scope |
| --- | --- | --- |
| Markdown: `.md`, `.markdown`, `.mdown` | Parsed headings, references and blocks; native rendered-text preview | Extra spacing after nonempty ATX heading markers only, with rendered-equivalence check |
| TOML: `.toml` | TOML 1.1 syntax and source-order ownership diagnostics; structure view | Horizontal whitespace next to proven assignment tokens, with semantic revalidation |
| JSON: `.json` | JSON structure, syntax and duplicate-key diagnostics | Indented JSON output; invalid content is retained |
| YAML: `.yaml`, `.yml` | YAML 1.2 Core Schema structure, aliases and key-equality diagnostics; unsupported canonical forms are warnings | Proven same-line mapping separator gaps only, with semantic revalidation |
| CSV: `.csv` | Quote-aware logical records, cells and width diagnostics; source-backed grid | Re-emits logical records with necessary quoting; table edits quote values as required |
| Plain text: all other extensions | Source text, without language diagnostics | No language beautification |

Formatting is deliberately narrower than a general-purpose beautifier. It may
leave a document unchanged. It is an explicit edit, can be undone, and is not
performed automatically during Save. CSV does not assume its first row is a
header; inconsistent record width is a warning. In the table, use Replace cell
(F2 on Windows) to edit a decoded cell through the canonical source; ordinary
table text is not a second editing buffer.

Markdown preview is native text, not a browser. Raw HTML and unsafe URI schemes
are not executable document features. Opening Markdown does not authorize
running scripts or loading arbitrary document code.

### Completeness and background work

Analysis is versioned and may run after an edit. A result for an old version must
not replace a newer document's result. Larger or structurally difficult input
can be **Provisional** (not globally checked), **CoveredRegion** (a defined
source region checked), or **Complete** (the policy's global check completed).
A provisional result with no visible error does **not** mean the whole file has
zero problems. Some resource-limited files remain provisional even after an idle
full-analysis request. Structure previews can be bounded rather than enumerating
all nodes in a giant file.

The default preview convention is full-width plain text and split source/preview
for structured formats; release profile-specific behavior is recorded on the
release page. `[editor].preview` can select `auto`, `source`, or `split`.

## Encodings

Ordinary Open uses recognized Unicode byte-order marks and strict UTF-8 for
unmarked text. Invalid UTF-8 is not silently replaced. For known legacy text,
choose File > Open with Encoding and explicitly select the codec.

Supported codec choices include UTF-8, UTF-16 little/big endian, UTF-32
little/big endian, GBK (Windows code page 936), GB18030 (.NET code page 54936),
and Big5 (Windows code page 950). A selected codec conflicting with a recognized
BOM is rejected. Non-invertible legacy mappings are rejected rather than silently
altering the original bytes. GB18030 support is the shipped .NET mapping, not a
claim of every revision of the standard.

Save retains the document's selected encoding and compatible BOM. An edit that
cannot be encoded exactly fails rather than using best-fit substitution. Keep an
independent original when investigating unfamiliar encodings; mote does not
automatically guess a legacy Chinese encoding or offer a general transcoding UI.

## Save and recovery

Save writes an explicit captured document snapshot. Later edits can leave the
current document dirty even after that snapshot was saved. Save As to an existing
target requires overwrite approval tied to a captured target fingerprint. A
changed external target is rejected; do not repeatedly overwrite a file another
program is modifying.

A Save failure is not universally proof that the target stayed unchanged. The
error reports what could be inspected: original bytes, saved bytes, other
content, missing target, or unknown. Keep the window open and read the message.
The application does not automatically retry a possibly committed replacement.

Atomic staging must be beside the target, on its filesystem. A failed commit
can leave a `.mote-save-<hash>.recovery` file there. This is an explicit exception
to the `~/.mote` convention: it contains document content, not cache or telemetry.
It may inherit directory permissions rather than the original file's metadata.
Protect sensitive document directories accordingly.

When a complete owned recovery snapshot is retained, the next Save/Save As
command offers export to a **new, distinct** path. Export preserves the captured
failed-save snapshot, not necessarily your newest edits. It does not associate
the export path with the document or mark the current buffer saved; explicitly
Save again when ready. Cancel leaves the recovery copy in place.

A pre-existing recovery slot blocks another Save rather than overwriting it.
After a restart, recovery files are unowned and unverified; mote does not silently
adopt, trust, or delete them. Inspect/copy them before explicitly removing one.
Incomplete or inaccessible recovery bytes require manual inspection; an error
message does not certify that they contain a whole document. Closing/New can
leave recovery bytes in place and still require separate dirty-buffer consent.

There is no automatic crash-recovery catalog or autosave promise. Filesystem
filters, concurrent writers and power loss remain distinct from tested live
Save outcomes. Keep normal backups for important data.

## Settings and privacy

Edit `~/.mote/config.toml`, then invoke Reload Settings to apply live appearance
and preview changes. Writer destinations and telemetry opt-in changes require
restart. Invalid settings are reported rather than rewriting your configuration.
See the [configuration examples](configuration.md).

Tracing is **off by default** and remains local when enabled. Fixed-schema JSONL
records contain operation names, timings, IDs, coarse size/format information,
versions, counts and limited Save error numbers; they contain no document text,
full filename/path or free-form exception message. There is no network uploader.
This is still diagnostic metadata: review trace files before sharing them.

## Known limitations

- v0.1.0 qualification and the release-default source profile must be confirmed
  on the versioned release page; diagnostic flags are not alternate supported
  products.
- The Windows and macOS packages are not publisher-signed; macOS is not
  notarized. Security prompts and installation are real user-facing limitations.
- Complete semantics are not exhaustive independent conformance certification.
  Deep, dense, large or recovery-uncertain structures can remain provisional.
- Large-file tests are capacity evidence, not universal startup, memory, typing,
  or 100-MB usability guarantees. Long physical lines and full-native text
  residency can be particularly expensive.
- Real CJK composition and attended screen-reader behavior need platform-specific
  evidence. A smoke launch, synthetic text injection, palette contrast check or
  accessibility tree is not proof of all IME/assistive-technology combinations.
- The full-native-source candidate cannot edit embedded NUL safely in its native
  text surface; canonical content is retained and the surface becomes protected
  rather than silently truncating it. Follow its recovery notice.
- Preview is bounded native rendering, not browser feature parity. No workspace,
  extension marketplace, language server, regex search, or automatic updater is
  advertised.

## Reporting a problem

Use [GitHub Issues](https://github.com/kleedaisuki/mote/issues). Include release
version, OS version, CPU architecture, selected source profile if nondefault,
format, approximate file size and shape, steps, expected result and actual
result. For Save failures include the displayed outcome/error number and whether
a recovery slot exists; do not post private paths or content unnecessarily.

If safe, attach a minimal redacted reproducer. Optional local traces help identify
which runtime stage occurred, but they do not prove physical display timing or
capture your complete editing history. Do not send passwords, proprietary files,
private recovery snapshots or signing credentials.
