# Native CSV Grid controller workflow validation

Date: 2026-09-30. Base HEAD at final run: `6f990ed`, with the concurrently
integrated, uncommitted CSV Grid controller/driver/commands and native adapters.
Status: **15 initial, 4 composition/Continuous and 3 source-follow controller cases pass; platform Grid UI acceptance is
not established by these tests**.

## Boundary, contract, and method

The acceptance basis is the reviewed [CSV Grid architecture](../csv-grid-architecture.md):
Engine owns one canonical source/undo history; native tables contain only bounded
immutable projections; clipboard and replacement commands use proved source
origins and exact presentation identity, not sanitized display values. Missing,
pending, invalid, stale, hidden, or over-budget selections cannot silently acquire
command authority. CSV serialization must preserve actual empty final records.

`tests/Mote.Tests/NativeCsvGridControllerTests.cs` is a separate test file, not an
extension to the controller owner's tests. A minimal `INativeEditorShell` fake
records actual `SetDocument`, `SetAnalysis`, clipboard, prompt, and error boundary
calls. It opens real `.csv` files through `NativeEditorController.Run` and the
real serialized format-session driver. No synthetic Grid, cached parser result,
or source-span authority is injected. Source line-ending mode is Preserve. All
fixtures and isolated configuration roots use the repository's `.temp/tests`
through `RepoTemp`; no user settings or system clipboard are touched.

Positive asynchronous workflows await independently observed installed state or
publication. Copy refusal tests await actual `ShowError` completions, not merely
an elapsed delay. A stale queued-copy test waits for a posted callback without
delivering it, changes source, then drains completions. Modal replacement
reentrancy synchronously changes source from inside `PromptGridReplacement`.

## Commands and environment

Windows development host; PowerShell; .NET SDK `10.0.400`, Release `net10.0`.

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release `
  --filter FullyQualifiedName~NativeCsvGridControllerTests --no-restore `
  --logger "trx;LogFileName=csv-grid-controller.trx" `
  --results-directory .temp/csv-grid-controller --verbosity minimal
```

Final observed result: **15 passed, 0 failed, 0 skipped**, approximately 6 seconds
test duration. Local TRX: `.temp/csv-grid-controller/csv-grid-controller.trx`
(scratch evidence, not a tracked release artifact).

## Tested claims

| Workflow | Independent expected outcome | Observed |
|---|---|---|
| CSV startup with CRLF inside a quoted field | Two logical records, first row remains data; original source unchanged | Pass |
| Bounded rebase to row 50, columns 10–13 | Same document stamp but new presentation sequence; old identity refuses Copy; new identity copies `r50c10` | Pass |
| Source edit then old Copy/Replace request | No clipboard publication or value prompt; only user source edit remains | Pass |
| Quoted field containing NUL | Refusal before native clipboard call, sentinel untouched, source unmodified | Pass |
| CSV Copy of `a\r\n\r\n` and `\r\n\r\n` | Explicit empty tokens (`a\r\n""`, `""\r\n""`) round-trip as two records | 2 pass |
| Quoted-cell replacement with comma, quote, and CRLF | Exact CSV escaping, neighboring records and delimiters conserved on disk, small source stays visible, one Undo returns original | Pass |
| Cancel and source edit inside replacement prompt | No replacement transaction; Undo only reverses the user edit when present | 2 pass |
| Unterminated quote and ragged Missing cell | Both exact-value Copy and Replace refuse; no prompt/publication | 2 pass |
| 9 Mi UTF-16 unquoted field | Delivered Oversized state, Copy exceeds the 8 Mi payload budget, Replace refuses non-Complete field | Pass |
| Explicit source-only layout | Hidden Grid has no Copy/Replace authority | Pass |
| Queued Copy followed by source edit | Old snapshot completion does not publish | Pass |
| Copy of CRLF/TAB value and whole source row | Exact decoded control characters, then exact record syntax including final CRLF; never display substitutions | Pass |

Oversized **display** is not universally a Copy refusal: exact decoded fields
within the separate 8 Mi UTF-16 payload budget may be copied by design. The 9 Mi
case tests the actual resource limit, not an invented ban on all Oversized cells.

## Defect detected and fixed by the implementation owner

The first runnable 13-case suite passed 12 and failed the structured replacement
case. For source `left,"old",right\r\nnext,row,tail\r\n` and replacement
`new,"value"\r\nline`, `NativeDocumentView.Text` ended immediately after the first
record instead of displaying the complete small file. Inspection localized the
failure to retained `_pageLength`: Engine replacement enlarged source, but native
projection kept the old page capacity. This was a **view truncation defect**;
the observation alone did not prove Engine data loss.

The owner reset `_pageLength` before projecting the structured replacement. The
final test additionally saves and reads the complete expected disk bytes before
checking native text and performing one Undo. This separates source conservation
from view completeness. All 15 cases then passed. The validator modified no
production code, and no assertions were weakened to accept the truncated view.

An earlier attempt could not build because of an unassigned `error` local in the
concurrently written command serializer (`CS0165`). The owner corrected that
production compilation error before workflow execution; it was not a test result.

## Coverage limits and supported verdict

This verifies real controller/session orchestration through a deterministic
platform boundary, including source persistence and undo. It does **not** verify
Win32 ListView/NSTableView events, scrolling/selection visuals, native dialogs,
system clipboard encoding, accessibility, IME interaction, macOS behavior, four-RID
Native AOT, or strict single-binary packaging. It is not a startup, input latency,
RSS, or large-file cold-analysis benchmark. Continuous native painting, source
scrolling and input-host callbacks remain target adapter checks; the additional
case below exercises only the actual Continuous controller branch through its
existing platform boundary.

**Verdict:** the tested controller contracts hold after the detected small-file
projection regression was corrected. These results support integration of the
controller layer, not a claim that native CSV Grid productization is complete.

## Focused follow-up: composition, queued identity and Continuous branch

At the integration owner's request, four additional cases were run separately;
the completed initial 15 were not repeated. Final follow-up base HEAD:
`b767026`, plus concurrent uncommitted Grid integration.

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release `
  --filter "FullyQualifiedName~NativeCsvGridControllerTests.Additional_" --no-restore `
  --logger "trx;LogFileName=csv-grid-controller-additional.trx" `
  --results-directory .temp/csv-grid-controller --verbosity minimal
```

Observed: **4 passed, 0 failed, 0 skipped**, 753 ms test duration. Local TRX:
`.temp/csv-grid-controller/csv-grid-controller-additional.trx`.

1. **Composition commits before Replace admission:** fake `CommitPendingText`
   emits a synchronous final source `TextChanged` replacing `old` with `中文`.
   Exactly one preflight call occurs; the original Grid stamp then loses
   authority, the replacement prompt is never entered, and only the composition
   edit exists. One Undo returns the original source.
2. **Composition cannot settle:** false preflight preserves pending text and
   unchanged source, never enters the replacement prompt, and leaves the
   document unmodified.
3. **Queued Copy plus same-version viewport reinstall:** exact-value Copy is
   prepared and posted but not delivered. A rebase installs row 1, column 1
   under the same document stamp and a different presentation sequence. The old
   completion cannot publish; a fresh request copies `d`. Source stays unchanged.
4. **Continuous controller branch:** a small adapter implements only the
   existing `INativeCanvasShell` boundary. `EditorPresentationProfile.Continuous`
   binds the actual immutable Engine snapshot, not an invented source mirror.
   Grid Select leaves snapshot version/text/modified flag unchanged. Replace
   preserves quoting, CRLF, and the neighboring record; Save persists exact bytes;
   one Undo restores original source. No `SetDocument` hidden page editor is
   ever installed. The adapter returns no fabricated physical caret geometry.

These checks model composition **settlement ordering**, not a real IME. The
Continuous case establishes that Grid commands reach the intended canonical
controller path; it does not prove Win32/AppKit input, geometry, paint, scrolling,
or accessibility. No generic simulation framework or production test hooks were
introduced. The completed initial coverage still stands under its recorded
assumptions; the follow-up is not a blanket revalidation of concurrent changes.

## Reviewer-directed source-follow admission regression

Date: 2026-10-01. Base HEAD `a17ad93` plus uncommitted Grid integration and the
owner's source-follow admission correction. Only the three new `FollowSource_`
cases were run; the previously completed 19 cases were not repeated.

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release `
  --filter "FullyQualifiedName~NativeCsvGridControllerTests.FollowSource_" --no-restore `
  --logger "trx;LogFileName=csv-grid-controller-follow-source.trx" `
  --results-directory .temp/csv-grid-controller --verbosity minimal
```

Observed: **3 passed, 0 failed, 0 skipped**, 661 ms. TRX:
`.temp/csv-grid-controller/csv-grid-controller-follow-source.trx`.

The reviewer found that `NativeGridWindowRequest.FollowSource` ignored Row when
building the policy anchor but still used it for admission. A detached blank
window whose old far ordinal exceeds a subsequently certified exact row count
therefore could not return to the source caret. The owner now applies negative
and exact-extent Row admission only to actual ordinal requests.

Two theory cases open actual CSV `a\nb\nc\n` and independently assert the policy's
exact three-record extent. A typed `GridRenderProjection` constructor creates
valid immutable blank delivery for requested far row 1000, retaining all actual
policy version, source length, extent, coverage, and diagnostic facts. A small
managed-test reflection helper installs only `_presentedPreview` delivery state
and mirrors that same identity to the fake shell; it does not forge source,
record contents, or parser cache facts. The fake requests FollowSource with
Row=1000 and Row=-1. Both recover actual policy-delivered row 0 (`a`) under the
same document stamp and a new presentation sequence. No source edit occurs.

The old admission predicates would reject these two values respectively through
`Row >= ExactRowCount` and `Row < 0`; the validator did not restore/execute the old
production implementation. The correction is tested through the real controller
and format-session lane. Reflection is intentionally restricted to reproducing
the otherwise transient valid blank detached state, not to bypass admission.

The companion case opens an actual empty CSV, proves ExactRowCount=0, requests
actual ordinal row zero, and verifies synchronously that no new presentation is
installed and no row is invented. It checks the other side of the corrected
branch: ignoring Row applies only to source-follow semantics.
