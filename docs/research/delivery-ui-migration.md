# Relaxed delivery contract: packaging and UI migration are separate decisions

Date: 2026-10-02. Status: evidence-backed architecture recommendation, not a product switch, implementation, build, performance run, or release certification.

## 1. Scope and source snapshot

The question is **relaxing one on-disk delivery binary**, not removing mote's **one source document** model. An application directory or macOS `.app` may contain companions without introducing a workspace, external language server, runtime plugin, or second canonical document. The existing strict requirement remains in README/architecture until a separate product decision changes it.

Inspection began at HEAD `6827cd1a19142c9ad766d296c18d0770e600e79c`. The working tree was already being edited by the native-source teams: `EditorPresentationProfile.cs`, `NativeEditorController.cs`, Windows/Mac shells, and new `NativeEditorController.Source.cs`, `NativeSourceBinding.cs`, `NativeSourceContracts.cs`, and platform Source partials were changed/untracked. These are **observed candidate boundaries**, not a frozen accepted release. The map below is therefore semantic/file-area guidance, not a claim that the live branch's signatures are stable. No production edit, shared build, local GUI, commit/push, or CI action was performed for this research.

Filename-led internal material reused:

- [Architecture](../architecture.md): ownership, UTF-16 coordinates, immutable snapshots, static policies, version checks, Save safety.
- [Ordinary editing locus](../architecture/ordinary-editing-locus.md): one visible source painter/input target; native-source experiment and composition boundaries.
- [Large-file UI evaluation](../large-file-ui-evaluation.md): existing Avalonia/AvaloniaEdit AOT measurements; do not repeat these completed probes.
- [Bare macOS distribution](../macos-bare-binary-distribution.md): signing/notarization credentials remain absent; bare-binary offline stapling limitation.
- [Theme overrides](../theme-override-configuration.md), [theme composition](../theme-override-composition.md), [tracing](../end-to-end-tracing.md), and [save recovery](../save-failure-recovery.md): portable user contracts, not UI implementation details.

## 2. Recommendation

**For near-term ordinary editing, retain Win32/AppKit and qualify the single-native-source candidate. If the delivery restriction is relaxed, accept `.app` packaging independently. Do not replace the UI merely to use the newly permitted package shape.** This is the smallest change that can improve trusted macOS distribution while preserving the investment in exact source ingress, engine history, Save routing, native input and native diagnostics.

**For a strategic shared UI, Avalonia 12.1.3 + Skia + AvaloniaEdit 12.0.0 is a concrete candidate, not an automatic promotion of `Mote.Desktop`.** Its benefit would be one chrome/layout/control implementation and a single editor layout authority across platforms. Its risks are a fresh text/IME/accessibility integration, semantic/CSV preview port, runtime/native dependency servicing, and existing large-file regression evidence. In particular, the pinned editor lacks visible IME preedit: this matters directly to a mainland-China user and must be resolved before the migration earns a default switch.

Keep Native AOT as the first candidate because mote already has explicit registrations and AOT-compatible core modules. Use **self-contained JIT as a qualification control and supported alternative only if real dependency compatibility or measured performance justifies it**. ReadyToRun is a measured JIT-startup optimization, not a third architecture or elimination of the JIT.

The decision hierarchy is:

```text
delivery shape              UI/toolkit                   source surface
binary / folder / .app  !=   native / Avalonia        !=  native source / AvaloniaEdit / custom viewport
         |                          |                            |
 distribution/trust         maintenance/layout reuse       editing/IME/AX/capacity
```

Changing the left column permits choices in the middle; it does not prove their user benefit or solve the right column.

## 3. Concrete alternatives

| Alternative | Runtime shape | Actual benefit | Work/risk that does not disappear | Recommendation |
| --- | --- | --- | --- | --- |
| Retained Win32/AppKit Native AOT, strict binary | Existing OS libraries only | Lowest deployment payload/mechanism count | Existing source-candidate qualification, native duplication, bare-Mach-O trust limits | Valid baseline; no reason to discard just because companions become legal |
| Retained Win32/AppKit Native AOT, `.app` on macOS | Same executable in standard bundle with metadata/resources; companions optional, not mandatory | Standard icon/document integration layout and staplable application distribution after signing/notarization | Publisher credentials, fresh quarantined launch tests, launch/file-event behavior | **Preferred first relaxed-delivery slice**; largely packaging, not UI redesign |
| Retained native shell with narrow native companion | AOT executable plus explicitly needed native library | Could put typed Objective-C/AppKit glue in a conventional native module, reducing fragile dynamic-message ABI work | New ABI, lifetime, architecture, signing and version-coexistence contracts | Add only for demonstrated simplification; do not invent a library to exploit permission |
| Avalonia + Skia, Native AOT | AOT executable plus RID-specific native assets; `.app` on macOS | Shared UI/layout, mature toolkit services, no installed .NET runtime | Toolkit/editor qualification, AOT warnings, native asset/signing inventory | **Strategic candidate**, not release-ready Desktop resurrection |
| Same UI, self-contained JIT | Apphost + private runtime/assemblies + native assets; directory or `.app` | Widest normal .NET control/library compatibility; easier runtime diagnostics | Larger runtime payload, cold/JIT tails and macOS JIT entitlements | Fallback/control; benchmark, do not assume speed or size |
| Same UI, self-contained ReadyToRun | JIT deployment plus precompiled assembly code | Can reduce initial JIT work | Still includes IL/runtime/JIT, larger assemblies; no IME/AX/storage fix | Enable only if paired startup/tail measurements justify it |

Avalonia's [Native AOT guide](https://docs.avaloniaui.net/docs/deployment/native-aot) supports compiled XAML/bindings and self-contained deployment, but warns about third-party controls and trimming configuration. AOT compilation of managed code does **not** statically link every native renderer dependency. Its [Skia project](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/master/src/Skia/Avalonia.Skia/Avalonia.Skia.csproj) imports SkiaSharp/HarfBuzzSharp; its [macOS backend project](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/master/src/Avalonia.Native/Avalonia.Native.csproj) packages `libAvaloniaNative.dylib`. Existing local `src/Mote.Desktop/obj/project.assets.json` lists that asset and Skia/HarfBuzz runtime assets; it is an existing restore manifest, **not** a newly produced publish inventory. Expect `libSkiaSharp` / `libHarfBuzzSharp` and the Mac backend; freeze actual published filenames per RID instead of treating this expectation as a complete manifest.

[Microsoft Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) rules out runtime assembly loading/code generation and requires trimming-safe dependencies. [Microsoft ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run) explains that precompiled code coexists with IL and tiered JIT, with assembly growth and workload-dependent trade-offs. [Single-file bundling](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) can embed native libraries **for extraction**, not make native dependencies disappear. Under relaxed packaging, a normal signed directory/bundle is simpler than introducing hidden extraction/cache/security semantics merely to retain an apparent single-file download.

### Packaging-only macOS value

Use a standard `mote.app/Contents/{MacOS,Resources,Frameworks}` with `Info.plist`, the executable and any genuinely required resources/native assets in their appropriate locations. The retained AppKit shell needs no Avalonia companions. Preserve the actual CLI entry `mote [path]`: a PATH-installed link/launcher must forward arguments to the bundle executable, not replace synchronous CLI parsing with `open -a` behavior accidentally. Document any install/launcher creation explicitly. A `.app` is one user-facing application but multiple filesystem entries.

[Avalonia macOS deployment](https://docs.avaloniaui.net/docs/deployment/macos) documents bundle construction, inside-out signing and notarization/stapling. [Apple's authoritative workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow) is the trust contract; the existing internal bare-binary research explains the standalone limitation. **A bundle enables a stapled ticket; it does not supply a Developer ID, approval, or guaranteed offline launch.** The user's absent Apple Developer account remains a real blocked trust gate, not a UI-framework problem. Do not copy broad sample entitlements (JIT/library validation/DYLD) indiscriminately: retained native AOT needs no JIT permission; self-contained JIT/R2R requires a deliberate minimum-entitlement assessment.

## 4. AvaloniaEdit: maturity must be decomposed by capability

The [upstream editor](https://github.com/AvaloniaUI/AvaloniaEdit) provides a real reusable control: selection/caret, wrapping, folding, extensibility and viewport visual-line virtualization. This is substantially less text-surface construction than starting from a drawing API. However, reuse of a port of AvalonEdit is not proof of every platform capability.

### License, dependency and maintenance facts

- [Avalonia](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/master/licence.md), [pinned AvaloniaEdit](https://raw.githubusercontent.com/AvaloniaUI/AvaloniaEdit/86fdebec4cff7affb0e14c7885df71d28edce777/LICENSE), and [SkiaSharp](https://raw.githubusercontent.com/mono/SkiaSharp/main/LICENSE.txt) have MIT licenses. [Skia](https://github.com/google/skia/blob/main/LICENSE) has the three-condition BSD license; [HarfBuzz](https://raw.githubusercontent.com/harfbuzz/harfbuzz/main/COPYING) describes its principal license as Old MIT and directs readers to component-specific notices. A renderer package's transitive notices, fonts and assets still require their own inventory. These permissive upstream terms do not require relicensing mote's GPL-3.0 source, nor eliminate mote's existing distribution obligations. This is a dependency inventory, not a completed legal compliance audit.
- Existing Desktop assets pin `Avalonia.Native/12.1.3`, `SkiaSharp/3.119.4`, `HarfBuzzSharp/8.3.1.3`; a relaxed release must track each shipped native binary and security update, not just the Avalonia managed package. Bundled Inter/font assets are separate from the OS-font fallback choice and require exact shipped-resource/notices checks.
- Direct upstream GitHub API check on 2026-10-02: [repository metadata](https://api.github.com/repos/AvaloniaUI/AvaloniaEdit) reports `archived=false`; [master](https://api.github.com/repos/AvaloniaUI/AvaloniaEdit/commits/master) is `be976eacf40ed3c6992ca3157773e8f1e7315eae`, committer date `2026-06-05T07:54:45Z`; [PR #592](https://api.github.com/repos/AvaloniaUI/AvaloniaEdit/pulls/592) is `state=open`, `merged=false`, head `ef48d7ad5290816f1069bff0d76e2c53e218de41`. This establishes a nonarchived project and an unmerged fix, not a maintenance service-level commitment. Do not call the editor abandoned, or describe the preedit fix as available in the pinned release.

| Capability | Evidence | Consequence for mote |
| --- | --- | --- |
| Text, caret, selection, adornments | Existing Desktop bridge; upstream source and README | Reuse the control, not a home-made Skia caret/selection stack |
| IME committed input versus visible composition | Pinned source and live master have `SupportsPreedit => false` and empty `SetPreeditText` | **Stock 12.0.0 is not accepted for Chinese in-place composition**; committed text support is not enough |
| IME improvement | Upstream #524 is open; PR #592 is open in retrieved live evidence | Proposed code is a candidate patch/fork, not an already-shipped supported feature |
| Accessibility | Pinned `TextEditor.cs`/`TextArea.cs` contain no editor-specific automation override; no inspected proof of a document text-range provider | Toolkit automation support cannot certify editor text, selection/range navigation, or CSV table accessibility |
| Viewport virtualization | Visual lines bounded by viewport, but editor maintains its own text/line/height indexes | Not a snapshot-backed external rope; no automatic large-file memory bound |
| Native AOT | Existing repo AOT GUI probe succeeded; upstream [#404 binding warning](https://github.com/AvaloniaUI/AvaloniaEdit/issues/404) was closed by #405 | Build feasibility is real, but exact editor/input/accessibility behavior still requires qualification |

The strongest IME evidence is [pinned 12.0.0 `TextArea.cs`, lines 1144/1209](https://raw.githubusercontent.com/AvaloniaUI/AvaloniaEdit/86fdebec4cff7affb0e14c7885df71d28edce777/src/AvaloniaEdit/Editing/TextArea.cs), not a hearsay toolkit comparison. [Feature #524](https://github.com/AvaloniaUI/AvaloniaEdit/issues/524) and [PR #592](https://github.com/AvaloniaUI/AvaloniaEdit/pull/592) corroborate the missing preedit and proposed rendering layer. A downstream maintainer's reproduction inside that PR reports remaining click/scroll/baseline issues on a patched fork: useful test leads, not independent mote validation. A focused maintained upstream patch may be reasonable, but it adds maintenance rather than magically removing the current native input work. Prefer fixing inline composition over recreating another detached input ribbon.

For accessibility, absence of an override in two inspected files is a **limited source observation**, not a proof that all framework inheritance yields no automation. References: [pinned TextEditor](https://raw.githubusercontent.com/AvaloniaUI/AvaloniaEdit/86fdebec4cff7affb0e14c7885df71d28edce777/src/AvaloniaEdit/TextEditor.cs), pinned TextArea above, [Avalonia automation properties](https://github.com/AvaloniaUI/Avalonia/blob/main/src/Avalonia.Controls/Automation/AutomationProperties.cs). Require external Windows UIA and macOS AX tests plus attended NVDA/VoiceOver before claiming parity. Reuse engine-backed accessibility semantics where needed; do not expose only a page or silently regress to a generic control with a name.

### Production signals, with their limits

- [ILSpy 11.0 release](https://github.com/icsharpcode/ILSpy/releases/tag/v11.0) shipped its WPF-to-Avalonia desktop port; [its project](https://github.com/icsharpcode/ILSpy/blob/master/ILSpy/ILSpy.csproj) references AvaloniaEdit. This establishes meaningful deployed control/framework use, not mote's editable CJK, engine-history or 100-MiB contract; a decompiler text surface is not the same workload.
- The TextSS maintainer's field report in PR #592 describes a long-running text application's Avalonia migration hitting exactly the missing-preedit problem. This is a direct production-maintenance warning against assuming a reusable editor equals native text-service parity, not a comparative performance benchmark.
- [VS Code's production buffer redesign](https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation) shows representation crossing and per-line metadata can dominate costs. [Zed's rope/sum-tree description](https://zed.dev/blog/zed-decoded-rope-sumtree) supports cheap snapshots and indexed summaries. Neither says changing widget libraries fixes parser semantics or glyph shaping.

### Measured capacity debt already exists

Reuse [the local evaluation](../large-file-ui-evaluation.md), Windows i9-12900H / 32 GiB / .NET 10.0.400, Avalonia 12.1.3, AvaloniaEdit 12.0.0, Native AOT. These are **assignment-to-visual-line-layout**, synchronous insertion and observed working set, not physical presentation or p95:

| Workload / projection | n | Assignment → visual lines | Middle insert | Working set |
| --- | ---: | ---: | ---: | ---: |
| 100 MiB ASCII lines, full mirror | 3 | 1,241.2 ms median | 42.4 ms median | 1,444 MiB |
| Same engine document, 256 KiB page | 3 | 9.6 ms median | 0.2 ms median | 359 MiB |
| 16 MiB unbroken line, full mirror | 1 | 3,583.1 ms | 12.0 ms | 2,646 → 4,480 MiB before/after insertion |

This rejects full-mirror promotion as the **general large-file default**, not ordinary few-MiB use. Smaller-file measurements exist in the original report. A no-newline file is not safely admitted by total-size threshold alone. The present Desktop page bridge breaks global selection/navigation semantics; adding companions does not repair that. Preserve current large-file capacity while evaluating alternatives; do not substitute a silent read-only mode or drop whole-document commands.

## 5. Reuse/rewrite map grounded in current components

| Component/file area | Retained native + relaxed package | Avalonia migration |
| --- | --- | --- |
| `Mote.Engine` (`Document`, snapshots, changes, history, I/O) | Keep unchanged | **Reuse unchanged**; canonical text, encoding, Save conflict/recovery remain here |
| `Mote.Formats` policies + incremental contracts/projections | Keep | **Reuse unchanged**; no TextMate semantic replacement, no LSP dependency |
| `Mote.Configuration`, `Mote.Themes` | Keep | Reuse typed loading, paths, role IDs, `ThemeComposer`; rewrite only platform brushes/typography/appearance application |
| `Mote.Telemetry` | Keep | Reuse schema/writer/privacy/causality; add toolkit receipt/render endpoints truthfully |
| `NativeFormatSessionDriver`, `NativeAnalysisDispatcher`, `NativeIdleFullAnalysis` | Keep | Extract/use serialized session and UI-dispatch responsibilities; do not revive Desktop string-slice `Analyze` as equivalent semantics |
| `NativeNavigationModel`, `NativeGridNavigation`, `Viewport/{ContinuousViewport,HorizontalViewport,SparseHeightIndex}` | Keep | Reuse global-coordinate state/tests; adapt measured layout/hit-testing. They are not automatically AvaloniaEdit's backing store |
| `NativeEditorController` and Save/Grid/Source partials | Keep source-candidate work with reviewed integration | Separate document/session orchestration from toolkit calls. Port generation/version admission, Save settlement, stale work rejection; do not copy a monolithic Window plus second controller |
| `NativeSourceContracts`, `NativeSourceBinding`, `NativeTextProjection` | Candidate exactness layer retained | Reuse identity/commit/failure semantics, redesign adapter records for exact UTF-16 Avalonia text. Native CRLF mapping is reusable evidence, not a mandatory no-op map on every platform |
| `WindowsEditorShell`, `MacEditorShell`, Source partials | Keep OS control ownership/input | Replace shell/platform control code with Avalonia adapter. Retain native executable for compatibility diagnostics during coexistence |
| DirectWrite/CoreText Canvas renderers and native input islands | Not changed by packaging | Replace source painter with editor surface; do not keep old source canvas and add another shaper under it. Custom snapshot-backed Avalonia surface is a separate high-risk option |
| `NativeFlowPresentation`, `NativeCsvGrid`, `NativePreviewBuilder` | Keep | Reuse projection data and bounded-interest/navigation rules; rewrite Avalonia visual realization, hit testing, and automation peers |
| `Accessibility/{AccessibleDocument,AccessibleSelection,AccessibleViewport}` | Keep | Reuse source/range state and stale-generation invariants; replace UIA/AX glue only after toolkit provider equivalence is demonstrated |
| Desktop `MainWindow.axaml`, `App.axaml`, `SemanticColorizer` | No role in retained release | Reuse styling/layout and semantic-colorizer concepts as scaffolding, not current Window behavior as product authority |
| Desktop `MainWindow.axaml.cs` / `Program.cs` | Remain prototype | **Rewrite composition/controller binding**: presently uses string-slice analysis budgets/manual pages, synchronous formatting, legacy trace spans, `--smoke-ui` and first-arg path handling |
| Packaging and diagnostic harnesses | Add bundle path/inventory/signing gates; keep native entry | New RID inventory + signed bundle/runtime lifecycle; retain native diagnostics rather than pretending new GUI proves old probe names |

Specifically, Desktop `ShowDocument` sets `_largeMode` above **8 Mi UTF-16 units**, materializes a **256 Ki-unit** source slice (line-boundary adjusted), assigns `Editor.Document.Text`, moves/clamps its caret to that local projection, and calls `UndoStack.ClearAll`. Prev/Next buttons and page-local control selection remain. In contrast, the in-flight native `NativeSourceInstallation` / binding identifies **a complete exact source replica** with generation/version/nonce; engine-originated changes use guarded `NativeSourceReplacement`, while native-originated admitted changes acknowledge the existing matching replica without reimporting it. The latter pattern is portable even though RichEdit newline mapping is platform-specific. Reusing Desktop's appearance does not justify reinstating its weaker source/policy/history path.

## 6. Minimum shared controller/adapter contract

This is an architectural target, not a demand to freeze new public interfaces now. The dominant workflow should have **one controller owning one document**, with a toolkit adapter owning the editable replica and ephemeral composition/layout only.

```text
Open(path, explicit encoding) → engine Document[generation, version]
    → install exact replica once → certify text/selection → editable
adapter composition → provisional native/toolkit state, no engine history item
settled edit[generation, version, installation nonce] → one engine transaction
    → acknowledge matching replica; publish analysis only for matching stamp
Undo/Redo/Format → engine transaction → guarded range update, no edit echo
Save receipt → settle composition → admit text → save one captured snapshot
replace/close → invalidate identities, cancel work, dispose session/document
```

Key invariants:

1. **One committed text/history authority.** AvaloniaEdit's `TextDocument` is a replica. Consume its concrete `DocumentChangeEventArgs` deltas, rather than full `.Text` read/diff on each normal key; route engine changes back as range replacements under an echo guard. Group committed IME/edit transactions according to the existing engine history contract. Intercept every Undo/Redo ingress (menu, shortcut, contextual command, automation), disable/clear competing control history at acknowledged boundaries, and test paste/drop/selection replacements. Clearing control undo is a tool, not by itself a complete history contract.
2. **Coordinates and identity are explicit.** UTF-16 code units, mixed newline preservation, surrogate safety, grapheme navigation, global selection. Reject stale nonce/generation/version proposals; never clamp a stale span into new source. Import failure leaves engine text recoverable and disables the bad replica. Unadmitted user text must not be silently thrown away before Save.
3. **The editor layout owns the caret/candidates/selection.** Do not implement custom Skia hit testing over a separately shaped text control. Composition/theme/document-replacement settlement must be coordinated; pending visual theme data must not force a text reimport.
4. **Scheduling and lifetime stay serialized.** Policy session access is single-lane; cancellation is advisory and current-generation/version checks authorize publication. Open returns a candidate document, and replaces the old one only after discard consent and current-request validation. Close invalidates posted callbacks; a late clipboard/analysis/open completion cannot mutate the next document.
5. **Bounded derived data is independent of exact source capacity.** Coverage/completeness, visible tokens, CSV interests and preview maps must retain their honest meanings. Full file copying into a control does not allow pretending full semantics was obtained.

Avoid introducing a generic UI framework adapter hierarchy with dozens of optional capabilities before it is needed. A small source edit/selection adapter plus window command/chrome adapter matches the actual responsibilities. Existing native-specific types can stay internal until extraction has one concrete Avalonia consumer; architectural improvement is not permission to break established APIs or data formats.

## 7. User-visible contracts that packaging/UI must preserve

| Contract | Required preservation / migration test |
| --- | --- |
| `mote [path]`, Open/New, one document per process | Correct quoting/unicode paths and current-directory semantics; opening another source does not create workspace/multi-tab canonical state |
| Established mode/diagnostic CLI | Keep native routes/exit conventions during side-by-side qualification. Do not make `--legacy-page`, canvas diagnostics or new `--native-source` launch Avalonia and claim equivalent evidence |
| `~/.mote` | Use existing selected home/config/cache/trace paths; no automatic rename to toolkit app-data conventions, no installation-directory writes |
| Theme IDs/roles/overrides | Preserve static IDs, 28-role map validation/contrast composition, nonfatal notices, live-system theme and Reload Settings semantics; font unit conversion must be explicit |
| File bytes/encoding | Same strict UTF-8/BOM and explicit-encoding behavior; invalid source/embedded NUL must not be silently truncated by a control; preserve CR/LF/CRLF unless explicit normalization |
| Save/Save As/history | Same conflict-confirmation token and recovery/export distinctions; Save snapshot v clears dirty only if still current; UI cannot add independent autosave or undo semantics |
| Tracing | Same opt-in local JSONL schema/path/privacy. Receipt, mutation, analysis publication, draw submission and physical presentation remain different endpoints; Avalonia layout/Opened is not physical paint |
| Preview/CSV | Same source-mapped generation/presentation authorization; visible cells and clipboard requests are not permission to reparse in visual callbacks or copy stale data |
| Large-file command semantics | Continuous global navigation/select/copy/find and honest partial-analysis status remain. Manual pages or new silent capacity limits are regressions, not packaging consequences |

During coexistence, distribute a clearly named opt-in candidate while `mote` retains native behavior. Both must share the same validated configuration and byte-preserving engine; do not run them concurrently writing the same trace file or assume duplicate GUI processes share document state. Rollback is changing the launch target, **not** converting user document files/config to a new format. Packaging paths may change, user-data schema need not.

## 8. Work packages, order, and relative risk

No calendar estimates are warranted without implementation measurements. These packages give estimable boundaries and go/no-go results:

| Package | Dependencies / deliverable | Relative implementation effort | Dominant risk / acceptance |
| --- | --- | --- | --- |
| P0: decision/contracts | Confirm allowed folder/`.app`, companions and CLI installation; inventory established behavior | Small | Requirement ambiguity; no UI work needed |
| P1: retained-native package | Bundle layout/metadata/CLI path; native dependency manifest; signing design | Small–medium | Trust credentials still absent; fresh launch/file activation gate remains |
| P2: controller extraction + ordinary replica | Toolkit-neutral session/Save/history orchestration, exact AvaloniaEdit edit adapter | Medium–large | Highest data-integrity risk: duplicate commits, stale completion, Save settlement |
| P3: input/automation qualification | Resolve editor preedit upstream or narrowly maintained fork; external UIA/AX and attended readers | Large / uncertain | Hard blocker for Chinese daily use; toolkit support does not imply editor parity |
| P4: themes and semantic previews | Typed role/appearance/reload adapter; source-mapped flow and bounded CSV visual/automation realization | Medium–large | Hidden feature loss if Desktop plain lists/text substitute for native semantic grids |
| P5: full capacity path | Measure few-MiB ordinary files; preserve current large-file behavior; design continuous bounded bridge only if needed | Large / high | Existing full-mirror RSS/long-line counterexample; global selection must survive rebinding |
| P6: runtime/package comparison | Same UI/corpus on AOT versus self-contained JIT, then R2R if useful | Medium | Confounding UI with runtime; complete native inventory/security servicing |
| P7: promotion/rollback | Closed regression matrix, diagnostics provenance, launch selection and user documentation | Medium | A green publish cannot substitute for input/presentation/Save reliability |

Execute P0/P1 independently of P2–P7. For the strategic UI, start with the cheapest decisive P3 check **before** broad preview porting: stock pinned preedit fails the source-level capability gate already. Qualify a maintained fix on target OSes and test one exact edit/Undo/Save/reopen chain through P2 before large visual rewrites. Do not run every package simultaneously or rewrite engine/policies to make the widget convenient.

Discriminating matrix for later authorized qualification:

- Ordinary structured files below 1 MiB and the user's few-MiB TXT journey; malformed source remains editable and byte-safe.
- Microsoft Chinese Pinyin and relevant third-party IME on Windows; Chinese input on macOS; commit/cancel, candidate geometry, click while composing, horizontal scroll, Find dialog, theme reload and Save/close while composing; emoji/combining/bidi and mixed newlines.
- External document text/selection/range reading and preview/CSV navigation; actual attended NVDA/VoiceOver coexistence.
- Cross-boundary global selection/find/copy and 100-MiB lines plus huge single line, retaining existing capacity semantics.
- Identical AOT/JIT data/visual path, fresh processes, cold/warm startup, open-to-editable, **presented** frame where measurable, edit-to-draw/presentation tails, allocation/RSS and trace overhead. Never compare toolkit AOT with native JIT and attribute all changes to one axis.

## 9. Academic connection and bounded conclusion

[Lipták, Masillo and Navarro, ESA 2024, *A Textbook Solution for Dynamic Strings*](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.ESA.2024.86) offers simpler augmented-splay dynamic-string operations with amortized bounds and probabilistic query correctness. It changes storage/query design options, not text shaping, per-line UI indexes, IME or accessibility. Treat it as a possible future engine experiment only if profiling identifies relevant query costs; mote's exact byte/history contract does not justify probabilistic equality shortcuts or an engine rewrite during a UI migration. This connects the frontier to production's actual lesson: text mutation, indexing, representation crossing and layout are separate costs.

**Conclusion:** relaxed delivery buys freedom to use standard macOS packaging and deployed renderer companions. It does not compel replacing the native shell, resolve the single-painter editing decision, or relax engine/policy/data compatibility. The lowest-risk useful step is retained native + standard bundle where allowed. Avalonia is a credible long-term shared-UI option with substantial existing scaffolding, but stock AvaloniaEdit's missing preedit and measured full-mirror capacity debt prevent an honest claim that it immediately removes the hard editor problems. Keep the native candidate progressing; authorize a narrow Avalonia input/history/AX qualification only when shared-UI maintenance benefit is explicitly worth the migration.
