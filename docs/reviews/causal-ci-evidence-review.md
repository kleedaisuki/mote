# Causal CI recovery and evidence summary: independent review

Date: 2026-10-01. Scope: the additive Native AOT job recovery/summary region in
`.github/workflows/ci.yml`, `tests/summarize_ci_evidence.py`, its fixtures, and
`docs/validation/ci-evidence-summary.md`. The recovery driver and probe were
inspected only for invocation, ownership, bounds, and test-versus-product scope;
this does not repeat the Telemetry transport or Engine/Native Save reviews.

## Verdict

**No substantive defect identified in the reviewed integration.** The change
improves diagnostic visibility without weakening existing release gates or
claiming that synthetic prefix recovery is a successful real document Save.
Hosted execution of the new steps is still required; local checks are not a
four-platform acceptance certificate.

## Checks and evidence

- The workflow diff is an additive 53-line region at the end of the Native AOT
  job. Existing strict checks, diagnostic oracles, action versions, and product
  publish steps are unchanged. All new experiment, summary, and upload steps
  are non-gating. Missing uploads warn rather than fabricate success.
- Recovery runs only after the native-architecture Python setup reports success.
  PowerShell passes explicit arguments, not a scalar splat. Publish and driver
  nonzero exits are checked explicitly. The entire publish/experiment step has
  a five-minute timeout; summary has a separate two-minute timeout. An always-run
  summary may itself fail or be interrupted, and missing evidence stays unknown.
- The driver creates fresh output strictly beneath repository `.cache` or
  `.temp`. It terminates only its own `subprocess.Popen` child after a positively
  observed linked prefix; it has per-case deadlines and finally cleanup. There
  is no global process-name kill, user input injection, or user-profile I/O.
- The probe references Telemetry only and is separately published beneath
  `.cache/causal-recovery-probe/<RID>`. It does not enter the product payload,
  change the strict one-binary product contract, or pretend to exercise Engine
  persistence. Three expected kills can pass the synthetic experiment while
  the request evidence remains censored and absence is not certified.
- Summary output separates report availability, normalized claims, typed exits,
  and evidence health. It never infers exit zero from a pass claim. Empty,
  missing, or pending recovery requests remain unverified; observed terminals
  require an explicit normal session and recognized request classifications.
  JSON normal completion is not inferred from the absence of forced cleanup.
- Native Save-reader claims use the exact `native-save-causal-v1` contract and
  fixed status enum. Only request count and typed lifecycle/absence Booleans
  accompany the claim; SHA values, paths, and request payloads are not copied.
  The summary is a view of that reader claim, not an independent certificate.
- Fixed manifests include the original macOS Grid external report and the
  same-client pair. Cached run `36818175897` arm64 evidence independently
  summarized to Swift exit 1 / 40 of 41 checks, and C0 AX reply 0 versus P0
  -25205 with owner/client exits 0. Pair experiment completion is not promoted
  to product acceptance.
- Output copies fixed IDs/status enums and typed scalar fields, not paths,
  source text, raw statuses, error strings, PIDs, screenshots, or raw traces.
  Unknown inventory filenames are counted without disclosure. Input size and
  nested output bounds prevent accidental large report/payload export.

## Independent local validation

`python -B -m unittest discover -s tests -p test_summarize_ci_evidence.py -v`:
**23/23 passed**. PyYAML parsed the workflow with five jobs. `git diff --check`
found no whitespace errors. Tests use repository-local temporary fixtures.
No new hosted recovery run was performed as part of this review.

## Limits and next acceptance boundary

The summary is intentionally a typed view of selected report claims, not an
independent rerun or complete release inventory. A malformed report, missing
numeric exit, interrupted upload, or unavailable summary does not establish
that a callback failed to execute. Trace `no-observed-drops` is not producer-loss
certification. The first hosted run must inspect the raw four-RID recovery case
outcomes and ordinary native Save evidence, rather than trusting a green job.
The five-minute publish/experiment budget may produce incomplete diagnostics
on an unusually slow runner; that is surfaced as missing or failed evidence,
not a release-gate relaxation.

