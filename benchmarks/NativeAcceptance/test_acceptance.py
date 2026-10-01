"""Artifact-only regression checks; no GUI, user home, input, or clipboard access."""

import copy
import csv
import io
import json
from pathlib import Path
import unittest
import uuid

import acceptance as tool


def record(operation, span, parent=None, status="success", version=None):
    """Create fixed synthetic causal data, including out-of-order parent records."""
    return {"schema_version": 1, "utc_time": "2026-10-01T00:00:00Z",
            "session_id": "a" * 32, "trace_id": "b" * 32,
            "span_id": f"{span:016x}", "parent_span_id": None if parent is None else f"{parent:016x}",
            "operation": operation, "duration_us": 1000 * span, "status": status,
            "attributes": {} if version is None else {"version": version}}


class AcceptanceTests(unittest.TestCase):
    """Check exact workload grammar, integrity failures, and artifact confinement."""

    @classmethod
    def setUpClass(cls):
        """Allocate one repository-local test directory; never use system temp."""
        cls.directory = tool.artifact_path(f".temp/native-acceptance-tests/{uuid.uuid4().hex}")
        cls.directory.mkdir(parents=True)

    @classmethod
    def tearDownClass(cls):
        """Remove only exact files this class created; no recursive deletion."""
        for path in cls.directory.iterdir():
            if path.is_file() and not path.is_symlink():
                path.unlink()
        cls.directory.rmdir()

    def test_all_small_corpora_exact_and_valid(self):
        """Use 1 MiB cases to validate grammar without repeating production timing."""
        csv.field_size_limit(2 * 1048576)
        for format_name in tool.FORMATS:
            for shape in ("many", "long"):
                with self.subTest(format=format_name, shape=shape):
                    path = self.directory / f"{format_name}-{shape}"
                    digest = tool.generate(path, format_name, shape, 1048576)
                    self.assertEqual(64, len(digest))
                    self.assertEqual(1048576, path.stat().st_size)
                    text = path.read_text(encoding="ascii")
                    if format_name == "json":
                        value = json.loads(text)
                        self.assertIsInstance(value, list if shape == "many" else dict)
                    if format_name == "csv":
                        rows = list(csv.reader(io.StringIO(text, newline=""), strict=True))
                        self.assertGreater(len(rows), 1 if shape == "many" else 0)
                        if shape == "many":
                            self.assertEqual(["row", "quoted, value", 'escaped "quote"', "42"], rows[0])
                            self.assertTrue(all(len(row) == 4 for row in rows))
                    self.assertEqual(shape == "long", "\n" not in text)

    def test_out_of_order_joins_and_censoring(self):
        """Cancellation is reported separately from successful duration quantiles."""
        rows = [record("document.edit_to_draw_submission", 3, 2, version=1),
                record("document.edit_to_draw_submission", 5, 4, "cancelled", 0),
                record("document.edit_to_presentation", 2, 1, version=1),
                record("document.edit_to_presentation", 4, 1, "cancelled", 0),
                record("mote.session", 1)]
        result = tool.audit(rows, {"document.edit_to_draw_submission": 2})
        self.assertEqual("pass", result["causal_integrity"])
        summary = result["operations"]["document.edit_to_draw_submission"]
        self.assertEqual(1, summary["outcomes"]["cancelled"])
        self.assertEqual(3000, summary["success_duration"]["p95_us"])
        self.assertTrue(summary["success_duration"]["small_sample"])

    def test_integrity_negative_controls(self):
        """Missing, mismatched, mixed-session and duplicate records cannot pass."""
        base = [record("mote.session", 1), record("document.edit_to_presentation", 2, 1, version=1),
                record("document.edit_to_draw_submission", 3, 2, version=1)]
        mutations = [base[:-1], base[1:], base + [base[-1]], [base[0], base[-1]]]
        mismatched = copy.deepcopy(base)
        mismatched[-1]["attributes"]["version"] = 2
        mutations.append(mismatched)
        mixed = copy.deepcopy(base)
        mixed[-1]["session_id"] = "c" * 32
        mutations.append(mixed)
        dropped = record("telemetry.dropped", 4, 1)
        dropped["attributes"] = {"count": 1}
        mutations.append(base + [dropped])
        missing_parent = copy.deepcopy(base)
        missing_parent[-1]["parent_span_id"] = None
        mutations.append(missing_parent)
        no_versions = copy.deepcopy(base)
        no_versions[-1]["attributes"].clear()
        no_versions[-2]["attributes"].clear()
        mutations.append(no_versions)
        cycle = copy.deepcopy(base)
        cycle[-2]["parent_span_id"] = cycle[-1]["span_id"]
        mutations.append(cycle)
        empty_drop = record("telemetry.dropped", 4, 1)
        mutations.append(base + [empty_drop])
        mutations.append(base + [record("mote.session", 4, status="failure")])
        self_parent = copy.deepcopy(base)
        self_parent[-2]["parent_span_id"] = self_parent[-2]["span_id"]
        mutations.append(self_parent)
        zero_drop = copy.deepcopy(empty_drop)
        zero_drop["attributes"]["count"] = 0
        mutations.append(base + [zero_drop])
        for rows in mutations:
            with self.subTest(records=len(rows)):
                self.assertEqual("incomplete-or-invalid", tool.audit(rows, {
                    "document.edit_to_draw_submission": 1})["causal_integrity"])

    def test_schema_rejects_content_and_bad_duration(self):
        """Fail closed before output for unknown content fields and fake counters."""
        for index, patch in enumerate(({"source_text": "private"}, {"duration_us": -1},
                                       {"duration_us": True}, {"operation": "unknown"},
                                       {"attributes": {"count": True}}, {"attributes": {"count": -1}})):
            row = record("mote.session", 1)
            row.update(patch)
            path = self.directory / f"negative-{index}.jsonl"
            path.write_text(json.dumps(row) + "\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                tool.load_records([str(path)])

    def test_independent_menu_checkpoints_have_closed_content_free_shape(self):
        """Admit all five fixed names without weakening existing schema privacy."""
        rows = [record("mote.session", 1)]
        for span, operation in enumerate(sorted(tool.MENU_OPERATIONS), 2):
            row = record(operation, span, 1)
            row["duration_us"] = 0
            rows.append(row)
        path = self.directory / "menu-checkpoints.jsonl"
        path.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
        loaded, _ = tool.load_records([str(path)])
        self.assertEqual(len(loaded), 6)
        self.assertEqual(tool.audit(loaded, {})["causal_integrity"], "pass")
        for patch in ({"operation": "native.menu.private-document"},
                      {"attributes": {"format": "private-document"}},
                      {"attributes": {"source_text": "private-document"}},
                      {"duration_us": 1}, {"status": "failure"}, {"parent_span_id": None}):
            row = copy.deepcopy(rows[-1])
            row.update(patch)
            path.write_text(json.dumps(row) + "\n", encoding="utf-8")
            with self.subTest(patch=patch), self.assertRaises(ValueError):
                tool.load_records([str(path)])

    def test_artifact_paths_and_no_overwrite(self):
        """Protect repository confinement and previously persisted measurements."""
        for value in ("docs/report.json", ".cache/../docs/report.json", ".temp", "../report.json"):
            with self.subTest(path=value), self.assertRaises(ValueError):
                tool.artifact_path(value)
        path = self.directory / "report.json"
        tool.write_json(path, {"value": 1})
        with self.assertRaises(FileExistsError):
            tool.write_json(path, {"value": 2})

    def test_quantiles_are_descriptive_only(self):
        """Small samples expose their limitation; missing endpoints remain null."""
        self.assertIsNone(tool.distribution([])["p95_us"])
        self.assertEqual(3, tool.distribution([3, 1, 2])["p95_us"])
        self.assertEqual(2.5, tool.distribution([4, 1, 3, 2])["p50_us"])


if __name__ == "__main__":
    unittest.main()
