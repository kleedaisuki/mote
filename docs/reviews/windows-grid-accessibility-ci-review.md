# Windows CSV Grid hosted diagnostic integration review

Date: 2026-10-01. Scope: only the uncommitted Windows Grid external diagnostic and upload steps in `.github/workflows/ci.yml`, and `docs/validation/windows-grid-accessibility-ci.md`. This review does not run hosted UIA or certify production accessibility.

## Final verdict

No remaining substantive integration issue was found after the targeted artifact-upload fix. This verdict covers workflow integration only, not live accessibility acceptance. Reviewed final workflow SHA256 after the exact checkout-encoding correction: `75C722AA32A1B12508C99D78D4E79C6C890C1DEF5991BDDD8BBAD99FC9E427FA`.

## Resolved necessary correction

### Resolved P2: Diagnostic artifact transport could fail the strict Native AOT job

Location: `Upload external Windows CSV Grid accessibility diagnostic`.

The execution step has `continue-on-error: true`, but its artifact upload does not. `if-no-files-found: warn` covers missing files, not service/network/action failures. Such a failure therefore fails the surrounding `native-aot` job even if all strict product tests passed. This contradicts the requested non-gating diagnostic boundary. Add `continue-on-error: true` to the new upload step as well. Confidence: high, direct workflow control-flow inspection. No actual upload failure was induced.

Disposition: the upload now has `continue-on-error: true` at `.github/workflows/ci.yml:620`. A targeted reread confirmed the fix and the matching validation-document wording. Broad parsing, builds and UIA execution were not repeated.

## Verified boundaries

- Parsed the complete workflow with Python/PyYAML; extracted the exact new diagnostic run block and parsed its PowerShell AST without execution. No parser errors. Scratch extraction: `.cache/windows-grid-accessibility-ci-validation/review-step.ps1`.
- Execution is restricted to `win-x64`, explicitly non-gating, with a five-minute Actions limit. Client execution has a 120-second wait; timeout kills that client process tree, then waits at most ten additional seconds.
- The executable path is the existing job's published `src/Mote.Native/bin/Release/net10.0/win-x64/publish/mote.exe`. The client is a separately built managed inspection tool, not a substitute editor. Both inventory and client independently hash that same executable; exact ordinal hash equality is required before success.
- Source pins independently matched reviewed bytes: `Program.cs` SHA256 `1AF72C383BB49ADD171AD8E6DADA7EE2BD012506262D5FBC292DA06B4453F6C7`; project SHA256 `5A442CDB96A250C26556165CABD5C58224378CD4573D6ECC13318DFCFF95F5E2`. The project treats compiler warnings as errors. Build output stays under repository `.cache/`; synthetic fixture and isolated child home stay under repository `.temp/`.
- Pinned client code creates only a synthetic 1100-by-32 CSV, removes child `MOTE_TRACE`, isolates child `MOTE_HOME`, and enables only the child opt-in. No clipboard access, global key injection, input-source changes or reader interaction. F6 is posted to the target window thread's focus; coordinate-dialog PID ownership is checked before mutation. Normal cleanup targets the exact launched editor process; outer timeout cleanup targets only the launched probe tree.
- Success requires an actual zero probe exit code and ordinal `pass` classification. In the pinned client, any collected Errors produce `product-fail`; any uncaught test exception produces `inconclusive` and nonzero exit. Missing JSON and binary-provenance mismatch fail the diagnostic. Thus the aggregation cannot silently convert its known Errors/Inconclusive into pass.
- Global semantic focus owned by another PID remains explicitly blocked, and its name/type is not inspected. A subset pass does not imply global focus, off-owner cell SetFocus, range writes, reader speech, IME, ARM64, large-file memory or multi-monitor acceptance.
- Uploaded paths are explicit report/inventory/build-log paths. Fixture/home files and built binaries are excluded. Retention is fourteen days; missing artifacts are warnings, not fabricated evidence.

## Limits

No live UIA/editor launch, full build, hosted run, external focus acceptance, timing benchmark or artifact-service outage was performed by this review. Existing client implementation was inspected only to assess the integration's safety, report aggregation and scope; it was not redesigned or changed. Production source and workflow were not modified by this reviewer.

## Hosted checkout pin correction review

Hosted run [36782853022](https://github.com/kleedaisuki/mote/actions/runs/36782853022), commit `6fada3e2f880d2d564cf597b6ac9ded849d81a19`, failed the old LF-only project pin before build/client execution. Independently inspected downloaded `.cache/ci-36782853022-grid-windows/inventory.json` and the hosted log: Program.cs matches the existing pin; project hash is `6A99955812E8EA1631792E3EF0B47798B42A1A9F916FE86AF6D992F2E32FA3D4`; `process_exit_code` is null and `timed_out` false. The log explicitly reports the reviewed-bytes exception before build. No Grid UIA success can be inferred from this run.

The narrow correction accepts only the two explicit project hashes using ordinal equality and correct `not LF AND not CRLF` rejection logic; the Program.cs condition remains unchanged. Independently derived exact LF and CRLF byte streams from the reviewed local project: LF reproduces `5A442CDB96A250C26556165CABD5C58224378CD4573D6ECC13318DFCFF95F5E2`; replacing each LF with CRLF reproduces the hosted hash exactly. No production or project content was changed. The workflow itself performs no newline normalization and therefore does not admit arbitrary mixed endings or modified source.

Evaluated the exact rejection predicate: both reviewed project hashes are accepted, while the changed-content project `<UseWPF>false` hash `8E14CECD7FB44BC2613B7F66BCBC60429152FD9B9BAE447C1C8E55567FCCE174` is rejected. Binary provenance, exit/classification checks, source pin, timeouts and non-gating upload remain intact. No remaining substantive issue was found in this narrow correction. A new hosted execution is still required; this review does not relabel the earlier rejection as a UIA pass.
## Windows ARM64 extension review (2026-10-01)

Verdict: no substantive issue found in the frozen three-file extension. This is approval of bounded, non-gating diagnostic integration, not ARM64 UIA acceptance. Reviewed workflow SHA256: `27B021817E81D0377BFB8EE71C1BDEC915E9EE8799F33393B5DA641FEB94D145`.

Scope: incremental diff of `.github/workflows/ci.yml`, `tests/WindowsGridExternalProbe/Program.cs`, and `docs/validation/windows-grid-accessibility-ci.md`; reused prior safety/classification review. No editor/client process was launched and no build was repeated.

- Both explicit Windows RIDs are admitted; the existing matrix assigns win-x64 to `windows-latest` and win-arm64 to `windows-11-arm`. Each diagnostic resolves that matrix child's already-published Native AOT executable at `src/Mote.Native/bin/Release/net10.0/$rid/publish/mote.exe`, never the other architecture or a JIT editor. Existing inventory/report hash equality remains unchanged.
- Build, report and synthetic fixture/home directories interpolate `$rid`; upload names and three exact upload paths interpolate the same matrix RID. Independent YAML inspection confirmed these conditions, both `continue-on-error: true` flags and the five-minute execution limit. Timeout/process-tree handling is unchanged: 120 seconds for the client, kill only that launched tree, at most ten seconds termination wait. No new clipboard/global input or user-file path was added.
- Independently derived new source LF SHA256 `4347749886467393FC82BC95A97EE8EBB4E9F2DA600DCF784EC6A88EE4E10E95` and CRLF SHA256 `5D6E7D538B53685C3C592D1174A00C49F2DF116F13A28E58E47183768233B0ED`; both match the exact workflow pins. Project content/pins remain unchanged. Each source/project predicate rejects unless one of its two exact ordinal hashes matches; no unknown-input normalization is introduced.
- Program.cs diff changes only RemainingGates metadata from unconditional `win-arm64` to `cross-RID parity beyond this one run`; executable assertions, result aggregation, cleanup and process ownership are unchanged. A passing ARM run cannot misleadingly call its own architecture untested; cross-RID parity and release/reader/focus acceptance are still not inferred.
- The client remains a portable managed Windows/WPF tool launched using the runner's `dotnet`, rather than a Native AOT WPF application. WPF has supported Windows ARM64 since .NET 6, per the [official WPF repository documentation](https://github.com/dotnet/dotnet/blob/main/src/wpf/README.md). Thus the .NET 10 native Windows ARM runner is a coherent target; actual desktop-runtime availability, chosen client architecture and external COM/UIA behavior still require the hosted execution. The client report already records `Architecture`, which should be audited with its RID/provenance rather than assuming an x64 local build proves ARM execution.
- Inspected existing `.cache/grid-arm-ci-validation/build.log`: local client build succeeded with zero warnings/errors. This is only compile evidence. Per-RID extracted PowerShell scripts exist in that directory; worker parser/negative-case checks are documented, not counted as live ARM acceptance by this reviewer.

No sources/workflow were changed, staged or committed by this reviewer. Retain each new hosted RID's report/inventory and independently check actual architecture, exact binary/source hashes, exit/timeout/classification and Errors/Inconclusive before reporting any hosted subset pass. Foreign global focus or refused off-owner cell SetFocus must retain their existing blocked status.
