# Native source diagnostic directory admission

## Scope and expected behavior

This verifies only the actual private `NativeSourceCapabilityProbe.AdmitDirectory`
helper through test-only reflection. It does not invoke `Run`, command-line dispatch,
telemetry configuration, native hosts or GUI. Tests do not change process cwd or
runner environment variables. The private helper takes an explicit repository root;
production `Run` passes its actual `Environment.CurrentDirectory`. Tests discover
the actual repository read-only from test assembly ancestors containing `.git`.
Both use the same helper, not a duplicated path-validation implementation.

A new descendant of repository `.cache/` or `.temp/` should be admitted without
creating output artifacts. Existing output files/directories, non-directory
ancestors, lexical prefix lookalikes, normalized traversal outside the allowed
areas and every reparse ancestor (including dangling links) should be refused.
All fixtures and link targets are under repository `.temp/` or `.cache/`.
Known owned links are deleted individually, nonrecursively; targets are never
recursively deleted or redirected outside the workspace.

The entry-point hosted guard additionally checks `GITHUB_ACTIONS`, `RUNNER_OS` and
`RUNNER_ENVIRONMENT=github-hosted`. These are mutable declared runner identifiers,
**not security attestation**. This suite does not change them or claim to validate
that a machine is hosted. Path admission is also not an atomic filesystem sandbox:
a concurrent actor can change ancestors after checking and before creating files.

## Harness discovery and repair

The first coordinated Release build completed with zero warnings/errors. The first
filtered run failed **12/12**, exit **1**, because VSTest starts the testhost outside
the repository root. All failed at the explicit cwd prerequisite **before invoking
admission**. Those results are harness failures, not product failures or admission
evidence. They remain in `.cache/validation/native-source-admission/{run.log,admission.trx}`.
No global cwd mutation, environment override, console helper or native execution
was used to turn those failures into apparent passes.

The root approved a minimal private testability seam, committed as `7b32321`:
`AdmitDirectory(string output, string repositoryRoot)`. `Run` passes the exact
current cwd; the helper normalizes it and resolves relative output against that
root. The same 12 cases were adapted to the two-argument signature. The fresh
`.cache` case supplies a relative output and `.temp` an absolute output. Tests
still discover and verify the real repository, not an invented directory with a
fake `.git`. The matching second Release build completed with zero warnings/errors
in 12.00 seconds (`.cache/validation/native-source-capability/admission-seam-build.log`).

## Qualified execution

On 2026-10-01, Windows 10.0.26200 x64, SDK 10.0.400, net10.0 target:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-build `
  --filter FullyQualifiedName~NativeSourceDiagnosticAdmissionTests `
  --logger "trx;LogFileName=admission.trx" `
  --results-directory .cache/validation/native-source-admission/qualified
```

Observed: **12/12 passed, zero failures/skips**, process exit **0**, duration **84 ms**.
The qualified log, TRX and source/assembly identities are retained separately under
`.cache/validation/native-source-admission/qualified/`.

| Frozen file | SHA-256 |
| --- | --- |
| NativeSourceCapabilityProbe.cs | `7097EF0924FC5F792EB1E9A118EF4613FE8824EAD8B7B2EEB4CA65CCAF3971E0` |
| NativeSourceDiagnosticAdmissionTests.cs | `FAF7BEF905BCD9379C74058F5B2FC93A98272E8C6C33965EB93B396A2C4C1AE3` |
| Mote.Tests.dll | `E6CF68DEB8380DA23400B95F43D15E8188811E488BC4403A322974A863C20E13` |
| mote.dll | `A9DC93829CCE6115C76FEB63463E4216DCE8C97A06D92D86113E4D3D65EE6DFF` |

## Results and limits

Fresh absolute and relative descendants were admitted without creating output or
changing the prepared parent directory listing. Existing output file/directory,
file ancestor, area roots, prefix lookalikes and traversal were refused with their
specific closed failure codes. Actual directory symbolic links were created without
elevation, read back as reparse points and rejected: existing-target ancestor,
dangling-target ancestor and dangling output itself. All three link cases really
executed; **none were skipped**. Owned sentinel files and targets remained unchanged.
On a platform lacking unprivileged link support, these specific cases explicitly
skip instead of changing Developer Mode, privileges or system configuration.

**Verdict:** the tested private directory-admission contracts passed. This does not
prove GUI isolation, hosted-runner provenance, native editing correctness or a
general security boundary. Test-only reflection is not used in the product Native
AOT path. Access-denied and concurrent ancestor mutation races are not simulated.
The existing 33 binding/model cases were not repeated by this task.
