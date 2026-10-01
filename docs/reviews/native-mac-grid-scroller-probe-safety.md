# macOS logical Grid scroller probe: independent static safety review

Reviewed: 2026-10-01 on Windows. Scope: frozen
`src/Mote.Native/Mac/MacGridScrollerProbe.cs`, its validation document, reachable
`MacCsvGrid` construction/action/disposal, `MacGridScrollInterop`, and typed
Objective-C bridge declarations. No target execution, production modification,
staging, commit, push or workflow dispatch was performed.

## Decision and identity

**Approve this frozen probe for bounded, separate published Native AOT process
invocations on disposable GitHub-hosted macOS x64 and ARM64 runners, subject to
the integration conditions below. No substantive static safety or probe
correctness blocker was found in the examined scope.** This is not approval of
an unexamined CLI route or workflow; neither exists in this assignment's scope.

Final independently measured source SHA-256 after narrow safety hardening:

- Current raw LF: `E0E0CCDA60C1AC2DC144CE01CE65B420457F1B9BDDB85899225762AB9F5B2938`.
- Equivalent UTF-8 CRLF: `63A3CFF32138CBA344D8A8CA91347B666AF37DB9FC9825ACB3527400EDCF97C5`.

These supersede initial raw hash
`B55AF603AB5638D0D7944EBB700A4C2E7547F2C1718C38736F5994E7BFC6605F`.
Any probe change requires a delta review before target dispatch.

Narrow re-review: fixed failure stderr and preallocated previous-class slots
with all instance replacements inside the restoration `try/finally` are approved.
No assertion, callback ABI, execution scope or acceptance boundary changed.

## Containment, instance replacement and lifetime

- `Run` loads system AppKit, creates an autorelease pool and initializes the
  process-local shared application. `Check` owns one hidden `MacCsvGrid` through
  `using`; normal and managed-failure unwinding disposes it before pool drain.
  No window is created or activated, application run loop started, or mouse
  tracking loop entered. Framework housekeeping is not a promise of zero OS
  filesystem activity.
- Scroller discovery is restricted to immediate subviews of that newly owned
  Grid container. The production constructor creates exactly two standalone
  `MoteCsvGridScroller` instances there; the probe requires two NSScroller-kind
  controls and discovers the distinct production NSScrollView separately.
- `objc_allocateClassPair` creates a new subclass of the production scroller
  with zero extra bytes and no added instance variables. The sole new method is
  `hitPart`; no inherited method implementation is replaced. `object_setClass`
  is called only on the two owned scrollers. Thus this is per-instance class
  replacement, not a global method swizzle or change to shared NSScroller.
- The previous class pointers returned by `object_setClass` are recorded and
  restored in `finally` before sparse-table assertions and Grid disposal. No
  native action outlives the synchronous scope or escapes into an event loop.
  Grid disposal clears navigation and callback targets, removes handle lookup
  entries, and releases owned objects. The registered probe class intentionally
  remains process-local until exit; a fresh process is required for each run.
  Discovery/registration failures occur before instance replacement. The
  previous-class slots are preallocated before mutation; every replacement is
  inside the restoration `try`, and each successfully saved nonzero original
  class is restored even if a later replacement fails with managed unwinding.
  A native exception/crash still cannot promise CLR finally execution.
  Therefore restoration is a normal/recoverable-managed-failure invariant,
  not a fatal-runtime recovery guarantee. No instance is shared outside this
  isolated process, so this does not leave persistent host state modified.
- The only new unmanaged callback returns a pointer-sized integer field and
  cannot throw. Managed callbacks invoked by the production adapter are
  protected by its existing unmanaged-boundary catches. Native exceptions or
  runtime faults remain process failures, not managed recovery guarantees.
- No reachable probe path reads/writes clipboard, opens/saves a document,
  reads user settings, requests TCC/AX permission, changes input sources, injects
  desktop events, or invokes networking. Stdout/stderr are the probe's explicit
  output. Production menu construction does not invoke its modal commands.
  Recoverable managed failure stderr now emits only the fixed identifier
  `mote-native-mac-grid-scroller-failed; contract=acceptance`, not arbitrary
  exception messages. Thus this application-authored catch output cannot leak
  exception host paths. Fatal runtime/native diagnostics are outside that catch's
  content-free guarantee. No user-document content is read by this route.

## Static ABI assessment

Both intended RIDs are 64-bit. `object_setClass(id, Class)` uses pointer-sized
parameters and result. `hitPart` returns an NSScrollerPart/NSInteger through
`nint`, with Objective-C encoding `q@:` and direct
`delegate* unmanaged[Cdecl]` / `UnmanagedCallersOnly` function-pointer retention.
No managed reflection, generated trampoline, dynamic managed compilation or
external application library is required by this new callback.

`viewAtColumn:row:makeIfNecessary:` uses pointer-sized column/row arguments and
a one-byte BOOL. Position/proportion getters and setters use typed `double`
signatures, appropriate for 64-bit CGFloat and double register classes.
Rectangle readback delegates to the existing x64 stret versus ARM64 direct
aggregate return bridge. Direct `Fire` uses the actual installed target and
selector, passing the sender pointer; its return value is ignored. This checks
the callback invocation, not AppKit's control event dispatch machinery.
Actual Native AOT compilation/linking and target ABI execution remain pending.

## Evidence contract and limitations

The assertions are finite and use a bounded tiny table despite distant logical
coordinates. Six vertical synthetic part actions each require exactly one
terminal action with the expected navigation stamp and unaffected column;
the horizontal endpoint requires exact zero. Unique sequence IDs and bounded
measured page sizes are checked. Real native getter readback verifies logical
position/proportion and exact/prefix labels. Pending and sparse ordinal slots
are read through real table callbacks, and source command attempts are refused.
After explicit navigation retirement, a later synthetic action cannot obtain
fresh admission.

The vertical expected coordinate calls the same pure mapping helper as the
adapter; it verifies native selector wiring and frame propagation, not an
independent arithmetic oracle. The separately documented portable mapping
tests provide that additional coverage. Label getters do not prove external
accessibility delivery. There is no controller/engine/file instance, so no
controller stale-token, source version, Undo or disk-integrity acceptance follows.
No physical drag/hit test, synchronous tracking invalidation, overlay opacity,
screen-reader behavior, paint, timing or real input acceptance is established.
The validation document states these boundaries accurately.

## Required CLI and CI integration envelope

1. Add an exact, single-purpose opt-in argument route before settings, ordinary
   controller composition, document opening and telemetry startup. Reject other
   hosts/argument shapes, and return `Run()`'s exit code unchanged.
2. Invoke synchronously on the executable entry-point/main thread; do not wrap
   AppKit creation in `Task.Run`, a worker thread, or a reused application session.
3. Use each published Native AOT RID in a fresh process on its matching disposable
   GitHub-hosted macOS runner, with a finite external timeout (for example a
   three-minute CI step plus subprocess timeout). No developer desktop run is
   necessary for this static approval.
4. Require both process exit code zero and the exact complete marker below;
   absence, mismatch, timeout or nonzero exit is failed/unverified acceptance.
   Preserve target stdout/stderr and identify the RID and source commit. A
   non-gating diagnostic step must not turn a parent green run into a claim that
   the diagnostic passed.

```text
mote-native-mac-grid-scroller-ready; native-action=synthetic; physical-input=not-tested; controller-stale-token=not-tested
```

## External reference check

Apple documents [object_setClass](https://developer.apple.com/documentation/objectivec/object_setclass(_:_:))
as setting one object's class and returning its previous class. Apple's
[Objective-C Runtime overview](https://developer.apple.com/documentation/objectivec/objective-c-runtime)
identifies the system runtime and its bridge/debugging role. The
[upstream runtime header](https://github.com/apple-oss-distributions/objc4/blob/main/runtime/runtime.h)
provides the C API signatures. Existing AppKit part/geometry reference trail is
retained in [adapter validation](../validation/mac-grid-logical-scroller.md).
These sources support static API interpretation, not execution evidence.

## Final CLI/CI integration delta review

Reviewed the current `Program.cs` and `.github/workflows/ci.yml` delta on
2026-10-01, without target execution. **The implemented integration satisfies
the execution envelope above and is approved for the specified hosted runs.**
Independently measured current raw working-tree SHA-256 values:

- `src/Mote.Native/Program.cs`:
  `7132272485A2BBD06286129A1A269758BB3F025B0612E77CA284175F2E7B0330`.
- `.github/workflows/ci.yml`:
  `0E775B5101DFA593A3791D982374FB2D21942FBBC27F5795E18B00D65115771F`.
- Probe remains the approved LF hash
  `E0E0CCDA60C1AC2DC144CE01CE65B420457F1B9BDDB85899225762AB9F5B2938`.

The exact one-argument `--check-native-mac-grid-scroller` route runs directly
from synchronous `Main`, requires macOS and returns `Run()` unchanged, before
normal launch parsing/settings/document/controller initialization. It is
distinct from the existing two-argument `--check-native-mac-grid-clipboard
fake|actual` route. That route, its second-stage disposable-runner admission,
invocation script and dedicated actual-clipboard jobs are unchanged by this
delta; no clipboard permission is set or inherited by the new step.

The existing strict native publish matrix maps `osx-x64` to `macos-15-intel`
and `osx-arm64` to `macos-latest`. The new step runs only those two RIDs, starts
their published executable as a separate process, checks raw probe bytes against
the two fixed approved LF/CRLF hashes before launch, and has a three-minute
external GitHub step timeout. It prints captured stdout/stderr, requires exit
zero and exactly one case-sensitive complete success marker, and fails that
diagnostic step for missing/duplicate marker or nonzero exit. Publication failure
or an absent executable cannot satisfy the marker/exit contract.

`continue-on-error: true` intentionally makes this diagnostic non-gating, and
`always()` permits evidence collection after other failures. Consequently the
coordinator must inspect the individual diagnostic outcomes/logs for **both**
RIDs; a green parent job/workflow alone is not acceptance. No target outcome is
claimed by this final static approval. The hashes identify examined working-tree
bytes, not an assertion that unrelated future edits are approved.

### Final whitespace-only identity update

Root removed exactly one trailing blank line at probe EOF before staging.
Independent reconstruction of the current LF text plus one final LF hashes to
the previous approved `5CA4DFAF46AB66583117530EB00F80CAC3412549E4CE8EB6F67F01D36A84BA82`,
establishing the entire source delta is exactly that newline deletion. Current
raw LF and equivalent CRLF hashes above were independently recomputed; CI's two
fixed source pins now use those new values. No executable statement, signature,
assertion or execution condition changed. Final approval remains in force for
these identities. Prior CRLF identity was
`C8BCC6F46107012E6533BF22B8069371722313D7C34CBAA95E22BF3595104940`.
