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

    def write_trace(self, rows, tail=b""):
        """Create a bounded original-process trace in the fixture home."""
        home = self.directory / "causal-home"
        trace_dir = home / "traces"
        trace_dir.mkdir(parents=True)
        path = trace_dir / ("mote-trace-" + "a" * 32 + "-000001.jsonl")
        path.write_bytes(b"".join(json.dumps(row).encode() + b"\n" for row in rows) + tail)
        return home

    def complete_save_rows(self):
        """Model contractual Save checkpoint ancestry independently of GUI tooling."""
        rows = [record("mote.session", 1, None), record("command.save.received", 2)]
        phases = sorted(probe.causal_save.MOTE_SAVE_CONTRACT.successful_required)
        for span, phase in enumerate(phases, 3):
            rows.append(record(phase, span, 2, 1))
        rows.extend([record("save.commit_move", 90, 2, 1), record("save.completed", 91, 2, 1),
                     record("command.save", 92, 2, 1)])
        rows[-1]["attributes"]["reason"] = "completed"
        return rows

    def test_complete_native_save_chain_is_separate_from_byte_oracle(self):
        """A closed, version-consistent native request has its own acceptance result."""
        home = self.write_trace(self.complete_save_rows())
        report = probe.save_chain_evidence(home, terminated=True, normal_exit=True)
        self.assertEqual(report["status"], "complete")
        self.assertEqual(report["requests"][0]["saved_version"], 1)
        self.assertFalse(report["absence_certified"])
        self.assertEqual(len(report["trace_sha256"]), 1)

    def test_receipt_only_kill_is_censored_and_unterminated_utf8_tail_discarded(self):
        """Abrupt owned termination never invents a request terminal or absence proof."""
        home = self.write_trace([record("command.save.received", 2)], b'{"operation":"\xe4')
        report = probe.save_chain_evidence(home, terminated=True)
        self.assertEqual(report["status"], "censored")
        self.assertEqual(report["requests"][0]["classification"], "censored")
        self.assertEqual(report["discarded_partial_files"], 1)
        self.assertFalse(report["absence_certified"])

    def test_no_retained_receipt_is_unobserved_even_after_normal_shutdown(self):
        """No request record is a coverage gap, not target non-delivery certification."""
        for normal in (False, True):
            home = self.write_trace([record("mote.session", 1, None)])
            report = probe.save_chain_evidence(home, terminated=True, normal_exit=normal)
            self.assertEqual(report["status"], "unobserved")
            self.assertFalse(report["absence_certified"])
            for path in (home / "traces").iterdir():
                path.unlink()
            (home / "traces").rmdir()
            home.rmdir()

    def test_complete_malformed_record_is_invalid_not_ignored(self):
        """Only an unfinished final row can be censored; complete corruption fails."""
        home = self.write_trace([record("command.save.received", 2)], b'{broken}\n')
        self.assertEqual(probe.save_chain_evidence(home, terminated=True)["status"], "invalid")

    def test_version_mismatch_missing_phase_and_drop_reject_causal_success(self):
        """A coarse successful Save cannot substitute for persistence-chain evidence."""
        for mutate in ("version", "phase", "drop", "duplicate"):
            rows = self.complete_save_rows()
            if mutate == "version":
                next(row for row in rows if row["operation"] == "save.temp_flush")["attributes"]["version"] = 2
            elif mutate == "phase":
                rows = [row for row in rows if row["operation"] != "save.temp_hash"]
            elif mutate == "drop":
                rows.append(record("telemetry.dropped", 93))
                rows[-1]["attributes"]["count"] = 1
            else:
                rows.extend([record("command.save.received", 94), record("command.save", 95, 94, 1)])
            home = self.write_trace(rows)
            report = probe.save_chain_evidence(home, terminated=True, normal_exit=True)
            self.assertEqual(report["status"], "incomplete", mutate)
            for path in (home / "traces").iterdir():
                path.unlink()
            (home / "traces").rmdir()
            home.rmdir()

    def test_closed_reason_and_operation_vocabulary_remains_strict(self):
        """New instrumentation does not admit arbitrary content-bearing names."""
        for bad in ("reason", "operation"):
            rows = self.complete_save_rows()
            if bad == "reason":
                rows[-1]["attributes"]["reason"] = "user document contents"
            else:
                rows[-1]["operation"] = "user document contents"
            home = self.write_trace(rows)
            self.assertEqual(probe.save_chain_evidence(home, terminated=True)["status"], "invalid")
            for path in (home / "traces").iterdir():
                path.unlink()
            (home / "traces").rmdir()
            home.rmdir()

    def test_menu_inventory_is_independent_of_complete_save_chain(self):
        """Extra menu checkpoints neither join a request nor change Save acceptance."""
        rows = self.complete_save_rows()
        for span, operation in enumerate(sorted(probe.acceptance.MENU_OPERATIONS), 100):
            row = record(operation, span)
            row["duration_us"] = 0
            rows.append(row)
        report = probe.save_chain_evidence(self.write_trace(rows), terminated=True, normal_exit=True)
        self.assertEqual(report["status"], "complete")
        self.assertEqual(len(report["requests"]), 1)
        inventory = report["native_menu_inventory"]
        self.assertEqual(inventory["status"], "observed")
        self.assertEqual(inventory["boundary"], "normal-exit-observed")
        self.assertEqual(inventory["request_correlation"], "none")
        self.assertFalse(inventory["absence_certified"])
        for outcomes in inventory["counts"].values():
            self.assertEqual(outcomes, {"success": 1, "cancelled": 0, "failure": 0, "skipped": 0})

    def test_menu_only_killed_prefix_retains_positives_without_fabricating_request(self):
        """A missing request or return after a menu entry cannot certify nonexecution."""
        rows = []
        for span in (2, 3):
            row = record("native.menu.save_family.entered", span)
            row["duration_us"] = 0
            rows.append(row)
        report = probe.save_chain_evidence(self.write_trace(rows, b'{"operation":"'), terminated=True)
        self.assertEqual(report["status"], "unobserved")
        self.assertEqual(report["requests"], [])
        self.assertEqual(report["discarded_partial_files"], 1)
        inventory = report["native_menu_inventory"]
        self.assertEqual(inventory["boundary"], "censored")
        self.assertEqual(inventory["counts"]["native.menu.save_family.entered"]["success"], 2)
        self.assertEqual(inventory["counts"]["native.menu.save_family.returned_true"]["success"], 0)
        self.assertFalse(inventory["absence_certified"])

    def test_missing_menu_inventory_is_unobserved_not_absence_certified(self):
        """Even a normal successful Save does not imply menu callback coverage."""
        report = probe.save_chain_evidence(self.write_trace(self.complete_save_rows()),
                                          terminated=True, normal_exit=True)
        inventory = report["native_menu_inventory"]
        self.assertEqual(inventory["status"], "unobserved")
        self.assertFalse(inventory["absence_certified"])
        self.assertTrue(all(not any(outcomes.values()) for outcomes in inventory["counts"].values()))

    def test_menu_unknown_vocabulary_and_content_are_invalid(self):
        """Reject arbitrary names and content instead of reflecting them into reports."""
        for patch_value in ({"operation": "native.menu.user-private-text"},
                            {"attributes": {"format": "user-private-text"}}):
            row = record("native.menu.observation.ready", 2)
            row["duration_us"] = 0
            row.update(patch_value)
            home = self.write_trace([row])
            report = probe.save_chain_evidence(home, terminated=True)
            self.assertEqual(report["status"], "invalid")
            self.assertNotIn("user-private-text", json.dumps(report))
            for path in (home / "traces").iterdir():
                path.unlink()
            (home / "traces").rmdir()
            home.rmdir()

    def input_row(self, operation, span):
        """Specify monitor checkpoint shape independently of production constants."""
        row = record(operation, span)
        row["duration_us"] = 0
        if operation in {"native.input.monitor.unavailable", "native.input.monitor.callback_failed",
                         "native.input.monitor.removal_failed"}:
            row["status"] = "failure"
        return row

    def test_input_inventory_does_not_change_complete_save_contract(self):
        """Monitor and menu evidence remain independent of the request certificate."""
        operations = ("native.input.monitor.ready", "native.input.monitor.unavailable",
                      "native.input.monitor.callback_failed", "native.input.monitor.removed",
                      "native.input.monitor.removal_failed", "native.input.save_family_candidate")
        rows = self.complete_save_rows()
        rows += [self.input_row(operation, span) for span, operation in enumerate(operations, 100)]
        menu = record("native.menu.save_family.entered", 110)
        menu["duration_us"] = 0
        rows.append(menu)
        report = probe.save_chain_evidence(self.write_trace(rows), terminated=True, normal_exit=True)
        self.assertEqual(report["status"], "complete")
        self.assertEqual(len(report["requests"]), 1)
        self.assertEqual(report["native_menu_inventory"]["counts"]["native.menu.save_family.entered"]["success"], 1)
        inventory = report["native_input_inventory"]
        self.assertEqual(set(inventory["counts"]), set(operations))
        self.assertEqual(inventory["status"], "observed")
        self.assertEqual(inventory["boundary"], "normal-exit-observed")
        for operation in operations:
            status = self.input_row(operation, 200)["status"]
            self.assertEqual(inventory["counts"][operation][status], 1)
            self.assertEqual(sum(inventory["counts"][operation].values()), 1)
        self.assertEqual(inventory["candidate_to_menu_edge"], "unknown")
        self.assertEqual(inventory["menu_to_request_edge"], "unknown")
        self.assertEqual(inventory["request_correlation"], "none")
        self.assertFalse(inventory["absence_certified"])

    def test_input_only_killed_prefix_does_not_fabricate_menu_or_request(self):
        """Repeated retained candidates do not prove subsequent routing or absence."""
        rows = [self.input_row("native.input.monitor.ready", 2),
                self.input_row("native.input.save_family_candidate", 3),
                self.input_row("native.input.save_family_candidate", 4)]
        report = probe.save_chain_evidence(self.write_trace(rows, b'{"operation":"'), terminated=True)
        self.assertEqual(report["status"], "unobserved")
        self.assertEqual(report["requests"], [])
        self.assertEqual(report["discarded_partial_files"], 1)
        self.assertEqual(report["native_menu_inventory"]["status"], "unobserved")
        inventory = report["native_input_inventory"]
        self.assertEqual(inventory["boundary"], "censored")
        self.assertEqual(inventory["counts"]["native.input.save_family_candidate"]["success"], 2)
        self.assertEqual(inventory["counts"]["native.input.monitor.removed"]["success"], 0)
        self.assertFalse(inventory["absence_certified"])
        self.assertEqual(inventory["candidate_to_menu_edge"], "unknown")

    def test_missing_input_inventory_is_unobserved_across_lifecycle_boundaries(self):
        """Empty evidence is not receipt denial, regardless of process termination."""
        for terminated, normal, boundary in ((False, False, "open"), (True, False, "censored"),
                                             (True, True, "normal-exit-observed")):
            inventory = probe.native_input_inventory([], terminated=terminated, normal_exit=normal)
            self.assertEqual(inventory["status"], "unobserved")
            self.assertEqual(inventory["boundary"], boundary)
            self.assertFalse(inventory["absence_certified"])
            self.assertTrue(all(not any(outcomes.values()) for outcomes in inventory["counts"].values()))

    def test_no_trace_file_still_has_explicit_unknown_input_inventory(self):
        """The no-file return must not omit the coverage gap or claim a receipt."""
        report = probe.save_chain_evidence(self.directory, terminated=True)
        self.assertEqual(report["status"], "unobserved")
        self.assertEqual(report["trace_sha256"], [])
        self.assertEqual(report["native_input_inventory"]["status"], "unobserved")
        self.assertEqual(report["native_input_inventory"]["boundary"], "censored")
        self.assertFalse(report["native_input_inventory"]["absence_certified"])

    def test_input_unknown_content_status_and_shape_are_invalid(self):
        """Complete invalid input rows fail evidence without reflecting private text."""
        for patch_value in ({"operation": "native.input.private-text"},
                            {"attributes": {"format": "private-text"}}, {"duration_us": 1},
                            {"status": "failure"}, {"parent_span_id": None}):
            row = self.input_row("native.input.monitor.ready", 2)
            row.update(patch_value)
            home = self.write_trace([row])
            report = probe.save_chain_evidence(home, terminated=True)
            self.assertEqual(report["status"], "invalid", patch_value)
            self.assertNotIn("private-text", json.dumps(report))
            for path in (home / "traces").iterdir():
                path.unlink()
            (home / "traces").rmdir()
            home.rmdir()

    def test_environment_strips_inherited_witness(self):
        """Default, runtime-control and reopen cannot inherit opt-in instrumentation."""
        with patch.dict(probe.os.environ, {probe.ENVIRONMENT_KEY: "1"}):
            env = probe.environment(self.directory)
        self.assertNotIn(probe.ENVIRONMENT_KEY, env)
        self.assertEqual(env["MOTE_TRACE"], "1")

    def test_witness_is_original_child_only_and_collected_before_reopen(self):
        """Normal original evidence is finalized before child replacement; oracles stay intact."""
        case = probe.prepare(self.directory, [1])[0]
        sample_dir = self.directory / "sample"
        sample_dir.mkdir()
        events, children = [], []
        class Collector:
            """Observe association/lifecycle without launching any diagnostic thread."""
            def __init__(self):
                """Require initialization before the original Popen call."""
                events.append("initialize")
            def attach(self, stream):
                """Associate with only the original pipe."""
                self_stream = stream
                events.append("attach")
                if self_stream is not children[0].stderr:
                    raise AssertionError("wrong child association")
            def finish(self, original_normal_exit=False):
                """Mark the bounded evidence collection boundary."""
                events.append("finish")
                if not original_normal_exit:
                    raise AssertionError("original normal exit not supplied")
                return {"positive_original_only": True}
        class Driver:
            """Model an ordinary source, no input activation or retries."""
            def __init__(self, *arguments):
                """No native APIs are initialized."""
            def observe(self, version):
                """Certify the preexisting source/semantic predicates."""
                return {"ready": True, "complete": True}
            def observation_summary(self):
                """Keep the Mac diagnostic metadata interface inert."""
                return {}
            def edit(self):
                """Record exactly one original edit dispatch."""
                events.append("edit")
            def close(self):
                """Record unchanged ordinary close dispatch."""
                events.append("close")
        def launch(*arguments, **kwargs):
            """Assert explicit opt-in on only the original process."""
            first = not children
            self.assertEqual(kwargs["stderr"], probe.subprocess.PIPE if first else probe.subprocess.DEVNULL)
            self.assertEqual(kwargs["env"].get(probe.ENVIRONMENT_KEY), "1" if first else None)
            if not first:
                self.assertIn("finish", events)
            events.append("original-launch" if first else "reopen-launch")
            child = SimpleNamespace(pid=42, stderr=object(), poll=lambda: None)
            def exited(timeout):
                """Transition the fake owned process only on normal wait."""
                child.poll = lambda: 0
                return 0
            child.wait = exited
            children.append(child)
            return child
        def runtime(arguments, seconds, env):
            """Assert runtime-control never inherits the diagnostic switch."""
            self.assertNotIn(probe.ENVIRONMENT_KEY, env)
        def save(child, driver, working, size, expected, result):
            """Perform the fixture's exact one-byte mutation, preserving later byte oracles."""
            events.append("save")
            with working.open("r+b") as stream:
                stream.seek(probe.EDIT_OFFSET)
                stream.write(b"X")
        with patch.dict(probe.os.environ, {probe.ENVIRONMENT_KEY: "1"}), \
             patch.object(probe, "SaveDiagnosticCollector", Collector), \
             patch.object(probe, "WindowsDriver", Driver), patch.object(probe, "MacDriver", Driver), \
             patch.object(probe, "bounded_command", side_effect=runtime), \
             patch.object(probe.subprocess, "Popen", side_effect=launch), \
             patch.object(probe, "save_exact", side_effect=save), \
             patch.object(probe, "trace_evidence", return_value={"endpoint_integrity": "pass"}), \
             patch.object(probe, "save_chain_evidence", return_value={"status": "complete"}) as causal:
            result = probe.sample(Path("unused"), case, sample_dir, None, True)
        self.assertEqual(result["status"], "pass")
        self.assertEqual(result["editor_exit_code"], 0)
        self.assertEqual(result["reopen_exit_code"], 0)
        self.assertEqual(causal.call_count, 2)
        self.assertTrue(all(call.args[0] == sample_dir / "home" for call in causal.call_args_list))
        self.assertTrue(all(call.kwargs["normal_exit"] for call in causal.call_args_list))
        self.assertEqual(result["mac_save_witness"], {"positive_original_only": True})
        self.assertEqual(events.count("attach"), 1)
        self.assertEqual(events.count("finish"), 1)
        self.assertEqual(events.count("edit"), 1)
        self.assertEqual(events.count("save"), 1)
        self.assertLess(events.index("initialize"), events.index("original-launch"))
        self.assertLess(events.index("finish"), events.index("reopen-launch"))

    def test_witness_finalized_after_forced_original_cleanup(self):
        """Failed Save/edit does not trigger retry, and retained facts survive owned kill."""
        case = probe.prepare(self.directory, [1])[0]
        sample_dir = self.directory / "sample"
        sample_dir.mkdir()
        events = []
        class Child:
            """A live owned process that only forced cleanup can terminate."""
            pid, stderr, alive = 42, object(), True
            def poll(self):
                """Keep existing liveness checks faithful to child ownership."""
                return None if self.alive else -9
            def kill(self):
                """Observe ordering relative to collector finalization."""
                events.append("kill")
                self.alive = False
            def wait(self, timeout):
                """Bounded wait follows termination."""
                events.append("wait")
                return -9
        class Collector:
            """Return already received positive markers after the forced exit."""
            def attach(self, stream):
                """Attach only to the original owned pipe."""
                events.append("attach")
            def finish(self, original_normal_exit=False):
                """Retain evidence only after owned process cleanup."""
                events.append("finish")
                if original_normal_exit:
                    raise AssertionError("forced exit classified normal")
                return {"retained_selector": True, "stream_completion": "censored"}
        class Driver:
            """Model one timed-out edit and a failed normal-close attempt."""
            def __init__(self, *arguments):
                """Do not initialize native APIs."""
            def observe(self, version):
                """Let unchanged initial predicates pass."""
                return {"ready": True, "complete": True}
            def observation_summary(self):
                """Preserve Mac diagnostic shape."""
                return {}
            def failure_observation(self):
                """Return no source content during cleanup."""
                return {}
            def edit(self):
                """Consume exactly one attempt before failing."""
                events.append("edit")
                raise TimeoutError()
            def close(self):
                """Prevent normal cleanup without retrying Save."""
                raise TimeoutError()
        with patch.object(probe, "SaveDiagnosticCollector", Collector), \
             patch.object(probe, "WindowsDriver", Driver), patch.object(probe, "MacDriver", Driver), \
             patch.object(probe, "bounded_command"), patch.object(probe.subprocess, "Popen", return_value=Child()):
            result = probe.sample(Path("unused"), case, sample_dir, None, True)
        self.assertEqual(result["status"], "failed")
        self.assertTrue(result["forced_cleanup"])
        self.assertTrue(result["mac_save_witness"]["retained_selector"])
        self.assertEqual(events, ["attach", "edit", "kill", "wait", "finish"])

    def test_failed_workload_normal_cleanup_has_separate_transport_completeness(self):
        """Healthy diagnostic transport cannot promote a failed workload to Save acceptance."""
        import io
        case = probe.prepare(self.directory, [1])[0]
        sample_dir = self.directory / "sample"
        sample_dir.mkdir()
        child = SimpleNamespace(pid=42, alive=True,
                                stderr=io.BytesIO(b"mote-save-diag-v1:ready\n"
                                                  b"mote-save-diag-v1:completed\n"))
        child.poll = lambda: None if child.alive else 0
        def exited(timeout):
            """Model successful normal owned cleanup only after the failure."""
            child.alive = False
            return 0
        child.wait = exited
        class Driver:
            """Fail the one edit attempt but permit ordinary normal-close cleanup."""
            def __init__(self, *arguments):
                """Avoid platform APIs in this lifecycle test."""
            def observe(self, version):
                """Let unchanged initial predicates pass."""
                return {"ready": True, "complete": True}
            def observation_summary(self):
                """Return content-free Mac metadata."""
                return {}
            def failure_observation(self):
                """Return no document contents."""
                return {}
            def edit(self):
                """Consume one attempt and preserve the failed workload status."""
                raise TimeoutError()
            def close(self):
                """Permit normal cleanup without another input attempt."""
        with patch.object(probe, "WindowsDriver", Driver), patch.object(probe, "MacDriver", Driver), \
             patch.object(probe, "bounded_command"), patch.object(probe.subprocess, "Popen", return_value=child):
            result = probe.sample(Path("unused"), case, sample_dir, None, True)
        self.assertEqual(result["status"], "failed")
        self.assertFalse(result["normal_exit"])
        self.assertTrue(result["failure_cleanup_normal_exit"])
        self.assertTrue(result["mac_save_witness"]["original_normal_exit"])
        self.assertTrue(result["mac_save_witness"]["healthy_completed_stream"])
        self.assertEqual(result["edit_attempts"], 1)

    def test_witness_startup_failure_is_censored_without_child(self):
        """Popen failure still finalizes preinitialized diagnostic state without fake EOF."""
        case = probe.prepare(self.directory, [1])[0]
        sample_dir = self.directory / "sample"
        sample_dir.mkdir()
        with patch.object(probe, "bounded_command"), \
             patch.object(probe.subprocess, "Popen", side_effect=OSError("private startup text")):
            result = probe.sample(Path("unused"), case, sample_dir, None, True)
        self.assertEqual(result["status"], "failed")
        self.assertFalse(result["mac_save_witness"]["eof"])
        self.assertEqual(result["mac_save_witness"]["records"], [])
        self.assertNotIn("private startup text", json.dumps(result))
        self.assertIsNone(result["editor_exit_code"])
        self.assertIsNone(result["reopen_exit_code"])

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
        """Legacy unversioned records can pass generic audit, not typed Save certification."""
        home, path, rows = self.trace()
        for index in (2, 8, 9):
            rows[index]["attributes"] = {}
        path.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
        result = probe.trace_evidence(home, True)
        self.assertEqual(result["endpoint_integrity"], "pass")
        self.assertIsNone(result["child_monotonic_endpoints"]["document.save"][0]["version"])
        self.assertEqual(result["io_revision_contract"], "open-engine-record-optional-version;save-captured-version-required-by-separate-causal-contract")

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

    def test_save_timeout_finally_collects_original_prefix_without_retry(self):
        """The ordinary sample persists a killed request, not just helper fixtures."""
        case = probe.prepare(self.directory, [1])[0]
        sample_dir = self.directory / "timeout-sample"
        sample_dir.mkdir()
        class Child:
            """Only this owned fake is killed and reaped."""
            pid = 42
            killed = False
            def poll(self):
                """Expose abrupt termination distinctly from exit zero."""
                return -9 if self.killed else None
            def kill(self):
                """Record exact owned cleanup."""
                self.killed = True
            def wait(self, timeout):
                """Return the owned abrupt exit."""
                return self.poll()
        class Driver:
            """Execute one Save attempt that retains receipt but times out."""
            attempts = 0
            def __init__(self, *arguments):
                """No OS handles are touched."""
            def observe(self, version):
                """Supply the source/semantics preconditions without byte changes."""
                return {"ready": True, "complete": True, "modified": True}
            def observation_summary(self):
                """Provide only content-free Mac-interface metadata."""
                return {}
            def edit(self):
                """Keep the fixture's on-disk bytes unchanged before Save."""
            def save(self):
                """Retain target callback entry before the external timeout."""
                Driver.attempts += 1
                traces = sample_dir / "home" / "traces"
                traces.mkdir(parents=True)
                (traces / ("mote-trace-" + "a" * 32 + "-000001.jsonl")).write_text(
                    json.dumps(record("command.save.received", 2)) + "\n", encoding="utf-8")
                raise TimeoutError()
            def failure_observation(self):
                """Return no fabricated routing acknowledgement."""
                return {}
            def close(self):
                """The dirty process does not close normally."""
        child = Child()
        with patch.object(probe, "WindowsDriver", Driver), patch.object(probe, "MacDriver", Driver), \
             patch.object(probe, "bounded_command"), patch.object(probe.subprocess, "Popen", return_value=child):
            result = probe.sample(Path("unused"), case, sample_dir, None)
        self.assertEqual(Driver.attempts, 1)
        self.assertEqual(result["status"], "failed")
        self.assertEqual(result["phase"], "save-exact-bytes")
        self.assertEqual(result["editor_exit_code"], -9)
        self.assertIsNone(result["reopen_exit_code"])
        self.assertEqual(result["save_causal_evidence"]["status"], "censored")
        self.assertEqual(result["save_causal_evidence"]["requests"][0]["last_positive_stage"], "command.save.received")
        self.assertFalse(result["save_causal_evidence"]["normal_exit"])
        self.assertFalse(result["save_causal_evidence"]["absence_certified"])
        self.assertTrue(result["forced_cleanup"])
        self.assertEqual(result["working_sha256_after"], case["input_sha256"])

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

    def test_mac_rejected_edit_preflight_survives_cleanup_report(self):
        """Transaction entry is not input dispatch; later close cannot erase it."""
        failed = {"status": "failed", "requested_pid": 42, "guard_stage": "window-count",
                  "ax_error": -25204, "dispatched_events": 0,
                  "private_ax_value": "must never enter report"}
        closed = {"status": "observed", "requested_pid": 42, "guard_stage": "complete"}
        driver = probe.MacDriver(42, 1048576, Path("unused"))
        returns = [SimpleNamespace(stdout=json.dumps(row).encode()) for row in (failed, closed)]
        with patch.object(probe, "bounded_command", side_effect=returns) as command:
            with self.assertRaises(RuntimeError):
                driver.edit()
            retained = driver.last_edit_report
            driver.close()
        self.assertEqual([call.args[0][3] for call in command.call_args_list], ["edit", "close"])
        self.assertIs(driver.last_edit_report, retained)
        self.assertEqual(retained["guard_stage"], "window-count")
        self.assertEqual(retained["dispatched_events"], 0)
        self.assertEqual(driver.last_report["guard_stage"], "complete")
        self.assertNotIn("private_ax_value", retained)

    def test_mac_acknowledged_edit_report_survives_later_read_failure(self):
        """A failed ready poll must not replace the independent edit transaction."""
        edited = {"status": "observed", "requested_pid": 42, "guard_stage": "edit-ack",
                  "dispatched_events": 22, "selection_start": 10, "selection_length": 0}
        pending = {"status": "failed", "requested_pid": 42, "guard_stage": "window-count",
                   "ax_error": -25204, "dispatched_events": 0}
        driver = probe.MacDriver(42, 1048576, Path("unused"))
        returns = [SimpleNamespace(stdout=json.dumps(row).encode()) for row in (edited, pending)]
        with patch.object(probe, "bounded_command", side_effect=returns) as command:
            driver.edit()
            with self.assertRaises(RuntimeError):
                driver.observe(1)
        self.assertEqual(command.call_count, 2)
        self.assertEqual(driver.last_edit_report["guard_stage"], "edit-ack")
        self.assertEqual(driver.last_edit_report["dispatched_events"], 22)
        self.assertEqual(driver.last_report["guard_stage"], "window-count")

    def test_mac_unvalidated_edit_never_reuses_ready_report(self):
        """Timeout, malformed JSON and foreign identity leave this boundary unknown."""
        ready = {"status": "observed", "requested_pid": 42, "guard_stage": "ready"}
        for response, error in ((TimeoutError(), TimeoutError),
                                (SimpleNamespace(stdout=b"not-json"), ValueError),
                                (SimpleNamespace(stdout=json.dumps({**ready, "requested_pid": 99}).encode()), ValueError)):
            driver = probe.MacDriver(42, 1048576, Path("unused"))
            driver.last_report = probe.mac_report(ready)
            driver.last_edit_report = probe.mac_report(ready)
            with patch.object(probe, "bounded_command", side_effect=[response]) as command:
                with self.assertRaises(error):
                    driver.edit()
            self.assertEqual(command.call_count, 1)
            self.assertIsNone(driver.last_edit_report)
            self.assertEqual(driver.last_report["guard_stage"], "ready")

    def test_sample_retains_rejected_mac_edit_without_promoting_cleanup(self):
        """The reported zero-post transaction remains failed despite editor exit zero."""
        case = probe.prepare(self.directory, [1])[0]
        sample_dir = self.directory / "edit-preflight"
        sample_dir.mkdir()
        child = SimpleNamespace(pid=42, alive=True)
        child.poll = lambda: None if child.alive else 0
        child.wait = lambda timeout: 0
        operations = []
        ready = {"status": "observed", "requested_pid": 42, "guard_stage": "ready",
                 "ready": True, "complete": True}
        failed = {"status": "failed", "requested_pid": 42, "guard_stage": "window-count",
                  "ax_error": -25204, "dispatched_events": 0}
        def client(arguments, *unused):
            """Only owned close changes fake child liveness; no OS action occurs."""
            if "--check-runtime" in arguments:
                return SimpleNamespace(stdout=b"")
            operation = arguments[3]
            operations.append(operation)
            row = failed if operation == "edit" else ready
            if operation == "close":
                child.alive = False
            return SimpleNamespace(stdout=json.dumps(row).encode())
        with patch.object(probe.sys, "platform", "darwin"), \
             patch.object(probe.subprocess, "Popen", return_value=child), \
             patch.object(probe, "bounded_command", side_effect=client), \
             patch.object(probe, "save_chain_evidence", return_value={"status": "unobserved"}):
            result = probe.sample(Path("unused"), case, sample_dir, Path("client"))
        self.assertEqual(operations, ["observe", "observe", "edit", "close"])
        self.assertEqual(result["mac_edit_transaction_report"]["dispatched_events"], 0)
        self.assertEqual(result["mac_edit_transaction_report"]["guard_stage"], "window-count")
        self.assertEqual(result["edit_attempts"], 1)
        self.assertEqual(result["status"], "failed")
        self.assertEqual(result["phase"], "native-local-edit")
        self.assertEqual(result["editor_exit_code"], 0)
        self.assertTrue(result["failure_cleanup_normal_exit"])
        self.assertFalse(result["normal_exit"])
        self.assertIsNone(result["reopen_exit_code"])

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
