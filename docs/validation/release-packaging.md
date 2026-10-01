# Release packaging implementation and evidence

Date: 2026-10-02. Scope: multi-file delivery with Native AOT, no user-installed
.NET, exact source/version provenance, four Windows/macOS architecture assets.
This document records **implementation and local contract tests**, not a published
release or hosted qualification result.

## Implementation

- `packaging/release_package.py` owns native executable inventory, ZIP/app TAR
  assembly, safe extraction and complete byte verification. Windows gets a
  portable versioned folder; macOS gets `mote.app` with Info.plist, executable and
  resources. There are no payload symlinks. TAR preserves executable bits.
- Source SHA must equal checkout HEAD, and affected tracked source must be clean.
  Canonical versions and full SHA identifiers cannot introduce refs/path fragments.
  Source project version and packaged CLI version are independently gated by CI.
- Header checks enforce PE32+ x64/ARM64 or little-endian 64-bit Mach-O x64/ARM64;
  PE CLR data directories are rejected. Header validity alone is not executable
  correctness/AOT proof; the pinned publish invocation and real runtime probes
  supply the complementary evidence.
- Every listed file is checked for exact size/hash, required licenses/docs and
  complete enumeration. Missing/extra files, duplicate case-folded names, links,
  wrong architecture, source/version mismatch or changed bytes fail validation.
- Output/extraction paths stay inside repository `.cache`/`.temp`, resolved before
  writes; existing version outputs cannot be replaced. Extraction rejects path
  traversal, drive/absolute paths, links/devices and duplicate members.
- `packaging/release_index.py` re-extracts/reverifies exactly one archive and one
  matching external manifest for each RID, requires the same workflow run/source,
  archives the exact Git tree and emits the nine download assets, release index
  and checksums. Verification trees never enter public download assets.
- `.github/workflows/release.yml` uses read-only permission, workflow_call/dispatch,
  immutable inputs, two full-solution test hosts, four matching native runners,
  packaged version/runtime/GUI initialization, existing reviewed Windows PE static-import allowlist and macOS system-path import checks, the actual six-format native
  workflow/fresh-process reopen, seven embedded codec checks, and unchanged bytes
  after probes. Only successful RID artifacts enter all-four consolidation;
  failure evidence remains separate and is retained with always(). No publishing,
  release overwriting, signing or notarization is attempted.

## Dependency licensing evidence

Production Native references Mote.Engine/Formats/Configuration/Themes/Telemetry.
The Formats package references and restored net10.0 NuGet nuspec dependency groups
were inspected: Markdig1.3.2, SharpYaml3.13.1 and Tomlyn2.10.1 have no transitive
runtime NuGet dependencies in this group; Configuration references the same Tomlyn.
Their original license texts were retrieved from the precise repository commits
recorded in nuspec (listed in THIRD-PARTY-NOTICES.txt). .NET runtime original MIT
license and conservative upstream third-party inventory are from v10.0.11.
SDK10.0.400 is pinned so that inventory does not silently drift. The Desktop
Avalonia prototype is not linked by Native; its graphics/text DLL license table
would be misleading here. OS-provided RichEdit/AppKit/fonts are not redistributed.

The matching exact-source Git archive is included beside GPL binaries, and the
release source retains project references, build scripts and dependency versions.
This implementation is not a legal opinion or claim that every optional component
in the upstream runtime notices is actually linked.

## Local verification actually performed

```text
python -B -m unittest discover -s tests -p test_release_packaging.py -v
15 test methods passed; 0 failures or skips (2026-10-02).
```

Evidence: `.cache/release-packaging-validation/unit-tests.txt`.
The four architecture roundtrips use **header fixtures, not runnable binaries**.
Tests cover archive/layout/full inventory, changed hash, extra file, mismatched
source/architecture, missing packaged document link, CLR fallback, overwrite refusal, output escape, duplicate
inventory, traversal/symlink extraction, invalid identities, missing RID set and
complete consolidation checksum enumeration. Git source archival is mocked only
in the consolidation unit test; actual git archive remains a hosted gate. Python
YAML parsing confirmed the workflow structure/four-RID matrix; all eight inline PowerShell blocks parsed without syntax errors. No hosted execution
is inferred from parsing. No local GUI, platform settings, clipboard or user files
were changed; no workflow was dispatched by this task.

## Platform trust and acceptance limits

The macOS bundle declares15.0 minimum; workflow records actual runner OS and Mach-O
load commands/deployment target. That does not certify every supported OS version.
No Apple publisher certificate/notary credentials are requested. Native AOT can
carry linker/ad-hoc signatures; the release accurately says **no publisher
signature or notarization**, rather than claiming all Mach-O signatures absent.
Unsigned Windows may trigger SmartScreen/Smart App Control; macOS quarantine and
Gatekeeper may prevent ordinary downloaded startup. Packaging is not platform trust. PE/Mach-O import inspection does not prove dynamic library lookup safety; that remains source review and native runtime evidence. Future private native companions need an explicit reviewed allowlist change, not an automatic multi-file exception.

Six-format probes are native surface/message/callback runtime evidence, not real
Pinyin input, physical keyboard delivery, NVDA/VoiceOver, screenshot pixel accuracy,
physical first paint or a universal latency SLA. Existing capacity/Canvas diagnostic
work remains separate and is not deleted or relabeled as ordinary user demand.

## Publication protocol

The root owner must freeze commit/version/default profile and run the workflow.
Only a complete successful release-assets job for the intended commit is eligible.
Before upload, inspect run/job evidence and verify checksum/index identities. A
release tag must resolve to the exact qualified source; create a new draft release,
never overwrite existing version assets. The root owns public notes/manual/product
page updates and final publication; this agent does not commit, push or dispatch.

References: [Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/),
[workflow dispatch](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_dispatch),
[.NET10 supported OS](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).


