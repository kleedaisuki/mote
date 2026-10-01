"""Consolidate four qualified RID artifacts into a non-overwritable release set."""

import argparse
import json
from pathlib import Path
import shutil
import subprocess

from release_package import ROOT, RIDS, checked_output, digest, identity, unpack, verify, write_json


def consolidate(input_directory, output, version, source, run_url):
    """Recheck archive contents before emitting checksums and a complete release index."""
    identity(version, source)
    output = checked_output(output)
    if output.exists():
        raise ValueError("Release destination already exists")
    output.mkdir(parents=True)
    inputs = Path(input_directory).resolve(strict=True)
    assets = []
    for rid in RIDS:
        stem = f"mote-{version}-{rid}"
        suffix = ".zip" if rid.startswith("win-") else ".tar.gz"
        matches = list(inputs.rglob(stem + suffix))
        manifests = list(inputs.rglob(stem + ".manifest.json"))
        if len(matches) != 1 or len(manifests) != 1:
            raise ValueError(f"Require exactly one archive and manifest for {rid}")
        extraction = unpack(matches[0], output / "verification" / rid)
        roots = list(extraction.iterdir())
        if len(roots) != 1 or not roots[0].is_dir():
            raise ValueError("Package archive must have exactly one root directory")
        verify(roots[0], rid, version, source)
        embedded = json.loads((roots[0] / "package-manifest.json").read_text(encoding="utf-8"))
        external = json.loads(manifests[0].read_text(encoding="utf-8"))
        if embedded != external or embedded["build"]["workflowRun"] != run_url:
            raise ValueError("External package manifest or workflow provenance mismatch")
        for path in (matches[0], manifests[0]):
            destination = output / path.name
            shutil.copyfile(path, destination)
            assets.append({"name": destination.name, "bytes": destination.stat().st_size,
                           "sha256": digest(destination), "runtimeIdentifier": rid})
    # This is the complete Git tree for the same immutable source, not a moving
    # branch-generated archive. GPL source and build scripts travel with binaries.
    actual = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    if actual != source:
        raise ValueError("Source checkout does not match release binaries")
    source_archive = output / f"mote-{version}-source.tar.gz"
    subprocess.run(["git", "archive", "--format=tar.gz", f"--prefix=mote-{version}/",
                    f"--output={source_archive}", source], cwd=ROOT, check=True)
    assets.append({"name": source_archive.name, "bytes": source_archive.stat().st_size,
                   "sha256": digest(source_archive), "runtimeIdentifier": None})
    # Verification trees remain private CI evidence, never download assets.
    shutil.rmtree(output / "verification")
    index = output / "release-index.json"
    write_json(index, {"schemaVersion": 1, "product": "mote", "version": version,
                       "sourceCommit": source, "workflowRun": run_url,
                       "runtime": "NativeAOT", "assets": assets,
                       "signing": "no-publisher-signature-or-notarization"})
    checksum_paths = sorted(path for path in output.iterdir() if path.is_file())
    (output / "SHA256SUMS").write_text("".join(f"{digest(path)}  {path.name}\n"
                                             for path in checksum_paths), encoding="ascii")
    return output


def main():
    """Require exact source/version/run identity at the release-set boundary."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--source", required=True)
    parser.add_argument("--run-url", required=True)
    args = parser.parse_args()
    print(consolidate(args.input, args.output, args.version, args.source, args.run_url))


if __name__ == "__main__":
    main()
