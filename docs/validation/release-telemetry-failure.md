# Release candidate telemetry cascade

Date: 2026-10-02. Candidate: `25b897e2bc375c0e36556ff569717843ff2bfa16`.
Hosted run: [36915680535](https://github.com/kleedaisuki/mote/actions/runs/36915680535).

## Diagnosis

The Windows complete `Mote.Tests` run failed 31 of 3,661 tests (3,630 passed,
zero skipped). The earliest failure in the retained job log is
`NativePostedCallbackTests.Windows_uncreated_shell_drain_preserves_notice_policy`
with `existingNotice: true`: a `NullReferenceException` at line 22, not an
allocation assertion or telemetry configuration failure.

The test configures process-wide telemetry, then looks up a nonpublic
`WindowsEditorShell(bool, bool)` constructor before entering its cleanup `try`.
The additive native-source integration changed that internal constructor to
`WindowsEditorShell(bool, bool, bool)`, with optional parameters. Reflection's
exact two-parameter lookup does not bind the optional third parameter and returns
null. Null-forgiving syntax does not validate that lookup. The test consequently
throws before the `finally` that shuts down tracing.

The other theory case immediately reports `Telemetry is already configured`.
Subsequent enabled tests hit the same exception; disabled tests find tracing still
enabled and correctly reject allocations or validation. Those are downstream
failures, not evidence that disabled tracing changed its production semantics.
`NativeSourceTelemetryTests` already belongs to the nonparallel `Telemetry`
collection and is not the primary leak.

## Independent reproduction

From the repository root, using the existing Release build:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-build `
  --filter 'FullyQualifiedName~NativePostedCallbackTests.Windows_uncreated_shell_drain_preserves_notice_policy' `
  --logger 'trx;LogFileName=before.trx' `
  --results-directory .cache/release-telemetry-failure
```

A fresh test process reproduced both failures: the first case failed with the
missing-constructor `NullReferenceException`, and the second failed with
already-configured telemetry. Evidence is retained in
`.cache/release-telemetry-failure/before.log` and `before.trx`. A repository search
found this to be the only exact two-Boolean constructor lookup in `Mote.Tests`.

## Correction and verification

The corrected test looks up the exact three-Boolean constructor, asserts that it
exists, and passes `false, false, false`: the established noncanvas, nonfragment,
non-native-source shell. All constructor lookup, shell setup and original
assertions now execute inside the `try` immediately after successful tracing
configuration. Future setup/assertion failures therefore still execute shutdown.
Every original notice, callback count and content-free event assertion remains.

Focused validation on Windows x64, .NET SDK 10.0.400:

- Rebuilt Release and ran `NativePostedCallbackTests`: **8/8 passed**, zero skips.
  Evidence: `posted-after.log` and `posted-after.trx` in the artifact directory.
- Ran every class explicitly assigned `[Collection("Telemetry")]`, including
  the new native-source operation tests, in one fresh process with `--no-build`:
  **153/153 passed**, zero skips, approximately four seconds of test duration.
  The exact 15-class filter is retained in `collection-filter.txt`; results are
  `collection-after.log` and `collection-after.trx`.

No production telemetry change, assertion relaxation, retry, timeout increase, or
GUI invocation was made. `NativeSourceTelemetryTests` is unchanged. These are
focused local fixture/collection results, not a replacement for rerunning hosted
release qualification on both platforms, and do not explain any independently
observed slow macOS job.
