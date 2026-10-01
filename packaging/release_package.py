"""Build and verify Native AOT release packages without installing a runtime.

The inventory is a byte-integrity/provenance contract, not a signing claim.
All writes are confined to repository .cache/ or .temp/ directories.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import plistlib
import re
import shutil
import stat
import struct
import subprocess
import tarfile
import zipfile
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
RIDS = ("win-x64", "win-arm64", "osx-x64", "osx-arm64")
DOCS = ("README.md", "CHANGELOG.md", "LICENSE", "packaging/THIRD-PARTY-NOTICES.txt",
        "docs/user/manual.md", "docs/user/installation.md", "docs/user/configuration.md")


def checked_output(path):
    """Reject output paths outside project scratch trees, including symlink escapes."""
    result = Path(path).resolve()
    if not any(result.is_relative_to(ROOT / name) and result != ROOT / name
               for name in (".cache", ".temp")):
        raise ValueError("Output must be a child of repository .cache or .temp")
    return result


def identity(version, source):
    """Accept only stable numeric versions and immutable Git commit identifiers."""
    if not re.fullmatch(r"(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)", version):
        raise ValueError("Version must be canonical major.minor.patch")
    if not re.fullmatch(r"[0-9a-f]{40}", source):
        raise ValueError("Source must be a full lowercase Git SHA")


def digest(path):
    """Hash files incrementally so inventory creation never mirrors large payloads."""
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def checkout_identity(source):
    """Prevent a package from claiming a commit different from its source checkout."""
    actual = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    if actual != source:
        raise ValueError("Source checkout does not match requested package identity")
    subprocess.run(["git", "diff", "--quiet", "HEAD", "--", "src", "packaging", "docs",
                    "README.md", "CHANGELOG.md", "LICENSE"], cwd=ROOT, check=True)


def files(root):
    """Enumerate regular files, rejecting links rather than silently omitting assets."""
    result = []
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise ValueError(f"Symlinks are not accepted in release payload: {path}")
        if path.is_file():
            result.append(path)
        elif not path.is_dir():
            raise ValueError(f"Nonregular payload: {path}")
    return result


def binary_arch(path, rid):
    """Check executable architecture and reject managed PE/CLR fallback binaries."""
    data = path.read_bytes()
    if rid.startswith("win-"):
        if len(data) < 64 or data[:2] != b"MZ":
            raise ValueError("Missing PE executable header")
        offset = struct.unpack_from("<I", data, 60)[0]
        if offset + 24 > len(data) or data[offset:offset + 4] != b"PE\0\0":
            raise ValueError("Invalid PE signature")
        machine = struct.unpack_from("<H", data, offset + 4)[0]
        if machine != (0x8664 if rid.endswith("x64") else 0xAA64):
            raise ValueError("PE architecture does not match RID")
        optional = offset + 24
        if optional + 240 > len(data) or struct.unpack_from("<H", data, optional)[0] != 0x20B:
            raise ValueError("Expected PE32+ optional header")
        # IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR is entry 14 in PE32+ directories.
        clr = struct.unpack_from("<II", data, optional + 112 + 14 * 8)
        if clr != (0, 0):
            raise ValueError("Managed CLR PE is not a Native AOT artifact")
    else:
        if len(data) < 32 or data[:4] != b"\xcf\xfa\xed\xfe":
            raise ValueError("Expected little-endian 64-bit Mach-O")
        cpu = struct.unpack_from("<I", data, 4)[0]
        if cpu != (0x01000007 if rid.endswith("x64") else 0x0100000C):
            raise ValueError("Mach-O architecture does not match RID")


def write_json(path, value):
    """Write stable UTF-8 JSON with a final newline."""
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def build(publish, output, rid, version, source, run_url, sdk):
    """Assemble an immutable package and archive; never overwrite an existing version."""
    identity(version, source)
    if rid not in RIDS:
        raise ValueError("Unsupported runtime identifier")
    checkout_identity(source)
    if sdk != "10.0.400":
        raise ValueError("SDK must match the release's pinned runtime license inventory")
    output = checked_output(output)
    publish = Path(publish).resolve(strict=True)
    if output.is_relative_to(publish) or publish.is_relative_to(output):
        raise ValueError("Publish and output trees must not overlap")
    payload = files(publish)
    if not payload:
        raise ValueError("Empty publish payload")
    executable = "mote.exe" if rid.startswith("win-") else "mote"
    binary_arch(publish / executable, rid)
    stem = f"mote-{version}-{rid}"
    package = output / (stem if rid.startswith("win-") else "mote.app")
    archive = output / (stem + (".zip" if rid.startswith("win-") else ".tar.gz"))
    if package.exists() or archive.exists():
        raise ValueError("Release output already exists; use a fresh scratch directory")
    target = package if rid.startswith("win-") else package / "Contents/MacOS"
    target.mkdir(parents=True)
    for path in payload:
        if path.suffix.lower() in (".pdb", ".dbg") or any(part.endswith(".dSYM") for part in path.parts):
            continue
        if path.name.endswith((".runtimeconfig.json", ".deps.json")):
            raise ValueError("Runtime-dependent publish asset is not allowed")
        if path.suffix.lower() in (".exe", ".dll", ".dylib"):
            binary_arch(path, rid)
        destination = target / path.relative_to(publish)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, destination)
    resources = package if rid.startswith("win-") else package / "Contents/Resources"
    resources.mkdir(parents=True, exist_ok=True)
    for relative in DOCS + (f"docs/releases/v{version}.md",):
        destination = resources / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / relative, destination)
    shutil.copytree(ROOT / "packaging/licenses", resources / "packaging/licenses")
    exe_relative = executable
    if rid.startswith("osx-"):
        with (ROOT / "src/Mote.Native/Info.plist").open("rb") as stream:
            plist = plistlib.load(stream)
        plist.update(CFBundleExecutable="mote", CFBundleVersion=version,
                     CFBundleShortVersionString=version, LSMinimumSystemVersion="15.0")
        with (package / "Contents/Info.plist").open("wb") as stream:
            plistlib.dump(plist, stream)
        (target / executable).chmod(0o755)
        exe_relative = "Contents/MacOS/mote"
    inventory = [{"path": path.relative_to(package).as_posix(), "bytes": path.stat().st_size,
                  "sha256": digest(path)} for path in files(package)]
    manifest = {"schemaVersion": 1, "product": "mote", "version": version,
                "sourceCommit": source, "runtimeIdentifier": rid, "runtime": "NativeAOT",
                "executable": exe_relative, "signing": "no-publisher-signature-or-notarization",
                "build": {"workflowRun": run_url, "dotnetSdk": sdk}, "files": inventory}
    write_json(package / "package-manifest.json", manifest)
    if rid.startswith("win-"):
        with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED) as stream:
            for path in files(package):
                stream.write(path, path.relative_to(output).as_posix())
    else:
        with tarfile.open(archive, "x:gz", format=tarfile.PAX_FORMAT) as stream:
            stream.add(package, arcname=package.name)
    write_json(output / f"{stem}.manifest.json", manifest)
    (output / f"{archive.name}.sha256").write_text(f"{digest(archive)}  {archive.name}\n", encoding="ascii")
    return archive


def safe_member(name):
    """Accept portable relative paths, never extraction traversal or drive paths."""
    path = PurePosixPath(name)
    if not name or "\\" in name or ":" in name or path.is_absolute() or ".." in path.parts:
        raise ValueError("Unsafe archive member")
    return path


def documentation_links(package, resources):
    """Check packaged Markdown relative-link closure, excluding web links and anchors."""
    for path in files(resources):
        if path.suffix.lower() != ".md":
            continue
        for name in re.findall(r"\[[^\]]*\]\(([^)\s]+)(?:\s+\"[^\"]*\")?\)",
                               path.read_text(encoding="utf-8-sig")):
            parsed = urlsplit(name)
            if parsed.scheme or parsed.netloc or not parsed.path:
                continue
            target = (path.parent / unquote(parsed.path)).resolve()
            if not target.is_relative_to(package) or not target.exists():
                raise ValueError(f"Packaged documentation link is missing: {path.name}: {name}")


def unpack(archive, output):
    """Extract regular files/directories only, retaining macOS executable permissions."""
    output = checked_output(output)
    if output.exists():
        raise ValueError("Extraction destination must not exist")
    output.mkdir(parents=True)
    seen = set()
    def destination(name):
        """Reject duplicate/case-colliding paths on either supported platform."""
        relative = safe_member(name)
        folded = str(relative).casefold()
        if folded in seen:
            raise ValueError("Duplicate archive member")
        seen.add(folded)
        return output.joinpath(*relative.parts)
    if str(archive).endswith(".zip"):
        with zipfile.ZipFile(archive) as stream:
            for entry in stream.infolist():
                target = destination(entry.filename)
                mode = entry.external_attr >> 16
                if stat.S_ISLNK(mode):
                    raise ValueError("Archive symlink not allowed")
                if entry.is_dir():
                    target.mkdir(parents=True, exist_ok=True)
                else:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with stream.open(entry) as src, target.open("xb") as dst:
                        shutil.copyfileobj(src, dst)
    else:
        with tarfile.open(archive, "r:gz") as stream:
            for entry in stream:
                target = destination(entry.name)
                if entry.isdir():
                    target.mkdir(parents=True, exist_ok=True)
                elif entry.isfile():
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with stream.extractfile(entry) as src, target.open("xb") as dst:
                        shutil.copyfileobj(src, dst)
                    target.chmod(entry.mode & 0o777)
                else:
                    raise ValueError("Archive link/device not allowed")
    return output


def verify(package, rid, version, source):
    """Check complete inventory, identity, hashes, native architecture and bundle metadata."""
    identity(version, source)
    package = Path(package).resolve(strict=True)
    manifest = json.loads((package / "package-manifest.json").read_text(encoding="utf-8"))
    expected = {"schemaVersion": 1, "product": "mote", "version": version,
                "sourceCommit": source, "runtimeIdentifier": rid, "runtime": "NativeAOT",
                "signing": "no-publisher-signature-or-notarization"}
    if any(manifest.get(key) != value for key, value in expected.items()):
        raise ValueError("Package provenance mismatch")
    if manifest.get("build", {}).get("dotnetSdk") != "10.0.400":
        raise ValueError("Package SDK does not match license inventory")
    expected_exe = "mote.exe" if rid.startswith("win-") else "Contents/MacOS/mote"
    if manifest.get("executable") != expected_exe:
        raise ValueError("Package executable mismatch")
    inventory = manifest["files"]
    names = [str(safe_member(row["path"])) for row in inventory]
    if len(set(name.casefold() for name in names)) != len(names):
        raise ValueError("Duplicate inventory path")
    actual = {path.relative_to(package).as_posix() for path in files(package)
              if path.name != "package-manifest.json" or path.parent != package}
    if set(names) != actual:
        raise ValueError("Package has missing/uninventoried files")
    resources = "" if rid.startswith("win-") else "Contents/Resources/"
    required = {resources + name for name in DOCS + (f"docs/releases/v{version}.md",)}
    required.update(resources + "packaging/licenses/" + path.name
                    for path in (ROOT / "packaging/licenses").glob("*.txt"))
    if not required.issubset(actual):
        raise ValueError("Package lacks required documentation/licenses")
    for row in inventory:
        if type(row.get("bytes")) is not int or row["bytes"] < 0 or not re.fullmatch(r"[0-9a-f]{64}", row.get("sha256", "")):
            raise ValueError("Invalid file inventory record")
        path = package / row["path"]
        if path.stat().st_size != row["bytes"] or digest(path) != row["sha256"]:
            raise ValueError("Package file hash/size mismatch")
    documentation_links(package, package / resources)
    binary_arch(package / expected_exe, rid)
    if rid.startswith("osx-"):
        with (package / "Contents/Info.plist").open("rb") as stream:
            plist = plistlib.load(stream)
        if any(plist.get(key) != value for key, value in
               {"CFBundleExecutable": "mote", "CFBundleVersion": version,
                "CFBundleShortVersionString": version, "CFBundlePackageType": "APPL",
                "LSMinimumSystemVersion": "15.0"}.items()):
            raise ValueError("Bundle metadata mismatch")
        if os.name != "nt" and not os.access(package / expected_exe, os.X_OK):
            raise ValueError("macOS executable lost execute permissions")
    return package / expected_exe


def main():
    """Expose construction and extracted-package validation to CI and maintainers."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("build", "verify", "unpack"))
    parser.add_argument("--rid", choices=RIDS)
    parser.add_argument("--version")
    parser.add_argument("--source")
    parser.add_argument("--publish")
    parser.add_argument("--output")
    parser.add_argument("--package")
    parser.add_argument("--archive")
    parser.add_argument("--run-url", default="local-unpublished")
    parser.add_argument("--sdk", default="unknown-local")
    args = parser.parse_args()
    if args.action == "build":
        result = build(args.publish, args.output, args.rid, args.version, args.source, args.run_url, args.sdk)
    elif args.action == "unpack":
        result = unpack(args.archive, args.output)
    else:
        result = verify(args.package, args.rid, args.version, args.source)
    print(result)


if __name__ == "__main__":
    main()
