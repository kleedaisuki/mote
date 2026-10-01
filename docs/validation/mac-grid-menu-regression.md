# macOS opt-in Grid Missing-cell menu regression

Date: 2026-10-01. Scope: preserving established native keyboard and frozen-menu
intent dispatch with experimental Grid accessibility enabled. This is not
source-action acceptance, external AX acceptance, or VoiceOver acceptance.

## Hosted negative evidence

At commit `f76c56e`, CI run `36780934192` failed the opt-in native CSV Grid
diagnostic on both `osx-x64` and `osx-arm64`. The nested selector diagnostic
printed its success marker, then the enclosing probe failed
`menu freezes old identity and active field`, exit 1. The non-opt-in diagnostic
passed. This result was reported by the integration owner from hosted logs;
the Windows investigation host cannot execute AppKit.

The selector success marker is not an enclosing diagnostic success, and neither
marker establishes external reader behavior.

## Mechanism and compatible correction

The enclosing fixture delivers row 0, column 2 as `Missing`, with no
`SourceRange`. Shift-right makes this the native active endpoint; installing
presentation sequence 2 preserves it. Normal semantic focus falls back to that
same active endpoint, so the defect does not require an explicit AX focus
override or a different focused coordinate.

Before the correction, `MacCsvGrid.CaptureIntent` used stricter admission in
its opt-in focused Reveal/Replace branch: a non-null source range and a state
other than Pending **and Missing**. Established native capture admits every
delivered non-Pending descriptor. Consequently, `menuWillOpen:` froze a null
`_menuCell`, not an incorrect coordinate. After sequence 3 installed,
`MenuCommand` emitted no Reveal callback, leaving the previous CopyTsv intent
as the last entry and causing the existing assertion to fail. The same frozen
null also suppressed active-cell CopyValue/CopySource/Replace menu dispatch.

The corrected pure `CaptureFocusedIntent` helper admits a delivered non-Pending
descriptor, including Missing, and captures the exact installed identity and
coordinate. It does not authorize any source operation. Existing controller
checks remain unchanged: stale identity is rejected; Reveal requires a proved
source range; Replace requires a complete, syntax-valid field. Missing therefore
still cannot reveal or replace source. No AX press, Reveal, Copy, Replace, or
editable-value selector was added.

## Discriminating regression

The enclosing native probe now performs these checks **before** calling the
nested AX selector probe:

1. Under opt-in, semantic focus is row 0, column 2 and its state is Missing.
2. Return and Command-Return emit exactly two coordinate intents at sequence 2,
   row 0, column 2 (Reveal and Replace), not source changes.
3. Menu opening freezes a non-null Reveal intent at that same sequence and
   active cell. A read-only internal diagnostic getter exposes this value.
4. After sequence 3 installs, invoking the frozen menu emits exactly one new
   callback carrying sequence 2, column 2, not a rebound current identity.
5. Existing frozen rectangle and Follow-source checks still run. Only then does
   the separate AX probe run; the original native table is explicitly restored
   as first responder before later Edit/Copy/navigation checks.

This ordering removes nested-probe responder side effects as an explanation
for the Missing/menu regression. The selector probe still independently checks
that AX reads/selection dispatch no source commands and that retired/pending
nodes cannot dispatch them. Its implementation was not changed.

## Local validation and limits

Host: Windows, .NET SDK 10.0.400. The focused tests construct descriptors and
run managed capture/geometry/layout code; they do not load AppKit or exercise
the macOS native ABI.

Command:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj `
  --filter 'FullyQualifiedName~MacGridAccessibility' --verbosity minimal `
  --logger 'trx;LogFileName=mac-menu-regression.trx' `
  --results-directory .cache/mac-grid-menu-regression
```

Result: **10 passed, 0 failed, 0 skipped**, including two new Reveal/Replace
cases. Each case admits Complete, Missing and Oversized descriptors, and refuses
sparse Pending slots and absent coordinates. Captured identities remain the
opening sequence 2; capture does not obtain a later identity.

For a causal red-to-green check, the helper's predicate was temporarily replaced
with the previous source-range/non-Missing condition. Running only the two new
cases failed **2/2**, both at Missing: expected the exact sequence-2 coordinate
intent, actual null. The original repaired bytes were restored in `finally`,
and the full focused filter passed 10/10 again. Evidence stays under
`.cache/mac-grid-menu-regression/`: `old-admission-negative.log`,
`old-admission-negative.trx`, and `mac-menu-regression.trx`. This is a managed
policy reproduction, not a native macOS negative run.

Independent static review is recorded in
[`mac-grid-menu-regression-review.md`](../reviews/mac-grid-menu-regression-review.md).
The reviewer found no remaining substantive issue in this bounded correction.

**Still required:** rerun the existing native diagnostic in both non-opt-in and
opt-in processes on `osx-x64` and `osx-arm64` from the repaired commit. Capture
the enclosing process exit, not merely the nested selector marker. No workflow
change is necessary for this follow-up. External AX/VoiceOver, geometry and
physical input/IME release gates remain separate and unclaimed.

## Semantic proxy shown-menu relation (2026-10-01)

The later non-view Table proxy passed the preceding frozen-intent checks in
both native targets at CI `36791254056`, but its external helper failed after
`AXShowMenu` returned success: no unique `Go to row:column…` item was found.
That success means only that AppKit triggered an action, not that a readable
menu was observed. The external traversal deliberately does not expand Table
children. Thus this failure alone cannot distinguish a missing native menu
from an omitted transient relationship or a helper traversal/label assumption.
See the independent target accounting in [the proxy record](mac-grid-table-proxy.md).

Inspection found a concrete representation omission: the proxy forwards the
existing physical Table's `accessibilityPerformShowMenu`, but did not expose
its `accessibilityShownMenu` informational getter. Apple's
[modern property contract](https://developer.apple.com/documentation/appkit/nsaccessibility-c.protocol/accessibilityshownmenu)
defines this as the currently displayed menu, and the external
[`AXShownMenuUIElement` relation](https://developer.apple.com/documentation/applicationservices/kaxshownmenuuielementattribute)
provides access to contextual menus without general descendant traversal.

The correction registers that modern getter on the semantic root and forwards
the **exact existing physical Table getter**. Main-thread and live-owner checks
precede the forwarding; a retained detached root returns nil. This is read-only
information, not an AX setter, a newly fabricated menu, a synthetic event or a
second menu lifecycle. The installed `menu` is not returned unconditionally:
a configured context menu is not necessarily currently shown. Existing
`accessibilityPerformShowMenu`, `menuWillOpen:` identity capture, physical input
and menu command admission are unchanged.

The in-process native selector probe adds a discriminating relation test using
a process-owned NSMenu marker temporarily assigned to the physical native
`accessibilityShownMenu` property. It requires the proxy to return that same
object, refuses an off-main lookup, then clears the property and requires nil
while the installed physical context menu remains unchanged. `finally`
restores the exact original property with explicit balanced retains/releases.
The retained-root-after-disposal test now also requires nil for this getter.
The marker test does **not** display a menu and is not evidence of actual menu
tracking, cross-process transport or successful logical navigation. With the
old proxy getter omission, the marker identity assertion would fail; this
negative is a code-path inference until exercised on a Mac target.

Local Windows/.NET SDK 10.0.400 validation compiled the source and passed the
existing focused managed filter **10/10**, with no AppKit execution:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj --no-restore `
  --filter 'FullyQualifiedName~MacGridAccessibility' --verbosity minimal `
  --logger 'trx;LogFileName=shown-menu-bridge.trx' `
  --results-directory .cache/mac-grid-menu-regression
```

Actual two-RID native relation/combined-probe execution and the separate client
remain required. The helper's new bounded, content-free shown-menu/type/role
diagnostics preserve its original query budget and menu assertion; they are
needed to distinguish product presentation from traversal/label assumptions.
This narrow information bridge is **not** a claim that the hosted external
failure has been fixed. No public Engine/Formats API or extra product binary
was introduced.

Independent static review found no substantive defect in this bounded bridge;
its exact scope and target limitations are preserved in
[the shown-menu review](../reviews/mac-grid-shown-menu-bridge-review.md).

## Native lifecycle discriminator (2026-10-01)

The shown-menu getter correction did not resolve external discovery in CI
`36792454502`. Both native relation-marker/combined probes actually passed;
both external clients passed 26 assertions through AXShowMenu, but neither
found the exact coordinate-command title. The Table/application shown-menu
attribute was absent (`-25205` ARM, `-25204` x64). ARM's action interval was
about 0.21 ms, which does not support a prolonged synchronous tracking wait,
but is not proof that an asynchronous popup occurred. See the independently
audited [external record](mac-grid-external-ax.md) for the complete accounting.

The next test deliberately adds **observations, not a display fallback**.
`MOTE_NATIVE_GRID_MENU_DIAGNOSTIC=1` is effective only together with the
existing Grid AX opt-in. One process-owned managed collector admits at most
16 lifecycle events across all Grid attachments. Without both opt-ins the
collector is null; normal menu callbacks do not allocate diagnostic data,
inspect diagnostic native state or write output.

The exact stdout protocol is:

```text
mote-grid-menu-v1 phase=show-enter seq=1 requests=1 opens=0 closes=0 open=0 result=-1 configured=1 items=12 coordinate=1 shown=0 key=1 first=0 active=1
```

- `phase` is one of `show-enter`, `native-return`, `will-open`, `did-close`.
- `seq` is 1–16; `requests`, `opens`, `closes` are 0–16 observations.
- `open` is the last observed delegate transition, **not** a visibility claim.
- `result` is the exact returned BOOL as 0/1 only on `native-return`; otherwise -1.
- `configured` records whether the physical Table's existing `menu` is non-nil.
- `items` is the exact native item count 0–16 or -1 for an over-bound count;
  `coordinate` is exact equality with the established coordinate-command title
  within that bounded scan. No arbitrary title is written.
- `shown` records only whether the physical current-menu getter is non-nil.
- `key`, `first`, `active` record exact physical window/application state;
  neither global desktop state nor another process is queried.

Only fixed enums and invariant-culture ASCII numeric fields are output, with
no source text, user path, document coordinates or PID. The separate harness
drains editor streams asynchronously and retains only strictly whitelisted,
bounded protocol rows; it does not preserve raw native output. Every diagnostic
exception is contained inside the observation method, leaving the established
menu action and frozen-intent capture unchanged.

| Observed order | What it can distinguish | What it cannot establish |
| --- | --- | --- |
| enter → return, no open callback before external cleanup | No observed native delegate opening | Why AppKit did not call the delegate; paint/reader visibility |
| enter → return → will-open | Deferred opening callback | A successful painted/readable menu |
| enter → will-open → did-close → return | Menu delegate lifecycle completed before the external action returned | Which event closed it |
| will-open without did-close, coordinate item exists natively but not externally | Callback-live configured menu vs external tree omission | Whether the actual popup is painted, another AX path exports it, or a reader can use it |

Apple's
[menuWillOpen](https://developer.apple.com/documentation/appkit/nsmenudelegate/menuwillopen(_:))
and [menuDidClose](https://developer.apple.com/documentation/appkit/nsmenudelegate/menudidclose(_:))
are the existing delegate observation seam. The action's documented
[BOOL contract](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol/accessibilityperformshowmenu())
explicitly distinguishes triggering from successful completion. In addition,
the inherited action previously used pointer-return `ObjC.Send`; it now uses
an exact `byte`-return `objc_msgSend` declaration for the native BOOL. The same
typed declaration reads the diagnostic native BOOL facts. This prevents
unspecified upper-register bits from being interpreted as an affirmative
result; it is ABI correctness, **not evidence that this caused the failure**.
No selector, receiver, menu item or physical input implementation changed.

Local portable tests pass **6/6** for ordered callback/return discrimination,
success/failure not inventing transitions, unknown-phase and exhausted-budget
refusal, invariant exact protocol, bounded item facts and the byte-return native
declaration. They do not execute AppKit:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj --no-restore `
  --filter 'FullyQualifiedName~MacGridMenuDiagnostic' --verbosity minimal `
  --logger 'trx;LogFileName=menu-lifecycle.trx' `
  --results-directory .cache/mac-grid-menu-regression
```

Fresh hosted lifecycle rows plus the unchanged external assertion are required
before choosing any menu-display fix. The original finite external budgets and
exact-title predicate remain intact; neither budget inflation nor global input
is an allowed substitute for evidence.

The independent [lifecycle/BOOL review](../reviews/mac-grid-menu-lifecycle-review.md)
found no demonstrated substantive defect in this bounded change. A possible
inter-part static-initialization dependency was eliminated by declaring the
diagnostic collector immediately after its accessibility gate in the same file.

### First forwarding native/external execution

CI 36792454502 / `0b85a0e` actually runs the selector and enclosing native probe
on both Mac RIDs; both success markers occur without a step error. Thus the
marker-property/off-main/clear/detached forwarding assertions execute on AppKit,
not just Windows managed compilation. Actual external menu discovery still fails
on both targets. The shown-menu relation is absent with -25204 transport error
on x64 and -25205 no-value on ARM; getter correctness alone does not establish
native menu lifecycle or external accessibility transport. See
[full target accounting](mac-grid-table-proxy.md#shown-menu-forwarding-target-ci-36792454502).

### Corrected native BOOL lifecycle execution

CI 36794910486 / `e439942` passes the actual in-process combined probes on both
Mac RIDs, but both external clients reject AXShowMenu (`AX=-25205`). Bounded
native lifecycle trace reports a configured 12-item menu containing the exact
coordinate command, followed by inherited BOOL false, with zero open/close
callbacks. No menu relation query or navigation polling is reached. This
supersedes earlier apparent action success for this corrected roundtrip.
See [full target evidence](mac-grid-table-proxy.md#correct-bool-and-lifecycle-target-ci-36794910486).
