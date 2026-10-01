# Independent Windows Grid focus provenance client

This is a **hosted-only** Windows `net10.0-windows` WPF/UIA client. It is not a
replacement for `tests/WindowsGridExternalProbe`, its assertions, project or pins.
No local GUI execution has been performed or authorized for this client.

Build-only command from the checkout root:

```powershell
dotnet build tests/WindowsGridFocusProvenanceProbe/WindowsGridFocusProvenanceProbe.csproj -c Release
```

After independent review and coordinator approval, the promoted hosted client accepts exactly
three arguments: strict published editor executable, a fresh empty scratch directory,
and report file. Scratch and report must be under checkout-root `.cache/` or `.temp/`.
Existing reparse/symlink ancestors are refused and reports use CreateNew, never overwrite.
One editor owns a synthetic 1100x32 CSV and isolated `MOTE_HOME`; existing `MOTE_TRACE=1`
and `MOTE_NATIVE_GRID_ACCESSIBILITY=1` opt-ins are enabled. No application flag is added.

## Native identity contract

Before any UIA query, discovery certifies exact launched process/creating GUI thread,
parent/control identity, class and native visibility for these windows:

| Role | Native identity (kept in memory only) |
|---|---|
| Main | MoteNativeEditorWindow, unowned top-level |
| Canvas parent | Direct main child MoteInteractiveCanvas, control ID 0 |
| Source | Exact RICHEDIT50W child 301 of certified canvas parent |
| Grid group | Direct main STATIC child 1204 |
| Table | SysListView32 child104 of group |
| Rows / Columns | SCROLLBAR group children1104 /1105 |
| GoTo | BUTTON group child1205 |

Canvas parent is **not Source**; if physically focused it is `owned_other`. Missing
canvas or source is refused discovery, never legacy101 fallback. Every focus sample
rechecks all these identity contracts. `GetGUIThreadInfo` targets only the certified
GUI thread. Neither global UIA focus nor foreign application's metadata is queried.
Native HWND/PID/thread/class names and synthetic text/coordinates are not serialized.
Class comparisons use OrdinalIgnoreCase (Windows Button/ScrollBar casing is not an identity failure).
Each owner-queue sample validates main native lifetime both before and after GetGUIThreadInfo;
a non-null active window must be the certified main or its exact owned text prompt.
This is `FocusBoundary=owner_gui_queue`, **not global physical/foreground keyboard focus**.
Operations expose OwnerQueuePaneBefore/After with only unavailable/none/source/table/
row_scroller/column_scroller/coordinate/owned_other/outside values. None means focus HWND0;
unavailable means observation or identity could not be certified.

## Sequential client receipts

Fixed labels, each with independent begin/end UTC, integer receipt and explicit
terminal-parent receipt, closed owner GUI-queue pane before/after, closed exception class
and HRESULT if thrown:

1. `discover`
2. `initial_uia`
3. `query_1000`
4. `select_first`
5. `add_second`
6. `refuse_sparse`
7. `tree_raw`, `tree_control`, `tree_content`
8. `navigation_reads`
9. `f6_once`
10. `goto_once`
11. `distant_read`
12. `cell_focus_once`

The original exact selection identity and 64x16 direct-child tree checks are computed
in memory, not logged. F6 is posted once to currently owner-queue-focused certified
pane. The observed successor relation is descriptive and does not relabel the old
failing oracle. No activation, source reset, global key, clipboard, retry or rescue.
GoTo is invoked once; prompt discovery requires exact main owner via GetParent,
exact PID/GUI thread/class/visibility, and exactly one eligible prompt. Multiple
eligible prompts immediately fail incomplete, never choose the last enumerated HWND.
Only the unique owned modal input301 receives the fixed coordinate,
then owned accept302 submits once. Fresh distant cell SetFocus executes once and its
actual return/exception is retained, not a guessed thread/refusal explanation.
Readiness polls are bounded reads, never repeated actions. `ActionAttempted` witnesses
client action-body entry only, not API or server delivery. A failed before query records
`not_attempted` and propagates even when the action exception would be contained. A failed
after query records unavailable/ObservationException while retaining the real action outcome.

## Evidence boundary

`Classification=observed` means the sequence completed without an observation-query fault;
it is **not** a product pass or a server correlation certificate. Any before/after query
fault marks the overall report incomplete and client exit nonzero, without relabeling
the actual action return/exception. Discover-before unavailable is expected before
identity exists and is not a query fault. Any unexpected workload failure
stops the sequence, preserving completed receipts. `CrossProcessEdge=unjoined` and
`AbsenceCertified=false` are unconditional. No server graph is parsed in C#; retain
raw JSONL for the existing strict Python reader in subsequent hosted integration.

Reports retain binary/fixture hashes, fixture-after hash/unchanged fact, runtime
architecture, actual child exit code (null only if unavailable), close request,
forced cleanup and boundary. Only actual exit0 without forced cleanup receives
normal-exit-observed; nonzero-exit-observed and censored remain distinct. Cleanup/hash/report
write faults make the run incomplete and cannot produce a zero client exit. Owned child normal close waits five seconds; if forced
kill is required the boundary is censored, not missing-event evidence. A zero client
exit requires all observations, unchanged fixture, no kill, actual child exit0.
This says nothing about original external probe acceptance, real reader speech,
foreground physical keyboard input, or complete provider entry coverage.
## Local verification (build/source only)

On 2026-10-01, Release compilation returned exit0 with zero warnings/errors.
After the Source301 classification and enumeration-callback containment adjustments,
the corrected permanent client build again returned exit0 with zero warnings/errors.
No editor or UIA-client executable was launched. Static source controls confirmed absence of activation, thread attachment,
global input/clipboard/global focus/source reset APIs; exactly one F6 post, one
GoTo Invoke and one distant cell SetFocus callsite; and no raw identity/path/text
fields in report DTOs. These checks do not certify native execution or UIA routing.

The final source-extracted controls (no WPF/native code compiled) passed 11/11:
before-query failure prevents body entry, after-query failure preserves the real action
result but makes report classification incomplete, primary-only contained failure remains
a true action observation, discovery-before unavailable is not a query fault, and prompt
ambiguity is refused. Replacing the final classifier with the old unconditional observed
value fails the same control. The retained earlier eight-control negative detects the
original swallowed before-query error. These are source-branch controls, not native/UIA
runtime evidence. Build and control logs are under `.cache/validation/windows-grid-focus/`.

Source identity references:
- `src/Mote.Native/Windows/Canvas/WindowsRichEditIsland.cs`: registered
  MoteInteractiveCanvas direct child0, RICHEDIT50W input301; binding makes input visible.
- `src/Mote.Native/Windows/WindowsCsvGrid.cs`: Grid104, group1204, scrollbars1104/1105,
  coordinate BUTTON1205.
- `src/Mote.Native/Windows/WindowsEditorShell.cs`: initial/source focus targets the
  canvas InputHandle, not the canvas body; native F6 prunes invisible/disabled panes.
