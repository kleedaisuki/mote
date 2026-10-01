"""Independent failure controls for the release artifact oracle, without GUI input."""

import json
from pathlib import Path
import shutil
import unittest
import uuid

from release_trace_oracle import ROOT, verify


class ReleaseTraceOracleTests(unittest.TestCase):
    """Reject incomplete/leaky evidence rather than accepting a green child marker."""

    def setUp(self):
        """Create isolated repository-local evidence and fixed, valid causal records."""
        self.area = ROOT / ".temp" / "release-trace-oracle-tests" / uuid.uuid4().hex
        self.area.mkdir(parents=True)
        self.records = [self.row("mote.session", "0000000000000001", None)]
        for index, operation in enumerate(
            ("document.open", "document.edit", "document.save", "save.completed"), start=2
        ):
            self.records.append(self.row(operation, f"{index:016x}", "0000000000000001"))
        receipt = "0000000000000006"
        self.records.append(self.row("command.save.received", receipt, "0000000000000001"))
        self.records.append(self.row("command.save", "0000000000000007", receipt))
        for record in self.records[3:5] + self.records[6:7]:
            record["parent_span_id"] = receipt
            record["attributes"] = {"version": 1}
        phases = ("save.admitted", "save.worker_started", "save.snapshot_captured",
                  "save.ui_started", "save.gate_wait", "save.snapshot_capture",
                  "save.target_check", "save.temp_encode_write", "save.temp_flush",
                  "save.temp_hash", "save.saved_stamp", "save.bookkeeping", "save.commit_move")
        for index, phase in enumerate(phases, start=8):
            record = self.row(phase, f"{index:016x}", receipt)
            record["attributes"] = {"version": 1}
            self.records.append(record)

    def tearDown(self):
        """Remove only the verified UUID child of the repository-local test area."""
        assert self.area.parent == ROOT / ".temp" / "release-trace-oracle-tests"
        shutil.rmtree(self.area)

    @staticmethod
    def row(operation, span, parent):
        """Build a closed-schema record; do not derive expected behavior from the reader."""
        return {"schema_version": 1, "utc_time": "2026-10-02T00:00:00Z",
                "session_id": "1" * 32, "trace_id": "2" * 32,
                "span_id": span, "parent_span_id": parent,
                "operation": operation, "duration_us": 0, "status": "success", "attributes": {}}

    def write(self, records=None, name="trace.jsonl", suffix="\n"):
        """Retain explicit input bytes so partial-row tests exercise the real file reader."""
        path = self.area / name
        path.write_text("\n".join(json.dumps(row) for row in
                                  (self.records if records is None else records)) + suffix,
                        encoding="utf-8")
        return str(path)

    def test_success_and_separate_reopen_session(self):
        """A successful edited session may be accompanied by a clean reopen-only process."""
        edited = self.write()
        reopened = self.write([self.records[0], self.records[1]], name="reopen.jsonl")
        result = verify([edited, reopened], ["private-document-name"], True)
        self.assertEqual("passed", result["status"])
        self.assertEqual(2, len(result["traces"]))

    def test_missing_save_is_not_certified(self):
        """An open-only process is not a Save task, despite a successful terminal marker."""
        with self.assertRaisesRegex(ValueError, "no process trace"):
            verify([self.write(self.records[:2])], [], True)

    def test_unlinked_or_wrong_version_save_is_not_certified(self):
        """Counts alone cannot certify a Save attributed to the wrong request/version."""
        self.records[3]["parent_span_id"] = "0000000000000001"
        with self.assertRaisesRegex(ValueError, "complete version-linked Save chain"):
            verify([self.write()], [], True)
        self.records[3]["parent_span_id"] = "0000000000000006"
        self.records[3]["attributes"]["version"] = 9
        with self.assertRaisesRegex(ValueError, "complete version-linked Save chain"):
            verify([self.write()], [], True)

    def test_leak_and_unknown_operation_are_rejected(self):
        """Content-derived names must not be normalized into acceptable operations."""
        self.records[1]["operation"] = "document.private-secret"
        with self.assertRaises(ValueError):
            verify([self.write()], ["private-secret"])
        self.records[1]["operation"] = "document.open"
        self.records[1]["utc_time"] = "private-secret"
        with self.assertRaisesRegex(ValueError, "private sentinel"):
            verify([self.write()], ["private-secret"])

    def test_missing_parent_and_terminal_are_rejected(self):
        """Missing causal ancestors are missing evidence, not silently ignored rows."""
        self.records[1]["parent_span_id"] = "f" * 16
        with self.assertRaisesRegex(ValueError, "invalid causal"):
            verify([self.write()], [])
        with self.assertRaisesRegex(ValueError, "invalid causal"):
            verify([self.write(self.records[1:])], [])

    def test_partial_record_is_rejected(self):
        """A terminated process must retain complete JSONL, not a discarded suffix."""
        with self.assertRaisesRegex(ValueError, "unterminated"):
            verify([self.write(suffix="")], [])

    def test_cycles_and_duplicates_are_rejected(self):
        """Serialization order does not excuse duplicate identities or causal cycles."""
        self.records[1]["parent_span_id"] = self.records[2]["span_id"]
        self.records[2]["parent_span_id"] = self.records[1]["span_id"]
        with self.assertRaisesRegex(ValueError, "causal-cycle"):
            verify([self.write()], [])
        with self.assertRaisesRegex(ValueError, "duplicate-span"):
            verify([self.write([self.records[0], self.records[0]])], [])


if __name__ == "__main__":
    unittest.main()
