# First hosted macOS Grid external AX target audit

Date: 2026-10-01. Independent evidence audit; no production or helper modification.
Verdict: **both native targets executed the external client and failed the same
origin-row label predicate. Neither target passed external Grid AX acceptance.**

## Contract and evidence

Expected behavior comes from the [bounded Grid contract](../csv-grid-accessibility-contract.md)
and [external harness contract](mac-grid-external-ax.md), not from current output:
record 1 remains data; its absolute ordinal is Row 1; source and semantic Table
coexist; selection/navigation/retirement and normal close require their own checks.

Hosted run: [36786762929](https://github.com/kleedaisuki/mote/actions/runs/36786762929),
checkout `1448ce45c22528d4226caa43a0bcc6b3e50e590b`. Raw evidence (repository-relative):

- `.cache/ci-36786762929-mac-grid-ax/arm-artifact/mac-grid-ax-external.json`
- `.cache/ci-36786762929-mac-grid-ax/x64-artifact/mac-grid-ax-external.json`
- `.cache/ci-36786762929-mac-grid-ax/osx-arm64.log`
- `.cache/ci-36786762929-mac-grid-ax/osx-x64.log`
- `.cache/ci-36786762929-mac-grid-ax/run.json`

Report SHA-256, independently computed from downloaded bytes:

| Report | SHA-256 |
| --- | --- |
| ARM | `9FEE9A4520224948917805F6460EB9D21BE36E81D67132FD8000FAA050FF2FE0` |
| x64 | `ACCC8E0B82836526898192FA6C76A9663E6D1CD8F8AF09D71EBFE9DB0E8413B9` |

Both logs show this checkout, fresh matching-RID `dotnet publish` with Native AOT,
publish inventory/strict single-file checks, and pinned helper hashes before the
ordinary-open external probe. The report records one-file inventory and exact binary
hashes. Binary bytes were not downloaded/rehashed in this audit: binary attribution
is the hosted fresh-publish chain plus reported hashes, not a local executable test.

`git show 1448ce4:tests/MacGridAxExternalProbe/Probe.swift` bytes independently hash to
`0CB3E56BF68EBB22DA13316FE9AF1520D055DF0E41433F699359BCAC5E8FF36D`;
`Run.ps1` bytes hash to
`0A867687F62795790989B20A347A70EBEB93888923A666157DB45F5F5F816CEA`.
Both match report and workflow log pins. The working-tree helper has subsequently
changed; it is not the source of this first-target verdict.

## Observed results

| Fact | osx-arm64 | osx-x64 |
| --- | --- | --- |
| OS / host architecture | macOS 26.6.2 / Arm64 | macOS 15.7.9 / X64 |
| AOT binary bytes | 16369992 | 16722464 |
| AOT SHA-256 | `CD3FD2A82F5702C355FFA17EA5AB69328A2830AF987EC9C37540EDC03A9D3ABE` | `8293C7AA35742CC0393E6C332096FFA44865835897CA6D71C123FA7058D70DD3` |
| Editor / separate client PID | 7992 / 7993 | 10553 / 10554 |
| Swift typecheck / trust | true / true | true / true |
| Client exit | 1 | 1 |
| Last phase / first falsifier | origin-window / first-record-is-data | origin-window / first-record-is-data |
| Prior checks passed | 8 | 8 |
| Admission count (not API count or benchmark) | 2268 | 1485 |
| Close attempted / normal exit | false / null | false / null |
| Forced editor cleanup / cleanup error | true / empty | true / empty |
| Fixture unchanged | true | true |

Both use the identical independently specified fixture: 1100 records, maximum 24
columns, 263760 UTF-16 units; SHA-256
`8BACC6F97A374853C5F04EE176384738D8BFF7F71B1C7E768D088310C98DD8C2`.
The wrapper rehashes after owned-child cleanup. This proves unchanged fixture bytes
for these runs, not edit/Save behavior.

The eight completed named checks, in order, are:

1. `separate-editor-pid`
2. `one-opt-in-grid-table`
3. `one-semantic-table-without-default-duplicate`
4. `source-document-coexists`
5. `source-full-fixture-count`
6. `source-selection-readable`
7. `source-offscreen-exact`
8. `bounded-counts-match-arrays`

Thus the native SDK accepted and compiled the external Swift client; AX access was
trusted, not permission-blocked; it discovered the single semantic Table without a
second default Table and independently readable full source. Bounded row/column
array counts agreed with their reported counts. These are meaningful partial
facts, not complete semantic acceptance.

## Exact failing predicate and diagnosis boundary

At the tested commit, `Probe.swift` evaluates:

```swift
try require("first-record-is-data", try label(rows[0]) == "Row 1")
```

Its `label` function returns any nonnil string `AXDescription` first and only then
tries `AXTitle`. The production `AccessibilityNodeRead` callback supplies
`accessibilityLabel` as `Row {frame.Rows.Start + localRow + 1}` for row nodes;
`accessibilityIndex` separately supplies the local index. The first-target report
contains neither attribute error/type/label classification nor local index for the
failing node. Therefore the check name does **not** establish that record 1 was
consumed as a CSV header, that the window starts at Row 2, or that ordinal arithmetic
is wrong. It also does not establish a correct externally transported Row 1 label.

A material unresolved distinction is:

- provider exposes incorrect/missing semantic row identity through external AX; or
- AppKit transports the intended label differently, or supplies a generic nonnil
  description which the helper chooses over another correct attribute.

This is a mechanism hypothesis from the static selector/helper mismatch, not a
native observation. Distinguish it with bounded content-free attribute facts on
only the first row and column (errors/types/exact known ordinal classifications,
local numeric index and expected custom-wrapper identifier), retaining the Row 1
oracle unchanged. Do not substitute API success, a local index, a role description
or a permissive substring for the required absolute ordinal.

No product/provider root cause is assigned by this audit. No production workaround
is justified by the first report alone. Both RIDs reproducing the same falsifier
reduces the chance of an architecture-specific failure, but is not repeated-run
statistics and does not identify the bridge behavior.

## What is not verified

Execution aborted before ordinal header counts/labels, exact first-cell value,
Complete-empty versus Missing, cell range/parent identity, selection setters,
logical Go To navigation, retained-node retirement, and normal close. No successful
AXClose or normal process exit exists; owned `Kill()` cleanup is reported separately
and cannot satisfy it. No VoiceOver speech, physical IME, geometry/hit testing,
notification delivery, whole-file virtualization or performance acceptance follows.

Both raw logs end this diagnostic with `Process completed with exit code 1`
(ARM line 1546; x64 line 1464).
GitHub job/step summaries present success under `continue-on-error`; that presentation
must not override nested `status: failed` and the raw exit. In-process selector
checks passed separately, but do not certify the external bridge.

## Reproducible independent inspection

On the Windows audit host in `D:\Code\mote` (Python available):

```powershell
# Read downloaded reports and logs; no native Mac runtime is exercised locally.
Get-Content .cache/ci-36786762929-mac-grid-ax/arm-artifact/mac-grid-ax-external.json
Get-Content .cache/ci-36786762929-mac-grid-ax/x64-artifact/mac-grid-ax-external.json
git show 1448ce4:tests/MacGridAxExternalProbe/Probe.swift
git show 1448ce4:src/Mote.Native/Mac/MacCsvGrid.Accessibility.cs
```

Exact helper attribution was verified using Python `subprocess.check_output` on
`git show` (raw bytes, avoiding PowerShell text re-encoding), `hashlib.sha256`, and
JSON parsing of both reports. Raw log checkout/publish/pins, wrapper exit and
cleanup handling were cross-checked against the tested commit. No native rerun,
production edit, helper edit, staging, commit or push was performed for this audit.
