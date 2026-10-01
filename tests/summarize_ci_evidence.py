"""Summarize local diagnostic evidence without promoting non-gating probes.

Only fixed report identities and typed, allowlisted scalar fields are emitted.
Unknown report names, source text, file paths, exception messages, and raw traces
never enter the JSON or GitHub Actions Step Summary. A missing exit code is
unknown, never inferred from a report's claimed pass or the Actions green badge.
"""

import argparse
import json
from pathlib import Path

RIDS = ("win-x64", "win-arm64", "osx-x64", "osx-arm64")
STATUS = {"pass": "pass", "passed": "pass", "failed": "failed",
          "incomplete": "incomplete", "blocked": "blocked",
          "inconclusive": "inconclusive", "censored": "censored",
          "control-completed": "experiment-completed",
          "completed-success-observed": "experiment-completed-success-observed",
          "completed-failure-observed": "experiment-completed-failure-observed"}
EXIT_FIELDS = ("exit_code", "owner_exit_code", "child_exit_code", "target_exit",
               "clang_exit_code", "client_exit_code", "swift_exit_code")


def scalar_fields(data):
    """Copy only numeric exits and contract-relevant Boolean evidence."""
    result = {}
    for key in EXIT_FIELDS:
        if type(data.get(key)) is int:
            result[key] = data[key]
    for key in ("normal_exit", "reopen_normal_exit", "forced_cleanup",
                "owner_normal_exit", "source_unchanged", "source_file_unchanged",
                "editor_normal_exit", "editor_cleanup_forced", "input_sha256_unchanged"):
        if type(data.get(key)) is bool:
            result[key] = data[key]
    return result


def claimed_result(data):
    """Normalize known claims; unfamiliar claims cannot certify a pass."""
    if isinstance(data.get("status"), str):
        return STATUS.get(data["status"], "unrecognized")
    if type(data.get("passed")) is bool:
        return "pass" if data["passed"] else "failed"
    return "unrecognized"


def trace_health(data):
    """Keep trace integrity separate from document behavior and exit status."""
    trace = data.get("trace_evidence")
    if not isinstance(trace, dict):
        return "missing"
    if trace.get("causal_integrity") != "pass":
        return "unverified"
    if type(trace.get("dropped_records")) is not int:
        return "unverified"
    if trace["dropped_records"] < 0:
        return "unverified"
    return "no-observed-drops" if trace["dropped_records"] == 0 else "dropped"


def sample_evidence_health(sample):
    """Require an explicit normal outcome; missing cleanup flags prove nothing."""
    if sample.get("forced_cleanup") is True:
        return "censored"
    result = claimed_result(sample)
    if result not in ("pass", "failed") or sample.get("normal_exit") is not True:
        return "unverified"
    if result == "pass" and sample.get("reopen_normal_exit") is not True:
        return "unverified"
    return "observed"


def request_evidence_health(evidence):
    """Observe only explicit request terminals in a normally terminated session."""
    requests = evidence.get("requests")
    if not isinstance(requests, list) or not requests:
        return "unverified"
    if any(isinstance(request, dict) and request.get("classification") == "censored"
           for request in requests):
        return "censored"
    terminals = ("success", "failure", "cancelled", "skipped")
    if (evidence.get("normal_session_terminal_observed") is True and
            all(isinstance(request, dict) and request.get("classification") in terminals
                for request in requests)):
        return "observed"
    return "unverified"


def save_causal_evidence(sample):
    """Retain typed native Save-reader claims without copying provenance payloads."""
    evidence = sample.get("save_causal_evidence")
    result = {"status": "unverified"}
    if not isinstance(evidence, dict) or evidence.get("contract") != "native-save-causal-v1":
        return result
    if evidence.get("status") in ("complete", "incomplete", "censored", "unobserved", "invalid"):
        result["status"] = evidence["status"]
    requests = evidence.get("requests")
    if isinstance(requests, list):
        result["recorded_requests"] = len(requests)
    for key in ("absence_certified", "normal_exit", "terminated"):
        if type(evidence.get(key)) is bool:
            result[key] = evidence[key]
    return result


def summarize_report(identity, path):
    """Interpret a single fixed manifest member, retaining nested outcomes."""
    row = {"diagnostic": identity, "report_health": "missing",
           "result": "unknown", "nested": []}
    if not path.is_file():
        return row
    # Bound damaged reports and avoid an accidental giant source-file read.
    if path.stat().st_size > 8 * 1024 * 1024:
        row["report_health"] = "oversized"
        return row
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (ValueError, OSError, UnicodeError):
        row["report_health"] = "malformed"
        return row
    if not isinstance(data, dict):
        row["report_health"] = "unrecognized"
        return row
    row.update(scalar_fields(data))
    row["result"] = claimed_result(data)
    row["report_health"] = "present" if row["result"] != "unrecognized" else "unrecognized"
    if identity == "native-json-large":
        samples = data.get("samples")
        if not isinstance(samples, list):
            row["report_health"] = "unrecognized"
            return row
        row["expected_cases"] = 2
        row["observed_cases"] = len(samples)
        for sample in samples[:16]:
            if not isinstance(sample, dict):
                row["report_health"] = "unrecognized"
                continue
            nested = {"result": claimed_result(sample), "trace_health": trace_health(sample),
                      "save_causal_evidence": save_causal_evidence(sample)}
            nested.update(scalar_fields(sample))
            if type(sample.get("size_mib")) is int and sample["size_mib"] in (1, 100):
                nested["size_mib"] = sample["size_mib"]
            nested["evidence_health"] = sample_evidence_health(sample)
            row["nested"].append(nested)
        if (len(samples) != 2 or {s.get("size_mib") for s in row["nested"]} != {1, 100}
                or any(s["result"] != "pass" for s in row["nested"])) and row["result"] == "pass":
            row["result"] = "inconsistent-pass-claim"
    elif identity == "native-canvas-theme":
        workers = data.get("workers", [])
        if isinstance(workers, list):
            for worker in workers[:16]:
                if not isinstance(worker, dict):
                    continue
                nested = scalar_fields(worker)
                nested["result"] = ("pass" if worker.get("completed") is True and
                                    type(worker.get("exit_code")) is int and worker["exit_code"] == 0
                                    else "incomplete")
                if worker.get("phase") in ("dark-before", "light", "dark-after"):
                    nested["mode"] = worker["phase"]
                row["nested"].append(nested)
    elif identity == "causal-trace-recovery":
        cases = data.get("cases", [])
        if not isinstance(cases, list):
            row["report_health"] = "unrecognized"
            return row
        for case in cases[:16]:
            if not isinstance(case, dict):
                continue
            nested = {"result": claimed_result(case), **scalar_fields(case)}
            if case.get("mode") in ("held", "normal", "receipt-save", "receipt-save-as"):
                nested["mode"] = case["mode"]
            evidence = case.get("evidence", {})
            nested["evidence_health"] = "unverified"
            if isinstance(evidence, dict):
                nested["evidence_health"] = request_evidence_health(evidence)
                for key in ("normal_session_terminal_observed", "dropped_records_observed", "absence_certified"):
                    if type(evidence.get(key)) is bool:
                        nested[key] = evidence[key]
            row["nested"].append(nested)
    elif identity == "mac-grid-ax-external":
        swift = data.get("swift_report")
        checks = swift.get("checks") if isinstance(swift, dict) else None
        if isinstance(checks, list):
            row["recorded_checks"] = len(checks)
            row["passed_checks"] = sum(isinstance(check, dict) and check.get("passed") is True
                                       for check in checks)
            row["failed_checks"] = sum(isinstance(check, dict) and check.get("passed") is False
                                       for check in checks)
    elif identity == "mac-grid-showmenu-pair":
        sessions = data.get("sessions")
        if not isinstance(sessions, list):
            row["report_health"] = "unrecognized"
            return row
        for session in sessions[:16]:
            if not isinstance(session, dict) or session.get("session") not in ("C0", "P0"):
                continue
            nested = {"mode": session["session"], "result": claimed_result(session), **scalar_fields(session)}
            client = session.get("client")
            actions = client.get("actions") if isinstance(client, dict) else None
            if isinstance(actions, dict) and type(actions.get("original_error")) is int:
                nested["ax_reply"] = actions["original_error"]
            row["nested"].append(nested)
    return row


def manifest(root, rid):
    """List platform-applicable reports, including intentionally missing evidence."""
    inventory = root / ".cache" / "ci-inventory" / rid
    reports = [("native-json-large", inventory / "native-json-large.json"),
               ("causal-trace-recovery", root / ".cache" / ("causal-recovery-" + rid) / "report.json")]
    if rid.startswith("win-"):
        reports.extend((identity, inventory / (identity + ".json")) for identity in
                       ("native-continuous-win", "native-continuous-many100", "native-canvas-theme"))
        reports.append(("windows-source-range", root / ".cache" / "windows-uia-range-external" / "report.json"))
    else:
        reports.append(("native-continuous-mac", inventory / "native-continuous-mac.json"))
        reports.append(("mac-grid-action-contract", root / ".cache" / "ci-inventory" /
                        ("mac-grid-action-contract-" + rid + ".json")))
        reports.append(("mac-grid-ax-external", inventory / "mac-grid-ax-external.json"))
        reports.append(("mac-grid-showmenu-pair", root / ".cache" / "ci-inventory" /
                        ("mac-grid-showmenu-pair-" + rid + ".json")))
    return reports


def summarize(root, rid):
    """Produce deterministic evidence, without treating unrecognized reports as passes."""
    reports = manifest(root, rid)
    known = {path.resolve() for _, path in reports}
    inventory = root / ".cache" / "ci-inventory" / rid
    known.add((inventory / "evidence-summary.json").resolve())
    unknown = sum(path.resolve() not in known for path in inventory.glob("*.json"))
    return {"schema": "mote-ci-evidence-summary-v1", "rid": rid,
            "scope": "selected-non-gating-diagnostics-not-release-acceptance",
            "unrecognized_inventory_reports": unknown,
            "diagnostics": [summarize_report(identity, path) for identity, path in reports]}


def markdown(report):
    """Render allowlisted generated values, never arbitrary report strings."""
    lines = ["## Native diagnostic evidence / " + report["rid"],
             "Non-gating observations; a green job does not certify these diagnostics.",
             "Missing exit codes mean unknown, not zero. Trace no-observed-drops is not loss certification.",
             "", "| Diagnostic | Report health | Report result | Recorded exits | Nested evidence |",
             "|---|---|---|---|---|"]
    for row in report["diagnostics"]:
        exits = ", ".join(f"{key}={row[key]}" for key in EXIT_FIELDS if key in row) or "unknown"
        nested = "; ".join(f"{item.get('size_mib', item.get('mode', 'case'))}: {item['result']}, "
                           f"{item.get('evidence_health', 'unknown')}, trace={item.get('trace_health', 'not-applicable')}, "
                           f"exits={','.join(f'{key}={item[key]}' for key in EXIT_FIELDS if key in item) or 'unknown'}, "
                           f"AX-reply={item.get('ax_reply', 'not-applicable')}, "
                           f"Save-chain={item.get('save_causal_evidence', {}).get('status', 'not-applicable')}"
                           for item in row["nested"]) or "not-recorded"
        if "recorded_checks" in row:
            nested += f"; checks={row['passed_checks']}/{row['recorded_checks']}, failed={row['failed_checks']}"
        lines.append(f"| {row['diagnostic']} | {row['report_health']} | {row['result']} | {exits} | {nested} |")
    lines.append("\nUnrecognized inventory report count: " + str(report["unrecognized_inventory_reports"]))
    return "\n".join(lines) + "\n"


def main():
    """Write diagnostic JSON and optionally append the Actions Step Summary."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", choices=RIDS, required=True)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--step-summary", type=Path)
    args = parser.parse_args()
    report = summarize(args.root, args.rid)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    if args.step_summary:
        with args.step_summary.open("a", encoding="utf-8") as stream:
            stream.write(markdown(report))
    print(markdown(report))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
