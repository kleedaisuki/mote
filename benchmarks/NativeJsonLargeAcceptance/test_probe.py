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


if __name__ == "__main__":
    unittest.main()
