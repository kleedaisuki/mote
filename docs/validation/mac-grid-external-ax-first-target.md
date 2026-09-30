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

## Second hosted target: bounded metadata discrimination (2026-10-01)

[Run 36787947202](https://github.com/kleedaisuki/mote/actions/runs/36787947202),
checkout `5977250330fd47a633b95692b5a3ed8e1c988495`, retains the original Row 1
predicate and adds only bounded content-free first-row/first-column attribute
observations. This is a new native experiment, not a rerun-until-pass claim.

Evidence:

- `.cache/ci-36787947202-mac-grid-ax/arm-artifact/mac-grid-ax-external.json`
- `.cache/ci-36787947202-mac-grid-ax/x64-artifact/mac-grid-ax-external.json`
- `.cache/ci-36787947202-mac-grid-ax/osx-arm64.log`
- `.cache/ci-36787947202-mac-grid-ax/osx-x64.log`

The auditor downloaded the two complete job logs with `gh run view 36787947202
--job 110133758823 --log` (ARM) and `--job 110133758767 --log` (x64), writing only
inside this evidence directory. Logs verify exact checkout, fresh matching-RID
AOT publish, strict inventory and helper pins. Python raw-byte `git show 5977250:...`
hashing verifies the Swift helper
`A65E460046E0AA72B88F5E4289CAA0915A4EE15E1C24984B9C40DC6FD1227B8C`
and unchanged driver
`0A867687F62795790989B20A347A70EBEB93888923A666157DB45F5F5F816CEA`
against both reports and log pins. The tested helper's classification code compares
exact expected role/ordinal and parses bounded custom wrapper identifier shape;
it does not record raw strings or reinterpret a local index as an absolute label.
As in the first audit, binary attribution uses the hosted chain and hashes;
no executable was downloaded/rehashed or run on the Windows audit host.

| Fact | osx-arm64 | osx-x64 |
| --- | --- | --- |
| Report SHA-256 | `5F740742E4075BEE5986269477718B2C735D0A465AD9FDC574F64743597224DE` | `9C84412B5971C9316AAE487544AD2F52C60B03092DB0589DED92BEF615D8469E` |
| AOT SHA-256 | `21A6D32444F52F3FEC9C7E78AB4F3EA1DE3578166543291A89EB35D6DCA3CF7D` | `84D4A00CB483F72011D2FBF701C3B0A847E85C26B9E4032CCD4940A0B2C69A38` |
| Binary bytes | 16370776 | 16727360 |
| Editor / client PID | 6990 / 6991 | 9072 / 9073 |
| Swift typecheck / trust | true / true | true / true |
| Passed checks / first failure | same 8 / first-record-is-data | same 8 / first-record-is-data |
| Admission count | 2282 | 1904 |
| Client exit / last phase | 1 / origin-window | 1 / origin-window |
| Probe close / normal exit / forced cleanup | false / null / true | false / null / true |
| Fixture unchanged / cleanup error | true / empty | true / empty |

Both fixtures retain the same expected hash, 1100 records, maximum 24 columns and
263760 UTF-16 units as the first run. OS/architectures remain macOS 26.6.2 Arm64
and macOS 15.7.9 X64. Raw diagnostic exits remain 1 despite non-gating job success.

### New external observations

Python JSON comparison finds the complete **16-observation arrays identical**
across RIDs, not merely the same final check name. Each node was queried for eight
fixed attributes. The following is observed, not inferred:

| Attribute | First AXRows node | First AXColumns node |
| --- | --- | --- |
| AXRole | success; exact AXRow, UTF-16 length 5 | success; exact AXColumn, length 8 |
| AXRoleDescription | success; unclassified string, length 9 | success; exact generic axis-role string, length 6 |
| AXDescription | absent; error -25205 | success; exact Column 1, length 8 |
| AXTitle | absent; error -25205 | absent; error -25212 |
| AXIndex | success; bounded numeric index 0 | success; bounded numeric index 0 |
| AXIdentifier | absent; error -25212 | success; expected-window-axis-identifier, length 29 |
| AXValue | absent; error -25205 | absent; error -25212 |
| AXHelp | absent; error -25205 | success; unclassified string, length 52 |

The tested source's custom row/column callbacks share the label, identifier and
help machinery. In particular, the row callback should provide `Row 1`, a custom
`mote.csv.window...row.0.-1` identity and help; the custom column callback should
provide `Column 1` and corresponding column identity. The column exposes the
expected custom-wrapper shape externally, whereas the row exposes only role and
local index with none of those custom semantic attributes.

**Strongly supported mechanism inference:** native `NSTableView`/AppKit AX row
transport is substituting a native row (or otherwise bypassing the custom row
wrapper), while custom column wrappers survive. This is substantially better
supported than the first-run generic label-precedence hypothesis: here neither
row Description nor Title contains any string, so a nonnil generic Description
cannot be shadowing a correct Title in this run. The local index 0 also gives no
evidence for a Row 2 label or CSV header consumption.

**Not direct proof:** the external report does not expose internal object/class
identity, trace callback entry or the exact AppKit bridge path. Attribute omission
could still arise from a row-specific bridge policy rather than literal object
replacement. Treat native-row substitution as a high-confidence diagnostic
hypothesis, not a demonstrated private-framework implementation detail. The
experiment does establish a product-visible semantic contract failure: the first
externally enumerated row lacks the required absolute Row 1 identity, irrespective
of whether arithmetic inside the custom callback is correct.

The next discriminating investigation is row transport/custom identity and a
bounded native adaptation that preserves the existing Table, source/input island
and local-index semantics. Do not accept AXIndex=0 as a substitute for Row 1, relax
the helper predicate, invent raw row-label content from length 9, or claim the
working custom column establishes all row/cell behavior.

No later selection, logical navigation, retained-element retirement or normal-close
checks executed. VoiceOver, IME, geometry, event delivery and performance remain
unverified. This second audit changed only this document; no production/helper
editing, staging, commit or push was performed.

### Third hosted target: Route A falsification (2026-10-01)

[Run 36789139005](https://github.com/kleedaisuki/mote/actions/runs/36789139005),
checkout `3aae8d57eeaf9033d3539fd9a05e383afba8c83f`, tests the opt-in Route A
legacy `accessibilityAttributeValue:` override for `AXRows`. Static comparison to
`5977250` shows this route returns the current custom row array for that attribute
and delegates other attributes to `NSTableView`; this source intent is not evidence
that external AppKit actually invoked the new route.

Raw evidence is under `.cache/ci-36789139005-mac-grid-ax/`:
`{arm,x64}-artifact/mac-grid-ax-external.json` and `{osx-arm64,osx-x64}.log`.
Logs were downloaded using `gh run view 36789139005 --job 110137600228 --log`
(ARM) and job `110137600215` (x64), preserving project-local evidence. They show
exact checkout, fresh matching-RID AOT publish, external helper pins and raw exit.
Python raw `git show` bytes independently verify unchanged helper SHA
`A65E460046E0AA72B88F5E4289CAA0915A4EE15E1C24984B9C40DC6FD1227B8C`
and driver SHA
`0A867687F62795790989B20A347A70EBEB93888923A666157DB45F5F5F816CEA`.
The tested Route A source `MacCsvGrid.Accessibility.cs` SHA is
`8B0BECCB3A7C1471BF8F20F00C763492E6D5BEFB03F6D51BDDD2010E9F919E8B`.
Binary attribution remains the hosted fresh-publish/report chain, not a local
binary download or native rerun.

| Fact | osx-arm64 | osx-x64 |
| --- | --- | --- |
| Report SHA-256 | `1B846F3694E6949DDE22ECEE7F9BA6A1BEA71C5B4245D99048F2B25F4BB1115E` | `7BD182F1C110161B58919BEC5359B73FAFB562AB7C8BB67168CB0A778AF8C618` |
| AOT SHA-256 | `A97EDEB1DA577B4ABB3ABC064207543DC015322506ABDFAE08B4F0E4C668F130` | `A7EAD04CB11E6899E40F1943DFEAA04460F93757607B51842581087099A1A385` |
| Binary bytes | 16390696 | 16734984 |
| Editor / client PID | 5972 / 5973 | 23924 / 23925 |
| Swift typecheck / trust | true / true | true / true |
| Prior checks / first failure | same 8 / first-record-is-data | same 8 / first-record-is-data |
| Client exit / phase | 1 / origin-window | 1 / origin-window |
| Admission count | 2302 | 1501 |
| Close attempt / normal exit / forced cleanup | false / null / true | false / null / true |
| Fixture unchanged / cleanup error | true / empty | true / empty |

OS/architecture and expected fixture hash/counts remain unchanged. JSON comparison
finds every one of the **16 observation records identical to the second target**
for each RID, and identical across RIDs: first row is AXRow/index 0 without
Description, Title, Identifier or Help; first column has Column 1 description,
expected custom-wrapper identifier and Help. No later semantic gate executed.

**Direct experimental conclusion:** the Route A change did not restore the
required externally enumerated Row 1 identity on either hosted target. Thus the
hypothesis that this particular legacy AXRows override is sufficient is falsified
for this workflow. This is not a TCC/Swift setup failure: the external client was
trusted, typechecked, ran, and reached the same contract failure on the exact
changed binaries.

**Remaining internal inference:** native-row substitution or row-specific bridge
bypass remains strongly supported by the row/column asymmetry, but this report does
not prove callback invocation or internal object identity. The negative result
cannot distinguish the legacy override never being used from AppKit transforming
its result later. It does not prove every possible native-Table adaptation is
impossible or justify claiming a replacement proxy already works. Preserve the
unchanged absolute-ordinal oracle and investigate a genuinely discriminating route
rather than broaden legacy machinery blindly.

**Separate in-process selector step was not exercised.** The workflow still
required old `MacCsvGridAccessibilityProbe.cs` SHA
`0E2CD0B186477128ABBCA4741AA85B0835DE72E8B08AE470301C9A401CCA198A`, while raw
`git show 3aae8d5:src/Mote.Native/Mac/MacCsvGridAccessibilityProbe.cs` hashes to
`A2B604B335E94931CC89E8D41D8C123A125C956A758E7F7C947ED0BA0DE0B348`.
Both logs record `Mac Grid AX selector probe differs from reviewed bytes.` at the
preflight guard before that step invokes the executable. This is a stale CI pin /
harness setup failure, **not an in-process product probe failure or pass**. It does
not invalidate the separate external step, whose own helper pins matched and whose
reports demonstrate native execution.

Strict CI remains green while these diagnostics are non-gating; that does not
convert either external failure or preflight-blocked selector check into acceptance.
Selection, navigation, retirement and normal close remain untested, as do real
VoiceOver/IME, geometry and performance. This audit only updates this document;
no production, helper or workflow edit, staging, commit or push was performed.
