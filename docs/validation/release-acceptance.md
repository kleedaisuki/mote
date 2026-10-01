# Release acceptance: ordinary source tasks and independent artifacts

## Contract and current evidence

The release direction permits application packages with multiple files and keeps
Native AOT. This does not permit projects/workspaces, multiple simultaneously
editable documents, or weakening exact-text/history/Save contracts. Packaging
and native-source qualification are separate checks.

`tests/NativeReleaseProductWorkflow.ps1 -ExecutablePath <extracted executable>
-OutputDirectory <repository .cache/.temp child> [-RuntimeIdentifier <rid>]` is
the cross-platform release suite. Packaging should run it against the extracted
release archive on each of Windows x64/ARM64 and macOS x64/ARM64. This document
does **not** certify those hosted executions before artifacts are reviewed.

The suite is independent of the historical Canvas ancestry/input-island oracle.
Windows delegates real Win32 RichEdit edits/menu commands/fresh GUI reopen to
`Invoke-NativeWindowsReleaseProduct.ps1`. macOS uses the actual NativeSource
product shell/controller through additive in-process AppKit workflow and fresh
GUI-process reopen probes. Neither is a physical keyboard or real Pinyin test.

## Expected task outcomes

All samples are self-created, small representative task shapes, **not** authentic
user data or evidence of market frequency. They share exactly one editable
`mote-release-original` value; selection replaces it with
`mote-release-edited中`. The replacement stays valid in each structured format.
The marker is not an appended token that makes JSON invalid just to test Save.

| Format | Representative task shape | Independent invariant |
| --- | --- | --- |
| Markdown | Checklist owner, headings and Chinese note | Source surrounding owner unchanged |
| TOML | Nested profile/display settings | String value replaced; tables/scalars unchanged |
| JSON | Profile object and feature array | String value replaced; braces/array unchanged |
| YAML | Profile mapping and feature sequence | Value replaced; indentation/comments unchanged |
| CSV | CRLF records with comma/escaped-quote fields | Name changed; quoting and CRLF exact |
| Plain text | Short Unicode reading note | Ranged replacement; accents/emoji/newlines exact |

For each task the wrapper creates an original and computes the expected bytes
by an independent string replacement, before executing the product. It requires
native selection/edit, engine Undo/Redo, Save, and **a new GUI process** showing
the saved file. The original and saved outputs are checked byte-for-byte using
BOMless strict UTF-8; checking marker presence alone is insufficient. Future
encoding additions require separately specified bytes rather than assuming this
six-task set proves every codec.

The Windows task driver now exercises a real dirty-close dialog Cancel; the Mac
workflow calls the actual AppKit window close route, cancels the prompt, and
checks committed marked text/dirty state retained. This is a cancellation
witness, not acceptance of every Save/Discard/Cancel branch. Both drivers retain
`native-product.png` from their owned window/content view. The wrapper requires
a valid PNG signature/IHDR and dimensions at least 100 by 100, recording its
hash and dimensions. Image existence/format alone is not a pixel-quality oracle;
visual review is separately necessary.

The suite also requires deterministic CLI help/runtime readiness and invalid
multi-path/conflicting-profile/unknown-option exit 2 with stderr. Missing-file
GUI dialogs are not treated as CLI exit errors and are not silently included.

Configuration checks use child-specific `MOTE_HOME` and no global settings:
default tracing-off smoke creates no mutable home; a light-theme config enables
tracing to a relative custom directory; an environment opt-in overrides disabled
config. Config bytes must remain unchanged. This verifies loading/placement and
clean shutdown, **not** rendered theme pixels or nonexistent cache/data writers.

## Runtime evidence and limits

Every actual task opts into local JSONL and retains stdout/stderr, task originals,
outputs and result JSON. `release_trace_oracle.py` reuses the existing closed
schema and causal graph validator; it rejects unknown names/attributes, missing
parents, cycles, duplicate spans, drops, absent successful session shutdown,
unfinished records, and input/path/home sentinels. At least one individual
process trace must contain successful open/edit plus a complete native Save
request chain whose engine phases/completion agree with the captured version;
the existing causal Save reader supplies this oracle. Phase counts without
request ancestry or with mismatched versions do not pass. A clean
reopen-only process may have its own trace and is checked independently.

Successful telemetry means instrumented phases and callback return, not event
transport completeness, physical input, visible pixels, percentile UX or a
universal latency budget. The 90-second process bound is a hang watchdog only.
The existing 100 MiB/dense controls remain capacity/mechanism evidence, not the
ordinary-task demand or an automatic release experience gate.

Important remaining qualification limits: actual Pinyin composition/cancellation
via OS input methods, NVDA/VoiceOver reading behavior, remaining dirty-close
Save/Discard branches, physical rendering, broad codec cases and measured ordinary-task
responsiveness. Product documentation must identify unexercised paths rather
than converting a green aggregate into those claims. An actual data-loss or
incorrect Save failure is a blocker, not a benchmark caveat.

## Locally executed harness checks

On Windows, without starting a GUI or modifying OS/global settings:

```powershell
pwsh -NoProfile -File tests/NativeReleaseProductWorkflow.ps1 -SelfTest
python -B -m unittest discover -s tests -p test_release_trace_oracle.py -v
```

The fixture self-test exercises six independent expected-byte controls and a
negative comparison against every unedited original, plus an outside-artifact
path rejection. The Python tests exercise accepted separate edited/reopen
process traces and negative missing Save, unknown operation, content leak,
missing parent/terminal, unterminated row, cycle and duplicate identity cases.
These checks validate the harness, **not release binaries**. Hosted records and
their exact commit/archive identities are to be appended only after execution.

Executed on 2026-10-02: the PowerShell self-test passed all six positive/negative
byte controls and the outside-path rejection; its retained report is
`.temp/release-acceptance-selftest/release-acceptance.json`. The Python oracle
initial suite passed **6/6**, zero failures/skips, in 0.027 seconds; after adding
native Save request/version linkage controls it passed **7/7** in 0.033 seconds.
These are distinct harness revisions, not a fabricated combined count. A later
artifact-safety self-test at `.temp/release-acceptance-safe-selftest/` passed the
same fixture byte controls and rejected an actual Windows junction ancestor;
fresh output directories are required and existing evidence is not overwritten.
Python standard
library JSON/TOML/CSV readers additionally decoded the generated edited fixtures:
the JSON/TOML profile value equals the edited Unicode marker, and CSV yields
three exact records including the comma-containing Chinese cell and escaped
`He said "ready"` value. YAML and Markdown were not independently parsed by this
local harness check. No product GUI was launched.

## Prior knowledge reused

- [Independent standards audit](../research/editor-experience-standard-audit.md)
- [Task audit](../research/editor-standard-task-audit.md)
- [Oracle audit](../research/editor-standard-oracle-audit.md)
- [Product native-source binding](native-source-product-binding.md)
- [Product controller contracts](native-source-product-controller.md)
- [Native-source integration review](../reviews/native-source-product-integration-review.md)
