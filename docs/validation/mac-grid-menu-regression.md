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
