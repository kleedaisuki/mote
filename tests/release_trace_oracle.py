"""Check retained release traces with the existing closed privacy/causal reader.

This is an artifact oracle, not a GUI driver or proof of physical presentation.
Only repository-local evidence files are accepted by the reused reader.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

from causal_save_trace_reader import MOTE_SAVE_CONTRACT, classify_requests

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "native_acceptance", ROOT / "benchmarks/NativeAcceptance/acceptance.py"
)
ACCEPTANCE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ACCEPTANCE)

SEMANTIC_FIELDS = frozenset("""schema_version generation version installation_nonce
presentation_sequence document_kind completeness coverage_start coverage_length
source_units token_count diagnostic_count style_ready geometry_known grid_required
grid_ready grid_rows grid_columns grid_cells grid_pending_cells""".split())
SEMANTIC_KINDS = frozenset({"PlainText", "Markdown", "Toml", "Json", "Yaml", "Csv"})
WINDOWS_SEMANTIC_FIELDS = frozenset("""schema_version document_kind version trace_session_id
source_units analysis_status_current parse_publish_style_witness grid_cells grid_labels_exact endpoint""".split())
WINDOWS_SEMANTIC_ENDPOINT = "native status/preview or actual owner-data callbacks plus version-linked trace; not physical pixels"


def verify_windows_semantics(witness: dict, expected_kind: str, expected_units: int,
                             saved_processes: list[tuple[list[dict], list[dict]]]) -> None:
    """Check the distinct Windows observable contract without inventing Mac identities."""
    for key in ("schema_version", "version", "source_units", "grid_cells"):
        if type(witness[key]) is not int or not 0 <= witness[key] <= 2**63 - 1:
            raise ValueError("invalid Windows semantic integer: " + key)
    for key in ("analysis_status_current", "parse_publish_style_witness", "grid_labels_exact"):
        if type(witness[key]) is not bool:
            raise ValueError("invalid Windows semantic boolean: " + key)
    if (witness["schema_version"] != 1 or witness["document_kind"] != expected_kind or
            witness["source_units"] != expected_units or witness["endpoint"] != WINDOWS_SEMANTIC_ENDPOINT or
            not witness["analysis_status_current"] or not witness["parse_publish_style_witness"] or
            not ACCEPTANCE.valid_hex(witness["trace_session_id"], 32)):
        raise ValueError("Windows witness does not certify the expected current source")
    csv = expected_kind == "Csv"
    if witness["grid_cells"] != (9 if csv else 0) or witness["grid_labels_exact"] != csv:
        raise ValueError("Windows Grid witness differs from the complete small CSV task")
    required = {"analysis.parse", "analysis.published", "native.source.style_publish"}
    for records, requests in saved_processes:
        matching = [request for request in requests if request["saved_version"] == witness["version"] and
                    request["session_id"] == witness["trace_session_id"]]
        current = {row["operation"] for row in records if row["status"] == "success" and
                   row["attributes"].get("version") == witness["version"] and
                   row["session_id"] == witness["trace_session_id"]}
        if matching and required <= current:
            return
    raise ValueError("Windows semantic session/version lacks complete Save and current analysis/style evidence")


def verify_semantics(path: str, expected_kind: str, expected_units: int,
                     saved_processes: list[tuple[list[dict], list[dict]]], platform: str = "macos") -> dict:
    """Bind closed final-ready evidence to independent text length and traced Save.

    Nonce/sequence readiness is admitted by the production identity guard, not
    independently attested by trace fields. Pixel quality is not inferred here.
    """
    artifact = ACCEPTANCE.artifact_path(path)
    if artifact.stat().st_size > 16384:
        raise ValueError("semantic witness exceeds bounded size")
    raw = artifact.read_bytes()
    witness = json.loads(raw.decode("utf-8-sig"))
    if platform not in {"macos", "windows"}:
        raise ValueError("unknown semantic platform")
    fields = SEMANTIC_FIELDS if platform == "macos" else WINDOWS_SEMANTIC_FIELDS
    if not isinstance(witness, dict) or set(witness) != fields:
        raise ValueError("semantic witness has unsupported fields")
    if expected_kind not in SEMANTIC_KINDS or type(expected_units) is not int or not 0 < expected_units <= 1048576:
        raise ValueError("invalid independent semantic expectation")
    if platform == "windows":
        verify_windows_semantics(witness, expected_kind, expected_units, saved_processes)
        return {"status": "passed", "platform": platform, "sha256": hashlib.sha256(raw).hexdigest(),
                "witness": witness, "saved_version_linked": True,
                "current_parse_publish_style_observed": True,
                "endpoint": "native status/preview/owner-data observations, not physical pixels"}
    booleans = {"style_ready", "geometry_known", "grid_required", "grid_ready"}
    strings = {"document_kind", "completeness"}
    for key in SEMANTIC_FIELDS - booleans - strings:
        if type(witness[key]) is not int or not 0 <= witness[key] <= 2**63 - 1:
            raise ValueError("invalid semantic integer: " + key)
    for key in booleans:
        if type(witness[key]) is not bool:
            raise ValueError("invalid semantic boolean: " + key)
    if (witness["schema_version"] != 1 or witness["generation"] < 1 or
            witness["installation_nonce"] < 1 or witness["document_kind"] != expected_kind or
            witness["completeness"] != "Complete" or witness["coverage_start"] != 0 or
            witness["coverage_length"] != expected_units or witness["source_units"] != expected_units or
            not witness["style_ready"] or not witness["geometry_known"]):
        raise ValueError("semantic witness does not certify current complete expected source")
    if witness["diagnostic_count"] != 0 or witness["token_count"] > expected_units * 8:
        raise ValueError("valid task semantic counts are invalid or unbounded")
    if (expected_kind == "PlainText" and witness["token_count"] != 0) or (
            expected_kind != "PlainText" and witness["token_count"] == 0):
        raise ValueError("semantic tokens do not match the specified task kind")
    csv = expected_kind == "Csv"
    if witness["grid_required"] != csv or witness["grid_ready"] != csv:
        raise ValueError("semantic Grid requirement differs from task kind")
    expected_grid = (3, 3, 9, 0) if csv else (0, 0, 0, 0)
    if tuple(witness[key] for key in ("grid_rows", "grid_columns", "grid_cells", "grid_pending_cells")) != expected_grid:
        raise ValueError("semantic Grid counts differ from the full small-task ready viewport")
    version = witness["version"]
    required = {"analysis.parse", "analysis.published", "native.source.style_publish"}
    linked = False
    for records, requests in saved_processes:
        matching = [request for request in requests if request["saved_version"] == version]
        current = {row["operation"] for row in records if row["status"] == "success" and
                   row["attributes"].get("version") == version}
        linked |= bool(matching) and required <= current
    if not linked:
        raise ValueError("semantic version lacks complete Save and current analysis/style evidence")
    return {"status": "passed", "platform": platform, "sha256": hashlib.sha256(raw).hexdigest(),
            "witness": witness, "saved_version_linked": True,
            "current_parse_publish_style_observed": True,
            "endpoint": "admitted native identities and ready cells, not pixel quality"}


def verify(paths: list[str], private: list[str], require_save: bool = False,
           require_native_source: bool = False, semantics_path: str | None = None,
           expected_kind: str | None = None, expected_units: int | None = None,
           semantic_platform: str = "macos") -> dict:
    """Reject malformed graphs, leaked sentinels, drops and missing Save evidence.

    Each path must contain one complete process session. Multiple paths are
    checked independently rather than accidentally merging process identities.
    Requiring Save checks engine/native phase evidence, not external key input.
    """
    if not paths:
        raise ValueError("no retained trace")
    reports = []
    saved_task = False
    saved_processes = []
    for path in paths:
        records, hashes = ACCEPTANCE.load_records([path])
        raw = Path(path).read_text(encoding="utf-8-sig")
        if not raw.endswith("\n"):
            raise ValueError("unterminated trace")
        if any(value and value.casefold() in raw.casefold() for value in private):
            raise ValueError("trace contains a private sentinel")
        result = ACCEPTANCE.audit(records, {})
        if result["causal_integrity"] != "pass":
            raise ValueError("invalid causal trace: " + ",".join(result["issues"]))
        operations = {row["operation"] for row in records if row["status"] == "success"}
        source_surface = {"native.source.install", "native.source.readback"} <= operations
        if require_native_source and not source_surface:
            raise ValueError("bare/default launch lacks successful native-source install/readback witness")
        if require_save or semantics_path:
            needed = {"document.open", "document.edit", "document.save", "save.completed"}
            save = classify_requests(records, MOTE_SAVE_CONTRACT, terminated=True)
            complete = [request for request in save["requests"]
                        if request["successful_chain_complete"] and
                        request["coverage"] == "instrumented_chain_only"]
            saved_task |= needed <= operations and bool(complete)
            if needed <= operations and complete:
                saved_processes.append((records, complete))
        reports.append({"path": str(Path(path).relative_to(ROOT)), "sha256": hashes[0],
                        "records": len(records), "operations": sorted(operations),
                        "native_source_surface_witness": source_surface,
                        "causal_integrity": "pass", "dropped_records": 0})
    if require_save and not saved_task:
        raise ValueError("no process trace contains successful open/edit and a complete version-linked Save chain")
    verdict = {"status": "passed", "traces": reports,
               "endpoint": "instrumented callbacks and engine phases, not physical input/pixels"}
    if semantics_path:
        verdict["semantics"] = verify_semantics(semantics_path, expected_kind, expected_units, saved_processes, semantic_platform)
    return verdict


def main() -> None:
    """Read CLI evidence and emit a bounded, content-free JSON verdict."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trace", action="append", required=True)
    parser.add_argument("--private", action="append", default=[])
    parser.add_argument("--require-save", action="store_true")
    parser.add_argument("--require-native-source", action="store_true")
    parser.add_argument("--semantics")
    parser.add_argument("--expected-kind", choices=sorted(SEMANTIC_KINDS))
    parser.add_argument("--expected-source-units", type=int)
    parser.add_argument("--semantic-platform", choices=("macos", "windows"), default="macos")
    args = parser.parse_args()
    print(json.dumps(verify(args.trace, args.private, args.require_save,
                            args.require_native_source, args.semantics,
                            args.expected_kind, args.expected_source_units, args.semantic_platform), sort_keys=True))


if __name__ == "__main__":
    main()
