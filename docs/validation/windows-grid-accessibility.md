# Windows bounded CSV Grid accessibility validation

Date: 2026-10-01. Scope: opt-in bounded-window UIA implementation; **not desktop
reader/release acceptance**. Enable per process with `MOTE_NATIVE_GRID_ACCESSIBILITY=1`.
The source Document/input-island provider is unchanged. The native navigation
Group, coordinate button, concise status and F6 pane cycle are independently
useful; the custom UIA registration is not promoted by default.

## Implemented architecture and contracts

- The native group HWND owns the existing owner-data table, native logical row
  and column scrollers, a real Go-to button, and a real status control. Notifications
  and scroll requests still reach the established shell/controller handler.
- A stable HWND-hosted Table root survives installations. Its runtime ID is null,
  allowing the HWND host to provide root identity. Cells/ordinal headers retain
  only a bounded local key plus the exact installation epoch. They never retain
  a historical projection. Replacing the window clears the wrapper map; retained
  old cells become unavailable instead of acquiring new coordinates.
- Table implements complete Grid, Table and Selection pattern vtables. Cells
  implement GridItem, TableItem, SelectionItem and, only when a presentation
  value exists, read-only Value. Source Invoke is omitted until acknowledged
  controller command admission/completion exists. Gutter/headers are not data
  columns. Table DescribedBy links to the actual status provider.
- Counts and GridItem positions are local to the admitted window. Names and
  associated headers carry absolute one-based CSV ordinals. Every admitted slot
  exists, including Pending and ragged Missing states. No callback parses or
  decodes source, waits for a worker, or allocates by file size.
- Native client subitem/header geometry is captured on the UI owner during
  publication, clipped to the table client, and cached. UIA reads never issue
  native layout requests. A publication predicate rejects capture superseded by
  nested layout callbacks. Provider state/node maps are guarded independently
  of event dispatch; no OS event is raised under the map lock.
- HwndOverride substitutes a nonsemantic header host, removing the otherwise
  merged default native Header from Control/Content views and avoiding duplicate
  column semantics. A real target-host probe disproved the proposed native
  scroller pattern fallback: Simple overrides removed RangeValue; removing those
  overrides and using MSAA name annotation still exposed native ScrollBar HWNDs
  as generic Pane elements, without RangeValue. This navigation pattern gate is
  preserved as negative evidence rather than hidden. The final implementation
  therefore supplies its own complete **read-only** RangeValue vtable: Minimum
  zero, Maximum the actual last legal window origin, Value the admitted first
  origin, SmallChange one, LargeChange min(Page, Count). It omits RangeValue for
  unavailable extent; exact empty is a real zero domain with no invented first
  coordinate. SetValue is Unsupported, never queued or inferred successful.
  Prefix row and known-column scopes explicitly distinguish unknown file totals
  and maximum width. Native focus/keys and Go-to remain the established controls.
- Adapter selection is the single owner. Exact unions/differences are admitted
  against the latest full retained rectangle. Holes and sparse hulls fail
  atomically. Clear removes painted membership and blocks Copy of old endpoints.
  A successful mutation commits a coherent frame without callbacks, cancels any
  earlier controller Copy, then publishes native labels and accessibility events.
- Coordinate commands share one modal path. A target is bound to the document,
  admitted gesture and the first **observed** pending request serial. It is only
  selected after matching ready delivery proves that target. Superseding serials
  retire it. No predicted serial or pending guessed selection is used.

## Actual threading finding and safety gate

The first separate MTA UIA client could browse all container/cell patterns, but
Select failed because generated COM callbacks did not run on the HWND UI thread.
Explicit owner STA acquisition plus `ProviderOptions_UseComThreading` **did not**
solve that problem when the manual dispatch path was disabled. Records:

- `.cache/windows-grid-accessibility/external-first.json` (fresh AOT negative).
- `.cache/windows-grid-accessibility/sta-experiment.json` (controlled JIT STA
  experiment, negative; not AOT acceptance).

Selection therefore uses a bounded synchronous HWND admission token. At most
one outstanding command is admitted per Grid; concurrent commands are refused.
The 500 ms SendMessageTimeout payload is a dictionary key, not a retained GCHandle
or native caller buffer. A timed-out/cancelled token cannot later mutate. A
per-request gate covers only callback-free commit plus actual completion receipt;
OS/native callbacks are outside that gate. On timeout, an already completed
transaction reports its actual result rather than inventing an unchanged state.
The dedicated stalled-composition-hook, concurrent-request and throwing-hook
regression passed, including explicit failure instead of default LRESULT zero
being interpreted as Applied.

**Off-owner COM Focus is deliberately Unsupported before native focus transfer.**
Direct owner-thread focus remains guarded against composition, native failure and
reentrant replacement. This limitation is material: the read/selection/navigation
matrix passing does not establish external cell SetFocus usability. The custom
registration remains opt-in until a sound platform focus admission boundary and
reader task evidence are available. No delayed focus, preedit commit/cancel, or
queued false-success workaround is used.

Local focus regressions distinguish focused cell A from independently selected
cell B: Enter/F2 target A, Copy retains B, arrows start from A. Native selection
changes reset explicit cell focus; Copy does not. Window/document retirement
clears focus overrides. F6 and Shift+F6 pane cycling are shell-owned, leave Tab
as source editing, skip hidden/unavailable controls and refuse active composition.

## Reproduction

All fixtures/reports are repository-local and the external probe supplies an
isolated `MOTE_HOME`; it uses no clipboard, global keyboard injection, screen
reader, or IME activation.

```powershell
dotnet test tests/Mote.Tests --filter "FullyQualifiedName~WindowsGridAccessibilityTests|FullyQualifiedName~NativeCsvGridWindowsTests|FullyQualifiedName~NativeGridLogicalWindowsTests"
dotnet publish src/Mote.Native -c Release -r win-x64 -o .cache/windows-grid-accessibility/aot
dotnet run --project tests/WindowsGridExternalProbe -- .cache/windows-grid-accessibility/aot/mote.exe .temp/windows-grid-accessibility .cache/windows-grid-accessibility/external-aot-final.json
```

The external client records binary/fixture SHA-256, exact launched PID, OS and
architecture; validates prompt PID before interacting; and sets the actual
coordinate input via its native Value pattern with readback (cross-process
SetWindowText was a failed probe mechanism, not a valid navigation test).

## Completed focused evidence

- 21 focused native/provider tests passed after the selection/focus/timeout
  changes; the dedicated maximum-window enumeration test additionally passed.
- Generated COM SDK GUIDs and Grid vtable positions were executed, not merely
  reflected from managed interface declarations. SAFEARRAY header/selection
  counts and invalid local GetItem requests were checked.
- A separate JIT client passed all three UIA tree views with 1104 custom children
  (64 row headers + 16 column headers + 1024 cells), no merged default Header,
  exact selection add/refusal, distant Go-to, and old-cell unavailability.
- The JIT F6 probe traversed the real owner's message loop through table, row
  scroller, column scroller, Go-to and back to source, with physical HWND focus
  readback. This is **synthetic owned-process F6 evidence**, not a physical
  keyboard or reader speech pass.
- `.cache/windows-grid-accessibility/worst-case-enumeration.json` records an
  actual 8192-cell COM selection enumeration and 64 retained wrappers after
  retirement. This is bounded managed allocation/timing evidence, not RSS or a
  100 MiB fixture memory-lifetime claim.

An earlier strict one-binary AOT exposed the native navigation pattern limitation
above; that intermediate wider matrix was not a pass. The earlier AOT report
`.cache/windows-grid-accessibility/external-aot-final.json` contains product-fail
assertions for absent row/column RangeValue and an inconclusive subsequent
unsupported-pattern probe exception. The direct-native-handle experiment at
`.cache/windows-grid-accessibility/dispatcher-debug-native-proxy.json` records
ClassName ScrollBar, ControlType Pane, status-derived names and no RangeValue.
This is evidence against assuming host proxy capabilities from visual classes.
The final read-only provider resolves the inspected range-read gap; range writes
remain explicitly gated rather than claimed complete. win-arm64, real reader speech, physical Pinyin,
100 MiB memory teardown and desktop multi-monitor/scaling acceptance remain
unexecuted here, not implied by these checks.

## Primary references

- [Microsoft Grid conventions](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementinggrid)
  require zero-based coordinates and a provider for empty slots.
- [Microsoft Table conventions](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingtable)
  require concurrent Grid support and cell/header relationships.
- [Microsoft server-side provider architecture](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-serversideprovider)
  distinguishes independent native vtables, host merging, fragment navigation,
  and registration lifetime.
- Exact GUIDs, method order, ProviderOptions and event constants were checked
  against installed Windows SDK 10.0.26100.0 `UIAutomationCore.h` and
  `UIAutomationClient.h`.
- The shared design contract's Chromium cached accessibility-tree precedent
  applies here as immutable bounded facts plus coherent events, not a copy of
  Chromium's whole-tree IPC machinery. Its research/reader acceptance arguments
  remain unresolved empirical gates, not implementation correctness shortcuts.

## Final fresh win-x64 AOT scoped result

Report: `.cache/windows-grid-accessibility/external-aot-delivery-owned-focus.json` (final observer ownership filter).
Classification: **pass for the explicitly scoped falsifiers**, with remaining
focus/write/reader/release gates recorded separately. The preceding failures
remain in their original reports; no failed run is rewritten as a pass.

| Evidence | Actual result |
| --- | --- |
| OS / process | Windows 10.0.26200.0, x64; exact launched PID 39696 |
| Strict AOT binary SHA-256 | `BE2D17B41853A586F080F4DA3094203C2E9DD0A2AF57FF6B653DDB5B8708D0A0` |
| Synthetic fixture SHA-256 | `86796C9AF5EADC2DB5A0B8FBE3F14245DF7AB2F0456E6EE5189ED6987819E0B4` |
| Initial Table | 64 local rows, 16 local columns; first cell Row 1 / Column 1 / R1C1 |
| Raw / Control / Content views | 1104 children each = 64 row headers + 16 column headers + 1024 cells; zero unexpected default proxy children |
| MTA selection | Select/Add form exactly two cells; impossible union refuses without changing selection |
| Row read-only range | min 0, max 1076 (window origin, **not** file last row), value 0, SmallChange 1, LargeChange 24 |
| Column read-only range | min 0, max 28, value 0, SmallChange 1, LargeChange 4 |
| Go-to readback | Input exactly `1001:17`; first admitted cell R1001C17 with local GridItem 0/0; status selection anchor/active both Row 1001 / Column 17 |
| Post-Go-to range values | Row origin 1000, column origin 16; still read-only |
| Retained cell | Old pre-rebase cell unavailable |
| 1000 bounded cell/name queries | 111.3852 ms total; slowest 0.3077 ms (one scoped run, not a cross-machine benchmark) |
| Owned F6 message-loop traversal | Exact HWND path table -> row -> column -> Go-to -> source |
| Global semantic UIA focus | **Blocked**: the global focused element was outside the exact target PID; the final observer did not inspect foreign semantic metadata |
| Owned-thread focus properties | New row/column focus true and prior false; physical Table root focus false. These cached owner-thread facts do not prove global foreground focus parity |
| External cell SetFocus | Explicit Unsupported before native focus effects; remains **blocked**, not a successful focus acceptance claim |

Eight dedicated provider/adapter regressions have passed across their focused
runs: generated pattern ABI/role guards, retained epochs, clear/paint/copy,
focused versus selected keyboard coordinates, timeout/concurrency/throw,
8192-slot retirement, read-only prefix/unavailable/exact-empty/nil ranges, and detached COM-owner weak-reference lifetime.
The established 16 Windows native Grid/logical tests also passed. The worst-case
8192-cell enumeration record measured 6,196,296 managed allocated bytes and
23.2282 ms, then zero cached nodes after retirement while 64 stale wrappers were
retained. These are local managed measurements, not an AOT RSS/100 MiB result.

The AOT source inventory before and after publish was identical:
`6BD6F9BE93C2E3D579D76B10C40AF6D92DB2B418E000FFEE2E33DF8473483BD6`.
It is the SHA-256 of the sorted normalized production `.cs`/`.csproj` path + file
hash inventory (excluding bin/obj), at
`.cache/windows-grid-accessibility/source-inventory-final.json`. It includes all
current uncommitted shared Save production; the build produced only `mote.exe`
(7,068,160 bytes), with no shipped helper or sidecar.

After that normal-matrix AOT evidence, two deliberately narrow final admission/lifetime
fixes were made: owner-thread Focus on a Pending cell now returns NotReady **before**
SetFocus; Pending cell IsKeyboardFocusable is false. Final bridge Detach also clears the
actions and geometry delegates so retained stale COM nodes cannot indirectly
root the disposed adapter/controller owner. The two focused role/focus
regressions passed after the Pending change, including unchanged native GetFocus
and selection on refusal. The retained-node field-release regression and a
separate deterministic weak-reference regression passed after the lifetime fix:
a native COM pointer and stale node remain held while the captured action/geometry
owner is collected. This is an indirect-owner reachability proof, not a large-file
RSS acceptance result. That AOT binary is therefore **not claimed to include the
last Pending/lifetime guards**. The normal complete-cell matrix is valid prior evidence,
not relabeled as an exact-current-binary test. Only these production files differ
from its build inventory:

- `src/Mote.Native/Windows/WindowsCsvGrid.cs`
- `src/Mote.Native/Windows/Accessibility/WindowsGridUiaProvider.cs`

The final delivery inventory is
`.cache/windows-grid-accessibility/source-inventory-delivery.json`, SHA-256
`C24DB00BF6C6B6E3E05D175379D5B900EDE28E0F254BE08376C98911101728A3`.
Parent integration CI owns the fresh four-RID rebuild. Production is now frozen.

### Exact inventory coverage and final whitespace normalization

Root removed one extra trailing EOF blank line from `WindowsCsvGrid.cs` and
`tests/Mote.Tests/GridAccessibilityTests.cs` while staging. This was whitespace-only,
not an executable-code change. The former affects this raw-byte inventory; the
latter does not, because `tests/` is excluded. Recomputing with the original
procedure changed the delivery aggregate from
`A242175ED3AEED9398413BB6538676A56F0BAD66DD83FBB40E55C2AA57CF225F`
to the current `C24DB00B...` value above. Comparing all path/hash records found
**exactly one changed entry**, `src/Mote.Native/Windows/WindowsCsvGrid.cs`:
`E033D5600E27210C1AA1DC4B995491F141FF7515A297682568876C6D1D8BDF4F`
became `80813D2C7521DE4EC79868503D825FC300370ACEE0D3E3DB8A640080E8A8A8F8`.
The pre-normalization inventory is preserved at
`.cache/windows-grid-accessibility/source-inventory-delivery-pre-eof-normalization.json`.
This does not relabel the older AOT binary as an exact-current build.

Coverage is **all** recursive `src/` files whose extension is `.cs` or `.csproj`,
excluding any path component `bin/` or `obj/`; it is not only Native, nor the
precise set of inputs linked into the Native executable. The complete exact
relative path list is the `Path` column in the inventory JSON. Current coverage:

| Directory (including descendants) | C# / project files |
| --- | ---: |
| `src/Mote.Configuration/` | 3 |
| `src/Mote.Desktop/` | 5 |
| `src/Mote.Engine/` | 9 |
| `src/Mote.Formats/` | 28 |
| `src/Mote.Native/` | 85 |
| `src/Mote.Telemetry/` | 5 |
| `src/Mote.Themes/` | 6 |
| **Total** | **141** |

Tests, probes under `tests/`, docs, generated `bin/obj` files, `.plist`, `.axaml`,
`.props` and all other extensions are excluded. Desktop entries are intentionally
included by the original broad `src/` procedure, not asserted to be Native AOT
dependencies. Each source file is hashed as raw bytes, without newline/EOF
normalization. Only relative path separators are normalized to `/`.

Reproduction uses PowerShell **7.6.5** from repository root on Windows. Records
sort by `FullName` using `Sort-Object`; property order is `Path`, then `Sha256`.
Aggregation hashes the **actual JSON file bytes**, not a custom concatenation.
The original PowerShell defaults produce pretty JSON, UTF-8 without BOM,
Windows CRLF and a terminal CRLF:

```powershell
$inventory = Get-ChildItem src -Recurse -File |
    Where-Object { $_.Extension -in @('.cs', '.csproj') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Sort-Object FullName |
    ForEach-Object {
        [pscustomobject]@{
            Path = $_.FullName.Substring((Get-Location).Path.Length + 1).Replace('\', '/')
            Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    }
$inventory | ConvertTo-Json | Set-Content .cache/windows-grid-accessibility/source-inventory-delivery.json
(Get-FileHash .cache/windows-grid-accessibility/source-inventory-delivery.json -Algorithm SHA256).Hash
```

Global focus remains materially gated: native owner-thread focus may persist
while another process is foreground. The final observer checks the focused
UIA element's ProcessId before any semantic metadata and classifies foreign
foreground focus as blocked. Earlier unfiltered semantic-label observations are
not accepted as target-scoped global focus proof. Do not infer reader orientation
or full HasKeyboardFocus foreground parity from those earlier labels.

No default accessibility
promotion, source-provider rewrite, controller command-ack claim, physical
keyboard/reader speech pass, or untested target architecture is included.
