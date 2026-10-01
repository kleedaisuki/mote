"""Qualify a predeclared same-binary series without filtering rejected attempts.

The timing endpoint is sampled software, not physical input or presentation.
Incomplete/rejected series retain evidence but never produce an effect estimate.
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
import re
import statistics
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tests"))
from causal_save_trace_reader import MOTE_SAVE_CONTRACT, classify_requests
sys.path.insert(0, str(ROOT / "benchmarks" / "NativeAcceptance"))
from acceptance import audit, load_records

ORACLES = (
    "source_unchanged_before_save", "disk_oracle_passed", "undo_exact_oracle",
    "redo_exact_oracle", "undo_screen_distinct", "redo_screen_match",
    "source_specific_verified", "synthetic_source_removed",
)
IDENTITY = (
    "os", "cpu_name", "logical_processors", "physical_memory_bytes", "display_dpi",
    "canvas_client_width_px", "canvas_client_height_px", "primary_display_width_px",
    "primary_display_height_px", "roi_client_x_px", "roi_client_y_px", "roi_width_px",
    "roi_height_px", "original_sha256", "source_bytes", "profile", "endpoint",
    "activation_attempts_enabled", "local_topmost", "case", "repetition",
)
METRICS = (
    "input_ack_ms", "launch_to_source_ready_ms", "first_changed_capture_ms",
    "process_cpu_ms", "process_lifetime_ms",
)


def artifact_path(value: str, root: Path = ROOT) -> Path:
    """Reject redirects and paths outside the repository's evidence directory."""
    path = root / value
    allowed = root / ".cache"
    # Check lexical containment before resolve: traversal is not an accepted alias.
    if ".." in path.parts or not path.is_relative_to(allowed):
        raise ValueError("artifact_outside_repository_cache")
    for ancestor in (path, *path.parents):
        if ancestor.is_symlink() or ancestor.is_junction():
            raise ValueError("artifact_reparse_point")
        if ancestor == root:
            break
    if not path.resolve().is_relative_to(allowed.resolve()):
        raise ValueError("resolved_artifact_outside_repository_cache")
    return path


def number(value: object) -> bool:
    """Finite nonnegative measurements exclude bool and absent observations."""
    return type(value) in (int, float) and math.isfinite(value) and value >= 0


def trace_evidence(row: dict, root: Path) -> dict:
    """Audit retained normal-exit traces with the actual native Save contract."""
    names = row.get("trace_files")
    if (not isinstance(names, list) or any(not isinstance(name, str) for name in names) or
        type(row.get("trace_file_count")) is not int or
        type(row.get("trace_bytes")) is not int or row["trace_bytes"] < 0 or
        len(names) != row["trace_file_count"] or len(names) > 8 or len(set(names)) != len(names)):
        raise ValueError("trace_inventory_mismatch")
    paths = []
    byte_count = 0
    for name in names:
        path = artifact_path(name, root)
        size = path.stat().st_size
        if size > 32 * 1048576:
            raise ValueError("retained_trace_size_limit_exceeded")
        byte_count += size
        if size:
            with path.open("rb") as stream:
                stream.seek(-1, 2)
                if stream.read(1) != b"\n":
                    raise ValueError("normal_exit_trace_partial_row")
        paths.append(str(path))
    if byte_count != row.get("trace_bytes"):
        raise ValueError("trace_bytes_mismatch")
    try:
        records, _hashes = load_records(paths, discard_partial=False)
        evidence = classify_requests(records, MOTE_SAVE_CONTRACT, terminated=True)
    except (ValueError, OSError, UnicodeError) as error:
        raise ValueError("retained_trace_schema_or_privacy_invalid") from error
    if row["trace_mode"] == "off":
        if names:
            raise ValueError("disabled_trace_files_observed")
        return evidence
    integrity = audit([{**record, "parent_span_id": record.get("parent_span_id")}
                       for record in records], {})
    if integrity["causal_integrity"] != "pass":
        raise ValueError("retained_trace_causal_integrity_failed")
    requests = evidence["requests"]
    sessions = [record for record in records if record["operation"] == "mote.session"]
    if len(sessions) != 1 or sessions[0]["status"] != "success":
        raise ValueError("enabled_normal_session_unobserved_or_ambiguous")
    if len(requests) != 3 or sorted(request["saved_version"] for request in requests) != [1, 2, 3]:
        raise ValueError("expected_three_captured_save_versions_unobserved")
    if any(request["command_operation"] != "command.save" or
           not request["successful_chain_complete"] or
           request["coverage"] != "instrumented_chain_only" for request in requests):
        raise ValueError("native_save_chain_incomplete")
    if evidence["unlinked_positive_stages"] or evidence["dropped_records_observed"]:
        raise ValueError("native_save_chain_degraded")
    return evidence


def qualify_sample(row: dict, entry: dict, manifest: dict) -> None:
    """Require the original behavioral, foreground and frozen-binary controls."""
    if type(entry.get("driver_exit_code")) is not int or entry["driver_exit_code"] != 0:
        raise ValueError("driver_failed_or_exit_unobserved")
    sha = manifest["binary_sha256"]
    if (not isinstance(row.get("executable_sha256"), str) or
        row["executable_sha256"].lower() != sha or entry.get("binary_sha256_after") != sha):
        raise ValueError("binary_identity_changed")
    if row.get("trace_mode") != entry["mode"] or row.get("status") != "passed-foreground":
        raise ValueError("sample_failed_or_mode_mismatch")
    for key in ("hosted_desktop", "foreground_at_focus", "foreground_before_edit",
                "normal_exit_observed", "activation_attempts_enabled", *ORACLES):
        if row.get(key) is not True:
            raise ValueError("required_control_unobserved:" + key)
    for key in ("forced_cleanup", "negative_control_changed", "local_topmost"):
        if row.get(key) is not False:
            raise ValueError("required_negative_control_unobserved:" + key)
    if type(row.get("exit_code")) is not int or row["exit_code"] != 0:
        raise ValueError("target_normal_numeric_exit_unobserved")
    if (row.get("case") != "many-1" or type(row.get("repetition")) is not int or row["repetition"] != 1 or
        type(row.get("source_bytes")) is not int or row["source_bytes"] != 1024 * 1024 or
        not isinstance(row.get("original_sha256"), str) or
        row["original_sha256"].lower() != "5a900aeab7463e7b2fcf7481453882043dee41ca15f8a9bb8fa367e5c8c43e1f" or
        row.get("profile") != "fresh-default-home-canvas-experimental-crlf-sentinel"):
        raise ValueError("fixture_mismatch")
    expected_roi = {"roi_client_x_px": 42, "roi_client_y_px": 4, "roi_width_px": 256, "roi_height_px": 32}
    if any(type(row.get(key)) is not int or row[key] != value for key, value in expected_roi.items()):
        raise ValueError("roi_contract_mismatch")
    for key in ("logical_processors", "physical_memory_bytes", "display_dpi", "canvas_client_width_px",
                "canvas_client_height_px", "primary_display_width_px", "primary_display_height_px"):
        if type(row.get(key)) is not int or row[key] <= 0:
            raise ValueError("environment_identity_invalid:" + key)
    if any(row.get(key) is None for key in IDENTITY):
        raise ValueError("environment_identity_unavailable")
    for key in METRICS:
        if key == "process_cpu_ms" and row.get(key) is None and row.get("process_cpu_status") == "unavailable":
            continue
        if not number(row.get(key)):
            raise ValueError("measurement_unavailable_or_invalid:" + key)
    if row.get("process_cpu_ms") is not None and row.get("process_cpu_status") != "available":
        raise ValueError("cpu_status_mismatch")


def median_interval(values: list[float]) -> dict:
    """Return the narrowest order-statistic median interval with coverage >=95%."""
    ordered = sorted(values)
    n = len(ordered)
    ranks = [k for k in range(1, (n + 1) // 2 + 1)
             if 1 - 2 * sum(math.comb(n, j) for j in range(k)) / 2**n >= .95]
    if not ranks:
        return {"median": statistics.median(ordered), "interval": None,
                "interval_reason": "no_finite_distribution_free_95pct_interval_at_this_n",
                "maximum_finite_coverage": 1 - 2 / 2**n}
    k = max(ranks)
    return {"median": statistics.median(ordered), "interval": [ordered[k - 1], ordered[n - k]],
            "coverage": 1 - 2 * sum(math.comb(n, j) for j in range(k)) / 2**n,
            "ranks": [k, n - k + 1]}


def summarize(manifest: dict, entries: list[dict], loader, trace_loader) -> dict:
    """Stop at the first protocol breach; no partial series is promoted to inference."""
    result = {"schema_version": 1, "comparison": "incomplete", "reason": "declared_series_incomplete",
              "planned_pairs": manifest.get("planned_pairs") if isinstance(manifest, dict) else None,
              "attempted_samples": len(entries) if isinstance(entries, list) else 0,
              "completed_equivalent_pairs": 0, "paired_estimate": None, "samples": [],
              "absence_certified": False, "appkit_cost_measured": False}
    try:
        if not isinstance(manifest, dict) or not isinstance(entries, list):
            raise ValueError("invalid_manifest_or_index_shape")
        n = manifest["planned_pairs"]
        if type(n) is not int or not 1 <= n <= 30:
            raise ValueError("invalid_predeclared_pairs")
        if manifest.get("ordering") != "odd-off-on-even-on-off" or manifest.get("fixture") != "many-1":
            raise ValueError("manifest_protocol_mismatch")
        if not isinstance(manifest.get("binary_sha256"), str) or not re.fullmatch("[a-f0-9]{64}", manifest["binary_sha256"]):
            raise ValueError("invalid_binary_identity")
        if len(entries) > 2 * n:
            raise ValueError("extra_samples_not_predeclared")
        baseline = None
        rows = []
        for index, entry in enumerate(entries):
            if not isinstance(entry, dict):
                raise ValueError("invalid_index_entry_shape")
            pair = index // 2 + 1
            order = ("off", "on") if pair % 2 else ("on", "off")
            if type(entry.get("pair")) is not int or entry["pair"] != pair or entry.get("mode") != order[index % 2]:
                raise ValueError("attempt_order_mismatch")
            row = loader(entry)
            if not isinstance(row, dict):
                raise ValueError("invalid_target_report_shape")
            # Retain the actual failed report rather than synthesizing pass facts.
            sample = {"pair": pair, "mode": entry["mode"], "report": entry.get("report"),
                      "driver_exit_code": entry.get("driver_exit_code"), "observations": row}
            result["samples"].append(sample)
            qualify_sample(row, entry, manifest)
            identity = {key: row[key] for key in IDENTITY}
            if baseline is not None and identity != baseline:
                raise ValueError("environment_or_profile_changed")
            baseline = identity
            sample["causal_save_evidence"] = trace_loader(row)
            rows.append(row)
            if index % 2:
                result["completed_equivalent_pairs"] += 1
        if len(entries) != 2 * n:
            return result
        estimates = {}
        for metric in METRICS:
            if any(row[metric] is None for row in rows):
                estimates[metric] = {"median": None, "interval": None, "reason": "measurement_unavailable"}
                continue
            deltas = []
            for index in range(0, len(rows), 2):
                modes = {row["trace_mode"]: row for row in rows[index:index + 2]}
                deltas.append(modes["on"][metric] - modes["off"][metric])
            estimates[metric] = {**median_interval(deltas), "on_minus_off_deltas": deltas}
        result.update(comparison="qualified", reason=None, paired_estimate=estimates)
    except (ValueError, KeyError, TypeError, OSError) as error:
        # Owned artifact corruption is a retained rejection, not arbitrary text
        # in an outward performance claim. Fixed local codes are safe to retain.
        reason = str(error) if type(error) is ValueError and re.fullmatch("[a-z0-9_:]+", str(error)) else "malformed_or_unavailable_owned_evidence"
        result.update(comparison="not_qualified", reason=reason)
    return result


def main() -> int:
    """Update one series summary after each immutable index append; return stop status."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--series", required=True)
    args = parser.parse_args()
    series = artifact_path(args.series)
    def load(entry):
        """Exactly one target report is expected from each fresh driver interpreter."""
        report = artifact_path(entry["report"])
        if report.parent.parent != series:
            raise ValueError("report_outside_owned_series")
        rows = [json.loads(line) for line in report.read_text(encoding="utf-8-sig").splitlines()]
        if len(rows) != 1:
            raise ValueError("expected_one_report_per_attempt")
        if not isinstance(rows[0], dict) or not isinstance(rows[0].get("trace_files"), list):
            raise ValueError("invalid_target_report_shape")
        trace_directory = artifact_path(str(report.parent / "many-1-1"))
        actual = []
        if trace_directory.exists():
            for path in trace_directory.iterdir():
                path = artifact_path(str(path))
                if not path.is_file() or path.suffix != ".jsonl":
                    raise ValueError("unexpected_retained_trace_inventory")
                actual.append(path)
        if any(not isinstance(name, str) for name in rows[0]["trace_files"]):
            raise ValueError("invalid_retained_trace_inventory")
        claimed = [artifact_path(name) for name in rows[0]["trace_files"]]
        if set(actual) != set(claimed):
            raise ValueError("retained_trace_inventory_not_fully_declared")
        for name in rows[0].get("trace_files", []):
            if not artifact_path(name).is_relative_to(report.parent):
                raise ValueError("trace_outside_owned_sample")
        return rows[0]

    try:
        manifest_path = artifact_path(str(series / "manifest.json"))
        index_path = artifact_path(str(series / "index.jsonl"))
        manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
        entries = [json.loads(line) for line in index_path.read_text(encoding="utf-8-sig").splitlines()]
        result = summarize(manifest, entries, load, lambda row: trace_evidence(row, ROOT))
    except (ValueError, KeyError, TypeError, OSError) as error:
        result = {"schema_version": 1, "comparison": "not_qualified",
                  "reason": "malformed_or_unavailable_series_evidence", "planned_pairs": None,
                  "attempted_samples": None, "completed_equivalent_pairs": 0,
                  "paired_estimate": None, "samples": [], "absence_certified": False,
                  "appkit_cost_measured": False}
    # An unsafe summary target is never followed, even to report a rejection.
    output = artifact_path(str(series / "summary.json"))
    output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: result[key] for key in ("comparison", "reason", "attempted_samples", "completed_equivalent_pairs")}))
    return 2 if result["comparison"] == "not_qualified" else 0


if __name__ == "__main__":
    sys.exit(main())
