# Windows external source-range CI review

## Scope and verdict

Independent review on 2026-10-01 of the uncommitted addition to
`.github/workflows/ci.yml` invoking `tests/WindowsUiaRangeExternal.ps1` in the
`win-x64` and `win-arm64` Native AOT jobs. The reviewer inspected the existing
range implementation review and external validation notes first, then the exact
workflow diff, complete probe script, and surrounding matrix/publish inventory.
No workflow, script, or product source was modified by the reviewer.

**No substantive issue found in the reviewed addition.** This is CI wiring and
safety review, not new hosted execution or release acceptance.

## Checked contracts

| Property | Evidence |
| --- | --- |
| Exact script identity | Independently recomputed SHA-256 for LF `933BD36094C7168CA1B43A08021331732B72BD8AB902482D1F76C63C0980D12D` and CRLF `B7F6CD28BC77F2A04E15271E3C03F47D2E3F672B45C2FB769A9EADC1EA1DBA00`; both equal the workflow pins. Current checkout is the LF form. |
| Platform and binary | `startsWith(matrix.rid, 'win-')` admits the two existing native-architecture Windows runners, not macOS. The executable path selects the existing published Native AOT `mote.exe`, with no experimental launch flag. |
| Syntax | Complete workflow loaded through PyYAML; existing probe parsed through the PowerShell language parser with no errors. PyYAML parsing is not a full Actions schema validation. |
| Failure propagation | Extracted the actual run block, substituting only matrix RID and child invocation. Synthetic child exits 0 and 7 produced wrapper exits 0 and 1 respectively. The latter retained the failed-child exit in its exception message. Exact pin check remained active in both controls. |
| Diagnostic classification | `continue-on-error: true` intentionally makes the experiment non-gating, but does not suppress its nonzero step outcome or script report. A green job is not evidence of a passed range probe. |
| Artifacts | Upload uses `always()` plus Windows predicate, dedicated `windows-source-range-${{ matrix.rid }}` names, and the script's exact `report.json` location, with 14-day retention. Each matrix job has its own checkout/runner, so the fixed script report path does not collide across RIDs. Missing reports remain a warning, not a fabricated pass. |
| Bounds | Outer step limit is five minutes; independently owned UIA client limit is 60 seconds by default (validated configurable 1–120), plus bounded normal target-close waits. Client build occurs before editor launch. Runner cancellation cannot guarantee execution of PowerShell finally; it is not ordinary-close evidence. |
| Ownership | Fixture and isolated `MOTE_HOME` are under repository `.temp/windows-uia-range-external`; generated external client project/build/report are under `.cache/windows-uia-range-external`. Discovery is filtered to the launched PID. Normal close and fallback kill operate only on the process the script launched. |
| No global mutations | Script contains no registry writes, global keyboard input, clipboard calls, explicit foreground/focus mutation, or reads/writes of user document paths. Source unchanged status and forced cleanup are retained in the final report. |
| Delivery isolation | Generated WPF client and its managed build sidecars stay under `.cache`, never in the product publish directory. The existing strict single-binary inventory remains unchanged. |

The local synthetic exit controls are retained under
`.temp/windows-range-ci-review/exit0.ps1` and `exit7.ps1`; they do not launch an
editor or external UIA client. `git diff --check` passed for the reviewed work.

## Coverage limits and acceptance interpretation

This review does not rerun the already recorded local original-source Native AOT
range test, and does not establish Windows ARM64 hosted behavior. The existing
[external range validation](../validation/windows-uia-range-external.md) records
local exact endpoint navigation, Select/GetSelection, clone independence, normal
close, stale-range rejection, and unchanged source. The next hosted run must
independently demonstrate those same downstream assertions on each RID. A timeout,
missing report, failed Select, or forced cleanup cannot be reported as a full pass.

The probe is deliberately a small synthetic contract check. It does not establish
screen-reader speech, physical IME behavior, range geometry, or complete UIA
pattern coverage. Its acceptance must stay distinct from those gates.

## Primary references

- [GitHub Actions workflow syntax: shell, continue-on-error and step timeout](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)
- [Official upload-artifact documentation](https://github.com/actions/upload-artifact/blob/main/README.md)
- [Existing independent range implementation review](windows-uia-range-review.md)
