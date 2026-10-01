# Native source admission: macOS dangling-link cleanup failure

## Hosted evidence

CI run [36887820181](https://github.com/kleedaisuki/mote/actions/runs/36887820181),
source `23f1c1a9bc01f198add7e44df8b026fe1955f923`, completed managed job
[110455526017](https://github.com/kleedaisuki/mote/actions/runs/36887820181/job/110455526017)
(`Test / macos-latest`) failed on macOS 26.6.2, build 25G83, ARM64 image
`macos-26-arm64`, image version `20260907.0351.1`.

The main test assembly reported **3434 passed, 2 failed, 0 skipped, 3436 total**.
Themes reported **14/14** and Configuration **9/9**, with no failures/skips.
The only main failures were:

| Test | Actual failure location |
| --- | --- |
| `DirectoryLinkAncestorIsRejectedIncludingDanglingTarget(true)` | `finally`, old line 93: `Directory.Delete(link, false)` |
| `DanglingOutputLinkIsRejected` | `finally`, old line 110: `Directory.Delete(link, false)` |

Both exceptions were `DirectoryNotFoundException` in
`System.IO.FileSystem.RemoveEmptyDirectory`, naming their owned dangling link.
The rejection and missing-target assertions preceding cleanup did not fail.
Thus this evidence identifies a test cleanup failure, **not failed production
admission of the link**. The real hosted run remains failed; it is not reclassified
as a passing suite.

The completed job log was retrieved while other run jobs were still active via
`gh api repos/kleedaisuki/mote/actions/jobs/110455526017/logs` and retained at
`.cache/ci-36887820181-managed-macos/job.log`, SHA-256
`10C1CEBAEEEEACA0C0AFC6036D1A42D923F61C8103C350FBA075986AE79D8011`.
No run dispatch or restart was used.

## Mechanism and scoped repair

The [.NET 10 Unix filesystem implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Unix.cs)
first attempts `rmdir` for `Directory.Delete`. Its symlink-to-directory fallback
requires a successful directory-target existence check; a missing target instead
produces `DirectoryNotFoundException`. Unix `File.Delete` calls `unlink`, which
removes the link itself without requiring or removing its target.

Only `NativeSourceDiagnosticAdmissionTests.cs` changes. A private fixture cleanup
helper first verifies the known owned path is a reparse point. It uses nonrecursive
`Directory.Delete` for Windows directory links and `File.Delete` for Unix links.
It then checks the parent directory listing no longer contains the link; ordinary
existence predicates alone would incorrectly report a dangling link absent even
before deletion. Target-preservation assertions remain, and are additionally
checked after successful cleanup. Production path validation, failure identifiers,
link creation, privilege handling and skip conditions are unchanged. There is no
catch-and-ignore cleanup exception or weakened product assertion.

All fixture paths are unique descendants of repository `.temp/`. No target or
directory tree is deleted, and no process cwd, environment, privileges, GUI or
global input state is changed.

## Focused local qualification

On Windows x64, SDK `10.0.400`, net10.0, the following actual build/test command
completed with exit **0**, no build warnings/errors, **12/12 passed, 0 failed,
0 skipped**, test duration **128 ms**:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release `
  --filter FullyQualifiedName~NativeSourceDiagnosticAdmissionTests `
  --logger "trx;LogFileName=admission-cleanup.trx" `
  --results-directory .cache/ci-36887820181-managed-macos/qualified
```

Log: `.cache/ci-36887820181-managed-macos/qualified-run.log`.
TRX: `.cache/ci-36887820181-managed-macos/qualified/admission-cleanup.trx`.
All three actual symbolic-link fixtures executed without elevation, including
both dangling cases. Frozen qualification SHA-256 identities:

| File | SHA-256 |
| --- | --- |
| `NativeSourceDiagnosticAdmissionTests.cs` | `2C26C260A60899CEA5BCC227D72AFBBE5F5F99DC6515D77C7A81E70ECCFD22B0` |
| Loaded `Mote.Tests.dll` | `6A8A93940D220690B787ED11C8E44FAB1678A1B53C9363D6CB6C48DDA1B88357` |
| Loaded `mote.dll` | `445B9705451A60DEE7804C3F2D86490AE0660859A6D301FACA4BE440172A591F` |

This qualifies the changed Windows cleanup path and unchanged admission assertions.
The repaired Unix path is grounded in the exact hosted failure and upstream
implementation, but has **not yet executed on macOS**. The next normal exact-source
CI must supply that runtime result. No complete suite rerun or native GUI test was
performed for this repair. Native encoding/workflow failures are separate work.
