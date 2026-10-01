"""Verify resolved Native AOT runtime/compiler packs, not the installed host runtime."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

SDK_VERSION = "10.0.401"
RUNTIME_VERSION = "10.0.12"
RIDS = ("win-x64", "win-arm64", "osx-x64", "osx-arm64")


def validate_summary(summary, rid, source=None, run_url=None):
    """Require the serviced SDK and native-runtime/compiler identities in package provenance."""
    if rid not in RIDS or any(summary.get(key) != value for key, value in
                             {"schemaVersion": 1, "runtimeIdentifier": rid,
                              "dotnetSdk": SDK_VERSION, "runtimeVersion": RUNTIME_VERSION,
                              "ilCompilerVersion": RUNTIME_VERSION}.items()):
        raise ValueError("Release toolchain summary identity mismatch")
    expected = {"runtimePacks": f"Microsoft.NETCore.App.Runtime.{rid}",
                "ilCompilerPacks": f"runtime.{rid}.Microsoft.DotNet.ILCompiler",
                "nativeRuntimePacks": f"Microsoft.NETCore.App.Runtime.NativeAOT.{rid}"}
    for group, required in expected.items():
        rows = summary.get(group, [])
        if not rows or sum(row.get("name", "").lower() == required.lower() for row in rows) != 1:
            raise ValueError("Missing or duplicate required resolved toolchain pack")
        if group != "runtimePacks" and len(rows) != 1:
            raise ValueError("Unexpected resolved compiler/native runtime pack")
        for row in rows:
            if row.get("version") != RUNTIME_VERSION or not re.fullmatch(r"[0-9a-f]{64}", row.get("nuspecSha256", "")):
                raise ValueError("Unserviced or invalid resolved toolchain pack")
    if source is not None and (summary.get("sourceCommit") != source or summary.get("workflowRun") != run_url or
                               not re.fullmatch(r"[0-9a-f]{64}", summary.get("resolvedMetadataSha256", ""))):
        raise ValueError("Resolved toolchain source/run provenance mismatch")
    return summary


def installed_pack(row):
    """Verify installed package metadata matches MSBuild's exact resolved identity."""
    name = row.get("NuGetPackageId", "")
    version = row.get("NuGetPackageVersion", "")
    directory = Path(row.get("PackageDirectory", ""))
    if version != RUNTIME_VERSION or not name or not directory.is_dir():
        raise ValueError("Resolved pack version/directory mismatch")
    nuspecs = list(directory.glob("*.nuspec"))
    if len(nuspecs) != 1:
        raise ValueError("Require one installed pack nuspec")
    root = ET.parse(nuspecs[0]).getroot()
    values = {node.tag.rsplit("}", 1)[-1]: node.text for metadata in root
              if metadata.tag.rsplit("}", 1)[-1] == "metadata" for node in metadata}
    if values.get("id", "").lower() != name.lower() or values.get("version") != version:
        raise ValueError("Installed nuspec does not match resolved pack")
    return {"name": name, "version": version,
            "nuspecSha256": hashlib.sha256(nuspecs[0].read_bytes()).hexdigest()}


def verify_resolved(resolved, rid):
    """Check actual framework-resolution output and emit a path-free manifest summary."""
    properties = resolved.get("Properties", {})
    if properties.get("NETCoreSdkVersion") != SDK_VERSION or properties.get("RuntimeFrameworkVersion") != RUNTIME_VERSION:
        raise ValueError("Resolved SDK/runtime property mismatch")
    items = resolved.get("Items", {})
    summary = {"schemaVersion": 1, "runtimeIdentifier": rid, "dotnetSdk": SDK_VERSION,
               "runtimeVersion": RUNTIME_VERSION, "ilCompilerVersion": RUNTIME_VERSION}
    for target, source in (("runtimePacks", "ResolvedRuntimePack"),
                           ("ilCompilerPacks", "ResolvedILCompilerPack"),
                           ("nativeRuntimePacks", "ResolvedTargetILCompilerPack")):
        summary[target] = [installed_pack(row) for row in items.get(source, [])]
    return validate_summary(summary, rid)


def main():
    """Validate retained MSBuild metadata before publishing native code."""
    from release_package import checked_output, write_json
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--resolved", required=True)
    parser.add_argument("--rid", required=True, choices=RIDS)
    parser.add_argument("--output", required=True)
    parser.add_argument("--source", required=True)
    parser.add_argument("--run-url", required=True)
    args = parser.parse_args()
    raw = Path(args.resolved).read_bytes()
    summary = verify_resolved(json.loads(raw.decode("utf-8-sig")), args.rid)
    if not re.fullmatch(r"[0-9a-f]{40}", args.source):
        raise ValueError("Toolchain source must be immutable full commit SHA")
    summary.update(sourceCommit=args.source, workflowRun=args.run_url,
                   resolvedMetadataSha256=hashlib.sha256(raw).hexdigest())
    validate_summary(summary, args.rid, args.source, args.run_url)
    output = checked_output(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    if output.exists():
        raise ValueError("Toolchain evidence must not be overwritten")
    write_json(output, summary)
    print(json.dumps(summary, separators=(",", ":")))


if __name__ == "__main__":
    main()
