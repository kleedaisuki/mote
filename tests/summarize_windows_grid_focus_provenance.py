"""Report independent hosted client/server witnesses without cross-process joins.

Only closed metadata enters this summary. Raw reports, logs, homes and traces
remain retained even when classification fails. This is not product acceptance.
"""

from __future__ import annotations

import argparse
from datetime import datetime
import json
from pathlib import Path
import re

from windows_grid_focus_trace_reader import read_focus_paths, _STRICT


LABELS = """discover initial_uia query_1000 select_first add_second refuse_sparse
tree_raw tree_control tree_content navigation_reads f6_once goto_once distant_read
cell_focus_once""".split()
PANES = set("unavailable none source table row_scroller column_scroller coordinate owned_other outside".split())
ERRORS = {None, "element_unavailable", "invalid_operation", "com", "argument", "io", "access", "other"}
REPORT_KEYS = set("""Schema Architecture CrossProcessEdge FocusBoundary AbsenceCertified
Classification Boundary BinarySha256 FixtureSha256 FixtureAfterSha256 FixtureUnchanged
IdentityCertified F6SuccessorRelation Operations Exception HResult CloseRequested
ForcedCleanup EditorExitCode CleanupException CleanupHResult""".split())
OP_KEYS = set("""Receipt TerminalParentReceipt Label BeginUtc EndUtc OwnerQueuePaneBefore
OwnerQueuePaneAfter Outcome ActionAttempted Exception HResult ObservationException ObservationHResult""".split())
SUPERVISOR_KEYS = set("""schema_version rid binary_sha256 binary_sha256_after
client_source_sha256 client_project_sha256 build_exit_code client_exit_code
timed_out job_empty cleanup_forced error_class""".split())


def require(condition):
    """Reject malformed owned evidence without disclosing its text."""
    if not condition:
        raise ValueError("invalid_owned_evidence")


def integer(value, nullable=False):
    """HRESULTs and exits are signed int32 observations, never bools."""
    return (nullable and value is None) or (type(value) is int and -(2**31) <= value < 2**31)


def hash_value(value, nullable=False):
    """Hashes remain identity checks; they are not displayed or joined to spans."""
    return (nullable and value is None) or (isinstance(value, str) and re.fullmatch(r"[0-9a-fA-F]{64}", value) is not None)


def client_projection(report, supervisor):
    """Validate the exact client DTO, retaining outcomes apart from query health."""
    require(isinstance(report, dict) and set(report) == REPORT_KEYS)
    require(type(report["Schema"]) is int and report["Schema"] == 1)
    require(report["Architecture"] == {"win-x64": "X64", "win-arm64": "Arm64"}[supervisor["rid"]])
    require(report["CrossProcessEdge"] == "unjoined" and report["FocusBoundary"] == "owner_gui_queue")
    require(report["AbsenceCertified"] is False)
    require(report["Classification"] in ("observed", "incomplete"))
    require(report["Boundary"] in ("open", "censored", "normal-exit-observed", "nonzero-exit-observed"))
    require(report["F6SuccessorRelation"] in (None, "matches", "differs"))
    for key in ("FixtureUnchanged", "IdentityCertified", "CloseRequested", "ForcedCleanup"):
        require(type(report[key]) is bool)
    for key in ("BinarySha256", "FixtureSha256", "FixtureAfterSha256"):
        require(hash_value(report[key], nullable=True))
    require(hash_value(report["BinarySha256"]) and report["BinarySha256"].lower() == supervisor["binary_sha256"].lower())
    for key in ("Exception", "CleanupException"):
        require(report[key] in ERRORS)
    for key in ("HResult", "CleanupHResult", "EditorExitCode"):
        require(integer(report[key], nullable=True))
    for error, code in (("Exception", "HResult"), ("CleanupException", "CleanupHResult")):
        require((report[error] is None) == (report[code] is None))
    if report["FixtureUnchanged"]:
        require(hash_value(report["FixtureSha256"]) and report["FixtureSha256"] == report["FixtureAfterSha256"])
    normal = report["EditorExitCode"] == 0 and not report["ForcedCleanup"]
    require((report["Boundary"] == "normal-exit-observed") == normal)
    if report["Boundary"] == "nonzero-exit-observed":
        require(report["EditorExitCode"] is not None and report["EditorExitCode"] != 0 and not report["ForcedCleanup"])
    require(not report["ForcedCleanup"] or report["Boundary"] == "censored")
    operations = report["Operations"]
    require(isinstance(operations, list) and len(operations) <= len(LABELS))
    projected = []
    query_unknown = False
    for index, operation in enumerate(operations):
        require(isinstance(operation, dict) and set(operation) == OP_KEYS)
        require(type(operation["Receipt"]) is int and operation["Receipt"] == index + 1)
        require(operation["Label"] == LABELS[index])
        require(operation["Outcome"] in ("entered", "returned", "threw", "not_attempted"))
        require(type(operation["ActionAttempted"]) is bool)
        require(operation["ActionAttempted"] == (operation["Outcome"] in ("returned", "threw")))
        if operation["Outcome"] == "not_attempted":
            require(operation["ObservationException"] is not None and operation["Exception"] is None)
        for key in ("OwnerQueuePaneBefore", "OwnerQueuePaneAfter"):
            require(operation[key] in PANES)
        for key in ("Exception", "ObservationException"):
            require(operation[key] in ERRORS)
        for key in ("HResult", "ObservationHResult"):
            require(integer(operation[key], nullable=True))
        for error, code in (("Exception", "HResult"), ("ObservationException", "ObservationHResult")):
            require((operation[error] is None) == (operation[code] is None))
        require((operation["Exception"] is not None) == (operation["Outcome"] == "threw"))
        require(isinstance(operation["BeginUtc"], str))
        datetime.fromisoformat(operation["BeginUtc"].replace("Z", "+00:00"))
        if operation["Outcome"] == "entered":
            require(operation["EndUtc"] is None and operation["TerminalParentReceipt"] is None)
        else:
            require(type(operation["TerminalParentReceipt"]) is int and operation["TerminalParentReceipt"] == index + 1)
            require(isinstance(operation["EndUtc"], str))
            datetime.fromisoformat(operation["EndUtc"].replace("Z", "+00:00"))
        query_unknown |= operation["ObservationException"] is not None or (
            operation["Label"] != "discover" and "unavailable" in
            (operation["OwnerQueuePaneBefore"], operation["OwnerQueuePaneAfter"]))
        projected.append({key: operation[key] for key in OP_KEYS - {"BeginUtc", "EndUtc"}})
    if report["Classification"] == "observed":
        require(len(operations) == len(LABELS) and all(op["Outcome"] in ("returned", "threw") for op in operations))
        require(all(op["Outcome"] == "returned" for op in operations[:-1]))
        require(report["IdentityCertified"] and report["Exception"] is None and report["CleanupException"] is None)
    return {"status": "unknown" if query_unknown else report["Classification"],
            "declared_classification": report["Classification"], "boundary": report["Boundary"],
            "editor_exit_code": report["EditorExitCode"], "forced_cleanup": report["ForcedCleanup"],
            "fixture_unchanged": report["FixtureUnchanged"], "identity_certified": report["IdentityCertified"],
            "f6_successor_relation": report["F6SuccessorRelation"], "operations": projected,
            "error_class": report["Exception"], "cleanup_error_class": report["CleanupException"],
            "query_health": "unknown" if query_unknown else "observed"}


def validate_supervisor(value):
    """Check independently retained supervisor exits and ownership witnesses."""
    require(isinstance(value, dict) and set(value) == SUPERVISOR_KEYS)
    require(type(value["schema_version"]) is int and value["schema_version"] == 1)
    require(value["rid"] in ("win-x64", "win-arm64"))
    for key in ("binary_sha256", "binary_sha256_after", "client_source_sha256", "client_project_sha256"):
        require(hash_value(value[key], nullable=True))
    for key in ("build_exit_code", "client_exit_code"):
        require(integer(value[key], nullable=True))
    for key in ("timed_out", "job_empty", "cleanup_forced"):
        require(type(value[key]) is bool)
    require(value["error_class"] in (None, "preflight", "build", "supervisor", "control", "timeout", "cleanup", "client", "binary_changed"))
    return value


def summarize(directory):
    """Classify after supervisor termination; failures never remove raw evidence."""
    result = {"schema_version": 1, "status": "unknown", "error_class": None,
              "client": {"status": "unknown", "editor_exit_code": None},
              "server": {"status": "unknown", "observations": []},
              "client_exit_code": None, "supervisor_job_empty": None,
              "cross_process_edge": "unjoined", "absence_certified": False,
              "foreground_certified": False, "physical_keyboard_certified": False,
              "product_pass_certified": False}
    try:
        directory = _STRICT.artifact_path(str(directory), ".cache")
        supervisor = validate_supervisor(load_json(directory / "supervisor.json"))
        result["client_exit_code"] = supervisor["client_exit_code"]
        result["supervisor_job_empty"] = supervisor["job_empty"]
        result["error_class"] = supervisor["error_class"]
    except (ValueError, TypeError, KeyError, OSError):
        result["error_class"] = "invalid_supervisor_evidence"
        return result
    try:
        result["client"] = client_projection(load_json(directory / "report.json"), supervisor)
    except (ValueError, TypeError, KeyError, OSError):
        result["client"]["error_class"] = "invalid_or_missing_client_report"
    # Enumerate only the isolated retained home, never paths supplied by a report.
    try:
        trace_root = _STRICT.artifact_path(str(directory / "scratch/mote-home/traces"), ".cache")
        paths = sorted(trace_root.glob("*.jsonl")) if trace_root.exists() else []
        require(len(paths) <= 16)
        result["server"] = read_focus_paths(paths, terminated=supervisor["job_empty"])
        if not supervisor["job_empty"]:
            result["server"]["status"] = "unknown"
    except (ValueError, TypeError, KeyError, OSError):
        result["server"] = {"status": "unknown", "observations": [], "error_class": "invalid_server_trace"}
    healthy = (supervisor["job_empty"] and not supervisor["timed_out"] and not supervisor["cleanup_forced"]
               and supervisor["build_exit_code"] == 0 and supervisor["client_exit_code"] == 0
               and supervisor["error_class"] is None and hash_value(supervisor["binary_sha256"])
               and supervisor["binary_sha256_after"] == supervisor["binary_sha256"])
    client = result["client"]
    if healthy and client["status"] == "observed" and client["editor_exit_code"] == 0 and not client["forced_cleanup"] and client["fixture_unchanged"] and client["boundary"] == "normal-exit-observed" and result["server"]["status"] != "unknown":
        result["status"] = "observed"
    elif supervisor["timed_out"] or supervisor["cleanup_forced"] or client["status"] == "incomplete":
        result["status"] = "incomplete"
    return result


def load_json(path):
    """Bound and reject links before reading an owned JSON artifact."""
    path = _STRICT.artifact_path(str(path), ".cache")
    require(path.is_file() and path.stat().st_size <= 4 * 1024 * 1024)
    return json.loads(path.read_text(encoding="utf-8-sig"))


def run_cli(args):
    """Write a fixed-schema summary and optional safe Actions Step Summary."""
    directory = _STRICT.artifact_path(args.directory, ".cache")
    result = summarize(directory)
    directory.mkdir(parents=True, exist_ok=True)
    output = _STRICT.artifact_path(str(directory / "summary.json"), ".cache")
    with output.open("x", encoding="utf-8") as handle:
        handle.write(json.dumps(result, indent=2) + "\n")
    if args.step_summary:
        # GitHub supplies this path; no artifact field may select a summary target.
        with Path(args.step_summary).open("a", encoding="utf-8") as handle:
            handle.write("\n## Independent Windows Grid focus provenance\n")
            handle.write(f"Evidence status: **{result['status']}**; client: **{result['client']['status']}**; server: **{result['server']['status']}**.\n\n")
            handle.write(f"Actual client exit: {result['client_exit_code']}; actual editor exit: {result['client']['editor_exit_code']}; owned job empty: {result['supervisor_job_empty']}.\n\n")
            handle.write("Cross-process edge: **unjoined**. Client observation is not product acceptance. No callback-absence, foreground, physical keyboard, or complete provider-entry certificate.\n")
    print(json.dumps({key: result[key] for key in ("status", "client_exit_code", "cross_process_edge")}))
    return 0 if result["status"] == "observed" else 1


def main():
    """Contain classifier/output failures using fixed outward error classes."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", required=True)
    parser.add_argument("--step-summary")
    args = parser.parse_args()
    try:
        return run_cli(args)
    except (ValueError, TypeError, KeyError, OSError):
        print(json.dumps({"status": "unknown", "error_class": "unsafe_or_unavailable_summary_output",
                          "cross_process_edge": "unjoined"}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
