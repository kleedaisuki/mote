"""Deterministic privacy and non-promotion tests for CI evidence summaries."""

import json
from pathlib import Path
import tempfile
import unittest

import summarize_ci_evidence as summary


class SummaryTests(unittest.TestCase):
    """Reports are untrusted claims, not permission to copy diagnostic payloads."""

    def setUp(self):
        """Keep fixture artifacts inside the repository cache."""
        cache = Path(__file__).resolve().parents[1] / ".cache" / "summary-tests"
        cache.mkdir(parents=True, exist_ok=True)
        self.directory = tempfile.TemporaryDirectory(dir=cache)
        self.root = Path(self.directory.name)
        self.addCleanup(self.directory.cleanup)

    def report(self, identity, data, rid="win-x64"):
        """Write the fixed manifest member and summarize it."""
        path = dict(summary.manifest(self.root, rid))[identity]
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(data), encoding="utf-8")
        return summary.summarize_report(identity, path)

    def test_missing_is_not_failure_or_pass(self):
        row = summary.summarize(self.root, "win-x64")["diagnostics"][0]
        self.assertEqual((row["report_health"], row["result"]), ("missing", "unknown"))

    def test_malformed_is_not_censored(self):
        path = dict(summary.manifest(self.root, "win-x64"))["native-json-large"]
        path.parent.mkdir(parents=True)
        path.write_text("{", encoding="utf-8")
        self.assertEqual(summary.summarize_report("native-json-large", path)["report_health"], "malformed")

    def test_control_complete_is_not_product_pass(self):
        row = self.report("mac-grid-action-contract", {"status": "control-completed", "child_exit_code": 0}, "osx-arm64")
        self.assertEqual(row["result"], "experiment-completed")
        self.assertEqual(row["child_exit_code"], 0)

    def test_failure_keeps_nested_exit(self):
        row = self.report("causal-trace-recovery", {"passed": False, "cases": [{"mode": "normal", "passed": False, "exit_code": 13}]})
        self.assertEqual(row["result"], "failed")
        self.assertEqual(row["nested"][0]["exit_code"], 13)

    def test_expected_kill_can_pass_with_censored_evidence(self):
        row = self.report("causal-trace-recovery", {"passed": True, "cases": [{"mode": "held", "passed": True, "exit_code": -9,
            "evidence": {"requests": [{"classification": "censored"}], "absence_certified": False}}]})
        self.assertEqual(row["result"], "pass")
        self.assertEqual(row["nested"][0]["evidence_health"], "censored")
        self.assertFalse(row["nested"][0]["absence_certified"])

    def test_json_forced_cleanup_is_censored_not_no_receipt(self):
        row = self.report("native-json-large", {"status": "incomplete", "samples": [{"size_mib": 100, "status": "failed", "forced_cleanup": True}]})
        self.assertEqual(row["nested"][0]["evidence_health"], "censored")
        self.assertEqual(row["nested"][0]["trace_health"], "missing")

    def test_unknown_report_count_does_not_disclose_name(self):
        directory = self.root / ".cache" / "ci-inventory" / "win-x64"
        directory.mkdir(parents=True)
        (directory / "PRIVATE-PATH.json").write_text('{"source": "SECRET"}', encoding="utf-8")
        report = summary.summarize(self.root, "win-x64")
        self.assertEqual(report["unrecognized_inventory_reports"], 1)
        self.assertNotIn("PRIVATE", json.dumps(report) + summary.markdown(report))

    def test_privacy_rejects_arbitrary_fields_status_and_fake_exits(self):
        row = self.report("native-continuous-win", {"status": "PRIVATE-TEXT", "exit_code": True, "target_exit": "PRIVATE-PATH",
            "error": "SECRET", "source": "SECRET", "stage": "SECRET"})
        serialized = json.dumps(row)
        self.assertNotIn("PRIVATE", serialized)
        self.assertNotIn("SECRET", serialized)
        self.assertNotIn("exit_code", row)
        self.assertEqual(row["result"], "unrecognized")

    def test_json_pass_requires_exact_two_cases(self):
        row = self.report("native-json-large", {"status": "pass", "samples": [{"size_mib": 1, "status": "pass"}]})
        self.assertEqual(row["result"], "inconsistent-pass-claim")

    def test_trace_drops_separate_from_pass_claim(self):
        row = self.report("native-json-large", {"status": "pass", "samples": [
            {"size_mib": size, "status": "pass", "trace_evidence": {"causal_integrity": "pass", "dropped_records": 2}}
            for size in (1, 100)]})
        self.assertEqual(row["result"], "pass")
        self.assertEqual(row["nested"][0]["trace_health"], "dropped")

    def test_missing_exit_is_not_inferred_from_pass(self):
        row = self.report("windows-source-range", {"passed": True})
        self.assertEqual(row["result"], "pass")
        self.assertNotIn("target_exit", row)

    def test_theme_worker_exit_is_not_hidden_by_parent_pass(self):
        row = self.report("native-canvas-theme", {"status": "passed", "workers": [
            {"phase": "light", "completed": True, "exit_code": 17, "error": "SECRET"}]})
        self.assertEqual(row["nested"][0]["exit_code"], 17)
        self.assertEqual(row["nested"][0]["result"], "incomplete")
        self.assertNotIn("SECRET", json.dumps(row))

    def test_summary_output_does_not_count_itself(self):
        directory = self.root / ".cache" / "ci-inventory" / "win-x64"
        directory.mkdir(parents=True)
        (directory / "evidence-summary.json").write_text("{}", encoding="utf-8")
        self.assertEqual(summary.summarize(self.root, "win-x64")["unrecognized_inventory_reports"], 0)

    def test_all_rid_summaries_only_have_applicable_diagnostics(self):
        for rid in summary.RIDS:
            report = summary.summarize(self.root, rid)
            self.assertIn(rid, summary.markdown(report))
            names = {row["diagnostic"] for row in report["diagnostics"]}
            self.assertEqual("windows-source-range" in names, rid.startswith("win-"))
            self.assertEqual("mac-grid-ax-external" in names, rid.startswith("osx-"))
            self.assertEqual("mac-grid-showmenu-pair" in names, rid.startswith("osx-"))

    def test_missing_or_empty_requests_are_unverified(self):
        for evidence in ({}, {"requests": []}, {"requests": None},
                         {"normal_session_terminal_observed": True, "requests": []}):
            row = self.report("causal-trace-recovery", {"passed": True, "cases": [
                {"mode": "normal", "passed": True, "evidence": evidence}]})
            self.assertEqual(row["nested"][0]["evidence_health"], "unverified")

    def test_recovery_observed_requires_explicit_session_and_request_terminals(self):
        for requests in ([{}], [{"classification": "pending"}], ["success"]):
            self.assertEqual(summary.request_evidence_health({"requests": requests,
                "normal_session_terminal_observed": True}), "unverified")
        self.assertEqual(summary.request_evidence_health({"requests": [{"classification": "success"}]}), "unverified")
        self.assertEqual(summary.request_evidence_health({"requests": [{"classification": "success"}],
            "normal_session_terminal_observed": True}), "observed")

    def test_json_unknown_normal_outcome_is_unverified(self):
        samples = [{"status": "incomplete", "forced_cleanup": False}, {"status": "pass"},
                   {"status": "failed", "normal_exit": False},
                   {"status": "pass", "normal_exit": True, "reopen_normal_exit": False}]
        for sample in samples:
            self.assertEqual(summary.sample_evidence_health(sample), "unverified")
        self.assertEqual(summary.sample_evidence_health({"status": "pass", "normal_exit": True,
            "reopen_normal_exit": True}), "observed")

    def test_grid_external_exposes_real_nested_failure_without_copying_checks(self):
        row = self.report("mac-grid-ax-external", {"status": "failed", "swift_exit_code": 1,
            "editor_normal_exit": True, "swift_report": {"checks": [
                {"passed": True, "name": "SECRET"}, {"passed": False, "detail": "PRIVATE"}]}}, "osx-x64")
        self.assertEqual((row["swift_exit_code"], row["passed_checks"], row["failed_checks"]), (1, 1, 1))
        self.assertNotIn("SECRET", json.dumps(row))
        self.assertNotIn("PRIVATE", json.dumps(row))

    def test_grid_pair_completion_is_not_product_pass_and_keeps_numeric_replies(self):
        row = self.report("mac-grid-showmenu-pair", {"status": "completed-failure-observed", "sessions": [
            {"session": "C0", "status": "completed-success-observed", "owner_exit_code": 0,
             "client_exit_code": 0, "client": {"actions": {"original_error": 0}}},
            {"session": "P0", "status": "completed-failure-observed", "owner_exit_code": 0,
             "client_exit_code": 0, "client": {"actions": {"original_error": -25205}}},
            {"session": "SECRET", "status": "pass", "client_exit_code": 0}]}, "osx-arm64")
        self.assertEqual(row["result"], "experiment-completed-failure-observed")
        self.assertEqual(len(row["nested"]), 2)
        self.assertEqual(row["nested"][1]["ax_reply"], -25205)
        self.assertNotEqual(row["nested"][0]["result"], "pass")
        output = summary.markdown({"rid": "osx-arm64", "diagnostics": [row], "unrecognized_inventory_reports": 0})
        self.assertIn("client_exit_code=0", output)
        self.assertIn("AX-reply=-25205", output)
        self.assertNotIn("SECRET", output)

    def test_numeric_exits_and_ax_replies_reject_boolean_and_string(self):
        row = self.report("mac-grid-showmenu-pair", {"status": "completed-failure-observed", "sessions": [
            {"session": "P0", "status": "completed-failure-observed", "client_exit_code": True,
             "owner_exit_code": "0", "client": {"actions": {"original_error": False}}}]}, "osx-x64")
        self.assertNotIn("client_exit_code", row["nested"][0])
        self.assertNotIn("owner_exit_code", row["nested"][0])
        self.assertNotIn("ax_reply", row["nested"][0])

    def test_negative_trace_drop_count_is_unverified(self):
        self.assertEqual(summary.trace_health({"trace_evidence": {
            "causal_integrity": "pass", "dropped_records": -1}}), "unverified")

    def test_native_save_causal_evidence_is_typed_and_separate_from_sample_claim(self):
        row = self.report("native-json-large", {"status": "incomplete", "samples": [
            {"status": "failed", "save_causal_evidence": {"contract": "native-save-causal-v1",
             "status": "censored", "requests": [{"source": "SECRET", "reason": "PRIVATE"}],
             "absence_certified": False, "normal_exit": False, "terminated": True,
             "trace_sha256": "SECRET", "discarded_partial_files": ["PRIVATE-PATH"]}}]})
        evidence = row["nested"][0]["save_causal_evidence"]
        self.assertEqual(evidence, {"status": "censored", "recorded_requests": 1,
            "absence_certified": False, "normal_exit": False, "terminated": True,
            "native_menu_inventory": {"status": "unverified", "boundary": "unverified", "counts": {}}})
        self.assertNotIn("SECRET", json.dumps(row))
        self.assertNotIn("PRIVATE", json.dumps(row))

    def test_native_save_evidence_unknown_contract_and_fake_booleans_are_not_promoted(self):
        self.assertEqual(summary.save_causal_evidence({"save_causal_evidence": {
            "contract": "SECRET", "status": "complete"}}), {"status": "unverified"})
        self.assertEqual(summary.save_causal_evidence({"save_causal_evidence": {
            "contract": "native-save-causal-v1", "status": "SECRET", "requests": "PRIVATE",
            "absence_certified": "false", "normal_exit": 1}}), {"status": "unverified",
            "native_menu_inventory": {"status": "unverified", "boundary": "unverified", "counts": {}}})

    def test_markdown_exposes_save_claim_request_count_and_normal_exit_privately(self):
        row = self.report("native-json-large", {"status": "incomplete", "samples": [
            {"status": "failed", "save_causal_evidence": {"contract": "native-save-causal-v1",
             "status": "censored", "requests": [{"reason": "SECRET"}], "normal_exit": False,
             "trace_sha256": "PRIVATE", "discarded_partial_files": ["PRIVATE-PATH"]}}]})
        output = summary.markdown({"rid": "win-x64", "diagnostics": [row], "unrecognized_inventory_reports": 0})
        self.assertIn("Save-chain=censored, requests=1, normal-exit=False", output)
        self.assertNotIn("SECRET", output)
        self.assertNotIn("PRIVATE", output)

    def test_markdown_save_claim_missing_fields_remain_unknown(self):
        self.assertEqual(summary.causal_markdown({}), "not-recorded")
        self.assertEqual(summary.causal_markdown({"save_causal_evidence": {"status": "unverified"}}),
            "unverified, requests=unknown, normal-exit=unknown")

    def test_json_actual_editor_and_reopen_exits_are_retained_and_rendered(self):
        row = self.report("native-json-large", {"status": "incomplete", "samples": [
            {"size_mib": 1, "status": "failed", "editor_exit_code": -9, "reopen_exit_code": 7,
             "forced_cleanup": True, "normal_exit": False, "reopen_normal_exit": False,
             "error": "PRIVATE-PATH"}]})
        self.assertEqual(row["nested"][0]["editor_exit_code"], -9)
        self.assertEqual(row["nested"][0]["reopen_exit_code"], 7)
        self.assertEqual(row["nested"][0]["evidence_health"], "censored")
        output = summary.markdown({"rid": "win-x64", "diagnostics": [row], "unrecognized_inventory_reports": 0})
        self.assertIn("editor_exit_code=-9", output)
        self.assertIn("reopen_exit_code=7", output)
        self.assertNotIn("PRIVATE", output)

    def test_json_exit_booleans_and_unlaunched_null_never_become_zero(self):
        for fields in ({"editor_exit_code": None, "reopen_exit_code": None},
                       {"editor_exit_code": True, "reopen_exit_code": False},
                       {"editor_exit_code": "0", "reopen_exit_code": "PRIVATE"}, {}):
            row = self.report("native-json-large", {"status": "pass", "samples": [
                {"size_mib": 1, "status": "pass", "normal_exit": True, "reopen_normal_exit": True, **fields}]})
            self.assertNotIn("editor_exit_code", row["nested"][0])
            self.assertNotIn("reopen_exit_code", row["nested"][0])
            output = summary.markdown({"rid": "win-x64", "diagnostics": [row], "unrecognized_inventory_reports": 0})
            self.assertIn("exits=unknown", output)
            self.assertNotIn("editor_exit_code=0", output)
            self.assertNotIn("PRIVATE", output)

    def menu_fixture(self, boundary="normal-exit-observed"):
        """Create the complete fixed menu matrix, with setup ready but no Save entry."""
        counts = {operation: {status: 0 for status in summary.MENU_STATUSES}
                  for operation, _ in summary.MENU_FIELDS}
        counts["native.menu.observation.ready"]["success"] = 1
        return {"status": "observed", "boundary": boundary, "counts": counts,
                "absence_certified": False, "request_correlation": "none"}

    def test_menu_inventory_missing_and_empty_are_unverified(self):
        for evidence in ({}, {"native_menu_inventory": {}}, {"native_menu_inventory": None}):
            inventory = summary.menu_inventory(evidence)
            self.assertEqual(inventory, {"status": "unverified", "boundary": "unverified", "counts": {}})

    def test_menu_ready_alone_does_not_make_save_chain_complete(self):
        row = self.report("native-json-large", {"status": "incomplete", "samples": [
            {"status": "failed", "save_causal_evidence": {"contract": "native-save-causal-v1",
             "status": "unobserved", "native_menu_inventory": self.menu_fixture()}}]})
        evidence = row["nested"][0]["save_causal_evidence"]
        self.assertEqual(evidence["status"], "unobserved")
        self.assertEqual(evidence["native_menu_inventory"]["request_correlation"], "none")
        output = summary.markdown({"rid": "osx-x64", "diagnostics": [row], "unrecognized_inventory_reports": 0})
        self.assertIn("Save-chain=unobserved", output)
        self.assertIn("menu=observed, boundary=normal-exit-observed", output)
        self.assertIn("ready[1/0/0/0]", output)
        self.assertIn("entry[0/0/0/0]", output)
        self.assertIn("not Save routing or request edges", output)

    def test_menu_counter_types_and_unknown_payload_are_filtered(self):
        raw = self.menu_fixture("censored")
        raw["counts"]["native.menu.save_family.entered"] = {
            "success": True, "failure": -1, "cancelled": "SECRET", "skipped": 2, "PRIVATE": 99}
        raw["counts"]["PRIVATE-OP"] = {"success": 8}
        raw["reason"] = "SECRET-PATH"
        inventory = summary.menu_inventory({"native_menu_inventory": raw})
        self.assertEqual(inventory["status"], "unverified")
        self.assertEqual(inventory["boundary"], "censored")
        self.assertEqual(inventory["counts"]["native.menu.save_family.entered"], {"skipped": 2})
        output = summary.menu_markdown({"save_causal_evidence": {"native_menu_inventory": inventory}})
        self.assertIn("entry[?/?/?/2]", output)
        self.assertNotIn("PRIVATE", json.dumps(inventory) + output)
        self.assertNotIn("SECRET", json.dumps(inventory) + output)

    def test_menu_fixed_boundaries_preserve_censorship_without_absence_claims(self):
        for boundary in ("normal-exit-observed", "censored", "open"):
            inventory = summary.menu_inventory({"native_menu_inventory": self.menu_fixture(boundary)})
            self.assertEqual(inventory["boundary"], boundary)
            self.assertFalse(inventory["absence_certified"])
            self.assertEqual(inventory["request_correlation"], "none")

    def test_menu_unknown_status_boundary_and_claimed_correlation_are_unverified(self):
        raw = self.menu_fixture("SECRET")
        raw.update(status="PRIVATE", absence_certified=True, request_correlation="linked-to-secret")
        inventory = summary.menu_inventory({"native_menu_inventory": raw})
        self.assertEqual(inventory["status"], "unverified")
        self.assertEqual(inventory["boundary"], "unverified")
        self.assertNotIn("absence_certified", inventory)
        self.assertNotIn("request_correlation", inventory)
        self.assertNotIn("SECRET", json.dumps(inventory))
        self.assertNotIn("PRIVATE", json.dumps(inventory))


if __name__ == "__main__":
    unittest.main()
