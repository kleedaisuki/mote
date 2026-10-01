# Windows native source product adapter

## Scope and ownership

`WindowsEditorShell(..., nativeSource: true)` is an explicit, window-lifetime
product capability. It rejects Canvas plus native source. Existing menus, status,
Flow/Block/Grid panes and pane traversal remain the ordinary shell's machinery.
The full-body system `RICHEDIT50W` alone owns source input, layout and selection.
This is not the diagnostic probe window and is not automatic default promotion.

`INativeSourceShell` owns no Engine history or persistence. Its complete source
projection is installed once for New/Open/recovery and certified through
`WindowsNativeSourceSafety.Read` (`EM_GETTEXTEX`, `GT_USECRLF`, explicit returned
UTF-16 length). Embedded NUL is refused rather than truncated. Input remains
read-only before initial certification and after a failed certificate.

Native paragraphs are mapped using `RichEditOffsetMap`. Settled `EN_CHANGE`
readback is coalesced into an owner-window posted message, after native input
returns; only one request, not a queue of source strings, is retained. Each
candidate has the original stamp and nonce. Emission alone is not admission:
exact same-document/nonce version advancement and final replica certification
are required before that command barrier succeeds. Typed failure distinguishes
unadmitted native text (sticky command veto, never implicit discard) from an
already canonical document retained after a native publication failure (later
explicit Save/New remain possible). A subsequent canonical command after an
unadmitted failure offers an owned explicit discard-only-native confirmation.
Cancel retains the readonly replica; confirm requests controller recovery and
requires successful exact installation before proceeding. Native readonly
selection/copy remains available without claiming canonical coordinates. Controller acknowledgement certifies
matching text without importing it. Engine edits use one guarded `EM_REPLACESEL`
against the exact Before string and certify the After projection. The old
bounded `TextChanged` and `SelectionChanged` events do not fire in this profile.

The controller remains the single admission/history/Save owner. Native undo
limit is zero. `EM_UNDO`, `WM_UNDO`, `EM_REDO` and contextual Ctrl-Z/Y/Shift-Z
route through Engine history after `CommitPendingText`; formatting uses the
existing balanced TOM undo suspension. IME identity remains frozen while native
composition is active; final settlement precedes canonical commands. Real IME
journeys are not established by portable tests.

## Selection and bounded decoration

Native `EM_EXGETSEL` gives ordered endpoints. Only a collapsed selection reports
an active endpoint. Noncollapsed direction is deliberately unknown. Programmatic
selection/reveal posts a coalesced observation after the controller's synchronous
mutation guard unwinds. No source rewindowing or whole import is used to reveal
Find/Go To targets.

`EM_CHARFROMPOS` uses actual client-corner `POINTL` hit tests. The exposed interest
is capped at 8192 display UTF-16 units, trimmed inward at scalar/CRLF seams. This
is a **bounded viewport-derived interest, not a complete viewport coverage
certificate**. It never expands a long paragraph to a complete logical line.
The source replica remains whole and semantics remain canonical; this cap does
not declare offscreen analysis complete or truncate editable source.

Foreground planning is last-writer-wins in canonical publication order. Adjacent
equal colors coalesce. Detached TOM ranges apply at most 128 foreground calls
per timer turn, without selection changes, text imports or keyboard-attribute
updates. New identity or theme invalidates queued work; missing/currently stale
semantics produce neutral visible foreground. Known decoration failure is
reported rather than certifying stale colors. Legacy whole-source RTF styling
and full-source foreground reset are excluded from native-source `SetTheme`.

Product-native readback and actual nonempty bounded style turns record
`NativeSourceReadback` / `NativeSourceStylePublish` with fixed outcomes and
version/count dimensions. `DocumentBytes` is omitted; UTF-16 residency is not
claimed as encoded file bytes. Failures including native cleanup keep failure
status. Controller install/reconciliation/range scopes supply causal parents.

Native draw tracing uses the installed source stamp and the existing
`NativeDrawTrace`; successful paint return remains a draw submission witness,
not physical screen presentation.

## Verification boundaries

`WindowsProductSourceTests` checks pure clipping/coalescing, overlapping token
precedence, neutral presentation, 100,000-character line interests, empty ranges,
scalar/CRLF seams, and nine exact acknowledgement acceptance/refusal conditions. These tests do not create a native window. Coordinated
build/test results will be recorded by the product delivery owner.

Actual hosted Windows x64/ARM64 launch, native import/layout limits, external
input, selection/scroll conservation, IME, UIA canonical source geometry,
new-process exact reopen and latency remain required before promotion. Native
RichEdit accessibility support alone does not prove canonical CRLF/source range
compatibility. This implementation does not claim accepted 100-MiB editing.

## Platform references

- [Microsoft EM_CHARFROMPOS](https://learn.microsoft.com/en-us/windows/win32/controls/em-charfrompos):
  RichEdit takes `POINTL*` and returns its native UTF-16 character position.
- `WindowsRichEditForegroundRange` records the previously qualified Windows SDK
  TOM ABI and detached-font ownership. Product code reuses that implementation;
  it does not introduce reflection COM activation or runtime-generated interop.
- `docs/architecture/ordinary-editing-locus.md`, section 8, specifies admission,
  marked-text, history, selection and product promotion contracts.

### Qualified portable checkpoint (2026-10-02)

The product delivery owner ran the coordinated Release build and selected
portable tests. Independent inspection of
`.temp/native-source-product-test-2/native-source-product-2.trx` confirms exactly
**18 WindowsProductSourceTests results, all Passed**, with no skipped results.
They are part of the 78-result selected suite, not 78 Windows-specific cases.
`.temp/native-source-product-build-5.log` reports **0 warnings, 0 errors**,
4.23 seconds. Earlier failed compiler attempts remain in build-1/build-4 logs;
no failed invocation is counted as acceptance. No native window, global input,
clipboard or GUI probe was run by this workstream.

Exact SHA-256 identities at this checkpoint:

| Artifact | SHA-256 |
| --- | --- |
| WindowsEditorShell.cs | `040496D819460A0635F8A7F2BB5C6D644B7D31EFBF7C0D942FDA83E2D2C5CC41` |
| WindowsEditorShell.Source.cs | `1B79FF9932C1E29CE5AB26A10DBC888207C33541631DC9B0DD1AE825AD84444C` |
| WindowsSourceForegroundPlan.cs | `D344641A7CE5FC217B337E14002DD2F209C590393B00BCEC8686E63BC537AC0F` |
| WindowsSourceSettlement.cs | `4DB60D3BBFF87714080F437BEC8FFB5FE38B337D502C6280CA907C703B72FCC6` |
| WindowsProductSourceTests.cs | `B4D878C5FD21BC9A2B2FA8463D60055923CEE67C8EA7BDDFAD38C99D587CAE24` |
| Native Release mote.dll | `06DA3AC98CECA67F1D3AEAE6E157884121C27DA821C8D33CFB4A5CBAC6CD4AE4` |
| Tests Release Mote.Tests.dll | `643DCBA28026E47A3E6B1B44C98C5D68DE39DAAD6D7D32B16E85B8DBFEF121FB` |

These witnesses establish compilation and pure planner/acknowledgement behavior,
not RichEdit import, native viewport, composition, reader, timing or hosted
product acceptance. Those remain the runtime obligations listed above.

### Post-review correctness delta: compiler-only checkpoint

A later independent review found two concrete correctness gaps. The owned
recovery `MessageBox` can pump posted work; consent is now guarded against
nested prompts and bound to a recovery epoch, typed failure, original installed
identity and owner window. Successful certification and every unavailable
transition advance that epoch. A Yes response for changed state refuses recovery
instead of transferring consent to a later document/failure. The prompt guard
always clears in `finally`.

The unadmitted-source Copy salvage case formerly preceded CSV grid focus routing.
The grid-focused Copy branch is now first. Source salvage additionally requires
actual thread-local `user32!GetFocus() == _editor`; preview or another focused
control cannot enter that branch. This is a static routing inspection, not a
native clipboard/focus execution result.

The delivery owner's `.temp/native-source-product-build-7.log` was independently
read: **0 warnings, 0 errors, 4.52 seconds**. This qualifies compilation of the
modal/focus correctness delta only. The prior 18/18 portable test result remains
qualified for its earlier tested source; it was not replayed or relabeled as
post-delta native modal/copy validation. No new GUI or runtime acceptance was run.

Current post-delta SHA-256 identities:

| Artifact | SHA-256 |
| --- | --- |
| WindowsEditorShell.cs | `B1EFC44A3FB36BC5A78FA6980C2848F344A18E9D57942D7270B69088380AE98A` |
| WindowsEditorShell.Source.cs | `55926B0396C7FA4D7B7A12A32CDEDD2F4D77BCD5BB93DEE252E0E48C777CF88E` |
| Native Release mote.dll | `02FA47C501A048CB71E7E456AA4AA267401F8D149B845B71C959E76D5F4DAC97` |
| Tests Release Mote.Tests.dll | `A4C7943FEDC385E11F389DA35D75F7C4083F619D1D7F732187CD47DD1E66E933` |
