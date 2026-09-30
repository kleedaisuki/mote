# Native CSV Grid implementation

Status: bounded native implementation, focused local/synthetic hosted evidence,
scoped real Windows/macOS clipboard acceptance and experimental bounded Windows
UIA read/selection/navigation evidence; **not complete desktop, reader or release acceptance**.
The reviewed [Grid architecture](csv-grid-architecture.md) and shipped Formats
contract (`9034fbe`) remain the semantic source of truth.

## Ownership and invariants

- Engine remains the sole text, dirty-state and Undo owner. Native table rows are
  bounded immutable `GridRenderProjection` data, not a second document or parser.
- `NativeFormatSessionDriver` accepts an optional CSV request and calls
  `ICsvGridFormatSession.AnalyzeGrid` once on its existing serialized lane. Source
  tokens and table data belong to one snapshot. The driver never invents a hull
  across disjoint certified source intervals.
- Controller captures source-follow or detached row/column interests on the UI
  thread. Initial bounds are 64 rows and 16 columns; native requests are rejected
  beyond Formats' 256-row/64-column/8192-cell limits. Editing, changing formats or
  replacing documents invalidates detached ordinals instead of shifting them.
- Native callbacks read ready projections only. `NativePresentationId` includes
  document generation/version and a same-version installation sequence. Commands
  reject old identities before revealing source, publishing clipboard data or
  applying an edit; async clipboard publication repeats this admission.
- Idle Full indexing does not overwrite a table with textual Flow: after its
  committed result the controller re-queries the latest bounded table interest.
  Plain/Markdown and explicit source-only layout retain their existing behavior.

## Commands

Ordinary cell selection does not focus or mutate source. Reveal selects the proved
whole-field syntax and focuses source; display offsets are never interpolated.
Clipboard preparation occurs off-thread through additive Formats-owned
`CsvGridCommands`, from exact source spans rather than the display arena. Native
only admits presentation identity and maps UI intent; CSV/quoted-TSV grammar,
quote-style preservation and edit preparation are policy semantics. Obsolete
clipboard jobs are cooperatively canceled. Missing/pending/malformed cells refuse decoded Copy; explicit padded CSV is
a separate intent. CSV and quoted TSV encode every empty value as `""`, preserving
trailing/consecutive empty records. Source-row Copy retains actual delimiters.
Prepared payloads are bounded at 8 Mi UTF-16 units. Embedded U+0000 refuses native
text Copy before any adapter clipboard mutation, including exact source Copy;
size refusal does not assert that unread data was NUL-free. Native publication
failure is reported separately and does not promise rollback of the old clipboard.

Replace cell uses a temporary platform value editor. Only a complete bounded,
syntax-valid field is admitted; the controller settles source input before the
command and repeats identity admission after the modal editor. The helper prepares
one exact field `TextChange`, preserving existing quote style, escaping quotes and
quoting delimiter/newline values. One Engine apply means one Undo transaction;
other fields and source record delimiters are not normalized. Cancel/stale dialogs
do not modify Engine state.

## Evidence ledger

| Evidence actually executed | Result / scope |
| --- | --- |
| Driver Grid + existing serialized Flow/range/idle tests | 19/19, including seven new Grid cases; no repeat CSV parser analysis |
| Formats direct command + Native command mapping + controller tests | 59/59 after policy extraction; overlapping families, not additional to their component counts |
| Source-follow recovery / exact-empty ordinal admission | 3/3 new cases only; ignored far/negative row recovers source caret without source mutation |
| Native sparse ready-row/cell lookups | 2/2; sorted coordinate gaps do not imply contiguous list indices |
| Windows hidden HWND core | 6/6; real owner-data callback buffers, actual hit testing, selection/identity/focus and multiline input readback |
| Windows native scrollbar-edge case | 1/1; bounded overlapping row/column requests |
| Windows known-extent numeric navigation | 1/1; explicit refusal before emitting an out-of-range request |
| Windows focused command after lazy Grid creation | 1/1; ordinary source/Flow startup does not create an unused table |
| Windows visibility reentrancy correction | Original unchanged failing Flow test 1/1 after fix; affected Flow/Grid suite 18/18, including a new actual HWND Grid counterpart |
| Native / Formats Release warnings-as-errors | 0 warnings / 0 errors in the relevant owner runs |
| macOS target probes | Published-binary in-memory Grid and source-NUL checks passed on both Mac RIDs in CI 36746843707; see hosted result below |

Independent review exposed and corrected hidden-source Cut routing, selection
loss on same-version refresh and modal context-menu identity rebinding. Independent
controller validation exposed a legacy page retaining its old length after a
structured replacement; resetting page capacity corrected the view truncation,
and exact disk Save plus one Undo independently confirmed source conservation.
Continuous controller coverage also checks source-backed bindings, no hidden
SetDocument mirror, replacement/Save/Undo and composition/stale-Copy admission.

Root's full integration run subsequently found one genuine Flow regression
(804 passing / one failing Mote.Tests case): Grid integration moved synchronous
`ShowWindow` calls after the existing freshness checks. `WM_SHOWWINDOW` installed
a newer presentation, then the older outer call overwrote it. The unchanged
original assertion reproduced the failure. Guards after **each** table/preview
visibility call and after preview installation now preserve the newer nested
model and status. Independent review approved the correction; the original case
and affected 18-case Flow/Grid suite passed, with no assertion weakening and no
changes to the committed source-NUL safety paths.

The target route `--check-native-mac-csv-grid` owns only small in-memory data and
native views. It never publishes a clipboard value, opens source files, changes
input sources/TCC settings or injects external events. Its synthetic direct native
events and delegate calls do not prove physical keyboard/menu/IME or AT behavior.
Root-owned CI wiring was dispatched only after final source-hash safety approval.

### Hosted target checkpoint (2026-10-01)

[CI run 36746843707](https://github.com/kleedaisuki/mote/actions/runs/36746843707)
at `526cc9b` passed all six strict jobs: Windows/macOS solution tests and
single-binary Native AOT on win-x64, win-arm64, osx-x64 and osx-arm64. The
separate non-gating, published-binary in-memory Grid and source-NUL diagnostics
both exited zero with their exact success markers on
[osx-arm64](https://github.com/kleedaisuki/mote/actions/runs/36746843707/job/109995069624)
and [osx-x64](https://github.com/kleedaisuki/mote/actions/runs/36746843707/job/109995069743).
The Grid probe exercises a real `NSTableView`, bounded ready-cell/selection
readback, keyboard intents and identity transitions without publishing clipboard
data or editing a file. The separate NUL probe checks an owned `NSTextView`,
native insertion/notification and unsaved Engine Apply/Undo/Redo. These results
do **not** prove a full controller Save/reopen on Mac, actual clipboard
publication, physical keyboard/IME, external AX/VoiceOver, complete logical
scrollbar behavior, large-file responsiveness or pixel presentation.

### Actual clipboard target checkpoint (2026-10-01)

[CI 36752189587](https://github.com/kleedaisuki/mote/actions/runs/36752189587)
passed seven strict jobs, including a dedicated disposable Windows runner test
using a hidden production owner-data table, actual controller/publisher and
independent `CF_UNICODETEXT` readback. Its fresh exact report has
`status=passed`, `nativeClipboard=true`, current run identity, reviewed CRLF
source hash and all six case IDs. This is a freshly built **managed** test
harness, not Windows Native AOT clipboard execution
([Windows workflow evidence](validation/native-csv-grid-clipboard.md)).

[CI 36756839425](https://github.com/kleedaisuki/mote/actions/runs/36756839425)
at `7420d0b` passed **nine strict jobs**, including separate gating disposable
Mac x64 and ARM64 clipboard jobs. Both newly published strict single-binary
Native AOT executables ran the reviewed hidden production `NSTableView`, actual
controller and production `NSPasteboard` publisher workflow. Each exited zero,
emitted the exact actual-mode marker and produced a fresh `status=passed`,
`nativeClipboard=true` report with current run key, reviewed LF source hash
and all six case IDs; stderr was empty
([Mac workflow/report evidence](validation/native-mac-csv-grid-clipboard.md),
[reviewed invocation](validation/native-mac-grid-clipboard-invocation.md)).

| Independently expected case | Acceptance boundary |
| --- | --- |
| Quoted CRLF | Exact decoded CRLF/tab/quote payload, not sanitized display |
| Empty final row | Exact empty-record encoding, no invented terminal separator |
| Missing refusal | Delivered rejection before publication; sentinel unchanged |
| Explicit Missing padding | Exact explicitly requested CSV padding |
| Embedded NUL refusal | No successful publisher call; sentinel unchanged |
| Over-cap refusal | No clipped success or publication; sentinel unchanged |

Copy leaves canonical source identity/version, selection, modified/history state
and fixture bytes unchanged; Undo adds no transaction. Mac refusal cases also
check unchanged pasteboard `changeCount`. Actual-mode mutation is approved only
on reviewed disposable GitHub-hosted runners, not personal or self-hosted hosts;
old clipboard contents are deliberately not preserved or restored.
These results establish hidden-table/controller publisher/readback semantics,
**not** desktop menus/pointer/shortcuts, Mac source NSTextView editing, external
Grid AX/UIA/VoiceOver/Narrator, real IME, clipboard contention/failure recovery,
physical paint, Universal Clipboard behavior on a personal Mac or latency.

### Logical navigation implementation checkpoint (2026-10-01)

Commit `a604ae9` installs independent logical row/column scroll controls rather
than treating retained native rows as the complete file. Certified extent is
explicitly unavailable, prefix or exact; fully-visible page geometry and bounded
ordinal slots preserve gaps without inventing source authority. Navigation
uses document/epoch/gesture identity, and ready source commands retain their
separate presentation identity. The shared dispatcher reserves mandatory content
and Full work while coalescing viewport traffic; the 8 ms mailbox interval is a
scheduling choice, not a measured response guarantee
([implementation and scheduling evidence](csv-grid-logical-scrollbar-implementation.md)).

The local affected Release warnings-as-errors run passed **184/184**, including
**16/16 Windows real hidden-HWND** cases and **11/11 portable Mac interop**
cases ([Windows evidence](validation/windows-grid-logical-scrollbars.md),
[Mac portable scope](validation/mac-grid-logical-scroller.md)). Commit `b7ab02a`
wires the separately reviewed native AppKit scroller probe; both published
macOS x64/ARM64 processes then exited zero with exact reviewed markers in
[CI 36764576285](https://github.com/kleedaisuki/mote/actions/runs/36764576285)
([probe acceptance boundary](validation/native-mac-grid-scroller-probe.md)).
This supersedes the bounded-native-scrollbar-only implementation limit, not
external UIA/AX range/focus, desktop thumb/trackpad behavior, overlay pixel/hit-area
acceptance or end-to-end large-file performance. No clipboard/reader/IME claim
is inferred from portable arithmetic or hidden-control tests.

### Remaining release gates / deliberate truthful limits

- The bounded-window accessibility model and experimental native UIA/AX adapters
  are now implemented locally ([implementation ledger](csv-grid-accessibility-implementation.md),
  [Mac scope](validation/mac-grid-accessibility.md)). Native pattern indices stay
  local and absolute CSV ordinals remain in labels/headers; no whole-file Table
  is advertised. Mac registration is opt-in pending external target acceptance.
  Its [stable semantic Table proxy candidate](validation/mac-grid-table-proxy.md)
  now replaces the native accessibility subtree after two-RID external row
  bridge failures; NSTableView rendering/input and source accessibility remain
  unchanged. Earlier native probe passes do not certify the changed candidate.
  An initial real Windows AOT/MTA probe established readable Table/header/value
  facts but exposed wrong-thread selection refusal. The corrected bounded
  selection dispatcher, three-view tree, read-only range facts, synthetic
  own-thread F6, distant Go-to and stale-cell matrix subsequently passed scoped
  win-x64 AOT checks ([exact scope, source delta and focus gates](validation/windows-grid-accessibility.md)).
  Shared model tests and portable Mac geometry/fixture tests are not external
  reader evidence. Source-command
  Invoke/AX press/Copy/Replace remain omitted until controller acknowledgment
  and actual terminal outcomes are implemented and accepted; keyboard/menu
  commands retain their existing behavior.

- Logical row/column scroll controls are now implemented at the checkpoint
  above; retained native rows remain bounded. In-process Mac AppKit synthetic
  scroller acceptance is established, but external UIA/AX values/focus, real desktop
  dragging/trackpad behavior and visible overlay hit-area remain unverified.
  Implementation is not a full accessible logical-navigation release pass.
- Cross-window/offscreen rectangular Copy is refused until its exact selected
  data is available; no padded or clipped clipboard success is invented. Mac
  nonoverlapping window navigation starts at its first ready row rather than
  claiming an actionable unseen selection. Windows has a row gutter; a matching
  dedicated Mac whole-row gutter remains absent (exact row Copy is explicit).
- Windows has no dedicated selected-cell multiline detail panel. Mac has a
  bounded detail area. Giant values reveal source; neither adapter decodes a
  giant field on paint.
- Actual native clipboard publication/readback and NUL/size/Missing refusal now
  have the scoped dedicated Windows and both-Mac-RID hosted evidence above. The
  original safe in-memory Mac Grid probe still never publishes. Desktop menu/
  shortcut routing, injected OS publication failure and contention recovery remain
  unverified; no whole-clipboard rollback or old-content preservation is promised.
- Experimental UIA GridItem/Selection/Table and AX selected-cell implementations
  still need separate external target evidence, as do Narrator/VoiceOver,
  physical input and real IME editing. Cell labels, pattern pointers, managed
  compilation and native control creation do not establish these gates.
- Four-RID strict single-binary AOT inventory/runtime evidence is established
  at the cited checkpoints; that is not four-RID desktop Grid acceptance.
  Startup/source-ready, warm scrolling, Apply-to-visible, callback/install p95
  and peak-memory measurements remain release work. No native latency
  percentile is claimed.
- A failure-directed check reproduced a pre-existing **P0 Windows source-host
  data-loss bug**: RichEdit imported only the prefix before NUL and the next
  ordinary edit deleted the unseen canonical suffix. Length-aware streaming
  import also failed to preserve the suffix. The separately reviewed fix uses a
  truthful read-only bounded NUL interval, visible U+2400 markers and exact native
  readback certification; it never guesses an editable marker inverse. Accepted
  independent evidence is 22 source-host contracts plus six source Copy/Cut
  prepublication cases, including exact UTF-8 Save/reopen, adversarial real-NUL/
  literal-marker transitions and failed-import guards. Root separately validated
  the extracted HEAD worktree with 22/22 source-host, 6/6 clipboard and 104/104
  broader controller tests, then committed the independent fix as `c2a7613`.
  Its separate [six-job CI run 36745185336](https://github.com/kleedaisuki/mote/actions/runs/36745185336)
  completed successfully; this verifies cross-platform tests/AOT inventory,
  not editable NUL content or a physical keyboard workflow.
  NUL interval editing remains
  explicitly unavailable on these RichEdit-backed hosts; no general binary-text
  editing capability is claimed.

Related bounded implementation/review evidence is maintained separately:

- [driver](reviews/native-csv-grid-driver.md)
- [commands](reviews/native-csv-grid-commands.md)
- [policy boundary](reviews/native-csv-grid-policy-boundary.md)
- [controller](reviews/native-csv-grid-controller.md)
- [Windows adapter](reviews/native-csv-grid-windows.md)
- [macOS adapter](reviews/native-csv-grid-mac.md)
- [independent code review](reviews/native-csv-grid-review.md)
- [target probe safety](reviews/native-csv-grid-probe-safety.md)
- [Windows source-NUL evidence](reviews/native-source-nul-review.md)
- [Windows import experiment / safety decision](reviews/native-source-nul-native-import.md)
- [source safety independent review](reviews/native-source-nul-fix-review.md)
- [AppKit source-NUL audit scope](reviews/native-mac-source-nul.md)
- [AppKit source-NUL probe safety](reviews/native-mac-source-nul-safety.md)
