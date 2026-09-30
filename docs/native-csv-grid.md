# Native CSV Grid implementation

Status: bounded native implementation and focused local evidence;
**not macOS runtime/accessibility or release acceptance**.
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
| macOS target probe | Not run locally; compiled on Windows, safety-reviewed in-process route awaits both Mac CI RIDs |

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
Root-owned CI wiring requires final source-hash safety approval before dispatch.

### Remaining release gates / deliberate truthful limits

- Native scrollbars describe the **bounded installed window**, not the complete
  file. Keyboard, wheel/scrollbar edge rebasing, Go to row/column and Follow source
  are implemented; an accessible full logical-file scrollbar is not delivered.
- Cross-window/offscreen rectangular Copy is refused until its exact selected
  data is available; no padded or clipped clipboard success is invented. Mac
  nonoverlapping window navigation starts at its first ready row rather than
  claiming an actionable unseen selection. Windows has a row gutter; a matching
  dedicated Mac whole-row gutter remains absent (exact row Copy is explicit).
- Windows has no dedicated selected-cell multiline detail panel. Mac has a
  bounded detail area. Giant values reveal source; neither adapter decodes a
  giant field on paint.
- Real native clipboard publication/readback and injected publication failure
  are not exercised by local tests or the safe Mac probe. NUL refusal is proved
  through policy/controller adapter-call admission, not whole-clipboard rollback.
- Native UIA GridItem/Selection/Table patterns, external AX selected-cell
  semantics, Narrator/VoiceOver, physical input and real IME editing need separate
  target evidence. Cell labels/native control creation do not establish them.
- Four-RID AOT runtime evidence and startup/source-ready, warm scrolling,
  Apply-to-visible, callback/install p95 and peak-memory measurements remain
  root-owned release work. No native latency percentile is claimed.
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
  Its four-RID CI was dispatched separately; completion is not inferred here.
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
