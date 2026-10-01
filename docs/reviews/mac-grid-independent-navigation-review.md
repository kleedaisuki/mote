# macOS verified-menu independent downstream diagnostic review

Date: 2026-10-01. Scope: current test-only delta in
`tests/MacGridAxExternalProbe/Probe.swift` and `Run.ps1`. Production menu,
selector permissions, role, source behavior and trace are unchanged. Windows
static review; no native Swift compilation or execution is claimed here.

## Result

No substantive defect found in the target-safe guarded experiment. It may be
frozen/pinned for target execution. A completed independent route must never
be presented as an overall accessibility pass.

## Admission and action safety

- The original AXShowMenu call is not retried. Its failed check is recorded
  before followup, and the exact error is saved independently. Additional
  actions require attributeUnsupported, exactly 26 recorded checks with the
  preceding 25 all passed, and an advertised exact showMenu action.
- `ownedCoordinateItem` requires a success reply for the direct Table
  relationship, CF AX-element type tag, exact editor PID admission and AXMenu
  role. Child enumeration uses the existing count-before-copy limit of 128
  and per-child ownership/timeout/admission checks.
- Exactly one item must have AXMenuItem role and both the established exact
  coordinate title and label. A missing, duplicate, foreign, wrong-role or
  over-bound menu does not authorize any action. No app-tree menu fallback,
  key/mouse injection, broad label guess or source command is introduced.
- After these guards, only that validated numeric Go item is pressed. The
  original unique prompt/message/field/button predicates remain; the only new
  input is the existing fixed synthetic `1001:17` coordinate string. Existing
  full-source, exact absolute ordinal/value, local range, stale/mixed node and
  normal-close checks are reused rather than replaced by weaker expectations.
- Prompt/window discovery still follows the existing exact-PID bounded tree;
  it does not use another process's modal or global focus. No Copy, Replace,
  Save, pasteboard or input-source action is added.

## Verdict and lifecycle accounting

- Once `originalActionError` exists, both successful completion and every catch
  path return overall `failed`; the executable retains exit 1. Completion
  does not rewrite the original false check or original error.
- Downstream status/phase/error/note are separate. A guard-refused path performs
  no action, a later ordinary assertion still fails fast, and complete means
  client checks finished, **conditional on wrapper normal-exit evidence**.
- The wrapper's completed-downstream branch verifies child exit 1, trusted
  target, close attempt, global complete phase, exactly one false check named
  context-menu-accessible and original error -25205. It then checks actual
  owned editor exit 0. It preserves the whole failed status even when that
  close succeeds. If normal exit is absent, `editor_normal_exit=false` and an
  explicit error remain; completion alone is not normal-close acceptance.
- Existing fixture-byte verification and exact-child cleanup remain in
  `finally`. Neither failure nor a completed diagnostic suppresses those
  obligations. Retained handles are queried only within their existing scope.
- Existing 12,000 admissions, 55-second client lifetime, 75-second watchdog,
  per-call timeout, prompt/navigation read-only waits and array/tree limits
  remain unchanged. No increased budget manufactures a result.

## Evidence boundary

This experiment can establish a safe downstream workflow independently of
the unresolved AXShowMenu action reply. It cannot explain why that reply is
attributeUnsupported, establish VoiceOver/IME acceptance, or justify a legacy
permission/action override. Native Swift typecheck and two-RID execution are
still required, with the first downstream falsifier and actual normal exit
audited separately from the immutable primary failure.
