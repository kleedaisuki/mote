# Review: external Windows screen-latency probe

## Disposition after the 2026-09-30 corrections

The four findings below describe the **initial draft**, not the present source.
I inspected the revised observer, script, README, and repository-local 1/100 MiB
JSONL records. PowerShell parsing and `Add-Type` compilation still pass. I did
not rerun the GUI benchmark or inspect pixel arrays (which are intentionally
not persisted).

| Initial finding | Current disposition | Evidence and remaining boundary |
| --- | --- | --- |
| P1 edited-image attribution | **Closed for the synthetic reversible-source workload** | `Measure-WindowsScreen.ps1:229-282` now verifies exact X/original/X whole-file states through Save, Undo, Redo; `WindowsScreenObserver.cs:124-178` requires a stable, visibly different Undo shape and three later Redo ROI captures matching the timed candidate. A transient blank persisting for the original 100 ms can no longer qualify merely by matching itself. The 1 and 100 MiB local records both have `source_specific_verified=true`, `undo_shape_changed_pixels=1904`, and `redo_max_changed_pixels=0`. This remains an *observed image state*, not direct proof of compositor-present or physical-light time, and is specific to the fixture/ROI. |
| P1 unbounded cross-process dispatch | **Closed for the script's synchronous messages** | `WindowsScreenObserver.cs:361-389` uses finite 3 s `SendMessageTimeoutW` for `WM_CHAR`, `WM_NULL`, `WM_GETTEXT[LENGTH]`, and `EM_SETSEL`; `Observe` joins the sampler after a dispatch exception. The script records `cross-process-dispatch-timeout` and kills/reaps the exact launched child (`Measure-WindowsScreen.ps1:294-325`). This is a static path check, not a forced-hung-window test. |
| P2 categorical target-only privacy claim | **Claim corrected; residual opt-in local risk accepted only with disclosure** | Script header and README now state that five-point ownership sampling does *not* prove all 8,192 ROI pixels belong to mote. Automatic use remains restricted to a disposable GitHub-hosted Windows desktop, while local use needs explicit `-AllowLocal`; no pixel buffer is persisted. A small overlay between checked points remains possible in local mode, so do not automate local runs on an ordinary user desktop or call the capture strictly target-only. |
| P2 accumulated fixture disk use | **Closed for normal case execution** | `Remove-GeneratedCase` checks exact resolved child path and rejects a reparse-point case root before recursive removal; `Invoke-Case` invokes it after process reap in `finally`, reporting `synthetic_source_removed`. Both local 1/100 MiB rows report `true`. A hard process termination can still strand `.temp` data; that is an operational cleanup boundary rather than the original repeated-success leak. |

### Narrow recursive-delete safety follow-up

Root identified that the preceding lexical child check alone did not protect
against a junction in an ancestor such as `.temp` or the per-run scratch root.
The revised `Measure-WindowsScreen.ps1:21-58,93-117,128-131` now checks each
existing ancestor for `ReparsePoint` before creating the run paths, checks
again after creation and immediately before removal, resolves the target and
workspace `.temp` paths, requires the resolved target to remain below `.temp`,
and refuses a reparse-point descendant. The script is PowerShell-parse-clean.
The subsequent repository-local 1 MiB row at
`.cache/benchmarks/native-paint-latency/20142faa298f42dca65a1ea9687a439d/screen-observations.jsonl`
reports `passed-foreground` and `synthetic_source_removed=true`; I did not
rerun that GUI case.

**Disposition:** the identified ancestor-junction escape is closed under the
normal non-adversarial benchmark execution model. There remains a theoretical
check-to-delete replacement race if another actor with write access to this
workspace deliberately swaps a path component between validation and
`Remove-Item`; this script is not a security boundary against a hostile
same-user process. Given the exact generated child, reparse rejection,
post-process cleanup, and disposable hosted runner, that residual is not a
reason to block staging the **non-gating** benchmark. Do not weaken these
guards or run recursive cleanup through a different shell.

**Gate recommendation:** safe to try a **non-gating**, GitHub-hosted Windows run
against a newly published current-source Native AOT binary, with the JSONL
uploaded even on failure. Require every accepted row to have
`status=passed-foreground`, `source_specific_verified=true`, exact Save/Undo/Redo
oracles, quiet negative control, and `synthetic_source_removed=true`; treat
`first_changed_capture_ms` as a screen-copy observation, never as user-key or
photon latency. Do not promote it to a blocking performance gate or a product
p95 until larger fixed-source samples, observer-overhead controls, and target
environment stability are established.

## Initial draft assessment (historical)

Reviewed the untracked `WindowsScreenObserver.cs`, `Measure-WindowsScreen.ps1`,
and `README.md` on 2026-09-30, plus their existing fixture/Win32 helpers. This is
a static contract review, not a rerun of the large GUI benchmark. PowerShell
parsing and `Add-Type` compilation of the three C# helpers both passed locally.

## Findings

1. **P1 — The edited-image endpoint is not source-specific.**
   `WindowsScreenObserver.cs:143-154,177-188` treats any >=128 changed pixels
   as a candidate, ends sampling 100 ms after the first such frame, and calls
   the immediate next copy `settled`. If a blank or unrelated canvas repaint
   persists for >100 ms while the actual edited glyph is still pending, that
   blank can match itself within 64 pixels and be reported as the first edited
   image. The subsequent full-file Save oracle proves eventual source content,
   not content of this earlier frame. The negative control reduces but cannot
   eliminate this failure path, particularly under a backlog after input.
   **Correction:** require a numeric, source-specific glyph canary in the ROI
   (e.g. a precomputed expected X-prefix pattern or a measured shifted-glyph
   signature) before accepting a candidate; retain a separately bounded
   observation interval, and label any timeout/canary miss as inconclusive.

2. **P1 — Cross-process send is unbounded despite the phase timeout.**
   `WindowsScreenObserver.cs:164-173` invokes synchronous `SendMessageW` before
   joining the sampler. If the target GUI thread stops pumping messages or
   deadlocks, the caller never reaches the timeout/join/cleanup path; the
   sampler may stop after `timeoutMs`, but the benchmark process remains hung
   and its JSONL row may never be written. The same mechanism is used for
   `EM_SETSEL` and `WM_GETTEXTLENGTH` in the script/helper. Microsoft documents
   that `SendMessage` blocks until the receiver processes the message and
   provides `SendMessageTimeoutW` for a bounded cross-thread wait
   ([reference](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw)).
   **Correction:** use a checked finite `SendMessageTimeoutW` (no
   `SMTO_NOTIMEOUTIFNOTHUNG`) for each cross-process synchronous message; record
   a distinct dispatch-timeout failure and kill/reap the exact launched child.

3. **P2 — Five ownership points cannot establish target-only capture.**
   `WindowsScreenObserver.cs:279-306` validates four inset corners and the
   center before each 256x32 `BitBlt`. A small foreign overlay elsewhere inside
   the rectangle can be copied into the temporary pixel array and can alter
   the latency result, though no screenshot is persisted. This conflicts with
   the script's categorical “only sampled pixels belong to ... mote fixture”
   privacy assertion. **Correction:** either weaken the claim to “target ROI,
   with five-point occlusion sampling” and restrict runs to a disposable hosted
   desktop, or prove full ROI ownership/mask non-target pixels before accepting
   the capture. The explicit `-AllowLocal` gate is good, but local opt-in does
   not itself make the categorical claim true.

4. **P2 — Allowed repetitions retain several GiB of generated source.**
   `Measure-WindowsScreen.ps1:35-42,77-89,218-227` creates a fresh fixture per
   case/repetition under `.temp` and never removes it. The allowed 30 repeats
   over 1+10+100+50 MiB retain about **4.72 GiB** of synthetic source, before
   the executable and runner overhead. This can exhaust or skew hosted-runner
   disk and makes repeated benchmark runs accumulate. **Correction:** after
   recording the hash and oracle result, remove only each resolved generated
   case directory in `finally`, or explicitly cap retained size and publish
   free-space measurements. Keep report JSONL under `.cache`.

## Supported boundary and positives

- The generated fixture has exact 1/10/100/50 MiB size and a streaming
  SHA-256 suffix oracle; the script checks disk remains unchanged until explicit
  Save. This is stronger than checking an editor title or text length alone.
- Automatic execution is gated to GitHub-hosted Windows; local execution
  requires `-AllowLocal`. `MOTE_HOME` and report/scratch paths stay within the
  repository, and screenshots/document bodies are not persisted.
- The report honestly distinguishes `WM_CHAR` acknowledgement and first
  **screen-DC capture** from a compositor-present or physical-light timestamp.
  Even after the findings above are corrected, this is not physical-keyboard
  input-to-photon latency or a p95 estimate.
- `GetDC` is called and `ReleaseDC` performed on the main thread, while the
  observer uses the DC only between the main-thread baseline and final copy;
  this is consistent with Microsoft's single-thread-at-a-time DC and
  same-thread `ReleaseDC` requirements
  ([reference](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdc)).

The current local pilot therefore demonstrates that the capture machinery can
observe large changed desktop regions, but does not yet establish that its
timestamp is the first image containing the edited glyph. A fixed-current-source
hosted run should follow the endpoint and bounded-dispatch corrections, rather
than using these pilot numbers as a product optimization target.
