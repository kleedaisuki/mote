# Native CSV Grid clipboard acceptance

## Scope and contract

Owned harness: `tests/Mote.Tests/NativeCsvGridClipboardWorkflowTests.cs`.
No production code, workflows, existing tests, user profiles, input sources or global settings are changed.

The harness opens actual CSV fixture files through `NativeEditorController`, installs its
bounded projection into an actual hidden `WindowsCsvGrid` HWND, uses that adapter's current
selection to emit production intents, and lets the controller's background preparation and
UI freshness checks reach `WindowsEditorShell.SetClipboardText`. The production publisher
is bound to the hidden owner HWND. Independent Win32 `GetClipboardData(CF_UNICODETEXT)`,
`GlobalSize` and `GlobalLock` readback checks exact UTF-16 text, without newline normalization.

The dispatcher/source-host interface is a test adapter, not a running full desktop shell.
Private `Select` and `Emit` are invoked for deterministic table selection and explicit
CSV/padded choices; ordinary single-cell copy uses `WindowsCsvGrid.Copy` directly.
This does NOT prove desktop pointer hit testing, context-menu interaction, shortcuts,
continuous canvas integration, Native AOT execution, clipboard contention/recovery or macOS.
No unsupported path is substituted with invented expectations.

| Case | Independent expected result |
| --- | --- |
| Quoted CRLF, tab, doubled quote | Exact decoded `a\r\nb\t"c` |
| Empty final record | `a\r\n""`, not a synthetic trailing newline |
| Missing ragged cell, ordinary CSV | Refusal and unchanged sentinel |
| Explicit padded CSV | `a,b\r\nx,""` |
| Embedded U+0000 | Refusal before publication, unchanged sentinel |
| Payload larger than production cap | Refusal before publication, unchanged sentinel |

Every case checks canonical source text/version, disk contents, modified state,
nonempty canonical source selection, CanUndo/CanRedo and no new undo transaction.
The nonempty selection is fixture setup directly on the canonical navigation object,
not a claim about native source control selection. Clipboard refuses are witnessed by
controller error delivery and unchanged publication-call count, not by a delay alone.
The oversized case waits for proved exact Grid row extent: otherwise idle Full can
legitimately replace the initial visible presentation and retire the queued copy.

## Safety and opt-in

Normal tests execute the fake-publication workflow on Windows. It constructs hidden
HWNDs but **never opens, reads or writes the OS clipboard**. On non-Windows it returns.
The actual workflow returns inert unless `MOTE_DISPOSABLE_GRID_CLIPBOARD=1`; after opt-in,
all remaining failures are hard failures before clipboard access:

- Windows OS;
- `GITHUB_ACTIONS=true`;
- `RUNNER_ENVIRONMENT=github-hosted` (self-hosted forbidden);
- `GITHUB_WORKSPACE` resolves to this exact repository root;
- `.cache/native-grid-clipboard/approval.txt` equals `disposable-github-hosted-windows-only`;
- nonempty `GITHUB_RUN_ID`, `GITHUB_RUN_ATTEMPT`, `GITHUB_SHA`;
- `.cache/native-grid-clipboard/approval-run.txt` matches `<run>:<attempt>:<commit>`.

Environment gates are operator safeguards, not an adversarial sandbox. Never set/spoof them
on a development machine. Actual mode deliberately overwrites the disposable runner's
clipboard and does not claim to restore prior formats. No process-wide clipboard hook,
input injection, registry write, user-profile write or settings mutation occurs.

Actual invocation is permitted only after independent safety review and root approval,
on a dedicated GitHub-hosted Windows job with `timeout-minutes: 5`. Do not expose
the opt-in environment on the normal whole-suite step. The independently reviewed
script/hash and dedicated job are now wired; any later source change needs a new
review before the job can execute a changed harness:

```powershell
# Dedicated GitHub-hosted Windows job, with timeout-minutes: 5.
& ./tests/Invoke-NativeCsvGridClipboardWorkflow.ps1
```

The script refuses local/self-hosted execution before side effects, requires checkout
HEAD to equal `GITHUB_SHA`, requires committed harness/script paths and a clean `src`/`tests`
checkout (including nonignored untracked files). It rejects inherited mutation opt-in,
then builds this exact checkout with warnings as errors while permission is absent.
Only the exact one-test `dotnet test --no-build --no-restore` process inherits permission;
its DLL was freshly built by the immediately preceding successful build.

### Fixed reviewed source representations

Git checkout line-ending conversion is the only admitted representation difference.
Admission uses **two fixed independently reviewed raw SHA256 constants**:

| Representation | SHA256 |
| --- | --- |
| CRLF | `FE4609F6CBCACF6C02AA40B538695B886E268F4BA3E1A4A7934D26C3DC9B4299` |
| LF | `3D3A18BE04C7327869FED237CB4EC7F6E62662981EA812DBB22FD1977C7C04A2` |

LF bytes were generated solely by replacing CRLF pairs with LF in the frozen reviewed file.
No code edits, whitespace normalization, BOM conversion, Git clean filters or mixed endings
are admitted. These constants are not calculated from arbitrary current content.
The report's raw `sourceHash` separately matches whichever fixed representation was admitted.
Any substantive edit requires renewed review and new pinned constants before mutation.

### Report schema and stale-evidence prevention

The script clears only four exact named files beneath the checked repository
`.cache/native-grid-clipboard`: `report.json`, `clipboard.trx`, `approval.txt`, `approval-run.txt`.
No recursive cleanup is used. Permission markers are created only after checks/build and
bound to `<GITHUB_RUN_ID>:<GITHUB_RUN_ATTEMPT>:<GITHUB_SHA>`. On exit, both markers are removed
and the opt-in variable is unset. Upload `report.json` and `clipboard.trx` even on failure.

| Field | Meaning / successful value |
| --- | --- |
| `status` | `running`, `failed`, or required final `passed` |
| `nativeClipboard` | `true` in running/passed reports |
| `runKey` | Exact current run ID, attempt and commit |
| `sourceHash` | Raw SHA256 of admitted actual source bytes |
| `utc` | UTC timestamp; final report must be newer than invocation start |
| `cases` | Exactly six reviewed case identifiers on success |
| `coverage` | Scope limitations, present on success |
| `error` | Exception details on failure |

Success requires process exit zero and fresh identity/hash/time, actual-mode `passed`,
six exact case names and a TRX artifact. Opt-out or zero selected tests cannot satisfy
report verification. The test never creates permission markers itself. Keep the external
job timeout: bounded dispatcher waits do not bound every Win32 call or runner failure.

## Local evidence (2026-10-01)

Windows development machine, .NET 10, command (final run also set `-p:TreatWarningsAsErrors=true`):

`dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --filter FullyQualifiedName~NativeCsvGridClipboardWorkflowTests --logger 'console;verbosity=minimal'`

Fake-publication workflow passed all six cases. Actual workflow opted out and did not touch
clipboard; its ordinary xUnit pass is not native acceptance evidence. Initial oversized-case
failure was a harness race: starting before idle Full installation retired presentation
authority. Waiting for exact certified row extent resolves the harness defect without
changing production freshness checks. No production changes were made to pass validation.
The fake path does not itself establish actual CF_UNICODETEXT behavior.

## Disposable hosted result (2026-10-01)

[CI run 36752189587](https://github.com/kleedaisuki/mote/actions/runs/36752189587),
commit `8671c062cb9dc1c790220a1d3028acad737f7bd6`, passed the separate
`Native CSV Grid clipboard / disposable Windows` job. Its uploaded fresh
`report.json` has `status=passed`, `nativeClipboard=true`, the exact run key,
reviewed CRLF source hash
`FE4609F6CBCACF6C02AA40B538695B886E268F4BA3E1A4A7934D26C3DC9B4299`,
and all six case identifiers; `clipboard.trx` was also uploaded. The artifact
was downloaded only under repository `.cache/ci-36752189587-clipboard/` for
verification. The complete parent CI run also finished with all seven strict
jobs green (Windows/macOS solution tests, four Native AOT RID jobs, and this
dedicated clipboard job). The new macOS draw-trace steps in that same run were
**non-gating and failed on at least one target**; their status must not be
inferred from the parent result. This is actual hidden-HWND production publisher
and independent Win32 clipboard readback evidence, not a desktop context-menu, native AOT,
macOS, contention/recovery or physical input result.
