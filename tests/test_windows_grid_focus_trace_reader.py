"""Independent, content-free adapter graph and privacy fixtures; no GUI calls."""

import copy
import json
from pathlib import Path
import tempfile
import unittest

from windows_grid_focus_trace_reader import classify_focus, read_focus_paths


ROOT = Path(__file__).resolve().parents[1]


def row(span, operation, parent=None, status="success", **attributes):
    """Specify the public schema independently from producer implementation."""
    return {"schema_version": 1, "utc_time": "2026-10-01T00:00:00Z",
            "session_id": "2" * 32, "trace_id": "3" * 32,
            "span_id": f"{span:016x}",
            "parent_span_id": f"{parent:016x}" if parent is not None else None,
            "operation": operation, "status": status, "duration_us": 0,
            "attributes": attributes}


def pair(result="applied", native="owner", managed="owner", before="source", after="table"):
    """A receipt and terminal share frozen facts, not UTC coincidence."""
    attrs = {"native_thread_relation": native, "managed_admission_relation": managed,
             "focus_before": before, "focus_target": "cell"}
    return [row(2, "native.grid.focus.adapter.received", 1, **attrs),
            row(3, "native.grid.focus.adapter", 2,
                status="success" if result in ("applied", "no_change") else "failure",
                **attrs, focus_after=after, focus_result=result), row(1, "mote.session")]


class FocusGraphTests(unittest.TestCase):
    """Separate method-call witnesses, query uncertainty and transport limits."""

    def observation(self, rows, **kwargs):
        """Extract one fixture observation and enforce permanent no-join bounds."""
        report = classify_focus(rows, **kwargs)
        self.assertFalse(report["absence_certified"])
        self.assertEqual("none", report["client_correlation"])
        self.assertEqual("unjoined", report["cross_process_edge"])
        self.assertEqual("adapter_attempt_only", report["provider_entry_coverage"])
        self.assertEqual("not_certified", report["transport_health"])
        return report["observations"][0]

    def test_marshaled_owner_applied_is_explicit_pair_not_client_join(self):
        observed = self.observation(pair())
        self.assertEqual("success", observed["classification"])
        self.assertEqual("explicit_pair_only", observed["coverage"])
        self.assertEqual("owner", observed["facts"]["native_thread_relation"])
        self.assertEqual("owner", observed["facts"]["managed_admission_relation"])
        self.assertEqual("0000000000000002", observed["receipt_span_id"])
        self.assertEqual("0000000000000003", observed["terminal_span_id"])
        self.assertEqual("normal_exit_observed", observed["boundary"])

    def test_actual_non_owner_unsupported_is_refused_not_job_verdict(self):
        observed = self.observation(pair("unsupported", "non_owner", "non_owner", after="source"))
        self.assertEqual("failure", observed["classification"])
        self.assertEqual("unsupported", observed["facts"]["focus_result"])
        self.assertEqual("explicit_pair_only", observed["coverage"])

    def test_unknown_query_does_not_override_applied(self):
        observed = self.observation(pair(native="unknown", managed="owner", before="unavailable", after="unavailable"))
        self.assertEqual("success", observed["classification"])
        self.assertEqual("unknown", observed["facts"]["native_thread_relation"])
        self.assertEqual("owner", observed["facts"]["managed_admission_relation"])

    def test_fault_is_separate_from_eight_return_results(self):
        observed = self.observation(pair("fault"))
        self.assertEqual("failure", observed["classification"])
        self.assertEqual("fault", observed["facts"]["focus_result"])

    def test_all_eight_results_preserve_exact_action_status(self):
        for result in ("applied", "no_change", "unsupported", "stale", "not_ready",
                       "invalid_coordinate", "unavailable", "composition_blocked"):
            with self.subTest(result=result):
                observed = self.observation(pair(result))
                self.assertEqual(result, observed["facts"]["focus_result"])
                self.assertEqual("success" if result in ("applied", "no_change") else "failure",
                                 observed["classification"])

    def test_missing_receipt_even_after_normal_shutdown_is_unobserved(self):
        report = classify_focus([row(1, "mote.session")], terminated=True)
        self.assertEqual("unobserved", report["status"])
        self.assertEqual([], report["observations"])
        self.assertFalse(report["absence_certified"])

    def test_empty_live_and_terminated_trace_never_certify_absence(self):
        for terminated in (False, True):
            report = classify_focus([], terminated=terminated)
            self.assertEqual("unobserved", report["status"])
            self.assertFalse(report["absence_certified"])

    def test_missing_terminal_is_incomplete_despite_root_end(self):
        rows = pair()
        observed = self.observation([rows[0], rows[2]], terminated=True)
        self.assertEqual("censored", observed["classification"])
        self.assertEqual("normal_exit_observed", observed["boundary"])
        self.assertEqual(["adapter_terminal"], observed["missing_evidence"])
        self.assertFalse(observed["terminal_observed"])

    def test_root_can_be_last_or_first_without_time_sorting(self):
        rows = pair()
        rows[0]["utc_time"] = "2026-10-01T09:00:00Z"
        rows[1]["utc_time"] = "2026-10-01T01:00:00Z"
        expected = classify_focus(rows)
        self.assertEqual(expected, classify_focus([rows[2], rows[1], rows[0]]))
        self.assertNotIn("utc_time", json.dumps(expected))

    def test_complete_pair_missing_session_root_retains_positive_with_incomplete_coverage(self):
        observed = self.observation(pair()[:2], terminated=True)
        self.assertEqual("success", observed["classification"])
        self.assertEqual("censored", observed["boundary"])
        self.assertEqual("incomplete", observed["coverage"])
        self.assertEqual(["session_root"], observed["missing_evidence"])

    def test_failed_session_root_is_not_normal_exit(self):
        rows = pair()
        rows[-1]["status"] = "failure"
        observed = self.observation(rows, terminated=True)
        self.assertFalse(observed["normal_session_terminal_observed"])
        self.assertEqual(["normal_session_terminal"], observed["missing_evidence"])

    def test_drop_marks_complete_pair_degraded_without_erasing_result(self):
        rows = pair() + [row(4, "telemetry.dropped", 1, count=1)]
        observed = self.observation(rows)
        self.assertEqual("degraded", observed["coverage"])
        self.assertTrue(observed["dropped_records_observed"])
        self.assertEqual("success", observed["classification"])

    def test_unrelated_session_drop_does_not_degrade_pair(self):
        rows = pair() + [row(4, "telemetry.dropped", 1, count=1)]
        rows[-1]["session_id"] = "4" * 32
        self.assertEqual("explicit_pair_only", self.observation(rows)["coverage"])

    def test_old_save_records_can_coexist_without_focus_attribution(self):
        rows = pair() + [row(4, "command.save.received", 1),
                         row(5, "save.snapshot_captured", 4, version=7),
                         row(6, "command.save", 4, version=7, reason="completed")]
        report = classify_focus(rows)
        self.assertEqual(1, len(report["observations"]))
        self.assertNotIn("version", json.dumps(report))

    def test_multiple_attempts_remain_independent(self):
        rows = pair()
        other = pair("no_change")[:2]
        other[0]["span_id"] = "0000000000000004"
        other[1]["span_id"] = "0000000000000005"
        other[1]["parent_span_id"] = other[0]["span_id"]
        report = classify_focus(rows + other)
        self.assertEqual(2, len(report["observations"]))
        self.assertEqual("none", report["client_correlation"])

    def test_duplicate_span_and_repeated_terminal_are_rejected(self):
        for duplicate in (copy.deepcopy(pair()[0]), copy.deepcopy(pair()[1])):
            rows = pair()
            if duplicate["operation"] == "native.grid.focus.adapter":
                duplicate["span_id"] = "0000000000000004"
            with self.assertRaises(ValueError):
                classify_focus(rows + [duplicate])

    def test_two_session_terminal_anchors_in_one_trace_are_rejected(self):
        with self.assertRaises(ValueError):
            classify_focus(pair() + [row(4, "mote.session")])

    def test_session_root_from_another_trace_is_not_adopted(self):
        rows = pair()
        rows[-1]["trace_id"] = "4" * 32
        observed = self.observation(rows, terminated=True)
        self.assertEqual(["session_root"], observed["missing_evidence"])
        self.assertEqual("censored", observed["boundary"])

    def test_table_target_and_all_closed_panes_are_preserved(self):
        for pane in ("unavailable", "none", "source", "table", "row_scroller",
                     "column_scroller", "coordinate", "owned_other", "outside"):
            rows = pair(before=pane, after=pane)
            for item in rows[:2]:
                item["attributes"]["focus_target"] = "table"
            with self.subTest(pane=pane):
                observed = self.observation(rows)
                self.assertEqual(pane, observed["facts"]["focus_after"])
                self.assertEqual("table", observed["facts"]["focus_target"])

    def test_orphan_terminal_never_rebuilt_by_unique_count_or_timing(self):
        rows = pair()
        rows[1]["parent_span_id"] = "0000000000000099"
        with self.assertRaises(ValueError):
            classify_focus(rows)

    def test_session_or_trace_replacement_cannot_capture_terminal(self):
        for key in ("session_id", "trace_id"):
            rows = pair()
            rows[1][key] = "4" * 32
            with self.assertRaises(ValueError):
                classify_focus(rows)

    def test_receipt_cannot_parent_other_attempt_or_non_session(self):
        rows = pair()
        rows[-1]["operation"] = "mote.startup"
        with self.assertRaises(ValueError):
            classify_focus(rows)

    def test_receipt_session_root_cannot_have_parent(self):
        rows = pair()
        rows[-1]["parent_span_id"] = "0000000000000009"
        with self.assertRaises(ValueError):
            classify_focus(rows)

    def test_cycles_are_rejected_not_coverage_gaps(self):
        rows = pair()
        rows[0]["parent_span_id"] = rows[1]["span_id"]
        with self.assertRaises(ValueError):
            classify_focus(rows)

    def test_every_repeated_dimension_must_agree(self):
        for key, value in (("native_thread_relation", "non_owner"),
                           ("managed_admission_relation", "unknown"),
                           ("focus_before", "none"), ("focus_target", "table")):
            rows = pair()
            rows[1]["attributes"][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                classify_focus(rows)

    def test_illegal_status_parent_duration_and_dimension_types_are_rejected(self):
        mutations = ((0, "status", "failure"), (0, "duration_us", 1),
                     (0, "parent_span_id", None), (1, "status", "cancelled"),
                     (1, "duration_us", -1), (1, "duration_us", True))
        for index, key, value in mutations:
            rows = pair()
            rows[index][key] = value
            with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                classify_focus(rows)
        for value in (None, True, 1, "user-derived"):
            rows = pair()
            rows[0]["attributes"]["native_thread_relation"] = value
            with self.subTest(value=value), self.assertRaises(ValueError):
                classify_focus(rows)

    def test_action_status_is_not_inferred_from_query_unknown(self):
        rows = pair("unsupported", native="unknown", before="unavailable", after="unavailable")
        rows[1]["status"] = "success"
        with self.assertRaises(ValueError):
            classify_focus(rows)

    def test_privacy_and_operation_specific_attribute_scopes(self):
        cases = []
        for index in (0, 1):
            rows = pair()
            rows[index]["attributes"]["path"] = "PRIVATE-FIXTURE"
            cases.append(rows)
        rows = pair() + [row(4, "command.save", 1, native_thread_relation="owner")]
        cases.append(rows)
        rows = pair()
        rows[0]["raw_key"] = "PRIVATE-FIXTURE"
        cases.append(rows)
        rows = pair()
        rows[0]["attributes"]["focus_after"] = "table"
        cases.append(rows)
        for rows in cases:
            with self.assertRaises(ValueError) as raised:
                classify_focus(rows)
            self.assertNotIn("PRIVATE-FIXTURE", str(raised.exception))

    def test_path_reader_strict_complete_rows_and_censored_partial(self):
        area = ROOT / ".cache/focus-graph-validation"
        area.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=area) as folder:
            path = Path(folder) / "trace.jsonl"
            data = b"".join(json.dumps(item).encode() + b"\n" for item in pair())
            path.write_bytes(data)
            self.assertEqual(classify_focus(pair(), terminated=True),
                             read_focus_paths([path], terminated=True))
            path.write_bytes(data + b'{"private-incomplete":\xff')
            self.assertEqual("observed", read_focus_paths([path], terminated=True)["status"])
            with self.assertRaises(ValueError):
                read_focus_paths([path], terminated=False)
            path.write_bytes(data + b'{"private-incomplete":\xff\n')
            with self.assertRaises(ValueError):
                read_focus_paths([path], terminated=True)


if __name__ == "__main__":
    unittest.main()
