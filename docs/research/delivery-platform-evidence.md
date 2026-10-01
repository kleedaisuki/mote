# Delivery platform evidence: packaging is not the execution model

Date: 2026-10-02. Repository inspection: `6827cd1a19142c9ad766d296c18d0770e600e79c`. Scope: compare delivery constraints without changing implementation, publishing, signing, running CI, committing, or pushing. This is research for a possible contract change, not authorization to change today's contract.

## Answer and decision implications

The lowest-risk relaxation is **permit a complete application package and selected native companions while retaining Native AOT as an available execution model**. A macOS `.app` is a directory presented as an application in Finder, not a requirement to use JIT or to install .NET. An AOT app inside `.app` can still have one application executable and only OS framework dependencies. Permitting sidecars creates additional implementation options; it does not require adopting every dependency or replacing the existing shell.

Three independent decisions must not be collapsed:

| Axis | Alternatives | What it decides |
| --- | --- | --- |
| Installed shape | Literal executable; portable directory; macOS `.app`; installer/package | File layout, integration, update/distribution operations |
| Execution | Native AOT; ordinary .NET JIT; .NET ReadyToRun with JIT fallback | Compilation restrictions, diagnostics, dynamic features, execution costs |
| Runtime ownership | App-contained; framework-dependent | Whether the user must supply compatible .NET; who services runtime security fixes |

**Allowing a bundled runtime is not requiring one particular runtime bundle.** Native AOT already incorporates required runtime functionality, including garbage collection; it is not C# with every runtime removed. The relevant distinction is “no separately installed .NET required” versus “no .NET runtime functionality whatsoever.” The latter is not today's implementation.

## Internal evidence inspected first

- `README.md`: strict one on-disk executable, no product-shipped native companions, static format registries; `Mote.Native` is the release target and Avalonia is a prototype. README expressly distinguishes hosted functional verification from release acceptance and records no Apple Developer account/publisher notarization.
- `src/Mote.Native/Mote.Native.csproj`: `net10.0`, `PublishAot=true`, `IsAotCompatible=true`, AOT/trim analyzers, no invariant globalization, references to the engine/formats/configuration/themes/telemetry. macOS embeds `Info.plist` with linker `-sectcreate` rather than using a bundle. No repository-root `Directory.Build.props` was found; the only discovered file with that name is under a benchmark subdirectory.
- `src/Mote.Native/Info.plist`: stable-looking application identifier, display/version fields, plain text/Markdown/JSON/CSV document claims. Metadata is not evidence of actual registered file activation or user defaults. TOML/YAML claims need separate design if the association list changes.
- `docs/architecture.md`, `docs/single-binary-feasibility.md`, `docs/macos-bare-binary-distribution.md`: distinguish bare GUI capability, signing, notarization, downloaded quarantine assessment, and document integration. Existing evidence supports literal binary feasibility but not publisher trust. The prior user's online-first acceptance is historical context, not a license to invent credentials.
- `packaging/README.md`: existing experimental Avalonia folder/`.app` packaging is explicitly non-release-compliant. Its prior Windows x64 experiment emitted Avalonia ANGLE, HarfBuzzSharp and SkiaSharp companions despite AOT. That is an observed historical configuration, not a current universal size/performance result. Those scripts are reusable prior art, not a ready production migration.

## Official execution-model evidence

### Native AOT

Microsoft describes AOT as self-contained, with no runtime JIT or requirement for preinstalled .NET, and explicitly says the app includes a stripped-down runtime. It supports Windows/macOS x64 and Arm64. Runtime assembly loading/code generation restrictions and mandatory trimming remain even if sidecars are allowed. Packaging relaxation therefore solves dependency layout constraints, not all library compatibility constraints. Source: [Native AOT overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/), updated 2026-01-08.

Native AOT can call native libraries via P/Invoke; supported interop configurations also permit direct calls and static linking. Thus “AOT” does not logically imply “no native companion.” If native dependencies are adopted, pin their ABI and inspect their actual loading behavior rather than inferring it from NuGet assets. Source: [Native AOT native interop](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop).

### Self-contained JIT and ReadyToRun

A self-contained .NET publication carries required .NET files and avoids a user runtime installation, but remains target-specific and depends on necessary OS-native prerequisites. It does not automatically roll forward to the machine's latest security-patched runtime; the publisher must ship an updated app. Framework-dependent publication needs compatible installed .NET and can roll forward to installed security patches. This is a separate option, **not recommended as mote's default consumer path** because missing/mismatched runtime becomes a first-launch failure. Source: [Publishing overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/), updated 2025-10-28.

ReadyToRun is not Native AOT: assemblies contain precompiled native code plus IL, and some methods still use JIT. Tiered compilation can replace precompiled code. It offers a startup optimization path while retaining the ordinary runtime but increases publish complexity and file size; mote has no measured comparative result establishing its benefit. Composite ReadyToRun adds still more coupling and is not a justified default here. Source: [ReadyToRun overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run), updated 2022-06-29.

### Diagnostics are a bounded difference, not absence of AOT observability

The official diagnostic guide documents optional Native AOT EventPipe support (`EventSourceSupport=true`), partial runtime event coverage, native debugging and platform CPU profiling. It lists managed heap analysis as unsupported in its documented support model; several implementation details are explicitly described for .NET 8, so this is not an independently tested blanket claim about every .NET 10 diagnostic tool. The full runtime offers richer managed diagnostic tools, making a matched development build useful when behavior transfers. Native AOT profiling/debugging benefits from matching symbols; retaining symbols in private CI artifacts does not require shipping them to users. RichEdit/TextKit and compositor costs still need OS-level profiling regardless of managed runtime choice. Source: [Native AOT diagnostics](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/diagnostics), updated 2025-04-04.

### “Single-file” JIT is not the current literal binary contract

Microsoft's single-file deployment can be self-contained or framework-dependent. By default native runtime binaries remain separate; `IncludeNativeLibrariesForSelfExtract=true` embeds them for extraction onto disk before execution. Extraction directory permissions matter for tamper resistance, and compression has workload-dependent startup costs. Thus one download file can become multiple runtime files: it does not satisfy a prohibition on self-extraction or sidecars. Source: [Single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview), updated 2026-03-25.

## macOS: integration and trust remain distinct

Apple's canonical bundle layout puts `Info.plist` at `Contents/Info.plist`, executable code in `Contents/MacOS`, resources in `Contents/Resources`, and native frameworks/libraries in `Contents/Frameworks`. Correct placement reduces signing/distribution surprises. Inference for mote: a bundle gives icons and document declarations a conventional home and a coherent user-visible install/move unit; receiving and correctly opening a document still needs implementation and real Finder testing. Source: [Apple bundle placement](https://developer.apple.com/documentation/bundleresources/placing-content-in-a-bundle).

Apple's notarization workflow allows tickets to be stapled to app bundles, disk images and flat installer packages. ZIPs cannot themselves be stapled, and standalone binaries cannot currently carry a stapled ticket. Online ticket lookup can recognize notarized bare code, whereas a stapled `.app` offers a route to ticket availability without a network lookup. This is not a guarantee against every offline trust failure or Gatekeeper rule. Source: [Apple custom notarization workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow).

Distribution trust requires a publisher-controlled identity. Apple specifies Developer ID Application signing for independent distribution, secure timestamps, hardened runtime for main executables, and inside-out signing of nested code. It advises against `codesign --deep` for signing complex products. Mote currently has no publisher credentials: neither `.app`, AOT, nor JIT removes that unresolved release gate. An ad-hoc development signature is not Developer ID trust. Source: [Apple distribution signing](https://developer.apple.com/documentation/xcode/creating-distribution-signed-code-for-the-mac).

Microsoft's macOS guidance distinguishes non-AOT apps requiring the `allow-jit` entitlement from AOT apps not requiring that execution entitlement. It also now documents publishing x64 and Arm64 separately and merging AOT Mach-O executables using Apple's `lipo`, then signing the merged result. This is an additional packaging possibility, not an observed mote result. Universal application design must cover every bundled native dependency's slices, not only the main executable. Source: [Publish .NET apps for macOS](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos), updated 2026-08-01.

**Access method:** Apple documentation pages were JavaScript shells in the browser tool. Their linked Markdown endpoint returned an unsupported-content-type error there. Full live official Markdown was retrieved successfully with PowerShell `Invoke-WebRequest` from `https://developer.apple.com/tutorials/data/documentation/<path>.md`; signing, stapling and placement passages were inspected. No new macOS execution or credential verification was performed.

## Windows: a portable executable remains an option

Windows packaging controls install/update/integration, independently of runtime bundling. Traditional Win32 apps can stay unpackaged and use portable directory distribution or an installer. MSIX can provide package identity, manifest integration and a managed update path; using it is not necessary merely because companion DLLs become permitted. Not all Win32 apps require app-container behavior. Source: [Windows packaging overview](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/).

Companion DLLs add loading-path risk. Microsoft recommends qualified paths or controlled `LOAD_LIBRARY_SEARCH` policies rather than searching attacker-controlled/current-document directories. Mote often opens arbitrary user documents, so “load beside whichever file was opened” is unacceptable. Validate with actual module-load observation and missing-dependency controls. Source: [DLL security](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-security), updated 2021-01-07.

Windows trust is not erased by AOT or portable delivery. Microsoft's current Smart App Control guidance requires valid signing certificates from trusted providers for its trusted-certificate path. Availability, eligibility and publisher ownership of a signing service are separate questions; no existing certificate or service access is assumed here. SmartScreen reputation and other enterprise policies are additional acceptance concerns, not equivalent to this signing criterion. Source: [Smart App Control signing](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control), updated 2026-09-28.

## Candidate choices and product effects (synthesis, not benchmark results)

| Candidate | User runtime install | New capabilities | Risks/costs | Decision position |
| --- | --- | --- | --- | --- |
| Current strict AOT executable | No | Simple copy/move, minimal product dependency layout | Restricts library choices; bare macOS ticket/integration limitations remain | Keep as baseline/portable option if valued |
| AOT executable + optional companions; macOS `.app` | No | Mature native dependency choices; canonical bundle metadata/resources; stapling route | ABI/version matching, dependency vulnerabilities, nested signing, whole-package updates | First relaxation to consider |
| Self-contained JIT folder/`.app` | No | Ordinary .NET dynamic features, fewer AOT compatibility restrictions, ordinary diagnostics | Runtime/dependency servicing, macOS JIT entitlement, package and launch costs need measurement | Alternative if a chosen component materially needs it |
| Self-contained ReadyToRun folder/`.app` | No | JIT compatibility plus precompiled startup paths | JIT still exists; extra size/build work; no measured mote win | Experiment only with a demonstrated startup problem |
| Framework-dependent app | Yes, unless guaranteed by managed deployment | Less duplicated runtime; runtime updates can be centralized | Missing/wrong runtime, environment-dependent behavior; poor consumer first launch | Separate managed-enterprise option, not default |

Allowing packages does not fix parser correctness, save reliability, IME, accessibility, virtual editor complexity or native presentation by itself. A bundle-only change retains the current UI and its bugs. A framework change would be a separate migration with its own user-workflow acceptance, not a consequence of the delivery decision.

## Concrete acceptance and maintenance implications

1. Preserve `mote path`, text/encoding round-trip, save safety, configuration paths and static format behavior across any packaging migration. Keep the CLI entry point discoverable if macOS installation moves it inside a bundle; do not break existing scripts by silently dropping CLI support.
2. First assess bundle-only AOT with no companions conceptually: it isolates delivery/integration effects from runtime/UI changes. If later authorized, use new opt-in profiles and do not change the current release contract before approval.
3. Every package needs a complete per-RID native dependency inventory and pinned versions/licenses. x64/Arm64 source portability is not binary interchangeability; test native builds for all four existing targets. Universal macOS is optional and must include matching native slices.
4. Prefer whole-version package replacement over copying new DLLs over a live version. Keep old/new dependencies from mixing; verify update signatures, handle interrupted installation, preserve user data outside the application package, and define rollback. These are engineering recommendations, not claims that existing packers implement an updater.
5. AOT and self-contained JIT both make the publisher responsible for redistributing runtime-related security fixes. Add dependency monitoring and repeat release publication on supported SDK/runtime security updates; a one-file layout does not eliminate this duty.
6. Fresh quarantined macOS first launch, Finder/Open With, document activation, icons, Dock identity, and offline ticket behavior require release-path checks on actual target machines. Without credentials, record trust as unverified, continue unsigned development where appropriate, and do not repeatedly ask for an account.
7. Benchmark only after an implementation is selected: download/installed size, first editable frame under cold/warm launch, ordinary file open/edit/save tails, retained memory, and dependency/extraction behavior. Use identical UI/workloads where comparing execution models. There is no defensible numerical startup or memory conclusion from these documentation sources alone.

The decisive product question is **whether one physical installed file is more valuable than conventional installation/integration and freedom to use vetted native components**. It is not whether to force JIT onto users. The most informative follow-up, if authorized, is a bounded AOT bundle-only release-shape prototype followed by comparative package inventory and real first-launch/integration evidence—not an immediate UI rewrite.
