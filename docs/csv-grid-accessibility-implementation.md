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
`CS1566` until that resource was materialized. That unrelated build observation
is not a Grid test result. Runtime correctness remains unverified until the
actual hosted owned seams and unchanged external client execute.
