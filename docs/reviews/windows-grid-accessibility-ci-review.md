# Windows CSV Grid hosted diagnostic integration review

Date: 2026-10-01. Scope: only the uncommitted Windows Grid external diagnostic and upload steps in `.github/workflows/ci.yml`, and `docs/validation/windows-grid-accessibility-ci.md`. This review does not run hosted UIA or certify production accessibility.

## Final verdict

No remaining substantive integration issue was found after the targeted artifact-upload fix. This verdict covers workflow integration only, not live accessibility acceptance. Reviewed final workflow SHA256: `907B2046A796D112F4B943C1B71125D43398FB206E41DB0EAA9ED3E7EAB0F038`.

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
