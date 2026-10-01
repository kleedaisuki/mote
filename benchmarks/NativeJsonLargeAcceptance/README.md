# Ordinary Native AOT JSON root-array acceptance pilot

This is a **non-gating capability and causal-phase pilot**, not a startup SLA,
edit p95 baseline, physical-key test, compositor measurement or release gate.
It runs the actual strict-single-binary `mote <working.json>` product route with
no experimental/legacy/smoke launch flags. No product source is changed.

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python -m unittest discover -s benchmarks/NativeJsonLargeAcceptance -p test_probe.py -v
python benchmarks/NativeJsonLargeAcceptance/probe.py `
  --executable src/Mote.Native/bin/Release/net10.0/win-x64/publish/mote.exe `
  --rid win-x64 --output .cache/native-json-large/NEW-report.json
```

The four hosted native-RID matrix entries run this same contract. Python 3.14 is
selected with native `x64`/`arm64` architecture; a host/RID mismatch is refused,
not executed under emulation. macOS compiles the isolated Swift client before
fixtures/process timing. The [first four-RID hosted run](../../docs/validation/native-json-large-first-hosted.md)
compiled the initial Swift client on both Mac architectures, but both failed
before source acknowledgement. Windows ARM64 and x64 passed the complete 1 MiB
workflow; both 100 MiB Save oracles timed out under an observer that could obstruct
atomic replacement. Updated observer/failure metadata target evidence is pending;
none of these failures is disguised as four-RID acceptance.

## Workload and independent oracles

Each default run uses one **1 MiB control** and one **100 MiB workload**, in that
order, with distinct fresh editor processes, fresh repository-local `MOTE_HOME`,
and fresh GUI reopen processes. The generator reuses
[`NativeAcceptance/acceptance.py`](../NativeAcceptance/acceptance.py)'s valid
many-line root array (fixed objects with string `id`, string `value`, Boolean
`ok`). It replaces **only the first LF with one ASCII space** before launch:
the first editable string has identical native/canonical indices on Windows
and Mac. Subsequent LF rows and trailing whitespace remain byte exact. This is
not the richer duplicate-key/escape corpus from `JsonArrayPages`; zero global
diagnostics is the declared oracle. It covers one narrow ordinary root-array
shape, not all JSON, CRLF, nesting, Unicode or huge string owners.

The immutable source and mutable working copy remain below
`.temp/native-json-large/<exclusive-ID>/`. Corpus generation, initial SHA-256,
the streamed prospective saved SHA-256, compilation and configuration isolation
are outside editor timing. Fixtures are just-written and likely OS-cache-resident;
there is no disk-cache eviction or disk-cold claim. The only source mutation is
one **`r` → `X` replacement at canonical offset 9**, inside the first object's
`id` string. The array remains valid and size-preserving.

The observable sequence is:

1. Same-binary `--check-runtime` successful exit as a **separate launch-control**
   endpoint. It is not editable GUI and is never subtracted from GUI timings.
2. Ordinary GUI launch and native source binding acknowledgement.
3. Current status chrome must show `JSON · Complete · v0` and `No diagnostics.`;
   original and working bytes must still match the pinned input SHA.
4. Exactly one attempted native replacement, source acknowledgement, current
   `Complete · v1` zero-diagnostic status; working disk still unchanged.
5. Ordinary Save dispatch, with the owned native dirty→clean title acknowledgement
   polled **without target file handles**. Only afterwards a full streamed saved
   SHA and exact byte count must match the independent one-byte replacement oracle;
   clean chrome alone never certifies Save.
6. Normal close, successful exit, one successful terminal session, no drops,
   causal parent/version/count audit, successful v0 open and v1 edit **source
   draw callback return** intervals.
7. A fresh **GUI reopen**, source binding, `Complete · v0`, unchanged exact saved
   bytes, normal close and a separate audited no-edit/no-Save terminal session.

The report contains numeric durations/metadata, opaque source/binary/tool hashes,
fixed classifications and causal operation summaries. It contains no document
text, title, source path, raw exception message, screenshot, clipboard data or
input-source identity. Local traces already use mote's content-free schema and
are separately validated by the reused artifact auditor before interpretation.
Original and final working hashes are retained even on failed/blocked samples.

## Driver contracts and exclusions

| Host | Source acknowledgement | Modifying transaction | Explicit limitation |
| --- | --- | --- | --- |
| Windows x64/ARM64 | Exact-PID `MoteNativeEditorWindow`, ordinary canvas and bounded RichEdit island (1–16,384 units), exact synthetic prefix | Owned `EM_SETSEL(9,10)`, one `WM_CHAR('X')`, owned Save command 203, owned normal close | No full-source UIA length oracle; no foreground, physical key, IME or pixel claim |
| macOS x64/ARM64 | One owned window and exactly one `Mote editor` source AXTextArea with full exact character count; bounded AX range/focus metadata | Only `CGEvent.postToPid`: 9 Right key pairs from certified caret 0, acknowledged caret 9; Shift-Right acknowledges range 9:1; one Unicode X pair; Command-S with focused source/caret10 rechecked | Native input host is AX-hidden; no input-host length claim, global posting, app activation, TCC prompt/mutation, VoiceOver or physical display claim |

Mac source selection currently has a getter but no external setter. The guarded
key sequence intentionally exercises its existing ordinary key/responder path
rather than adding a product-only automation interface. `AXIsProcessTrusted()`
and `CGPreflightPostEventAccess()` must already be true; missing capability or
unproven source focus is **blocked**, not worked around. Posting returns no
acceptance result: read-only selection acknowledgements, exact saved bytes,
versioned semantics and traces are the independent outcome witnesses. Close
uses the exact owned window's AX close button, not a global shortcut. PID-specific
delivery does not prove physical keyboard behavior or foreground responsiveness.

Windows messages use pointer-width-correct ctypes ABIs on both native
architectures, only owned HWNDs, and `SendMessageTimeoutW` with a two-second
timeout and `SMTO_BLOCK | SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT`. No broadcast or
`SMTO_NOTIMEOUTIFNOTHUNG` is used. Stale HWND ownership is rechecked immediately
before reads/actions. Native-message acknowledgement is not a physical key.

Every observation stage has a deadline: open/source 60 s, initial Full status
60 s, edit acknowledgement 15 s, edited Full status 60 s, Save byte oracle
60 s, each normal exit 15 s, reopen binding and semantic status 60 s each.
Polling is 50 ms, so parent intervals contain observer overhead/quantization.
Individual Windows calls are at most 2 s; a Mac client invocation has a 6 s outer
subprocess watchdog, 0.15 s per AX message, at most 64 tree nodes/depth 8 and
32 children per node. A final call can overrun an observation deadline by its
own bound. The hosted step has a 20-minute hard watchdog. Runtime control has
15 s and Swift compilation 120 s. On failure bounded owned-dialog/fixed-enum
metadata is retained, then at most one owned normal-close request is attempted
(5 s); unknown dialogs are not dismissed. If necessary only the owned editor
process is killed/reaped (10 s); no modifying action is retried. Forced cleanup is retained
and **cannot certify a normal terminal session**. CI cancellation/hard watchdog
may prevent a report and is an incomplete diagnostic, never success.

## Endpoint interpretation

Child traces retain their own monotonic durations for startup-to-editable,
open-to-editable, open-to-draw-submission, edit-to-draw-submission and Save.
The startup interval begins after configuration; parent launch-to-source includes
launcher/observer overhead. The two clocks are **never subtracted**. Draw records
must have the appropriate causal parent and exact accepted version. Callback
return is not compositor presentation, photons, or a whole-window render.

Tracing is **on** for both sizes; there is no trace-off performance baseline or
claimed tracing overhead correction. One observation per size is a capability
pilot, not a tail distribution; descriptive per-operation small-sample summaries
from the existing auditor must not become a product p95 claim. Repeat-process,
paired off/on, multiple-host and representative workload baselines remain in
[`native-latency-acceptance.md`](../../docs/native-latency-acceptance.md).

The workflow step is `continue-on-error`: inspect the JSON `status`, each sample
`phase/status`, native exit/normal-session evidence, and raw step exit. GitHub's
green job or masked step conclusion does not certify this pilot.

## Completed local evidence (2026-10-01)

- Portable artifact/protocol tests: **12/12** passed, Python 3.14.6, Windows x64.
  Tests do not execute OS input/native processes. They cover exact valid corpus
  and one-byte oracle, wrong edit witness, causal count/version endpoint audit,
  mismatched/cancelled draw rejection, reopen mutation refusal, one-attempt
  failure cleanup and content-free error classification. Independent review
  reproduced an initially permissive trace predicate: a failed edit action or
  conflicting Save revision could be masked by successful draw/Save/terminal
  records. New mutation regressions failed before the fix, then passed. Every
  claimed action/presentation witness must now be successful; native startup/open
  are v0, document.edit is **pre-edit v0**, commit/presentation/draw are v1.
  Engine open/Save/save.completed currently omit revisions: absence remains
  unavailable; a supplied conflicting revision is rejected. This is not a new
  assertion that existing unversioned I/O spans certify v1.
- One local **1 MiB stale-binary harness preflight** passed, without rebuild:
  `.cache/windows-grid-accessibility/aot/mote.exe`, 7,068,160 bytes, SHA-256
  `BE2D17B41853A586F080F4DA3094203C2E9DD0A2AF57FF6B653DDB5B8708D0A0`.
  It predates JSON semantic pages and **cannot certify current product code or
  the 100 MiB parser/native performance**. Exact source, semantic v0/v1, Save,
  normal close/terminal traces and fresh GUI reopen all passed. Artifact:
  `.cache/native-json-large/local-stale-small-pilot.json`; scratch ID
  `3d4a384b9a6841f998eac462295babe6`.
  Parent source acknowledgement was 391.4002 ms; separate child startup-to-
  editable 320.084 ms, open-to-editable 24.494 ms, open draw-return 36.708 ms,
  edit draw-return 17.196 ms. These single noisy instrumented **harness evidence**
  observations are not a new current-source baseline and are not compared across
  OS/size. A subsequent field-only rename changed `edit_actions` to the more
  accurate `edit_attempts`; the retained old-schema artifact is not rewritten.
  The retained original trace was re-audited without another GUI launch after
  predicate tightening; `.cache/native-json-large/local-stale-small-trace-reaudit.json`
  passes the stricter successful-action/version progression contract.
- First-hosted execution: both Windows 1 MiB full workflows passed, including
  actual ARM64 ctypes ABI and independent raw trace audits. Both Windows 100 MiB
  pre-Save source/semantics/edit stages passed, but Save oracle timed out; both
  Mac clients compiled but failed before source acknowledgement. All actual
  pilot steps exited1 despite masked green job conclusions. See the linked
  validation document for exact identities, raw outcomes and timing limits.
- OS-level `ReplaceFileW` discriminator proved an ordinary Python reader can
  deny DELETE sharing and obstruct Save (error32). UI-only dirty→clean polling
  eliminates that observer handle instead of adding a special reader. One
  authorized stale-binary 100 MiB local workflow with the corrected observer
  passed exact Save/normal exit/GUI reopen/terminal trace; artifact
  `.cache/native-json-large/local-stale-large-title-only-pilot.json`. It is not
  current-source/page-performance evidence. Three new tests enforce no target
  read until clean acknowledgement, exact bytes after acknowledgement, and
  sanitized Mac failure metadata retention. Updated hosted outcomes remain pending.

## Grounding and why the boundaries matter

Apple's primary [CGEvent API](https://developer.apple.com/documentation/coregraphics/cgevent)
exposes `postToPid(pid_t)` separately from global `post(tap:)`;
[post-access preflight](https://developer.apple.com/documentation/coregraphics/cgpreflightposteventaccess%28%29)
and [AX trust/message timeout](https://developer.apple.com/documentation/applicationservices/1459345-axuielementsetmessagingtimeout)
support a permission-respecting bounded client. These API contracts do **not**
guarantee hosted trust or key acceptance, so this probe keeps them explicit.
Microsoft documents the system-message marshalling and timeout caveats in
[`SendMessageTimeoutW`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw).
Native Python architecture selection follows
[`actions/setup-python`](https://github.com/actions/setup-python#supported-architectures).

The research lesson is methodological, not a new runtime dependency:
[Mytkowicz et al., ASPLOS 2009](https://research.ibm.com/publications/producing-wrong-data-without-doing-anything-obviously-wrong)
demonstrated substantial setup-induced measurement bias, motivating preserved
binary/workload/environment identities and separate controls rather than a
single convincing aggregate. Recent
[benchkit, ICPE 2026 preprint](https://icpe2026.spec.org/preprint/benchkit_A_Declarative_Framework_for_Composable_Performance_Evaluation_Of_System_Software.pdf)
explores declarative/composable performance experiments; here we adopt the
explicit workload/driver/evidence contract, not its framework. One pilot cannot
replace randomized/paired replicated evaluation, and API-level observation
cannot silently be promoted to a different physical endpoint.
