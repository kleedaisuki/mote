# Windows Grid focus provenance: independent supervisor review

Date: 2026-10-01. Frozen integration:
`db8b8e2bd2f5dd9f325988c54e3e65bd4234f84b`; corrected client pinned at `2d96897`.
Scope: embedded native supervisor in
`tests/Invoke-WindowsGridFocusProvenanceWorkflow.ps1`, portable summary, and three
additive CI steps. The independent client and production focus implementation
have separate reviews; they were not re-audited here.

## Verdict

**No remaining substantive source defect found in this scoped integration.**
Root-identified summary/control contradictions were corrected before freezing;
the initial pin placeholders intentionally denied launch and are now replaced
with independently checked exact LF/CRLF hashes. This verdict permits hosted
validation, not a claim that the supervisor has executed correctly on either
Windows architecture. No test/build rerun, process launch, GUI/input operation,
production edit or push was performed by this reviewer.

## ABI, ownership and cleanup

| Boundary | Checked contract |
| --- | --- |
| 64-bit native layout | Sequential Security 24, BasicLimits 64, Startup 104, StartupEx 112, ProcessInfo 24, Limits 144 and Accounting 48 bytes. Standard input offset 80 and active-process count offset 40 are checked before launch. Windows x64/ARM64 hosted execution remains necessary. |
| Atomic admission | `CreateProcessW` uses `EXTENDED_STARTUPINFO_PRESENT` with JOB_LIST attribute `0x2000D`, not a post-launch AssignProcess race. No breakaway option, foreign parent, global PID acquisition or process-name termination is introduced. |
| Handle inheritance | HANDLE_LIST `0x20002` admits exactly the inheritable NUL/stdout/stderr handles with `bInheritHandles=true`. The job and returned process/thread handles are not inheritable. Explicit application path and CRT argument quoting avoid shell execution/search-path ambiguity. |
| Attribute storage | Native attribute list and job/file arrays survive CreateProcess and remain allocated until DeleteProcThreadAttributeList; arrays are freed afterward. SafeHandle owners remain live through their native use and close deterministically. |
| Owned process tree | New job has KILL_ON_JOB_CLOSE; the client/editor descendants inherit that job. Normal and timeout console controls each create an owned descendant before the one real UIA attempt. Looking up `$PID` retrieves the current supervisor executable path, not a foreign ownership/kill target. |
| Bounded cleanup | A 120-second client wait is followed by a job-active query. Any remaining owned tree is terminated through that job only, then polled for at most ten seconds. Actual queried emptiness, timeout and forced cleanup remain distinct facts. Closing the job also supplies the fail-safe ownership boundary, not an emptiness certificate. |
| Numeric exits | ExitCode is read only after the owned process handle signals and GetExitCodeProcess succeeds. Forced exit 124, unknown/null exit and normal zero are never inferred from report claims or job emptiness. Process/thread handles close on all finally paths. |

The local P/Invoke types and flags were compared with Microsoft's
[attribute-list contract](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute),
[CreateProcessW contract](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw)
and [job accounting structure](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_accounting_information).
The attribute-value storage requirement and restricted handle inheritance are
satisfied by this source construction. Unsupported native operations fail into
fixed supervisor/cleanup classes; they do not trigger another launch or an
unowned PID cleanup fallback. Managed out-of-memory remains fatal policy.

## Truthful classification and resolved follow-ups

The final classifier validates exact client/supervisor DTO keys, architecture,
fixed operation order/receipts/terminal parents, closed pane/outcome/error values,
actual signed-int32 exits/HResults and hash consistency. It projects neither
timestamps nor HWND/PID identities into its summary. Native server traces use the
existing strict schema/privacy reader and explicit receipt/terminal graph;
they are never joined to client operations by nearest time or apparent focus.

Root correctly found that the initial healthy summary omitted identity
certification, cleanup-error, normal-boundary and fixture-hash consistency checks.
Those omissions could have promoted contradictory evidence. Final observed
classification requires the corrected client identity/error invariants, exact
normal boundary and unchanged fixture identity, healthy supervisor facts and no
invalid/unknown server evidence. A valid **unobserved** server remains possible:
complete client observation is not a fabricated adapter receipt or product pass.
F6 `differs` or an observed final SetFocus exception are retained outcomes, not
silently relabeled successful product behavior.

The normal ownership control now requires no ErrorClass; timeout control requires
the exact timeout class plus forced cleanup, queried empty job and actual exit
124. Thus a control cannot ignore a supervisor/cleanup fault merely because some
exit/count values look plausible. Summary CLI creates output exclusively, refuses
unsafe/reused targets, and contains malformed/output errors with fixed outward
classes instead of raw evidence text or traceback. Complete malformed traces are
unknown; censored prefixes preserve positives without absence certification.

## Final pins, retained checks and CI scope

Independently computed both newline forms of the corrected client and project
and confirmed all four exact hashes occur in the frozen supervisor. The original
client/probe is not modified or rescued by this supplemental workflow. Compared
embedded C# text with retained `.cache/validation/windows-grid-focus-workflow/Supervisor.cs`:
the normalized text matches, and actual retained file SHA-256 is
`DEAE4D36C8CED6602EEA0C9BF8A732C5885848B5376B462982E66E31863BAFB8`.

Inspected completed artifacts, without executing them again:

* `summary-tests.log`: **17 tests, OK**, including root-found contradictory-report
  refusals and safe CLI output handling.
* `managed-supervisor-checks.json`: final embedded source compiled; managed layout
  checks and five CRT quoting cases passed; **zero native invocations**.
* `workflow-static.json`: final pins verified; existing workflow definitions
  unchanged apart from the three supplemental steps. Root independently owns
  final YAML/PowerShell integration checks.

CI adds Windows-only, non-gating six-minute diagnostic and two-minute summary
steps, followed by always-attempted complete artifact retention. The summary
fixture run precedes classification; failure cannot be described as product
acceptance. The diagnostic has no retry/rescue, global input/clipboard/activation
mutation or deletion. Build and all synthetic artifacts stay inside checkout
`.cache`. The existing external Grid oracle, pins and all prior gates remain
unchanged. Non-gating job success does not certify this new diagnostic succeeded.

Both hosted RIDs must still establish the actual normal/timeout descendant
controls, one client attempt, independently queried cleanup and real exit codes.
The intended hosted editor creates its owned GUI; this review executed no local
GUI and makes no foreground, physical-keyboard, complete provider-entry or
client-to-server causal certificate. See
[workflow evidence contract](../validation/windows-grid-focus-provenance-workflow.md)
for retained runtime obligations.
