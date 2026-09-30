# Natural-close trace-on external Windows startup: independent review

## Scope and decision

Reviewed the frozen delta of `Measure-WindowsOrdinary.ps1`, new
`NativeTraceEvidence.ps1` and `Test-NativeTraceEvidence.ps1`, and the additional
natural-close admission cases in `Test-ExternalFixture.ps1` against checkout
`f5b93002efdf8bc6ab494ae969906d348b46b550` on 2026-10-01.
The existing Python causal auditor and Windows WM_CLOSE implementation were
inspected as supporting contracts. This reviewer changed only this review file;
no staging, commit, production, CI, or SaveDiagnostic changes were performed.

**Decision: no unresolved substantive finding in this bounded change.** Suitable
for integration and a subsequent current-source Native AOT target capability
run. This does not certify any target execution, editable user input, physical
presentation, or performance baseline.

## Frozen raw-file SHA-256

| File under `benchmarks/NativeStartup/` | SHA-256 |
| --- | --- |
| `Measure-WindowsOrdinary.ps1` | `BD1453686F7FE7104F6E4210078FA09058315ED582A1B1CD6945B46FDD5C9D9E` |
| `NativeTraceEvidence.ps1` | `1E86BF9309663AF13DB1B69E8BEAEAD4E462ED4F49B8766F6CA25D706325FA3D` |
| `Test-NativeTraceEvidence.ps1` | `70DD0B13B0259A444E96BD41F8731B08694E2866412EBD98957EA5DC05B1D92F` |
| `Test-ExternalFixture.ps1` | `2EBACE4E6D18A460926BA4B0BD5328C45B4DAF75FF2B176AF15DCC3DFEDB622F` |

Hashes matched the owner's freeze after independent validation. These identify
local bytes, not a line-ending-normalized Git blob. Subsequent changes need
appropriate delta review.

## Material checks

- `NaturalCloseTrace` admits only the pinned external, ReadinessOnly route and
  rejects legacy DiagnosticTrace. Consequently the existing WM_CHAR/Save branch
  cannot run in this variant. Original and per-process copied fixture digests
  are rechecked after normal close before a passed result.
- Each child receives a distinct repository scratch MOTE_HOME and MOTE_TRACE=1.
  Metadata, fixture preparation, and Python version preflight precede the launch
  timer. The ordinary untraced and Markdown generation/edit/Save branches retain
  their previous behavior; report additions do not replace their endpoints.
- The existing external source oracle requires the exact child PID, editor window
  class, bounded source prefix readback and selection acknowledgement. It does
  not infer keyboard editability from selection acknowledgement.
- A configurable bounded sleep (default 1000 ms) offers queued drawing an
  opportunity without UpdateWindow, forced paint, clipboard, focus stealing,
  text edit or Save. Actual observation time is independently recorded.
  An early child exit or dirty title refuses the normal-close path.
- WM_CLOSE is posted to the discovered child editor window, followed by a finite
  10-second exit wait and exit-code-zero requirement. Cleanup kills only the
  exact launched child/tree on failure. Failed reaping preserves scratch and
  marks the sample failed; forced cleanup cannot be reported as normal close.
  Production's WM_CLOSE routes through ClosingRequested rather than bypassing
  document lifetime or normal trace disposal.
- Trace collection occurs after normal exit. Input/output lexical containment,
  all existing reparse-point ancestors, inventory size (1-8 files), exact trace
  filename pattern and 32 MiB per-file cap are checked. Existing output refuses
  overwrite. Copied raw JSONL, manifest and parser summary remain under .cache,
  including a parser failure after copy; scratch retention remains opt-in for
  other failures except unreaped children. This is trusted repository tooling,
  not a concurrent hostile-filesystem sandbox.
- The existing bounded Python parser checks privacy field allowlists, identity,
  causal parent availability/cycles, one successful root session, record loss,
  and exactly one document.open plus one document.open_to_editable. Draws
  additionally require the expected parent operation and matching version.
  The helper independently refuses recorded canonical edit/Save operations.
- A single successful endpoint yields only its child-reported duration converted
  from microseconds to milliseconds. Missing, cancelled, failed, skipped and
  multiple-record endpoints remain separately labelled with null duration;
  cancelled or missing drawing cannot become successful latency. A passed
  source/lifecycle sample with an incomplete endpoint is explicitly distinguished
  from collected successful endpoints.
- Configuration remains null/not-instrumented because trace initialization follows
  configuration. Parent monotonic launch/readiness/close measurements are not
  subtracted from child durations or UTC timestamps. Fresh-process/home labels
  explicitly do not claim cold OS caches. Draw callback return is not physical
  or compositor presentation. Memory peaks are sampled before the observation
  and shutdown portion, not a certified whole-lifetime memory profile.

## Independent validation

Executed only artifact/preflight operations, with all temporary artifacts under
repository .temp/.cache:

- `Test-NativeTraceEvidence.ps1`: **7/7** synthetic artifact cases passed, including
  successful draw duration, cancellation/missing censoring, missing terminal
  session, dropped records, and canonical edit/Save refusal.
- `Test-ExternalFixture.ps1`: **17/17** preflight cases passed, including the new
  natural-close accepted/rejected switch combinations; its dummy executable was
  never launched.
- PowerShell AST parsing: **4/4** scoped scripts parsed without errors.
- Extracted embedded Win32 C# compiled successfully using Add-Type; no imported
  function, native window, editor process, or GUI operation was invoked.

No GUI/native execution, user document, user home, clipboard, input-source,
network, TCC, or production validation was performed. Real normal-close flushing,
ordinary source draw availability, target timing and hosted desktop capability
remain to be established by the separately scheduled target run.
