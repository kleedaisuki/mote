# Native JSON large acceptance: independent harness review

Date: 2026-10-01. Scope: uncommitted
`benchmarks/NativeJsonLargeAcceptance/{probe.py,MacClient.swift,test_probe.py,README.md}`
and only its new Native AOT matrix steps in `.github/workflows/ci.yml`.
Reused `docs/native-latency-acceptance.md` and the existing NativeAcceptance
artifact/schema/causal auditor. No production, harness, or workflow file was
modified by this review. No repeated native acceptance or complete test run.

## Findings

**No unresolved substantive finding after the correction below.**

### Resolved P2: action-trace certification accepted failed or wrongly versioned witnesses

Location: `probe.py`, `trace_evidence`. The reused `acceptance.audit` checks
caller-supplied record counts, terminal success, causal structure and draw-parent
versions, not successful outcomes for every action. The wrapper checks success
only for its selected endpoints and assigns no expected version to Save.

Concrete discriminator: use `ProbeTests.trace()`, change `document.edit.status`
from `success` to `failure`, and change the existing `document.save` version from
1 to 0. `trace_evidence(home, True)` returns `endpoint_integrity: pass`, empty
`issues`, Save version 0 and edit outcomes success=0/failure=1. The experiment
used an exclusive `.temp/native-json-tests/<ID>` directory and removed its owned
leaves afterwards. This is a demonstrated false-positive trace acceptance, not
a demonstration that actual product Save writes incorrect bytes: the independent
saved-byte oracle remains valuable.

Correction: require success for all action/witness records claimed by this pilot;
validate available versions against actual instrumentation semantics. In actual
product traces, `document.edit` describes pre-edit v0 and `edit.committed` the
accepted v1; open/Save completion spans can lack version attributes. Preserve
that absence explicitly instead of inventing version evidence, but reject
contradictory supplied versions (open0/Save1). Add regressions for failed action
spans and conflicting available versions. Implementation owner corrected the predicate and added portable regressions.
Confidence: high.

Independent closure recheck (no native launch): the exact two-field mutation
above now returns `endpoint_integrity: incomplete` and `endpoint_issues` includes
both `document.edit` and `document.save`. Inspection confirms every claimed
startup/open/edit/presentation/commit/Save witness must occur exactly once and
succeed, with required v0/v1 versions and conflict rejection for optional I/O
versions. The new regressions cover individual failed action witnesses,
contradictory versions and explicitly unavailable I/O versions; the owner's
9/9 test result was not redundantly re-run.

Independently re-audited the retained original stale-binary 1 MiB raw trace in
`.temp/native-json-large/3d4a384b9a6841f998eac462295babe6/1/home/traces`:
tightened predicate passes with no endpoint issues. Observed versions were
startup/open-editable/open-draw/edit=0, committed/edit-presentation/edit-draw=1,
and document.open/document.save/save.completed=null. This is reuse of existing
raw evidence, not new native execution or current-source/100 MiB acceptance.

## Other reviewed boundaries

No additional substantive defect was found in the inspected paths:

- Corpus generation emits exact-size ASCII root arrays with legal trailing
  whitespace; replacing first LF by space retains valid JSON and aligns offset9
  on both native hosts. The one-byte in-string oracle is streamed independently;
  Save and fresh GUI reopen have exact size/hash witnesses.
- Windows ctypes uses pointer-width WPARAM/LPARAM/result, target-PID HWND checks,
  bounded system messages, no broadcast/activation, and a single edit attempt.
  Microsoft documents system-message marshalling below WM_USER and timeout
  caveats in [SendMessageTimeoutW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw).
- Mac uses application-scoped AX, read-only bounded metadata, trust and posting
  preflight, only PID-specific key posting, range acknowledgements and an owned
  close button; no global event posting, activation, clipboard or TCC request.
  Apple declarations confirm [postToPid(pid_t)](https://developer.apple.com/documentation/coregraphics/cgevent/posttopid(_:))
  and Boolean [CGPreflightPostEventAccess](https://developer.apple.com/documentation/coregraphics/cgpreflightposteventaccess()).
  API existence is not hosted permission or event-acceptance evidence.
- Child cleanup targets the launched process and distinguishes forced cleanup
  from normal terminal evidence. Observation/tool deadlines and hosted watchdog
  are explicit; no modifying action is retried after failure.
- One-binary inventory rejects linked ancestry and observer/RID mismatch. Reports
  omit source bodies/paths/raw exceptions; uploaded paths include reports and
  traces rather than corpus, working files or configuration directories.
- Isolated workflow is intentionally non-gating, checks raw driver exit and the
  exact two-sample report; masked green conclusions are not acceptance evidence.

## Limits

The Swift client has not been compiled/executed by this reviewer. Windows ARM64
ABI execution and four-RID current-source/100 MiB results remain hosted work.
The prior stale local x64 1 MiB pilot is not current product acceptance. This
review does not certify real IME, physical keys, foreground focus, screen reader
behavior, compositor presentation, disk-cold startup or latency tails. One traced
sample per size is explicitly a capability/causal-phase pilot, not an SLA.

## Scoped recheck: Save observer interference and failure metadata

Date: 2026-10-01. Inspected subsequent uncommitted changes to `save_exact`,
Windows/Mac observations and failure cleanup, Swift guard metadata and three
new portable regressions. **No new substantive finding.** No native launch or
redundant portable suite run was performed. The trace predicate was not changed,
so the previously completed adversarial trace reproduction was not repeated.

The Save observer now requires a witnessed dirty title, dispatches Save once,
and polls native metadata until the title is clean before calling target
`stat`/streamed digest. No target file read remains in the pending-Save polling
loop. The product title contract in `NativeEditorController.BuildView` uses the
same ` •` suffix. Clean title is necessary, not sufficient: exact size/hash and
successful Save/terminal trace remain independent acceptance requirements. This
removes the demonstrated observer-induced Windows DELETE-sharing interference
without weakening the byte oracle. The polling also reads bounded source/status
metadata through the existing driver; "title-only" means file-I/O-free, not
literally a single AX/Win32 attribute read.

Failure cleanup records bounded owned-dialog count/enabled/dirty metadata on
Windows, or the last sanitized Mac report. It makes one owned normal-close
attempt, leaves workload status failed/blocked, does not dismiss unknown dialogs
or retry Save, and retains forced kill/reap if the child stays alive. Mac reports
whitelist fixed status/guard-stage enums and exact numeric/Boolean fields before
retention; titles, arbitrary AX strings and raw exception messages are not
forwarded. Added Swift fields and AXError.rawValue assignment are consistent
with surrounding native client patterns; actual compilation of this changed
Swift source remains hosted validation, not a static-review claim.

The added regressions assert no hash before clean acknowledgement, wrong bytes
cannot pass despite clean UI, and failed Mac guard metadata survives without AX
strings. They are portable contract tests rather than OS sharing/input tests.
Owner reports 12/12 portable cases and one stale-binary local 100 MiB full
Save/reopen/terminal pass; this reviewer did not independently rerun them and
does not promote the stale binary result to current-source four-RID acceptance.
