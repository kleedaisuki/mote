# Windows source TextRange movement and selection contract

Status: implemented bounded range model with an open external Select integration
gate, 2026-10-01. This document does not claim a complete TextPattern or
Narrator/NVDA acceptance result. It supplements
[the source-backed provider model](../accessibility-provider-design.md) and
[the ordinary product tree scope](../windows-uia-tree-design.md). The latter's
historical two-Document experiment is not the ordinary product tree.

## Existing authority and the smallest coherent extension

`AccessibleDocument` publishes one immutable
`AccessibleCanvasState(Generation, TextSnapshot, CanvasFrame)`. Coordinates are
half-open source UTF-16 offsets; line endings and source markup are not replaced
by the RichEdit projection. Every range carries generation and snapshot version.
An edit or New/Open makes the old range unavailable. Provider detach invalidates
only that provider, not the controller's document. Closed providers must not keep
a large snapshot alive indefinitely.

`UiaRangeObject` adds an endpoint-only mutable range above this model, not a text mirror.
Its state is `(provider identity, G, V, start, end)`, with
`0 <= start <= end <= source length`. Movement changes these endpoints and **not**
the source or editor selection. `Select` is the separate controller transaction.
A clone copies the interval into a distinct object: moving the clone cannot move
the original, though both share the same source authority and stale checks.

`WindowsTextRangeNavigation` captures the original range identity and checked
document interval for one operation. The current implementation does not capture
one persistent snapshot object: it checks G/V on subsequently read source ranges
and revalidates the calculated result before the mutable endpoint commit. It
therefore cannot silently mix ranges from different source versions. Future code
must preserve those checks rather than assuming independently read line mappings
belong to one publication. A failed call leaves endpoints, selection, text, dirty
state and undo history unchanged. Native output arguments are initialized before
validation and exceptions are mapped at the COM boundary.

The implementation is in `WindowsTextRangeNavigation.cs`,
`WindowsUiaBridgePrototype.cs` and `WindowsTextProviderCore.cs` under
`src/Mote.Native/Windows/Accessibility/`. Its independent review is recorded in
[windows-uia-range-review.md](../reviews/windows-uia-range-review.md).

## Unit policy

Microsoft's [text-unit ordering and fallback rule](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationtextunits)
permits substitution only toward a **larger** supported unit. With Character,
Line and Document implemented, use one total mapping:

| Requested unit | Effective unit | Meaning |
| --- | --- | --- |
| Character | Character | Linguistic character boundaries, in source UTF-16 coordinates. |
| Format | Line | No independent format-run navigation is claimed. |
| Word | Line | No linguistic word segmentation is claimed. |
| Line | Line | One presented, unwrapped source line including its original delimiter. |
| Paragraph | Document | No paragraph segmentation is claimed. |
| Page | Document | A viewport/input-island page is not a source document page. |
| Document | Document | Exactly `0..Length`. |
| Invalid enum | Error | `E_INVALIDARG`, not arbitrary fallback. |

Line boundaries can reuse `TextSnapshot`'s rope line summaries while the source
canvas is unwrapped. CR, LF and CRLF delimit one line and stay byte-for-byte
unchanged in `GetText`; never create a boundary between CR and LF. If soft wrapping
is introduced, UIA Line must follow presented line starts, not merely the engine's
logical lines. A clipped horizontal source window is not a shorter Line.
Microsoft defines Line in presentation terms in the linked text-unit guide.

**Character uses extended grapheme clusters, not UTF-16 code-unit steps.**
The implemented oracle is .NET `StringInfo.ParseCombiningCharacters`, including
one CRLF cluster, following the text-segmentation model described by
[Unicode UAX #29](https://www.unicode.org/reports/tr29/). Runtime Unicode data is
part of this behavior; no separate hand-written scalar approximation is claimed.
The delimiter-unit interpretation does not claim exact screen-reader or every
native reference-control behavior.

Short logical lines of at most 4,096 UTF-16 units are segmented whole. Longer
lines read at most a 4,096-unit local source window around the requested offset.
Clipped seams use the conservative reset rule already used by
`CanvasInputWindowSelector`: reject surrogate halves and seams whose preceding
scalar is a regional indicator, extending mark, combining mark or format/joiner
character; certify the remaining seam with a two-scalar `StringInfo` break.
Only the interval between certified seams is segmented. This avoids assuming
that an arbitrary window start erases earlier RI parity or extending context.

Each call has a total 65,536-UTF-16 source-materialization budget, a maximum of
256 logical-line/segment cache entries, and a bounded Character walk. Those
structural limits matter even for many tiny lines where a byte budget alone
permits excessive rope queries. A long combining/ZWJ sequence with no certified
reset, or a walk exceeding the limits, fails with `UIA_E_INVALIDOPERATION` before
endpoint mutation. It does not falsely report the budget limit as a document
edge or truncate movement success. No whole-file string, whole long-line string,
or persistent per-character index is retained. A future versioned sparse index
could remove these bounded-operation failures without weakening seam correctness.

## Movement as one boundary algebra

Let `B` be the ordered boundaries of the effective unit, including `0` and `N`.
Reuse the same enclosing/next/previous primitives for all three movement APIs;
keep one crossing rule rather than separate directional special cases.

### ExpandToEnclosingUnit

An already nonempty range whose endpoints are both effective-unit boundaries
represents an exact quantity of units and stays unchanged, even if it spans more
than one unit. Otherwise normalize around the **start** endpoint to one effective
unit; this is not an envelope of all intersected units. Preserving exact quantities
is explicit in Microsoft's
[ExpandToEnclosingUnit remarks](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-itextrangeprovider-expandtoenclosingunit).

At an internal boundary a degenerate range belongs to the following unit. At EOF
a Character/Line caret deliberately stays `[N,N)`, rather than selecting the
preceding unit; Document expands to `[0,N)`. Empty source stays `[0,0)` for all
units. This terminal Character behavior is also consistent with the production
[Windows edit-box proxy](https://source.dot.net/UIAutomationClientSideProviders/MS/Internal/AutomationProxies/WindowsEditBoxRange.cs.html)
inspected in the independent review. Tail empty lines share the EOF offset and
must not create duplicate movement boundaries. This corrects the earlier design
sketch that always contracted multiunit ranges and proposed preceding-unit EOF
expansion; that sketch was not the final platform contract.

### MoveEndpointByUnit

For positive counts visit boundaries strictly greater than the original endpoint;
for negative counts visit strictly smaller boundaries. From inside a unit, the
first reached boundary counts as one. Clamp at the source edges and report the
signed number of boundaries actually crossed. A zero count has no effect.
If the moved start exceeds the end, also set end to the new start; if the moved
end precedes the start, also set start to the new end. This is the documented
[endpoint-crossing contract](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-itextrangeprovider-moveendpointbyunit).
Use wide signed arithmetic for `int.MinValue`; do not negate it in `int`.

### Move

For a degenerate range, move the insertion point by the effective units and retain
degeneracy. For a nonempty range and nonzero count: normalize the start to its unit
start, move by unit starts, then span exactly one destination unit. Do not preserve
the original range width or translate both endpoints independently. The signed
return count measures unit motion, not normalization. At the end, a nonempty
range must still fit a complete destination unit; it must not become an EOF
caret merely because the endpoint API can reach `N`. `Move(Document, count)` on
an already nonempty document range cannot move to another document unit; result
is zero. Zero-count Move must not normalize or otherwise change a range. These
requirements follow Microsoft's [Move algorithm](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-itextrangeprovider-move).

### MoveEndpointByRange and comparisons

Validate both range objects against the same live provider/source identity before
copying an endpoint. Copy its exact source offset, then apply the same crossing
rule. Do not reinterpret a stale range against the current snapshot.
`Compare` compares ordered endpoints, not object identity. `CompareEndpoints`
returns the sign of the source ordering. The implementation returns the exact
offset difference, safe because both validated offsets are nonnegative `int`
values; clients may rely on the sign, not a required distance interpretation.
See [the platform comparison contract](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-itextrangeprovider-compareendpoints).

## Native peer identity and concurrency

The peer parameter is a COM interface pointer, not a managed object address.
The implementation resolves its canonical `IUnknown` and uses documented
[`ComWrappers.TryGetObject`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.comwrappers)
to recover a local generated wrapper, requiring the expected range type and
the same `WindowsTextProviderCore` identity. Acquired interfaces are released in
`finally`. There is no raw pointer cast, foreign vtable guessing, global strong pointer
registry, or undocumented runtime unwrap. If a remarshal returns a proxy rather
than a recognized local wrapper, fail safely and retain this as an external
acceptance gap; do not silently compare unrelated coordinates. Test real client
round-trips on both AOT Windows architectures. The current external Windows x64
Native AOT experiment successfully returned live own peers across the UIA COM
boundary: endpoint comparisons produced exact distances 1 and 3, and `GetText`
returned `bc`. This is scoped evidence for those calls, not proof of all proxy
paths or win-arm64 acceptance; see the validation record below.

A range can be called concurrently. Protect its mutable interval or replace it
atomically as one immutable value. Snapshot a peer independently; never hold one
range lock while acquiring another, calling COM, invoking the controller or
raising UIA events. The pure calculation can run over an immutable checked source
view, but it cannot claim that a later UI mutation used that same version without
the controller's second validation.

## Select: source transaction, not input simulation

`SupportedTextSelection` remains Single. `Select` replaces the global selection;
a degenerate range sets the caret, as specified by Microsoft's
[Select contract](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-itextrangeprovider-select).
It does not modify text, engine version, undo history or dirty state. It can
reveal/rebind the bounded native input island through existing controller logic,
but must not force commit/cancel of active composition. Preserve the existing
`IAccessibleViewport` ownership model: an off-owner-thread callback fails
WrongThread, and an active composition fails CompositionBlocked without changes.
Never simulate global keyboard/mouse input or post-and-synchronously-wait on the
UI thread to manufacture a pass.

The separate [`UseComThreading` provider option](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.provider.provideroptions)
can constrain a genuine STA provider to its apartment thread; it is not a magic
replacement for proving apartment initialization, provider creation and teardown.
Do not turn it on without an external client test of those lifetimes. Successful
selection means the matching global selection and source frame were published
before return. Raise one source-root selection event after publication, outside
locks. No UIA focus/foreground theft is implied by Select.

`AddToSelection` and `RemoveFromSelection` remain `E_NOTIMPL`; no completed
multi-selection or full TextPattern is claimed. If these are implemented later,
reject disjoint Single-selection additions with `UIA_E_INVALIDOPERATION` and
define equivalent/overlapping Add and Remove behavior rather than using
unconditional success.

### External Select remains a genuine integration gate

[The independent external validation](../validation/windows-uia-range-external.md)
launched the ordinary product's freshly published win-x64 Native AOT executable.
Three attempts reached `Select` after successful peer endpoint comparisons 1/3
and text `bc`, but `Select` threw `0x80131509` (`UIA_E_INVALIDOPERATION`). The
external managed client was STA; that is not evidence of the target callback's
thread or apartment. The HRESULT is consistent with WrongThread or another
rejection, but the rejection branch was not observed. The tested workflow remains
**FAIL**; earlier STA ownership ideas did not establish a successful selection.
Normal target close had exit 0 with no forced cleanup and source bytes unchanged.
Downstream global selection, further clone-independence and retained stale-range
checks were not reached, and must not be inferred from the preceding successes.

Next, observe the actual provider callback thread and typed controller rejection
in that same target-owned harness. Fix the causal dispatch/apartment boundary
without adding unsafe post-and-wait or relaxing composition checks, then rerun
the complete external sequence on both Windows Native AOT architectures.

## Production and research signals

[Windows Terminal's production range implementation](https://github.com/microsoft/terminal/blob/main/src/types/UiaTextRangeBase.cpp)
uses specialized boundary helpers and source-buffer locking; this supports a
shared boundary algebra but not copying terminal cell semantics into an editor.
The relevant distinction is source coordinates versus terminal screen cells.
[Xi's metrics design](https://xi-editor.io/docs/rope_science_02.html) shows why
line counts and boundary indices should be measured structures rather than
repeated whole-string scans. Its auxiliary break-rope idea is a possible later
grapheme index, not evidence mote already has one.

The peer-reviewed [Finger Trees Explained Anew, and Slightly Simplified](https://www.cs.tufts.edu/comp/150FP/archive/koen-claessen/finger-trees.pdf)
(Haskell Symposium 2020, DOI `10.1145/3406088.3409026`) provides a measured-sequence
foundation. Current [formalized finger-tree proofs](https://isa-afp.org/entries/Finger-Trees.html)
also exemplify checking annotation invariants. Neither requires replacing mote's
existing rope. The actionable research direction is to store composable boundary
state/checkpoints and signed rank/select metrics, then verify edit-local updates
against a full segmentation oracle. A finite-state summary may compose state
transitions across chunks, but its cost and correctness for the pinned Unicode
rules must be demonstrated before introducing a new engine abstraction.

## Discriminating acceptance cases

| Case | Required observation |
| --- | --- |
| `ab\r\ncd\n` | Line `[0,4)`, `[4,7)`, final empty logical line represented deliberately; no CRLF-half endpoint created by movement. |
| `a` + supplementary emoji + `b` | Character movement never creates a surrogate-half endpoint; reported signed count matches linguistic units actually supported. |
| Combining, ZWJ, regional indicators, Indic | Compare with runtime StringInfo segmentation oracle; clipped long-line seams must be certified, pathological context must fail unchanged. |
| Exact multiple-unit expansion and EOF | Aligned nonempty multiunit ranges stay unchanged; Character/Line EOF carets stay degenerate; no duplicate trailing-empty-line movement. |
| Interior endpoints, count zero, `int.MinValue` | Zero unchanged; interior first boundary counted once; no integer overflow or unbounded loop. |
| Nonempty multiunit Move | Becomes one destination unit, clone/original remain independent. |
| Start/end crossing both directions | Collapses to moved endpoint, never inverted range. |
| Two cores with equal offsets; stale G/V; detached | Foreign identity rejected; stale/detached return unavailable; no source or selection changes. |
| UI thread, non-UI thread, active IME | Only a safe owner-thread settled transaction succeeds; blocked calls leave engine/island untouched. |
| 100 MiB file, long line, huge count | No source mirror or long-line materialization; Line/Document use indexed arithmetic; Character respects 4,096 local context, 65,536 total source units and 256 segment entries without false success. |
| External UIA source round-trip | Compare/MoveEndpointByRange recognizes live own ranges through COM; selection is globally observable and source events target the one Document. |

Run pure boundary tests first, generated COM ABI tests next, then an independent
external Windows UIA client against win-x64 and win-arm64 Native AOT executables.
Keep artifacts under repository `.cache/` or `.temp/`. A passing movement unit
test is not a reader, real-IME, bounding-rectangle or full-pattern release pass.
