"""Portable report/graph/ownership-contract fixtures; never run GUI or native APIs."""

import copy
import json
from pathlib import Path
import tempfile
import unittest
import subprocess
import sys

from summarize_windows_grid_focus_provenance import LABELS, client_projection, summarize, validate_supervisor


ROOT = Path(__file__).resolve().parents[1]
AREA = ROOT / ".cache/validation/windows-grid-focus-workflow"


def supervisor():
    """Successful supervisor observations, independent of client/server facts."""
    return {"schema_version": 1, "rid": "win-x64", "binary_sha256": "a" * 64,
            "binary_sha256_after": "a" * 64, "client_source_sha256": "b" * 64,
            "client_project_sha256": "c" * 64, "build_exit_code": 0, "client_exit_code": 0,
            "timed_out": False, "job_empty": True, "cleanup_forced": False, "error_class": None}


def client():
    """Exact serialized DTO with private UTC retained only in raw evidence."""
    operations = [{"Receipt": index + 1, "TerminalParentReceipt": index + 1, "Label": label,
                   "BeginUtc": "2026-10-01T00:00:00+00:00", "EndUtc": "2026-10-01T00:00:01+00:00",
                   "OwnerQueuePaneBefore": "unavailable" if index == 0 else "source",
                   "OwnerQueuePaneAfter": "source", "Outcome": "returned", "ActionAttempted": True,
                   "Exception": None, "HResult": None, "ObservationException": None,
                   "ObservationHResult": None} for index, label in enumerate(LABELS)]
    return {"Schema": 1, "Architecture": "X64", "CrossProcessEdge": "unjoined",
            "FocusBoundary": "owner_gui_queue", "AbsenceCertified": False,
            "Classification": "observed", "Boundary": "normal-exit-observed", "BinarySha256": "a" * 64,
            "FixtureSha256": "d" * 64, "FixtureAfterSha256": "d" * 64, "FixtureUnchanged": True,
            "IdentityCertified": True, "F6SuccessorRelation": "differs", "Operations": operations,
            "Exception": None, "HResult": None, "CloseRequested": True, "ForcedCleanup": False,
            "EditorExitCode": 0, "CleanupException": None, "CleanupHResult": None}


def trace(result="applied", relation="owner", after="table"):
    """Explicit native receipt/terminal/session edges, not client timing joins."""
    def row(span, operation, parent=None, status="success", **facts):
        return {"schema_version": 1, "utc_time": "2026-10-01T00:00:00Z", "session_id": "2" * 32,
                "trace_id": "3" * 32, "span_id": f"{span:016x}", "parent_span_id": f"{parent:016x}" if parent else None,
                "operation": operation, "status": status, "duration_us": 0, "attributes": facts}
    facts = {"native_thread_relation": relation, "managed_admission_relation": "owner",
             "focus_before": "source", "focus_target": "cell"}
    return [row(2, "native.grid.focus.adapter.received", 1, **facts),
            row(3, "native.grid.focus.adapter", 2, status="success" if result in ("applied", "no_change") else "failure",
                **facts, focus_after=after, focus_result=result), row(1, "mote.session")]


class SummaryTests(unittest.TestCase):
    """Missing runtime evidence and unknown query health cannot become green claims."""

    def setUp(self):
        """Keep fixtures inside the checkout, with one fresh retained home per case."""
        AREA.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=AREA)
        self.directory = Path(self.temp.name)
        self.addCleanup(self.temp.cleanup)

    def write(self, name, value):
        """Write only owned fixture JSON, never product data."""
        (self.directory / name).write_text(json.dumps(value), encoding="utf-8")

    def seed(self):
        """A complete client report does not require an invented server receipt."""
        self.write("supervisor.json", supervisor())
        self.write("report.json", client())

    def write_trace(self, records):
        """Retain raw native records in the actual MOTE_HOME layout."""
        path = self.directory / "scratch/mote-home/traces/native.jsonl"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("".join(json.dumps(row) + "\n" for row in records), encoding="utf-8")

    def test_complete_client_is_observed_not_product_pass_and_server_unobserved(self):
        self.seed()
        result = summarize(self.directory)
        self.assertEqual("observed", result["status"])
        self.assertEqual("unobserved", result["server"]["status"])
        self.assertEqual("unjoined", result["cross_process_edge"])
        for key in ("absence_certified", "foreground_certified", "physical_keyboard_certified", "product_pass_certified"):
            self.assertFalse(result[key])
        self.assertNotIn("BeginUtc", json.dumps(result))

    def test_every_adapter_result_and_thread_query_stays_independent(self):
        self.seed()
        for outcome in "applied no_change unsupported stale not_ready invalid_coordinate unavailable composition_blocked fault".split():
            with self.subTest(outcome=outcome):
                self.write_trace(trace(outcome, "unknown", "unavailable"))
                result = summarize(self.directory)
                observed = result["server"]["observations"][0]
                self.assertEqual(outcome, observed["facts"]["focus_result"])
                self.assertEqual("unknown", observed["facts"]["native_thread_relation"])
                self.assertEqual("unjoined", result["server"]["cross_process_edge"])
                self.assertEqual("observed", result["client"]["status"])

    def test_after_query_failure_preserves_return_but_is_unknown(self):
        self.seed()
        value = client()
        value["Operations"][-1].update(ObservationException="com", ObservationHResult=-1, OwnerQueuePaneAfter="unavailable")
        self.write("report.json", value)
        result = summarize(self.directory)
        self.assertEqual("unknown", result["status"])
        self.assertEqual("returned", result["client"]["operations"][-1]["Outcome"])
        self.assertEqual("observed", result["client"]["declared_classification"])

    def test_before_query_not_attempted_is_not_focus_threw(self):
        self.seed()
        value = client()
        value.update(Classification="incomplete")
        value["Operations"][-1].update(Outcome="not_attempted", ActionAttempted=False,
                                      ObservationException="invalid_operation", ObservationHResult=-1,
                                      OwnerQueuePaneBefore="unavailable")
        self.write("report.json", value)
        result = summarize(self.directory)
        operation = result["client"]["operations"][-1]
        self.assertFalse(operation["ActionAttempted"])
        self.assertIsNone(operation["Exception"])
        self.assertEqual("not_attempted", operation["Outcome"])

    def test_actual_client_throw_is_observed_not_adapter_refusal_join(self):
        value = client()
        value["Operations"][-1].update(Outcome="threw", Exception="com", HResult=-2147467259)
        result = client_projection(value, supervisor())
        self.assertEqual("observed", result["status"])
        self.assertEqual("com", result["operations"][-1]["Exception"])

    def test_timeout_is_incomplete_actual_numeric_exit_missing_editor_unknown(self):
        value = supervisor()
        value.update(timed_out=True, cleanup_forced=True, client_exit_code=124, error_class="timeout")
        self.write("supervisor.json", value)
        result = summarize(self.directory)
        self.assertEqual("incomplete", result["status"])
        self.assertEqual(124, result["client_exit_code"])
        self.assertIsNone(result["client"]["editor_exit_code"])

    def test_empty_unproven_tree_prevents_normal_claim(self):
        self.seed()
        value = supervisor()
        value.update(job_empty=False)
        self.write("supervisor.json", value)
        self.write_trace(trace())
        result = summarize(self.directory)
        self.assertEqual("unknown", result["status"])
        self.assertEqual("unknown", result["server"]["status"])

    def test_client_binary_and_architecture_are_checked(self):
        for key, bad in (("BinarySha256", "e" * 64), ("Architecture", "Arm64")):
            value = client(); value[key] = bad
            with self.subTest(key=key), self.assertRaises(ValueError):
                client_projection(value, supervisor())

    def test_privacy_unknown_keys_values_and_receipt_conflicts_are_rejected(self):
        mutations = [lambda row: row.update(UserText="PRIVATE_MARKER"),
                     lambda row: row["Operations"][0].update(Label="PRIVATE_MARKER"),
                     lambda row: row["Operations"][0].update(OwnerQueuePaneAfter="PRIVATE_MARKER"),
                     lambda row: row["Operations"][0].update(Receipt=True),
                     lambda row: row["Operations"][0].update(TerminalParentReceipt=2),
                     lambda row: row["Operations"][0].update(ActionAttempted=False)]
        self.seed()
        for mutate in mutations:
            value = client(); mutate(value); self.write("report.json", value)
            result = summarize(self.directory)
            self.assertEqual("unknown", result["client"]["status"])
            self.assertNotIn("PRIVATE_MARKER", json.dumps(result))

    def test_malformed_complete_trace_is_unknown_and_raw_retained(self):
        self.seed(); self.write_trace(trace())
        path = self.directory / "scratch/mote-home/traces/native.jsonl"
        path.write_text('PRIVATE_MARKER\n', encoding="utf-8")
        result = summarize(self.directory)
        self.assertEqual("unknown", result["server"]["status"])
        self.assertEqual("unknown", result["status"])
        self.assertEqual('PRIVATE_MARKER\n', path.read_text())
        self.assertNotIn("PRIVATE_MARKER", json.dumps(result))

    def test_censored_complete_prefix_keeps_positive_no_absence(self):
        self.seed(); self.write_trace(trace()[:2])
        value = supervisor(); value.update(timed_out=True, cleanup_forced=True, client_exit_code=124, error_class="timeout")
        self.write("supervisor.json", value)
        path = self.directory / "scratch/mote-home/traces/native.jsonl"
        with path.open("a") as stream: stream.write('{"partial":')
        result = summarize(self.directory)
        self.assertEqual("observed", result["server"]["status"])
        self.assertEqual("censored", result["server"]["observations"][0]["boundary"])
        self.assertFalse(result["server"]["absence_certified"])

    def test_supervisor_bool_exits_raw_error_and_hash_change_not_healthy(self):
        for key, bad in (("client_exit_code", False), ("error_class", "PRIVATE_MARKER"), ("schema_version", True)):
            value = supervisor(); value[key] = bad
            with self.subTest(key=key), self.assertRaises(ValueError): validate_supervisor(value)
        self.seed(); value = supervisor(); value["binary_sha256_after"] = "e" * 64
        self.write("supervisor.json", value)
        self.assertEqual("unknown", summarize(self.directory)["status"])

    def test_links_are_refused_without_following_or_deleting_target(self):
        self.seed()
        target = self.directory / "target.json"; target.write_text('PRIVATE_MARKER')
        path = self.directory / "report.json"; path.unlink()
        try: path.symlink_to(target)
        except OSError: self.skipTest("OS does not allow creating a fixture symlink")
        result = summarize(self.directory)
        self.assertEqual("unknown", result["client"]["status"])
        self.assertEqual('PRIVATE_MARKER', target.read_text())

    def test_supervisor_protocol_has_no_pid_kill_or_activation_escape(self):
        text = (ROOT / "tests/Invoke-WindowsGridFocusProvenanceWorkflow.ps1").read_text()
        for required in ("0x2000D", "0x20002", "0x08080000", "TerminateJobObject", "120000", "RUNNER_ENVIRONMENT", "CheckLayout"):
            self.assertIn(required, text)
        for forbidden in ("SetForegroundWindow", "OpenProcess", "Get-Process -Name", "Stop-Process", "Remove-Item", "LocalOverride", "CREATE_BREAKAWAY"):
            self.assertNotIn(forbidden, text)

    def test_observed_report_contradictions_are_not_healthy(self):
        self.seed()
        for mutation in ({"IdentityCertified": False}, {"CleanupException": "io", "CleanupHResult": -1},
                         {"Boundary": "open"}, {"FixtureAfterSha256": "e" * 64}):
            with self.subTest(mutation=mutation):
                value = client(); value.update(mutation); self.write("report.json", value)
                self.assertEqual("unknown", summarize(self.directory)["status"])

    def test_cli_missing_report_fixed_summary_and_nonzero_without_traceback(self):
        self.write("supervisor.json", supervisor())
        result = subprocess.run([sys.executable, "-B", str(ROOT / "tests/summarize_windows_grid_focus_provenance.py"),
                                 "--directory", str(self.directory)], capture_output=True, text=True)
        self.assertEqual(1, result.returncode)
        self.assertEqual("unknown", json.loads((self.directory / "summary.json").read_text())["status"])
        self.assertNotIn("Traceback", result.stderr)

    def test_cli_preexisting_output_is_not_overwritten_or_leaked(self):
        self.seed()
        path = self.directory / "summary.json"; path.write_text("PRIVATE_MARKER")
        result = subprocess.run([sys.executable, "-B", str(ROOT / "tests/summarize_windows_grid_focus_provenance.py"),
                                 "--directory", str(self.directory)], capture_output=True, text=True)
        self.assertEqual(1, result.returncode)
        self.assertEqual("PRIVATE_MARKER", path.read_text())
        self.assertNotIn("PRIVATE_MARKER", result.stdout + result.stderr)
        self.assertNotIn("Traceback", result.stderr)


if __name__ == "__main__":
    unittest.main()
