# macOS draw-trace probe: static safety and evidence review

Reviewed: 2026-10-01 on Windows. Scope: the exact early
`--check-native-mac-draw-trace legacy|continuous` route, its probe, reachable
AppKit source draw hooks and local telemetry writer. No Mac execution, workflow
change, production/test edit, staging, commit or dispatch was performed.

## Final decision

**Approve the corrected frozen route for two separate bounded process invocations
on disposable GitHub-hosted macOS x64/ARM runners. No remaining substantive
host-mutation or diagnostic-evidence blocker was found in this static scope.**
Bind approval to the corrected ledger and execution envelope below. AppKit/ABI
and Native AOT execution remain unverified until actual target execution.

## Initial finding, now resolved by targeted re-review

**P1, demonstrated: the final-record contract rejects every healthy orderly run.**
`MacDrawTraceProbe.CheckRecords` requires exactly four JSONL records and a numeric
version on each. `JsonlTraceSink.WriteLoopAsync` additionally appends one
`mote.session` record with empty dimensions before orderly flush/close. Therefore
the intended four endpoint/parent records become five, failing the count before
the attributes check. This is a diagnostic correctness blocker, not an observed
host-mutation hazard. Fix the diagnostic, not the production session contract:
validate one exact session record separately and the four causal records as a
separate set. Preserve uniqueness, parent links, statuses and content-free keys.
The owner and coordinator were notified before any dispatch approval.

Correction at probe SHA-256
`756309B04D1D06C9FEC6C1EB40700560D6A84E5877058917E6C16D1619A17AF0`
now requires five records, separates the unique session record with success,
empty dimensions and null parent, and checks the four endpoint/parent records
independently. All five span IDs are distinct; all endpoints share the session
and trace IDs, contain exactly one numeric version, and their fixed operations,
terminal statuses, child-to-parent and parent-to-session links are exact.
This resolves the demonstrated failure without changing production telemetry.
No target execution was performed during the re-review.

## Reachability and allowed host effects

- Two exact arguments enter the route before configuration loading, ordinary
  controller composition and document/file opening. macOS is required; invalid
  profiles and other hosts cannot enter `Run`.
- One unsaved tiny Engine document, its accepted prefix mutation, immutable
  snapshots and one native shell are owned by this process. No controller or
  open/save API is created or invoked. Theme selection is built-in, not a user
  configuration read.
- AppKit creates, shows and activates the owned window. This transient focus
  change is acceptable only on an idle disposable hosted desktop. Framework/OS
  housekeeping is outside the app's control; this is not a claim of absolutely
  no OS filesystem activity.
- No reachable clipboard read/write, TIS input-source change, TCC/AX permission
  request, screen capture, event tap, external event injection or network client
  is invoked. Shell clipboard methods exist but this route does not call them.
  Close posts only the existing process-local application-defined wake event.
  Continuous stage breadcrumbs optionally read `MOTE_NATIVE_MAC_STAGE_TRACE`
  and print fixed codes; they do not write alternate filesystem locations.
- Explicit telemetry output goes to a fresh GUID directory beneath repository
  `.temp/mac-draw-trace`. Repository markers and nonredirected ancestry are
  checked before telemetry starts. Each created segment is checked before
  proceeding beneath it. No preexisting output is deleted. Concurrent hostile
  symlink swapping is expressly excluded by the cooperative diagnostic boundary.
- Queue capacity 64, one retained file, 64 KiB file limit and four application
  intervals plus session closure are bounded. Callback producers enqueue; the
  asynchronous writer owns file I/O. No user home fallback is reached because
  OutputDirectory is absolute and explicit.
- Normal and managed non-OOM failures cancel the pending interval, close AppKit,
  dispose the synthetic document and request telemetry shutdown. The internal
  20-second failure watchdog exits 124 for a native stall. Abrupt exit cannot
  guarantee managed cleanup or a flushed trace. An external timeout remains
  mandatory; no stalled/terminated process counts as success.

## What the audit can actually establish

The probe uses `setNeedsDisplay:` and AppKit `displayIfNeeded` on its owned source
view. It does not invoke a fabricated `drawRect:` or graphics context. Legacy
success is emitted only after the real NSTextView superclass draw returns,
positive dirty size and unchanged installed generation/version. Continuous
success comes from the production source canvas with a real current CGContext,
positive source body, matching snapshot/frame/binding versions and unchanged
binding/frame identities. Neither preview nor status hooks complete these marks.

`NativeDrawTrace` keeps one interval and uses a ticket plus generation/version.
Supersession produces cancelled version 0; a matching draw may complete version 1
once. Duplicate drawing cannot emit another completion. Wrong-version stimuli
are present, but the final-record audit cannot independently timestamp when a
success occurred: it is NOT a standalone proof that the mismatched draw was
rejected. Parent records are synthetic causal anchors, not parser/controller
publication measurements. Generation 17 is internal, not serialized.

With the corrected audit, the success marker will establish narrowly scoped target
callback wiring and causal record linkage, not pixel completeness, compositor
presentation, physical input latency, performance percentiles, natural input,
IME, VoiceOver, or Native AOT correctness beyond the executed target process.
The telemetry shutdown API has a bounded two-second default and may return on a
writer timeout; static review must not describe that API as unconditional durable
drain. Final record readback is evidence of readable records, not independently
verified durable disk persistence.

## Narrow Program delta: existing Grid/source-NUL approvals

Compared with the prior frozen Program hash
`9DE1750454E973B0FD58EA7F23471E1BB3FA99043E4E75CF7E9CF46F375C8D8A`,
the only working-tree delta is a new six-line exact two-argument route after the
existing exact one-argument Grid and source-NUL checks. Both earlier routes and
their macOS guard, probe call and return are unchanged. The two-argument predicate
cannot shadow either one-argument invocation. The old branches return before the
new branch, so it cannot initialize telemetry or alter their host effects.
MacEditorShell and ObjC hashes still match the source-NUL safety ledger. This is
a delta review only; no unrelated source revalidation or new target acceptance
is implied. The existing Grid/source-NUL route approvals survive this Program
change. The corrected draw probe has its own narrowly bounded approval above.

## Frozen source ledger (initial rejected revision)

| Source | SHA-256 |
| --- | --- |
| `src/Mote.Native/Program.cs` | `A36098B5A62C160BF0B08101E9CD3C2772C8519EF727655DB8497F45788D1176` |
| `src/Mote.Native/Mac/MacDrawTraceProbe.cs` | `E22C912FBD75E9BA867520AE4D25A1649E6538C442010C55153D9181D783403C` |
| `docs/native-mac-draw-trace-probe.md` | `C0BB8F0F13D443AEB3D8467917F592FFABEF434A7B6733F8E3A293F41BBE49B0` |
| `src/Mote.Native/Mac/MacEditorShell.cs` | `68D95BFED79AB7A218A7B0EFCAC29ED183DE17F3CD2FCF724C482430D6A27F7F` |
| `src/Mote.Native/Mac/ObjC.cs` | `ECE56453C8756D81AB5E27ECAA40C79E6DEAB06B27536C7C48D8AF1A28006982` |
| `src/Mote.Native/Mac/Canvas/MacTextInputIsland.cs` | `52225A4F1A56EDE26C4A09300C6711647757C76060C520F7D6D8EC980A3DD729` |
| `src/Mote.Telemetry/NativeDrawTrace.cs` | `35400553CC13635712FD42E0707F584A02FD8EE04111FCCC248FD8593F3000ED` |
| `src/Mote.Telemetry/MoteTelemetry.cs` | `556F2B61C286C2B88E7B55B72DAE3D66C96D0F8940F9B6481BD1821A9E100563` |
| `src/Mote.Telemetry/JsonlTraceSink.cs` | `7B669B14B250FD31379F4A9F4B7DC05486377365F5DA53A4E70F3A592A71CDBF` |
| `src/Mote.Telemetry/TelemetryTypes.cs` | `082B7C282648950A98D54C23395F23B37EDC15F3AD9CBD39AB2E623EF8B17C56` |
| `src/Mote.Engine/Document.cs` | `F85FD585E5342ADDA7DFD8380E83B3BC3234BFAAC682FA3D8DAA014AF3FDF074` |

## Proposed execution envelope after correction and re-freeze

Run exactly these two invocations, separately, from the repository root on each
disposable GitHub-hosted macOS x64/ARM runner, with a three-minute external timeout
per process (in addition to the 20-second internal watchdog):

```sh
./mote --check-native-mac-draw-trace legacy
./mote --check-native-mac-draw-trace continuous
```

Record binary/source provenance, exit code and stdout/stderr. Require exit 0 and
the exact mode-specific success line; no fallback route, retries interpreted as
passing, changed arguments, environment output override, or target execution on
a personal active desktop is covered. A separate three-minute GitHub step/job
timeout for each invocation is acceptable; the internal watchdog still bounds
the probe itself to 20 seconds. A job containing both must not treat an earlier
failure or timeout as success or skip it behind a later passing marker.

## Corrected immutable approval ledger

All shared-source hashes above remain applicable. Replace only the initial probe
and explanatory document hashes with these final reviewed bytes; Program remains
`A36098B5A62C160BF0B08101E9CD3C2772C8519EF727655DB8497F45788D1176`.
Any subsequent change to the approved route or reachable hook/writer behavior
requires a corresponding delta review before relying on this approval.

| Source | SHA-256 |
| --- | --- |
| `src/Mote.Native/Mac/MacDrawTraceProbe.cs` | `756309B04D1D06C9FEC6C1EB40700560D6A84E5877058917E6C16D1619A17AF0` |
| `docs/native-mac-draw-trace-probe.md` | `71257B5D02CDA4CF9FE261CA8256871F83AF8BDEF08C7CF4A7CD55F7FA2BA415` |

The corrected documentation explicitly distinguishes pre-drain sink health,
detached public health and final readable session/causal records. It no longer
uses detached health as proof of durable flush. The public shutdown deadline and
force-exit limitations above remain relevant; neither weakens the narrowly
defined callback-wiring acceptance on a disposable runner.

## Target-failure corrective delta review (2026-10-01)

**Approve the reviewed corrective delta for the same two separate disposable
GitHub-hosted macOS invocations. No substantive static safety or false-success
blocker was found. This is permission to test, not target acceptance.** This
section supersedes the prior probe/document byte ledger only; the original
isolation, external timeout and evidence limits remain mandatory.

### Evidence inspected

- Run [36752189587](https://github.com/kleedaisuki/mote/actions/runs/36752189587),
  retained logs `.cache/ci-36752189587-logs/osx-arm64.log` and `osx-x64.log`, and
  JSONL artifacts `.cache/ci-36752189587-mac-draw-arm/` and
  `.cache/ci-36752189587-mac-draw-x64/`.
- ARM Legacy has five records but version-1 draw status `cancelled`; x64 Legacy
  has five records, version-1 draw `success`, and the exact success marker.
  Continuous on both targets has only its terminal session record and exit 1.
  These non-gating failed diagnostic steps must not be represented as accepted
  merely because their containing jobs succeeded.
- Working-tree production probe/document delta, the shell's `Post`, posted-action
  drain, owned-window lifetime, native binding validation, immutable snapshot
  lifetime, and unchanged `NativeDrawTrace` ticket/completion guards.
- No target probe was run, workflow dispatched, or duplicate build performed in
  this review. No production source was edited, staged or committed.

### Correctness and safety assessment

1. **Continuous setup violation corrected.** The fixed synthetic document uses
   LF-only lines. Taking its first line produces the exact bounded source slice
   at offset zero, with no CR/LF, under `MaxBindingLength`, for both snapshots.
   Full snapshot and frame remain installed for source drawing. This satisfies
   `MacTextInputIsland.Bind` rather than weakening its production validation.
   The old multiline binding fails before either causal mark; artifact shape
   agrees with that failure path, although the old coarse exception output alone
   cannot independently identify the exact throw site.
2. **Deferred close is a justified experiment, not proof of the race.** Each
   `PostAfter` asynchronously waits 250 ms and calls the existing thread-safe
   shell posting API. AppKit layout, installation, drawing and closing still run
   in the main-thread posted selector, not in the worker task. Layout is requested
   outside a layout callback. Close is no longer enqueued immediately into the
   same queue drain as the stimulus; deferred display can run while revision 1
   remains installed. Apple's [layout contract](https://developer.apple.com/documentation/appkit/nsview/layoutsubtreeifneeded%28%29)
   permits an explicit layout request, and its [display contract](https://developer.apple.com/documentation/appkit/nsview/displayifneeded%28%29)
   performs native drawing as needed. The delay cannot guarantee a callback or
   a particular number of event-loop iterations under load. A slow/starved runner
   can still fail closed; do not call the ARM race resolved until target evidence
   arrives. This diagnostic is not a latency benchmark.
3. **Lifetimes remain valid.** `CheckDraw` disposes the synthetic document before
   the deferred close, but its already-obtained immutable snapshots remain valid
   by the Engine contract; shell bindings retain them. The two worker tasks post
   only process-owned actions. External/manual closure is outside the idle hosted
   execution envelope. A failed worker/post or stalled native selector cannot
   manufacture acceptance and remains bounded by the 20-second exit-124 watchdog
   plus external timeout. Abrupt exit still does not guarantee cleanup/flush.
4. **Evidence gates unchanged.** Success still requires one exact terminal
   session and four distinct causal records, exact statuses, numeric-only version
   dimensions, and exact session/trace/parent linkage. Delayed cancellation after
   an actual eligible draw is inert; absent that draw it emits `cancelled` and the
   audit fails. Wrong-version/duplicate protection remains in production hooks;
   this final ledger is not an independent timestamped rejection test. New
   window display requests can trigger additional actual native callbacks but
   cannot create an additional accepted interval or bypass its ticket guards.
5. **No widened host mutation or content output.** New calls operate on the
   owned content view/window; no clipboard, configuration, TCC, input-source,
   external injection or network route was added. Output remains explicit and
   bounded beneath the repository. Private assertion identifiers derive solely
   from fixed in-code contracts; other failures print exception type names only,
   not arbitrary exception messages, document text or filesystem paths. The
   parent records remain synthetic anchors and success means source draw return,
   not physical/compositor presentation.

### Corrective frozen bytes

| Source | SHA-256 |
| --- | --- |
| `src/Mote.Native/Mac/MacDrawTraceProbe.cs` | `7AC992ACC9AC59D8F86EAC5B52900110AB8B73808181FF1149591EB10FD7454A` |
| `docs/native-mac-draw-trace-probe.md` | `425AA07CFEA1A2200A0CA1329E9AD517D2B11F211DD044C4A17E7EAE47B7793D` |

Program and MacEditorShell hashes were independently checked and remain
`A36098B5A62C160BF0B08101E9CD3C2772C8519EF727655DB8497F45788D1176` and
`68D95BFED79AB7A218A7B0EFCAC29ED183DE17F3CD2FCF724C482430D6A27F7F`.
The previous Grid/source-NUL early-route delta conclusions therefore remain
unchanged. Any further approved-source change requires a corresponding review.
