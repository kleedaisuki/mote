"""Exercise release integrity and hostile archive cases without launching a GUI."""

import importlib.util
import json
from pathlib import Path
import shutil
import struct
import sys
import unittest
from unittest import mock
import uuid
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("release_package", ROOT / "packaging/release_package.py")
pack = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pack)
sys.modules["release_package"] = pack
index_spec = importlib.util.spec_from_file_location("release_index", ROOT / "packaging/release_index.py")
index = importlib.util.module_from_spec(index_spec)
index_spec.loader.exec_module(index)


class ReleasePackagingTests(unittest.TestCase):
    """Use owned repository scratch directories, never OS-global temp files."""

    def setUp(self):
        """Prepare deterministic architecture fixtures and minimal package resources."""
        self.scratch = ROOT / ".temp" / ("release-package-tests-" + uuid.uuid4().hex)
        self.scratch.mkdir(parents=True)
        self.publish = self.scratch / "publish"
        self.publish.mkdir()
        self.old_root = pack.ROOT
        self.fake = self.scratch / "fixture-repo"
        self.fake.mkdir()
        for name in pack.DOCS + ("docs/releases/v0.1.0.md",):
            path = self.fake / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("fixture\n", encoding="utf-8")
        (self.fake / "packaging/licenses").mkdir()
        (self.fake / "packaging/licenses/license.txt").write_text("fixture", encoding="utf-8")
        (self.fake / "src/Mote.Native").mkdir(parents=True)
        shutil.copyfile(ROOT / "src/Mote.Native/Info.plist", self.fake / "src/Mote.Native/Info.plist")
        pack.ROOT = self.fake
        self.publish = self.fake / ".temp/publish"
        self.publish.mkdir(parents=True)
        self.sha = "a" * 40
        self.checkout = mock.patch.object(pack, "checkout_identity")
        self.checkout.start()

    def tearDown(self):
        """Delete only this test's verified descendant of repository .temp."""
        pack.ROOT = self.old_root
        self.checkout.stop()
        self.assertTrue(self.scratch.resolve().is_relative_to(ROOT / ".temp"))
        shutil.rmtree(self.scratch)

    def binary(self, rid, managed=False):
        """Write header fixtures; these are never claimed to be runnable native binaries."""
        data = bytearray(512)
        if rid.startswith("win-"):
            data[:2] = b"MZ"
            struct.pack_into("<I", data, 60, 64)
            data[64:68] = b"PE\0\0"
            struct.pack_into("<H", data, 68, 0x8664 if rid.endswith("x64") else 0xAA64)
            struct.pack_into("<H", data, 88, 0x20B)
            if managed:
                struct.pack_into("<II", data, 88 + 112 + 14 * 8, 1, 72)
            name = "mote.exe"
        else:
            data[:4] = b"\xcf\xfa\xed\xfe"
            struct.pack_into("<I", data, 4, 0x01000007 if rid.endswith("x64") else 0x0100000C)
            name = "mote"
        path = self.publish / name
        path.write_bytes(data)
        return path

    def assemble(self, rid):
        """Build and extract one synthetic package through the production packer."""
        self.binary(rid)
        output = self.fake / ".cache/package"
        archive = pack.build(self.publish, output, rid, "0.1.0", self.sha, "fixture-run", "10.0.400")
        extract = pack.unpack(archive, self.fake / ".cache/extracted")
        package = next(extract.iterdir())
        return package, archive

    def test_four_architecture_roundtrips(self):
        """All four RID headers/layouts survive archive extraction and exact validation."""
        for rid in pack.RIDS:
            with self.subTest(rid=rid):
                package, _ = self.assemble(rid)
                self.assertTrue(pack.verify(package, rid, "0.1.0", self.sha).is_file())
                shutil.rmtree(self.fake / ".cache")
                for path in self.publish.iterdir():
                    path.unlink()

    def test_reject_hash_mutation(self):
        """Changes after packaging fail even when executable architecture stays valid."""
        package, _ = self.assemble("win-x64")
        (package / "README.md").write_text("changed", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "hash/size"):
            pack.verify(package, "win-x64", "0.1.0", self.sha)

    def test_reject_extra_file(self):
        """A correct listed inventory cannot conceal unlisted payloads."""
        package, _ = self.assemble("win-x64")
        (package / "extra.dll").write_bytes(b"extra")
        with self.assertRaisesRegex(ValueError, "uninventoried"):
            pack.verify(package, "win-x64", "0.1.0", self.sha)

    def test_reject_wrong_source(self):
        """Source identity is checked independently of filenames and version labels."""
        package, _ = self.assemble("win-x64")
        with self.assertRaisesRegex(ValueError, "provenance"):
            pack.verify(package, "win-x64", "0.1.0", "b" * 40)

    def test_reject_broken_packaged_manual_link(self):
        """Self-consistent hashes do not hide a packaged manual with missing targets."""
        (self.fake / "docs/user/manual.md").write_text("[missing](missing.md)\n", encoding="utf-8")
        package, _ = self.assemble("win-x64")
        with self.assertRaisesRegex(ValueError, "documentation link"):
            pack.verify(package, "win-x64", "0.1.0", self.sha)

    def test_reject_wrong_architecture(self):
        """An ARM64 label cannot be applied to an x64 executable."""
        path = self.binary("win-x64")
        with self.assertRaisesRegex(ValueError, "architecture"):
            pack.binary_arch(path, "win-arm64")

    def test_reject_managed_executable(self):
        """A renamed managed assembly is not accepted as the AOT entry point."""
        with self.assertRaisesRegex(ValueError, "Managed CLR"):
            pack.binary_arch(self.binary("win-x64", managed=True), "win-x64")

    def test_reject_overwrite(self):
        """Re-running a pack cannot replace an existing immutable artifact."""
        self.assemble("win-x64")
        with self.assertRaisesRegex(ValueError, "already exists"):
            pack.build(self.publish, self.fake / ".cache/package", "win-x64", "0.1.0", self.sha, "run", "10.0.400")

    def test_reject_output_escape(self):
        """Package outputs cannot be directed at the source tree or global temp."""
        with self.assertRaisesRegex(ValueError, "child"):
            pack.checked_output(self.fake / "output")

    def test_reject_duplicate_inventory(self):
        """Repeated inventory rows are rejected, not silently collapsed into a set."""
        package, _ = self.assemble("win-x64")
        path = package / "package-manifest.json"
        value = json.loads(path.read_text())
        value["files"].append(value["files"][0])
        pack.write_json(path, value)
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            pack.verify(package, "win-x64", "0.1.0", self.sha)

    def test_reject_traversal_archive(self):
        """Extraction rejects traversal before creating any out-of-tree file."""
        archive = self.fake / ".temp/hostile.zip"
        with zipfile.ZipFile(archive, "w") as stream:
            stream.writestr("../escape", b"not allowed")
        with self.assertRaisesRegex(ValueError, "Unsafe"):
            pack.unpack(archive, self.fake / ".cache/hostile")

    def test_reject_symlink_archive(self):
        """ZIP symlinks are rejected on both Windows and macOS hosts."""
        archive = self.fake / ".temp/link.zip"
        with zipfile.ZipFile(archive, "w") as stream:
            entry = zipfile.ZipInfo("link")
            entry.external_attr = 0o120777 << 16
            stream.writestr(entry, "../escape")
        with self.assertRaisesRegex(ValueError, "symlink"):
            pack.unpack(archive, self.fake / ".cache/link")

    def test_reject_noncanonical_identity(self):
        """Versions/SHA strings cannot inject paths, refs or shell fragments."""
        for version, sha in (("01.0.0", self.sha), ("0.1.0;cmd", self.sha), ("0.1.0", "main")):
            with self.assertRaises(ValueError):
                pack.identity(version, sha)

    def test_release_set_requires_four_packages(self):
        """An incomplete artifact download cannot become a release-assets set."""
        inputs = self.fake / ".cache/inputs"
        inputs.mkdir(parents=True)
        with self.assertRaisesRegex(ValueError, "exactly one"):
            index.consolidate(inputs, self.fake / ".cache/assets", "0.1.0", self.sha, "fixture-run")

    def test_release_set_roundtrip_and_checksums(self):
        """Consolidation rechecks four packages and includes all downloads in SHA256SUMS.

        Git archive execution is mocked here; hosted CI proves the actual source
        archive. Architecture fixtures remain header-only, not runnable binaries.
        """
        inputs = self.fake / ".cache/inputs"
        for rid in pack.RIDS:
            self.binary(rid)
            pack.build(self.publish, inputs / rid, rid, "0.1.0", self.sha, "fixture-run", "10.0.400")
            for path in self.publish.iterdir():
                path.unlink()
        def archive_source(command, **kwargs):
            """Stand in for Git archive without claiming a source-build verification."""
            filename = next(arg.split("=", 1)[1] for arg in command if arg.startswith("--output="))
            Path(filename).write_bytes(b"source-archive-fixture")
        with mock.patch.object(index, "ROOT", self.fake), \
                mock.patch.object(index.subprocess, "check_output", return_value=self.sha + "\n"), \
                mock.patch.object(index.subprocess, "run", side_effect=archive_source):
            output = index.consolidate(inputs, self.fake / ".cache/assets", "0.1.0", self.sha, "fixture-run")
        value = json.loads((output / "release-index.json").read_text())
        self.assertEqual(9, len(value["assets"]))
        self.assertFalse((output / "verification").exists())
        lines = (output / "SHA256SUMS").read_text().splitlines()
        self.assertEqual(10, len(lines))
        for line in lines:
            checksum, name = line.split("  ")
            self.assertEqual(checksum, pack.digest(output / name))


if __name__ == "__main__":
    unittest.main()
