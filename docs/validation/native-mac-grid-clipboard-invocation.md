# Native macOS Grid clipboard invocation integration

Date: 2026-10-01. Independent integration review approved the exact invocation
bytes and detached pins below. The subsequent x64/ARM64 target result is recorded
at the end of this document.
Do not execute locally, spoof GitHub identity, or add inherited clipboard opt-in.

## Detached approval manifest

The following machine-readable pins were independently reviewed against the frozen
script, final Program route and dedicated job. Changed script bytes fail closed;
the approving review is `docs/reviews/native-mac-grid-clipboard-safety.md`.

Approved invocation LF SHA256: A351A2C9F173E2F927030B9B82FFE4B2B7451017D9E046EB547489EE29A9058B
Approved invocation CRLF SHA256: 4055AE25AFE6DFFBC94E474210C2A5BFA86C063BA0935467DFFFB3CAB410B962

Pins are detached because putting a file's hash inside that same file creates a
circular hash dependency. This tracked document is admitted only in an exact clean
HEAD/GITHUB_SHA checkout. It is an operator-reviewed approval manifest, **not an
authenticated sandbox**. Anyone trusted to change the checkout can change policy;
fresh trusted GitHub-hosted runners and code review remain required.

## Job contract

Use a **dedicated fresh** GitHub-hosted job per architecture: `macos-15-intel` /
`osx-x64`, and `macos-latest` / `osx-arm64`. Do not share the runner with another
clipboard writer, restore binaries/build outputs, or set opt-in at job/step level.
The runner must have no personal Apple account or paired personal device. Require
`timeout-minutes: 5`, checkout of the event SHA, .NET 10, PowerShell 7,
and a gating invocation (no `continue-on-error`):

```powershell
& ./tests/Invoke-NativeMacCsvGridClipboardWorkflow.ps1 -RuntimeIdentifier osx-arm64
```

For Intel use `osx-x64`. The invocation rejects a mismatched native `uname -m`.
Upload these repository-local artifacts with `if: always()` and hidden-file
inclusion enabled:

```text
.cache/native-mac-grid-clipboard/report.json
.cache/native-mac-grid-clipboard/stdout.txt
.cache/native-mac-grid-clipboard/stderr.txt
.cache/native-mac-grid-clipboard/exit-code.txt
```

No approval files should remain. GUID-isolated build/fixture directories remain
under the same `.cache` root; no recursive cleanup is performed. They need not be
uploaded. Failures before process launch may have no process artifacts; absence
must never be reported as actual target acceptance.

## Admission and lifetime

1. Reject local/non-Mac/self-hosted execution and inherited Mac/Windows mutation
   opt-in before side effects. Require exact workspace/script/current directory,
   ordinary nonsymlink ancestry, well-formed run identity, HEAD == GITHUB_SHA,
   tracked artifacts, no tracked checkout changes, and clean source/tests/manifest
   including untracked files.
2. Admit only the fixed independently reviewed LF/CRLF probe hashes recorded in
   `native-mac-csv-grid-clipboard.md`, and the detached invocation hashes above.
3. Clear only six named report/approval/stdout/stderr/exit artifacts, after checking
   ordinary paths. Fresh GUID output and intermediate directories prevent selecting
   a cached binary. Publish Native AOT with warnings as errors, strict one-file
   inventory, and only system Mach-O dependencies. Build has no mutation opt-in.
4. Create exact UTF-8 no-BOM/no-newline current-run markers only after publication.
   Pass opt-in solely through `ProcessStartInfo.Environment` to the exact binary
   `--check-native-mac-grid-clipboard actual`; the parent environment never gets it.
5. Drain stdout/stderr asynchronously, require process completion within 110 seconds
   (probe watchdog is 90 seconds), preserve exit/output even on failure, and remove
   markers in `finally`. Five-minute CI timeout additionally covers build/OS hangs;
   runner termination can bypass finally, which is why disposable ownership matters.
6. Require zero exit, exactly one complete actual marker line, ordinary fresh JSON,
   bounded (128 KiB) passed/nativeClipboard boolean/current run key/exact admitted source hash,
   UTC within invocation start/current time, and exactly six reviewed case IDs.

## Evidence and limitations

Frozen invocation hashes approved by the independent static integration review
(not evidence of target execution):

| Representation | SHA256 |
| --- | --- |
| LF | `A351A2C9F173E2F927030B9B82FFE4B2B7451017D9E046EB547489EE29A9058B` |
| CRLF only | `4055AE25AFE6DFFBC94E474210C2A5BFA86C063BA0935467DFFFB3CAB410B962` |

Windows PowerShell static parser accepted this frozen script with zero parse
errors. Independently computed probe LF/CRLF hashes matched both constants.
No script execution was performed, including its refusal route; no environment
was spoofed, and no process received mutation opt-in.

Only static PowerShell parsing is permitted on the Windows development machine;
no invocation, spoofed environment, clipboard access or macOS execution is part of
that validation. Native publish and target acceptance remain pending. This script
does not register Program routes or edit CI. The independent integration review
must cover those root-owned changes as well as approving the manifest pins.

Coverage remains hidden production NSTableView + actual controller + production
NSPasteboard publisher/readback. It does not prove physical input/menu routing,
IME, AX readers, compositor paint, clipboard contention recovery or latency.
General pasteboard publication destroys disposable runner contents; no rollback,
old-content save/restore or Universal Clipboard suppression is claimed. See the
probe validation and independent safety report for the platform evidence.

## Hosted invocation result

Both dedicated Mac jobs in
[CI run 36756839425](https://github.com/kleedaisuki/mote/actions/runs/36756839425)
passed on their native architectures. Root inspected both uploaded
`report.json`, `stdout.txt`, `stderr.txt` and `exit-code.txt` artifacts beneath
repository `.cache/ci-36756839425-mac-grid-{arm,x64}/`. Each independently
built published binary exited zero, printed one exact actual-mode marker,
reported `nativeClipboard=true`, matched the current run key and fixed LF
source hash, and listed exactly six cases; stderr was empty. This validates
the reviewed invocation on two disposable hosted runners, not safe direct
execution on a personal Mac or the desktop interactions excluded above.
