# Windows UIA mutable range review

## Scope

Independent review of the working changes to:

- `src/Mote.Native/Windows/Accessibility/WindowsUiaBridgePrototype.cs`
- `src/Mote.Native/Windows/Accessibility/WindowsTextRangeNavigation.cs`
- `src/Mote.Native/Windows/Accessibility/WindowsTextProviderCore.cs`
- canonical selection callback/controller integration at commit `19671e2`
- direct COM-vtable contract tests in `WindowsUiaRangeContractTests.cs`

The reviewer did not modify production code or the shared contract test file.
Review was performed on Windows with .NET SDK 10.0.400, 2026-10-01.

## Finding corrected during review

**P2: Preserve already exact quantities in ExpandToEnclosingUnit.**

The initial `WindowsTextRangeNavigation.Expand` ignored the old End endpoint and
always selected a single unit at Start. For `ab\ncd\nef`, an already line-aligned
range `[0,6)` became `[0,3)`. An exact character range `[0,2)` became `[0,1)`.
These observations were independently reproduced against the built implementation
using a repository-local reflection probe, not inferred from the test assertions.

Microsoft's native method contract explicitly preserves ranges already consisting
of an exact quantity of requested units. The production .NET Windows edit-box
provider likewise leaves nondegenerate character ranges unchanged. This affects
clients normalizing a selected interval before reading or selecting it: a successful
call could silently discard part of the interval.

The owner corrected Expand to preserve nonempty ranges whose two endpoints are
unit boundaries. Move independently normalizes a degenerate range at Start, so
preserving multi-unit expansion does not weaken Move's separate one-unit contract.
The independent probe then reported `[0,6)` and `[0,2)` respectively. This finding
is **resolved in the reviewed working tree**; retain ABI regressions for exact
multi-character, multi-line and grapheme-aligned intervals.

## Verified properties

- Current mutable endpoints are guarded by a range-local lock. Clone copies the
  interval into an independent range object. Endpoint crossing collapses the other
  endpoint. Output values are initialized before failure.
- Peer resolution queries canonical IUnknown, resolves an owned generated CCW,
  checks the same provider core, and balances the temporary COM reference in a
  finally block. Reciprocal peer operations capture the peer before taking their
  own mutation lock, avoiding an opposite-order two-range lock acquisition.
- Range navigation validates source generation/version, computes locally, and
  commits endpoints only after checked current-source validation. Fixed-work-budget
  failures do not publish partially moved endpoints.
- Unsupported Format/Word units promote to Line; Paragraph/Page promote to Document.
  Logical lines include original delimiters; CRLF is one grapheme cluster.
- Character segmentation has a 65,536-source-code-unit budget and 65,536 steps,
  and, after owner hardening during review, 256 distinct logical lines per operation.
  The structural limit matters because tiny lines otherwise permit thousands of
  rope queries even with a small source-byte budget. Long logical lines are no longer
  rejected solely for exceeding 65,536 units: they use at most 4,096 units of
  certified local context. Dominant ASCII single-step movement uses a separate
  constant-size read of at most six units (and delegates Unicode neighbors to
  segmentation). These are explicit failing
  requests, not truncated successful movement.
- The selection controller rejects wrong-thread, stale/lifetime, unsafe surrogate
  or CRLF boundaries, and composition requests before navigation mutation. The
  accepted path uses canonical selection and synchronous existing frame publication,
  with no FocusSource call, source edit or Undo entry.

## Independent verification

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj --no-restore --filter 'FullyQualifiedName~WindowsUiaRangeContractTests|FullyQualifiedName~Accessible_selection' --logger 'console;verbosity=minimal'
```

Initial suite: **36 passed, 0 failed**, about 15 seconds. This ran before the
exact-quantity correction and structural budget tightening, and does not claim to
cover subsequent additional regressions owned by the contract-test agent.

The temporary review probe lives under `.temp/uia-range-review/` and is deliberately
not a production or tracked test artifact. An independent boundary-list oracle
checked **420 Character/Line endpoint movement cases, all passing**, after the
correction: empty/single-line/trailing-newline/trailing-empty-line sources, mixed
CRLF/LF, combining marks and astral text, every UTF-16 interior offset, zero and
signed movement, and `int.MinValue`/`int.MaxValue` clipping. It enumerates strict
preceding/following boundaries rather than reproducing MoveLine's index arithmetic.

## Remaining scope limits, not findings

No further substantive defect was established in this bounded review. Direct local
COM-vtable tests prove CCW resolution for in-process interface pointers; they do not
prove that external UIA-marshaled peer interfaces roundtrip identically. Real UIA
Select must also reach the controller's owning UI thread. The parent owns external
ordinary-editor checks and Native AOT verification. No claim is made of full screen
reader support, hit-testing/geometry, all pattern methods, or release acceptance.
EOF degenerate expansion was inspected but not reported as a defect: production
Windows edit-box character expansion likewise cannot move forward beyond EOF.

## Primary references

- [Microsoft ITextRangeProvider::ExpandToEnclosingUnit](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-itextrangeprovider-expandtoenclosingunit)
- [Microsoft ITextRangeProvider::Move](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-itextrangeprovider-move)
- [Production .NET WindowsEditBoxRange source](https://source.dot.net/UIAutomationClientSideProviders/MS/Internal/AutomationProxies/WindowsEditBoxRange.cs.html)
- [Canonical controller selection validation](../validation/windows-uia-selection-controller.md)

## Follow-up: bounded long-line navigation

Reviewed the subsequent 4,096-unit local-context path and ASCII single-step
fast path. Character segments are transaction-local and cached by line; crossing
an artificial certified segment edge reslices bounded context rather than retaining
a whole long-line character index. The segmentation text budget still limits
repeated slicing. Certification rejects surrogate seams and context-dependent
regional-indicator, Mark and Format predecessors before testing the adjacent pair
with the same runtime StringInfo segmentation used for the final interval.
The six-unit ASCII path only succeeds when all inspected neighbors are ASCII,
handles CRLF as one unit, and revalidates identity before returning.

Independent adversarial oracle after the fast-path change: **6,830 exact movements,
20,470 explicit bounded-request failures, zero mismatches**. Source strings had
5,000 ASCII prefix/suffix units surrounding combining sequences, emoji skin-tone
and ZWJ families, regional-indicator runs, prepend characters, Indic sequences,
Hangul Jamo, 3,000/4,500-unit combining runs, and a mixed Indic/combining/emoji
modifier sequence. Every offset around or inside these sequences was checked in
both directions against full-source StringInfo boundaries. Long uncertain clusters
were permitted to fail explicitly; success was never allowed to disagree with the
full-source oracle. The independent short-source 420-case boundary oracle also
passed after this change, including ASCII CRLF interior offsets and EOF clipping.
Probe source variants are retained locally under `.temp/uia-range-review/`.

No additional substantive defect was established. This is evidence of equivalence
to the runtime grapheme model for the exercised cases, not a proof covering all
Unicode versions or a complete formal derivation of the certification rule.
The parent's 50 MiB ASCII ABI regression and external Native AOT diagnostics are
separate evidence; they were not rerun by this reviewer. External Select failure
is not closed by these pure navigation checks and remains a distinct integration
investigation.
