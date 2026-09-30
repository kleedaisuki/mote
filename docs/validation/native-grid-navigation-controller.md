# Native Grid navigation controller validation

## Contract and scope

`tests/Mote.Tests/NativeGridNavigationTests.cs` independently checks logical coordinate arithmetic and real `NativeEditorController` event workflows. Expectations derive from certified whole-file extents, a separately constructed 120-record CSV fixture without a trailing delimiter, bounded visible geometry, and the per-gesture authority contract. The fake shell queues real background completions and exposes installation reentrancy; it never accesses the operating-system clipboard, changes input sources, or edits user files. Fixture files remain under repository-local `RepoTemp` directories.

## Evidence

Executed on the local Windows development host using .NET 10, Release configuration:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~NativeGridNavigationTests -warnaserror --logger 'trx;LogFileName=navigation.trx' --results-directory .cache/grid-navigation-validation
```

Observed: **19 passed, 0 failed, 0 skipped**, approximately one second test duration. Evidence: `.cache/grid-navigation-validation/navigation.trx`.

Checked claims:

- Exact normalized endpoints, nonfinite rejection, monotonic mapping, small-domain origin roundtrip, invalid-domain rejection, and saturation at signed 64-bit step extremes.
- Geometry caps obey both per-axis limits and the aggregate cell budget.
- Sparse delivery retains null ordinal slots rather than collapsing gaps or inventing source-backed rows.
- Gesture B supersedes A even on the same document revision; terminal duplication is inert.
- A terminal phase consumes its token before a synchronous native installation callback admits B; B survives A's remaining work.
- Geometry changes advance the coordinate epoch; same-version asynchronous refresh preserves a live gesture token.
- Pending navigation cannot authorize source-backed Copy through an older ready presentation.
- End places the final fully visible page at row 96 for 120 records and 24 visible rows, without changing source, source version, selection-installation count, modified state, or disk contents.
- A source edit retires previous authority immediately.
- Invalid phase/kind and negative coordinates are inert without consuming a valid live token.
- Composition retires navigation before accepting a phase.
- Cancel rejects subsequent phases. Its final accepted-placement semantics are verified in the cancellation delta below; the original latest-Track expectation was superseded.

The initial code inspection identified unchecked `First + long.MaxValue` overflow in the pure step planner. The implementer independently replaced it with saturation comparisons before this test run; the new extreme-step assertion verifies the corrected contract. No original-code executable reproduction was retained, so this is not presented as a before/after regression execution.

## Frozen integration delta

After the controller added native geometry retirement, an 8 ms latest-interest dispatch mailbox, and native delivery failure recovery, the affected navigation test class was revalidated. This run was required by those changed contracts, not a repeat of unrelated completed validation:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~NativeGridNavigationTests -warnaserror --logger 'trx;LogFileName=navigation-delta.trx' --results-directory .cache/grid-navigation-validation
```

Observed: **23 passed, 0 failed, 0 skipped**, approximately two seconds test duration. Evidence: `.cache/grid-navigation-validation/navigation-delta.trx`.

Four additional checks cover:

- `GridGeometryChanged` retires an already-live token immediately, publishes pending state without command identity, and installs a recertified page. Late old phases are inert without requiring a newer Begin.
- Fifty synchronous Track samples advance only the latest placement; no parser schedule starts until the UI mailbox is pumped. Exactly one additional analysis schedule and one successful ready origin (row 50) are observed. The scheduling witness reads `_analysisSerial` only; it does not modify controller state.
- A deliberately queued row-30 parser result is cancelled by a newer row-70 interest before publication. One callback at a time is pumped to distinguish dispatch admission from completion. Row 30 never enters the independently collected ready-installation history, and pending row 70 has no command identity until its own delivery.
- An exception thrown inside the actual `SetAnalysis` boundary before accepting a row-70 grid is caught by production recovery. Last successfully delivered row 20 is retained with `Pending=true` and `Ready=null`; old Copy authority is refused. A later row-60 gesture successfully recovers. The shell counts the actual exception rather than injecting recovery state or directly invoking `GridDeliveryFailed`.

The existing terminal-reentrancy test also passes with the new coalescing path: a synchronously admitted B survives A's terminal tail and subsequently delivers its requested row 70.

## Cancellation contract delta

Independent assessment: restoring the last successfully accepted placement on Cancel is consistent with the implementation document's accepted-placement recovery contract. An uninstalled Track target is only an interest, not evidence of a successful navigation; Cancel must not silently commit it. An unused Begin/Cancel has neither changed cells nor their source map, so preserving its ready presentation identity also preserves a valid frozen source command. The previous test's expected row 30 encoded an unnecessarily stronger commit-on-cancel assumption and was corrected to independently known last delivered row 0.

The changed cancellation tests alone were run:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~NativeGridNavigationTests.Cancel_restores|FullyQualifiedName~NativeGridNavigationTests.Unused_cancel|FullyQualifiedName~NativeGridNavigationTests.Cancel_restoring' -warnaserror --logger 'trx;LogFileName=navigation-cancel.trx' --results-directory .cache/grid-navigation-validation
```

Observed: **3 passed, 0 failed, 0 skipped**. Evidence: `.cache/grid-navigation-validation/navigation-cancel.trx`.

- Pending uninstalled Track row 30 cancels back to delivered row 0 and rejects subsequent old phases.
- An unused Begin/Cancel preserves the exact ready presentation identity, starts no analysis, and accepts a frozen source-backed Copy returning independently expected `r0`; source and disk remain unchanged. Clipboard publication is fake-shell only.
- Cancellation restoring a retained older prefix payload preserves already learned exact same-version navigation totals and issues a new ready identity. This last check injects only a validated, snapshot-matching weaker retained `_gridDeliveredView` payload; it does not modify current `_gridExtent` or call recovery directly. It is a deterministic monotonic-facts contract check, not evidence of a natural large-file Full/Cancel runtime race.

## Coverage limits

These are controller and planner tests, not native scrollbar input, physical painting, accessibility, actual clipboard publication, macOS runtime, huge-file prefix-index scheduling, or latency measurements. They do not establish that a platform adapter captures and dispatches the correct gesture token or computes fully visible geometry correctly. Target-runtime probes and native acceptance checks must supply that evidence separately.
