# Release gap audit — strict one-binary product

Audit date: 2026-10-01 (implementation evidence [CI 36836309613](https://github.com/kleedaisuki/mote/actions/runs/36836309613) / `b4093b8`, blocking-gate followup [CI 36837499493](https://github.com/kleedaisuki/mote/actions/runs/36837499493) / `a33c5ca`; current verdict below; historical checkpoints retain their original scope). This is a **moving source audit**, not a claim about a shipped release. `P0` means the hard user contract cannot yet be met; `P1` means substantial verification or integration remains. Owners and decisive experiments are included so a gap can be closed by evidence. Target architecture: [architecture.md](architecture.md); incremental migration: [incremental-plan.md](incremental-plan.md); local-path/theme contracts: [configuration.md](configuration.md), [themes.md](themes.md).

## What is already established

`Mote.Engine` has immutable UTF-16 rope snapshots, CR/LF/CRLF indexing, bounded history, ordered `Changed` events, strict encoding and SHA-256-guarded same-path Save. Explicit overwrite of an existing different target requires `FileOverwriteToken`; ordinary Save As refuses silent replacement ([review and regression evidence](code-review.md)). All six format policies expose structural semantic results and per-document `IFormatSession` implementations; the native controller uses a serialized session driver. This does not imply all large files get `Full` results. `Mote.Configuration` resolves default `~/.mote/{config.toml,cache,data,traces}` with `MOTE_HOME` and config overrides; `Mote.Themes` statically registers dark, light and high-contrast policies. Its focused contrast suite now covers preview roles (**14/14**); that palette test does not certify native chrome or selection. The native shell resolves OS `PrefersDark` at startup **and re-resolves on appearance notifications**; the controller coalesces/defer-applies `system` palette changes without reanalysis, while explicit theme IDs remain fixed. Hosted synthetic Windows and window-local Mac transitions now exercise that runtime path, but actual OS-wide macOS switching and real IME composition remain unverified ([theme audit](themes.md), [validation](../tests/VALIDATION.md)). App-level Save As confirmation and format-aware, source-mapped preview spans are present; Windows styling is debounced into a one-pass update. Unknown valid theme IDs fall back with a nonfatal `CONFIG_THEME` warning. Telemetry defaults to `~/.mote/traces` when opted in, has a bounded nonblocking JSONL sink, and does not accept path/content fields.

**Single-binary and bounded native-workflow milestones achieved, not full-product validation:** Integrated hosted [CI run 36542765032](https://github.com/kleedaisuki/mote/actions/runs/36542765032) passed all six jobs: Windows/macOS solution tests plus Native AOT `Mote.Native` on win-x64, win-arm64, osx-x64 and osx-arm64. Its strict inventory gate accepted **one release executable and zero sidecars** for each RID; runtime and native GUI startup/Launch Services smoke passed. Reported binary sizes for that run are 5.21/5.31/12.14/11.87 MB in that RID order (inventory artifacts are attached). At commit `40bdfd2`, hosted [CI run 36543623820](https://github.com/kleedaisuki/mote/actions/runs/36543623820) again passed all six jobs and added a Windows `dumpbin /dependents` **static-import allowlist gate on both x64 and ARM64**. This closes the former P0 binary-count/static-import feasibility gap. `dumpbin` does **not** prove that dynamic `LoadLibrary` calls only load system DLLs. **Distribution boundary:** the strict CI inventory establishes a lone Native AOT Mach-O and technical GUI/workflow feasibility on hosted macOS; those development binaries have **no publisher Developer ID signature or notarization evidence**. A passing CI launch, `open -a`, or an ad-hoc signature is not an ordinary quarantined user download passing Gatekeeper. Apple requires the separate Developer ID/notary/trust path, with no stapled ticket on the lone binary ([Apple-source distribution analysis](macos-bare-binary-distribution.md)).

The later [hosted CI run 36548638781](https://github.com/kleedaisuki/mote/actions/runs/36548638781) also passed all six jobs and retained four one-file/zero-sidecar inventories. On **Windows x64 and ARM64**, `NativeWindowsWorkflow.ps1` drove the published executable through a real Win32 top-level window and RichEdit: open, `WM_CHAR` edit, Save, close, reopen in a second process, and exact disk/control-text comparison. On **macOS x64 and ARM64**, `NativeMacWorkflow.ps1` used a distinctly **in-process AppKit/NSTextView** probe: insert text and marked `中`, Save As commit, New/discard, reopen, and independent saved-byte comparison. These pass the specified bounded workflows; the Mac probe does **not** establish external keyboard delivery, a real Chinese IME, the file picker, or accessibility. Neither platform workflow covers large-file continuous editing, screen readers, first physical paint, or signing/notarization. The macOS external AX probe in this run failed with an AppleScript `set enabled` variable/property conflict (`-10006`); it is a **probe defect, not evidence of a TCC denial or product AX failure**. See [validation log](../tests/VALIDATION.md).

The frozen next-wave Windows Release solution checkpoint passed **243/243** tests (221 general, 9 configuration, 13 themes), with zero build warnings/errors; it covers sparse continuous-viewport model behavior, idle Full analysis scheduling, JSON exact-key, TOML root array-table, and YAML streaming regressions ([validation log](../tests/VALIDATION.md)). [Benchmark run 36552700169](https://github.com/kleedaisuki/mote/actions/runs/36552700169) passed Windows x64 and macOS ARM64 Native AOT 100 MiB unique-key JSON scans: 7,489,828 distinct keys, `Complete`, zero diagnostics, 204.51 MiB analyzer-thread allocation; one sample took 3.87 s/4.59 s respectively. Windows peak process working set was 428.45 MB; macOS reported `PeakWorkingSet64=0`, an unavailable metric, **not** a zero-memory result. This is not edit-incremental reuse or a p95 GUI latency claim. TOML's restricted root array-table certifier and YAML's bounded stream/canonical summaries also improve coverage/memory on specified corpora; unsupported nested TOML ownership and YAML resource/recovery cases still return `Provisional` ([format investigation](../src/Mote.Formats/README.md)).

The newer [integrated CI run 36553927870](https://github.com/kleedaisuki/mote/actions/runs/36553927870) passed all six jobs with four one-executable/zero-sidecar Native AOT inventories and static import gates. **Both macOS RIDs passed a strict external keyboard workflow**: System Events sent `X` to the focused published app, the probe checked exactly one native text edit, invoked Command-S, compared BOMless UTF-8 bytes, reopened in a fresh process, and verified global Select All/Copy with `pbpaste`. Windows x64/ARM64 RichEdit open-edit-save-reopen gates also passed. Read-only offscreen DirectWrite/Core Text canvas probes passed on all four RIDs. Those probes do not connect the canvas to the interactive editor or prove first physical paint, real IME, screen readers, pointer/file-picker behavior, or continuous large-file editing. Earlier failed strict Mac attempts exposed harness/focus defects; one corrected hosted pass is evidence of this workflow, **not** its reliability distribution.

**Later opt-in canvas checkpoint, not default-product promotion:** The on-screen **read-only** canvas/contrast gate became blocking and passed on all four Native AOT RIDs in [run 36559682956](https://github.com/kleedaisuki/mote/actions/runs/36559682956). A separate `--canvas-experimental` mode connects `ContinuousViewport` to a visible bounded RichEdit/NSTextView input island while the engine retains canonical source and Undo roots; the ordinary editor remains the default. [Runs 36566797297](https://github.com/kleedaisuki/mote/actions/runs/36566797297) and [36567450134](https://github.com/kleedaisuki/mote/actions/runs/36567450134) each passed six strict jobs and separate **non-gating in-process AppKit** clipboard/Delete probes on both Mac ABIs: 40 Ki and 50 Mi-code-unit paste with bounded host and Undo/Redo, LF/CRLF forward/reverse global Delete/Undo, Save As/reopen and unchanged input hashes. The second run's **non-gating win-x64 real-HWND** diagnostic also passed 17 Mi and 50 Mi-unit paste→Undo/Redo→Save/reopen with an 8,196-character native island ([validation details](../tests/VALIDATION.md)). These repeated fixed-fixture passes support promoting those narrow diagnostics to strict CI, but are not external keyboard, real CJK IME, whole-document AX, physical frame latency, or signed/notarized distribution. The prior Mac selection-echo failures remain regression evidence, not a current pass claim.

The later [AX integration run 36572346343](https://github.com/kleedaisuki/mote/actions/runs/36572346343) passed all **six strict jobs** and retained one Native AOT executable per RID. On **both Mac architectures**, in-process AppKit selector/lifecycle checks and an independent external Swift `AXUIElement` client passed source-backed document count, selected/visible and line ranges, bounded offscreen strings, and explicit oversize failure without falsely truncated success. On **both Windows architectures**, external UIA clients passed source-range and stale-object checks. This is genuine target-host API evidence, not VoiceOver/Narrator speech or complete editor accessibility: the baseline Windows canvas in that run exposed a duplicate focused RichEdit Document alongside its source provider, and an ARM64 focus/privacy-button observation remained inconclusive. Keep the opt-in provider separate from default-mode and release acceptance until tree/focus, TextPattern completeness, large-range reader behavior and live IME coexistence pass.

Two later hosted runs sharpen—not close—those boundaries. [CI 36576635105](https://github.com/kleedaisuki/mote/actions/runs/36576635105) passed all six strict jobs and the **external Swift AX Close lifecycle** on both macOS RIDs: 38/38 checks per RID, successful close, a retained old element refusing source text with AX error −25204, normal exit and unchanged fixture bytes. Its first separate, **non-gating external osx-arm64 100 MiB Canvas GUI** attempt timed out in AX-focus readiness *before any key or Save*; the 50 MiB-line case never ran. This is an unverified harness/product boundary, not evidence of either successful GUI editing or a product failure ([validation details](../tests/VALIDATION.md)). [CI 36577240864](https://github.com/kleedaisuki/mote/actions/runs/36577240864) passed all six strict jobs and paired same-published-binary Windows UIA runs per RID. Baseline opt-in Canvas still had **two Documents** in Raw/Control/Content; the *separately opted-in* fragment-root mode had **one source-backed Document** and 19/19 external checks on **both** x64 and ARM64, including input-HWND-to-source routing. ARM64 desktop-global `FocusedElement` was an unrelated OOBE privacy button in both modes despite local focus flags, so global focus truth requires a corrected target-host recheck. Separately, a **local same-binary Windows x64** comparison observed real Microsoft Pinyin marked text/candidate and exact Insert, Esc-cancel, and same-text off-host replacement Save/reopen with the fragment flag on; physical RichEdit focus was sampled during marked text ([input evidence](native-input-accessibility.md), [tree evidence](accessibility-provider-design.md)). Neither result proves Narrator/NVDA speech, simultaneous UIA inspection during composition, macOS IME, or default-mode accessibility. The fragment remains opt-in until these gates pass.

### Hosted Canvas checkpoints — CI 36594818528 through 36598787518

[CI run 36594818528](https://github.com/kleedaisuki/mote/actions/runs/36594818528) **subsequently hosted the revised probe** at `f69a6b0` and completed all six **strict** jobs green; the previous Mac ARM Objective-C font-call ABI crash remained absent in the blocking Canvas clipboard/Delete/AX path. Its distinct **non-gating in-process AppKit horizontal diagnostic** returned `mac-horizontal-workflow-ok` on **osx-x64 and osx-arm64**. Each RID exercised a 16,384-unit row at marker 3,000 and a 52,428,800-unit row at marker 41,943,040, bounded slices (max 16,384), source hit-test/global copy, changed before/after PNG hashes, source/ribbon caret at the reachable minimum, zero visible rows when deliberately forced to zero body, and restored positive body. Both input fixture hashes were unchanged. The x64 initial AppKit active/key flags were false despite a first-responder text view, but became true at the remote step; no *external* key is inferred from the in-process probe. Reports: `.cache/ci-run-36594818528/mac-horizontal-{x64,arm}/ci-inventory/osx-*/horizontal.json` and [horizontal contract](../src/Mote.Native/Mac/Canvas/README.md). This closes the earlier **in-process geometry/horizontal diagnostic**, not default Canvas promotion, trackpad control, real IME or VoiceOver.

The **separate non-gating external macOS ARM Canvas GUI diagnostic** in the same run first passed a Swift source-proxy focus/selection challenge on its 1 MiB control and 100 MiB many-line fixture, then sent one external `X`, Saved and verified every output byte after fresh-process reopen: source length grew by one. On the 100 MiB case, AX source readiness was **3,007.5 ms**, X-to-dirty-title **336.3 ms** and X-to-exact-Save **1,055.7 ms**, all automation-inclusive one-run intervals, not first-paint or p95 measurements. AX visible range changed `0/2176→2113/2240` after native Next Page; this is a source-anchor observation, not pixel proof. Point working set was **305,905,664→324,255,744 bytes**; the peak counter was unavailable. The 50 MiB single-line external case passed Shift+Right and Left source-selection routing, but `X` did **not** produce the expected dirty title within its bounded poll; it stopped **before Save/reopen**, so neither an edit failure nor a successful persistence claim is justified. The source AX length was not checked after that key in this run. A follow-up one-key diagnostic will poll exact AX length/selection separately from the dirty marker before deciding whether Save is safe; it is **not yet target-run** ([external probe contract](../benchmarks/NativeCanvasGui/README.md), retained `.cache/ci-run-36594818528/native-canvas-gui-osx-arm64/.cache/benchmarks/native-canvas-gui-osx-arm64.jsonl`). In all three cases Swift `NSWorkspace` identified mote as frontmost while System Events reported Finder, confirming that System Events `frontmost=false` is not by itself evidence of lost keyboard routing.


[CI run 36596274416](https://github.com/kleedaisuki/mote/actions/runs/36596274416) finished **all six strict jobs green**. Its distinct **non-gating external osx-arm64 Canvas GUI** probe repeated exact external one-key Save/fresh-reopen success for the 1 MiB control and 100 MiB many-line fixture; this is a second bounded hosted observation, not a reliability distribution. For the 50 MiB single line, the source-backed focus and Shift+Right→Left selection challenge passed **before** the one `X`. The revised probe then polled **73 times for 25 seconds** without observing the required source AX length +1/selection `1/0`; the dirty title was also **not observed**. It correctly stopped **before Save/reopen** rather than converting an uncertain edit into a persistence claim. The last helper result has `sourceCandidates=0` and `focusedWindowMatches=false` while the application, workspace-frontmost and focused-window PIDs all remain the **same mote PID**. The focused window therefore did not match the *target file title* at that observation; a same-process modal is a plausible but **unconfirmed** explanation. Its `focusError=−25200` and `selectionError=−25200` are helper **initialization sentinels when no expected-length source proxy was found**, not evidence that AX API calls returned those errors. Keep the failure classification at **external edit unverified**. The next probe should capture content-free actual AX text length and focused-window title/role metadata, plus default-close stage codes, before attributing cause; it must not resend the non-idempotent `X` or Save without renewed exact-source proof. Retained report: `.cache/ci-run-36596274416-canvas-gui/.cache/benchmarks/native-canvas-gui-osx-arm64.jsonl` and [probe contract](../benchmarks/NativeCanvasGui/README.md).


[CI run 36598787518](https://github.com/kleedaisuki/mote/actions/runs/36598787518) at `c6862b6` completed **all six strict jobs green**. Its separate **non-gating in-process AppKit horizontal** probes returned `mac-horizontal-workflow-ok` on both x64 and ARM64: at a 50 MiB one-line source, native `X` made the source length +1 and Undo restored it **before** the established remote pan/source-hit/copy, distinct PNG and unchanged-fixture-SHA checks. This is a native selector/controller path on published Mach-O, not an external user key, physical trackpad or default-editor path (retained `.cache/ci-run-36598787518/mac-horizontal-{x64,arm}/ci-inventory/osx-*/horizontal.json`).

The distinct **non-gating external macOS ARM Canvas GUI** probe passed all **three** cases—1 MiB control, 100 MiB many-line and **50 MiB single ASCII line**—with a source-backed selection challenge, exactly one external `X`, source AX length +1 and selection `1/0`, dirty title, streaming full-byte Save oracle and fresh-process reopen. For the 50 MiB line specifically, AX length was **52,428,800→52,428,801**; X→source AX +1 took **341.7 ms** and X→exact Save **977.3 ms** including automation round trips. The reopened source length was 52,428,801. These are **one hosted process per case**, not a reliability distribution, p95, first editable frame, paint, real IME, VoiceOver or a user-driven horizontal scroll result. Retained `.cache/ci-run-36598787518/native-canvas-gui-osx-arm64/.cache/benchmarks/native-canvas-gui-osx-arm64.jsonl` and [external probe analysis](../benchmarks/NativeCanvasGui/README.md).

The earlier 50 MiB failures remain important **historical root-cause evidence**, but are no longer a current uncorrected external-edit gap for this binary. A later content-free observer in [CI 36597329870](https://github.com/kleedaisuki/mote/actions/runs/36597329870) found the old-length source proxy still present, a target-owned `AXDialog` (two windows), and native `K0/K1→T0/T2→K2→E0/E1` with no `textDidChange` or controller edit. Source-model reasoning explains the veto: the old Mac binding could fill the 16,384-unit native host on a 50 MiB unbroken line, so insertion would require 16,385 units and hit the hard limit. The observed modal was an error callback, not a global AppKit freeze; the earlier −25200 values were helper no-candidate sentinels, not AX failures. Native commit `1e1bcc7` reduced the binding request to **8 Ki** while preserving the **16 Ki** hard host bound, leaving edit slack; the new target-host pass confirms this exact case. It does not yet establish all composition/paste boundary cases, user-driven horizontal navigation or screen-reader behavior ([root-cause analysis](../benchmarks/NativeCanvasGui/README.md)).


**Historical theme checkpoint (before ordinary Continuous became default):** [CI 36672506098](https://github.com/kleedaisuki/mote/actions/runs/36672506098) passed all six **strict** build/test jobs; its separately **non-gating** published-binary theme probes passed a synthetic HKCU `AppsUseLightTheme` dark→light→dark transition on **Windows x64** and a **window-local AppKit** DarkAqua→Aqua→DarkAqua override on **macOS x64 and ARM64** in **both** default and Canvas editor modes. The Mac probes recorded three callbacks, `mote-dark→mote-light→mote-dark`, unchanged document generation/version/selection across palette changes, Undo/Redo and exact source SHA, preview-heading RGB transitions, and differing light/dark PNG hashes that returned to the initial dark hash. The default Mac editor additionally sampled interior heading RGB; the Canvas probe did **not** sample a Core Text source-glyph color. Win x64 checked background/glyph-pixel counts, selection/text stability and registry restoration in its earlier [CI 36671876359](https://github.com/kleedaisuki/mote/actions/runs/36671876359) pass. These scoped results supersede the old “startup-only” theme claim and the preceding Mac Canvas stage-2 probe failure, **not** the remaining actual OS-wide Mac switch, real IME/theme transition, physical compositor presentation, complete native chrome/selection contrast or user-customized high-contrast acceptance ([theme contract](themes.md), [validation details](../tests/VALIDATION.md)).


### Scoped non-gating accessibility audit — CI 36680070533

[CI 36680070533](https://github.com/kleedaisuki/mote/actions/runs/36680070533) completed all six **strict** jobs, but the separate accessibility diagnostics must be read by their artifacts rather than the `continue-on-error` step conclusion. The Windows x64/ARM64 **baseline** external UIA reports each passed **19/19** source-behavior checks and retained the **historical diagnostic-baseline** Raw/Control/Content **2/2/2 Document** blocker (not the current ordinary Continuous route): the focused native RichEdit Document duplicates the source Document. On hosted ARM64, foreign OOBE foreground PID **7584** versus mote PID **6324** makes the baseline host's UIA 30008 `true` a native-host/local-focus observation, **not** a valid global source-focus pass. The paired opt-in fragment reports each passed **19/19**, exposed **1/1/1** source Documents, and had zero behavior/tree blockers. The x64 fragment had mote foreground and source/host UIA 30008 true; the ARM64 fragment had OOBE foreground PID **7584** versus mote PID **6632**, global focus on the OOBE button, and source/host 30008 false through all four client paths. Its one `focus-inconclusive-external-foreground` flag is an honest missing **positive ARM foreground** case, not a new fragment failure. This reproduces the previously documented baseline/fragment distinction and foreground negative control; it does not warrant rerunning completed source-range gates ([provider history](accessibility-provider-design.md)). Exact reports are retained under `.cache/ci-run-36680070533/accessibility-audit/windows-ax-{external,fragment}-win-{x64,arm64}/`.

The **osx-x64 in-process Canvas AX selector/lifecycle** report is a distinct unresolved negative: it reached source selector checks, resize, New and held-element stale-content rejection (`stage-0→1→2→3` by **670 ms**), then waited **90 seconds** at stage 3 after injecting an AX-only fault. It never observed *both* provider detachment and the “Accessibility provider unavailable” status, so the workflow exited 1 without its success marker. The wrapper's `input_sha256_unchanged=false` is a **not-reached comparison**, not evidence that the fixture changed: the wrapper throws on child exit before calculating the hash. In the same CI, **osx-arm64** in-process Canvas AX completed all stages in **1.87 s**, and separately compiled **external Swift AX** source-range/Close probes passed on both Mac RIDs. A prior osx-x64 in-process probe in [CI 36676668596](https://github.com/kleedaisuki/mote/actions/runs/36676668596) reached stage 3→4 and completed. Thus this is a **target-host AX-only fault-recovery/delayed-detach regression or intermittency candidate**, not a proven general AX API/source-document failure and not safely dismissible as a fixture-only error. The current artifact does not separate a lost deferred selector from a detach callback/status mismatch. The next *focused* investigation should record content-free fault-scheduled, detach-entered, provider-null and status-published stages on x64, then compare an unchanged-source target-host run; avoid changing healthy external AX contracts based on this one timeout. Reports/metrics: `.cache/ci-run-36680070533/accessibility-audit/mac-canvas-ax-osx-x64/.cache/ci-inventory/osx-x64/` and the same-run arm64 counterpart. Neither diagnostic is VoiceOver speech or real IME evidence.


**Focused fault-path follow-up, not a replay of completed AX source-range gates:** [CI 36684299172](https://github.com/kleedaisuki/mote/actions/runs/36684299172) was **overall red** because its Windows managed-test job failed; its separate non-gating in-process Canvas AX probes nevertheless passed on **both** Mac RIDs. The x64 fault request returned at **891 ms** with the provider still attached and no unavailable-status notice, then deferred stage 3→4 completed at **1,041 ms** and the whole probe at **1,324 ms**; ARM completed stage 3→4 at **770 ms**. [CI 36685502550](https://github.com/kleedaisuki/mote/actions/runs/36685502550) then finished **all six strict jobs green**, and both Mac AX probes again passed: x64 request-returned **905 ms**, stage 3→4 **1,176 ms**, total **1,509 ms**; ARM stage 3→4 **1,660 ms**, total **2,181 ms**. Both runs produced the exact success marker and independently checked unchanged input SHA. The new content-free request-returned metrics on all four cases show `provider_attached=True`, `status_match=False`, source generation/version/length/hash equal, and editable/focused native input **immediately after queuing** the deferred AX fault. That is the expected pre-detach state; it proves neither that CI 366800's x64 deferred selector was lost nor that its status publication was wrong, because the failing binary lacked these stage observations. The isolated 90-second x64 timeout remains a **non-reproduced fault-recovery intermittency candidate**, not a current repeatable failure or a reason to reopen the healthy external AX source-range/Close checks. If it recurs, capture content-free detach-entry, provider-null and status-publication stages on the failing host; do not infer a cause from the successful request-returned snapshot alone. Reports are retained under `.cache/ax-fault-followup/ci-run-{36684299172,36685502550}/mac-canvas-ax-osx-{x64,arm64}/`.

## Active gates

### Current hosted checkpoint — CI 36874262096 / 3a0552a

[CI 36874262096](https://github.com/kleedaisuki/mote/actions/runs/36874262096)
completes all ten jobs, with actual main suites **3253/3253** on both OSes,
Themes 14/14, Configuration 9/9, four strict one-executable inventories and both
blocking Mac controls. The green aggregate does **not** qualify the new focus
observation or close ordinary Save reliability:

- Both Windows new ownership timeout controls retain a started descendant,
  timeout/forced cleanup and queried empty job, but **null process exit**. The
  strict expected-exit predicate fails before UIA client launch. Client/native
  adapter evidence is **unobserved**, not callback absence; the initial `build`
  phase label is stale even though compilation succeeds. Follow-up `2f2d413`
  shares the original ten-second cleanup budget with process-signaling/exit
  observation and adds the exact `control` phase; fresh hosted proof is pending
  ([counterexamples and correction](validation/windows-grid-focus-provenance-workflow.md)).
- The original external Grid remains **product-fail / exit 1** on both Windows
  RIDs. Supplemental infrastructure neither replaces its oracle nor fixes it.
- Ordinary JSON is **7/8**, not 8/8: Mac x64 100 MiB times out at Save, editor
  **-9**, no reopen. Its 36-row complete trace prefix retains monitor/menu-ready,
  but no candidate/menu-entry/Save request/normal session terminal. The censored
  prefix cannot locate a delivery failure or certify callback absence.

The automatically selected [Benchmarks 36874262133](https://github.com/kleedaisuki/mote/actions/runs/36874262133)
qualifies a separate 20-pair/40-return series for binary `731f76ef…fb362b1`.
The enabled-minus-disabled process-CPU median is **+15.625 ms**, conditional rank
interval **[+15.625, +46.875] ms**; it is not a zero-cost result. Other endpoint
intervals include zero. Different runner CPU and binary prevent pooling or a
cross-version regression conclusion ([scoped audit](performance/causal-trace-overhead.md)).

[Direct user evidence](product/large-file-demand-and-experience.md) puts ordinary
sub-1-MB files and few-MiB reading first. Existing large-file capacity regressions
remain; further giant-format specialization needs task value. Mac current-frame
ribbon-label correction `132e49c` is locally qualified, not part of this hosted
source. A single native editing-locus design is [specified](architecture/ordinary-editing-locus.md),
not implemented/promoted; current label/font fixes do not resolve that product gap.

### Previous clean-tree hosted checkpoint — CI 36863766458 / 5d0fcb6

[CI 36863766458](https://github.com/kleedaisuki/mote/actions/runs/36863766458)
at `5d0fcb6` completes **all ten jobs**. Windows/macOS main suites each pass
**3218/3218**, Themes 14/14 and Configuration 9/9, with four one-executable
Native AOT inventories. Both Mac blocking Flow/ABI/fault controls execute their
actual markers, and ordinary JSON summaries retain **8/8**, numeric editor/reopen
exits 0/0. This integrates the normative TOML/scanner/cache/public-recovery/scalar
source checkpoint and subsequent valid-tree certification. The additional 86 main
tests are retained contracts, not new native TOML GUI/performance coverage
([exact hosted audit](validation/toml-tree-certification.md#hosted-clean-tree-integration--ci-36863766458)).
The next Windows focus-provenance source is not included in this pushed checkpoint;
its local build/portable validation does not supply hosted coverage.

Both Windows architectures actually execute the new blocking owned-HWND pane
checks: **2/2 per RID**, with correct native host/test architecture. Source-first
passive publication and bidirectional pane traversal hold in that bounded test.
The **unchanged external AOT Grid** reports nevertheless remain `product-fail`,
actual exit 1 on both RIDs, with the original physical-focus/scroller/off-owner
assertions. They lack a serialized initial Source→Table or callback-thread
witness; managed/native-HWND success is not external UIA acceptance or a causal
historical fix ([actual reports and scope](validation/windows-grid-accessibility-ci.md)).

The previous source checkpoint `c856816` automatically selected [Benchmarks 36858899230](https://github.com/kleedaisuki/mote/actions/runs/36858899230)
qualifies a separate declared 20-pair/40-indexed-return checkpoint for changed
binary `4d12c303…538a51a8` (7,211,008 bytes). All five endpoint intervals contain
zero; unchanged classifier/hash results and 40 normal-process reports were checked
without repeating the prior raw 60-chain audit. No pooling of the different-binary
series or zero-cost/tail claim follows. The later TOML-only `5d0fcb6` push did
not select another native trace benchmark; this is evidence for its stated prior
binary, not a performance measurement of the current executable
([declared-binary study](performance/causal-trace-overhead.md)).

The following foundational checkpoints retain their original scopes.

The foundation run **CI 36836309613 / b4093b8** has all ten jobs succeed. Actual Windows/macOS strict suites each pass **1369/1369**,
Themes 14/14 and Configuration 9/9, with no failures/skips; all four Native AOT
inventories preserve the one-binary payload. All **eight ordinary JSON Save/reopen
samples** pass exact-byte and captured-version-1 causal contracts with numeric
editor/reopen exits 0/0. All 16 separate synthetic recovery controls pass.

Both Mac RIDs actually execute the global Block ABI/install/remove and primary/
reporter posted-fault continuation controls before Flow-ready. **That Flow step
was non-gating in this run**; raw markers plus the successful script's exit-0
predicate, not a green job alone, support this scoped runtime result. Each of the
four Mac edited traces independently retains input ready/candidate/removed =
1/1/1 and menu entry/returned-true = 1/1; fresh reopen retains ready/removed but
no candidate. Empty-attribute session observations do not create an input-to-menu
or menu-to-request edge. Historical censored failures remain reliability
counterexamples, not repaired by this eight-case pass
([scoped hosted evidence](validation/native-local-input-monitor.md)).

The workflow-only followup **CI 36837499493 / a33c5ca** actually executes and
passes the new blocking Flow/ABI step on both Mac RIDs; all ten jobs and strict
1369/14/9 suites pass. New ordinary reports retain 8/8 and numeric exits 0/0;
unchanged-source raw-chain/recovery checks are reused, not independently repeated.
Its opt-in Windows canvas-baseline still observes duplicate Documents (2/2/2),
and external Windows Grid still fails physical F6/scroller/off-owner focus
contracts despite positive semantic focus; the prior run has the same blockers.
Green jobs do not close these separately non-gating acceptance failures
([precise followup](validation/native-local-input-monitor.md#blocking-gate-execution-followup--ci-36837499493)).

The next performance question now has a scoped result: [Benchmarks 36841148501](https://github.com/kleedaisuki/mote/actions/runs/36841148501)
at `c19f4c6` qualifies one declared **20-pair / 40 indexed driver-return** Windows
foreground study, with independently verified source/screen oracles, normal
numeric exits, and **60** complete native Save chains. Paired synthetic input
acknowledgement median on-minus-off is **+0.0029 ms**, conditional rank interval
**[-0.0310, +0.0379] ms**; all five endpoint intervals include zero. This resolves
the missing equivalent-series evidence for that binary/workload/runner, **not**
zero overhead, tail latency or generalized nonregression. Companion CI
36841148496 passes strict 1369/14/9 and both blocking Mac controls; ordinary
inventories retain 8/8 with numeric exits 0/0
([raw-study interpretation](performance/causal-trace-overhead.md#first-hosted-foreground-paired-study--cibenchmarks-c19f4c6)).

**Product source checkpoint integrated by c856816, not release acceptance:** TOML
now uses normative table ownership and a stateful logical-statement scanner, with
one committed source snapshot plus validated statement summaries for actual edit
reuse. This removes three false Complete control/trivia cases and the blanket
valid parent-array re-entry refusal. The published corpus and independent retained
cache controls are linked in [the model](architecture/toml-statement-semantic-reuse.md).
Historical matched <=8 MiB managed samples show multiline cumulative allocation about
287.27 -> 50.74 MiB after correctness/scanner work, and later full-call edit medians
0.18–0.93 ms in five approximately 5 MiB cached fixtures frozen at `b93c4c2`,
before the later uniform public validator and scalar projection. Cold short-key cache
retention increases to 14.65 MiB and allocation to 215.14 MiB; these trade-offs and
first-repair JIT samples are retained, not hidden or advertised as GUI/AOT latency
([cost provenance](performance/toml-statement-cost.md)). Large malformed-file error
census and arbitrary cardinality remain gaps.

Public TOML Analyze/Format now share the normative model without large-session
budgets. Independent diagnostics survive recoverable seams; invalid-header scope
and failed mutation rollback prevent false later ownership errors. Expanded
source-anchor checks exposed 28 EOF overflows, repaired without weakening the
checks: **801/801** public/reuse entries pass, with nine decoder-only boundaries.
Cached key/primitive projection additionally passes **65/65** including affected
reuse cases (overlapping sets, not additive coverage). Multiline scalar interiors
are now classified from validated spans; long comments and nested collections
remain explicit lexical context gaps. These managed checks do not establish
native GUI latency or arbitrary invalid-file diagnostic completeness
([retained recovery evidence](validation/toml-statement-reuse.md),
[final source review](reviews/toml-uniform-validation-review.md)).

Windows passive Grid publication uses `SW_SHOWNA` instead of an activating show.
Source-first phase checks and complete bidirectional pane-focus tests execute
and pass on both Windows architectures at the current hosted checkpoint. Historical
external `sourceFocus` labels
actually include a Table HWND, and fixed off-owner prose is not a thread witness;
this changes what the old failures establish, not their retained failure status.
The mechanism is reviewed and both owned native controls pass, **not a demonstrated historical external-probe fix**
([focus review](reviews/windows-grid-focus-review.md)).

**Clean-tree implementation integrated by hosted `5d0fcb6`:** `a813852`
removes the second grammar construction only after full read-only tree certification;
all unknown/invalid paths retain the existing uniform recovery. Independent controls
pass **915/915** plus **48/48**, and all **703 decoded** frozen corpus outputs match
exactly including messages, tokens, recursive semantics and Format. A new 50-row
qualified managed comparison reduces valid cumulative allocation **39.9–44.9%**;
retained result medians stay unchanged. Late semantic failure adds **1.368 MiB**
before fallback, while grammar-error allocation remains equal; overlapping invalid
timing ranges do not prove equivalence. This resolves much of the measured valid
public double-parse cost without deleting checks, not arbitrary semantic/GUI
completeness or native latency ([tree evidence](validation/toml-tree-certification.md),
[costs and qualification](performance/toml-statement-cost.md)).

Current work closes Windows Grid adapter-boundary provenance and adds an
independently supervised external observation, without replacing the failing
original acceptance probe or inventing client/server causal joins. Next, quantify
Mac monitor cost and pursue real IME/accessibility coexistence and larger semantic
workloads without replaying the completed Windows study. Enabled Mac monitor cost
remains unmeasured. Mac Grid's
original failing AX reply, Windows ARM inconclusive workflows, historical
Windows AV, arbitrary-format semantic domains, physical presentation and real
IME/reader coexistence remain open. No signing work or Gatekeeper acceptance.

### Historical source verdict — CI 36831903238 / a13a9b0

All ten strict jobs pass; Windows and macOS each report **Mote.Tests 1362/1362**,
Themes 14/14 and Configuration 9/9, zero failed/skipped. Four Native AOT
inventories preserve one executable, zero sidecars and zero bundled libraries.
The queued-Save test now uses an asynchronously observed entry and joins its
owned saves during cleanup, without enlarging its timeout or changing production
scheduling. The posted-only callback guard contains primary and reporting
faults without retrying an action; real created-native fault injection is still
unverified, and normal native regression does not supply it.

**Ordinary JSON GUI acceptance is 7/8 despite green non-gating jobs.** Seven
1/100 MiB samples retain exact saved bytes, unchanged fixtures, fresh reopen,
numeric original/reopen exits 0/0, and complete native Save chains with captured
version 1. macOS x64 100 MiB times out at `save-exact-bytes`, exits -9 after owned
forced cleanup, leaves original disk bytes and has no reopen (numeric exit null).
Its 11,167-byte trace contains **32 valid complete rows**, one menu-ready
checkpoint, no observed menu entry/return or Save request, and no session
terminal. This is **censored**, not evidence of callback nonexecution or a
Save-engine root cause. Sixteen separate synthetic recovery controls pass,
but do not certify document persistence or input delivery.

**The permanent owned-menu boundary now has actual hosted evidence.** Both Mac
RIDs execute its forwarding/BOOL ABI controls. The three passing Mac edited
samples retain independent ready/entry/returned-true checkpoints; the failed
sample retains ready only. Empty-attribute session checkpoints, typed Save
requests and disk/reopen outcomes remain separate evidence. Numeric exits and
fixed menu counts are now retained and rendered by the CI summary, including
the failed sample; the historical eight-case pass is not a current 8/8 claim.
See the [independent raw-artifact audit](validation/native-menu-observation-hosted.md#repaired-strict-suite-and-posted-guard-followup--ci-36831903238).

**Next decisive work:** implement and independently validate the already
designed, trace-opt-in application-local key-down monitor, since the menu-only
failure still leaves the earlier boundary unknown. Preserve one external Save
attempt, identical-event return, no keyboard content, no global monitor,
no invented event/request edges, and the strict one-binary contract. This
second slice is not hosted evidence yet. Mac Grid remains **40/41** with the
original AX error -25205 and normal exits; C0/P0 remain 0/-25205. Windows ARM
Continuous/many-100MiB remain inconclusive. Current tracing overhead still has
zero equivalent inferential pairs; real IME/readers, physical presentation,
arbitrary-format semantics and the historical Windows AV remain open. No
signing/certificate work or Gatekeeper acceptance is claimed.

### Historical source verdict — CI 36824892264 / da3fcb6

[CI 36824892264](https://github.com/kleedaisuki/mote/actions/runs/36824892264)
at `da3fcb688c58d9b08d2443968deb86931d4ae2d2` completes **all ten strict jobs
green**. Windows/macOS solution logs each show **Mote.Tests 1340/1340**, Themes
14/14 and Configuration 9/9, zero failed/skipped. Four Native AOT inventories
retain exactly one executable, zero sidecars and zero bundled native libraries.
The three disposable native clipboard gates reach their reviewed-byte/current-run
checks and pass their six scoped cases. These are integration and payload-shape
evidence, not full product, physical-input or macOS trusted-distribution acceptance
([independent initial and repair audit](validation/causal-observability-hosted.md#follow-up-repaired-strict-contracts-ci-36824892264)).

**Permanent Save provenance now has four-RID hosted acceptance, within its
instrumented endpoints.** All **eight ordinary 1/100 MiB JSON cases** pass exact
saved bytes, unchanged fixtures, normal original/fresh-reopen exit predicates,
and native causal contracts. Independent reclassification of all 16 ordinary
traces finds one complete request in each edited session, saved snapshot version
exactly 1, route-aware Engine phases and local UI completion, normal session
terminals and no observed dropped-record events. Reopen traces do not invent
Save requests. Numeric editor exit fields are not retained in the pilot JSON;
zero exit is an enforced source-level predicate, not a separately retained
numeric field. This is one eight-case capability observation, **not a reliability
distribution or proof that intermittent Mac Save delivery is fixed**.

The separate **16 synthetic recovery controls** (four per RID) also pass:
held nested and typed receipt-only Save/Save As prefixes remain identifiable
after owned child termination, while normal controls retain completed synthetic
graphs. Killed controls remain **censored** with `absence_certified=false`.
These Telemetry-only controls do not execute Engine Save or certify bytes,
zero-loss transport, crash/power-loss durability, OS input delivery or photons.
Tracing remains **default off, local only**, with convention/override-resolved
`~/.mote/traces`, bounded nonwaiting producers, original-sink explicit causality
and writer-only recoverable-prefix flushing
([current contract](architecture/observability-provenance.md),
[endpoint boundaries](end-to-end-tracing.md)).

**The previous failed run and repairs must not be conflated with a Save fix.**
[CI 36823606282](https://github.com/kleedaisuki/mote/actions/runs/36823606282)
at `3f3e59a` had six/eight successful ordinary cases; Mac x64 100 MiB and ARM64
1 MiB retained pre-Save progress but no Save receipt/stages, with forced cleanup
and censored sessions. Missing receipt did not certify nonexecution or OS delivery
failure. That run's strict failures were three reviewed-source pin mismatches
before clipboard launch and a removed established `save.failure.replace` event.
The repaired run validates pin admission and **additive schema-v1 failure-event
compatibility restoration**: the unchanged Windows held-handle control now sees
both `save.commit_replace` failure and `save.failure.replace`, preserves original
bytes/dirty state, and exits normally. Neither repair changes Mac command delivery.
Historical failures remain applicable repeatability evidence
([initial audit](validation/causal-observability-hosted.md#verdict)).

**Remaining non-gating failures stay visible despite strict green integration:**
both original Mac Grid clients still return Swift exit 1, **40/41 checks** and
`AXShowMenu=-25205`, with normal editor exits and unchanged fixtures. Same-client
C0/P0 experiments still observe **AX 0 / -25205** with normal owner/client exits 0;
experiment completion is not product AX acceptance. The initial run's ARM original
Grid **25/26**, guarded downstream refusal and forced cleanup remain historical
evidence, not retroactively replaced by the newer normal-close result. Windows
ARM ordinary Continuous and many-100MiB summaries remain **inconclusive**, not
passes. Real IME/readers, physical presentation, historical Windows AV and
arbitrary structured-file semantic/performance gates remain open.

**Performance verdict is unqualified.** The current causal-tracing local overhead
attempt has **zero equivalent inferential pairs**; timing effect/confidence
interval are unavailable, not zero. Display/workload qualification failures
prevent a no-regression claim. The older frozen-binary overhead result predates
this infrastructure and cannot substitute for it
([current attempt and limits](performance/causal-trace-overhead.md)).

**Next decisive work (Native + Telemetry + Verification/performance):** finish and
independently validate the bounded macOS menu/key-routing observation slice now
in progress, then inspect real hosted ordinary Save artifacts while preserving
one non-idempotent Save attempt, independent bytes and unknown/censored absence
semantics. The new menu-boundary slice is **not included in this validated source
verdict** and has no current hosted acceptance claim. Requalify a matched tracing
off/on workload on a disposable consistently foreground Windows desktop, retain
failed qualifications, and do not retry the live local desktop until favorable
pairs appear. Preserve Grid's original failing reply while choosing a bounded
identity/dispatch discriminator; pursue ARM positive foreground/source focus,
real IME/reader coexistence and honest large-file semantics as separate gates.
The user has no Apple Developer account and requests **no signing/certificate
work**: continue unsigned/ad-hoc strict one-Mach-O technical builds, do not request
credentials, and do not claim Gatekeeper acceptance.

### Historical source verdict — CI 36818175897 / 1171d0f

[CI 36818175897](https://github.com/kleedaisuki/mote/actions/runs/36818175897)
at `1171d0f` completes strict jobs green, but **Mac ordinary JSON acceptance is
3/4, not wholly passed**. The x64 1 MiB sample times out at Save; x64 100 MiB and
both ARM64 sizes pass exact bytes, Complete v0/v1, normal original exit and fresh
GUI reopen. Independent checks confirm six successful GUI trace sessions with
valid terminal/causal/version contracts and zero drops. These Mac runs enable
the original-child-only Save diagnostic; receipt durations must not be pooled
with ordinary performance baselines
([independent workload/transport audit](validation/native-json-large-ci.md#follow-up-viable-original-only-witness-failed-small-case-stays-censored)).

The three successful original children each emit **ready -> selector_entered ->
controller_admitted -> completed**, with healthy completed transport, normal
exit, coherent watermark and no observed overflow/loss. This positively locates
selector and admission boundaries, not the originating physical/posted event or
Save worker/I/O execution. Exact-byte/terminal/reopen acceptance stays independent
([implementation and hosted evidence](validation/mac-json-save-witness-implementation.md#first-actual-hosted-witness-collection-36818175897)).

The failed x64 small case reaches dirty/Complete v1 after one witnessed edit and
one two-event Save attempt with no delivery acknowledgment. Disk stays original,
owned close fails and forced cleanup leaves a 0-byte normal trace and no reopen.
Its collector receives **ready only**, then EOF after kill: no normal exit or
completed watermark. The correct result is **censored**, not healthy transport.
Ready proves initial writer liveness; missing selector/admission frames **do not
prove callback nonexecution**, event loss, failed admission or a product I/O defect.
The instrumentation is not a Save reliability fix. Prior 368119 8/8 success and
later small/large failures remain scoped evidence of unresolved repeatability.

**The same-client C0/P0 comparison now executes successfully as an experiment,
not a product repair.** On both Mac RIDs, one frozen native client returns
**C0=0, P0=-25205** after one action attempt; all four sessions have one admitted
callback, actual menu open/close and normal target/client exit, unchanged fixture
and no forced cleanup. Strict raw parsers accept all four reports. Minimal
preparation reproduces the product failure, so the old Swift client and original
selection-setter bundle are not necessary conditions. P0's external identity/
parent audit remains **unknown** because its unchanged 128-admission budget
exhausts before completion; native current-root/epoch facts do not substitute
for that external graph audit. Getter/dispatcher observation is unavailable.
No Native AOT bridge, wrapper identity or lifetime cause is established
([hosted ledger](validation/mac-grid-showmenu-first-pair.md#scalar-only-hosted-verification-ci-36818175897--1171d0f),
[independent review](reviews/mac-grid-showmenu-discriminator-review.md)).

The unchanged original product Grid probe still fails **40/41 true, AXShowMenu
-25205 / Swift exit 1** on both RIDs, despite guarded downstream/normal exits.
A completed first-pair owner may return 0 for a fully observed failing reply;
its green status is not product AX acceptance. Windows source-range/theme subset
passes remain scoped, ARM global foreground inconclusive, historical Windows AV
unresolved, and no signing work or Gatekeeper acceptance is claimed.

**Next decisive experiments:** separately review a later live transport/producer
boundary for the censored Save case, preserving one Save attempt, independent
byte outcome, target ownership and no activation/global-input/TCC changes; absent
markers must remain unknown unless completeness is established. For Grid, review
a bounded post-reply identity discriminator that can reach the already discovered
product Table without silently expanding the cap; preserve null on exhaustion,
original action failure and separate native/external facts. Reversed-order fresh
controls can test order dependence without retries. Real IME/readers, physical
presentation, disk-cold/trace-off tails and repeatable workflows remain separate
release gates; the user's no-Apple-account/no-signing decision is unchanged.

<a id="current-source-verdict--ci-36815303415--6750cd9"></a>

### Historical source verdict — CI 36815303415 / 6750cd9

[CI 36815303415](https://github.com/kleedaisuki/mote/actions/runs/36815303415)
at `6750cd9a744a8e967b9b1936289ac125a7adebcb` completed **ten strict jobs green**,
but the **non-gating Mac x64 100 MiB ordinary JSON GUI pilot fails Save-outcome
acceptance**. The narrow independent Mac audit checks **3/4 Mac cases**: x64
1 MiB and both ARM64 sizes pass their reported exact Save/reopen/zero-drop
contracts; x64 100 MiB actually exits incomplete / 1. No fresh full Windows or
successful-case deep trace audit was performed for that narrow follow-up
([current raw-artifact audit](validation/native-json-large-ci.md#follow-up-x64-save-timeout-moves-to-100-mib-on-6750cd9)).

The failed large case reaches Complete zero-diagnostic v0/v1, one acknowledged
edit, exact source and unchanged disk before Save. It makes **one Save attempt**
with two PID-posted events and `execution_acknowledged=false`; the clean-title
wait times out, source stays dirty/Complete and disk hash stays original.
Target-active/frontmost/main-window are true, owned-window AXFocused false,
source first-responder focus true, and window count/copy succeed. Owned close
fails, forced cleanup follows, and the sole retained trace is **0 bytes** with
no GUI reopen. This establishes no accepted Save outcome—not Save-handler
failure, event loss, replacement failure, TCC denial or a telemetry-drop recurrence.
Posting, handler receipt and I/O remain distinct unobserved boundaries.

The preceding **36814164862 x64 1 MiB** Save timeout has analogous dirty/Complete,
original-disk/empty-trace evidence while its large case passes. The prior
**36811953139 8/8** success and 16 zero-drop normal sessions remain valid scoped
historical evidence, but failures on different Mac sizes show why neither one
pass nor successful large cases establish repeatable Save reliability. These
are different sources/hosts, not a controlled causal comparison
([small-case failure](validation/native-json-large-ci.md#follow-up-both-mac100-saves-pass-x64-small-save-remains-intermittent),
[prior complete pilot](validation/native-json-large-ci.md#follow-up-four-rid-scoped-pass-on-d9ddda9)).

**The new Mac C0/P0 shared-client discriminator produces no AX causality yet.**
On both RIDs its helper fails typecheck before any session because `Marker`
collides with an SDK typedef; owner reports are probe-error with `sessions=[]`.
The local bounded rename/pin repair has portable validation but is **not yet
hosted**, so no control/product action, identity or admission result follows.
The separate unchanged original product probe still executes on both RIDs and
returns **AXShowMenu -25205 / exit 1**, despite successful guarded downstream
and normal owned exit. Do not transfer those exits to the unlaunched pair or
call the helper compilation defect a product AX failure
([prelaunch failure and bounded repair](validation/mac-grid-showmenu-first-pair.md#first-hosted-execution-ci-36815303415--6750cd9)).

**Next decisive experiments:** rehost the repaired C0/P0 helper with one action
per fresh exact-PID process and original replies intact. Separately review/run
bounded content-free target-owned Save selector-entry/admission/I/O witnesses on
a diagnostic channel that survives forced exit; keep one Save attempt, current
byte/trace predicates, deadlines and no global input/activation/TCC changes.
Absent entry instrumentation must not be relabeled event loss. Preserve failure
and cleanup evidence rather than retrying writes or weakening acceptance. The
Windows source-range/theme subset remains established, ARM global foreground
inconclusive, and historical Windows AV unresolved. Physical paint, real IME/
readers, disk-cold/tail performance, full release and Apple trust remain separate;
the user's no-signing-work decision is unchanged.

<a id="current-source-verdict--ci-36811953139--d9ddda9"></a>

### Historical source verdict — CI 36811953139 / d9ddda9

[CI 36811953139](https://github.com/kleedaisuki/mote/actions/runs/36811953139)
at `d9ddda97cedf98245fd499c8bb6a64cf64919633` again establishes **8/8 complete
scoped ordinary Native AOT JSON GUI cases**, 1 MiB and 100 MiB on win-x64,
win-arm64, osx-x64 and osx-arm64. Independent raw nested-execution/artifact audit
confirms actual pass markers on all four RIDs, exact source/Save/final hashes,
Complete zero-diagnostic v0/v1, one edit, normal initial exit, fresh GUI reopen
and normal reopen exit. **16 distinct GUI sessions have successful terminals,
valid causal/version/endpoint contracts and zero dropped records**; reopen has
no edits/Saves. Portable suites pass 22/22 per RID and separate inventories
corroborate one executable with no sidecars
([independent current-source audit](validation/native-json-large-ci.md#follow-up-four-rid-scoped-pass-on-d9ddda9)).

The preceding CI 36809964231 ARM64 100 MiB Save timeout remains an **unattributed
historical reliability counterexample**, not evidence erased by this pass or a
cause proved fixed. There is no new product Save fix. Mac Save remains one
synthetic PID-posted event pair with `execution_acknowledged=false`; new read-only
activity/window facts do not turn posting into delivery acknowledgment or
authorize activation/global input. Exact bytes, clean title and retained product
traces separately establish this run's outcome. The cache-resident, trace-on,
restricted root-array pilot is not disk-cold startup, p95/tail latency, physical
paint, true IME/readers, arbitrary JSON semantics or repeated-run reliability
certification. Nor do 16 zero-drop sessions guarantee permanent trace reliability.

The new Mac pre-Save snapshots have application-active/frontmost/main-window
true but **owned-window AXFocused false in all four successful cases**. Source
first-responder, application activity and AX window focus are different axes;
AXFocused is not a necessary Save-success guard or a direct key-window witness.
The prior failed run has none of these new fields, so no cross-run activity
contrast or routing cause can be inferred. Read-only instrumentation is not a
Save fix ([routing discriminator](validation/mac-json-save-routing-discriminator.md#first-hosted-routing-observations-36811953139)).

**Mac ShowMenu independent control completes 14/14 variants, while the actual
product still fails.** Seven native AppKit controls on each Mac architecture
return successful external AXShowMenu acknowledgment. Same-run product reports
retain the original sole false `context-menu-accessible` check **-25205 / exit 1**
on both RIDs, despite admitted native callback/open/close and guarded downstream
navigation/selection/retirement/normal exit. The controls do not support tested generic runtime-registration, abbreviated
BOOL metadata or getter/legacy-bridge differences as sufficient explanations; native C control success does not eliminate product-specific Native
AOT/bridge differences or certify product/VoiceOver acceptance
([independent same-host control and product audit](validation/mac-grid-showmenu-action-contract-control.md#first-native-execution-and-independent-audit-ci-36811953139)).

The established two-RID Windows ordinary source-range and synthetic Canvas
live-theme passes remain scoped as below, with ARM global foreground still
inconclusive. Historical Windows preview AV and the user's no-Apple-account/
no-signing-work decision remain open boundaries. No full release, Apple trust,
physical presentation or general accessibility acceptance is claimed.

**Next decisive experiments:** use one frozen external AX action client against
fresh native control and product processes, preserving a single action and
original reply, with content-free retained/current Table identity and bounded
callback-admission witnesses before adding a bridge mechanism. For recurring
Mac Save uncertainty, separate owned handler receipt/admission/I/O from event
posting without resending writes or broadening focus/TCC authority. Repeat exact
byte/zero-drop workflows under controlled fresh-process/shutdown conditions;
keep target activity facts diagnostic, not acceptance substitutes. Continue
positive Windows ARM foreground/IME/reader tests, preserve passing range/theme
oracles, and retain image/RichEdit/failure-sequence evidence for AV recurrence.
Actual screen endpoints and disk-cold/trace-off performance remain separate gates.

<a id="current-source-verdict--ci-36809964231--8d57965"></a>

### Historical source verdict — CI 36809964231 / 8d57965

[CI 36809964231](https://github.com/kleedaisuki/mote/actions/runs/36809964231)
at `8d57965` completed **ten strict jobs green**, including **Mote.Tests 1229/1229
on both Windows and macOS**. Current strict integration remains distinct from
non-gating diagnostic outcomes and full product release acceptance.

**Windows ordinary source ranges and synthetic live theme now pass on both
native RIDs.** The independent external Native AOT COM client establishes exact
source selection `[1,3)` (`bc`), independent range/selection clones, unchanged
source, stale-range refusal and normal owned exit. The original theme probe also
passes **dark -> light -> dark** on x64 and ARM64: all six phase workers preserve
exact source and global `[1,3)` selection, check policy-backed source-body and
foreground raster colors, and retain matching PNG hashes. Both owners verify
exact registry restoration, unchanged disk hash and normal exit. This closes the
previous E_NOTIMPL range obstacle and the **bounded ordinary Canvas synthetic
live-theme gate**, not the entire UIA contract
([range audit](validation/windows-uia-range-external.md#hosted-windows-x64--arm64-audit-ci-36809964231),
[theme audit](validation/windows-canvas-live-theme.md#first-complete-bounded-native-pass-run-36809964231)).

No edits/Undo/Redo occur in the theme probe, so editing-history preservation is
not established. Immutable engine version is explicitly unverified; GetDC and
PrintWindow are not compositor/physical-display evidence. Real IME, screen-reader
speech, physical presentation, every native theme role and repeated reliability
remain separate gates. The adjacent ordinary Windows ARM source-tree diagnostic
still genuinely exits 1 with **foreign foreground / focus inconclusive**, despite
passing all 19 source/tree/lifetime checks: target-owned range selection does not
prove global foreground consistency or turn that diagnostic into a pass.

| Ordinary Native AOT JSON pilot, this run | 1 MiB | 100 MiB |
| --- | --- | --- |
| win-x64 | Complete scoped pass | Complete scoped pass |
| win-arm64 | Complete scoped pass | Complete scoped pass |
| osx-x64 | Complete scoped pass | Complete scoped pass |
| osx-arm64 | Complete scoped pass | Save acknowledgment timeout; no accepted Save/reopen/trace |

**7/8 JSON workflows pass; their 14 normal GUI sessions satisfy the exact trace
contract with zero drops. Mac ARM64 100 MiB remains incomplete.** The failed
case witnesses one edit and dirty/Complete source, then one attempted synthetic
`CGEvent` key-event pair for Save with no delivery acknowledgment. Clean-title
Save polling times out; disk remains the original. Forced cleanup follows, with
a 0-byte retained trace and no GUI reopen. This does not distinguish lost event delivery,
blocked operation or product Save failure; no resend or silently weakened oracle
is authorized. The prior run's 8/8 pass is preserved below as a **single scoped
success**, not a permanent reliability certificate. The current failed Save
cannot be called a telemetry-drop regression without a retained drop witness
([independent run evidence](validation/native-json-large-ci.md#follow-up-mac-arm100-save-observation-fails-on-a-later-green-run)).

Mac Grid's original **AXShowMenu -25205** acknowledgment boundary, historical
Windows preview AV and the user's no-Apple-account/no-signing-work decision are
unchanged. Neither the Windows theme pass nor prior JSON capability success
supplies Mac Grid whole-external acceptance or Gatekeeper trust.

**Next decisive tests (Native desktop + Verification/CI):** instrument bounded,
content-free owned Mac Save-command receipt/controller progression separately
from attempted event posting and independent disk/title outcomes; preserve the
single-attempt rule and unchanged failure evidence. Repeat exact-byte/zero-drop
workloads across fresh processes and controlled shutdown stress without inferring
p95 or reliability from isolated passes. Obtain positive target-owned Windows ARM
foreground/source focus in a suitable host; exercise real IME/reader coexistence
and actual system appearance transitions during composition. Keep the existing
successful source-range/theme oracles intact while expanding Unicode/stale-range
and target-native coverage. Continue the Mac AXShowMenu reply discriminator and
Windows image/RichEdit/failure-sequence inventory; actual screen endpoints and
matched disk-cold/trace-off performance remain separate experiments.

<a id="current-source-verdict--ci-36806841387--48a3711"></a>

### Historical source verdict — CI 36806841387 / 48a3711

[CI 36806841387](https://github.com/kleedaisuki/mote/actions/runs/36806841387)
at `48a371108a67370bc46cea52299dc5c6561e9cc5` establishes the **first complete
single-run ordinary Native AOT JSON root-array capability pilot on all four
RIDs**. Independent artifact and nested-execution audit—not containing green
job conclusions—confirms **8/8 scoped GUI cases pass**:

| Ordinary Native AOT JSON pilot | 1 MiB | 100 MiB |
| --- | --- | --- |
| win-x64 | Complete scoped pass | Complete scoped pass |
| win-arm64 | Complete scoped pass | Complete scoped pass |
| osx-x64 | Complete scoped pass | Complete scoped pass |
| osx-arm64 | Complete scoped pass | Complete scoped pass |

All cases retain immutable original bytes, unchanged working disk before Save,
Complete zero-diagnostic v0/v1, one witnessed edit, exact Save/final hashes,
normal initial exit, fresh GUI reopen and normal reopen exit. **16 distinct GUI
sessions** have successful terminal roots, valid causal/endpoint versions and
parents, expected action counts and **zero dropped records**. Reopen sessions
have no edits or Saves. Portable protocol/artifact tests pass 21/21 per RID;
four separate inventories corroborate one executable with no sidecars.
The acceptance predicates were not relaxed
([exact independent audit](validation/native-json-large-ci.md#follow-up-first-complete-four-rid-ordinary-json-pilot)).

This source includes bounded admitted-producer telemetry shutdown `ea3035f`.
The prior Mac x64 1 MiB saved-byte success with one dropped record in run
36804628122 was correctly refused before reopen; the new run supplies the
missing zero-drop trace and actual reopen. **One successful hosted run does
not prove permanent trace reliability or eliminate every shutdown/admission
race**; the mechanism and directed evidence remain separately scoped
([shutdown review](reviews/trace-drop-normal-exit.md)). Earlier Save/AX failures
and the prior drop witness remain historical evidence, not current failed cases
or retroactively accepted results. Mac Save still uses synthetic target-process
event posting without delivery acknowledgment; exact resulting bytes and
independent status/trace witnesses establish outcome, not physical-key input.
This trace-on, cache-resident, restricted root-array pilot is **not p95/tail
latency, disk-cold startup, physical paint, true IME, reader acceptance, general
JSON coverage, repeated-run reliability or full mote release acceptance**.

**Windows ordinary Canvas theme now exposes a product range gap, not a palette
defect.** On both Windows RIDs, the corrected observer resolves exactly one
owned source Document, TextPattern, exact 11-unit LF fixture and visible native
Canvas/input. Source-child readiness passes. The next operation
`MoveEndpointByRange(End, sourceRange, Start)` fails: the ordinary source
provider explicitly returns **E_NOTIMPL (0x80004001)**. Nearby standard range
comparison/movement/Select operations also remain stubs. The actual workers
exit 1 before source selection, theme notification or raster sampling; exact
registry restoration, unchanged fixture and normal close pass. No theme
transition or observed palette defect follows from that failure. The product
range-contract work is assigned to Native desktop + Accessibility; replacing
source-range selection with local RichEdit selection or weakening the probe
would evade the actual contract
([hosted evidence and implementation boundary](validation/windows-canvas-live-theme.md#source-child-correction-hosted-audit-run-36806841387)).

The **Mac Grid AXShowMenu -25205** whole-external-acceptance boundary and the
**unresolved historical Windows preview access violation** remain as scoped in
the prior checkpoint below. This JSON pass does not close either. The user's
no-Apple-account/no-signing-work decision remains unchanged; technical one-file
builds do not establish ordinary Gatekeeper-trusted distribution.

**Next decisive tests:** retain the existing exact-byte/zero-drop predicates
and repeat the four-RID pilot across fresh processes and bounded producer/shutdown
stress, measuring trace-off/on separately rather than inferring reliability from
one run; add matched disk-cold startup and sustained edit/scroll distributions
with actual screen endpoints. Complete the coherent ordinary source TextPattern
range contract and exported Native AOT COM path, independently testing same-owner
and stale/foreign range identity, endpoint crossing/collapse, Unicode Character
movement, selection routing and unchanged source bytes, then rerun the unchanged
native theme diagnostic through all three palette phases. Continue discriminating
Mac AXShowMenu acknowledgment while retaining guarded downstream and original
failure; preserve Windows image/RichEdit inventory and collect failure sequence/
native fault evidence if AV recurs. Real IME/readers remain separate target-OS gates.

<a id="current-source-verdict--ci-36802378381--c453506"></a>

### Historical source verdict — CI 36802378381 / c453506

[CI 36802378381](https://github.com/kleedaisuki/mote/actions/runs/36802378381)
at `c453506c84e249dbff7c73748140314f6760ff33` completed **ten strict jobs green**,
including **Mote.Tests 1158/1158 on both Windows and macOS**, Themes 14/14 and
Configuration 9/9. Four single-binary Native AOT RID gates and dedicated native
clipboard gates remain established. This verdict is bounded to that source and
hosted run: **green strict integration is not full release acceptance**, and
nested non-gating failures below remain failures regardless of their containing
job/step color. Later unhosted worktree candidates are not included.

| Independently audited native JSON pilot | 1 MiB | 100 MiB |
| --- | --- | --- |
| win-x64 | Full scoped pass | Full scoped pass |
| win-arm64 | Full scoped pass | Full scoped pass |
| osx-x64 | Save timeout after witnessed edit and Complete v1; disk unchanged, retained trace 0 bytes | Full scoped pass |
| osx-arm64 | Full scoped pass | External AX window-copy -25204 before any edit; internal Full succeeds, owned cleanup exits normally |

The **six successful cases** satisfy the ordinary-route source/Complete v0/v1,
one witnessed edit, exact-byte Save, normal exit, fresh GUI reopen and audited
causal/terminal-trace contract. Both Windows two-size pilots pass; both Mac
pilots remain **incomplete / actual exit 1**, with different size-specific
failures. Mac ARM64 100 MiB first bound the exact source, then had successful
window count but failed window copy: this is not absent windows, the earlier
count-readiness failure, permission denial, or an observed parser failure.
Its 14-record terminal trace independently proves successful v0 Full analysis,
not the unexecuted edit/Save/reopen. Mac x64 1 MiB provides no accepted Save or
normal exit: unchanged disk and empty trace do not discriminate lost Command-S,
a product Save failure, or a blocked operation. Neither workflow resends writes
([independent three-run audit](validation/native-json-large-ci.md#follow-up-bounded-initial-ax-count-readiness),
[prior Windows acceptance and Mac guard evidence](validation/native-json-large-second-hosted.md)).

- **Windows native preview crash remains unresolved:** the earlier strict run
  36800944850 aborted with `0xC0000005` at `SetWindowTextW` during preview/theme
  import on hosted image **20260925.250.1**. This new full-suite pass used
  **20260922.246.2**, the earlier passing image, with test-only owner-thread
  preconditions and environment recording. It is a successful guarded run,
  **not a causal production fix or validation on the crashing image**. No crash
  sequence or native dump was retained from the successful run. Retain image
  and RichEdit identity; a recurrence should correlate the failure sequence and
  native fault evidence before choosing a production change
  ([investigation and exact image/DLL comparison](validation/windows-preview-access-violation.md)).
- **Mac Grid guarded downstream succeeds; original action still fails:** both
  external clients retain the sole `AXShowMenu` acknowledgment failure **-25205**
  and overall **exit 1**. Independently guarded downstream observes the owned
  menu, exact numeric prompt/jump, absolute value/selection, retired old nodes
  and normal exit without forced cleanup on both RIDs. This corroborates the
  conditional workflow, not the primary action reply or whole AX acceptance
  ([fresh corroboration](validation/mac-grid-table-proxy.md#fresh-integration-corroboration-ci-36802378381)).
- **Windows ordinary Canvas theme reaches native targets but not theme action:**
  both corrected workers fail their first source-readiness predicate before
  source selection, target notification or raster sampling. Exact registry
  restoration, unchanged input and normal owned shutdown pass. No product
  palette defect or accepted live transition is observed; the compound predicate
  lacks per-conjunct last values, so cause remains unclassified
  ([hosted audit](validation/windows-canvas-live-theme.md#corrected-hosted-audit-run-36802378381)).

**Next decisive experiments (owners: Native desktop + Verification/CI):**
separately record bounded read-only AX window-copy readiness and owned ordinary
Save-command delivery/status on macOS, without resending non-idempotent input,
raising deadlines to hide a failure, or substituting internal Full for external
acceptance; record each Windows theme precondition's content-free last witness
before attempting theme mutation; retain Windows image/RichEdit inventory and
failure-only test sequence, obtaining native fault evidence if AV recurs; isolate
Mac AXShowMenu reply semantics while preserving the original failure and guarded
downstream contract. These experiments target observed boundaries, not broad
speculative architecture changes. Startup/physical paint, latency tails, real
IME/readers and arbitrary structured-file semantics still need their own gates.
The user's no-Apple-account/no-signing-work decision is unchanged.

**Historical checkpoint (2026-09-30; read with the current verdict above):**
[CI 36727036440](https://github.com/kleedaisuki/mote/actions/runs/36727036440)
passed all six strict jobs after the bounded typed Markdown Flow subgraph landed;
both published Mac RIDs also emitted the exact non-gating AppKit Flow marker.
Flow now retains precise ordered inline text and source origins, Windows imports
generated RTF separately from literal fallback text, and Mac installs attributed
text directly. Plain text defaults to source-only while an explicit preview
preference can override policy convention. This closes neither CSV's native Grid,
safe local images, physical Copy/IME/reader acceptance nor GUI latency
([implementation and evidence](native-flow-implementation-contract.md)).

[CI 36732329913](https://github.com/kleedaisuki/mote/actions/runs/36732329913)
then passed all six strict jobs with data-only theme overrides and explicit
Reload Settings; both published Mac RIDs passed the separate in-memory AppKit
reload marker. An independent real-file Windows hidden-HWND test passed the
same-ID color change, half-write/read-failure retention, recovery and Undo
contract. Those are scoped target/control results, not user-palette pixel,
physical IME or screen-reader acceptance
([theme result](native-theme-overrides.md),
[file-backed workflow](validation/native-theme-file-workflow.md)). A bounded,
source-backed CSV Grid **format projection** also landed
([format evidence](csv-grid-format-implementation.md)). Its format-only
commit `9034fbe` passed all six strict jobs in
[CI 36734783210](https://github.com/kleedaisuki/mote/actions/runs/36734783210);
that run alone did not establish a native table. The later native Grid commit
`526cc9b` installed Windows owner-data ListView and AppKit NSTableView with
bounded coordinate windows and versioned source-backed commands.
[CI 36746843707](https://github.com/kleedaisuki/mote/actions/runs/36746843707)
passed six strict jobs and both separately non-gating in-memory Mac Grid and
source-NUL probes on x64/ARM64. This is not actual clipboard publication,
external AX/UIA reader acceptance, full logical-file scrolling, physical IME,
or native large-file latency ([Grid scope](native-csv-grid.md)). A separately
reviewed Windows NUL data-loss fix `c2a7613` also passed six strict jobs in
[CI 36745185336](https://github.com/kleedaisuki/mote/actions/runs/36745185336):
RichEdit-backed NUL intervals are explicitly read-only instead of silently
deleting their canonical suffix. This preserves data, not full binary-text
editing capability.

### Newly established scoped evidence (2026-10-01)

- **Actual Grid clipboard, not desktop interaction:** [CI 36752189587](https://github.com/kleedaisuki/mote/actions/runs/36752189587) passed seven strict jobs, including the dedicated disposable Windows hidden-HWND workflow with production `CF_UNICODETEXT` publication and independent readback. [CI 36756839425](https://github.com/kleedaisuki/mote/actions/runs/36756839425) at `7420d0b` passed **nine strict jobs**, including dedicated published-Native-AOT hidden `NSTableView`/production `NSPasteboard` workflows on **both macOS x64 and ARM64**. Fresh exact reports on each platform assert `status=passed`, `nativeClipboard=true`, current run identity, reviewed source hash and six cases (quoted CRLF, empty final row, Missing refusal, explicit padding, NUL refusal, over-cap refusal). This closes the narrow real publisher/readback gap, not desktop menus/pointer/shortcuts, Mac source NSTextView editing, clipboard contention/recovery, real IME or external Grid AX/UIA/reader acceptance ([Windows evidence](validation/native-csv-grid-clipboard.md), [Mac evidence](validation/native-mac-csv-grid-clipboard.md), [Grid limits](native-csv-grid.md)). The Windows test runs a freshly built managed harness; it is not native-AOT clipboard acceptance.
- **Draw-return endpoint, not physical paint:** In [CI 36754713708](https://github.com/kleedaisuki/mote/actions/runs/36754713708), all four separate published-AOT Mac Legacy/Continuous × x64/ARM64 **non-gating** probes emitted exact mode markers and each uploaded exactly five causal JSONL records: successful version-1 draw submission, cancelled version-0 draw, two parents and terminal session. Those actual markers/artifacts—not the seven green strict jobs—establish source draw callback return. Natural keystroke, compositor presentation and latency percentiles remain unmeasured ([probe evidence](native-mac-draw-trace-probe.md), [endpoint contract](end-to-end-tracing.md)).
- **Typed Flow heading/theme compatibility restored, synthetic composition only:** The shared adapter preserves heading block identity and Accent for ordinary heading text while retaining explicit inline roles/provenance. In CI 36756839425, separate **non-gating** window-local AppKit theme and synthetic marked-text reports passed on both Mac RIDs in both default and canvas modes (**eight complete mode/RID workflows**). Exact reports cover dark→light→dark heading RGB, unchanged generation/version/selection, deferred application during marked text, commit/cancel isolation, source hashes and Undo/Redo. These close the previous stage-0 heading readiness failure without weakening checks, not real Chinese IME, OS-wide Settings transitions, VoiceOver or compositor paint ([heading contract and hosted evidence](validation/native-flow-heading-identity.md)).
- **Historical AX recovery failure, superseded by the repair checkpoint below:** CI 36756839425's separately **non-gating** in-process Canvas AX probes on both RIDs exited 1 at stage 5 after approximately 90 seconds. Each fresh wrapper report is `unverified`, independently confirms unchanged fixture SHA and 11 metrics lines. Deadline samples show provider absent, empty source binding, matching binding/snapshot version, editable/focused input, but `status_match=false` and `status_exact_suffix=false`. Thus the required unavailable-status condition is not observed after New; this is not evidence that New failed to expose an empty binding or that file contents changed. At that checkpoint root cause and intended status-lifetime contract remained open; do not infer controller canonical state or external AX/VoiceOver acceptance from shell-binding getters ([diagnostic semantics](validation/native-mac-canvas-ax-state-diagnostics.md), [preceding failure audit](validation/ci-36752189587-mac-nongating-audit.md)). Exact historical checkpoint reports/metrics are retained under `.cache/ci-36756839425-ax/osx-{x64,arm64}/.cache/ci-inventory/osx-{x64,arm64}/`.

- **Logical Grid scrollers implemented; Mac synthetic target passed:** Commit `a604ae9` adds independent logical row/column controls, certified unavailable/prefix/exact extent, bounded viewport slots and gesture identity, plus coalesced navigation and a shared content/Full/viewport scheduler. Local affected Release warnings-as-errors tests passed **184/184**, including **16/16** Windows hidden-HWND and **11/11** portable Mac interop cases. Commit `b7ab02a` added the independently reviewed AppKit native scroller probe; both published Mac RID steps emitted exact success markers in [CI 36764576285](https://github.com/kleedaisuki/mote/actions/runs/36764576285). This establishes hidden native control readback and synthetic target/action dispatch, not physical drag, controller stale-token acceptance, external AX or end-to-end latency ([implementation/evidence](csv-grid-logical-scrollbar-implementation.md), [Mac probe scope](validation/native-mac-grid-scroller-probe.md)).
- **AX warning-loss repair accepted in two scoped target runs:** Commit `5a25483` fixes session-capability warning loss when New/analysis replaced ordinary status: the controller now owns one persistent notice composition from latched AX/theme/reload/preview/settings facts, and Mac status observation reads the effective native field. In [CI 36763097147](https://github.com/kleedaisuki/mote/actions/runs/36763097147), both separately **non-gating** published-AOT x64/ARM64 in-process probes emitted the exact marker, reported `mac-canvas-ax-workflow-ok`, unchanged fixture SHA and fresh metrics through stage 6. Stage 5 now shows empty generation-4/version-0 binding, detached provider, editable/focused input and `status_match=True`; final insertion and held-element teardown completed. This supersedes the stage-5 warning-loss blocker above without changing stage acceptance. It is not external AXUIElement/VoiceOver, real IME or repeated stress acceptance ([root cause and fix evidence](validation/mac-ax-warning-lifetime-fix.md)).

**Earlier nine-job integration checkpoint:** [CI 36770328576](https://github.com/kleedaisuki/mote/actions/runs/36770328576) passed Windows/macOS solution tests, all four strict single-binary Native AOT jobs and three dedicated native Grid clipboard jobs. Commit `37b6398` retains a single certified `TOML_OWNERSHIP` error above the 4 MiB whole-parser threshold through the Native visible frame and page-away/back, still reporting `Provisional` with unknown global diagnostic count; local final related tests passed 35/35, independent Formats differential 24/24, and hosted cross-platform build/test passed. This closes the specific proved-error loss, **not** general large-file TOML completeness or native GUI proof for that file. The separate non-gating fresh win-x64 AOT 1 MiB long-string JSON readiness pilot passed exact source-prefix binding, native selection acknowledgment and unchanged input/copy hashes ([case and report](../benchmarks/NativeAcceptance/WindowsReadinessPilot.md)). Child open→editable, draw return and edit/Save endpoints were not collected; its one parent-clock observation is not a latency distribution, semantic acceptance or physical paint.

**New trace-on capability checkpoint:** [CI 36773769055](https://github.com/kleedaisuki/mote/actions/runs/36773769055) again passed all nine strict jobs. Its separate non-gating win-x64 natural-close 1 MiB JSON probe used the same freshly published one-file AOT binary, normally exited with a terminal `mote.session`, zero dropped records and a valid causal graph. The one child trace recorded `document.open` 10.896 ms, open→editable 25.270 ms, startup-after-configuration→editable 99.777 ms, and open→source-draw-callback-return 45.255 ms ([raw evidence and endpoint definitions](../benchmarks/NativeAcceptance/WindowsNaturalCloseTrace.md)). Configuration remains uninstrumented; these one-process child intervals are not parent observation intervals, p95, physical paint or tracing-overhead comparisons. No edit or Save was performed in that probe.


**Previous ten-job run, nine strict gates plus one successful non-gating diagnostic:** [CI 36780934192](https://github.com/kleedaisuki/mote/actions/runs/36780934192) at `f76c56e` finished with all ten job conclusions successful. **Nine** jobs were strict: both solution-test jobs, four Native AOT RID jobs and three dedicated native Grid clipboard jobs. All four Native AOT publish inventories list **one executable and zero sidecars** (retained `.cache/ci-36780934192-inventory/` for Windows and `.cache/ci-36780934192-grid-mac/inventory-osx-{x64,arm64}/` for Mac); this is binary-shape evidence, not macOS Gatekeeper trust. The tenth, Windows Save diagnostic job, is `continue-on-error`; its **own retained result passed** the first complete held no-Delete-share positive control, recording one causally parented `save.failure.replace` **HResult 0x80070020** (Win32 32), original target bytes, exact owned recovery snapshot, normal exit and terminal trace. Four following *ordinary* 1 MiB synthetic Saves each wrote exact `X` + original bytes, left no recovery slot, exited normally and retained complete causal terminal traces. Independent byte/JSONL audit confirms these scoped facts; `0/4` ordinary failures in one hosted batch is **not** a reliability estimate, nor a reproduction/explanation of the historical inferred 1175 or documented target-missing 1176 ([Save audit](validation/windows-save-diagnostic-first-target.md), [recovery contract](save-failure-recovery.md)).

<a id="current-scoped-evidence--semantic-pages-grid-identity-and-theme-target"></a>

### Historical scoped checkpoint — semantic pages, Grid identity, and theme target

[CI 36797859586](https://github.com/kleedaisuki/mote/actions/runs/36797859586) at `833ef48` completed **nine strict jobs**: two solution-test jobs, four single-binary Native AOT RID jobs, and three dedicated native Grid clipboard jobs. A tenth Windows Save diagnostic job is separately non-gating. Nested `continue-on-error` diagnostics must be judged by actual child exit, raw output and artifacts, **not** the containing green job or step conclusion. This is a source/build/workflow checkpoint, not release acceptance; later worktree candidates are not included in its verdict.

- **JSON root-array semantic pages implemented and independently challenged:** Full parsing harvests immutable approximately 64 Ki-unit owners in one authoritative pass, retaining versioned counts/spans rather than an AST or strong snapshot. Strict interior edits dirty owners; exact seams, shell/cross-owner edits or missing history invalidate reuse, and malformed intermediates stay honestly Provisional until repair. Certified Visible repairs/projections share **512 Ki charged source visits**; cold large Visible has a **256 Ki ceiling** and cannot invent global completeness. Other roots, giant indivisible values, depth/resource refusals and general large-JSON semantics remain separate limits. Expanded **28/28** Windows CoreCLR directed tests include 100 boundary edits, 1,000 seeded nested replacements, cancellation/retry, same-version different-snapshot rejection, absolute-span/count comparison against fresh Full, and independent System.Text.Json syntax/count checks ([contract](json-array-pages-production.md), [test evidence](validation/json-array-semantic-pages-tests.md), [review](reviews/json-array-semantic-pages-review.md)). Matched-source 100 MiB managed Full LF median is **583.234→582.567 ms**; ordinary allocations are effectively unchanged. CRLF's initial five-pair paired median **+7.29%**, including a **+23.35%** outlier, is retained; ten additional pairs yield **+0.406%** paired median, not an erased original regression observation. After Full, 200 local plus 5,000 dispersed edits per corpus retain exact Complete/version/count and the work ceiling; dispersed Analyze p95 is **0.631 ms LF / 0.595 ms CRLF / 2.251 ms compact**, with approximately **32 KiB certificate metadata**. Compact cold allocation adds 114,320 bytes; there is no universal allocation win. These are controlled CoreCLR parser times, **not Native AOT input/paint tails** ([measurements and rejected candidates](json-array-pages-performance.md)).
- **Engine range reads support that representation without prefix copying:** The additive `TextSnapshot.GetChunks(start, length)` seeks directly into immutable rope leaves, eagerly validates arguments, and preserves existing parameterless/API lifetime semantics. Independent review and completed **44/44** focused validation support exact range/lifetime/compatibility behavior; traversal is O(log n + k) for k emitted leaves, with iterator/stack allocations. A retained sequence roots its captured snapshot; this is not allocation-free or a memory-reclamation guarantee ([range review](reviews/engine-range-chunks-review.md)). The separate inline Open-producer retention investigation rejected a clear-before-completion optimization because measured heap benefit was absent; benchmark continuation unwinding must not be relabeled an Engine production fix or certificate leak ([retention investigation](engine-retained-memory.md)).
- **Ordinary Windows source UIA now has fresh two-RID host evidence:** CI 36797859586 actually launches `mote <fixture>` with **no presentation flags** on win-x64 and win-arm64. Both product reports pass **19/19 source/lifetime checks**, expose **1/1/1 source Documents** in Raw/Control/Content with **zero tree blockers**, map the 16-unit native input island back to the full source, reject stale ranges after New/Close, and close the editor normally. x64 has consistent target-owned global source focus and client exit **0**; ARM64 has foreign foreground, truthful false source-focus properties and exactly one focus `Inconclusive`, so client exit **1**, with no foreign semantic identity inspected. Same-binary diagnostic A/B controls still show baseline **2/2/2** versus fragment **1/1/1**: the baseline duplication is a **historical diagnostic-route finding, not a current default-product release blocker**. This closes ordinary-route/tree attribution, not ARM positive foreground, complete TextPattern, Narrator/NVDA speech, real IME or general large-file accessibility ([independent scope correction and hosted audit](reviews/windows-source-uia-scope-correction.md#fresh-ordinary-product-hosted-evidence-run-36797859586)).
- **Windows Grid external identity/selection subset now passes on both RIDs:** In [CI 36791254056](https://github.com/kleedaisuki/mote/actions/runs/36791254056), both actual published-AOT external clients exit **0**, report `Classification=pass`, and expose **1,104 children with 1,104 unique nonempty AutomationIds and runtime IDs in each Raw/Control/Content view**. Executed pinned assertions check cross-view identity-set equality; the artifact does not serialize all identities, so arbitrary identity-to-coordinate mapping is not claimed. Exact one-/two-cell selected snapshots survive Add and rejected sparse union; Go-to-cell `1001:17`, bounded/read-only range facts and stale-cell refusal pass. Five synthetic owned F6 HWND transitions pass on both; x64 global semantic focus is target-owned, while ARM64 global focus is **blocked by foreign foreground**, with no foreign semantic metadata inspected. Off-owner cell SetFocus is refused, not a positive focus pass. This closes earlier count-only/unique-ID gaps, not reader speech, real IME, RangeValue writes, 100 MiB teardown, arbitrary rectangles or default enablement ([independent audit](validation/windows-grid-accessibility-ci.md)). Historical CRLF pin rejection in CI 36782853022 was a pre-execution harness defect; earlier count-only passes and the composite Mac Missing-menu correction retain their evidence in the focused [Windows](validation/windows-grid-accessibility-ci.md) and [Mac](validation/mac-grid-menu-regression.md) records.
- **Mac popup now opens, but external action acknowledgment still fails:** CI 36797859586 passes both actual in-process combined markers and Swift/AppKit typechecks; both separate clients still **exit 1 after 25 passed checks**, first failure `context-menu-accessible`, **AX −25205**. Unlike the earlier inherited native BOOL refusal in CI 36794910486, the explicit next-turn popup now records four bounded phases through `will-open`, with one open and truthful shown state. The read-only action discriminator verifies advertised AXShowMenu and matching action constant on both RIDs. AppKit `shownMenu.rawValue` is **the same `AXShownMenuUIElement` literal** on both hosts, falsifying the presumed modern/Carbon wire-key distinction. x64 Table relationship reads return an **owned AXMenu, 12 children and exactly one coordinate title**; ARM64's sampled relationships return unsupported. Both single app-tree traversals miss the coordinate item, which does not prove it was absent throughout menu lifetime; the x64 relationship directly demonstrates otherwise. No action retry, menu press, prompt, logical jump, retirement or normal close is attempted; originals are unchanged and cleanup remains forced. This establishes native opening and x64 explicit menu transport, **not** action success or full external AX acceptance. The next decisive question is the failed action reply, not guessed keys/labels or a larger query budget ([final discriminator evidence](validation/mac-grid-table-proxy.md#read-only-action-discriminator-ci-36797859586), [menu history](validation/mac-grid-menu-regression.md)).
- **Corrected Windows theme diagnostic passes only explicit legacy mode:** CI 36794910486's separately non-gating win-x64 report is actually `passed`, reaches `dark-after`, and passes all three synthetic HKCU dark→light→dark cases with exact fixture/selection, policy-backed source/status/caption pixels, restored registry and unchanged source hash. Its launch explicitly uses **`--legacy-page`**, visible populated ID 101 RichEdit, and no Canvas sibling. Earlier hidden-empty RichEdit selection failures were a mode/host mismatch. This closes the legacy x64 synthetic transition, **not ordinary/default Continuous Canvas palette/selection/history**, ARM theme, real IME, OS high-contrast or physical presentation ([exact artifacts and corrected scope](themes.md#corrected-legacy-target-host-result-ci-36794910486)).

- **Historical pre-hosted Native JSON acceptance pilot checkpoint:** Commit `cf6ce09` adds a separately non-gating ordinary-route 1/100 MiB root-array pilot on all four native RIDs, with exact source/semantics/Save/reopen and content-free causal-trace oracles. CI 36797859586 predates that integration; at that checkpoint exact-current-source hosted results, Mac client compile/capability, Windows ARM execution and 100 MiB outcomes were **pending the next CI**. The current verdict above replaces that pending status, without erasing the earlier checkpoint. Local harness tests or an older-binary control do not supply those missing target results; even an eventual pass would not establish physical paint, real IME or tail-latency acceptance ([pilot contract](../benchmarks/NativeJsonLargeAcceptance/README.md)).

| Priority | Code/evidence and impact | Owner + decisive experiment | Exit condition |
| --- | --- | --- | --- |
| **P0 — unresolved Windows native crash** | Strict CI 36800944850 aborted with `0xC0000005` during preview/theme import on image `20260925.250.1`. CI 36802378381 passes all 1158 general tests on `20260922.246.2`; test guards and a different image do not constitute a product fix ([evidence](validation/windows-preview-access-violation.md)). | **Native desktop + Verification/CI.** Preserve exact image/RichEdit inventory; correlate failure-only sequence and native fault evidence if the crash recurs, ideally on the failing image before attributing cause. | The native failure is causally understood and corrected or demonstrably scoped to an external component, with targeted regression evidence; a single different-image green run does not close it. |
| **P0 — full semantic editing on large files is not yet delivered** | All six policies have versioned `IFormatSession` semantics, but `Complete` has format- and grammar-specific limits. CSV cold `Visible` now scans a bounded prefix, preserves honest `CoveredRegion`/`Provisional`, and a cancellable idle `Full` is admitted by the Native scheduler; the prior 2.2 MiB offscreen `CSV004` controller regression now reaches exact `Complete`. A comma-run fast path cuts one 100 MiB giant empty-field row’s Release median Full scan from 1,900.5 to 96.4 ms without changing exact counts; mixed-file timing ranges overlap, so no general speedup follows ([CSV evidence](csv-cold-analysis.md)). Markdown certifies restricted blank-separated headings/paragraphs/closed fences and safe adjacent ATX-heading seams; changed block kind recertifies rather than reusing stale boundaries ([block certificate](markdown-block-certification.md)). A **new bounded explicit-reference domain** also resolves cross-block first-winning declarations and `[text][label]` consumers through a certified source-owner index: isolated ASCII single-line owners/declarations, bounded labels/atoms and exact closed fences can reach whole-file `Complete`/exact warning totals, while cold large-file `Visible` stays `Provisional`. Adjacent/collapsed/shortcut or Unicode references, containers and general CommonMark still refuse certification rather than claiming all references complete. One managed Windows x64 100 MiB file-backed corpus measured cold Full **260–285 ms**, 30 destination edits median **4.164 ms**/nearest-rank p95 **13.945 ms**, and cold Visible **32–39 ms** provisional; these are corpus-specific managed samples, not native GUI first paint or production tails. Commit `24bf145` passed [CI 36758358616](https://github.com/kleedaisuki/mote/actions/runs/36758358616) with four Native AOT RIDs among nine strict jobs, establishing cross-platform build/test compatibility only ([production domain](markdown-reference-production.md), [independent validation](validation/markdown-reference-production.md)). The 100 MiB JSON unique-key corpus reaches `Complete`; root-array semantic pages now also reuse validated owners under explicit 256/512 Ki interactive work ceilings, with 28-case directed/seeded and matched-source managed evidence above. This is not arbitrary JSON-root incremental coverage; all eight scoped AOT GUI cases passed in CI 36811953139; later Mac Save timeouts include x64 1 MiB in CI 36814164862/36818175897, x64 100 MiB in CI 36815303415/36823606282 and ARM64 1 MiB in CI 36823606282. The latest CI 36824892264 passes all eight ordinary cases with exact version-1 causal chains, but this is one capability run, not a delivery/reliability fix. Their causes remain unattributed; these runs do not certify arbitrary JSON workloads or repeated-run reliability. TOML now uniformly represents latest array elements, including valid parent re-entry after nested headers; explicit-header/array dotted traversal is a proved ownership error. The pinned 712-fixture manifest has 218/218 decoded valid Complete and 0/485 decoded invalid Complete; nine ill-formed UTF-8 cases remain a separate encoding boundary. Stateful seams eliminate repeated multiline-prefix copies, and validated statement IR reuses actual local edits, but mapping/projection remain O(statements) and changed namespace effects replay ownership. Existing statement/binding caps, malformed-input Provisional totals and contextual token boundaries remain open ([model](architecture/toml-statement-semantic-reuse.md), [local measurements](performance/toml-statement-cost.md)). YAML now retains `yaml.undefined-alias` and downgrades to `Provisional` with unknown global count when an undefined alias makes a mapping key or anchored node noncanonical, including direct/nested cases and an offscreen key beyond 2 MiB; it does not invent duplicate-key errors. Density/recovery limits and the measured 100 MiB full-edit re-stream cost remain open ([alias-key recovery](yaml-alias-key-recovery.md)). | **Formats + Native desktop.** Compare versioned offscreen errors/dependencies, cancellation/retry and exact oracle results on adversarial >2 MiB files, including real AOT UI responsiveness while CSV/Markdown Full runs; measure CPU/RSS and status honesty. | Every supported format reaches whole-file semantics for its **claimed** domain without blocking edits; unsupported grammar and resource limits are visible as `Provisional`, never invented zero diagnostics. |
| **P0 — ordinary Continuous profile is wired, not release accepted** | On this development branch, ordinary `mote [path]` now routes to the source-backed `Continuous` canvas; `--legacy-page` preserves the established 64 Ki-unit native text-page workflow as an explicit process-restart rollback. Windows ordinary routing also selects the single-source UIA fragment; presentation stays fixed for a window lifetime. A local Windows x64 one-binary ordinary/legacy smoke passed. [CI 36689963214](https://github.com/kleedaisuki/mote/actions/runs/36689963214) passed all six **strict** jobs; its separate **non-gating ordinary-route** Windows x64 external HWND/UIA probe reported `passed` with source **81,941→81,942** UTF-16 units, exact Save/fresh-reopen bytes and foreground `mote.source.document` focus. Windows ARM64 reported `inconclusive`: the same edit/Save/reopen and source tree succeeded, but an unrelated OOBE process stayed foreground, so positive source focus was not established. Both Mac ordinary probes in that run stopped at `open-original` **before any source AX observation or edit** because the PowerShell harness assigned read-only `$Pid`; this is a harness error, not a product editing result. After the `71212ce` harness correction, [CI 36690808291](https://github.com/kleedaisuki/mote/actions/runs/36690808291) again passed six strict jobs, and **both non-gating ordinary macOS x64/ARM64 external** probes reported `passed`: the exact-length labeled source AX proxy showed reversible selection **0/0→0/1→0/0**, then one `X` gave **11→12** and selection **1/0**, with exact full-byte Save SHA and a fresh-process reopen of length **12**. These are target-host automation workflows, not one four-RID focus acceptance ([native behavior](../src/Mote.Native/README.md), [migration contract](virtual-editor-design.md); retained reports `.cache/continuous-run-{36689963214,36690808291}/`). Earlier `--canvas-experimental` evidence remains **opt-in diagnostic evidence**, not a pass for the new ordinary route: Windows x64 local/hosted real-HWND 50 MiB one-line pan/global copy/Save/reopen passed with bounded host and distinct rasters; both hosted Mac RIDs passed non-gating in-process 16 Ki/50 MiB horizontal source-hit/copy and geometry restoration; Mac ARM separately passed external 100 MiB many-line and corrected 50 MiB long-line one-key Save/fresh-reopen, while both Mac RIDs passed non-gating in-process 50 MiB X→source+1→Undo checks ([horizontal design](horizontal-navigation-design.md), [Mac external probe](../benchmarks/NativeCanvasGui/README.md)). These narrow single-run diagnostics do not establish ARM ordinary-route positive foreground focus, sustained first-key reliability, physical trackpad/horizontal control, 100 MiB first paint, real IME, VoiceOver/Narrator or release-level parity. | **Native desktop + large-file UI/perf + Verification.** Rehost **positive** Windows ARM64 foreground/source focus without OOBE interference, then repeat ordinary external edit/selection/horizontal navigation/Save/reopen and `--legacy-page` rollback on all four RIDs. Challenge first-key, real IME/reader coexistence and 100 MiB input-to-screen/resource behavior before release acceptance. | One coherent ordinary continuously and horizontally navigable source view with exact global edits, selection, source accessibility and Save at scale; real IME/reader/focus and paint gates pass without a full hidden mirror, while `--legacy-page` remains a safe rollback until parity is demonstrated. |
| **P1 — native rendered preview needs interaction/fidelity gates** | `NativePreviewBuilder` now emits bounded (16 Ki characters/120 lines), format-specific text and source-mapped spans: Markdown headings/lists/fences, CSV columns and structured trees; Win32/AppKit apply semantic colors/fonts. The native shells now emit stamped pointer/keyboard preview activation; the controller resolves only explicit semantic runs to global source coordinates, vetoes stale versions and active IME composition, and preserves text/Undo. Focused tests passed 8/8 and local Windows managed real-HWND pointer/keyboard probes passed in LegacyPage and Continuous; hosted macOS x64/ARM external synthetic pointer/keyboard navigation passed all eight scoped cases in [CI 36718845829](https://github.com/kleedaisuki/mote/actions/runs/36718845829), after the probe cleared inherited Command flags; physical keyboard/VoiceOver/IME and image/link activation remain open ([navigation contract](native-preview-navigation.md)). This is materially beyond the former 40-line outline, but not a complete rich document preview. | **Native desktop + Formats.** Use fixtures for Markdown heading/list/fence/link/image, CSV quoted newline and nested JSON/TOML/YAML; verify styled source maps, safe link behavior, keyboard/pointer navigation back to source, and truthful truncation. | Bounded source-mapped native rendering supports the promised interactions or clearly declares unsupported rich elements; no browser or sidecar. |
| **P1 — IME, accessibility and distribution trust need deeper target-OS evidence** | Both Mac RIDs passed strict **default-editor** external keyboard/Save/reopen, while System Events `frontmost=false` was shown to be an unreliable sole foreground test; the opt-in Canvas now has an external Mac ARM bounded-key Save result, but other RIDs, long-term focus reliability and active-IME/reader coexistence remain open. Local final-source Windows x64 single-file AOT passed real Microsoft Pinyin candidate Insert, Escape cancel and offscreen-caret 50 MiB pan→commit→exact full-byte Save/fresh-reopen with a physically focused ribbon ([input evidence](native-input-accessibility.md)); a cold attempt lost the initial `n`, so first-key reliability is not established. Fresh ordinary Windows product UIA now exposes one source Document in all three views on both RIDs and has x64 foreground focus consistency; the historical diagnostic baseline duplication is not the current product route. ARM foreign-foreground four-path property 30008 correctly reports false and the client remains inconclusive, so ARM positive foreground focus, simultaneous active-IME UIA and actual Narrator/NVDA remain untested. External Mac AX source-range/Close checks passed; the historical stage-3 fault timeout and later passes retain their scoped evidence above. The CI 36756839425 both-RID stage-5 warning-loss failure is retained as historical evidence. Its production lifetime fix `5a25483` passed both separately non-gating published-AOT in-process probes through stage 6 in CI 36763097147, with exact marker, matching native warning, unchanged fixture SHA and held-element teardown ([repair evidence](validation/mac-ax-warning-lifetime-fix.md)). This closes that scoped failure, not external AXUIElement/VoiceOver, real IME or repeated stress acceptance. The opt-in Mac Grid Table proxy now transports rows/cells/selection externally; CI 36802378381 still fails the original coordinate-menu action acknowledgment (-25205), while independently guarded downstream navigation/retirement/normal close passes on both RIDs; Windows Grid identity passes remain a separate API subset with ARM global focus blocked. VoiceOver, real macOS IME and source caret speech are not proved. The Mac ARM font-message ABI crash exposed by hosted opt-in GUI was fixed; CI 365910 and 365948 passed all six strict jobs, not a stress or Gatekeeper result. Bare Mach-O distribution still needs signing/notarization and online first launch ([feasibility](macos-bare-binary-distribution.md)); **the user has no Apple Developer account and explicitly does not want signing/certificate work now**. Therefore ordinary trusted distribution is **blocked by absent publisher Developer ID credentials and notarization**, not merely waiting for another CI run. Continue unsigned/ad-hoc one-Mach-O technical tests without asking for secrets, proposing a delivery workaround, or claiming Gatekeeper acceptance; revisit only at the user’s later direction. | **Native desktop + Accessibility + Verification/CI.** Trace the cold Pinyin first-key case and active-candidate source/reader focus without content logging; test real macOS IME, Windows ARM foreground focus, Narrator/VoiceOver and high-DPI candidate geometry. Keep unsigned/ad-hoc technical builds; do not begin credential setup without a later user decision. | Real IME and assistive-technology workflows pass on target OSes; single-binary distribution/trust and the offline limitation are stated accurately. |
| **P1 — configuration/theme runtime integration is partly verified** | Default `~/.mote` paths and configurable cache/data/traces, privacy-bounded opt-in tracing, and hosted Mac x64/ARM five-case config workflows are established. Statically bundled VS Code/JetBrains-inspired dark/light/high-contrast policies passed 14/14 palette/preview contrast checks and four-RID read-only canvas sentinels, but native chrome/selection/IME contrast is not thereby certified. A valid unknown theme ID now produces a **nonfatal `CONFIG_THEME` warning** while falling back; the older silent-fallback finding is superseded. `system` now reacts to appearance notifications in source, coalesces/defer-applies changes during composition, and passed bounded published-binary dark→light→dark probes on Mac x64/ARM in the **historical default and opt-in Canvas** modes. The historical win-x64 probe passes **legacy-page only** in CI 36794910486. In CI 36809964231 the unchanged ordinary Continuous probe now passes synthetic dark→light→dark on **both Windows RIDs**, with global source selection/text, source-body raster, exact registry restoration, unchanged input and normal exit. The earlier source-range E_NOTIMPL obstacle is closed for this sequence; editing history, immutable engine version, physical presentation and real IME remain unverified ([target audit](validation/windows-canvas-live-theme.md#first-complete-bounded-native-pass-run-36809964231)). The typed Flow heading fix has now passed all eight window-local theme/synthetic marked-text mode/RID workflows in CI 36756839425, including deferred application and commit/cancel isolation ([exact hosted reports](validation/native-flow-heading-identity.md)); **actual OS-wide Mac switching and a real IME-in-progress transition remain untested** ([theme audit](themes.md), [validation](../tests/VALIDATION.md)). | **Native desktop + Configuration/Themes + Verification.** Verify published-binary Windows path overrides and the native unknown-ID warning; exercise actual Windows Settings and macOS system-wide appearance changes during open editing and real preedit, then inspect candidate position, exact text/version/selection and native chrome/ribbon/preview colors. | Defaults and overrides work end to end on both OSes; typo fallback is visible, `system` safely tracks live appearance, with no sidecar or unexpected write. |
| **P1 — latency evidence does not yet establish “instant” native editing** | Hosted 100 MiB JSON and local CSV/Markdown parser measurements isolate semantic work, not GUI first paint or p95 edits. Root-array page reuse now has sub-3 ms managed dispersed Analyze p95 on three controlled 100 MiB corpora, but its cold Full, retained-memory/producer controls and compact-allocation trade-off do not establish Native AOT natural-input or paint performance. CSV cold Visible and giant-comma Full improved in managed Release, but mixed CSV Full still costs meaningful CPU; idle scheduling is an admission policy, not proof that UI remains smooth ([CSV evidence](csv-cold-analysis.md)). One external Mac ARM 100 MiB many-line run observed source AX readiness at 3,007.5 ms and automation-inclusive X→exact Save at 1,055.7 ms, with point working set 291.7→309.2 MiB; those are neither first editable/paint timings nor distributions, and Mac peak-RSS counter remained unavailable. The corrected 50 MiB long-line external edit reached exact Save/reopen once on Mac ARM (automation-inclusive X→source AX +1 341.7 ms; X→Save 977.3 ms), but this is not p95, input-to-paint or physical navigation latency. Engine copy-free notifications removed a known 50 MiB Redo temporary allocation, while paired hosted phase latencies were mixed ([performance analysis](native-performance-baseline.md)). | **Large-file UI/perf + Verification.** Run matched cold/warm Native AOT 1/10/100 MiB and long-line workloads on both OSes, instrument input→actual draw/compositor endpoints, p50/p95 scroll/edit, allocations/RSS, and telemetry overhead; keep automation round-trip separate. | Reproducible OS-specific native GUI baselines and regression thresholds grounded in target workloads. |
| **P1 — permanent causal tracing is hosted, not full input/transport/presentation certification** | The default-off local `~/.mote/traces` JSONL pipeline correlates accepted edit/apply/parse/presentation and version-matched **source draw return**, not photons. Save/Save As now adds typed target receipt, explicit worker/Engine phases, the exact captured saved snapshot and guarded local UI completion. CI 36824892264 independently verifies eight native Save chains plus 16 separate synthetic recovery controls; the controls do not certify Engine Save or loss-free transport. Schema-v1 `save.failure.replace` compatibility is restored additively, not by weakening the Windows held-handle oracle. Missing censored receipt remains unknown. The macOS menu-boundary follow-up is in progress and unvalidated. Current-binary overhead has zero equivalent inferential pairs, so no no-regression conclusion is available ([hosted audit](validation/causal-observability-hosted.md#follow-up-repaired-strict-contracts-ci-36824892264), [endpoint/privacy contract](end-to-end-tracing.md), [performance limit](performance/causal-trace-overhead.md)). | **Native desktop + Telemetry + Verification/performance.** Validate the narrow earlier menu/key-routing boundary without retries or content logging; qualify paired off/on native workloads on a disposable foreground desktop. Preserve bounded queue/fault behavior, original-sink ancestry and censored/orphan semantics; measure separate compositor/screen endpoints only under an explicit contract. | End-to-end claims use actual scoped endpoints; privacy and schema compatibility remain intact; overhead is quantified rather than inferred from green CI; absence and draw return are never mislabeled delivery or physical paint. |
| **P1 — residual Save/Save As race and native picker integration** | Same-path Save hashes original bytes; default Save As refuses an existing different target, while explicit `FileOverwriteToken` authorizes replacement. Native controller now performs its **own** `ConfirmOverwrite(path)` before `SaveOverAsync`, so it no longer relies solely on native panel behavior. [Code review](code-review.md) verified the engine contract locally. A narrow verification-to-`File.Replace` race remains, and actual Win/mac picker-confirmation flow is not GUI-tested. A bounded win-x64 AOT diagnostic now proves the expected held no-Delete-share `save.failure.replace` **0x80070020** with retained original/recovery bytes, plus four separate exact ordinary Saves and complete terminal traces; it does **not** reproduce the historical inferred 1175, exercise 1176, or measure Save reliability ([independent artifact audit](validation/windows-save-diagnostic-first-target.md)). | **Engine + Native desktop + Verification.** On both OSes cancel an existing-target Save As and compare bytes, then confirm replacement; use a deterministic writer barrier to characterize remaining TOCTOU race. | Explicit user intent precedes every overwrite and the concurrent-writer guarantee is stated accurately, not promised as perfect atomic detection. |

## Do not resurrect resolved findings

Earlier audit rows about metadata-only Save conflict detection, unordered `Changed` events, unconditional in-flight Open replacement, no-op Markdown/TOML/YAML formatters, and scalar-only YAML duplicate detection are stale. Engine now fingerprints bytes and queues notifications; Open reconfirms; formatters have conservative nonidentity implementations; YAML uses tag-aware canonical/structural comparison and warns where equality cannot be decided. Keep regression tests, but reopen only with fresh failure evidence. The strict one-binary, Windows native-control and Mac external-keyboard workflow gates have passed; **interactive** continuous large-file behavior, general `Full` semantics, real IME/AX and distribution trust are the decisive frontier—not polishing the noncompliant Avalonia package.
