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

    def semantic_fixture(self, kind="Json"):
        """Create current-version witnesses whose expected source length is independent."""
        for index, operation in enumerate(("analysis.parse", "analysis.published", "native.source.style_publish"), start=100):
            row=self.row(operation,f"{index:016x}","0000000000000001")
            row["attributes"]={"version":1}
            self.records.append(row)
        witness={"schema_version":1,"generation":1,"version":1,"installation_nonce":1,
                 "presentation_sequence":3,"document_kind":kind,"completeness":"Complete",
                 "coverage_start":0,"coverage_length":64,"source_units":64,"token_count":10,
                 "diagnostic_count":0,"style_ready":True,"geometry_known":True,
                 "grid_required":kind=="Csv","grid_ready":kind=="Csv",
                 "grid_rows":3 if kind=="Csv" else 0,"grid_columns":3 if kind=="Csv" else 0,
                 "grid_cells":9 if kind=="Csv" else 0,"grid_pending_cells":0}
        if kind=="PlainText":witness["token_count"]=0
        return witness

    def check_semantics(self,witness,kind="Json",units=64,platform="macos"):
        """Invoke the real combined trace/sidecar oracle on repository-local fixtures."""
        path=self.area/"native-product-semantics.json"
        path.write_text(json.dumps(witness),encoding="utf-8")
        return verify([self.write()],[],True,semantics_path=str(path),expected_kind=kind,expected_units=units,semantic_platform=platform)

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

    def test_default_native_source_witness_requires_actual_surface_operations(self):
        """A normal session alone cannot certify the bare default source profile."""
        with self.assertRaisesRegex(ValueError, "install/readback witness"):
            verify([self.write()], [], require_native_source=True)
        self.records.append(self.row("native.source.install", "0000000000000100", "0000000000000001"))
        with self.assertRaisesRegex(ValueError, "install/readback witness"):
            verify([self.write()], [], require_native_source=True)
        self.records.append(self.row("native.source.readback", "0000000000000101", "0000000000000001"))
        result=verify([self.write()], [], require_native_source=True)
        self.assertTrue(result["traces"][0]["native_source_surface_witness"])

    def test_unlinked_or_wrong_version_save_is_not_certified(self):
        """Counts alone cannot certify a Save attributed to the wrong request/version."""
        self.records[3]["parent_span_id"] = "0000000000000001"
        with self.assertRaisesRegex(ValueError, "complete version-linked Save chain"):
            verify([self.write()], [], True)
        self.records[3]["parent_span_id"] = "0000000000000006"
        self.records[3]["attributes"]["version"] = 9
        with self.assertRaisesRegex(ValueError, "complete version-linked Save chain"):
            verify([self.write()], [], True)

    def test_semantic_witness_current_save_coverage_and_schema(self):
        """Reject partial text, another saved version and user-derived sidecar fields."""
        witness=self.semantic_fixture()
        verdict=self.check_semantics(witness)
        self.assertTrue(verdict["semantics"]["saved_version_linked"])
        for key,value in (("coverage_length",63),("source_units",65),("version",2),
                          ("document_kind","Yaml"),("diagnostic_count",True),("style_ready",1),
                          ("installation_nonce",0),("completeness","Incomplete"),("token_count",10000)):
            with self.subTest(key=key):
                changed=dict(witness);changed[key]=value
                with self.assertRaises(ValueError):self.check_semantics(changed)
        changed=dict(witness);changed["path"]="private source path"
        with self.assertRaisesRegex(ValueError,"unsupported fields"):self.check_semantics(changed)

    def test_semantic_witness_requires_current_analysis_and_style_not_just_saved_text(self):
        """A successful Save cannot certify stale analysis/style identities."""
        witness=self.semantic_fixture()
        for operation in ("analysis.parse","analysis.published","native.source.style_publish"):
            row=next(row for row in self.records if row["operation"]==operation)
            row["attributes"]["version"]=0
            with self.subTest(operation=operation):
                with self.assertRaisesRegex(ValueError,"current analysis/style"):self.check_semantics(witness)
            row["attributes"]["version"]=1

    def test_csv_semantics_requires_all_nine_visible_cells_ready(self):
        """The small CSV includes its header; partial/pending frames do not pass."""
        witness=self.semantic_fixture("Csv")
        self.check_semantics(witness,"Csv")
        for key,value in (("grid_pending_cells",1),("grid_cells",8),("grid_rows",2),
                          ("grid_columns",2),("grid_ready",False),("grid_required",False)):
            changed=dict(witness);changed[key]=value
            with self.subTest(key=key):
                with self.assertRaises(ValueError):self.check_semantics(changed,"Csv")

    def test_plain_text_semantics_has_no_format_tokens(self):
        """Plain text coverage is meaningful without inventing structured syntax."""
        witness=self.semantic_fixture("PlainText")
        self.check_semantics(witness,"PlainText")
        witness["token_count"]=1
        with self.assertRaisesRegex(ValueError,"semantic tokens"):self.check_semantics(witness,"PlainText")

    def test_windows_final_view_schema_is_session_version_linked(self):
        """Windows observations must not borrow a different process's Save or fields."""
        self.semantic_fixture()
        witness={"schema_version":1,"document_kind":"Json","version":1,"trace_session_id":"1"*32,
                 "source_units":64,"analysis_status_current":True,"parse_publish_style_witness":True,
                 "grid_cells":0,"grid_labels_exact":False,
                 "endpoint":"native status/preview or actual owner-data callbacks plus version-linked trace; not physical pixels"}
        self.assertTrue(self.check_semantics(witness,platform="windows")["semantics"]["saved_version_linked"])
        for key,value in (("trace_session_id","3"*32),("version",2),("source_units",65),
                          ("version",True),("analysis_status_current",1),("analysis_status_current",False),
                          ("endpoint","private user path"),("grid_cells",9),("grid_labels_exact",True)):
            changed=dict(witness);changed[key]=value
            with self.subTest(key=key,value=value):
                with self.assertRaises(ValueError):self.check_semantics(changed,platform="windows")
        changed=dict(witness);changed["generation"]=1
        with self.assertRaisesRegex(ValueError,"unsupported fields"):self.check_semantics(changed,platform="windows")
        witness.update(document_kind="Csv",grid_cells=9,grid_labels_exact=True)
        self.check_semantics(witness,"Csv",platform="windows")
        witness["grid_cells"]=8
        with self.assertRaisesRegex(ValueError,"Grid witness"):self.check_semantics(witness,"Csv",platform="windows")

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
