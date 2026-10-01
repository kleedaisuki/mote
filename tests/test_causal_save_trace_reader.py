"""Independent structural and censorship fixtures; vocabulary is test-local."""

import json
import unittest

from causal_save_trace_reader import MOTE_SAVE_CONTRACT, SaveContract, TraceIntegrityError, classify_requests, read_prefix


CONTRACT = SaveContract(frozenset({"test.command"}), "test.received",
                        ("test.admitted", "test.phase.entered", "test.completed"),
                        frozenset({"test.admitted", "test.completed"}))


def record(span, operation, parent=None, status="success", **attributes):
    """Build independently specified schema-v1 records with deterministic IDs."""
    row = {"schema_version": 1, "utc_time": "2026-10-01T00:00:00Z", "session_id": "fixture",
           "trace_id": "1" * 32, "span_id": f"{span:016x}", "operation": operation,
           "duration_us": 0, "status": status, "attributes": attributes}
    if parent is not None:
        row["parent_span_id"] = f"{parent:016x}"
    return row


def encode(rows):
    """Serialize one newline per record, matching the transport framing contract."""
    return b"".join(json.dumps(row).encode() + b"\n" for row in rows)


class CausalReaderTests(unittest.TestCase):
    """Check positive evidence, compatibility, integrity, and independent requests."""

    def test_held_phase_after_terminated_process_is_censored(self):
        rows = [record(2, "test.received", 1), record(3, "test.admitted", 1),
                record(4, "test.phase.entered", 1)]
        report = classify_requests(read_prefix(encode(rows), terminated=True).records, CONTRACT, terminated=True)
        request = report["requests"][0]
        self.assertTrue(request["receipt_observed"])
        self.assertEqual("censored", request["classification"])
        self.assertEqual("test.phase.entered", request["last_positive_stage"])
        self.assertFalse(request["successful_chain_complete"])
        self.assertFalse(report["absence_certified"])

    def test_live_partial_is_retained_and_suffix_can_complete(self):
        data = encode([record(2, "test.received", 1)])
        cut = len(data) // 2
        first = read_prefix(data[:cut], terminated=False)
        self.assertFalse(first.records)
        self.assertEqual(data[:cut], first.partial)
        self.assertEqual(1, len(read_prefix(first.partial + data[cut:], terminated=False).records))

    def test_only_final_unterminated_record_is_discarded_after_kill(self):
        data = encode([record(2, "test.received", 1)]) + b'{"partial":\xff'
        prefix = read_prefix(data, terminated=True)
        self.assertTrue(prefix.discarded_partial)
        self.assertEqual(1, len(prefix.records))
        with self.assertRaises(TraceIntegrityError):
            read_prefix(data + b"\n", terminated=True)

    def test_malformed_complete_line_before_valid_tail_is_integrity_failure(self):
        with self.assertRaises(TraceIntegrityError):
            read_prefix(b"not-json\n" + encode([record(2, "test.received", 1)]), terminated=True)

    def test_complete_json_without_newline_is_still_partial(self):
        data = encode([record(2, "test.received", 1)])[:-1]
        self.assertFalse(read_prefix(data, terminated=False).records)
        self.assertTrue(read_prefix(data, terminated=True).discarded_partial)

    def test_children_before_terminal_and_nested_parent_are_linked(self):
        rows = [record(2, "test.received", 1), record(3, "test.admitted", 1),
                record(4, "test.completed", 5), record(5, "unknown.coarse", 1),
                record(1, "test.command", reason="completed")]
        request = classify_requests(rows, CONTRACT, terminated=True)["requests"][0]
        self.assertTrue(request["successful_chain_complete"])
        self.assertTrue(request["receipt_observed"])

    def test_two_requests_do_not_borrow_each_others_stages(self):
        rows = [record(2, "test.received", 1), record(3, "test.admitted", 1),
                record(4, "test.completed", 1), record(1, "test.command", reason="completed"),
                record(7, "test.received", 6), record(6, "test.command", status="skipped", reason="already_saving")]
        first, second = classify_requests(rows, CONTRACT, terminated=True)["requests"]
        self.assertTrue(first["successful_chain_complete"])
        self.assertEqual("skipped", second["classification"])
        self.assertFalse(second["observed_stages"])

    def test_duplicate_terminal_is_invalid(self):
        rows = [record(1, "test.command"), record(1, "test.command", status="failure")]
        with self.assertRaises(TraceIntegrityError):
            classify_requests(rows, CONTRACT, terminated=True)

    def test_unknown_operation_does_not_certify_stage(self):
        rows = [record(2, "test.received", 1), record(3, "future.completed", 1), record(1, "test.command")]
        request = classify_requests(rows, CONTRACT, terminated=True)["requests"][0]
        self.assertEqual("success", request["classification"])
        self.assertFalse(request["successful_chain_complete"])

    def test_dangling_phase_entry_remains_positive_but_unlinked(self):
        rows = [record(2, "test.received", 1), record(4, "test.phase.entered", 3)]
        report = classify_requests(rows, CONTRACT, terminated=True)
        self.assertEqual("test.received", report["requests"][0]["last_positive_stage"])
        self.assertEqual("test.phase.entered", report["unlinked_positive_stages"][0]["operation"])

    def test_native_held_anchor_graph_is_recoverable_without_duration_terminals(self):
        rows = [record(1, "command.save.received", 9), record(2, "document.save.entered", 1),
                record(3, "save.temp_flush.entered", 2)]
        report = classify_requests(rows, MOTE_SAVE_CONTRACT, terminated=True)
        self.assertEqual("censored", report["requests"][0]["classification"])
        self.assertEqual("save.temp_flush.entered", report["requests"][0]["last_positive_stage"])
        self.assertFalse(report["unlinked_positive_stages"])

    def test_native_distinct_terminal_identity_resolves_receipt(self):
        rows = [record(1, "command.save.received", 9), record(2, "command.save", 1, status="skipped")]
        request = classify_requests(rows, MOTE_SAVE_CONTRACT, terminated=True)["requests"][0]
        self.assertEqual(f"{1:016x}", request["request_span_id"])
        self.assertEqual("skipped", request["classification"])
        self.assertTrue(request["receipt_observed"])

    def test_native_two_terminal_ids_for_one_request_are_invalid(self):
        rows = [record(1, "command.save.received", 9), record(2, "command.save", 1), record(3, "command.save", 1)]
        with self.assertRaises(TraceIntegrityError):
            classify_requests(rows, MOTE_SAVE_CONTRACT, terminated=True)

    def test_both_typed_receipt_only_kills_keep_known_command_kind(self):
        for command in ("command.save", "command.save_as"):
            rows = [record(1, command + ".received", 9)]
            request = classify_requests(rows, MOTE_SAVE_CONTRACT, terminated=True)["requests"][0]
            self.assertEqual(command, request["command_operation"])
            self.assertEqual("censored", request["classification"])
            self.assertEqual(command + ".received", request["last_positive_stage"])
            self.assertFalse(request["observed_stages"])

    def test_terminal_kind_must_agree_with_typed_receipt(self):
        for receipt, terminal in (("command.save.received", "command.save_as"),
                                  ("command.save_as.received", "command.save")):
            with self.assertRaises(TraceIntegrityError):
                classify_requests([record(1, receipt, 9), record(2, terminal, 1)], MOTE_SAVE_CONTRACT, terminated=True)

    def test_legacy_generic_receipt_does_not_certify_native_save(self):
        report = classify_requests([record(1, "command.received", 9)], MOTE_SAVE_CONTRACT, terminated=True)
        self.assertFalse(report["requests"])
        self.assertEqual("legacy_health_unknown", report["transport_health"])

    def test_failed_required_stage_cannot_certify_successful_chain(self):
        rows = [record(2, "test.received", 1), record(3, "test.admitted", 1),
                record(4, "test.completed", 1, status="failure"), record(1, "test.command")]
        request = classify_requests(rows, CONTRACT, terminated=True)["requests"][0]
        self.assertFalse(request["successful_chain_complete"])
        self.assertEqual("failure", request["unsuccessful_stages"][0]["status"])

    def test_old_schema_is_readable_without_health_certificate(self):
        rows = [record(1, "document.save"), record(2, "mote.session")]
        rows[1]["parent_span_id"] = None
        report = classify_requests(read_prefix(encode(rows), terminated=True).records, CONTRACT, terminated=True)
        self.assertEqual("legacy_health_unknown", report["transport_health"])
        self.assertFalse(report["requests"])
        self.assertTrue(report["normal_session_terminal_observed"])
        self.assertFalse(report["absence_certified"])

    def test_late_commit_after_cancelled_request_does_not_change_terminal(self):
        rows = [record(2, "test.received", 1), record(1, "test.command", status="cancelled"),
                record(3, "test.phase.entered", 1)]
        request = classify_requests(rows, CONTRACT, terminated=True)["requests"][0]
        self.assertEqual("cancelled", request["classification"])
        self.assertFalse(request["successful_chain_complete"])

    def test_boolean_integer_and_unknown_schema_are_rejected(self):
        for field, value in (("duration_us", True), ("schema_version", 2)):
            row = record(1, "legacy")
            row[field] = value
            with self.assertRaises(TraceIntegrityError):
                read_prefix(encode([row]), terminated=True)

    def test_invalid_watermark_order_is_integrity_failure(self):
        cases = [
            [record(2, "health", record_sequence=1, flushed_sequence=1)],
            [record(2, "health", record_sequence=3, flushed_sequence=2),
             record(3, "health", record_sequence=4, flushed_sequence=1)],
            [record(2, "health", record_sequence=2), record(3, "health", record_sequence=1)],
        ]
        for rows in cases:
            with self.assertRaises(TraceIntegrityError):
                classify_requests(rows, CONTRACT, terminated=True)

    def test_continuous_sequences_do_not_certify_zero_loss(self):
        rows = [record(2, "health", record_sequence=1), record(3, "health", record_sequence=2)]
        report = classify_requests(rows, CONTRACT, terminated=True)
        self.assertEqual("health_not_certified", report["transport_health"])
        self.assertFalse(report["absence_certified"])


if __name__ == "__main__":
    unittest.main()
