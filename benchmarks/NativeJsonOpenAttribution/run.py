"""Run bounded fresh-process Native AOT open attribution and rejected prototypes.

Usage from the repository root after prepare/publish:
    python benchmarks/NativeJsonOpenAttribution/run.py --suite attribution
    python benchmarks/NativeJsonOpenAttribution/run.py --suite broad
    python benchmarks/NativeJsonOpenAttribution/run.py --suite hybrid

All source fixtures and child logs stay in .temp; metadata/raw observations stay
in .cache. Every timed child has a 30-second bound; correctness has 180 seconds.
"""

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import time

sys.dont_write_bytecode = True
from prepare import ROOT, artifact


def digest(path):
    """Stream exact input/binary identity without whole-file allocation."""
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def line_oracle(path):
    """Count ASCII fixture CR/LF/CRLF across byte-block boundaries independently."""
    count, last_cr = 0, False
    with path.open("rb") as stream:
        while block := stream.read(65536):
            count += block.count(b"\r") + block.count(b"\n") - block.count(b"\r\n")
            if last_cr and block.startswith(b"\n"):
                count -= 1
            last_cr = block.endswith(b"\r")
    return count + 1


def fixtures(directory, suite):
    """Reuse the exact upstream JSON pilot corpus plus pathological newline shapes."""
    spec = importlib.util.spec_from_file_location("native_json_probe", ROOT / "benchmarks/NativeJsonLargeAcceptance/probe.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    directory.mkdir(parents=True, exist_ok=True)
    cases = module.prepare(directory, [1, 100] if suite == "attribution" else [100])
    result = {f"json-LF-{case['size_mib']}": case["fixture"] for case in cases}
    if suite != "attribution":
        for name, pattern in {"LF": b"a\n", "CRLF": b"a\r\n", "CR": b"a\r", "none": b"a"}.items():
            path = directory / (name + "-10Mi.txt")
            block = pattern * (65536 // len(pattern))
            with path.open("wb") as stream:
                remain = 10 * 1048576
                while remain:
                    chunk = block[:remain]
                    stream.write(chunk)
                    remain -= len(chunk)
            result[name + "-10"] = path
    return result


def run(args):
    """Rotate matched controls; preserve process-wide allocations and raw phase counts."""
    directory = artifact(args.directory, ".temp")
    output = artifact(args.output, ".cache")
    output.mkdir(parents=True, exist_ok=True)
    binary = artifact(directory / "publish/Probe.exe", ".temp")
    if not binary.is_file():
        raise ValueError("Publish the Windows x64 Native AOT probe first")
    prepared = json.loads((directory / "prepared.json").read_text())
    if set(prepared["engine_source_sha256"]) != {p.name for p in (ROOT / "src/Mote.Engine").glob("*.cs")}:
        raise ValueError("Engine changed after preparation; source file inventory differs")
    for name, expected in prepared["engine_source_sha256"].items():
        if digest(ROOT / "src/Mote.Engine" / name) != expected:
            raise ValueError("Engine changed after preparation; prepare/publish a new matched probe")
    for name, expected in prepared.get("template_sha256", {}).items():
        if digest(Path(__file__).parent / name) != expected:
            raise ValueError("Probe template changed after preparation")
    for name, expected in prepared.get("generated_source_sha256", {}).items():
        if digest(directory / name) != expected:
            raise ValueError("Generated probe source changed after preparation")
    if (output / (args.suite + "-rows.jsonl")).exists():
        raise ValueError("Results already exist; choose a new --output instead of overwriting evidence")
    identity = subprocess.run([str(binary), "identity", str(directory / "identity-placeholder")], cwd=ROOT,
                              text=True, capture_output=True, timeout=10, check=True)
    if identity.stdout.strip() != prepared["preparation_sha256"]:
        raise ValueError("Published binary belongs to a different preparation; publish the fresh probe")
    oracle = subprocess.run([str(binary), "line-check", str(directory / "oracle-placeholder")], cwd=ROOT,
                            text=True, capture_output=True, timeout=180, check=True)
    (output / (args.suite + "-line-check.txt")).write_text(oracle.stdout)
    paths = fixtures(directory / "corpus" / args.suite, args.suite)
    metadata = {"schema_version": 1, "binary_sha256": digest(binary),
                "prepared": prepared,
                "cache_state": "generated and SHA-scanned; no eviction; new process per invocation",
                "suite": args.suite, "repetitions": args.repeats,
                "fixtures": {name: {"relative_path": str(path.relative_to(ROOT)), "bytes": path.stat().st_size,
                                    "sha256": digest(path), "expected_lines": line_oracle(path)} for name, path in paths.items()}}
    (output / (args.suite + "-metadata.json")).write_text(json.dumps(metadata, indent=2))
    modes = ["baseline", "copy-control", "phases"]
    if args.suite != "attribution":
        modes.append({"broad": "line-prototype", "hybrid": "line-hybrid", "direct": "line-direct"}[args.suite])
    with (output / (args.suite + "-rows.jsonl")).open("w") as results:
        for name, path in paths.items():
            for repetition in range(args.repeats):
                start = repetition % len(modes)
                for mode in modes[start:] + modes[:start]:
                    child = subprocess.run([str(binary), mode, str(path)], cwd=ROOT, text=True,
                                           capture_output=True, timeout=30, check=True)
                    row = json.loads(child.stdout)
                    row.update(Fixture=name, Repetition=repetition)
                    if row.get("PreparationSha256") != prepared["preparation_sha256"]:
                        raise ValueError("Measured binary preparation identity mismatch")
                    expected = metadata["fixtures"][name]
                    if row["LengthUtf16"] != expected["bytes"] or row["LineCount"] != expected["expected_lines"]:
                        raise ValueError("Independent ASCII length/line oracle mismatch")
                    if mode not in ("baseline", "copy-control") and row["PhaseCalls"][0] < 1:
                        raise ValueError("Read phase instrumentation missing")
                    row["LineOracleChecked"] = True
                    results.write(json.dumps(row) + "\n")
                    results.flush()
                    print(name, repetition, mode, round(row["OpenMs"], 3), flush=True)
                    time.sleep(0.05)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", default=ROOT / ".temp/native-json-open-attribution", type=Path)
    parser.add_argument("--output", default=ROOT / ".cache/native-json-open-attribution/reproduced", type=Path)
    parser.add_argument("--suite", choices=("attribution", "broad", "hybrid", "direct"), default="attribution")
    parser.add_argument("--repeats", type=int, default=6)
    args = parser.parse_args()
    if not 1 <= args.repeats <= 20:
        parser.error("Repetitions must be between 1 and 20")
    run(args)
