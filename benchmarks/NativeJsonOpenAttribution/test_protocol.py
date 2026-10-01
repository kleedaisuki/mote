"""Portable protocol checks; no GUI, native toolchain, user files or large fixture generation."""

import argparse
import json
import hashlib
from pathlib import Path
import sys
import unittest
import uuid

sys.dont_write_bytecode = True
from prepare import ROOT, artifact, prepare, replace_once
from run import line_oracle, run


class ProtocolTests(unittest.TestCase):
    """Test scratch confinement, fail-closed code generation and independent CRLF counting."""

    def test_outside_or_linked_artifacts_fail(self):
        """A cache result cannot be redirected to source or outside the checkout."""
        for path in (ROOT / "src/out.json", ROOT.parent / "outside.json"):
            with self.assertRaises(ValueError):
                artifact(path, ".cache")

    def test_source_shape_requires_exactly_one_match(self):
        """Partial or duplicated substitutions cannot become accepted phase data."""
        for text in ("none", "read read"):
            with self.assertRaises(ValueError):
                replace_once(text, "read", "timer")
        self.assertEqual("timer", replace_once("read", "read", "timer"))

    def test_prepare_keeps_engine_copy_and_positive_read_hook(self):
        """Fresh preparation namespaces all Engine files and inserts all five phase hooks."""
        directory = ROOT / ".temp/native-json-open-attribution-protocol" / uuid.uuid4().hex
        prepare(directory)
        document = (directory / "Document.cs").read_text()
        self.assertIn("namespace Probe.Engine;", document)
        self.assertIn("PhaseProbe.End(0, readStart)", document)
        for phase in range(1, 5):
            self.assertIn(f"PhaseProbe.End({phase},", document)
        self.assertTrue((directory / "prepared.json").is_file())
        self.assertIn("line-hybrid", (directory / "Program.cs").read_text())

    def test_byte_oracle_preserves_cross_block_crlf(self):
        """The independent oracle corrects CRLF even across its own read boundary."""
        directory = ROOT / ".temp/native-json-open-attribution-protocol" / uuid.uuid4().hex
        directory.mkdir(parents=True)
        path = directory / "oracle.txt"
        for data, expected in ((b"", 1), (b"x\r\ny\rz\n", 4), (b"x" * 65535 + b"\r\n", 2)):
            path.write_bytes(data)
            self.assertEqual(expected, line_oracle(path))

    def test_source_drift_fails_before_starting_a_child(self):
        """A stale source-copy baseline cannot silently compare different Engine implementations."""
        directory, output = self.stub_run_paths()
        (directory / "prepared.json").write_text(json.dumps({"engine_source_sha256": {"Document.cs": "0" * 64}}))
        with self.assertRaisesRegex(ValueError, "Engine changed"):
            run(argparse.Namespace(directory=directory, output=output, suite="attribution", repeats=1))

    def test_existing_results_fail_before_starting_a_child(self):
        """Rejected experiments remain available instead of being overwritten by a later cohort."""
        directory, output = self.stub_run_paths()
        (directory / "prepared.json").write_text(json.dumps({"engine_source_sha256": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in (ROOT / "src/Mote.Engine").glob("*.cs")}}))
        (output / "attribution-rows.jsonl").write_text("retained witness")
        with self.assertRaisesRegex(ValueError, "Results already exist"):
            run(argparse.Namespace(directory=directory, output=output, suite="attribution", repeats=1))
        self.assertEqual("retained witness", (output / "attribution-rows.jsonl").read_text())

    def test_preparation_rejects_nonempty_directory(self):
        """A stale published binary cannot survive a reprepare relabeling its manifest."""
        directory = ROOT / ".temp/native-json-open-attribution-protocol" / uuid.uuid4().hex
        directory.mkdir(parents=True)
        (directory / "stale-binary-witness").write_text("keep")
        with self.assertRaisesRegex(ValueError, "empty scratch"):
            prepare(directory)
        self.assertEqual("keep", (directory / "stale-binary-witness").read_text())

    def stub_run_paths(self):
        """Create an inert binary sentinel; both guards must reject before subprocess execution."""
        identity = uuid.uuid4().hex
        directory = ROOT / ".temp/native-json-open-attribution-protocol" / identity
        output = ROOT / ".cache/native-json-open-attribution/protocol" / identity
        (directory / "publish").mkdir(parents=True)
        output.mkdir(parents=True)
        (directory / "publish/Probe.exe").write_bytes(b"inert test sentinel")
        return directory, output


if __name__ == "__main__":
    unittest.main()
