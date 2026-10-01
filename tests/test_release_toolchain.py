"""Verify serviced native pack identities with installed-metadata fixtures, not an AOT build."""

import copy
from pathlib import Path
import shutil
import sys
import unittest
import uuid

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "packaging"))
import release_toolchain as toolchain


class ReleaseToolchainTests(unittest.TestCase):
    """Reject SDK-only and host-runtime-only evidence for native release servicing."""

    def setUp(self):
        """Create owned nuspec fixtures under repository .temp."""
        self.scratch = ROOT / ".temp" / ("release-toolchain-tests-" + uuid.uuid4().hex)
        self.scratch.mkdir(parents=True)

    def tearDown(self):
        """Remove only this fixture's checked project scratch descendant."""
        self.assertTrue(self.scratch.resolve().is_relative_to(ROOT / ".temp"))
        shutil.rmtree(self.scratch)

    def resolved(self, rid):
        """Model MSBuild's exact resolved pack rows and matching installed nuspecs."""
        result = {"Properties": {"NETCoreSdkVersion": toolchain.SDK_VERSION,
                                 "RuntimeFrameworkVersion": toolchain.RUNTIME_VERSION}, "Items": {}}
        names = (("ResolvedRuntimePack", f"Microsoft.NETCore.App.Runtime.{rid}"),
                 ("ResolvedILCompilerPack", f"runtime.{rid}.Microsoft.DotNet.ILCompiler"),
                 ("ResolvedTargetILCompilerPack", f"Microsoft.NETCore.App.Runtime.NativeAOT.{rid}"))
        for group, name in names:
            directory = self.scratch / name
            directory.mkdir(exist_ok=True)
            (directory / "package.nuspec").write_text(
                f'<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
                f'<metadata><id>{name}</id><version>{toolchain.RUNTIME_VERSION}</version></metadata></package>',
                encoding="utf-8")
            result["Items"][group] = [{"NuGetPackageId": name, "NuGetPackageVersion": toolchain.RUNTIME_VERSION,
                                       "PackageDirectory": str(directory)}]
        return result

    def test_four_matching_pack_resolutions(self):
        """All supported RIDs produce complete path-free versioned summaries."""
        for rid in toolchain.RIDS:
            with self.subTest(rid=rid):
                summary = toolchain.verify_resolved(self.resolved(rid), rid)
                self.assertEqual(toolchain.RUNTIME_VERSION, summary["ilCompilerVersion"])
                self.assertNotIn(str(self.scratch), str(summary))

    def test_reject_sdk400(self):
        """The previous SDK cannot claim the serviced toolchain identity."""
        result = self.resolved("win-x64")
        result["Properties"]["NETCoreSdkVersion"] = "10.0.400"
        with self.assertRaisesRegex(ValueError, "property mismatch"):
            toolchain.verify_resolved(result, "win-x64")

    def test_reject_old_runtime_or_compiler(self):
        """New SDK selection does not cover stale Core, compiler or Native AOT packs."""
        original = self.resolved("win-x64")
        for group in original["Items"]:
            with self.subTest(group=group):
                result = copy.deepcopy(original)
                result["Items"][group][0]["NuGetPackageVersion"] = "10.0.11"
                with self.assertRaisesRegex(ValueError, "version/directory"):
                    toolchain.verify_resolved(result, "win-x64")

    def test_reject_missing_pack(self):
        """A missing native runtime cannot pass based on host/compiler versions."""
        result = self.resolved("win-x64")
        del result["Items"]["ResolvedTargetILCompilerPack"]
        with self.assertRaisesRegex(ValueError, "Missing"):
            toolchain.verify_resolved(result, "win-x64")

    def test_reject_installed_metadata_mismatch(self):
        """Installed pack nuspec identity must agree with resolved metadata."""
        result = self.resolved("win-x64")
        path = Path(result["Items"]["ResolvedILCompilerPack"][0]["PackageDirectory"]) / "package.nuspec"
        path.write_text('<package><metadata><id>wrong</id><version>10.0.12</version></metadata></package>')
        with self.assertRaisesRegex(ValueError, "nuspec"):
            toolchain.verify_resolved(result, "win-x64")

    def test_reject_wrong_rid(self):
        """Native host and target pack names must match the requested architecture."""
        with self.assertRaisesRegex(ValueError, "Missing"):
            toolchain.verify_resolved(self.resolved("win-x64"), "win-arm64")

    def test_reject_duplicate_compiler(self):
        """Two resolved compiler identities are ambiguous even at the right version."""
        result = self.resolved("win-x64")
        result["Items"]["ResolvedILCompilerPack"] *= 2
        with self.assertRaisesRegex(ValueError, "duplicate"):
            toolchain.verify_resolved(result, "win-x64")

    def test_reject_source_or_run_mismatch(self):
        """A pack summary cannot be reused as proof for another source or workflow run."""
        summary = toolchain.verify_resolved(self.resolved("win-x64"), "win-x64")
        summary.update(sourceCommit="a" * 40, workflowRun="fixture-run", resolvedMetadataSha256="b" * 64)
        for source, run in (("c" * 40, "fixture-run"), ("a" * 40, "other-run")):
            with self.subTest(source=source, run=run):
                with self.assertRaisesRegex(ValueError, "provenance"):
                    toolchain.validate_summary(summary, "win-x64", source, run)


if __name__ == "__main__":
    unittest.main()
