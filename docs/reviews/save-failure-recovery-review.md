# Save failure recovery independent review

Date: 2026-10-01. Reviewer scope: `Document.cs` Save changes, `Document.Recovery.cs`, `SaveFailureInfo.cs`, `DocumentSaveOperations.cs`, Save-related `NativeEditorController.cs` additions, `docs/save-failure-recovery.md`, and `SaveFailureRecoveryTests.cs`. No production or test edits were made. Review artifact ownership is this file only; isolated executable probes reside under `.temp/save-recovery-review/`.

## Summary

The principal fix is sound: a failed commit is no longer treated as proof of original-file preservation, potentially unique staged bytes are retained, cleanup errors are secondary, and successful commit followed by failed bookkeeping is separately classified. Existing external-change/hash checks and explicit Save As approval remain intact. No retry, target deletion, unchecked overwrite, metadata-ignore change, or disposal cleanup was introduced.

Two concrete integration issues were reported and resolved by the implementation owner. The reviewer re-inspected the corrective source changes and corresponding contract updates without repeating completed validation. No remaining substantive blocking defect was identified in the reviewed scope. Neither issue is evidence of observed user-data loss.

## Findings

### Resolved P2: macOS filename-equivalent target aliases did not share the advertised fixed slot

Location: `Document.Recovery.cs`, `GetSaveRecoveryPath`, initially line 24.

The filename is case-folded only on Windows. On a case-insensitive macOS filesystem, `report.txt` and `REPORT.txt` identify the same target but hash into distinct sidecar filenames. After the first document leaves failed recovery, another process/document using the other casing can stage/save rather than finding the reserved slot. The one-slot-per-target/restart bound therefore does not hold on that supported common platform. APFS also treats canonical normalization variants as equivalent: `é.txt` and `e` plus U+0301 plus `.txt` hash differently without normalization even if case-folding is added. Apple confirms these semantics in its [APFS FAQ](https://developer.apple.com/library/archive/documentation/FileManagement/Conceptual/APFS_Guide/FAQ/FAQ.html), while its current [files and directories overview](https://developer.apple.com/documentation/technologyoverviews/files-and-directories) confirms APFS is case insensitive by default.

Confidence: high for the source-level mapping; filesystem behavior was not executed on macOS in this Windows review environment.

Correction: normalize macOS names to canonical Form C and conservatively case-fold them too, with documented false contention on case-sensitive volumes, or determine actual filesystem case semantics. Conservative contention preserves bytes and is materially simpler than filesystem-specific identity machinery. Add a platform-aware mapping test. This does not purport to solve all symlink/hard-link aliasing.

Resolution inspected: `GetSaveRecoveryPath` now normalizes macOS filenames to Form C and folds case on both Windows and macOS. The contract documents conservative contention on case-sensitive volumes and expressly limits alias guarantees. No macOS runtime verification is implied by this source reinspection.

### Resolved P2: Incomplete retained recovery was sent through an export workflow that cannot succeed

Locations: `Document.Recovery.cs` `VerifyRecoveryAsync` (initial lines 84-90), `NativeEditorController.cs` `StartRecoveryExport` (initial lines 711-716).

When writing/fingerprinting is canceled or fails and stage cleanup also fails, recovery is intentionally retained with `Hash == null`. Both export and discard always reject that object. Native Save nevertheless promises a captured snapshot and opens the new-path export picker, so every attempt necessarily fails. Removing the sidecar manually also leaves the same live document's Save permanently blocked by pending ownership. Closing/reopening is currently necessary but not identified in this workflow. The incomplete bytes may be only a prefix, so describing them unconditionally as the attempted snapshot is also misleading.

Demonstrated: a 16 MiB document was canceled after stage creation with an injected cleanup IOException. The primary TaskCanceledException survived, `IsCompleteSnapshot` was false, export and discard both reported incomplete recovery, and Save remained blocked after manually removing the sidecar. See the reproducibility section.

Confidence: high; deterministic application-policy failure injection, not an OS-level reproduction.

Correction: branch on `IsCompleteSnapshot` before offering verified export, clearly label partial/unknown bytes and manual-copy handling, and explain whether a close/reopen is required. An optional explicit discard acknowledgement can clear pending ownership only after directly establishing the slot is Missing; preserve Unknown/changed bytes. Do not infer completion from an unverified surviving path or silently delete partial recovery.

Resolution inspected: Native export now checks `IsCompleteSnapshot`, refuses to open the picker for incomplete recovery, clearly labels the bytes unverified, and explains manual inspection/copy plus close/reopen. Close warnings likewise distinguish incomplete bytes. Explicit `DiscardSaveRecoveryAsync` now directly inspects the slot and acknowledges an observed Missing outcome without calling Delete, including incomplete ownership; Unknown and changed bytes remain pending. A new focused missing-stage test covers the acknowledgement. The policy probe results below describe the pre-correction implementation; they were not re-run merely to repeat completed validation.

## Nonblocking limitation: verification and pathname deletion are not atomic

`DeleteRecoveryAsync` finishes hash verification and then issues `File.Delete(path)` after its observation handle closes. An external writer/renamer can replace the path in between. Thus this protects against already-visible altered stage bytes, not arbitrary concurrent replacement through the deletion syscall. This is a real limitation, but no such incident was observed, and a portable compare-and-delete primitive is absent from this design. Avoid an unconditional claim that hash verification makes removal race-free; document the check-to-delete window just as the target check-to-replace window is documented.

Resolution of documentation concern: the contract now explicitly documents the check-to-delete window and disclaims adversarial concurrent directory-writer protection. This accepted scope limitation is not an open blocking finding.

A stronger Windows-specific solution would open with DELETE access while denying write/delete sharing, hash through that same handle, then set delete disposition on that handle only after validation. A portable quarantine/claim protocol adds names, crash states and contention rules and is not automatically simpler or safer. No redesign is demanded merely for theoretical perfection.

## Evidence and verification

- Internal knowledge reused: `docs/windows-atomic-save-investigation.md` and `docs/save-failure-recovery.md`.
- Microsoft primary contract: [ReplaceFileW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew) documents 1175 retaining the original names, 1176 with null backup allowing target absence while the replacement retains its name, and 1177 permitting the old target to survive under another name. These are possible outcomes, not reproduced native errors in this review.
- The implementation owner reported 47 focused Save/engine/regression tests already passing. The reviewer did not repeat those tests or run the whole suite.
- Review probe command: `dotnet run --project .temp/save-recovery-review/Probe.csproj`, referencing the already-built `src/Mote.Engine/bin/Debug/net10.0/Mote.Engine.dll`; no production build output was changed by the probe.
- Probe source/results: `.temp/save-recovery-review/Program.cs`, `Probe.csproj`, `results.txt`. All fixtures are inside that directory.

Observed probe results:

| Probe | Result |
| --- | --- |
| Commit throws while owned stage is held with Windows `FileShare.None` | Original exception identity preserved; stage classified Unknown; pending recovery retained |
| Release stage lock then export | Exact owned bytes exported; pending cleared |
| Cancel during 16 MiB stage write plus cleanup failure | Original TaskCanceledException preserved; incomplete pending recovery retained |
| Export/discard incomplete pending recovery | Both reject incomplete ownership |
| Manually remove incomplete sidecar then Save same document | Save still blocked by pending ownership |

## Remaining scope limits

No native physical-input workflow, macOS runtime filesystem probe, AOT deployment test, crash/power-loss durability experiment, filter-driver reproduction of 1175/1176/1177, or adversarial path-swap execution was performed. Existing classification tests establish application policy, not causality for the prior Windows incident. Later edits, Save As association, pre-existing-slot nonadoption, export nonoverwrite, hash rejection and disposal retention were assessed from the bounded production code and the focused tests, not independently repeated end-to-end UI runs.



## Final narrow delta: restart Open-failure discovery

The final review scope additionally includes only the `StartOpen` catch/message change, `OpenFailureMessage` helper, and `NativeSaveRecoveryTests.Native_restart_open_failure_hints_only_same_path_unowned_sidecar`.

Assessment: no substantive blocking issue found. The helper executes in the existing background Open-failure branch, computes only the same-target fixed sidecar path, and adds an explicitly unowned/unverified manual-inspection hint only when `File.Exists` returns true. It does not read content, create ownership, delete bytes, enumerate directories, retry Open, or alter successful Open. Standard path/filesystem hint errors are caught so they do not replace the original Open-error message. A false negative from `File.Exists` simply omits an optional hint; unlike outcome classification, no Missing or preservation claim is made from that false result.

The added test exercises the real controller with a fake shell: same-path missing target shows the sidecar path without content, leaves the untitled document unowned and bytes unchanged, another missing target does not inherit that hint, and subsequent successful Open remains unchanged despite the retained sidecar. The owner reported the focused test passed 1/1; the reviewer inspected its source and did not repeat execution. This is controller-level evidence, not a platform-native dialog or OS failure reproduction.

Final status: reviewed scope may be frozen. Both earlier P2 findings remain resolved; no new blocking finding was introduced by this delta.

## Integration delta: attempted-target export and best-effort failure evidence

Additional reviewed code: `SaveRecovery.AttemptedTargetPath`, lexical target guard in recovery export, inspection-provider boundary, pre-inspection pending registration, guarded exception annotation, widened secondary cleanup handling, and their focused engine tests. The owner reported 5/5 focused tests passed; no validation was repeated.

The attempted-target ambiguity is corrected: recovery records the attempted absolute target independently of the document's current `FilePath`. Export resolves its destination once and rejects the attempted target before creating anything, including a missing target after 1176 and Save As over a different approved target. Windows/macOS case aliases and macOS canonical Unicode aliases are rejected conservatively; filesystem identity aliases remain explicitly outside the lexical guarantee.

The failure-evidence masking issue is corrected within the engine Save path: pending ownership is registered before optional evidence work; both ordinary inspection and the injectable provider boundary convert non-OOM failures into Unknown secondary evidence; only an observed Missing stage clears owned pending; annotation writes and cleanup errors cannot supersede the primary nonfatal exception. The focused tests inspect original exception identity, retained bytes and ownership, and separate destination/association behavior.

### Follow-up consumer gap reported before freeze

The new rejecting-`Exception.Data` test establishes an engine provider boundary that the diagnostic consumers did not yet follow: `SaveFailureInfo.FromException` directly reads `exception.Data`, as does opt-in `MoteTelemetry.RecordSaveFailure` for phase extraction. With that same exception, Native's message construction can throw; when tracing is enabled the telemetry call inside Native's catch can fault the worker before its completion Post, leaving `_saving` true. This is not asserted to arise from ordinary built-in `File.Replace` exceptions; it is a concrete gap for the newly declared/custom-provider robustness contract.

Recommended closure: return null for unavailable optional structured evidence, and map unavailable telemetry phase to unknown (or otherwise protect the optional telemetry call so Native always reaches completion). The implementation owner and root were notified. Final resolution is recorded below when verified.

Consumer-gap corrective source review: `SaveFailureInfo.FromException` now returns null if a non-OOM evidence getter fails. `MoteTelemetry.RecordSaveFailure` guards only phase extraction and records unknown phase with the original numeric HResult, without persisting new exception text or paths. Both changes are appropriately bounded. The review scope expands only to this telemetry getter correction and the newly added rejecting-Data native/telemetry tests.

The native test verifies completion beyond merely observing text: after the failure is posted, a second Save exports pending recovery, demonstrating `_saving` is reset rather than permanently stuck. The telemetry test enables tracing, verifies `save.failure.unknown` and the exact original HResult, and asserts secret exception/data-access text is absent. Source-level follow-up finding is resolved. At this point the owner reported these newest tests were temporarily blocked by an unrelated shared Windows grid compilation error; no pass was claimed and the reviewer did not launch duplicate validation. Final execution evidence is recorded next when supplied.

Final execution evidence supplied by the implementation owner: the unrelated grid compilation blocker was corrected by its file owner. The filter `Failed_exception_annotation|Native_rejecting_failure_data|Save_failure_trace_rejecting_data` passed 3/3 in 169 ms on .NET 10, with Engine, Native, Telemetry and tests compiling successfully. The earlier attempted-target/provider-inspection focused delta passed 5/5. These are owner-run focused results, not independently repeated reviewer executions. The recovery contract records the corresponding evidence.

Final integrated review conclusion: the attempted-target export restriction, nonfatal failure-inspection/annotation preservation, and downstream evidence consumers now form a coherent contract. All concrete findings raised in this review are resolved within the documented scope. No remaining substantive blocking defect was identified. The Save failure recovery change may be frozen; earlier macOS runtime, native-platform/AOT, filesystem crash-durability, adversarial pathname-swap and real ReplaceFileW 1175/1176/1177 reproduction limitations remain unchanged.
