"""Artifact-only adapter schema fixtures; no GUI, native input, or clipboard access."""

import copy
import json
import unittest
import uuid

import acceptance as tool


def focus_record(*, receipt=False, result="applied"):
    """Build a schema-valid adapter fixture with opaque native-schema identifiers."""
    attrs = {"native_thread_relation": "owner", "managed_admission_relation": "owner",
             "focus_before": "source", "focus_target": "cell"}
    if not receipt:
        attrs.update(focus_after="table", focus_result=result)
    return {"schema_version": 1, "utc_time": "2026-10-01T00:00:00Z",
            "session_id": "a" * 32, "trace_id": "b" * 32,
            "span_id": "0000000000000002" if receipt else "0000000000000003",
            "parent_span_id": "0000000000000001" if receipt else "0000000000000002",
            "operation": tool.FOCUS_RECEIPT_OPERATION if receipt else tool.FOCUS_TERMINAL_OPERATION,
            "duration_us": 0 if receipt else 123,
            "status": "success" if receipt or result in ("applied", "no_change") else "failure",
            "attributes": attrs}


class FocusContractTests(unittest.TestCase):
    """Enforce fixed focus fields without widening legacy attribute acceptance."""

    @classmethod
    def setUpClass(cls):
        """Create only a repository-local synthetic trace directory."""
        cls.directory = tool.artifact_path(f".temp/focus-contract-tests/{uuid.uuid4().hex}")
        cls.directory.mkdir(parents=True)
        cls.path = cls.directory / "fixture.jsonl"

    @classmethod
    def tearDownClass(cls):
        """Delete exactly the fixture and directory owned by this class."""
        if cls.path.exists():
            cls.path.unlink()
        cls.directory.rmdir()

    def load(self, row):
        """Exercise the public bounded reader, not merely a private validator."""
        self.path.write_text(json.dumps(row) + "\n", encoding="utf-8")
        rows, hashes = tool.load_records([str(self.path)])
        self.assertEqual([row], rows)
        self.assertEqual(1, len(hashes))

    def reject(self, row):
        """Require malformed complete records to fail, never discard them."""
        self.path.write_text(json.dumps(row) + "\n", encoding="utf-8")
        for discard_partial in (False, True):
            with self.subTest(discard_partial=discard_partial), self.assertRaises(ValueError):
                tool.load_records([str(self.path)], discard_partial=discard_partial)

    def test_all_closed_dimensions_and_results(self):
        """Accept every producer enum including independent ownership relations."""
        dimensions = {"native_thread_relation": tool.FOCUS_RELATIONS,
                      "managed_admission_relation": tool.FOCUS_RELATIONS,
                      "focus_before": tool.FOCUS_PANES, "focus_after": tool.FOCUS_PANES,
                      "focus_target": tool.FOCUS_TARGETS}
        for receipt in (False, True):
            self.load(focus_record(receipt=receipt))
            for field, values in dimensions.items():
                if receipt and field == "focus_after":
                    continue
                for value in values:
                    with self.subTest(receipt=receipt, field=field, value=value):
                        row = focus_record(receipt=receipt)
                        row["attributes"][field] = value
                        self.load(row)
        for result in tool.FOCUS_RESULTS:
            self.load(focus_record(result=result))
        row = focus_record()
        row["duration_us"] = 0
        self.load(row)

    def test_dimensions_are_exact_and_operation_scoped(self):
        """Reject missing/extra fields and all legacy attributes on focus events."""
        self.assertEqual({"format", "size_bucket", "version", "count", "hresult", "reason"},
                         tool.ATTRIBUTES)
        for receipt in (False, True):
            base = focus_record(receipt=receipt)
            for field in base["attributes"]:
                row = copy.deepcopy(base)
                del row["attributes"][field]
                self.reject(row)
            for field in tool.ATTRIBUTES | {"source_text", "focus_after", "focus_result"}:
                if field in base["attributes"]:
                    continue
                row = copy.deepcopy(base)
                row["attributes"][field] = "private-document"
                self.reject(row)
        for operation in tool.OPERATIONS - tool.FOCUS_OPERATIONS:
            row = focus_record()
            row["operation"] = operation
            self.reject(row)

    def test_rejects_wrong_types_unknown_enums_and_disguised_content(self):
        """Fail closed for booleans, collections, numbers, and content-like labels."""
        for receipt in (False, True):
            base = focus_record(receipt=receipt)
            for field in base["attributes"]:
                for value in (None, True, False, 1, [], {}, "unknown-enum", "private text",
                              "C:/Users/private/file", "owner\n"):
                    with self.subTest(receipt=receipt, field=field, value=value):
                        row = copy.deepcopy(base)
                        row["attributes"][field] = value
                        self.reject(row)

    def test_outcome_duration_and_parent_shape(self):
        """Validate receipt and terminal semantics but leave graph joins separate."""
        for receipt in (False, True):
            for parent in (None, True, 12, "", "user-id", "a" * 32, "A" * 16):
                row = focus_record(receipt=receipt)
                row["parent_span_id"] = parent
                self.reject(row)
            for duration in (True, False, -1, 1.5, "0", None):
                row = focus_record(receipt=receipt)
                row["duration_us"] = duration
                self.reject(row)
            for attributes in (None, [], "private"):
                row = focus_record(receipt=receipt)
                row["attributes"] = attributes
                self.reject(row)
        for result in tool.FOCUS_RESULTS:
            base = focus_record(result=result)
            for status in tool.STATUSES:
                if status == base["status"]:
                    continue
                row = copy.deepcopy(base)
                row["status"] = status
                self.reject(row)
        for status in ("cancelled", "failure", "skipped"):
            row = focus_record(receipt=True)
            row["status"] = status
            self.reject(row)
        row = focus_record(receipt=True)
        row["duration_us"] = 1
        self.reject(row)


if __name__ == "__main__":
    unittest.main()
