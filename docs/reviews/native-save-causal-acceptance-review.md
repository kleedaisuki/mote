# Ordinary native JSON Save causal acceptance: independent review

Date: 2026-10-01. Reviewed change: `028af4a` against its parent. Scope:
`benchmarks/NativeJsonLargeAcceptance/probe.py`, its fixtures and README,
and the additive vocabulary/tail handling in `NativeAcceptance/acceptance.py`.
The typed reader was inspected for contract and captured-version consistency;
this does not duplicate its prior independent review or certify native UI runs.

## Verdict

**No substantive defect or push blocker identified.** The pilot preserves its
existing exact-byte and UI acceptance requirements while adding a distinct
native Save causality result. The closed result vocabulary avoids mistaking
missing telemetry for input non-delivery or a killed session for completion.

## Source-backed assessment

- `save_exact` and its one attempted Save transaction are unchanged. There is
  no resend, input activation, unknown-modal dismissal, or new Save retry.
  Exact saved bytes, clean acknowledgement, normal original exit, matched
  edit/draw endpoints, fresh GUI reopen and immutable fixture checks remain
  required. A complete trace chain cannot replace any of those oracles.
- Normal acceptance additionally requires one `command.save` request and the
  native contract, not `RECOVERY_SAVE_CONTRACT`. Persistence phases, the
  route-specific move/replace alternative, stamp/bookkeeping and local UI
  completion must succeed. Replace additionally requires final target check.
  Missing session terminal, scoped drops or unlinked stages degrade coverage.
- Authoritative `save.snapshot_captured` versions must exist and agree.
  Successful saved phases, local UI outcome and request terminal must carry
  that same version. Generic endpoint checks also retain the pilot's expected
  version 1; current UI state cannot substitute for the saved snapshot.
- The final audit runs after owned cleanup and retains `original_child` and
  original `home`, even after `child` is replaced by the reopen process. It
  deliberately overwrites the earlier Save evidence with the final stopped
  original-process evidence. A reopen failure can coexist with a complete
  original Save chain, but still leaves the overall sample failed.
- Failed/non-normal cleanup never fabricates a normal original exit. A retained
  incomplete request after termination is censored. No retained request is
  unobserved, even after normal shutdown; `absence_certified` stays false.
  Failure cleanup that genuinely exits zero can provide a drained original
  trace without making the workload's normal-exit or overall-pass flags true.
- Generic reader changes add only fixed operations, phase-entry names and a
  closed reason enum. Arbitrary names, reasons, attributes and source payloads
  remain rejected. Optional tail handling skips only an unterminated final
  physical row, including split UTF-8; malformed newline-complete rows fail.
  Native traces are bounded by inventory, file, record and count limits. Reader
  and driver/auditor SHA-256 values preserve artifact provenance.

## Evidence inspected, not rerun

The retained regression at
`.cache/native-save-reader-regression/0a92cfa32537437580cc2a55d6314d7d/regression.json`
records old generic-auditor rejection of legal `command.save.received`
(`unknown operation/status`) and the new native-contract result: one censored
receipt-only request, no fabricated terminal, absence not certified.
README documents this regression and correctly labels portable checks as
fixture/protocol evidence, not a current native GUI certificate.

Test bodies were inspected for complete-chain, missing-phase, captured-version
mismatch, observed-drop, duplicate-request, strict vocabulary, complete-malformed
row, split-UTF-8 tail, original/reopen association and timeout/finally coverage.
Previously completed portable tests were not rerun. No platform acceptance,
performance measurement, physical input, native wake or compositor result is
claimed by this review.

## Remaining acceptance boundary

Inspect the next hosted ordinary 1/100 MiB reports and raw traces on every RID.
The summary remains an instrumented callback-to-local-UI claim, not proof of
physical delivery, power-loss durability or pixels. A green non-gating job is
not a substitute for the sample's byte, exit, reopen and causal outcomes.
