# Windows Save diagnostic CI integration review

Reviewed 2026-10-01 against checkout HEAD `6b3e5a7c4645c8bb088d051abe30211449c93db4`.

## Scope and disposition

Reviewed only the new uncommitted `windows-save-diagnostic` job in
`.github/workflows/ci.yml`, its wiring to the committed diagnostic/publisher,
the updated driver documentation, and relevant current Save recovery UI and
telemetry contracts. Existing related knowledge was reused from
`windows-save-diagnostic-review.md`, `save-failure-recovery-review.md`, and
`windows-readiness-ci-review.md` rather than re-running their validations.

**Disposition: no unresolved substantive integration defect found.** The job
is a bounded, non-gating hosted capability/evidence pilot, not a validated GUI
experiment, a Save reliability gate, or proof that error 1175 is reproduced or
fixed. The first hosted report still needs examination.

No workflow or production source was modified by this reviewer. No publish,
native executable, GUI, clipboard operation, user document, or old local binary
was used. Only the review and repository-local static parsing artifacts were
written.

## Reviewed identity

| File | SHA-256 |
| --- | --- |
| `.github/workflows/ci.yml` | `86F1F5DC3E3CD28A87D2E0A659A46E011ECFC353579E148D09B213A702B9E4C4` |
| `tests/Invoke-WindowsSaveDiagnostic.ps1` | `17EFCF3D86FE6B77368D84B1099F2EA31C94B813211AA0D6207C189DC3751BB3` |
| `tests/New-WindowsSaveDiagnosticPublish.ps1` | `F849E683B779C7F16F3E63C4DDBADF74DDA1534836183BF042790BF77E5B7997` |
| `docs/windows-save-diagnostic-driver.md` | `E7BE27E252435E14DB060C0225B1D8BCED2E7860524AFA5457310706D07B318A` |

The workflow identity covers the whole file but the substantive assessment is
limited to this new job. Concurrent unrelated working-tree production changes
were not reviewed or used to authorize a local publish.

## Clarification resolved during review

The initial upload step called the artifact "private", while the driver document
still described the implementation as not integrated into CI. An artifact name
or seven-day retention does not create an access restriction: authenticated
users with repository read access can download it. The owner removed "private"
from the step name and documented repository-level visibility, synthetic-only
upload, and wired-but-not-yet-executed CI status. The final reviewed delta no
longer implies a private artifact service or a completed hosted experiment.

No confidential-content leak was demonstrated: this new runner receives no
explicit user document or custom secret input, and the upload patterns do not
include checkout credentials, `.git`, the entire cache, or arbitrary runner
state. This conclusion assumes the reviewed trusted checkout and publisher,
not adversarially modified workflow or producer code.

## Verified integration contracts

### Non-gating behavior and strict CI isolation

- `continue-on-error: true` is valid at job level, including a non-matrix job.
  GitHub documents that this prevents a failed job from failing the workflow.
  The upload additionally has step-level `continue-on-error: true` and
  `if: always()`; an absent file is separately configured to warn.
- This job has no `needs`; no current job names it as a dependency. Mandatory
  test, Native AOT, import, inventory, GUI and other existing gates are unchanged.
  No failure from this diagnostic skips strict steps inside another job.
- The condition runs only on `push`, not pull requests or manual dispatch.
  It checks out and publishes that run's source on a separate Windows hosted
  runner. There is no GUI job sharing its desktop. Workflow-wide cancellation
  of superseded runs remains possible and is not a successful diagnostic.
- Repository branch protection is external configuration. Do not configure this
  experimental check as a new required release/merge check; that configuration
  was not inspected here. Non-gating status still consumes runner time and can
  extend overall workflow completion, but does not weaken strict test criteria.

### Output and provenance wiring

- Publisher build output is piped through `Out-Host`; its sole successful
  success-stream result is the final absolute manifest path. `Select-Object
  -Last 1` therefore selects that path, not the final compiler-log line.
- A terminating publisher error propagates under `Stop`; an empty result is
  explicitly rejected. There is no fallback executable or old manifest search.
  The consumer receives the trimmed path and runs at most four ordinary
  children after the independent held-share control.
- The self-test creates untracked scratch only and does not alter tracked
  publish inputs. Fresh checkout, no restored build cache, and the publisher's
  unique artifacts root avoid the local dirty-tree and stale-intermediate
  issues described in the original driver review.
- Producer and consumer pin/check tracked input hashes, HEAD, executable
  bytes/hash, SDK, runtime pack, publish/log provenance, freshness and single-file
  inventory. Uploaded producer manifest and publish log preserve the input
  fingerprints and log hash; the diagnostic manifest/rows preserve runtime
  environment and binary identity. The executable/build tree is intentionally
  not uploaded. This is accidental-staleness provenance, not a signed or
  adversary-resistant supply-chain attestation.

### Save safety, user state and artifact scope

- All document bytes originate from the driver's fixed synthetic in-memory
  generator. Each native child receives one repository-local generated fixture
  and isolated `MOTE_HOME`; tracing uses that home, not a user's real home.
- The driver sends bounded messages only to exact child PID/class/owner-filtered
  windows and controls. There is no clipboard read/write or foreground global
  keyboard injection. Unknown dialogs are not acknowledged.
- Current source emits the `Save failed.` prefix and a retained-recovery warning
  before discard. The driver uses the current prefix and fixture-specific
  deterministic recovery slot, followed by the exact dirty-close discard
  prompt. Recovery bytes are preserved; the workflow adds no deletion or retry.
- Final capture copies only that child's synthetic directory, including hidden
  recovery sidecars. `include-hidden-files: true` is appropriate for these
  narrowly scoped evidence patterns, not a general permission to upload homes.
- The patterns upload diagnostic rows/manifests/traces and finalized synthetic
  fixture/home/recovery captures plus publisher manifest/log, not the full
  publish output or unrelated `.cache` products. Fixed numeric telemetry fields
  and `RecordSaveFailure` omit exception messages, arbitrary Data and user paths.
  Producer logs may contain repository/runner paths and public dependency/build
  details; they are not claimed to be path-free.
- Seven-day retention reduces duration, not access. Repository visibility is the
  artifact boundary. No Procmon/ETW collector or machine-wide capture is added.

### Timeouts and truthful interpretation

The 12-minute combined publish/diagnostic step contains the driver's normal
285-second polling ceiling and per-child 45-second watchdog. At the full driver
polling ceiling, about 435 seconds remain for publish/preflight within that step.
The 15-minute job has three minutes beyond the step's 12-minute ceiling, shared
by checkout, SDK setup, self-test and upload. The two-minute self-test limit is
another ceiling, not a guaranteed two-minute duration.

This is a reasonable bounded pilot budget, **not a measured proof that a cold
AOT publish plus GUI can always complete**. Slow setup/publish or stalled evidence
I/O can exhaust the job budget before upload. `always()` is not a guarantee of
artifact delivery after job timeout, VM failure or workflow cancellation. Such
runs remain missing/incomplete evidence, not a clean Save result. There is no
basis from static inspection to require a larger budget before the first hosted
sample. If observed timing leaves insufficient upload reserve, separate publish
and diagnostic step budgets and increase the total budget based on that evidence.

The driver can return normally after the first ordinary failure or incomplete
ordinary trace: it stops further children and retains that row, rather than
throwing every diagnostic finding. Its summary's `control = passed` refers only
to the positive control, and `ordinary_save_failures = 0` does not imply all
ordinary children completed (an infrastructure failure can also produce zero).
A green job/workflow therefore means neither four successful ordinary Saves nor
complete trace drain. The workflow has no unconditional "Save passed" message;
the updated documentation explicitly requires inspecting the first report even
when strict CI is green. Consumers must check each ordinary row's exact byte
outcome, trace completeness, failure list, attempted count and exit status before
making any bounded non-reproduction claim. Fake self-test traces prove only
oracle behavior, not hosted UI capability or production shutdown.

## Validation and external evidence

- Python/PyYAML 6.0.3 parsed the actual workflow and asserted the new job's
  push-only condition, non-gating flag, Windows runner, timeout, no dependencies,
  five steps, and always/non-gating upload.
- PowerShell's AST parser parsed both extracted new `run` blocks: zero errors.
  Extracts are under `.temp/windows-save-diagnostic-ci-review/`. No script entry
  point was executed and previously completed self-test validation was not
  repeated. This is not actionlint or a GitHub-hosted run.
- GitHub's [workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)
  documents job/step failure control and job/step timeout semantics.
- GitHub's [hosted runner documentation](https://docs.github.com/en/actions/concepts/runners/github-hosted-runners)
  documents fresh VMs for these hosted jobs; it does not prove this application's
  desktop capability.
- The [upload-artifact v4 README](https://github.com/actions/upload-artifact/blob/v4/README.md)
  documents hidden-file inclusion and missing-file policy.
- GitHub's [artifact download documentation](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts)
  establishes authenticated repository-read-access visibility.

Next acceptance is the first clean-source hosted publish/control/ordinary batch
with retained rows and traces. No timing threshold, reliability rate, historical
1175 reproduction, binary execution or GUI completeness is certified here.
