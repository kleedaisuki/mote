"""Run an owned trace child, observe its positive prefix, then kill only that child.

Usage: python -B tests/Invoke-CausalTraceRecovery.py --binary <published probe>
The JSON report is infrastructure evidence, not a real Save byte oracle.
"""

import argparse
import json
from pathlib import Path
import subprocess
import time
import uuid

from causal_save_trace_reader import MOTE_SAVE_CONTRACT, classify_requests, read_prefix


def read_directory(directory, terminated):
    """Read this fresh single-session directory in the writer's filename order."""
    records = []
    for path in sorted(directory.glob("*.jsonl")):
        records.extend(read_prefix(path.read_bytes(), terminated=terminated).records)
    return classify_requests(records, MOTE_SAVE_CONTRACT, terminated=terminated)


def run_case(binary, directory, mode):
    """Require causal nested entries before kill; cleanup cannot certify completion."""
    directory.mkdir(parents=True)
    command = (["dotnet", str(binary)] if binary.suffix == ".dll" else [str(binary)])
    with (directory / "stdout.log").open("wb") as stdout, (directory / "stderr.log").open("wb") as stderr:
        child = subprocess.Popen(command + [str(directory), mode], stdout=stdout, stderr=stderr)
        try:
            if mode == "normal":
                exit_code = child.wait(timeout=20)
                report = read_directory(directory, True)
                requests = report["requests"]
                passed = exit_code == 0 and len(requests) == 1 and requests[0]["successful_chain_complete"]
                passed = passed and report["normal_session_terminal_observed"] and not report["dropped_records_observed"]
                return {"mode": mode, "passed": passed, "exit_code": exit_code, "evidence": report}
            deadline = time.monotonic() + 20
            before = None
            while time.monotonic() < deadline:
                if child.poll() is not None:
                    raise RuntimeError("held child exited before positive prefix")
                report = read_directory(directory, False)
                requests = report["requests"]
                if len(requests) == 1:
                    request = requests[0]
                    stages = request["observed_stages"]
                    if request["receipt_observed"] and "document.save.entered" in stages and "save.temp_flush.entered" in stages:
                        before = report
                        break
                time.sleep(0.025)
            if before is None:
                raise RuntimeError("positive receipt and linked held phase prefix not observed within deadline")
            child.kill()
            exit_code = child.wait(timeout=10)
            after = read_directory(directory, True)
            request = after["requests"][0]
            passed = request["classification"] == "censored" and request["receipt_observed"]
            passed = passed and "save.temp_flush.entered" in request["observed_stages"]
            passed = passed and not after["normal_session_terminal_observed"] and not after["absence_certified"]
            return {"mode": mode, "passed": passed, "exit_code": exit_code, "before_kill": before, "evidence": after}
        finally:
            if child.poll() is None:
                child.kill()
                child.wait(timeout=10)


def main():
    """Keep all artifacts under repository .cache/.temp and return actual failure."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--binary", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    output = (args.output or root / ".temp" / ("causal-recovery-" + uuid.uuid4().hex)).resolve()
    if not any(output.is_relative_to(root / name) for name in (".temp", ".cache")):
        parser.error("output must stay beneath repository .temp or .cache")
    output.mkdir(parents=True, exist_ok=False)
    results = []
    for mode in ("held", "normal"):
        try:
            results.append(run_case(args.binary.resolve(), output / mode, mode))
        except Exception as error:
            results.append({"mode": mode, "passed": False, "error": str(error)})
    report = {"passed": all(case["passed"] for case in results), "cases": results,
              "scope": "owned synthetic nested telemetry request; no document Save or power-loss durability claim"}
    (output / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"passed": report["passed"], "report": str(output / "report.json")}))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
