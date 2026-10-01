# Native JSON large acceptance: independent harness review

Date: 2026-10-01. Scope: uncommitted
`benchmarks/NativeJsonLargeAcceptance/{probe.py,MacClient.swift,test_probe.py,README.md}`
and only its new Native AOT matrix steps in `.github/workflows/ci.yml`.
Reused `docs/native-latency-acceptance.md` and the existing NativeAcceptance
artifact/schema/causal auditor. No production, harness, or workflow file was
modified by this review. No repeated native acceptance or complete test run.

## P2: action-trace certification can accept failed or wrongly versioned witnesses

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
spans and conflicting available versions. Implementation owner acknowledged the
finding and this instrumentation distinction; correction is pending at this
review snapshot. Confidence: high.

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
