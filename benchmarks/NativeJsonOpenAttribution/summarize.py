"""Summarize raw Open rows and exploratory within-host matched differences.

Usage: python benchmarks/NativeJsonOpenAttribution/summarize.py \
    .cache/native-json-open-attribution/reproduced/hybrid-rows.jsonl

Uses medians and observed ranges, not p95. A six-pair bootstrap interval describes
only this host/session and is not an SLA or heterogeneous-machine confidence.
"""

import json
from pathlib import Path
import random
import statistics
import sys

sys.dont_write_bytecode = True
from prepare import artifact


def summary(path):
    """Keep timer/copy controls separate and pair only within fixture/repetition."""
    rows = [json.loads(line) for line in artifact(path, ".cache").read_text().splitlines()]
    result = []
    for fixture in dict.fromkeys(row.get("Fixture", row.get("SizeMiB")) for row in rows):
        group = [row for row in rows if row.get("Fixture", row.get("SizeMiB")) == fixture]
        modes = {}
        for mode in dict.fromkeys(row["Mode"] for row in group):
            selected = [row for row in group if row["Mode"] == mode]
            elapsed = [row["OpenMs"] for row in selected]
            modes[mode] = {"n": len(selected), "median_open_ms": statistics.median(elapsed),
                           "observed_min_ms": min(elapsed), "observed_max_ms": max(elapsed),
                           "median_allocation_bytes": statistics.median(row["AllocatedBytes"] for row in selected),
                           "median_phase_ms": [statistics.median(row["PhaseMs"][i] for row in selected) for i in range(5)]}
        paired = {}
        baseline = {row["Repetition"]: row for row in group if row["Mode"] == "baseline"}
        for mode in modes.keys() - {"baseline"}:
            candidate = {row["Repetition"]: row for row in group if row["Mode"] == mode}
            differences = [candidate[key]["OpenMs"] - baseline[key]["OpenMs"] for key in sorted(baseline.keys() & candidate.keys())]
            if not differences:
                continue
            rng = random.Random(73219)
            boot = sorted(statistics.mean(rng.choices(differences, k=len(differences))) for _ in range(20000))
            paired[mode] = {"differences_ms": differences, "mean_difference_ms": statistics.mean(differences),
                            "exploratory_percentile_95_ci_ms": [boot[499], boot[19499]],
                            "scope": "within-host/session resampling only; no population tail claim"}
        result.append({"fixture": fixture, "modes": modes, "paired": paired})
    return result


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    print(json.dumps(summary(Path(sys.argv[1])), indent=2))
