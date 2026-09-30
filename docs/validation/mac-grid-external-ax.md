# macOS CSV Grid external AX acceptance harness

Date: 2026-10-01. Status: **first native Swift/cross-process execution obtained on
both Mac RIDs; partial external evidence with the same origin-row falsifier,
not external acceptance**. This does not change production
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

The next recheck changes no assertion or product source. Immediately before the
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
proof of any of these. The discriminator itself has not yet executed on macOS.

## Primary references

- [Apple AXUIElement API](https://developer.apple.com/documentation/applicationservices/axuielement): external element/value/action operations.
- [Apple messaging timeout](https://developer.apple.com/documentation/applicationservices/1460640-axuielementsetmessagingtimeout): timeout scope must be installed on each separate element.
- [Apple Accessibility trust query](https://developer.apple.com/documentation/applicationservices/1459186-axisprocesstrustedwithoptions): permission query is not a permission grant.
- [Apple Show Menu action](https://developer.apple.com/documentation/appkit/nsaccessibility-swift.struct/action/showmenu): contextual menu action, not keyboard-event substitution.
- [Existing real reader acceptance protocol](../screen-reader-acceptance.md): API checks cannot replace emitted VoiceOver speech.
