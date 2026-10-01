"""Check retained release traces with the existing closed privacy/causal reader.

This is an artifact oracle, not a GUI driver or proof of physical presentation.
Only repository-local evidence files are accepted by the reused reader.
"""

from __future__ import annotations

import argparse
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


def verify(paths: list[str], private: list[str], require_save: bool = False) -> dict:
    """Reject malformed graphs, leaked sentinels, drops and missing Save evidence.

    Each path must contain one complete process session. Multiple paths are
    checked independently rather than accidentally merging process identities.
    Requiring Save checks engine/native phase evidence, not external key input.
    """
    if not paths:
        raise ValueError("no retained trace")
    reports = []
    saved_task = False
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
        if require_save:
            needed = {"document.open", "document.edit", "document.save", "save.completed"}
            save = classify_requests(records, MOTE_SAVE_CONTRACT, terminated=True)
            complete = [request for request in save["requests"]
                        if request["successful_chain_complete"] and
                        request["coverage"] == "instrumented_chain_only"]
            saved_task |= needed <= operations and bool(complete)
        reports.append({"path": str(Path(path).relative_to(ROOT)), "sha256": hashes[0],
                        "records": len(records), "operations": sorted(operations),
                        "causal_integrity": "pass", "dropped_records": 0})
    if require_save and not saved_task:
        raise ValueError("no process trace contains successful open/edit and a complete version-linked Save chain")
    return {"status": "passed", "traces": reports,
            "endpoint": "instrumented callbacks and engine phases, not physical input/pixels"}


def main() -> None:
    """Read CLI evidence and emit a bounded, content-free JSON verdict."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trace", action="append", required=True)
    parser.add_argument("--private", action="append", default=[])
    parser.add_argument("--require-save", action="store_true")
    args = parser.parse_args()
    print(json.dumps(verify(args.trace, args.private, args.require_save), sort_keys=True))


if __name__ == "__main__":
    main()
