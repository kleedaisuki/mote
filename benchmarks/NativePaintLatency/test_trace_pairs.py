"""Artifact-only independent contract checks; never launch a native/GUI target.

Run with ``python -B -m unittest discover -s benchmarks/NativePaintLatency
-p test_trace_pairs.py -v``. Synthetic schema-v1 records test the classifier,
not native instrumentation or the validity of any real benchmark sample.
"""

from copy import deepcopy
import json
import math
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import summarize_trace_pairs as subject


ROOT = Path(__file__).resolve().parents[2]
SHA = "b" * 64
PROFILE = "fresh-default-home-canvas-experimental-crlf-sentinel"
ENDPOINT = ("first visual-change screen-DC capture with later exact-source Undo/Redo "
            "screen-state oracle; not compositor-present time")


def sample(mode):
    """Supply independently specified successful screen-driver observations."""
    row = dict(status="passed-foreground", trace_mode=mode, executable_sha256=SHA,
               case="many-1", repetition=1, source_bytes=1048576,
               original_sha256="5a900aeab7463e7b2fcf7481453882043dee41ca15f8a9bb8fa367e5c8c43e1f",
               profile=PROFILE, endpoint=ENDPOINT, os="Windows-fixture", cpu_name="fixture",
               logical_processors=4, physical_memory_bytes=8000000000, display_dpi=96,
               canvas_client_width_px=1057, canvas_client_height_px=988,
               primary_display_width_px=1920, primary_display_height_px=1080,
               roi_client_x_px=42, roi_client_y_px=4, roi_width_px=256, roi_height_px=32,
               exit_code=0, process_cpu_status="available", trace_files=[],
               trace_file_count=0, trace_bytes=0)
    for key in ("hosted_desktop", "foreground_at_focus", "foreground_before_edit",
                "normal_exit_observed", "activation_attempts_enabled", "source_unchanged_before_save",
                "disk_oracle_passed", "undo_exact_oracle", "redo_exact_oracle",
                "undo_screen_distinct", "redo_screen_match", "source_specific_verified",
                "synthetic_source_removed"):
        row[key] = True
    for key in ("forced_cleanup", "negative_control_changed", "local_topmost"):
        row[key] = False
    for key in ("input_ack_ms", "launch_to_source_ready_ms", "first_changed_capture_ms",
                "process_cpu_ms", "process_lifetime_ms"):
        row[key] = 11 if mode == "on" else 10
    return row


def series(n=2):
    """Declare all attempts in alternating order before any measurement."""
    manifest = dict(planned_pairs=n, binary_sha256=SHA, fixture="many-1",
                    ordering="odd-off-on-even-on-off")
    entries = []
    for pair in range(1, n + 1):
        for mode in (("off", "on") if pair % 2 else ("on", "off")):
            entries.append(dict(pair=pair, mode=mode, driver_exit_code=0,
                                binary_sha256_after=SHA, row=sample(mode)))
    return manifest, entries


def record(span, operation, parent=None, **attributes):
    """Build schema-v1 transport records, without deriving expected stages from code."""
    return dict(schema_version=1, utc_time="2026-10-01T00:00:00Z", session_id="fixture",
                trace_id="1" * 32, span_id=f"{span:016x}", parent_span_id=parent,
                operation=operation, duration_us=0, status="success", attributes=attributes)


def native_records():
    """Represent three fully linked captured Saves and their normal session root."""
    rows = []
    operations = ("save.admitted", "save.worker_started", "save.gate_wait",
                  "save.snapshot_capture", "save.snapshot_captured", "save.target_check",
                  "save.temp_encode_write", "save.temp_flush", "save.temp_hash",
                  "save.final_target_check", "save.commit_replace", "save.saved_stamp",
                  "save.bookkeeping", "document.save", "save.ui_started", "save.completed")
    for version in (1, 2, 3):
        anchor = version * 100
        rows.append(record(anchor, "command.save.received", f"{900:016x}"))
        rows.extend(record(anchor + i, operation, f"{anchor:016x}", version=version)
                    for i, operation in enumerate(operations, 1))
        rows.append(record(anchor + 90, "command.save", f"{anchor:016x}", version=version))
    rows.append(record(900, "mote.session"))
    for sequence, row in enumerate(rows, 1):
        row["attributes"]["record_sequence"] = sequence
    return rows


def encode(rows):
    """Preserve newline framing, including the final normal-exit record."""
    return b"".join(json.dumps(row).encode("utf-8") + b"\n" for row in rows)


class PairQualificationTests(unittest.TestCase):
    """Check declared-series inference and every reported control independently."""

    def summarize(self, manifest, entries):
        """Use injected artifact loaders only for qualification-unit tests."""
        return subject.summarize(manifest, entries, lambda entry: entry["row"], lambda row: {})

    def test_complete_ordered_pairs_and_direction(self):
        manifest, entries = series(6)
        result = self.summarize(manifest, entries)
        self.assertEqual("qualified", result["comparison"])
        self.assertEqual(6, result["completed_equivalent_pairs"])
        estimate = result["paired_estimate"]["input_ack_ms"]
        self.assertEqual([1] * 6, estimate["on_minus_off_deltas"])
        self.assertEqual([1, 1], estimate["interval"])
        self.assertFalse(result["absence_certified"])
        self.assertFalse(result["appkit_cost_measured"])

    def test_every_incomplete_prefix_has_no_estimate(self):
        manifest, entries = series()
        for count in range(len(entries)):
            with self.subTest(count=count):
                result = self.summarize(manifest, entries[:count])
                self.assertEqual("incomplete", result["comparison"])
                self.assertIsNone(result["paired_estimate"])

    def test_controls_exit_codes_numbers_and_binary_are_strict(self):
        mutations = {"hosted_desktop": False, "foreground_at_focus": False,
                     "foreground_before_edit": False, "normal_exit_observed": False,
                     "forced_cleanup": True, "negative_control_changed": True,
                     "local_topmost": True, "activation_attempts_enabled": False,
                     "exit_code": False, "executable_sha256": "c" * 64,
                     "case": "many-10", "repetition": True,
                     "cpu_name": None, "process_cpu_status": "unavailable"}
        for key in ("source_unchanged_before_save", "disk_oracle_passed", "undo_exact_oracle",
                    "redo_exact_oracle", "undo_screen_distinct", "redo_screen_match",
                    "source_specific_verified", "synthetic_source_removed"):
            mutations[key] = False
        for key, value in mutations.items():
            with self.subTest(key=key):
                manifest, entries = series()
                entries[0]["row"][key] = value
                result = self.summarize(manifest, entries)
                self.assertEqual("not_qualified", result["comparison"])
                self.assertEqual(1, len(result["samples"]))
                self.assertIsNone(result["paired_estimate"])
        for value in (False, -1, None, math.nan, math.inf, "1"):
            self.assertFalse(subject.number(value))

    def test_first_failure_or_identity_change_is_not_filtered(self):
        for key in ("profile", "endpoint", "source_bytes", "original_sha256", "roi_width_px",
                    "cpu_name", "display_dpi", "os"):
            with self.subTest(key=key):
                manifest, entries = series()
                entries[2]["row"][key] = "changed"
                result = self.summarize(manifest, entries)
                self.assertEqual("not_qualified", result["comparison"])
                self.assertEqual(3, len(result["samples"]))
                self.assertEqual(1, result["completed_equivalent_pairs"])
                self.assertIsNone(result["paired_estimate"])

    def test_consistently_wrong_fixed_fixture_is_not_equivalence(self):
        for key, value in (("source_bytes", 1), ("original_sha256", "d" * 64),
                           ("profile", "other-profile"), ("roi_width_px", 255),
                           ("logical_processors", True), ("display_dpi", 0)):
            with self.subTest(key=key):
                manifest, entries = series()
                for entry in entries:
                    entry["row"][key] = value
                self.assertEqual("not_qualified", self.summarize(manifest, entries)["comparison"])

    def test_order_extra_samples_and_bool_driver_exit_rejected(self):
        for mutation in ("order", "extra", "driver", "binary", "pair", "planned"):
            manifest, entries = series()
            if mutation == "order": entries[2]["mode"] = "off"
            if mutation == "extra": entries.append(deepcopy(entries[0]))
            if mutation == "driver": entries[0]["driver_exit_code"] = False
            if mutation == "binary": entries[0]["binary_sha256_after"] = "c" * 64
            if mutation == "pair": entries[0]["pair"] = True
            if mutation == "planned": manifest["planned_pairs"] = True
            with self.subTest(mutation=mutation):
                self.assertEqual("not_qualified", self.summarize(manifest, entries)["comparison"])

    def test_cpu_unavailable_is_null_not_zero(self):
        manifest, entries = series()
        entries[1]["row"].update(process_cpu_ms=None, process_cpu_status="unavailable")
        result = self.summarize(manifest, entries)
        self.assertEqual("qualified", result["comparison"])
        self.assertIsNone(result["paired_estimate"]["process_cpu_ms"]["median"])
        self.assertIsNone(result["paired_estimate"]["process_cpu_ms"]["interval"])

    def test_rank_interval_attainable_coverage(self):
        for n in range(1, 31):
            interval = subject.median_interval(list(range(n)))
            if n < 6:
                self.assertIsNone(interval["interval"])
                self.assertEqual(1 - 2 ** (1 - n), interval["maximum_finite_coverage"])
            else:
                k = interval["ranks"][0]
                coverage = 1 - 2 * sum(math.comb(n, j) for j in range(k)) / 2 ** n
                self.assertGreaterEqual(coverage, .95)
                self.assertEqual([k - 1, n - k], interval["interval"])
                if k + 1 <= (n + 1) // 2:
                    self.assertLess(1 - 2 * sum(math.comb(n, j) for j in range(k + 1)) / 2 ** n, .95)


class ArtifactTests(unittest.TestCase):
    """Exercise public trace qualification and CLI with repository-local artifacts."""

    def setUp(self):
        """Keep even temporary fixtures inside the repository evidence directory."""
        cache = ROOT / ".cache" / "trace-pairs-validation"
        cache.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(dir=cache)
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)

    def trace_row(self, rows=None, data=None):
        """Materialize test-local transport bytes and their independently sized inventory."""
        path = self.directory / "trace.jsonl"
        raw = encode(native_records() if rows is None else rows) if data is None else data
        path.write_bytes(raw)
        row = sample("on")
        row.update(trace_files=[path.relative_to(ROOT).as_posix()], trace_file_count=1, trace_bytes=len(raw))
        return row

    def test_three_actual_contract_chains_and_off_zero_files(self):
        evidence = subject.trace_evidence(self.trace_row(), ROOT)
        self.assertEqual([1, 2, 3], [request["saved_version"] for request in evidence["requests"]])
        self.assertFalse(evidence["absence_certified"])
        self.assertEqual("health_not_certified", evidence["transport_health"])
        self.assertFalse(subject.trace_evidence(sample("off"), ROOT)["requests"])
        row = self.trace_row()
        row["trace_mode"] = "off"
        with self.assertRaises(ValueError): subject.trace_evidence(row, ROOT)

    def test_trace_failures_are_rejected(self):
        for defect in ("partial", "missing_stage", "wrong_version", "missing_root", "failed_root",
                       "drop", "orphan", "four_saves", "inventory", "bytes"):
            with self.subTest(defect=defect):
                rows = native_records()
                if defect == "missing_stage": rows = [r for r in rows if r["operation"] != "save.temp_flush"]
                if defect == "wrong_version": rows[5]["attributes"]["version"] = 9
                if defect == "missing_root": rows.pop()
                if defect == "failed_root": rows[-1]["status"] = "failure"
                if defect == "drop": rows[-1]["attributes"]["dropped_total"] = 1
                if defect == "orphan": rows[2]["parent_span_id"] = f"{999:016x}"
                if defect == "four_saves": rows.append(record(1000, "command.save.received", f"{900:016x}"))
                row = self.trace_row(rows, encode(rows)[:-1] if defect == "partial" else None)
                if defect == "inventory": row["trace_file_count"] = 2
                if defect == "bytes": row["trace_bytes"] += 1
                with self.assertRaises(ValueError): subject.trace_evidence(row, ROOT)

    def test_inventory_counters_exclude_bool_and_invalid_types(self):
        for key in ("trace_file_count", "trace_bytes"):
            for value in (False, True, -1, None, "0", 0.0):
                with self.subTest(key=key, value=value):
                    row = sample("off")
                    row[key] = value
                    with self.assertRaises(ValueError): subject.trace_evidence(row, ROOT)

    def test_paths_reject_traversal_and_redirect(self):
        for path in (".temp/not-evidence", ".cache/../.cache/alias", str(ROOT.parent / "outside")):
            with self.assertRaises(ValueError): subject.artifact_path(path)
        target = self.directory / "target"
        target.mkdir()
        link = self.directory / "redirect"
        try:
            link.symlink_to(target, target_is_directory=True)
        except OSError as error:
            self.skipTest(f"Symlink creation unavailable: {error}")
        with self.assertRaises(ValueError): subject.artifact_path(str(link / "file"))

    def test_cli_retains_summary_and_returns_stop_status(self):
        manifest, entries = series(1)
        (self.directory / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
        for entry in entries:
            output = self.directory / entry["mode"]
            output.mkdir()
            row = entry.pop("row")
            if entry["mode"] == "on":
                retained = output / "many-1-1"
                retained.mkdir()
                trace = retained / "trace.jsonl"
                raw = encode(native_records())
                trace.write_bytes(raw)
                row.update(trace_files=[trace.relative_to(ROOT).as_posix()], trace_file_count=1, trace_bytes=len(raw))
            report = output / "screen-observations.jsonl"
            report.write_text(json.dumps(row) + "\n", encoding="utf-8")
            entry["report"] = report.relative_to(ROOT).as_posix()
        index = self.directory / "index.jsonl"
        command = [sys.executable, "-B", str(Path(subject.__file__)), "--series", str(self.directory)]
        for count, expected in ((1, "incomplete"), (2, "qualified")):
            index.write_text("".join(json.dumps(e) + "\n" for e in entries[:count]), encoding="utf-8")
            completed = subprocess.run(command, cwd=ROOT, capture_output=True, text=True)
            self.assertEqual(0, completed.returncode, completed.stderr)
            summary = json.loads((self.directory / "summary.json").read_text())
            self.assertEqual(expected, summary["comparison"])
        entries[1]["driver_exit_code"] = 1
        index.write_text("".join(json.dumps(e) + "\n" for e in entries), encoding="utf-8")
        completed = subprocess.run(command, cwd=ROOT, capture_output=True, text=True)
        self.assertEqual(2, completed.returncode, completed.stderr)
        summary = json.loads((self.directory / "summary.json").read_text())
        self.assertIsNone(summary["paired_estimate"])
        self.assertEqual(2, len(summary["samples"]))

    def test_cli_rejects_undeclared_retained_files_and_subdirectories(self):
        for defect in ("off-hidden-file", "on-extra-file", "retained-subdirectory"):
            with self.subTest(defect=defect):
                directory = self.directory / defect
                directory.mkdir()
                manifest, entries = series(1)
                (directory / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
                for entry in entries:
                    output = directory / entry["mode"]
                    output.mkdir()
                    row = entry.pop("row")
                    retained = output / "many-1-1"
                    retained.mkdir()
                    if entry["mode"] == "on":
                        trace = retained / "trace.jsonl"
                        raw = encode(native_records())
                        trace.write_bytes(raw)
                        row.update(trace_files=[trace.relative_to(ROOT).as_posix()],
                                   trace_file_count=1, trace_bytes=len(raw))
                    if defect == "off-hidden-file" and entry["mode"] == "off":
                        (retained / "undeclared.jsonl").write_bytes(b"{}\n")
                    if defect == "on-extra-file" and entry["mode"] == "on":
                        (retained / "undeclared.jsonl").write_bytes(b"{}\n")
                    if defect == "retained-subdirectory" and entry["mode"] == "off":
                        (retained / "unlisted-subdirectory").mkdir()
                    report = output / "screen-observations.jsonl"
                    report.write_text(json.dumps(row) + "\n", encoding="utf-8")
                    entry["report"] = report.relative_to(ROOT).as_posix()
                (directory / "index.jsonl").write_text(
                    "".join(json.dumps(e) + "\n" for e in entries), encoding="utf-8")
                command = [sys.executable, "-B", str(Path(subject.__file__)), "--series", str(directory)]
                completed = subprocess.run(command, cwd=ROOT, capture_output=True, text=True)
                self.assertEqual(2, completed.returncode, completed.stderr)
                summary = json.loads((directory / "summary.json").read_text())
                self.assertEqual("not_qualified", summary["comparison"])
                self.assertIsNone(summary["paired_estimate"])
                expected = ("unexpected_retained_trace_inventory" if defect == "retained-subdirectory"
                            else "retained_trace_inventory_not_fully_declared")
                self.assertEqual(expected, summary["reason"])
                for output in directory.glob("*/many-1-1"):
                    self.assertTrue(output.exists(), "Rejected evidence must not be deleted by the CLI")

    def test_receipt_must_link_actual_session_root(self):
        rows = native_records()
        rows[0]["parent_span_id"] = f"{999:016x}"
        with self.assertRaises(ValueError): subject.trace_evidence(self.trace_row(rows), ROOT)

    def test_unhealthy_sequence_or_cyclic_parent_is_rejected(self):
        for defect in ("sequence", "cycle", "session_identity"):
            with self.subTest(defect=defect):
                rows = native_records()
                if defect == "sequence": rows[2]["attributes"]["record_sequence"] = 1
                if defect == "cycle": rows[0]["parent_span_id"] = rows[1]["span_id"]
                if defect == "session_identity": rows[-1]["session_id"] = "other-session"
                with self.assertRaises(ValueError): subject.trace_evidence(self.trace_row(rows), ROOT)


class DriverSourceTests(unittest.TestCase):
    """Inspect protocol branches only; these checks cannot certify a native run."""

    def test_scheduler_is_bounded_and_stops_after_summary_rejection(self):
        source = (Path(__file__).parent / "Measure-WindowsTracePairs.ps1").read_text()
        self.assertIn("[ValidateRange(1, 30)][int] $Pairs", source)
        self.assertIn("$env:RUNNER_ENVIRONMENT -cne 'github-hosted'", source)
        self.assertIn("if ($pair % 2) { @('off', 'on') } else { @('on', 'off') }", source)
        self.assertIn("& $pwsh -NoProfile -File", source)
        self.assertIn("-ExecutablePath $exe -Cases many-1 -Repetitions 1 -TraceMode $mode", source)
        self.assertIn("if ($LASTEXITCODE -ne 0) { $failed = $true; break Series }", source)
        self.assertNotIn("-AllowLocal", source)
        self.assertNotIn("-LocalTopmost", source)
        self.assertLess(source.index("[IO.File]::AppendAllText($index"),
                        source.index("& python -B"))

    def test_screen_driver_preserves_hosted_foreground_and_real_exit_observations(self):
        source = (Path(__file__).parent / "Measure-WindowsScreen.ps1").read_text()
        self.assertIn("if ($hosted -and -not $result.foreground_at_focus)", source)
        self.assertIn("if ($hosted -and -not $result.foreground_before_edit)", source)
        self.assertIn("$result.exit_code = $script:child.ExitCode", source)
        self.assertIn("$script:child.ExitCode -ne 0", source)
        self.assertIn("$result.forced_cleanup = $true", source)
        self.assertIn("$result.process_cpu_ms = $script:child.TotalProcessorTime.TotalMilliseconds", source)
        self.assertIn("process_cpu_ms = $null; process_cpu_status = 'unavailable'", source)
        self.assertIn("Assert-NoReparseAncestors $traceDir", source)
        self.assertIn("Copy-Item -LiteralPath $traceFile.FullName -Destination $destination", source)
        self.assertIn("Sort-Object Name", source)


if __name__ == "__main__":
    unittest.main()
