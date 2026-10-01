# macOS product native source candidate

## Implementation boundary

`MacEditorShell(nativeSource: true)` is an explicit window-lifetime product
profile, not the isolated diagnostic probe and not a size-dependent fallback.
Its existing full-body NSTextView is the sole source input, layout, caret and
native accessibility element. Product menu/status and Flow/Block/Grid panes
retain the established shell implementation. Canvas and this profile cannot be
enabled together; bounded `SetDocument` is rejected for this profile.

The controller owns canonical text, history, I/O and semantic facts. The shell
owns a whole native replica with original snapshot stamp and installation nonce.
New/Open imports are certified by complete NSString readback before editing is
enabled. Engine changes use `replaceCharactersInRange:withString:` against the
exact Before projection, then certify the complete After projection. Native
admission acknowledgment changes only identity/baseline, never reimports text.
Embedded NUL and failed exact publication disable input without changing the
canonical document. Unadmitted native text vetoes command settlement rather than
letting Save/New/Open silently discard it. The command barrier presents distinct
explicit discard-uncommitted-input consent with Cancel as the default button.
Cancel keeps the read-only native replica; confirmation calls controller recovery
and succeeds only after a new certified canonical installation. Modal reentry
and changed original installation identity veto the stale recovery request. A failure after the engine has already
committed remains canonical-retained: a later explicit Save can preserve that
canonical snapshot. There is no silent truncation or live profile substitution.

Marked input freezes baseline and attributes. Final NSString and actual ordered
selection are emitted through SourceCandidate only after native marked text ends.
No legacy bounded TextChanged/SelectionChanged event accompanies it. Selection
range alone does not certify direction: active endpoint is known only for a
collapsed range or an explicitly installed command witness; uncontrolled native
selection notifications revoke that witness. Native undo is disabled; actual
NSTextView `undo:` / `redo:` responder selectors route to engine commands using
the existing composition settlement barrier. Source context-menu validation uses
canonical CanUndo/CanRedo supplied by the controller, with actual marked and
unadmitted text barriers; other menu selectors preserve superclass validation.

## Viewport and decoration

TextKit 2 is checked before any `layoutManager` accessor because Apple documents
that the latter can switch NSTextView to legacy compatibility mode. For an
actual TextKit 1 surface, the existing visible rectangle minus container inset
is converted to an already-laid-out glyph range, then a character range. This
does not expand to a complete logical line or force additional layout.
TextKit 2 obtains its actual native viewportRange via its content manager. An
absent range or a paragraph-sized witness exceeding 65,536 characters is
explicitly unknown: empty interest and a pending-decoration notice, while the
independently certified exact source remains editable. Fine-grained TextKit 2
visible segment geometry is still an acceptance gap, not a promoted capability.

Canonical semantic tokens remain absolute and global. Foreground publication
clips their ordered overlays to qualified native visible display characters,
uses neutral foreground for pending/uncovered positions, coalesces adjacent
identical colors, and makes at most 256 native run mutations per UI turn.
Queued batches carry original stamp/nonce/viewport/accepted-semantic-revision/theme
identity and are superseded without claiming success. Every accepted publication
advances the semantic revision, including a same-presentation-sequence pending
clear; stale ready colors therefore cannot survive a neutral invalidation. Last successful installed
identity is distinct from a pending batch. Unchanged source draw observations do
not issue another attribute batch; this prevents a draw/attribute/redraw loop.
Attributes preserve selection and clip-view origin. Draw traces advance only
with successful exact text identity; draw return is not physical presentation.
Each actual NSString copy and each actual style batch has the corresponding
fixed NativeSourceReadback/NativeSourceStylePublish telemetry interval with a
numeric text version, explicit success/failure, and no fabricated encoded byte
count or content/path attributes.

## Evidence status

Portable tests in `MacProductSourceTests.cs` cover invalid import identities,
NUL/scalar selection boundaries, complete predecessor/successor range equations,
ordered selection without invented direction, invalid native ranges and bounded
ordered foreground overlays. These tests make no AppKit calls. The parent
coordinates compilation and focused execution; actual test counts/results must
be attached after execution, not inferred from the source.

No local GUI, input-source, clipboard, global input or platform settings changes
were performed for this implementation. Actual four-RID AOT, AppKit viewport,
real IME/responder/context menu, native range publication, selection/scroll and
save/new-process-reopen evidence remains required before candidate promotion.

## Primary platform references

- [Apple NSTextView documentation](https://developer.apple.com/documentation/appkit/nstextview)
  describes native selection/input and the legacy-manager compatibility switch.
- [Apple NSLayoutManager documentation](https://developer.apple.com/documentation/appkit/nslayoutmanager)
  defines visible glyph layout and glyph-to-character conversion.
- [Apple NSTextViewportLayoutController documentation](https://developer.apple.com/documentation/appkit/nstextviewportlayoutcontroller)
  defines the TextKit 2 viewport range, not a guarantee that every character in a
  very large paragraph is physically visible.
- The alternative representations and production/research context are retained
  in `docs/architecture/ordinary-editing-locus.md`, especially section 8.

## Coordinated portable checkpoint (before salvage-Copy follow-up)

The parent ran the coordinated Release build recorded in
`.temp/native-source-product-build-5.log`: **0 warnings, 0 errors, 4.23 s**.
I independently parsed
`.temp/native-source-product-test-2/native-source-product-2.trx`:
**10 MacProductSourceTests results, all Passed, no failed/skipped Mac results**.
The parent reports 78 passing cases for the selected aggregate run; the Mac
count above comes from its individual TRX result names, not inference from that
aggregate. No AppKit/native GUI operations are covered by these portable tests.

Hashes observed immediately before the subsequent salvage-Copy implementation:

| Artifact | SHA-256 |
| --- | --- |
| `MacEditorShell.cs` | `A1EACC94C426E6DFAC65F7EC64F195B5B30C8306B13DDBFE416944324BF506D7` |
| `MacEditorShell.Source.cs` | `DFF22FD175159748DA0F73947E37EB2DAA71E9D678AF05542EF94345C0F3EE50` |
| `MacProductSourceModel.cs` | `F9C6C9E4A56AEAC68BBA040A18D512BB2BDB9853238F7C148792CB0851FB1E90` |
| `MacProductSourceTests.cs` | `504E8C946C5A191A57634EE5F80204D5451A1E4914D78D9665CB4B1D47852932` |
| Release `mote.dll` | `06DA3AC98CECA67F1D3AEAE6E157884121C27DA821C8D33CFB4A5CBAC6CD4AE4` |
| Release `Mote.Tests.dll` | `643DCBA28026E47A3E6B1B44C98C5D68DE39DAAD6D7D32B16E85B8DBFEF121FB` |

### Read-only unadmitted-input salvage Copy

Review found that the established main-menu/delegate Copy route went through
command settlement and therefore showed recovery consent instead of allowing
selected failed native input to be salvaged. The product-only follow-up invokes
actual NSTextView superclass `copy:` when and only when the explicit native
source is unavailable, contains unadmitted text, and is the actual window first
responder. Copy bypasses engine selection and settlement, but does not mutate
text/history or admit Save/Cut. Preview/table/legacy/editable/canonical-retained
routes retain their established behavior. A failed native Copy cannot fall
through to copying an unrelated canonical selection.

A new portable test enumerates all 16 boolean admission combinations. This
raises the Mac class to 11 cases, but **the earlier 10/10 run does not qualify
this follow-up**. The parent is coordinating the one affected rebuild/test.
Actual AppKit pasteboard/responder behavior remains unqualified until hosted
runtime execution; no local clipboard or native GUI was exercised here.

### Qualified salvage-Copy portable follow-up

The parent performed the affected coordinated Release rebuild recorded in
`.temp/native-source-product-build-6.log`: **0 warnings, 0 errors, 5.89 s**.
I independently inspected
`.temp/native-source-product-regression/native-source-product-regression.trx`:
`MacProductSourceTests.Unadmitted_copy_requires_all_source_only_guards` is
**1/1 Passed** (7.1802 ms recorded test duration), exercising the 16 internal
boolean combinations. The TRX contains exactly **258 Passed** results and
counters total/executed/passed=258, failed/error/timeout/aborted/notExecuted=0;
the associated run log reports 38 s and no skipped cases. Only this additional
Mac case appears in that affected run. The earlier 10 Mac cases were not rerun;
their independent 10/10 run-2 checkpoint remains the retained evidence. Do not
combine the separate checkpoints into an invented single 11/11 execution.

Build-7 subsequently reports **0 warnings, 0 errors, 4.52 s** for the separate
Windows-focus-only delta. Mac source was unchanged for that delta; this is not a
second Mac native/runtime acceptance result.

Current exact Mac source hashes after the salvage-Copy follow-up:

| Artifact | SHA-256 |
| --- | --- |
| `MacEditorShell.cs` | `B6A79504E5DF5E8C3662575A46FB7DFF29DDE59CEC0D7155E376860E5139C851` |
| `MacEditorShell.Source.cs` | `75E73B4BFE4B3FB1F3EF2B080E2BBCA023CF9930B37B38B37591138B4F9010AC` |
| `MacProductSourceModel.cs` | `BD91E487791DC6D98BC839DF523F5A895842DD81478085FCB6A9C115C4CB2410` |
| `MacProductSourceTests.cs` | `BF5DA639E07D4354C3C3A3E25C236EDD35C6867969E73B403445DBF6275529B2` |

This qualifies the portable Copy admission contract and coordinated compilation,
not actual AppKit superclass Copy, native pasteboard content, IME, accessibility,
viewport geometry, presentation latency or product promotion. No native GUI or
performance experiment was run for this documentation checkpoint.
