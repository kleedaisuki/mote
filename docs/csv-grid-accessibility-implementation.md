# Bounded CSV Grid accessibility implementation

Date: 2026-10-01. Status: **implemented experimental opt-in, locally frozen;
scoped Windows x64 external evidence, not reader or release acceptance**.

## Frozen internal model and ownership

`src/Mote.Native/GridAccessibility.cs` implements snapshot-free immutable window facts,
absolute coordinates and a pure exact-rectangle reducer. Platform adapters remain the
sole selection/focus owners. The provider does not parse, decode source, await Formats,
modify Engine, publish a clipboard or own an independent selected-cell set.

Window identity includes document generation/version and an adapter-issued window
serial. Providers retire children when placement, shape, projection or command
readiness changes. Selection-only changes preserve window identity; each mutation
uses the adapter's current **complete** retained selection, including off-window
endpoints, rather than an earlier provider-read intersection.

The common frame exposes bounded local row/column counts and one-based absolute CSV
labels. Null delivered descriptors remain Pending. Complete empty, Missing,
Oversized, Clipped, syntax-error and sanitized display descriptions are distinct.
Read-only presentation values exist only for Complete/Clipped and are not exact
clipboard data. Pending navigation drops retained ready projection authority.
Geometry and actual UI-thread/native focus readback remain platform-specific.

Mac representation has since evolved in response to actual two-RID external
row-bridge failures: a stable non-view Table root now replaces the native
NSTableView semantic subtree, while preserving native rendering/input and the
same epoch-bound frame/children. The AXRows-only legacy experiment was rejected.
See [the proxy candidate and pending target gates](validation/mac-grid-table-proxy.md);
earlier hosted in-process evidence does not certify this changed representation.

`NativeGridAccessibility.Mutate` admits singleton add/remove only when the exact
result remains a rectangle: adding cannot select the entire bounding hull when
that would add extra cells; removing cannot create a hole or split. Whole-row
add/remove is refused because bounded visible columns cannot prove whole-row set
semantics. AX array replacement deduplicates at most 8192 entries, validates every
entry in-window, and tests cardinality against the bounding rectangle. Empty
replacement clears the entire retained selection.

## Deliberate action gate

Source-command Invoke/AX press/Copy/Replace are **omitted**. The existing void
adapter command event is not acknowledgment. The controller admission/token/
terminal-outcome seam in the design contract has not been implemented or externally
accepted. Existing native keyboard/menu commands retain their established behavior.
This is a narrow coherent first stage, not a claim of accessible source-command
completion. Both adapters must block unsafe native focus transfer during actual
source composition through the shell's `IsTextComposing` callback.

### Windows keyboard pane access

Root explicitly authorized a narrow F6 / Shift+F6 cycle: source, Grid Table,
logical row scroller, logical column scroller, Go-to, then source. The shell
prunes hidden/disabled controls and refuses composition or foreign-thread
transfer. Each successful operation reads back actual native focus. F6 is
handled before existing accelerator translation; source Tab, Ctrl-key routes
and reader modifiers are not repurposed. The change does not call the external
COM focus boundary. Native owned offscreen HWND verification passed **1/1**:
forward/reverse, unavailable-axis pruning, hidden Grid pruning, composition
refusal and unchanged source-control text. This is synthetic native evidence,
not physical keyboard, Narrator or cross-platform parity. Mac retains native
standard traversal pending its separate target evidence.

## External design evidence

- [Microsoft ProviderOptions](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.provider.provideroptions)
  specifies COM threading expectations; the flag is not proof of actual apartment
  marshalling. External MTA callbacks must determine whether selection/focus can be
  offered synchronously or require a verified boundary.
- [W3C grid/table properties](https://www.w3.org/WAI/ARIA/apg/practices/grid-and-table-properties/)
  supplies an explicit unknown-total sentinel for ARIA. That **web-only** mechanism
  is not transplanted into UIA/AX, whose local table count contracts differ. This
  reinforces separating proved navigation domain from bounded native table scope.
- [Rich Screen Reader Experiences for Accessible Data Visualization, IEEE TVCG](https://vis.mit.edu/pubs/rich-screen-reader-vis-experiences/)
  studies the combined role of structure, navigation and descriptions rather than
  treating a flat textual alternative as sufficient. Applied here as design
  motivation for named extent/status plus navigable cells, not evidence that this
  implementation works with Narrator or VoiceOver.
- [Visual Cues for Data Analysis Features Amplify Challenges for Blind Spreadsheet Users, CHI 2024](https://research.monash.edu/en/publications/visual-cues-for-data-analysis-features-amplify-challenges-for-bli/)
  reports a study of 12 blind screen-reader users. It motivates explicit state/
  coordinates and actual reader-task validation; it does not establish our native
  provider ABI, tree merging, or focus behavior.

## Verification ledger

Portable shared model tests: **16/16 Release passed**, including an independent
finite-set oracle in the test suite and reversed endpoint/focused-Pending regressions.
Independent model review additionally checked 8,192 endpoint-direction/set cases;
three failure-directed post-fix assertions passed without repeating that oracle
([model review](reviews/csv-grid-accessibility-model-review.md)). The review found
and resolved Pending focus and endpoint-role normalization defects. The later
exact-empty regression additionally proves zero rows/columns and no nonexistent
selected/focused cell, without clearing legitimate off-window selection merely
because an unknown/prefix window happens to be empty. The latest shared filter
also ran three new Windows provider tests for **19/19 combined** passing cases;
that overlapping count is not an additional 19 model cases. Native review
remains separate ([native review](reviews/csv-grid-accessibility-native-review.md)).
Windows/UIA and Mac/AX target evidence will be recorded in their platform-specific
validation documents. Cross-RID AOT
inventory, in-process selectors, external APIs and reader speech are separate gates.
No screen-reader, real IME or whole-file Table acceptance is inferred from compilation.

### Failure-directed Windows target findings

The first external x64 AOT MTA client could read the bounded Grid and header/value
relationships, but SelectionItem actions reached a non-owner callback thread and
were refused. A controlled STA / UseComThreading experiment also failed to establish
owner-thread callbacks; it is separate JIT negative evidence, not AOT acceptance.
This led to a bounded selection-only HWND request boundary with atomic cancellation
versus callback-free coherent commit. External Focus is conservatively refused
before any native side effect; no transport timeout pretends it can cancel an
already reentrant native focus transfer. Both native registrations remain opt-in.

A later fresh AOT client additionally found that returning a custom Simple provider
just to label the native scrollbars removed their expected native RangeValue
patterns. Host-provider presence **did not preserve those patterns**. The negative
record is `.cache/windows-grid-accessibility/external-aot-final.json`. Native names
and pattern preservation must be proved together; a label/F6 success is not value
acceptance. A controlled native dynamic-annotation attempt also failed to expose
the required native pattern. Root approved the smallest truthful alternative:
complete **read-only** RangeValue over the actual admitted axis, with no writer
dispatcher. Its maximum is the last legal zero-based window origin, not the last
data ordinal. Prefix totals remain explicitly unknown; Unavailable omits the
pattern; exact-empty invents no first CSV coordinate. SetValue always refuses.

The final ownership-filtered external client passed bounded Table/read-only
values/header relationships, exact rectangle selection/refusal, all three UIA
tree views (1104 custom children / zero duplicates), read-only range facts,
synthetic own-thread F6, Go-to Row 1001 / Column 17 and stale-cell retirement.
The global focused element was outside the exact target PID; the client did not
inspect foreign semantic metadata. **Global foreground/AT focus remains blocked**,
and earlier unfiltered label observations are not accepted as stable focus parity.
See [Windows evidence](validation/windows-grid-accessibility.md) for exact reports.

Tested strict AOT: one 7,068,160-byte `mote.exe`, SHA-256
`BE2D17B41853A586F080F4DA3094203C2E9DD0A2AF57FF6B653DDB5B8708D0A0`.
The sorted production C#/project inventory before/after publish matched
`6BD6F9BE93C2E3D579D76B10C40AF6D92DB2B418E000FFEE2E33DF8473483BD6`,
including the shared Save production. The final code then received two small
failure-directed guards: Pending-focus refusal occurs before native transfer and
Pending cells are not keyboard-focusable; final Detach clears the action owner
and instance geometry delegate so retained stale COM nodes cannot indirectly
root a disposed adapter/controller closure. The two role/focus checks and the
retained-node/actual-COM weak-reference checks passed. These are deterministic
admission/reachability proofs, not a 100 MiB RSS claim. The AOT normal matrix is
reused as prior evidence, **not claimed to include those final guards**.
Only `WindowsCsvGrid.cs` and `WindowsGridUiaProvider.cs` differ from that binary's
inventory. Current frozen inventory:
`C24DB00BF6C6B6E3E05D175379D5B900EDE28E0F254BE08376C98911101728A3`.
This supersedes `A242175E...` only because root normalized one extra EOF blank
line in `WindowsCsvGrid.cs`; no executable code changed. The exact 141-file
`src/**/*.cs` / `.csproj` coverage and original byte-hashing procedure are
recorded in the Windows evidence document. Test EOF normalization is outside
this source inventory.
Root integration owns the fresh broad test and four-RID AOT rebuild.

## Failure-directed focus investigation: 2026-10-01

The unchanged external Windows client at run `36837499493`, commit
`a33c5ca8451081aa9a21a8589d832c1a12ea0637`, actually exited 1 and classified
the x64 sample `product-fail`. Its report and driver inventory are retained in
`.cache/ci-36837499493-flow-gate/windows-grid-accessibility-ci-win-x64/`.
The preceding `b4093b8` comparison report has the same failure shape; this is
not attributed to the later Mac Flow gate or trace-performance changes.

The report's first and last `SyntheticF6Expected` handles are identical:
`1048752`, the Table HWND. The client labels its pre-cycle owner-thread
`GUITHREADINFO.hwndFocus` as `sourceFocus` without independently establishing
that it is the source HWND. Each of the first four observed transitions is
exactly one pane ahead of the expected target. The ownership-filtered semantic
facts agree with the **observed** next pane, not with the expected target. Thus
those two scroller HasKeyboardFocus failures do not, by themselves, establish
incorrect scroller publication: they sampled a different physical focus target.
Initial focus ownership, keyboard traversal, cached property publication and
external cell focus must be discriminated rather than conflated.

The external cell call returned without the expected InvalidOperationException.
The report's fixed `ExternalCellFocus` text claiming an off-owner callback is
not a callback-thread measurement. The native shell now initializes its owner
COM STA (`1a3bbef`), and the Grid advertises UseComThreading. Whether the actual
callback is owner-thread and performs a safe admitted focus transfer remains to
be proved; no success or off-owner safety claim follows from the fixed text.

The retained native pane regression sets source focus only **after** Grid
installation and Show, so it cannot falsify an initial installation/show focus
transfer. The next inexpensive discriminator must set source focus first and
check each transition without a repair/reset between them. No production focus
reset, retry, changed external oracle or broad unsupported-focus policy is
justified by the current evidence.

Primary contracts checked:

- [GetGUIThreadInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getguithreadinfo)
  supports an explicitly identified GUI thread; the returned focus handle is a
  native fact, not an assertion about the source pane's identity.
- [SetFocus](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setfocus)
  requires the caller's input queue and synchronously sends focus messages; its
  return is the previous HWND, not a Boolean success acknowledgment.
- [ProviderOptions](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-provideroptions)
  defines ProviderOwnsSetFocus as `0x10` and UseComThreading as `0x20`. These are
  not measured apartment delivery evidence.
- [IRawElementProviderFragment.SetFocus](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.provider.irawelementproviderfragment.setfocus)
  distinguishes framework HWND focus from provider-owned focus. Preserve the
  explicit no-native-side-effect off-owner refusal rather than introducing a
  timeout focus dispatcher whose native effects could finish after refusal.

Research motivation is reused from the contract's CHI/TVCG sources: reader
orientation needs actual structure/navigation/feedback evidence, not merely a
truthful label. No new literature claim or physical-reader result is inferred
from this narrowly scoped investigation.

### Passive visibility mechanism and pending discriminator

Root approved replacing the six Grid child `SW_SHOW` calls with `SW_SHOWNA`:
the [ShowWindow contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow)
distinguishes activating `5` from nonactivating `8`. These surfaces are installed
by asynchronous ready/pending analysis, not an explicit user input action.
`WindowsGridInterop.ShowWithoutActivation` names that mechanism;
`WindowsCsvGrid.Show` uses it for group, table, both scrollers, status and Go-to
visibility. Hide remains `SW_HIDE=0`; keyboard F6, pointer selection, admitted
owner-thread cell Focus and source Reveal retain their explicit focus paths.
No global source-focus reset, COM apartment change, focus timeout dispatcher or
provider/pattern ABI change was made. This is an independently justified passive
visibility correction, **not a proven cause or cure for the hosted failure**.

The added owned-window regression establishes source focus before Grid
construction, Install, SetNavigation, Resize, Show, repeated same-version
installation, selection-only mutation and provider reads; it does not reset
focus to repair a failed transition. The retained F6 regression now checks both
complete forward/reverse native cycles and both scroller/table/cell cached focus
facts at each target. These are managed-provider reads over owned HWND seams,
not an external COM or physical-reader certificate. The original external
client and its refusal/selection/navigation assertions remain unchanged.

No local HWND test was executed: even the retained offscreen NOACTIVATE setup
must not be assumed incapable of disturbing the desktop while activation is the
disputed boundary. Root owns hosted execution on both Windows architectures.
The modified Native project compiled in Release with zero warnings/errors; log
is `.cache/validation/windows-grid-focus/focus-native-build.log`. The final test
source had previously compiled cleanly, but a subsequent concurrent TOML fixture
resource addition temporarily made the full test project build fail with
`CS1566` until that resource was materialized. After its writer confirmed a
complete write, one final `dotnet build tests/Mote.Tests/Mote.Tests.csproj
--no-restore -v minimal` completed with zero warnings/errors; retained log is
`.cache/validation/windows-grid-focus/focus-discriminator-final-build.log`.
The missing-resource failure remains separately retained. Neither compilation
result is a Grid runtime test result. Runtime correctness remains unverified
until the actual hosted owned seams and unchanged external client execute.

## Focus provenance construction after CI 36858899063

The current final-source integration at `c856816` establishes both new managed
owned-HWND regressions on both native Windows architectures (2/2 each), while
the original published-AOT client still exits 1 with the same seven product
errors on each RID. The separately owned authoritative hosted audit is
[Windows Grid CI evidence](validation/windows-grid-accessibility-ci.md).
Passive SW_SHOWNA is therefore **not established as a cure**. Do not repeat the
successful owned-HWND test or reinterpret its scope as the missing cross-process
callback proof.

### Missing observations and source constraints

The native adapter's `Focus` admits only its captured managed owner thread;
off-owner calls return Unsupported before native SetFocus. The generated COM
provider advertises `UseComThreading`, and the shell requests STA before HWND
creation. The fixed external report text provides neither the native callback
thread relation nor the actual adapter return. Legitimate owner-thread delivery,
real off-owner refusal, and UIA framework behavior must be separate outcomes.
The initial physical pane also needs an independently established source HWND,
not the client's arbitrary initial GUITHREADINFO focus handle.

An external client alone cannot measure the server callback's native thread:
client apartment/thread IDs, a provider option flag, a normal COM return, and
FocusedElement are not server-thread witnesses. A timeout-driven focus dispatcher
is explicitly rejected: native focus can reenter and complete after timeout,
so a failed caller receipt cannot promise no late effect. No FocusSource reset,
SetForegroundWindow, AttachThreadInput, global event tap, retry, or changed
legacy oracle is part of this construction.

### Proposed opt-in production boundary (awaiting root approval)

Instrument **only the actual Windows Grid provider Focus invocation**, before
and after its existing sole adapter action; do not instrument ambient Activity
or infer a request from selection/focus labels. A Windows-only observation
interface implemented by the adapter supplies content-free native samples.
It does not change shared Engine/Formats/Mac actions or focus admission.

| Fact | Construction | Persistence constraint |
| --- | --- | --- |
| Actual callback thread relation | GetCurrentThreadId versus GetWindowThreadProcessId of the live table HWND, independently compare with the existing managed owner admission | Separate closed native_thread_relation and managed_admission_relation owner/non_owner/unknown fields; no numeric thread/PID/HWND values |
| Physical pane before/after | GetGUIThreadInfo for that explicit native owner thread; compare returned HWND against actual source/table/row/column/coordinate handles | Closed source/table/row_scroller/column_scroller/coordinate/owned_other/outside/none/unavailable names only; inspect no foreign metadata |
| Source identity | Shell passes its actual native input HWND (Canvas input or legacy source) at Grid creation; enabled capture validates live PID/thread/parent identity and rejects changed adapter lifetime | Native handle stays process-local; Canvas source absence never becomes a legacy fallback; no document text/path or new provider tree |
| Target | Provider knows table versus cell | Closed table/cell classification; no names or coordinates |
| Admission result | Capture the existing GridAccessibilityResult returned by that exact adapter invocation; preserve translated HRESULT | Closed eight existing result names plus fault; no guessed outcome from HasKeyboardFocus |
| Request identity | Immediately persisted nonambient receipt anchor under the session; terminal has a new span and that receipt as parent | Original-sink once-only completion; no retained Activity, producer lease, context string or fallback to another session |

Proposed names are `native.grid.focus.received` and `native.grid.focus`; the
receipt carries fixed thread/before/target facts, and its terminal carries the
same facts plus after/result. A dedicated typed payload—not free-form attributes
or opaque packed counts—must serialize only closed values. Schema-1 remains
additive; existing Save/menu/input records keep their exact shapes and enum IDs.
Strict readers accept these fields **only** on these two fixed operations and
validate their operation-specific field/status/parent contracts. The generic
privacy whitelist must not start accepting these fields on unrelated records.

The exact receipt boundary is awaiting root's final choice. A lean two-operation
boundary at `WindowsGridUiaBridge.Focus` observes the **adapter attempt**, where
the exact eight-valued result exists before its many-to-one HRESULT map. If
selected, its names must explicitly be `native.grid.focus.adapter.received` and
`native.grid.focus.adapter`; early stale/unsupported `WindowsGridUiaNode.SetFocus`
returns are outside that coverage, and missing adapter receipt is unobserved,
not a certified absence of a provider call. A full Node.SetFocus-entry boundary
instead needs a distinct provider-refused terminal disposition carrying its
actual HRESULT without inventing an adapter result. It must preserve both
existing early-return and actual adapter paths. Neither boundary may use a
capturing lambda wrapper on the disabled path.

For the adapter-specific boundary, freeze action status independently of query
health: Applied/NoChange map to success; each of the other six existing adapter
results maps to failure (a refused focus attempt, not a product/job verdict).
An original thrown action maps to a fault terminal while preserving its throw
and existing COM conversion. An unavailable physical/thread observation is a
closed unknown fact, **not** a replacement focus result; it does not turn an
Applied action into failure or an Unsupported action into success. Observation
serialization/query failure must be contained separately and cannot fabricate
a complete receipt/terminal pair.

The disabled path must branch before any evidence capture/native query, allocate
nothing, and execute the original action exactly once. Enabled observation may
query focus but never sets it. Nonfatal observation faults must not alter the
original return or escape COM, and must not call user-facing error UI. Missing
receipt/terminal, failed query, drop/fault, censored shutdown or multiple requests
remain incomplete/ambiguous, never inferred absence or an invented successful
focus transfer. Published request lineage is a positive method-call witness,
not an OS input delivery or physical-reader certificate.

### Proposed independent sequential external discriminator

Keep `tests/WindowsGridExternalProbe/Program.cs`, its project, all pins and old
assertions unchanged. A separate temporary prototype under
`.temp/windows-grid-focus-provenance/` will become a separately reviewed hosted
client only after approval. It starts one owned published editor with existing
MOTE_TRACE and MOTE_HOME options, no new application CLI. Native child discovery
certifies source and Grid pane handles by exact launched PID/owner GUI thread,
known parent/control identities and native visibility, **before any UIA query**.
It records one physical pane sample at initial discovery, before/after each
original read/selection block, before F6, and before/after one cell SetFocus.
Source can legitimately be absent/unavailable; that is refused discovery, not
an invented source assignment.

Each sequential operation has a fixed operation label and one explicit client
begin/end receipt; no rescue action, focus reset or repeated input occurs.
Mirror the original selection and bounded tree-read sequence to preserve the
suspect context, then post at most one F6 to the physically focused, certified
owned HWND. Retain the independently classified actual pane and the original
unmodified acceptance report; the new client's purpose is provenance, not to
replace its failing expected cycle with a green adjusted oracle. One explicit
cell SetFocus records the actual exception/HResult and before/after native facts.
Server receipt/terminal pairs are reconstructed by their explicit span edges.
There is no proposed cross-process request token: even a unique server pair
within one client interval supports compatibility, **not a certified causal
edge**. Retain the independent client/server graphs and classify cross-process
operation attribution unjoined. No claim that unobserved Focus
callbacks never happened follows from an incomplete or lossy trace.

Both Windows RIDs must run against the strict published AOT binary, once, with
exact process exits and retained raw report/traces. The old external client
remains an independent non-gating product-fail oracle. Portable checks will
cover closed-value serialization, explicit receipt/terminal graph integrity,
default-off allocation, preservation of action/HResult and observation-fault
containment; source/build checks are not native UIA execution.

Primary runtime contracts:
[GetWindowThreadProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid)
identifies the window's creating thread;
[GetCurrentThreadId](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getcurrentthreadid)
identifies the executing callback thread;
[GetGUIThreadInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getguithreadinfo)
queries an explicit GUI thread without adopting foreground/global focus;
[ProviderOptions](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-provideroptions)
documents COM threading/focus responsibilities but is not itself delivery proof.
Existing CHI/TVCG motivation and reader/IME release gates remain unchanged.

### Approved adapter-specific implementation and portable evidence

Root approved the lean adapter boundary. The implemented names are exactly
`native.grid.focus.adapter.received` / `native.grid.focus.adapter`, not the
earlier unqualified proposal. `WindowsGridUiaBridge.Focus` calls
`WindowsGridFocusOperation.Invoke` before its existing HRESULT map. Earlier
`WindowsGridUiaNode.SetFocus` stale/header refusals remain outside coverage.
No client/server graph edge exists; both readers preserve `unjoined` even when
there is only one server receipt inside a client interval.

`focus_before` and `focus_after` mean the explicit owner **GUI-queue** pane
(`GetGUIThreadInfo(ownerThread).hwndFocus`), not global keyboard focus, desktop
foreground, actual key delivery, or photons. A background ARM64 window can retain
queue focus while UIA global HasKeyboardFocus is correctly false. Neither fact
is inferred from the other. The production sample requires the checked main as
its queue's active window; modal/unavailable active context yields unavailable
pane evidence without changing the adapter result.

`WindowsGridFocusEvidence.cs` validates main/group/table/source/scroller/button
capabilities with native PID/thread, live parent, fixed class, child control ID
and adapter installation/lifetime checks. The shell supplies the actual source
input capability and mode-specific control ID (301 Canvas / 101 legacy), never
falls back from absent Canvas input, and clears it before disposal. Native class
comparisons use UTF-16 stack storage. Native identity numbers and strings remain
process-local. The independent reviewer found one consequential contract defect
before hosted execution: top-level `GetDlgCtrlID` has no valid meaning, so main
must not use the child ID==0 validator. The owner split main validation from child
validation; both checks now omit top-level ID. See the resolved finding in
[independent review](reviews/windows-grid-focus-review.md) and the
[Microsoft contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdlgctrlid).

The disabled/faulted sink path allocates nothing, queries no evidence, creates
no capturing delegate, and calls the original adapter exactly once. Enabled
query faults become unknown samples; optional Begin/End failures cannot change
the original result or exception. Original action faults preserve the same
exception and produce `focus_result=fault` when observation succeeds, never an
invented adapter result. OutOfMemoryException remains outside the nonfatal guard.
Unknown original adapter enum values keep their existing return/HRESULT behavior
and leave incomplete evidence rather than manufacturing a supported result.

| Retained verification | Result | Scope |
| --- | --- | --- |
| Telemetry producer + prior telemetry regression (`9e80a66`) | 58/58, zero failed/skipped | Dedicated typed payload, exact closed fields/statuses, nonambient receipt/terminal, original sink, once-only terminal; no native callback proof |
| Strict NativeAcceptance reader (`c1255b4`, `873d7cd`) | 14/14 | New fields allowed only on the two operations; all standard dimension/menu/input/Save policies unchanged; shared full row validator |
| Independent server graph (`4a00852`) | 29/29 | Explicit edges, privacy, missing/drop/censored distinctions, no temporal client join |
| Portable native wrapper/classifier (`56e05dc`) | 21/21, zero skipped | Exact eight results/HRESULT map, same action exception, observer/writer faults, unknown result, role reduction; no native DLL/HWND calls |
| Default-off warmed wrapper | 1000 calls, 0 managed allocated bytes, 0 evidence captures, 0 files | Allocation/query evidence only; not a zero-CPU or startup performance claim |
| Native Release compile | Zero warnings/errors | Source/build compatibility, not AOT/UIA execution |

Telemetry TRX is `.cache/validation/focus-telemetry/focus-telemetry-regression.trx`;
graph log is `.cache/focus-graph-validation/focus-graph-tests.log`; portable wrapper
TRX is `.cache/validation/windows-grid-focus-observation/portable-focus-observation-corrected.trx`.
The initial writer-fault fixture failed because it never enqueued a record into
the lazy writer. The validator added a seed receipt, then observed the actual
directory-create fault and the no-query direct action path. Both failed and
corrected TRXs remain retained; this was a fixture correction, not a product retry.
No local GUI/native focus/input/clipboard experiment was executed.

Independent review `86440cc` found no remaining substantive blocker after the
top-level correction and inspected the temporary sequential client. Individual
synchronous UIA calls have no in-process hard timeout: finite polling/Stopwatch
checks do not cancel a hung call. Hosted process timeout and exact owned-tree
cleanup must retain this limitation. Supplemental client promotion, workflow
integration and both-RID published-AOT runtime evidence remain pending; prior
green CI and managed-HWND controls do not certify this new boundary.

Root's post-freeze review identified another concrete ownership gap: native
identity agreement against the table's returned PID alone did not prove that it
was **this editor process**. Destroyed/reused handles belonging to another mote
process could share fixed classes and child IDs. Enabled evidence capture now
requires the table owner PID to equal `Environment.ProcessId` before querying
other role/class metadata. Mismatch yields native unknown/pane unavailable;
managed admission and the original adapter action are unchanged. Disposal still
advances installation and clears source before native teardown, and capture
rechecks installation/main/Table lifetime before publishing a category.

The corrected native evidence file SHA256 is
`2E1D3940F64743126A40540F66D46D2ADDAC516D7A207B3BA19885532FEBB366`.
One affected Native Release build completed with zero warnings/errors; retained
log is `.cache/validation/windows-grid-focus/focus-provenance-process-guard-build.log`.
No completed telemetry/reader/graph suite was repeated for this native-only
guard, and no GUI or HWND experiment was executed. The preceding independent
review does not falsely certify this later delta; root supplied this ownership
finding and owns its final integration inspection.

The follow-up lifetime inspection also distinguishes retirement from generation:
a sample can begin after Dispose increments installation but before HWND
destruction. `_focusEvidenceAlive` is therefore published true only after full
construction, retired by a volatile write at the **first** Dispose step, and
checked before native evidence queries and again before publishing a sample.
This state controls observations only; the original adapter Focus/admission is
unchanged. Root inspected and accepted the two-file correction. Updated evidence
file SHA256 is `83DF8111AC5617356F85B19E2F44AC797D1E65BD84ECA96A6344575CDDF8E0ED`.

One new unpublished-lifetime fixture (`0876d5f`) bypasses the constructor with
RuntimeHelpers.GetUninitializedObject and directly verifies native unknown,
managed non-owner and unavailable pane. Only that new method was executed:
**1/1**, zero warnings/skips; TRX is
`.cache/validation/windows-grid-focus-observation/focus-lifetime-unpublished.trx`.
It creates no HWND and does not call Dispose; Windows return values alone do not
exclude a hypothetical accidentally reached import that returns zero. Non-Windows
hosted execution strengthens the no-Windows-DLL branch proof, but neither case
certifies a constructed instance's actual concurrent retirement. One affected
Native Release build exited 0 with zero warnings/errors; log is
`.cache/validation/windows-grid-focus/focus-provenance-lifetime-build.log`.
The completed 58/14/29/21 suites were not repeated.


### Supplemental client promotion and observer truthfulness closure (2026-10-01)

The coordinator approved the independent client at
`tests/WindowsGridFocusProvenanceProbe/{Program.cs,WindowsGridFocusProvenanceProbe.csproj,README.md}`.
Its legacy counterpart `tests/WindowsGridExternalProbe` remains byte-unchanged against
`5d0fcb6`; legacy assertions, project and pins are not replaced. The new client accepts
exactly editor executable, fresh scratch, and report path; scratch/report stay under
checkout `.cache`/`.temp`, with isolated `MOTE_HOME`, unchanged synthetic fixture bytes,
existing trace/Grid opt-ins and a sequential single F6/GoTo/distant-cell Focus workload.
It never activates or repairs focus. A synchronous UIA hang still requires the hosted
external 120-second bound and exact owned-tree cleanup; polling is not a call deadline.

Promotion inspection found two client ownership/truthfulness defects, not established
product causes. Prompt enumeration now requires exact main ownership via GetParent and
unique eligible identity; ambiguity stops incomplete instead of writing a last-match
prompt. Before observation is now outside action exception classification: query failure
records `not_attempted`, ActionAttempted=false, closed observation error/HRESULT, and
propagates even for the contained cell-focus action. ActionAttempted witnesses client
body entry only, not API/server delivery. After-query failure preserves the real action
return/exception and marks the sample unavailable. Independent review then found Main's
unconditional observed classification: a final after-query fault could still exit0.
The pure final classifier now marks any ObservationException incomplete/nonzero without
relabelling the actual action result. Discover-before unavailable is expected before
identity exists, so unavailable alone is not treated as a query exception.

| Verification | Actual result | Retained evidence |
|---|---|---|
| First source-extracted observation/prompt controls | 8/8, exit0; no WPF/native code compiled | `focus-client-pure-observation.log` |
| Old before-query/action conflation negative | Expected nonzero; `before was swallowed` | `focus-client-pure-observation-negative.log` |
| Final controls, including aggregate classification | 11/11, exit0 | `focus-client-pure-observation-final.log` |
| Old unconditional final classification negative | Expected nonzero; aggregate control fails | `focus-client-pure-observation-final-negative.log` |
| Final permanent Release build with restore | Exit0, zero warnings/errors | `focus-provenance-client-final-restored-build.log` |

Logs above are under `.cache/validation/windows-grid-focus/`; extracted subject markers,
source hashes, fake Snapshot boundary and out-of-scope fake exception mapping are recorded
under `.temp/windows-grid-focus-provenance/pure-observation{,-final}/extraction.json`.
The old temporary client and negative controls are retained, not silently overwritten.
The initial final build used --no-restore but earlier validation had a separate ArtifactsPath,
so the normal project lacked assets (NETSDK1004). That setup failure is retained in
`focus-provenance-client-final-build.log`; the affected build with restore passed. Its
default bin/obj were moved into the same repository `.cache` validation area. No local
editor, UIA client, HWND, global input, clipboard or registry operation was executed.
These checks prove source-level observer semantics/build compatibility, not native routing.
Independent narrow review is `08b1df3` after the finding in `fbc7fcc`.

Frozen normalized SHA256 client source:
- LF: `B7E498A9D6BD190D4F835874E53C818F7A12E542FD9979CD038892280624FBF3`
- CRLF: `B96BD2445B69186FE51941C396ED089D3B431E50243CE1AF86F9674D05C7D33D`
Frozen project:
- LF: `5A442CDB96A250C26556165CABD5C58224378CD4573D6ECC13318DFCFF95F5E2`
- CRLF: `6A99955812E8EA1631792E3EF0B47798B42A1A9F916FE86AF6D992F2E32FA3D4`

The root-found graph resource-bound correction is separately committed as `a276d8a`.
The old eager list(records) consumed all 100017 finite items and reached an infinite
fixture's 100002-item guard. Incremental intake retains at most 100000 records and refuses
the 100001st before validation/storage; exactly 100000 is accepted. The affected suite
passed **32/32**, exit0 (`.cache/focus-graph-validation/focus-graph-bound-after.log`), with
the negative retained in `focus-graph-bound-before.log`. Reader report/API remains unchanged.
Loader bounds are 32 MiB per file, 16384 decoded characters per retained line and 100000
aggregate records; path count/aggregate file bytes have no independent cap, so callers
must supply a finite inventory. This fix does not expand the shared loader policy.

Both graphs remain independent: explicit persisted receipt-to-terminal edges only; no
UTC/unique-count cross-process join, absence_certified=false, and adapter-attempt-only
coverage. Owner GUI-queue focus is not global keyboard/foreground delivery. Any unknown
sample or observer fault remains unknown even if an independent primary action returned.
Supplemental published-AOT runtime evidence on both Windows RIDs remains pending hosted
integration; previously green CI is not evidence of this newly promoted client.
