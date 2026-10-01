"""Portable product source vocabulary checks against the actual strict reader."""

import unittest

import acceptance as tool


SOURCE_OPERATIONS = (
    "native.source.install", "native.source.readback", "native.source.reconcile",
    "native.source.range_publish", "native.source.style_publish",
)


def source_row(operation):
    """Construct public v1 data independently of the producer and allowlist."""
    return {
        "schema_version": 1, "utc_time": "2026-10-02T00:00:00Z",
        "session_id": "a" * 32, "trace_id": "b" * 32,
        "span_id": "c" * 16, "parent_span_id": "d" * 16,
        "operation": operation, "duration_us": 123, "status": "success",
        "attributes": {"format": "json", "size_bucket": "1-4KiB",
                       "version": 7, "count": 2},
    }


class NativeSourceOperationTests(unittest.TestCase):
    """Adding fixed names must not relax schema, privacy, or value validation."""

    def test_exact_vocabulary_accepts_content_free_rows_and_all_statuses(self):
        """Five fixed duration names retain all four established outcome values."""
        self.assertEqual(frozenset(SOURCE_OPERATIONS), tool.NATIVE_SOURCE_OPERATIONS)
        for operation in SOURCE_OPERATIONS:
            for status in ("success", "failure", "cancelled", "skipped"):
                with self.subTest(operation=operation, status=status):
                    row = source_row(operation)
                    row["status"] = status
                    tool.validate_record(row)

    def test_unknown_source_names_remain_rejected(self):
        """No prefix wildcard, user-derived operation, or spelling alias is allowed."""
        for operation in ("native.source", "native.source.install.extra",
                          "native.source.SECRET.txt", "native.source.range-publish"):
            with self.subTest(operation=operation):
                with self.assertRaisesRegex(ValueError, "unknown operation/status"):
                    tool.validate_record(source_row(operation))

    def test_content_and_foreign_attributes_remain_rejected(self):
        """New names accept neither content fields nor unrelated native dimensions."""
        for operation in SOURCE_OPERATIONS:
            for attribute in ("text", "path", "exception", "native_handle", "focus_target"):
                with self.subTest(operation=operation, attribute=attribute):
                    row = source_row(operation)
                    row["attributes"][attribute] = "SECRET-user-content"
                    with self.assertRaisesRegex(ValueError, "unsupported trace attributes"):
                        tool.validate_record(row)

    def test_existing_schema_numeric_and_closed_dimension_checks_remain_strict(self):
        """Fixed new names do not bypass ordinary v1 root or dimension rules."""
        changes = (
            ("schema_version", 2), ("duration_us", -1), ("duration_us", True),
            ("parent_span_id", "SECRET"), ("status", "unknown"),
        )
        for operation in SOURCE_OPERATIONS:
            for key, value in changes:
                with self.subTest(operation=operation, field=key, value=value):
                    row = source_row(operation)
                    row[key] = value
                    with self.assertRaises(ValueError):
                        tool.validate_record(row)
            for key, value in (("version", True), ("count", -1), ("format", "SECRET"),
                               ("size_bucket", "2048")):
                with self.subTest(operation=operation, attribute=key):
                    row = source_row(operation)
                    row["attributes"][key] = value
                    with self.assertRaises(ValueError):
                        tool.validate_record(row)
            row = source_row(operation)
            row["text"] = "SECRET"
            with self.assertRaisesRegex(ValueError, "unsupported trace schema"):
                tool.validate_record(row)


if __name__ == "__main__":
    unittest.main()
