"""Read schema-v1 causal evidence without converting missing records into absence.

The operation vocabulary is supplied by the integration owner. This module does
not infer target receipt from external action reports, UTC ordering, or focus.
"""

from __future__ import annotations

from dataclasses import dataclass
import json
import re
from typing import Iterable


class TraceIntegrityError(ValueError):
    """The retained complete records violate the trace's structural contract."""


@dataclass(frozen=True)
class Prefix:
    """Complete retained records plus the unfinished suffix for a live reader."""

    records: tuple[dict, ...]
    partial: bytes = b""
    discarded_partial: bool = False


@dataclass(frozen=True)
class SaveContract:
    """Closed operation vocabulary; phases are checkpoints, not latency spans."""

    request_operations: frozenset[str]
    receipt_operation: str | tuple[str, ...]
    stage_operations: tuple[str, ...]
    successful_required: frozenset[str] = frozenset()
    successful_one_of: frozenset[str] = frozenset()
    receipt_is_anchor: bool = False


SAVE_PHASES = (
    "document.save", "save.gate_wait", "save.snapshot_capture", "save.target_check",
    "save.temp_encode_write", "save.temp_flush", "save.temp_hash", "save.final_target_check",
    "save.commit_move", "save.commit_replace", "save.saved_stamp", "save.bookkeeping",
    "save.failure_cleanup", "save.failure_inspection",
)
SAVE_EVENTS = (
    "save.composition_settled", "save.composition_blocked", "save.controller_entered",
    "save.admitted", "save.worker_started", "save.overwrite_requested",
    "save.overwrite_approved", "save.overwrite_declined", "save.snapshot_captured",
    "save.ui_local_queued", "save.ui_wake_requested", "save.ui_post_returned",
    "save.ui_started", "save.ui_deferred", "save.completed",
)
MOTE_SAVE_CONTRACT = SaveContract(
    frozenset({"command.save", "command.save_as"}), ("command.save.received", "command.save_as.received"),
    SAVE_EVENTS + SAVE_PHASES + tuple(phase + ".entered" for phase in SAVE_PHASES),
    frozenset({"save.admitted", "save.worker_started", "save.snapshot_captured",
               "document.save", "save.ui_started"}),
    frozenset({"save.completed", "save.ui_deferred"}),
    True,
)


def _integer(value: object, minimum: int = 0) -> bool:
    """Reject Boolean values, which Python otherwise treats as integers."""
    return type(value) is int and value >= minimum


def _validate(record: object, line: int) -> dict:
    """Validate required v1 types without rejecting compatible future operations."""
    if not isinstance(record, dict):
        raise TraceIntegrityError(f"line {line}: record is not an object")
    if type(record.get("schema_version")) is not int or record["schema_version"] != 1:
        raise TraceIntegrityError(f"line {line}: unsupported schema version")
    for field in ("utc_time", "session_id", "operation"):
        if not isinstance(record.get(field), str) or not record[field]:
            raise TraceIntegrityError(f"line {line}: invalid {field}")
    for field, length in (("trace_id", 32), ("span_id", 16), ("parent_span_id", 16)):
        if field == "parent_span_id" and record.get(field) is None:
            continue
        value = record.get(field)
        if not isinstance(value, str) or not re.fullmatch(rf"[0-9a-f]{{{length}}}", value) or not int(value, 16):
            raise TraceIntegrityError(f"line {line}: invalid {field}")
    if not _integer(record.get("duration_us")):
        raise TraceIntegrityError(f"line {line}: invalid duration_us")
    if record.get("status") not in ("success", "failure", "cancelled", "skipped"):
        raise TraceIntegrityError(f"line {line}: invalid status")
    attributes = record.get("attributes")
    if not isinstance(attributes, dict):
        raise TraceIntegrityError(f"line {line}: invalid attributes")
    for field in ("version", "count", "session_elapsed_us", "flushed_sequence", "dropped_total"):
        if field in attributes and not _integer(attributes[field]):
            raise TraceIntegrityError(f"line {line}: invalid {field}")
    if "record_sequence" in attributes and not _integer(attributes["record_sequence"], 1):
        raise TraceIntegrityError(f"line {line}: invalid record_sequence")
    return record


def read_prefix(data: bytes, *, terminated: bool) -> Prefix:
    """Parse newline-terminated records; preserve live suffix, discard killed suffix.

    A valid JSON object without its newline remains a partial transport record.
    A malformed newline-terminated record is never silently skipped.
    """
    parts = data.split(b"\n")
    suffix = parts.pop()
    records = []
    for line, raw in enumerate(parts, 1):
        try:
            parsed = json.loads(raw.decode("utf-8"))
        except (UnicodeDecodeError, ValueError) as error:
            raise TraceIntegrityError(f"line {line}: malformed complete record") from error
        records.append(_validate(parsed, line))
    return Prefix(tuple(records), b"" if terminated else suffix, bool(suffix) and terminated)


def classify_requests(records: Iterable[dict], contract: SaveContract, *, terminated: bool) -> dict:
    """Reconstruct request parentage and report local evidence, never absence proof.

    Children may precede their parent's terminal or outlive a cancelled request.
    Unknown operations are retained but cannot certify a stage. Terminal status
    and successful-chain coverage are deliberately separate result fields.
    """
    rows = list(records)
    receipts = (contract.receipt_operation,) if isinstance(contract.receipt_operation, str) else contract.receipt_operation
    sequences = {}
    watermarks = {}
    for row in rows:
        session = (row["session_id"], row["trace_id"])
        attributes = row["attributes"]
        sequence = attributes.get("record_sequence")
        watermark = attributes.get("flushed_sequence")
        previous = sequences.get(session)
        if sequence is not None:
            if previous is not None and sequence <= previous:
                raise TraceIntegrityError("nonincreasing record sequence")
            sequences[session] = sequence
        if watermark is not None:
            if watermark < watermarks.get(session, 0) or (sequence is not None and watermark >= sequence):
                raise TraceIntegrityError("invalid flush watermark order")
            watermarks[session] = watermark
    index = {}
    for row in rows:
        key = (row["session_id"], row["trace_id"], row["span_id"])
        if key in index:
            raise TraceIntegrityError("duplicate span/terminal identity")
        index[key] = row
    requests = {}
    for row in rows:
        if row["operation"] in contract.request_operations:
            identity = row.get("parent_span_id") if contract.receipt_is_anchor else row["span_id"]
            if not identity:
                raise TraceIntegrityError("request terminal has no receipt parent")
            key = (row["session_id"], row["trace_id"], identity)
            request = requests.setdefault(key, {"terminal": None, "receipt": None, "stages": [], "stage_records": []})
            if request["terminal"] is not None:
                raise TraceIntegrityError("duplicate request terminal")
            request["terminal"] = row
        if row["operation"] in receipts:
            parent = row.get("parent_span_id")
            if not parent:
                raise TraceIntegrityError("request receipt has no parent")
            key = (row["session_id"], row["trace_id"], row["span_id"] if contract.receipt_is_anchor else parent)
            request = requests.setdefault(key, {"terminal": None, "receipt": None, "stages": [], "stage_records": []})
            if request["receipt"] is not None:
                raise TraceIntegrityError("duplicate request receipt")
            request["receipt"] = row
    orphan_stages = []
    for row in rows:
        if row["operation"] not in contract.stage_operations:
            continue
        parent = row.get("parent_span_id")
        visited = set()
        attached = False
        while parent:
            key = (row["session_id"], row["trace_id"], parent)
            if key in visited:
                raise TraceIntegrityError("causal parent cycle")
            visited.add(key)
            if key in requests:
                requests[key]["stages"].append(row["operation"])
                requests[key]["stage_records"].append(row)
                attached = True
                break
            parent_record = index.get(key)
            parent = parent_record.get("parent_span_id") if parent_record else None
        if not attached:
            orphan_stages.append({"span_id": row["span_id"], "operation": row["operation"]})
    result = []
    for key, request in requests.items():
        terminal = request["terminal"]
        receipt = request["receipt"]
        if contract.receipt_is_anchor and receipt and terminal and receipt["operation"] != terminal["operation"] + ".received":
            raise TraceIntegrityError("request terminal kind differs from received command")
        observed = {row["operation"] for row in request["stage_records"] if row["status"] == "success"}
        missing = sorted(contract.successful_required - observed)
        if contract.successful_one_of and not observed.intersection(contract.successful_one_of):
            missing.append("one_of:" + "|".join(sorted(contract.successful_one_of)))
        result.append({
            "request_span_id": key[2], "session_id": key[0], "trace_id": key[1],
            "receipt_observed": request["receipt"] is not None,
            "command_operation": receipt["operation"].removesuffix(".received") if receipt and contract.receipt_is_anchor else (terminal["operation"] if terminal else None),
            "classification": terminal["status"] if terminal else ("censored" if terminated else "pending"),
            "reason": terminal["attributes"].get("reason") if terminal else None,
            "observed_stages": request["stages"],
            "unsuccessful_stages": [{"operation": row["operation"], "status": row["status"]}
                                    for row in request["stage_records"] if row["status"] != "success"],
            "last_positive_stage": request["stages"][-1] if request["stages"] else (receipt["operation"] if receipt else None),
            "successful_chain_complete": bool(terminal and terminal["status"] == "success" and request["receipt"] and not missing),
            "missing_required_stages": missing,
        })
    return {"requests": result, "transport_health": "legacy_health_unknown" if not sequences else "health_not_certified",
            "unlinked_positive_stages": orphan_stages,
            "dropped_records_observed": any(row["operation"] == "telemetry.dropped" for row in rows),
            "normal_session_terminal_observed": any(row["operation"] == "mote.session" and row["status"] == "success" for row in rows),
            "absence_certified": False}
