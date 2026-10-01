# macOS CSV Grid external AX acceptance harness

Date: 2026-10-01. Status: **proxy execution on both Mac RIDs fixes observed row metadata; 26 external
checks pass, coordinate-menu discovery fails, not full external acceptance**. This does not change production
accessibility defaults or certify VoiceOver/IME/release readiness.

## Purpose and ownership

`tests/MacGridAxExternalProbe/Run.ps1` launches the freshly published strict one-file
Native AOT binary using the **ordinary file-opening route**, with only the process-local
`MOTE_NATIVE_GRID_ACCESSIBILITY=1` experiment enabled. `Probe.swift` is separately
compiled by the host Xcode toolchain and connects through
`AXUIElementCreateApplication(editorPID)`. It does not link Mote, call Objective-C
selectors in-process, use a diagnostic editor flag or ship with the product.

This follows the installed bounded-window contract in
[`../csv-grid-accessibility-contract.md`](../csv-grid-accessibility-contract.md)
and extends, rather than repeats, the already-passed in-process probes described
in [`mac-grid-accessibility.md`](mac-grid-accessibility.md). It reuses the established
Swift external source-provider client model in
[`../../tests/MacAxExternalProbe/README.md`](../../tests/MacAxExternalProbe/README.md).
The research/industry rationale already documented in the contract remains relevant:
truthful bounded metadata and genuine assistive-technology tasks are distinct
acceptance surfaces. This harness is one of the former, never a simulated reader.

## Invocation and CI handoff

Run on matching macOS x64/ARM64 after the normal publish inventory/cleanup step:

```powershell
& ./tests/MacGridAxExternalProbe/Run.ps1 `
  -ExecutablePath "src/Mote.Native/bin/Release/net10.0/$rid/publish/mote" `
  -RuntimeIdentifier $rid `
  -ReportPath ".cache/ci-inventory/$rid/mac-grid-ax-external.json"
```

Use an initially **non-gating** CI step with `shell: pwsh`, `timeout-minutes: 6`,
`continue-on-error: true`, `startsWith(matrix.rid, 'osx-')`; upload the report on
`always()` even after a failure. No shell environment setup is required: the wrapper
sets the Grid gate and isolated `MOTE_HOME` only on its editor child; it explicitly
disables inherited `MOTE_TRACE` and Mac stage tracing. A helper/source hash pin should
be added to the workflow only after review freezes the final bytes. Root owns
workflow integration, not this workstream.

The wrapper requires exactly one file in the executable's publish directory, checks
RID against host process architecture, records binary size/SHA-256 and both harness
source hashes, and runs `xcrun swiftc -typecheck` before compilation or editor launch.
It does not itself prove compilation provenance: the CI job must build from the
checked-out commit and pass the fresh publish path, not substitute an old executable.

## Synthetic fixture and independent oracle

- UTF-8 without BOM, LF separators, no final LF: **1100 CSV records**, maximum
  **24 fields**, **263760 UTF-16 units** (all ASCII).
- Record 1 field 2 is Complete empty; record 2 contains exactly one field, so field
  2 is Missing, not an invented empty value. Every other field is `rRRRRRcCC`.
- Record 1001 field 17 must be `r01001c17`; record 1100 field 24 is
  `r01100c24`, also queried through the separate full source AXTextArea.
- Fixture SHA-256:
  `8BACC6F97A374853C5F04EE176384738D8BFF7F71B1C7E768D088310C98DD8C2`.
- Unique scratch directories, fixture, compiler output and isolated configuration
  remain under `.temp/mac-grid-ax-external/`; reports remain under
  `.cache/ci-inventory/`. The fixture-relative path is reported for reproducibility.

Portable validation:

```powershell
pwsh -NoProfile -File tests/MacGridAxExternalProbe/Test-Fixture.ps1
```

On Windows PowerShell 7 this passed on 2026-10-01: driver AST parsed; both RID fixture
preflights produced identical expected bytes/structure and unchanged SHA; the report
root escape was refused before writing. Preflight deliberately records no binary
inventory, editor PID, Swift typecheck or AX verdict. The Swift Mac SDK cannot be
loaded on this host; successful PowerShell parsing is not Swift typechecking.

## Actual external gate sequence

| Phase | Assertions |
| --- | --- |
| Preconditions | Separate editor/client PIDs; AX trust query only; exactly one `CSV grid window` semantic AXTable and no extra default AXTable; one labeled `Mote editor` AXTextArea exposing full fixture count and exact offscreen source. |
| Origin window | Local bounded row/column arrays agree with counts; ordinal headers agree with axes; record 1 is data; first exact value and cell parent row; local NSRange; Complete empty differs from Missing; out-of-window lookup returns no element with a terminal/refusal error, not a transport timeout. |
| Selection | Exact 2-cell rectangle setter/readback; duplicate input deduplicated; sparse set, 8193-entry input and native selected-row bypass return only terminal/server-declared statuses and do not alter existing selection; source selection unchanged; empty input clears. Readback comparison rejects duplicate elements rather than treating arrays as lossy sets. |
| Logical navigation | Exact-PID Table `AXShowMenu`; unique `Go to row:column…` item; exact static `Go to CSV row:column` prompt with unique field/Go button; set only numeric `1001:17`; press Go; absolute Row 1001/Column 17 labels, still-local `{0,1}` ranges and exact `r01001c17` value. |
| Retained elements | Original origin cell retains no value and only absent/empty index range after navigation; terminal/unavailable AX errors only, no `.cannotComplete` transport-timeout shortcut. Mixed current/old selection is atomically refused while current-only selection remains. Full source remains independently readable. |
| Normal close | Press this exact process's single window AXCloseButton; bounded absent-value observation on the retained remote cell; wrapper additionally demands editor normal exit code 0 within 10s and unchanged fixture SHA. Transport failure alone never certifies close. |

Local Table bounds are capped at 256 rows/64 columns and selected-cell arrays at
8192. Tree discovery excludes Table/Row/Column expansion, caps 256 nodes/12 levels
and every copied child array at 128. Array counts are obtained before bounded copy;
truncation is never a full-array pass. Each admitted exact-PID AXUIElement receives
its own checked 1s messaging timeout (not merely the application element).
The client caps 12000 admissions and 55s monotonic lifetime, while the wrapper has
a separate 75s client watchdog. `admissionCount` is not an exact API-call count or
performance benchmark. Forced cleanup is separately reported and cannot pass normal
close. No repeated edit/Save or blind coordinate-submit retries occur.

## Important possible failure and scope limits

The existing coordinate command is exposed through the Table's context menu, not
through the main application menu. Native `AXShowMenu`/transient menu accessibility
may fail. Such a result is retained at `logical-navigation`, **not** worked around by
global key injection, direct Mote calls, blind scroller AXValue mutation or a changed
oracle. It is a concrete accessibility navigation gate. A TCC-denied client records
`external-accessibility-unavailable`, warns without a product-pass verdict, and never
requests, prompts for, modifies or grants permission.

The fixture cannot deterministically force asynchronous Pending cells; Pending focus/
selection semantics retain their native in-process regression evidence, not a new
external claim here. This run likewise does not test geometry/hit testing, arbitrary
whole-file virtualization, row/column source edits, notification delivery, retained
nodes across theme/edit/Undo/New, query/memory distributions, actual VoiceOver speech
or real Pinyin composition. Those gates remain in the existing acceptance ledger.
An early failure retains prior independent checks but the overall status is not pass.

## Report and interpretation

Schema 1 outer report: status, RID/OS/architecture, editor PID, binary/hash/size,
harness hashes, strict inventory, fixture/hash/path/counts, integrity, Swift typecheck/
exit/report, normal-exit/forced-cleanup flags, cleanup error and original error. Nested Swift report: status,
phase, editor/client PIDs, trust, close attempt, admission count, ordered named checks
and a content-free note. Synthetic cell values may be compared in memory; no external
application/document/caption content or clipboard snapshot is collected.

Possible native statuses: `passed`, `failed`, `probe-error`, or
`external-accessibility-unavailable`. Portable-only status: `fixture-preflight-passed`.
A green `continue-on-error` job is not native acceptance: inspect the JSON and raw
step exit. The first hosted run is documented below; it failed the bounded origin-row gate,
not the entire product or the external protocol's later gates.

## First hosted result and bounded discriminator

[CI run 36786762929](https://github.com/kleedaisuki/mote/actions/runs/36786762929)
at commit `1448ce4` ran the separately compiled Swift client on freshly published
strict one-file binaries for **osx-x64 and osx-arm64**. Both typechecked, had AX trust,
and passed separate-PID, one opt-in Table/no default duplicate, source Document
coexistence/full source count/readable selection/offscreen exact string, and bounded
axis-count/array agreement. Both stopped at **`first-record-is-data`**, phase
`origin-window`, Swift exit 1. Both fixtures remained unchanged; editor cleanup was
forced, so neither normal close nor any subsequent selection/navigation/lifetime
assertion was executed. A non-gating job's green conclusion is not this probe's pass.

The reports capture the failed predicate, not the failing row's label attributes.
The client currently prefers any nonnil `AXDescription` over `AXTitle`; production
provides `accessibilityLabel` with the intended one-based row label. Existing evidence
does **not** distinguish an AX label-transport mismatch, a default/native row object,
a stale object or an actual shifted ordinal. Do not relax the ordinal oracle or
change production based on this hypothesis alone. The independent first-target audit
is maintained in `mac-grid-external-ax-first-target.md` by the parent validator.

The discriminator recheck changed no assertion or product source. Immediately before the
unchanged first-row predicate, the helper observes only **two nodes** (first AXRows
and first AXColumns) and eight fixed metadata attributes per node: `AXRole`,
`AXRoleDescription`, `AXDescription`, `AXTitle`, `AXIndex`, `AXIdentifier`, `AXValue`,
`AXHelp`. An additive `observations` array retains AX error/type, UTF-16 length and
fixed classification. Exact expected ordinal/role, recognized role, generic axis
role, empty/missing string and expected custom wrapper topology are enums; a
syntactically bounded decimal ordinal is an integer. No raw string/help/source value,
attribute-name dump, desktop or unrelated process data is returned. This preserves
the current refusal/timeout budgets and stops at the same assertion if it still fails.

Interpretation after native recheck: an expected ordinal in AXTitle with a generic/
empty AXDescription establishes a client extraction issue; a parsed different ordinal
requires investigating actual window origin; a native/default role or missing custom
identifier motivates a node-merging investigation. A missing/timeout value is not
proof of any of these. The second hosted execution below supplied this discriminator on both targets.

## Classified second hosted result: row bridge failure, not a weakened oracle

[CI run 36787947202](https://github.com/kleedaisuki/mote/actions/runs/36787947202)
built commit `5977250330fd47a633b95692b5a3ed8e1c988495`. The unchanged fixture and
original `first-record-is-data` predicate again failed on **both Mac RIDs**. Both
Swift clients typechecked and ran with AX trust; both retained the same preceding
eight successful source/Table/count assertions. The reviewed classifier helper was
`A65E460046E0AA72B88F5E4289CAA0915A4EE15E1C24984B9C40DC6FD1227B8C`; driver remained
`0A867687F62795790989B20A347A70EBEB93888923A666157DB45F5F5F816CEA`.

| RID / actual hosted OS | Strict published binary size | Binary SHA-256 |
| --- | ---: | --- |
| osx-x64 / macOS 15.7.9 | 16,727,360 bytes | `84D4A00CB483F72011D2FBF701C3B0A847E85C26B9E4032CCD4940A0B2C69A38` |
| osx-arm64 / macOS 26.6.2 | 16,370,776 bytes | `21A6D32444F52F3FEC9C7E78AB4F3EA1DE3578166543291A89EB35D6DCA3CF7D` |

The fixed metadata observations are identical on both architectures:

| External attribute | `AXRows[0]` | `AXColumns[0]` |
| --- | --- | --- |
| AXRole | Success; expected AXRow classification | Success; expected AXColumn classification |
| AXIndex | Success; numeric local index 0 | Success; numeric local index 0 |
| AXDescription | `-25205` / unsupported; absent | Success; exact expected Column 1, ordinal 1 |
| AXTitle | `-25205` / unsupported; absent | `-25212` / no value; absent |
| AXIdentifier | `-25212` / no value; absent | Success; expected custom window-axis identifier topology |
| AXHelp | `-25205` / unsupported; absent | Success; bounded string length 52, no raw help captured |
| AXValue | `-25205` / unsupported; absent | `-25212` / no value; absent |

This **rules out repairing the failure by merely reversing the helper's
Description/Title precedence**: neither candidate row label exists externally.
The column provides a positive control for custom metadata transport and the exact
same editor PID/client/framework route. The row exposes the axis role/local index
but none of the custom ordinal/identifier/help contract. These are terminal
unsupported/no-value responses, not indeterminate messaging timeouts or denied AX
trust. The production provider therefore fails its external absolute-row metadata
contract, regardless of the UI's visible first record. It is not an assertion that
CSV parsing skipped a header or that the true window origin shifted.

**Mechanism inference, not directly observed object identity:** AppKit's external
NSTableView bridge appears to expose a native row representation rather than the
custom metadata row returned by the in-process `accessibilityRows` selector. The
AX report does not reveal the Objective-C class/pointer or prove which private
framework dispatch path substituted it. The row/column asymmetry justifies a
narrow production provider investigation; it does not yet justify an unbounded
proxy rewrite or bypassing the row oracle. Root and the Grid implementation owner
coordinate any legacy/modern bridge or provider replacement change. Harness
assertions and the opted-in/default boundary remain unchanged.

Raw reports (downloaded unchanged into repository cache):

- `.cache/ci-36787947202-mac-grid-ax/x64-artifact/mac-grid-ax-external.json`, report
  SHA-256 `9C84412B5971C9316AAE487544AD2F52C60B03092DB0589DED92BEF615D8469E`.
- `.cache/ci-36787947202-mac-grid-ax/arm-artifact/mac-grid-ax-external.json`, report
  SHA-256 `5F740742E4075BEE5986269477718B2C735D0A465AD9FDC574F64743597224DE`.

Both report Swift exit 1, phase `origin-window`, unchanged fixture SHA, forced editor
cleanup, and no normal-close result. x64/ARM64 editor/client PIDs were 9072/9073
and 6990/6991 respectively. Selection, shifted-window navigation, retired-node
lifetime, normal close, VoiceOver, IME and geometry remain unexecuted in these
runs. The green overall/non-gating job conclusion is not external acceptance.

## Primary references

- [Apple AXUIElement API](https://developer.apple.com/documentation/applicationservices/axuielement): external element/value/action operations.
- [Apple messaging timeout](https://developer.apple.com/documentation/applicationservices/1460640-axuielementsetmessagingtimeout): timeout scope must be installed on each separate element.
- [Apple Accessibility trust query](https://developer.apple.com/documentation/applicationservices/1459186-axisprocesstrustedwithoptions): permission query is not a permission grant.
- [Apple Show Menu action](https://developer.apple.com/documentation/appkit/nsaccessibility-swift.struct/action/showmenu): contextual menu action, not keyboard-event substitution.
- [Existing real reader acceptance protocol](../screen-reader-acceptance.md): API checks cannot replace emitted VoiceOver speech.

## Proxy execution and bounded menu discriminator

CI 36791254056 / `f657c00` resolves the earlier first-row metadata falsifier on
both RIDs and reaches the next boundary: external coordinate-menu discovery.
See [exact target results and scope](mac-grid-table-proxy.md#first-proxy-target-execution-ci-36791254056).
The added optional `diagnostics` report field and per-check `admissionCount` /
`elapsedSeconds` fields retain only fixed phase counters, timings and exact
synthetic command equality counts. They never retain arbitrary AX strings,
source text, desktop identity or foreign-process metadata. Existing schema-1
fields and acceptance predicates remain unchanged. Query/lifetime/tree limits
are not raised; menu AXTitle is diagnostic only, not a relaxed selection oracle.

The instrumented helper was natively typechecked and exercised on both targets
in CI 36792454502 / `0b85a0e`. It preserved the first external menu failure while
localizing the ARM budget pressure to 13 bounded menu-search traversals. Neither
Table nor application exposed a returned shown-menu element. Exact results,
error-code interpretation and unexercised paths are recorded in
[the proxy target audit](mac-grid-table-proxy.md#shown-menu-forwarding-target-ci-36792454502).

## Bounded native menu lifecycle capture

The external wrapper additionally enables the process-local
`MOTE_NATIVE_GRID_MENU_DIAGNOSTIC=1` discriminator alongside Grid AX, without
changing ordinary product defaults. `MenuTraceCapture.cs` asynchronously drains
the owned editor stdout and stderr. Only exact fixed-token `mote-grid-menu-v1`
rows with known phases, bounded counters, item count (-1 or 0..16), boolean
facts and phase-consistent result values survive; at most 16 rows are retained
in `native_menu_trace`. Lines longer than 384 characters are discarded while
reading 1024-character blocks, rather than materializing arbitrary output.
No raw editor output is written to disk. All other stdout and **all stderr**
are discarded; absent stderr in this report is not evidence of no native error.
Editor exit, client status, watchdogs, integrity checks and all external semantic
assertions remain unchanged. Pipe-drain failure is a separate probe-error.

Portable validation on Windows/PowerShell 7 independently rejects private text,
invalid phase/result/counter combinations, injected prefixes/suffixes, repeated
CR and a 100,000-character line; it retains only exact valid rows, enforces the
16-row bound, and checks discard-only behavior. A real owned PowerShell child
writes 1,000,000-character stdout and stderr lines concurrently, then one valid
row. Both asynchronous drains complete without pipe deadlock or raw output
persistence; only that valid row remains. Commands:

```powershell
pwsh -NoProfile -File tests/MacGridAxExternalProbe/Test-MenuTraceCapture.ps1
pwsh -NoProfile -File tests/MacGridAxExternalProbe/Test-Fixture.ps1
```

Both passed locally. Actual AppKit lifecycle facts require a fresh Mac target
run; synthetic capture success does not establish menu presentation.

The lifecycle whitelist also recognizes `schedule-return`, `popup-begin` and
`popup-return` for the coordinated next-turn native popup discriminator.
`native-return`, `schedule-return` and `popup-return` require boolean results;
all entry/open/close phases require -1. A schedule result certifies admission,
not completion; a native popup result distinguishes selection from cancellation,
not semantic navigation success. Independent tests reject inconsistent results
for these additions. Neither the 16-row bound nor external menu oracle changes.
