"""Prepare synthetic native workloads and audit causal traces without driving a GUI.

All writes remain inside repository .temp/.cache. This tool never opens a user
document, sends input, touches the clipboard, or treats a draw as physical paint.
"""

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import stat
import statistics
import uuid

ROOT = Path(__file__).resolve().parents[2]
FORMATS = {"json": ".json", "csv": ".csv", "markdown": ".md"}
OPERATIONS = frozenset("""mote.session mote.startup mote.startup_to_editable
document.open_to_editable document.open_to_draw_submission document.open
document.decode document.edit document.edit_to_analysis document.edit_to_presentation
document.edit_to_draw_submission document.edit_to_paint document.save
analysis.parse analysis.semantic analysis.publish analysis.to_presentation
view.layout view.paint edit.committed analysis.published analysis.discarded
save.completed telemetry.dropped save.failure.target_check
save.failure.temp_write_and_hash save.failure.final_target_check save.failure.move
save.failure.replace save.failure.cleanup save.failure.saved_stamp save.failure.unknown""".split())
# Fixed native Save provenance vocabulary; no user-derived operation strings.
SAVE_PHASES = frozenset("""document.save save.gate_wait save.snapshot_capture save.target_check
save.temp_encode_write save.temp_flush save.temp_hash save.final_target_check
save.commit_move save.commit_replace save.saved_stamp save.bookkeeping
save.failure_cleanup save.failure_inspection""".split())
OPERATIONS |= SAVE_PHASES | {phase + ".entered" for phase in SAVE_PHASES} | frozenset("""
command.save command.save_as command.save.received command.save_as.received command.received
save.composition_settled save.composition_blocked save.controller_entered save.admitted
save.worker_started save.overwrite_requested save.overwrite_approved save.overwrite_declined
save.snapshot_captured save.ui_local_queued save.ui_wake_requested save.ui_post_returned
save.ui_started save.ui_deferred""".split())
REASONS = frozenset("""completed view_deferred composition_blocked missing_handler callback_failed
already_saving picker_cancelled recovery_redirected overwrite_declined operation_cancelled
stale_document lifetime_ended save_failed ui_post_failed""".split())
MENU_OPERATIONS = frozenset("""native.menu.observation.ready native.menu.observation.unavailable
native.menu.save_family.entered native.menu.save_family.returned_true
native.menu.save_family.returned_false""".split())
OPERATIONS |= MENU_OPERATIONS
OPERATIONS |= {"native.posted.callback.failed", "native.posted.callback.report_failed"}
# Session-only monitor observations never identify an event or Save request.
INPUT_SUCCESS_OPERATIONS = frozenset("""native.input.monitor.ready
native.input.save_family_candidate native.input.monitor.removed""".split())
INPUT_FAILURE_OPERATIONS = frozenset("""native.input.monitor.unavailable
native.input.monitor.callback_failed native.input.monitor.removal_failed""".split())
INPUT_OPERATIONS = INPUT_SUCCESS_OPERATIONS | INPUT_FAILURE_OPERATIONS
OPERATIONS |= INPUT_OPERATIONS
STATUSES = ("success", "cancelled", "failure", "skipped")
ATTRIBUTES = {"format", "size_bucket", "version", "count", "hresult", "reason"}


def artifact_path(value, area=None):
    """Reject traversal and link/junction ancestors, including nonexistent outputs."""
    path = Path(os.path.abspath(ROOT / value))
    roots = [ROOT / area] if area else [ROOT / ".temp", ROOT / ".cache"]
    if not any(path != base and path.is_relative_to(base) for base in roots):
        raise ValueError("artifact must be below repository .temp or .cache")
    for ancestor in (path, *path.parents):
        if ancestor.is_symlink() or getattr(os.path, "isjunction", lambda _: False)(ancestor):
            raise ValueError("artifact ancestry must not contain links or junctions")
        if ancestor.exists() and getattr(ancestor.lstat(), "st_file_attributes", 0) & getattr(
                stat, "FILE_ATTRIBUTE_REPARSE_POINT", 1024):
            raise ValueError("artifact ancestry must not contain reparse points")
    return path


def write_json(path, value):
    """Write a new report, refusing to silently replace earlier evidence."""
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, indent=2, allow_nan=False)
        stream.write("\n")


def fill(stream, block, length):
    """Emit exact ASCII byte counts with bounded temporary memory."""
    batch = block * max(1, (65536 // len(block)))
    while length:
        piece = batch[:min(len(batch), length)]
        stream.write(piece)
        length -= len(piece)


def generate(path, format_name, shape, size):
    """Generate valid structured text, not arbitrarily truncated parse fixtures.

    JSON many-line is an array of fixed records plus legal trailing whitespace;
    long JSON/CSV contain one huge string/quoted field. Markdown many-line has
    bounded headings, emphasis, and local links; its long shape is one paragraph.
    """
    with path.open("xb") as stream:
        if shape == "long":
            prefix, suffix = {"json": (b'{"value":"', b'"}'),
                              "csv": (b'"', b'"'),
                              "markdown": (b"", b"")}[format_name]
            stream.write(prefix)
            fill(stream, b"a", size - len(prefix) - len(suffix))
            stream.write(suffix)
        elif format_name == "json":
            record = b'{"id":"row","value":"structured text","ok":true}'
            count = (size - 3) // (len(record) + 2)
            stream.write(b"[\n")
            for index in range(count):
                stream.write(record + (b",\n" if index + 1 < count else b"\n"))
            stream.write(b"]")
            fill(stream, b" ", size - stream.tell())
        elif format_name == "csv":
            row = b'row,"quoted, value","escaped ""quote""",42\r\n'
            count = (size - 9) // len(row)
            fill(stream, row, count * len(row))
            remainder = size - stream.tell()
            stream.write(b'tail,"' + b"a" * (remainder - 9) + b'",,')
        else:
            row = b"# Heading\n\nParagraph with **strong** and [link](#heading).\n\n"
            count = size // len(row)
            fill(stream, row, count * len(row))
            fill(stream, b"a", size - stream.tell())
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    if path.stat().st_size != size:
        raise ValueError("fixture size mismatch")
    return digest


def prepare(args):
    """Prepare only selected corpus sizes; no editor process is launched."""
    run_id = uuid.uuid4().hex
    directory = artifact_path(f".temp/native-acceptance/{run_id}", ".temp")
    directory.mkdir(parents=True)
    cases = []
    for format_name in args.formats:
        for size in args.sizes:
            for shape in args.shapes:
                case = f"{format_name}-{shape}-{size}"
                path = directory / (case + FORMATS[format_name])
                digest = generate(path, format_name, shape, size * 1048576)
                cases.append({"case": case, "format": format_name, "shape": shape,
                              "size_bytes": path.stat().st_size, "sha256": digest,
                              "fixture": path.relative_to(ROOT).as_posix()})
    manifest = {"schema_version": 1, "corpus_version": 1, "run_id": run_id,
                "cache_state": "just-written-not-cache-evicted", "cases": cases}
    output = artifact_path(f".cache/native-acceptance/corpus-{run_id}.json", ".cache")
    write_json(output, manifest)
    print(output.relative_to(ROOT).as_posix())


def valid_hex(value, length):
    """Validate opaque identifiers, never accept content-bearing labels as IDs."""
    return isinstance(value, str) and re.fullmatch(f"[0-9a-f]{{{length}}}", value) is not None


def load_records(paths, *, discard_partial=False):
    """Bound and validate records; optionally discard only an unterminated final row."""
    records = []
    hashes = []
    for value in paths:
        path = artifact_path(value)
        if path.stat().st_size > 32 * 1048576:
            raise ValueError("trace file exceeds bounded reader size")
        with path.open("rb") as stream:
            hashes.append(hashlib.file_digest(stream, "sha256").hexdigest())
        with path.open("rb") as stream:
            for raw in stream:
                if discard_partial and not raw.endswith(b"\n"):
                    continue
                line = raw.decode("utf-8-sig")
                if len(line) > 16384 or len(records) >= 100000:
                    raise ValueError("trace exceeds bounded record size/count")
                row = json.loads(line)
                allowed = {"schema_version", "utc_time", "session_id", "run_id", "trace_id",
                           "span_id", "parent_span_id", "operation", "duration_us", "status", "attributes"}
                required = allowed - {"run_id"}
                if (not isinstance(row, dict) or set(row) - allowed or required - set(row)
                        or row.get("schema_version") != 1):
                    raise ValueError("unsupported trace schema")
                if row.get("operation") not in OPERATIONS or row.get("status") not in STATUSES:
                    raise ValueError("unknown operation/status")
                if type(row.get("duration_us")) is not int or row["duration_us"] < 0:
                    raise ValueError("invalid monotonic duration")
                for key, length in (("session_id", 32), ("trace_id", 32), ("span_id", 16)):
                    if not valid_hex(row.get(key), length):
                        raise ValueError("invalid causal identifier")
                parent = row.get("parent_span_id")
                if parent is not None and not valid_hex(parent, 16):
                    raise ValueError("invalid parent identifier")
                attrs = row.get("attributes")
                if not isinstance(attrs, dict) or set(attrs) - ATTRIBUTES:
                    raise ValueError("unsupported trace attributes")
                if row["operation"] in MENU_OPERATIONS and (
                        row["duration_us"] != 0 or row["status"] != "success"
                        or attrs or parent is None):
                    raise ValueError("invalid independent menu checkpoint")
                if row["operation"] in INPUT_OPERATIONS and (
                        row["duration_us"] != 0 or attrs or parent is None
                        or row["status"] != ("success" if row["operation"] in
                                              INPUT_SUCCESS_OPERATIONS else "failure")):
                    raise ValueError("invalid independent input checkpoint")
                for key in ("version", "count"):
                    if key in attrs and (type(attrs[key]) is not int or attrs[key] < 0):
                        raise ValueError("invalid numeric attribute")
                if "reason" in attrs and (not isinstance(attrs["reason"], str) or attrs["reason"] not in REASONS):
                    raise ValueError("unsupported terminal reason")
                records.append(row)
    return records, hashes


def distribution(values):
    """Return median and nearest-rank p95; no inferred population/SLA tail."""
    if not values:
        return {"n": 0, "p50_us": None, "p95_us": None, "max_us": None}
    ordered = sorted(values)
    return {"n": len(values), "p50_us": statistics.median(values),
            "p95_us": ordered[math.ceil(.95 * len(values)) - 1], "max_us": ordered[-1],
            "tail_claim": "observed-nearest-rank-only", "small_sample": len(values) < 100}


def audit(records, expected):
    """Join serialized-out-of-order spans by identity and keep censoring explicit."""
    issues = []
    sessions = {row["session_id"] for row in records}
    if len(sessions) != 1:
        issues.append("expected-one-process-session")
    by_id = {}
    for row in records:
        key = row["session_id"], row["trace_id"], row["span_id"]
        if key in by_id:
            issues.append("duplicate-span")
        by_id[key] = row
    terminal = [row for row in records if row["operation"] == "mote.session"]
    if len(terminal) != 1 or terminal[0]["status"] != "success" or terminal[0]["parent_span_id"] is not None:
        issues.append("normal-session-shutdown-not-certified")
    summaries = {}
    for operation in sorted({row["operation"] for row in records} | set(expected)):
        rows = [row for row in records if row["operation"] == operation]
        counts = {status: sum(row["status"] == status for row in rows) for status in STATUSES}
        summaries[operation] = {"outcomes": counts, "success_duration": distribution(
            [row["duration_us"] for row in rows if row["status"] == "success"])}
        if operation in expected and len(rows) != expected[operation]:
            issues.append("unexpected-record-count:" + operation)
    for row in records:
        parent_id = row["parent_span_id"]
        if parent_id is None:
            if row["operation"] != "mote.session":
                issues.append("missing-causal-parent")
            continue
        parent = by_id.get((row["session_id"], row["trace_id"], parent_id))
        if parent is None:
            issues.append("missing-causal-parent")
            continue
        if row["operation"] in ("document.edit_to_draw_submission", "document.open_to_draw_submission"):
            wanted = ("document.edit_to_presentation" if row["operation"].startswith("document.edit")
                      else "document.open_to_editable")
            if parent["operation"] != wanted:
                issues.append("wrong-draw-parent")
            if "version" not in row["attributes"] or "version" not in parent["attributes"]:
                issues.append("draw-parent-version-unavailable")
            elif row["attributes"]["version"] != parent["attributes"]["version"]:
                issues.append("draw-parent-version-mismatch")
    # Each span has at most one parent. Walk each unseen chain once, never recurse
    # or compare UTC timestamps; serialization order need not be topological.
    visited = set()
    for start in by_id:
        chain = set()
        current = start
        while current in by_id and current not in visited:
            if current in chain:
                issues.append("causal-cycle")
                break
            chain.add(current)
            node = by_id[current]
            parent_id = node["parent_span_id"]
            if parent_id is None:
                break
            current = (node["session_id"], node["trace_id"], parent_id)
        visited.update(chain)
    if any(row["operation"] == "telemetry.dropped" and row["attributes"].get("count", 0) <= 0
           for row in records):
        issues.append("invalid-dropped-record-counter")
    dropped = sum(row["attributes"].get("count", 0) for row in records if row["operation"] == "telemetry.dropped")
    if dropped:
        issues.append("trace-dropped-records")
    return {"causal_integrity": "pass" if not issues else "incomplete-or-invalid",
            "coverage_contract": "caller-supplied-counts" if expected else "not-specified",
            "issues": sorted(set(issues)), "dropped_records": dropped,
            "operations": summaries, "endpoint": "native-callback-return-not-physical-presentation"}


def summarize(args):
    """Audit one known process per manifest sample; never pool different hosts/modes."""
    source = artifact_path(args.manifest)
    manifest = json.loads(source.read_text(encoding="utf-8-sig"))
    if manifest.get("schema_version") != 1 or not 1 <= len(manifest.get("samples", [])) <= 1000:
        raise ValueError("invalid sample manifest")
    outputs = []
    seen = set()
    for sample in manifest["samples"]:
        sample_id = sample["sample_id"]
        if not re.fullmatch("[a-zA-Z0-9_-]{1,80}", sample_id) or sample_id in seen:
            raise ValueError("invalid or duplicate sample ID")
        seen.add(sample_id)
        binary_sha = sample.get("binary_sha256")
        if binary_sha is not None and not valid_hex(binary_sha, 64):
            raise ValueError("invalid binary SHA256")
        expected = sample.get("expected_records", {})
        if any(key not in OPERATIONS or type(count) is not int or count < 0 for key, count in expected.items()):
            raise ValueError("invalid expected counts")
        traces = sample["traces"]
        if not isinstance(traces, list) or not 1 <= len(traces) <= 8:
            raise ValueError("expected 1-8 rotation files per process")
        records, hashes = load_records(traces)
        result = audit(records, expected)
        # Labels identify the caller's stratum, never certify OS/AOT or file-open truth.
        outputs.append({"sample_id": sample_id, "binary_sha256": binary_sha,
                        "binary_identity": "caller-supplied-not-verified" if binary_sha else "unavailable",
                        "trace_sha256": hashes, **result})
    output = artifact_path(args.output, ".cache")
    write_json(output, {"schema_version": 1, "input_manifest_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
                        "measurement_kind": "trace-integrity-and-descriptive-durations", "samples": outputs})
    print(output.relative_to(ROOT).as_posix())
    return int(any(row["causal_integrity"] != "pass" for row in outputs))


def main():
    """Expose artifact-only operations; GUI launch belongs to reviewed OS drivers."""
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    corpus = commands.add_parser("prepare")
    corpus.add_argument("--sizes", type=int, choices=(1, 10, 100), nargs="+", default=[1])
    corpus.add_argument("--formats", choices=tuple(FORMATS), nargs="+", default=list(FORMATS))
    corpus.add_argument("--shapes", choices=("many", "long"), nargs="+", default=["many", "long"])
    summary = commands.add_parser("summarize")
    summary.add_argument("--manifest", required=True)
    summary.add_argument("--output", required=True)
    args = parser.parse_args()
    return summarize(args) if args.command == "summarize" else prepare(args)


if __name__ == "__main__":
    raise SystemExit(main())
