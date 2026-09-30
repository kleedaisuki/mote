# Windows JSON readiness CI pilot review

## Scope and disposition

Reviewed only the uncommitted `.github/workflows/ci.yml` readiness integration
against committed HEAD `37b63983805d78e1cce62f89f0962d4fd97b36c8`, the committed
`benchmarks/NativeStartup/Measure-WindowsOrdinary.ps1`, its existing
`ExternalFixtureReview.md`, `NativeStartup/README.md`, and the corpus preparation
contract in `benchmarks/NativeAcceptance/acceptance.py`.

**Disposition: no unresolved substantive findings after the upload correction
below.** This approves the bounded CI integration, not hosted GUI capability or
latency acceptance. No production/workflow file was edited by this reviewer;
no native process, GUI, clipboard, or user-state operation was run. Existing
adapter preflight validation was not repeated.

Final reviewed raw workflow SHA-256:
`5B03EE9287AA483385B262B588E97F91B33E5DF167A9C978FD59AB62D97012CC`.

## Material finding resolved

**P2, high confidence: diagnostic upload was inadvertently gating.** The first
revision made Python setup and the probe non-gating, but not the new upload
step. An artifact-service, network, or quota failure would fail the otherwise
healthy win-x64 job and skip subsequent default-success hard gates. The owner
added `continue-on-error: true` to this upload. Reinspection confirms all three
new readiness steps are now non-gating; existing mandatory gates are unchanged.
`if-no-files-found: warn` alone would not handle actual upload errors.

## Verified contracts

- Only win-x64 receives Python setup and the readiness probe. Other supported
  RIDs and mandatory imports, inventory, canvas, and GUI gates are unchanged.
- The ordinary executable is freshly published earlier in this same job. No
  prebuilt or local binary is substituted. The row must identify the checkout
  HEAD and the freshly published executable's SHA-256. On pull requests HEAD
  can be GitHub's tested merge commit, not necessarily the contributor tip.
- Corpus preparation selects exactly one 1 MiB JSON long-string case. Its
  manifest hash must equal the independently pinned digest. The adapter also
  verifies actual bytes, owns a separate copied fixture and isolated MOTE_HOME,
  refuses traces for external fixtures, and cannot enter edit/Save with the
  supplied `ReadinessOnly` switch. Both post-probe fixture hashes are checked
  again by the CI row predicate.
- A passed row additionally requires process identity, canvas and editor
  discovery, certified source-prefix readback, selection acknowledgement, no
  trace, and null edit/Save and uncollected child endpoint fields. A missing or
  failed result cannot reach the success message. The driver throws failed
  samples, so routing its output through `Out-Host` does not suppress failure.
- The one-row condition is appropriate for the fresh hosted checkout: no
  earlier step invokes this append-only startup driver. A reused local
  workspace with previous rows intentionally fails rather than silently using
  a tail row; this integration is not a general local benchmark runner.
- Upload runs after failure or skipped preparation, warns on absent evidence,
  uses a unique win-x64 artifact name, and retains only JSONL plus synthetic
  corpus manifests for 14 days. The first one-case result is capability only:
  neither p95 nor foreground physical-input/paint latency is claimed.
- Missing `include-hidden-files` is **not** a demonstrated defect for these
  explicit nested paths. Toolkit glob starts at the exact JSONL filename and
  `native-acceptance` directory, then checks each visited basename for hidden
  status; it does not traverse `.cache` as a hidden search-root item. Replacing
  these patterns with the whole `.cache` directory would require reassessment.

## Validation and external evidence

- PowerShell 7.6.5 parsed the actual extracted readiness run block with
  `System.Management.Automation.Language.Parser.ParseInput`: zero errors.
- Python/PyYAML 6.0.3 loaded the workflow and independently confirmed the three
  readiness step conditions and `continue-on-error: true` values. This is YAML
  structure validation, not a claim of actionlint or hosted execution.
- GitHub documents step-level failure control and default shell behavior in
  [workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax).
- Artifact absence handling is distinct from transport failure; hidden-file
  defaults are documented in the
  [upload-artifact v4 README](https://github.com/actions/upload-artifact/blob/v4/README.md).
- The explicit-path hidden-ancestor analysis follows the implementation in
  [upload-artifact search](https://github.com/actions/upload-artifact/blob/main/src/shared/search.ts)
  and [toolkit glob traversal](https://github.com/actions/toolkit/blob/main/packages/glob/src/internal-globber.ts).

Remaining acceptance requires actual hosted execution and retained evidence.
No local GUI run, action service fault injection, timing threshold, or general
JSON correctness validation was performed or implied by this review.
