"""Portable artifact/protocol tests; no native process or OS input is executed."""

import hashlib
import json
from pathlib import Path
import unittest
from unittest.mock import patch
from types import SimpleNamespace
import uuid

import probe


def record(operation, span, parent=1, version=None):
    """Construct schema-valid content-free causal records for deterministic audits."""
    return {"schema_version": 1, "utc_time": "2026-10-01T00:00:00Z", "session_id": "a" * 32,
            "trace_id": "b" * 32, "span_id": f"{span:016x}",
            "parent_span_id": None if parent is None else f"{parent:016x}",
            "operation": operation, "duration_us": 123, "status": "success",
            "attributes": {} if version is None else {"version": version}}


class ProbeTests(unittest.TestCase):
    """Check exact byte witnesses, causal endpoints and non-idempotent failure behavior."""

    def setUp(self):
        """Every test artifact remains inside an exclusive repository-local directory."""
        self.directory = probe.acceptance.artifact_path(f".temp/native-json-tests/{uuid.uuid4().hex}")
        self.directory.mkdir(parents=True)

    def tearDown(self):
        """Delete only known owned leaves, rejecting links before every unlink."""
        for path in sorted(self.directory.rglob("*"), key=lambda value: len(value.parts), reverse=True):
            probe.acceptance.artifact_path(path)
            path.rmdir() if path.is_dir() else path.unlink()
        self.directory.rmdir()

    def test_corpus_and_local_edit_oracle(self):
        """One exact-size valid array and only one in-string byte change are witnessed."""
        case = probe.prepare(self.directory, [1])[0]
        source = case["fixture"].read_bytes()
        self.assertEqual(len(source), 1048576)
        self.assertTrue(source.startswith(b'[ {"id":"row"'))
        values = json.loads(source)
        self.assertGreater(len(values), 1000)
        edited = source[:9] + b"X" + source[10:]
        self.assertEqual(json.loads(edited)[0]["id"], "Xow")
        self.assertEqual(case["input_sha256"], hashlib.sha256(source).hexdigest())
        self.assertEqual(case["expected_saved_sha256"], hashlib.sha256(edited).hexdigest())
        self.assertNotEqual(case["input_sha256"], case["expected_saved_sha256"])

    def test_edit_witness_refuses_different_prefix(self):
        """A same-length but different fixture cannot silently choose another edit."""
        path = self.directory / "wrong.json"
        path.write_bytes(b'[ {"id":"qow"}]')
        with self.assertRaises(ValueError):
            probe.expected_digest(path)

    def trace(self):
        """Write one normal-edit session with exact causal draw parent versions."""
        home = self.directory / "home"
        traces = home / "traces"
        traces.mkdir(parents=True)
        path = traces / ("mote-trace-" + "a" * 32 + "-000001.jsonl")
        rows = [record("mote.session", 1, None), record("document.open_to_editable", 2, version=0),
                record("document.open", 3, 2, 0), record("document.open_to_draw_submission", 4, 2, 0),
                record("document.edit_to_presentation", 5, version=1),
                record("document.edit_to_draw_submission", 6, 5, 1), record("document.edit", 7, version=0),
                record("edit.committed", 8, version=1), record("document.save", 9, version=1),
                record("save.completed", 10, version=1), record("mote.startup_to_editable", 11, version=0)]
        path.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
        return home, path, rows

    def test_exact_trace_coverage(self):
        """The existing causal audit is reused and callback endpoints stay explicit."""
        home, _, _ = self.trace()
        result = probe.trace_evidence(home, True)
        self.assertEqual(result["endpoint_integrity"], "pass")
        self.assertEqual(result["causal_integrity"], "pass")
        self.assertIn("not-physical", result["endpoint"])
        self.assertEqual(result["child_monotonic_endpoints"]["document.edit_to_draw_submission"][0]["version"], 1)

    def test_wrong_draw_version_retained(self):
        """A mismatched/cancelled endpoint is classified, not discarded or assigned a time."""
        home, path, rows = self.trace()
        rows[5]["attributes"]["version"] = 0
        path.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
        result = probe.trace_evidence(home, True)
        self.assertEqual(result["endpoint_integrity"], "incomplete")
        self.assertIn("document.edit_to_draw_submission", result["endpoint_issues"])
        self.assertIn("draw-parent-version-mismatch", result["issues"])
        rows[5]["status"] = "cancelled"
        path.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
        self.assertEqual(probe.trace_evidence(home, True)["endpoint_integrity"], "incomplete")

    def test_reopen_refuses_edit(self):
        """GUI reopen cannot pass if its supposedly read-only session contains an edit."""
        home, _, _ = self.trace()
        with self.assertRaises(ValueError):
            probe.trace_evidence(home, False)

    def test_failed_action_witness_cannot_be_masked(self):
        """A successful draw/Save/terminal cannot mask failure of any claimed action."""
        home, path, rows = self.trace()
        for index in (2, 4, 6, 7, 8, 9):
            rows[index]["status"] = "failure"
            path.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
            self.assertEqual(probe.trace_evidence(home, True)["endpoint_integrity"], "incomplete", rows[index]["operation"])
            rows[index]["status"] = "success"

    def test_wrong_action_version_cannot_be_masked(self):
        """Declared pre-edit0/commit1 progression and optional Save1 must agree."""
        home, path, rows = self.trace()
        for index, wrong_version in ((2, 1), (4, 0), (6, 1), (7, 0), (8, 0), (9, 0)):
            original = rows[index]["attributes"]["version"]
            rows[index]["attributes"]["version"] = wrong_version
            path.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
            self.assertEqual(probe.trace_evidence(home, True)["endpoint_integrity"], "incomplete", rows[index]["operation"])
            rows[index]["attributes"]["version"] = original

    def test_unversioned_engine_io_is_explicit(self):
        """Existing engine I/O spans have no revision; do not fabricate one."""
        home, path, rows = self.trace()
        for index in (2, 8, 9):
            rows[index]["attributes"] = {}
        path.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
        result = probe.trace_evidence(home, True)
        self.assertEqual(result["endpoint_integrity"], "pass")
        self.assertIsNone(result["child_monotonic_endpoints"]["document.save"][0]["version"])
        self.assertEqual(result["io_revision_contract"], "open-save-engine-records-unversioned;exact-bytes-and-native-version-witness-separate")

    def test_failed_edit_never_retried(self):
        """A driver timeout consumes one attempt and kills only the owned child."""
        case = probe.prepare(self.directory, [1])[0]
        sample_dir = self.directory / "sample"
        sample_dir.mkdir()
        class Child:
            """Owned deterministic process substitute; never launches a process."""
            pid = 42
            killed = False
            def poll(self):
                """Model a live process until owned cleanup."""
                return 0 if self.killed else None
            def kill(self):
                """Acknowledge exact-child cleanup."""
                self.killed = True
            def wait(self, timeout):
                """Acknowledge bounded reaping."""
                return 0
        class Driver:
            """Model readiness and a failed single non-idempotent edit."""
            attempts = 0
            def __init__(self, *arguments):
                """No operating-system APIs are initialized."""
            def observe(self, version):
                """Certify the inexpensive source/semantic control."""
                return {"ready": True, "complete": True}
            def observation_summary(self):
                """Keep the Mac diagnostic interface inert in this shared fake driver."""
                return {"attempts": 1}
            def edit(self):
                """Fail once after one dispatch rather than invite a retry."""
                Driver.attempts += 1
                raise TimeoutError("do not disclose raw exception text")
        child = Child()
        with patch.object(probe, "WindowsDriver", Driver), patch.object(probe, "MacDriver", Driver), \
             patch.object(probe, "bounded_command"), patch.object(probe.subprocess, "Popen", return_value=child):
            result = probe.sample(Path("unused"), case, sample_dir, None)
        self.assertEqual(Driver.attempts, 1)
        self.assertEqual(result["edit_attempts"], 1)
        self.assertEqual(result["status"], "failed")
        self.assertEqual(result["error_class"], "TimeoutError")
        self.assertNotIn("do not disclose", json.dumps(result))
        self.assertTrue(result["forced_cleanup"])
        self.assertEqual(result["input_sha256_after"], case["input_sha256"])

    def test_save_observer_does_not_open_target_before_clean_ack(self):
        """UI-only polling cannot deny DELETE sharing during atomic replacement."""
        calls = []
        class Driver:
            """Deterministic dirty-to-clean native chrome oracle."""
            modified = True
            saved = False
            observations = 0
            def observe(self, version):
                """Acknowledge clean only after one Save and two polls."""
                self.observations += 1
                if self.saved and self.observations >= 3:
                    self.modified = False
                calls.append("dirty" if self.modified else "clean")
                return {"modified": self.modified}
            def save(self):
                """Record one modifying dispatch."""
                self.saved = True
                calls.append("save")
        driver = Driver()
        child = SimpleNamespace(poll=lambda: None)
        working = SimpleNamespace(stat=lambda: SimpleNamespace(st_size=100))
        def hash_once(path):
            """Assert every target handle occurs strictly after clean acknowledgement."""
            self.assertFalse(driver.modified)
            calls.append("hash")
            return "expected"
        with patch.object(probe, "digest", side_effect=hash_once):
            probe.save_exact(child, driver, working, 100, "expected")
        self.assertEqual(calls, ["dirty", "save", "dirty", "clean", "hash"])

    def test_clean_title_alone_cannot_certify_save(self):
        """A clean UI with wrong exact bytes is failure, not Save acceptance."""
        driver = SimpleNamespace(observe=lambda version: {"modified": True}, save=lambda: None)
        working = SimpleNamespace(stat=lambda: SimpleNamespace(st_size=100))
        with patch.object(probe, "wait", return_value=True), patch.object(probe, "digest", return_value="wrong"):
            with self.assertRaises(ValueError):
                probe.save_exact(None, driver, working, 100, "expected")

    def test_mac_failure_report_retained_without_ax_strings(self):
        """Guard/error metadata survives a failed client while arbitrary strings do not."""
        raw = {"status": "failed", "requested_pid": 42, "guard_stage": "window-count",
               "ax_error": -25204, "window_count": 0, "trusted": True,
               "private_ax_title": "must never enter report"}
        driver = probe.MacDriver(42, 1048576, Path("unused"))
        with patch.object(probe, "bounded_command", return_value=SimpleNamespace(stdout=json.dumps(raw).encode())):
            with self.assertRaises(RuntimeError):
                driver.observe(0)
        result = driver.failure_observation()
        self.assertEqual(result["guard_stage"], "window-count")
        self.assertEqual(result["ax_error"], -25204)
        self.assertNotIn("private_ax_title", result)
        self.assertNotIn("must never", json.dumps(result))
        with self.assertRaises(ValueError):
            probe.mac_report({**raw, "guard_stage": "arbitrary AX text"})

    def test_initial_mac_count_cannot_complete_then_ready(self):
        """A read-only startup pending response reaches the existing liveness-checked wait."""
        pending = {"status": "observed", "requested_pid": 42, "guard_stage": "window-count",
                   "ax_error": -25204, "window_count": None, "ready": False,
                   "trusted": True, "post_event_access": True}
        ready = {**pending, "guard_stage": "ready", "ax_error": 0, "window_count": 1,
                 "window_copy_error": 0, "window_copy_count": 1, "ready": True}
        driver = probe.MacDriver(42, 1048576, Path("unused"))
        results = [SimpleNamespace(stdout=json.dumps(row).encode()) for row in (pending, ready)]
        def condition():
            """Poll one read-only observation, not a modifying retry."""
            data = driver.observe(0)
            return data if data["ready"] else None
        with patch.object(probe, "bounded_command", side_effect=results), patch.object(probe.time, "sleep"):
            result = probe.wait(SimpleNamespace(poll=lambda: None), condition, 60)
        self.assertTrue(result["ready"])
        summary = driver.observation_summary()
        self.assertEqual(summary["attempts"], 2)
        self.assertEqual(summary["first_ax_error"], -25204)
        self.assertEqual(summary["last_ax_error"], 0)
        self.assertIsNotNone(summary["first_ready_ms"])
        self.assertEqual(result["window_copy_count"], 1)

    def test_persistent_mac_count_cannot_complete_times_out(self):
        """Persistent messaging refusal remains a censored failure, never empty-window success."""
        pending = {"status": "observed", "requested_pid": 42, "guard_stage": "window-count",
                   "ax_error": -25204, "window_count": None, "ready": False}
        driver = probe.MacDriver(42, 1048576, Path("unused"))
        result = SimpleNamespace(stdout=json.dumps(pending).encode())
        with patch.object(probe, "bounded_command", return_value=result), patch.object(probe.time, "sleep"), \
             patch.object(probe.time, "monotonic", side_effect=[0, 0, 0.2, 0.4]):
            with self.assertRaises(TimeoutError):
                probe.wait(SimpleNamespace(poll=lambda: None), lambda: driver.observe(0)["ready"], 0.3)
        summary = driver.observation_summary()
        self.assertEqual(summary["attempts"], 2)
        self.assertEqual(summary["first_ax_error"], -25204)
        self.assertEqual(summary["last_ax_error"], -25204)
        self.assertIsNone(summary["first_ready_ms"])
        self.assertIsNone(driver.failure_observation()["window_count"])

    def test_nontransient_mac_count_errors_remain_fatal(self):
        """Only the explicitly pending count response is retried; other guards fail closed."""
        driver = probe.MacDriver(42, 1048576, Path("unused"))
        failed = {"status": "failed", "requested_pid": 42, "guard_stage": "window-count", "ax_error": -25205}
        with patch.object(probe, "bounded_command", return_value=SimpleNamespace(stdout=json.dumps(failed).encode())):
            with self.assertRaises(RuntimeError):
                driver.observe(0)
        self.assertEqual(driver.observation_summary()["attempts"], 1)

    def test_mac_copy_cannot_complete_then_ready(self):
        """Count success and temporary copy messaging refusal are distinct read-only facts."""
        pending = {"status": "observed", "requested_pid": 42, "guard_stage": "window-read",
                   "ax_error": 0, "window_count": 1, "window_copy_error": -25204,
                   "window_copy_count": None, "ready": False}
        ready = {**pending, "guard_stage": "ready", "window_copy_error": 0,
                 "window_copy_count": 1, "ready": True}
        driver = probe.MacDriver(42, 1048576, Path("unused"))
        results = [SimpleNamespace(stdout=json.dumps(row).encode()) for row in (pending, ready)]
        def condition():
            """Require a completed owned-source observation rather than copy-call return."""
            observed = driver.observe(0)
            return observed if observed["ready"] else None
        with patch.object(probe, "bounded_command", side_effect=results), patch.object(probe.time, "sleep"):
            result = probe.wait(SimpleNamespace(poll=lambda: None), condition, 60)
        self.assertTrue(result["ready"])
        summary = driver.observation_summary()
        self.assertEqual(summary["window_copy_attempts"], 2)
        self.assertEqual(summary["first_ax_error"], 0)
        self.assertEqual(summary["first_window_copy_error"], -25204)
        self.assertEqual(summary["last_window_copy_error"], 0)
        self.assertEqual(summary["copy_pending_observations"], 1)
        self.assertEqual(summary["count_pending_observations"], 0)

    def test_persistent_mac_copy_cannot_complete_times_out(self):
        """A successful count cannot turn a persistently unsuccessful copy into readiness."""
        pending = {"status": "observed", "requested_pid": 42, "guard_stage": "window-read",
                   "ax_error": 0, "window_count": 1, "window_copy_error": -25204,
                   "window_copy_count": None, "ready": False}
        driver = probe.MacDriver(42, 1048576, Path("unused"))
        result = SimpleNamespace(stdout=json.dumps(pending).encode())
        with patch.object(probe, "bounded_command", return_value=result), patch.object(probe.time, "sleep"), \
             patch.object(probe.time, "monotonic", side_effect=[0, 0, 0.2, 0.4]):
            with self.assertRaises(TimeoutError):
                probe.wait(SimpleNamespace(poll=lambda: None), lambda: driver.observe(0)["ready"], 0.3)
        self.assertEqual(driver.observation_summary()["copy_pending_observations"], 2)
        self.assertIsNone(driver.observation_summary()["first_ready_ms"])
        self.assertIsNone(driver.failure_observation()["window_copy_count"])

    def test_mac_other_copy_failure_and_pending_modification_are_fatal(self):
        """Neither other copy errors nor pending modifying transactions receive a retry."""
        driver = probe.MacDriver(42, 1048576, Path("unused"))
        failed = {"status": "failed", "requested_pid": 42, "guard_stage": "window-read",
                  "ax_error": 0, "window_count": 1, "window_copy_error": -25205}
        with patch.object(probe, "bounded_command", return_value=SimpleNamespace(stdout=json.dumps(failed).encode())):
            with self.assertRaises(RuntimeError):
                driver.observe(0)
        pending = {**failed, "status": "observed", "window_copy_error": -25204}
        with patch.object(probe, "bounded_command", return_value=SimpleNamespace(stdout=json.dumps(pending).encode())) as command:
            for operation in ("edit", "save", "close"):
                with self.assertRaises(RuntimeError):
                    driver.command(operation, 0)
            self.assertEqual(command.call_count, 3)

    def test_failed_client_does_not_count_stale_copy_report_again(self):
        """A tool timeout retains last validated metadata without claiming another AX copy."""
        driver = probe.MacDriver(42, 1048576, Path("unused"))
        ready = {"status": "observed", "requested_pid": 42, "guard_stage": "ready", "ax_error": 0,
                 "window_count": 1, "window_copy_error": 0, "window_copy_count": 1, "ready": True}
        with patch.object(probe, "bounded_command", side_effect=[SimpleNamespace(stdout=json.dumps(ready).encode()), TimeoutError()]):
            driver.observe(0)
            with self.assertRaises(TimeoutError):
                driver.observe(0)
        summary = driver.observation_summary()
        self.assertEqual(summary["attempts"], 2)
        self.assertEqual(summary["validated_observations"], 1)
        self.assertEqual(summary["window_copy_attempts"], 1)

    def test_save_command_report_survives_ack_timeout_without_retry(self):
        """Posted transaction metadata remains even when the product never acknowledges Save."""
        report = {}
        calls = []
        transaction = {"method": "CGEvent.postToPid", "status": "attempted-posts-no-delivery-acknowledgement",
                       "execution_acknowledged": False, "attempted_events": 2, "target_pid": 42}
        def save():
            """Model one returned transaction rather than target delivery."""
            calls.append("save")
            return transaction
        driver = SimpleNamespace(observe=lambda version: {"modified": True}, save=save)
        with patch.object(probe, "wait", side_effect=TimeoutError()), patch.object(probe, "digest") as read:
            with self.assertRaises(TimeoutError):
                probe.save_exact(None, driver, None, 100, "expected", report)
        self.assertEqual(calls, ["save"])
        self.assertTrue(report["save_command_attempted"])
        self.assertEqual(report["save_command_report"], transaction)
        self.assertFalse(report["save_command_report"]["execution_acknowledged"])
        self.assertGreaterEqual(report["save_command_return_elapsed_ms"], 0)
        read.assert_not_called()

    def test_mac_save_labels_posting_not_delivery(self):
        """A successful client return supplies guard metadata, not a Save-handler acknowledgment."""
        driver = probe.MacDriver(42, 1048576, Path("unused"))
        raw = {"status": "observed", "requested_pid": 42, "guard_stage": "save-dispatch",
               "ax_error": 0, "window_copy_error": 0, "dispatched_events": 2,
               "trusted": True, "post_event_access": True, "ready": True, "focused": True,
               "selection_start": 10, "selection_length": 0,
               "target_app_active": False, "frontmost_is_target": False,
               "window_main": True, "window_focused": None,
               "foreign_frontmost_pid": 99999, "foreign_app_name": "must not escape"}
        with patch.object(probe, "bounded_command", return_value=SimpleNamespace(stdout=json.dumps(raw).encode())):
            result = driver.save()
        self.assertEqual(result["attempted_events"], 2)
        self.assertEqual(result["status"], "attempted-posts-no-delivery-acknowledgement")
        self.assertFalse(result["execution_acknowledged"])
        self.assertEqual(result["guard_report"]["guard_stage"], "save-dispatch")
        self.assertFalse(result["guard_report"]["target_app_active"])
        self.assertFalse(result["guard_report"]["frontmost_is_target"])
        self.assertTrue(result["guard_report"]["window_main"])
        self.assertIsNone(result["guard_report"]["window_focused"])
        self.assertNotIn("foreign_frontmost_pid", result["guard_report"])
        self.assertNotIn("must not escape", json.dumps(result))

    def test_mac_routing_facts_are_nullable_booleans_not_focus_aliases(self):
        """True/false/unavailable activity facts stay separate from responder focus."""
        names = ("target_app_active", "frontmost_is_target", "window_main", "window_focused")
        template = {"status": "observed", "requested_pid": 42, "guard_stage": "ready", "focused": True}
        for value in (True, False, None):
            result = probe.mac_report({**template, **dict.fromkeys(names, value)})
            self.assertTrue(result["focused"])
            for name in names:
                self.assertIs(result[name], value)
        omitted = probe.mac_report(template)
        self.assertTrue(all(omitted[name] is None for name in names))
        for name in names:
            for invalid in (1, 0, "true", "foreign identity"):
                with self.assertRaises(ValueError):
                    probe.mac_report({**template, name: invalid})


if __name__ == "__main__":
    unittest.main()
